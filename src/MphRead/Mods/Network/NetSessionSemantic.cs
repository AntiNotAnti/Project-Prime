using System;
using MphRead.Mods.MatchEvents;
namespace MphRead.Mods.Network;
public static partial class NetSession
{
    private static void AcceptSemanticPacket(PacketType type, ReadOnlySpan<byte> payload)
    {
        // Only the server->client dispatcher calls this, after transport peer validation.
        // Old generations/lives are retained as historical identities, never resolved to a current occupant.
        int size = type == PacketType.MatchSemanticEvent ? MatchSemanticEventPacket.Size : MatchAwardPacket.Size;
        if (payload.Length != size - 1 || CurrentMatchId == 0 || AuthorityEpoch == 0) return;
        Span<byte> packet = stackalloc byte[MatchSemanticEventPacket.Size];
        packet[0] = (byte)type; payload.CopyTo(packet[1..]);
        SemanticReceived.Begin(CurrentMatchId, AuthorityEpoch);
        uint tick;
        if (type == PacketType.MatchSemanticEvent)
        {
            if (!MatchSemanticEventPacket.TryRead(packet[..size], out var fact) || !SemanticReceived.Accept(fact)) return;
            tick = fact.Tick;
        }
        else
        {
            if (!MatchAwardPacket.TryRead(packet[..size], out var award) || !SemanticReceived.Accept(award)) return;
            tick = award.Tick;
        }
        ReplayCapture.Recorder.AcceptSemantic(packet[..size], CurrentMatchId, AuthorityEpoch, tick, NetFrame);
    }
}
