using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Retained owner-authored tick/target decisions for delayed continuous
/// claims. This is target and cadence validation, not N4 strict resource proof.</summary>
internal static class NetContinuousAdmission
{
    private const int Capacity = 512;
    private const uint Retention = 256;
    private struct Entry
    {
        public bool Live, Conflict, Rejected;
        public ShotKey Key;
        public FireEvent Fire;
        public uint Arrived;
    }
    private static readonly Entry[,] Entries = new Entry[8, Capacity];
    private static bool Expired(in Entry e) => !e.Live || NetSession.NetFrame - e.Arrived > Retention
        || !NetSession.MatchesStream(e.Key.MatchId, e.Key.AuthorityEpoch)
        || !NetPlayerLifecycle.Matches(e.Key.ShooterSlot, e.Key.Generation, e.Key.LifeId);
    internal static void Observe(int slot, in IntentPacket intent)
    {
        if (!NetHitClaims.Arbitrating || (uint)slot >= 8
            || !NetSession.MatchesStream(intent.MatchId, intent.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(slot, intent.SlotGeneration, intent.LifeId)) return;
        for (int n = 0; n < intent.FireEventCount; n++)
        {
            var fire = intent.FireEvents[n];
            if (fire.Kind != FireEventKind.ContinuousTick || fire.Weapon != (byte)BeamType.ShockCoil
                || fire.ShotId == 0 || fire.ContinuousPhase == 0 || fire.ContinuousPhase != fire.SourceFrame
                || !fire.HasPose || !fire.Target.IsWellFormed) continue;
            ushort life = fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId;
            if (life != intent.LifeId) continue;
            var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot, intent.SlotGeneration, life, fire.ShotId);
            int free = -1, found = -1;
            for (int i = 0; i < Capacity; i++)
            {
                ref var e = ref Entries[slot, i];
                if (Expired(e)) { if (free < 0) free = i; continue; }
                if (e.Key == key || e.Fire.ContinuousPhase == fire.ContinuousPhase) { found = i; break; }
            }
            if (found >= 0)
            {
                ref var prior = ref Entries[slot, found];
                if (prior.Key != key || prior.Fire != fire) prior.Conflict = true;
            }
            else if (free >= 0) Entries[slot, free] = new Entry
                { Live = true, Key = key, Fire = fire, Arrived = NetSession.NetFrame };
        }
    }
    private static bool Find(int slot, in HitClaimPacket claim, out FireEvent fire)
    {
        fire = default;
        if ((uint)slot >= 8) return false;
        var key = NetShotProof.Key(slot, claim);
        for (int i = 0; i < Capacity; i++)
        {
            ref var e = ref Entries[slot, i];
            if (!Expired(e) && !e.Conflict && !e.Rejected && e.Key == key && e.Fire.ContinuousPhase == claim.Frame)
            { fire = e.Fire; return true; }
        }
        return false;
    }
    internal static void Reject(in ShotKey key, in FireEvent fire)
    {
        if ((uint)key.ShooterSlot >= 8) return;
        for (int i = 0; i < Capacity; i++)
        {
            ref var entry = ref Entries[key.ShooterSlot, i];
            if (entry.Live && entry.Key == key && entry.Fire == fire) entry.Rejected = true;
        }
    }
    // Arrival may precede its repeated fire-event carrier. Grace permits recovery;
    // settlement below still requires the exact immutable decision.
    internal static bool Known(int slot, in HitClaimPacket claim) => Find(slot, claim, out _);
    internal static bool Validate(int slot, in HitClaimPacket claim, out bool lifeDrain)
    {
        lifeDrain = false;
        if (!Find(slot, claim, out var fire) || (claim.Flags & HitClaimPacket.FlagContinuousTick) == 0
            || (claim.Flags & HitClaimPacket.FlagHeadshot) != 0 || claim.Frame % 2 != 0
            || fire.Target != new NetTargetIdentity((byte)(NetTargetIdentity.Valid | (claim.VictimSlot + 1)),
                claim.VictimGeneration, claim.VictimLifeId)
            || claim.AckFrame != fire.AckFrame || !NetPlayerLifecycle.Matches(claim.VictimSlot, claim.VictimGeneration, claim.VictimLifeId)
            || (uint)slot >= PlayerEntity.Players.Count || claim.VictimSlot >= PlayerEntity.Players.Count) return false;
        var owner = PlayerEntity.Players[slot]; var victim = PlayerEntity.Players[claim.VictimSlot];
        var scene = owner.OwningScene;
        bool affinity = Weapons.GetAffinityBeam(owner.Hunter) == BeamType.ShockCoil || (fire.HasContinuousContext ? fire.ContinuousPrime : owner.IsPrimeHunter);
        var weapon = scene.WeaponRules[(int)BeamType.ShockCoil + (affinity ? 9 : 0)];
        bool charged = weapon.Flags.TestFlag(WeaponFlags.CanCharge)
            && fire.Charge >= (weapon.Flags.TestFlag(WeaponFlags.PartialCharge) ? weapon.MinCharge : weapon.FullCharge) * 2;
        if (!NetUnlagged.TryContinuousClaimPose(victim, fire.AckFrame, out var pose, out bool morphing, out int team)) return false;
        Vector3 target = pose.Position.AddY(pose.AltForm ? 0 : .5f);
        if (NetContinuousTargeting.Evaluate(fire.Origin, fire.Direction, target,
            victim != owner && victim.ModIsInPlay && victim.LoadFlags.TestFlag(LoadFlags.Active)
                && !victim.Flags2.TestFlag(PlayerFlags2.Spectating), victim.GetTargetable(),
            TeamRules.AreAllies(owner.TeamIndex, team), pose.AltForm, morphing,
            Fixed.ToFloat(weapon.HomingRange), Fixed.ToFloat(weapon.HomingTolerance), out _, out _, out _)
                != ContinuousTargetRejection.None) return false;
        if (!NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene, fire.Origin, target, fire.AckFrame, out float wall)
            || wall < .999f) return false;
        // Keep the native fractional pulse boundary. Modifier/source enforcement
        // remains N4 shadow policy; this ceiling never invents an odd-frame tick.
        int raw = Math.Max(weapon.UnchargedDamage, Math.Max(weapon.MinChargeDamage, weapon.ChargedDamage));
        int maximum = ContinuousWeaponPhase.Amount(raw * 2, fire.ContinuousPhase, damage: true) * 4;
        maximum = (int)Math.Ceiling(maximum * Metadata.DamageLevels[scene.GameState.DamageLevel] * 2);
        if (claim.Damage <= 0 || claim.Damage > maximum) return false;
        byte allowedAfflictions = NetHitClaims.AfflictionClaimFlags(weapon.Afflictions[charged ? 1 : 0]);
        if ((claim.Flags & (HitClaimPacket.FlagFrozen | HitClaimPacket.FlagBurning | HitClaimPacket.FlagDisrupted)
            & ~allowedAfflictions) != 0) return false;
        lifeDrain = !fire.ContinuousPrime && weapon.Flags.TestFlag(charged ? WeaponFlags.LifeDrainCharged : WeaponFlags.LifeDrainUncharged);
        return true;
    }
    internal static void ForgetSlot(int slot)
    { if ((uint)slot < 8) for (int i = 0; i < Capacity; i++) Entries[slot, i] = default; }
    internal static void Reset() => Array.Clear(Entries);
}
