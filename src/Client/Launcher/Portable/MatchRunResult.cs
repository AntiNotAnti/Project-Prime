using System;

namespace MphRead.Mods.Launcher;

public enum MatchExitReason
{
    Completed, LeftMatch, Disconnected, Kicked, FailedToStart, ClientError, QuitApplication
}

/// <summary>Presentation data copied from the immutable replicated authority result.</summary>
public sealed record MatchResultsSnapshot(string MapKey, GameMode Mode, MatchResult Result);

public sealed record MatchRunResult(MatchExitReason Reason, Guid? MatchId = null,
    string? Message = null, MatchResultsSnapshot? Results = null)
{
    public static MatchExitReason Classify(bool started, bool quit, bool left,
        bool completed, bool interrupted, bool connectionFailed, bool clientError)
        => quit ? MatchExitReason.QuitApplication
        : left ? MatchExitReason.LeftMatch
        : interrupted ? MatchExitReason.Disconnected
        : completed ? MatchExitReason.Completed
        : !started ? MatchExitReason.FailedToStart
        : connectionFailed ? MatchExitReason.Disconnected
        : clientError ? MatchExitReason.ClientError : MatchExitReason.LeftMatch;
}
