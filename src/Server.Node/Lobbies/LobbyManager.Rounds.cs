using System.Collections.Immutable;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;

namespace ProjectPrime.Server.Node.Lobbies;

public sealed partial class LobbyManager
{
    private sealed class RoundState
    {
        public Guid? Tournament, Round, CompletedRound;
        public bool Paused, Ended, AwaitingMapReadiness;
        public long ConfigurationRevision;
        public uint BallotRevision;
        public LobbyVoteEntry? Resolved;
        public HashSet<Guid> Electorate = [];
        public DateTimeOffset? Deadline;
        public ImmutableArray<LobbyVoteEntry> Options = [];
        public Dictionary<Guid, byte> Votes = [];
    }
    private readonly Dictionary<Guid, RoundState> _rounds = [];
    public TimeProvider RoundClock { get; init; } = TimeProvider.System;
    private RoundState Round(Guid id)
    {
        if (!_rounds.TryGetValue(id, out var round)) _rounds[id] = round = new();
        return round;
    }
    private void OnLobbyRemoved(Guid id) => _rounds.Remove(id);
    private void OnRoundEnded(Lobby lobby, bool interrupted)
    {
        var state = Round(lobby.Id);
        InvalidateReady(lobby);
        state.Options = []; state.Votes.Clear(); state.Deadline = null; state.Resolved = null; state.Electorate.Clear();
        if (state.Tournament != null)
        {
            state.Paused = true; state.CompletedRound = state.Round;
            if (interrupted) ReopenCore(lobby);
            return;
        }
        if (interrupted) { ReopenCore(lobby); return; }
        var maps = ContentCatalog?.RoundMaps(lobby.MapKey, lobby.Mode)
            ?? [new LobbyMapChoice(lobby.MapKey, lobby.Mode)];
        var next = maps[0];
        var options = ImmutableArray.CreateBuilder<LobbyVoteEntry>();
        options.Add(new(1, LobbyVoteChoice.Rematch, lobby.MapKey, lobby.Mode, 0,
            ContentCatalog?.RequiredMap(lobby.MapKey)));
        options.Add(new(2, LobbyVoteChoice.NextMap, next.MapKey, next.Mode, 0, next.RequiredMap));
        options.Add(new(3, LobbyVoteChoice.ReturnToLobby, lobby.MapKey, lobby.Mode, 0,
            ContentCatalog?.RequiredMap(lobby.MapKey)));
        SpawnPolicy currentSpawnPolicy = lobby.HostRules.SpawnPolicy
            ?? MatchRules.CreateDefault(lobby.Mode, lobby.MapKey,
                lobby.Rules.PlayerLimit).SpawnPolicy;
        SpawnPolicy[] policyChoices = lobby.Mode == MatchMode.Battle
            && lobby.Rules.PlayerLimit == 2
            ? [SpawnPolicy.Classic, SpawnPolicy.Enhanced, SpawnPolicy.Duel]
            : [SpawnPolicy.Classic, SpawnPolicy.Enhanced];
        SpawnPolicy[] alternatePolicies = policyChoices.Where(policy =>
            policy != currentSpawnPolicy).ToArray();
        // Preserve the established ids of map choices. Control clients may vote
        // by id while rendering the authoritative ballot, so new policy choices
        // are appended after reserving their bounded share of the eight slots.
        int remainingMapChoices = Math.Max(0, 8 - options.Count
            - alternatePolicies.Length);
        foreach (var map in maps.Where(m => m.MapKey != lobby.MapKey
            && m.MapKey != next.MapKey).Take(remainingMapChoices))
            options.Add(new((byte)(options.Count + 1), LobbyVoteChoice.Map, map.MapKey, map.Mode, 0,
                map.RequiredMap));
        foreach (SpawnPolicy policy in alternatePolicies)
        {
            options.Add(new((byte)(options.Count + 1), LobbyVoteChoice.SpawnPolicy,
                lobby.MapKey, lobby.Mode, 0,
                ContentCatalog?.RequiredMap(lobby.MapKey), policy));
        }
        state.Options = options.ToImmutable();
        state.BallotRevision++; if (state.BallotRevision == 0) state.BallotRevision = 1;
        state.Electorate = lobby.Members.Values.Where(m => !m.Observer).Select(m => m.SessionId).ToHashSet();
        state.Deadline = RoundClock.GetUtcNow().AddSeconds(PostMatchVoteSeconds);
    }
    public NodeContentCatalog? ContentCatalog { get; set; }
    public NodeRoundSnapshot? RoundForSession(Guid session)
    {
        lock (_gate) return _membership.TryGetValue(session, out var id)
            ? RoundSnapshot(_lobbies[id], Round(id), session) : null;
    }
    private void ResolveBallot(Lobby lobby, RoundState state)
    {
        PruneVotes(lobby, state);
        if (state.Options.IsEmpty || state.Resolved != null ||
            (RoundClock.GetUtcNow() < state.Deadline && state.Votes.Count < state.Electorate.Count)) return;
        int maximum = state.Options.Max(o => state.Votes.Values.Count(v => v == o.Id));
        state.Resolved = maximum == 0 ? state.Options[1]
            : state.Options.First(o => state.Votes.Values.Count(v => v == o.Id) == maximum);
        state.Resolved = state.Resolved with { Votes = maximum };
        state.ConfigurationRevision++;
        if (state.Resolved.Choice == LobbyVoteChoice.ReturnToLobby) ReopenCore(lobby);
        else if (state.Resolved.RequiredMap != null && state.Resolved.MapKey != lobby.MapKey)
            BeginMapReadiness(lobby, state, state.Resolved);
        Publish(lobby);
    }
    private void BeginMapReadiness(Lobby lobby, RoundState state, LobbyVoteEntry selected)
    {
        ContentIdentity content = ContentCatalog?.Get(selected.MapKey, selected.Mode)
            ?? throw Error("map_unavailable", "No hosted content catalog.");
        if (content.RequiredMap != selected.RequiredMap)
            throw Error("map_identity", "The selected map package identity changed.");
        lobby.MapKey = selected.MapKey;
        lobby.Mode = selected.Mode;
        lobby.HostRules = lobby.HostRules.ForMode(selected.Mode);
        lobby.Phase = LobbyPhase.Open;
        lobby.MatchId = null;
        InvalidateReady(lobby);
        state.AwaitingMapReadiness = true;
        state.Options = [];
        state.Votes.Clear();
        state.Deadline = null;
        state.Electorate.Clear();
    }
    private void ReopenCore(Lobby lobby)
    {
        lobby.Phase = LobbyPhase.Open; lobby.MatchId = null; InvalidateReady(lobby);
        var state = Round(lobby.Id);
        state.Options = []; state.Votes.Clear(); state.Deadline = null; state.Electorate.Clear();
        state.AwaitingMapReadiness = false;
    }
    public IReadOnlyList<(MatchSpec Spec, LobbyMember[] Members)> PrepareContinuations(NodeId node, Guid incarnation, int maximum = 64)
        => PrepareContinuations(node, incarnation, maximum, out _);
    public IReadOnlyList<(MatchSpec Spec, LobbyMember[] Members)> PrepareContinuations(NodeId node, Guid incarnation,
        int maximum, out IReadOnlyList<(Guid MatchId, LobbyMember[] Members)> failures)
    {
        lock (_gate)
        {
            PruneExpiredSessionMembership();
            var failed = new List<(Guid, LobbyMember[])>();
            failures = failed;
            var result = new List<(MatchSpec, LobbyMember[])>();
            foreach (var lobby in _lobbies.Values.ToArray())
            {
                // Active-match transitions are retired by the coordinator at
                // the old Worker terminal boundary. Their private continuation
                // selection is prepared before the legacy post-match ballot.
                if (TryPrepareTransitionContinuation(lobby, node, incarnation,
                    result, failed, maximum))
                    continue;
                var state = Round(lobby.Id);
                bool postMatch = lobby.Phase == LobbyPhase.PostMatch;
                bool acquiring = lobby.Phase == LobbyPhase.Open && state.AwaitingMapReadiness;
                if ((!postMatch && !acquiring) || state.Tournament != null) continue;
                ResolveBallot(lobby, state);
                if (result.Count >= maximum) continue;
                if (state.Resolved is not { } selected) continue;
                if (state.AwaitingMapReadiness
                    && lobby.Members.Values.Any(member => !member.Observer && !member.Ready)) continue;
                if (lobby.Phase is not (LobbyPhase.PostMatch or LobbyPhase.Open)) continue;
                try
                {
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    var content = ContentCatalog?.Get(selected.MapKey, selected.Mode)
                        ?? throw Error("map_unavailable", "No hosted content catalog.");
                    // A rotation may select a different mode in a future
                    // catalog. Preserve common host options, but project
                    // mode-specific values before changing the lobby so an
                    // old Survival/Battle rule cannot invalidate continuation.
                    var nextRules = lobby.HostRules.ForMode(selected.Mode);
                    if (selected.Choice == LobbyVoteChoice.SpawnPolicy
                        && selected.SpawnPolicy is { } spawnPolicy)
                    {
                        nextRules = nextRules with { SpawnPolicy = spawnPolicy };
                    }
                    _ = nextRules.ToMatchRules(selected.Mode, selected.MapKey, lobby.Rules.PlayerLimit);
                    lobby.MapKey = selected.MapKey; lobby.Mode = selected.Mode;
                    lobby.HostRules = nextRules;
                    bool requireReady = state.AwaitingMapReadiness;
                    state.AwaitingMapReadiness = false;
                    var spec = PrepareMatchCore(lobby, content, node, incarnation, requireReady);
                    state.Options = []; state.Votes.Clear(); state.Deadline = null; state.Electorate.Clear();
                    result.Add((spec, lobby.Members.Values.ToArray()));
                }
                catch (Exception ex) when (ex is LobbyCommandException or ArgumentException)
                {
                    if (ex is LobbyCommandException { Code: "player_disconnected" })
                        continue;
                    if (lobby.MatchId is { } match) failed.Add((match, lobby.Members.Values.ToArray()));
                    ReopenCore(lobby); Publish(lobby);
                }
            }
            return result;
        }
    }
    private void ValidateRoundStart(Guid id)
    {
        var round = Round(id);
        if (round.Paused || round.Ended) throw Error("paused", "The next round is held by the lobby authority.");
        if (round.Tournament != null && (round.Round == null || round.Round == round.CompletedRound)) throw Error("round_identity", "Select the tournament round before starting.");
        if (!round.Options.IsEmpty) throw Error("vote_pending", "Resolve the intermission vote before starting.");
    }
    private MatchSpec ApplyRoundIdentity(Guid id, MatchSpec spec)
    {
        var round = Round(id);
        return spec with { TournamentId = round.Tournament, RoundId = round.Round };
    }
    private bool TryExecuteRoundCommand(LobbyIdentity identity, NodeCommand command, out object response)
    {
        response = null!;
        long expected = command switch
        {
            LobbyRoundStatus c => c.ExpectedRevision, LobbyTournamentIdentity c => c.ExpectedRevision,
            LobbyTournamentControl c => c.ExpectedRevision, LobbyTournamentSelectNext c => c.ExpectedRevision,
            LobbyTournamentAssignTeam c => c.ExpectedRevision, LobbyTournamentSetObserver c => c.ExpectedRevision,
            LobbyVoteOpen c => c.ExpectedRevision, LobbyVoteCast c => c.ExpectedRevision,
            LobbyVoteResolve c => c.ExpectedRevision, _ => -1
        };
        if (command is not (LobbyRoundStatus or LobbyTournamentIdentity or LobbyTournamentControl
            or LobbyTournamentSelectNext or LobbyTournamentAssignTeam or LobbyTournamentSetObserver or LobbyVoteOpen or LobbyVoteCast or LobbyVoteResolve)) return false;
        if (command is LobbyVoteOpen or LobbyVoteResolve) throw Error("unsupported", "The Node owns ballot creation and resolution.");
        var lobby = RequireLobby(identity.SessionId); Revision(lobby, expected);
        if (_transitionContinuations.ContainsKey(lobby.Id) && command is not LobbyRoundStatus)
            throw Error("transitioning", "Match transition is preparing.");
        var state = Round(lobby.Id);
        if (command is not (LobbyRoundStatus or LobbyVoteCast) && lobby.Owner != identity.SessionId)
            throw Error("owner", "Only the lobby owner may control the next round.");
        switch (command)
        {
            case LobbyRoundStatus: response = RoundSnapshot(lobby, state, identity.SessionId); return true;
            case LobbyTournamentIdentity set:
                RequireConfigurable(lobby);
                if (!state.Options.IsEmpty) throw Error("vote_pending", "The active ballot must finish before tournament configuration.");
                if (set.TournamentId == Guid.Empty || set.RoundId == Guid.Empty) throw Error("invalid", "Tournament and round identities are required.");
                if (state.Tournament != set.TournamentId) state.CompletedRound = null;
                state.Tournament = set.TournamentId; state.Round = set.RoundId; state.Ended = false; state.Paused = true;
                state.ConfigurationRevision++; InvalidateReady(lobby); break;
            case LobbyTournamentControl control:
                if (state.Tournament == null) throw Error("tournament", "This lobby has no tournament identity.");
                switch (control.Action)
                {
                    case TournamentControl.PauseBetweenRounds: state.Paused = true; break;
                    case TournamentControl.Resume:
                        if (state.Ended) throw Error("ended", "Configure a new tournament before resuming.");
                        state.Paused = false; break;
                    case TournamentControl.EndTournament: state.Ended = state.Paused = true; break;
                    default: throw Error("unsupported", "Active match results cannot be forced by a lobby command; use the authenticated match admin route.");
                }
                break;
            case LobbyTournamentSelectNext select:
                RequireConfigurable(lobby);
                if (state.Tournament == null || state.Ended || select.RoundId == Guid.Empty || select.RoundId == state.Round)
                    throw Error("round_identity", "Select a new round identity for an active tournament.");
                ValidateMap(select.MapKey, select.Mode);
                LobbyRulesOptions nextRules;
                try
                {
                    // Validate the existing canonical rules against the next
                    // mode before reopening the lobby. A failed mode change
                    // must not partially mutate round or lobby state.
                    nextRules = lobby.HostRules.ForMode(select.Mode);
                    _ = nextRules.ToMatchRules(select.Mode, select.MapKey, lobby.Rules.PlayerLimit);
                }
                catch (ArgumentException ex)
                { throw Error("invalid", ex.Message); }
                Reopen(lobby, identity);
                Execute(identity, new LobbyConfigure(lobby.Revision, select.MapKey, select.Mode,
                    lobby.BotCount, nextRules));
                state.Round = select.RoundId; state.ConfigurationRevision++; state.Paused = true;
                state.Options = []; state.Votes.Clear(); state.Deadline = null; break;
            case LobbyTournamentAssignTeam assign:
                RequireConfigurable(lobby);
                if (state.Tournament == null) throw Error("tournament", "Configure tournament identity first.");
                int assignableTeamCount = lobby.Mode.IsTeamMode()
                    ? ConfiguredTeamCount(lobby) : 2;
                if (assign.Team >= assignableTeamCount
                    || !lobby.Members.TryGetValue(assign.SessionId, out var teammate)
                    || teammate.Observer)
                    throw Error("role", "Assign a player member to a configured team.");
                lobby.Members[assign.SessionId] = teammate with { Team = assign.Team, Ready = false };
                state.ConfigurationRevision++; InvalidateReady(lobby); break;
            case LobbyTournamentSetObserver spectator:
                RequireConfigurable(lobby);
                if (state.Tournament == null) throw Error("tournament", "Configure tournament identity first.");
                if (!lobby.Members.TryGetValue(spectator.SessionId, out var target)) throw Error("not_found", "Lobby member not found.");
                if (target.Observer != spectator.Observer && lobby.Members.Values.Count(m => m.Observer == spectator.Observer)
                    >= (spectator.Observer ? lobby.Rules.ObserverLimit : lobby.Rules.PlayerLimit - lobby.BotCount)) throw Error("capacity", "Target role capacity reached.");
                lobby.Members[spectator.SessionId] = target with { Observer = spectator.Observer, Ready = false };
                state.ConfigurationRevision++; InvalidateReady(lobby); break;
            case LobbyVoteCast cast:
                RequireBallot(lobby, state, cast.BallotRevision);
                if (!state.Electorate.Contains(identity.SessionId)) throw Error("role", "Observers cannot vote.");
                if (RoundClock.GetUtcNow() >= state.Deadline) throw Error("deadline", "The vote deadline has passed.");
                if (cast.OptionId == 0 || cast.OptionId > state.Options.Length) throw Error("invalid", "Unknown vote option.");
                if (state.Votes.TryGetValue(identity.SessionId, out byte prior))
                { if (prior != cast.OptionId) throw Error("already_voted", "A confirmed vote cannot be changed."); response = RoundSnapshot(lobby, state, identity.SessionId); return true; }
                state.Votes[identity.SessionId] = cast.OptionId; ResolveBallot(lobby, state); break;

        }
        Publish(lobby);
        response = RoundSnapshot(lobby, state, identity.SessionId); return true;
    }
    private void Reopen(Lobby lobby, LobbyIdentity identity)
    { if (lobby.Phase == LobbyPhase.PostMatch) ReturnToLobby(identity.SessionId, lobby.Revision); }
    private static void RequireConfigurable(Lobby lobby)
    { if (lobby.Phase is not (LobbyPhase.Open or LobbyPhase.PostMatch)) throw Error("phase", "Round configuration is frozen while a match is starting or active."); }
    private static void ValidateMap(string map, MatchMode mode)
    { if (!Text(map, 128) || !Enum.IsDefined(mode)) throw Error("invalid", "Invalid map/mode."); _ = MatchRules.CreateDefault(mode, map); }
    private static void InvalidateReady(Lobby lobby)
    { foreach (var member in lobby.Members.ToArray()) lobby.Members[member.Key] = member.Value with { Ready = false }; }
    private static void RequireBallot(Lobby lobby, RoundState state, uint revision)
    { if (lobby.Phase != LobbyPhase.PostMatch || state.Options.IsEmpty || state.Resolved != null || revision != state.BallotRevision) throw Error("stale_ballot", "The intermission ballot is absent or changed."); }
    private static void PruneVotes(Lobby lobby, RoundState state)
    { state.Electorate.RemoveWhere(id => !lobby.Members.TryGetValue(id, out var member) || member.Observer);
        foreach (Guid id in state.Votes.Keys.ToArray()) if (!state.Electorate.Contains(id)) state.Votes.Remove(id); }
    private NodeRoundSnapshot RoundSnapshot(Lobby lobby, RoundState state, Guid session)
    {
        PruneVotes(lobby, state);
        return new(lobby.Snapshot(IdentityForSession(lobby, session), RequirementFor(lobby)),
            state.Tournament, state.Round, state.Paused, state.Ended, state.ConfigurationRevision,
            state.BallotRevision, state.Deadline, state.Options.Select(o => o with { Votes = state.Votes.Values.Count(v => v == o.Id) }).ToImmutableArray(), state.Votes.GetValueOrDefault(session), state.Resolved);
    }
}
