using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct MatchTransitionPacket(uint MatchId, uint ServerTick, MatchRules Rules)
    {
        public GameMode Mode => Rules?.Mode.ToLegacyMode() ?? GameMode.None;
        public string Room => Rules?.RoomKey ?? "";
        public const int Size = 8 + MatchRulesWire.Size;
        public MatchTransitionPacket(uint matchId, uint tick, GameMode mode, string room)
            : this(matchId, tick, MatchRules.CreateDefault(mode.ToMatchMode(), room)) { }
        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], ServerTick);
            MatchRulesWire.Write(destination.Slice(8, MatchRulesWire.Size), Rules);
        }
        public static bool TryRead(ReadOnlySpan<byte> source, out MatchTransitionPacket value)
        {
            value = default;
            if (source.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(source) == 0
                || !MatchRulesWire.TryRead(source[8..], out MatchRules rules)) { return false; }
            value = new(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), rules);
            return true;
        }
    }

    public readonly record struct NetApplicationEvent(uint MatchId, ReliableEventType Type, ReadOnlyMemory<byte> Payload);
    public delegate bool NetWorldPacketValidator(ReadOnlySpan<byte> payload, uint matchId);
    public delegate void NetWorldPacketHandler(ReadOnlySpan<byte> payload);
}
