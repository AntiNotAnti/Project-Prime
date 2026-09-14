using Xunit;

namespace MphRead.Tests;

public sealed class WorkerProgramTests
{
    [Fact]
    public async Task RuntimeSmokeDoesNotRequireGameContent()
    {
        Assert.Equal(0, await ProjectPrime.Server.Worker.Program.Main(
            ["--runtime-smoke", "true"]));
    }

    [Fact]
    public void ArtifactInjectionOptionsAreBoundedAndDeterministic()
    {
        var options = new ProjectPrime.Server.Worker.WorkerOptions
        {
            ArtifactDelayMilliseconds = 100,
            ArtifactFailureRate = .25,
            ArtifactFailureSeed = 42
        };
        options.Validate();
        Assert.Throws<ArgumentException>(() => (options with { ArtifactDelayMilliseconds = 120001 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { ArtifactFailureRate = 1.01 }).Validate());
    }
}
