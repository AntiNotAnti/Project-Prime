using MphRead.Mods.Input;
using Xunit;

namespace MphRead.Tests.Client;

public class WeaponSelectionTests
{
    [Fact]
    public void LatestValidIntentWaitsForLegalEquipAndPreviousTracksSuccess()
    {
        var intent = new WeaponSelectionIntent();
        intent.Observe(0, true, 10, 1);
        intent.Request(1, 511);
        intent.Request(2, 511);
        Assert.Equal(255, intent.Pending(false, 511));
        Assert.Equal(255, intent.Previous);
        Assert.Equal(2, intent.Pending(true, 511));
        intent.Observe(2, true, 10, 1);
        Assert.Equal(0, intent.Previous);
        Assert.Equal(255, intent.Desired);
        intent.Request(intent.Previous, 511);
        Assert.Equal(0, intent.Pending(true, 511));
    }

    [Fact]
    public void DeathIdentityAndLostAvailabilityCancelQueuedSelection()
    {
        var intent = new WeaponSelectionIntent();
        intent.Observe(0, true, 10, 1);
        intent.Request(2, 511);
        Assert.Equal(255, intent.Pending(true, 1));
        intent.Request(2, 511);
        intent.Observe(0, false, 10, 1);
        Assert.Equal(255, intent.Desired);
        intent.Request(2, 511);
        intent.Observe(0, true, 10, 2);
        Assert.Equal(255, intent.Desired);
        intent.Request(2, 511);
        intent.Observe(0, true, 20, 2);
        Assert.Equal(255, intent.Desired);
    }

    [Fact]
    public void WheelDeadzonePreviewReleaseAndCancelNeverCommitTwice()
    {
        var wheel = new WeaponRadialSelection();
        Assert.Equal(255, wheel.Update(true, false, 0, 0, .2f, 511));
        Assert.Equal(255, wheel.Preview);
        wheel.Update(true, false, 0, 1, .2f, 511);
        Assert.Equal(0, wheel.Preview);
        Assert.Equal(0, wheel.Update(false, false, 0, 0, .2f, 511));
        Assert.Equal(255, wheel.Update(false, false, 0, 0, .2f, 511));
        wheel.Update(true, false, 0, 1, .2f, 511);
        wheel.Update(true, true, 0, 1, .2f, 511);
        Assert.Equal(255, wheel.Update(false, false, 0, 0, .2f, 511));
        wheel.Update(true, false, 0, 1, .2f, 0);
        Assert.Equal(255, wheel.Preview);
        wheel.Reset();
        Assert.Equal(255, wheel.Update(false, false, 0, 0, .2f, 511));
    }
}
