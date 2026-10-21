using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Authority payment tombstones, independent of optional shot-proof policy.
/// Reserve before native effects; commit only a real health/turret reduction. A paid
/// component is never consumed by a retry. Direct, splash and child payments differ.</summary>
internal static class NetCombatPayments
{
    internal const int CapacityPerShooter = 4096;
    internal const uint Retention = 1024;
    private struct Entry
    {
        public bool Live, Paid, RescuedClaim;
        public ShotPaymentKey Key;
        public uint Frame;
        public byte Weapon;
        public CombatAckEntry Outcome;
    }
    private static readonly Entry[,] Entries = new Entry[8, CapacityPerShooter];
    // Avoid a second ledger scan on native contacts until a rescued claim
    // has actually paid for this shooter during the current match.
    private static readonly bool[] MayHaveRescuedClaim = new bool[8];
    internal static long Suppressed, CapacityRefused, Payments;
    // Per-process evidence survives the authority clearing its final empty room.
    internal static long LifetimePayments, LifetimeSuppressed, LifetimeCapacityRefused;
    private static readonly HashSet<ShotKey> PaidEvidence = new();
    private static long EvidenceOverflow;
    internal static object Capture() => new { PaidShotKeys = PaidEvidence, EvidenceOverflow, LifetimePayments, LifetimeSuppressed,
        LifetimeCapacityRefused, CapacityPerShooter, Retention, EvidenceCapacity = NetCampaignEvidence.EvidenceShotKeyLimit };
    private static bool Valid(in ShotPaymentKey key) => (uint)key.Shot.ShooterSlot < 8
        && key.Victim < 8 && key.Shot.ShotId != 0 && key.Component != 0
        && NetSession.MatchesStream(key.Shot.MatchId, key.Shot.AuthorityEpoch)
        && key.Shot.Generation == NetPlayerLifecycle.Generation(key.Shot.ShooterSlot)
        && key.Shot.Generation != 0 && key.Shot.LifeId != 0;
    private static bool Expired(in Entry entry) => !entry.Live
        || unchecked(NetSession.NetFrame - entry.Frame) > Retention
        || !NetSession.MatchesStream(entry.Key.Shot.MatchId, entry.Key.Shot.AuthorityEpoch)
        || entry.Key.Shot.Generation != NetPlayerLifecycle.Generation(entry.Key.Shot.ShooterSlot);
    private static int Find(in ShotPaymentKey key)
    {
        if (!Valid(key)) return -1;
        for (int i = 0; i < CapacityPerShooter; i++)
            if (!Expired(Entries[key.Shot.ShooterSlot, i]) && Entries[key.Shot.ShooterSlot, i].Key == key) return i;
        return -1;
    }
    internal static bool TryGet(in ShotPaymentKey key, byte weapon, out CombatAckEntry outcome)
    {
        int at = Find(key);
        if (at >= 0 && Entries[key.Shot.ShooterSlot, at].Paid && Entries[key.Shot.ShooterSlot, at].Weapon == weapon)
        { outcome = Entries[key.Shot.ShooterSlot, at].Outcome; return true; }
        outcome = default; return false;
    }
    // An individual native beam cannot hit one body directly AND explode against
    // that same body as a separate splash: CheckSplashDamage excludes colWith.
    // A client rescue can choose the opposite outcome from the authoritative
    // collision on a different rewind timeline. Only that cross-timeline pair
    // is exclusive. Two independent native contacts and different child
    // components/victims are deliberately not collapsed.
    // A direct player impact ends a Judicator/Magmaul parent; a later child
    // cannot be another outcome for that same victim if the parent already
    // paid a rescued direct claim. The inverse arrival order is identical.
    // Parent wall splash followed by child direct is legal and stays payable.
    private static bool DescendsFrom(uint child, uint parent)
    {
        if (child == 0 || parent == 0 || child == parent) return false;
        for (int depth = 0; depth < 8 && child != 0; depth++)
        {
            child >>= 4;
            if (child == parent) return true;
        }
        return false;
    }
    private static int FindAlternative(in ShotPaymentKey key, byte weapon, bool incomingClaim)
    {
        if (!Valid(key) || !incomingClaim && !MayHaveRescuedClaim[key.Shot.ShooterSlot]) return -1;
        for (int i = 0; i < CapacityPerShooter; i++)
        {
            ref readonly var entry = ref Entries[key.Shot.ShooterSlot, i];
            if (Expired(entry) || !entry.Paid || entry.Weapon != weapon
                || (!incomingClaim && !entry.RescuedClaim)
                || entry.Key.Shot != key.Shot || entry.Key.Victim != key.Victim
                || entry.Key.Generation != key.Generation || entry.Key.Life != key.Life
                || entry.Key.Phase != key.Phase
                || (entry.Key.Kind & 4) != (key.Kind & 4)) continue;
            int existingKind = entry.Key.Kind & 3, newKind = key.Kind & 3;
            bool oppositeCollisionKind = entry.Key.Component == key.Component
                && ((existingKind == 1 && newKind == 2) || (existingKind == 2 && newKind == 1));
            bool mutuallyExclusiveRicochet = weapon is (byte)BeamType.Judicator or (byte)BeamType.Magmaul
                && (existingKind == 1 && DescendsFrom(key.Component, entry.Key.Component)
                    || newKind == 1 && DescendsFrom(entry.Key.Component, key.Component));
            if (oppositeCollisionKind || mutuallyExclusiveRicochet) return i;
        }
        return -1;
    }
    internal static bool TryGetClaimOutcome(in ShotPaymentKey key, byte weapon, out CombatAckEntry outcome)
    {
        if (TryGet(key, weapon, out outcome)) return true;
        int at = FindAlternative(key, weapon, incomingClaim: true);
        if (at >= 0 && Entries[key.Shot.ShooterSlot, at].Weapon == weapon)
        { outcome = Entries[key.Shot.ShooterSlot, at].Outcome; return true; }
        outcome = default; return false;
    }
    // A read does not reserve, refresh, retire or correct the existing ledger.
    internal static FastPaymentFact Inspect(in ShotPaymentKey key, byte weapon)
    {
        int at = Find(key);
        if (at < 0) return default;
        ref readonly var entry = ref Entries[key.Shot.ShooterSlot, at];
        return new(entry.Paid ? FastPaymentKnowledge.Paid : FastPaymentKnowledge.Reserved,
            entry.Weapon == weapon, entry.Outcome);
    }
    internal readonly struct Reservation : IDisposable
    {
        private readonly int _slot, _at;
        private readonly ShotPaymentKey _key;
        public bool Allowed { get; }
        internal Reservation(bool allowed, int slot = -1, int at = -1, ShotPaymentKey key = default)
        { Allowed = allowed; _slot = slot; _at = at; _key = key; }
        internal void Commit(CombatAckEntry outcome, int actualTurretDamage = 0, ushort claimId = 0)
        {
            if (_at < 0) return;
            ref var entry = ref Entries[_slot, _at];
            if (entry.Key != _key || entry.Paid) return;
            outcome.ShotId = _key.Shot.ShotId;
            entry.Outcome = outcome; entry.Paid = true; Payments++; LifetimePayments++;
            if (entry.RescuedClaim) MayHaveRescuedClaim[_slot] = true;
            NetCampaignEvidence.TimelinePayment("payment-committed", _key, entry.Weapon, outcome: outcome, claimId: claimId);
            NetCombatFastPath.Payment(_key, outcome);
            NetSelfSplash.RecordPayment(_key, outcome);
            if (NetCampaignEvidence.Enabled)
            {
                NativeImpactWitness? witness = null;
                if (claimId == 0 && (outcome.DamageApplied > 0 || actualTurretDamage > 0)
                    && NetNativeImpactWitness.TryCapture(out var captured)
                    && captured.Payment == _key && captured.Weapon == entry.Weapon)
                    witness = captured.WithOutcome(outcome);
                NetTimedCampaignJoin.Payment(_key, entry.Weapon, entry.Frame, outcome, actualTurretDamage,
                    claimId, impact: witness);
            }
            if (NetCampaignEvidence.Enabled && !PaidEvidence.Contains(_key.Shot))
            {
                if (PaidEvidence.Count < NetCampaignEvidence.EvidenceShotKeyLimit) PaidEvidence.Add(_key.Shot);
                else EvidenceOverflow++;
            }
        }
        public void Dispose()
        {
            if (_at >= 0 && Entries[_slot, _at].Key == _key && !Entries[_slot, _at].Paid)
                Entries[_slot, _at] = default;
        }
    }
    internal static Reservation Begin(in ShotPaymentKey key, byte weapon, bool fromClaim = false)
    {
        if (!Valid(key)) return new(true);
        NetCampaignEvidence.TimelinePayment("payment-attempt", key, weapon);
        if (Find(key) >= 0 || FindAlternative(key, weapon, fromClaim) >= 0)
        {
            NetCampaignEvidence.TimelinePayment("payment-refused", key, weapon, result: 1);
            Suppressed++; LifetimeSuppressed++; return new(false);
        }
        for (int i = 0; i < CapacityPerShooter; i++)
            if (Expired(Entries[key.Shot.ShooterSlot, i]))
            {
                Entries[key.Shot.ShooterSlot, i] = new Entry { Live = true, Key = key,
                    Weapon = weapon, Frame = NetSession.NetFrame, RescuedClaim = fromClaim };
                return new(true, key.Shot.ShooterSlot, i, key);
            }
        NetCampaignEvidence.TimelinePayment("payment-refused", key, weapon, result: 2);
        CapacityRefused++; LifetimeCapacityRefused++; return new(false);
    }
    internal static Reservation Begin(PlayerEntity victim, EntityBase? source, DamageFlags flags)
    {
        if (source is BeamProjectileEntity { ModCustomShot: not null } custom)
            return new(custom.OwningScene == victim.OwningScene && (NetSession.Active
                ? victim.OwningScene.CustomCombat?.OwnsDamage(victim,custom) == true
                : Mods.CustomWeapons.PirateBlasterDefinition.IsProfile(custom.ModCustomDefinition)
                    ? victim.OwningScene.PirateBlaster?.TryReserveDamage(victim,custom) == true
                    : victim.OwningScene.OfflineWeapons?.IsEligible == true));
        if (!NetHitClaims.Arbitrating || NetDamage.ApplyingClaim || NetDamage.Replaying
            || source is not BeamProjectileEntity beam || beam.Beam == BeamType.ShockCoil
            || beam.EnhancedMicroSeeker || Mods.EnhancedHunters.EnhancedHunters.ApplyingBonus)
            return new(true);
        byte kind = (byte)((beam.EnhancedDirectHit ? 1 : 2) | (flags.TestFlag(DamageFlags.Halfturret) ? 4 : 0));
        return Begin(new ShotPaymentKey(beam.ModLaunchKey, (byte)victim.SlotIndex,
            NetPlayerLifecycle.Generation(victim.SlotIndex), NetPlayerLifecycle.Get(victim.SlotIndex),
            beam.ModComponentId, kind), (byte)beam.Beam);
    }
    // The existing shadow Imperialist body/head reconciliation can promote a
    // native body payment once. It never creates another component payment.
    internal static void Correct(in ShotPaymentKey key, CombatAckEntry outcome)
    {
        int at = Find(key);
        if (at < 0 || !Entries[key.Shot.ShooterSlot, at].Paid) return;
        outcome.ShotId = key.Shot.ShotId;
        Entries[key.Shot.ShooterSlot, at].Outcome = outcome;
        NetCombatFastPath.Correct(key);
        if (NetCampaignEvidence.Enabled)
            NetTimedCampaignJoin.Payment(key, Entries[key.Shot.ShooterSlot, at].Weapon,
                Entries[key.Shot.ShooterSlot, at].Frame, outcome, 0, outcome.ClaimId, correction: true);
    }
    internal static void ForgetSlot(int slot, bool preserveFlights)
    {
        if (preserveFlights) return; // Full life identity protects replacements on respawn.
        for (int owner = 0; owner < 8; owner++)
            for (int i = 0; i < CapacityPerShooter; i++)
                if (owner == slot || Entries[owner, i].Key.Victim == slot) Entries[owner, i] = default;
    }
    internal static void Reset()
    { Array.Clear(Entries); Array.Clear(MayHaveRescuedClaim); Suppressed = CapacityRefused = Payments = 0; }
}
