using System;
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
        public bool Live, Paid;
        public ShotPaymentKey Key;
        public uint Frame;
        public byte Weapon;
        public CombatAckEntry Outcome;
    }
    private static readonly Entry[,] Entries = new Entry[8, CapacityPerShooter];
    internal static long Suppressed, CapacityRefused, Payments;
    // Per-process evidence survives the authority clearing its final empty room.
    internal static long LifetimePayments, LifetimeSuppressed, LifetimeCapacityRefused;
    internal static object Capture() => new { LifetimePayments, LifetimeSuppressed,
        LifetimeCapacityRefused, CapacityPerShooter, Retention };
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
    internal readonly struct Reservation : IDisposable
    {
        private readonly int _slot, _at;
        private readonly ShotPaymentKey _key;
        public bool Allowed { get; }
        internal Reservation(bool allowed, int slot = -1, int at = -1, ShotPaymentKey key = default)
        { Allowed = allowed; _slot = slot; _at = at; _key = key; }
        internal void Commit(CombatAckEntry outcome)
        {
            if (_at < 0) return;
            ref var entry = ref Entries[_slot, _at];
            if (entry.Key != _key || entry.Paid) return;
            outcome.ShotId = _key.Shot.ShotId;
            entry.Outcome = outcome; entry.Paid = true; Payments++; LifetimePayments++;
            NetSelfSplash.RecordPayment(_key, outcome);
        }
        public void Dispose()
        {
            if (_at >= 0 && Entries[_slot, _at].Key == _key && !Entries[_slot, _at].Paid)
                Entries[_slot, _at] = default;
        }
    }
    internal static Reservation Begin(in ShotPaymentKey key, byte weapon)
    {
        if (!Valid(key)) return new(true);
        if (Find(key) >= 0) { Suppressed++; LifetimeSuppressed++; return new(false); }
        for (int i = 0; i < CapacityPerShooter; i++)
            if (Expired(Entries[key.Shot.ShooterSlot, i]))
            {
                Entries[key.Shot.ShooterSlot, i] = new Entry { Live = true, Key = key,
                    Weapon = weapon, Frame = NetSession.NetFrame };
                return new(true, key.Shot.ShooterSlot, i, key);
            }
        CapacityRefused++; LifetimeCapacityRefused++; return new(false);
    }
    internal static Reservation Begin(PlayerEntity victim, EntityBase? source, DamageFlags flags)
    {
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
    }
    internal static void ForgetSlot(int slot, bool preserveFlights)
    {
        if (preserveFlights) return; // Full life identity protects replacements on respawn.
        for (int owner = 0; owner < 8; owner++)
            for (int i = 0; i < CapacityPerShooter; i++)
                if (owner == slot || Entries[owner, i].Key.Victim == slot) Entries[owner, i] = default;
    }
    internal static void Reset()
    { Array.Clear(Entries); Suppressed = CapacityRefused = Payments = 0; }
}
