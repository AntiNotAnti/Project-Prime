using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Xunit;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Node.Tests;

public sealed class WorkerPackageContractTests
{
    [Fact]
    public void PackageRelativeWorkerResolvesFromApplicationBaseButPathCommandsRemainUnchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "project-prime-node-package-" + Guid.NewGuid().ToString("N"));
        string relative = WorkerLaunchOptions.ResolveExecutableFileName("worker/ProjectPrime.Server.Worker", root);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "worker", "ProjectPrime.Server.Worker")), relative);
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

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public async Task WorkerPackageAcceptsOnlySupportedSnapshotCadence(int rateHz)
    {
        WorkerLaunchOptions launch = new()
        {
            FileName = "dotnet",
            SnapshotRateHz = rateHz,
            Arguments = ["worker.dll", "--lanes", "1", "--max-matches", "1", "--max-matches-per-lane", "1"],
            Capacity = new(1, 32, 0, 0)
        };
        Assert.Equal(rateHz, launch.SnapshotRateHz);
        await using var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(launch with
        {
            SnapshotRateHz = 15
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(launch with
        {
            Arguments = ["worker.dll", "--snapshot-rate-hz", "60"]
        }));
        Assert.Equal(SnapshotCadence.DefaultRateHz, new WorkerLaunchOptions
        {
            FileName = "dotnet"
        }.SnapshotRateHz);
        Assert.False(new WorkerLaunchOptions { FileName = "dotnet" }.AckCoalescingEnabled);
        await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(launch with
        {
            Arguments = ["worker.dll", "--ack-coalescing", "true"]
        }));
    }
}
