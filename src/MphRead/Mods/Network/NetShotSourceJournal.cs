using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Authority resource operations, independent of the inventory overwritten by intents.
/// Grants take effect after the last applied owner input, never retroactively. A retained
/// source-time checkpoint allows late shots without refunding old expenditure.</summary>
internal static class NetShotSourceJournal
{
    internal const int Capacity = 512, ReorderFrames = 256;
    private enum Kind { Ammo, Weapon, Double, Prime, Spend }
    private readonly record struct Operation(uint Frame, long Order, Kind Kind, int A, int B,
        ShotKey Key = default, int Cooldown = 0, byte Weapon = 0);
    private sealed class Life
    {
        internal ShotKey Key;
        internal ShotProofSource Baseline;
        internal uint Birth, Frontier, Floor;
        internal bool HasFrontier, HasFloor, Invalid;
        internal int DoubleTicks;
        internal long Order;
        internal Operation? LastSpend;
        internal readonly List<Operation> Operations = new(Capacity);
    }
    private static readonly Life?[,] Lives = new Life?[8, 8];
    internal static long CapacityRefused, Compacted;
    private static bool Active => NetHitClaims.Arbitrating && NetShotProof.Mode != ShotProofMode.Off;
    private static bool SameLife(ShotKey a, ShotKey b) => a with { ShotId = 0 } == b with { ShotId = 0 };
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static Life? Get(ShotKey key) => (uint)key.ShooterSlot < 8
        && Lives[key.ShooterSlot, key.LifeId % 8] is { } life && SameLife(life.Key, key) ? life : null;

    internal static void Seed(PlayerEntity player, ShotProofSource source)
    {
        if (!Active || (uint)player.SlotIndex >= 8) return;
        var key = ShotKey.For(player.SlotIndex, 0);
        Lives[key.ShooterSlot, key.LifeId % 8] = new Life { Key = key, Baseline = source,
            Birth = NetSession.NetFrame, DoubleTicks = source.Double ? 1800 : 0 };
    }
    internal static void Observe(int slot, in IntentPacket intent)
    {
        if (!Active) return;
        var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot, intent.SlotGeneration, intent.LifeId, 0);
        var life = Get(key);
        if (life == null) return;
        if (!life.HasFrontier || Before(life.Frontier, intent.Frame))
        { life.Frontier = intent.Frame; life.HasFrontier = true; Compact(life); }
    }
    internal static uint? Birth(ShotKey key) => Get(key)?.Birth;
    internal static void Invalidate(PlayerEntity player)
    { if (Active && Get(ShotKey.For(player.SlotIndex, 0)) is { } life) life.Invalid = true; }

    // Source and authority clocks are separate. A successful native pickup occurs
    // after the input/shot phase which used this position. It cannot fund that input.
    private static bool Boundary(PlayerEntity player, out Life life, out uint frame)
    {
        life = Get(ShotKey.For(player.SlotIndex, 0))!; frame = 0;
        if (!Active || life == null || life.Invalid || !life.Baseline.Supported) return false;
        int slot = player.SlotIndex;
        if (player.IsBot || slot == NetSession.LocalSlot) frame = NetSession.NetFrame + 1;
        else if (NetSession.RemoteIntentValid[slot]
            && NetSession.RemoteIntents[slot].LifeId == life.Key.LifeId
            && NetSession.RemoteIntents[slot].SlotGeneration == life.Key.Generation
            && NetSession.RemoteIntents[slot].MatchId == life.Key.MatchId
            && NetSession.RemoteIntents[slot].AuthorityEpoch == life.Key.AuthorityEpoch)
            frame = NetSession.RemoteIntents[slot].Frame + 1;
        else { life.Invalid = true; return false; }
        return true;
    }
    private static void Add(Life life, Operation operation)
    {
        Compact(life);
        if (life.Operations.Count == Capacity || life.HasFloor && Before(operation.Frame, life.Floor))
        { life.Invalid = true; CapacityRefused++; return; }
        life.Operations.Add(operation);
        life.Operations.Sort(Compare);
    }
    private static int Compare(Operation a, Operation b)
    {
        int time = unchecked((int)(a.Frame - b.Frame));
        if (time != 0) return time;
        // Grants at frame N describe the end of N-1. Shots are ordered by authored ID.
        if (a.Kind == Kind.Spend && b.Kind == Kind.Spend)
            return unchecked((int)(a.Key.ShotId - b.Key.ShotId));
        if (a.Kind == Kind.Spend) return 1;
        if (b.Kind == Kind.Spend) return -1;
        return a.Order.CompareTo(b.Order);
    }
    private static ShotProofSource Apply(ShotProofSource state, Operation op)
    {
        int ammo = op.A == 0 ? state.Ammo0 : state.Ammo1;
        int cap = op.A == 0 ? state.AmmoMax0 : state.AmmoMax1;
        switch (op.Kind)
        {
            case Kind.Ammo:
                ammo = (int)Math.Min(cap, (long)ammo + op.B);
                return op.A == 0 ? state with { Ammo0 = ammo } : state with { Ammo1 = ammo };
            case Kind.Weapon:
                state = state with { Weapons = (ushort)(state.Weapons | (1 << op.A)),
                    Charges = (ushort)(state.Charges | (1 << op.A)) };
                // Native ordinary weapon pickup refills toward 60, never above an existing reserve.
                if (op.B is 0 or 1)
                    state = op.B == 0 ? state with { Ammo0 = Math.Max(state.Ammo0, 60) }
                        : state with { Ammo1 = Math.Max(state.Ammo1, 60) };
                return state;
            case Kind.Double: return state with { Double = op.A != 0 };
            case Kind.Prime: return state with { Prime = op.A != 0 };
            case Kind.Spend:
                if (!state.Infinite && op.A is 0 or 1)
                    state = op.A == 0 ? state with { Ammo0 = state.Ammo0 - op.B }
                        : state with { Ammo1 = state.Ammo1 - op.B };
                if (op.Weapon == (byte)BeamType.OmegaCannon) // Omega is a single grant, independent of native intent inventory.
                    state = state with { Weapons = (ushort)(state.Weapons & ~(1 << (int)BeamType.OmegaCannon)),
                        Charges = (ushort)(state.Charges & ~(1 << (int)BeamType.OmegaCannon)) };
                return state;
        }
        return state;
    }
    private static void Compact(Life life)
    {
        if (!life.HasFrontier) return;
        uint floor = life.Frontier - ReorderFrames;
        if (life.HasFloor && !Before(life.Floor, floor)) return;
        int count = 0;
        while (count < life.Operations.Count && Before(life.Operations[count].Frame, floor))
        {
            var op = life.Operations[count++];
            life.Baseline = Apply(life.Baseline, op);
            if (op.Kind == Kind.Spend) life.LastSpend = op;
            Compacted++;
        }
        if (count != 0) life.Operations.RemoveRange(0, count);
        life.Floor = floor; life.HasFloor = true;
    }
    internal static bool TryState(ShotKey key, uint frame, out ShotProofSource state)
    {
        state = default;
        var life = Get(key);
        if (life == null || life.Invalid || !life.Baseline.Supported || life.HasFloor && Before(frame, life.Floor)) return false;
        state = life.Baseline;
        foreach (var op in life.Operations)
        {
            if (Before(frame, op.Frame)) break;
            // Input/charge advances before shots in the same source frame.
            if (op.Kind == Kind.Spend && op.Frame == frame) continue;
            state = Apply(state, op);
        }
        return true;
    }
    internal static ShotProofReason Inspect(ShotKey key, in FireEvent fire, int ammoType, int cost, int cooldown)
    {
        var life = Get(key);
        if (life == null || !life.Baseline.Supported) return ShotProofReason.SourceUnavailable;
        if (life.Invalid) return ShotProofReason.SourceChanged;
        if (life.HasFloor && Before(fire.SourceFrame, life.Floor)) return ShotProofReason.SourceHistoryIncomplete;
        if (life.Operations.Count >= Capacity) return ShotProofReason.Capacity;
        var candidate = new Operation(fire.SourceFrame, 0, Kind.Spend, ammoType, cost, key,
            cooldown, fire.Weapon);
        if (life.LastSpend is { } last && unchecked((int)(fire.SourceFrame - last.Frame)) < cooldown)
            return ShotProofReason.FireRate;
        ShotProofSource state = life.Baseline;
        bool inserted = false;
        foreach (var op in life.Operations)
        {
            if (op.Kind == Kind.Spend)
            {
                if (op.Key == key) return ShotProofReason.Verified;
                long gap = unchecked((int)(fire.SourceFrame - op.Frame));
                if (gap >= 0 ? gap < cooldown : -gap < Math.Abs(op.Cooldown)) return ShotProofReason.FireRate;
            }
            if (!inserted && Compare(candidate, op) < 0)
            {
                if ((state.Weapons & (1 << fire.Weapon)) == 0) return ShotProofReason.UnownedWeapon;
                state = Apply(state, candidate); inserted = true;
                if (!state.Infinite && (state.Ammo0 < 0 || state.Ammo1 < 0)) return ShotProofReason.Ammunition;
            }
            if (op.Kind == Kind.Spend && (state.Weapons & (1 << op.Weapon)) == 0)
                return ShotProofReason.UnownedWeapon;
            state = Apply(state, op);
            if (!state.Infinite && (state.Ammo0 < 0 || state.Ammo1 < 0)) return ShotProofReason.Ammunition;
        }
        if (!inserted)
        {
            if ((state.Weapons & (1 << fire.Weapon)) == 0) return ShotProofReason.UnownedWeapon;
            state = Apply(state, candidate);
        }
        return !state.Infinite && (state.Ammo0 < 0 || state.Ammo1 < 0)
            ? ShotProofReason.Ammunition : ShotProofReason.Verified;
    }
    internal static void Fired(ShotKey key, in FireEvent fire, int ammoType, int cost, int cooldown)
    {
        var life = Get(key);
        if (life == null || life.Invalid) return;
        foreach (var op in life.Operations) if (op.Kind == Kind.Spend && op.Key == key) return;
        Add(life, new(fire.SourceFrame, ++life.Order, Kind.Spend, ammoType, cost, key,
            cooldown, fire.Weapon));
    }
    internal static void GrantAmmo(PlayerEntity player, int ammoType, int amount)
    {
        if (ammoType is not (0 or 1) || amount < 0 || !Boundary(player, out var life, out uint frame)) return;
        Add(life, new(frame, ++life.Order, Kind.Ammo, ammoType, amount));
    }
    internal static void GrantWeapon(PlayerEntity player, BeamType weapon, int ammoType, int minimumAmmo)
    {
        if ((uint)weapon > 8 || !Boundary(player, out var life, out uint frame)) return;
        if (minimumAmmo != 60) { life.Invalid = true; return; }
        Add(life, new(frame, ++life.Order, Kind.Weapon, (int)weapon, ammoType));
    }
    internal static void SetDouble(PlayerEntity player, int ticks)
    {
        if (!Boundary(player, out var life, out uint frame)) return;
        life.DoubleTicks = ticks;
        Add(life, new(frame, ++life.Order, Kind.Double, ticks > 0 ? 1 : 0, 0));
    }
    internal static void Tick(PlayerEntity player)
    {
        if (!Active || Get(ShotKey.For(player.SlotIndex, 0)) is not { } life) return;
        if (player.Health == 0) { if (life.DoubleTicks > 0) SetDouble(player, 0); return; }
        if (life.DoubleTicks > 0 && --life.DoubleTicks == 0) SetDouble(player, 0);
    }
    internal static void PrimeChanged(Scene scene)
    {
        if (!Active) return;
        foreach (var player in scene.Entities)
            if (player is PlayerEntity p && Boundary(p, out var life, out uint frame))
                Add(life, new(frame, ++life.Order, Kind.Prime, scene.GameState.PrimeHunter == p.SlotIndex ? 1 : 0, 0));
    }
    internal static object Capture() => new { CapacityRefused, Compacted };
    internal static void Reset() => Array.Clear(Lives);
}
