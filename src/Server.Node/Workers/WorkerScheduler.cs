using System.Security.Cryptography;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectPrime.Server.Node.Reporting;
using MphRead.Identity;

namespace ProjectPrime.Server.Node.Workers;

public sealed record WorkerMatchAssignment(MatchSpec Spec, WorkerId WorkerId, Guid WorkerIncarnation,
    MatchPlacement? Placement, string? ArtifactDirectory);

/// <summary>Read-only cancellation facts used by the Node transition watchdog.
/// The operation identity is generated once by the scheduler and remains
/// stable across the one permitted retry.</summary>
public sealed record WorkerCancellationSnapshot(string? OperationId, bool Sent,
    bool Acknowledged, bool TerminalObserved);

public sealed class WorkerPlacementException : MatchControlException
{
    public WorkerPlacementException(string message,
        MatchControlFailure failure = MatchControlFailure.PlacementFailed)
        : base(failure, message) { }
}

/// <summary>Low-cardinality scheduler retention facts for diagnostics/tests.</summary>
public sealed record WorkerSchedulerRetentionSnapshot(int ActivePlacements,
    int TerminalPlacements, int AwaitingCoordinatorConsumption,
    int AwaitingReportResolution, int PendingAdmissionInstalls,
    int PendingAdmissionRetirements = 0, int QuarantinedWorkers = 0);

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
        public string? CancelOperationId;
        public bool CancelAcknowledged;
        public bool CreationTimedOut;
        public bool TerminalNoticeSent;
        public bool TerminalObserved;
        public bool CoordinatorConsumed;
        public bool AdmissionsResolved = true;
        public bool ReportAdmissionClaimed;
        public Task? ReportDurability;
        public long? TerminalTimestamp;
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
    private sealed class AdmissionRetirement(RetireAdmission command, ManagedWorker worker)
    {
        public RetireAdmission Command { get; } = command;
        public ManagedWorker Worker { get; } = worker;
        public TaskCompletionSource<AdmissionRetired> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly WorkerManager _manager;
    private readonly Dictionary<WorkerId, ManagedWorker> _workers = new();
    private readonly Dictionary<WorkerId, Task> _readers = new();
    private readonly HashSet<WorkerId> _quarantinedWorkers = new();
    private readonly HashSet<WorkerId> _placementDisabledWorkers = new();
    private readonly Dictionary<MatchId, Placement> _placements = new();
    private readonly Dictionary<Guid, AdmissionInstall> _admissionInstalls = new();
    private readonly Dictionary<Guid, AdmissionRetirement> _admissionRetirements = new();
    private readonly TimeSpan _creationTimeout;
    private readonly TimeSpan _admissionInstallTimeout;
    private readonly TimeProvider _clock;
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
    public WorkerSchedulerRetentionSnapshot RetentionSnapshot
    {
        get
        {
            lock (_gate)
            {
                return new(_placements.Values.Count(p => IsActive(p.Status)),
                    _placements.Values.Count(p => !IsActive(p.Status)),
                    _placements.Values.Count(p => !IsActive(p.Status) && !p.CoordinatorConsumed),
                    _placements.Values.Count(p => !IsActive(p.Status) && p.ReportExpected && !p.ReportQueued),
                    _admissionInstalls.Count, _admissionRetirements.Count,
                    _quarantinedWorkers.Count);
            }
        }
    }

    public WorkerScheduler(WorkerManager manager, TimeSpan? creationTimeout = null,
        double maximumTickP99 = 16.6667, ILogger<WorkerScheduler>? logger = null, NodeReportIngestor? reports = null,
        TimeSpan? admissionInstallTimeout = null, TimeProvider? clock = null)
    {
        _manager = manager; _reports = reports; _creationTimeout = creationTimeout ?? TimeSpan.FromSeconds(30);
        _admissionInstallTimeout = admissionInstallTimeout ?? TimeSpan.FromSeconds(5);
        _clock = clock ?? TimeProvider.System;
        if (_creationTimeout <= TimeSpan.Zero || _admissionInstallTimeout <= TimeSpan.Zero
            || !double.IsFinite(maximumTickP99) || maximumTickP99 <= 0) throw new ArgumentOutOfRangeException();
        _maximumTickP99 = maximumTickP99; _logger = logger ?? NullLogger<WorkerScheduler>.Instance;
        NodeMetrics.RegisterWorkerMetrics(ReadMetricSnapshot);
    }

    internal NodeWorkerMetricSnapshot ReadMetricSnapshot()
    {
        WorkerSchedulerRetentionSnapshot retention = RetentionSnapshot;
        WorkerSnapshot[] workers = _manager.Snapshot().ToArray();
        double historyUtilization = workers
            .Where(worker => worker.IdentityHistoryCapacity > 0)
            .Select(worker => worker.IdentityHistoryUsed
                / (double)worker.IdentityHistoryCapacity)
            .DefaultIfEmpty(0)
            .Max();
        double oldestTerminalAge = 0;
        lock (_gate)
            foreach (Placement placement in _placements.Values)
                if (placement.TerminalTimestamp is { } terminal)
                    oldestTerminalAge = Math.Max(oldestTerminalAge,
                        _clock.GetElapsedTime(terminal).TotalSeconds);
        return new(workers.Sum(worker => worker.Matches.Values.Count(IsActive)),
            workers.Sum(worker => worker.IdentityHistoryUsed),
            retention.ActivePlacements + retention.TerminalPlacements,
            retention.TerminalPlacements, oldestTerminalAge, historyUtilization,
            workers.Sum(worker => worker.Health?.Diagnostics?.ActiveAdmissions ?? 0),
            retention.PendingAdmissionInstalls);
    }

    public Task<ManagedWorker> StartWorkerAsync(WorkerLaunchOptions options,
        CancellationToken cancellationToken = default)
        => StartWorkerCoreAsync(options, false, cancellationToken);

    public Task<ManagedWorker> StartWorkerAsync(WorkerLaunchOptions options,
        bool requireSigningInitialization, CancellationToken cancellationToken = default)
        => StartWorkerCoreAsync(options, requireSigningInitialization,
            cancellationToken);

    private async Task<ManagedWorker> StartWorkerCoreAsync(WorkerLaunchOptions options,
        bool requireSigningInitialization, CancellationToken cancellationToken)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); if (_draining) throw new WorkerPlacementException("Node is draining.", MatchControlFailure.ServerDraining); }
        ManagedWorker worker = await _manager.StartAsync(options, cancellationToken);
        bool reject;
        lock (_gate)
        {
            reject = _disposed || _draining;
            if (!reject)
            {
                _workers.Add(worker.Id, worker);
                _readers.Add(worker.Id, ConsumeAsync(worker));
                if (requireSigningInitialization)
                    _placementDisabledWorkers.Add(worker.Id);
            }
        }
        if (reject)
        {
            await worker.DisposeAsync();
            NodeDiagnostics.Worker(_logger, "worker_start", "draining");
            throw new WorkerPlacementException("Node is draining.", MatchControlFailure.ServerDraining);
        }
        NodeDiagnostics.Worker(_logger, "worker_start", "success");
        return worker;
    }

    public async Task InitializeSigningKeyAsync(ManagedWorker worker,
        UpdateNodeSigningKey command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(ManagedWorker source, WorkerEvent message)
        {
            if (ReferenceEquals(source, worker)
                && message is NodeSigningKeyUpdated updated
                && updated.WorkerId == worker.Id
                && updated.WorkerIncarnation == worker.Incarnation
                && StringComparer.Ordinal.Equals(updated.KeyId, command.KeyId))
                completion.TrySetResult();
        }

        Observed += Observe;
        try
        {
            if (!worker.TrySend(command))
                throw new WorkerPlacementException(
                    "Worker signing-key initialization was rejected.",
                    MatchControlFailure.WorkerUnavailable);
            await completion.Task.WaitAsync(_admissionInstallTimeout,
                cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_workers.TryGetValue(worker.Id, out ManagedWorker? current)
                    || !ReferenceEquals(current, worker)
                    || _quarantinedWorkers.Contains(worker.Id))
                    throw new WorkerPlacementException(
                        "Worker signing-key acknowledgement became stale.",
                        MatchControlFailure.WorkerUnavailable);
                _placementDisabledWorkers.Remove(worker.Id);
            }
        }
        catch (TimeoutException)
        {
            throw new WorkerPlacementException(
                "Worker signing-key initialization timed out.",
                MatchControlFailure.WorkerUnavailable);
        }
        finally { Observed -= Observe; }
    }

    public async Task<MatchPlacement> PlaceAsync(MatchSpec spec,
        CancellationToken cancellationToken = default)
    {
        using var activity = NodeMetrics.StartActivity("match.place");
        activity?.SetTag("match.id", spec.MatchId.Value);
        activity?.SetTag("lobby.id", spec.LobbyId.Value);
        activity?.SetTag("lifecycle.epoch", spec.LifecycleEpoch.Value);
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
                if (_placements.Count >= 4096) throw new WorkerPlacementException("Placement retention capacity reached.", MatchControlFailure.WorkerBusy);
                var candidates = _workers.Values.Select(w => (Worker: w, State: w.Snapshot()))
                    .Where(p => !_placementDisabledWorkers.Contains(p.Worker.Id)
                        && p.State.Status == WorkerStatus.Ready && Compatible(p.Worker.Content, spec.Content)
                        && (p.State.Health == null || p.State.Health.Status == WorkerStatus.Ready
                            && p.State.Health.TickP99Milliseconds <= _maximumTickP99
                            && (p.State.Health.Diagnostics == null || p.State.Health.Diagnostics.CpuPercent < 95)))
                    .OrderBy(p => p.State.Matches.Values.Count(IsActive)).ThenBy(p => p.Worker.Id.Value).ToArray();
                bool reserved = RequiresBackendReport(spec);
                if (reserved && (_reports == null || !_reports.TryReserve(spec.MatchId)))
                {
                    NodeDiagnostics.Worker(_logger, "placement", "report_unavailable");
                    throw new WorkerPlacementException("Official reporting is unavailable or at capacity.", MatchControlFailure.WorkerUnavailable);
                }
                ManagedWorker? selected = null;
                foreach (var candidate in candidates.Where(candidate => !_quarantinedWorkers.Contains(candidate.Worker.Id)))
                    if (candidate.Worker.TrySend(new CreateMatch(spec))) { selected = candidate.Worker; break; }
                if (selected == null)
                {
                    if (reserved) _reports!.CancelReservation(spec.MatchId);
                    NodeDiagnostics.Worker(_logger, "placement", "capacity");
                    throw new WorkerPlacementException("No compatible healthy worker has capacity.", MatchControlFailure.WorkerBusy);
                }
                var placement = new Placement(spec, hash, selected);
                _placements.Add(spec.MatchId, placement);
                _ = ExpireCreationAsync(placement);
                NodeDiagnostics.Worker(_logger, "placement", "accepted");
                result = placement.Ready.Task;
            }
        }
        // A disconnected caller does not cancel a reservation shared by retries.
        return await (cancellationToken.CanBeCanceled
            ? result.WaitAsync(cancellationToken) : result).ConfigureAwait(false);
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
            if (!_placements.TryGetValue(matchId, out var p) || IsActive(p.Status)) return false;
            p.CoordinatorConsumed = true;
            return TryRetireLocked(matchId, p);
        }
    }

    /// <summary>Marks the coordinator's lifecycle consumption edge after all
    /// subscribed consumers have completed successfully.  Report disposition
    /// is a separate NodeReportIngestor-owned edge and cannot be acknowledged
    /// by this method.</summary>
    public bool MarkCoordinatorConsumed(MatchId matchId)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement)) return false;
            placement.CoordinatorConsumed = true;
            return TryRetireLocked(matchId, placement);
        }
    }

    /// <summary>Compatibility spelling for consumers that complete a lifecycle notice.</summary>
    public bool CompleteLifecycle(MatchId matchId)
        => MarkCoordinatorConsumed(matchId);

    private bool TryRetireLocked(MatchId matchId, Placement placement)
    {
        if (IsActive(placement.Status) || !placement.TerminalObserved
            || !placement.CoordinatorConsumed || !placement.AdmissionsResolved
            || placement.ReportExpected && !placement.ReportQueued)
            return false;
        if (!placement.Worker.ForgetMatch(matchId)) return false;
        placement.Deadline.Dispose();
        return _placements.Remove(matchId);
    }

    public bool CancelMatch(MatchId matchId, string reason)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out var p) || !IsActive(p.Status)) return false;
            string operationId = p.CancelOperationId ??= Guid.NewGuid().ToString("N");
            if (p.CancelAcknowledged) return true;
            if (p.Worker.TrySend(new CancelMatch(matchId, operationId, reason))) return true;
            p.CancelOperationId = null;
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
        using var activity = NodeMetrics.StartActivity("worker.cancel");
        activity?.SetTag("match.id", matchId.Value);
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
            string operationId = placement.CancelOperationId ??= Guid.NewGuid().ToString("N");
            bool sent = placement.Worker.TrySend(new CancelMatch(matchId, operationId, reason));
            // Keep the claim and operation identity when the first enqueue
            // fails. The coordinator may retry the exact same operation once;
            // a failed send must never make a still-running match ordinary
            // again in the gap before that retry.
            if (sent) placement.TransitionCancelSent = true;
            return sent;
        }
    }

    /// <summary>Retries a transition cancellation with the exact operation
    /// identity created by <see cref="TryCancelTransition"/>. A retry never
    /// creates a second Worker stop operation.</summary>
    public bool TryRetryTransitionCancellation(MatchId matchId, string reason)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement)
                || placement.Claim != TerminalClaim.Transition
                || placement.CancelOperationId is not { Length: > 0 } operationId)
                return false;
            if (!IsActive(placement.Status) || placement.CancelAcknowledged
                || placement.TerminalObserved) return true;
            bool sent = placement.Worker.TrySend(new CancelMatch(matchId, operationId, reason));
            if (sent) placement.TransitionCancelSent = true;
            return sent;
        }
    }

    public bool TryGetCancellationSnapshot(MatchId matchId,
        out WorkerCancellationSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement))
            {
                snapshot = new(null, false, false, false);
                return false;
            }
            snapshot = new(placement.CancelOperationId, placement.TransitionCancelSent,
                placement.CancelAcknowledged, placement.TerminalObserved);
            return true;
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
            if (_admissionInstalls.Count >= MultiplayerLimits.MaxAdmissionLeasesPerMatch) throw new WorkerPlacementException("Admission-key install queue is full.", MatchControlFailure.AdmissionUnavailable);
            if (_admissionInstalls.ContainsKey(command.AdmissionId))
                throw new WorkerPlacementException("Admission-key identity is already pending.");
            if (!_placements.TryGetValue(command.MatchId, out Placement? placement) || !IsActive(placement.Status)
                || placement.Worker.Id != command.WorkerId || placement.Worker.Incarnation != command.WorkerIncarnation
                || placement.Ready.Task.IsCompletedSuccessfully && placement.Ready.Task.Result.WireMatchId != command.WireMatchId)
                throw new WorkerPlacementException("Admission-key placement is stale.", MatchControlFailure.MatchUnavailable);
            pending = new(command, placement.Worker);
            placement.AdmissionsResolved = false;
            _admissionInstalls.Add(command.AdmissionId, pending);
        }

        if (!pending.Worker.TrySend(command))
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker rejected admission-key installation.", MatchControlFailure.AdmissionRejected);
        }
        try
        {
            return await pending.Completion.Task.WaitAsync(_admissionInstallTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker admission-key installation timed out.", MatchControlFailure.AdmissionTimeout);
        }
        catch
        {
            RemoveAdmissionInstall(command.AdmissionId, pending);
            throw;
        }
    }

    public async Task<AdmissionRetired> RetireAdmissionAsync(RetireAdmission command,
        CancellationToken cancellationToken = default)
    {
        WorkerIpcCodec.Encode(command);
        AdmissionRetirement pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_admissionRetirements.Count >= MultiplayerLimits.MaxHandoffQueue)
                throw new WorkerPlacementException("Admission retirement queue is full.", MatchControlFailure.AdmissionUnavailable);
            if (_admissionRetirements.ContainsKey(command.AdmissionId))
                throw new WorkerPlacementException("Admission retirement is already pending.");
            if (!_placements.TryGetValue(command.MatchId, out Placement? placement)
                || placement.Worker.Id != command.WorkerId
                || placement.Worker.Incarnation != command.WorkerIncarnation)
                throw new WorkerPlacementException("Admission retirement placement is stale.", MatchControlFailure.MatchUnavailable);
            pending = new(command, placement.Worker);
            placement.AdmissionsResolved = false;
            _admissionRetirements.Add(command.AdmissionId, pending);
        }
        if (!pending.Worker.TrySend(command))
        {
            RemoveAdmissionRetirement(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker rejected admission retirement.", MatchControlFailure.AdmissionRejected);
        }
        try
        {
            return await pending.Completion.Task.WaitAsync(_admissionInstallTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            RemoveAdmissionRetirement(command.AdmissionId, pending);
            throw new WorkerPlacementException("Worker admission retirement timed out.", MatchControlFailure.AdmissionTimeout);
        }
        catch
        {
            RemoveAdmissionRetirement(command.AdmissionId, pending);
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

    /// <summary>
    /// Quarantines one Worker from new placement and asks it to drain. A
    /// quarantine is retained when the drain command cannot be enqueued, so a
    /// transient control-pipe failure cannot accidentally make the Worker
    /// eligible again.
    /// </summary>
    public bool QuarantineWorker(WorkerId workerId, string reason)
    {
        ManagedWorker? worker;
        lock (_gate)
        {
            if (!_workers.TryGetValue(workerId, out worker)) return false;
            _quarantinedWorkers.Add(workerId);
        }
        return worker.Drain(reason) || worker.Completion.IsCompleted;
    }

    public bool IsWorkerQuarantined(WorkerId workerId)
    {
        lock (_gate) return _quarantinedWorkers.Contains(workerId);
    }

    /// <summary>Re-enables placement only for an explicitly quarantined Worker.</summary>
    public bool ReleaseWorkerQuarantine(WorkerId workerId)
    {
        lock (_gate) return _quarantinedWorkers.Remove(workerId);
    }

    /// <summary>
    /// Force-retires a quarantined Worker. Cancellation is intentionally a
    /// child-lifetime operation, not ordinary match-cancel behavior: the
    /// scheduler's reader observes Worker loss and interrupts all owned
    /// matches before the manager slot is removed.
    /// </summary>
    public async Task<bool> ForceRetireWorkerAsync(WorkerId workerId,
        CancellationToken cancellationToken = default)
    {
        using var activity = NodeMetrics.StartActivity("worker.retire");
        activity?.SetTag("worker.id", workerId.Value);
        activity?.SetTag("retire.mode", "force");
        ManagedWorker worker;
        Task? reader;
        lock (_gate)
        {
            if (!_workers.TryGetValue(workerId, out worker!)) return false;
            _quarantinedWorkers.Add(workerId);
            _readers.TryGetValue(workerId, out reader);
        }
        await worker.ForceStopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (reader != null) await reader.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool retired = await _manager.RetireAsync(workerId).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (retired)
            lock (_gate)
            {
                _workers.Remove(workerId);
                _readers.Remove(workerId);
                _quarantinedWorkers.Remove(workerId);
                _placementDisabledWorkers.Remove(workerId);
            }
        return retired;
    }

    /// <summary>Gracefully retires one quarantined Worker after all of its
    /// authoritative matches have reached terminal state. This is the rolling
    /// pool path; it never interrupts an active placement.</summary>
    public async Task<bool> RetireDrainedWorkerAsync(WorkerId workerId,
        string reason, CancellationToken cancellationToken = default)
    {
        using var activity = NodeMetrics.StartActivity("worker.retire");
        activity?.SetTag("worker.id", workerId.Value);
        activity?.SetTag("retire.mode", "drained");
        ManagedWorker worker;
        Task? reader;
        lock (_gate)
        {
            if (!_workers.TryGetValue(workerId, out worker!)) return false;
            if (_placements.Values.Any(placement => placement.Worker.Id == workerId
                && IsActive(placement.Status))) return false;
            _quarantinedWorkers.Add(workerId);
            _readers.TryGetValue(workerId, out reader);
        }
        await worker.ShutdownAsync(reason, cancellationToken).ConfigureAwait(false);
        if (reader != null) await reader.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool retired = await _manager.RetireAsync(workerId).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (retired)
            lock (_gate)
            {
                _workers.Remove(workerId);
                _readers.Remove(workerId);
                _quarantinedWorkers.Remove(workerId);
                _placementDisabledWorkers.Remove(workerId);
            }
        return retired;
    }

    public async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_reportFailure != null) throw new IOException(_reportFailure);
                if (_placements.Values.All(p => !IsActive(p.Status) && p.CoordinatorConsumed
                    && p.AdmissionsResolved && (!p.ReportExpected || p.ReportQueued))) break;
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
            // Keep the placement active until the Worker emits a terminal
            // event (or the Worker lifetime is lost).  The creation waiter is
            // failed now, but capacity and terminal ownership must not be
            // released merely because the Node-side deadline elapsed.
            placement.CreationTimedOut = true;
            placement.Ready.TrySetException(new WorkerPlacementException("Worker match creation timed out."));
            placement.CancelOperationId ??= Guid.NewGuid().ToString("N");
            if (!placement.Worker.TrySend(new CancelMatch(placement.Spec.MatchId,
                placement.CancelOperationId, "Match creation timed out.")))
                placement.CancelOperationId = null;
            placement.TerminalNoticeSent = true;
            notify = true;
        }
        if (notify)
            AcknowledgeCoordinatorConsumption(placement.Spec.MatchId,
                NotifyEnded(placement.Spec.MatchId, true));
    }

    private async Task ConsumeAsync(ManagedWorker worker)
    {
        await foreach (WorkerEvent message in worker.Events.ReadAllAsync())
        {
            if (Observed is { } observers)
                foreach (Action<ManagedWorker, WorkerEvent> observer in observers.GetInvocationList())
                    try { observer(worker, message); }
                    catch (Exception error) { _logger.LogError(error, "Worker event observer failed for {WorkerId}", worker.Id.Value); }
            if (message is NodeSigningKeyUpdated)
                continue;
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
            if (message is AdmissionRetired retired)
            {
                CompleteAdmissionRetirement(worker, retired);
                continue;
            }
            if (message is AdmissionRetireFailed retireFailed)
            {
                FailAdmissionRetirement(worker, retireFailed, new WorkerPlacementException(
                    "Worker rejected admission retirement."));
                continue;
            }
            if (message is MatchCancelAccepted cancelAccepted)
            {
                lock (_gate)
                {
                    if (_placements.TryGetValue(cancelAccepted.MatchId, out Placement? placement)
                        && placement.Worker == worker
                        && placement.CancelOperationId == cancelAccepted.OperationId)
                        placement.CancelAcknowledged = true;
                }
                continue;
            }
            if (message is MatchCancelRejected cancelRejected)
            {
                lock (_gate)
                {
                    if (_placements.TryGetValue(cancelRejected.MatchId, out Placement? placement)
                        && placement.Worker == worker
                        && placement.CancelOperationId == cancelRejected.OperationId)
                    {
                        placement.CancelOperationId = null;
                        placement.CancelAcknowledged = false;
                        if (placement.Claim == TerminalClaim.Transition)
                            placement.TransitionCancelSent = false;
                    }
                }
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
                    bool resolved = assignment?.Placement is { } transitionPlacement
                        && assignment.ArtifactDirectory is { } transitionRoot
                        && NodeReportIngestor.TryDiscardArtifact(assignment.Spec,
                            assignment.WorkerId, assignment.WorkerIncarnation,
                            transitionPlacement.WireMatchId.Value, transitionRoot, report);
                    if (resolved) ResolveReport(report.MatchId);
                    else FailReportDisposition(report.MatchId,
                        "Transition report artifact was invalid or unavailable.");
                    continue;
                }
                if (assignment is null) continue;
                if (RequiresBackendReport(assignment.Spec))
                {
                    if (assignment.Placement is not { } placement || assignment.ArtifactDirectory is not { } root
                        || _reports is null
                        || !_reports.TryQueue(assignment.Spec, assignment.WorkerId,
                            assignment.WorkerIncarnation, placement.WireMatchId.Value, root,
                            report, out Task durability))
                    {
                        FailReportDisposition(report.MatchId,
                            "Official report ingestion admission failed; artifact retained.");
                    }
                    else
                    {
                        SetReportDurability(report.MatchId, durability);
                        _ = ObserveReportDurabilityAsync(report.MatchId, durability);
                    }
                }
                else
                {
                    // Community and guest artifacts are local-only.  They are
                    // resolved only after the exact immutable artifact has
                    // been validated and removed; no ingestor is required.
                    bool resolved = assignment.Placement is { } localPlacement
                        && assignment.ArtifactDirectory is { } localRoot
                        && NodeReportIngestor.TryDiscardArtifact(assignment.Spec,
                            assignment.WorkerId, assignment.WorkerIncarnation,
                            localPlacement.WireMatchId.Value, localRoot, report);
                    if (resolved) ResolveReport(report.MatchId);
                    else FailReportDisposition(report.MatchId,
                        "Local report artifact was invalid or unavailable.");
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
            bool creationTimeoutTerminal = false;
            lock (_gate)
            {
                if (!_placements.TryGetValue(id, out var p) || p.Worker != worker
                    || !IsActive(p.Status) && !(p.CreationTimedOut && message is MatchCompleted or MatchFailed or MatchInterrupted)) continue;
                if (message is MatchCompleted or MatchFailed or MatchInterrupted)
                    p.TerminalTimestamp ??= _clock.GetTimestamp();
                // Creation timeout fails the waiter but deliberately keeps the
                // placement active until terminal evidence.  Progress emitted
                // after that deadline is stale and must not reopen readiness
                // or mutate the retained placement.
                if (p.CreationTimedOut && message is not (MatchCompleted or MatchFailed or MatchInterrupted))
                    continue;
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
                    p.TerminalObserved = true;
                    p.AdmissionsResolved = false;
                    // A Worker may emit the report notice after the
                    // transition terminal. Retain this bounded placement
                    // until that notice is discarded/queued.
                    // Only a normal Worker completion can have a report
                    // notice. Failed/interrupted transition terminals have no
                    // artifact edge to wait for, while a completion terminal
                    // retains the placement until its durable notice is
                    // admitted or explicitly discarded.
                    p.ReportExpected = message is MatchCompleted && ReportNoticeExpected(p);
                    p.ReportQueued = !p.ReportExpected;
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
                        p.TerminalObserved = true;
                        p.AdmissionsResolved = false;
                        p.Claim = TerminalClaim.Completion;
                        p.ReportExpected = ReportNoticeExpected(p);
                        interrupted = false; notify = !p.TerminalNoticeSent; creationTimeoutTerminal = p.CreationTimedOut; p.TerminalNoticeSent = true; break;
                    case MatchFailed: p.Status = MatchStatus.Failed; p.TerminalObserved = true; p.AdmissionsResolved = false; notify = !p.TerminalNoticeSent; creationTimeoutTerminal = p.CreationTimedOut; p.TerminalNoticeSent = true; break;
                    case MatchInterrupted: p.Status = MatchStatus.Interrupted; p.TerminalObserved = true; p.AdmissionsResolved = false; notify = !p.TerminalNoticeSent; creationTimeoutTerminal = p.CreationTimedOut; p.TerminalNoticeSent = true; break;
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
                FailAdmissionRetirementsForMatch(id, new WorkerPlacementException("Match transitioned before admission retirement completed."));
                AcknowledgeCoordinatorConsumption(id, NotifyTransitionEnded(id, true));
                continue;
            }
            if (notify)
            {
                bool consumed = message is MatchCompleted completed && NotifyCompleted(completed.Summary);
                if (message is not MatchCompleted) consumed = true;
                consumed &= NotifyEnded(id, interrupted);
                AcknowledgeCoordinatorConsumption(id, consumed);
                FailAdmissionInstallsForMatch(id, new WorkerPlacementException("Match ended before admission-key installation."));
                FailAdmissionRetirementsForMatch(id, new WorkerPlacementException("Match ended before admission retirement completed."));
                lock (_gate)
                    if (_placements.TryGetValue(id, out Placement? terminalPlacement))
                        TryRetireLocked(id, terminalPlacement);
            }
            else if (creationTimeoutTerminal)
            {
                // The timeout already notified the coordinator; this late
                // Worker terminal now supplies the missing capacity-release
                // proof without publishing a duplicate lifecycle event.
                FailAdmissionInstallsForMatch(id, new WorkerPlacementException("Match ended before admission-key installation."));
                FailAdmissionRetirementsForMatch(id, new WorkerPlacementException("Match ended before admission retirement completed."));
                lock (_gate)
                    if (_placements.TryGetValue(id, out Placement? terminalPlacement))
                        TryRetireLocked(id, terminalPlacement);
            }
        }
        await worker.Completion;
        FailAdmissionInstallsForWorker(worker, new WorkerPlacementException("Worker was lost during admission-key installation."));
        FailAdmissionRetirementsForWorker(worker, new WorkerPlacementException("Worker was lost during admission retirement."));
        MatchId[] interruptedIds, transitionIds;
        lock (_gate)
        {
            // A lost Worker cannot deliver a remaining artifact notice. Keep
            // the report edge unresolved and fail closed rather than silently
            // retiring a report-producing placement.
            if (_placements.Values.Any(p => p.Worker == worker && p.ReportExpected
                && !p.ReportQueued && p.ReportDurability == null))
            {
                _reportFailure = "Worker loss left a report artifact unresolved.";
                _draining = true;
                NodeDiagnostics.Worker(_logger, "report_disposition", "worker_lost");
            }
            interruptedIds = _placements.Where(p => p.Value.Worker == worker && IsActive(p.Value.Status)).Select(p => p.Key).ToArray();
            transitionIds = _placements.Where(p => p.Value.Worker == worker && IsActive(p.Value.Status)
                && p.Value.Claim == TerminalClaim.Transition).Select(p => p.Key).ToArray();
            foreach (MatchId id in interruptedIds)
            {
                var p = _placements[id]; p.Status = MatchStatus.Interrupted; p.TerminalObserved = true;
                p.TerminalTimestamp ??= _clock.GetTimestamp();
                p.AdmissionsResolved = false; p.Deadline.Cancel();
                p.Ready.TrySetException(new WorkerPlacementException("Worker was lost during match creation."));
                // Worker-loss handling above settles any pending install or
                // retirement operations. Recompute after the terminal edge so
                // a force-retired worker cannot strand an otherwise complete
                // placement behind a false unresolved admission flag.
                RefreshAdmissionStateLocked(id);
            }
        }
        foreach (MatchId id in interruptedIds)
        {
            bool consumed = transitionIds.Contains(id)
                ? NotifyTransitionEnded(id, true)
                : NotifyEnded(id, true);
            AcknowledgeCoordinatorConsumption(id, consumed);
            lock (_gate)
                if (_placements.TryGetValue(id, out Placement? placement))
                    TryRetireLocked(id, placement);
        }
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

    private bool NotifyEnded(MatchId id, bool interrupted)
    {
        if (interrupted) _reports?.CancelReservation(id);
        bool succeeded = true;
        if (Ended is { } handlers)
            foreach (Action<MatchId, bool> handler in handlers.GetInvocationList())
                try { handler(id, interrupted); }
                catch (Exception error)
                {
                    succeeded = false;
                    NodeDiagnostics.Worker(_logger, "coordinator_consumption", "failed");
                    _logger.LogError(error, "Match outcome subscriber failed.");
                }
        return succeeded;
    }

    private bool NotifyCompleted(MatchCompletionSummary summary)
    {
        if (Completed is not { } handlers) return true;
        bool succeeded = true;
        foreach (Action<MatchCompletionSummary> handler in handlers.GetInvocationList())
            try { handler(summary); }
            catch (Exception error)
            {
                succeeded = false;
                NodeDiagnostics.Worker(_logger, "coordinator_consumption", "failed");
                _logger.LogError(error, "Match completion subscriber failed.");
            }
        return succeeded;
    }

    private bool NotifyTransitionEnded(MatchId id, bool interrupted)
    {
        // Reservation ownership stays with the old placement until the Worker
        // terminal acknowledgement.  It is safe to release only at this
        // transition-specific terminal boundary, outside _gate.
        _reports?.CancelReservation(id);
        bool succeeded = true;
        if (TransitionEnded is { } handlers)
            foreach (Action<MatchId, bool> handler in handlers.GetInvocationList())
                try { handler(id, interrupted); }
                catch (Exception error)
                {
                    succeeded = false;
                    NodeDiagnostics.Worker(_logger, "coordinator_consumption", "failed");
                    _logger.LogError(error, "Transition outcome subscriber failed.");
                }
        return succeeded;
    }

    private static bool ContainsGuest(MatchSpec spec) => spec.Roster.Any(seat => seat.GuestSessionId.HasValue);
    private bool ReportNoticeExpected(Placement placement)
        => placement.Worker.ArtifactDirectory != null || RequiresBackendReport(placement.Spec);
    private static bool RequiresBackendReport(MatchSpec spec)
        => !ContainsGuest(spec) && (spec.TrustClass is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked or MatchTrustClass.Tournament);

    private void AcknowledgeCoordinatorConsumption(MatchId matchId, bool succeeded)
    {
        if (succeeded)
        {
            MarkCoordinatorConsumed(matchId);
            return;
        }
        // Retention remains visible through AwaitingCoordinatorConsumption;
        // an operator or a later idempotent lifecycle retry can acknowledge it
        // after the failed consumer has recovered.
        NodeDiagnostics.Worker(_logger, "coordinator_consumption", "retained");
    }

    private void ResolveReport(MatchId matchId)
    {
        lock (_gate)
        {
            if (!_placements.TryGetValue(matchId, out Placement? placement)) return;
            placement.ReportDurability = null;
            placement.ReportQueued = true;
            TryRetireLocked(matchId, placement);
        }
    }

    private void SetReportDurability(MatchId matchId, Task durability)
    {
        lock (_gate)
            if (_placements.TryGetValue(matchId, out Placement? placement))
                placement.ReportDurability = durability;
    }

    private async Task ObserveReportDurabilityAsync(MatchId matchId, Task durability)
    {
        try
        {
            await durability.ConfigureAwait(false);
            ResolveReport(matchId);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            FailReportDisposition(matchId, "Report durability failed; artifact retained.", error);
        }
    }

    private void FailReportDisposition(MatchId matchId, string message, Exception? error = null)
    {
        lock (_gate)
        {
            _reportFailure ??= message;
            _draining = true;
        }
        NodeDiagnostics.Worker(_logger, "report_disposition", "failed");
        if (error == null) _logger.LogError("{ReportFailure}", message);
        else _logger.LogError(error, "{ReportFailure}", message);
    }

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
            RefreshAdmissionStateLocked(pending.Command.MatchId);
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
            { pending = current; _admissionInstalls.Remove(admissionId); RefreshAdmissionStateLocked(matchId); }
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
            RefreshAdmissionStateLocked(matchId);
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
            foreach (MatchId matchId in pending.Select(value => value.Command.MatchId).Distinct())
                RefreshAdmissionStateLocked(matchId);
        }
        foreach (AdmissionInstall value in pending) value.Completion.TrySetException(error);
    }

    private void RemoveAdmissionInstall(Guid admissionId, AdmissionInstall pending)
    {
        lock (_gate)
            if (_admissionInstalls.TryGetValue(admissionId, out var current) && ReferenceEquals(current, pending))
            {
                _admissionInstalls.Remove(admissionId);
                RefreshAdmissionStateLocked(pending.Command.MatchId);
            }
    }

    private void RefreshAdmissionStateLocked(MatchId matchId)
    {
        if (!_placements.TryGetValue(matchId, out Placement? placement)) return;
        placement.AdmissionsResolved = !_admissionInstalls.Values.Any(value => value.Command.MatchId == matchId)
            && !_admissionRetirements.Values.Any(value => value.Command.MatchId == matchId);
        if (placement.AdmissionsResolved) TryRetireLocked(matchId, placement);
    }

    private void CompleteAdmissionRetirement(ManagedWorker worker, AdmissionRetired retired)
    {
        AdmissionRetirement? pending = null;
        Exception? failure = null;
        lock (_gate)
        {
            if (!_admissionRetirements.TryGetValue(retired.AdmissionId, out pending)
                || pending.Worker != worker) return;
            if (pending.Command.MatchId != retired.MatchId
                || pending.Command.SeatId != retired.SeatId
                || pending.Command.HandoffGeneration != retired.HandoffGeneration
                || pending.Command.WorkerId != retired.WorkerId
                || pending.Command.WorkerIncarnation != retired.WorkerIncarnation)
                failure = new WorkerPlacementException("Worker returned a stale admission-retirement acknowledgement.");
            _admissionRetirements.Remove(retired.AdmissionId);
            RefreshAdmissionStateLocked(pending.Command.MatchId);
        }
        if (failure != null) pending.Completion.TrySetException(failure);
        else pending.Completion.TrySetResult(retired);
    }

    private void FailAdmissionRetirement(ManagedWorker worker, AdmissionRetireFailed failed, Exception error)
    {
        AdmissionRetirement? pending = null;
        lock (_gate)
        {
            if (_admissionRetirements.TryGetValue(failed.AdmissionId, out AdmissionRetirement? current)
                && current.Worker == worker && current.Command.MatchId == failed.MatchId)
            {
                pending = current;
                _admissionRetirements.Remove(failed.AdmissionId);
                RefreshAdmissionStateLocked(failed.MatchId);
            }
        }
        pending?.Completion.TrySetException(error);
    }

    private void RemoveAdmissionRetirement(Guid admissionId, AdmissionRetirement pending)
    {
        lock (_gate)
        {
            if (_admissionRetirements.TryGetValue(admissionId, out AdmissionRetirement? current)
                && ReferenceEquals(current, pending))
                _admissionRetirements.Remove(admissionId);
            RefreshAdmissionStateLocked(pending.Command.MatchId);
        }
    }

    private void FailAdmissionRetirementsForMatch(MatchId matchId, Exception error)
    {
        AdmissionRetirement[] pending;
        lock (_gate)
        {
            pending = _admissionRetirements.Values.Where(value => value.Command.MatchId == matchId).ToArray();
            foreach (AdmissionRetirement value in pending) _admissionRetirements.Remove(value.Command.AdmissionId);
            RefreshAdmissionStateLocked(matchId);
        }
        foreach (AdmissionRetirement value in pending) value.Completion.TrySetException(error);
    }

    private void FailAdmissionRetirementsForWorker(ManagedWorker worker, Exception error)
    {
        AdmissionRetirement[] pending;
        lock (_gate)
        {
            pending = _admissionRetirements.Values.Where(value => value.Worker == worker).ToArray();
            foreach (AdmissionRetirement value in pending) _admissionRetirements.Remove(value.Command.AdmissionId);
            foreach (MatchId matchId in pending.Select(value => value.Command.MatchId).Distinct())
                RefreshAdmissionStateLocked(matchId);
        }
        foreach (AdmissionRetirement value in pending) value.Completion.TrySetException(error);
    }

    private static bool AdmissionMatches(InstallAdmissionKey command, AdmissionKeyInstalled installed)
        => command.AdmissionId == installed.AdmissionId && command.TicketId == installed.TicketId
            && command.NodeSessionId == installed.NodeSessionId && command.NodeId == installed.NodeId
            && command.NodeIncarnation == installed.NodeIncarnation && command.MatchId == installed.MatchId
            && command.WireMatchId == installed.WireMatchId && command.WorkerId == installed.WorkerId
            && command.WorkerIncarnation == installed.WorkerIncarnation && command.SeatId == installed.SeatId
            && command.JoinNonce == installed.JoinNonce && command.ExpiresAt == installed.ExpiresAt
            && command.HandoffGeneration == installed.HandoffGeneration;

    public async ValueTask DisposeAsync()
    {
        AdmissionInstall[] pending;
        AdmissionRetirement[] retirements;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _draining = true;
            pending = _admissionInstalls.Values.ToArray();
            _admissionInstalls.Clear();
            retirements = _admissionRetirements.Values.ToArray();
            _admissionRetirements.Clear();
        }
        foreach (AdmissionInstall value in pending)
            value.Completion.TrySetException(new WorkerPlacementException("Worker scheduler was disposed."));
        foreach (AdmissionRetirement value in retirements)
            value.Completion.TrySetException(new WorkerPlacementException("Worker scheduler was disposed."));
        await _manager.DisposeAsync();
        Task[] readers;
        lock (_gate) readers = _readers.Values.ToArray();
        await Task.WhenAll(readers);
        lock (_gate) foreach (var p in _placements.Values) { p.Deadline.Cancel(); p.Deadline.Dispose(); }
    }
}
