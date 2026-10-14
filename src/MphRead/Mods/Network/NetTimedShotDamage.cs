using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Values copied from the completed native kernel, after charge, row selection,
// scope and trusted modifiers. No owner, equipment or pooled beam is retained.
internal readonly record struct TimedShotDamageTemplate(float Body, float Head, float Splash,
    Vector3 Origin, Vector3 Direction, float Radius, float Speed, float Reach, int Curve,
    bool Balanced, byte Effects, bool Present, byte DirectOnlyEffects = 0)
{
    internal static TimedShotDamageTemplate From(BeamProjectileEntity beam)
    {
        bool balanced = beam.OwningScene.GameState.BalancedMode;
        byte effects = FilterEffects(beam.ModOwnerHunter, beam.Beam, balanced,
            beam.Flags.TestFlag(BeamFlags.Charged), NetHitClaims.AfflictionClaimFlags(beam.Afflictions),
            out byte directOnly);
        return new(beam.Damage, beam.HeadshotDamage, beam.SplashDamage, beam.SpawnPosition,
            beam.Velocity.LengthSquared > .000001f ? beam.Velocity.Normalized() : beam.Direction,
            beam.CylinderRadius, beam.Speed, beam.MaxDistance, beam.DamageInterpolation,
            balanced, effects, true, directOnly);
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
        && Effects == other.Effects && DirectOnlyEffects == other.DirectOnlyEffects && Present == other.Present;

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
