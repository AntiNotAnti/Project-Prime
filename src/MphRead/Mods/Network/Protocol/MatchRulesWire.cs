using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>Version 7 reliable match configuration. Durations use exact TimeSpan ticks;
    /// -1 represents an absent limit/goal. All validation precedes rule construction.</summary>
    public static class MatchRulesWire
    {
        public const int Size = 28 + MatchStatePacket.MaxNameBytes;
        public static void Write(Span<byte> destination, MatchRules rules)
        {
            MatchLifecycle.ValidateRules(rules);
            if (rules.RoomKey.Length > MatchStatePacket.MaxNameBytes)
            { throw new ArgumentException("Room key must fit the protocol ASCII field.", nameof(rules)); }
            foreach (char character in rules.RoomKey)
            { if (character is < ' ' or > '~') { throw new ArgumentException("Room key must be ASCII.", nameof(rules)); } }
            destination[0] = (byte)rules.Mode.ToLegacyMode();
            destination[1] = (byte)rules.MaxPlayers;
            destination[2] = (byte)((rules.FriendlyFire ? 1 : 0) | (rules.AffinityWeapons ? 2 : 0)
                | (rules.PlayerRadar ? 4 : 0) | (rules.OctolithReset ? 8 : 0));
            destination[3] = (byte)rules.DamageLevel;
            BinaryPrimitives.WriteInt32LittleEndian(destination[4..], rules.ScoreGoal);
            BinaryPrimitives.WriteInt32LittleEndian(destination[8..], rules.StartingLives);
            BinaryPrimitives.WriteInt64LittleEndian(destination[12..], rules.TimeLimit?.Ticks ?? -1);
            BinaryPrimitives.WriteInt64LittleEndian(destination[20..], rules.ObjectiveTimeGoal?.Ticks ?? -1);
            NetText.Write(destination.Slice(28, MatchStatePacket.MaxNameBytes), rules.RoomKey);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out MatchRules rules)
        {
            rules = null!;
            if (source.Length != Size || source[0] < (byte)GameMode.Battle || source[0] > (byte)GameMode.PrimeHunter
                || source[1] is < 1 or > 8 || source[2] > 15 || source[3] > 2
                || !NetWireIdentity.ValidText(source[28..])) { return false; }
            int score = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
            int lives = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
            long limit = BinaryPrimitives.ReadInt64LittleEndian(source[12..]);
            long objective = BinaryPrimitives.ReadInt64LittleEndian(source[20..]);
            if (score < 0 || lives < 0 || limit < -1 || objective < -1
                || limit > (long)int.MaxValue * TimeSpan.TicksPerSecond / 60) { return false; }
            string room = NetText.Read(source[28..]);
            if (String.IsNullOrWhiteSpace(room)) { return false; }
            rules = new MatchRules(((GameMode)source[0]).ToMatchMode(), room, source[1],
                limit == -1 ? null : TimeSpan.FromTicks(limit), score,
                objective == -1 ? null : TimeSpan.FromTicks(objective), lives,
                (source[2] & 1) != 0, (source[2] & 2) != 0, (source[2] & 4) != 0,
                (source[2] & 8) != 0, source[3]);
            try { MatchLifecycle.ValidateRules(rules); }
            catch (ArgumentOutOfRangeException) { rules = null!; return false; }
            return true;
        }
    }
}
