using System;
using MphRead.Mods.Network;
namespace MphRead.Mods.MatchEvents;
/// <summary>Scene-owned passive recording. Network fanout uses separately retained, budgeted delivery.</summary>
internal sealed class MatchSemanticPublisher
{
    private ushort _match;
    private ulong _epoch;
    private uint _lastEvent, _awardId;
    internal long HistoryGaps { get; private set; }
    internal void Publish(Scene scene)
    {
        if (scene.Services.IsReplica || !NetSession.Active || !NetSession.IsAuthority) return;
        var bus = scene.MatchEvents.Bus;
        if (bus.MatchId == 0) return;
        if (_match != bus.MatchId || _epoch != bus.AuthorityEpoch)
        { _match = bus.MatchId; _epoch = bus.AuthorityEpoch; _lastEvent = _awardId = 0; }
        Span<byte> bytes = stackalloc byte[MatchSemanticEventPacket.Size];
        foreach (var fact in bus.Events)
        {
            if (fact.EventId <= _lastEvent) continue;
            if (fact.EventId != _lastEvent + 1) HistoryGaps++;
            _lastEvent = fact.EventId;
            var packet = MatchSemanticEventPacket.From(fact);
            if (!packet.Validate()) { HistoryGaps++; continue; }
            packet.Write(bytes);
            ReplayCapture.Recorder.AcceptSemantic(bytes, _match, _epoch, fact.Tick, NetSession.NetFrame);
            NetSession.MatchSemanticSink?.Invoke(PacketType.MatchSemanticEvent, bytes[1..]);
            foreach (var award in bus.Awards.Awards)
            {
                if (award.EventId != fact.EventId) continue;
                if (_awardId == uint.MaxValue) { HistoryGaps++; continue; }
                var awardPacket = new MatchAwardPacket(_match, _epoch, ++_awardId, award.EventId, award.Tick,
                    award.Actor.Slot, award.Actor.SlotGeneration, award.Actor.Life, award.Kind);
                awardPacket.Write(bytes);
                ReplayCapture.Recorder.AcceptSemantic(bytes[..MatchAwardPacket.Size], _match, _epoch, award.Tick, NetSession.NetFrame);
                NetSession.MatchSemanticSink?.Invoke(PacketType.MatchAward, bytes.Slice(1, MatchAwardPacket.Size - 1));
            }
        }
    }
}
