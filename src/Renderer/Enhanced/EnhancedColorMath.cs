using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Backend-neutral reference math for the Enhanced color pipeline. GPU
    /// shaders should use equivalent equations so screenshots and CPU-side
    /// validation agree about transfer functions and tone mapping.
    /// </summary>
    internal static class EnhancedColorMath
    {
        public const float DefaultExposure = 1f;

        public static float SrgbToLinear(float srgb)
        {
            ValidateUnit(srgb, nameof(srgb));
            return srgb <= 0.04045f
                ? srgb / 12.92f
                : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
        }

        public static Vector3 SrgbToLinear(Vector3 srgb)
        {
            ValidateUnit(srgb, nameof(srgb));
            return new Vector3(SrgbToLinear(srgb.X), SrgbToLinear(srgb.Y),
                SrgbToLinear(srgb.Z));
        }

        public static float LinearToSrgb(float linear)
        {
            ValidateUnit(linear, nameof(linear));
            return linear <= 0.0031308f
                ? linear * 12.92f
                : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        }

        public static Vector3 LinearToSrgb(Vector3 linear)
        {
            ValidateUnit(linear, nameof(linear));
            return new Vector3(LinearToSrgb(linear.X), LinearToSrgb(linear.Y),
                LinearToSrgb(linear.Z));
        }

        /// <summary>
        /// Applies a fixed exposure followed by a restrained ACES-inspired
        /// fitted curve. The result is display-referred linear color in 0..1;
        /// gamma conversion remains a separate, explicit operation.
        /// </summary>
        public static Vector3 ToneMapAces(Vector3 linearHdr,
            float exposure = DefaultExposure)
        {
            ValidateNonNegative(linearHdr, nameof(linearHdr));
            if (!float.IsFinite(exposure) || exposure <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exposure));
            }

            return new Vector3(
                ToneMapComponent(linearHdr.X, exposure),
                ToneMapComponent(linearHdr.Y, exposure),
                ToneMapComponent(linearHdr.Z, exposure));
        }

        private static float ToneMapComponent(float value, float exposure)
        {
            // Double intermediates keep the reference implementation finite
            // even for extreme but finite HDR values.
            double x = (double)value * exposure;
            double mapped = x * (2.51 * x + 0.03)
                / (x * (2.43 * x + 0.59) + 0.14);
            return (float)Math.Clamp(mapped, 0, 1);
        }

        private static void ValidateUnit(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateUnit(Vector3 value, string parameterName)
        {
            if (!IsFinite(value) || value.X < 0 || value.X > 1
                || value.Y < 0 || value.Y > 1 || value.Z < 0 || value.Z > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateNonNegative(Vector3 value, string parameterName)
        {
            if (!IsFinite(value) || value.X < 0 || value.Y < 0 || value.Z < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
