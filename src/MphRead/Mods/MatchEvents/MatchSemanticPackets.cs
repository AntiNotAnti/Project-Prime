using MphRead.Mods.Network;
using MphRead.Mods.Network.Generated;
namespace MphRead.Mods.MatchEvents;
[NetPacket(PacketType.MatchSemanticEvent, 35)]
internal readonly partial record struct MatchSemanticEventPacket(
    [NetRange(1, ushort.MaxValue)] ushort MatchId,
    ulong AuthorityEpoch,
    [NetRange(1, uint.MaxValue)] uint EventId, uint Tick, MatchSemanticEventType Type,
    byte ActorSlot, ushort ActorGeneration, ushort ActorLife,
    byte TargetSlot, ushort TargetGeneration, ushort TargetLife,
    [NetRange(-1, 255)] int Weapon, [NetRange(0, 127)] byte Flags,
    [NetRange(-1, int.MaxValue)] int EntityId, int Value)
{
    internal MatchSemanticEvent ToFact() => new(EventId, MatchId, AuthorityEpoch, Tick, Type,
        new(ActorSlot, ActorGeneration, ActorLife), new(TargetSlot, TargetGeneration, TargetLife),
        Weapon, (MatchSemanticEventFlags)Flags, EntityId, Value);
    internal static MatchSemanticEventPacket From(in MatchSemanticEvent fact) => new(fact.MatchId, fact.AuthorityEpoch,
        fact.EventId, fact.Tick, fact.Type, fact.Actor.Slot, fact.Actor.SlotGeneration, fact.Actor.Life,
        fact.Target.Slot, fact.Target.SlotGeneration, fact.Target.Life, fact.Weapon, (byte)fact.Flags, fact.EntityId, fact.Value);
    partial void ValidateSchema(ref bool valid)
    {
        var fact = ToFact();
        valid = AuthorityEpoch != 0 && MatchSemanticEventBus.ValidActor(fact.Actor) && MatchSemanticEventBus.ValidActor(fact.Target)
            && MatchSemanticEventBus.ValidIdentity(fact);
    }
}
[NetPacket(PacketType.MatchAward, 35)]
internal readonly partial record struct MatchAwardPacket(
    [NetRange(1, ushort.MaxValue)] ushort MatchId,
    ulong AuthorityEpoch,
    [NetRange(1, uint.MaxValue)] uint AwardId,
    [NetRange(1, uint.MaxValue)] uint SourceEventId, uint Tick,
    [NetRange(0, 7)] byte ActorSlot,
    [NetRange(1, ushort.MaxValue)] ushort ActorGeneration,
    [NetRange(1, ushort.MaxValue)] ushort ActorLife, MatchAwardKind Kind)
{
    partial void ValidateSchema(ref bool valid) => valid = AuthorityEpoch != 0;
}
