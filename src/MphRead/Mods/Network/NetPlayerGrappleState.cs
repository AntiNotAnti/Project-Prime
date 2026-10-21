using System;
using System.Buffers.Binary;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Accepted accepted state. The enclosing snapshot/slow entry owns the
// match, epoch, slot, generation and life fence. This is never a client anchor.
public readonly record struct PlayerGrappleState(bool Active, ushort RequestNonce,
    Vector3 Anchor, float RopeLength)
{
    public const int Size = 19;
    public const float MaximumCoordinate = 100000f;
    private const byte ActiveFlag = 1;

    public static PlayerGrappleState Inactive(ushort requestNonce)
        => new(false, requestNonce, Vector3.Zero, 0f);

    public bool IsValid => Active
        ? RequestNonce != 0 && GrappleMovementV1.Finite(Anchor)
            && MathF.Abs(Anchor.X) <= MaximumCoordinate
            && MathF.Abs(Anchor.Y) <= MaximumCoordinate
            && MathF.Abs(Anchor.Z) <= MaximumCoordinate
            && GrappleMovementV1.ValidAcceptedLength(RopeLength)
        : PositiveZero(Anchor.X) && PositiveZero(Anchor.Y) && PositiveZero(Anchor.Z)
            && PositiveZero(RopeLength);

    private static bool PositiveZero(float value) => BitConverter.SingleToInt32Bits(value) == 0;

    // All validation precedes any destination mutation. A larger destination is
    // allowed; only the first19 bytes are written and the rest is untouched.
    public bool TryWrite(Span<byte> destination, out int written)
    {
        written = 0;
        if (destination.Length < Size || !IsValid) return false;
        destination[0] = Active ? ActiveFlag : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[1..], RequestNonce);
        BinaryPrimitives.WriteSingleLittleEndian(destination[3..], Anchor.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[7..], Anchor.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[11..], Anchor.Z);
        BinaryPrimitives.WriteSingleLittleEndian(destination[15..], RopeLength);
        written = Size;
        return true;
    }

    // Exact width and canonical inactive zeros are part of the wire contract.
    // On failure, out state remains default; no externally owned state mutates.
    public static bool TryRead(ReadOnlySpan<byte> source, out PlayerGrappleState state)
    {
        state = default;
        if (source.Length != Size || (source[0] & ~ActiveFlag) != 0) return false;
        var decoded = new PlayerGrappleState((source[0] & ActiveFlag) != 0,
            BinaryPrimitives.ReadUInt16LittleEndian(source[1..]),
            new Vector3(BinaryPrimitives.ReadSingleLittleEndian(source[3..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[7..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[11..])),
            BinaryPrimitives.ReadSingleLittleEndian(source[15..]));
        if (!decoded.IsValid) return false;
        state = decoded;
        return true;
    }
}

internal enum GrappleRequestOrder : byte { Invalid, Same, Newer, Older, Ambiguous }

internal static class GrappleRequestOrdering
{
    // Zero is reserved for no request. A zero baseline has no prior request;
    // the first nonzero candidate establishes it. Equality does not authorize
    // another admission; half-range differences must be rejected, not guessed.
    internal static GrappleRequestOrder Compare(ushort candidate, ushort baseline)
    {
        if (candidate == 0) return GrappleRequestOrder.Invalid;
        if (baseline == 0) return GrappleRequestOrder.Newer;
        ushort distance = unchecked((ushort)(candidate - baseline));
        return distance == 0 ? GrappleRequestOrder.Same
            : distance == 0x8000 ? GrappleRequestOrder.Ambiguous
            : distance < 0x8000 ? GrappleRequestOrder.Newer : GrappleRequestOrder.Older;
    }

    internal static ushort Next(ushort previous)
        => previous == ushort.MaxValue ? (ushort)1 : (ushort)(previous + 1);
}
