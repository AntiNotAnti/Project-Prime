using System;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public enum GyroCalibrationState
    {
        Calibrating,
        Ready
    }

    public readonly record struct GyroConditioningStatus(
        GyroCalibrationState CalibrationState,
        Vector3 BiasRadiansPerSecond,
        int CalibrationSamples,
        int MotionRestarts,
        float NoiseFloorDegreesPerSecond,
        float SmoothingFactor,
        bool HasFreshOutput);

    public readonly record struct GamepadGyroStatus(
        GyroConditioningStatus Conditioning,
        bool HasSourceTimestamp,
        double LastSourceTimestamp);

    /// <summary>
    /// Conditions unscaled SDL angular velocity before it can claim look
    /// ownership. Stationary bias is learned first; calibration and noise
    /// samples always publish zero. Device Y drives yaw, device X drives pitch,
    /// and roll is ignored after participating in motion rejection.
    /// </summary>
    public sealed class GyroLookProcessor
    {
        public const double DefaultStaleSeconds = 0.10;
        public const double CalibrationDurationSeconds = 0.50;
        public const int MinimumCalibrationSamples = 30;
        public const float DefaultNoiseFloorDegreesPerSecond = 0.75f;
        public const float CalibrationMotionThresholdDegreesPerSecond = 5f;
        public const float MaximumSmoothingFactor = 0.50f;

        private const float RadiansToDegrees = 180f / MathF.PI;
        private const float CalibrationMotionThresholdRadiansPerSecond
            = CalibrationMotionThresholdDegreesPerSecond / RadiansToDegrees;

        private Vector2 _angularVelocity;
        private Vector2 _smoothedVelocity;
        private Vector3 _calibrationSum;
        private Vector3 _bias;
        private double _calibrationStartSeconds;
        private double _lastSampleSeconds;
        private int _calibrationSamples;
        private int _motionRestarts;
        private bool _calibrationStarted;
        private bool _calibrated;
        private bool _hasSample;
        private bool _hasSmoothedVelocity;

        public GyroLookProcessor(
            float noiseFloorDegreesPerSecond = DefaultNoiseFloorDegreesPerSecond,
            float smoothingFactor = 0)
        {
            ConfigureConditioning(noiseFloorDegreesPerSecond, smoothingFactor);
        }

        public float NoiseFloorDegreesPerSecond { get; private set; }
        public float SmoothingFactor { get; private set; }

        public GyroConditioningStatus Status => new(
            _calibrated ? GyroCalibrationState.Ready : GyroCalibrationState.Calibrating,
            _bias, _calibrationSamples, _motionRestarts,
            NoiseFloorDegreesPerSecond, SmoothingFactor,
            _calibrated && _hasSample && _angularVelocity != Vector2.Zero);

        /// <summary>
        /// Configure internal conditioning. Zero smoothing is the default raw
        /// path; changing either value does not discard a learned bias.
        /// </summary>
        public void ConfigureConditioning(float noiseFloorDegreesPerSecond,
            float smoothingFactor = 0)
        {
            NoiseFloorDegreesPerSecond = float.IsFinite(noiseFloorDegreesPerSecond)
                ? Math.Clamp(noiseFloorDegreesPerSecond, 0, 10)
                : DefaultNoiseFloorDegreesPerSecond;
            SmoothingFactor = float.IsFinite(smoothingFactor)
                ? Math.Clamp(smoothingFactor, 0, MaximumSmoothingFactor) : 0;
            ClearOutput();
        }

        public Vector2 SubmitRadiansPerSecond(Vector3 value, double timestampSeconds,
            bool enabled, float sensitivity, bool invertX, bool invertY)
        {
            if (!enabled)
            {
                SuppressOutput();
                return Vector2.Zero;
            }
            if (!Finite(value) || !double.IsFinite(timestampSeconds)
                || timestampSeconds < 0)
            {
                if (!_calibrated) RestartCalibrationForRejectedMotion();
                SuppressOutput();
                return Vector2.Zero;
            }

            _lastSampleSeconds = timestampSeconds;
            _hasSample = true;
            if (!_calibrated)
            {
                Calibrate(value, timestampSeconds);
                _angularVelocity = Vector2.Zero;
                return Vector2.Zero;
            }

            Vector3 corrected = value - _bias;
            Vector2 rawDegrees = new(-corrected.Y * RadiansToDegrees,
                -corrected.X * RadiansToDegrees);
            if (!float.IsFinite(rawDegrees.X) || !float.IsFinite(rawDegrees.Y)
                || rawDegrees.Length <= NoiseFloorDegreesPerSecond)
            {
                ClearOutput(keepSampleTimestamp: true);
                return Vector2.Zero;
            }

            Vector2 conditioned = rawDegrees;
            if (SmoothingFactor > 0 && _hasSmoothedVelocity)
            {
                conditioned = _smoothedVelocity
                    + (rawDegrees - _smoothedVelocity) * (1 - SmoothingFactor);
            }
            _smoothedVelocity = conditioned;
            _hasSmoothedVelocity = true;

            float scale = float.IsFinite(sensitivity)
                ? Math.Clamp(sensitivity, 0.01f, 10f) : 1f;
            float yaw = conditioned.X * scale;
            float pitch = conditioned.Y * scale;
            if (invertX) yaw = -yaw;
            if (invertY) pitch = -pitch;
            _angularVelocity = new Vector2(yaw, pitch);
            return _angularVelocity;
        }

        public Vector2 Sample(double nowSeconds, bool enabled,
            double staleSeconds = DefaultStaleSeconds)
        {
            if (!enabled || !_calibrated || !_hasSample
                || !double.IsFinite(nowSeconds) || nowSeconds < _lastSampleSeconds
                || nowSeconds - _lastSampleSeconds
                    > Math.Clamp(staleSeconds, 0.01, 1))
            {
                SuppressOutput();
                return Vector2.Zero;
            }
            return _angularVelocity;
        }

        /// <summary>
        /// Drop live output without discarding bias or calibration progress.
        /// Use for temporary mode, zoom, radial, pause, or ownership changes.
        /// </summary>
        public void SuppressOutput() => ClearOutput();

        /// <summary>Restart bias acquisition while retaining conditioning configuration.</summary>
        public void Recalibrate()
        {
            _calibrationSum = Vector3.Zero;
            _bias = Vector3.Zero;
            _calibrationStartSeconds = 0;
            _calibrationSamples = 0;
            _calibrationStarted = false;
            _calibrated = false;
            ClearOutput();
        }

        /// <summary>Reset all device-specific state for disconnect/reconnect.</summary>
        public void ResetDevice()
        {
            _motionRestarts = 0;
            Recalibrate();
        }

        /// <summary>Compatibility spelling for a full processor reset.</summary>
        public void Reset() => ResetDevice();

        private void Calibrate(Vector3 value, double timestampSeconds)
        {
            if (value.Length > CalibrationMotionThresholdRadiansPerSecond)
            {
                RestartCalibrationForRejectedMotion();
                return;
            }
            if (!_calibrationStarted)
            {
                _calibrationStarted = true;
                _calibrationStartSeconds = timestampSeconds;
            }
            else if (timestampSeconds < _calibrationStartSeconds)
            {
                RestartCalibrationForRejectedMotion();
                return;
            }
            _calibrationSum += value;
            _calibrationSamples++;
            if (_calibrationSamples >= MinimumCalibrationSamples
                && timestampSeconds - _calibrationStartSeconds
                    >= CalibrationDurationSeconds)
            {
                _bias = _calibrationSum / _calibrationSamples;
                _calibrated = true;
            }
        }

        private void RestartCalibrationForRejectedMotion()
        {
            _calibrationSum = Vector3.Zero;
            _calibrationStartSeconds = 0;
            _calibrationSamples = 0;
            _calibrationStarted = false;
            _motionRestarts++;
        }

        private void ClearOutput(bool keepSampleTimestamp = false)
        {
            _angularVelocity = Vector2.Zero;
            _smoothedVelocity = Vector2.Zero;
            _hasSmoothedVelocity = false;
            if (!keepSampleTimestamp)
            {
                _lastSampleSeconds = 0;
                _hasSample = false;
            }
        }

        private static bool Finite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }

    public static class GamepadGyro
    {
        private static readonly object Gate = new();
        private static readonly GyroLookProcessor Processor = new();
        private static double _lastSourceTimestamp;
        private static bool _hasSourceTimestamp;

        public static GamepadGyroStatus Status
        {
            get
            {
                lock (Gate)
                    return new GamepadGyroStatus(Processor.Status,
                        _hasSourceTimestamp, _lastSourceTimestamp);
            }
        }

        public static void ConfigureConditioning(float noiseFloorDegreesPerSecond,
            float smoothingFactor = 0)
        {
            lock (Gate)
                Processor.ConfigureConditioning(noiseFloorDegreesPerSecond,
                    smoothingFactor);
        }

        public static void SubmitRadiansPerSecond(Vector3 value, double timestampSeconds)
        {
            lock (Gate)
            {
                // SDL's sensor clock is monotonic but has a different epoch
                // from Stopwatch. Use it to reject reordered samples, then
                // stamp arrival in the same clock used by stale checks.
                if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0
                    || _hasSourceTimestamp && timestampSeconds <= _lastSourceTimestamp)
                    return;
                _lastSourceTimestamp = timestampSeconds;
                _hasSourceTimestamp = true;
                Processor.SubmitRadiansPerSecond(value, NowSeconds(),
                    InputSettings.GamepadGyroEnabled,
                    InputSettings.GamepadGyroSensitivity,
                    InputSettings.GamepadGyroInvertX,
                    InputSettings.GamepadGyroInvertY);
            }
        }

        public static Vector2 Sample(double nowSeconds)
        {
            lock (Gate)
                return Processor.Sample(nowSeconds, InputSettings.GamepadGyroEnabled);
        }

        /// <summary>Suppress output without restarting calibration.</summary>
        public static void SuppressOutput()
        {
            lock (Gate) Processor.SuppressOutput();
        }

        /// <summary>
        /// Compatibility hook for existing transient lifecycle fences. Bias and
        /// in-progress calibration intentionally survive.
        /// </summary>
        public static void Reset() => SuppressOutput();

        public static void Recalibrate()
        {
            lock (Gate) Processor.Recalibrate();
        }

        public static void ResetDevice()
        {
            lock (Gate)
            {
                Processor.ResetDevice();
                _lastSourceTimestamp = 0;
                _hasSourceTimestamp = false;
            }
        }

        private static double NowSeconds()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
}
