using MphRead.Mods.Network;

namespace MphRead;

/// <summary>Versioned, tick-only definitions for the initial Prime awards.</summary>
public static class AwardPolicy
{
    public const uint KillWindowTicks = 180; // three seconds at the 60 Hz authority
    public const uint DefenseWindowTicks = 120; // two seconds, reserved for source facts
    public const int MaximumActors = 8;
    public const int MaximumSeenEvents = 256;

    public static bool IsCompetitiveKill(in MatchEvent value)
        => value.Kind == MatchEventKind.PlayerKilled && value.Subject.IsValid
            && (value.Flags & (MatchEventFlags.Suicide | MatchEventFlags.TeamKill | MatchEventFlags.EnvironmentKill)) == 0;

    public static bool WithinKillWindow(uint now, uint previous)
    {
        uint age = unchecked(now - previous);
        return age <= KillWindowTicks && (age == 0 || Sequence32.IsNewer(now, previous));
    }
}
