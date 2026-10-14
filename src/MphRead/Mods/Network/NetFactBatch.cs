using System;
using MphRead.Mods.MatchEvents;

namespace MphRead.Mods.Network;

// Protocol 46 carries one or more same-type authority facts per reliable
// envelope. Individual semantic/replay records and their identities are unchanged.
internal static class NetFactBatch
{
    internal const int MaximumPayload = 1200 - NetHeader.Size - 4;
    internal static int Stride(PacketType type) => type switch
    {
        PacketType.MatchSemanticEvent => MatchSemanticEventPacket.Size - 1,
        PacketType.MatchAward => MatchAwardPacket.Size - 1,
        PacketType.ReplayShotFact => ReplayShotFactPacket.Size,
        PacketType.BombEvent => BombEvent.Size,
        PacketType.TurretFireOutcome => TurretFireOutcome.Size,
        PacketType.PickupVerdict => PickupVerdict.Size,
        PacketType.PickupItem => PickupItemFact.Size,
        _ => 0
    };
    internal static bool Validate(PacketType type, ReadOnlySpan<byte> payload)
    {
        int stride = Stride(type);
        if (stride == 0 || payload.IsEmpty || payload.Length > MaximumPayload || payload.Length % stride != 0) return false;
        Span<byte> packet = stackalloc byte[MatchSemanticEventPacket.Size];
        packet[0] = (byte)type;
        for (int offset = 0; offset < payload.Length; offset += stride)
        {
            var record = payload.Slice(offset, stride);
            if (type == PacketType.BombEvent)
            { if (!BombEvent.TryRead(record, out _)) return false; }
            else if (type == PacketType.TurretFireOutcome)
            { if (!TurretFireOutcome.TryRead(record, out _)) return false; }
            else if (type == PacketType.PickupVerdict)
            { if (!PickupVerdict.TryRead(record, out _)) return false; }
            else if (type == PacketType.PickupItem)
            { if (!PickupItemFact.TryRead(record, out _)) return false; }
            else if (type == PacketType.ReplayShotFact)
            { if (!ReplayShotFactPacket.TryRead(record, out _)) return false; }
            else
            {
                record.CopyTo(packet[1..]);
                if (type == PacketType.MatchSemanticEvent
                    ? !MatchSemanticEventPacket.TryRead(packet[..(stride + 1)], out _)
                    : !MatchAwardPacket.TryRead(packet[..(stride + 1)], out _)) return false;
            }
        }
        return true;
    }
}
