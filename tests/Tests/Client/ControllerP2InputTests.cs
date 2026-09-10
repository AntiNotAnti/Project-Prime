using System;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Mathematics;
using SDL;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ControllerP2InputTests
{
    [Fact]
    public void GyroMapsSdlRadiansAndExpiresStaleSamples()
    {
        var processor = new GyroLookProcessor();
        Vector2 value = processor.SubmitRadiansPerSecond(
            new Vector3(MathF.PI / 2, -MathF.PI, 7), 10,
            enabled: true, sensitivity: 1, invertX: false, invertY: false);

        Assert.Equal(180, value.X, 3);
        Assert.Equal(-90, value.Y, 3);
        Assert.Equal(value, processor.Sample(10.05, enabled: true));
        Assert.Equal(Vector2.Zero, processor.Sample(10.101, enabled: true));
    }

    [Fact]
    public void GyroResetAndDisabledInputCannotLeakVelocity()
    {
        var processor = new GyroLookProcessor();
        processor.SubmitRadiansPerSecond(Vector3.UnitX, 1, true, 1, false, false);
        processor.Reset();
        Assert.Equal(Vector2.Zero, processor.Sample(1.01, true));
        Assert.Equal(Vector2.Zero, processor.SubmitRadiansPerSecond(
            Vector3.UnitY, 2, false, 1, false, false));
    }

    [Fact]
    public void SdlPenCompatibilityMouseIsFilteredButDesktopTouchMouseIsPreserved()
    {
        Assert.True(SdlGameHost.IsSyntheticPenMouseId((uint)SDL3.SDL_PEN_MOUSEID));
        Assert.False(SdlGameHost.IsSyntheticPenMouseId((uint)SDL3.SDL_TOUCH_MOUSEID));
        Assert.False(SdlGameHost.IsSyntheticPenMouseId(1));
    }

    [Fact]
    public void OnFootFastStylusMotionRemainsLookInputWithoutFlickBoost()
    {
        var coordinator = new LookInputCoordinator(() => 1);
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, density: 1);
        stylus.ConfigureGestures(classic: true, doubleTap: false, flick: true,
            flickContext: SdlGameHost.IsStylusFlickContext(
                hasLocalPlayer: true, isAltForm: false), density: 1);
        Assert.True(stylus.PointerDown(new PointerSample(1, PointerToolKind.Stylus,
            0, 0, .5f, StylusButtons.None, 1000)));
        Assert.True(stylus.PointerMove(new PointerSample(1, PointerToolKind.Stylus,
            80, 0, .5f, StylusButtons.None, 1050)));

        Assert.NotEqual(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, 1).DeltaDegrees);
        Assert.False(stylus.ConsumeState().FlickBoost);
        Assert.True(SdlGameHost.IsStylusFlickContext(true, true));
    }

    [Fact]
    public void HapticsUsesPriorityCombinationAndIdentityDedupe()
    {
        bool prior = InputSettings.GamepadHapticsEnabled;
        var sink = new HapticSink();
        try
        {
            InputSettings.GamepadHapticsEnabled = true;
            GamepadHaptics.Stop(clearIdentities: true);
            GamepadHaptics.Attach(sink);
            Assert.True(GamepadHaptics.Play(HapticEvent.WeaponFire, 42));
            Assert.False(GamepadHaptics.Play(HapticEvent.WeaponFire, 42));
            Assert.True(GamepadHaptics.Play(HapticEvent.TakingDamage, 43));
            Assert.True(GamepadHaptics.Pump());

            Assert.Equal(GamepadHaptics.Pattern(HapticEvent.TakingDamage), sink.Last);
            Assert.Equal(1, sink.ApplyCount);
        }
        finally
        {
            GamepadHaptics.Detach(sink);
            GamepadHaptics.Stop(clearIdentities: true);
            InputSettings.GamepadHapticsEnabled = prior;
        }
    }

    [Fact]
    public void HapticNativeCallbacksRunOnlyFromPumpAndOutsideMutationLock()
    {
        bool prior = InputSettings.GamepadHapticsEnabled;
        var sink = new HapticSink();
        try
        {
            InputSettings.GamepadHapticsEnabled = true;
            GamepadHaptics.Stop(clearIdentities: true);
            GamepadHaptics.Attach(sink);
            Assert.True(GamepadHaptics.Play(HapticEvent.WeaponFire, 90));
            Assert.Equal(0, sink.ApplyCount);
            Assert.True(GamepadHaptics.Pump());
            Assert.Equal(1, sink.ApplyCount);
            Assert.False(sink.CallbackObservedMutationLock);

            GamepadHaptics.Stop();
            Assert.Equal(0, sink.StopCount);
            Assert.True(GamepadHaptics.Pump());
            Assert.Equal(1, sink.StopCount);
            GamepadHaptics.Detach(sink);
            Assert.Equal(1, sink.StopCount);
        }
        finally
        {
            GamepadHaptics.Detach(sink);
            GamepadHaptics.Stop(clearIdentities: true);
            InputSettings.GamepadHapticsEnabled = prior;
        }
    }

    [Theory]
    [InlineData(ControllerFamily.Xbox, GamepadButtons.A, "A")]
    [InlineData(ControllerFamily.PlayStation, GamepadButtons.A, "Cross")]
    [InlineData(ControllerFamily.Nintendo, GamepadButtons.A, "B")]
    [InlineData(ControllerFamily.Generic, GamepadButtons.RightTrigger, "RT")]
    public void GlyphLabelsDoNotChangeNormalizedBindings(ControllerFamily family,
        GamepadButtons button, string expected)
    {
        Assert.Equal(expected, ControllerGlyphs.Label(button, family));
    }

    [Fact]
    public void LocalTelemetryIsOptInBoundedAndIdentityFree()
    {
        bool prior = InputSettings.InputBalanceTelemetryEnabled;
        try
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = false;
            InputBalanceTelemetry.RecordShot(LookDeviceKind.GamepadStick, 1, 2,
                zoomed: true, angularErrorDegrees: 3);
            Assert.True(InputBalanceTelemetry.TrySnapshot(LookDeviceKind.GamepadStick,
                1, 2, out InputBalanceSnapshot disabled));
            Assert.Equal(0u, disabled.Shots);

            InputSettings.InputBalanceTelemetryEnabled = true;
            InputBalanceTelemetry.RecordShot(LookDeviceKind.GamepadStick, 1, 2,
                zoomed: true, angularErrorDegrees: 3);
            InputBalanceTelemetry.RecordHit(LookDeviceKind.GamepadStick, 1, 2,
                zoomed: true, damage: 17);
            InputBalanceTelemetry.RecordAssist(LookDeviceKind.GamepadStick, 1, 2,
                120, .2f, .8f, acquiredTarget: true);
            Assert.True(InputBalanceTelemetry.TrySnapshot(LookDeviceKind.GamepadStick,
                1, 2, out InputBalanceSnapshot value));
            Assert.Equal(1u, value.Shots);
            Assert.Equal(1u, value.Hits);
            Assert.Equal(17u, value.Damage);
            Assert.Equal(3, value.AverageAngularError);
            Assert.Equal(120, value.AverageAcquisitionMilliseconds);
            Assert.False(InputBalanceTelemetry.TrySnapshot(LookDeviceKind.GamepadStick,
                hunter: 99, weapon: 2, out _));
        }
        finally
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = prior;
        }
    }

    [Fact]
    public void DelayedHitUsesBoundedFiringTimeDeviceAndZoomContext()
    {
        bool prior = InputSettings.InputBalanceTelemetryEnabled;
        try
        {
            InputSettings.InputBalanceTelemetryEnabled = true;
            InputBalanceTelemetry.Reset();
            InputBalanceTelemetry.RecordShot(LookDeviceKind.GamepadGyro, 3, 4,
                zoomed: true, angularErrorDegrees: 2, commandSequence: 1);
            Assert.True(InputBalanceTelemetry.RecordAttributedHit(1, 4, 25));
            Assert.False(InputBalanceTelemetry.RecordAttributedHit(99, 4, 25));
            Assert.True(InputBalanceTelemetry.TrySnapshot(LookDeviceKind.GamepadGyro,
                3, 4, out InputBalanceSnapshot attributed));
            Assert.Equal(1u, attributed.Hits);
            Assert.Equal(1u, attributed.ZoomedHits);
            Assert.Equal(0u, attributed.UnzoomedHits);

            for (uint command = 2; command <= 130; command++)
                InputBalanceTelemetry.RecordShot(LookDeviceKind.Mouse, 0, 0,
                    zoomed: false, angularErrorDegrees: 0,
                    commandSequence: command);
            Assert.False(InputBalanceTelemetry.RecordAttributedHit(1, 4, 1));
            Assert.True(InputBalanceTelemetry.RecordAttributedHit(130, 0, 1));
        }
        finally
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = prior;
        }
    }

    [Fact]
    public void ControllerNavigationSeparatesFocusTravelFromValueAdjustment()
    {
        GamepadState prior = GamepadInput.State;
        try
        {
            GamepadInput.State = new GamepadState { Connected = true };
            Assert.Equal(1, PrimeShellView.PadDirection(GamepadButtons.DpadDown));
            Assert.Equal(-1, PrimeShellView.PadDirection(GamepadButtons.DpadUp));
            Assert.Equal(2, PrimeShellView.PadDirection(GamepadButtons.DpadRight));
            Assert.Equal(-2, PrimeShellView.PadDirection(GamepadButtons.DpadLeft));
        }
        finally { GamepadInput.State = prior; }
    }

    [Fact]
    public void ControllerNavigationInventoryCoversEveryPlannedSurfaceWithoutMouseSimulation()
    {
        ControllerNavigationSurface[] expected = Enum.GetValues<ControllerNavigationSurface>();
        Assert.Equal(expected, ControllerNavigationContract.Surfaces.ToArray());
        Assert.Equal(10, expected.Length);
        Assert.True(ControllerNavigationContract.UsesFocusOrNormalizedBindings);
        Assert.False(ControllerNavigationContract.SynthesizesPointerInput);
    }

    [Theory]
    [InlineData((int)ControllerComboSelector.Map)]
    [InlineData((int)ControllerComboSelector.Mode)]
    [InlineData((int)ControllerComboSelector.Bots)]
    [InlineData((int)ControllerComboSelector.Hunter)]
    public void ControllerComboBoxOpenCommitCancelAndVerticalSelectionAreExplicit(
        int selectorValue)
    {
        ControllerComboSelector selector = (ControllerComboSelector)selectorValue;
        var closed = new ControllerComboState(selector, SelectedIndex: 1,
            OriginalIndex: 1, IsOpen: false);
        ControllerComboState opened = ControllerComboNavigation.Transition(closed,
            itemCount: 3, ControllerComboCommand.Accept);
        Assert.True(opened.IsOpen);
        ControllerComboState moved = ControllerComboNavigation.Transition(opened,
            3, ControllerComboCommand.Next);
        Assert.Equal(2, moved.SelectedIndex);
        Assert.Equal(selector, moved.Selector);
        ControllerComboState cancelled = ControllerComboNavigation.Transition(moved,
            3, ControllerComboCommand.Cancel);
        Assert.False(cancelled.IsOpen);
        Assert.Equal(1, cancelled.SelectedIndex);

        ControllerComboState reopened = ControllerComboNavigation.Transition(cancelled,
            3, ControllerComboCommand.Accept);
        ControllerComboState previous = ControllerComboNavigation.Transition(reopened,
            3, ControllerComboCommand.Previous);
        ControllerComboState committed = ControllerComboNavigation.Transition(previous,
            3, ControllerComboCommand.Accept);
        Assert.False(committed.IsOpen);
        Assert.Equal(0, committed.SelectedIndex);
    }

    [Fact]
    public void HunterControllerPreviewDefersCallbackUntilSingleAcceptAndCancelRestores()
    {
        var committed = new System.Collections.Generic.List<Hunter>();
        var gate = new DeferredControllerSelection<Hunter>(Hunter.Samus,
            committed.Add);

        gate.Begin(Hunter.Samus);
        gate.Preview(Hunter.Kanden);
        gate.Preview(Hunter.Trace);
        Assert.Empty(committed);
        Assert.Equal(Hunter.Samus, gate.Cancel());
        Assert.Empty(committed);

        gate.Begin(Hunter.Samus);
        gate.Preview(Hunter.Kanden);
        Assert.True(gate.Accept());
        Assert.False(gate.Accept());
        Assert.Equal([Hunter.Kanden], committed);

        gate.Begin(Hunter.Samus);
        gate.Preview(Hunter.Kanden);
        gate.Preview(Hunter.Samus);
        Assert.False(gate.Accept());
        Assert.Equal([Hunter.Kanden], committed);
    }

    [Fact]
    public void P2SettingsRoundTripAndDefaultGyroOff()
    {
        InputSettings.Reset();
        Assert.False(InputSettings.GamepadGyroEnabled);
        Assert.True(InputSettings.GamepadHapticsEnabled);
        Assert.False(InputSettings.InputBalanceTelemetryEnabled);

        InputSettings.LoadLines([
            "gamepad_gyro_enabled=true",
            "gamepad_gyro_sensitivity=1.75",
            "gamepad_gyro_invert_x=true",
            "gamepad_haptics_enabled=false",
            "input_balance_telemetry=true"]);

        Assert.True(InputSettings.GamepadGyroEnabled);
        Assert.Equal(1.75f, InputSettings.GamepadGyroSensitivity);
        Assert.True(InputSettings.GamepadGyroInvertX);
        Assert.False(InputSettings.GamepadHapticsEnabled);
        Assert.True(InputSettings.InputBalanceTelemetryEnabled);
        Assert.Contains("input_schema=4", InputSettings.GetSaveLines());
        InputSettings.Reset();
    }

    private sealed class HapticSink : IGamepadHapticsSink
    {
        public HapticPattern Last { get; private set; }
        public int ApplyCount { get; private set; }
        public int StopCount { get; private set; }
        public bool CallbackObservedMutationLock { get; private set; }
        public void Apply(in HapticPattern pattern)
        {
            CallbackObservedMutationLock |= GamepadHaptics.MutationLockHeldByCurrentThread;
            Last = pattern;
            ApplyCount++;
        }
        public void Stop()
        {
            CallbackObservedMutationLock |= GamepadHaptics.MutationLockHeldByCurrentThread;
            StopCount++;
        }
    }
}
