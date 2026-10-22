using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum TimedDisruptionPolicy : byte { None, Ordinary, BalancedKanden }

// Values copied from the completed native kernel, after charge, row selection,
// scope and trusted modifiers. No owner, equipment or pooled beam is retained.
internal readonly record struct TimedShotDamageTemplate(float Body, float Head, float Splash,
    Vector3 Origin, Vector3 Direction, float Radius, float Speed, float Reach, int Curve,
    bool Balanced, byte Effects, bool Present, byte DirectOnlyEffects = 0,
    TimedDisruptionPolicy DisruptionPolicy = TimedDisruptionPolicy.None,
    Vector3 NativeVelocity = default, bool FirstSegmentKnown = false, uint FirstCollisionFrame = 0)
{
    internal static TimedShotDamageTemplate From(BeamProjectileEntity beam)
    {
        bool balanced = beam.OwningScene.GameState.BalancedMode;
        byte effects = FilterEffects(beam.ModOwnerHunter, beam.Beam, balanced,
            beam.Flags.TestFlag(BeamFlags.Charged), NetHitClaims.AfflictionClaimFlags(beam.Afflictions),
            out byte directOnly);
        uint firstFrame = 0;
        bool firstSegment = beam.Beam == BeamType.Imperialist && beam.Owner is PlayerEntity owner
            && beam.ModComponentId == 1 && beam.Position == beam.SpawnPosition
            && beam.Acceleration == Vector3.Zero && beam.SpeedDecayTime == 0
            && beam.Homing == 0 && beam.RicochetWeapon == null && beam.EnhancedBounceCount == 0
            && (beam.Flags & (BeamFlags.Continuous | BeamFlags.Ricochet | BeamFlags.Collided)) == 0
            && beam.Velocity.LengthSquared > .000001f
            && NetUnlagged.TryTimedFirstCollisionFrame(owner, out firstFrame);
        return new(beam.Damage, beam.HeadshotDamage, beam.SplashDamage, beam.SpawnPosition,
            beam.Velocity.LengthSquared > .000001f ? beam.Velocity.Normalized() : beam.Direction,
            beam.CylinderRadius, beam.Speed, beam.MaxDistance, beam.DamageInterpolation,
            balanced, effects, true, directOnly,
            DisruptionFor(beam.ModOwnerHunter, beam.Beam, balanced,
                beam.Flags.TestFlag(BeamFlags.Charged), effects),
            firstSegment ? beam.Velocity : default, firstSegment, firstFrame);
    }

    internal static TimedDisruptionPolicy DisruptionFor(Hunter hunter, BeamType beam,
        bool balanced, bool charged, byte effects)
    {
        if ((effects & HitClaimPacket.FlagDisrupted) == 0) return TimedDisruptionPolicy.None;
        if (balanced && hunter == Hunter.Kanden && beam == BeamType.VoltDriver)
            return charged ? TimedDisruptionPolicy.BalancedKanden : TimedDisruptionPolicy.None;
        return TimedDisruptionPolicy.Ordinary;
    }

    internal static byte FilterEffects(Hunter hunter, BeamType beam, bool balanced, bool charged,
        byte effects, out byte directOnly)
    {
        directOnly = 0;
        if (balanced && hunter == Hunter.Kanden && beam == BeamType.VoltDriver)
            directOnly |= HitClaimPacket.FlagDisrupted;
        if (balanced && hunter == Hunter.Spire && beam == BeamType.Magmaul)
            directOnly |= HitClaimPacket.FlagBurning;
        directOnly &= effects;
        if (!charged) { effects &= unchecked((byte)~directOnly); directOnly = 0; }
        return effects;
    }

    internal byte EffectsFor(bool direct)
        => direct ? Effects : (byte)(Effects & ~DirectOnlyEffects);

    internal bool SameChannels(in TimedShotDamageTemplate other) => Body == other.Body
        && Head == other.Head && Splash == other.Splash && Origin == other.Origin
        && Reach == other.Reach && Curve == other.Curve && Balanced == other.Balanced
        && Effects == other.Effects && DirectOnlyEffects == other.DirectOnlyEffects
        && DisruptionPolicy == other.DisruptionPolicy && Present == other.Present
        && FirstSegmentKnown == other.FirstSegmentKnown && NativeVelocity == other.NativeVelocity
        && FirstCollisionFrame == other.FirstCollisionFrame;

    internal float Channel(in HitClaimPacket claim, out bool nativeHead)
    {
        bool direct = (claim.Flags & HitClaimPacket.FlagDirect) != 0;
        bool head = direct && (claim.Flags & HitClaimPacket.FlagHeadshot) != 0;
        nativeHead = head && MathF.Abs(Body - Head) > 1 / 4096f;
        float damage = direct ? head ? Head : Body : Splash;
        // HitPoint is the claimant's body origin, not an authority collision
        // point or explosion centre. It cannot reconstruct native falloff.
        // Retain the legal per-launch channel ceiling; do not reject base
        // damage using a guessed distance. General enforcement remains gated
        // until impact/trajectory facts make an exact payout bound possible.
        if (Balanced && BalancedModeRules.HasRangeDamageCurve((BeamType)claim.Beam))
            damage = Math.Max(BalancedModeRules.ScaleRangeDamage((BeamType)claim.Beam, damage, 0),
                BalancedModeRules.ScaleRangeDamage((BeamType)claim.Beam, damage, float.MaxValue));
        return Math.Max(0, damage);
    }
}
