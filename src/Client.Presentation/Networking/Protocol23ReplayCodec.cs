// Frozen protocol-23 replay layouts. Never call this codec for live traffic.
using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Reads the protocol-23 authoritative presentation records after the live
    /// protocol has advanced. The explicit sizes are deliberate: a future live
    /// schema must not reinterpret an existing replay simply because the live
    /// decoder still happens to accept its prefix.
    /// </summary>
    internal static class Protocol23ReplayCodec
    {
        private const int HistoricalHeaderSize = 26;
        private const int HistoricalPlayerSize = SnapshotPlayer.LegacySize;
        private const int HistoricalCombatEventSize = 82;
        private const int HistoricalCombatMaxCount = 6;

        internal static bool TryReadSnapshot(ReadOnlySpan<byte> bytes,
            Span<SnapshotPlayer> players, out SnapshotPacket packet,
            out int count)
        {
            packet = default;
            count = 0;
            if (bytes.Length < HistoricalHeaderSize || bytes[16] > 1
                || bytes[17] > 8 || bytes[17] > players.Length
                || bytes.Length != HistoricalHeaderSize
                    + bytes[17] * HistoricalPlayerSize)
            {
                return false;
            }

            int slotMask = 0;
            for (int i = 0; i < bytes[17]; i++)
            {
                ReadOnlySpan<byte> source = bytes.Slice(
                    HistoricalHeaderSize + i * HistoricalPlayerSize,
                    HistoricalPlayerSize);
                if (!SnapshotPlayer.TryReadLegacy(source, out SnapshotPlayer decoded)
                    || (slotMask & (1 << decoded.Slot)) != 0)
                {
                    return false;
                }
                slotMask |= 1 << decoded.Slot;
                players[i] = decoded;
            }

            packet = new SnapshotPacket(
                BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
                bytes[16] != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[18..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[22..]));
            count = bytes[17];
            return true;
        }

        internal static bool TryReadCombatBatch(ReadOnlySpan<byte> bytes,
            Span<CombatEvent> events, out int count)
        {
            count = 0;
            if (bytes.IsEmpty || bytes[0] is < 1 or > HistoricalCombatMaxCount
                || bytes[0] > events.Length
                || bytes.Length != 1 + bytes[0] * HistoricalCombatEventSize)
            {
                return false;
            }

            for (int i = 0; i < bytes[0]; i++)
            {
                if (!CombatEvent.TryRead(bytes.Slice(
                        1 + i * HistoricalCombatEventSize,
                        HistoricalCombatEventSize), out CombatEvent value)
                    || value.Kind > CombatEventKind.Effect
                    || (value.Flags & ~(CombatEventFlags)511) != 0)
                {
                    return false;
                }
                events[i] = value;
            }
            count = bytes[0];
            return true;
        }
    }
}
