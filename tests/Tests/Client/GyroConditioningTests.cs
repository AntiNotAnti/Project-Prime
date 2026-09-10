using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class GyroConditioningTests
{
    private static readonly Vector3 Bias = new(.01f, -.008f, .002f);

    [Fact]
    public void StationaryBiasAndNoiseAreRemovedBeforeDeliberateMotion()
    {
        var processor = new GyroLookProcessor();
        Calibrate(processor, Bias);

        GyroConditioningStatus status = processor.Status;
        Assert.Equal(GyroCalibrationState.Ready, status.CalibrationState);
        AssertVector(Bias, status.BiasRadiansPerSecond, .00001f);
        Assert.InRange(status.NoiseFloorDegreesPerSecond, .5f, 1f);
        Assert.Equal(0, status.SmoothingFactor);

        Assert.Equal(Vector2.Zero, processor.SubmitRadiansPerSecond(
            Bias + new Vector3(.003f, .002f, 0), .52, true, 1, false, false));
        Vector2 deliberate = processor.SubmitRadiansPerSecond(
            Bias + new Vector3(.1f, -.2f, 0), .54, true, 1, false, false);
        Assert.InRange(deliberate.X, 11.45f, 11.47f);
        Assert.InRange(deliberate.Y, -5.74f, -5.72f);
    }

    [Fact]
    public void MotionDuringCalibrationRejectsWindowAndRestartsStationaryCount()
    {
        var processor = new GyroLookProcessor();
        for (int i = 0; i < 20; i++)
            Assert.Equal(Vector2.Zero, Submit(processor, Bias, i / 60d));

        Assert.Equal(Vector2.Zero, Submit(processor, new Vector3(.2f, 0, 0), .34));
        Assert.Equal(GyroCalibrationState.Calibrating, processor.Status.CalibrationState);
        Assert.Equal(0, processor.Status.CalibrationSamples);
        Assert.Equal(1, processor.Status.MotionRestarts);

        for (int i = 0; i <= 30; i++)
            Assert.Equal(Vector2.Zero, Submit(processor, Bias, .40 + i / 60d));
        Assert.Equal(GyroCalibrationState.Ready, processor.Status.CalibrationState);
        Assert.Equal(31, processor.Status.CalibrationSamples);
    }

    [Fact]
    public void FullDeviceResetRequiresFreshCalibrationForReconnect()
    {
        var processor = new GyroLookProcessor();
        Calibrate(processor, Bias);
        Assert.NotEqual(Vector2.Zero, Submit(processor,
            Bias + new Vector3(0, -.1f, 0), .52));

        processor.ResetDevice();

        Assert.Equal(GyroCalibrationState.Calibrating, processor.Status.CalibrationState);
        Assert.Equal(0, processor.Status.CalibrationSamples);
        Assert.Equal(Vector3.Zero, processor.Status.BiasRadiansPerSecond);
        Assert.Equal(Vector2.Zero, processor.Sample(.53, enabled: true));
    }

    [Fact]
    public void ExplicitRecalibrationPreservesConditioningConfiguration()
    {
        var processor = new GyroLookProcessor(.6f, .25f);
        Calibrate(processor, Bias);

        processor.Recalibrate();

        Assert.Equal(GyroCalibrationState.Calibrating, processor.Status.CalibrationState);
        Assert.Equal(0, processor.Status.CalibrationSamples);
        Assert.Equal(.6f, processor.Status.NoiseFloorDegreesPerSecond);
        Assert.Equal(.25f, processor.Status.SmoothingFactor);
        Assert.Equal(Vector2.Zero, Submit(processor,
            Bias + new Vector3(0, -.1f, 0), .6));
    }

    [Fact]
    public void StaleOutputExpiresWithoutDiscardingCalibration()
    {
        var processor = new GyroLookProcessor();
        Calibrate(processor, Bias);
        Assert.NotEqual(Vector2.Zero, Submit(processor,
            Bias + new Vector3(0, -.1f, 0), .52));

        Assert.Equal(Vector2.Zero, processor.Sample(.63, enabled: true));
        Assert.Equal(GyroCalibrationState.Ready, processor.Status.CalibrationState);
        Assert.Equal(Vector2.Zero, processor.Sample(.64, enabled: true));
    }

    [Fact]
    public void ReorderedSourceSampleIsRejectedWithoutChangingPipelineState()
    {
        bool previousEnabled = InputSettings.GamepadGyroEnabled;
        try
        {
            InputSettings.GamepadGyroEnabled = true;
            GamepadGyro.ResetDevice();
            GamepadGyro.SubmitRadiansPerSecond(Bias, 10);
            GamepadGyroStatus accepted = GamepadGyro.Status;

            GamepadGyro.SubmitRadiansPerSecond(Vector3.Zero, 9);
            GamepadGyroStatus rejected = GamepadGyro.Status;

            Assert.True(rejected.HasSourceTimestamp);
            Assert.Equal(10, rejected.LastSourceTimestamp);
            Assert.Equal(accepted.Conditioning.CalibrationSamples,
                rejected.Conditioning.CalibrationSamples);
        }
        finally
        {
            GamepadGyro.ResetDevice();
            InputSettings.GamepadGyroEnabled = previousEnabled;
        }
    }

    [Fact]
    public void TemporarySuppressionPreservesLearnedCalibration()
    {
        var processor = new GyroLookProcessor();
        Calibrate(processor, Bias);
        Assert.NotEqual(Vector2.Zero, Submit(processor,
            Bias + new Vector3(0, -.1f, 0), .52));

        processor.SuppressOutput();

        Assert.Equal(GyroCalibrationState.Ready, processor.Status.CalibrationState);
        AssertVector(Bias, processor.Status.BiasRadiansPerSecond, .00001f);
        Assert.Equal(Vector2.Zero, processor.Sample(.53, enabled: true));
        Assert.NotEqual(Vector2.Zero, Submit(processor,
            Bias + new Vector3(0, -.1f, 0), .54));
    }

    [Fact]
    public void OptionalSmoothingIsDisabledByDefaultAndLightWhenConfigured()
    {
        var raw = new GyroLookProcessor(noiseFloorDegreesPerSecond: 0);
        var smooth = new GyroLookProcessor(noiseFloorDegreesPerSecond: 0,
            smoothingFactor: .25f);
        Calibrate(raw, Vector3.Zero);
        Calibrate(smooth, Vector3.Zero);

        Submit(raw, new Vector3(0, -.1f, 0), .52);
        Submit(smooth, new Vector3(0, -.1f, 0), .52);
        Vector2 rawSecond = Submit(raw, new Vector3(0, -.2f, 0), .54);
        Vector2 smoothSecond = Submit(smooth, new Vector3(0, -.2f, 0), .54);

        Assert.InRange(rawSecond.X, 11.45f, 11.47f);
        Assert.InRange(smoothSecond.X, 10.02f, 10.04f);
        Assert.True(smoothSecond.X < rawSecond.X);
    }

    private static void Calibrate(GyroLookProcessor processor, Vector3 bias)
    {
        for (int i = 0; i <= 30; i++)
            Assert.Equal(Vector2.Zero, Submit(processor, bias, i / 60d));
        Assert.Equal(GyroCalibrationState.Ready, processor.Status.CalibrationState);
    }

    private static Vector2 Submit(GyroLookProcessor processor, Vector3 value,
        double timestamp) => processor.SubmitRadiansPerSecond(value, timestamp,
            enabled: true, sensitivity: 1, invertX: false, invertY: false);

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }
}
