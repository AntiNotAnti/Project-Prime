using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public readonly record struct ControllerStickCalibrationSample(
        Vector2 Value, float InnerDeadzone, float OuterDeadzone,
        Vector2 LearnedCenter, float LearnedNoise,
        float LearnedMaximumMagnitude = 0);

    public sealed record ControllerStickCalibrationProfile(
        string DeviceId, float CenterX, float CenterY, float Noise, int Samples,
        float MaximumMagnitude = 0);

    /// <summary>
    /// Conservative calibration keyed by the platform's stable controller
    /// identity. The presentation persists bounded profiles between sessions.
    /// Only samples already inside the configured dead
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

        public IReadOnlyList<ControllerStickCalibrationProfile> ExportProfiles()
        {
            lock (_gate)
            {
                return _profileOrder
                    .Where(key => _profiles.ContainsKey(key))
                    .Select(key => _profiles[key].Export(key))
                    .ToArray();
            }
        }

        public void ImportProfiles(
            IEnumerable<ControllerStickCalibrationProfile>? profiles)
        {
            if (profiles == null) return;
            lock (_gate)
            {
                _profiles.Clear();
                _profileOrder.Clear();
                foreach (ControllerStickCalibrationProfile saved in profiles)
                {
                    if (_profiles.Count >= MaximumProfiles) break;
                    if (String.IsNullOrWhiteSpace(saved.DeviceId)
                        || saved.DeviceId.Length > 256
                        || !float.IsFinite(saved.CenterX)
                        || !float.IsFinite(saved.CenterY)
                        || !float.IsFinite(saved.Noise)
                        || saved.Samples <= 0)
                    {
                        continue;
                    }
                    string key = DeviceKey(saved.DeviceId);
                    if (_profiles.ContainsKey(key)) continue;
                    var profile = new Profile(saved);
                    _profiles.Add(key, profile);
                    _profileOrder.Enqueue(key);
                }
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
            private float _outerSeconds;
            private float _maximumMagnitude;
            private int _samples;

            internal Profile()
            {
            }

            internal Profile(ControllerStickCalibrationProfile saved)
            {
                Vector2 center = new(saved.CenterX, saved.CenterY);
                _center = center.Length <= .25f ? center : Vector2.Zero;
                _noise = Math.Clamp(saved.Noise, 0, .25f);
                _samples = Math.Clamp(saved.Samples, 1, 1_000_000);
                _maximumMagnitude = saved.MaximumMagnitude is >= .85f and <= 1
                    ? saved.MaximumMagnitude : 0;
            }

            internal ControllerStickCalibrationProfile Export(string deviceId)
                => new(deviceId, _center.X, _center.Y, _noise, _samples,
                    _maximumMagnitude);

            internal void Observe(Vector2 raw, float deltaSeconds,
                float configuredInnerDeadzone)
            {
                if (!IsFinite(raw) || !float.IsFinite(deltaSeconds)
                    || deltaSeconds <= 0)
                {
                    return;
                }
                float rawMagnitude = raw.Length;
                float travelMagnitude = (raw - _center).Length;
                if (travelMagnitude >= .8f)
                {
                    _outerSeconds = Math.Clamp(_outerSeconds + deltaSeconds, 0, 60);
                    if (_outerSeconds >= .2f)
                    {
                        // A sustained outer-ring sample is a much stronger
                        // full-travel signal than a transient flick. Correction
                        // is capped so a partial hold cannot inflate input by
                        // more than a conservative amount.
                        _maximumMagnitude = MathF.Max(_maximumMagnitude,
                            Math.Clamp(travelMagnitude, .85f, 1));
                    }
                }
                else
                {
                    _outerSeconds = 0;
                }
                float learnRadius = Math.Clamp(configuredInnerDeadzone * .9f,
                    .035f, .12f);
                if (rawMagnitude > learnRadius)
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
                        Vector2.Zero, 0, 0);
                }
                Vector2 adjusted = raw - _center;
                adjusted /= _maximumMagnitude >= .85f
                    ? _maximumMagnitude : 1;
                float magnitude = adjusted.Length;
                if (magnitude > 1) adjusted /= magnitude;
                float effectiveInner = Math.Clamp(
                    MathF.Max(innerDeadzone, _noise + NoiseMargin), 0, .9f);
                return new(adjusted, effectiveInner, outerDeadzone,
                    _center, _noise, _maximumMagnitude);
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
