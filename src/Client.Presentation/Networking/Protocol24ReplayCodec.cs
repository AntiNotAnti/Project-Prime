// Frozen protocol-24 replay layouts. Never called for live traffic.
using System;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Protocol 24 changed match-rule metadata but did not change gameplay
    /// snapshot records. Keep the 112-byte player wrapper explicit so the
    /// protocol-25 live suffix cannot be mistaken for a replay field.
    /// </summary>
    internal static class Protocol24ReplayCodec
    {
        internal const int HistoricalPlayerSize = SnapshotPlayer.LegacySize;

        internal static bool TryReadSnapshot(ReadOnlySpan<byte> bytes,
            Span<SnapshotPlayer> players, out SnapshotPacket packet,
            out int count)
            => Protocol23ReplayCodec.TryReadSnapshot(bytes, players,
                out packet, out count);

        internal static bool TryReadCombatBatch(ReadOnlySpan<byte> bytes,
            Span<CombatEvent> events, out int count)
            => Protocol23ReplayCodec.TryReadCombatBatch(bytes, events,
                out count);
    }
}
