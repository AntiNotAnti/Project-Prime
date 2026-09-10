using System.Security.Cryptography;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectPrime.Server.Node.Reporting;
using MphRead.Identity;

namespace ProjectPrime.Server.Node.Workers;

public sealed record WorkerMatchAssignment(MatchSpec Spec, WorkerId WorkerId, Guid WorkerIncarnation,
    MatchPlacement? Placement, string? ArtifactDirectory);

public sealed class WorkerPlacementException(string message) : Exception(message);

/// <summary>One event consumer per registered worker. Serializes placement reservations and
/// returns the same creation task for retries of the identical frozen MatchSpec.</summary>
public sealed class WorkerScheduler : IAsyncDisposable
{
    private sealed class Placement(MatchSpec spec, byte[] hash, ManagedWorker worker)
    {
        public MatchSpec Spec = spec;
        public byte[] Hash = hash;
        public ManagedWorker Worker = worker;
        public TaskCompletionSource<MatchPlacement> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MatchStatus Status = MatchStatus.Starting;
        public bool ReportExpected, ReportQueued;
        public CancellationTokenSource Deadline = new();
    }
    private readonly object _gate = new();
    private readonly WorkerManager _manager;
    private readonly Dictionary<WorkerId, ManagedWorker> _workers = new();
    private readonly Dictionary<WorkerId, Task> _readers = new();
    private readonly Dictionary<MatchId, Placement> _placements = new();
    private readonly TimeSpan _creationTimeout;
    private readonly double _maximumTickP99;
    private readonly ILogger<WorkerScheduler> _logger;
    private bool _draining, _disposed;
    private readonly NodeReportIngestor? _reports;
    private string? _reportFailure;
    public event Action<MatchId, bool>? Ended;
    public event Action<MatchCompletionSummary>? Completed;
    public event Action<ManagedWorker, WorkerEvent>? Observed;
    public event Action<WorkerMatchAssignment, MatchReportReady>? ReportReady;

    public WorkerScheduler(WorkerManager manager, TimeSpan? creationTimeout = null,
        double maximumTickP99 = 16.6667, ILogger<WorkerScheduler>? logger = null, NodeReportIngestor? reports = null)
    {
        _manager = manager; _reports = reports; _creationTimeout = creationTimeout ?? TimeSpan.FromSeconds(30);
        if (_creationTimeout <= TimeSpan.Zero || !double.IsFinite(maximumTickP99) || maximumTickP99 <= 0) throw new ArgumentOutOfRangeException();
        _maximumTickP99 = maximumTickP99; _logger = logger ?? NullLogger<WorkerScheduler>.Instance;
    }

    public async Task<ManagedWorker> StartWorkerAsync(WorkerLaunchOptions options, CancellationToken cancellationToken = default)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); if (_draining) throw new WorkerPlacementException("Node is draining."); }
        ManagedWorker worker = await _manager.StartAsync(options, cancellationToken);
        bool reject;
        lock (_gate)
        {
            reject = _disposed || _draining;
            if (!reject) { _workers.Add(worker.Id, worker); _readers.Add(worker.Id, ConsumeAsync(worker)); }
        }
        if (reject) { await worker.DisposeAsync(); throw new WorkerPlacementException("Node is draining."); }
        return worker;
    }

    public Task<MatchPlacement> PlaceAsync(MatchSpec spec, CancellationToken cancellationToken = default)
    {
        spec.Validate();
        byte[] hash = SHA256.HashData(WorkerIpcCodec.Encode(new CreateMatch(spec)));
        Task<MatchPlacement> result;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_placements.TryGetValue(spec.MatchId, out var existing))
            {
                if (!CryptographicOperations.FixedTimeEquals(hash, existing.Hash)) throw new WorkerPlacementException("Match identity was reused with a different specification.");
                result = existing.Ready.Task;
            }
            else
            {
                if (_draining) throw new WorkerPlacementException("Node is draining.");
                if (_placements.Count >= 4096) throw new WorkerPlacementException("Placement retention capacity reached.");
                var candidates = _workers.Values.Select(w => (Worker: w, State: w.Snapshot()))
                    .Where(p => p.State.Status == WorkerStatus.Ready && Compatible(p.Worker.Content, spec.Content)
                        && (p.State.Health == null || p.State.Health.Status == WorkerStatus.Ready
                            && p.State.Health.TickP99Milliseconds <= _maximumTickP99
                            && (p.State.Health.Diagnostics == null || p.State.Health.Diagnostics.CpuPercent < 95)))
                    .OrderBy(p => p.State.Matches.Values.Count(IsActive)).ThenBy(p => p.Worker.Id.Value).ToArray();
                bool reserved = RequiresBackendReport(spec);
                if (reserved && (_reports == null || !_reports.TryReserve(spec.MatchId)))
                    throw new WorkerPlacementException("Official reporting is unavailable or at capacity.");
                ManagedWorker? selected = null;
                foreach (var candidate in candidates)
                    if (candidate.Worker.TrySend(new CreateMatch(spec))) { selected = candidate.Worker; break; }
                if (selected == null)
                {
                    if (reserved) _reports!.CancelReservation(spec.MatchId);
                    throw new WorkerPlacementException("No compatible healthy worker has capacity.");
                }
                var placement = new Placement(spec, hash, selected);
                _placements.Add(spec.MatchId, placement);
                _ = ExpireCreationAsync(placement);
                result = placement.Ready.Task;
            }
        }
        // A disconnected caller does not cancel a reservation shared by retries.
        return cancellationToken.CanBeCanceled ? result.WaitAsync(cancellationToken) : result;
    }

    public bool TryGetAssignment(MatchId matchId, out WorkerMatchAssignment? assignment)
    {
        lock (_gate)
        {
            assignment = _placements.TryGetValue(matchId, out var p) ? Assignment(p) : null;
            return assignment != null;
        }
    }
    private static WorkerMatchAssignment Assignment(Placement p) => new(p.Spec, p.Worker.Id, p.Worker.Incarnation,
        p.Ready.Task.IsCompletedSuccessfully ? p.Ready.Task.Result : null, p.Worker.ArtifactDirectory);

    public bool ForgetMatch(MatchId matchId)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out var p) || IsActive(p.Status) || p.ReportExpected && !p.ReportQueued) return false;
            p.Worker.ForgetMatch(matchId); p.Deadline.Dispose(); return _placements.Remove(matchId);
        }
    }

    public bool CancelMatch(MatchId matchId, string reason)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out var p) || !IsActive(p.Status)) return false;
            if (p.Worker.TrySend(new CancelMatch(matchId, reason))) return true;
            _ = p.Worker.DisposeAsync();
            return false;
        }
    }

    /// <summary>
    /// Routes a validated match-admin command over the Node-owned authenticated
    /// Worker pipe. This is intentionally a host integration point, not a
    /// public player/control-socket command.
    /// </summary>
    public bool TrySendMatchAdmin(MatchAdminCommand command)
    {
        WorkerIpcCodec.Encode(command);
        lock (_gate)
        {
            if (!_placements.TryGetValue(command.MatchId, out var placement) || !IsActive(placement.Status))
                return false;
            return placement.Worker.TrySend(command);
        }
    }

    public void Drain(string reason)
    {
        lock (_gate)
        {
            _draining = true;
            foreach (ManagedWorker worker in _workers.Values)
                if (!worker.Drain(reason) && !worker.Completion.IsCompleted) _ = worker.DisposeAsync();
        }
    }

    public async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_reportFailure != null) throw new IOException(_reportFailure);
                if (_placements.Values.All(p => !IsActive(p.Status) && (!p.ReportExpected || p.ReportQueued))) break;
            }
            await Task.Delay(25, cancellationToken);
        }
        if (_reports != null) await _reports.WaitForDurabilityAsync(cancellationToken);
    }

    public async Task ShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        Drain(reason);
        ManagedWorker[] workers;
        lock (_gate) workers = _workers.Values.ToArray();
        await Task.WhenAll(workers.Select(w => w.ShutdownAsync(reason, cancellationToken)));
    }

    private static bool Compatible(WorkerContentIdentity? worker, ContentIdentity content) => worker != null
        && worker.ContentVersion == content.ContentVersion && worker.ContentHash == content.ContentHash
        && worker.BuildVersion == content.BuildVersion && worker.ProtocolVersion == content.ProtocolVersion;
    private static bool IsActive(MatchStatus status) => status is MatchStatus.Starting or MatchStatus.Ready or MatchStatus.Running;

    private async Task ExpireCreationAsync(Placement placement)
    {
        try { await Task.Delay(_creationTimeout, placement.Deadline.Token); }
        catch (OperationCanceledException) { return; }
        bool notify = false;
        lock (_gate)
        {
            if (placement.Status != MatchStatus.Starting) return;
            placement.Status = MatchStatus.Interrupted;
            placement.Ready.TrySetException(new WorkerPlacementException("Worker match creation timed out."));
            if (!placement.Worker.TrySend(new CancelMatch(placement.Spec.MatchId, "Match creation timed out."))) _ = placement.Worker.DisposeAsync();
            notify = true;
        }
        if (notify) NotifyEnded(placement.Spec.MatchId, true);
    }

    private async Task ConsumeAsync(ManagedWorker worker)
    {
        await foreach (WorkerEvent message in worker.Events.ReadAllAsync())
        {
            if (Observed is { } observers)
                foreach (Action<ManagedWorker, WorkerEvent> observer in observers.GetInvocationList())
                    try { observer(worker, message); }
                    catch (Exception error) { _logger.LogError(error, "Worker event observer failed for {WorkerId}", worker.Id.Value); }
            if (message is MatchReportReady report)
            {
                WorkerMatchAssignment? assignment = null;
                lock (_gate)
                    if (_placements.TryGetValue(report.MatchId, out var p) && p.Worker == worker) assignment = Assignment(p);
                if (assignment != null && ContainsGuest(assignment.Spec))
                {
                    // Guest matches still produce and validate a local report-ready
                    // event, but their artifacts never acquire Backend ownership.
                    if (assignment.Placement is not { } placement || assignment.ArtifactDirectory is not { } root
                        || !NodeReportIngestor.TryDiscardArtifact(assignment.Spec, assignment.WorkerId,
                            assignment.WorkerIncarnation, placement.WireMatchId.Value, root, report))
                        _logger.LogWarning("Suppressed guest report artifact could not be validated and cleaned for {MatchId}; Backend submission remains disabled.", report.MatchId.Value);
                    // Completion stays visible immediately; retention/drain waits only
                    // for this local cleanup attempt, never for Backend submission.
                    lock (_gate) if (_placements.TryGetValue(report.MatchId, out var p)) p.ReportQueued = true;
                }
                else if (assignment != null && _reports != null)
                {
                    if (assignment.Placement is not { } placement || assignment.ArtifactDirectory is not { } root
                        || !_reports.TryQueue(assignment.Spec, assignment.WorkerId, assignment.WorkerIncarnation,
                            placement.WireMatchId.Value, root, report))
                    {
                        lock (_gate) { _reportFailure = "Report ingestion admission failed; worker artifact retained."; _draining = true; }
                        _logger.LogError("Worker report could not be queued for {MatchId}", report.MatchId.Value);
                    }
                    else
                    {
                        lock (_gate) if (_placements.TryGetValue(report.MatchId, out var p)) p.ReportQueued = true;
                    }
                }
                if (assignment != null && ReportReady is { } subscribers)
                    foreach (Action<WorkerMatchAssignment, MatchReportReady> subscriber in subscribers.GetInvocationList())
                        try { subscriber(assignment, report); }
                        catch (Exception error) { _logger.LogError(error, "Report subscriber failed for {MatchId}", report.MatchId.Value); }
                continue;
            }
            MatchId? matchId = message switch
            {
                MatchReady m => m.Placement.MatchId, MatchStarted m => m.MatchId, MatchCompleted m => m.Summary.MatchId,
                MatchFailed m => m.MatchId, MatchInterrupted m => m.MatchId, _ => null
            };
            if (matchId is not { } id) continue;
            bool notify = false, interrupted = true;
            lock (_gate)
            {
                if (!_placements.TryGetValue(id, out var p) || p.Worker != worker || !IsActive(p.Status)) continue;
                switch (message)
                {
                    case MatchReady ready:
                        p.Status = MatchStatus.Ready; p.Deadline.Cancel(); p.Ready.TrySetResult(ready.Placement); break;
                    case MatchStarted: p.Status = MatchStatus.Running; break;
                    case MatchCompleted:
                        p.Status = MatchStatus.Completed;
                        p.ReportExpected = ContainsGuest(p.Spec) ? p.Worker.ArtifactDirectory != null : _reports != null;
                        interrupted = false; notify = true; break;
                    case MatchFailed: p.Status = MatchStatus.Failed; notify = true; break;
                    case MatchInterrupted: p.Status = MatchStatus.Interrupted; notify = true; break;
                }
                if (notify)
                {
                    p.Deadline.Cancel();
                    string reason = message switch
                    {
                        MatchFailed failure => failure.Reason,
                        MatchInterrupted interruption => interruption.Reason,
                        _ => "Worker ended the match before placement completed."
                    };
                    p.Ready.TrySetException(new WorkerPlacementException(reason.Replace('\r', ' ').Replace('\n', ' ')));
                }
            }
            if (notify)
            {
                if (message is MatchCompleted completed) NotifyCompleted(completed.Summary);
                NotifyEnded(id, interrupted);
            }
        }
        await worker.Completion;
        MatchId[] interruptedIds;
        lock (_gate)
        {
            // A lost Worker cannot deliver a remaining guest artifact notice.
            foreach (var placement in _placements.Values.Where(p => p.Worker == worker && ContainsGuest(p.Spec)))
                placement.ReportQueued = true;
            interruptedIds = _placements.Where(p => p.Value.Worker == worker && IsActive(p.Value.Status)).Select(p => p.Key).ToArray();
            foreach (MatchId id in interruptedIds)
            {
                var p = _placements[id]; p.Status = MatchStatus.Interrupted; p.Deadline.Cancel();
                p.Ready.TrySetException(new WorkerPlacementException("Worker was lost during match creation."));
            }
        }
        foreach (MatchId id in interruptedIds) NotifyEnded(id, true);
    }

    private void NotifyEnded(MatchId id, bool interrupted)
    {
        if (interrupted) _reports?.CancelReservation(id);
        if (Ended is not { } handlers) return;
        foreach (Action<MatchId, bool> handler in handlers.GetInvocationList())
            try { handler(id, interrupted); }
            catch (Exception error) { _logger.LogError(error, "Match outcome subscriber failed for {MatchId}", id.Value); }
    }

    private void NotifyCompleted(MatchCompletionSummary summary)
    {
        if (Completed is not { } handlers) return;
        foreach (Action<MatchCompletionSummary> handler in handlers.GetInvocationList())
            try { handler(summary); }
            catch (Exception error) { _logger.LogError(error, "Match completion subscriber failed for {MatchId}", summary.MatchId.Value); }
    }

    private static bool ContainsGuest(MatchSpec spec) => spec.Roster.Any(seat => seat.GuestSessionId.HasValue);
    private static bool RequiresBackendReport(MatchSpec spec)
        => !ContainsGuest(spec) && (spec.TrustClass is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked or MatchTrustClass.Tournament);

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _draining = true; }
        await _manager.DisposeAsync();
        Task[] readers;
        lock (_gate) readers = _readers.Values.ToArray();
        await Task.WhenAll(readers);
        lock (_gate) foreach (var p in _placements.Values) { p.Deadline.Cancel(); p.Deadline.Dispose(); }
    }
}
