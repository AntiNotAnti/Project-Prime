using System;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private void AppendLiveImpact(ReadOnlySpan<byte> bytes)
    {
        if (!NetCombatFactPublisher.LiveEnabled || !LiveCombatImpactPacket.TryRead(bytes, out var impact)
            || impact.Fact.MatchId != _matchId || impact.Fact.AuthorityEpoch != _authorityEpoch) return;
        // Only peers present and ready at publication get these facts. No join-time history.
        foreach (var peer in _peers)
            if (peer.AdmissionReady && peer.MatchReady) peer.LiveImpacts.Add(impact, NetSession.NetFrame);
    }
    private void PumpLiveImpacts()
    {
        foreach (var peer in _peers)
        {
            if (!NetCombatFactPublisher.LiveEnabled || !peer.AdmissionReady || !peer.MatchReady)
            { peer.LiveImpacts.Reset(); continue; }
            peer.LiveImpacts.Pump(NetSession.NetFrame, _matchId, _authorityEpoch,
                peer.LiveImpactSender ??= bytes => _transport?.Send(peer.EndPoint, PacketType.LiveCombatImpact, bytes));
        }
    }
}
