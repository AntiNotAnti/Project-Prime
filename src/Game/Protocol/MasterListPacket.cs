using System;

namespace MphRead.Mods.Network
{
    /// <summary>Checked discovery-only list framing, including passive relay-directory replies.</summary>
    public static class MasterListPacket
    {
        public static bool TryRead(ReadOnlySpan<byte> source, Span<MasterEntryPacket> entries,
            out int count, out int total, out NetWireFamily family, out byte protocol)
        {
            count = total = 0; family = NetWireFamily.Unknown; protocol = 0;
            if (source.Length < 2 || source.Length > NetConfig.MaxPacketSize - 1) { return false; }
            int length = source[0];
            if (length > entries.Length || length > source[1] || (length == 0 && source[1] != 0)) { return false; }
            bool legacy = source.Length == 2 + length * MasterEntryPacket.LegacySize;
            int offset = legacy ? 2 : 4;
            int size = legacy ? MasterEntryPacket.LegacySize : MasterEntryPacket.Size;
            if (!legacy && (source.Length != offset + length * size
                || source[2] is < (byte)NetWireFamily.LegacyRelay or > (byte)NetWireFamily.Authoritative
                || source[3] == 0)) { return false; }
            // Check the full packet before publishing any entries to the caller.
            for (int i = 0; i < length; i++)
            {
                if (!MasterEntryPacket.TryRead(source.Slice(offset + i * size, size), out _)) { return false; }
            }
            for (int i = 0; i < length; i++)
            {
                MasterEntryPacket.TryRead(source.Slice(offset + i * size, size), out entries[i]);
            }
            count = length; total = source[1];
            family = legacy ? NetWireFamily.LegacyRelay : (NetWireFamily)source[2];
            protocol = legacy ? (byte)0 : source[3];
            return true;
        }
    }
}
