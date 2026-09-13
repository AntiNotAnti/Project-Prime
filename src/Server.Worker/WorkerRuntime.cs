using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker.Simulation;
using ProjectPrime.Server.Worker.Reporting;
using MphRead;
using MphRead.Identity;
using MphRead.Admin;
using MphRead.Replay;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using BotPolicy = MphRead.Mods.Network.BotFillPolicy;
using SharedBotPolicy = ProjectPrime.Server.Shared.BotFillPolicy;
using OpenTK.Mathematics;
using ProjectPrime.Server.Worker.Maps;

namespace ProjectPrime.Server.Worker;

/// <summary>Worker control owner. Worlds are created, stepped and disposed exclusively on their assigned lane.</summary>
public sealed class WorkerRuntime : IAsyncDisposable
{
    private readonly WorkerOptions _options;
    private readonly WorkerContent _content;
    private readonly WorkerContentLease _contentLease;
    private readonly WorkerMapBuildService _mapBuilds;
    private readonly WorkerNetworkHub _hub;
    private readonly MatchRegistry _registry = new();
    private readonly SimulationLaneManager _lanes;
    private readonly WorkerHealthSampler _health = new();
    private readonly Channel<(MatchRegistry.Entry Entry, MatchCompletion? Completion, Guid? ReplayId, ServerReplaySession? Replay, WorkerEvent? Override)> _artifacts;
    private readonly Task _artifactWriter;
    private readonly CancellationTokenSource _ioStop = new();
    private readonly AutoResetEvent _networkWake = new(false);
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
            _mapBuilds = new(options, content);
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
            _hub.SetNetworkWake(() => { _networkWake.Set(); });
            _io = new Thread(PumpNetwork) { IsBackground = true, Name = "worker-network" };
            _io.Start();
        }
        catch
        {
            _artifacts?.Writer.TryComplete();
            _lanes?.Dispose(); _hub.SetNetworkWake(null); _hub.Dispose(); _ioStop.Dispose();
            _networkWake.Dispose(); _contentLease.Dispose();
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

    public async Task<WorkerEvent> CreateAsync(MatchSpec spec)
    {
        spec.Validate();
        MatchContentSnapshot? snapshot;
        try
        {
            using var timeout = new CancellationTokenSource(_options.CreationTimeout);
            snapshot = await _mapBuilds.PrepareAsync(spec.Content, timeout.Token).ConfigureAwait(false);
        }
        catch (MapDependencyException error)
        {
            return new MatchFailed(spec.MatchId,
                $"MAP-RUN-005: {error.Message}");
        }
        catch (MapCompilationException error)
        {
            MapDiagnostic? diagnostic = error.Diagnostics.FirstOrDefault(
                value => value.Severity == MapDiagnosticSeverity.Error);
            return new MatchFailed(spec.MatchId, diagnostic == null
                ? $"MAP-RUN-004: Required map compilation failed: {error.Message}"
                : $"{diagnostic.Code}: {diagnostic.Message}");
        }
        catch (MapRuntimeException error)
        {
            return new MatchFailed(spec.MatchId,
                $"{error.Code}: Runtime map registration failed.");
        }
        catch (MapPackageException error)
        {
            return new MatchFailed(spec.MatchId,
                $"MAP-PKG-001: {error.Message}");
        }
        catch (OperationCanceledException)
        {
            return new MatchFailed(spec.MatchId,
                "MAP-RUN-010: Required map preparation timed out.");
        }
        return await RegisterAsync(spec, snapshot).ConfigureAwait(false);
    }

    private Task<WorkerEvent> RegisterAsync(MatchSpec spec, MatchContentSnapshot? contentSnapshot)
    {
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
                    : spec.Content.RequiredMap == null
                        && !_content.SupportedRooms.Contains(spec.Content.MapKey, StringComparer.Ordinal)))
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
            entry = new(spec, fingerprint, new(_registry.AllocateWireId()))
                { Lease = lease, ContentSnapshot = contentSnapshot };
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
            && spec.BotFillPolicy == ProjectPrime.Server.Shared.BotFillPolicy.FillVacancies
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
                    entry.Transport = _hub.RegisterMatch(entry.WireId.Value,
                        maxConnections: MultiplayerLimits.MaxWorkerMatchConnections,
                        queueV2Enabled: _options.TransportQueueV2Enabled,
                        criticalReserve: _options.TransportCriticalReserveEnabled
                            ? _options.CriticalTransportReserve : 0);
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
                        SnapshotRateHz = _options.SnapshotRateHz,
                        AdaptiveTimingEnabled = _options.AdaptiveTimingEnabled,
                        AdaptiveTimingV2Enabled = _options.AdaptiveTimingV2Enabled,
                        AdaptiveInputPlayoutEnabled = _options.AdaptiveInputPlayoutEnabled,
                        ReliableAdaptiveRtoEnabled = _options.ReliableAdaptiveRtoEnabled,
                        AckCoalescingEnabled = _options.AckCoalescingEnabled,
                        UdpAuthenticationEnabled = _options.UdpAuthenticationEnabled,
                        Observers = _options.Observers,
                        ValidationFixture = _options.ValidationFixture,
                        HeadshotValidationScenario = _options.HeadshotValidationScenario,
                        HeadshotScenarioSeconds = _options.HeadshotScenarioSeconds,
                        ContentSnapshot = entry.ContentSnapshot
                    }, entry.Transport);
                    deadline.Token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed != 0, this);
                    entry.Instance.Start();
                    entry.Snapshot = entry.Instance.Status;
                    long admissionStarted = Stopwatch.GetTimestamp();
                    entry.Lease.Lane.Add(entry.Instance, match => Terminal(entry, match),
                        admissionCheck: match => EvaluateAdmissionTimeout(entry, match, admissionStarted),
                        snapshot: status => entry.Snapshot = status);
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

    private void EvaluateAdmissionTimeout(MatchRegistry.Entry entry, MatchInstance match, long admissionStarted)
    {
        // This callback is invoked by the owning SimulationLane after every
        // authoritative tick. Do not move it to the lower-rate status
        // publication path: admission is a gameplay/control boundary and must
        // retain its exact tick cadence and lane ownership.
        if (match.Simulation.Scene.Match.Phase == MatchPhase.WaitingForPlayers
            && entry.HasPlayerSeat && match.Network.Count == 0
            && Stopwatch.GetElapsedTime(admissionStarted) >= _options.AdmissionTimeout)
            match.RequestStop(MatchStopReason.AdmissionTimeout);
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
                {
                    MatchParticipationSpan? span = p.Spans.IsDefaultOrEmpty ? null : p.Spans[^1];
                    RosterSeat? seat = span is { } finalSpan
                        ? item.Entry.Spec.Roster.FirstOrDefault(candidate => candidate.SeatId == finalSpan.Slot)
                        : null;
                    return new PlayerOutcomeSummary(p.ParticipantId, p.PlayerId, p.Kind, p.DisplayName, p.Outcome,
                        p.Metrics.Standing, p.Metrics.TeamStanding, p.Metrics.Points, p.Metrics.Kills, p.Metrics.Deaths,
                        p.Kind == ParticipantKind.Guest ? seat?.GuestSessionId : null,
                        span?.Slot, span?.Hunter, span?.TeamIndex);
                }).ToImmutableArray()
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

    public Task<MatchInstanceStatus?> GetStatusAsync(MatchId id)
    {
        MatchRegistry.Entry? entry;
        lock (_registry.Gate)
        {
            _registry.Entries.TryGetValue(id, out entry);
        }
        if (entry == null) return Task.FromResult<MatchInstanceStatus?>(null);
        // Status is an immutable, lane-published view. Reading it does not
        // enqueue a control command or force a fresh record allocation on the
        // simulation thread; lifecycle changes publish immediately and the
        // remaining fields refresh at the lane's fixed status cadence.
        return Task.FromResult(entry.Snapshot);
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

    /// <summary>
    /// Dispatches admission-key installation to the owning match lane. The
    /// control reader never mutates WorkerTicketAuthority directly.
    /// </summary>
    public async Task<WorkerEvent> InstallAdmissionKeyAsync(InstallAdmissionKey command)
    {
        WorkerIpcCodec.Encode(command);
        if (command.WorkerId != _options.WorkerId || command.WorkerIncarnation != _options.Incarnation)
            return new AdmissionKeyInstallFailed(command.AdmissionId, command.MatchId, "worker_scope");
        try
        {
            (bool Accepted, string Reason) installed = await InvokeMatchAsync(command.MatchId, match =>
            {
                bool accepted = match.TryInstallAdmissionKey(command, out string reason);
                return (Accepted: accepted, Reason: reason);
            });
            return installed.Accepted
                ? new AdmissionKeyInstalled(command.AdmissionId, command.TicketId, command.NodeSessionId,
                    command.NodeId, command.NodeIncarnation, command.MatchId, command.WireMatchId,
                    command.WorkerId, command.WorkerIncarnation, command.SeatId, command.JoinNonce, command.ExpiresAt,
                    command.HandoffGeneration)
                : new AdmissionKeyInstallFailed(command.AdmissionId, command.MatchId, installed.Reason);
        }
        catch (InvalidOperationException)
        {
            return new AdmissionKeyInstallFailed(command.AdmissionId, command.MatchId, "match_unavailable");
        }
    }

    public async Task<WorkerEvent> CancelAsync(CancelMatch command)
    {
        MatchRegistry.Entry? entry;
        SimulationLane? lane;
        lock (_registry.Gate)
        {
            _registry.Entries.TryGetValue(command.MatchId, out entry);
            if (entry == null)
                return new MatchCancelRejected(_options.WorkerId, _options.Incarnation,
                    command.MatchId, command.OperationId, "match_not_found");
            if (entry.CancelOperationId is { } prior)
            {
                return prior == command.OperationId
                    ? new MatchCancelAccepted(_options.WorkerId, _options.Incarnation,
                        command.MatchId, command.OperationId, AlreadyAccepted: true)
                    : new MatchCancelRejected(_options.WorkerId, _options.Incarnation,
                        command.MatchId, command.OperationId, "cancel_conflict");
            }
            if (entry.Terminal != null || entry.Lease == null)
                return new MatchCancelRejected(_options.WorkerId, _options.Incarnation,
                    command.MatchId, command.OperationId, "match_not_active");
            entry.CancelOperationId = command.OperationId;
            lane = entry.Lease.Lane;
        }

        bool accepted;
        try
        {
            accepted = await lane.InvokeAsync(() =>
            {
                lock (_registry.Gate)
                {
                    if (entry!.Instance == null) return entry.Terminal != null;
                    if (!entry.CancelRequested)
                    {
                        entry.CancelRequested = true;
                        entry.Instance.RequestStop(MatchStopReason.Requested);
                    }
                    return true;
                }
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            lock (_registry.Gate)
            {
                if (entry!.Terminal == null && entry.CancelOperationId == command.OperationId)
                    entry.CancelOperationId = null;
            }
            accepted = false;
        }
        return accepted
            ? new MatchCancelAccepted(_options.WorkerId, _options.Incarnation,
                command.MatchId, command.OperationId)
            : new MatchCancelRejected(_options.WorkerId, _options.Incarnation,
                command.MatchId, command.OperationId, "match_unavailable");
    }

    public async Task<WorkerEvent> RetireAdmissionAsync(RetireAdmission command)
    {
        WorkerIpcCodec.Encode(command);
        if (command.WorkerId != _options.WorkerId || command.WorkerIncarnation != _options.Incarnation)
            return new AdmissionRetireFailed(command.MatchId, command.AdmissionId, command.SeatId,
                command.HandoffGeneration, _options.WorkerId, _options.Incarnation, "worker_scope");
        try
        {
            (bool Accepted, string Reason) result = await InvokeMatchAsync(command.MatchId, match =>
            {
                bool accepted = match.TryRetireAdmission(command, out string reason);
                return (Accepted: accepted, Reason: reason);
            }).ConfigureAwait(false);
            return result.Accepted
                ? new AdmissionRetired(command.MatchId, command.AdmissionId, command.SeatId,
                    command.HandoffGeneration, command.WorkerId, command.WorkerIncarnation)
                : new AdmissionRetireFailed(command.MatchId, command.AdmissionId, command.SeatId,
                    command.HandoffGeneration, command.WorkerId, command.WorkerIncarnation, result.Reason);
        }
        catch (InvalidOperationException)
        {
            return new AdmissionRetireFailed(command.MatchId, command.AdmissionId, command.SeatId,
                command.HandoffGeneration, command.WorkerId, command.WorkerIncarnation, "match_unavailable");
        }
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
            IReadOnlyList<LaneMetrics> lanes = _lanes.Metrics;
            BoundedPercentileSnapshot pumpDuration = _hub.PumpDurationPercentiles;
            WorkerNetworkLoopSnapshot networkLoop = _hub.NetworkLoopDiagnostics;
            ImmutableArray<WorkerLaneHealth> laneHealth = lanes.Select(lane => new WorkerLaneHealth(lane.LaneId, lane.Matches, lane.Ticks,
                lane.CatchUpTicks, lane.DroppedTicks, lane.P50Milliseconds, lane.P95Milliseconds, lane.P99Milliseconds,
                lane.MaxMilliseconds, lane.P999Milliseconds, lane.DeadlineMisses, lane.CommandQueueHighWater)).ToImmutableArray();
            ImmutableArray<WorkerMatchHealth> matchHealth = _registry.ByWireId.Values
                .OrderBy(entry => entry.Spec.MatchId.Value).Take(WorkerDiagnostics.MaximumMatchSamples).Select(entry =>
                {
                    MatchInstanceStatus? status = entry.Snapshot;
                    MatchPerformanceSnapshot? performance = entry.Instance?.Performance;
                    MatchDiagnosticsSnapshot diagnostics = entry.Instance?.Diagnostics
                        ?? MatchDiagnosticsSnapshot.Empty;
                    StatusPublicationMetrics publication = default;
                    if (entry.Lease is { Lane: { } lane })
                        lane.TryGetStatusPublication(entry.Spec.MatchId.Value, out publication);
                    BoundedPercentileSnapshot tick = performance?.TickDurationMilliseconds ?? default;
                    return new WorkerMatchHealth(entry.Spec.MatchId, entry.WireId,
                        status?.Tick ?? 0, status?.Phase.ToString() ?? "Starting",
                        status?.State.ToString() ?? "Created", performance?.TickCount ?? 0, performance?.DeadlineMisses ?? 0,
                        tick.P50, tick.P95, tick.P99, tick.P999, tick.Max,
                        performance?.AllocatedBytesPerTick ?? 0, performance?.AllocatedBytesPerSecond ?? 0,
                        performance?.ProcessGen0Collections ?? 0, performance?.ProcessGen1Collections ?? 0,
                        performance?.ProcessGen2Collections ?? 0,
                        ObserverRetainedFrames: diagnostics.ObserverRetainedFrames,
                        ObserverRetainedBytes: diagnostics.ObserverRetainedBytes,
                        ReplayQueueDepth: diagnostics.ReplayQueueDepth,
                        ReplayQueueHighWater: diagnostics.ReplayQueueHighWater,
                        ReplayQueueOverflowed: diagnostics.ReplayQueueOverflowed,
                        StatusPublicationCount: publication.Count,
                        StatusPublicationCadenceHz: publication.CadenceHz);
                }).ToImmutableArray();
            long timingObserved = 0;
            long timingStale = 0;
            double timingMaxAge = 0;
            long timingStaleIntervals = 0;
            long timingDownshiftBlocked = 0;
            foreach (MatchRegistry.Entry entry in _registry.ByWireId.Values)
            {
                ServerTimingTelemetrySnapshot timing = entry.Instance?.TimingTelemetry
                    ?? ServerTimingTelemetrySnapshot.Empty;
                timingObserved += timing.ObservedConnections;
                timingStale += timing.StaleConnections;
                timingMaxAge = Math.Max(timingMaxAge, timing.MaxAgeSeconds);
                timingStaleIntervals += timing.StaleIntervals;
                timingDownshiftBlocked += timing.DownshiftBlocked;
            }
            return new(_options.WorkerId, _options.Incarnation, CapacityLocked(),
                new(_status, _health.UptimeMilliseconds, lanes.Max(lane => lane.P99Milliseconds), _health.WorkingSetBytes,
                    new(laneHealth, _health.CpuPercent, GC.GetTotalMemory(false),
                        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), _hub.Metrics.PacketsReceived,
                        _hub.Metrics.PacketsSent, _hub.Metrics.BytesReceived, _hub.Metrics.BytesSent,
                        _hub.Metrics.QueueDrops, _hub.Metrics.PacketsRejected,
                        matchHealth, _registry.ByWireId.Count, _registry.ByWireId.Count > WorkerDiagnostics.MaximumMatchSamples,
                        _health.AllocationBytesPerSecond, pumpDuration.P99, pumpDuration.P999,
                        _hub.Metrics.QueueHighWater, pumpDuration.TotalCount,
                        _hub.PreAuthIngressDrops, _hub.AdmissionIngressDrops,
                        _hub.EstablishedIngressDrops, _hub.PerConnectionQuotaDrops,
                        _hub.MaximumConnectionIngressDepth, _hub.CriticalTransportDrops,
                        timingObserved, timingStale, timingMaxAge, timingStaleIntervals,
                        timingDownshiftBlocked,
                        NetworkWakeups: networkLoop.NetworkWakeups,
                        ImmediateRepumps: networkLoop.ImmediateRepumps,
                        IdleWaits: networkLoop.IdleWaits,
                        ReceiveToRouteSampleCount: networkLoop.ReceiveToRouteAgeMilliseconds.TotalCount,
                        ReceiveToRouteP50Milliseconds: networkLoop.ReceiveToRouteAgeMilliseconds.P50,
                        ReceiveToRouteP95Milliseconds: networkLoop.ReceiveToRouteAgeMilliseconds.P95,
                        ReceiveToRouteP99Milliseconds: networkLoop.ReceiveToRouteAgeMilliseconds.P99,
                        ReceiveToRouteP999Milliseconds: networkLoop.ReceiveToRouteAgeMilliseconds.P999,
                        ReceiveToRouteMaxMilliseconds: networkLoop.ReceiveToRouteAgeMilliseconds.Max,
                        OutboundEnqueueToSendSampleCount: networkLoop.OutboundEnqueueToSendAgeMilliseconds.TotalCount,
                        OutboundEnqueueToSendP50Milliseconds: networkLoop.OutboundEnqueueToSendAgeMilliseconds.P50,
                        OutboundEnqueueToSendP95Milliseconds: networkLoop.OutboundEnqueueToSendAgeMilliseconds.P95,
                        OutboundEnqueueToSendP99Milliseconds: networkLoop.OutboundEnqueueToSendAgeMilliseconds.P99,
                        OutboundEnqueueToSendP999Milliseconds: networkLoop.OutboundEnqueueToSendAgeMilliseconds.P999,
                        OutboundEnqueueToSendMaxMilliseconds: networkLoop.OutboundEnqueueToSendAgeMilliseconds.Max,
                        LifetimeMatchesAccepted: _registry.Entries.Count,
                        IdentityHistoryUsed: _registry.Entries.Count,
                        IdentityHistoryCapacity: _options.CompletedHistoryCapacity,
                        ActiveAdmissions: _hub.ActiveAdmissionRouteCount)));
        }
    }
    private WorkerCapacity CapacityLocked() => new(_configuredLimit, _configuredPlayers, _registry.ByWireId.Count,
        _registry.ByWireId.Values.Sum(entry => entry.Snapshot?.PlayerCount ?? entry.Spec.Roster.Length));
    private void PumpNetwork()
    {
        try
        {
            while (!_ioStop.IsCancellationRequested)
            {
                WorkerNetworkPumpResult result = _hub.PumpOnce();
                if (result.CanImmediateRepump)
                {
                    _hub.RecordImmediateRepump();
                    continue;
                }
                // A producer may publish and signal between PumpOnce and the
                // wait. Recheck ownership-visible readiness immediately before
                // sleeping; AutoResetEvent retains an earlier signal.
                if (_hub.HasReadyNetworkWork) continue;
                long now = Stopwatch.GetTimestamp();
                long deadline = _hub.NextNetworkDeadlineTimestamp;
                if (_hub.HasReadyNetworkWork) continue;
                _hub.RecordIdleWait();
                _networkWake.WaitOne(NetworkWaitMilliseconds(now, deadline));
                _hub.RecordNetworkWakeup();
            }
        }
        catch (Exception error)
        {
            lock (_registry.Gate) _status = WorkerStatus.Faulted;
            Emit(new WorkerFault(_options.WorkerId, _options.Incarnation, Bounded(error.Message)));
        }
    }

    private static int NetworkWaitMilliseconds(long now, long deadline)
    {
        if (deadline == long.MaxValue) return Timeout.Infinite;
        long remaining = deadline - now;
        if (remaining <= 0) return 0;
        double milliseconds = remaining * (1000.0 / Stopwatch.Frequency);
        return milliseconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(milliseconds));
    }
    private void Emit(WorkerEvent value)
    {
        if (Event is not { } handlers) return;
        foreach (Action<WorkerEvent> handler in handlers.GetInvocationList())
            try { handler(value); } catch (Exception) { /* A control subscriber cannot terminate a simulation lane. */ }
    }
    private MatchPlacement Placement(MatchRegistry.Entry entry) => new(entry.Spec.MatchId, entry.WireId,
        _options.WorkerId, _options.Incarnation, _options.AdvertisedHost, checked((ushort)_hub.LocalPort),
        _options.UdpAuthenticationEnabled);
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
        Emit(new NodeSigningKeyUpdated(_options.WorkerId,
            _options.Incarnation, command.KeyId));
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
            _networkWake.Set();
            try
            {
                if (!_io.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Worker I/O did not stop.");
            }
            finally
            {
                _mapBuilds.Dispose();
                _hub.SetNetworkWake(null);
                _hub.Dispose();
                _ioStop.Dispose();
                _networkWake.Dispose();
                _contentLease.Dispose();
            }
            lock (_registry.Gate) _status = WorkerStatus.Stopped;
        }
    }
}
