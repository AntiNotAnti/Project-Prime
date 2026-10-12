using System;
using MphRead.Mods.Input;

namespace MphRead.Mods.Network;

/// <summary>Live intent encoding. Replay records retain IntentPacket's fixed-width format.</summary>
public static class CompactIntentCodec
{
    public const int ShotInputOffset = IntentPacket.LegacyFullSize + 1;
    public const int TouchOffset = ShotInputOffset + ShotInputHistory.WireSize;
    public const int HeaderSize = TouchOffset + NativeTouchReport.WireSize;

    public static int Size(in IntentPacket intent)
    {
        if (intent.FireEventCount > NetFireEvents.LiveCapacity)
            throw new ArgumentOutOfRangeException(nameof(intent), "Too many fire events.");
        if (!intent.ShotInputs.Valid(intent.ShotInputCount))
            throw new ArgumentException("Invalid shot input history.", nameof(intent));
        return HeaderSize + intent.FireEventCount * FireEvent.Size;
    }

    public static int Write(in IntentPacket intent, Span<byte> destination)
    {
        int size = Size(intent);
        if (destination.Length < size)
            throw new ArgumentException("Destination is too short.", nameof(destination));
        intent.Write(destination[..IntentPacket.LegacyFullSize]);
        destination[IntentPacket.LegacyFullSize] = intent.FireEventCount;
        destination[ShotInputOffset] = intent.ShotInputCount;
        for (int i = 0; i < ShotInputHistory.Capacity; i++)
            destination[ShotInputOffset + 1 + i] = intent.ShotInputs[i];
        intent.NativeTouch.Write(destination.Slice(TouchOffset, NativeTouchReport.WireSize));
        for (int i = 0; i < intent.FireEventCount; i++)
            intent.FireEvents[i].Write(destination.Slice(HeaderSize + i * FireEvent.Size, FireEvent.Size));
        return size;
    }

    public static bool TryRead(ReadOnlySpan<byte> payload, out IntentPacket intent)
    {
        intent = default;
        if (payload.Length < HeaderSize) return false;
        byte count = payload[IntentPacket.LegacyFullSize];
        if (count > NetFireEvents.LiveCapacity || payload.Length != HeaderSize + count * FireEvent.Size)
            return false;
        IntentPacket decoded = IntentPacket.Read(payload[..IntentPacket.LegacyFullSize]);
        decoded.NativeTouch = NativeTouchReport.Read(payload.Slice(TouchOffset, NativeTouchReport.WireSize));
        decoded.HasFireEvents = true;
        decoded.FireEventCount = count;
        decoded.ShotInputCount = payload[ShotInputOffset];
        for (int i = 0; i < ShotInputHistory.Capacity; i++)
            decoded.ShotInputs[i] = payload[ShotInputOffset + 1 + i];
        if (!decoded.ShotInputs.Valid(decoded.ShotInputCount)) return false;
        for (int i = 0; i < count; i++)
        {
            var fire = FireEvent.Read(payload.Slice(HeaderSize + i * FireEvent.Size, FireEvent.Size));
            // Live events carry their own source life. Only legacy canonical
            // replay records may inherit it from a carrier.
            if (fire.SourceLife == 0) return false;
            decoded.FireEvents[i] = fire;
        }
        if (!float.IsFinite(decoded.Aim.X) || !float.IsFinite(decoded.Aim.Y) || !float.IsFinite(decoded.Aim.Z)
            || !float.IsFinite(decoded.Position.X) || !float.IsFinite(decoded.Position.Y) || !float.IsFinite(decoded.Position.Z)
            || !NetFireEvents.Validate(decoded)) return false;
        intent = decoded;
        return true;
    }
}
