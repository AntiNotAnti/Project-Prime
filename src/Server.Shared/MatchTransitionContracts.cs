using System.Text.Json.Serialization;
using MphRead;

namespace ProjectPrime.Server.Shared;

/// <summary>Operation requested for an active Node-hosted match.</summary>
public enum MatchTransitionChoice
{
    Restart,
    ChangeMap
}

/// <summary>Authoritative state of one active-match transition ballot.</summary>
/// <remarks>
/// This is deliberately the public wire name.  The server may keep richer
/// internal bookkeeping, but clients only need one flat state projection.
/// </remarks>
public enum MatchTransitionVoteState
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Failed
}

/// <summary>
/// Requests a Node-owned ballot for the current match. Restart leaves
/// <see cref="MapKey"/> null; ChangeMap requires a Node catalog map.
/// The match identity is deliberately carried by every command so an old
/// menu cannot affect a later MatchInstance in the same lobby.
/// </summary>
[method: JsonConstructor]
public sealed record LobbyMatchTransitionPropose(
    long ExpectedRevision,
    Guid MatchId,
    MatchTransitionChoice Choice,
    string? MapKey = null) : NodeCommand
{
    public void Validate()
    {
        ContractGuard.Id(MatchId);
        ContractGuard.Defined(Choice);
        if (ExpectedRevision < 0) throw new ArgumentException("Invalid lobby revision.");
        if (Choice == MatchTransitionChoice.Restart && MapKey != null)
            throw new ArgumentException("Restart cannot select a target map.");
        if (Choice == MatchTransitionChoice.ChangeMap
            && (MapKey is not { Length: > 0 and <= 128 }
                || MapKey.Any(c => c is < ' ' or > '~')))
            throw new ArgumentException("Change Map requires a bounded target map.");
    }
}

/// <summary>One immutable yes/no response to a transition ballot.</summary>
[method: JsonConstructor]
public sealed record LobbyMatchTransitionVote(
    long ExpectedRevision,
    Guid MatchId,
    uint BallotRevision,
    bool Accept) : NodeCommand
{
    public void Validate()
    {
        ContractGuard.Id(MatchId);
        if (ExpectedRevision < 0 || BallotRevision == 0)
            throw new ArgumentException("Invalid transition vote identity.");
    }
}

/// <summary>Immutable per-session flat projection of a transition ballot.</summary>
public sealed record NodeMatchTransitionVoteSnapshot(
    Guid LobbyId,
    Guid MatchId,
    Guid TransitionId,
    uint BallotRevision,
    Guid ProposerSessionId,
    string ProposerName,
    MatchTransitionChoice Choice,
    string TargetMapKey,
    MatchMode Mode,
    int Eligible,
    int Yes,
    int No,
    int Needed,
    DateTimeOffset Deadline,
    MatchTransitionVoteState State = MatchTransitionVoteState.Pending,
    bool? OwnVote = null,
    string? FailureCode = null)
{
    public void Validate()
    {
        ContractGuard.Id(LobbyId); ContractGuard.Id(MatchId); ContractGuard.Id(TransitionId);
        ContractGuard.Id(ProposerSessionId); ContractGuard.Text(ProposerName, 16);
        if (BallotRevision == 0 || TargetMapKey is not { Length: > 0 and <= 128 }
            || TargetMapKey.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Invalid transition ballot identity.");
        ContractGuard.Defined(Choice); ContractGuard.Defined(Mode); ContractGuard.Defined(State);
        if (Eligible < 1 || Eligible > 32 || Yes < 0 || Yes > Eligible
            || No < 0 || No > Eligible || Yes + No > Eligible
            || Needed < 1 || Needed > Eligible)
            throw new ArgumentException("Invalid transition ballot counts.");
        if (Deadline.Offset != TimeSpan.Zero || Deadline == DateTimeOffset.MinValue)
            throw new ArgumentException("Invalid transition ballot deadline.");
        if (FailureCode is { Length: > 128 } || FailureCode?.Any(char.IsControl) == true)
            throw new ArgumentException("Invalid transition ballot failure.");
        if (State == MatchTransitionVoteState.Pending && FailureCode != null)
            throw new ArgumentException("Pending transition ballots cannot carry a failure.");
        if (OwnVote is { } own && ((own && Yes < 1) || (!own && No < 1)))
            throw new ArgumentException("Own transition vote is not reflected in the tally.");
    }
}

/// <summary>
/// Announces that the Node has claimed the old match for a transition. The
/// replacement MatchId is intentionally not part of this event; clients use
/// <see cref="TransitionId"/> to reject stale started events.
/// </summary>
public sealed record NodeMatchTransitionStarted(
    Guid LobbyId,
    Guid PreviousMatchId,
    Guid TransitionId,
    MatchTransitionChoice Choice,
    string TargetMapKey,
    MatchMode Mode)
{
    public void Validate()
    {
        ContractGuard.Id(LobbyId); ContractGuard.Id(PreviousMatchId); ContractGuard.Id(TransitionId);
        ContractGuard.Defined(Choice); ContractGuard.Defined(Mode);
        if (TargetMapKey is not { Length: > 0 and <= 128 }
            || TargetMapKey.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Invalid transition target map.");
    }
}
