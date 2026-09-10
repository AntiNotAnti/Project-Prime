using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Presentation-only force-field styling. It contains no collision or lock state.
    /// </summary>
    public readonly record struct ForceFieldVisualProfile
    {
        public const float MaximumNoiseScale = 64;
        public const float MaximumNoiseSpeed = 32;
        public const float MaximumUvFlow = 8;
        public const float MaximumFresnelPower = 16;
        public const float MaximumEffectStrength = 4;
        public const float MaximumEmissionStrength = 16;
        public const float MaximumDistortionStrength = 0.05f;

        public ForceFieldVisualProfile(float noiseScale, float noiseStrength,
            float noiseSpeed, Vector2 uvFlow, float fresnelPower, float fresnelStrength,
            Vector3 emissionColor, float emissionStrength, float distortionStrength,
            float intersectionStrength)
        {
            if (!float.IsFinite(noiseScale) || noiseScale <= 0 || noiseScale > MaximumNoiseScale)
                throw new ArgumentOutOfRangeException(nameof(noiseScale));
            ValidateRange(noiseStrength, 0, 1, nameof(noiseStrength));
            ValidateRange(noiseSpeed, 0, MaximumNoiseSpeed, nameof(noiseSpeed));
            if (!float.IsFinite(uvFlow.X) || !float.IsFinite(uvFlow.Y)
                || MathF.Abs(uvFlow.X) > MaximumUvFlow || MathF.Abs(uvFlow.Y) > MaximumUvFlow)
                throw new ArgumentOutOfRangeException(nameof(uvFlow));
            ValidateRange(fresnelPower, 1, MaximumFresnelPower, nameof(fresnelPower));
            ValidateRange(fresnelStrength, 0, MaximumEffectStrength, nameof(fresnelStrength));
            ValidateUnitColor(emissionColor, nameof(emissionColor));
            ValidateRange(emissionStrength, 0, MaximumEmissionStrength,
                nameof(emissionStrength));
            ValidateRange(distortionStrength, 0, MaximumDistortionStrength,
                nameof(distortionStrength));
            ValidateRange(intersectionStrength, 0, MaximumEffectStrength,
                nameof(intersectionStrength));

            NoiseScale = noiseScale;
            NoiseStrength = noiseStrength;
            NoiseSpeed = noiseSpeed;
            UvFlow = uvFlow;
            FresnelPower = fresnelPower;
            FresnelStrength = fresnelStrength;
            EmissionColor = emissionColor;
            EmissionStrength = emissionStrength;
            DistortionStrength = distortionStrength;
            IntersectionStrength = intersectionStrength;
        }

        public float NoiseScale { get; }
        public float NoiseStrength { get; }
        public float NoiseSpeed { get; }
        public Vector2 UvFlow { get; }
        public float FresnelPower { get; }
        public float FresnelStrength { get; }
        public Vector3 EmissionColor { get; }
        public float EmissionStrength { get; }
        public float DistortionStrength { get; }
        public float IntersectionStrength { get; }

        public ForceFieldVisualSample Sample(TimeSpan presentationTime, ulong stableFieldKey)
        {
            if (NoiseScale <= 0)
                throw new InvalidOperationException("Force-field visual profile is not initialized.");
            if (presentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
            double seconds = PresentationTimeMath.Seconds(presentationTime);
            float offsetX = PresentationTimeMath.Fraction(seconds * UvFlow.X
                + PresentationTimeMath.StableUnit(stableFieldKey));
            float offsetY = PresentationTimeMath.Fraction(seconds * UvFlow.Y
                + PresentationTimeMath.StableUnit(
                    stableFieldKey ^ 0x9E3779B97F4A7C15ul));
            float noisePhase = PresentationTimeMath.Fraction(seconds * NoiseSpeed
                + PresentationTimeMath.StableUnit(
                    stableFieldKey ^ 0xD1B54A32D192ED03ul));
            return new ForceFieldVisualSample(new Vector2(offsetX, offsetY), noisePhase);
        }

        private static void ValidateRange(float value, float minimum, float maximum,
            string parameterName)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateUnitColor(Vector3 value, string parameterName)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
                || !float.IsFinite(value.Z) || value.X < 0 || value.X > 1
                || value.Y < 0 || value.Y > 1 || value.Z < 0 || value.Z > 1)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public readonly record struct ForceFieldVisualSample(Vector2 UvOffset, float NoisePhase);

    public static class EnhancedForceFieldProfiles
    {
        public static ForceFieldVisualProfile Default { get; } = new(
            noiseScale: 7, noiseStrength: 0.42f, noiseSpeed: 1.8f,
            uvFlow: new Vector2(0.08f, -0.16f), fresnelPower: 4,
            fresnelStrength: 1.15f, emissionColor: new Vector3(0.2f, 0.72f, 1),
            emissionStrength: 2.4f, distortionStrength: 0.012f,
            intersectionStrength: 1.2f);
    }
}
