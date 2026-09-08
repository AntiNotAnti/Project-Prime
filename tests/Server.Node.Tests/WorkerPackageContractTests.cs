using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class WorkerPackageContractTests
{
    [Fact]
    public void PackageRelativeWorkerResolvesFromApplicationBaseButPathCommandsRemainUnchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "fruity-node-package-" + Guid.NewGuid().ToString("N"));
        string relative = WorkerLaunchOptions.ResolveExecutableFileName("worker/FruityPrime.Server.Worker", root);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "worker", "FruityPrime.Server.Worker")), relative);
        Assert.Equal("dotnet", WorkerLaunchOptions.ResolveExecutableFileName("dotnet", root));

        string absolute = Path.Combine(root, "worker", "absolute-worker");
        Assert.Equal(absolute, WorkerLaunchOptions.ResolveExecutableFileName(absolute, Path.Combine(root, "other")));
        Assert.Throws<ArgumentException>(() => WorkerLaunchOptions.ResolveExecutableFileName("../outside-worker", root));
    }

    [Fact]
    public async Task NodeRejectsWorkerCapacityThatCannotBeHonoredByTheConfiguredWorker()
    {
        await using var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        WorkerLaunchOptions launch = new()
        {
            FileName = "dotnet",
            Arguments = ["worker.dll", "--lanes", "2", "--max-matches", "2", "--max-matches-per-lane", "1"],
            Capacity = new(4, 32, 0, 0)
        };

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(launch));
        Assert.Contains("MatchLimit", error.Message, StringComparison.Ordinal);
        Assert.Empty(manager.Snapshot());
    }

    [Fact]
    public async Task NodeRejectsPlayerCapacityAboveWorkersBoundedPerMatchBudget()
    {
        await using var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        WorkerLaunchOptions launch = new()
        {
            FileName = "dotnet",
            Arguments = ["worker.dll", "--lanes", "2", "--max-matches", "2", "--max-matches-per-lane", "1"],
            Capacity = new(2, 65, 0, 0)
        };

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(launch));
        Assert.Contains("PlayerLimit", error.Message, StringComparison.Ordinal);
        Assert.Empty(manager.Snapshot());
    }
}
