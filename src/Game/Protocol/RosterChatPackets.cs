using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct NetRosterEntry(byte Slot, ulong ConnectionId, Hunter Hunter, byte Team,
        string Name, ushort PingMs = 0, bool IsBot = false, ushort SkinId = 0,
        ushort ArmorEffectId = 0, ushort DeathEffectId = 0);

    public static class SessionRosterPacket
    {
        public const int EntrySize = 36;
        public const int HeaderSize = 5;
        public const int MaxSize = HeaderSize + 8 * EntrySize;

        public static int Write(Span<byte> destination, uint revision, ReadOnlySpan<NetRosterEntry> entries)
        {
            if (entries.Length > 8) { throw new ArgumentOutOfRangeException(nameof(entries)); }
            BinaryPrimitives.WriteUInt32LittleEndian(destination, revision);
            destination[4] = (byte)entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                Span<byte> target = destination.Slice(HeaderSize + i * EntrySize, EntrySize);
                NetRosterEntry entry = entries[i];
                target[0] = entry.Slot;
                BinaryPrimitives.WriteUInt64LittleEndian(target[1..], entry.ConnectionId);
                target[9] = (byte)entry.Hunter;
                target[10] = entry.Team;
                NetText.Write(target.Slice(11, ChatPacket.MaxNameBytes), entry.Name);
                BinaryPrimitives.WriteUInt16LittleEndian(target[27..], entry.PingMs);
                target[29] = entry.IsBot ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt16LittleEndian(target[30..], entry.SkinId);
                BinaryPrimitives.WriteUInt16LittleEndian(target[32..], entry.ArmorEffectId);
                BinaryPrimitives.WriteUInt16LittleEndian(target[34..], entry.DeathEffectId);
            }
            return HeaderSize + entries.Length * EntrySize;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, Span<NetRosterEntry> entries,
            out uint revision, out int count)
        {
            revision = 0;
            count = 0;
            if (source.Length < HeaderSize || source[4] > 8 || source[4] > entries.Length
                || source.Length != HeaderSize + source[4] * EntrySize) { return false; }
            int occupied = 0;
            Span<ulong> identities = stackalloc ulong[8];
            for (int i = 0; i < source[4]; i++)
            {
                ReadOnlySpan<byte> entry = source.Slice(HeaderSize + i * EntrySize, EntrySize);
                ulong id = BinaryPrimitives.ReadUInt64LittleEndian(entry[1..]);
                if (entry[0] >= 8 || id == 0 || (occupied & (1 << entry[0])) != 0
                    || !PlayableHunterCatalog.IsPlayable((Hunter)entry[9])
                    || entry[10] >= 8 || entry[29] > 1
                    || !IsText(entry.Slice(11, ChatPacket.MaxNameBytes), required: true)) { return false; }
                for (int other = 0; other < i; other++)
                {
                    if (identities[other] == id) { return false; }
                }
                identities[i] = id;
                occupied |= 1 << entry[0];
            }
            // No partial output on malformed late entries.
            count = source[4];
            revision = BinaryPrimitives.ReadUInt32LittleEndian(source);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = source.Slice(HeaderSize + i * EntrySize, EntrySize);
                entries[i] = new(entry[0], identities[i], (Hunter)entry[9], entry[10], NetText.Read(entry.Slice(11, ChatPacket.MaxNameBytes)),
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[27..]), entry[29] == 1,
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[34..]));
            }
            return true;
        }

        internal static bool IsText(ReadOnlySpan<byte> text, bool required)
        {
            if (required && text[0] == 0) { return false; }
            bool ended = false;
            foreach (byte value in text)
            {
                if (value == 0) { ended = true; }
                else if (ended || value < 32 || value > 126) { return false; }
            }
            return true;
        }
    }

    public static class SessionChatRequest
    {
        public const int Size = ChatPacket.MaxTextBytes;
        public static void Write(Span<byte> destination, string text) => NetText.Write(destination[..Size], text);
        public static bool TryRead(ReadOnlySpan<byte> source, out string text)
        {
            text = String.Empty;
            if (source.Length != Size || !SessionRosterPacket.IsText(source, required: true)) { return false; }
            text = NetText.Read(source);
            return true;
        }
    }

    public readonly record struct SessionChatPacket(ulong ConnectionId, byte Slot, string Name, string Text)
    {
        public const int Size = 8 + 1 + ChatPacket.MaxNameBytes + ChatPacket.MaxTextBytes;
        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, ConnectionId);
            destination[8] = Slot;
            NetText.Write(destination.Slice(9, ChatPacket.MaxNameBytes), Name);
            SessionChatRequest.Write(destination[(9 + ChatPacket.MaxNameBytes)..], Text);
        }
        public static bool TryRead(ReadOnlySpan<byte> source, out SessionChatPacket packet)
        {
            packet = default;
            if (source.Length != Size || source[8] >= 8 || BinaryPrimitives.ReadUInt64LittleEndian(source) == 0
                || !SessionRosterPacket.IsText(source.Slice(9, ChatPacket.MaxNameBytes), required: true)
                || !SessionChatRequest.TryRead(source[(9 + ChatPacket.MaxNameBytes)..], out string text)) { return false; }
            packet = new(BinaryPrimitives.ReadUInt64LittleEndian(source), source[8],
                NetText.Read(source.Slice(9, ChatPacket.MaxNameBytes)), text);
            return true;
        }
    }
}
