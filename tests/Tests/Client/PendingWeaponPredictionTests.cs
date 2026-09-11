using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class PendingWeaponPredictionTests
{
    [Fact]
    public void RepeatedSelectionKeepsTheFirstAcknowledgementSequence()
    {
        var prediction = NewPrediction();

        prediction.ObserveInput(7, 3, 2, 40);
        prediction.ObserveInput(7, 3, 2, 41);

        Assert.True(prediction.HasPending);
        Assert.Equal((byte)2, prediction.PendingWeapon);
        Assert.Equal(40u, prediction.FirstInputSequence);
        Assert.False(prediction.ShouldApplyAuthoritative(7, 3, true, 39));
        Assert.True(prediction.ShouldApplyAuthoritative(7, 3, true, 40));
        Assert.False(prediction.HasPending);
    }

    [Fact]
    public void AcknowledgedRejectionAppliesTheServerWeapon()
    {
        var prediction = NewPrediction();
        prediction.ObserveInput(7, 3, 2, 40);

        Assert.False(prediction.ShouldApplyAuthoritative(7, 3, true, 39));
        Assert.True(prediction.ShouldApplyAuthoritative(7, 3, true, 42));
        Assert.False(prediction.HasPending);
    }

    [Fact]
    public void ANewSelectionSupersedesAnOlderPendingSelection()
    {
        var prediction = NewPrediction();
        prediction.ObserveInput(7, 3, 2, 40);
        prediction.ObserveInput(7, 3, 4, 41);

        Assert.Equal((byte)4, prediction.PendingWeapon);
        Assert.Equal(41u, prediction.FirstInputSequence);
        Assert.False(prediction.ShouldApplyAuthoritative(7, 3, true, 40));
        Assert.True(prediction.ShouldApplyAuthoritative(7, 3, true, 41));
    }

    [Fact]
    public void EpochChangesReleaseStalePredictionAndWrapIsNewer()
    {
        var prediction = NewPrediction();
        prediction.ObserveInput(7, 3, 2, uint.MaxValue);

        Assert.False(prediction.ShouldApplyAuthoritative(7, 3, true, uint.MaxValue - 1));
        Assert.True(prediction.ShouldApplyAuthoritative(7, 3, true, 0));

        prediction.ObserveInput(7, 3, 2, 10);
        Assert.True(prediction.ShouldApplyAuthoritative(8, 3, true, 10));
        Assert.False(prediction.HasPending);
        prediction.ObserveInput(8, 4, 2, 10);
        Assert.True(prediction.ShouldApplyAuthoritative(8, 4, true, 10));
    }

    [Fact]
    public void ReturningToTheAuthoritativeWeaponCancelsPendingSelection()
    {
        var prediction = NewPrediction();
        prediction.ObserveInput(7, 3, 2, 40);
        prediction.ObserveInput(7, 3, 1, 41);

        Assert.False(prediction.HasPending);
        Assert.True(prediction.ShouldApplyAuthoritative(7, 3, true, 40));
    }

    private static PendingWeaponPrediction NewPrediction()
    {
        var prediction = new PendingWeaponPrediction();
        prediction.ObserveAuthoritative(7, 3, 1);
        return prediction;
    }
}
