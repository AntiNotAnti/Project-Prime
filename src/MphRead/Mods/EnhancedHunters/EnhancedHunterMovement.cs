using System;
using System.Buffers.Binary;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
public struct EnhancedMovementEvent
{
    public const int Size = 8;
    public ushort Sequence;
    public short X, Y, Z;
    internal readonly Vector3 Impulse => new Vector3(X, Y, Z) / 32767f;
    internal void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, Sequence);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[2..], X);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[4..], Y);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[6..], Z);
    }
    internal static EnhancedMovementEvent Read(ReadOnlySpan<byte> bytes) => new()
    {
        Sequence = BinaryPrimitives.ReadUInt16LittleEndian(bytes), X = BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]),
        Y = BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]), Z = BinaryPrimitives.ReadInt16LittleEndian(bytes[6..])
    };
}
internal static class EnhancedHunterMovement
{
    internal static void PushOwner(PlayerEntity owner, Vector3 impulse)
    {
        if (!EnhancedHunters.Enabled(owner) || !EnhancedHunters.Alive(owner)) return;
        if (impulse.LengthSquared > .4f * .4f) impulse = impulse.Normalized() * .4f;
        var s = owner.EnhancedState;
        ushort next = unchecked((ushort)(s.Impulse0.Sequence + 1));
        if (next == 0) next = 1;
        s.Impulse1 = s.Impulse0;
        s.Impulse0 = new EnhancedMovementEvent { Sequence = next, X = (short)(impulse.X * 32767),
            Y = (short)(impulse.Y * 32767), Z = (short)(impulse.Z * 32767) };
        s.AppliedImpulse = next;
        owner.Speed += impulse;
    }
    internal static void Predict(PlayerEntity owner, Vector3 impulse)
    {
        if (EnhancedHunters.Authority(owner) || owner.SceneServices.IsReplica
            || owner.SceneServices.PlayerReplication.LocalSlot != owner.SlotIndex) return;
        var s = owner.EnhancedState;
        if (s.PredictedUntil > (int)owner.OwningScene.FrameCount) return;
        if (impulse.LengthSquared > .16f) impulse = impulse.Normalized() * .4f;
        s.PredictedImpulse = impulse; s.PredictedUntil = (int)owner.OwningScene.FrameCount + 120;
        owner.Speed += impulse;
    }
    internal static void Apply(PlayerEntity owner, EnhancedMovementEvent older, EnhancedMovementEvent newer)
    {
        ApplyOne(owner, older); ApplyOne(owner, newer);
    }
    private static void ApplyOne(PlayerEntity owner, EnhancedMovementEvent value)
    {
        var s = owner.EnhancedState;
        if (value.Sequence == 0 || s.AppliedImpulse != 0 && (short)(value.Sequence - s.AppliedImpulse) <= 0) return;
        s.AppliedImpulse = value.Sequence;
        var impulse = value.Impulse;
        if (impulse.LengthSquared <= .41f * .41f)
        {
            if (s.PredictedUntil > (int)owner.OwningScene.FrameCount
                && (s.PredictedImpulse - impulse).LengthSquared < .0025f)
            { s.PredictedUntil = 0; s.PredictedImpulse = default; }
            else owner.Speed += impulse;
        }
    }
}
