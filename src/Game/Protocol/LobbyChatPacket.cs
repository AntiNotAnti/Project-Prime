using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public enum ChatScope : byte
    {
        Match = 1,
        Lobby = 2,
        Team = 3
    }

    public readonly record struct LobbyChatRequestPacket(uint SessionId, uint Revision, uint RequestId,
        ChatScope Scope, string Text)
    {
        public const int Size = 16 + ChatPacket.MaxTextBytes;
        public void Write(Span<byte> destination)
        {
            if (destination.Length != Size) { throw new ArgumentException("Invalid lobby chat request size.", nameof(destination)); }
            if (!ValidText(Text, ChatPacket.MaxTextBytes))
                throw new ArgumentException("Lobby chat text is not canonical.", nameof(Text));
            destination.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(destination, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], RequestId);
            destination[12] = (byte)Scope;
            NetText.Write(destination[16..], Text);
            if (!Validate(destination)) { throw new ArgumentException("Invalid lobby chat request.", nameof(destination)); }
        }
        public static bool TryRead(ReadOnlySpan<byte> source, out LobbyChatRequestPacket request)
        {
            request = default;
            if (!Validate(source)) { return false; }
            request = new(BinaryPrimitives.ReadUInt32LittleEndian(source), BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[8..]), (ChatScope)source[12], NetText.Read(source[16..]));
            return true;
        }
        private static bool Validate(ReadOnlySpan<byte> source) => source.Length == Size
            && BinaryPrimitives.ReadUInt32LittleEndian(source) != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != 0
            && source[12] is >= (byte)ChatScope.Match and <= (byte)ChatScope.Team
            && (source[13] | source[14] | source[15]) == 0
            && SessionRosterPacket.IsText(source[16..], required: true);

        private static bool ValidText(string? value, int maximum)
        {
            if (value == null || value.Length > maximum || String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }
    }

    [Flags]
    public enum LobbyChatSenderFlags : byte
    {
        None = 0,
        Observer = 1,
        Host = 2,
        Admin = 4
    }

    public readonly record struct LobbyChatPacket(uint SessionId, uint Revision, uint RequestId,
        ulong ConnectionId, ulong SenderIdentity, byte Slot, ChatScope Scope,
        LobbyChatSenderFlags Flags, string Name, string Text)
    {
        public const int Size = 48 + ChatPacket.MaxTextBytes;
        public void Write(Span<byte> destination)
        {
            if (destination.Length != Size) { throw new ArgumentException("Invalid lobby chat size.", nameof(destination)); }
            if (!ValidText(Name, ChatPacket.MaxNameBytes) || !ValidText(Text, ChatPacket.MaxTextBytes))
                throw new ArgumentException("Lobby chat text is not canonical.", nameof(destination));
            destination.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(destination, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], RequestId);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[12..], ConnectionId);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[20..], SenderIdentity);
            destination[28] = Slot;
            destination[29] = (byte)Scope;
            destination[30] = (byte)Flags;
            NetText.Write(destination.Slice(32, ChatPacket.MaxNameBytes), Name);
            NetText.Write(destination[48..], Text);
            if (!Validate(destination)) { throw new ArgumentException("Invalid lobby chat.", nameof(destination)); }
        }
        public static bool TryRead(ReadOnlySpan<byte> source, out LobbyChatPacket packet)
        {
            packet = default;
            if (!Validate(source)) { return false; }
            packet = new(BinaryPrimitives.ReadUInt32LittleEndian(source), BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[8..]), BinaryPrimitives.ReadUInt64LittleEndian(source[12..]),
                BinaryPrimitives.ReadUInt64LittleEndian(source[20..]), source[28], (ChatScope)source[29],
                (LobbyChatSenderFlags)source[30], NetText.Read(source.Slice(32, ChatPacket.MaxNameBytes)),
                NetText.Read(source[48..]));
            return true;
        }
        private static bool Validate(ReadOnlySpan<byte> source)
        {
            if (source.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) == 0
                || BinaryPrimitives.ReadUInt64LittleEndian(source[12..]) == 0
                || BinaryPrimitives.ReadUInt64LittleEndian(source[20..]) == 0
                || source[29] is < (byte)ChatScope.Match or > (byte)ChatScope.Team
                || (source[30] & ~(byte)(LobbyChatSenderFlags.Observer | LobbyChatSenderFlags.Host | LobbyChatSenderFlags.Admin)) != 0
                || source[31] != 0 || !SessionRosterPacket.IsText(source.Slice(32, ChatPacket.MaxNameBytes), required: true)
                || !SessionRosterPacket.IsText(source[48..], required: true)) { return false; }
            bool observer = (source[30] & (byte)LobbyChatSenderFlags.Observer) != 0;
            return observer == (source[28] == byte.MaxValue) && (observer || source[28] < LobbySnapshotPacket.MaximumPlayers);
        }

        private static bool ValidText(string? value, int maximum)
        {
            if (value == null || value.Length > maximum || String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }
    }
}
