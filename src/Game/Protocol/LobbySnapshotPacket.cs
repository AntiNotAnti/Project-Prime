using System;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace MphRead.Mods.Network
{
    public enum LobbyWireIdentityKind : byte
    {
        Guest,
        Registered,
        Bot
    }

    [Flags]
    public enum LobbyMemberFlags : byte
    {
        None = 0,
        Ready = 1,
        Observer = 2,
        Host = 4,
        Admin = 8,
        Loading = 16,
        DisconnectedGrace = 32
    }

    public readonly record struct LobbySnapshotMember(byte Slot, ulong ConnectionId, string DisplayName,
        Hunter Hunter, byte Team, LobbyWireIdentityKind Identity, LobbyMemberFlags Flags,
        ushort PingMs = 0, byte StarTier = 0)
    {
        public bool Ready => (Flags & LobbyMemberFlags.Ready) != 0;
        public bool Observer => (Flags & LobbyMemberFlags.Observer) != 0;
        public bool Host => (Flags & LobbyMemberFlags.Host) != 0;
        public bool Admin => (Flags & LobbyMemberFlags.Admin) != 0;
        public bool Loading => (Flags & LobbyMemberFlags.Loading) != 0;
        public bool DisconnectedGrace => (Flags & LobbyMemberFlags.DisconnectedGrace) != 0;
        public bool Bot => Identity == LobbyWireIdentityKind.Bot;
        public bool Registered => Identity == LobbyWireIdentityKind.Registered;
    }

    /// <summary>A complete protocol-8 lobby projection. Account UUIDs remain on the
    /// server; the wire identifies registered, guest and bot rows explicitly.</summary>
    public sealed record LobbySnapshotPacket(uint SessionId, uint Revision, LobbyPhase Phase,
        LobbyPolicyKind Policy, bool ReadyRequired, byte MinimumPlayers, bool HostMayForceStart,
        LobbyPermissions Permissions, bool BotFillEnabled, byte BotMinimumParticipants, byte BotSkill,
        uint LastMatchId, MatchRules Rules,
        ImmutableArray<LobbySnapshotMember> Members)
    {
        public const int MaximumPlayers = 8;
        public const int MaximumObservers = 16;
        public const int PolicyOffset = 16 + MatchRulesWire.Size;
        public const int HeaderSize = PolicyOffset + 8;
        public const int MemberSize = 32;
        public const int MaximumSize = HeaderSize + (MaximumPlayers + MaximumObservers) * MemberSize;

        public int Write(Span<byte> destination)
        {
            int players = 0;
            foreach (LobbySnapshotMember member in Members)
            {
                if (!member.Observer) { players++; }
            }
            int observers = Members.Length - players;
            int length = HeaderSize + Members.Length * MemberSize;
            if (destination.Length < length || players > MaximumPlayers || observers > MaximumObservers)
            {
                throw new ArgumentException("Invalid lobby snapshot capacity.", nameof(destination));
            }
            Span<byte> packet = destination[..length];
            packet.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(packet, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[4..], Revision);
            packet[8] = (byte)Phase;
            packet[9] = (byte)Policy;
            packet[10] = (byte)players;
            packet[11] = (byte)observers;
            BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], LastMatchId);
            MatchRulesWire.Write(packet.Slice(16, MatchRulesWire.Size), Rules);
            packet[PolicyOffset] = (byte)((ReadyRequired ? 1 : 0) | (HostMayForceStart ? 2 : 0));
            packet[PolicyOffset + 1] = MinimumPlayers;
            BinaryPrimitives.WriteUInt16LittleEndian(packet[(PolicyOffset + 2)..], (ushort)Permissions);
            packet[PolicyOffset + 4] = BotFillEnabled ? (byte)1 : (byte)0;
            packet[PolicyOffset + 5] = BotMinimumParticipants;
            packet[PolicyOffset + 6] = BotSkill;
            int playerIndex = 0;
            int observerIndex = players;
            foreach (LobbySnapshotMember member in Members)
            {
                int index = member.Observer ? observerIndex++ : playerIndex++;
                WriteMember(packet.Slice(HeaderSize + index * MemberSize, MemberSize), member);
            }
            if (!Validate(packet)) { throw new ArgumentException("Invalid lobby snapshot.", nameof(destination)); }
            return length;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out LobbySnapshotPacket? snapshot)
        {
            snapshot = null;
            if (!Validate(source) || !MatchRulesWire.TryRead(source.Slice(16, MatchRulesWire.Size), out MatchRules rules))
            {
                return false;
            }
            int count = source[10] + source[11];
            var members = ImmutableArray.CreateBuilder<LobbySnapshotMember>(count);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> row = source.Slice(HeaderSize + i * MemberSize, MemberSize);
                members.Add(new LobbySnapshotMember(row[0], BinaryPrimitives.ReadUInt64LittleEndian(row[8..]),
                    NetText.Read(row[16..]), (Hunter)row[1], row[2], (LobbyWireIdentityKind)row[4],
                    (LobbyMemberFlags)row[3], BinaryPrimitives.ReadUInt16LittleEndian(row[6..]), row[5]));
            }
            snapshot = new LobbySnapshotPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), (LobbyPhase)source[8],
                (LobbyPolicyKind)source[9], (source[PolicyOffset] & 1) != 0,
                source[PolicyOffset + 1], (source[PolicyOffset] & 2) != 0,
                (LobbyPermissions)BinaryPrimitives.ReadUInt16LittleEndian(source[(PolicyOffset + 2)..]),
                source[PolicyOffset + 4] != 0, source[PolicyOffset + 5], source[PolicyOffset + 6],
                BinaryPrimitives.ReadUInt32LittleEndian(source[12..]), rules, members.MoveToImmutable());
            return true;
        }

        private static void WriteMember(Span<byte> row, LobbySnapshotMember member)
        {
            if (!ValidText(member.DisplayName, ChatPacket.MaxNameBytes, required: true))
                throw new ArgumentException("Lobby names must be bounded printable ASCII.", nameof(member));
            row[0] = member.Slot;
            row[1] = (byte)member.Hunter;
            row[2] = member.Team;
            row[3] = (byte)member.Flags;
            row[4] = (byte)member.Identity;
            row[5] = member.StarTier;
            BinaryPrimitives.WriteUInt16LittleEndian(row[6..], member.PingMs);
            BinaryPrimitives.WriteUInt64LittleEndian(row[8..], member.ConnectionId);
            NetText.Write(row[16..], member.DisplayName);
        }

        private static bool ValidText(string? value, int maximum, bool required)
        {
            if (value == null || value.Length > maximum || required && String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }

        private static bool Validate(ReadOnlySpan<byte> source)
        {
            if (source.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) == 0
                || source[8] > (byte)LobbyPhase.Locked || source[9] > (byte)LobbyPolicyKind.PersistentLobby
                || source[10] > MaximumPlayers || source[11] > MaximumObservers
                || source.Length != HeaderSize + (source[10] + source[11]) * MemberSize
                || !MatchRulesWire.TryRead(source.Slice(16, MatchRulesWire.Size), out _)
                || source[PolicyOffset] > 3
                || source[PolicyOffset + 1] is < 1 or > MaximumPlayers
                || (BinaryPrimitives.ReadUInt16LittleEndian(source[(PolicyOffset + 2)..])
                    & ~(ushort)LobbyPermissions.Admin) != 0
                || source[PolicyOffset + 4] > 1
                || source[PolicyOffset + 5] > MaximumPlayers
                || (source[PolicyOffset + 4] != 0) != (source[PolicyOffset + 5] != 0)
                || source[PolicyOffset + 6] > 2
                || source[PolicyOffset + 7] != 0)
            {
                return false;
            }
            int count = source[10] + source[11];
            int occupiedSlots = 0;
            int hosts = 0;
            Span<ulong> connections = stackalloc ulong[MaximumPlayers + MaximumObservers];
            int connectionCount = 0;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> row = source.Slice(HeaderSize + i * MemberSize, MemberSize);
                bool observer = (row[3] & (byte)LobbyMemberFlags.Observer) != 0;
                bool ready = (row[3] & (byte)LobbyMemberFlags.Ready) != 0;
                bool host = (row[3] & (byte)LobbyMemberFlags.Host) != 0;
                bool loading = (row[3] & (byte)LobbyMemberFlags.Loading) != 0;
                bool disconnectedGrace = (row[3] & (byte)LobbyMemberFlags.DisconnectedGrace) != 0;
                bool bot = row[4] == (byte)LobbyWireIdentityKind.Bot;
                if (row[1] > (byte)Hunter.Guardian || (observer ? row[2] != byte.MaxValue : row[2] >= MaximumPlayers)
                    || (row[3] & ~(byte)(LobbyMemberFlags.Ready | LobbyMemberFlags.Observer | LobbyMemberFlags.Host
                        | LobbyMemberFlags.Admin | LobbyMemberFlags.Loading | LobbyMemberFlags.DisconnectedGrace)) != 0
                    || row[4] > (byte)LobbyWireIdentityKind.Bot
                    || !SessionRosterPacket.IsText(row[16..], required: true)
                    || observer != (i >= source[10]) || observer != (row[0] == byte.MaxValue)
                    || BinaryPrimitives.ReadUInt64LittleEndian(row[8..]) == 0
                    || observer && (ready || host || disconnectedGrace || bot)
                    || bot && (!ready || host || (row[3] & (byte)LobbyMemberFlags.Admin) != 0
                        || loading || disconnectedGrace)
                    || disconnectedGrace && (ready || host || loading))
                {
                    return false;
                }
                if (!observer)
                {
                    if (row[0] >= MaximumPlayers || (occupiedSlots & (1 << row[0])) != 0) { return false; }
                    occupiedSlots |= 1 << row[0];
                }
                ulong connection = BinaryPrimitives.ReadUInt64LittleEndian(row[8..]);
                if (connection != 0)
                {
                    for (int other = 0; other < connectionCount; other++)
                    {
                        if (connections[other] == connection) { return false; }
                    }
                    connections[connectionCount++] = connection;
                }
                if ((row[3] & (byte)LobbyMemberFlags.Host) != 0 && ++hosts > 1) { return false; }
            }
            return true;
        }
    }
}
