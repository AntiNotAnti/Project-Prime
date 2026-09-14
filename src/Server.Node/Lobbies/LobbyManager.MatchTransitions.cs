using System.Collections.Immutable;
using MphRead;
using MphRead.Identity;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Lobbies;

/// <summary>Frozen authority output consumed by NodeMatchCoordinator after a
/// transition ballot passes. The old MatchId is retained until the Worker
/// terminal acknowledgement arrives.</summary>
public sealed record LobbyMatchTransitionSelection(
    Guid LobbyId,
    Guid MatchId,
    Guid TransitionId,
    MatchTransitionChoice Choice,
    string TargetMapKey,
    MatchMode Mode,
    ImmutableArray<LobbyMember> Members,
    MatchLifecycleEpoch LifecycleEpoch = default,
    ImmutableDictionary<Guid, MembershipGeneration>? MembershipGenerations = null,
    bool ReturnToLobby = false)
{
    public string MapKey => TargetMapKey;
}

/// <summary>
/// The single approved continuation descriptor consumed by the Node
/// preparation loop.  Post-match ballots and acknowledged active transitions
/// retain their distinct public voting contracts, but both eventually produce
/// this immutable intent and use the same fresh MatchSpec preparation path.
/// </summary>
internal sealed record MatchContinuationIntent(
    Guid LobbyId,
    Guid? PreviousMatchId,
    Guid? TransitionId,
    LobbyVoteChoice? BallotChoice,
    MatchTransitionChoice? ActiveChoice,
    string TargetMapKey,
    MatchMode Mode,
    bool RequiresMapReadiness,
    SpawnPolicy? SpawnPolicy = null,
    Guid? ReplacementMatchId = null)
{
    public bool IsActiveTransition => ActiveChoice.HasValue;
}

public sealed partial class LobbyManager
{
    private sealed class MatchTransitionState
    {
        public Guid LobbyId;
        public Guid MatchId;
        public Guid TransitionId;
        public Guid ProposerSessionId;
        public string ProposerName = "";
        public HumanIdentityKey ProposerIdentity;
        public MatchTransitionChoice Choice;
        public bool ReturnToLobby;
        public string TargetMapKey = "";
        public MatchMode Mode;
        public uint BallotRevision;
        public DateTimeOffset Deadline;
        public int EligibleVoters;
        public int NeededVotes;
        public Dictionary<Guid, bool> Votes = [];
        public MatchTransitionVoteState State;
        public string? FailureCode;
        public bool Started;
        public MatchLifecycleEpoch LifecycleEpoch;
        public ImmutableDictionary<Guid, MembershipGeneration> MembershipGenerations =
            ImmutableDictionary<Guid, MembershipGeneration>.Empty;
    }

    /// <summary>Links a fresh replacement MatchId back to the approved
    /// transition until the Worker acknowledges MatchReady. This identity is
    /// internal and is never projected through a lobby snapshot.</summary>
    private sealed record TransitionOrigin(Guid LobbyId, Guid OldMatchId, Guid TransitionId);

    private readonly Dictionary<Guid, MatchTransitionState> _matchTransitions = [];
    private readonly Dictionary<Guid, MatchContinuationIntent> _continuationIntents = [];
    private readonly Dictionary<Guid, TransitionOrigin> _transitionOrigins = [];
    private readonly Dictionary<Guid, DateTimeOffset> _transitionRoomCooldowns = [];
    private readonly Dictionary<HumanIdentityKey, DateTimeOffset> _transitionProposerCooldowns = [];
    private uint _nextTransitionBallotRevision;

    public static readonly TimeSpan MatchTransitionVoteWindow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MatchTransitionRoomCooldown = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan MatchTransitionProposerCooldown = TimeSpan.FromSeconds(180);

    private bool HasActiveContinuation(Guid lobbyId)
        => _continuationIntents.TryGetValue(lobbyId,
            out MatchContinuationIntent? intent) && intent.IsActiveTransition;

    /// <summary>
    /// Removes executable continuation state and replacement origins. A
    /// failed active transition remains visible as a bounded failure
    /// projection until the next independent lobby lifecycle replaces it.
    /// </summary>
    private void ClearContinuationExecution(Guid lobbyId,
        bool clearFailureProjection)
    {
        _continuationIntents.Remove(lobbyId);
        foreach (Guid replacement in _transitionOrigins
            .Where(pair => pair.Value.LobbyId == lobbyId)
            .Select(pair => pair.Key).ToArray())
            _transitionOrigins.Remove(replacement);
        if (clearFailureProjection) _matchTransitions.Remove(lobbyId);
    }

    /// <summary>Returns the current immutable transition ballot projection for
    /// one session. Internal per-session votes never cross the control boundary
    /// except as this session's own yes/no value.</summary>
    public NodeMatchTransitionVoteSnapshot? MatchTransitionForSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (!_membership.TryGetValue(sessionId, out Guid lobbyId)
                || !_lobbies.TryGetValue(lobbyId, out Lobby? lobby)
                || !_matchTransitions.TryGetValue(lobbyId, out MatchTransitionState? state)
                || !TransitionMembershipStillValid(lobby, state))
                return null;
            PruneMatchTransitionElectorate(lobby, state);
            return TransitionSnapshot(state, sessionId);
        }
    }

    public NodeMatchTransitionVoteSnapshot? MatchTransitionForSession(Guid sessionId, Guid matchId)
        => MatchTransitionForSession(sessionId) is { MatchId: var current } snapshot
            && current == matchId ? snapshot : null;

    /// <summary>
    /// Creates the exact transition identity for an owner's explicit request
    /// to stop the active MatchInstance and reopen this same lobby. This is an
    /// owner control operation, not a ballot: non-owners leaving gameplay must
    /// not be able to stop a match that other players are still using.
    /// </summary>
    public LobbyMatchTransitionSelection RequestActiveMatchReturn(
        LobbyIdentity identity, long expectedRevision)
    {
        identity.Validate();
        lock (_gate)
        {
            Lobby lobby = RequireLobby(identity.SessionId);
            Revision(lobby, expectedRevision);
            if (lobby.Owner != identity.SessionId)
                throw Error("owner", "Only the owner may stop the active match.");
            if (lobby.Phase != LobbyPhase.InMatch || lobby.MatchId is not { } matchId)
                throw Error("phase", "There is no active match to return from.");
            if (_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? existing)
                && (existing.State == MatchTransitionVoteState.Pending || existing.Started))
                throw Error("transition_pending", "A match transition is already active.");
            if (Round(lobby.Id).Tournament != null)
                throw Error("unsupported", "Tournament matches cannot be stopped from the match menu.");

            DateTimeOffset now = RoundClock.GetUtcNow();
            var state = new MatchTransitionState
            {
                LobbyId = lobby.Id,
                MatchId = matchId,
                TransitionId = Guid.NewGuid(),
                ProposerSessionId = identity.SessionId,
                ProposerName = identity.DisplayName,
                ProposerIdentity = identity.IdentityKey,
                Choice = MatchTransitionChoice.Restart,
                ReturnToLobby = true,
                TargetMapKey = lobby.MapKey,
                Mode = lobby.Mode,
                LifecycleEpoch = lobby.LifecycleEpoch,
                MembershipGenerations = FreezeGenerations(lobby),
                BallotRevision = NextTransitionBallotRevision(),
                Deadline = now + MatchTransitionVoteWindow,
                EligibleVoters = lobby.Members.Values.Count(member => !member.Observer),
                NeededVotes = 1,
                State = MatchTransitionVoteState.Approved
            };
            state.Votes[identity.SessionId] = true;
            _matchTransitions[lobby.Id] = state;
            return Selection(lobby, state);
        }
    }

    /// <summary>Used by the coordinator to discover a newly approved ballot;
    /// no mutation occurs until <see cref="TryBeginMatchTransition"/>.</summary>
    public bool TryGetApprovedMatchTransition(Guid matchId,
        out LobbyMatchTransitionSelection? selection)
    {
        lock (_gate)
        {
            TryGetLobbyByMatchLocked(matchId, out Lobby? lobby);
            if (lobby == null || !_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.MatchId != matchId || state.State != MatchTransitionVoteState.Approved
                || state.Started || !TransitionMembershipStillValid(lobby, state))
            {
                selection = null;
                return false;
            }
            selection = Selection(lobby, state);
            return true;
        }
    }

    /// <summary>Atomically marks an approved ballot as started. Coordinator
    /// code calls this only after the scheduler has claimed the old placement.
    /// </summary>
    public bool TryBeginMatchTransition(Guid matchId, Guid transitionId,
        out LobbyMatchTransitionSelection? selection)
    {
        lock (_gate)
        {
            TryGetLobbyByMatchLocked(matchId, out Lobby? lobby);
            if (lobby == null || lobby.Phase != LobbyPhase.InMatch
                || !_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.MatchId != matchId || state.TransitionId != transitionId
                || state.State != MatchTransitionVoteState.Approved || state.Started
                || !TransitionMembershipStillValid(lobby, state))
            {
                selection = null;
                return false;
            }
            state.Started = true;
            selection = Selection(lobby, state);
            return true;
        }
    }

    public bool TryBeginMatchTransition(Guid matchId,
        out LobbyMatchTransitionSelection? selection)
    {
        lock (_gate)
        {
            TryGetLobbyByMatchLocked(matchId, out Lobby? lobby);
            MatchTransitionState? state = lobby != null && _matchTransitions.TryGetValue(lobby.Id, out var found)
                ? found : null;
            if (state == null) { selection = null; return false; }
            return TryBeginMatchTransition(matchId, state.TransitionId, out selection);
        }
    }

    /// <summary>
    /// Commits the transition boundary after the old Worker has acknowledged
    /// cancellation. The old MatchId is retired exactly once, while a private
    /// continuation selection carries the next map to PrepareMatchCore.
    /// </summary>
    public bool CompleteMatchTransition(Guid matchId, Guid transitionId)
    {
        lock (_gate)
        {
            TryGetLobbyByMatchLocked(matchId, out Lobby? lobby);
            if (lobby == null || !_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.MatchId != matchId || state.TransitionId != transitionId || !state.Started)
                return false;
            if (state.ReturnToLobby)
            {
                ReopenCore(lobby);
                _matchTransitions.Remove(lobby.Id);
                Publish(lobby);
                return true;
            }
            ContentIdentity content = ContentCatalog?.Get(state.TargetMapKey, state.Mode)
                ?? throw Error("map_unavailable", "No hosted content catalog.");
            bool requiresReadiness = state.Choice == MatchTransitionChoice.ChangeMap
                && content.RequiredMap != null;
            lobby.MapKey = state.TargetMapKey;
            lobby.Mode = state.Mode;
            // Host rules are intentionally preserved because active transitions
            // cannot change mode/rules. Keep only a valid frozen projection.
            lobby.HostRules = lobby.HostRules.ForMode(state.Mode);
            SetLobbyMatch(lobby, null);
            lobby.Phase = LobbyPhase.Open;
            InvalidateReady(lobby);
            ResetRoundState(lobby);
            _continuationIntents[lobby.Id] = new MatchContinuationIntent(
                lobby.Id, matchId, transitionId, null, state.Choice,
                state.TargetMapKey, state.Mode, requiresReadiness);
            Round(lobby.Id).AwaitingMapReadiness = requiresReadiness;
            Publish(lobby);
            return true;
        }
    }

    public bool CompleteMatchTransition(MatchId matchId, Guid transitionId)
        => CompleteMatchTransition(matchId.Value, transitionId);

    /// <summary>Records a transition failure without retiring the active
    /// MatchId.  A failed cancel send means the Worker may still be running, so
    /// the lobby must remain InMatch and the dedicated failure projection must
    /// stay visible until a later proposal replaces it.</summary>
    public bool FailMatchTransition(Guid matchId, Guid transitionId, string code = "transition_failed")
    {
        lock (_gate)
        {
            TryGetLobbyByMatchLocked(matchId, out Lobby? lobby);
            if (lobby == null || !_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.MatchId != matchId || state.TransitionId != transitionId)
                return false;
            state.State = MatchTransitionVoteState.Failed;
            state.FailureCode = BoundedFailure(code);
            state.Started = false;
            ClearContinuationExecution(lobby.Id, clearFailureProjection: false);
            // Keep the old MatchId and InMatch phase: the cancellation command
            // was not acknowledged, therefore the old MatchInstance remains
            // authoritative and ordinary completion is still meaningful.
            Publish(lobby);
            return true;
        }
    }

    public bool FailMatchTransition(MatchId matchId, Guid transitionId, string code = "transition_failed")
        => FailMatchTransition(matchId.Value, transitionId, code);

    /// <summary>Fails a replacement that was prepared for an approved
    /// transition before MatchReady. The old transition identity remains
    /// visible as a bounded Failed snapshot while the lobby is reopened, so a
    /// client waiting in continuation loading cannot become stranded.</summary>
    public bool FailPreparedMatchTransition(MatchId replacementMatchId, string code,
        out LobbyMatchTransitionSelection? selection)
    {
        lock (_gate)
        {
            selection = null;
            if (!_transitionOrigins.TryGetValue(replacementMatchId.Value, out TransitionOrigin? origin)
                || !_lobbies.TryGetValue(origin.LobbyId, out Lobby? lobby)
                || !_matchTransitions.TryGetValue(origin.LobbyId, out MatchTransitionState? state)
                || state.MatchId != origin.OldMatchId || state.TransitionId != origin.TransitionId)
                return false;
            _transitionOrigins.Remove(replacementMatchId.Value);
            state.State = MatchTransitionVoteState.Failed;
            state.FailureCode = BoundedFailure(code);
            state.Started = false;
            ClearContinuationExecution(origin.LobbyId, clearFailureProjection: false);
            ReopenCore(lobby);
            selection = Selection(lobby, state);
            Publish(lobby);
            return true;
        }
    }

    public bool FailPreparedMatchTransition(MatchId replacementMatchId,
        string code = "replacement_failed")
        => FailPreparedMatchTransition(replacementMatchId, code, out _);

    /// <summary>Handles a transition whose old Worker has already acknowledged
    /// cancellation but whose lobby boundary could not be committed. Unlike a
    /// failed cancel send, the old MatchInstance is no longer authoritative,
    /// so this path retires its MatchId and reopens the lobby.</summary>
    public bool FailCompletedMatchTransition(MatchId oldMatchId, Guid transitionId,
        string code, out LobbyMatchTransitionSelection? selection)
    {
        lock (_gate)
        {
            selection = null;
            TryGetLobbyByMatchLocked(oldMatchId.Value, out Lobby? lobby);
            if (lobby == null || !_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.MatchId != oldMatchId.Value || state.TransitionId != transitionId)
                return false;
            state.State = MatchTransitionVoteState.Failed;
            state.FailureCode = BoundedFailure(code);
            state.Started = false;
            ClearContinuationExecution(lobby.Id, clearFailureProjection: false);
            ReopenCore(lobby);
            selection = Selection(lobby, state);
            Publish(lobby);
            return true;
        }
    }

    public bool FailCompletedMatchTransition(MatchId oldMatchId, Guid transitionId,
        string code = "transition_ack_rejected")
        => FailCompletedMatchTransition(oldMatchId, transitionId, code, out _);

    /// <summary>Clears the private continuation identity exactly at the fresh
    /// MatchReady boundary. Ordinary matches do not have an origin entry.</summary>
    internal void CompletePreparedMatchTransition(MatchPlacement placement)
    {
        lock (_gate)
        {
            if (_transitionOrigins.Remove(placement.MatchId.Value,
                    out TransitionOrigin? origin))
            {
                ClearContinuationExecution(origin.LobbyId,
                    clearFailureProjection: false);
                _matchTransitions.Remove(origin.LobbyId);
                return;
            }

            // Ordinary post-match ballots use the same continuation intent,
            // but do not have an active-transition origin. Retire that intent
            // at the first Worker-ready boundary as well.
            TryGetLobbyByMatchLocked(placement.MatchId.Value, out Lobby? lobby);
            if (lobby is not null
                && _continuationIntents.TryGetValue(lobby.Id,
                    out MatchContinuationIntent? continuation)
                && continuation.ReplacementMatchId == placement.MatchId.Value)
            {
                _continuationIntents.Remove(lobby.Id);
                ResetRoundState(lobby);
            }
        }
    }

    private bool TryExecuteMatchTransitionCommand(LobbyIdentity identity,
        NodeCommand command, out object response)
    {
        response = null!;
        if (command is not (LobbyMatchTransitionPropose or LobbyMatchTransitionVote)) return false;
        switch (command)
        {
            case LobbyMatchTransitionPropose propose:
                propose.Validate();
                response = ProposeMatchTransition(identity, propose);
                return true;
            case LobbyMatchTransitionVote vote:
                vote.Validate();
                response = VoteMatchTransition(identity, vote);
                return true;
            default: return false;
        }
    }

    private NodeMatchTransitionVoteSnapshot ProposeMatchTransition(
        LobbyIdentity identity, LobbyMatchTransitionPropose command)
    {
        Lobby lobby = RequireLobby(identity.SessionId);
        Revision(lobby, command.ExpectedRevision);
        RequireTransitionParticipant(lobby, identity, command.MatchId);
        MatchTransitionState? existing = _matchTransitions.GetValueOrDefault(lobby.Id);
        if (existing is { State: MatchTransitionVoteState.Pending })
            throw Error("transition_pending", "A match transition ballot is already active.");
        if (existing is { Started: true })
            throw Error("transition_pending", "The match transition is already being applied.");
        if (Round(lobby.Id).Tournament != null)
            throw Error("unsupported", "Tournament matches cannot be transitioned from the match menu.");
        DateTimeOffset now = RoundClock.GetUtcNow();
        if (_transitionRoomCooldowns.TryGetValue(lobby.Id, out DateTimeOffset roomUntil)
            && roomUntil > now)
            throw Error("cooldown", "This lobby recently proposed a match transition.");
        if (_transitionProposerCooldowns.TryGetValue(identity.IdentityKey, out DateTimeOffset proposerUntil)
            && proposerUntil > now)
            throw Error("cooldown", "This identity recently proposed a match transition.");

        string targetMap = lobby.MapKey;
        if (command.Choice == MatchTransitionChoice.ChangeMap)
        {
            if (String.Equals(command.MapKey, lobby.MapKey, StringComparison.Ordinal))
                throw Error("map_same", "Change Map must select a different map.");
            if (ContentCatalog == null) throw Error("map_unavailable", "No hosted content catalog.");
            ContentCatalog.Validate(command.MapKey!, lobby.Mode);
            targetMap = command.MapKey!;
        }
        else if (command.Choice != MatchTransitionChoice.Restart)
            throw Error("invalid", "Unknown match transition choice.");

        var state = new MatchTransitionState
        {
            LobbyId = lobby.Id,
            MatchId = command.MatchId,
            TransitionId = Guid.NewGuid(),
            ProposerSessionId = identity.SessionId,
            ProposerName = identity.DisplayName,
            ProposerIdentity = identity.IdentityKey,
            Choice = command.Choice,
            TargetMapKey = targetMap,
            Mode = lobby.Mode,
            LifecycleEpoch = lobby.LifecycleEpoch,
            MembershipGenerations = FreezeGenerations(lobby),
            BallotRevision = NextTransitionBallotRevision(),
            Deadline = now + MatchTransitionVoteWindow,
            EligibleVoters = lobby.Members.Values.Count(member => !member.Observer),
            State = MatchTransitionVoteState.Pending
        };
        state.NeededVotes = (state.EligibleVoters * 7 + 9) / 10;
        state.Votes[identity.SessionId] = true;
        _transitionRoomCooldowns[lobby.Id] = now + MatchTransitionRoomCooldown;
        _transitionProposerCooldowns[identity.IdentityKey] = now + MatchTransitionProposerCooldown;
        _matchTransitions[lobby.Id] = state;
        ResolveMatchTransition(lobby, state);
        Publish(lobby);
        return TransitionSnapshot(state, identity.SessionId);
    }

    private NodeMatchTransitionVoteSnapshot VoteMatchTransition(
        LobbyIdentity identity, LobbyMatchTransitionVote command)
    {
        Lobby lobby = RequireLobby(identity.SessionId);
        Revision(lobby, command.ExpectedRevision);
        if (!_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
            || state.MatchId != command.MatchId || state.State != MatchTransitionVoteState.Pending
            || state.Started || state.BallotRevision != command.BallotRevision)
            throw Error("stale_ballot", "The transition ballot is absent or changed.");
        RequireTransitionParticipant(lobby, identity, command.MatchId);
        PruneMatchTransitionElectorate(lobby, state);
        DateTimeOffset now = RoundClock.GetUtcNow();
        if (now >= state.Deadline)
        {
            MatchTransitionVoteState before = state.State;
            ResolveMatchTransition(lobby, state);
            if (before != state.State) Publish(lobby);
            throw Error("deadline", "The vote deadline has passed.");
        }
        if (state.Votes.TryGetValue(identity.SessionId, out bool prior))
        {
            if (prior != command.Accept)
                throw Error("already_voted", "A confirmed vote cannot be changed.");
            return TransitionSnapshot(state, identity.SessionId);
        }
        state.Votes[identity.SessionId] = command.Accept;
        ResolveMatchTransition(lobby, state);
        Publish(lobby);
        return TransitionSnapshot(state, identity.SessionId);
    }

    private void ResolveMatchTransition(Lobby lobby, MatchTransitionState state)
    {
        if (state.State != MatchTransitionVoteState.Pending) return;
        PruneMatchTransitionElectorate(lobby, state);
        DateTimeOffset now = RoundClock.GetUtcNow();
        int yes = state.Votes.Values.Count(value => value);
        int no = state.Votes.Count - yes;
        if (now >= state.Deadline)
        {
            state.State = MatchTransitionVoteState.Expired;
            state.FailureCode = "deadline";
        }
        else if (yes >= state.NeededVotes)
            state.State = MatchTransitionVoteState.Approved;
        else if (no > state.EligibleVoters - state.NeededVotes)
        {
            state.State = MatchTransitionVoteState.Rejected;
            state.FailureCode = "threshold";
        }
    }

    private void PruneMatchTransitionElectorate(Lobby lobby, MatchTransitionState state)
    {
        // Reconnect-grace sessions remain in lobby.Members and therefore remain
        // electorate until NodeSessionManager expires them. Observers/bots do
        // not enter the electorate in the first place.
        foreach (Guid session in state.Votes.Keys.ToArray())
            if (!lobby.Members.TryGetValue(session, out LobbyMember? member) || member.Observer)
                state.Votes.Remove(session);
        state.EligibleVoters = lobby.Members.Values.Count(member => !member.Observer);
        state.NeededVotes = Math.Max(1, (state.EligibleVoters * 7 + 9) / 10);
    }

    private NodeMatchTransitionVoteSnapshot TransitionSnapshot(MatchTransitionState state, Guid sessionId)
    {
        bool? ownVote = state.Votes.TryGetValue(sessionId, out bool vote) ? vote : null;
        return new NodeMatchTransitionVoteSnapshot(state.LobbyId, state.MatchId,
            state.TransitionId, state.BallotRevision, state.ProposerSessionId,
            state.ProposerName, state.Choice, state.TargetMapKey, state.Mode,
            state.EligibleVoters, state.Votes.Values.Count(value => value),
            state.Votes.Values.Count(value => !value), state.NeededVotes,
            state.Deadline, state.State, ownVote, state.FailureCode,
            state.LifecycleEpoch,
            state.MembershipGenerations.GetValueOrDefault(sessionId));
    }

    private LobbyMatchTransitionSelection Selection(Lobby lobby, MatchTransitionState state)
        => new(lobby.Id, state.MatchId, state.TransitionId, state.Choice,
            state.TargetMapKey, state.Mode, lobby.Members.Values.ToImmutableArray(),
            state.LifecycleEpoch.Value == 0 ? MatchLifecycleEpoch.Initial : state.LifecycleEpoch,
            lobby.Members.Keys.ToImmutableDictionary(sessionId => sessionId,
                sessionId => state.MembershipGenerations[sessionId]),
            state.ReturnToLobby);

    private bool TransitionMembershipStillValid(Lobby lobby,
        MatchTransitionState state)
    {
        // A ballot may prune departed electorate members, but it must never
        // absorb a newly joined session or a session that left and rejoined.
        foreach (Guid sessionId in lobby.Members.Keys)
            if (!state.MembershipGenerations.ContainsKey(sessionId)) return false;
        foreach ((Guid sessionId, MembershipGeneration generation) in state.MembershipGenerations)
            if (lobby.Members.ContainsKey(sessionId)
                && (!_membershipGenerations.TryGetValue(sessionId, out MembershipGeneration current)
                    || current != generation)) return false;
        return true;
    }

    private void RequireTransitionParticipant(Lobby lobby, LobbyIdentity identity, Guid matchId)
    {
        if (lobby.Phase != LobbyPhase.InMatch || lobby.MatchId != matchId)
            throw Error("phase", "This match is no longer active.");
        if (!lobby.Members.TryGetValue(identity.SessionId, out LobbyMember? member)
            || member.Observer || member.IdentityKey != identity.IdentityKey)
            throw Error("role", "Only active human players may transition the match.");
        if (Round(lobby.Id).Tournament != null)
            throw Error("unsupported", "Tournament matches cannot be transitioned.");
    }

    private uint NextTransitionBallotRevision()
    {
        unchecked { _nextTransitionBallotRevision++; }
        if (_nextTransitionBallotRevision == 0) _nextTransitionBallotRevision = 1;
        return _nextTransitionBallotRevision;
    }

    private static string BoundedFailure(string code)
        => code is { Length: > 0 and <= 128 } && !code.Any(char.IsControl) ? code : "transition_failed";

    /// <summary>Advances active ballots from the existing Node-owned
    /// continuation/reaper loop. No per-lobby timer or thread is created.</summary>
    private void PruneMatchTransitionBallots()
    {
        DateTimeOffset now = RoundClock.GetUtcNow();
        foreach (Lobby lobby in _lobbies.Values.ToArray())
        {
            if (!_matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state)
                || state.State != MatchTransitionVoteState.Pending)
                continue;
            MatchTransitionVoteState before = state.State;
            ResolveMatchTransition(lobby, state);
            if (before != state.State) Publish(lobby);
        }
        foreach (Guid lobbyId in _transitionRoomCooldowns
            .Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            _transitionRoomCooldowns.Remove(lobbyId);
        foreach (HumanIdentityKey identity in _transitionProposerCooldowns
            .Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            _transitionProposerCooldowns.Remove(identity);
    }

    /// <summary>Called by PrepareContinuations while holding _gate. It keeps
    /// transition selection private and uses the existing PrepareMatchCore so
    /// seeds, tickets, admission nonces and fresh MatchIds are all regenerated.
    /// </summary>
    private bool TryPrepareContinuationIntent(Lobby lobby, NodeId node,
        Guid incarnation, List<(MatchSpec Spec, LobbyMember[] Members)> result,
        List<(Guid MatchId, Guid LobbyId, LobbyMember[] Members)> failures, int maximum)
    {
        if (!_continuationIntents.TryGetValue(lobby.Id, out MatchContinuationIntent? continuation))
            return false;
        if (result.Count >= maximum) return true;
        // Preparation is retried by the owned continuation loop until the
        // Worker acknowledges MatchReady. Do not mint a second MatchId while
        // the first replacement is still being placed.
        if (continuation.ReplacementMatchId.HasValue) return true;
        if (continuation.RequiresMapReadiness
            && lobby.Members.Values.Any(member => !member.Observer && !member.Ready))
            return true;
        string previousMapKey = lobby.MapKey;
        MatchMode previousMode = lobby.Mode;
        LobbyRulesOptions previousRules = lobby.HostRules;
        try
        {
            if (_admissionClosed) throw Error("draining", "Node is draining.");
            ContentIdentity content = ContentCatalog?.Get(continuation.TargetMapKey, continuation.Mode)
                ?? throw Error("map_unavailable", "No hosted content catalog.");
            LobbyRulesOptions nextRules = lobby.HostRules.ForMode(continuation.Mode);
            if (continuation.SpawnPolicy is { } spawnPolicy)
                nextRules = nextRules with { SpawnPolicy = spawnPolicy };
            _ = nextRules.ToMatchRules(continuation.Mode,
                continuation.TargetMapKey, lobby.Rules.PlayerLimit);
            lobby.MapKey = continuation.TargetMapKey;
            lobby.Mode = continuation.Mode;
            lobby.HostRules = nextRules;
            bool requireReady = continuation.RequiresMapReadiness;
            MatchSpec spec = PrepareMatchCore(lobby, content, node, incarnation, requireReady);
            Round(lobby.Id).AwaitingMapReadiness = false;
            _continuationIntents[lobby.Id] = continuation with
            { ReplacementMatchId = spec.MatchId!.Value };
            if (continuation.IsActiveTransition
                && continuation.PreviousMatchId is Guid previousMatchId
                && continuation.TransitionId is Guid transitionId)
                _transitionOrigins[spec.MatchId!.Value] = new(continuation.LobbyId,
                    previousMatchId, transitionId);
            result.Add((spec, lobby.Members.Values.ToArray()));
        }
        catch (LobbyCommandException ex) when (ex.Code == "player_disconnected")
        {
            // Keep the approved intent and readiness marker. Reconnect/ready
            // will cause the same descriptor to be retried without minting a
            // replacement identity until PrepareMatchCore succeeds.
            lobby.MapKey = previousMapKey;
            lobby.Mode = previousMode;
            lobby.HostRules = previousRules;
            return true;
        }
        catch (Exception ex) when (ex is LobbyCommandException or ArgumentException)
        {
            lobby.MapKey = previousMapKey;
            lobby.Mode = previousMode;
            lobby.HostRules = previousRules;
            ClearContinuationExecution(lobby.Id, clearFailureProjection: false);
            _matchTransitions.TryGetValue(lobby.Id, out MatchTransitionState? state);
            if (state != null && continuation.PreviousMatchId is Guid previousMatchId
                && continuation.TransitionId is Guid transitionId
                && state.MatchId == previousMatchId
                && state.TransitionId == transitionId)
            {
                state.State = MatchTransitionVoteState.Failed;
                state.FailureCode = BoundedFailure(ex is LobbyCommandException command
                    ? command.Code : "replacement_prepare_failed");
                state.Started = false;
            }
            ReopenCore(lobby);
            Publish(lobby);
        }
        return true;
    }
}
