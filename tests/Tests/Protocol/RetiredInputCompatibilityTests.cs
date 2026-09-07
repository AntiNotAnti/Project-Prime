using System;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class RetiredInputCompatibilityTests
{
    [Fact]
    public void LegacyScanBitStaysReservedWithoutShiftingWeaponOrStateBits()
    {
        Assert.Equal(1u << 10, (uint)IntentButtons.ReservedScanVisor);
        Assert.Equal(1u << 11, (uint)IntentButtons.NextWeapon);
        Assert.Equal(1u << 12, (uint)IntentButtons.PrevWeapon);
        Assert.Equal(1u << 17, (uint)IntentButtons.ZoomedState);
        Assert.Equal(1u << 20, (uint)IntentButtons.SpectatingState);
        // The current authoritative protocol has always used a different mask.
        Assert.Equal(1u << 10, (uint)InputButtons.NextWeapon);
    }

    [Fact]
    public void RetiredPadSettingsCannotRemapAnExistingAction()
    {
        GamepadButtons jump = PadBindings.Get(PadAction.Jump);
        Assert.False(PadBindings.TryLoad("pad_Scan", "A"));
        Assert.False(PadBindings.TryLoad("pad_ScanVisor", "A"));
        Assert.False(PadBindings.TryLoad("pad_4", "A"));
        Assert.False(PadBindings.TryLoad("pad_5", "A"));
        Assert.Equal(jump, PadBindings.Get(PadAction.Jump));
        Assert.Equal(6, (int)PadAction.Scoreboard);
        Assert.DoesNotContain(PadBindings.Actions, action => action.ToString().StartsWith("Scan", StringComparison.Ordinal));
    }

    [Fact]
    public void SpectatorViewHasItsOwnTouchSettingAfterScanRetirement()
    {
        bool enabled = TouchSettings.IsEnabled(TouchControl.SpectatorView);
        try
        {
            Assert.False(TouchSettings.ReadSetting("touch_scan", "false"));
            Assert.False(TouchSettings.ReadSetting("touch_scanvisor", "false"));
            Assert.True(TouchSettings.ReadSetting("touch_spectatorview", "false"));
            Assert.False(TouchSettings.IsEnabled(TouchControl.SpectatorView));
            Assert.Equal(5, (int)TouchControl.Missile);
        }
        finally { TouchSettings.SetEnabled(TouchControl.SpectatorView, enabled); }
    }
}
