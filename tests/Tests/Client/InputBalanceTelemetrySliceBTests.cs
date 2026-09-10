using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class InputBalanceTelemetrySliceBTests
{
    [Fact]
    public void DisabledTelemetryDoesNotLatchFixedStepLook()
    {
        bool prior = InputSettings.InputBalanceTelemetryEnabled;
        try
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = false;

            InputBalanceTelemetry.BeginFixedStepLook(LookDeviceKind.GamepadStick);
            InputBalanceTelemetry.RecordFixedStepLook(LookDeviceKind.GamepadStick,
                new Vector2(2, -1), new Vector2(1.5f, -.5f), 3.25f, 2.5f);

            Assert.False(InputBalanceTelemetry.TryGetFixedStepLook(out _));
        }
        finally
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = prior;
        }
    }

    [Fact]
    public void FixedStepLookLatchSurvivesRenderOwnershipChanges()
    {
        bool prior = InputSettings.InputBalanceTelemetryEnabled;
        try
        {
            InputSettings.InputBalanceTelemetryEnabled = true;
            InputBalanceTelemetry.Reset();
            InputBalanceTelemetry.BeginFixedStepLook(LookDeviceKind.GamepadStick);
            InputBalanceTelemetry.RecordFixedStepLook(LookDeviceKind.GamepadStick,
                new Vector2(2, -1), new Vector2(1.5f, -.5f), 3.25f, 2.5f);

            Assert.Equal(LookDeviceKind.GamepadStick,
                InputBalanceTelemetry.FixedStepLookDevice);
            Assert.True(InputBalanceTelemetry.TryGetFixedStepLook(
                out FixedStepLookObservation value));
            Assert.Equal(new Vector2(2, -1), value.PreAssistDelta);
            Assert.Equal(new Vector2(1.5f, -.5f), value.PostAssistDelta);
            Assert.Equal(3.25f, value.PreAssistAngularErrorDegrees);
            Assert.Equal(2.5f, value.PostAssistAngularErrorDegrees);
        }
        finally
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = prior;
        }
    }

    [Fact]
    public void FiringTargetMeasurementsAndHistogramsAreBoundedAndExplicit()
    {
        bool prior = InputSettings.InputBalanceTelemetryEnabled;
        try
        {
            InputSettings.InputBalanceTelemetryEnabled = true;
            InputBalanceTelemetry.Reset();
            AimAssistTargetObservation targets = new(
                HasNearestEnemy: true, NearestEnemyErrorDegrees: 4.25f,
                HasRetainedAssistTarget: false,
                RetainedAssistTargetErrorDegrees: float.NaN);
            InputBalanceTelemetry.RecordShot(LookDeviceKind.GamepadStick,
                1, 2, zoomed: true, targets: targets, commandSequence: 8,
                fixedStepLook: new FixedStepLookObservation(
                    LookDeviceKind.GamepadStick, new Vector2(2, -1),
                    new Vector2(1.5f, -.5f), 3.25f, 2.5f));
            InputBalanceTelemetry.RecordAssist(LookDeviceKind.GamepadStick,
                1, 2, acquisitionMilliseconds: 250,
                rotationalDegrees: 2, frictionMultiplier: .8f,
                acquiredTarget: true);
            InputBalanceTelemetry.RecordShot(LookDeviceKind.GamepadStick,
                1, 2, zoomed: false,
                targets: AimAssistTargetObservation.NoTargets);

            Assert.True(InputBalanceTelemetry.TrySnapshot(
                LookDeviceKind.GamepadStick, 1, 2, out InputBalanceSnapshot value));
            Assert.Equal(2u, value.Shots);
            Assert.Equal(1u, value.NoTargetShots);
            Assert.Equal(2u, value.NoRetainedAssistTargetShots);
            Assert.Equal(InputBalanceTelemetry.TargetErrorHistogramBinCount,
                value.TargetErrorHistogram.Length);
            Assert.Equal(1u, value.TargetErrorHistogram[4]);
            Assert.Equal(1u, value.AcquisitionHistogram[2]);
            Assert.Equal(1u, value.RotationHistogram[4]);
            Assert.True(value.HasFixedStepLook);
            Assert.Equal(new Vector2(2, -1), value.LastPreAssistDelta);
            Assert.Equal(new Vector2(1.5f, -.5f), value.LastPostAssistDelta);
            Assert.Equal(3.25, value.AveragePreAssistAngularError, 5);
            Assert.Equal(2.5, value.AveragePostAssistAngularError, 5);
            Assert.Equal(1u, value.PreAssistErrorHistogram[3]);
            Assert.Equal(1u, value.PostAssistErrorHistogram[2]);
        }
        finally
        {
            InputBalanceTelemetry.Reset();
            InputSettings.InputBalanceTelemetryEnabled = prior;
        }
    }
}
