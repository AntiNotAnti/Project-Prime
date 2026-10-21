using System;
using System.Buffers.Binary;
using System.IO;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Published84 has the same16/17,192/1299 shapes, but no wall presentation.
// Projection refuses new facts; it must never erase them to fabricate history.
internal static class Protocol84CheckpointFixtures
{
    internal static byte[] ToProtocol84(ReadOnlySpan<byte> current)
    {
        if (current.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(current) != 0x43525050
            || current[6] != 85 || PlayerState.Size != 192 || IntentPacket.TouchCanonicalSize != 1299)
            throw new InvalidOperationException("Unexpected protocol85 checkpoint fixture widths/header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(current[4..]);
        bool custom = version == 17;
        if ((!custom && version != 16) || (current[7] & ~8) != (custom ? 119 : 87))
            throw new InvalidOperationException("Unexpected protocol85 checkpoint fixture features.");
        int at = Protocol83CheckpointFixtures.FirstActorOffset(current);
        for (int slot = 0; slot < 8; slot++)
        {
            if ((current[at + 7 + 173] & 0xf0) != 0)
                throw new InvalidOperationException("Historical84 fixture cannot discard accepted wall cue.");
            at += 7 + 192 + 1;
            if ((current[at + 1298] & 0xf0) != 0)
                throw new InvalidOperationException("Historical84 fixture cannot discard requested wall cue.");
            at += 1299 + 4;
        }
        // Validate every source row and all variable tails before retaining them.
        var checkpoint = new ReplayReplicaCheckpoint(current);
        if (custom) ReplayReplicaState.InspectCustomCheckpointLayout(checkpoint);
        else ReplayReplicaState.InspectCheckpointLayout(checkpoint);
        byte[] historical = current.ToArray(); historical[6] = 84;
        return historical;
    }
}
