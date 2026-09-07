using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public enum LobbyFeedbackCode : byte
    {
        None,
        StaleSession,
        StaleRevision,
        DuplicateRequest,
        InvalidRequest,
        NotPermitted,
        WrongPhase,
        Capacity,
        Unsupported,
        Conflict,
        RateLimited
    }

    public readonly record struct LobbyFeedbackPacket(uint SessionId, uint Revision, uint RequestId,
        LobbyFeedbackCode Code, string Message)
    {
        public const int MessageBytes = 96;
        public const int Size = 16 + MessageBytes;
        public bool Accepted => Code == LobbyFeedbackCode.None;

        public void Write(Span<byte> destination)
        {
            if (destination.Length != Size) { throw new ArgumentException("Invalid lobby feedback size.", nameof(destination)); }
            if (!ValidText(Message, MessageBytes, required: Code != LobbyFeedbackCode.None))
                throw new ArgumentException("Lobby feedback text is not canonical.", nameof(Message));
            destination.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(destination, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], RequestId);
            destination[12] = (byte)Code;
            NetText.Write(destination[16..], Message);
            if (!Validate(destination)) { throw new ArgumentException("Invalid lobby feedback.", nameof(destination)); }
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out LobbyFeedbackPacket feedback)
        {
            feedback = default;
            if (!Validate(source)) { return false; }
            feedback = new LobbyFeedbackPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
                (LobbyFeedbackCode)source[12], NetText.Read(source[16..]));
            return true;
        }

        private static bool Validate(ReadOnlySpan<byte> source) => source.Length == Size
            && BinaryPrimitives.ReadUInt32LittleEndian(source) != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != 0
            && source[12] <= (byte)LobbyFeedbackCode.RateLimited
            && (source[13] | source[14] | source[15]) == 0
            && SessionRosterPacket.IsText(source[16..], required: source[12] != 0);

        private static bool ValidText(string? value, int maximum, bool required)
        {
            if (value == null || value.Length > maximum || required && String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }
    }
}
