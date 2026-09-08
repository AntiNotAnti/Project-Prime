using System.Collections.Immutable;
using MphRead;

namespace FruityPrime.Server.Shared;

public enum LobbyPhase { Open, StartingMatch, InMatch, PostMatch, Closing }
public enum LobbyVisibility { Public, Unlisted }
public sealed record LobbyMember(Guid SessionId, Guid PlayerId, string DisplayName, Hunter Hunter, byte Team, bool Ready, bool Observer);
public sealed record LobbyChatEntry(long Sequence, Guid SessionId, string DisplayName, string Text);
public sealed record LobbySnapshot(Guid LobbyId, string Name, LobbyVisibility Visibility, Guid OwnerSessionId,
    LobbyPhase Phase, long Revision, int PlayerLimit, int ObserverLimit,
    ImmutableArray<LobbyMember> Members, ImmutableArray<LobbyChatEntry> Chat,
    string MapKey = "", MatchMode Mode = MatchMode.Battle, Guid? CurrentMatchId = null, int BotCount = 0, int? TimeLimitSeconds = null);
public sealed record LobbyListEntry(Guid LobbyId, string Name, LobbyPhase Phase, int Players, int PlayerLimit, int Observers, long Revision);
public sealed record LobbyListSnapshot(ImmutableArray<LobbyListEntry> Lobbies, int? NextOffset);
public sealed record NodeSessionSnapshot(Guid SessionId, Guid PlayerId, string DisplayName, Guid NodeId, string ResumeToken)
{ public override string ToString() => $"NodeSessionSnapshot {{ SessionId = {SessionId}, PlayerId = {PlayerId}, NodeId = {NodeId} }}"; }
public sealed record NodePing : NodeCommand;
public sealed record NodePong(long ServerUnixMilliseconds);
public sealed record NodeControlError(string Code, string Message);
public sealed record LobbyLeft(Guid LobbyId);

public abstract record NodeCommand;
public sealed record LobbyCreate(string Name, LobbyVisibility Visibility, int PlayerLimit = 8, int ObserverLimit = 16) : NodeCommand;
public sealed record LobbyList(int Offset = 0, int Limit = 16) : NodeCommand;
public sealed record LobbyJoin(Guid LobbyId, long ExpectedRevision, bool Observer = false) : NodeCommand;
public sealed record LobbyLeave(long ExpectedRevision) : NodeCommand;
public sealed record LobbySetReady(bool Ready, long ExpectedRevision) : NodeCommand;
public sealed record LobbySelectHunter(Hunter Hunter, long ExpectedRevision) : NodeCommand;
public sealed record LobbyRequestTeam(byte Team, long ExpectedRevision) : NodeCommand;
public sealed record LobbyChat(string Text, long ExpectedRevision) : NodeCommand;
public sealed record LobbyConfigure(long ExpectedRevision, string MapKey, MatchMode Mode, int BotCount = 0, int? TimeLimitSeconds = null) : NodeCommand;
public sealed record LobbyStart(long ExpectedRevision) : NodeCommand;
public sealed record LobbyRematch(long ExpectedRevision) : NodeCommand;
public sealed record LobbyReturn(long ExpectedRevision) : NodeCommand;
public sealed record NodeMatchHandoff(Guid MatchId, uint WireMatchId, string Host, ushort Port, string Ticket, ulong Nonce, bool Observer, Hunter Hunter)
{ public override string ToString() => $"NodeMatchHandoff {{ MatchId = {MatchId}, WireMatchId = {WireMatchId}, Observer = {Observer} }}"; }
public sealed record NodeMatchEnded(Guid MatchId, bool Interrupted);
public sealed record NodeMatchRejoin(Guid MatchId) : NodeCommand;
public sealed record NodeControlRequest(Guid RequestId, NodeCommand Command);
