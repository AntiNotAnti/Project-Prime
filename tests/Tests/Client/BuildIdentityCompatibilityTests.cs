using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
using MphRead.Mods.Update;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class BuildIdentityCompatibilityTests
{
    [Fact]
    public void ClientAndWorkerUseTheSameDirectoryBuildIdentity()
        => Assert.Equal(BuildVersion.Display, WorkerOptions.ActualBuildVersion);
}
