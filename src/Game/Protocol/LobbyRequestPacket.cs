using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public enum LobbyRequestType : byte
    {
        SetReady = 1,
        SelectHunter,
        RequestTeam,
        SetMap,
        SetMode,
        SetRule,
        StartMatch,
        ReturnToLobby,
        Rematch,
        SetBotFillEnabled,
        SetBotMinimumParticipants,
        SetBotSkill
    }

    public enum LobbyRuleField : byte
    {
        None,
        MaxPlayers,
        TimeLimitSeconds,
        ScoreGoal,
        ObjectiveTimeSeconds,
        StartingLives,
        FriendlyFire,
        AffinityWeapons,
        PlayerRadar,
        OctolithReset,
        DamageLevel,
        SpawnPolicy,
        OvertimePolicy,
        LateJoinPolicy
    }

    /// <summary>One fixed-width, replay-addressable lobby mutation request.</summary>
    public readonly record struct LobbyRequestPacket(uint SessionId, uint Revision, uint RequestId,
        LobbyRequestType Type, LobbyRuleField Rule, int Value, string Text)
    {
        public const int TextBytes = 40;
        public const int Size = 20 + TextBytes;

        public void Write(Span<byte> destination)
        {
            if (destination.Length != Size) { throw new ArgumentException("Invalid lobby request size.", nameof(destination)); }
            if (Type == LobbyRequestType.SetMap
                ? !ValidText(Text, TextBytes, required: true)
                : !ValidText(Text, TextBytes, required: false) || !String.IsNullOrEmpty(Text))
                throw new ArgumentException("Lobby request text is not canonical.", nameof(Text));
            destination.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(destination, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], RequestId);
            destination[12] = (byte)Type;
            destination[13] = (byte)Rule;
            BinaryPrimitives.WriteInt32LittleEndian(destination[16..], Value);
            NetText.Write(destination[20..], Text);
            if (!Validate(destination)) { throw new ArgumentException("Invalid lobby request.", nameof(destination)); }
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out LobbyRequestPacket request)
        {
            request = default;
            if (!Validate(source)) { return false; }
            request = new LobbyRequestPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
                (LobbyRequestType)source[12], (LobbyRuleField)source[13],
                BinaryPrimitives.ReadInt32LittleEndian(source[16..]), NetText.Read(source[20..]));
            return true;
        }

        private static bool Validate(ReadOnlySpan<byte> source)
        {
            if (source.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) == 0
                || source[12] is < (byte)LobbyRequestType.SetReady or > (byte)LobbyRequestType.SetBotSkill
                || (source[14] | source[15]) != 0 || !SessionRosterPacket.IsText(source[20..], required: false))
            {
                return false;
            }
            var type = (LobbyRequestType)source[12];
            var rule = (LobbyRuleField)source[13];
            int value = BinaryPrimitives.ReadInt32LittleEndian(source[16..]);
            bool hasText = source[20] != 0;
            if (type == LobbyRequestType.SetMap)
            {
                return rule == LobbyRuleField.None && value == 0 && hasText;
            }
            if (hasText) { return false; }
            return type switch
            {
                LobbyRequestType.SetReady => rule == LobbyRuleField.None && value is 0 or 1,
                LobbyRequestType.SelectHunter => rule == LobbyRuleField.None && value is >= 0 and <= (int)Hunter.Random,
                LobbyRequestType.RequestTeam => rule == LobbyRuleField.None && value is >= 0 and < LobbySnapshotPacket.MaximumPlayers,
                LobbyRequestType.SetMode => rule == LobbyRuleField.None && value is >= (int)MatchMode.Battle and <= (int)MatchMode.PrimeHunter,
                LobbyRequestType.SetRule => ValidRule(rule, value),
                LobbyRequestType.SetBotFillEnabled => rule == LobbyRuleField.None && value is 0 or 1,
                LobbyRequestType.SetBotMinimumParticipants => rule == LobbyRuleField.None
                    && value is >= 0 and <= LobbySnapshotPacket.MaximumPlayers,
                LobbyRequestType.SetBotSkill => rule == LobbyRuleField.None && value is >= 0 and <= 2,
                LobbyRequestType.StartMatch => rule == LobbyRuleField.None && value is 0 or 1,
                LobbyRequestType.ReturnToLobby or LobbyRequestType.Rematch
                    => rule == LobbyRuleField.None && value == 0,
                _ => false
            };
        }

        private static bool ValidRule(LobbyRuleField rule, int value) => rule switch
        {
            LobbyRuleField.MaxPlayers => value is >= 1 and <= LobbySnapshotPacket.MaximumPlayers,
            LobbyRuleField.TimeLimitSeconds or LobbyRuleField.ObjectiveTimeSeconds => value is >= 0 and <= 86400,
            LobbyRuleField.ScoreGoal => value is >= 0 and <= 1_000_000,
            LobbyRuleField.StartingLives => value is >= 0 and <= 255,
            LobbyRuleField.FriendlyFire or LobbyRuleField.AffinityWeapons or LobbyRuleField.PlayerRadar
                or LobbyRuleField.OctolithReset => value is 0 or 1,
            LobbyRuleField.DamageLevel => value is >= 0 and <= 2,
            LobbyRuleField.SpawnPolicy => Enum.IsDefined((SpawnPolicy)value),
            LobbyRuleField.OvertimePolicy => Enum.IsDefined((OvertimePolicy)value),
            LobbyRuleField.LateJoinPolicy => Enum.IsDefined((LateJoinPolicy)value),
            _ => false
        };

        private static bool ValidText(string? value, int maximum, bool required)
        {
            if (value == null || value.Length > maximum || required && String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }
    }
}
