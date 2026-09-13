using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    internal static class Protocol7ReplayRoster
    {
        public const int EntrySize = 29;
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
                NetText.Write(target.Slice(11, 16), entry.Name);
                BinaryPrimitives.WriteUInt16LittleEndian(target[27..], entry.PingMs);
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
                    || entry[9] > (byte)Hunter.Guardian || entry[10] >= 8
                    || !IsText(entry.Slice(11, 16), required: true)) { return false; }
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
                entries[i] = new(entry[0], identities[i], (Hunter)entry[9], entry[10], NetText.Read(entry.Slice(11, 16)),
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[27..]));
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

}
