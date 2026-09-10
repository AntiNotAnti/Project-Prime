using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    public enum BeamImpactStyle : byte
    {
        EnergyFlash,
        ElectricBurst,
        ExplosiveBloom,
        HeavyPulse,
        PrecisionSpark,
        IceShatter,
        HeatBloom,
        ArcDischarge
    }

    /// <summary>
    /// Immutable presentation-only styling for an enhanced energy beam. Widths
    /// are relative presentation multipliers and never replace collision geometry.
    /// </summary>
    public readonly record struct BeamVisualProfile
    {
        public const float MaximumWidth = 4;
        public const float MaximumEmission = 16;
        public const float MaximumNoiseScale = 64;
        public const float MaximumNoiseSpeed = 32;
        public const float MaximumPulseFrequency = 30;
        public const float MaximumPulseStrength = 0.5f;
        public const float MaximumImpactScale = 4;

        public BeamVisualProfile(Vector3 coreColor, Vector3 glowColor,
            float coreWidth, float glowWidth, float noiseStrength, float noiseScale,
            float noiseSpeed, float pulseFrequency, float pulseStrength,
            BeamImpactStyle impactStyle, float impactScale, float secondaryArcStrength,
            VisualLightProfile light)
        {
            ValidateHdrColor(coreColor, nameof(coreColor));
            ValidateHdrColor(glowColor, nameof(glowColor));
            if (!float.IsFinite(coreWidth) || coreWidth <= 0 || coreWidth > MaximumWidth)
                throw new ArgumentOutOfRangeException(nameof(coreWidth));
            if (!float.IsFinite(glowWidth) || glowWidth < coreWidth || glowWidth > MaximumWidth)
                throw new ArgumentOutOfRangeException(nameof(glowWidth));
            ValidateUnit(noiseStrength, nameof(noiseStrength));
            if (!float.IsFinite(noiseScale) || noiseScale <= 0 || noiseScale > MaximumNoiseScale)
                throw new ArgumentOutOfRangeException(nameof(noiseScale));
            if (!float.IsFinite(noiseSpeed) || noiseSpeed < 0 || noiseSpeed > MaximumNoiseSpeed)
                throw new ArgumentOutOfRangeException(nameof(noiseSpeed));
            if (!float.IsFinite(pulseFrequency) || pulseFrequency < 0
                || pulseFrequency > MaximumPulseFrequency)
                throw new ArgumentOutOfRangeException(nameof(pulseFrequency));
            if (!float.IsFinite(pulseStrength) || pulseStrength < 0
                || pulseStrength > MaximumPulseStrength)
                throw new ArgumentOutOfRangeException(nameof(pulseStrength));
            if (pulseStrength > 0 && pulseFrequency <= 0)
                throw new ArgumentException("A pulsing beam requires a positive frequency.",
                    nameof(pulseFrequency));
            if (!Enum.IsDefined(impactStyle))
                throw new ArgumentOutOfRangeException(nameof(impactStyle));
            if (!float.IsFinite(impactScale) || impactScale <= 0
                || impactScale > MaximumImpactScale)
                throw new ArgumentOutOfRangeException(nameof(impactScale));
            ValidateUnit(secondaryArcStrength, nameof(secondaryArcStrength));
            ValidateLight(light);

            CoreColor = coreColor;
            GlowColor = glowColor;
            CoreWidth = coreWidth;
            GlowWidth = glowWidth;
            NoiseStrength = noiseStrength;
            NoiseScale = noiseScale;
            NoiseSpeed = noiseSpeed;
            PulseFrequency = pulseFrequency;
            PulseStrength = pulseStrength;
            ImpactStyle = impactStyle;
            ImpactScale = impactScale;
            SecondaryArcStrength = secondaryArcStrength;
            Light = light;
        }

        public Vector3 CoreColor { get; }
        public Vector3 GlowColor { get; }
        public float CoreWidth { get; }
        public float GlowWidth { get; }
        public float NoiseStrength { get; }
        public float NoiseScale { get; }
        public float NoiseSpeed { get; }
        public float PulseFrequency { get; }
        public float PulseStrength { get; }
        public BeamImpactStyle ImpactStyle { get; }
        public float ImpactScale { get; }
        /// <summary>
        /// Presentation-only secondary arc strength. It never represents hit
        /// detection, damage, or authoritative beam geometry.
        /// </summary>
        public float SecondaryArcStrength { get; }
        public VisualLightProfile Light { get; }

        public BeamVisualSample Sample(TimeSpan presentationTime, ulong stableSourceKey)
        {
            if (CoreWidth <= 0)
                throw new InvalidOperationException("Beam visual profile is not initialized.");
            if (presentationTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(presentationTime));

            double seconds = PresentationTimeMath.Seconds(presentationTime);
            float pulse = 1;
            if (PulseStrength > 0)
            {
                double phase = PresentationTimeMath.StableUnit(stableSourceKey) * Math.Tau;
                pulse += PulseStrength * (float)Math.Sin(
                    seconds * PulseFrequency * Math.Tau + phase);
            }
            float noisePhase = PresentationTimeMath.Fraction(seconds * NoiseSpeed
                + PresentationTimeMath.StableUnit(
                    stableSourceKey ^ 0xD1B54A32D192ED03ul));
            return new BeamVisualSample(pulse, noisePhase);
        }

        private static void ValidateHdrColor(Vector3 color, string parameterName)
        {
            if (!IsFinite(color) || color.X < 0 || color.X > MaximumEmission
                || color.Y < 0 || color.Y > MaximumEmission
                || color.Z < 0 || color.Z > MaximumEmission
                || color.LengthSquared <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateUnit(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateLight(VisualLightProfile light)
        {
            if (!IsFinite(light.Color) || light.Color.X < 0 || light.Color.Y < 0
                || light.Color.Z < 0 || !float.IsFinite(light.Radius) || light.Radius <= 0
                || !float.IsFinite(light.Intensity) || light.Intensity <= 0
                || light.Priority < 0 || !float.IsFinite(light.Lifetime)
                || light.Lifetime <= 0 || !float.IsFinite(light.Falloff)
                || light.Falloff <= 0)
            {
                throw new ArgumentException("Visual light profile must be initialized.",
                    nameof(light));
            }
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    public readonly record struct BeamVisualSample(float Pulse, float NoisePhase);

    /// <summary>
    /// Conservative initial beam identities. Values are renderer-safe starting
    /// points and remain subject to capture-based art tuning.
    /// </summary>
    public static class WeaponBeamVisualProfiles
    {
        private static readonly BeamVisualProfile _powerBeam = Create(BeamType.PowerBeam,
            new Vector3(4, 2.4f, 0.7f), new Vector3(1.4f, 0.55f, 0.12f),
            0.18f, 0.58f, 0.12f, 5, 1.5f, 5, 0.08f,
            BeamImpactStyle.EnergyFlash, 0.8f, 0);
        private static readonly BeamVisualProfile _voltDriver = Create(BeamType.VoltDriver,
            new Vector3(1.1f, 3.2f, 5), new Vector3(0.2f, 1.1f, 2.4f),
            0.16f, 0.7f, 0.42f, 11, 5, 9, 0.16f,
            BeamImpactStyle.ElectricBurst, 1.05f, 0);
        private static readonly BeamVisualProfile _missile = Create(BeamType.Missile,
            new Vector3(5.5f, 2.2f, 0.45f), new Vector3(2.8f, 0.5f, 0.08f),
            0.3f, 1.05f, 0.24f, 4, 2, 4, 0.1f,
            BeamImpactStyle.ExplosiveBloom, 1.6f, 0);
        private static readonly BeamVisualProfile _battlehammer = Create(BeamType.Battlehammer,
            new Vector3(1.2f, 4.2f, 0.8f), new Vector3(0.2f, 1.8f, 0.25f),
            0.34f, 0.94f, 0.2f, 3, 1.5f, 3, 0.1f,
            BeamImpactStyle.HeavyPulse, 1.4f, 0);
        private static readonly BeamVisualProfile _imperialist = Create(BeamType.Imperialist,
            new Vector3(6, 0.9f, 1.1f), new Vector3(2.4f, 0.12f, 0.18f),
            0.1f, 0.46f, 0.08f, 8, 0.6f, 2, 0.04f,
            BeamImpactStyle.PrecisionSpark, 0.72f, 0);
        private static readonly BeamVisualProfile _judicator = Create(BeamType.Judicator,
            new Vector3(1.4f, 4.5f, 5.2f), new Vector3(0.2f, 1.7f, 2.3f),
            0.22f, 0.76f, 0.18f, 6, 1.4f, 4, 0.08f,
            BeamImpactStyle.IceShatter, 1.18f, 0);
        private static readonly BeamVisualProfile _magmaul = Create(BeamType.Magmaul,
            new Vector3(6, 1.5f, 0.2f), new Vector3(3.2f, 0.32f, 0.04f),
            0.38f, 1.14f, 0.5f, 5, 3.5f, 6, 0.18f,
            BeamImpactStyle.HeatBloom, 1.5f, 0);
        private static readonly BeamVisualProfile _shockCoil = Create(BeamType.ShockCoil,
            new Vector3(3.6f, 1.4f, 6), new Vector3(1.2f, 0.2f, 3),
            0.12f, 0.84f, 0.65f, 14, 7, 12, 0.2f,
            BeamImpactStyle.ArcDischarge, 1.22f, 0.7f);

        private static readonly IReadOnlyList<BeamType> _supportedBeamTypes
            = Array.AsReadOnly(new[]
            {
                BeamType.PowerBeam,
                BeamType.VoltDriver,
                BeamType.Missile,
                BeamType.Battlehammer,
                BeamType.Imperialist,
                BeamType.Judicator,
                BeamType.Magmaul,
                BeamType.ShockCoil
            });

        public static IReadOnlyList<BeamType> SupportedBeamTypes => _supportedBeamTypes;

        public static bool TryGet(BeamType beam, out BeamVisualProfile profile)
        {
            profile = beam switch
            {
                BeamType.PowerBeam => _powerBeam,
                BeamType.VoltDriver => _voltDriver,
                BeamType.Missile => _missile,
                BeamType.Battlehammer => _battlehammer,
                BeamType.Imperialist => _imperialist,
                BeamType.Judicator => _judicator,
                BeamType.Magmaul => _magmaul,
                BeamType.ShockCoil => _shockCoil,
                _ => default
            };
            return beam is BeamType.PowerBeam or BeamType.VoltDriver or BeamType.Missile
                or BeamType.Battlehammer or BeamType.Imperialist or BeamType.Judicator
                or BeamType.Magmaul or BeamType.ShockCoil;
        }

        private static BeamVisualProfile Create(BeamType beam, Vector3 coreColor,
            Vector3 glowColor, float coreWidth, float glowWidth, float noiseStrength,
            float noiseScale, float noiseSpeed, float pulseFrequency, float pulseStrength,
            BeamImpactStyle impactStyle, float impactScale, float secondaryArcStrength)
        {
            if (!WeaponVisualLightProfiles.TryGet(beam, out VisualLightProfile light))
                throw new InvalidOperationException($"Missing visual-light profile for {beam}.");
            return new BeamVisualProfile(coreColor, glowColor, coreWidth, glowWidth,
                noiseStrength, noiseScale, noiseSpeed, pulseFrequency, pulseStrength,
                impactStyle, impactScale, secondaryArcStrength, light);
        }
    }
}
