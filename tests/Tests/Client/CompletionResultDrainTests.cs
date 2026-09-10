using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class CompletionResultDrainTests
{
    [Fact]
    public void NodeFirstCompletionPollsAndAppliesQueuedReplicaBeforeReturning()
    {
        var drain = new CompletionResultDrain();
        bool result = false;
        int polls = 0;
        Assert.False(drain.Advance(10, () => polls++, () => result));
        Assert.True(drain.Advance(10.1, () => { polls++; result = true; }, () => result));
        Assert.Equal(2, polls);
        Assert.True(drain.Advance(10.2, () => polls++, () => result));
        Assert.Equal(2, polls);
    }

    [Fact]
    public void MissingTerminalReplicaHasBoundedFallbackWithoutInventingResults()
    {
        var drain = new CompletionResultDrain();
        int polls = 0;
        Assert.False(drain.Advance(0, () => polls++, () => false));
        Assert.False(drain.Advance(0.249, () => polls++, () => false));
        Assert.True(drain.Advance(CompletionResultDrain.TimeoutSeconds, () => polls++, () => false));
        Assert.Equal(3, polls);
    }
}
