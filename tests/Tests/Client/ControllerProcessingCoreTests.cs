using System;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ControllerProcessingCoreTests
{
    private const float ActiveMagnitude = 0.8f;

    [Fact]
    public void MovementDirectionHysteresisCoversEveryNeighboringBoundaryBothWays()
    {
        for (int sector = 0; sector < 8; sector++)
        {
            float center = sector * 45;
            float nextCenter = (sector + 1) * 45;
            float boundary = center + 22.5f;
            GamepadMovementDirection current = DirectionAt(center);
            GamepadMovementDirection next = DirectionAt(nextCenter);

            var increasing = new GamepadMovementProcessor();
            Assert.Equal(current, increasing.Process(At(center)).Direction);
            Assert.Equal(current, increasing.Process(At(boundary + 5.5f)).Direction);
            Assert.Equal(next, increasing.Process(At(boundary + 6.5f)).Direction);

            var decreasing = new GamepadMovementProcessor();
            Assert.Equal(next, decreasing.Process(At(nextCenter)).Direction);
            Assert.Equal(next, decreasing.Process(At(boundary - 5.5f)).Direction);
            Assert.Equal(current, decreasing.Process(At(boundary - 6.5f)).Direction);
        }
    }

    [Fact]
    public void MovementDirectionStateResetsOnInactivityExplicitResetAndNonfiniteInput()
    {
        var processor = new GamepadMovementProcessor();
        GamepadMovementDirection diagonal = GamepadMovementDirection.Up
            | GamepadMovementDirection.Right;

        Assert.Equal(GamepadMovementDirection.Right,
            processor.Process(At(0)).Direction);
        Assert.False(processor.Process(Vector2.Zero).IsActive);
        Assert.Equal(diagonal, processor.Process(At(24)).Direction);

        processor.Process(At(0));
        processor.Reset();
        Assert.Equal(diagonal, processor.Process(At(24)).Direction);

        processor.Process(At(0));
        Assert.False(processor.Process(new Vector2(float.NaN, 0)).IsActive);
        Assert.Equal(diagonal, processor.Process(At(24)).Direction);

        processor.Process(At(0));
        Assert.False(processor.Process(
            new Vector2(float.PositiveInfinity, 0)).IsActive);
        Assert.Equal(diagonal, processor.Process(At(24)).Direction);
    }

    [Fact]
    public void MovementDirectionHysteresisDoesNotDelayReversal()
    {
        var processor = new GamepadMovementProcessor();
        Assert.Equal(GamepadMovementDirection.Right,
            processor.Process(Vector2.UnitX).Direction);
        Assert.Equal(GamepadMovementDirection.Left,
            processor.Process(-Vector2.UnitX).Direction);
        Assert.Equal(GamepadMovementDirection.Down,
            processor.Process(-Vector2.UnitY).Direction);
    }

    [Fact]
    public void ControllerDisconnectClearsPreviousMovementSector()
    {
        InputSettings.Reset();
        GamepadInput.Reset();
        try
        {
            GamepadInput.State = new GamepadState
            {
                Connected = true,
                LeftX = 1,
                LeftY = 0
            };
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadMovementDirection.Right,
                GamepadInput.Movement.Direction);

            GamepadInput.State = default;
            GamepadInput.BeginFrame(allowLook: false);

            Vector2 justPastBoundary = At(24);
            GamepadInput.State = new GamepadState
            {
                Connected = true,
                LeftX = justPastBoundary.X,
                LeftY = justPastBoundary.Y
            };
            GamepadInput.BeginFrame(allowLook: false);
            Assert.Equal(GamepadMovementDirection.Up | GamepadMovementDirection.Right,
                GamepadInput.Movement.Direction);
        }
        finally
        {
            GamepadInput.State = default;
            GamepadInput.Reset();
            InputSettings.Reset();
        }
    }

    [Fact]
    public void ResponseCurvePresetsMapToProcessorExponentsWithoutChangingDefault()
    {
        Assert.Equal(1, GamepadLookProfiles.ResponseExponent(
            GamepadResponseCurvePreset.Linear, 1.37f));
        Assert.Equal(1.6f, GamepadLookProfiles.ResponseExponent(
            GamepadResponseCurvePreset.Balanced, 1.37f));
        Assert.Equal(2, GamepadLookProfiles.ResponseExponent(
            GamepadResponseCurvePreset.Precision, 1.37f));
        Assert.Equal(1.37f, GamepadLookProfiles.ResponseExponent(
            GamepadResponseCurvePreset.Custom, 1.37f));

        var currentDefault = new GamepadLookProcessor();
        var balanced = CreateLook(GamepadResponseCurvePreset.Balanced,
            GamepadTurnAccelerationPreset.Standard);
        GamepadLookSample current = currentDefault.Evaluate(new Vector2(.5f, .25f));
        GamepadLookSample mapped = balanced.Evaluate(new Vector2(.5f, .25f));
        Assert.Equal(current.ResponseMagnitude, mapped.ResponseMagnitude);
        Assert.Equal(current.AngularVelocity, mapped.AngularVelocity);
    }

    [Fact]
    public void TurnAccelerationPresetsMapDirectlyToExistingBoostFields()
    {
        GamepadTurnAccelerationProfile off = GamepadLookProfiles.TurnAcceleration(
            GamepadTurnAccelerationPreset.Off);
        GamepadTurnAccelerationProfile standard = GamepadLookProfiles.TurnAcceleration(
            GamepadTurnAccelerationPreset.Standard);
        GamepadTurnAccelerationProfile fast = GamepadLookProfiles.TurnAcceleration(
            GamepadTurnAccelerationPreset.Fast);

        Assert.False(off.Enabled);
        Assert.True(standard.Enabled);
        Assert.True(fast.Enabled);
        Assert.Equal(GamepadLookProcessor.DefaultOuterBoostStart,
            standard.OuterBoostStart);
        Assert.Equal(GamepadLookProcessor.DefaultOuterYawBoost,
            standard.OuterYawBoost);
        Assert.Equal(GamepadLookProcessor.DefaultOuterPitchBoost,
            standard.OuterPitchBoost);
        Assert.Equal(GamepadLookProcessor.DefaultBoostDelaySeconds,
            standard.BoostDelaySeconds);
        Assert.Equal(GamepadLookProcessor.DefaultBoostRampSeconds,
            standard.BoostRampSeconds);
        Assert.Equal(standard.OuterBoostStart, fast.OuterBoostStart);
        Assert.Equal(standard.OuterYawBoost, fast.OuterYawBoost);
        Assert.Equal(standard.OuterPitchBoost, fast.OuterPitchBoost);
        Assert.Equal(.09f, fast.BoostDelaySeconds);
        Assert.Equal(.06f, fast.BoostRampSeconds);
    }

    [Fact]
    public void LookProfilesPreserveDeadzoneBoostTimingAndReversalRules()
    {
        var standard = CreateLook(GamepadResponseCurvePreset.Balanced,
            GamepadTurnAccelerationPreset.Standard);
        Assert.False(standard.Evaluate(new Vector2(.1f, 0)).IsActive);
        Assert.True(standard.Evaluate(new Vector2(.11f, 0)).IsActive);
        Assert.Equal(1, standard.Evaluate(new Vector2(.98f, 0)).Magnitude);

        Assert.Equal(0, standard.Advance(Vector2.UnitX, .18f).BoostProgress);
        Assert.Equal(.5f, standard.Advance(Vector2.UnitX, .06f).BoostProgress, 5);
        Assert.Equal(1, standard.Advance(Vector2.UnitX, .06f).BoostProgress);
        GamepadLookSample reversed = standard.Advance(-Vector2.UnitX, .01f);
        Assert.Equal(0, reversed.BoostProgress);
        Assert.True(reversed.AngularVelocity.X > 0);

        var fast = CreateLook(GamepadResponseCurvePreset.Balanced,
            GamepadTurnAccelerationPreset.Fast);
        Assert.Equal(0, fast.Advance(Vector2.UnitX, .09f).BoostProgress);
        Assert.Equal(.5f, fast.Advance(Vector2.UnitX, .03f).BoostProgress, 5);

        var off = CreateLook(GamepadResponseCurvePreset.Balanced,
            GamepadTurnAccelerationPreset.Off);
        Assert.Equal(0, off.Advance(Vector2.UnitX, 1).BoostProgress);
    }

    [Fact]
    public void LookProfileKeepsFovAndUserZoomScalingSingleAndMultiplicative()
    {
        var processor = CreateLook(GamepadResponseCurvePreset.Linear,
            GamepadTurnAccelerationPreset.Off, zoomMultiplier: .5f);
        GamepadLookSample normal = processor.Evaluate(Vector2.UnitX);
        GamepadLookSample fovOnly = processor.Evaluate(Vector2.UnitX, .5f);
        GamepadLookSample zoomed = processor.Evaluate(Vector2.UnitX, .5f,
            zoomed: true);

        Assert.Equal(-300, normal.AngularVelocity.X, 3);
        Assert.Equal(-150, fovOnly.AngularVelocity.X, 3);
        Assert.Equal(-75, zoomed.AngularVelocity.X, 3);
    }

    [Fact]
    public void FlickStickChoosesHeadingThenTracksRimRotationWithoutPitch()
    {
        var flick = new FlickStickProcessor();

        FlickStickSample right = flick.Advance(Vector2.UnitX, 1f / 60f);
        Assert.Equal(-90, right.DeltaDegrees, 3);
        Assert.Equal(0, right.PredictionDegreesPerSecond);

        FlickStickSample back = flick.Advance(-Vector2.UnitY, 1f / 60f);
        Assert.Equal(-90, back.DeltaDegrees, 3);
        Assert.Equal(-5400, back.PredictionDegreesPerSecond, 1);

        flick.Advance(Vector2.Zero, 1f / 60f);
        Assert.Equal(180, flick.Advance(-Vector2.UnitY, 1f / 60f)
            .DeltaDegrees, 3);
    }

    [Fact]
    public void AutoCalibrationLearnsOnlyRestingInputAndKeepsDevicesIndependent()
    {
        var calibration = new ControllerStickCalibrationStore();
        ControllerStickCalibrationSample learned = default;
        for (int i = 0; i < 30; i++)
        {
            learned = calibration.Advance("pad-a", new Vector2(.05f, -.02f),
                1f / 60f, enabled: true, innerDeadzone: .1f,
                outerDeadzone: .02f);
        }

        Assert.InRange(learned.Value.Length, 0, .001f);
        Assert.Equal(new Vector2(.05f, -.02f), learned.LearnedCenter);

        ControllerStickCalibrationSample other = calibration.Evaluate("pad-b",
            new Vector2(.05f, -.02f), enabled: true,
            innerDeadzone: .1f, outerDeadzone: .02f);
        Assert.Equal(new Vector2(.05f, -.02f), other.Value);
        Assert.Equal(Vector2.Zero, other.LearnedCenter);

        ControllerStickCalibrationSample deliberate = calibration.Advance(
            "pad-a", new Vector2(.5f, 0), 1f / 60f, enabled: true,
            innerDeadzone: .1f, outerDeadzone: .02f);
        Assert.True(deliberate.Value.X > .44f);

        for (int i = 0; i < 15; i++)
        {
            deliberate = calibration.Advance("pad-a", new Vector2(.9f, -.02f),
                1f / 60f, enabled: true, innerDeadzone: .1f,
                outerDeadzone: .02f);
        }
        Assert.Equal(.85f, deliberate.LearnedMaximumMagnitude, 4);
        Assert.Equal(1f, deliberate.Value.X, 4);
    }

    [Fact]
    public void CalibrationProfilesRoundTripWithoutMergingDevices()
    {
        var source = new ControllerStickCalibrationStore();
        for (int i = 0; i < 30; i++)
        {
            source.Advance("pad-a", new Vector2(.04f, -.015f), 1f / 60f,
                enabled: true, innerDeadzone: .1f, outerDeadzone: .02f);
        }

        var restored = new ControllerStickCalibrationStore();
        restored.ImportProfiles(source.ExportProfiles());
        ControllerStickCalibrationSample padA = restored.Evaluate("pad-a",
            new Vector2(.04f, -.015f), enabled: true,
            innerDeadzone: .1f, outerDeadzone: .02f);
        ControllerStickCalibrationSample padB = restored.Evaluate("pad-b",
            new Vector2(.04f, -.015f), enabled: true,
            innerDeadzone: .1f, outerDeadzone: .02f);

        Assert.InRange(padA.Value.Length, 0, .001f);
        Assert.Equal(Vector2.Zero, padB.LearnedCenter);
        Assert.True(padB.Value.Length > .04f);
    }

    [Fact]
    public void CalibrationFileRoundTripsWithoutRuntimeJsonReflection()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "prime-controller-calibration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = new ControllerStickCalibrationStore();
            for (int i = 0; i < 30; i++)
            {
                source.Advance("pad,with-delimiter", new Vector2(.03f, -.01f),
                    1f / 60f, enabled: true, innerDeadzone: .1f,
                    outerDeadzone: .02f);
            }
            ControllerCalibrationPersistence.Save(directory, source);

            var restored = new ControllerStickCalibrationStore();
            ControllerCalibrationPersistence.Load(directory, restored);
            ControllerStickCalibrationSample sample = restored.Evaluate(
                "pad,with-delimiter", new Vector2(.03f, -.01f), enabled: true,
                innerDeadzone: .1f, outerDeadzone: .02f);

            Assert.InRange(sample.Value.Length, 0, .001f);
            Assert.Equal(new Vector2(.03f, -.01f), sample.LearnedCenter);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ControllerSettingsProfilesFollowStableDeviceIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "prime-controller-profile-" + Guid.NewGuid().ToString("N"));
        InputSettings.Reset();
        ControllerCalibrationPersistence.ResetForTests();
        try
        {
            var calibration = new ControllerStickCalibrationStore();
            ControllerCalibrationPersistence.Initialize(directory, calibration);
            ControllerCalibrationPersistence.Activate("pad-a");
            InputSettings.GamepadHorizontalSensitivity = 2;
            InputSettings.GamepadInvertY = true;

            ControllerCalibrationPersistence.Activate("pad-b");
            Assert.Equal(1, InputSettings.GamepadHorizontalSensitivity);
            Assert.False(InputSettings.GamepadInvertY);
            InputSettings.GamepadHorizontalSensitivity = 3;

            ControllerCalibrationPersistence.Activate("pad-a");
            Assert.Equal(2, InputSettings.GamepadHorizontalSensitivity);
            Assert.True(InputSettings.GamepadInvertY);
            ControllerCalibrationPersistence.Save(directory, calibration);

            ControllerCalibrationPersistence.ResetForTests();
            InputSettings.Reset();
            ControllerCalibrationPersistence.Initialize(directory,
                new ControllerStickCalibrationStore());
            ControllerCalibrationPersistence.Activate("pad-b");
            Assert.Equal(3, InputSettings.GamepadHorizontalSensitivity);
        }
        finally
        {
            ControllerCalibrationPersistence.ResetForTests();
            InputSettings.Reset();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AntiDeadzoneRaisesOnlyActiveLookOutput()
    {
        StickSample neutral = StickProcessor.Process(new Vector2(.1f, 0),
            .1f, .02f, 1.6f, .08f);
        StickSample active = StickProcessor.Process(new Vector2(.11f, 0),
            .1f, .02f, 1.6f, .08f);

        Assert.False(neutral.IsActive);
        Assert.InRange(active.ResponseMagnitude, .08f, .081f);
    }

    [Fact]
    public void AdaptiveSmoothingFiltersFineNoiseButReleasesAtFullSpeed()
    {
        var processor = new GamepadLookProcessor(smoothingSeconds: .05f);
        GamepadLookSample first = processor.Advance(new Vector2(.2f, 0),
            1f / 60f);
        GamepadLookSample fine = processor.Advance(new Vector2(.21f, 0),
            1f / 60f);
        GamepadLookSample fineTarget = new GamepadLookProcessor(
            smoothingSeconds: 0).Advance(new Vector2(.21f, 0), 1f / 60f);
        GamepadLookSample full = processor.Advance(Vector2.UnitX, 1f / 60f);

        Assert.InRange(fine.AngularVelocity.X,
            fineTarget.AngularVelocity.X, first.AngularVelocity.X);
        Assert.NotEqual(fineTarget.AngularVelocity.X,
            fine.AngularVelocity.X);
        Assert.InRange(MathF.Abs(full.AngularVelocity.X), 250, 300);
    }

    [Fact]
    public void CompetitivePresetsProvideFastTraditionalAndGyroFlickProfiles()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.ApplyPreset(ControllerPreset.Competitive);
            Assert.Equal(.07f, InputSettings.GamepadLookDeadZone);
            Assert.Equal(360, InputSettings.GamepadYawRate);
            Assert.Equal(GamepadTurnAccelerationPreset.Fast,
                InputSettings.GamepadTurnAcceleration);
            Assert.Equal(GamepadStickAimMode.Traditional,
                InputSettings.GamepadStickAimMode);
            Assert.False(InputSettings.GamepadGyroEnabled);

            InputSettings.ApplyPreset(ControllerPreset.GyroCompetitive);
            Assert.Equal(GamepadStickAimMode.FlickStick,
                InputSettings.GamepadStickAimMode);
            Assert.Equal(GamepadGyroMode.Always,
                InputSettings.GamepadGyroMode);
            Assert.Equal(1.10f,
                InputSettings.GamepadZoomHorizontalMultiplier);
            Assert.Equal(1f,
                InputSettings.GamepadZoomVerticalMultiplier);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void CompetitiveTuningDrillImprovesFineTurnResponseWithoutChangingAuthorityStep()
    {
        var baseline = new GamepadLookProcessor();
        Vector2 drillInput = new(.30f, 0);
        float baselineTurn = MathF.Abs(baseline.Advance(drillInput,
            1f / 60f).AngularVelocity.X);

        var competitive = new GamepadLookProcessor(innerDeadzone: .07f,
            exponent: 1.35f, yawRate: 360, pitchRate: 285,
            boostDelaySeconds: .09f, boostRampSeconds: .06f);
        GamepadLookSample sample = competitive.Advance(drillInput, 1f / 60f);

        Assert.True(sample.AngularVelocity.X < 0);
        Assert.True(MathF.Abs(sample.AngularVelocity.X) > baselineTurn * 1.5f);
        Assert.Equal(0, sample.BoostProgress);
    }

    private static GamepadLookProcessor CreateLook(
        GamepadResponseCurvePreset response,
        GamepadTurnAccelerationPreset acceleration,
        float customExponent = GamepadLookProcessor.DefaultExponent,
        float zoomMultiplier = 1)
    {
        GamepadTurnAccelerationProfile profile
            = GamepadLookProfiles.TurnAcceleration(acceleration);
        return new GamepadLookProcessor(
            exponent: GamepadLookProfiles.ResponseExponent(response, customExponent),
            outerBoostStart: profile.OuterBoostStart,
            outerYawBoost: profile.OuterYawBoost,
            outerPitchBoost: profile.OuterPitchBoost,
            boostDelaySeconds: profile.BoostDelaySeconds,
            boostRampSeconds: profile.BoostRampSeconds,
            outerBoostEnabled: profile.Enabled,
            zoomMultiplier: zoomMultiplier);
    }

    private static Vector2 At(float degrees)
    {
        float radians = degrees * MathF.PI / 180;
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians))
            * ActiveMagnitude;
    }

    private static GamepadMovementDirection DirectionAt(float degrees)
        => GamepadMovementProcessor.Quantize(At(degrees));
}
