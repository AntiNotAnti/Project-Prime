using System;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Converts SDL gyro angular velocity (radians/second, right-handed device
    /// X/Y/Z axes) into camera yaw/pitch degrees/second. Device Y drives yaw;
    /// device X drives pitch. Roll is intentionally ignored.
    /// </summary>
    public sealed class GyroLookProcessor
    {
        public const double DefaultStaleSeconds = 0.10;
        private const float RadiansToDegrees = 180f / MathF.PI;
        private Vector2 _angularVelocity;
        private double _lastSampleSeconds;
        private bool _hasSample;

        public Vector2 SubmitRadiansPerSecond(Vector3 value, double timestampSeconds,
            bool enabled, float sensitivity, bool invertX, bool invertY)
        {
            if (!enabled || !Finite(value) || !double.IsFinite(timestampSeconds)
                || timestampSeconds < 0)
            {
                Reset();
                return Vector2.Zero;
            }
            float scale = float.IsFinite(sensitivity)
                ? Math.Clamp(sensitivity, 0.01f, 10f) : 1f;
            float yaw = -value.Y * RadiansToDegrees * scale;
            float pitch = -value.X * RadiansToDegrees * scale;
            if (invertX) yaw = -yaw;
            if (invertY) pitch = -pitch;
            _angularVelocity = new Vector2(yaw, pitch);
            _lastSampleSeconds = timestampSeconds;
            _hasSample = true;
            return _angularVelocity;
        }

        public Vector2 Sample(double nowSeconds, bool enabled,
            double staleSeconds = DefaultStaleSeconds)
        {
            if (!enabled || !_hasSample || !double.IsFinite(nowSeconds)
                || nowSeconds < _lastSampleSeconds
                || nowSeconds - _lastSampleSeconds > Math.Clamp(staleSeconds, 0.01, 1))
            {
                Reset();
                return Vector2.Zero;
            }
            return _angularVelocity;
        }

        public void Reset()
        {
            _angularVelocity = Vector2.Zero;
            _lastSampleSeconds = 0;
            _hasSample = false;
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

        public static void Reset()
        {
            lock (Gate)
            {
                Processor.Reset();
                _lastSourceTimestamp = 0;
                _hasSourceTimestamp = false;
            }
        }

        private static double NowSeconds()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
}
