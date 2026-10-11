using System;
using System.Buffers.Binary;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Cosmetic confirmation only. Neither admission nor damage/healing reads this lane.
internal readonly record struct ContinuousPresentation(ushort OwnerLife, uint Phase,
    NetTargetIdentity Target, Vector3 Endpoint)
{
    internal const int Size = 18;
    internal bool Valid => OwnerLife != 0 && Phase != 0 && Target.IsWellFormed && Target.HasPlayer;
    internal void Write(Span<byte> bytes)
    {
        bytes[..Size].Clear();
        if (!Valid || !Finite(Endpoint) || Math.Abs(Endpoint.X) >= 512
            || Math.Abs(Endpoint.Y) >= 512 || Math.Abs(Endpoint.Z) >= 512) return;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, OwnerLife);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[2..], Phase);
        bytes[6] = Target.EncodedSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[7..], Target.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[9..], Target.LifeId);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[11..], (short)(Endpoint.X * 64));
        BinaryPrimitives.WriteInt16LittleEndian(bytes[13..], (short)(Endpoint.Y * 64));
        BinaryPrimitives.WriteInt16LittleEndian(bytes[15..], (short)(Endpoint.Z * 64));
        bytes[17] = 1;
    }
    internal static bool WellFormed(ReadOnlySpan<byte> bytes) => bytes.Length >= Size
        && (bytes[17] == 0 && bytes[..Size].IndexOfAnyExcept((byte)0) < 0
            || bytes[17] == 1 && Read(bytes).Valid);
    internal static ContinuousPresentation Read(ReadOnlySpan<byte> bytes) => bytes[17] != 1 ? default : new(
        BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]),
        new NetTargetIdentity(bytes[6], BinaryPrimitives.ReadUInt16LittleEndian(bytes[7..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[9..])),
        new Vector3(BinaryPrimitives.ReadInt16LittleEndian(bytes[11..]) / 64f,
            BinaryPrimitives.ReadInt16LittleEndian(bytes[13..]) / 64f, BinaryPrimitives.ReadInt16LittleEndian(bytes[15..]) / 64f));
    private static bool Finite(Vector3 p) => Single.IsFinite(p.X) && Single.IsFinite(p.Y) && Single.IsFinite(p.Z);
}

internal static class NetContinuousPresentation
{
    private static readonly (ShotKey Key, ContinuousPresentation View, uint Frame)[] Latest = new (ShotKey, ContinuousPresentation, uint)[8];
    internal static void Confirm(in ContinuousPaymentKey key, Vector3 point)
    {
        if ((uint)key.OwnerTick.ShooterSlot >= 8) return;
        ref var prior = ref Latest[key.OwnerTick.ShooterSlot];
        if (prior.Key.Generation == key.OwnerTick.Generation && prior.Key.LifeId == key.OwnerTick.LifeId
            && prior.Key.MatchId == key.OwnerTick.MatchId && prior.Key.AuthorityEpoch == key.OwnerTick.AuthorityEpoch
            && prior.View.Valid && !SequenceMath.Newer(key.OwnerTick.ShotId, prior.View.Phase)) return;
        prior = (key.OwnerTick, new(key.OwnerTick.LifeId, key.OwnerTick.ShotId, key.Victim, point), NetSession.NetFrame);
    }
    internal static ContinuousPresentation Capture(in PlayerState state)
    {
        if (state.SlotIndex >= 8) return default;
        var value = Latest[state.SlotIndex];
        return NetSession.NetFrame - value.Frame <= 12
            && NetSession.MatchesStream(value.Key.MatchId, value.Key.AuthorityEpoch)
            && state.SlotGeneration == value.Key.Generation && state.LifeId == value.Key.LifeId
            && NetPlayerLifecycle.Matches(value.View.Target.Slot, value.View.Target.Generation, value.View.Target.LifeId)
            ? value.View : default;
    }
    internal static void Reset() => Array.Clear(Latest);
    internal static void ForgetSlot(int slot)
    {
        for (int i = 0; i < Latest.Length; i++)
            if (i == slot || Latest[i].View.Target.Slot == slot) Latest[i] = default;
    }
}
