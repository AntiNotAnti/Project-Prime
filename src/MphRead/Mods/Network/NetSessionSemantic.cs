using System;
using MphRead.Mods.MatchEvents;
namespace MphRead.Mods.Network;
public static partial class NetSession
{
    private static void AcceptSemanticPacket(PacketType type, ReadOnlySpan<byte> payload)
    {
        // Validate the whole bounded envelope before accepting its first record.
        if (!NetFactBatch.Validate(type, payload)) return;
        int stride = NetFactBatch.Stride(type);
        for (int offset = 0; offset < payload.Length; offset += stride)
            AcceptSingleSemanticPacket(type, payload.Slice(offset, stride));
    }
    private static void AcceptSingleSemanticPacket(PacketType type, ReadOnlySpan<byte> payload)
    {
        // Only the server->client dispatcher calls this, after transport peer validation.
        // Old generations/lives are retained as historical identities, never resolved to a current occupant.
        int size = type == PacketType.MatchSemanticEvent ? MatchSemanticEventPacket.Size : MatchAwardPacket.Size;
        if (payload.Length != size - 1 || CurrentMatchId == 0 || AuthorityEpoch == 0) return;
        Span<byte> packet = stackalloc byte[MatchSemanticEventPacket.Size];
        packet[0] = (byte)type; payload.CopyTo(packet[1..]);
        SemanticReceived.Begin(CurrentMatchId, AuthorityEpoch, requireBaseline: true);
        uint tick;
        if (type == PacketType.MatchSemanticEvent)
        {
            if (!MatchSemanticEventPacket.TryRead(packet[..size], out var fact) || !SemanticReceived.Accept(fact)) return;
            NetCampaignEvidence.Semantic(type, packet[..size]);
            if (fact.IsBaseline) return;
            tick = fact.Tick;
        }
        else
        {
            if (!MatchAwardPacket.TryRead(packet[..size], out var award) || !SemanticReceived.Accept(award)) return;
            NetCampaignEvidence.Semantic(type, packet[..size]);
            tick = award.Tick;
        }
        ReplayCapture.Recorder.AcceptSemantic(packet[..size], CurrentMatchId, AuthorityEpoch, tick, NetFrame);
    }
}
