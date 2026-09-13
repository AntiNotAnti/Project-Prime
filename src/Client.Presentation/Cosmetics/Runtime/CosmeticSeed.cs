using System;
using System.Buffers.Binary;
using System.Text;

namespace MphRead.Cosmetics.Presentation;

/// <summary>
/// Presentation-only deterministic seed derivation. The caller supplies
/// immutable match/life/effect facts; authoritative simulation RNG state is
/// intentionally absent from this API.
/// </summary>
public static class CosmeticSeed
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Derive(Guid matchId, byte playerSlot, uint lifeCounter,
        ushort effectId, uint eventTick, uint stream = 0)
    {
        Span<byte> bytes = stackalloc byte[16 + 1 + 4 + 2 + 4 + 4];
        if (!matchId.TryWriteBytes(bytes, bigEndian: true, out int written)
            || written != 16)
        {
            throw new InvalidOperationException("Could not encode match identity.");
        }
        bytes[16] = playerSlot;
        BinaryPrimitives.WriteUInt32BigEndian(bytes[17..], lifeCounter);
        BinaryPrimitives.WriteUInt16BigEndian(bytes[21..], effectId);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[23..], eventTick);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[27..], stream);
        return Hash(bytes);
    }

    public static ulong Derive(Guid matchId, byte playerSlot, uint lifeCounter,
        string effectKey, uint eventTick, uint stream = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectKey);
        ulong seed = Derive(matchId, playerSlot, lifeCounter, effectId: 0,
            eventTick, stream);
        return Hash(Encoding.UTF8.GetBytes(effectKey), seed);
    }

    private static ulong Hash(ReadOnlySpan<byte> bytes, ulong seed = OffsetBasis)
    {
        ulong hash = seed;
        foreach (byte value in bytes)
        {
            hash ^= value;
            hash *= Prime;
        }
        return hash;
    }
}
