using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

// A separate bounded assembly keeps periodic world facts from replacing the
// frozen objective baseline while bootstrap fragments are being retried.
internal sealed class WorldBootstrapObjectives
{
    private const int Header = 6;
    private const int PartBytes = NetConfig.MaxPayloadSize - WorldBootstrapIdentity.Size - 1 - Header;
    private byte[]? _bytes;
    private ulong _received;
    internal ReplayAuthorityWorld? World { get; private set; }
    internal void Reset() { _bytes = null; _received = 0; World = null; }

    internal static byte[][] Packets(WorldBootstrapIdentity identity, ReplayAuthorityWorld world)
    {
        byte[] bytes = world.Encode();
        int count = (bytes.Length + PartBytes - 1) / PartBytes;
        var packets = new byte[count][];
        for (int part = 0; part < count; part++)
        {
            int length = Math.Min(PartBytes, bytes.Length - part * PartBytes);
            byte[] packet = packets[part] = new byte[WorldBootstrapIdentity.Size + 1 + Header + length];
            identity.Write(packet); packet[WorldBootstrapIdentity.Size] = 3;
            var data = packet.AsSpan(WorldBootstrapIdentity.Size + 1);
            BinaryPrimitives.WriteInt32LittleEndian(data, bytes.Length);
            data[4] = (byte)part; data[5] = (byte)count;
            bytes.AsSpan(part * PartBytes, length).CopyTo(data[Header..]);
        }
        return packets;
    }

    internal bool Accept(ReadOnlySpan<byte> data, WorldBootstrapIdentity identity)
    {
        if (data.Length < Header) return false;
        int total = BinaryPrimitives.ReadInt32LittleEndian(data), part = data[4], count = data[5];
        if (total is <= 0 or > ReplayAuthorityWorld.MaximumBytes || count is <= 0 or > 63
            || count != (total + PartBytes - 1) / PartBytes || part >= count
            || data.Length != Header + Math.Min(PartBytes, total - part * PartBytes)) return false;
        if (_bytes == null) _bytes = new byte[total];
        if (_bytes.Length != total) return false;
        var destination = _bytes.AsSpan(part * PartBytes, data.Length - Header);
        ulong bit = 1UL << part;
        if ((_received & bit) != 0) return data[Header..].SequenceEqual(destination);
        data[Header..].CopyTo(destination); _received |= bit;
        if (_received != (1UL << count) - 1) return true;
        try
        {
            var world = ReplayAuthorityWorld.Decode(_bytes);
            if (world.MatchId != identity.Start.MatchId || world.Epoch != identity.Start.AuthorityEpoch
                || world.Tick != identity.AuthorityFrame) return false;
            World = world; return true;
        }
        catch (InvalidDataException) { return false; }
    }
}
