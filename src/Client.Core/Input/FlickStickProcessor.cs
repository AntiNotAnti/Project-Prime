using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public readonly record struct FlickStickSample(
        float DeltaDegrees, float PredictionDegreesPerSecond, float Magnitude)
    {
        public bool IsActive => DeltaDegrees != 0;
    }

    /// <summary>
    /// Optional flick-stick yaw. Deflection chooses an absolute heading
    /// relative to the current view; rotation around the rim then turns by the
    /// same angular change. Pitch remains owned by gyro.
    /// </summary>
    public struct FlickStickProcessor
    {
        public const float ActivationThreshold = .75f;
        public const float ReleaseThreshold = .55f;

        private bool _active;
        private float _previousAngle;

        public FlickStickSample Advance(Vector2 raw, float deltaSeconds)
        {
            if (!IsFinite(raw) || !float.IsFinite(deltaSeconds)
                || deltaSeconds <= 0)
            {
                Reset();
                return default;
            }
            float magnitude = raw.Length;
            if (magnitude < ReleaseThreshold)
            {
                Reset();
                return default;
            }
            float angle = Angle(raw);
            if (!_active)
            {
                if (magnitude < ActivationThreshold) return default;
                _active = true;
                _previousAngle = angle;
                return new(Wrap(angle), 0, MathF.Min(magnitude, 1));
            }
            float delta = Wrap(angle - _previousAngle);
            _previousAngle = angle;
            return new(delta, delta / deltaSeconds, MathF.Min(magnitude, 1));
        }

        public readonly FlickStickSample Evaluate(Vector2 raw,
            float fixedDeltaSeconds)
        {
            if (!_active || !IsFinite(raw) || raw.Length < ReleaseThreshold
                || !float.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0)
            {
                return default;
            }
            float delta = Wrap(Angle(raw) - _previousAngle);
            return new(0, delta / fixedDeltaSeconds,
                MathF.Min(raw.Length, 1));
        }

        public void Reset()
        {
            _active = false;
            _previousAngle = 0;
        }

        internal static float Angle(Vector2 raw)
            => MathHelper.RadiansToDegrees(MathF.Atan2(-raw.X, raw.Y));

        internal static float Wrap(float degrees)
        {
            if (!float.IsFinite(degrees)) return 0;
            degrees %= 360;
            if (degrees > 180) degrees -= 360;
            if (degrees <= -180) degrees += 360;
            return degrees;
        }

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
