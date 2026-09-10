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
}
