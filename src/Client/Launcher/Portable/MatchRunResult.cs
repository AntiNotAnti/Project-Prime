using System;
using MphRead.Identity;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher;

public enum MatchExitReason
{
    Completed, LeftMatch, Disconnected, Kicked, FailedToStart, ClientError,
    /// <summary>The Node intentionally replaced this match; no Results screen is shown.</summary>
    Transitioning,
    QuitApplication
}

/// <summary>Presentation data copied from the immutable replicated authority result.</summary>
public sealed record MatchResultsSnapshot(string MapKey, GameMode Mode, MatchResult? Result,
    MatchCompletionSummary? Completion = null, PlayerId? LocalPlayerId = null,
    Guid? LocalGuestSessionId = null);

public sealed record MatchResultsPresentationResult(bool QuitApplication = false, string? Failure = null);

public sealed record MatchRunResult(MatchExitReason Reason, Guid? MatchId = null,
    string? Message = null, MatchResultsSnapshot? Results = null)
{
    public static MatchExitReason Classify(bool started, bool quit, bool left,
        bool completed, bool interrupted, bool connectionFailed, bool clientError,
        bool transitioning = false)
        => quit ? MatchExitReason.QuitApplication
        : left ? MatchExitReason.LeftMatch
        : transitioning ? MatchExitReason.Transitioning
        : interrupted ? MatchExitReason.Disconnected
        : completed ? MatchExitReason.Completed
        : !started ? MatchExitReason.FailedToStart
        : connectionFailed ? MatchExitReason.Disconnected
        : clientError ? MatchExitReason.ClientError : MatchExitReason.LeftMatch;
}
