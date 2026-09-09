using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Protocol 9 projection of one normalized match-semantic fact. The payload is
/// bounded and numeric; low-level combat and world packets remain unchanged.
/// </summary>
[NetPacket(NetMessageType.Event, protocol: 9)]
public readonly partial record struct MatchSemanticEventPacket(
    [property: NetUnsignedRange(1, uint.MaxValue)] uint EventId,
    uint ServerTick,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint MatchId,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint PhaseRevision,
    MatchEventKind Kind,
    [property: NetUnsignedRange(0, byte.MaxValue)] byte SubjectSlot,
    ulong SubjectConnectionId,
    uint SubjectLife,
    [property: NetUnsignedRange(0, byte.MaxValue)] byte TargetSlot,
    ulong TargetConnectionId,
    uint TargetLife,
    uint EntityId,
    [property: NetUnsignedRange(0, byte.MaxValue)] byte Team,
    uint Value,
    [property: NetUnsignedRange(0, 127)] ushort Flags);

public static class MatchSemanticEventPacketConversion
{
    public static MatchSemanticEventPacket FromEvent(in MatchEvent value)
    {
        if (!value.IsValid || value.Id == 0)
            throw new ArgumentException("Invalid normalized match event.", nameof(value));
        MatchSemanticEventPacket packet = new(value.Id, value.Tick, value.MatchId,
            value.PhaseRevision, value.Kind,
            value.Subject.IsNone ? (byte)255 : value.Subject.Slot,
            value.Subject.IsNone ? 0 : value.Subject.ConnectionId,
            value.Subject.IsNone ? 0 : value.Subject.Life,
            value.Target.IsNone ? (byte)255 : value.Target.Slot,
            value.Target.IsNone ? 0 : value.Target.ConnectionId,
            value.Target.IsNone ? 0 : value.Target.Life,
            value.EntityId, value.Team, value.Value, (ushort)value.Flags);
        if (!packet.Validate())
            throw new ArgumentException("Match event does not fit the generated wire schema.", nameof(value));
        return packet;
    }

    public static bool TryToEvent(in MatchSemanticEventPacket packet, out MatchEvent value)
    {
        value = default;
        if (!packet.Validate()
            || !TryActor(packet.SubjectSlot, packet.SubjectConnectionId, packet.SubjectLife, out CombatActor subject)
            || !TryActor(packet.TargetSlot, packet.TargetConnectionId, packet.TargetLife, out CombatActor target))
            return false;
        MatchEvent candidate = new(packet.EventId, packet.ServerTick, packet.MatchId,
            packet.PhaseRevision, packet.Kind, subject, target, packet.EntityId,
            packet.Team, packet.Value, (MatchEventFlags)packet.Flags);
        if (!candidate.IsValid) return false;
        value = candidate;
        return true;
    }

    private static bool TryActor(byte slot, ulong connectionId, uint life, out CombatActor actor)
    {
        if (slot == 255)
        {
            actor = CombatActor.None;
            return connectionId == 0 && life == 0;
        }
        actor = new(slot, connectionId, life);
        return slot < 8 && actor.IsValid;
    }
}
