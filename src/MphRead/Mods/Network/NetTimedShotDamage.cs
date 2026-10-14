using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Values copied from the completed native kernel, after charge, row selection,
// scope and trusted modifiers. No owner, equipment or pooled beam is retained.
internal readonly record struct TimedShotDamageTemplate(float Body, float Head, float Splash,
    Vector3 Origin, Vector3 Direction, float Radius, float Speed, float Reach, int Curve,
    bool Balanced, byte Effects, bool Present)
{
    internal static TimedShotDamageTemplate From(BeamProjectileEntity beam)
        => new(beam.Damage, beam.HeadshotDamage, beam.SplashDamage, beam.SpawnPosition,
            beam.Velocity.LengthSquared > .000001f ? beam.Velocity.Normalized() : beam.Direction,
            beam.CylinderRadius, beam.Speed, beam.MaxDistance, beam.DamageInterpolation,
            beam.OwningScene.GameState.BalancedMode,
            NetHitClaims.AfflictionClaimFlags(beam.Afflictions), true);

    internal bool SameChannels(in TimedShotDamageTemplate other) => Body == other.Body
        && Head == other.Head && Splash == other.Splash && Origin == other.Origin
        && Reach == other.Reach && Curve == other.Curve && Balanced == other.Balanced
        && Effects == other.Effects && Present == other.Present;

    internal float Channel(in HitClaimPacket claim, out bool nativeHead)
    {
        bool direct = (claim.Flags & HitClaimPacket.FlagDirect) != 0;
        bool head = direct && (claim.Flags & HitClaimPacket.FlagHeadshot) != 0;
        nativeHead = head && MathF.Abs(Body - Head) > 1 / 4096f;
        float damage = direct ? head ? Head : Body : Splash;
        float distance = Vector3.Distance(Origin, claim.HitPoint);
        if (direct && Reach > 0)
            damage = BeamProjectileEntity.GetInterpolatedValue(Curve, damage, 0, distance / Reach);
        if (Balanced && BalancedModeRules.HasRangeDamageCurve((BeamType)claim.Beam))
            damage = BalancedModeRules.ScaleRangeDamage((BeamType)claim.Beam, damage, distance);
        return Math.Max(0, damage);
    }
}
