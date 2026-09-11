using ProjectPrime.Server.Node.Maps;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node;

/// <summary>Projects existing map/package and Worker owners into one readiness result.</summary>
public sealed class NodeReadinessEvaluator(NodeMapPackageStore packages, WorkerManager workers)
{
    public NodeReadinessResult Evaluate()
        => Evaluate(packages.States, workers.Snapshot());

    public static NodeReadinessResult Evaluate(IReadOnlyList<NodeMapReadiness> maps,
        IReadOnlyList<WorkerSnapshot> workers)
    {
        bool mapsValid = maps.All(map => map.State == NodeMapPackageState.Ready);
        bool workerUsable = workers.Any(worker => worker.Status == WorkerStatus.Ready
            && (worker.Health == null || worker.Health.Status == WorkerStatus.Ready));
        if (!mapsValid) return new(false, false, workerUsable, "maps_unready");
        if (!workerUsable) return new(false, true, false, "worker_unready");
        return NodeReadinessResult.Ready;
    }
}
