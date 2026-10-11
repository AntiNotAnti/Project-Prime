using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct SelfSplashOutcome(ushort Damage, Vector3 Impulse, byte Effects);

/// <summary>Self rescue needs a real authority explosion, an independently admitted
/// source, and the owner body captured at that explosion. No client explosion centre,
/// damage multiplier, impulse or affliction is authoritative.</summary>
internal static class NetSelfSplash
{
    internal static bool Enabled = Environment.GetEnvironmentVariable("PRIME_SELF_SPLASH") != "0";
    internal const int Capacity = 512;
    private const uint Retention = 512;
    private readonly record struct Explosion(ShotKey Key, uint Component, uint Frame, uint Launch,
        byte Weapon, Vector3 Origin, Vector3 Body, float Radius, float Damage, byte Curve,
        byte DirectionType, float DirectionMagnitude, Vector3 Velocity, float Effectiveness,
        float DamageLevel, byte Handicap, bool EnhancedJump, bool Eligible, byte Effects);
    private static readonly Explosion?[,] Explosions = new Explosion?[8, Capacity];
    internal static long Observed, Verified, Refused, CapacityRefused, EvidenceOverflow;
    private static readonly List<object> Paid = new(), Receipts = new(), Feedback = new();
    private static object Outcome(in CombatAckEntry o) => new { o.ShotId, o.ClaimId, o.Result, o.VictimSlot,
        o.VictimGeneration, o.VictimLife, o.DamageApplied, o.HealthAfter, o.DamageSequence, o.Flags };
    private static float[] Vector(Vector3 v) => new[] { v.X, v.Y, v.Z };
    internal static void RecordReceipt(in ShotPaymentKey key, in CombatAckEntry outcome)
    {
        if (!NetCampaignEvidence.Enabled || key.Shot.ShooterSlot != key.Victim) return;
        if (Receipts.Count >= 8192) { EvidenceOverflow++; return; }
        Receipts.Add(new { Key = key, AuthorityFrame = NetSession.NetFrame, Outcome = Outcome(outcome) });
    }
    internal static void RecordFeedback(int slot, ushort generation, ushort life, ushort sequence,
        Vector3 predicted, Vector3 authority, Vector3 before, Vector3 after)
    {
        if (!NetCampaignEvidence.Enabled) return;
        if (Feedback.Count >= 8192) { EvidenceOverflow++; return; }
        Feedback.Add(new { NetSession.AuthorityEpoch, MatchId = NetSession.CurrentMatchId,
            VictimSlot = slot, Generation = generation, Life = life, DamageSequence = sequence,
            Predicted = Vector(predicted), Authority = Vector(authority), Before = Vector(before), After = Vector(after) });
    }
    internal static void RecordPayment(in ShotPaymentKey key, in CombatAckEntry outcome)
    {
        if (!NetCampaignEvidence.Enabled || key.Shot.ShooterSlot != key.Victim) return;
        if (Paid.Count >= 8192) { EvidenceOverflow++; return; }
        PlayerState state = default; NetDamage.Write(key.Victim, ref state);
        Paid.Add(new { Key = key, AuthorityFrame = NetSession.NetFrame, Outcome = Outcome(outcome), Impulse = Vector(state.HitDirection) });
    }
    internal static bool Supports(BeamType beam) => beam is BeamType.Missile or BeamType.Magmaul
        or BeamType.Judicator or BeamType.OmegaCannon;
    internal static bool Claimable(BeamType beam, DamageFlags flags, bool direct, uint shot, uint component)
        => Enabled && Supports(beam) && shot != 0 && component != 0
            && !flags.TestAny(DamageFlags.Burn | DamageFlags.Halfturret | DamageFlags.Headshot | DamageFlags.Death);
    internal static bool Shape(int slot, in HitClaimPacket claim) => Enabled
        && claim.VictimSlot == slot && Supports((BeamType)claim.Beam) && claim.ShotId != 0 && claim.ComponentId != 0
        && (claim.Flags & ~(HitClaimPacket.FlagDirect | HitClaimPacket.FlagLethal | HitClaimPacket.FlagBurning | HitClaimPacket.FlagFrozen | HitClaimPacket.FlagDisrupted)) == 0;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    internal static void Observe(BeamProjectileEntity beam, Vector3 origin, EntityBase? excluded = null)
    {
        if (!Enabled || !NetHitClaims.Arbitrating || !Supports(beam.Beam)
            || beam.Owner is not PlayerEntity owner || beam.ModComponentId == 0 || beam.ModShotId == 0
            || !NetPlayerLifecycle.Matches(owner.SlotIndex, beam.ModLaunchKey.Generation, beam.ModLaunchKey.LifeId)
            || !Finite(origin) || !float.IsFinite(beam.SplashRadius) || beam.SplashRadius <= 0) return;
        int slot = owner.SlotIndex, free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (Explosions[slot, i] is not { } e || NetSession.NetFrame - e.Frame > Retention
                || !NetSession.MatchesStream(e.Key.MatchId, e.Key.AuthorityEpoch)
                || e.Key.Generation != beam.ModLaunchKey.Generation) { if (free < 0) free = i; }
            else if (e.Key == beam.ModLaunchKey && e.Component == beam.ModComponentId) return;
        }
        if (free < 0) { CapacityRefused++; return; }
        // Capture immunity at the impact. Waiting out spawn protection must not
        // turn an immune explosion into a later damaging claim.
        Explosions[slot, free] = new(beam.ModLaunchKey, beam.ModComponentId, NetSession.NetFrame,
            beam.ModLaunchFrame, (byte)beam.Beam, origin, owner.Position, beam.SplashRadius,
            beam.ModBalancedRangeDamage(beam.SplashDamage, origin), beam.SplashDamageType,
            beam.DamageDirType, beam.DamageDirMag, beam.Velocity,
            Metadata.DamageMultipliers[(int)owner.BeamEffectiveness[(int)beam.Beam]],
            Metadata.DamageLevels[owner.OwningScene.GameState.DamageLevel], NetSession.SlotDamageReduction[slot],
            beam.ModEnhancedSelfJump, owner.ModSelfSplashEligible && owner != excluded,
            owner.OwningScene.GameState.BalancedMode ? (byte)0 : NetHitClaims.AfflictionClaimFlags(beam.Afflictions));
        Observed++;
    }
    internal static bool Validate(int slot, in HitClaimPacket claim, out SelfSplashOutcome outcome)
    {
        outcome = default;
        if (!Shape(slot, claim) || (claim.Flags & HitClaimPacket.FlagDirect) != 0 || (uint)slot >= 8 || slot >= PlayerEntity.Players.Count
            || !NetPlayerLifecycle.Matches(slot, claim.ShooterGeneration, claim.ShooterLifeId)
            || claim.ShooterGeneration != claim.VictimGeneration || claim.ShooterLifeId != claim.VictimLifeId)
            return Reject();
        var key = NetShotProof.Key(slot, claim);
        if (!NetShotProof.TryEmittedFire(key, claim.ComponentId, claim.Beam, out var fire)
            || fire.AckFrame != claim.LaunchFrame) return Reject();
        Explosion? found = null;
        for (int i = 0; i < Capacity; i++)
            if (Explosions[slot, i] is { } e && e.Key == key && e.Component == claim.ComponentId) { found = e; break; }
        if (found is not { } hit || hit.Weapon != claim.Beam || !hit.Eligible
            || NetSession.NetFrame - hit.Frame > Retention || hit.Launch != claim.LaunchFrame
            || !Finite(claim.HitPoint)
            || (claim.Flags & (HitClaimPacket.FlagBurning | HitClaimPacket.FlagFrozen | HitClaimPacket.FlagDisrupted)) != hit.Effects) return Reject();
        var owner = PlayerEntity.Players[slot];
        // Bounded by each weapon's native radius. This permits a small difference
        // between local and authority movement, never a historical remote body or
        // a client-supplied explosion centre. The body is the server-owned body at impact.
        float tolerance = Math.Min(.75f, hit.Radius * .25f);
        float distance = Vector3.Distance(hit.Origin, hit.Body);
        if ((claim.HitPoint - hit.Body).LengthSquared > tolerance * tolerance
            || Vector3.Distance(hit.Origin, hit.Body) >= hit.Radius + tolerance || distance >= hit.Radius)
            return Reject();
        CollisionResult collision = default;
        if (CollisionDetection.CheckBetweenPoints(hit.Origin, hit.Body, TestFlags.Beams, owner.OwningScene, ref collision)
            || CollisionDetection.CheckBetweenPoints(hit.Origin, claim.HitPoint, TestFlags.Beams, owner.OwningScene, ref collision)) return Reject();
        uint damage = (uint)Math.Max(0, (int)BeamProjectileEntity.GetInterpolatedValue(hit.Curve, hit.Damage, 0, distance / hit.Radius));
        if (hit.Effectiveness == 0 || damage == 0) return Reject();
        damage = Math.Max(1u, (uint)(damage * hit.Effectiveness));
        damage = Math.Max(1u, (uint)(damage * hit.DamageLevel));
        if (hit.Handicap > 0) damage = PlayerHandicap.ScaleDamage(damage, hit.Handicap);
        Vector3 impulse = Impulse(hit, hit.Body);
        if (hit.EnhancedJump) { damage = (uint)Math.Ceiling(damage * .25f); impulse *= 1.35f; }
        outcome = new((ushort)Math.Min(damage, ushort.MaxValue), impulse, hit.Effects);
        Verified++; return true;
    }
    private static Vector3 Impulse(in Explosion hit, Vector3 body)
    {
        Vector3 direction;
        if (hit.DirectionType == 1) direction = hit.Velocity == Vector3.Zero ? Vector3.UnitY : hit.Velocity.Normalized();
        else if (hit.DirectionType == 2)
        {
            direction = body - hit.Origin;
            if (direction != Vector3.Zero) direction = direction.Normalized();
            direction.Y = Math.Max(.03f, direction.Y / 2);
        }
        else if (hit.DirectionType == 3)
        { direction = (body - hit.Origin).WithY(0); if (direction != Vector3.Zero) direction = direction.Normalized(); }
        else if (hit.DirectionType == 4) direction = Vector3.UnitY;
        else return Vector3.Zero;
        return direction * hit.DirectionMagnitude;
    }
    private static bool Reject() { Refused++; return false; }
    internal static void Reset() => Array.Clear(Explosions);
    internal static object Capture() => new { Enabled, Observed, Verified, Refused, CapacityRefused, EvidenceOverflow, Paid, Receipts, Feedback };
}
