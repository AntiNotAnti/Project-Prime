using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public enum FireEventKind : byte { PressFire, ReleaseFire, AutomaticFire, ContinuousTick, TurretFire }
public readonly record struct FireEvent(uint ShotId, uint SourceFrame, uint AckFrame, byte AckSubFrame,
    FireEventKind Kind, byte Weapon, byte Charge, uint ContinuousPhase,
    byte PoseFlags = 0, Vector3 Origin = default, Vector3 Direction = default,
    Vector3 Aim = default, Vector3 View = default, Vector2 Reticle = default)
{
    public const int LegacySize = 20;
    public const int Size = 67;
    public const byte FlagPose = 1 << 0;
    public const byte FlagReticle = 1 << 1;
    // Non-continuous Imperialist events do not use ContinuousPhase. Preserve
    // shot-time scope in its high bit so a repeated/recovered fire event cannot
    // inherit the scope state of a later carrier packet.
    internal const uint ScopedStateBit = 1u << 31;
    internal bool ScopedAtFire => Weapon == (byte)BeamType.Imperialist
        && (ContinuousPhase & ScopedStateBit) != 0;
    internal bool HasPose => (PoseFlags & FlagPose) != 0;
    internal bool HasReticle => (PoseFlags & FlagReticle) != 0;

    public void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ShotId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], SourceFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], AckFrame);
        bytes[12] = AckSubFrame; bytes[13] = (byte)Kind; bytes[14] = Weapon; bytes[15] = Charge;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], ContinuousPhase);
        bytes[20] = PoseFlags;
        BinaryPrimitives.WriteSingleLittleEndian(bytes[21..], Origin.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[25..], Origin.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[29..], Origin.Z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[33..], Direction.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[37..], Direction.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[41..], Direction.Z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[45..], Aim.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[49..], Aim.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[53..], Aim.Z);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[57..], PackUnit(View.X));
        BinaryPrimitives.WriteInt16LittleEndian(bytes[59..], PackUnit(View.Y));
        BinaryPrimitives.WriteInt16LittleEndian(bytes[61..], PackUnit(View.Z));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[63..], PackReticle(Reticle.X));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[65..], PackReticle(Reticle.Y));
    }

    public static FireEvent Read(ReadOnlySpan<byte> bytes)
    {
        byte poseFlags = bytes.Length >= Size ? bytes[20] : (byte)0;
        bool pose = (poseFlags & FlagPose) != 0;
        Vector3 view = default;
        if (pose)
        {
            view = new Vector3(UnpackUnit(BinaryPrimitives.ReadInt16LittleEndian(bytes[57..])),
                UnpackUnit(BinaryPrimitives.ReadInt16LittleEndian(bytes[59..])),
                UnpackUnit(BinaryPrimitives.ReadInt16LittleEndian(bytes[61..])));
            if (view.LengthSquared >= 0.000001f) view = view.Normalized();
        }
        return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), bytes[12],
            (FireEventKind)bytes[13], bytes[14], bytes[15],
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            poseFlags,
            pose ? new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes[21..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[25..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[29..])) : default,
            pose ? new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes[33..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[37..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[41..])) : default,
            pose ? new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes[45..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[49..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[53..])) : default,
            pose ? view : default,
            pose && (poseFlags & FlagReticle) != 0
                ? new Vector2(UnpackReticle(BinaryPrimitives.ReadUInt16LittleEndian(bytes[63..])),
                    UnpackReticle(BinaryPrimitives.ReadUInt16LittleEndian(bytes[65..]))) : default);
    }

    private static short PackUnit(float value)
        => !float.IsFinite(value) ? (short)0
            : (short)Math.Clamp((int)MathF.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue),
                short.MinValue, short.MaxValue);
    private static float UnpackUnit(short value) => Math.Clamp(value / (float)short.MaxValue, -1f, 1f);
    private static ushort PackReticle(float value)
        => !float.IsFinite(value) ? (ushort)0
            : (ushort)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * ushort.MaxValue),
                ushort.MinValue, ushort.MaxValue);
    private static float UnpackReticle(ushort value) => value / (float)ushort.MaxValue;
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
    public const int Capacity = 16, RetentionFrames = 32;
    public const int LegacyWireSize = 1 + Capacity * FireEvent.LegacySize;
    public const int WireSize = 1 + Capacity * FireEvent.Size;
    private sealed class Slot
    {
        public ShotKey Fence;
        public uint NextId, LastConsumed, SelectedAt;
        public bool Seen, Selected;
        public bool AcceptedEmission, AcceptedPhaseConsumed;
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
        { state.Fence = fence; state.Count = 0; state.NextId = state.LastConsumed = 0; state.Seen = state.Selected = false; state.Active = default; state.AcceptedEmission = state.AcceptedPhaseConsumed = false; }
        return state;
    }
    public static void Reset()
    { NetAcceptedAttacks.Reset(); foreach (var slot in _slots) { slot.Fence = default; slot.Count = 0; slot.Selected = slot.Seen = false; slot.Active = default; slot.NextId = slot.LastConsumed = 0; slot.AcceptedEmission = slot.AcceptedPhaseConsumed = false; } }
    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    public static bool Validate(in IntentPacket intent)
    {
        if (intent.FireEventCount > Capacity) return false;
        uint previous = 0;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var e = intent.FireEvents[i];
            if (e.ShotId == 0 || (byte)e.Kind > (byte)FireEventKind.TurretFire || e.Weapon > (byte)BeamType.OmegaCannon
                || unchecked(intent.Frame - e.SourceFrame) > RetentionFrames
                || i > 0 && !NetLifecycleTracker.Newer(e.ShotId, previous)
                || (e.PoseFlags & ~(FireEvent.FlagPose | FireEvent.FlagReticle)) != 0
                || e.HasReticle && !e.HasPose
                || e.HasPose && (!Finite(e.Origin) || !Finite(e.Direction) || !Finite(e.Aim)
                    || !Finite(e.View) || e.Direction.LengthSquared < 0.000001f
                    || e.Aim.LengthSquared < 0.000001f || e.View.LengthSquared < 0.000001f
                    || e.HasReticle && (!float.IsFinite(e.Reticle.X) || !float.IsFinite(e.Reticle.Y)
                        || e.Reticle.X is < 0 or > 1 || e.Reticle.Y is < 0 or > 1))) return false;
            previous = e.ShotId;
        }
        return true;
    }
    internal static bool UsesEvents(PlayerEntity player)
    {
        if (player.SceneServices.IsReplica)
            return player.OwningScene.ReplayPoses?.UsesFireEvents(player) == true;
        if ((uint)player.SlotIndex < 8 && For(player.SlotIndex).AcceptedEmission) return true;
        return NetSession.Active && !player.IsBot && player.SlotIndex != NetSession.LocalSlot
            && (uint)player.SlotIndex < 8 && NetSession.RemoteIntentValid[player.SlotIndex]
            && NetSession.RemoteIntents[player.SlotIndex].HasFireEvents;
    }
    internal static uint ActiveShotId(PlayerEntity player)
    {
        if (player.SceneServices.IsReplica)
            return player.OwningScene.ReplayPoses?.ActiveFireShotId(player) ?? 0;
        return (uint)player.SlotIndex < 8 ? For(player.SlotIndex).Active.ShotId : 0;
    }
    internal static bool HasPending(PlayerEntity player)
    {
        if (player.SceneServices.IsReplica)
            return player.OwningScene.ReplayPoses?.HasPendingFire(player) == true;
        return UsesEvents(player) && For(player.SlotIndex).Selected
            && For(player.SlotIndex).Active.Kind != FireEventKind.TurretFire;
    }
    internal static bool CanFireTurret(PlayerEntity player)
    {
        if (player.SceneServices.IsReplica)
        {
            FireEvent fire = default;
            return player.OwningScene.ReplayPoses?.TryActiveFire(player, out fire) == true
                && fire.Kind == FireEventKind.TurretFire;
        }
        return !UsesEvents(player)
            || For(player.SlotIndex).Selected && For(player.SlotIndex).Active.Kind == FireEventKind.TurretFire;
    }
    internal static bool CanFire(PlayerEntity player) => !UsesEvents(player) || HasPending(player);
    /// <summary>
    /// Scope belongs to the authored Imperialist shot, not necessarily to the
    /// newer intent packet that happened to deliver a recovered fire event.
    /// Keep that historical answer local to projectile damage so the puppet's
    /// current presentation state still follows the newest ZoomedState.
    /// </summary>
    internal static bool TryScopedAtFire(PlayerEntity player, out bool scoped)
    {
        scoped = false;
        if (!UsesEvents(player)) return false;
        FireEvent fire;
        if (player.SceneServices.IsReplica)
        {
            if (player.OwningScene.ReplayPoses?.TryActiveFire(player, out fire) != true) return false;
        }
        else
        {
            fire = For(player.SlotIndex).Active;
        }
        if (fire.ShotId == 0 || fire.Weapon != (byte)BeamType.Imperialist) return false;
        scoped = fire.ScopedAtFire;
        return true;
    }
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
            if (NetSession.IsAuthority && !NetAcceptedAttacks.Authorized(player.SlotIndex, e.ShotId)) continue;
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
    internal static void SelectAccepted(PlayerEntity player, in FireEvent fire)
    {
        var state = For(player.SlotIndex);
        state.Active = fire; state.Selected = true; state.SelectedAt = NetSession.NetFrame;
        state.LastConsumed = fire.ShotId; state.Seen = true;
    }

    /// <summary>
    /// Temporarily expose one admitted authority event to native spawning and
    /// rewind. Its continuous phase is independent of the newest carrier clock.
    /// Restore presentation/selection state and the previous homing target even
    /// when native spawning fails; consumed shot IDs only move forward.
    /// </summary>
    internal static AcceptedEmissionScope BeginAcceptedEmission(PlayerEntity player,
        in FireEvent fire, NetTargetIdentity target)
        => new(player, fire, target);

    internal sealed class AcceptedEmissionScope : IDisposable
    {
        private PlayerEntity? _player;
        private readonly Slot _state;
        private readonly ShotKey _fence;
        private readonly FireEvent _previousActive;
        private readonly bool _previousSelected, _previousSeen;
        private readonly uint _previousSelectedAt, _previousConsumed;
        private readonly NetTargetIdentity _previousTarget;

        internal AcceptedEmissionScope(PlayerEntity player, in FireEvent fire, NetTargetIdentity target)
        {
            if (!NetSession.IsAuthority || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8
                || fire.ShotId == 0 || !NetAcceptedAttacks.Authorized(player.SlotIndex, fire.ShotId))
                throw new InvalidOperationException("Native accepted emission requires an admitted authority event.");
            _state = For(player.SlotIndex);
            if (_state.AcceptedEmission) throw new InvalidOperationException("Accepted emission scopes cannot overlap.");
            _fence = _state.Fence;
            _previousActive = _state.Active; _previousSelected = _state.Selected;
            _previousSeen = _state.Seen; _previousConsumed = _state.LastConsumed;
            _previousSelectedAt = _state.SelectedAt;
            _previousTarget = player.ModConsumePendingHomingTarget();
            _player = player;
            SelectAccepted(player, fire);
            player.ModSetPendingHomingTarget(target);
            _state.AcceptedEmission = true; _state.AcceptedPhaseConsumed = false;
        }

        public void Dispose()
        {
            PlayerEntity? player = _player;
            if (player == null) return;
            _player = null;
            // A lifecycle reset owns its new slot; never restore old-life state.
            if (For(player.SlotIndex).Fence != _fence) return;
            uint consumed = _state.LastConsumed;
            if (_previousSeen && !NetLifecycleTracker.Newer(consumed, _previousConsumed))
                consumed = _previousConsumed;
            _state.LastConsumed = consumed; _state.Seen |= _previousSeen;
            _state.Active = _previousActive;
            _state.Selected = _previousSelected && (!_state.Seen
                || NetLifecycleTracker.Newer(_previousActive.ShotId, consumed));
            _state.SelectedAt = _previousSelectedAt;
            _state.AcceptedEmission = _state.AcceptedPhaseConsumed = false;
            player.ModSetPendingHomingTarget(_previousTarget);
        }
    }

    internal static bool TryAcceptedContinuousPhase(PlayerEntity player, out ulong phase, out bool fresh)
    {
        phase = 0; fresh = false;
        if (player.SceneServices.IsReplica || !NetSession.IsAuthority || (uint)player.SlotIndex >= 8) return false;
        Slot state = For(player.SlotIndex);
        if (!state.AcceptedEmission || state.Active.Kind != FireEventKind.ContinuousTick
            || state.Active.ContinuousPhase == 0 || state.Active.Weapon != (byte)player.EquipInfo.Weapon.Beam) return false;
        phase = state.Active.ContinuousPhase;
        fresh = !state.AcceptedPhaseConsumed;
        state.AcceptedPhaseConsumed = true;
        return true;
    }
    internal static void Begin(PlayerEntity player, Vector3 origin = default,
        Vector3 direction = default, Vector3 aim = default, Vector3 view = default,
        Vector2 reticle = default, bool hasReticle = false, bool turret = false)
    {
        if (player.SceneServices.IsReplica)
        {
            player.OwningScene.ReplayPoses?.BeginFire(player, turret);
            return;
        }
        if (!NetSession.Active || (uint)player.SlotIndex >= 8) return;
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
        uint shotState = player.CurrentWeapon == BeamType.Imperialist && player.EquipInfo.Zoomed
            ? FireEvent.ScopedStateBit : 0;
        if (!Finite(aim) || aim.LengthSquared < 0.000001f) aim = direction;
        if (!Finite(view) || view.LengthSquared < 0.000001f) view = aim;
        bool reticleValid = hasReticle && float.IsFinite(reticle.X) && float.IsFinite(reticle.Y)
            && reticle.X is >= 0 and <= 1 && reticle.Y is >= 0 and <= 1;
        bool poseValid = Finite(origin) && Finite(direction) && Finite(aim) && Finite(view)
            && direction.LengthSquared >= 0.000001f && aim.LengthSquared >= 0.000001f
            && view.LengthSquared >= 0.000001f;
        byte poseFlags = poseValid ? FireEvent.FlagPose : (byte)0;
        if (poseValid && reticleValid) poseFlags |= FireEvent.FlagReticle;
        state.Active = new(id, NetSession.NetFrame, ack, sub, kind,
            (byte)(turret ? player.ModTurretAttackWeapon.Beam : player.CurrentWeapon),
            turret ? (byte)0 : (byte)Math.Clamp((int)player.EquipInfo.ChargeLevel, 0, 255), turret ? 0 : shotState,
            poseFlags,
            poseValid ? origin : default,
            poseValid ? direction.Normalized() : default,
            poseValid ? aim.Normalized() : default,
            poseValid ? view.Normalized() : default,
            poseValid && reticleValid ? reticle : default);
    }
    internal static void Commit(PlayerEntity player)
    {
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8) return;
        if (UsesEvents(player)) { NetAcceptedAttacks.Launched(player); return; }
        var state = For(player.SlotIndex);
        if (state.Active.ShotId == 0) return;
        if (state.Count == Capacity) { Array.Copy(state.Events, 1, state.Events, 0, Capacity - 1); state.Count--; }
        uint phaseOrState = state.Active.Kind == FireEventKind.ContinuousTick
            ? player.ModContinuousFireTick : state.Active.ContinuousPhase;
        state.Events[state.Count++] = state.Active with { ContinuousPhase = phaseOrState };
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
    {
        if (player.SceneServices.IsReplica)
        {
            e = default;
            return player.OwningScene.ReplayPoses?.TryActiveFire(player, out e) == true
                && e.ShotId != 0;
        }
        e = (uint)player.SlotIndex < 8 ? For(player.SlotIndex).Active : default;
        return UsesEvents(player) && e.ShotId != 0;
    }

    internal static bool TryAuthoredPose(PlayerEntity player, out Vector3 origin,
        out Vector3 direction, out Vector3 aim)
    {
        origin = direction = aim = default;
        if (!UsesEvents(player) || !TryTiming(player, out FireEvent fire) || !fire.HasPose)
            return false;
        origin = fire.Origin;
        direction = fire.Direction;
        aim = fire.Aim;
        return Finite(origin) && Finite(direction) && Finite(aim)
            && direction.LengthSquared >= 0.000001f && aim.LengthSquared >= 0.000001f;
    }
}
