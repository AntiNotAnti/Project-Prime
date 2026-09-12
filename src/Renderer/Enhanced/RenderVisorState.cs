using System;
using OpenTK.Mathematics;

namespace MphRead
{
    internal static class EnhancedVisorPolicy
    {
        public static bool IsEligible(Mods.GraphicsPreset preset,
            bool playerHud, bool spectator, bool alive, bool altForm,
            bool morphing, bool firstPerson, bool cameraSequence)
            => preset == Mods.GraphicsPreset.Enhanced && playerHud && !spectator
                && alive && !altForm && !morphing && firstPerson
                && !cameraSequence;
    }

    /// <summary>
    /// Immutable, backend-neutral visor values sampled by the presentation owner.
    /// No player, combat-event, or clock owner crosses the sealed-frame boundary.
    /// </summary>
    public readonly record struct RenderVisorState
    {
        public RenderVisorState(CombatVisorProfile combat, DamageVisorSample damage,
            LowHealthVisorSample lowHealth, float distortionPhase,
            float interferencePhase)
        {
            CombatVisorProfile.ValidateCenter(combat.CenterClearRadius);
            CombatVisorProfile.ValidateRange(combat.EdgeVignette, 0,
                VisorPresentationBounds.MaximumEdgeOpacity, nameof(combat));
            CombatVisorProfile.ValidateRange(combat.ChromaticSeparation, 0,
                VisorPresentationBounds.MaximumChromaticSeparation, nameof(combat));
            CombatVisorProfile.ValidateRange(combat.Distortion, 0,
                VisorPresentationBounds.MaximumDistortion, nameof(combat));
            CombatVisorProfile.ValidateRange(combat.HelmetReflection, 0, 1,
                nameof(combat));
            ValidateDamage(damage);
            ValidateLowHealth(lowHealth);
            CombatVisorProfile.ValidateRange(distortionPhase, 0, 1,
                nameof(distortionPhase));
            CombatVisorProfile.ValidateRange(interferencePhase, 0, 1,
                nameof(interferencePhase));

            Enabled = true;
            Combat = combat;
            Damage = damage;
            LowHealth = lowHealth;
            DistortionPhase = distortionPhase;
            InterferencePhase = interferencePhase;
        }

        public bool Enabled { get; }
        public CombatVisorProfile Combat { get; }
        public DamageVisorSample Damage { get; }
        public LowHealthVisorSample LowHealth { get; }
        public float DistortionPhase { get; }
        public float InterferencePhase { get; }

        public static RenderVisorState Disabled => default;

        private static void ValidateDamage(DamageVisorSample sample)
        {
            if (!Finite(sample.Direction) || sample.Direction.LengthSquared > 1.000001f)
                throw new ArgumentOutOfRangeException(nameof(sample));
            CombatVisorProfile.ValidateRange(sample.EdgeOpacity, 0,
                VisorPresentationBounds.MaximumEdgeOpacity, nameof(sample));
            CombatVisorProfile.ValidateRange(sample.Distortion, 0,
                VisorPresentationBounds.MaximumDistortion, nameof(sample));
            if (!Finite(sample.ColorImpulse) || sample.ColorImpulse.X < 0
                || sample.ColorImpulse.Y < 0 || sample.ColorImpulse.Z < 0
                || sample.ColorImpulse.X > 1 || sample.ColorImpulse.Y > 1
                || sample.ColorImpulse.Z > 1)
                throw new ArgumentOutOfRangeException(nameof(sample));
            CombatVisorProfile.ValidateRange(sample.ScanlineInterference, 0,
                VisorPresentationBounds.MaximumInterference, nameof(sample));
            CombatVisorProfile.ValidateCenter(sample.CenterClearRadius);
        }

        private static void ValidateLowHealth(LowHealthVisorSample sample)
        {
            CombatVisorProfile.ValidateRange(sample.EdgeOpacity, 0,
                VisorPresentationBounds.MaximumEdgeOpacity, nameof(sample));
            CombatVisorProfile.ValidateRange(sample.Interference, 0,
                VisorPresentationBounds.MaximumInterference, nameof(sample));
            CombatVisorProfile.ValidateCenter(sample.CenterClearRadius);
        }

        private static bool Finite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static bool Finite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }
}
