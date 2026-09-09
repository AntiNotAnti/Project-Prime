namespace MphRead;

/// <summary>Authoritative semantic facts emitted by one match owner.</summary>
public enum MatchEventKind : byte
{
    MatchStarted = 1,
    CountdownStarted,
    PlayerKilled,
    PlayerAssisted,
    PlayerSpawned,
    ObjectivePickedUp,
    ObjectiveDropped,
    ObjectiveCaptured,
    ObjectiveDefended,
    PrimeChanged,
    NodeCaptured,
    OvertimeStarted,
    MatchPointReached,
    MatchEnded
}
