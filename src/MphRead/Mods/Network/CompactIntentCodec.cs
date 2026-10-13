using System;
using MphRead.Mods.Input;

namespace MphRead.Mods.Network;

/// <summary>Live intent encoding. Historical canonical replays retain their original width.</summary>
public static class CompactIntentCodec
{
    public const int ShotInputOffset = IntentPacket.LegacyFullSize + 1;
    public const int TouchOffset = ShotInputOffset + ShotInputHistory.WireSize;
    // Optional touch reports are present only for the native control mode.
    // ModernFlick traffic and 14-event combat packets retain the original MTU.
    public const int HeaderSize = TouchOffset;
    public const byte TouchPresent = 0x80;
    public const byte TouchMode = 0x40;

    private static bool IncludeTouch(in IntentPacket intent)
        => intent.NativeTouch.Active && intent.FireEventCount < NetFireEvents.LiveCapacity;

    public static int Size(in IntentPacket intent)
    {
        if (intent.FireEventCount > NetFireEvents.LiveCapacity)
            throw new ArgumentOutOfRangeException(nameof(intent), "Too many fire events.");
        if (!intent.ShotInputs.Valid(intent.ShotInputCount))
            throw new ArgumentException("Invalid shot input history.", nameof(intent));
        return HeaderSize + (IncludeTouch(intent) ? NativeTouchReport.WireSize : 0)
            + intent.FireEventCount * FireEvent.Size;
    }

    public static int Write(in IntentPacket intent, Span<byte> destination)
    {
        int size = Size(intent);
        if (destination.Length < size)
            throw new ArgumentException("Destination is too short.", nameof(destination));
        bool hasTouch = IncludeTouch(intent);
        int eventStart = HeaderSize + (hasTouch ? NativeTouchReport.WireSize : 0);
        intent.Write(destination[..IntentPacket.LegacyFullSize]);
        destination[IntentPacket.LegacyFullSize] = intent.FireEventCount;
        destination[ShotInputOffset] = (byte)(intent.ShotInputCount
            | (intent.NativeTouch.Active ? TouchMode : 0)
            | (hasTouch ? TouchPresent : 0));
        for (int i = 0; i < ShotInputHistory.Capacity; i++)
            destination[ShotInputOffset + 1 + i] = intent.ShotInputs[i];
        if (hasTouch) intent.NativeTouch.Write(destination.Slice(TouchOffset, NativeTouchReport.WireSize));
        for (int i = 0; i < intent.FireEventCount; i++)
            intent.FireEvents[i].Write(destination.Slice(eventStart + i * FireEvent.Size, FireEvent.Size));
        return size;
    }

    public static bool TryRead(ReadOnlySpan<byte> payload, out IntentPacket intent)
    {
        intent = default;
        if (payload.Length < HeaderSize) return false;
        byte count = payload[IntentPacket.LegacyFullSize];
        bool hasTouch = (payload[ShotInputOffset] & TouchPresent) != 0;
        bool touchMode = (payload[ShotInputOffset] & TouchMode) != 0;
        int eventStart = HeaderSize + (hasTouch ? NativeTouchReport.WireSize : 0);
        if (count > NetFireEvents.LiveCapacity || hasTouch && !touchMode
            || hasTouch && count == NetFireEvents.LiveCapacity
            || payload.Length != eventStart + count * FireEvent.Size) return false;
        IntentPacket decoded = IntentPacket.Read(payload[..IntentPacket.LegacyFullSize]);
        if (hasTouch)
        {
            decoded.NativeTouch = NativeTouchReport.Read(payload.Slice(TouchOffset, NativeTouchReport.WireSize));
            if (!decoded.NativeTouch.Active || decoded.NativeTouch.Suppressed) return false;
        }
        else if (touchMode)
        {
            // A full 14-event frame has no room for an extra touch sidecar.
            // Preserve ClassicTouch ownership, not the previous roll impulse.
            decoded.NativeTouch = new NativeTouchReport(true, false, false, 0, 0, 0, false, true);
        }
        decoded.HasFireEvents = true;
        decoded.FireEventCount = count;
        decoded.ShotInputCount = (byte)(payload[ShotInputOffset] & ~(TouchPresent | TouchMode));
        for (int i = 0; i < ShotInputHistory.Capacity; i++)
            decoded.ShotInputs[i] = payload[ShotInputOffset + 1 + i];
        if (!decoded.ShotInputs.Valid(decoded.ShotInputCount)) return false;
        for (int i = 0; i < count; i++)
        {
            var fire = FireEvent.Read(payload.Slice(eventStart + i * FireEvent.Size, FireEvent.Size));
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
