using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public enum FireEventKind : byte { PressFire, ReleaseFire, AutomaticFire, ContinuousTick, TurretFire }
public readonly record struct FireEvent(uint ShotId, uint SourceFrame, uint AckFrame, byte AckSubFrame,
    FireEventKind Kind, byte Weapon, byte Charge, uint ContinuousPhase,
    byte PoseFlags = 0, Vector3 Origin = default, Vector3 Direction = default,
    Vector3 Aim = default, Vector3 View = default, Vector2 Reticle = default,
    ushort SourceLife = 0, NetTargetIdentity Target = default)
{
    public const int LegacySize = 20;
    public const int PoseSize = 67;
    public const int Size = 74;
    public const byte FlagPose = 1 << 0;
    public const byte FlagReticle = 1 << 1;
    // Protocol46 uses otherwise spare pose bits for Shock Coil source strength.
    // Canonical/replay record sizes stay unchanged; old recordings lack this bit.
    internal const byte FlagContinuousContext = 128, FlagContinuousPrime = 4, FlagContinuousDouble = 8;
    internal bool HasContinuousContext => Kind == FireEventKind.ContinuousTick && (PoseFlags & FlagContinuousContext) != 0;
    internal bool ContinuousPrime => HasContinuousContext && (PoseFlags & FlagContinuousPrime) != 0;
    internal bool ContinuousDouble => HasContinuousContext && (PoseFlags & FlagContinuousDouble) != 0;
    internal int ContinuousRamp => HasContinuousContext ? (PoseFlags >> 4) & 7 : 0;
    // Non-continuous Imperialist events do not use ContinuousPhase. Preserve
    // shot-time scope in its high bit so a repeated/recovered fire event cannot
    // inherit the scope state of a later carrier packet.
    internal const uint ScopedStateBit = 1u << 31;
    internal const uint PrimeStateBit = 1u << 30, DoubleStateBit = 1u << 29, EnhancedStateBit = 1u << 28, AffinityStateBit = 1u << 27;
    internal bool AffinityAtFire => Kind != FireEventKind.ContinuousTick && (ContinuousPhase & AffinityStateBit) != 0;
    internal bool HasEnhancedContext => Kind != FireEventKind.ContinuousTick && (ContinuousPhase & EnhancedStateBit) != 0;
    internal byte EnhancedPips => HasEnhancedContext ? (byte)(ContinuousPhase & 0xff) : (byte)0;
    internal bool PrimeAtFire => Kind != FireEventKind.ContinuousTick && (ContinuousPhase & PrimeStateBit) != 0;
    internal bool DoubleAtFire => Kind != FireEventKind.ContinuousTick && (ContinuousPhase & DoubleStateBit) != 0;
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
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[67..], SourceLife);
        bytes[69] = Target.EncodedSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[70..], Target.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[72..], Target.LifeId);
    }

    public static FireEvent Read(ReadOnlySpan<byte> bytes)
    {
        byte poseFlags = bytes.Length >= PoseSize ? bytes[20] : (byte)0;
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
                    UnpackReticle(BinaryPrimitives.ReadUInt16LittleEndian(bytes[65..]))) : default,
            bytes.Length >= Size ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[67..]) : (ushort)0,
            bytes.Length >= Size ? new NetTargetIdentity(bytes[69],
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[70..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[72..])) : default);
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
    public const int Capacity = 16, LiveCapacity = 14, RetentionFrames = 32;
    // Rollout switch; disabled only for explicit comparison runs.
    internal static bool AcceptedPlaybackEnabled { get; set; }
        = Environment.GetEnvironmentVariable("PRIME_ACCEPTED_SHOTS") != "0";
    public const int LegacyWireSize = 1 + Capacity * FireEvent.LegacySize;
    public const int WireSize = 1 + Capacity * FireEvent.Size;
    private sealed class Slot
    {
        public ShotKey Fence;
        public ushort CurrentLife;
        public uint NextId, SelectedAt;
        public bool Selected, PlayingAccepted, EventStream;
        public ShotKey ActiveKey, TurretRetiredLife;
        public uint TurretRetiredThrough;
        public bool HasTurretRetirement;
        public readonly TurretTerminal[] TurretTerminals = new TurretTerminal[256];
        public bool TurretTerminalOverflow;
        public uint TurretTerminalOverflowAt;
        public long TurretTerminalOverflows;
        public readonly TurretRecovery[] TurretRecovery = new TurretRecovery[NetIncomingFireHistory.Capacity];
        public AcceptedShotLedger Ledger = NewLedger();
        public FireEvent Active;
        public Vector3 NativeOrigin, NativeDirection;
        public readonly NetOutgoingFireHistory Outgoing = new();
        public readonly NetIncomingFireHistory Incoming = new();
    }
    private struct TurretTerminal { internal ShotKey Key; internal uint At; }
    // Use authority observation time exclusively. Retain beyond both late delivery
    // and parked-claim settlement; never evict a still-live terminal identity.
    internal const int TurretTerminalRetentionFrames = NetHitClaims.DeliveryMaxAge
        + NetUnlagged.HistoryFrames + NetHitClaims.MaxGraceFrames;
    internal static void RememberRetiredUnemittedTurret(in ShotKey key)
    {
        if (!NetSession.IsAuthority || (uint)key.ShooterSlot >= 8 || key.ShotId == 0) return;
        RememberRetiredUnemittedTurret(For(key.ShooterSlot), key);
    }
    // Callbacks can run while For is retiring a life. Use its owned state rather
    // than re-entering For before CurrentLife has advanced.
    private static void RememberRetiredUnemittedTurret(Slot state, in ShotKey key)
    {
        if (!NetSession.IsAuthority || key.ShotId == 0
            || state.Fence != (key with { LifeId = 0, ShotId = 0 })) return;
        int free = -1;
        for (int i = 0; i < state.TurretTerminals.Length; i++)
        {
            ref var terminal = ref state.TurretTerminals[i];
            if (terminal.Key == key) return;
            if (terminal.Key.ShotId == 0
                || unchecked((int)(NetSession.NetFrame - terminal.At)) > TurretTerminalRetentionFrames) free = i;
        }
        if (free >= 0) state.TurretTerminals[free] = new() { Key = key, At = NetSession.NetFrame };
        else { state.TurretTerminalOverflow = true; state.TurretTerminalOverflowAt = NetSession.NetFrame; state.TurretTerminalOverflows++; }
    }
    internal static bool IsPendingTurret(in ShotKey key)
    {
        if ((uint)key.ShooterSlot >= 8 || key.ShotId == 0) return false;
        var state = For(key.ShooterSlot);
        if (state.Fence != (key with { LifeId = 0, ShotId = 0 })
            || !state.Incoming.TryGetPending(key.ShotId, out var fire)
            || fire.Kind != FireEventKind.TurretFire) return false;
        ushort sourceLife = fire.SourceLife != 0 ? fire.SourceLife : state.CurrentLife;
        return key.LifeId == sourceLife;
    }
    internal static bool IsRetiredUnemittedTurret(in ShotKey key)
    {
        if ((uint)key.ShooterSlot >= 8 || key.ShotId == 0) return false;
        var state = For(key.ShooterSlot);
        if (state.Fence != (key with { LifeId = 0, ShotId = 0 })) return false;
        if (state.TurretTerminalOverflow
            && unchecked((int)(NetSession.NetFrame - state.TurretTerminalOverflowAt)) <= TurretTerminalRetentionFrames) return true;
        foreach (ref var terminal in state.TurretTerminals.AsSpan())
            if (terminal.Key == key
                && unchecked((int)(NetSession.NetFrame - terminal.At)) <= TurretTerminalRetentionFrames) return true;
        return false;
    }
    private struct TurretRecovery { internal ShotKey Key; internal uint AcceptedAt; internal PlayerFormState Form; internal bool EverAvailable; }
    private static AcceptedShotLedger NewLedger() => new() { OnDisposition = NetCampaignEvidence.Disposition };
    private static readonly Slot[] _slots = CreateSlots();
    private static Slot[] CreateSlots()
    {
        var result = new Slot[8];
        for (int i = 0; i < 8; i++)
        {
            var state = result[i] = new(); int slot = i;
            state.Incoming.OnExpired = fire =>
            {
                var key = state.Fence with { ShooterSlot = slot,
                    LifeId = fire.SourceLife != 0 ? fire.SourceLife : state.CurrentLife, ShotId = fire.ShotId };
                // An expired pending source was never consumed by native Commit.
                // Its claim rescue veto is production policy, independent of diagnostics.
                if (NetSession.IsAuthority && fire.Kind == FireEventKind.TurretFire)
                {
                    RememberRetiredUnemittedTurret(state, key);
                    NetSession.RefuseTurret(key, fire, TurretFireOutcomeReason.RecoveryExpired);
                }
                // Preserve diagnostic native gate history alongside the explicit
                // refusal. Recovery expiry never claims a successful emission.
                if (NetSession.IsAuthority && fire.Kind == FireEventKind.TurretFire
                    && (uint)slot < PlayerEntity.Players.Count)
                {
                    var owner = PlayerEntity.Players[slot];
                    if (NetCampaignEvidence.Enabled) LogTurretGate(owner, key, fire, "expired");
                    ref var proof = ref state.TurretRecovery[key.ShotId % NetIncomingFireHistory.Capacity];
                    if (NetCampaignEvidence.Enabled && owner.SlotIndex == slot && owner.Hunter == Hunter.Weavel && proof.Key == key
                        && TurretRecoveryElapsed(proof.AcceptedAt, NetSession.NetFrame, proof.EverAvailable)
                        && ExpiredTurretUnavailable(key, ShotKey.For(slot, 0), owner.ModCaptureForm()))
                    { NetCampaignEvidence.TurretRecoveryUnavailable(owner, key, fire, proof.AcceptedAt, proof.Form, proof.EverAvailable); }
                }
                NetCampaignEvidence.Disposition(new(key, fire), ShotDisposition.Expired,
                    "fire-event recovery retention elapsed");
            };
        }
        return result;
    }
    internal static bool TurretRecoveryElapsed(uint acceptedAt, uint now, bool everAvailable)
        => !everAvailable && unchecked((int)(now - acceptedAt)) >= NetIncomingFireHistory.RecoveryFrames;
    internal static bool ExpiredTurretUnavailable(in ShotKey eventKey, in ShotKey currentOwner,
        in PlayerFormState form)
        => eventKey.ShotId != 0 && currentOwner.AuthorityEpoch != 0 && currentOwner.MatchId != 0
            && currentOwner.Generation != 0 && currentOwner.LifeId != 0
            && eventKey == (currentOwner with { ShotId = eventKey.ShotId })
            && form.Phase == FormPhase.Alt && !form.TurretActive && form.TurretHealth == 0;
    internal static void ObserveAuthorityTurret(PlayerEntity owner)
    {
        if ((uint)owner.SlotIndex >= 8) return;
        var state = _slots[owner.SlotIndex];
        ObserveTurretRecovery(state, owner.SlotIndex);
        if (!NetCampaignEvidence.Enabled || !NetSession.IsAuthority || NetSession.NetFrame % 4 != 0
            || TurretDiagnosticFrames[owner.SlotIndex] == NetSession.NetFrame) return;
        FireEvent fire;
        if (state.Selected && state.Active.Kind == FireEventKind.TurretFire) fire = state.Active;
        else if (!state.Incoming.TryPeek(out fire) || fire.Kind != FireEventKind.TurretFire) return;
        TurretDiagnosticFrames[owner.SlotIndex] = NetSession.NetFrame;
        var key = ShotKey.For(owner.SlotIndex, fire.ShotId) with
        { LifeId = fire.SourceLife != 0 ? fire.SourceLife : state.CurrentLife };
        LogTurretGate(owner, key, fire, "pending");
    }
    private static readonly uint?[] TurretDiagnosticFrames = new uint?[8];
    private static readonly JsonSerializerOptions TurretDiagnosticJson = new()
    { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static void LogTurretGate(PlayerEntity owner, ShotKey key, FireEvent fire, string stage)
    {
        var form = owner.ModCaptureForm();
        Console.WriteLine("[net2-turret-gate] " + JsonSerializer.Serialize(new
        { Stage = stage, Key = key, fire.SourceFrame, AuthorityFrame = NetSession.NetFrame,
            OwnerForm = new { form.Phase, form.TurretActive, form.TurretHealth, form.StartedFrame },
            NativeGate = owner.Halfturret?.ModNativeFiringGateSnapshot() },
            TurretDiagnosticJson));
    }
    internal static bool TryTurretRecovery(ShotKey key, out uint acceptedAt, out PlayerFormState form, out bool everAvailable)
    {
        acceptedAt = 0; form = default; everAvailable = false;
        if ((uint)key.ShooterSlot >= 8) return false;
        var proof = _slots[key.ShooterSlot].TurretRecovery[key.ShotId % NetIncomingFireHistory.Capacity];
        if (proof.Key != key) return false;
        acceptedAt = proof.AcceptedAt; form = proof.Form; everAvailable = proof.EverAvailable; return true;
    }
    private static void ObserveTurretRecovery(Slot state, int slot)
    {
        if (!NetCampaignEvidence.Enabled || !NetSession.IsAuthority || (uint)slot >= PlayerEntity.Players.Count) return;
        var owner = PlayerEntity.Players[slot];
        if (owner.SlotIndex != slot || owner.Hunter != Hunter.Weavel
            || !owner.Flags2.TestFlag(PlayerFlags2.Halfturret) && owner.Halfturret.Health <= 0) return;
        var current = ShotKey.For(slot, 0);
        foreach (ref var proof in state.TurretRecovery.AsSpan())
            if (proof.Key.ShotId != 0 && proof.Key == (current with { ShotId = proof.Key.ShotId })) proof.EverAvailable = true;
    }
    private static Slot For(int slot)
    {
        var state = _slots[slot]; var fence = ShotKey.For(slot, 0) with { LifeId = 0 };
        if (state.Fence != fence)
        { RejectSpecial(state, slot, "ownership retired");
          state.Ledger.Retire(fence.AuthorityEpoch, fence.MatchId, slot, fence.Generation);
          state.Ledger.ResetObservationClock();
          state.EventStream = false; state.Fence = fence; state.Outgoing.Clear(); state.Incoming.Clear(); Array.Clear(state.TurretRecovery); Array.Clear(state.TurretTerminals); state.TurretTerminalOverflow = false; state.NextId = 0; state.Selected = false; state.Active = default; }
        ObserveTurretRecovery(state, slot);
        ushort life = NetPlayerLifecycle.Get(slot);
        if (state.CurrentLife != life)
        {
            // Cadence and turret ownership belong to the current body; only
            // discrete accepted shots and resend history survive its death.
            RejectSpecial(state, slot, "firing cadence life retired");
            state.CurrentLife = life; state.Incoming.Clear(); Array.Clear(state.TurretRecovery); state.Selected = false;
            if (!state.PlayingAccepted) state.Active = default;
        }
        return state;
    }
    private static void RejectSpecial(Slot state, int slot, string reason)
    {
        while (state.Incoming.TryPeek(out var fire))
        {
            var key = state.Fence with { ShooterSlot = slot,
                LifeId = fire.SourceLife != 0 ? fire.SourceLife : state.CurrentLife, ShotId = fire.ShotId };
            if (fire.Kind == FireEventKind.ContinuousTick && (uint)slot < PlayerEntity.Players.Count
                && (PlayerEntity.Players[slot].Health <= 0 || key.LifeId != NetPlayerLifecycle.Get(slot)))
                CompleteUnavailableContinuous(PlayerEntity.Players[slot], key, fire);
            else if (fire.Kind == FireEventKind.TurretFire && NetSession.IsAuthority)
            {
                RememberRetiredUnemittedTurret(state, key);
                NetSession.RefuseTurret(key, fire, TurretFireOutcomeReason.OwnerRetired);
                if ((uint)slot < PlayerEntity.Players.Count)
                    NetCampaignEvidence.TurretUnavailable(PlayerEntity.Players[slot], key, fire);
                else NetCampaignEvidence.Disposition(new(key, fire), ShotDisposition.Rejected, reason);
            }
            else NetCampaignEvidence.Disposition(new(key, fire), ShotDisposition.Rejected, reason);
            state.Incoming.Consume(fire.ShotId);
        }
    }
    public static void Reset()
    { NetShotProof.Reset(); NetTimedShotProof.Reset(); NetSession.ResetTurretOutcomes(); foreach (var slot in _slots) { slot.Ledger.Retire(0, 0, -1, 0); slot.Ledger = NewLedger(); slot.HasTurretRetirement = slot.PlayingAccepted = slot.EventStream = false; slot.Fence = default; slot.Outgoing.Clear(); slot.Incoming.Clear(); Array.Clear(slot.TurretRecovery); Array.Clear(slot.TurretTerminals); slot.TurretTerminalOverflow = false; slot.TurretTerminalOverflows = 0; slot.Selected = false; slot.Active = default; slot.NextId = 0; } }
    internal static bool Accept(int slot, in IntentPacket intent)
    {
        if ((uint)slot >= 8 || !Validate(intent)) return false;
        for (int i = 0; i < intent.FireEventCount; i++)
            if (intent.FireEvents[i] is { Kind: FireEventKind.TurretFire } turret
                && (turret.Weapon != (byte)BeamType.Battlehammer || turret.Charge != 0
                    || !turret.HasPose || turret.SourceLife == 0)) return false;
        NetContinuousAdmission.Observe(slot, intent);
        NetShotInputProof.Observe(slot, intent);
        NetShotSourceJournal.Observe(slot, intent);
        var state = For(slot);
        state.EventStream |= intent.HasFireEvents;
        if (!AcceptedPlaybackEnabled) return state.Incoming.Accept(intent);
        state.Ledger.Expire(intent.Frame);
        // Discrete events have one owner, the full-key ledger. Continuous and
        // turret events keep their existing cadence/owner-specific policy.
        var special = intent; special.FireEventCount = 0; special.FireEvents = default;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var fire = intent.FireEvents[i];
            ushort life = fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId;
            if (fire.Kind == FireEventKind.ContinuousTick || fire.Kind == FireEventKind.TurretFire && NetSession.IsAuthority)
            {
                if (life == NetPlayerLifecycle.Get(slot))
                    special.FireEvents[special.FireEventCount++] = fire;
                else NetCampaignEvidence.AdmissionRejected(new ShotKey(intent.AuthorityEpoch,
                    intent.MatchId, slot, intent.SlotGeneration, life, fire.ShotId), fire, "cadence life retired");
                continue;
            }
            var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot,
                intent.SlotGeneration, life, fire.ShotId);
            var admission = state.Ledger.Accept(key, fire, intent.Frame);
            if (admission == ShotAdmission.Accepted)
                NetCampaignEvidence.TimelineFire("source-admitted", key, fire);
            if (admission is not (ShotAdmission.Accepted or ShotAdmission.Duplicate))
                NetCampaignEvidence.AdmissionRejected(key, fire, admission.ToString());
        }
        if (!state.Incoming.Accept(special)) return false;
        if (NetCampaignEvidence.Enabled && NetSession.IsAuthority && (uint)slot < PlayerEntity.Players.Count)
        {
            var owner = PlayerEntity.Players[slot]; var form = owner.ModCaptureForm();
            form.DamageEventId = NetDamage.Sequence(slot);
            for (int i = 0; i < special.FireEventCount; i++)
            {
                var fire = special.FireEvents[i]; if (fire.Kind != FireEventKind.TurretFire) continue;
                var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot, intent.SlotGeneration, fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId, fire.ShotId);
                ref var proof = ref state.TurretRecovery[fire.ShotId % NetIncomingFireHistory.Capacity];
                if (proof.Key != key) proof = new TurretRecovery { Key = key, AcceptedAt = NetSession.NetFrame,
                    Form = form, EverAvailable = form.TurretActive || form.TurretHealth > 0 };
            }
        }
        return true;
    }
    internal static AcceptedShotLedger CaptureLedger(int slot) => For(slot).Ledger;
    internal static bool TryAcceptedKey(PlayerEntity player, out ShotKey key)
    {
        key = default;
        if ((uint)player.SlotIndex >= 8) return false;
        var state = _slots[player.SlotIndex];
        if (!state.PlayingAccepted) return false;
        key = state.ActiveKey; return true;
    }
    internal static void RetireTurret(PlayerEntity player)
    {
        int slot = player.SlotIndex;
        if (!NetSession.IsAuthority || (uint)slot >= 8 || !NetSession.RemoteIntentValid[slot]) return;
        var state = For(slot);
        state.HasTurretRetirement = true;
        state.TurretRetiredLife = ShotKey.For(slot, 0);
        state.TurretRetiredThrough = NetSession.RemoteIntents[slot].Frame;
    }
    internal static void DrainAccepted(PlayerEntity player)
    {
        if (!AcceptedPlaybackEnabled || !NetSession.Active || player.SceneServices.IsReplica
            || player.IsBot || player.SlotIndex == NetSession.LocalSlot || (uint)player.SlotIndex >= 8) return;
        var state = For(player.SlotIndex);
        // An owner offer is not an authority emission. Native turret target,
        // freeze, health and cooldown checks remain authoritative. Retire only
        // a source whose actual form is gone; do not silently age that offer out.
        while (NetSession.IsAuthority && state.Incoming.TryPeek(out var retired)
            && retired.Kind == FireEventKind.TurretFire
            && (player.Health <= 0 || state.HasTurretRetirement
                && state.TurretRetiredLife == ShotKey.For(player.SlotIndex, 0)
                && !NetLifecycleTracker.Newer(retired.SourceFrame, state.TurretRetiredThrough)
                && !player.IsAltForm && !player.IsMorphing))
        {
            var key = ShotKey.For(player.SlotIndex, retired.ShotId) with
            { LifeId = retired.SourceLife != 0 ? retired.SourceLife : state.CurrentLife };
            RememberRetiredUnemittedTurret(state, key);
            NetSession.RefuseTurret(key, retired, TurretFireOutcomeReason.OwnerRetired);
            NetCampaignEvidence.TurretUnavailable(player, key, retired);
            state.Incoming.Consume(retired.ShotId);
            state.Selected = false; state.Active = default;
        }
        while (state.Incoming.TryPeek(out var fire) && fire.Kind == FireEventKind.ContinuousTick)
        {
            var key = ShotKey.For(player.SlotIndex, fire.ShotId) with { LifeId = fire.SourceLife };
            if (fire.SourceLife != NetPlayerLifecycle.Get(player.SlotIndex) || player.Health <= 0)
                CompleteUnavailableContinuous(player, key, fire);
            else
            {
                try
                {
                    if (PlayAccepted(player, key, fire) == BeamResultFlags.NoSpawn)
                        NetCampaignEvidence.Disposition(new(key, fire), ShotDisposition.Rejected, "continuous tick spawn refused");
                    else NetTimedShotProof.UnsupportedSource(player, key);
                }
                catch { NetTimedShotProof.UnsupportedSource(player, key); throw; }
            }
            state.Incoming.Consume(fire.ShotId);
            state.Selected = false; state.Active = default;
        }
        while (state.Ledger.TryPeek(out var shot))
        {
            if (shot.Key.Generation != NetPlayerLifecycle.Generation(player.SlotIndex)
                || shot.Key.AuthorityEpoch != NetSession.AuthorityEpoch || shot.Key.MatchId != NetSession.CurrentMatchId)
            { state.Ledger.Complete(shot.Key, ShotDisposition.Rejected, "ownership retired"); continue; }
            if (!shot.Fire.HasPose)
            { state.Ledger.Complete(shot.Key, ShotDisposition.Rejected, "missing authored pose"); continue; }
            var result = PlayAccepted(player, shot.Key, shot.Fire, admittedDiscrete: true);
            state.Ledger.Complete(shot.Key, result == BeamResultFlags.NoSpawn
                ? ShotDisposition.Rejected : ShotDisposition.Fired,
                result == BeamResultFlags.NoSpawn ? "projectile spawn refused" : "projectile spawned");
        }
    }
    private static void CompleteUnavailableContinuous(PlayerEntity player, ShotKey key, FireEvent fire)
    {
        // A rescued pulse can finish before its queued native playback. Its
        // payment remains final even if the owner died in the meantime.
        if (NetContinuousPayments.WasPaid(key, fire.ContinuousPhase, fire.Target))
            NetCampaignEvidence.Committed(key, fire);
        else
        {
            NetContinuousAdmission.Reject(key, fire);
            NetCampaignEvidence.ContinuousUnavailable(player, key, fire);
        }
    }
    internal static BeamResultFlags PlayAccepted(PlayerEntity player, ShotKey key, FireEvent fire)
        => PlayAccepted(player, key, fire, admittedDiscrete: false);

    private static BeamResultFlags PlayAccepted(PlayerEntity player, ShotKey key, FireEvent fire, bool admittedDiscrete)
    {
        var state = _slots[player.SlotIndex];
        if (state.PlayingAccepted) throw new InvalidOperationException("Nested accepted shot playback.");
        FireEvent saved = state.Active;
        state.Active = fire; state.ActiveKey = key; state.PlayingAccepted = true;
        TimedShotCertificate? timedAttempt = null;
        try
        {
            // Protocol 52 peers present only published native emissions here.
            // Historical replay adapters retain their original offer semantics.
            // This presentation path cannot authorize server damage.
            if (fire.Kind == FireEventKind.TurretFire) return player.ModSpawnAcceptedTurret(fire);
            NetShotProof.Admit(player, key, fire);
            var admission = NetShotProof.ObserveEmission(key);
            if (NetHitClaims.Arbitrating && NetShotProof.Mode == ShotProofMode.Enforce
                && admission != ShotProofReason.Verified)
                return BeamResultFlags.NoSpawn;
            timedAttempt = admittedDiscrete ? NetTimedShotProof.AcceptedAttempt(player, key, fire) : null;
            var modifiers = NetTimedShotModifiers.Resolve(timedAttempt, player);
            using var fastSource = NetCombatFastPath.Source(timedAttempt);
            var result = player.ModSpawnAcceptedShot(fire, out var timedEmission, modifiers);
            if (result != BeamResultFlags.NoSpawn)
            {
                // Accepted remote emissions bypass the local TryFireWeapon diagnostic.
                if (!player.SceneServices.IsReplica)
                    NetDamage.NoteFired(player, fire.Direction, fire.Aim);
                NetShotProof.Fired(key);
                NetTimedShotProof.Complete(timedAttempt, timedEmission);
            }
            else
            {
                // An admitted, validated shot can still fail native spawn
                // (blocked muzzle, exhausted projectile slots or retired
                // source). Do not leave its timed certificate unresolved.
                // A failed emission never writes a shot spend or hit fact.
                NetTimedShotProof.Abort(timedAttempt);
            }
            return result;
        }
        catch { NetTimedShotProof.Abort(timedAttempt); throw; }
        finally { state.PlayingAccepted = false; state.ActiveKey = default; state.Active = saved; }
    }
    internal static void ForgetSlot(int slot)
    {
        if ((uint)slot >= 8) return;
        var state = _slots[slot];
        RejectSpecial(state, slot, "ownership retired"); state.Fence = default;
        state.Ledger.Retire(0, 0, -1, 0);
        state.Ledger.ResetObservationClock(); state.EventStream = false;
        state.Outgoing.Clear(); state.Incoming.Clear(); Array.Clear(state.TurretRecovery); Array.Clear(state.TurretTerminals); state.TurretTerminalOverflow = false; state.NextId = 0;
        state.Selected = false; state.Active = default;
    }
    internal static object CaptureHistory() => Array.ConvertAll(_slots, state => new
    {
        Outgoing = state.Outgoing.Count, Pending = state.Incoming.Pending,
        OutgoingOverflow = state.Outgoing.Overflows, UnsentExpired = state.Outgoing.UnsentExpired,
        IncomingOverflow = state.Incoming.Overflows, Conflicts = state.Incoming.Conflicts,
        TurretTerminalOverflow = state.TurretTerminalOverflow, TurretTerminalOverflows = state.TurretTerminalOverflows,
        StaleCarriers = state.Incoming.StaleCarriers, ExpiredPending = state.Incoming.ExpiredPending,
        Discrete = new { state.Ledger.Accepted, state.Ledger.Fired, state.Ledger.Rejected, state.Ledger.Expired,
            state.Ledger.Pending, state.Ledger.Conflicts, state.Ledger.Overflows, state.Ledger.AdmissionRejected }
    });
    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    public static bool Validate(in IntentPacket intent)
    {
        if (intent.FireEventCount > Capacity || !intent.ShotInputs.Valid(intent.ShotInputCount)) return false;
        uint previous = 0;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var e = intent.FireEvents[i];
            if (e.ShotId == 0 || (byte)e.Kind > (byte)FireEventKind.TurretFire || e.Weapon > (byte)BeamType.OmegaCannon
                || unchecked(intent.Frame - e.SourceFrame) > RetentionFrames
                || e.Kind == FireEventKind.ContinuousTick && (e.ContinuousPhase == 0 || e.ContinuousPhase != e.SourceFrame)
                || i > 0 && !NetLifecycleTracker.Newer(e.ShotId, previous)
                || (e.HasContinuousContext ? e.Weapon != (byte)BeamType.ShockCoil || e.ContinuousRamp > 4
                    : (e.PoseFlags & ~(FireEvent.FlagPose | FireEvent.FlagReticle)) != 0)
                || e.Target != default && !e.Target.IsWellFormed
                || e.HasReticle && !e.HasPose
                || e.HasPose && (!Finite(e.Origin) || Math.Abs(e.Origin.X) >= 100000
                    || Math.Abs(e.Origin.Y) >= 100000 || Math.Abs(e.Origin.Z) >= 100000 || !Finite(e.Direction) || !Finite(e.Aim)
                    || !Finite(e.View) || e.Direction.LengthSquared < 0.000001f
                    || e.Aim.LengthSquared < 0.000001f || e.View.LengthSquared < 0.000001f
                    // Fire-event rays are normalized before authoring. Bound
                    // finite-but-oversized vectors, while allowing a generous
                    // 2x tolerance for older packet quantization.
                    || e.Direction.LengthSquared > 4f || e.Aim.LengthSquared > 4f
                    || e.View.LengthSquared > 4f
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
        return NetSession.Active && !player.IsBot && player.SlotIndex != NetSession.LocalSlot
            && (uint)player.SlotIndex < 8
            && (For(player.SlotIndex).EventStream || NetSession.RemoteIntentValid[player.SlotIndex]
                && NetSession.RemoteIntents[player.SlotIndex].HasFireEvents);
    }
    internal static uint ActiveShotId(PlayerEntity player)
    {
        if (TryAcceptedKey(player, out var accepted)) return accepted.ShotId;
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
        if (TryAcceptedKey(player, out _)) return !NetSession.IsAuthority && _slots[player.SlotIndex].Active.Kind == FireEventKind.TurretFire;
        if (player.SceneServices.IsReplica)
        {
            FireEvent fire = default;
            return player.OwningScene.ReplayPoses?.TryActiveFire(player, out fire) == true
                && fire.Kind == FireEventKind.TurretFire;
        }
        // A live observer never authors a remote autonomous turret, even before
        // its first carrier or after a stream reset. Published authority
        // emissions use the exact accepted scope above.
        if (NetSession.IsClient && !NetSession.IsAuthority
            && player.SlotIndex != NetSession.LocalSlot) return false;
        return !UsesEvents(player) ? CanAuthor(player)
            : For(player.SlotIndex).Selected && For(player.SlotIndex).Active.Kind == FireEventKind.TurretFire;
    }
    private static bool CanAuthor(PlayerEntity player) => !NetSession.Active
        || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8
        || (!NetSession.FreezeGameplay && NetSession.WorldAdmissionConfirmed
            && For(player.SlotIndex).Outgoing.CanAdd(NetSession.NetFrame));
    internal static bool CanFire(PlayerEntity player) => UsesEvents(player) ? HasPending(player) : CanAuthor(player);
    /// <summary>
    /// Scope belongs to the authored Imperialist shot, not necessarily to the
    /// newer intent packet that happened to deliver a recovered fire event.
    /// Keep that historical answer local to projectile damage so the puppet's
    /// current presentation state still follows the newest ZoomedState.
    /// </summary>
    internal static bool TryScopedAtFire(PlayerEntity player, out bool scoped)
    {
        scoped = false;
        if (TryAcceptedKey(player, out _))
        { scoped = _slots[player.SlotIndex].Active.ScopedAtFire; return _slots[player.SlotIndex].Active.Weapon == (byte)BeamType.Imperialist; }
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
        // Intake unions events before movement ordering. A newer empty carrier
        // cannot erase a shot, and consuming a newer ID cannot suppress a late
        // older ID still inside the bounded recovery window.
        while (state.Incoming.TryPeek(out var e))
        {
            // Continuous ticks are pumped after intent/death reconciliation,
            // independently of present-life trigger/cooldown animation gates.
            if (e.Kind == FireEventKind.ContinuousTick && AcceptedPlaybackEnabled) return;
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
    internal static void Begin(PlayerEntity player, Vector3 origin = default,
        Vector3 direction = default, Vector3 aim = default, Vector3 view = default,
        Vector2 reticle = default, bool hasReticle = false, bool turret = false, EquipInfo? firedEquip = null)
    {
        if (player.SceneServices.IsReplica)
        {
            player.OwningScene.ReplayPoses?.BeginFire(player, turret);
            return;
        }
        if (!NetSession.Active || (uint)player.SlotIndex >= 8) return;
        if (TryAcceptedKey(player, out _)) return;
        var state = For(player.SlotIndex);
        state.NativeOrigin = origin; state.NativeDirection = direction.Normalized();
        if (UsesEvents(player))
        {
            if (!state.Selected) { state.Active = default; return; }
            // Retire only after successful creation (Commit), never before Spawn.
            state.Selected = false;
            return;
        }
        if (!CanAuthor(player)) { state.Active = default; return; }
        uint id = unchecked(++state.NextId); if (id == 0) id = ++state.NextId;
        var kind = turret ? FireEventKind.TurretFire : player.EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.Continuous)
            ? FireEventKind.ContinuousTick : player.EquipInfo.ChargeLevel >= player.EquipInfo.Weapon.MinCharge * 2
                && player.EquipInfo.Weapon.Flags.TestFlag(WeaponFlags.CanCharge) ? FireEventKind.ReleaseFire
            : player.Controls.Shoot.IsPressed ? FireEventKind.PressFire : FireEventKind.AutomaticFire;
        uint ack = NetUnlagged.LaunchFrameFor(player); byte sub = 0;
        if (NetSmoothing.AckPoint(out uint read, out byte fraction)) { ack = read; sub = fraction; }
        // A continuous pulse exists even when its muzzle hits a wall before the
        // homing selector runs. Its phase is the source clock, not a target result.
        uint shotState = kind == FireEventKind.ContinuousTick ? NetSession.NetFrame
            : player.CurrentWeapon == BeamType.Imperialist && player.EquipInfo.Zoomed
            ? FireEvent.ScopedStateBit : 0;
        if (kind != FireEventKind.ContinuousTick)
        { if (player.IsPrimeHunter) shotState |= FireEvent.PrimeStateBit;
          if (player.DoubleDamage) shotState |= FireEvent.DoubleStateBit;
          if (ReferenceEquals((firedEquip ?? player.EquipInfo).Weapon,
              player.OwningScene.WeaponRules[(int)(firedEquip?.Weapon.Beam ?? player.CurrentWeapon) + 9]))
              shotState |= FireEvent.AffinityStateBit;
        }
        if (!Finite(aim) || aim.LengthSquared < 0.000001f) aim = direction;
        if (!Finite(view) || view.LengthSquared < 0.000001f) view = aim;
        bool reticleValid = hasReticle && float.IsFinite(reticle.X) && float.IsFinite(reticle.Y)
            && reticle.X is >= 0 and <= 1 && reticle.Y is >= 0 and <= 1;
        bool poseValid = Finite(origin) && Finite(direction) && Finite(aim) && Finite(view)
            && direction.LengthSquared >= 0.000001f && aim.LengthSquared >= 0.000001f
            && view.LengthSquared >= 0.000001f;
        byte poseFlags = poseValid ? FireEvent.FlagPose : (byte)0;
        if (poseValid && reticleValid) poseFlags |= FireEvent.FlagReticle;
        if (kind == FireEventKind.ContinuousTick && player.CurrentWeapon == BeamType.ShockCoil)
        {
            poseFlags |= FireEvent.FlagContinuousContext;
            if (player.IsPrimeHunter) poseFlags |= FireEvent.FlagContinuousPrime;
            if (player.DoubleDamage) poseFlags |= FireEvent.FlagContinuousDouble;
        }
        state.Active = new(id, NetSession.NetFrame, ack, sub, kind, (byte)(firedEquip?.Weapon.Beam ?? player.CurrentWeapon),
            (byte)Math.Clamp((int)(firedEquip?.ChargeLevel ?? player.EquipInfo.ChargeLevel), 0, 255), shotState,
            poseFlags,
            poseValid ? origin : default,
            poseValid ? direction.Normalized() : default,
            poseValid ? aim.Normalized() : default,
            poseValid ? view.Normalized() : default,
            poseValid && reticleValid ? reticle : default,
            NetPlayerLifecycle.Get(player.SlotIndex), player.ModCaptureFireTarget());
        NetCampaignEvidence.TimelineFire("source-authored", ShotKey.For(player.SlotIndex, id), state.Active);
        NetShotProof.Admit(player, ShotKey.For(player.SlotIndex, id), state.Active);
    }
    internal static void NoteContinuousRamp(PlayerEntity player, int ramp)
    {
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8 || UsesEvents(player)) return;
        var state = For(player.SlotIndex);
        if (state.Active.HasContinuousContext)
            state.Active = state.Active with { PoseFlags = (byte)((state.Active.PoseFlags & ~112) | Math.Clamp(ramp, 0, 4) << 4) };
    }
    internal static void Commit(PlayerEntity player)
    {
        // Accepted playback commits this source once without borrowing outgoing
        // author history, consuming a native pending event, or rebasing its
        // source frame through For while an authority emission is in scope.
        if (NetSession.Active && !player.SceneServices.IsReplica
            && (uint)player.SlotIndex < 8 && _slots[player.SlotIndex].PlayingAccepted)
        {
            NetCampaignEvidence.Committed(player, _slots[player.SlotIndex].Active);
            return;
        }
        if (NetCampaignEvidence.Enabled && NetSession.Active && !player.SceneServices.IsReplica
            && (uint)player.SlotIndex < 8)
            NetCampaignEvidence.Committed(player, For(player.SlotIndex).Active);
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8) return;
        var committed = For(player.SlotIndex);
        if (NetSession.IsAuthority && committed.Active.Kind == FireEventKind.TurretFire && committed.Active.ShotId != 0)
        {
            var key = ShotKey.For(player.SlotIndex, committed.Active.ShotId) with { LifeId = committed.Active.SourceLife };
            NetSession.PublishTurretOutcome(new(key, NetSession.NetFrame, TurretFireOutcomeStatus.Emitted,
                TurretFireOutcomeReason.None, committed.Active, committed.NativeOrigin, committed.NativeDirection));
        }
        if (UsesEvents(player))
        { For(player.SlotIndex).Incoming.Consume(For(player.SlotIndex).Active.ShotId); return; }
        var state = For(player.SlotIndex);
        if (state.Active.ShotId == 0) return;
        state.Outgoing.Add(state.Active with {
            Target = state.Active.Kind == FireEventKind.ContinuousTick
                ? player.ModContinuousFireTick == state.Active.SourceFrame
                    ? player.ModContinuousNetworkTarget : NetTargetIdentity.None
                : state.Active.Target });
        NetShotProof.Fired(ShotKey.For(player.SlotIndex, state.Active.ShotId));
    }
    internal static IntentPacket WithoutTurretOffers(in IntentPacket intent)
    {
        var result = intent; result.FireEvents = default; result.FireEventCount = 0;
        for (int i = 0; i < intent.FireEventCount; i++)
            if (intent.FireEvents[i].Kind != FireEventKind.TurretFire)
                result.FireEvents[result.FireEventCount++] = intent.FireEvents[i];
        return result;
    }
    internal static void Fill(ref IntentPacket intent, int slot)
    {
        For(slot).Outgoing.Fill(ref intent);
    }
    internal static bool PendingAccepted(ShotKey key, in FireEvent fire)
    {
        if ((uint)key.ShooterSlot >= 8) return false;
        var state = _slots[key.ShooterSlot];
        return state.PlayingAccepted && state.ActiveKey == key
            && state.Ledger.TryGet(key, out var shot, out var disposition, out _)
            && disposition == ShotDisposition.Pending && shot.Fire == fire;
    }
    internal static bool TryActiveAttempt(PlayerEntity player, out ShotKey key, out FireEvent fire)
    {
        key = default; fire = default;
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8) return false;
        var state = _slots[player.SlotIndex]; fire = state.Active;
        if (fire.ShotId == 0) return false;
        key = state.PlayingAccepted ? state.ActiveKey : ShotKey.For(player.SlotIndex, fire.ShotId)
            with { LifeId = fire.SourceLife };
        return key.LifeId != 0;
    }
    internal static bool TryTiming(PlayerEntity player, out FireEvent e)
    {
        if (TryAcceptedKey(player, out _)) { e = _slots[player.SlotIndex].Active; return true; }
        if (player.SceneServices.IsReplica)
        {
            e = default;
            return player.OwningScene.ReplayPoses?.TryActiveFire(player, out e) == true
                && e.ShotId != 0;
        }
        e = (uint)player.SlotIndex < 8 ? For(player.SlotIndex).Active : default;
        return UsesEvents(player) && e.ShotId != 0;
    }

    internal static bool TryContinuousTiming(PlayerEntity player, uint shotId, uint phase, out FireEvent fire)
    {
        fire = default;
        if (!NetSession.Active || player.SceneServices.IsReplica || (uint)player.SlotIndex >= 8) return false;
        fire = _slots[player.SlotIndex].Active;
        return shotId != 0 && fire.ShotId == shotId && fire.Kind == FireEventKind.ContinuousTick
            && fire.SourceFrame == phase && fire.AckFrame != 0;
    }

    internal static bool TryAuthoredPose(PlayerEntity player, out Vector3 origin,
        out Vector3 direction, out Vector3 aim)
    {
        origin = direction = aim = default;
        if (!(TryAcceptedKey(player, out _) || UsesEvents(player)) || !TryTiming(player, out FireEvent fire) || !fire.HasPose)
            return false;
        origin = fire.Origin;
        direction = fire.Direction;
        aim = fire.Aim;
        return Finite(origin) && Finite(direction) && Finite(aim)
            && direction.LengthSquared >= 0.000001f && aim.LengthSquared >= 0.000001f;
    }
}
