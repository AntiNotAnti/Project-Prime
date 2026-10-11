using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private readonly Queue<ConfirmedImpact> _impacts = new(64);
    private long _impactDrops;
    private void AppendImpact(ConfirmedImpact fact)
    {
        if (!NetConfirmedImpacts.Enabled || !fact.Valid || fact.Key.AuthorityEpoch != _authorityEpoch
            || fact.Key.MatchId != _matchId) return;
        if (_impacts.Count >= 64) { _impactDrops++; return; }
        _impacts.Enqueue(fact);
    }
    private void PumpImpacts()
    {
        if (!NetConfirmedImpacts.Enabled) { _impacts.Clear(); return; }
        Span<byte> bytes = stackalloc byte[ConfirmedImpact.Size];
        for (int work = 0; work < 8 && _impacts.TryDequeue(out var fact); work++)
        {
            if (fact.Key.AuthorityEpoch != _authorityEpoch || fact.Key.MatchId != _matchId
                || unchecked((int)(NetSession.NetFrame - fact.Frame)) > 30) continue;
            fact.Write(bytes);
            foreach (var peer in _peers)
                if (peer.AdmissionReady && peer.MatchReady && _transport?.TrySendImpact(peer.EndPoint, bytes) != true) _impactDrops++;
        }
    }
}
