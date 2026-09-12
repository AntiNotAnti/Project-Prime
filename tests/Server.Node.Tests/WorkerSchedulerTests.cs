using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class WorkerSchedulerTests
{
    private static WorkerManager Manager() => new(new(Guid.NewGuid()), Guid.NewGuid());
    private static WorkerLaunchOptions Launch(string mode) => WorkerManagerTests.Launch(mode) with { Content = new("1", "hash", "test", 8) };

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
    public async Task TransitionClaimSuppressesLateReportAfterWorkerAcknowledgement()
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
        Assert.True(scheduler.TryAdmitReport(worker, report,
            out WorkerMatchAssignment? assignment, out bool transitionClaimed));

        Assert.NotNull(assignment);
        Assert.True(transitionClaimed);
        Assert.Equal(0, Volatile.Read(ref reportAdmissions));
        Assert.Equal(MatchStatus.Interrupted, Assert.Single(manager.Snapshot()).Matches[spec.MatchId]);
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
        Assert.Equal(MatchStatus.Interrupted, running.Matches[first.MatchId]);
        Assert.Equal(MatchStatus.Running, running.Matches[second.MatchId]);
        Assert.False(scheduler.IsTransitionClaimed(second.MatchId));

        Assert.True(scheduler.TrySendMatchAdmin(new(second.MatchId, AdminAction.EndMatch, null)));
        MatchCompletionSummary summary = await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(second.MatchId, summary.MatchId);
        Assert.Equal(1, Volatile.Read(ref transitionCount));
        Assert.Equal(MatchStatus.Completed, Assert.Single(manager.Snapshot()).Matches[second.MatchId]);
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
    }

    [Fact]
    public async Task AdmissionKeyInstallTimesOutAndStaleAcknowledgementCannotCompleteIt()
    {
        var manager = Manager();
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromMilliseconds(150));
        ManagedWorker worker = await scheduler.StartWorkerAsync(Launch("admission-key-noack"));
        MatchSpec spec = WorkerManagerTests.Spec(manager);
        MatchPlacement placement = await scheduler.PlaceAsync(spec);

        await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            scheduler.InstallAdmissionKeyAsync(Install(spec, placement, worker)));
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

        var lossManager = Manager();
        await using var lossScheduler = new WorkerScheduler(lossManager, admissionInstallTimeout: TimeSpan.FromSeconds(2));
        ManagedWorker lost = await lossScheduler.StartWorkerAsync(Launch("admission-key-crash"));
        MatchSpec lossSpec = WorkerManagerTests.Spec(lossManager);
        MatchPlacement lossPlacement = await lossScheduler.PlaceAsync(lossSpec);
        await Assert.ThrowsAsync<WorkerPlacementException>(() =>
            lossScheduler.InstallAdmissionKeyAsync(Install(lossSpec, lossPlacement, lost)));
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
    }

    private static InstallAdmissionKey Install(MatchSpec spec, MatchPlacement placement, ManagedWorker worker)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), spec.NodeId, spec.NodeIncarnation,
            spec.MatchId, placement.WireMatchId, worker.Id, worker.Incarnation, spec.Roster[0].SeatId,
            123, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
            Convert.ToBase64String(new byte[AdmissionKeyRules.ByteLength]));
}
