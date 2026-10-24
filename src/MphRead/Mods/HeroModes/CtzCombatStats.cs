using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.HeroModes;

/// <summary>Full-match CTZ totals. Life changes do not reset totals; slot replacements do.
/// Healing records actual allied health restored, never attempted healing, overheal,
/// health pickups or self-sustain. Damage taken is actual HP lost after protection/DR,
/// including environmental/self damage and Weavel's shared body/turret pool.</summary>
internal sealed class CtzCombatStats
{
    private readonly CtzCombatRow[] _rows = new CtzCombatRow[PlayerEntity.SlotCapacity];
    private readonly bool[] _known = new bool[PlayerEntity.SlotCapacity];
    internal void Reset() { Array.Clear(_rows); Array.Clear(_known); }
    internal void ForgetSlot(int slot)
    { if ((uint)slot < _rows.Length) { _known[slot] = false; _rows[slot] = default; } }
    private ref CtzCombatRow Row(int slot, ushort generation)
    {
        if ((uint)slot >= _rows.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        if (!_known[slot] || _rows[slot].Generation != generation)
        { _rows[slot] = new((byte)slot, generation, 0, 0, 0); _known[slot] = true; }
        return ref _rows[slot];
    }
    internal void DamageDone(int slot, int amount)
    { ref var r=ref Row(slot,NetPlayerLifecycle.Generation(slot));r=r with { Damage=Add(r.Damage,amount) }; }
    internal void Assist(int slot)
    { ref var r=ref Row(slot,NetPlayerLifecycle.Generation(slot));r=r with { Assists=Add(r.Assists,1) }; }
    internal void Objective(int slot,int amount=1,bool secondary=false)
    { ref var r=ref Row(slot,NetPlayerLifecycle.Generation(slot));r=secondary?r with { Secondary=Add(r.Secondary,amount) }:r with { Objective=Add(r.Objective,amount) }; }
    internal CtzCombatRow Get(int slot) => Row(slot, NetPlayerLifecycle.Generation(slot));
    private static int Add(int value, int delta) => (int)Math.Min(int.MaxValue, (long)value + Math.Max(0, delta));
    internal void HealingReceived(int slot, int amount)
    { ref var r = ref Row(slot, NetPlayerLifecycle.Generation(slot)); r = r with { Received = Add(r.Received, amount) }; }
    internal void HealingDone(int slot, ushort generation, int amount)
    {
        // An aura may continue after its owner disconnects. Never credit their replacement.
        if ((uint)slot >= _rows.Length || NetPlayerLifecycle.Generation(slot) != generation) return;
        ref var r = ref Row(slot, generation); r = r with { Done = Add(r.Done, amount) };
    }
    internal void DamageTaken(int slot, int amount)
    { ref var r = ref Row(slot, NetPlayerLifecycle.Generation(slot)); r = r with { Taken = Add(r.Taken, amount) }; }
    internal void Capture(Span<CtzCombatRow> rows)
    {
        if (rows.Length != PlayerEntity.SlotCapacity) throw new ArgumentException("CTZ totals require eight slots.");
        for (int slot = 0; slot < rows.Length; slot++) rows[slot] = Get(slot);
    }
    internal CtzCombatRow[] Capture()
    { var result = new CtzCombatRow[PlayerEntity.SlotCapacity]; Capture(result); return result; }
    internal void Apply(ReadOnlySpan<CtzCombatRow> rows)
    {
        // Called only after the normal server/match/epoch/tick fences. Assignment,
        // rather than addition, makes duplicate full snapshots idempotent.
        foreach (var r in rows)
            if (r.Valid && NetPlayerLifecycle.Generation(r.Slot) == r.Generation)
            { _rows[r.Slot] = r; _known[r.Slot] = true; }
    }
    internal static bool CanRecord(Scene scene) => scene.GameState.Multiplayer && !scene.Services.IsReplica && !scene.Services.SuppressDamage
        && scene.AimTrainer == null && (!NetSession.Active || NetSession.IsAuthority)
        && !NetDamage.Replaying && !NetHitPrediction.Predicting;
}

internal readonly record struct CtzCombatRow(byte Slot, ushort Generation, int Done, int Received, int Taken, int Damage=0, int Assists=0, int Objective=0, int Secondary=0)
{
    internal bool Valid => Slot < PlayerEntity.SlotCapacity && Done >= 0 && Received >= 0 && Taken >= 0 && Damage >= 0 && Assists >= 0 && Objective >= 0 && Secondary >= 0;
    internal void Write(BinaryWriter w, bool extended=true)
    {
        if (!Valid) throw new InvalidDataException("Invalid CTZ combat totals.");
        w.Write(Slot); w.Write(Generation); w.Write(Done); w.Write(Received); w.Write(Taken);
        if(extended){w.Write(Damage);w.Write(Assists);w.Write(Objective);w.Write(Secondary);}
    }
    internal static CtzCombatRow Read(BinaryReader r, bool extended=true)
    {
        var row = new CtzCombatRow(r.ReadByte(), r.ReadUInt16(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        if(extended) row=row with { Damage=r.ReadInt32(),Assists=r.ReadInt32(),Objective=r.ReadInt32(),Secondary=r.ReadInt32() };
        if (!row.Valid) throw new InvalidDataException("Invalid CTZ combat totals.");
        return row;
    }
}
