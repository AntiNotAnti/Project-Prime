using System.Collections.Generic;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal enum ReplayKillLifeStatus { Unknown, Conflict, FencedMarker, VerifiedSemanticFact, LegacyUnqualified }
internal readonly record struct ReplayKillLifeResult(ReplayKillLifeStatus Status, ReplayKillIdentity Identity, uint FactEventId = 0)
{
    internal bool Known => Status is ReplayKillLifeStatus.FencedMarker or ReplayKillLifeStatus.VerifiedSemanticFact;
}

// Pure attribution over already accepted bounded authority history. A semantic
// global EventId is not the victim's damage EventId; the exact dead life is the join.
internal static class ReplayKillLifeResolver
{
    internal static ReplayKillLifeResult Resolve(ReplayKillIdentity kill, IReadOnlyCollection<MatchSemanticEventPacket> facts)
    {
        if (kill.MatchId == 0 || kill.AuthorityEpoch == 0 || kill.EventId == 0 || kill.KillerSlot >= 8 || kill.VictimSlot >= 8
            || kill.KillerSlot == kill.VictimSlot || kill.KillerGeneration == 0
            || kill.VictimGeneration == 0 || kill.VictimLifeId == 0)
            return new(ReplayKillLifeStatus.Unknown, kill);
        if (kill.KillerLifeId != 0) return new(ReplayKillLifeStatus.FencedMarker, kill);
        MatchSemanticEventPacket? found = null;
        foreach (var fact in facts)
        {
            if (fact.IsBaseline || fact.Type != MatchSemanticEventType.PlayerKilled || fact.MatchId != kill.MatchId
                || fact.AuthorityEpoch != kill.AuthorityEpoch || fact.TargetSlot != kill.VictimSlot
                || fact.TargetGeneration != kill.VictimGeneration || fact.TargetLife != kill.VictimLifeId) continue;
            if (!fact.Validate() || fact.ActorSlot != kill.KillerSlot || fact.ActorGeneration != kill.KillerGeneration
                || fact.ActorLife == 0 || found is { } prior && prior != fact)
                return new(ReplayKillLifeStatus.Conflict, kill);
            found = fact; // repeated copies of the exact same canonical fact are harmless
        }
        return found is { } proof
            ? new(ReplayKillLifeStatus.VerifiedSemanticFact, kill with { KillerLifeId = proof.ActorLife }, proof.EventId)
            : new(ReplayKillLifeStatus.Unknown, kill);
    }
}
