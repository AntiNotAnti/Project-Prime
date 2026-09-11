namespace MphRead.Mods.Network;

/// <summary>
/// Canonical semantic priority for reliable events. Queue admission and the
/// Worker transport must use the same policy so a critical event cannot lose
/// its reserved delivery capacity at either boundary.
/// </summary>
public static class ReliableEventPolicy
{
    public static bool IsCritical(ReliableEventType type)
        => type is ReliableEventType.Welcome or ReliableEventType.ClientReady
            or ReliableEventType.MapTransition or ReliableEventType.Disconnect
            or ReliableEventType.MatchState or ReliableEventType.Kill
            or ReliableEventType.WorldEvent or ReliableEventType.ObserverTransition
            or ReliableEventType.IntermissionBallot or ReliableEventType.TimingProfile
            or ReliableEventType.TimingProfileApplied;
}
