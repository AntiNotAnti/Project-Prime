using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>Version 8 reliable match configuration. Durations use exact TimeSpan ticks;
    /// -1 represents an absent limit/goal. All validation precedes rule construction.</summary>
    public static class MatchRulesWire
    {
        // Byte 83 is the pre-existing PowerupsEnabled field. Balanced profile
        // metadata and resource radar policy are appended so old fields never
        // change meaning.
        public const int Size = 86;
        public const int LegacyProtocol24Size = 85;
        public const int LegacyProtocol23Size = 84;
        public static void Write(Span<byte> destination, MatchRules rules)
        {
            if (destination.Length != Size) { throw new ArgumentException("Invalid match rules size.", nameof(destination)); }
            MatchLifecycle.ValidateRules(rules);
            if (rules.RoomKey.Length > MatchStatePacket.MaxNameBytes)
            { throw new ArgumentException("Room key must fit the protocol ASCII field.", nameof(rules)); }
            foreach (char character in rules.RoomKey)
            { if (character is < ' ' or > '~') { throw new ArgumentException("Room key must be ASCII.", nameof(rules)); } }
            destination.Clear();
            destination[0] = (byte)rules.Mode.ToLegacyMode();
            destination[1] = (byte)rules.MaxPlayers;
            destination[2] = (byte)((rules.FriendlyFire ? 1 : 0) | (rules.AffinityWeapons ? 2 : 0)
                | (rules.PlayerRadar ? 4 : 0) | (rules.OctolithReset ? 8 : 0)
                | (rules.EnhancedHunters ? 16 : 0));
            destination[3] = (byte)rules.DamageLevel;
            BinaryPrimitives.WriteInt32LittleEndian(destination[4..], rules.ScoreGoal);
            BinaryPrimitives.WriteInt32LittleEndian(destination[8..], rules.StartingLives);
            BinaryPrimitives.WriteInt64LittleEndian(destination[12..], rules.TimeLimit?.Ticks ?? -1);
            BinaryPrimitives.WriteInt64LittleEndian(destination[20..], rules.ObjectiveTimeGoal?.Ticks ?? -1);
            NetText.Write(destination.Slice(28, MatchStatePacket.MaxNameBytes), rules.RoomKey);
            destination[68] = (byte)rules.SpawnPolicy;
            destination[69] = (byte)rules.OvertimePolicy;
            destination[70] = (byte)rules.LateJoinPolicy;
            destination[71] = (byte)rules.RulesetPreset;
            destination[78] = (byte)rules.RankingEligibility;
            destination[79] = (byte)rules.RadarPolicy;
            destination[80] = (byte)rules.TeamBalancePolicy;
            destination[81] = (byte)rules.KillcamPolicy;
            destination[82] = (byte)rules.TeamCount;
            // Zero preserves the historical/default meaning. A set bit opts
            // the match out of major power-ups while retaining health, ammo,
            // ordinary weapons, and objective items.
            destination[83] = rules.PowerupsEnabled ? (byte)0 : (byte)1;
            destination[84] = rules.BalancedMode ? (byte)1 : (byte)0;
            destination[85] = (byte)rules.ResourceRadarPolicy;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[72..],
                (ushort)((rules.CancelSpawnProtectionOnOffensiveAction ? 1 : 0) | (rules.PickupRespawnAnnouncements ? 2 : 0)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination[74..], (ushort)rules.AssistMinimumDamage);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[76..], (ushort)rules.AssistWindowTicks);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out MatchRules rules)
            => TryReadCore(source, hasBalancedProfile: true,
                hasResourceRadarPolicy: true, out rules);

        /// <summary>
        /// Reads the frozen protocol-24 rule payload. Protocol 24 carries the
        /// balanced profile byte but predates resource radar policy.
        /// </summary>
        public static bool TryReadLegacyProtocol24(ReadOnlySpan<byte> source,
            out MatchRules rules)
            => TryReadCore(source, hasBalancedProfile: true,
                hasResourceRadarPolicy: false, out rules);

        /// <summary>
        /// Reads the frozen protocol-23 rule payload. Protocol 23 predates the
        /// appended profile byte and therefore always maps to Classic.
        /// </summary>
        public static bool TryReadLegacyProtocol23(ReadOnlySpan<byte> source,
            out MatchRules rules)
            => TryReadCore(source, hasBalancedProfile: false,
                hasResourceRadarPolicy: false, out rules);

        private static bool TryReadCore(ReadOnlySpan<byte> source,
            bool hasBalancedProfile, bool hasResourceRadarPolicy,
            out MatchRules rules)
        {
            rules = null!;
            int expectedSize = hasResourceRadarPolicy ? Size
                : hasBalancedProfile ? LegacyProtocol24Size : LegacyProtocol23Size;
            if (source.Length != expectedSize || source[0] < (byte)GameMode.Battle || source[0] > (byte)GameMode.PrimeHunter
                || source[1] is < 1 or > 8 || (source[2] & ~0x1F) != 0 || source[3] > 2
                || BinaryPrimitives.ReadUInt16LittleEndian(source[74..]) == 0
                || source[69] > (byte)OvertimePolicy.ModeDefault || source[70] > (byte)LateJoinPolicy.Disabled
                || source[68] > (byte)SpawnPolicy.Duel
                || source[71] > (byte)RulesetPreset.Custom || source[78] > (byte)RankingEligibility.VerifiedServerOnly
                || source[79] > (byte)RadarPolicy.Enabled || source[80] > (byte)TeamBalancePolicy.Locked
                || source[81] > (byte)KillcamPolicy.PostRound
                || source[82] is < 1 or > MatchRules.MaximumTeamCount
                || source[83] > 1
                || hasBalancedProfile && source[84] > 1
                || hasResourceRadarPolicy && source[85] > (byte)ResourceRadarPolicy.AvailableWithRespawn
                || source[71] == (byte)RulesetPreset.Duel && (source[0] != (byte)GameMode.Battle || source[1] != 2)
                || source[79] == (byte)RadarPolicy.Disabled && (source[2] & 4) != 0
                || source[79] == (byte)RadarPolicy.Enabled && (source[2] & 4) == 0
                || BinaryPrimitives.ReadUInt16LittleEndian(source[72..]) > 3
                || !NetWireIdentity.ValidText(source.Slice(28, MatchStatePacket.MaxNameBytes))) { return false; }
            int score = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
            int lives = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
            long limit = BinaryPrimitives.ReadInt64LittleEndian(source[12..]);
            long objective = BinaryPrimitives.ReadInt64LittleEndian(source[20..]);
            if (score < 0 || lives < 0 || limit < -1 || objective < -1
                || limit > (long)int.MaxValue * TimeSpan.TicksPerSecond / 60) { return false; }
            string room = NetText.Read(source.Slice(28, MatchStatePacket.MaxNameBytes));
            if (String.IsNullOrWhiteSpace(room)) { return false; }
            MatchMode mode = ((GameMode)source[0]).ToMatchMode();
            int teamCount = source[82];
            if ((mode.IsTeamMode() ? teamCount < 2 : teamCount != 1)
                || teamCount > 2 && mode is not (MatchMode.TeamBattle or MatchMode.TeamSurvival))
            {
                return false;
            }
            try
            {
                rules = new MatchRules(mode, room, source[1],
                    limit == -1 ? null : TimeSpan.FromTicks(limit), score,
                    objective == -1 ? null : TimeSpan.FromTicks(objective), lives,
                    (source[2] & 1) != 0, (source[2] & 2) != 0, (source[2] & 4) != 0,
                    (source[2] & 8) != 0, source[3], (SpawnPolicy)source[68],
                    (source[72] & 1) != 0, BinaryPrimitives.ReadUInt16LittleEndian(source[74..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(source[76..]), (OvertimePolicy)source[69], (LateJoinPolicy)source[70], (source[72] & 2) != 0,
                    (RulesetPreset)source[71], (RankingEligibility)source[78], (RadarPolicy)source[79], (TeamBalancePolicy)source[80],
                    (KillcamPolicy)source[81], mode.IsTeamMode() ? teamCount : 2,
                    powerupsEnabled: source[83] == 0,
                    enhancedHunters: (source[2] & 16) != 0,
                    balancedMode: hasBalancedProfile && source[84] == 1,
                    resourceRadarPolicy: hasResourceRadarPolicy
                        ? (ResourceRadarPolicy)source[85] : ResourceRadarPolicy.Disabled);
                MatchLifecycle.ValidateRules(rules);
            }
            catch (ArgumentException) { rules = null!; return false; }
            return true;
        }
    }
}
