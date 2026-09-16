using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>A small value returned by the radial stick processor.</summary>
    public readonly struct StickSample
    {
        public StickSample(Vector2 direction, Vector2 vector, float rawMagnitude,
            float magnitude, float responseMagnitude)
        {
            Direction = direction;
            Vector = vector;
            RawMagnitude = rawMagnitude;
            Magnitude = magnitude;
            ResponseMagnitude = responseMagnitude;
        }

        /// <summary>Unit direction, or zero while inside the inner deadzone.</summary>
        public Vector2 Direction { get; }

        /// <summary>Direction multiplied by the linear processed magnitude.</summary>
        public Vector2 Vector { get; }

        public float RawMagnitude { get; }
        public float Magnitude { get; }
        public float ResponseMagnitude { get; }
        public bool IsActive => Magnitude > 0;
    }

    /// <summary>
    /// Radial inner/outer deadzone and response processing shared by movement
    /// and look.  It is a struct so a fixed-step input path does not allocate.
    /// </summary>
    public readonly struct StickProcessor
    {
        public const float DefaultInnerDeadzone = 0.10f;
        public const float DefaultOuterDeadzone = 0.02f;
        public const float DefaultExponent = 1.60f;

        public StickProcessor(float innerDeadzone = DefaultInnerDeadzone,
            float outerDeadzone = DefaultOuterDeadzone, float exponent = 1,
            float antiDeadzone = 0)
        {
            InnerDeadzone = SanitizeDeadzone(innerDeadzone, 0);
            OuterDeadzone = SanitizeDeadzone(outerDeadzone, 0,
                1 - InnerDeadzone);
            Exponent = SanitizeExponent(exponent);
            AntiDeadzone = SanitizeAntiDeadzone(antiDeadzone);
        }

        public float InnerDeadzone { get; }
        public float OuterDeadzone { get; }
        public float Exponent { get; }
        public float AntiDeadzone { get; }

        public StickSample Process(Vector2 raw)
            => Process(raw, InnerDeadzone, OuterDeadzone, Exponent, AntiDeadzone);

        /// <summary>
        /// Process a raw -1..1 pair.  The outer deadzone maps the last usable
        /// ring to one, so a worn stick can still reach full turn speed.
        /// </summary>
        public static StickSample Process(Vector2 raw, float innerDeadzone,
            float outerDeadzone, float exponent = 1, float antiDeadzone = 0)
        {
            if (!IsFinite(raw))
            {
                return default;
            }

            float inner = SanitizeDeadzone(innerDeadzone, 0);
            float outer = SanitizeDeadzone(outerDeadzone, 0, 1 - inner);
            float curve = SanitizeExponent(exponent);
            float minimumOutput = SanitizeAntiDeadzone(antiDeadzone);
            float rawMagnitude = raw.Length;
            if (!float.IsFinite(rawMagnitude) || rawMagnitude <= inner || rawMagnitude <= 0)
            {
                return default;
            }

            float usableRange = MathF.Max(1e-6f, 1 - inner - outer);
            float magnitude = Math.Clamp((rawMagnitude - inner) / usableRange, 0, 1);
            Vector2 direction = raw / rawMagnitude;
            float curved = MathF.Pow(magnitude, curve);
            float response = curved <= 0 ? 0
                : minimumOutput + (1 - minimumOutput) * curved;
            if (!float.IsFinite(response))
            {
                return default;
            }
            return new StickSample(direction, direction * magnitude,
                MathF.Min(rawMagnitude, 1), magnitude, response);
        }

        /// <summary>Evaluate with the linear response while retaining direction.</summary>
        public static StickSample Evaluate(Vector2 raw, float innerDeadzone,
            float outerDeadzone)
            => Process(raw, innerDeadzone, outerDeadzone, 1);

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static float SanitizeDeadzone(float value, float fallback,
            float maximum = 0.99f)
        {
            if (!float.IsFinite(value))
            {
                return fallback;
            }
            return Math.Clamp(value, 0, Math.Clamp(maximum, 0, 0.99f));
        }

        private static float SanitizeExponent(float value)
            => !float.IsFinite(value) || value <= 0 ? 1 : Math.Clamp(value, 0.05f, 8);

        private static float SanitizeAntiDeadzone(float value)
            => !float.IsFinite(value) ? 0 : Math.Clamp(value, 0, .5f);
    }
}
