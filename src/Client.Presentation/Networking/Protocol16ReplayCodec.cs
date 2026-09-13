// Frozen protocol 8-16 snapshot layout. Never called by live networking.
using System;

namespace MphRead.Mods.Network
{
    internal static class Protocol16ReplayCodec
    {
        private const int HistoricalPlayerSize = 96;

        internal static bool TryReadSnapshot(ReadOnlySpan<byte> bytes,
            Span<SnapshotPlayer> players, out SnapshotPacket packet, out int count)
        {
            packet = default;
            count = 0;
            if (bytes.Length < SnapshotPacket.HeaderSize || bytes[16] > 1
                || bytes[17] > 8 || bytes[17] > players.Length
                || bytes.Length != SnapshotPacket.HeaderSize + bytes[17] * HistoricalPlayerSize)
            {
                return false;
            }

            int playerCount = bytes[17];
            int expandedLength = SnapshotPacket.HeaderSize + playerCount * SnapshotPlayer.Size;
            Span<byte> expanded = stackalloc byte[SnapshotPacket.MaxSize];
            bytes[..SnapshotPacket.HeaderSize].CopyTo(expanded);
            for (int i = 0; i < playerCount; i++)
            {
                bytes.Slice(SnapshotPacket.HeaderSize + i * HistoricalPlayerSize,
                    HistoricalPlayerSize).CopyTo(expanded.Slice(
                        SnapshotPacket.HeaderSize + i * SnapshotPlayer.Size,
                        HistoricalPlayerSize));
                // Protocol 8-16 did not encode charge presentation state.
                expanded.Slice(SnapshotPacket.HeaderSize + i * SnapshotPlayer.Size
                    + HistoricalPlayerSize, sizeof(ushort)).Clear();
            }
            return SnapshotPacket.TryRead(expanded[..expandedLength], players,
                out packet, out count);
        }
    }
}
