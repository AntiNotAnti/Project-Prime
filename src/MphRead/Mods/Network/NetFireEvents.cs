using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public enum FireEventKind : byte { PressFire, ReleaseFire, AutomaticFire, ContinuousTick, TurretFire }
public readonly record struct FireEvent(uint ShotId, uint SourceFrame, uint AckFrame, byte AckSubFrame,
    FireEventKind Kind, byte Weapon, byte Charge, uint ContinuousPhase)
{
    public const int Size = 20;
    public void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ShotId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], SourceFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], AckFrame);
        bytes[12] = AckSubFrame; bytes[13] = (byte)Kind; bytes[14] = Weapon; bytes[15] = Charge;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], ContinuousPhase);
    }
    public static FireEvent Read(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), bytes[12],
        (FireEventKind)bytes[13], bytes[14], bytes[15], BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]));
}
public struct FireEventHistory
{
    private FireEvent _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15;
    public FireEvent this[int index]
    {
        readonly get => index switch { 0 => _0, 1 => _1, 2 => _2, 3 => _3, 4 => _4, 5 => _5, 6 => _6, 7 => _7, 8 => _8, 9 => _9, 10 => _10, 11 => _11, 12 => _12, 13 => _13, 14 => _14, 15 => _15, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        set { switch (index) { case 0: _0 = value; break; case 1: _1 = value; break; case 2: _2 = value; break; case 3: _3 = value; break; case 4: _4 = value; break; case 5: _5 = value; break; case 6: _6 = value; break; case 7: _7 = value; break; case 8: _8 = value; break; case 9: _9 = value; break; case 10: _10 = value; break; case 11: _11 = value; break; case 12: _12 = value; break; case 13: _13 = value; break; case 14: _14 = value; break; case 15: _15 = value; break; default: throw new ArgumentOutOfRangeException(nameof(index)); } }
    }
}

/// <summary>Repeated, bounded actual fire events. IDs are allocated before projectile
/// creation, shared by all pellets/children, and committed only after successful spawn.
/// Source frames and ACKs are timing metadata, never pairing keys.</summary>
public static class NetFireEvents
{
    public const int Capacity = 16, RetentionFrames = 32, WireSize = 1 + Capacity * FireEvent.Size;
    private sealed class Slot
    {
        public ShotKey Fence;
        public uint NextId, LastConsumed, SelectedAt;
        public bool Seen, Selected;
        public FireEvent Active;
        public readonly FireEvent[] Events = new FireEvent[Capacity];
        public int Count;
    }
    private static readonly Slot[] _slots = CreateSlots();
    private static Slot[] CreateSlots() { var result = new Slot[8]; for (int i = 0; i < 8; i++) result[i] = new(); return result; }
    private static Slot For(int slot)
    {
        var state = _slots[slot]; var fence = ShotKey.For(slot, 0);
        if (state.Fence != fence)
        { state.Fence = fence; state.Count = 0; state.NextId = state.LastConsumed = 0; state.Seen = state.Selected = false; state.Active = default; }
        return state;
    }
    public static void Reset()
    { foreach (var slot in _slots) { slot.Fence = default; slot.Count = 0; slot.Selected = slot.Seen = false; slot.Active = default; slot.NextId = slot.LastConsumed = 0; } }
    public static bool Validate(in IntentPacket intent)
    {
        if (intent.FireEventCount > Capacity) return false;
        uint previous = 0;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var e = intent.FireEvents[i];
            if (e.ShotId == 0 || (byte)e.Kind > (byte)FireEventKind.TurretFire || e.Weapon > (byte)BeamType.OmegaCannon
                || unchecked(intent.Frame - e.SourceFrame) > RetentionFrames
                || i > 0 && !NetLifecycleTracker.Newer(e.ShotId, previous)) return false;
            previous = e.ShotId;
        }
        return true;
    }
    internal static bool UsesEvents(PlayerEntity player) => NetSession.Active && !player.SceneServices.IsReplica
        && !player.IsBot && player.SlotIndex != NetSession.LocalSlot && (uint)player.SlotIndex < 8
        && NetSession.RemoteIntentValid[player.SlotIndex] && NetSession.RemoteIntents[player.SlotIndex].HasFireEvents;
    internal static uint ActiveShotId(PlayerEntity player) => (uint)player.SlotIndex < 8 ? For(player.SlotIndex).Active.ShotId : 0;
    internal static bool HasPending(PlayerEntity player)
        => UsesEvents(player) && For(player.SlotIndex).Selected && For(player.SlotIndex).Active.Kind != FireEventKind.TurretFire;
    internal static bool CanFireTurret(PlayerEntity player) => !UsesEvents(player)
        || For(player.SlotIndex).Selected && For(player.SlotIndex).Active.Kind == FireEventKind.TurretFire;
    internal static bool CanFire(PlayerEntity player) => !UsesEvents(player) || HasPending(player);
    internal static void Prepare(PlayerEntity player, in IntentPacket intent)
    {
        if (!UsesEvents(player)) return;
        var state = For(player.SlotIndex);
        if (state.SelectedAt == NetSession.NetFrame && state.Selected) return;
        state.Selected = false; state.Active = default;
        // Events are repeated in every carrier; use their original order and identity.
        // A delayed carrier cannot re-run an already consumed shot.
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var e = intent.FireEvents[i];
            if (state.Seen && !NetLifecycleTracker.Newer(e.ShotId, state.LastConsumed)) continue;
            if (e.Kind == FireEventKind.ContinuousTick && e.ContinuousPhase != intent.ContinuousFireTick)
            { state.Seen = true; state.LastConsumed = e.ShotId; continue; }
            state.Active = e; state.Selected = true; state.SelectedAt = NetSession.NetFrame;
            if (e.Kind == FireEventKind.TurretFire) return;
            player.ModSetWeapon((BeamType)e.Weapon);
            player.EquipInfo.ChargeLevel = e.Charge;
            if (e.Kind != FireEventKind.ContinuousTick)
            {
                bool release = e.Kind == FireEventKind.ReleaseFire;
                player.Controls.Shoot.IsDown = !release;
                player.Controls.Shoot.IsPressed = e.Kind == FireEventKind.PressFire;
                player.Controls.Shoot.IsReleased = release;
            }
            return;
        }
    }
    internal static void Begin(PlayerEntity player, bool turret = false)
    {
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8) return;
        var state = For(player.SlotIndex);
        if (UsesEvents(player))
        {
            if (!state.Selected) { state.Active = default; return; }
            state.LastConsumed = state.Active.ShotId; state.Seen = true; state.Selected = false;
            return;
        }
        uint id = unchecked(++state.NextId); if (id == 0) id = ++state.NextId;
        var kind = turret ? FireEventKind.TurretFire : player.EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.Continuous)
            ? FireEventKind.ContinuousTick : player.EquipInfo.ChargeLevel >= player.EquipInfo.Weapon.MinCharge * 2
                && player.EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.CanCharge) ? FireEventKind.ReleaseFire
            : player.Controls.Shoot.IsPressed ? FireEventKind.PressFire : FireEventKind.AutomaticFire;
        uint ack = NetUnlagged.LaunchFrameFor(player); byte sub = 0;
        if (NetSmoothing.AckPoint(out uint read, out byte fraction)) { ack = read; sub = fraction; }
        state.Active = new(id, NetSession.NetFrame, ack, sub, kind, (byte)player.CurrentWeapon,
            (byte)Math.Clamp((int)player.EquipInfo.ChargeLevel, 0, 255), 0);
    }
    internal static void Commit(PlayerEntity player)
    {
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8 || UsesEvents(player)) return;
        var state = For(player.SlotIndex);
        if (state.Active.ShotId == 0) return;
        if (state.Count == Capacity) { Array.Copy(state.Events, 1, state.Events, 0, Capacity - 1); state.Count--; }
        state.Events[state.Count++] = state.Active with { ContinuousPhase = player.ModContinuousFireTick };
    }
    internal static void Fill(ref IntentPacket intent, int slot)
    {
        var state = For(slot); int count = 0;
        for (int i = 0; i < state.Count; i++)
            if (unchecked(intent.Frame - state.Events[i].SourceFrame) <= RetentionFrames) state.Events[count++] = state.Events[i];
        state.Count = count; intent.FireEventCount = (byte)count; intent.FireEvents = default; intent.HasFireEvents = true;
        for (int i = 0; i < count; i++) intent.FireEvents[i] = state.Events[i];
    }
    internal static bool TryTiming(PlayerEntity player, out FireEvent e)
    { e = (uint)player.SlotIndex < 8 ? For(player.SlotIndex).Active : default; return UsesEvents(player) && e.ShotId != 0; }
}
