using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Node.Reporting;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Node.Lobbies;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead.Identity;
using MphRead.Reporting;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Trait("LifecycleFast", "true")]
public sealed class WorkerSchedulerTests
{
    private static WorkerManager Manager() => new(new(Guid.NewGuid()), Guid.NewGuid());
    private static WorkerLaunchOptions Launch(string mode) => WorkerManagerTests.Launch(mode) with { Content = new("1", "hash", "test", 8) };

    [Fact]
    public async Task SigningKeyInitializationRequiresWorkerAcknowledgement()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager,
            admissionInstallTimeout: TimeSpan.FromMilliseconds(500));
        using var issuer = new WorkerAdmissionIssuer("test");
        ManagedWorker acknowledged = await scheduler.StartWorkerAsync(Launch("normal"),
            requireSigningInitialization: true);
        await scheduler.InitializeSigningKeyAsync(acknowledged,
            new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey()));

        ManagedWorker silent = await scheduler.StartWorkerAsync(Launch("signing-key-noack"),
            requireSigningInitialization: true);
        WorkerPlacementException error = await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            scheduler.InitializeSigningKeyAsync(silent,
                new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey())));
        Assert.Equal(MatchControlFailure.WorkerUnavailable, error.Failure);
    }

    [Fact]
    public async Task WorkerCannotReceivePlacementUntilSigningKeyIsAcknowledged()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("normal"),
            requireSigningInitialization: true);

        WorkerPlacementException unavailable = await Assert.ThrowsAsync<WorkerPlacementException>(
            () => scheduler.PlaceAsync(WorkerManagerTests.Spec(manager)));
        Assert.Equal(MatchControlFailure.WorkerBusy, unavailable.Failure);

        await scheduler.InitializeSigningKeyAsync(worker,
            new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey()));
        Assert.Equal(worker.Id,
            (await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager))).WorkerId);
    }

    [Fact]
    public async Task RollingRetirementStartsReadyReplacementBeforeOldWorkerStops()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid(), 2);
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        UpdateNodeSigningKey key = new(issuer.KeyId, issuer.ExportPublicKey());
        ManagedWorker old = await scheduler.StartWorkerAsync(Launch("normal"));
        await scheduler.InitializeSigningKeyAsync(old, key);
        Assert.True(scheduler.QuarantineWorker(old.Id, "rolling recycle"));

        ManagedWorker replacement = await scheduler.StartWorkerAsync(Launch("normal"));
        await scheduler.InitializeSigningKeyAsync(replacement, key);
        Assert.Equal(2, manager.Snapshot().Count);
        Assert.Equal(WorkerStatus.Ready, replacement.Snapshot().Status);

        Assert.True(await scheduler.RetireDrainedWorkerAsync(old.Id,
            "rolling recycle complete"));
        WorkerSnapshot remaining = Assert.Single(manager.Snapshot());
        Assert.Equal(replacement.Id, remaining.WorkerId);
        Assert.Equal(WorkerStatus.Ready, remaining.Status);
    }

    [Fact]
    public async Task FailedOldWorkerRetirementCleansUpUnadoptedReplacement()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid(), 2);
        await using var scheduler = new WorkerScheduler(manager,
            admissionInstallTimeout: TimeSpan.FromSeconds(3));
        using var issuer = new WorkerAdmissionIssuer("test");
        ManagedWorker old = await scheduler.StartWorkerAsync(
            Launch("ignore-shutdown") with { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        Assert.True(scheduler.QuarantineWorker(old.Id, "rolling recycle"));
        var service = new WorkerPoolHostedService(scheduler, issuer,
            new LobbyManager(), new WorkerPoolOptions(), TimeProvider.System,
            NullLogger<WorkerPoolHostedService>.Instance);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.ReplaceDrainedWorkerAsync(old, Launch("normal"),
                CancellationToken.None));

        WorkerSnapshot retained = Assert.Single(manager.Snapshot());
        Assert.Equal(old.Id, retained.WorkerId);
        Assert.DoesNotContain(manager.Snapshot(), worker =>
            worker.WorkerId != old.Id && worker.Status == WorkerStatus.Ready);
    }

    [Fact]
    public async Task CanceledSigningInitializationRemovesUnreadyWorkerSlot()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid(), 1);
        await using var scheduler = new WorkerScheduler(manager,
            admissionInstallTimeout: TimeSpan.FromSeconds(3));
        using var issuer = new WorkerAdmissionIssuer("test");
        var signingStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new WorkerPoolHostedService(scheduler, issuer,
            new LobbyManager(), new WorkerPoolOptions
            {
                BeforeSigningInitialization = _ => signingStarted.TrySetResult()
            }, TimeProvider.System,
            NullLogger<WorkerPoolHostedService>.Instance);
        using var canceled = new CancellationTokenSource();

        Task<ManagedWorker> starting = service.StartReadyWorkerAsync(
            Launch("signing-key-noack"), canceled.Token);
        await signingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        Assert.Empty(manager.Snapshot());
        Assert.Equal(0, scheduler.RetentionSnapshot.QuarantinedWorkers);
    }

    [Fact]
    public async Task CompatiblePlacementIsIdempotentAndDrainRefusesNewMatches()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("normal"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement first = await scheduler.PlaceAsync(spec);
        Assert.Equal(first, await scheduler.PlaceAsync(spec));
        Assert.Single(worker.Snapshot().Matches);
        await Assert.ThrowsAsync<WorkerPlacementException>(() => scheduler.PlaceAsync(spec with { Rng1Seed = 999 }));
        await Assert.ThrowsAsync<WorkerPlacementException>(() => scheduler.PlaceAsync(WorkerManagerTests.Spec(manager) with { Content = spec.Content with { ContentHash = "wrong" } }));
        scheduler.Drain("update");
        await Assert.ThrowsAsync<WorkerPlacementException>(() => scheduler.PlaceAsync(WorkerManagerTests.Spec(manager)));
    }

    [Fact]
    public async Task HealthyCompatibleLeastLoadedWorkerReceivesNextPlacement()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker a = await scheduler.StartWorkerAsync(Launch("normal"));
        ManagedWorker b = await scheduler.StartWorkerAsync(Launch("normal"));
        MatchPlacement first = await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager));
        MatchPlacement second = await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager));
        Assert.NotEqual(first.WorkerId, second.WorkerId);
        Assert.Contains(first.WorkerId, new[] { a.Id, b.Id });
    }

    [Fact]
    public async Task QuarantinedWorkerIsExcludedFromNewPlacement()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker first = await scheduler.StartWorkerAsync(Launch("normal"));
        ManagedWorker second = await scheduler.StartWorkerAsync(Launch("normal"));
        Assert.True(scheduler.QuarantineWorker(first.Id, "maintenance"));
        Assert.True(scheduler.IsWorkerQuarantined(first.Id));

        MatchPlacement placement = await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager));
        Assert.Equal(second.Id, placement.WorkerId);
        Assert.True(scheduler.ReleaseWorkerQuarantine(first.Id));
        Assert.False(scheduler.IsWorkerQuarantined(first.Id));
    }

    [Fact]
    public async Task ForceRetirementUsesWorkerLossToInterruptOwnedMatches()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("controlled-completion"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (id, interrupted) =>
        {
            if (id == spec.MatchId) ended.TrySetResult(interrupted);
        };
        await scheduler.PlaceAsync(spec);

        Assert.True(await scheduler.ForceRetireWorkerAsync(worker.Id));
        Assert.True(await ended.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.DoesNotContain(manager.Snapshot(), snapshot => snapshot.WorkerId == worker.Id);
    }

    [Theory]
    [InlineData("create-hang")]
    [InlineData("crash-match")]
    [InlineData("crash-running")]
    public async Task TimeoutOrCrashReturnsOwnedMatchToCoordinatorExactlyOnce(string mode)
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, TimeSpan.FromMilliseconds(250));
        await scheduler.StartWorkerAsync(Launch(mode));
        var ended = new TaskCompletionSource<(MatchId, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        scheduler.Ended += (id, interrupted) => { Interlocked.Increment(ref count); ended.TrySetResult((id, interrupted)); };
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        try { await scheduler.PlaceAsync(spec); } catch (WorkerPlacementException) { }
        var outcome = await ended.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(spec.MatchId, outcome.Item1); Assert.True(outcome.Item2);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task NormalCompletionReportsNonInterruptedOutcome()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("completed"));
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (_, interrupted) => ended.TrySetResult(interrupted);
        await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager));
        Assert.False(await ended.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task OfficialUnavailableReceiptReleasesReservationAndRetiresPlacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "scheduler-unavailable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var outbox = new MatchReportOutbox(new(
                Path.Combine(root, "outbox"), MaximumReports: 1), new NoopReportTransport());
            await using var ingestor = new NodeReportIngestor(outbox,
                Path.Combine(root, "receipts"));
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!ingestor.CanAcceptOfficial)
            {
                readyTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            var manager = Manager();
            await using var scheduler = new WorkerScheduler(manager, reports: ingestor);
            await scheduler.StartWorkerAsync(Launch("official-unavailable"));
            MatchSpec spec = WorkerManagerTests.Spec(manager) with
            {
                TrustClass = MatchTrustClass.Ranked
            };
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (id, interrupted) =>
            {
                if (id == spec.MatchId && !interrupted) ended.TrySetResult();
            };

            await scheduler.PlaceAsync(spec);
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            while (scheduler.RetentionSnapshot.TerminalPlacements != 0
                || outbox.Status.ReservedReports != 0)
            {
                readyTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Assert.False(scheduler.TryGetAssignment(spec.MatchId, out _));
            Assert.Equal(0, outbox.Status.ReservedReports);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "receipts", "unavailable"), "*.json"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TerminalPlacementRetiresAfterCoordinatorConsumptionWithoutForgetShim()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("completed"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (id, interrupted) =>
        {
            if (id == spec.MatchId && !interrupted) ended.TrySetResult();
        };

        await scheduler.PlaceAsync(spec);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (scheduler.RetentionSnapshot.TerminalPlacements != 0)
            await Task.Delay(5, timeout.Token);

        Assert.False(scheduler.TryGetAssignment(spec.MatchId, out _));
        Assert.Equal(0, scheduler.RetentionSnapshot.AwaitingCoordinatorConsumption);
    }

    [Fact]
    public async Task ProcessHarnessRetentionRemainsBoundedAcrossTenThousandMatches()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("completed") with
        {
            HeartbeatTimeout = TimeSpan.FromSeconds(30)
        });
        int maximumRetained = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        for (int index = 0; index < 10_000; index++)
        {
            await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager), timeout.Token);
            WorkerSchedulerRetentionSnapshot snapshot;
            do
            {
                snapshot = scheduler.RetentionSnapshot;
                maximumRetained = Math.Max(maximumRetained,
                    snapshot.ActivePlacements + snapshot.TerminalPlacements);
                if (snapshot.ActivePlacements != 0 || snapshot.TerminalPlacements != 0)
                    await Task.Yield();
            }
            while (snapshot.ActivePlacements != 0 || snapshot.TerminalPlacements != 0);
        }

        Assert.InRange(maximumRetained, 0, 1);
        Assert.Equal(new WorkerSchedulerRetentionSnapshot(0, 0, 0, 0, 0),
            scheduler.RetentionSnapshot);
    }

    [Fact]
    public async Task WorkerHistoryMetricSurvivesSchedulerPlacementRetirement()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker worker = await scheduler.StartWorkerAsync(
            Launch("completed-history") with
            {
                HeartbeatTimeout = TimeSpan.FromSeconds(5)
            });
        await scheduler.PlaceAsync(WorkerManagerTests.Spec(manager));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (scheduler.RetentionSnapshot.ActivePlacements != 0
            || scheduler.RetentionSnapshot.TerminalPlacements != 0
            || worker.Snapshot().IdentityHistoryUsed == 0)
            await Task.Delay(10, timeout.Token);

        NodeWorkerMetricSnapshot metrics = scheduler.ReadMetricSnapshot();
        Assert.Equal(0, metrics.SchedulerRetainedPlacements);
        Assert.Equal(1, metrics.WorkerRetainedMatches);
        Assert.True(metrics.HistoryUtilization > 0);
    }

    [Fact]
    public async Task PrematureReportAdmissionWinsBeforeTransitionClaim()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("controlled-completion"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);

        await scheduler.PlaceAsync(spec);
        using var runningTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (Assert.Single(manager.Snapshot()).Matches[spec.MatchId] != MatchStatus.Running)
            await Task.Delay(5, runningTimeout.Token);
        MatchReportReady reportNotice = new(spec.MatchId, Guid.NewGuid(), worker.Id,
            worker.Incarnation, new string('A', 64), 100);
        Assert.True(scheduler.TryAdmitReport(worker, reportNotice,
            out WorkerMatchAssignment? assignment, out bool transitionClaimed));

        Assert.Equal(spec.MatchId, reportNotice.MatchId);
        Assert.NotNull(assignment);
        Assert.False(transitionClaimed);
        Assert.Equal(MatchStatus.Running, Assert.Single(manager.Snapshot()).Matches[spec.MatchId]);
        Assert.False(scheduler.TryClaimTransition(spec.MatchId, out _));
    }

    [Fact]
    public async Task InterruptedTransitionRetiresWithoutWaitingForAReport()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("controlled-completion"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        int reportAdmissions = 0;
        var transitionEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.ReportReady += (_, _) => Interlocked.Increment(ref reportAdmissions);
        scheduler.TransitionEnded += (id, _) =>
        {
            if (id == spec.MatchId) transitionEnded.TrySetResult();
        };

        await scheduler.PlaceAsync(spec);
        Assert.True(scheduler.TryClaimTransition(spec.MatchId, out _));
        Assert.True(scheduler.TryCancelTransition(spec.MatchId, "restart"));
        await transitionEnded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        MatchReportReady report = new(spec.MatchId, Guid.NewGuid(), worker.Id,
            worker.Incarnation, new string('A', 64), 100);
        Assert.False(scheduler.TryAdmitReport(worker, report,
            out WorkerMatchAssignment? assignment, out bool transitionClaimed));

        Assert.Null(assignment);
        Assert.False(transitionClaimed);
        Assert.Equal(0, Volatile.Read(ref reportAdmissions));
        Assert.False(scheduler.TryGetAssignment(spec.MatchId, out _));
    }

    [Fact]
    public async Task TransitionClaimSuppressesOrdinaryTerminalEventsAndCancellationIsIdempotent()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("controlled-completion"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        await scheduler.PlaceAsync(spec);

        int ordinaryEnded = 0, completed = 0, transitionEnded = 0;
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (_, _) => Interlocked.Increment(ref ordinaryEnded);
        scheduler.Completed += _ => Interlocked.Increment(ref completed);
        scheduler.TransitionEnded += (_, _) =>
        {
            if (Interlocked.Increment(ref transitionEnded) == 1) acknowledged.TrySetResult();
        };

        Assert.True(scheduler.TryClaimTransition(spec.MatchId, out var assignment));
        Assert.NotNull(assignment);
        Assert.True(scheduler.IsTransitionClaimed(spec.MatchId));
        Assert.True(scheduler.TryCancelTransition(spec.MatchId, "restart"));
        // A duplicate coordinator retry must not enqueue another CancelMatch.
        Assert.True(scheduler.TryCancelTransition(spec.MatchId, "duplicate retry"));
        await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, Volatile.Read(ref ordinaryEnded));
        Assert.Equal(0, Volatile.Read(ref completed));
        Assert.Equal(1, Volatile.Read(ref transitionEnded));
    }

    [Fact]
    public async Task TransitionClaimIsPerMatchAndNaturalCompletionWinsBeforeClaim()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("normal"));
        MatchSpec first = WorkerManagerTests.Spec(manager);
        MatchSpec second = WorkerManagerTests.Spec(manager);
        await scheduler.PlaceAsync(first);
        await scheduler.PlaceAsync(second);
        Assert.True(scheduler.TryClaimTransition(first.MatchId, out _));
        Assert.False(scheduler.IsTransitionClaimed(second.MatchId));
        Assert.True(scheduler.RollbackTransitionClaim(first.MatchId));

        var completedManager = Manager();
        await using var completedScheduler = new WorkerScheduler(completedManager);
        await completedScheduler.StartWorkerAsync(Launch("completed"));
        MatchSpec completed = WorkerManagerTests.Spec(completedManager);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completedScheduler.Ended += (_, interrupted) =>
        {
            if (!interrupted) ended.TrySetResult();
        };
        await completedScheduler.PlaceAsync(completed);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(completedScheduler.TryClaimTransition(completed.MatchId, out _));
    }

    [Fact]
    public async Task TransitionTerminalOwnershipDoesNotAffectAnotherMatchOnTheSameWorker()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager);
        await scheduler.StartWorkerAsync(Launch("controlled-completion"));
        MatchSpec first = WorkerManagerTests.Spec(manager);
        MatchSpec second = WorkerManagerTests.Spec(manager);
        await scheduler.PlaceAsync(first);
        await scheduler.PlaceAsync(second);

        var firstAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource<MatchCompletionSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        int transitionCount = 0;
        scheduler.TransitionEnded += (id, _) =>
        {
            if (id == first.MatchId && Interlocked.Increment(ref transitionCount) == 1)
                firstAcknowledged.TrySetResult();
        };
        scheduler.Completed += summary =>
        {
            if (summary.MatchId == second.MatchId) secondCompleted.TrySetResult(summary);
        };

        Assert.True(scheduler.TryClaimTransition(first.MatchId, out _));
        Assert.True(scheduler.TryCancelTransition(first.MatchId, "restart"));
        await firstAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(scheduler.TryGetAssignment(second.MatchId, out WorkerMatchAssignment? secondAssignment));
        Assert.NotNull(secondAssignment);
        WorkerSnapshot running = Assert.Single(manager.Snapshot());
        Assert.DoesNotContain(first.MatchId, running.Matches.Keys);
        Assert.Equal(MatchStatus.Running, running.Matches[second.MatchId]);
        Assert.False(scheduler.IsTransitionClaimed(second.MatchId));

        Assert.True(scheduler.TrySendMatchAdmin(new(second.MatchId, AdminAction.EndMatch, null)));
        MatchCompletionSummary summary = await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(second.MatchId, summary.MatchId);
        Assert.Equal(1, Volatile.Read(ref transitionCount));
        Assert.False(scheduler.TryGetAssignment(second.MatchId, out _));
    }

    [Fact]
    public async Task AdmissionKeyInstallRequiresWorkerAcknowledgement()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromMilliseconds(500));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);
        InstallAdmissionKey command = Install(spec, placement, worker);

        AdmissionKeyInstalled installed = await scheduler.InstallAdmissionKeyAsync(command);

        Assert.Equal(command.AdmissionId, installed.AdmissionId);
        Assert.Equal(command.TicketId, installed.TicketId);
        Assert.Equal(command.WireMatchId, installed.WireMatchId);
        AssertAdmissionCounters(scheduler);
    }

    [Fact]
    public async Task AdmissionKeyInstallTimesOutAndStaleAcknowledgementCannotCompleteIt()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromMilliseconds(150));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key-noack"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);

        Task<AdmissionKeyInstalled> pending = scheduler.InstallAdmissionKeyAsync(
            Install(spec, placement, worker));
        await WaitUntilAsync(() => scheduler.AdmissionCounterSnapshots().Single(
            snapshot => snapshot.MatchId == spec.MatchId).PendingAdmissionInstalls == 1);
        AssertAdmissionCounters(scheduler);
        await Assert.ThrowsAsync<WorkerPlacementException>(() => pending);
        AssertAdmissionCounters(scheduler);
    }

    [Fact]
    public async Task AdmissionKeyInstallRejectsStaleAckAndWorkerLoss()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromMilliseconds(500));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key-stale"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);
        await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            scheduler.InstallAdmissionKeyAsync(Install(spec, placement, worker)));
        AssertAdmissionCounters(scheduler);

        var lossManager = Manager();
        await using var lossScheduler = new WorkerScheduler(lossManager, admissionInstallTimeout: TimeSpan.FromSeconds(2));
        ManagedWorker lost = await lossScheduler.StartWorkerAsync(Launch("admission-key-crash"));
        MatchSpec lossSpec = WorkerManagerTests.Spec(lossManager);
        MatchPlacement lossPlacement = await lossScheduler.PlaceAsync(lossSpec);
        await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            lossScheduler.InstallAdmissionKeyAsync(Install(lossSpec, lossPlacement, lost)));
        AssertAdmissionCounters(lossScheduler);
    }

    [Fact]
    public async Task AdmissionKeyInstallFailureWithWrongMatchIsIgnoredUntilTimeout()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromMilliseconds(150));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key-failure-stale"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);

        await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            scheduler.InstallAdmissionKeyAsync(Install(spec, placement, worker)));
        AssertAdmissionCounters(scheduler);
    }

    [Fact]
    public async Task AdmissionKeyInstallFailurePreservesBoundedWorkerReason()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager,
            admissionInstallTimeout: TimeSpan.FromMilliseconds(500));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key-failure"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);

        WorkerPlacementException error = await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            scheduler.InstallAdmissionKeyAsync(Install(spec, placement, worker)));

        Assert.Equal(MatchControlFailure.PlacementFailed, error.Failure);
        Assert.Contains("admission_route_capacity", error.Message, StringComparison.Ordinal);
        AssertAdmissionCounters(scheduler);
    }

    private static InstallAdmissionKey Install(MatchSpec spec, MatchPlacement placement, ManagedWorker worker)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), spec.NodeId, spec.NodeIncarnation,
            spec.MatchId, placement.WireMatchId, worker.Id, worker.Incarnation, spec.Roster[0].SeatId,
            123, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
            Convert.ToBase64String(new byte[AdmissionKeyRules.ByteLength]), HandoffGeneration.Initial);

    private static void AssertAdmissionCounters(WorkerScheduler scheduler)
    {
        foreach (WorkerAdmissionCounterSnapshot snapshot in scheduler.AdmissionCounterSnapshots())
        {
            Assert.Equal(snapshot.DictionaryAdmissionInstalls, snapshot.PendingAdmissionInstalls);
            Assert.Equal(snapshot.DictionaryAdmissionRetirements, snapshot.PendingAdmissionRetirements);
            Assert.True(snapshot.PendingAdmissionInstalls >= 0);
            Assert.True(snapshot.PendingAdmissionRetirements >= 0);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class NoopReportTransport : IMatchReportTransport
    {
        public Task<ReportDelivery> SubmitAsync(Guid matchId, string hash,
            ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => Task.FromResult(new ReportDelivery(ReportDeliveryKind.Accepted));
    }
}
