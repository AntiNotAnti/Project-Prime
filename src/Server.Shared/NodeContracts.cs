using System.Collections.Immutable;
using System.Text.Json.Serialization;
using MphRead;

namespace FruityPrime.Server.Shared;

public enum LobbyPhase { Open, StartingMatch, InMatch, PostMatch, Closing }
public enum LobbyVisibility { Public, Unlisted }
/// <summary>Policy used when a queued identity can claim a human player seat.</summary>
public enum LobbySeatPolicy { ImmediateSeat, NextMatchSeat, ObserverUntilNextMatch }
/// <summary>Vocabulary reserved for Duel queue ordering. Server.Node currently
/// accepts FIFO only; the other values fail closed until authoritative match
/// results can drive a real policy.</summary>
public enum DuelQueuePolicy { Fifo, WinnerStays, LoserStays, Manual }
public enum LobbyQueueRequestedRole { Player, Observer }
public enum LobbyQueueEntryState { Queued, SeatOffered, Promoted, Expired, Cancelled }
public sealed record LobbyMember(Guid SessionId, Guid? PlayerId, string DisplayName, Hunter Hunter, byte Team, bool Ready, bool Observer,
    Guid? GuestSessionId = null)
{
    [JsonIgnore]
    public HumanIdentityKey IdentityKey => HumanIdentityValidation.Require(PlayerId, GuestSessionId);
    public void Validate()
    {
        ContractGuard.Id(SessionId); IdentityKey.ToString();
        if (DisplayName is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(DisplayName)
            || DisplayName.Any(ch => ch < 32 || ch > 126)) throw new ArgumentException("Invalid lobby member.");
        if (!Enum.IsDefined(Hunter) || Hunter > Hunter.Guardian || Team > 1) throw new ArgumentException("Invalid lobby member.");
    }
}
public sealed record LobbyChatEntry(long Sequence, Guid SessionId, string DisplayName, string Text);
public sealed record LobbyQueueEntrySummary(int Position, string DisplayName, LobbyQueueEntryState State, long QueueSequence);
public sealed record LobbyQueueOffer(Guid OfferId, DateTimeOffset ExpiresAt, LobbySeatPolicy Policy);
public sealed record LobbyWaitlistSnapshot(int Count, ImmutableArray<LobbyQueueEntrySummary> Entries,
    bool IsSelfQueued = false, LobbyQueueEntryState? SelfState = null, long? SelfQueueSequence = null,
    LobbyQueueOffer? SelfOffer = null)
{
    public static LobbyWaitlistSnapshot Empty => new(0, ImmutableArray<LobbyQueueEntrySummary>.Empty);
};
public sealed record LobbySnapshot(Guid LobbyId, string Name, LobbyVisibility Visibility, Guid OwnerSessionId,
    LobbyPhase Phase, long Revision, int PlayerLimit, int ObserverLimit,
    ImmutableArray<LobbyMember> Members, ImmutableArray<LobbyChatEntry> Chat,
    string MapKey = "", MatchMode Mode = MatchMode.Battle, Guid? CurrentMatchId = null, int BotCount = 0, int? TimeLimitSeconds = null,
    LobbySeatPolicy SeatPolicy = LobbySeatPolicy.ImmediateSeat, DuelQueuePolicy DuelQueuePolicy = DuelQueuePolicy.Fifo,
    LobbyWaitlistSnapshot? Waitlist = null, int? PointGoal = null);
public sealed record LobbyListEntry(Guid LobbyId, string Name, LobbyPhase Phase, int Players, int PlayerLimit, int Observers, long Revision,
    int WaitlistCount = 0, int ObserverLimit = 16, int BotCount = 0);
public sealed record LobbyListSnapshot(ImmutableArray<LobbyListEntry> Lobbies, int? NextOffset);
public sealed record NodeSessionSnapshot(Guid SessionId, Guid? PlayerId, string DisplayName, Guid NodeId, string ResumeToken,
    Guid? GuestSessionId = null)
{
    [JsonIgnore]
    public HumanIdentityKey IdentityKey => HumanIdentityValidation.Require(PlayerId, GuestSessionId);
    public void Validate()
    {
        ContractGuard.Id(SessionId); ContractGuard.Id(NodeId); IdentityKey.ToString();
        if (DisplayName is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(DisplayName)
            || DisplayName.Any(ch => ch < 32 || ch > 126)) throw new ArgumentException("Invalid display name.");
        if (ResumeToken is not { Length: 43 } || ResumeToken.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid resume token.");
    }
    public override string ToString() => $"NodeSessionSnapshot {{ SessionId = {SessionId}, PlayerId = {PlayerId}, GuestSessionId = {GuestSessionId}, NodeId = {NodeId} }}";
}
public sealed record NodePing : NodeCommand;
public sealed record NodePong(long ServerUnixMilliseconds);
public sealed record NodeControlError(string Code, string Message);
public sealed record LobbyLeft(Guid LobbyId);

public abstract record NodeCommand;
public sealed record LobbyCreate(string Name, LobbyVisibility Visibility, int PlayerLimit = 8, int ObserverLimit = 16,
    LobbySeatPolicy SeatPolicy = LobbySeatPolicy.ImmediateSeat, DuelQueuePolicy DuelQueuePolicy = DuelQueuePolicy.Fifo) : NodeCommand;
public sealed record LobbyList(int Offset = 0, int Limit = 16) : NodeCommand;
public sealed record LobbyJoin(Guid LobbyId, long ExpectedRevision, bool Observer = false) : NodeCommand;
[method: JsonConstructor]
public sealed record LobbyQueueJoin(Guid LobbyId, long ExpectedRevision,
    LobbyQueueRequestedRole RequestedRole = LobbyQueueRequestedRole.Player, byte? RequestedTeam = null) : NodeCommand
{
    public LobbyQueueJoin(Guid lobbyId, long expectedRevision, byte? requestedTeam)
        : this(lobbyId, expectedRevision, LobbyQueueRequestedRole.Player, requestedTeam) { }
    public LobbyQueueJoin(long expectedRevision, LobbyQueueRequestedRole requestedRole = LobbyQueueRequestedRole.Player,
        byte? requestedTeam = null) : this(Guid.Empty, expectedRevision, requestedRole, requestedTeam) { }
    public LobbyQueueJoin(long expectedRevision, byte? requestedTeam)
        : this(Guid.Empty, expectedRevision, LobbyQueueRequestedRole.Player, requestedTeam) { }
}
[method: JsonConstructor]
public sealed record LobbyQueueLeave(Guid LobbyId, long ExpectedRevision) : NodeCommand
{
    public LobbyQueueLeave(long expectedRevision) : this(Guid.Empty, expectedRevision) { }
}
[method: JsonConstructor]
public sealed record LobbyQueueAccept(Guid LobbyId, long ExpectedRevision, Guid OfferId) : NodeCommand
{
    public LobbyQueueAccept(long expectedRevision, Guid offerId) : this(Guid.Empty, expectedRevision, offerId) { }
    public LobbyQueueAccept(Guid offerId, long expectedRevision) : this(Guid.Empty, expectedRevision, offerId) { }
}
[method: JsonConstructor]
public sealed record LobbyQueueDecline(Guid LobbyId, long ExpectedRevision, Guid OfferId) : NodeCommand
{
    public LobbyQueueDecline(long expectedRevision, Guid offerId) : this(Guid.Empty, expectedRevision, offerId) { }
    public LobbyQueueDecline(Guid offerId, long expectedRevision) : this(Guid.Empty, expectedRevision, offerId) { }
}
public sealed record LobbyLeave(long ExpectedRevision) : NodeCommand;
public sealed record LobbySetReady(bool Ready, long ExpectedRevision) : NodeCommand;
public sealed record LobbySelectHunter(Hunter Hunter, long ExpectedRevision) : NodeCommand;
public sealed record LobbyRequestTeam(byte Team, long ExpectedRevision) : NodeCommand;
public sealed record LobbyChat(string Text, long ExpectedRevision) : NodeCommand;
public sealed record LobbyConfigure(long ExpectedRevision, string MapKey, MatchMode Mode, int BotCount = 0,
    int? TimeLimitSeconds = null, int? PointGoal = null) : NodeCommand;
public sealed record LobbyStart(long ExpectedRevision) : NodeCommand;
public sealed record LobbyRematch(long ExpectedRevision) : NodeCommand;
public sealed record LobbyReturn(long ExpectedRevision) : NodeCommand;
public sealed record NodeMatchHandoff(Guid MatchId, uint WireMatchId, string Host, ushort Port, string Ticket, ulong Nonce, bool Observer, Hunter Hunter)
{ public override string ToString() => $"NodeMatchHandoff {{ MatchId = {MatchId}, WireMatchId = {WireMatchId}, Observer = {Observer} }}"; }
public sealed record NodeMatchEnded(Guid MatchId, bool Interrupted);
public sealed record NodeMatchRejoin(Guid MatchId) : NodeCommand;
public sealed record NodeControlRequest(Guid RequestId, NodeCommand Command);
