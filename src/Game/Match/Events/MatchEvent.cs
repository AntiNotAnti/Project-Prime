using System;
using MphRead.Mods.Network;

namespace MphRead;

[Flags]
public enum MatchEventFlags : ushort
{
    None = 0,
    Suicide = 1,
    TeamKill = 2,
    EnvironmentKill = 4,
    ObjectiveCarrier = 8,
    DefendingObjective = 16,
    PrimeTarget = 32,
    Bot = 64,
}

/// <summary>
/// A small, immutable semantic fact. Actor fields carry slot, connection, and
/// life, so a reused slot can never inherit a former occupant's meaning.
/// Id may be zero before the owning dispatcher assigns it.
/// </summary>
public readonly record struct MatchEvent(uint Id, uint Tick, uint MatchId, uint PhaseRevision,
    MatchEventKind Kind, CombatActor Subject, CombatActor Target, uint EntityId = 0,
    byte Team = 255, uint Value = 0, MatchEventFlags Flags = MatchEventFlags.None)
{
    public bool IsValid
    {
        get
        {
            if (MatchId == 0 || PhaseRevision == 0 || Kind is < MatchEventKind.MatchStarted or > MatchEventKind.MatchEnded
                || (Team >= 8 && Team != 255) || (Flags & ~(MatchEventFlags)127) != 0)
            {
                return false;
            }

            return Kind switch
            {
                MatchEventKind.MatchStarted or MatchEventKind.CountdownStarted
                    or MatchEventKind.OvertimeStarted or MatchEventKind.MatchPointReached
                    or MatchEventKind.MatchEnded
                    => Subject.IsNone && Target.IsNone && EntityId == 0,
                MatchEventKind.PlayerKilled
                    => Target.IsValid && (Subject.IsValid || Subject.IsNone)
                        // A self-projectile from an earlier life is still a
                        // suicide attribution when slot and connection match;
                        // life remains part of the fact and must not be erased.
                        && ((Flags & MatchEventFlags.Suicide) == 0
                            || Subject.IsValid && Subject.Slot == Target.Slot
                                && Subject.ConnectionId == Target.ConnectionId)
                        && ((Flags & MatchEventFlags.TeamKill) == 0 || Subject.IsValid),
                MatchEventKind.PlayerAssisted
                    => Subject.IsValid && Target.IsValid && Subject != Target,
                MatchEventKind.PlayerSpawned
                    => Subject.IsValid && Target.IsNone,
                MatchEventKind.ObjectivePickedUp
                    => Subject.IsValid && EntityId != 0,
                MatchEventKind.ObjectiveDropped
                    => EntityId != 0 && (Subject.IsValid || Subject.IsNone),
                MatchEventKind.ObjectiveCaptured or MatchEventKind.ObjectiveDefended
                    => Subject.IsValid && EntityId != 0,
                MatchEventKind.PrimeChanged
                    => Subject.IsValid && Target.IsNone && EntityId == 0,
                MatchEventKind.NodeCaptured
                    => Subject.IsValid && EntityId != 0 && Team < 8,
                _ => false
            };
        }
    }

    public bool IsRoundBoundary => Kind is MatchEventKind.MatchStarted or MatchEventKind.CountdownStarted;
}
