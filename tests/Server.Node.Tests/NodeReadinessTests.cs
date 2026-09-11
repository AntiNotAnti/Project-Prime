using ProjectPrime.Server.Node.Maps;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeReadinessTests
{
    [Fact]
    public void ReadyWorkerAtCapacityStillReportsInfrastructureReady()
    {
        var worker = new WorkerSnapshot(new(Guid.NewGuid()), Guid.NewGuid(), WorkerStatus.Ready,
            new WorkerCapacity(1, 2, 1, 2),
            new WorkerHealth(WorkerStatus.Ready, 100, 2, 1024),
            new Dictionary<MatchId, MatchStatus>(), null, null);
        NodeReadinessResult result = NodeReadinessEvaluator.Evaluate(
            [new NodeMapReadiness("arena", NodeMapPackageState.Ready, "NODE-MAP-READY")], [worker]);
        Assert.True(result.IsReady);
        Assert.True(result.MapsValid);
        Assert.True(result.WorkerSubsystemUsable);
    }

    [Fact]
    public void InvalidMapsOrUnavailableWorkersAreNotReady()
    {
        var worker = new WorkerSnapshot(new(Guid.NewGuid()), Guid.NewGuid(), WorkerStatus.Faulted,
            new WorkerCapacity(1, 2, 0, 0), null,
            new Dictionary<MatchId, MatchStatus>(), "fault", null);
        NodeReadinessResult mapFailure = NodeReadinessEvaluator.Evaluate(
            [new NodeMapReadiness("arena", NodeMapPackageState.Invalid, "NODE-MAP-PKG-INVALID")], [worker]);
        Assert.False(mapFailure.IsReady);
        Assert.False(mapFailure.MapsValid);
        Assert.False(mapFailure.WorkerSubsystemUsable);

        NodeReadinessResult workerFailure = NodeReadinessEvaluator.Evaluate(
            Array.Empty<NodeMapReadiness>(), Array.Empty<WorkerSnapshot>());
        Assert.False(workerFailure.IsReady);
        Assert.True(workerFailure.MapsValid);
        Assert.False(workerFailure.WorkerSubsystemUsable);
    }
}
