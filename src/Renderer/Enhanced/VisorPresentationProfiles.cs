using System;
using OpenTK.Mathematics;

namespace MphRead
{
    public static class VisorPresentationBounds
    {
        public const float MinimumCenterClearRadius = 0.55f;
        public const float MaximumEdgeOpacity = 0.45f;
        public const float MaximumChromaticSeparation = 0.006f;
        public const float MaximumDistortion = 0.02f;
        public const float MaximumInterference = 0.3f;
    }

    public readonly record struct CombatVisorProfile
    {
        public CombatVisorProfile(float centerClearRadius, float edgeVignette,
            float chromaticSeparation, float distortion, float helmetReflection)
        {
            ValidateCenter(centerClearRadius);
            ValidateRange(edgeVignette, 0, VisorPresentationBounds.MaximumEdgeOpacity,
                nameof(edgeVignette));
            ValidateRange(chromaticSeparation, 0,
                VisorPresentationBounds.MaximumChromaticSeparation,
                nameof(chromaticSeparation));
            ValidateRange(distortion, 0, VisorPresentationBounds.MaximumDistortion,
                nameof(distortion));
            ValidateRange(helmetReflection, 0, 1, nameof(helmetReflection));
            CenterClearRadius = centerClearRadius;
            EdgeVignette = edgeVignette;
            ChromaticSeparation = chromaticSeparation;
            Distortion = distortion;
            HelmetReflection = helmetReflection;
        }

        public float CenterClearRadius { get; }
        public float EdgeVignette { get; }
        public float ChromaticSeparation { get; }
        public float Distortion { get; }
        public float HelmetReflection { get; }

        internal static void ValidateCenter(float value)
            => ValidateRange(value, VisorPresentationBounds.MinimumCenterClearRadius, 1,
                nameof(value));

        internal static void ValidateRange(float value, float minimum, float maximum,
            string parameterName)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public readonly record struct DamageVisorProfile
    {
        public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(1);

        public DamageVisorProfile(float centerClearRadius, TimeSpan duration,
            float edgeOpacity, float distortion, Vector3 color, float colorStrength,
            float scanlineInterference, float directionalBias)
        {
            CombatVisorProfile.ValidateCenter(centerClearRadius);
            if (duration <= TimeSpan.Zero || duration > MaximumDuration)
                throw new ArgumentOutOfRangeException(nameof(duration));
            CombatVisorProfile.ValidateRange(edgeOpacity, 0,
                VisorPresentationBounds.MaximumEdgeOpacity, nameof(edgeOpacity));
            CombatVisorProfile.ValidateRange(distortion, 0,
                VisorPresentationBounds.MaximumDistortion, nameof(distortion));
            ValidateUnitColor(color, nameof(color));
            CombatVisorProfile.ValidateRange(colorStrength, 0, 1, nameof(colorStrength));
            CombatVisorProfile.ValidateRange(scanlineInterference, 0,
                VisorPresentationBounds.MaximumInterference,
                nameof(scanlineInterference));
            CombatVisorProfile.ValidateRange(directionalBias, 0, 1,
                nameof(directionalBias));
            CenterClearRadius = centerClearRadius;
            Duration = duration;
            EdgeOpacity = edgeOpacity;
            Distortion = distortion;
            Color = color;
            ColorStrength = colorStrength;
            ScanlineInterference = scanlineInterference;
            DirectionalBias = directionalBias;
        }

        public float CenterClearRadius { get; }
        public TimeSpan Duration { get; }
        public float EdgeOpacity { get; }
        public float Distortion { get; }
        public Vector3 Color { get; }
        public float ColorStrength { get; }
        public float ScanlineInterference { get; }
        public float DirectionalBias { get; }

        public DamageVisorSample Sample(TimeSpan elapsedPresentationTime,
            Vector2 screenDirection)
        {
            if (Duration <= TimeSpan.Zero)
                throw new InvalidOperationException("Damage visor profile is not initialized.");
            if (elapsedPresentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsedPresentationTime));
            if (!float.IsFinite(screenDirection.X) || !float.IsFinite(screenDirection.Y))
                throw new ArgumentOutOfRangeException(nameof(screenDirection));
            if (elapsedPresentationTime >= Duration)
                return new DamageVisorSample(Vector2.Zero, 0, 0, Vector3.Zero, 0,
                    CenterClearRadius);

            float progress = (float)(elapsedPresentationTime.TotalSeconds
                / Duration.TotalSeconds);
            float envelope = 1 - progress;
            envelope *= envelope;
            Vector2 direction = NormalizeDirection(screenDirection) * DirectionalBias;
            return new DamageVisorSample(direction, EdgeOpacity * envelope,
                Distortion * envelope, Color * (ColorStrength * envelope),
                ScanlineInterference * envelope, CenterClearRadius);
        }

        private static Vector2 NormalizeDirection(Vector2 direction)
        {
            double lengthSquared = (double)direction.X * direction.X
                + (double)direction.Y * direction.Y;
            if (lengthSquared <= 1e-12) return Vector2.Zero;
            double scale = 1 / Math.Sqrt(lengthSquared);
            return new Vector2((float)(direction.X * scale), (float)(direction.Y * scale));
        }

        private static void ValidateUnitColor(Vector3 color, string parameterName)
        {
            if (!float.IsFinite(color.X) || !float.IsFinite(color.Y)
                || !float.IsFinite(color.Z) || color.X < 0 || color.X > 1
                || color.Y < 0 || color.Y > 1 || color.Z < 0 || color.Z > 1)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public readonly record struct DamageVisorSample(Vector2 Direction,
        float EdgeOpacity, float Distortion, Vector3 ColorImpulse,
        float ScanlineInterference, float CenterClearRadius);

    public readonly record struct LowHealthVisorProfile
    {
        public const float MaximumPulseFrequency = 4;

        public LowHealthVisorProfile(float centerClearRadius, float healthThreshold,
            float edgeOpacity, float interference, float pulseFrequency)
        {
            CombatVisorProfile.ValidateCenter(centerClearRadius);
            CombatVisorProfile.ValidateRange(healthThreshold, float.Epsilon, 1,
                nameof(healthThreshold));
            CombatVisorProfile.ValidateRange(edgeOpacity, 0,
                VisorPresentationBounds.MaximumEdgeOpacity, nameof(edgeOpacity));
            CombatVisorProfile.ValidateRange(interference, 0,
                VisorPresentationBounds.MaximumInterference, nameof(interference));
            CombatVisorProfile.ValidateRange(pulseFrequency, 0, MaximumPulseFrequency,
                nameof(pulseFrequency));
            CenterClearRadius = centerClearRadius;
            HealthThreshold = healthThreshold;
            EdgeOpacity = edgeOpacity;
            Interference = interference;
            PulseFrequency = pulseFrequency;
        }

        public float CenterClearRadius { get; }
        public float HealthThreshold { get; }
        public float EdgeOpacity { get; }
        public float Interference { get; }
        public float PulseFrequency { get; }

        public LowHealthVisorSample Sample(TimeSpan presentationTime,
            float healthFraction, ulong stablePlayerKey)
        {
            if (HealthThreshold <= 0)
                throw new InvalidOperationException("Low-health visor profile is not initialized.");
            if (presentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(presentationTime));
            CombatVisorProfile.ValidateRange(healthFraction, 0, 1, nameof(healthFraction));
            if (healthFraction >= HealthThreshold)
                return new LowHealthVisorSample(0, 0, CenterClearRadius);

            float severity = (HealthThreshold - healthFraction) / HealthThreshold;
            double seconds = PresentationTimeMath.Seconds(presentationTime);
            double phase = PresentationTimeMath.StableUnit(stablePlayerKey) * Math.Tau;
            float pulse = PulseFrequency <= 0 ? 1 : 0.75f + 0.25f * (float)Math.Sin(
                seconds * PulseFrequency * Math.Tau + phase);
            float strength = severity * pulse;
            return new LowHealthVisorSample(EdgeOpacity * strength,
                Interference * strength, CenterClearRadius);
        }
    }

    public readonly record struct LowHealthVisorSample(float EdgeOpacity,
        float Interference, float CenterClearRadius);

    public static class EnhancedVisorProfiles
    {
        public static CombatVisorProfile Combat { get; } = new(
            centerClearRadius: 0.7f, edgeVignette: 0.1f,
            chromaticSeparation: 0.0015f, distortion: 0.002f,
            helmetReflection: 0.16f);

        public static DamageVisorProfile Damage { get; } = new(
            centerClearRadius: 0.66f, duration: TimeSpan.FromMilliseconds(320),
            edgeOpacity: 0.28f, distortion: 0.01f,
            color: new Vector3(1, 0.16f, 0.08f), colorStrength: 0.34f,
            scanlineInterference: 0.18f, directionalBias: 0.72f);

        public static LowHealthVisorProfile LowHealth { get; } = new(
            centerClearRadius: 0.68f, healthThreshold: 0.25f,
            edgeOpacity: 0.16f, interference: 0.08f, pulseFrequency: 1.4f);
    }
}
