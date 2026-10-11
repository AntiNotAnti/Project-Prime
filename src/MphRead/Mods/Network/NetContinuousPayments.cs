using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

// ShotId is the owner's authored continuous phase, independent of emitted shot IDs.
internal readonly record struct ContinuousPaymentKey(ShotKey OwnerTick, NetTargetIdentity Victim, byte Flags)
{
    internal const byte Body = 0, Turret = 1;
}

/// <summary>One authority damage/heal transaction per lifecycle-fenced owner tick.
/// Reservations pin identity before effects. Repeated ticks targeting a different
/// body cannot redirect an existing payment or apply another heal.</summary>
internal static class NetContinuousPayments
{
    internal const int CapacityPerOwner = 1024;
    internal const uint Retention = 1024;
    private struct Entry
    {
        public bool Live, Paid;
        public ContinuousPaymentKey Key;
        public uint Frame;
        public CombatAckEntry Outcome;
        public int HealBody, HealTurret, DamageTurret;
    }
    private struct PhaseWindow
    {
        public bool Valid;
        public ShotKey Context;
        public uint Newest;
        public ulong Low, High;
    }
    private static readonly Entry[,] Entries = new Entry[8, CapacityPerOwner];
    private static readonly PhaseWindow[] Windows = new PhaseWindow[8];
    private const int EvidenceCapacity = 8192;
    private static readonly Entry[] Evidence = new Entry[EvidenceCapacity];
    private static int EvidenceHead, EvidenceCount;
    internal static long Payments, Suppressed, TargetConflicts, CapacityRefused;
    internal static long LifetimePayments, LifetimeSuppressed, LifetimeTargetConflicts, LifetimeCapacityRefused;
    internal static long LifetimeDamage, LifetimeTurretDamage, LifetimeHealBody, LifetimeHealTurret;
    internal static bool EvidenceEnabled;

    private static bool Valid(in ContinuousPaymentKey key) => (uint)key.OwnerTick.ShooterSlot < 8
        && key.OwnerTick.ShotId != 0 && key.OwnerTick.Generation != 0 && key.OwnerTick.LifeId != 0
        && key.Victim.IsWellFormed && key.Victim.HasPlayer && (key.Flags & ~1) == 0
        && NetSession.MatchesStream(key.OwnerTick.MatchId, key.OwnerTick.AuthorityEpoch)
        && NetPlayerLifecycle.Matches(key.OwnerTick.ShooterSlot, key.OwnerTick.Generation, key.OwnerTick.LifeId)
        && NetPlayerLifecycle.Matches(key.Victim.Slot, key.Victim.Generation, key.Victim.LifeId);
    private static bool Expired(in Entry entry) => !entry.Live
        || unchecked(NetSession.NetFrame - entry.Frame) > Retention
        || !NetSession.MatchesStream(entry.Key.OwnerTick.MatchId, entry.Key.OwnerTick.AuthorityEpoch)
        || entry.Key.OwnerTick.Generation != NetPlayerLifecycle.Generation(entry.Key.OwnerTick.ShooterSlot);
    private static int FindTick(in ContinuousPaymentKey key)
    {
        if (!Valid(key)) return -1;
        for (int i = 0; i < CapacityPerOwner; i++)
            if (!Expired(Entries[key.OwnerTick.ShooterSlot, i])
                && Entries[key.OwnerTick.ShooterSlot, i].Key.OwnerTick == key.OwnerTick) return i;
        return -1;
    }
    internal static bool TryGet(in ContinuousPaymentKey key, out CombatAckEntry outcome,
        out int healBody, out int healTurret)
    {
        int at = FindTick(key);
        if (at >= 0)
        {
            ref var entry = ref Entries[key.OwnerTick.ShooterSlot, at];
            if (entry.Paid && entry.Key == key)
            { outcome = entry.Outcome; healBody = entry.HealBody; healTurret = entry.HealTurret; return true; }
        }
        outcome = default; healBody = healTurret = 0; return false;
    }
    internal readonly struct Reservation : IDisposable
    {
        private readonly int _slot, _at;
        private readonly ContinuousPaymentKey _key;
        public bool Allowed { get; }
        internal Reservation(bool allowed, int slot = -1, int at = -1, ContinuousPaymentKey key = default)
        { Allowed = allowed; _slot = slot; _at = at; _key = key; }
        // Caller commits only after an actual body or turret reduction. Keep the
        // original emitted shot correlation in Outcome; OwnerTick is the phase.
        internal void Commit(in CombatAckEntry outcome, int healBody, int healTurret, int actualTurretDamage = 0)
        {
            if (!Allowed || _at < 0) return;
            ref var entry = ref Entries[_slot, _at];
            if (!entry.Live || entry.Key != _key || entry.Paid) return;
            entry.Outcome = outcome; entry.HealBody = Math.Max(0, healBody);
            entry.HealTurret = Math.Max(0, healTurret); entry.DamageTurret = Math.Max(0, actualTurretDamage); entry.Paid = true;
            Payments++; LifetimePayments++; LifetimeDamage += outcome.DamageApplied; LifetimeTurretDamage += entry.DamageTurret;
            LifetimeHealBody += entry.HealBody; LifetimeHealTurret += entry.HealTurret;
            RecordPhase(_key.OwnerTick);
            if (EvidenceEnabled || NetCampaignEvidence.Enabled)
            {
                int tail = (EvidenceHead + EvidenceCount) % EvidenceCapacity;
                Evidence[tail] = entry;
                if (EvidenceCount < EvidenceCapacity) EvidenceCount++;
                else EvidenceHead = (EvidenceHead + 1) % EvidenceCapacity;
            }
        }
        public void Dispose()
        {
            if (_at >= 0 && Entries[_slot, _at].Live && Entries[_slot, _at].Key == _key
                && !Entries[_slot, _at].Paid) Entries[_slot, _at] = default;
        }
    }
    internal static Reservation Begin(in ContinuousPaymentKey key)
    {
        if (!Valid(key)) return new(false);
        int found = FindTick(key);
        if (found >= 0)
        {
            Suppressed++; LifetimeSuppressed++;
            if (Entries[key.OwnerTick.ShooterSlot, found].Key != key)
            { TargetConflicts++; LifetimeTargetConflicts++; }
            return new(false);
        }
        for (int i = 0; i < CapacityPerOwner; i++)
            if (Expired(Entries[key.OwnerTick.ShooterSlot, i]))
            {
                Entries[key.OwnerTick.ShooterSlot, i] = new Entry
                { Live = true, Key = key, Frame = NetSession.NetFrame };
                return new(true, key.OwnerTick.ShooterSlot, i, key);
            }
        CapacityRefused++; LifetimeCapacityRefused++; return new(false);
    }

    internal static (int Body, int Turret) Heal(PlayerEntity owner, PlayerEntity victim,
        int actualBodyDamage, int actualTurretDamage, bool lifeDrain)
    {
        long removed = (long)Math.Max(0, actualBodyDamage) + Math.Max(0, actualTurretDamage);
        var scene = owner.OwningScene;
        if (!lifeDrain || removed == 0 || owner == victim || owner.Health <= 0 || owner.IsPrimeHunter
            || TeamRules.AreAllies(owner.TeamIndex, victim.TeamIndex) || scene.Services.IsReplica
            || NetDamage.Replaying || NetSession.Active && !NetSession.IsAuthority && !NetSession.IsHost
                && owner.SlotIndex != NetSession.LocalSlot) return default;
        int requested = (int)Math.Min(int.MaxValue, removed);
        if (scene.GameState.Multiplayer && scene.GameState.BalancedMode && owner.Hunter == Hunter.Sylux
            && BalancedModeRules.IsAffinity(owner.Hunter, BeamType.ShockCoil))
            requested = owner.ModBalancedLifeDrainHeal(requested, Math.Max(0, owner.HealthMax - owner.Health));
        if (requested <= 0) return default;
        int body = owner.Health, turret = owner.Halfturret?.Health ?? 0;
        owner.GainHealth(requested);
        return (Math.Max(0, owner.Health - body), Math.Max(0, (owner.Halfturret?.Health ?? 0) - turret));
    }

    private static bool SameContext(in ShotKey a, in ShotKey b) => a.AuthorityEpoch == b.AuthorityEpoch
        && a.MatchId == b.MatchId && a.ShooterSlot == b.ShooterSlot
        && a.Generation == b.Generation && a.LifeId == b.LifeId;
    private static void RecordPhase(in ShotKey key)
    {
        ref var window = ref Windows[key.ShooterSlot]; uint phase = key.ShotId;
        if (!window.Valid || !SameContext(window.Context, key))
        { window = new PhaseWindow { Valid = true, Context = key, Newest = phase, Low = 1 }; return; }
        if (SequenceMath.Newer(phase, window.Newest))
        {
            uint shift = unchecked(phase - window.Newest);
            if (shift >= 128) { window.Low = 0; window.High = 0; }
            else if (shift >= 64) { window.High = window.Low << (int)(shift - 64); window.Low = 0; }
            else if (shift != 0)
            { window.High = (window.High << (int)shift) | (window.Low >> (int)(64 - shift)); window.Low <<= (int)shift; }
            window.Newest = phase; window.Low |= 1;
        }
        else
        {
            uint age = unchecked(window.Newest - phase);
            if (age < 64) window.Low |= 1UL << (int)age;
            else if (age < 128) window.High |= 1UL << (int)(age - 64);
        }
    }
    internal static bool TryCapturePhase(int slot, out uint newest, out ulong low, out ulong high)
    {
        newest = 0; low = high = 0;
        if ((uint)slot >= 8) return false;
        ref var window = ref Windows[slot];
        if (!window.Valid || !NetSession.MatchesStream(window.Context.MatchId, window.Context.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(slot, window.Context.Generation, window.Context.LifeId)) return false;
        newest = window.Newest; low = window.Low; high = window.High; return true;
    }
    internal static object Capture()
    {
        var rows = new List<object>();
        if (EvidenceEnabled || NetCampaignEvidence.Enabled)
            for (int i = 0; i < EvidenceCount; i++)
            {
                var entry = Evidence[(EvidenceHead + i) % EvidenceCapacity];
                var outcome = entry.Outcome;
                rows.Add(new { entry.Key, entry.Frame,
                    Outcome = new { outcome.ShotId, outcome.ClaimId, outcome.Result, outcome.VictimSlot,
                        outcome.VictimGeneration, outcome.VictimLife, outcome.DamageApplied, outcome.HealthAfter,
                        outcome.HalfturretHealthAfter, outcome.DamageSequence, outcome.Flags },
                    entry.DamageTurret, entry.HealBody, entry.HealTurret });
            }
        return new { LifetimePayments, LifetimeSuppressed, LifetimeTargetConflicts, LifetimeCapacityRefused,
            LifetimeDamage, LifetimeTurretDamage, LifetimeHealBody, LifetimeHealTurret, CapacityPerOwner, Retention,
            EvidenceCapacity, EvidenceCount, Rows = rows };
    }
    internal static void ForgetSlot(int slot, bool preserveFlights = false)
    {
        if ((uint)slot >= 8) return;
        Windows[slot] = default;
        if (preserveFlights) return;
        // Victim replacement must not free another owner's tick for redirection.
        // Owner generation/life and retention fence these tombstones independently.
        Array.Clear(Entries, slot * CapacityPerOwner, CapacityPerOwner);
    }
    internal static void Reset()
    { Array.Clear(Entries); Array.Clear(Windows); Payments = Suppressed = TargetConflicts = CapacityRefused = 0; }
}
