using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using FruityPrime.Server.Shared;
using FruityPrime.Server.Worker.Simulation;
using FruityPrime.Server.Worker.Reporting;
using MphRead;
using MphRead.Identity;
using MphRead.Admin;
using MphRead.Replay;
using MphRead.Mods.Network;
using BotPolicy = MphRead.Mods.Network.BotFillPolicy;
using SharedBotPolicy = FruityPrime.Server.Shared.BotFillPolicy;
using OpenTK.Mathematics;

namespace FruityPrime.Server.Worker;

/// <summary>Worker control owner. Worlds are created, stepped and disposed exclusively on their assigned lane.</summary>
public sealed class WorkerRuntime : IAsyncDisposable
{
    private readonly WorkerOptions _options;
    private readonly WorkerContent _content;
    private readonly WorkerContentLease _contentLease;
    private readonly WorkerNetworkHub _hub;
    private readonly MatchRegistry _registry = new();
    private readonly SimulationLaneManager _lanes;
    private readonly WorkerHealthSampler _health = new();
    private readonly Channel<(MatchRegistry.Entry Entry, MatchCompletion? Completion, Guid? ReplayId, ServerReplaySession? Replay, WorkerEvent? Override)> _artifacts;
    private readonly Task _artifactWriter;
    private readonly CancellationTokenSource _ioStop = new();
    private readonly Thread _io;
    private WorkerStatus _status = WorkerStatus.Ready;
    private NodeId? _node;
    private Guid _nodeIncarnation;
    private int _configuredLimit, _configuredPlayers;
    private string? _keyId, _publicKey;
    private readonly Dictionary<string, long?> _keyRetirement = new(StringComparer.Ordinal);
    private int _disposed, _pendingArtifacts;
    private bool _validationFixtureMatchAccepted;
    public event Action<WorkerEvent>? Event;
    public WorkerOptions Options => _options;
    public IReadOnlyList<LaneMetrics> LaneMetrics => _lanes.Metrics;
    public WorkerContent Content => _content;

    public WorkerRuntime(WorkerOptions options, WorkerContent content, WorkerNetworkHub hub)
    {
        options.Validate(); _options = options; _content = content; _hub = hub;
        _contentLease = ContentEnvironment.AcquireContent();
        if (!ReferenceEquals(_contentLease.Content, content))
        { _contentLease.Dispose(); throw new ArgumentException("Worker must hold the active immutable content view."); }
        try
        {
            if (options.ValidationFixture != DeveloperValidationFixtureId.None)
            {
                DeveloperValidationFixtureDescriptor descriptor
                    = DeveloperValidationFixtures.Require(options.ValidationFixture);
                _ = DeveloperValidationFixtures.ValidateContent(descriptor, content.Version,
                    relative => content.ReadBytes(Path.Combine(content.Directory,
                        relative.Replace('/', Path.DirectorySeparatorChar))));
            }
            _configuredLimit = options.MaxMatches; _configuredPlayers = Math.Min(8192, options.MaxMatches * 32);
            _lanes = new(options);
            _artifacts = Channel.CreateBounded<(MatchRegistry.Entry, MatchCompletion?, Guid?, ServerReplaySession?, WorkerEvent?)>(new BoundedChannelOptions(options.MaxMatches * 2)
                { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            _artifactWriter = WriteArtifactsAsync();
            _io = new Thread(PumpNetwork) { IsBackground = true, Name = "worker-network" };
            _io.Start();
        }
        catch
        {
            _artifacts?.Writer.TryComplete();
            _lanes?.Dispose(); _hub.Dispose(); _ioStop.Dispose(); _contentLease.Dispose();
            throw;
        }
    }

    public WorkerReady Configure(WorkerConfigure command)
    {
        WorkerIpcCodec.Encode(command);
        lock (_registry.Gate)
        {
            if (_node.HasValue) throw new InvalidOperationException("Worker is already configured.");
            if (command.Capacity.MatchLimit > _options.MaxMatches || command.Capacity.PlayerLimit > command.Capacity.MatchLimit * 32
                || command.Capacity.ActiveMatches != 0 || command.Capacity.ActivePlayers != 0)
                throw new ArgumentException("Node capacity exceeds this Worker's configured hard limit.");
            if (_options.ValidationFixture != DeveloperValidationFixtureId.None
                && command.Capacity != new WorkerCapacity(1, 2, 0, 0))
                throw new ArgumentException("Developer validation fixture requires exact isolated Node capacity.");
            _node = command.NodeId; _nodeIncarnation = command.NodeIncarnation;
            _configuredLimit = command.Capacity.MatchLimit;
            _configuredPlayers = Math.Min(command.Capacity.PlayerLimit, Math.Min(8192, _configuredLimit * 32));
            return new(_options.WorkerId, _options.Incarnation, CapacityLocked());
        }
    }

    public Task<WorkerEvent> CreateAsync(MatchSpec spec)
    {
        spec.Validate();
        byte[] fingerprint = MatchRegistry.Fingerprint(spec);
        MatchRegistry.Entry entry;
        lock (_registry.Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_registry.Entries.TryGetValue(spec.MatchId, out var existing))
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint))
                    return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Conflicting CreateMatch for an existing identity."));
                return existing.Terminal != null ? Task.FromResult(existing.Terminal) : existing.Ready.Task;
            }
            if (_options.ValidationFixture != DeveloperValidationFixtureId.None
                && _validationFixtureMatchAccepted)
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId,
                    "Developer validation fixture Worker accepts one match identity."));
            if (_keyId == null || _publicKey == null)
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Node signing key is not configured."));
            if (_node.HasValue && (_node != spec.NodeId || _nodeIncarnation != spec.NodeIncarnation))
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Node identity or incarnation mismatch."));
            if (_status != WorkerStatus.Ready) return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Worker is not accepting matches."));
            bool fixtureMap = _options.ValidationFixture != DeveloperValidationFixtureId.None;
            if (spec.Content.ContentVersion != _content.Version || spec.Content.ContentHash != _content.ContentHash
                || spec.Content.BuildVersion != _options.BuildVersion || spec.Content.ProtocolVersion != _options.ProtocolVersion
                || (fixtureMap ? !IsValidationFixtureSpec(spec)
                    : !_content.SupportedRooms.Contains(spec.Content.MapKey, StringComparer.Ordinal)))
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Content, map, build or protocol mismatch."));
            if (spec.ReplayPolicy == ReplayPolicy.Record && string.IsNullOrWhiteSpace(_options.ReplayDirectory)
                || spec.TelemetryPolicy == TelemetryPolicy.Record && string.IsNullOrWhiteSpace(_options.ArtifactDirectory))
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Requested replay or telemetry storage is not configured."));
            if (!WorkerArtifactBudget.HasHeadroom(_options, _registry.ByWireId.Count))
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Worker artifact budget is full; admission is fenced until storage is reclaimed."));
            _health.Sample();
            if (_registry.ByWireId.Count + Volatile.Read(ref _pendingArtifacts) >= _options.MaxMatches * 2
                || _registry.ByWireId.Count >= _configuredLimit
                || _registry.ByWireId.Values.Sum(item => item.Spec.Roster.Length) + spec.Roster.Length > _configuredPlayers
                || _health.CpuPercent >= _options.PlacementCpuPercent
                || _health.MemoryHeadroomBytes < _options.MinimumMemoryHeadroomBytes)
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Worker is at capacity or lacks resource headroom."));
            // Retain all accepted identities for this incarnation; never silently make an old retry a new match.
            if (_registry.Entries.Count >= _options.CompletedHistoryCapacity)
                return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, "Worker identity history is full; drain and replace this worker."));
            SimulationLaneManager.Lease lease;
            try { lease = _lanes.Reserve(); }
            catch (InvalidOperationException error) { return Task.FromResult<WorkerEvent>(new MatchFailed(spec.MatchId, error.Message)); }
            entry = new(spec, fingerprint, new(_registry.AllocateWireId())) { Lease = lease };
            _registry.Entries.Add(spec.MatchId, entry); _registry.ByWireId.Add(entry.WireId.Value, entry);
            if (fixtureMap) _validationFixtureMatchAccepted = true;
        }
        _ = CreateOnLaneAsync(entry);
        return entry.Ready.Task;
    }

    private bool IsValidationFixtureSpec(MatchSpec spec)
    {
        DeveloperValidationFixtureDescriptor descriptor
            = DeveloperValidationFixtures.Require(_options.ValidationFixture);
        return spec.Content.MapKey == descriptor.MapKey
            && spec.Rules.RoomKey == descriptor.MapKey
            && spec.Rules.Mode.ToLegacyMode() == descriptor.Mode
            && spec.Rules.MaxPlayers == 2
            && spec.TrustClass == MatchTrustClass.Community
            && spec.TournamentId == null
            && spec.RoundId == null
            && spec.Roster.Length == 2
            && spec.Roster[0] is { SeatId: 0, PlayerId: not null, GuestSessionId: null,
                DisplayName: "WanProbe", Hunter: Hunter.Noxus, Team: 0,
                Role: SeatRole.Player, RankingEligible: false }
            && spec.Roster[1] is { SeatId: 1, PlayerId: null, GuestSessionId: null,
                DisplayName: "Bot1", Hunter: Hunter.Samus, Team: 1,
                Role: SeatRole.Bot, RankingEligible: false }
            && spec.BotFillPolicy == FruityPrime.Server.Shared.BotFillPolicy.FillVacancies
            && spec.ObserverPolicy == ObserverPolicy.Disabled
            && spec.ReplayPolicy == ReplayPolicy.Record
            && spec.TelemetryPolicy == TelemetryPolicy.Record;
    }

    private async Task CreateOnLaneAsync(MatchRegistry.Entry entry)
    {
        using var deadline = new CancellationTokenSource(_options.CreationTimeout);
        try
        {
            Task<bool> creation = entry.Lease!.Lane.InvokeAsync(() =>
            {
                try
                {
                    var lagCompensation = _options.ResolveLagCompensation();
                    entry.Transport = _hub.RegisterMatch(entry.WireId.Value, maxConnections: 32);
                    lock (_registry.Gate)
                        entry.Tickets = new WorkerTicketAuthority(entry.Spec, Placement(entry), _keyId!, _publicKey!);
                    entry.Instance = new MatchInstance(new(entry.Spec, entry.WireId.Value)
                    {
                        BotFill = entry.Spec.BotFillPolicy == SharedBotPolicy.FillVacancies ? new BotPolicy(entry.Spec.Rules.MaxPlayers) : new BotPolicy(),
                        ReplayDirectory = _options.ReplayDirectory,
                        RequireReplay = entry.Spec.ReplayPolicy == ReplayPolicy.Record,
                        CollectTelemetry = entry.Spec.TelemetryPolicy == TelemetryPolicy.Record,
                        ReportingServerId = entry.Spec.NodeId.Value,
                        Tickets = entry.Tickets,
                        LagCompEnabled = lagCompensation.LagCompEnabled,
                        ProjectileCatchUpEnabled = lagCompensation.ProjectileCatchUpEnabled,
                        HistoricalDynamicCollisionEnabled = lagCompensation.HistoricalDynamicCollisionEnabled,
                        ValidationFixture = _options.ValidationFixture
                    }, entry.Transport);
                    deadline.Token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed != 0, this);
                    entry.Instance.Start();
                    entry.Snapshot = entry.Instance.Status;
                    long admissionStarted = Stopwatch.GetTimestamp();
                    entry.Lease.Lane.Add(entry.Instance, match => Terminal(entry, match), status =>
                    {
                        Volatile.Write(ref entry.Snapshot, status);
                        if (status.Phase == MatchPhase.WaitingForPlayers && entry.Spec.Roster.Any(seat => seat.Role == SeatRole.Player)
                            && entry.Instance?.Network.Count == 0 && Stopwatch.GetElapsedTime(admissionStarted) >= _options.AdmissionTimeout)
                            entry.Instance.RequestStop(MatchStopReason.AdmissionTimeout);
                    });
                    entry.Ready.TrySetResult(new MatchReady(Placement(entry)));
                    return true;
                }
                catch
                {
                    entry.Instance?.Dispose(); entry.Transport?.Dispose(); Release(entry); throw;
                }
            }, deadline.Token);
            try { await creation.WaitAsync(_options.CreationTimeout).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                deadline.Cancel();
                var failed = new MatchFailed(entry.Spec.MatchId, "Match creation deadline exceeded.");
                lock (_registry.Gate) entry.Terminal = failed;
                entry.Ready.TrySetResult(failed);
                // The synchronous loader is not abortable. Keep its reservation until its owner cleans it up.
                try { await creation.ConfigureAwait(false); } catch (Exception) { }
                Release(entry);
            }
        }
        catch (Exception error)
        {
            Release(entry);
            var failed = new MatchFailed(entry.Spec.MatchId, error is OperationCanceledException ? "Match creation deadline exceeded." : Bounded(error.Message));
            lock (_registry.Gate) entry.Terminal = failed;
            entry.Ready.TrySetResult(failed);
        }
    }

    private void Terminal(MatchRegistry.Entry entry, MatchInstance match)
    {
        entry.Snapshot = match.Status;
        ServerReplaySession? replay = match.Replay;
        Guid? replayId = match.Replay?.ReplayId;
        MatchCompletion? completion = match.Completion;
        WorkerEvent? immediate = match.State == MatchInstanceState.Failed
            ? new MatchFailed(entry.Spec.MatchId, Bounded(match.Status.Error ?? "Match failed."))
            : completion?.StopReason != null ? new MatchInterrupted(entry.Spec.MatchId, completion.StopReason.Value.ToString()) : null;
        try { match.Dispose(); }
        finally { entry.Transport?.Dispose(); Release(entry); }
        if (immediate != null) RecordTerminal(entry, immediate);
        Interlocked.Increment(ref _pendingArtifacts);
        if (!_artifacts.Writer.TryWrite((entry, completion, replayId, replay, immediate)))
        {
            Interlocked.Decrement(ref _pendingArtifacts);
            RecordTerminal(entry, new MatchFailed(entry.Spec.MatchId, "Completion artifact queue is full."));
        }
    }

    private async Task WriteArtifactsAsync()
    {
        await foreach (var item in _artifacts.Reader.ReadAllAsync())
        {
            try
            {
                if (item.Replay != null)
                {
                    await item.Replay.Completion.ConfigureAwait(false);
                    if (item.Override == null && item.Replay.Status.State != "complete") throw new IOException("Replay did not complete successfully.");
                }
                if (item.Override != null) continue;
                if (item.Completion == null) throw new InvalidDataException("Match completion is missing.");
                Guid reportId = item.Entry.Spec.MatchId.Value;
                Guid? telemetryId = item.Completion.Telemetry != null ? reportId : null;
                MatchReportReady? reportReady = null;
                if (_options.ArtifactDirectory is { } directory)
                {
                    Directory.CreateDirectory(directory);
                    if (item.Completion.Report is { } report)
                        reportReady = WorkerReportArtifactWriter.Write(directory, _options.WorkerId, _options.Incarnation,
                            item.Entry.Spec, item.Entry.WireId.Value, report);
                    if (item.Completion.Telemetry is { } telemetry)
                        await File.WriteAllTextAsync(Path.Combine(directory, reportId + ".telemetry.json"), JsonSerializer.Serialize(telemetry));
                }
                var outcomes = item.Completion.Report?.Participants.Take(MatchCompletionSummary.MaxOutcomes).Select(p =>
                    new PlayerOutcomeSummary(p.ParticipantId, p.PlayerId, p.Kind, p.DisplayName, p.Outcome,
                        p.Metrics.Standing, p.Metrics.TeamStanding, p.Metrics.Points, p.Metrics.Kills, p.Metrics.Deaths)).ToImmutableArray()
                    ?? ImmutableArray<PlayerOutcomeSummary>.Empty;
                var summary = new MatchCompletionSummary(item.Entry.Spec.MatchId, item.Entry.Spec.LobbyId,
                    item.Completion.Result?.EndReason ?? MatchEndReason.Forced, outcomes, item.ReplayId, telemetryId, reportId);
                summary.Validate();
                RecordTerminal(item.Entry, new MatchCompleted(summary));
                if (reportReady != null) Emit(reportReady);
            }
            catch (Exception error)
            {
                if (item.Override == null) RecordTerminal(item.Entry, new MatchFailed(item.Entry.Spec.MatchId, Bounded("Completion persistence failed: " + error.Message)));
            }
            finally { Interlocked.Decrement(ref _pendingArtifacts); }
        }
    }

    private void RecordTerminal(MatchRegistry.Entry entry, WorkerEvent value)
    {
        lock (_registry.Gate) entry.Terminal = value;
        Emit(value);
    }
    private void Release(MatchRegistry.Entry entry)
    {
        lock (_registry.Gate)
        {
            if (entry.Released) return;
            entry.Released = true; _registry.ByWireId.Remove(entry.WireId.Value);
            entry.Lease?.Dispose(); entry.Tickets?.Dispose();
            entry.Instance = null; entry.Transport = null; entry.Lease = null; entry.Tickets = null;
        }
    }
    internal async Task<T> InvokeMatchAsync<T>(MatchId id, Func<MatchInstance, T> action)
    {
        MatchRegistry.Entry entry;
        SimulationLane lane;
        lock (_registry.Gate)
        {
            if (!_registry.Entries.TryGetValue(id, out entry!) || entry.Lease == null)
                throw new InvalidOperationException("Match is not active.");
            lane = entry.Lease.Lane;
        }
        return await lane.InvokeAsync(() => action(entry.Instance ?? throw new InvalidOperationException("Match has ended.")));
    }

    public async Task<MatchInstanceStatus?> GetStatusAsync(MatchId id)
    {
        MatchRegistry.Entry? entry;
        SimulationLane? lane;
        lock (_registry.Gate)
        {
            _registry.Entries.TryGetValue(id, out entry);
            lane = entry?.Lease?.Lane;
        }
        if (entry == null) return null;
        if (lane == null) return entry.Snapshot;
        return await lane.InvokeAsync(() => entry.Snapshot = entry.Instance?.Status ?? entry.Snapshot);
    }

    /// <summary>
    /// Returns bounded, server-selected lag-compensation facts for an
    /// authenticated developer tool. The call is lane-affine and does not
    /// expose a gameplay packet or permit a client to select authority state.
    /// </summary>
    public Task<string> NetDebugAsync(MatchId id, string command, CombatShot shot,
        Vector3 projectileStart, Vector3 projectileEnd)
        => InvokeMatchAsync(id, match => match.NetDebug(command, shot, projectileStart, projectileEnd));

    public async Task<MatchAdminResult> AdminAsync(MatchAdminCommand command)
    {
        WorkerIpcCodec.Encode(command);
        try
        {
            var result = await InvokeMatchAsync(command.MatchId, match => WorkerMatchAdmin.Apply(match, command));
            return new(command.MatchId, command.Action, result.Applied, result.Code, result.Message);
        }
        catch (InvalidOperationException) { return new(command.MatchId, command.Action, false, "not_active", "Match is not active."); }
    }

    public async Task CancelAsync(MatchId id)
    {
        MatchRegistry.Entry? entry;
        lock (_registry.Gate) _registry.Entries.TryGetValue(id, out entry);
        SimulationLane? lane;
        lock (_registry.Gate) lane = entry?.Lease?.Lane;
        if (entry == null || lane == null) return;
        await lane.InvokeAsync(() => { entry.Instance?.RequestStop(MatchStopReason.Requested); return true; });
    }
    public void Drain()
    {
        lock (_registry.Gate)
        {
            if (_status != WorkerStatus.Ready) return;
            _status = WorkerStatus.Draining;
        }
        Emit(new WorkerDraining(_options.WorkerId, _options.Incarnation));
    }
    public WorkerHeartbeat Heartbeat()
    {
        lock (_registry.Gate)
        {
            _health.Sample();
            return new(_options.WorkerId, _options.Incarnation, CapacityLocked(),
                new(_status, _health.UptimeMilliseconds, _lanes.Metrics.Max(lane => lane.P99Milliseconds), _health.WorkingSetBytes,
                    new(_lanes.Metrics.Select(lane => new WorkerLaneHealth(lane.LaneId, lane.Matches, lane.Ticks,
                        lane.CatchUpTicks, lane.DroppedTicks, lane.P50Milliseconds, lane.P95Milliseconds, lane.P99Milliseconds,
                        lane.MaxMilliseconds)).ToImmutableArray(), _health.CpuPercent, GC.GetTotalMemory(false),
                        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), _hub.Metrics.PacketsReceived,
                        _hub.Metrics.PacketsSent, _hub.Metrics.BytesReceived, _hub.Metrics.BytesSent,
                        _hub.Metrics.QueueDrops, _hub.Metrics.PacketsRejected,
                        _registry.ByWireId.Values.OrderBy(entry => entry.Spec.MatchId.Value).Take(128).Select(entry => new WorkerMatchHealth(entry.Spec.MatchId, entry.WireId,
                            entry.Snapshot?.Tick ?? 0, entry.Snapshot?.Phase.ToString() ?? "Starting",
                            entry.Snapshot?.State.ToString() ?? "Created")).ToImmutableArray(), _registry.ByWireId.Count, _registry.ByWireId.Count > 128)));
        }
    }
    private WorkerCapacity CapacityLocked() => new(_configuredLimit, _configuredPlayers, _registry.ByWireId.Count,
        _registry.ByWireId.Values.Sum(entry => entry.Snapshot?.PlayerCount ?? entry.Spec.Roster.Length));
    private void PumpNetwork()
    {
        try { while (!_ioStop.IsCancellationRequested) { _hub.Pump(); _ioStop.Token.WaitHandle.WaitOne(1); } }
        catch (Exception error)
        {
            lock (_registry.Gate) _status = WorkerStatus.Faulted;
            Emit(new WorkerFault(_options.WorkerId, _options.Incarnation, Bounded(error.Message)));
        }
    }
    private void Emit(WorkerEvent value)
    {
        if (Event is not { } handlers) return;
        foreach (Action<WorkerEvent> handler in handlers.GetInvocationList())
            try { handler(value); } catch (Exception) { /* A control subscriber cannot terminate a simulation lane. */ }
    }
    private MatchPlacement Placement(MatchRegistry.Entry entry) => new(entry.Spec.MatchId, entry.WireId,
        _options.WorkerId, _options.Incarnation, _options.AdvertisedHost, checked((ushort)_hub.LocalPort));
    public Task UpdateSigningKeyAsync(UpdateNodeSigningKey command)
    {
        WorkerIpcCodec.Encode(command);
        if (command.KeyId.Length > 32 || command.KeyId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
            || command.PublicKey.Length > 1024) throw new ArgumentException("Invalid signing key bounds.");
        using (var key = ECDsa.Create())
        {
            byte[] bytes = Convert.FromBase64String(command.PublicKey);
            key.ImportSubjectPublicKeyInfo(bytes, out int read);
            if (read != bytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new ArgumentException("Expected a P-256 public key.");
        }
        lock (_registry.Gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (string expired in _keyRetirement.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                _keyRetirement.Remove(expired);
            if (!_keyRetirement.ContainsKey(command.KeyId) && _keyRetirement.Count >= 16)
                throw new ArgumentException("Signing key overlap capacity is full.");
            // Verifier key imports are internally synchronized and do not touch lane-owned gameplay state.
            // Holding this gate prevents a concurrent creation from missing the publication.
            foreach (var entry in _registry.ByWireId.Values) entry.Tickets?.UpdatePublicKey(command.KeyId, command.PublicKey);
            foreach (string prior in _keyRetirement.Where(pair => pair.Key != command.KeyId && !pair.Value.HasValue).Select(pair => pair.Key).ToArray())
                _keyRetirement[prior] = now + 125;
            _keyRetirement[command.KeyId] = null;
            _keyId = command.KeyId; _publicKey = command.PublicKey;
        }
        return Task.CompletedTask;
    }
    private static string Bounded(string value) => new string(value.Where(ch => !char.IsControl(ch)).Take(1024).ToArray()) is { Length: > 0 } text ? text : "Worker operation failed.";
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Drain();
        try
        {
            try { _lanes.Dispose(); }
            finally { _artifacts.Writer.TryComplete(); }
            await _artifactWriter.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            _ioStop.Cancel();
            try
            {
                if (!_io.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Worker I/O did not stop.");
            }
            finally { _hub.Dispose(); _ioStop.Dispose(); _contentLease.Dispose(); }
            lock (_registry.Gate) _status = WorkerStatus.Stopped;
        }
    }
}
