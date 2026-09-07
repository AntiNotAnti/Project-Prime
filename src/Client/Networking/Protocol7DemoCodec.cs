using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>Frozen historical layouts. No live socket dispatch calls this adapter.</summary>
    internal static class Protocol7DemoCodec
    {
        internal static bool TryReadSnapshot(ReadOnlySpan<byte> bytes, Span<SnapshotPlayer> players,
            out SnapshotPacket packet, out int count)
        {
            packet = default;
            count = 0;
            Span<Protocol7SnapshotPlayer> old = stackalloc Protocol7SnapshotPlayer[8];
            if (!Protocol7SnapshotPacket.TryRead(bytes, old, out Protocol7SnapshotPacket header, out int parsed)
                || parsed > players.Length) return false;
            for (int i = 0; i < parsed; i++)
            {
                Protocol7SnapshotPlayer p = old[i];
                players[i] = new SnapshotPlayer
                {
                    Slot = p.Slot, Hunter = p.Hunter, TeamIndex = p.TeamIndex, Weapon = p.Weapon,
                    Flags = (SnapshotPlayerFlags)p.Flags, Health = p.Health, AmmoUa = p.AmmoUa,
                    AmmoMissiles = p.AmmoMissiles, Life = p.Life, ConnectionId = p.ConnectionId,
                    Position = p.Position, Speed = p.Speed, Aim = p.Aim, Facing = p.Facing,
                    Points = p.Points, Kills = p.Kills, Deaths = p.Deaths,
                    AvailableWeapons = p.AvailableWeapons, FrozenTicks = p.FrozenTicks
                    // Historical snapshots never encoded burn/disruption duration or assists.
                };
            }
            packet = new SnapshotPacket(header.ServerTick, header.Sequence, header.MatchId,
                header.LastProcessedInput, header.HasProcessedInput, header.Rng1, header.Rng2);
            count = parsed;
            return true;
        }

        internal static bool TryReadMatch(ReadOnlySpan<byte> bytes, out MatchTransitionPacket match)
        {
            match = default;
            if (bytes.Length != 76 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0
                || !Protocol7DemoRules.TryRead(bytes[8..], out MatchRules rules)) return false;
            match = new MatchTransitionPacket(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), rules);
            return true;
        }
    }
}
