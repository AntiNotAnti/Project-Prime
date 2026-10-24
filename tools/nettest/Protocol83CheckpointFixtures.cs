using System;
using System.Buffers.Binary;
using System.IO;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Test-only projection into the published protocol83 native14/custom15 rows.
// Frozen literal widths prevent the current writer from defining this oracle.
internal static class Protocol83CheckpointFixtures
{
    internal const int PublishedAuxiliaryOffset = 114;

    internal static int FirstActorOffset(ReadOnlySpan<byte> current)
    {
        using var input = new MemoryStream(current.ToArray(), writable: false);
        using var reader = new BinaryReader(input);
        input.Position = 8 + 5 * sizeof(uint) + 2 * sizeof(long);
        reader.ReadByte(); // hasSnapshot
        if (reader.ReadBoolean()) reader.ReadUInt32(); // optional roster revision
        if (reader.ReadBoolean()) input.Position += MatchStatePacket.Size;
        if (reader.ReadBoolean()) input.Position += SessionStatePacket.Size;
        input.Position += RosterPacket.Size;
        return checked((int)input.Position);
    }

    internal static byte[] ToProtocol83(ReadOnlySpan<byte> current)
    {
        if (current.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(current) != 0x43525050
            || current[6] is not (84 or 85 or 86) || PlayerState.Size != 192 || IntentPacket.TouchCanonicalSize != 1299
            || PublishedAuxiliaryOffset != 54 + DamageEvent.Size * PlayerState.DamageHistory)
            throw new InvalidOperationException("Unexpected protocol84/85/86 checkpoint fixture widths/header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(current[4..]);
        bool custom = version == 17;
        byte expectedFeatures = (byte)(custom ? 119 : 87);
        if ((!custom && version != 16) || (current[7] & ~8) != expectedFeatures)
            throw new InvalidOperationException("Unexpected protocol84/85/86 checkpoint fixture feature contract.");

        // Independently verify that the public writer still places its phase in
        // the frozen auxiliary byte, rather than trusting the offset expression.
        byte[] plain = new byte[192], phased = new byte[192];
        new PlayerState().Write(plain);
        new PlayerState { MantleVisualPhase = 15 }.Write(phased);
        if (phased[PublishedAuxiliaryOffset] != (byte)(plain[PublishedAuxiliaryOffset] | 0xf0))
            throw new InvalidOperationException("Published player auxiliary phase offset changed.");
        phased[PublishedAuxiliaryOffset] = plain[PublishedAuxiliaryOffset];
        if (!phased.AsSpan().SequenceEqual(plain))
            throw new InvalidOperationException("Player phase changed bytes outside the published auxiliary nibble.");

        int at = FirstActorOffset(current);
        using var output = new MemoryStream();
        output.Write(current[..at]);
        for (int slot = 0; slot < 8; slot++)
        {
            if ((current[at + 7 + 173] & 0xf0) != 0)
                throw new InvalidOperationException("Historical fixture cannot discard accepted wall cue.");
            if ((current[at + 7 + PublishedAuxiliaryOffset] & 0xf0) != 0)
                throw new InvalidOperationException("Historical fixture cannot discard accepted mantle phase.");
            output.Write(current.Slice(at, 7 + 192)); at += 7 + 192;
            output.WriteByte(current[at++]); // hasIntent
            if (current[at + 1298] != 0 || IntentPacket.Read(current.Slice(at, 1299)).MantleVisualPhase != 0)
                throw new InvalidOperationException("Historical fixture cannot discard requested mantle phase.");
            output.Write(current.Slice(at, 1298)); at += 1299;
            output.Write(current.Slice(at, 4)); at += 4; // received frame
        }
        output.Write(current[at..]); // time/world, touch, pickup, grant, timed and custom tails
        byte[] historical = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(historical.AsSpan(4), custom ? (ushort)15 : (ushort)14);
        historical[6] = 83;
        historical[7] &= 0xbf; // remove only phase feature64; retain grapple16 and custom32
        if (historical.Length != current.Length - 8)
            throw new InvalidOperationException("Historical projection must remove exactly one phase byte per slot.");
        return historical;
    }
}
