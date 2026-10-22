using MphRead.Mods.Network.Telemetry;
namespace MphRead.Mods.MatchEvents;

/// <summary>Canonical authority telemetry, separate from transport/claim diagnostics.
/// Exact identity fields stay integral, including full-width authority epochs.</summary>
internal static class MatchSemanticTelemetry
{
    internal static NetTelemetryEvent Convert(in MatchSemanticEventPacket fact) => new(
        TelemetryEventType.MatchSemantic, fact.Tick, Player: fact.ActorSlot, Victim: fact.TargetSlot,
        Weapon: fact.Weapon < 0 ? byte.MaxValue : (byte)fact.Weapon,
        Generation: fact.ActorGeneration, Life: fact.ActorLife, Id: fact.EventId,
        Result: (int)fact.Type, Flags: fact.Flags,
        MatchId: fact.MatchId, AuthorityEpoch: fact.AuthorityEpoch,
        VictimGeneration: fact.TargetGeneration, VictimLife: fact.TargetLife,
        EntityId: fact.EntityId, Value: fact.Value);
    internal static NetTelemetryEvent Convert(in MatchAwardPacket award) => new(
        TelemetryEventType.MatchAward, award.Tick, Player: award.ActorSlot,
        Generation: award.ActorGeneration, Life: award.ActorLife, Id: award.AwardId,
        Result: (int)award.Kind, MatchId: award.MatchId, AuthorityEpoch: award.AuthorityEpoch,
        SourceEventId: award.SourceEventId);
}
