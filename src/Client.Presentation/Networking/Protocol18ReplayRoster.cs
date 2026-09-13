using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network;

/// <summary>
/// Replay-only decoder for the reliable roster layout recorded by protocols
/// 8 through 18. Never use this compatibility codec on a live socket.
/// </summary>
internal static class Protocol18ReplayRoster
{
    internal const int EntrySize = 30;
    internal const int HeaderSize = 5;
    internal const int MaxSize = HeaderSize + 8 * EntrySize;

    internal static bool TryRead(ReadOnlySpan<byte> source, Span<NetRosterEntry> entries,
        out uint revision, out int count)
    {
        revision = 0;
        count = 0;
        if (source.Length < HeaderSize || source[4] > 8 || source[4] > entries.Length
            || source.Length != HeaderSize + source[4] * EntrySize) return false;
        int occupied = 0;
        Span<ulong> identities = stackalloc ulong[8];
        for (int i = 0; i < source[4]; i++)
        {
            ReadOnlySpan<byte> entry = source.Slice(HeaderSize + i * EntrySize, EntrySize);
            ulong id = BinaryPrimitives.ReadUInt64LittleEndian(entry[1..]);
            if (entry[0] >= 8 || id == 0 || (occupied & (1 << entry[0])) != 0
                || entry[9] > (byte)Hunter.Guardian || entry[10] >= 8 || entry[29] > 1
                || !SessionRosterPacket.IsText(entry.Slice(11, ChatPacket.MaxNameBytes), required: true))
                return false;
            for (int other = 0; other < i; other++)
                if (identities[other] == id) return false;
            identities[i] = id;
            occupied |= 1 << entry[0];
        }
        count = source[4];
        revision = BinaryPrimitives.ReadUInt32LittleEndian(source);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = source.Slice(HeaderSize + i * EntrySize, EntrySize);
            entries[i] = new NetRosterEntry(entry[0], identities[i], (Hunter)entry[9], entry[10],
                NetText.Read(entry.Slice(11, ChatPacket.MaxNameBytes)),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[27..]), entry[29] == 1);
        }
        return true;
    }
}
