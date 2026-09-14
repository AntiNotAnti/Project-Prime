using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public readonly record struct ControllerStickCalibrationSample(
        Vector2 Value, float InnerDeadzone, float OuterDeadzone,
        Vector2 LearnedCenter, float LearnedNoise);

    /// <summary>
    /// Conservative, session-local calibration keyed by the platform's stable
    /// controller identity. Only samples already inside the configured dead
    /// zone are allowed to teach the resting center, so deliberate playable
    /// input cannot be learned away. Evaluate is side-effect free for render
    /// sampling; Advance is the sole fixed-step mutation path.
    /// </summary>
    public sealed class ControllerStickCalibrationStore
    {
        private const int MaximumProfiles = 8;
        private const float NoiseMargin = 0.0125f;
        private readonly object _gate = new();
        private readonly Dictionary<string, Profile> _profiles = new(
            StringComparer.Ordinal);
        private readonly Queue<string> _profileOrder = new();

        public ControllerStickCalibrationSample Advance(string? deviceId,
            Vector2 raw, float deltaSeconds, bool enabled,
            float innerDeadzone, float outerDeadzone)
        {
            lock (_gate)
            {
                Profile profile = GetProfile(DeviceKey(deviceId));
                if (enabled)
                {
                    profile.Observe(raw, deltaSeconds, innerDeadzone);
                }
                return profile.Apply(raw, enabled, innerDeadzone, outerDeadzone);
            }
        }

        public ControllerStickCalibrationSample Evaluate(string? deviceId,
            Vector2 raw, bool enabled, float innerDeadzone, float outerDeadzone)
        {
            lock (_gate)
            {
                return GetProfile(DeviceKey(deviceId)).Apply(raw, enabled,
                    innerDeadzone, outerDeadzone);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _profiles.Clear();
                _profileOrder.Clear();
            }
        }

        private Profile GetProfile(string key)
        {
            if (_profiles.TryGetValue(key, out Profile? profile)) return profile;
            if (_profiles.Count >= MaximumProfiles)
            {
                string oldest = _profileOrder.Dequeue();
                _profiles.Remove(oldest);
            }
            profile = new Profile();
            _profiles.Add(key, profile);
            _profileOrder.Enqueue(key);
            return profile;
        }

        private static string DeviceKey(string? deviceId)
            => String.IsNullOrWhiteSpace(deviceId) ? "active-controller" : deviceId;

        private sealed class Profile
        {
            private Vector2 _center;
            private float _noise;
            private float _restSeconds;
            private int _samples;

            internal void Observe(Vector2 raw, float deltaSeconds,
                float configuredInnerDeadzone)
            {
                if (!IsFinite(raw) || !float.IsFinite(deltaSeconds)
                    || deltaSeconds <= 0)
                {
                    return;
                }
                float learnRadius = Math.Clamp(configuredInnerDeadzone * .9f,
                    .035f, .12f);
                if (raw.Length > learnRadius)
                {
                    _restSeconds = 0;
                    return;
                }
                _restSeconds = Math.Clamp(_restSeconds + deltaSeconds, 0, 60);
                if (_restSeconds < .25f) return;

                _samples++;
                float weight = _samples <= 60 ? 1f / _samples : .015f;
                _center += (raw - _center) * weight;
                float residual = (raw - _center).Length;
                _noise = MathF.Max(residual, _noise * .9975f);
            }

            internal ControllerStickCalibrationSample Apply(Vector2 raw,
                bool enabled, float innerDeadzone, float outerDeadzone)
            {
                innerDeadzone = SanitizeDeadzone(innerDeadzone, .1f, .9f);
                outerDeadzone = SanitizeDeadzone(outerDeadzone, .02f,
                    1 - innerDeadzone);
                if (!IsFinite(raw)) raw = Vector2.Zero;
                if (!enabled || _samples == 0)
                {
                    return new(raw, innerDeadzone, outerDeadzone,
                        Vector2.Zero, 0);
                }
                Vector2 adjusted = raw - _center;
                float magnitude = adjusted.Length;
                if (magnitude > 1) adjusted /= magnitude;
                float effectiveInner = Math.Clamp(
                    MathF.Max(innerDeadzone, _noise + NoiseMargin), 0, .9f);
                return new(adjusted, effectiveInner, outerDeadzone,
                    _center, _noise);
            }
        }

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static float SanitizeDeadzone(float value, float fallback,
            float maximum)
            => !float.IsFinite(value) ? fallback
                : Math.Clamp(value, 0, Math.Clamp(maximum, 0, .99f));
    }
}
