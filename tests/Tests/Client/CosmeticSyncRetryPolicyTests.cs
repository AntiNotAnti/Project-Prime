using System;
using MphRead.Cosmetics;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class CosmeticSyncRetryPolicyTests
{
    private static readonly CosmeticSyncAttempt Attempt = new(Guid.NewGuid(), 7,
        new CosmeticLoadoutIds(1, 2, 3));

    [Fact]
    public void ExactFailedAttemptRetriesOnlyWhenArmedAndStopsAtBound()
    {
        var policy = new CosmeticSyncRetryPolicy();
        Assert.True(policy.CanStart(Attempt));

        Assert.Equal(TimeSpan.FromMilliseconds(250), policy.RecordFailure(Attempt));
        Assert.False(policy.CanStart(Attempt));
        Assert.True(policy.ArmRetry(Attempt));
        Assert.True(policy.CanStart(Attempt));

        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.RecordFailure(Attempt));
        Assert.False(policy.CanStart(Attempt));
        Assert.True(policy.ArmRetry(Attempt));
        Assert.True(policy.CanStart(Attempt));

        Assert.Null(policy.RecordFailure(Attempt));
        Assert.False(policy.ArmRetry(Attempt));
        Assert.False(policy.CanStart(Attempt));
    }

    [Fact]
    public void RevisionOrLoadoutChangeStartsFreshWithoutWaitingForOldRetry()
    {
        var policy = new CosmeticSyncRetryPolicy();
        _ = policy.RecordFailure(Attempt);

        Assert.True(policy.CanStart(Attempt with { Revision = Attempt.Revision + 1 }));
        _ = policy.RecordFailure(Attempt);
        Assert.True(policy.CanStart(Attempt with
        {
            Ids = new CosmeticLoadoutIds(2, 2, 3)
        }));
        _ = policy.RecordFailure(Attempt);
        Assert.True(policy.CanStart(Attempt with { LobbyId = Guid.NewGuid() }));
    }

    [Fact]
    public void ResetAllowsSameAuthoritativeAttemptToStartAgain()
    {
        var policy = new CosmeticSyncRetryPolicy();
        for (int attempt = 0; attempt < CosmeticSyncRetryPolicy.MaximumAttempts; attempt++)
            _ = policy.RecordFailure(Attempt);
        Assert.False(policy.CanStart(Attempt));

        policy.Reset();

        Assert.True(policy.CanStart(Attempt));
    }
}
