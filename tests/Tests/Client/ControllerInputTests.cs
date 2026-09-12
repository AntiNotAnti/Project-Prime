using System;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Render;
using MphRead.Droid;
using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ControllerInputTests
{
    [Fact]
    public void LocalLookFramePreservesAdditiveStickAndPrecisionMetadata()
    {
        var coordinator = new LookInputCoordinator();
        var stick = new LocalLookFrame(LookDeviceKind.GamepadStick,
            new Vector2(1, 0), new Vector2(.6f, .2f), .632f);
        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 0), .1f, 1));
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Stylus,
            new Vector2(-2, 1), new Vector2(8, -4), 8.94f), 1.001));

        LocalLookFrame frame = coordinator.ConsumeForSimulation(1f / 60f, 1.002);

        Assert.Equal(LookDeviceKind.Stylus, frame.Device);
        Assert.True(frame.HasPrecisionContributor);
        Assert.Equal(new Vector2(-1, 1), frame.DeltaDegrees);
        Assert.Equal(new Vector2(1, 0), frame.ControllerDeltaDegrees);
        Assert.Equal(new Vector2(-2, 1), frame.StylusDeltaDegrees);
        Assert.Equal(new Vector2(.6f, .2f), frame.RawDirection);
    }

    [Fact]
    public void StylusFirstContactIsZeroRelativeAndRecontactDoesNotJump()
    {
        var coordinator = new LookInputCoordinator();
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, density: 2);
        Assert.True(stylus.PointerDown(Pen(3, 100, 50, 10)));
        LocalLookFrame contact = coordinator.ConsumeForSimulation(1f / 60f, 10);
        Assert.Equal(Vector2.Zero, contact.DeltaDegrees);
        Assert.Equal(LookDeviceKind.Stylus, contact.Device);

        Assert.True(stylus.PointerMove(Pen(3, 108, 54, 11)));
        LocalLookFrame moved = coordinator.ConsumeForSimulation(1f / 60f, 11);
        Assert.Equal(new Vector2(-1, -.5f), moved.DeltaDegrees);

        Assert.True(stylus.PointerUp(Pen(3, 108, 54, 12)));
        Assert.True(stylus.PointerDown(Pen(3, 500, 500, 13)));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, 13).DeltaDegrees);
    }

    [Fact]
    public void StylusLiftPreservesPendingMotionButCancelDiscardsOnlyStylus()
    {
        double now = 14;
        var coordinator = new LookInputCoordinator(() => now);
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, 1);

        Assert.True(stylus.PointerDown(Pen(5, 10, 10, 1)));
        Assert.True(stylus.PointerMove(Pen(5, 18, 10, 2)));
        Assert.True(stylus.PointerUp(Pen(5, 18, 10, 3)));
        Assert.Equal(new Vector2(-2, 0),
            coordinator.ConsumeForSimulation(1f / 60f, now).DeltaDegrees);

        now = 15;
        var cancelCoordinator = new LookInputCoordinator(() => now);
        var cancelled = new StylusInput(cancelCoordinator);
        cancelled.Configure(true, 1, false, false, .35f, 1);
        Assert.True(cancelCoordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
            new Vector2(3, 0), Vector2.UnitX, 3), now));
        Assert.True(cancelled.PointerDown(Pen(6, 20, 20, 4)));
        Assert.True(cancelled.PointerMove(Pen(6, 28, 20, 5)));
        cancelled.Cancel();
        Assert.Equal(new Vector2(3, 0),
            cancelCoordinator.ConsumeForSimulation(1f / 60f, now).DeltaDegrees);
    }

    [Fact]
    public void StylusClaimRejectsLifecycleEpochInvalidatedBeforeClaim()
    {
        var coordinator = new LookInputCoordinator();
        long expectedEpoch = coordinator.Epoch;
        coordinator.Reset();

        Assert.False(coordinator.Claim(LookDeviceKind.Stylus, expectedEpoch,
            out long captureEpoch));
        Assert.Equal(-1, captureEpoch);
    }

    [Fact]
    public void StylusZeroClaimSurvivesControllerSampleUntilTheSameConsume()
    {
        double now = 10;
        var coordinator = new LookInputCoordinator(() => now);
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, 1);

        Assert.True(stylus.PointerDown(Pen(4, 20, 20, 1)));
        Assert.True(coordinator.SubmitStateful(
            new LocalLookFrame(LookDeviceKind.GamepadStick,
                Vector2.Zero, new Vector2(.5f, 0), .45f),
            new Vector2(60, 0), seconds: now));

        LocalLookFrame consumed = coordinator.ConsumeForSimulation(1f / 60f, now);

        Assert.Equal(LookDeviceKind.Stylus, consumed.Device);
        Assert.True(consumed.HasPrecisionContributor);
        Assert.True(consumed.HasControllerContributor);
        Assert.Equal(new Vector2(1, 0), consumed.ControllerDeltaDegrees);
        Assert.Equal(Vector2.Zero, consumed.PrecisionDeltaDegrees);
    }

    [Fact]
    public void PrecisionReleaseUsesDeterministicQuietWindowBeforeStickReclaim()
    {
        double now = 20;
        var coordinator = new LookInputCoordinator(() => now);
        Assert.True(coordinator.Claim(LookDeviceKind.Stylus));
        long captureEpoch = coordinator.Epoch;
        Assert.True(coordinator.Release(LookDeviceKind.Stylus, captureEpoch));

        var stick = new LocalLookFrame(LookDeviceKind.GamepadStick,
            Vector2.Zero, new Vector2(.5f, 0), .45f);
        now += LookInputCoordinator.PrecisionReclaimQuietSeconds - .001;
        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 0)));
        Assert.NotEqual(LookDeviceKind.GamepadStick,
            coordinator.ActiveLookDevice);
        coordinator.ConsumeForSimulation(1f / 60f);

        now += .001;
        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 0)));
        Assert.Equal(LookDeviceKind.GamepadStick,
            coordinator.ActiveLookDevice);
    }

    [Fact]
    public void StylusStoresPressureAndButtonEdgesWithoutDefaultPressureFire()
    {
        var stylus = new StylusInput(new LookInputCoordinator());
        stylus.Configure(true, 1, false, false, .35f, 1);
        stylus.PointerDown(Pen(1, 10, 10, 1, .8f, StylusButtons.Primary));
        StylusState pressed = stylus.ConsumeState();
        Assert.Equal(.8f, pressed.Pressure);
        Assert.Equal(StylusButtons.Primary, pressed.PressedButtons);
        Assert.False(pressed.PressureFireActive);
        stylus.PointerUp(Pen(1, 10, 10, 2));
        Assert.Equal(StylusButtons.Primary,
            stylus.ConsumeState().ReleasedButtons);
    }

    [Fact]
    public void StylusPressureFireAndHoverHeldButtonProduceContactEdges()
    {
        var pressure = new StylusInput(new LookInputCoordinator());
        pressure.Configure(true, 1, false, true, .35f, 1);
        Assert.True(pressure.PointerDown(Pen(30, 10, 10, 1, .2f)));
        Assert.False(pressure.ConsumeState().PressureFirePressed);

        Assert.True(pressure.UpdateButtonState(Pen(30, 10, 10, 2, .8f)));
        StylusState thresholdCrossed = pressure.ConsumeState();
        Assert.True(thresholdCrossed.PressureFireActive);
        Assert.True(thresholdCrossed.PressureFirePressed);
        Assert.False(pressure.ConsumeState().PressureFirePressed);

        Assert.True(pressure.UpdateButtonState(Pen(30, 10, 10, 3, .2f)));
        StylusState thresholdReleased = pressure.ConsumeState();
        Assert.False(thresholdReleased.PressureFireActive);
        Assert.True(thresholdReleased.PressureFireReleased);

        var hoverButton = new StylusInput(new LookInputCoordinator());
        Assert.True(hoverButton.PointerProximityMove(Pen(31, 10, 10, 4,
            buttons: StylusButtons.Primary)));
        hoverButton.ConsumeState();
        Assert.True(hoverButton.PointerDown(Pen(31, 10, 10, 5,
            buttons: StylusButtons.Primary)));
        Assert.Equal(StylusButtons.Primary,
            hoverButton.ConsumeState().PressedButtons);
    }

    [Fact]
    public void StylusBindingsHonorConfiguredPrimaryAndSecondaryActions()
    {
        var bindings = new StylusBindings(StylusAction.Zoom, StylusAction.Fire);
        StylusState primary = PenState(StylusButtons.Primary);
        StylusState secondary = PenState(StylusButtons.Secondary);

        Assert.True(bindings.IsDown(primary, StylusAction.Zoom));
        Assert.True(bindings.IsPressed(primary, StylusAction.Zoom));
        Assert.False(bindings.IsDown(primary, StylusAction.Fire));
        Assert.True(bindings.IsDown(secondary, StylusAction.Fire));
        Assert.True(bindings.IsPressed(secondary, StylusAction.Fire));
        Assert.False(bindings.IsDown(secondary, StylusAction.Zoom));
    }

    [Fact]
    public void StylusFlickAndAimDeltaAreBothPreserved()
    {
        var coordinator = new LookInputCoordinator(() => 1);
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, 1);
        stylus.ConfigureGestures(classic: true, doubleTap: false, flick: true,
            flickContext: true, density: 1);

        Assert.True(stylus.PointerDown(Pen(20, 0, 0, 1000)));
        Assert.True(stylus.PointerMove(Pen(20, 60, 0, 1080)));

        Assert.Equal(new Vector2(-15, 0),
            coordinator.ConsumeForSimulation(1f / 60f, 1).DeltaDegrees);
        StylusState state = stylus.ConsumeState();
        Assert.True(state.FlickBoost);
        Assert.True(state.FlickX > 0);
    }

    [Fact]
    public void StylusFlickRearmsWhenFormBecomesEligibleDuringContact()
    {
        var stylus = new StylusInput(new LookInputCoordinator(() => 1));
        stylus.Configure(true, 1, false, false, .35f, 1);
        stylus.ConfigureGestures(classic: true, doubleTap: false, flick: true,
            flickContext: false, density: 1);

        Assert.True(stylus.PointerDown(Pen(21, 0, 0, 1000)));
        Assert.True(stylus.PointerMove(Pen(21, 10, 0, 1050)));
        stylus.ConfigureGestures(classic: true, doubleTap: false, flick: true,
            flickContext: true, density: 1);
        Assert.True(stylus.PointerMove(Pen(21, 70, 0, 1100)));

        StylusState state = stylus.ConsumeState();
        Assert.True(state.FlickBoost);
        Assert.True(state.FlickX > 0);
    }

    [Fact]
    public void TimingDiscontinuityPreservesActiveStylusCapture()
    {
        LookInputCoordinator coordinator = GamepadInput.LookCoordinator;
        var stylus = new StylusInput(coordinator);
        FrameTiming.Reset();
        GamepadInput.Reset();
        try
        {
            Assert.True(stylus.PointerDown(Pen(22, 0, 0, 1000)));
            Assert.True(stylus.PointerMove(Pen(22, 20, 0, 1020)));

            FrameTiming.Reset();
            GamepadInput.BeginFrame(allowLook: false);
            LocalLookFrame first = coordinator.ConsumeForSimulation(
                1f / 60f, 1);
            Assert.NotEqual(Vector2.Zero, first.StylusDeltaDegrees);

            Assert.True(stylus.PointerMove(Pen(22, 40, 0, 1040)));
            LocalLookFrame second = coordinator.ConsumeForSimulation(
                1f / 60f, 1.01);
            Assert.NotEqual(Vector2.Zero, second.StylusDeltaDegrees);
        }
        finally
        {
            stylus.Cancel();
            GamepadInput.Reset();
            FrameTiming.Reset();
        }
    }

    [Fact]
    public void StationaryStylusButtonUpdateDoesNotMoveAnchorOrSubmitLook()
    {
        var coordinator = new LookInputCoordinator(() => 2);
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, false, .35f, 1);

        Assert.True(stylus.PointerDown(Pen(21, 100, 100, 1)));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, 2).DeltaDegrees);
        Assert.True(stylus.UpdateButtonState(Pen(21, 150, 100, 2,
            buttons: StylusButtons.Primary)));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, 2).DeltaDegrees);
        StylusState pressed = stylus.ConsumeState();
        Assert.True(pressed.Contact);
        Assert.Equal(StylusButtons.Primary, pressed.PressedButtons);

        Assert.True(stylus.PointerMove(Pen(21, 108, 100, 3,
            buttons: StylusButtons.Primary)));
        Assert.Equal(new Vector2(-2, 0),
            coordinator.ConsumeForSimulation(1f / 60f, 2).DeltaDegrees);

        Assert.True(stylus.UpdateButtonState(Pen(21, 108, 100, 4)));
        StylusState released = stylus.ConsumeState();
        Assert.Equal(StylusButtons.Primary, released.ReleasedButtons);
    }

    [Fact]
    public void StylusProximityIsNotContactAndRecontactStartsFreshAnchor()
    {
        var coordinator = new LookInputCoordinator(() => 3);
        var stylus = new StylusInput(coordinator);

        Assert.True(stylus.PointerProximityMove(Pen(22, 50, 50, 1)));
        Assert.True(stylus.InProximity);
        Assert.False(stylus.Active);
        Assert.Equal(LookDeviceKind.None, coordinator.ActiveLookDevice);
        Assert.True(stylus.UpdateButtonState(Pen(22, 50, 50, 2,
            buttons: StylusButtons.Primary)));
        StylusState hover = stylus.ConsumeState();
        Assert.False(hover.Contact);
        Assert.True(hover.InProximity);
        Assert.False(StylusBindings.Default.IsDown(hover, StylusAction.Fire));

        Assert.True(stylus.PointerProximityExit(Pen(22, 50, 50, 3)));
        Assert.False(stylus.InProximity);
        Assert.True(stylus.PointerDown(Pen(22, 500, 400, 4)));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, 3).DeltaDegrees);
        Assert.True(stylus.PointerMove(Pen(22, 504, 400, 5)));
        Assert.Equal(new Vector2(-1, 0),
            coordinator.ConsumeForSimulation(1f / 60f, 3).DeltaDegrees);
    }

    [Fact]
    public void StylusProximityLossCancelsContactButLiftKeepsQueuedMovement()
    {
        var cancelledCoordinator = new LookInputCoordinator(() => 4);
        var cancelled = new StylusInput(cancelledCoordinator);
        Assert.True(cancelled.PointerDown(Pen(23, 0, 0, 1)));
        Assert.True(cancelled.PointerMove(Pen(23, 12, 0, 2)));
        Assert.True(cancelled.PointerProximityExit(Pen(23, 12, 0, 3)));
        Assert.Equal(Vector2.Zero,
            cancelledCoordinator.ConsumeForSimulation(1f / 60f, 4).DeltaDegrees);

        var liftedCoordinator = new LookInputCoordinator(() => 5);
        var lifted = new StylusInput(liftedCoordinator);
        Assert.True(lifted.PointerDown(Pen(24, 0, 0, 1)));
        Assert.True(lifted.PointerMove(Pen(24, 12, 0, 2)));
        Assert.True(lifted.PointerUp(Pen(24, 12, 0, 3)));
        Assert.True(lifted.InProximity);
        Assert.Equal(new Vector2(-3, 0),
            liftedCoordinator.ConsumeForSimulation(1f / 60f, 5).DeltaDegrees);

        var discardedCoordinator = new LookInputCoordinator(() => 6);
        var discarded = new StylusInput(discardedCoordinator);
        Assert.True(discarded.PointerDown(Pen(28, 0, 0, 1)));
        Assert.True(discarded.PointerMove(Pen(28, 12, 0, 2)));
        Assert.True(discarded.PointerUp(Pen(28, 12, 0, 3)));
        discarded.Cancel();
        Assert.Equal(Vector2.Zero,
            discardedCoordinator.ConsumeForSimulation(1f / 60f, 6).DeltaDegrees);
    }

    [Fact]
    public void HistoricalStylusSamplesRemainInSubmissionOrder()
    {
        var coordinator = new LookInputCoordinator(() => 6);
        var stylus = new StylusInput(coordinator);
        Assert.True(stylus.PointerDown(Pen(25, 10, 10, 10)));
        Assert.True(stylus.PointerMove(Pen(25, 14, 10, 11)));
        Assert.True(stylus.PointerMove(Pen(25, 19, 10, 12)));
        Assert.True(stylus.PointerMove(Pen(25, 25, 10, 13)));
        Assert.Equal(new Vector2(-3.75f, 0),
            coordinator.ConsumeForSimulation(1f / 60f, 6).DeltaDegrees);
    }

    [Fact]
    public void DirectAndIndirectPenCoordinateNormalizationUsesOnlyKnownScales()
    {
        PointerSample direct = Pen(26, 0, 0, 1) with
        {
            CoordinateKind = PointerCoordinateKind.Direct,
            LogicalDisplayScale = 2
        };
        Assert.Equal(new Vector2(2, 1), StylusInput.NormalizeRelativeDelta(
            new Vector2(4, 2), direct, density: 1));

        PointerSample indirect = Pen(27, 0, 0, 1) with
        {
            CoordinateKind = PointerCoordinateKind.Indirect,
            MappedExtentX = 100,
            MappedExtentY = 50
        };
        Assert.Equal(new Vector2(.04f, .04f), StylusInput.NormalizeRelativeDelta(
            new Vector2(4, 2), indirect, density: 1));

        PointerSample unknownExtent = indirect with { MappedExtentX = 0,
            MappedExtentY = 0 };
        Assert.Equal(new Vector2(4, 2), StylusInput.NormalizeRelativeDelta(
            new Vector2(4, 2), unknownExtent, density: 1));
    }

    [Fact]
    public void StylusTurnPreviewReportsLogicalDegreesAtCurrentSensitivity()
    {
        Assert.Equal(25, StylusTurnPreview.MeasureDegrees(
            new Avalonia.Vector(60, 80), sensitivity: 1));
        Assert.Equal(50, StylusTurnPreview.MeasureDegrees(
            new Avalonia.Vector(60, 80), sensitivity: 2));
        Assert.Equal(25, StylusTurnPreview.MeasureDegrees(
            new Avalonia.Vector(60, 80), sensitivity: float.NaN));
    }

    [Fact]
    public void PointerRouterKeepsFingerMovementAndButtonsWhileStylusAims()
    {
        var touch = new TouchControls();
        touch.Layout(1000, 500, 2);
        var stylus = new StylusInput(new LookInputCoordinator());
        stylus.Configure(true, 1, false, false, .35f, 2);
        var router = new PointerInputRouter(touch, stylus);

        router.PointerDown(new PointerSample(1, PointerToolKind.Finger,
            100, 350, 1, StylusButtons.None, 1));
        router.PointerMove(new PointerSample(1, PointerToolKind.Finger,
            180, 350, 1, StylusButtons.None, 2));
        router.PointerDown(Pen(2, 700, 250, 3));

        Assert.True(stylus.Active);
        Assert.True((touch.Direction & TouchControls.Dir.Right) != 0);
        router.PointerButton(Pen(2, 704, 250, 4,
            buttons: StylusButtons.Primary));
        Assert.Equal(StylusButtons.Primary,
            stylus.ConsumeState().PressedButtons);
        router.PointerButton(Pen(2, 704, 250, 5));
        Assert.Equal(StylusButtons.Primary,
            stylus.ConsumeState().ReleasedButtons);
        router.PointerDown(new PointerSample(3, PointerToolKind.Finger,
            650, 250, 1, StylusButtons.None, 6));
        router.PointerMove(new PointerSample(3, PointerToolKind.Finger,
            750, 250, 1, StylusButtons.None, 7));
        Assert.Equal((0f, 0f), touch.TakeAimDelta());
    }

    [Fact]
    public void StylusOnVisibleControlRemainsCapturedAsControl()
    {
        var touch = new TouchControls();
        touch.Layout(1000, 500, 1);
        var stylus = new StylusInput(new LookInputCoordinator());
        stylus.Configure(true, 1, false, false, .35f, 1);
        var router = new PointerInputRouter(touch, stylus);
        TouchButton fire = touch.Buttons[0];

        router.PointerDown(Pen(7, fire.CentreX, fire.CentreY, 1));
        router.PointerMove(Pen(7, fire.CentreX + 1, fire.CentreY, 2));
        Assert.True(touch.IsHeld(TouchAction.Shoot));
        Assert.False(stylus.Active);
        router.PointerUp(Pen(7, fire.CentreX + 1, fire.CentreY, 3));
        Assert.False(touch.IsHeld(TouchAction.Shoot));
    }

    [Fact]
    public void GestureRecognizerSeparatesDoubleTapFlickCooldownAndDisabledState()
    {
        var gestures = new AimGestureRecognizer { Density = 1 };
        gestures.PointerDown(10, 10, 100);
        gestures.PointerUp(10, 10, 150);
        Assert.False(gestures.TakeDoubleTap());
        gestures.PointerDown(12, 10, 250);
        gestures.PointerUp(12, 10, 300);
        Assert.True(gestures.TakeDoubleTap());

        gestures.PointerDown(0, 0, 1000);
        Assert.True(gestures.PointerMove(60, 0, 1080));
        Assert.True(gestures.TakeFlick().Fired);
        Assert.False(gestures.PointerMove(120, 0, 1100));
        gestures.Cancel();
        gestures.Enabled = false;
        gestures.PointerDown(0, 0, 2000);
        gestures.PointerUp(0, 0, 2010);
        Assert.False(gestures.TakeDoubleTap());
    }

    [Fact]
    public void AimAssistNeverRotatesFromNeutralAndRespectsCaps()
    {
        Vector2 neutral = PlayerAimAssist.ApplyAssistance(Vector2.Zero,
            new Vector2(4, 4), 4, 0, 1, 1f / 60f);
        Assert.Equal(Vector2.Zero, neutral);

        Vector2 assisted = PlayerAimAssist.ApplyAssistance(Vector2.Zero,
            new Vector2(20, -20), 1, 1, 1, 1f / 60f);
        Assert.InRange(assisted.X, 0, PlayerAimAssist.MaxYawRate / 60f);
        Assert.InRange(assisted.Y, -PlayerAimAssist.MaxPitchRate / 60f, 0);
    }

    [Fact]
    public void AimAssistUsesFfaAndTeamOpponentRules()
    {
        Assert.True(PlayerAimAssist.IsOpponent(teams: false, -1, -1));
        Assert.False(PlayerAimAssist.IsOpponent(teams: true, 0, 0));
        Assert.True(PlayerAimAssist.IsOpponent(teams: true, 0, 1));
    }

    [Fact]
    public void AimAssistSelectsLiveCenteredFfaTargetWithoutWorldCollision()
    {
        var scene = new Scene();
        scene.LocalPlayerSlot = 0;
        PlayerEntity owner = scene.Players[0];
        PlayerEntity target = scene.Players[1];
        owner.Health = target.Health = 100;
        owner.LoadFlags = target.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
        owner.TeamIndex = target.TeamIndex = -1;
        target.Position = new Vector3(0, 0, -10);
        owner.CameraInfo.Position = target.ModAimTarget + Vector3.UnitZ * 10;
        owner._gunVec1 = -Vector3.UnitZ;
        var assist = new PlayerAimAssist();
        LocalLookFrame frame = new LocalLookFrame(LookDeviceKind.GamepadStick,
            new Vector2(.2f, 0), new Vector2(.5f, 0), .5f)
            .WithAimAssist(true, 1);

        Vector2 result = assist.Process(owner, frame, 1f / 60f);

        Assert.True(result.X < frame.DeltaDegrees.X,
            "A centered live FFA opponent should apply conservative friction.");
        target.Health = 0;
        Assert.Equal(frame.DeltaDegrees,
            assist.Process(owner, frame, 1f / 60f));
    }

    private static PointerSample Pen(int id, float x, float y, long timestamp,
        float pressure = .5f, StylusButtons buttons = StylusButtons.None)
        => new(id, PointerToolKind.Stylus, x, y, pressure, buttons, timestamp);

    private static StylusState PenState(StylusButtons buttons)
        => new(true, 1, 0, 0, .5f, buttons, buttons, StylusButtons.None,
            1, false, false, false, 0, 0, true);

    [Fact]
    public void StickCurveIsRadialSymmetricAndRejectsNonFiniteValues()
    {
        StickSample center = StickProcessor.Process(Vector2.Zero, .10f, .02f, 1.60f);
        StickSample fullRight = StickProcessor.Process(new Vector2(1, 0), .10f, .02f, 1.60f);
        StickSample fullLeft = StickProcessor.Process(new Vector2(-1, 0), .10f, .02f, 1.60f);

        Assert.False(center.IsActive);
        Assert.Equal(1f, fullRight.Magnitude);
        Assert.Equal(1f, fullRight.ResponseMagnitude);
        Assert.Equal(-fullRight.ResponseMagnitude, fullLeft.Vector.X, 5);
        Assert.False(StickProcessor.Process(new Vector2(float.NaN, 0), .1f, .02f).IsActive);
        Assert.False(StickProcessor.Process(new Vector2(float.PositiveInfinity, 0), .1f, .02f).IsActive);
    }

    [Fact]
    public void MovementUsesRawRadialHysteresisAndEightSectors()
    {
        var processor = new GamepadMovementProcessor();
        Assert.False(processor.Process(new Vector2(.24f, 0)).IsActive);
        Assert.True(processor.Process(new Vector2(.25f, 0)).IsActive);
        Assert.True(processor.Process(new Vector2(.19f, 0)).IsActive);
        Assert.True(processor.Process(new Vector2(.18f, 0)).IsActive);
        Assert.False(processor.Process(new Vector2(.17f, 0)).IsActive);

        Vector2[] directions =
        {
            new(1, 0), new(1, 1), new(0, 1), new(-1, 1),
            new(-1, 0), new(-1, -1), new(0, -1), new(1, -1)
        };
        GamepadMovementDirection[] expected =
        {
            GamepadMovementDirection.Right,
            GamepadMovementDirection.Up | GamepadMovementDirection.Right,
            GamepadMovementDirection.Up,
            GamepadMovementDirection.Up | GamepadMovementDirection.Left,
            GamepadMovementDirection.Left,
            GamepadMovementDirection.Down | GamepadMovementDirection.Left,
            GamepadMovementDirection.Down,
            GamepadMovementDirection.Down | GamepadMovementDirection.Right
        };
        for (int i = 0; i < directions.Length; i++)
            Assert.Equal(expected[i], GamepadMovementProcessor.Quantize(directions[i]));

        float boundary = MathF.Tan(MathF.PI / 8);
        Assert.Equal(GamepadMovementDirection.Right,
            GamepadMovementProcessor.Quantize(new Vector2(1, boundary - .001f)));
        Assert.Equal(GamepadMovementDirection.Up | GamepadMovementDirection.Right,
            GamepadMovementProcessor.Quantize(new Vector2(1, boundary + .001f)));
    }

    [Fact]
    public void TriggerHysteresisProducesSingleEdges()
    {
        var processor = new TriggerProcessor();
        Assert.Equal(GamepadButtons.None, processor.Process(0, 0).Buttons);
        Assert.Equal(GamepadButtons.None, processor.Process(.19f, 0).Pressed);
        TriggerSample press = processor.Process(.20f, 0);
        Assert.Equal(GamepadButtons.LeftTrigger, press.Pressed);
        Assert.Equal(GamepadButtons.None, processor.Process(.16f, 0).Pressed);
        Assert.Equal(GamepadButtons.None, processor.Process(.16f, 0).Released);
        TriggerSample release = processor.Process(.11f, 0);
        Assert.Equal(GamepadButtons.LeftTrigger, release.Released);
        Assert.Equal(GamepadButtons.None, processor.Process(.11f, 0).Released);
    }

    [Fact]
    public void LookDeviceTrackerIgnoresDriftAndTransitionsOnDeliberateInput()
    {
        var tracker = new LookDeviceTracker();
        Assert.False(tracker.Observe(LookDeviceKind.GamepadStick, .10f));
        Assert.Equal(LookDeviceKind.None, tracker.ActiveLookDevice);
        Assert.True(tracker.Observe(LookDeviceKind.GamepadStick, .11f));
        Assert.Equal(LookDeviceKind.GamepadStick, tracker.ActiveLookDevice);
        Assert.True(tracker.Observe(LookDeviceKind.Mouse, .001f));
        Assert.True(tracker.IsPrecisionLook);
        tracker.Reset();
        Assert.Equal(LookDeviceKind.None, tracker.ActiveLookDevice);
    }

    [Fact]
    public void PrecisionLookWinsSameFrameWithoutDroppingContributors()
    {
        var coordinator = new LookInputCoordinator();
        try
        {
            Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
                new Vector2(2, 0), Vector2.UnitX, 2)));
            Assert.True(coordinator.SubmitStateful(new LocalLookFrame(
                LookDeviceKind.GamepadStick, Vector2.Zero, Vector2.UnitX, 1),
                new Vector2(30, 0)));
            LocalLookFrame consumed = coordinator.ConsumeForSimulation();
            Assert.True(consumed.HasPrecisionContributor);
            Assert.True(consumed.HasControllerContributor);
            Assert.Equal(LookDeviceKind.Mouse, coordinator.ActiveLookDevice);
        }
        finally
        {
            coordinator.Reset();
        }
    }

    [Fact]
    public void PrecisionOwnershipSurvivesRepeatedControllerSamples()
    {
        var coordinator = new LookInputCoordinator();
        try
        {
            Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
                new Vector2(2, 0), Vector2.UnitX, 2)));

            for (int i = 0; i < 4; i++)
            {
                Assert.True(coordinator.SubmitStateful(new LocalLookFrame(
                    LookDeviceKind.GamepadStick, Vector2.Zero, Vector2.UnitX, .8f),
                    new Vector2(30, 0)));
                Assert.Equal(LookDeviceKind.Mouse, coordinator.ActiveLookDevice);
                Assert.True(LookDeviceTracker.IsPrecisionDevice(
                    coordinator.PendingContributors));
            }

            LocalLookFrame consumed = coordinator.ConsumeForSimulation();
            Assert.True(consumed.HasPrecisionContributor);
            Assert.True(consumed.HasControllerContributor);
            Assert.Equal(LookDeviceKind.Mouse, coordinator.ActiveLookDevice);
        }
        finally
        {
            coordinator.Reset();
        }
    }

    [Fact]
    public void MixedPrecisionAndStickDeltasRemainAdditiveInRenderAndSimulation()
    {
        double now = 30;
        var coordinator = new LookInputCoordinator(() => now);
        var stick = new LocalLookFrame(LookDeviceKind.GamepadStick,
            Vector2.Zero, new Vector2(.6f, .2f), .55f);
        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 30)));
        Assert.Equal(new Vector2(60, 30), coordinator.Prediction.AngularVelocity);
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
            new Vector2(2, 3), Vector2.UnitX, 2)));
        Assert.Equal(new Vector2(60, 30), coordinator.Prediction.AngularVelocity);

        LocalLookFrame render = coordinator.PeekForRender(now + .005, true);
        Assert.Equal(new Vector2(2, 3), render.PrecisionDeltaDegrees);
        Assert.InRange(render.ControllerDeltaDegrees.X, .2999f, .3001f);
        Assert.InRange(render.ControllerDeltaDegrees.Y, .1499f, .1501f);

        LocalLookFrame simulation = coordinator.ConsumeForSimulation(1f / 60f,
            now + .005);
        Assert.Equal(new Vector2(2, 3), simulation.PrecisionDeltaDegrees);
        Assert.InRange(simulation.ControllerDeltaDegrees.X, .9999f, 1.0001f);
        Assert.InRange(simulation.ControllerDeltaDegrees.Y, .4999f, .5001f);
        Assert.InRange(simulation.DeltaDegrees.X, 2.9999f, 3.0001f);
        Assert.InRange(simulation.DeltaDegrees.Y, 3.4999f, 3.5001f);
        Assert.True(simulation.HasPrecisionContributor);
        Assert.True(simulation.HasControllerContributor);
    }

    [Fact]
    public void HeldControllerNeverBlocksLaterMouseLook()
    {
        double now = 35;
        var coordinator = new LookInputCoordinator(() => now);
        var stick = new LocalLookFrame(LookDeviceKind.GamepadStick,
            Vector2.Zero, new Vector2(.7f, 0), .7f);

        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 0)));
        Assert.Equal(LookDeviceKind.GamepadStick,
            coordinator.ActiveLookDevice);
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
            new Vector2(0, 3), new Vector2(0, 12), 12), now + .001));
        Assert.Equal(LookDeviceKind.Mouse, coordinator.ActiveLookDevice);

        // The held stick may continue publishing state, but it cannot reject
        // or erase mouse movement that arrived after controller ownership.
        Assert.True(coordinator.SubmitStateful(stick, new Vector2(60, 0),
            seconds: now + .002));
        LocalLookFrame frame = coordinator.ConsumeForSimulation(1f / 60f,
            now + .003);

        Assert.Equal(new Vector2(1, 0), frame.ControllerDeltaDegrees);
        Assert.Equal(new Vector2(0, 3), frame.MouseDeltaDegrees);
        Assert.Equal(new Vector2(1, 3), frame.DeltaDegrees);
        Assert.True(frame.HasControllerContributor);
        Assert.True(frame.HasPrecisionContributor);
    }

    [Fact]
    public void StylusReleasePreservesSampleAndResetFencesStaleSubmission()
    {
        double now = 40;
        var coordinator = new LookInputCoordinator(() => now);
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
            new Vector2(2, 0), Vector2.UnitX, 2)));
        Assert.True(coordinator.Claim(LookDeviceKind.Stylus));
        long captureEpoch = coordinator.Epoch;
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Stylus,
            new Vector2(8, 0), Vector2.UnitX, 8), expectedEpoch: captureEpoch));
        Assert.True(coordinator.Release(LookDeviceKind.Stylus, captureEpoch));
        Assert.Equal(new Vector2(10, 0),
            coordinator.ConsumeForSimulation(1f / 60f, now).DeltaDegrees);

        // A reset is a full lifecycle fence. A late movement from the old
        // capture cannot repopulate the cleared buffer.
        Assert.True(coordinator.Claim(LookDeviceKind.Stylus));
        long staleEpoch = coordinator.Epoch;
        coordinator.Reset();
        Assert.False(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Stylus,
            new Vector2(8, 0), Vector2.UnitX, 8), expectedEpoch: staleEpoch));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f, now).DeltaDegrees);
    }

    [Fact]
    public void ProcessedStickMagnitudeDrivesAssistIntentJustOverThreshold()
    {
        StickSample processed = StickProcessor.Process(new Vector2(.171f, 0),
            .10f, .02f, 1.60f);
        Assert.True(processed.Magnitude >= PlayerAimAssist.MinimumStickIntent);
        var frame = new LocalLookFrame(LookDeviceKind.GamepadStick,
            new Vector2(1, 0), new Vector2(.171f, 0), processed.Magnitude);
        Assert.Equal(processed.Magnitude, frame.Magnitude, 5);
        Assert.Equal(new Vector2(.171f, 0), frame.RawDirection);
    }

    [Fact]
    public void AndroidPrecisionRenderTransformMatchesSimulationOnce()
    {
        Vector2 transformed = RenderLookAccumulator.ApplySimulationAimTransforms(
            new Vector2(2, -3), invertX: true, invertY: true, fovScale: .5f);

        Assert.Equal(new Vector2(-1, 1.5f), transformed);
    }

    [Fact]
    public void LookPredictionPeeksWithoutConsumingAndResetsStaleState()
    {
        var prediction = new LookPredictionBuffer();
        prediction.Add(3, 4, 1);
        Assert.Equal(new Vector2(3, 4), prediction.PeekForRender(1, false));
        Assert.Equal(new Vector2(3, 4), prediction.PeekForRender(1, false));
        Assert.Equal(new Vector2(3, 4), prediction.ConsumeForSimulation(1));
        Assert.Equal(Vector2.Zero, prediction.ConsumeForSimulation(1));

        prediction.SetAngularVelocity(new Vector2(10, 0), 2);
        Assert.Equal(Vector2.Zero, prediction.PeekForRender(2.1, false));
        Assert.Equal(new Vector2(1, 0), prediction.PeekForRender(2.1, true));
        prediction.Add(1, 2, 3);
        Assert.Equal(Vector2.Zero, prediction.PeekForRender(3.3, false));
        prediction.Reset();
        Assert.Equal(Vector2.Zero, prediction.PeekForRender(3, true));
    }

    [Fact]
    public void LookPredictionUsesOnlyUnsimulatedTimeAtCommonRefreshRates()
    {
        foreach (double refreshSeconds in new[]
            { 1d / 60, 1d / 120, 1d / 144, 1d / 240 })
        {
            var prediction = new LookPredictionBuffer();
            prediction.SetAngularVelocity(new Vector2(10, 0), 10);
            Vector2 active = prediction.PeekForRender(10 + refreshSeconds, true);
            Assert.InRange(active.X, (float)(refreshSeconds * 10 - 0.0001),
                (float)(refreshSeconds * 10 + 0.0001));
            Assert.Equal(Vector2.Zero,
                prediction.PeekForRender(10 + refreshSeconds, false));
        }
    }

    [Fact]
    public void StatefulPredictionUsesTheFixedStepAccumulatorRemainder()
    {
        FrameTiming.Reset();
        try
        {
            var prediction = new LookPredictionBuffer();
            prediction.SetAngularVelocity(new Vector2(10, 0));

            Assert.Equal(0, FrameTiming.Advance(FrameTiming.StepSeconds / 2));
            Assert.Equal(FrameTiming.StepSeconds / 2,
                FrameTiming.SimulationRemainderSeconds, 10);
            Assert.Equal(10 * FrameTiming.StepSeconds / 2,
                prediction.PeekStatefulForRender(null, simulationActive: true,
                    FrameTiming.SimulationRemainderSeconds).X, 5);

            Assert.Equal(1, FrameTiming.Advance(FrameTiming.StepSeconds));
            Assert.Equal(FrameTiming.StepSeconds / 2,
                FrameTiming.SimulationRemainderSeconds, 10);
            Assert.Equal(10 * FrameTiming.StepSeconds / 2,
                prediction.PeekStatefulForRender(null, simulationActive: true,
                    FrameTiming.SimulationRemainderSeconds).X, 5);
        }
        finally
        {
            FrameTiming.Reset();
        }
    }

    [Fact]
    public void TimingDiscontinuityDoesNotRetriggerHeldGamepadButton()
    {
        GamepadState previousState = GamepadInput.State;
        FrameTiming.Reset();
        GamepadInput.Reset();
        try
        {
            GamepadInput.State = new GamepadState
            {
                Connected = true,
                Buttons = GamepadButtons.RightBumper
            };
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadButtons.RightBumper,
                GamepadInput.PressedButtons & GamepadButtons.RightBumper);

            // A stall/dropped-step reset must not manufacture another weapon
            // cycle edge while the same physical button remains held.
            FrameTiming.Reset();
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadButtons.None,
                GamepadInput.PressedButtons & GamepadButtons.RightBumper);
            Assert.Equal(GamepadButtons.RightBumper,
                GamepadInput.EffectiveButtons & GamepadButtons.RightBumper);

            GamepadInput.State = new GamepadState { Connected = true };
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadButtons.RightBumper,
                GamepadInput.ReleasedButtons & GamepadButtons.RightBumper);

            GamepadInput.State = new GamepadState
            {
                Connected = true,
                Buttons = GamepadButtons.RightBumper
            };
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadButtons.RightBumper,
                GamepadInput.PressedButtons & GamepadButtons.RightBumper);
        }
        finally
        {
            GamepadInput.State = previousState;
            GamepadInput.Reset();
            FrameTiming.Reset();
        }
    }

    [Fact]
    public void LookCurveBoostsOnlyAfterSustainedOuterRingAndResetsOnExit()
    {
        var processor = new GamepadLookProcessor();
        GamepadLookSample first = processor.Advance(Vector2.UnitX, .06f);
        Assert.Equal(0f, first.BoostProgress);
        processor.Advance(Vector2.UnitX, .06f);
        processor.Advance(Vector2.UnitX, .06f);
        GamepadLookSample boosted = processor.Advance(Vector2.UnitX, .06f);
        Assert.True(boosted.BoostProgress > 0);
        processor.Advance(Vector2.Zero, .016f);
        Assert.Equal(0f, processor.OuterSeconds);
        GamepadLookSample left = processor.Advance(-Vector2.UnitX, .016f);
        Assert.True(left.AngularVelocity.X > 0);
    }

    [Fact]
    public void SideEffectFreeLookEvaluationDoesNotReuseBoostAfterReversal()
    {
        var processor = new GamepadLookProcessor();
        GamepadLookSample boosted = processor.Advance(Vector2.UnitX, .30f);
        Assert.True(boosted.BoostProgress > 0);

        GamepadLookSample reversedPreview = processor.Evaluate(-Vector2.UnitX);
        Assert.Equal(0, reversedPreview.BoostProgress);
        GamepadLookSample reversed = processor.Advance(-Vector2.UnitX, .016f);
        Assert.Equal(0, reversed.BoostProgress);
    }

    [Fact]
    public void ControllerZoomMultiplierAppliesOnlyWhileZoomed()
    {
        var processor = new GamepadLookProcessor(zoomMultiplier: .5f);
        GamepadLookSample normal = processor.Evaluate(Vector2.UnitX);
        GamepadLookSample fovScaled = processor.Evaluate(Vector2.UnitX, .5f);
        GamepadLookSample zoomed = processor.Evaluate(Vector2.UnitX, .5f, zoomed: true);

        Assert.Equal(-300, normal.AngularVelocity.X, 3);
        Assert.Equal(-150, fovScaled.AngularVelocity.X, 3);
        Assert.Equal(-75, zoomed.AngularVelocity.X, 3);
    }

    [Fact]
    public void ControllerDisconnectPreservesPendingPrecisionLook()
    {
        GamepadInput.Reset();
        try
        {
            GamepadInput.State = new GamepadState { Connected = true };
            Assert.True(GamepadInput.LookCoordinator.Submit(
                new LocalLookFrame(LookDeviceKind.Mouse, new Vector2(3, 4),
                    Vector2.UnitX, 5), seconds: 1));

            GamepadInput.State = default;
            GamepadInput.SampleNativeFrame();

            Assert.Equal(LookDeviceKind.Mouse,
                GamepadInput.LookCoordinator.ActiveLookDevice);
            Assert.Equal(new Vector2(3, 4),
                GamepadInput.LookCoordinator.PeekForRender(1, false).DeltaDegrees);
        }
        finally
        {
            GamepadInput.Reset();
            GamepadInput.State = default;
        }
    }

    [Fact]
    public void SettingsMigrationSeedsOnlyMissingModernValuesAndPreservesExplicitBindings()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.LoadLines(new[]
            {
                "gamepad_deadzone=0.40",
                "gamepad_look=2",
                "gamepad_move_deadzone=0.20",
                "controller_preset=Competitive",
                "pad_Jump=LeftBumper"
            });

            Assert.Equal(.20f, InputSettings.GamepadMoveDeadZone, 5);
            Assert.Equal(.40f, InputSettings.GamepadLookDeadZone, 5);
            Assert.Equal(2, InputSettings.GamepadHorizontalSensitivity, 5);
            Assert.Equal(2, InputSettings.GamepadVerticalSensitivity, 5);
            Assert.Equal(GamepadButtons.LeftBumper, PadBindings.Get(PadAction.Jump));
            Assert.Equal(GamepadButtons.DpadLeft, PadBindings.Get(PadAction.PrevWeapon));
            Assert.Contains("input_schema=4", InputSettings.GetSaveLines());
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void CompetitivePresetUsesAttachmentMappingsAndManualBindingIsCustom()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.ApplyPreset(ControllerPreset.Competitive);
            Assert.Equal(GamepadButtons.LeftBumper | GamepadButtons.A,
                PadBindings.Get(PadAction.Jump));
            Assert.Equal(GamepadButtons.DpadLeft, PadBindings.Get(PadAction.PrevWeapon));
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);
            Assert.Equal(ControllerPreset.Custom, InputSettings.ControllerPreset);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomSettingsPreserveOmittedBindings(bool includeExplicitBinding)
    {
        InputSettings.Reset();
        try
        {
            PadBindings.Set(PadAction.Jump, GamepadButtons.Y);
            PadBindings.Set(PadAction.Shoot, GamepadButtons.None);
            InputSettings.LoadLines(includeExplicitBinding
                ? new[] { "controller_preset=Custom", "pad_Jump=LeftBumper" }
                : new[] { "controller_preset=Custom" });

            Assert.Equal(ControllerPreset.Custom, InputSettings.ControllerPreset);
            Assert.Equal(includeExplicitBinding ? GamepadButtons.LeftBumper : GamepadButtons.Y,
                PadBindings.Get(PadAction.Jump));
            Assert.Equal(GamepadButtons.None, PadBindings.Get(PadAction.Shoot));

            var saved = InputSettings.GetSaveLines();
            InputSettings.Reset();
            InputSettings.LoadLines(saved);
            Assert.Equal(ControllerPreset.Custom, InputSettings.ControllerPreset);
            Assert.Equal(includeExplicitBinding ? GamepadButtons.LeftBumper : GamepadButtons.Y,
                PadBindings.Get(PadAction.Jump));
            Assert.Equal(GamepadButtons.None, PadBindings.Get(PadAction.Shoot));
        }
        finally
        {
            InputSettings.Reset();
        }
    }
}
