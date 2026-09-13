using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Frozen decoder for the original protocol-8 match-rule extension layout.
    /// This is replay-only: live traffic must use <see cref="MatchTransitionPacket"/>.
    /// </summary>
    internal static class Protocol8ReplayCodec
    {
        internal static bool TryReadMatch(ReadOnlySpan<byte> bytes,
            out MatchTransitionPacket match)
        {
            match = default;
            if (bytes.Length != MatchTransitionPacket.Size
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0
                || !TryReadRules(bytes[8..], out MatchRules rules)) return false;
            match = new MatchTransitionPacket(
                BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), rules);
            return true;
        }

        private static bool TryReadRules(ReadOnlySpan<byte> source,
            out MatchRules rules)
        {
            rules = null!;
            if (source.Length != MatchRulesWire.Size
                || source[0] < (byte)GameMode.Battle
                || source[0] > (byte)GameMode.PrimeHunter
                || source[1] is < 1 or > 8 || source[2] > 15 || source[3] > 2
                || source[68] > (byte)SpawnPolicy.Duel
                || BinaryPrimitives.ReadUInt16LittleEndian(source[72..]) > 1
                || !NetWireIdentity.ValidText(
                    source.Slice(28, MatchStatePacket.MaxNameBytes))) return false;
            // In the original protocol-8 layout only spawn policy and the
            // cancel-protection flag used extension bytes 68 through 83.
            for (int i = 69; i < MatchRulesWire.Size; i++)
                if (i is not (72 or 73) && source[i] != 0) return false;

            int score = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
            int lives = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
            long limit = BinaryPrimitives.ReadInt64LittleEndian(source[12..]);
            long objective = BinaryPrimitives.ReadInt64LittleEndian(source[20..]);
            if (score < 0 || lives < 0 || limit < -1 || objective < -1
                || limit > (long)int.MaxValue * TimeSpan.TicksPerSecond / 60)
                return false;
            string room = NetText.Read(
                source.Slice(28, MatchStatePacket.MaxNameBytes));
            if (String.IsNullOrWhiteSpace(room)) return false;
            try
            {
                rules = new MatchRules(((GameMode)source[0]).ToMatchMode(), room,
                    source[1], limit == -1 ? null : TimeSpan.FromTicks(limit),
                    score, objective == -1 ? null : TimeSpan.FromTicks(objective),
                    lives, (source[2] & 1) != 0, (source[2] & 2) != 0,
                    (source[2] & 4) != 0, (source[2] & 8) != 0, source[3],
                    (SpawnPolicy)source[68], (source[72] & 1) != 0,
                    killcamPolicy: KillcamPolicy.Disabled);
                MatchLifecycle.ValidateRules(rules);
            }
            catch (ArgumentException)
            {
                rules = null!;
                return false;
            }
            return true;
        }
    }
}
