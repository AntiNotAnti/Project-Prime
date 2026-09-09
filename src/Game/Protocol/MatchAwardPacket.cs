using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Protocol 9 generator shape for a semantic award. All actor identity is
/// numeric; optional target is represented by CombatActor.None. Reliable event
/// enum integration remains with the QZ4 protocol pass.
/// </summary>
[NetPacket(NetMessageType.Event, protocol: 9)]
public readonly partial record struct MatchAwardPacket(
    [property: NetUnsignedRange(1, uint.MaxValue)] uint AwardId,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint SourceEventId,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint MatchId,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint PhaseRevision,
    uint ServerTick,
    MatchAwardKind Kind,
    [property: NetUnsignedRange(0, 7)] byte SubjectSlot,
    [property: NetUnsignedRange(1, ulong.MaxValue)] ulong SubjectConnectionId,
    [property: NetUnsignedRange(1, uint.MaxValue)] uint SubjectLife,
    [property: NetUnsignedRange(0, byte.MaxValue)] byte TargetSlot,
    ulong TargetConnectionId,
    uint TargetLife,
    [property: NetUnsignedRange(1, byte.MaxValue)] byte Count,
    ushort Value);

/// <summary>Maps generated wire values to the semantic domain without another codec.</summary>
public static class MatchAwardPacketConversion
{
    public static MatchAwardPacket FromAward(in MatchAward award)
    {
        if (!award.IsValid) throw new ArgumentException("Invalid match award.", nameof(award));
        var packet = new MatchAwardPacket(award.AwardId, award.SourceEventId, award.MatchId,
            award.PhaseRevision, award.Tick, award.Kind, award.Subject.Slot, award.Subject.ConnectionId,
            award.Subject.Life, award.Target.IsNone ? (byte)255 : award.Target.Slot,
            award.Target.IsNone ? 0 : award.Target.ConnectionId,
            award.Target.IsNone ? 0 : award.Target.Life, award.Count, award.Value);
        if (!packet.Validate()) throw new ArgumentException("Award does not fit the generated wire schema.", nameof(award));
        return packet;
    }

    /// <summary>Converts a generated packet after applying the domain's
    /// conditional optional-target identity rules. The generator validates
    /// field ranges; this helper validates the relationship between those
    /// fields before an untrusted packet reaches a presentation queue.</summary>
    public static bool TryToAward(in MatchAwardPacket packet, out MatchAward award)
    {
        award = default;
        if (!packet.Validate() || packet.TargetSlot != 255 && packet.TargetSlot >= 8
            || (packet.TargetSlot == 255
                ? packet.TargetConnectionId != 0 || packet.TargetLife != 0
                : packet.TargetConnectionId == 0 || packet.TargetLife == 0))
            return false;
        var value = new MatchAward(packet.AwardId, packet.SourceEventId, packet.MatchId,
            packet.PhaseRevision, packet.ServerTick, packet.Kind,
            new(packet.SubjectSlot, packet.SubjectConnectionId, packet.SubjectLife),
            packet.TargetSlot == 255 ? CombatActor.None
                : new(packet.TargetSlot, packet.TargetConnectionId, packet.TargetLife), packet.Count, packet.Value);
        if (!value.IsValid) return false;
        award = value;
        return true;
    }

    public static MatchAward ToAward(in MatchAwardPacket packet)
    {
        if (!TryToAward(packet, out MatchAward award))
            throw new ArgumentException("Invalid match award packet.", nameof(packet));
        return award;
    }
}
