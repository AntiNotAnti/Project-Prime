using MphRead.Droid;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class KillcamTouchTests
{
    [Fact]
    public void SkipHitUsesOnlyTheNormalizedTopRightTarget()
    {
        Assert.True(TouchControls.IsKillcamSkipHit(230, 16));
        Assert.False(TouchControls.IsKillcamSkipHit(207, 16));
        Assert.False(TouchControls.IsKillcamSkipHit(230, 40));
        Assert.False(TouchControls.IsKillcamSkipHit(float.NaN, 16));
    }

    [Fact]
    public void KillcamConsumesOutsidePointerDownWithoutGameplayState()
    {
        var controls = NewControls();
        controls.SetKillcamMode(true);
        try
        {
            controls.PointerDown(1, 200, 100);

            Assert.False(controls.ConsumeKillcamSkip());
            Assert.False(controls.IsHeld(TouchAction.Shoot));
            Assert.False(controls.StickActive);
            Assert.Equal(TouchControls.Dir.None, controls.Direction);
        }
        finally
        {
            controls.SetKillcamMode(false);
        }
    }

    [Fact]
    public void SkipIsOneRisingEdgeUntilThePointerIsReleased()
    {
        var controls = NewControls();
        controls.SetKillcamMode(true);
        try
        {
            float x = 230 * controls.Width / 256;
            float y = 16 * controls.Height / 192;
            controls.PointerDown(7, x, y);
            controls.PointerDown(7, x, y);

            Assert.True(controls.ConsumeKillcamSkip());
            Assert.False(controls.ConsumeKillcamSkip());

            controls.PointerDown(7, x, y);
            Assert.False(controls.ConsumeKillcamSkip());
            controls.PointerUp(7);
            controls.PointerDown(7, x, y);
            Assert.True(controls.ConsumeKillcamSkip());
        }
        finally
        {
            controls.SetKillcamMode(false);
        }
    }

    [Fact]
    public void LeavingKillcamDoesNotResurrectHeldTouchActions()
    {
        var controls = NewControls();
        TouchButton fire = controls.Buttons[0];
        controls.PointerDown(3, fire.CentreX, fire.CentreY);
        Assert.True(controls.IsHeld(TouchAction.Shoot));

        controls.SetKillcamMode(true);
        controls.SetKillcamMode(false);

        Assert.False(controls.IsHeld(TouchAction.Shoot));
        Assert.False(controls.StickActive);
        (float X, float Y) delta = controls.TakeAimDelta();
        Assert.Equal(0, delta.X);
        Assert.Equal(0, delta.Y);
    }

    private static TouchControls NewControls()
    {
        var controls = new TouchControls();
        controls.Layout(1280, 720, 1);
        return controls;
    }
}
