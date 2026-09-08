using System.Collections.Immutable;
using MphRead;

namespace FruityPrime.Server.Shared;

public enum TournamentControl { PauseBetweenRounds, Resume, EndTournament, ForceActiveResult }
public enum LobbyVoteChoice { Rematch, NextMap, ReturnToLobby, Map }
public sealed record LobbyMapChoice(string MapKey, MatchMode Mode);
public sealed record LobbyRoundStatus(long ExpectedRevision) : NodeCommand;
public sealed record LobbyTournamentIdentity(long ExpectedRevision, Guid TournamentId, Guid RoundId) : NodeCommand;
public sealed record LobbyTournamentControl(long ExpectedRevision, TournamentControl Action) : NodeCommand;
public sealed record LobbyTournamentSelectNext(long ExpectedRevision, Guid RoundId, string MapKey, MatchMode Mode) : NodeCommand;
public sealed record LobbyTournamentAssignTeam(long ExpectedRevision, Guid SessionId, byte Team) : NodeCommand;
public sealed record LobbyTournamentSetObserver(long ExpectedRevision, Guid SessionId, bool Observer) : NodeCommand;
public sealed record LobbyVoteOpen(long ExpectedRevision, ImmutableArray<LobbyMapChoice> Maps) : NodeCommand;
public sealed record LobbyVoteCast(long ExpectedRevision, uint BallotRevision, byte OptionId) : NodeCommand;
public sealed record LobbyVoteResolve(long ExpectedRevision, uint BallotRevision) : NodeCommand;
public sealed record LobbyVoteEntry(byte Id, LobbyVoteChoice Choice, string MapKey, MatchMode Mode, int Votes);
public sealed record NodeRoundSnapshot(LobbySnapshot Lobby, Guid? TournamentId, Guid? RoundId, bool Paused,
    bool TournamentEnded, long ConfigurationRevision, uint BallotRevision, DateTimeOffset? VoteDeadline,
    ImmutableArray<LobbyVoteEntry> Options, byte OwnVote);
