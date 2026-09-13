using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading.Channels;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Workers;

public sealed class ManagedWorker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly NodeId _nodeId;
    private readonly Guid _nodeIncarnation;
    private readonly WorkerLaunchOptions _options;
    private readonly Channel<WorkerCommand> _commands;
    private readonly Channel<WorkerEvent> _events;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly NamedPipeServerStream _pipe;
    private readonly string _pipeName = "fp" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<MatchId, MatchStatus> _matches = new();
    private readonly Dictionary<MatchId, MatchSpec> _specs = new();
    private readonly Dictionary<MatchId, Guid> _completedReportIds = new();
    private readonly Dictionary<MatchId, MatchReportReady> _reportNotices = new();
    private readonly Dictionary<uint, MatchId> _wireMatches = new();
    private ushort? _boundPort;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private Task? _run;
    private Task? _dispose;
    private WorkerStatus _status = WorkerStatus.Starting;
    private WorkerCapacity _capacity;
    private WorkerHealth? _health;
    private WorkerContentIdentity? _authenticatedContent;
    private string? _failure;
    private long _lastHeartbeat;
    private bool _shutdownRequested;
    private bool _drainAcknowledged;
    private int? _processId;
    public WorkerId Id { get; } = new(Guid.NewGuid());
    public Guid Incarnation { get; } = Guid.NewGuid();
    public WorkerContentIdentity? Content => _authenticatedContent;
    public string? ArtifactDirectory => _options.ArtifactDirectory;
    public ChannelReader<WorkerEvent> Events => _events.Reader;
    public Task Completion => _finished.Task;

    internal ManagedWorker(NodeId nodeId, Guid nodeIncarnation, WorkerLaunchOptions options)
    {
        _nodeId = nodeId; _nodeIncarnation = nodeIncarnation; _options = options; _capacity = options.Capacity;
        _boundPort = options.RequestedPort == 0 ? null : options.RequestedPort;
        _commands = Channel.CreateBounded<WorkerCommand>(new BoundedChannelOptions(options.CommandCapacity)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _events = Channel.CreateBounded<WorkerEvent>(new BoundedChannelOptions(options.EventCapacity)
            { SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public WorkerSnapshot Snapshot()
    {
        lock (_gate) return new(Id, Incarnation, _status, _capacity, _health,
            new Dictionary<MatchId, MatchStatus>(_matches), _failure, _processId);
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose != null, this);
            _run = RunAsync();
        }
        try { await _ready.Task.WaitAsync(_options.StartupTimeout, cancellationToken); }
        catch { _lifetime.Cancel(); await Completion; throw; }
    }

    /// <summary>Nonblocking bounded admission. The caller retains ownership when false is returned.</summary>
    public bool TrySend(WorkerCommand command)
    {
        // Validate before mutating placement ownership; never put a malformed frame in the queue.
        WorkerIpcCodec.Encode(command);
        lock (_gate)
        {
            if (_status is WorkerStatus.Faulted or WorkerStatus.Stopped or WorkerStatus.Starting) return false;
            if (command is WorkerConfigure) throw new ArgumentException("Configuration is owned by startup.");
            if (command is CreateMatch create)
            {
                if (_status != WorkerStatus.Ready || create.Spec.NodeId != _nodeId || create.Spec.NodeIncarnation != _nodeIncarnation
                    || _matches.Count >= 4096 || _matches.ContainsKey(create.Spec.MatchId)
                    || _matches.Values.Count(IsActive) >= _capacity.MatchLimit
                    || _matches.Where(p => IsActive(p.Value)).Sum(p => _specs[p.Key].Roster.Length) + create.Spec.Roster.Length > _capacity.PlayerLimit) return false;
                if (!_commands.Writer.TryWrite(command)) return false;
                _matches.Add(create.Spec.MatchId, MatchStatus.Starting);
                _specs.Add(create.Spec.MatchId, create.Spec);
                return true;
            }
            MatchId? target = command switch
            {
                CancelMatch c => c.MatchId,
                MatchAdminCommand c => c.MatchId,
                InstallAdmissionKey c => c.MatchId,
                RetireAdmission c => c.MatchId,
                _ => null
            };
            if (target is { } id && (!_matches.TryGetValue(id, out var status) || !IsActive(status))) return false;
            if (command is InstallAdmissionKey admission
                && (admission.WorkerId != Id || admission.WorkerIncarnation != Incarnation)) return false;
            if (!_commands.Writer.TryWrite(command)) return false;
            if (command is Drain) _status = WorkerStatus.Draining;
            if (command is Shutdown) { _shutdownRequested = true; _status = WorkerStatus.Draining; }
            return true;
        }
    }

    /// <summary>Release a terminal record only after the coordinator has durably consumed its outcome.</summary>
    public bool ForgetMatch(MatchId matchId)
    {
        lock (_gate)
        {
            if (!_matches.TryGetValue(matchId, out var status) || IsActive(status)) return false;
            _specs.Remove(matchId); _completedReportIds.Remove(matchId); _reportNotices.Remove(matchId);
            foreach (uint wire in _wireMatches.Where(p => p.Value == matchId).Select(p => p.Key).ToArray()) _wireMatches.Remove(wire);
            return _matches.Remove(matchId);
        }
    }

    public bool Drain(string reason) => TrySend(new Drain(reason));

    /// <summary>
    /// Stops this child without asking it to complete its matches. The normal
    /// lifecycle reader observes the resulting interruption events, so the
    /// scheduler can apply its ordinary Worker-loss handling to every match.
    /// </summary>
    internal async Task ForceStopAsync()
    {
        _lifetime.Cancel();
        await Completion.ConfigureAwait(false);
    }

    public async Task ShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!Completion.IsCompleted && !TrySend(new Shutdown(reason)))
        { _lifetime.Cancel(); }
        try { await Completion.WaitAsync(_options.ShutdownTimeout, cancellationToken); }
        catch { _lifetime.Cancel(); await Completion; throw; }
    }

    private async Task RunAsync()
    {
        Task[] pumps = [];
        byte[] token = RandomNumberGenerator.GetBytes(32);
        try
        {
            // Package-relative Worker apphosts are resolved from the Node
            // application directory. Bare commands such as "dotnet" remain
            // PATH lookups; only the executable field is resolved, never the
            // opaque argument list.
            var start = new ProcessStartInfo(WorkerLaunchOptions.ResolveExecutableFileName(_options.FileName))
            {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            // Only runtime/platform environment and the explicit map cache root reach the child.
            // Node reporting and signing credentials must not cross the process boundary through
            // inherited environment.
            start.Environment.Clear();
            foreach (string name in new[] { "PATH", "HOME", "TMPDIR", "TEMP", "TMP", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64",
                "SystemRoot", "SystemDrive", "WINDIR", "USERPROFILE", "LOCALAPPDATA", "COMSPEC", "LANG", "LC_ALL", "TZ",
                "PRIME_DATA_DIRECTORY" })
                if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
            if (_options.WorkingDirectory is { } directory) start.WorkingDirectory = directory;
            foreach (string argument in _options.Arguments) start.ArgumentList.Add(argument);
            start.ArgumentList.Add("--snapshot-rate-hz");
            start.ArgumentList.Add(_options.SnapshotRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--adaptive-timing");
            start.ArgumentList.Add(_options.AdaptiveTimingEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--adaptive-timing-v2");
            start.ArgumentList.Add(_options.AdaptiveTimingV2Enabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--adaptive-input-playout");
            start.ArgumentList.Add(_options.AdaptiveInputPlayoutEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--transport-queue-v2");
            start.ArgumentList.Add(_options.TransportQueueV2Enabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--transport-critical-reserve-enabled");
            start.ArgumentList.Add(_options.TransportCriticalReserveEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--critical-transport-reserve");
            start.ArgumentList.Add(_options.CriticalTransportReserve.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--worker-global-network-budget-enabled");
            start.ArgumentList.Add(_options.WorkerGlobalNetworkBudgetEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--max-datagrams-per-pump");
            start.ArgumentList.Add(_options.MaximumDatagramsPerPump.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--reliable-adaptive-rto");
            start.ArgumentList.Add(_options.ReliableAdaptiveRtoEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--ack-coalescing");
            start.ArgumentList.Add(_options.AckCoalescingEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--udp-authentication");
            start.ArgumentList.Add(_options.UdpAuthenticationEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_options.ArtifactDirectory is { } artifacts) { start.ArgumentList.Add("--artifact-dir"); start.ArgumentList.Add(artifacts); }
            foreach (string argument in new[] { "--node-pipe", _pipeName, "--node-id", _nodeId.Value.ToString("D"),
                "--worker-id", Id.Value.ToString("D"), "--worker-incarnation", Incarnation.ToString("D") }) start.ArgumentList.Add(argument);
            _process = Process.Start(start) ?? throw new IOException("Worker process could not start.");
            _processId = _process.Id;
            // Drain without retaining/logging child output: a child must not be able to exhaust Node memory or leak credentials.
            Task stdout = _process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, _lifetime.Token);
            Task stderr = _process.StandardError.BaseStream.CopyToAsync(Stream.Null, _lifetime.Token);
            pumps = [stdout, stderr];
            Task exited = _process.WaitForExitAsync();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            startup.CancelAfter(_options.StartupTimeout);
            await _process.StandardInput.WriteLineAsync(Convert.ToHexString(token).AsMemory(), startup.Token);
            _process.StandardInput.Close();
            Task connect = _pipe.WaitForConnectionAsync(startup.Token);
            if (await Task.WhenAny(connect, exited) == exited) throw new IOException("Worker exited before IPC connection.");
            await connect;
            WorkerMessage? first = await WorkerIpcCodec.ReadAsync(_pipe, startup.Token);
            if (first is not WorkerHello hello || hello.WorkerId != Id || hello.WorkerIncarnation != Incarnation || hello.NodeId != _nodeId
                || !TokenMatches(hello.StartupToken, token)
                || _options.Content is { } expected && (hello.Content != expected || hello.BuildVersion != expected.BuildVersion)) throw new InvalidDataException("Worker authentication failed.");
            _authenticatedContent = hello.Content;
            CryptographicOperations.ZeroMemory(token);
            await WorkerIpcCodec.WriteAsync(_pipe, new WorkerConfigure(_nodeId, _nodeIncarnation, _options.Capacity), startup.Token);
            _lastHeartbeat = Stopwatch.GetTimestamp();
            Task read = ReadEventsAsync();
            Task write = WriteCommandsAsync();
            Task heartbeat = WatchHeartbeatAsync();
            pumps = [stdout, stderr, read, write, heartbeat];
            Task winner = await Task.WhenAny(read, write, heartbeat, exited);
            await winner;
            bool orderly;
            lock (_gate) orderly = (_shutdownRequested || _drainAcknowledged) && _matches.Values.All(s => !IsActive(s));
            if (!orderly) throw new IOException("Worker IPC disconnected or process exited unexpectedly.");
            lock (_gate) _status = WorkerStatus.Stopped;
        }
        catch (Exception error)
        {
            // Never include payloads, arguments, or token text in lifecycle errors.
            string reason = error switch
            {
                OperationCanceledException => "Worker lifecycle cancelled or timed out.",
                InvalidDataException => "Worker IPC authentication or protocol violation.",
                TimeoutException => "Worker heartbeat timed out.",
                _ => "Worker process or IPC failed."
            };
            lock (_gate)
            {
                _status = WorkerStatus.Faulted; _failure = reason;
                foreach (MatchId id in _matches.Keys.ToArray()) if (IsActive(_matches[id])) _matches[id] = MatchStatus.Interrupted;
            }
            // Snapshot retains every interrupted identity even when the bounded event consumer is stalled.
            foreach (var pair in Snapshot().Matches)
                if (pair.Value == MatchStatus.Interrupted) _events.Writer.TryWrite(new MatchInterrupted(pair.Key, reason));
            _events.Writer.TryWrite(new WorkerFault(Id, Incarnation, reason));
            _ready.TrySetException(new IOException(reason));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
            _lifetime.Cancel();
            _commands.Writer.TryComplete();
            try
            {
                await _pipe.DisposeAsync();
                if (_process is { } process)
                {
                    try
                    {
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                        await process.WaitForExitAsync().WaitAsync(_options.ShutdownTimeout);
                    }
                    finally { process.Dispose(); }
                }
                try { await Task.WhenAll(pumps); } catch { /* Terminal state above owns the failure. */ }
            }
            catch (Exception)
            {
                lock (_gate) { _status = WorkerStatus.Faulted; _failure = "Worker child cleanup failed."; }
            }
            finally
            {
                _ready.TrySetException(new IOException("Worker stopped before becoming ready."));
                _events.Writer.TryComplete();
                _finished.TrySetResult();
            }
        }
    }

    private static bool TokenMatches(string candidate, ReadOnlySpan<byte> token)
    {
        if (candidate.Length != 64) return false;
        byte[] decoded;
        try { decoded = Convert.FromHexString(candidate); }
        catch (FormatException) { return false; }
        try { return CryptographicOperations.FixedTimeEquals(decoded, token); }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    private async Task WriteCommandsAsync()
    {
        await foreach (WorkerCommand command in _commands.Reader.ReadAllAsync(_lifetime.Token))
            await WorkerIpcCodec.WriteAsync(_pipe, command, _lifetime.Token);
    }

    private async Task WatchHeartbeatAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, _options.HeartbeatTimeout.TotalMilliseconds / 2)), _lifetime.Token);
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastHeartbeat)) > _options.HeartbeatTimeout)
                throw new TimeoutException();
        }
    }

    private async Task ReadEventsAsync()
    {
        while (await WorkerIpcCodec.ReadAsync(_pipe, _lifetime.Token) is { } message)
        {
            if (message is not WorkerEvent value || value is WorkerHello) throw new InvalidDataException();
            lock (_gate)
            {
                if (_status is WorkerStatus.Faulted or WorkerStatus.Stopped) throw new InvalidDataException();
                switch (value)
                {
                    case WorkerReady ready:
                        CheckIdentity(ready.WorkerId, ready.WorkerIncarnation);
                        if (_status != WorkerStatus.Starting || ready.Capacity.ActiveMatches != 0 || ready.Capacity.ActivePlayers != 0) throw new InvalidDataException();
                        ValidateCapacity(ready.Capacity);
                        _capacity = ready.Capacity; _status = WorkerStatus.Ready; break;
                    case WorkerHeartbeat heartbeat:
                        CheckIdentity(heartbeat.WorkerId, heartbeat.WorkerIncarnation);
                        if (_status == WorkerStatus.Starting) throw new InvalidDataException();
                        ValidateCapacity(heartbeat.Capacity);
                        if (heartbeat.Health.Status is WorkerStatus.Starting or WorkerStatus.Faulted or WorkerStatus.Stopped
                            || heartbeat.Health.Status == WorkerStatus.Draining && _status != WorkerStatus.Draining) throw new IOException("Worker reported unhealthy status.");
                        _capacity = heartbeat.Capacity; _health = heartbeat.Health;
                        Interlocked.Exchange(ref _lastHeartbeat, Stopwatch.GetTimestamp()); break;
                    case WorkerDraining draining:
                        CheckIdentity(draining.WorkerId, draining.WorkerIncarnation);
                        if (_status != WorkerStatus.Draining) throw new InvalidDataException();
                        _drainAcknowledged = true; break;
                    case WorkerFault fault:
                        CheckIdentity(fault.WorkerId, fault.WorkerIncarnation); throw new IOException("Worker reported fault.");
                    case NodeSigningKeyUpdated signingKey:
                        CheckIdentity(signingKey.WorkerId, signingKey.WorkerIncarnation);
                        break;
                    case MatchReady ready:
                        CheckIdentity(ready.Placement.WorkerId, ready.Placement.WorkerIncarnation);
                        if (!String.Equals(ready.Placement.Host, _options.AdvertisedHost, StringComparison.OrdinalIgnoreCase)
                            || _boundPort is { } port && ready.Placement.Port != port
                            || _wireMatches.ContainsKey(ready.Placement.WireMatchId.Value)) throw new InvalidDataException("Worker endpoint or wire identity mismatch.");
                        _boundPort = ready.Placement.Port;
                        _wireMatches.Add(ready.Placement.WireMatchId.Value, ready.Placement.MatchId);
                        Transition(ready.Placement.MatchId, MatchStatus.Ready); break;
                    case MatchStarted started: Transition(started.MatchId, MatchStatus.Running); break;
                    case MatchCompleted completed:
                        if (!_specs.TryGetValue(completed.Summary.MatchId, out var spec) || completed.Summary.LobbyId != spec.LobbyId
                            || completed.Summary.Players.Any(p => p.PlayerId is { } player && !spec.Roster.Any(seat => seat.PlayerId == player)))
                            throw new InvalidDataException("Completion ownership mismatch.");
                        Transition(completed.Summary.MatchId, MatchStatus.Completed);
                        _completedReportIds.Add(completed.Summary.MatchId, completed.Summary.ReportId); break;
                    case MatchFailed failed: Transition(failed.MatchId, MatchStatus.Failed); break;
                    case MatchInterrupted interrupted: Transition(interrupted.MatchId, MatchStatus.Interrupted); break;
                    case MatchAdminResult admin:
                        if (!_matches.TryGetValue(admin.MatchId, out var adminStatus) || !IsActive(adminStatus)) throw new InvalidDataException();
                        break;
                    case AdmissionKeyInstalled admission:
                        CheckIdentity(admission.WorkerId, admission.WorkerIncarnation);
                        // The command and terminal event are independent lanes: a
                        // valid install acknowledgement can arrive after the
                        // match has ended or its placement was forgotten. The
                        // scheduler owns the pending admission identity and
                        // drops such acknowledgements as stale; do not turn this
                        // expected race into a Worker protocol fault.
                        break;
                    case AdmissionKeyInstallFailed admission:
                        break;
                    case MatchCancelAccepted cancelled:
                        CheckIdentity(cancelled.WorkerId, cancelled.WorkerIncarnation);
                        ValidateOperation(cancelled.OperationId);
                        break;
                    case MatchCancelRejected rejected:
                        CheckIdentity(rejected.WorkerId, rejected.WorkerIncarnation);
                        ValidateOperation(rejected.OperationId);
                        break;
                    case AdmissionRetired retired:
                        CheckIdentity(retired.WorkerId, retired.WorkerIncarnation);
                        break;
                    case AdmissionRetireFailed retireFailed:
                        CheckIdentity(retireFailed.WorkerId, retireFailed.WorkerIncarnation);
                        break;
                    case MatchReportReady report:
                        CheckIdentity(report.WorkerId, report.WorkerIncarnation);
                        if (!_matches.TryGetValue(report.MatchId, out var reportStatus) || reportStatus != MatchStatus.Completed
                            || !_completedReportIds.TryGetValue(report.MatchId, out var reportId) || reportId != report.ReportId
                            || _reportNotices.TryGetValue(report.MatchId, out var priorReport) && priorReport != report)
                            throw new InvalidDataException("Report does not match completed immutable outcome.");
                        _reportNotices[report.MatchId] = report; break;
                    default: throw new InvalidDataException();
                }
            }
            if (!_events.Writer.TryWrite(value)) throw new IOException("Worker event consumer exceeded bounded capacity.");
            if (value is WorkerReady) _ready.TrySetResult();
        }
    }

    private void ValidateCapacity(WorkerCapacity capacity)
    {
        if (capacity.MatchLimit > _options.Capacity.MatchLimit || capacity.PlayerLimit > _options.Capacity.PlayerLimit)
            throw new InvalidDataException("Worker exceeded configured capacity.");
    }

    private void CheckIdentity(WorkerId worker, Guid incarnation)
    { if (worker != Id || incarnation != Incarnation) throw new InvalidDataException(); }
    private static void ValidateOperation(string operation)
    {
        if (operation is not { Length: > 0 and <= 128 } || operation.Any(char.IsControl))
            throw new InvalidDataException();
    }
    private static bool IsActive(MatchStatus status) => status is MatchStatus.Starting or MatchStatus.Ready or MatchStatus.Running;
    private void Transition(MatchId id, MatchStatus next)
    {
        if (!_matches.TryGetValue(id, out MatchStatus previous) || !IsActive(previous)
            || next == MatchStatus.Ready && previous != MatchStatus.Starting
            || next == MatchStatus.Running && previous != MatchStatus.Ready
            || next == MatchStatus.Completed && previous != MatchStatus.Running) throw new InvalidDataException();
        _matches[id] = next;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_dispose ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        if (_run == null)
        {
            _lifetime.Cancel(); await _pipe.DisposeAsync(); _events.Writer.TryComplete(); _finished.TrySetResult();
        }
        else
        {
            try { await ShutdownAsync("Node shutting down."); }
            catch (TimeoutException) { /* Forced termination has already completed. */ }
        }
    }
}
