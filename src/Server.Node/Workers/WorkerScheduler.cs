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
        // Terminal ownership is claimed under _gate. A transition claim keeps
        // the placement active until the Worker acknowledges cancellation so
        // capacity cannot be reused while the old MatchInstance still runs.
        public TerminalClaim Claim;
        public bool TransitionCancelSent;
        public bool ReportAdmissionClaimed;
        public CancellationTokenSource Deadline = new();
    }
    private enum TerminalClaim { None, Completion, Transition }
    private sealed class AdmissionInstall(InstallAdmissionKey command, ManagedWorker worker)
    {
        public InstallAdmissionKey Command { get; } = command;
        public ManagedWorker Worker { get; } = worker;
        public TaskCompletionSource<AdmissionKeyInstalled> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly WorkerManager _manager;
    private readonly Dictionary<WorkerId, ManagedWorker> _workers = new();
    private readonly Dictionary<WorkerId, Task> _readers = new();
    private readonly Dictionary<MatchId, Placement> _placements = new();
    private readonly Dictionary<Guid, AdmissionInstall> _admissionInstalls = new();
    private readonly TimeSpan _creationTimeout;
    private readonly TimeSpan _admissionInstallTimeout;
    private readonly double _maximumTickP99;
    private readonly ILogger<WorkerScheduler> _logger;
    private bool _draining, _disposed;
    private readonly NodeReportIngestor? _reports;
    private string? _reportFailure;
    public event Action<MatchId, bool>? Ended;
    /// <summary>Raised only for a Worker terminal acknowledgement belonging to
    /// an accepted active-match transition. It is intentionally separate from
    /// <see cref="Ended"/> so transition cancellation cannot look like a
    /// player-visible ordinary interruption.</summary>
    public event Action<MatchId, bool>? TransitionEnded;
    public event Action<MatchCompletionSummary>? Completed;
    public event Action<ManagedWorker, WorkerEvent>? Observed;
    public event Action<WorkerMatchAssignment, MatchReportReady>? ReportReady;

    public WorkerScheduler(WorkerManager manager, TimeSpan? creationTimeout = null,
        double maximumTickP99 = 16.6667, ILogger<WorkerScheduler>? logger = null, NodeReportIngestor? reports = null,
        TimeSpan? admissionInstallTimeout = null)
    {
        _manager = manager; _reports = reports; _creationTimeout = creationTimeout ?? TimeSpan.FromSeconds(30);
        _admissionInstallTimeout = admissionInstallTimeout ?? TimeSpan.FromSeconds(5);
        if (_creationTimeout <= TimeSpan.Zero || _admissionInstallTimeout <= TimeSpan.Zero
            || !double.IsFinite(maximumTickP99) || maximumTickP99 <= 0) throw new ArgumentOutOfRangeException();
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
        if (reject)
        {
            await worker.DisposeAsync();
            NodeDiagnostics.Worker(_logger, "worker_start", "draining");
            throw new WorkerPlacementException("Node is draining.");
        }
        NodeDiagnostics.Worker(_logger, "worker_start", "success");
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
                {
                    NodeDiagnostics.Worker(_logger, "placement", "report_unavailable");
                    throw new WorkerPlacementException("Official reporting is unavailable or at capacity.");
                }
                ManagedWorker? selected = null;
                foreach (var candidate in candidates)
                    if (candidate.Worker.TrySend(new CreateMatch(spec))) { selected = candidate.Worker; break; }
                if (selected == null)
                {
                    if (reserved) _reports!.CancelReservation(spec.MatchId);
                    NodeDiagnostics.Worker(_logger, "placement", "capacity");
                    throw new WorkerPlacementException("No compatible healthy worker has capacity.");
                }
                var placement = new Placement(spec, hash, selected);
                _placements.Add(spec.MatchId, placement);
                _ = ExpireCreationAsync(placement);
                NodeDiagnostics.Worker(_logger, "placement", "accepted");
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
    /// Atomically reserves terminal ownership for a transition. Natural
    /// completion and report admission both participate in this same gate, so
    /// callers can never publish a completion after this succeeds.
    /// </summary>
    public bool TryClaimTransition(MatchId matchId, out WorkerMatchAssignment? assignment)
    {
        lock (_gate)
        {
            assignment = null;
            if (!_placements.TryGetValue(matchId, out Placement? placement)
                || !IsActive(placement.Status) || placement.Claim != TerminalClaim.None
                || placement.ReportAdmissionClaimed)
                return false;
            placement.Claim = TerminalClaim.Transition;
            assignment = Assignment(placement);
        }
        return true;
    }

    /// <summary>Sends the one transition cancellation. A failed send leaves
    /// the shared Worker untouched and keeps the claim recoverable by the
    /// coordinator.</summary>
    public bool TryCancelTransition(MatchId matchId, string reason)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement)
                || placement.Claim != TerminalClaim.Transition)
                return false;
            // A terminal event may have won between the atomic claim and this
            // send attempt.  Treat that as an already-delivered cancellation
            // acknowledgement; the coordinator will consume it exactly once.
            if (!IsActive(placement.Status)) return true;
            // Retries are idempotent and never enqueue a second cancellation.
            if (placement.TransitionCancelSent) return true;
            bool sent = placement.Worker.TrySend(new CancelMatch(matchId, reason));
            // A failed enqueue does not transfer terminal ownership. Restore
            // the claim while still under _gate so a terminal Worker event
            // cannot be misclassified in the gap before the caller observes
            // the failure.
            if (!sent) placement.Claim = TerminalClaim.None;
            else placement.TransitionCancelSent = true;
            return sent;
        }
    }

    /// <summary>Releases a tentative transition claim after the cancellation
    /// command could not be sent. No placement or Worker capacity is freed.</summary>
    public bool RollbackTransitionClaim(MatchId matchId)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement)
                || placement.Claim != TerminalClaim.Transition || !IsActive(placement.Status))
                return false;
            placement.Claim = TerminalClaim.None;
            return true;
        }
    }

    /// <summary>Read-only seam used by coordinator tests to assert terminal
    /// ownership without exposing mutable placement state.</summary>
    public bool IsTransitionClaimed(MatchId matchId)
    {
        lock (_gate) return _placements.TryGetValue(matchId, out Placement? placement)
            && placement.Claim == TerminalClaim.Transition;
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

    /// <summary>
    /// Sends one admission-key install and awaits the Worker lane's explicit
    /// acknowledgement. Pending ownership is bounded by the scheduler and is
    /// failed on timeout, stale acknowledgement, match termination, or Worker loss.
    /// </summary>
    public async Task<AdmissionKeyInstalled> InstallAdmissionKeyAsync(InstallAdmissionKey command,
        CancellationToken cancellationToken = default)
    {
        WorkerIpcCodec.Encode(command);
        AdmissionInstall pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_admissionInstalls.Count >= 64) throw new WorkerPlacementException("Admission-key install queue is full.");
            if (_admissionInstalls.ContainsKey(command.AdmissionId))
                throw new WorkerPlacementException("Admission-key identity is already pending.");
            if (!_placements.TryGetValue(command.MatchId, out Placement? placement) || !IsActive(placement.Status)
                || placement.Worker.Id != command.WorkerId || placement.Worker.Incarnation != command.WorkerIncarnation
                || placement.Ready.Task.IsCompletedSuccessfully && placement.Ready.Task.Result.WireMatchId != command.WireMatchId)
                throw new WorkerPlacementException("Admission-key placement is stale.");
            pending = new(command, placement.Worker);
            _admissionInstalls.Add(command.AdmissionId, pending);
        }

        if (!pending.Worker.TrySend(command))
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker rejected admission-key installation.");
        }
        try
        {
            return await pending.Completion.Task.WaitAsync(_admissionInstallTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker admission-key installation timed out.");
        }
        catch
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw;
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
            if (placement.Claim == TerminalClaim.Transition) return;
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
            if (message is AdmissionKeyInstalled installed)
            {
                CompleteAdmissionInstall(worker, installed);
                continue;
            }
            if (message is AdmissionKeyInstallFailed failed)
            {
                FailAdmissionInstall(worker, failed.AdmissionId, failed.MatchId, new WorkerPlacementException(
                    "Worker rejected admission-key installation."));
                continue;
            }
            if (message is MatchReportReady report)
            {
                bool admitted = TryAdmitReport(worker, report, out WorkerMatchAssignment? assignment,
                    out bool transitionClaimed);
                if (!admitted) continue;
                if (transitionClaimed)
                {
                    // Transition-owned artifacts are never admitted to the
                    // official queue. Validate and clean them after leaving
                    // _gate; a malformed artifact remains retained safely.
                    if (assignment?.Placement is { } transitionPlacement
                        && assignment.ArtifactDirectory is { } transitionRoot)
                        _ = NodeReportIngestor.TryDiscardArtifact(assignment.Spec,
                            assignment.WorkerId, assignment.WorkerIncarnation,
                            transitionPlacement.WireMatchId.Value, transitionRoot, report);
                    continue;
                }
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
            bool transitionNotify = false;
            lock (_gate)
            {
                if (!_placements.TryGetValue(id, out var p) || p.Worker != worker || !IsActive(p.Status)) continue;
                if (p.Claim == TerminalClaim.Transition)
                {
                    // MatchReady and MatchStarted are progress messages, not
                    // cancellation acknowledgements.  A Worker can still
                    // flush either message after the Node sent CancelMatch;
                    // only a terminal event may release transition ownership.
                    if (message is not (MatchCompleted or MatchFailed or MatchInterrupted))
                        continue;
                    // Once transition ownership wins, every later terminal
                    // Worker message is an intentional transition ack. Never
                    // publish completion, report, or ordinary interruption.
                    p.Status = MatchStatus.Interrupted;
                    p.Deadline.Cancel();
                    p.Ready.TrySetException(new WorkerPlacementException("Match transitioned."));
                    transitionNotify = true;
                }
                else
                {
                switch (message)
                {
                    case MatchReady ready:
                        p.Status = MatchStatus.Ready; p.Deadline.Cancel(); p.Ready.TrySetResult(ready.Placement); break;
                    case MatchStarted: p.Status = MatchStatus.Running; break;
                    case MatchCompleted:
                        p.Status = MatchStatus.Completed;
                        p.Claim = TerminalClaim.Completion;
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
            }
            if (transitionNotify)
            {
                FailAdmissionInstallsForMatch(id, new WorkerPlacementException("Match transitioned before admission-key installation."));
                NotifyTransitionEnded(id, true);
                continue;
            }
            if (notify)
            {
                if (message is MatchCompleted completed) NotifyCompleted(completed.Summary);
                NotifyEnded(id, interrupted);
                FailAdmissionInstallsForMatch(id, new WorkerPlacementException("Match ended before admission-key installation."));
            }
        }
        await worker.Completion;
        FailAdmissionInstallsForWorker(worker, new WorkerPlacementException("Worker was lost during admission-key installation."));
        MatchId[] interruptedIds, transitionIds;
        lock (_gate)
        {
            // A lost Worker cannot deliver a remaining guest artifact notice.
            foreach (var placement in _placements.Values.Where(p => p.Worker == worker && ContainsGuest(p.Spec)))
                placement.ReportQueued = true;
            interruptedIds = _placements.Where(p => p.Value.Worker == worker && IsActive(p.Value.Status)).Select(p => p.Key).ToArray();
            transitionIds = _placements.Where(p => p.Value.Worker == worker && IsActive(p.Value.Status)
                && p.Value.Claim == TerminalClaim.Transition).Select(p => p.Key).ToArray();
            foreach (MatchId id in interruptedIds)
            {
                var p = _placements[id]; p.Status = MatchStatus.Interrupted; p.Deadline.Cancel();
                p.Ready.TrySetException(new WorkerPlacementException("Worker was lost during match creation."));
            }
        }
        foreach (MatchId id in interruptedIds)
            if (transitionIds.Contains(id)) NotifyTransitionEnded(id, true); else NotifyEnded(id, true);
    }

    /// <summary>Atomically admits the first report notice for a placement or
    /// identifies a transition-owned notice that must be discarded. The event
    /// consumer uses this same seam so report-vs-transition races are tested
    /// without requiring an invalid Worker protocol sequence.</summary>
    internal bool TryAdmitReport(ManagedWorker worker, MatchReportReady report,
        out WorkerMatchAssignment? assignment, out bool transitionClaimed)
    {
        lock (_gate)
        {
            assignment = null;
            transitionClaimed = false;
            if (!_placements.TryGetValue(report.MatchId, out Placement? placement)
                || placement.Worker != worker)
                return false;
            assignment = Assignment(placement);
            if (placement.Claim == TerminalClaim.Transition)
            {
                transitionClaimed = true;
                return true;
            }
            if ((placement.Claim == TerminalClaim.None || placement.Claim == TerminalClaim.Completion)
                && !placement.ReportAdmissionClaimed)
            {
                placement.ReportAdmissionClaimed = true;
                return true;
            }
            assignment = null; // duplicate/stale report admission
            return false;
        }
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

    private void NotifyTransitionEnded(MatchId id, bool interrupted)
    {
        // Reservation ownership stays with the old placement until the Worker
        // terminal acknowledgement.  It is safe to release only at this
        // transition-specific terminal boundary, outside _gate.
        _reports?.CancelReservation(id);
        if (TransitionEnded is not { } handlers) return;
        foreach (Action<MatchId, bool> handler in handlers.GetInvocationList())
            try { handler(id, interrupted); }
            catch (Exception error) { _logger.LogError(error, "Transition outcome subscriber failed for {MatchId}", id.Value); }
    }

    private static bool ContainsGuest(MatchSpec spec) => spec.Roster.Any(seat => seat.GuestSessionId.HasValue);
    private static bool RequiresBackendReport(MatchSpec spec)
        => !ContainsGuest(spec) && (spec.TrustClass is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked or MatchTrustClass.Tournament);

    private void CompleteAdmissionInstall(ManagedWorker worker, AdmissionKeyInstalled installed)
    {
        AdmissionInstall? pending = null;
        Exception? failure = null;
        lock (_gate)
        {
            if (!_admissionInstalls.TryGetValue(installed.AdmissionId, out pending) || pending.Worker != worker)
                return; // Timed-out/retired acknowledgements are stale by definition.
            if (!AdmissionMatches(pending.Command, installed))
                failure = new WorkerPlacementException("Worker returned a stale admission-key acknowledgement.");
            _admissionInstalls.Remove(installed.AdmissionId);
        }
        if (failure != null) pending.Completion.TrySetException(failure);
        else pending.Completion.TrySetResult(installed);
    }

    private void FailAdmissionInstall(ManagedWorker worker, Guid admissionId, MatchId matchId, Exception error)
    {
        AdmissionInstall? pending = null;
        lock (_gate)
        {
            if (_admissionInstalls.TryGetValue(admissionId, out var current)
                && current.Worker == worker && current.Command.MatchId == matchId)
            { pending = current; _admissionInstalls.Remove(admissionId); }
        }
        pending?.Completion.TrySetException(error);
    }

    private void FailAdmissionInstallsForMatch(MatchId matchId, Exception error)
    {
        AdmissionInstall[] pending;
        lock (_gate)
        {
            pending = _admissionInstalls.Values.Where(value => value.Command.MatchId == matchId).ToArray();
            foreach (AdmissionInstall value in pending) _admissionInstalls.Remove(value.Command.AdmissionId);
        }
        foreach (AdmissionInstall value in pending) value.Completion.TrySetException(error);
    }

    private void FailAdmissionInstallsForWorker(ManagedWorker worker, Exception error)
    {
        AdmissionInstall[] pending;
        lock (_gate)
        {
            pending = _admissionInstalls.Values.Where(value => value.Worker == worker).ToArray();
            foreach (AdmissionInstall value in pending) _admissionInstalls.Remove(value.Command.AdmissionId);
        }
        foreach (AdmissionInstall value in pending) value.Completion.TrySetException(error);
    }

    private void RemoveAdmissionInstall(Guid admissionId, AdmissionInstall pending)
    {
        lock (_gate)
            if (_admissionInstalls.TryGetValue(admissionId, out var current) && ReferenceEquals(current, pending))
                _admissionInstalls.Remove(admissionId);
    }

    private static bool AdmissionMatches(InstallAdmissionKey command, AdmissionKeyInstalled installed)
        => command.AdmissionId == installed.AdmissionId && command.TicketId == installed.TicketId
            && command.NodeSessionId == installed.NodeSessionId && command.NodeId == installed.NodeId
            && command.NodeIncarnation == installed.NodeIncarnation && command.MatchId == installed.MatchId
            && command.WireMatchId == installed.WireMatchId && command.WorkerId == installed.WorkerId
            && command.WorkerIncarnation == installed.WorkerIncarnation && command.SeatId == installed.SeatId
            && command.JoinNonce == installed.JoinNonce && command.ExpiresAt == installed.ExpiresAt;

    public async ValueTask DisposeAsync()
    {
        AdmissionInstall[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _draining = true;
            pending = _admissionInstalls.Values.ToArray();
            _admissionInstalls.Clear();
        }
        foreach (AdmissionInstall value in pending)
            value.Completion.TrySetException(new WorkerPlacementException("Worker scheduler was disposed."));
        await _manager.DisposeAsync();
        Task[] readers;
        lock (_gate) readers = _readers.Values.ToArray();
        await Task.WhenAll(readers);
        lock (_gate) foreach (var p in _placements.Values) { p.Deadline.Cancel(); p.Deadline.Dispose(); }
    }
}
