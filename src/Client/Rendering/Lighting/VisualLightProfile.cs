using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Immutable presentation-only parameters for a visual point light.
    /// Lifetime is measured in seconds and falloff is the exponent applied to
    /// the renderer's bounded <c>1 - distance / radius</c> attenuation.
    /// </summary>
    public readonly record struct VisualLightProfile
    {
        public const float SupportedFalloff = 2f;

        public VisualLightProfile(Vector3 color, float radius, float intensity,
            int priority, float lifetime, float falloff)
        {
            if (!IsFinite(color) || color.X < 0 || color.Y < 0 || color.Z < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(color),
                    "Visual light color must contain finite, non-negative components.");
            }
            if (color.LengthSquared <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(color),
                    "Visual light color must contain visible energy.");
            }
            if (!float.IsFinite(radius) || radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!float.IsFinite(intensity) || intensity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intensity));
            }
            if (priority < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }
            if (!float.IsFinite(lifetime) || lifetime <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }
            // The current scene-light shader ABI only implements quadratic
            // attenuation. Reject authored data the backend cannot represent
            // instead of silently discarding a different exponent.
            if (!float.IsFinite(falloff) || falloff != SupportedFalloff)
            {
                throw new ArgumentOutOfRangeException(nameof(falloff),
                    $"Only quadratic falloff ({SupportedFalloff}) is supported.");
            }

            Color = color;
            Radius = radius;
            Intensity = intensity;
            Priority = priority;
            Lifetime = lifetime;
            Falloff = falloff;
        }

        public Vector3 Color { get; }
        public float Radius { get; }
        public float Intensity { get; }
        public int Priority { get; }
        public float Lifetime { get; }
        public float Falloff { get; }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
