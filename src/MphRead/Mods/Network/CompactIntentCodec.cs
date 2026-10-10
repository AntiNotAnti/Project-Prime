using System;

namespace MphRead.Mods.Network;

/// <summary>Live intent encoding. Replay records retain IntentPacket's fixed-width format.</summary>
public static class CompactIntentCodec
{
    public const int HeaderSize = IntentPacket.LegacyFullSize + 1;

    public static int Size(in IntentPacket intent)
    {
        if (intent.FireEventCount > NetFireEvents.Capacity)
            throw new ArgumentOutOfRangeException(nameof(intent), "Too many fire events.");
        return HeaderSize + intent.FireEventCount * FireEvent.Size;
    }

    public static int Write(in IntentPacket intent, Span<byte> destination)
    {
        int size = Size(intent);
        if (destination.Length < size)
            throw new ArgumentException("Destination is too short.", nameof(destination));
        intent.Write(destination[..IntentPacket.LegacyFullSize]);
        destination[IntentPacket.LegacyFullSize] = intent.FireEventCount;
        for (int i = 0; i < intent.FireEventCount; i++)
            intent.FireEvents[i].Write(destination.Slice(HeaderSize + i * FireEvent.Size, FireEvent.Size));
        return size;
    }

    public static bool TryRead(ReadOnlySpan<byte> payload, out IntentPacket intent)
    {
        intent = default;
        if (payload.Length < HeaderSize) return false;
        byte count = payload[IntentPacket.LegacyFullSize];
        if (count > NetFireEvents.Capacity || payload.Length != HeaderSize + count * FireEvent.Size)
            return false;
        IntentPacket decoded = IntentPacket.Read(payload[..IntentPacket.LegacyFullSize]);
        decoded.HasFireEvents = true;
        decoded.FireEventCount = count;
        for (int i = 0; i < count; i++)
            decoded.FireEvents[i] = FireEvent.Read(payload.Slice(HeaderSize + i * FireEvent.Size, FireEvent.Size));
        if (!float.IsFinite(decoded.Aim.X) || !float.IsFinite(decoded.Aim.Y) || !float.IsFinite(decoded.Aim.Z)
            || !float.IsFinite(decoded.Position.X) || !float.IsFinite(decoded.Position.Y) || !float.IsFinite(decoded.Position.Z)
            || !NetFireEvents.Validate(decoded)) return false;
        intent = decoded;
        return true;
    }
}
