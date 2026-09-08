using System.Collections.Immutable;
using FruityPrime.Server.Shared;
using MphRead;
using MphRead.Identity;

namespace FruityPrime.Server.Node.Lobbies;

public sealed partial class LobbyManager
{
    private sealed class RoundState
    {
        public Guid? Tournament, Round, CompletedRound;
        public bool Paused, Ended;
        public long ConfigurationRevision;
        public uint BallotRevision, Random;
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
    private void OnRoundEnded(Guid id)
    {
        var state = Round(id);
        state.Options = []; state.Votes.Clear(); state.Deadline = null;
        // A tournament operator explicitly selects and identifies every next round.
        if (state.Tournament != null) { state.Paused = true; state.CompletedRound = state.Round; }
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
        var lobby = RequireLobby(identity.SessionId); Revision(lobby, expected);
        var state = Round(lobby.Id);
        if (command is not (LobbyRoundStatus or LobbyVoteCast) && lobby.Owner != identity.SessionId)
            throw Error("owner", "Only the lobby owner may control the next round.");
        switch (command)
        {
            case LobbyRoundStatus: response = RoundSnapshot(lobby, state, identity.SessionId); return true;
            case LobbyTournamentIdentity set:
                RequireConfigurable(lobby);
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
                Reopen(lobby, identity);
                Execute(identity, new LobbyConfigure(lobby.Revision, select.MapKey, select.Mode, lobby.BotCount, lobby.TimeLimitSeconds));
                state.Round = select.RoundId; state.ConfigurationRevision++; state.Paused = true;
                state.Options = []; state.Votes.Clear(); state.Deadline = null; break;
            case LobbyTournamentAssignTeam assign:
                RequireConfigurable(lobby);
                if (state.Tournament == null) throw Error("tournament", "Configure tournament identity first.");
                if (assign.Team > 1 || !lobby.Members.TryGetValue(assign.SessionId, out var teammate) || teammate.Observer)
                    throw Error("role", "Assign a player member to team 0 or 1.");
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
            case LobbyVoteOpen open:
                if (lobby.Phase != LobbyPhase.PostMatch || state.Tournament != null) throw Error("phase", "Intermission votes require a completed non-tournament match.");
                if (!state.Options.IsEmpty) throw Error("vote_pending", "The existing option set is frozen until resolution.");
                if (open.Maps.IsDefault || open.Maps.Length > 5) throw Error("invalid", "Supply at most five admitted map candidates.");
                foreach (var map in open.Maps) { if (map == null) throw Error("invalid", "Map candidate is required."); ValidateMap(map.MapKey, map.Mode); }
                if (open.Maps.Distinct().Count() != open.Maps.Length) throw Error("invalid", "Map candidates must be unique.");
                var next = open.Maps.FirstOrDefault() ?? new(lobby.MapKey, lobby.Mode);
                var options = ImmutableArray.CreateBuilder<LobbyVoteEntry>();
                options.Add(new(1, LobbyVoteChoice.Rematch, lobby.MapKey, lobby.Mode, 0));
                options.Add(new(2, LobbyVoteChoice.NextMap, next.MapKey, next.Mode, 0));
                options.Add(new(3, LobbyVoteChoice.ReturnToLobby, lobby.MapKey, lobby.Mode, 0));
                foreach (var map in open.Maps) options.Add(new((byte)(options.Count + 1), LobbyVoteChoice.Map, map.MapKey, map.Mode, 0));
                state.Options = options.ToImmutable(); state.BallotRevision++; if (state.BallotRevision == 0) state.BallotRevision = 1;
                state.Votes.Clear(); state.Deadline = RoundClock.GetUtcNow().AddSeconds(5); break;
            case LobbyVoteCast cast:
                RequireBallot(lobby, state, cast.BallotRevision);
                if (lobby.Members[identity.SessionId].Observer) throw Error("role", "Observers cannot vote.");
                if (RoundClock.GetUtcNow() >= state.Deadline) throw Error("deadline", "The vote deadline has passed.");
                if (cast.OptionId == 0 || cast.OptionId > state.Options.Length) throw Error("invalid", "Unknown vote option.");
                if (state.Votes.TryGetValue(identity.SessionId, out byte prior))
                { if (prior != cast.OptionId) throw Error("already_voted", "A confirmed vote cannot be changed."); response = RoundSnapshot(lobby, state, identity.SessionId); return true; }
                state.Votes[identity.SessionId] = cast.OptionId; break;
            case LobbyVoteResolve resolve:
                RequireBallot(lobby, state, resolve.BallotRevision);
                PruneVotes(lobby, state);
                if (RoundClock.GetUtcNow() < state.Deadline && state.Votes.Count < lobby.Members.Values.Count(m => !m.Observer))
                    throw Error("deadline", "Wait for all eligible votes or the deadline.");
                int maximum = state.Options.Max(option => state.Votes.Values.Count(v => v == option.Id));
                var tied = state.Options.Where(option => state.Votes.Values.Count(v => v == option.Id) == maximum).ToArray();
                var selected = maximum == 0 ? state.Options[1] : tied.Length == 1 ? tied[0]
                    : tied[(int)RngAlgorithm.Next(ref state.Random, (uint)tied.Length)];
                Reopen(lobby, identity);
                if (selected.Choice != LobbyVoteChoice.ReturnToLobby)
                    Execute(identity, new LobbyConfigure(lobby.Revision, selected.MapKey, selected.Mode, lobby.BotCount, lobby.TimeLimitSeconds));
                state.Options = []; state.Votes.Clear(); state.Deadline = null; state.ConfigurationRevision++; break;
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
    { if (lobby.Phase != LobbyPhase.PostMatch || state.Options.IsEmpty || revision != state.BallotRevision) throw Error("stale_ballot", "The intermission ballot is absent or changed."); }
    private static void PruneVotes(Lobby lobby, RoundState state)
    { foreach (Guid id in state.Votes.Keys.ToArray()) if (!lobby.Members.TryGetValue(id, out var member) || member.Observer) state.Votes.Remove(id); }
    private static NodeRoundSnapshot RoundSnapshot(Lobby lobby, RoundState state, Guid session)
    {
        PruneVotes(lobby, state);
        return new(lobby.Snapshot(), state.Tournament, state.Round, state.Paused, state.Ended, state.ConfigurationRevision,
            state.BallotRevision, state.Deadline, state.Options.Select(o => o with { Votes = state.Votes.Values.Count(v => v == o.Id) }).ToImmutableArray(), state.Votes.GetValueOrDefault(session));
    }
}
