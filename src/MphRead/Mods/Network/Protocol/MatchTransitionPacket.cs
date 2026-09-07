using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct MatchTransitionPacket(uint MatchId, uint ServerTick, GameMode Mode, string Room)
    {
        public const int Size = 9 + MatchStatePacket.MaxNameBytes;
        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], ServerTick);
            destination[8] = (byte)Mode;
            NetText.Write(destination.Slice(9, MatchStatePacket.MaxNameBytes), Room);
        }
        public static bool TryRead(ReadOnlySpan<byte> source, out MatchTransitionPacket value)
        {
            value = default;
            if (source.Length != Size || source[8] < (byte)GameMode.Battle
                || source[8] > (byte)GameMode.PrimeHunter) { return false; }
            foreach (byte character in source[9..])
            {
                if (character != 0 && (character < 32 || character > 126)) { return false; }
            }
            string room = NetText.Read(source[9..]);
            if (room.Length == 0) { return false; }
            value = new(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), (GameMode)source[8], room);
            return true;
        }
    }

    public readonly record struct NetApplicationEvent(uint MatchId, ReliableEventType Type, ReadOnlyMemory<byte> Payload);
    public delegate bool NetWorldPacketValidator(ReadOnlySpan<byte> payload, uint matchId);
    public delegate void NetWorldPacketHandler(ReadOnlySpan<byte> payload);
}
