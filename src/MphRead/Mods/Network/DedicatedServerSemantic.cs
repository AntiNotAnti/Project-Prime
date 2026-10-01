using System;
using MphRead.Mods.MatchEvents;
namespace MphRead.Mods.Network;
public sealed partial class DedicatedServer
{
    private ushort _semanticMatch;
    private ulong _semanticEpoch;
    private uint _semanticEventFrontier, _semanticAwardFrontier;
    private byte[] SemanticBaseline()
    {
        var baseline = new MatchSemanticEventPacket(_semanticMatch, _semanticEpoch, _semanticEventFrontier, 0,
            MatchSemanticEventType.MatchStarted, 255, 0, 0, 255, 0, 0, -1, 0, -1, 0, true, _semanticAwardFrontier);
        byte[] bytes = new byte[MatchSemanticEventPacket.Size]; baseline.Write(bytes);
        return bytes.AsSpan(1).ToArray();
    }
    private void AppendSemanticDelivery(PacketType type, ReadOnlySpan<byte> payload)
    {
        Span<byte> bytes = stackalloc byte[MatchSemanticEventPacket.Size]; bytes[0] = (byte)type; payload.CopyTo(bytes[1..]);
        if (type == PacketType.MatchSemanticEvent)
        {
            if (!MatchSemanticEventPacket.TryRead(bytes, out var fact) || fact.IsBaseline) return;
            if (_semanticMatch != fact.MatchId || _semanticEpoch != fact.AuthorityEpoch)
            {
                _semanticMatch = fact.MatchId; _semanticEpoch = fact.AuthorityEpoch;
                _semanticEventFrontier = _semanticAwardFrontier = 0;
                // Already-ready peers follow the new epoch from its first fact.
                foreach (var peer in _peers)
                {
                    peer.SemanticCursor = peer.AdmissionReady && peer.MatchReady ? _semanticDelivery.Join() : null;
                    peer.SemanticBaseline = peer.AdmissionReady && peer.MatchReady ? SemanticBaseline() : null;
                }
            }
            _semanticEventFrontier = fact.EventId;
        }
        else
        {
            if (!MatchAwardPacket.TryRead(bytes[..MatchAwardPacket.Size], out var award)
                || award.MatchId != _semanticMatch || award.AuthorityEpoch != _semanticEpoch) return;
            _semanticAwardFrontier = award.AwardId;
        }
        _semanticDelivery.Append(type, payload);
    }
}
