using System.Collections.Immutable;
using System.Text.Json.Serialization;
using MphRead;

namespace ProjectPrime.Server.Shared;

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

/// <summary>
/// User-facing host rules. Null means use the authoritative mode default; it
/// does not mean an arbitrary or client-selected value. The Node normalizes
/// legacy LobbyConfigure fields into this one representation before mutating a
/// lobby, and the same representation is used to build every MatchSpec.
/// </summary>
public sealed record LobbyRulesOptions(
    int? TimeLimitSeconds = null,
    int? ScoreGoal = null,
    int? StartingLives = null,
    int? ObjectiveTimeGoalSeconds = null,
    int? DamageLevel = null,
    bool? FriendlyFire = null,
    bool? AffinityWeapons = null,
    bool? PlayerRadar = null,
    bool? OctolithReset = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    KillcamPolicy? KillcamPolicy = null)
{
    public static LobbyRulesOptions Empty { get; } = new();

    /// <summary>
    /// Combines the structured fields with the two pre-rules-extension fields
    /// carried by older clients. This is deliberately the only merge point:
    /// callers must use the returned value as the lobby's canonical rules.
    /// </summary>
    public LobbyRulesOptions Normalize(MatchMode mode, int? legacyTimeLimitSeconds = null,
        int? legacyPointGoal = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown match mode.", nameof(mode));

        int? time = Merge(TimeLimitSeconds, legacyTimeLimitSeconds, "time limit");
        bool survival = mode is MatchMode.Survival or MatchMode.TeamSurvival;
        bool objective = mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        int? score = ScoreGoal;
        int? lives = StartingLives;

        if (survival)
        {
            if (score.HasValue) throw new ArgumentException("Score goal is not applicable to Survival modes.");
            lives = Merge(lives, legacyPointGoal, "starting lives");
        }
        else if (objective)
        {
            if (score.HasValue || lives.HasValue || legacyPointGoal.HasValue)
                throw new ArgumentException("Score goal and starting lives are not applicable to objective-time modes.");
            score = null;
            lives = null;
        }
        else
        {
            if (lives.HasValue) throw new ArgumentException("Starting lives are only applicable to Survival modes.");
            score = Merge(score, legacyPointGoal, "score goal");
            lives = null;
        }

        if (!objective && ObjectiveTimeGoalSeconds.HasValue)
            throw new ArgumentException("Objective time goal is only applicable to objective-time modes.");
        if (mode is not (MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty) && OctolithReset.HasValue)
            throw new ArgumentException("Octolith reset is only applicable to octolith modes.");

        ValidateRange(time, 1, 3600, "Time limit");
        ValidateRange(score, 1, ushort.MaxValue, "Score goal");
        ValidateRange(lives, 1, ushort.MaxValue, "Starting lives");
        ValidateRange(ObjectiveTimeGoalSeconds, 1, 3600, "Objective time goal");
        ValidateRange(DamageLevel, 0, 2, "Damage level");
        if (KillcamPolicy.HasValue && !Enum.IsDefined(KillcamPolicy.Value))
            throw new ArgumentException("Unknown killcam policy.", nameof(KillcamPolicy));

        return this with
        {
            TimeLimitSeconds = time,
            ScoreGoal = score,
            StartingLives = lives,
            ObjectiveTimeGoalSeconds = objective ? ObjectiveTimeGoalSeconds : null
        };
    }

    /// <summary>Builds the immutable Game rules from already-normalized values.</summary>
    public MphRead.MatchRules ToMatchRules(MatchMode mode, string mapKey, int maxPlayers = 8)
    {
        LobbyRulesOptions normalized = Normalize(mode);
        MphRead.MatchRules defaults = MphRead.MatchRules.CreateDefault(mode, mapKey, maxPlayers);
        return defaults.With(
            timeLimit: normalized.TimeLimitSeconds is { } time ? TimeSpan.FromSeconds(time) : defaults.TimeLimit,
            scoreGoal: normalized.ScoreGoal ?? defaults.ScoreGoal,
            startingLives: normalized.StartingLives ?? defaults.StartingLives,
            objectiveTimeGoal: normalized.ObjectiveTimeGoalSeconds is { } objective
                ? TimeSpan.FromSeconds(objective) : defaults.ObjectiveTimeGoal,
            damageLevel: normalized.DamageLevel ?? defaults.DamageLevel,
            friendlyFire: normalized.FriendlyFire ?? defaults.FriendlyFire,
            affinityWeapons: normalized.AffinityWeapons ?? defaults.AffinityWeapons,
            playerRadar: normalized.PlayerRadar ?? defaults.PlayerRadar,
            octolithReset: normalized.OctolithReset ?? defaults.OctolithReset,
            killcamPolicy: normalized.KillcamPolicy ?? defaults.KillcamPolicy);
    }

    /// <summary>
    /// Projects a frozen lobby rule set onto an explicitly selected next mode.
    /// Mode-specific values that cannot apply are dropped so the target mode's
    /// defaults are used; common host options remain unchanged. This is only
    /// for authoritative round transitions. A client configure still rejects
    /// inapplicable fields rather than silently rewriting them.
    /// </summary>
    public LobbyRulesOptions ForMode(MatchMode mode)
    {
        bool survival = mode is MatchMode.Survival or MatchMode.TeamSurvival;
        bool objective = mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        bool octolith = mode is MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty;
        return (this with
        {
            ScoreGoal = survival || objective ? null : ScoreGoal,
            StartingLives = survival ? StartingLives : null,
            ObjectiveTimeGoalSeconds = objective ? ObjectiveTimeGoalSeconds : null,
            OctolithReset = octolith ? OctolithReset : null
        }).Normalize(mode);
    }

    /// <summary>Legacy list/snapshot projection. Survival displays lives in the
    /// old point-goal field; objective-time modes have no point goal.</summary>
    public int? LegacyPointGoal(MatchMode mode)
        => mode is MatchMode.Survival or MatchMode.TeamSurvival ? StartingLives
            : mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter ? null : ScoreGoal;

    private static int? Merge(int? structured, int? legacy, string label)
    {
        if (structured.HasValue && legacy.HasValue && structured != legacy)
            throw new ArgumentException($"Conflicting {label} values.");
        return structured ?? legacy;
    }

    private static void ValidateRange(int? value, int minimum, int maximum, string label)
    {
        if (value is { } actual && (actual < minimum || actual > maximum))
            throw new ArgumentOutOfRangeException(label, $"{label} must be between {minimum} and {maximum}.");
    }
}

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
    LobbyWaitlistSnapshot? Waitlist = null, int? PointGoal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyRulesOptions? Rules = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MapRequirement? RequiredMap = null);
public sealed record LobbyListEntry(Guid LobbyId, string Name, LobbyPhase Phase, int Players, int PlayerLimit, int Observers, long Revision,
    int WaitlistCount = 0, int ObserverLimit = 16, int BotCount = 0, string MapKey = "", MatchMode Mode = MatchMode.Battle,
    int? TimeLimitSeconds = null, int? PointGoal = null, int? ObjectiveTimeGoalSeconds = null,
    LobbySeatPolicy SeatPolicy = LobbySeatPolicy.ImmediateSeat);
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
/// <summary>Requests one bounded, revision-pinned page of the Node catalog.</summary>
public sealed record NodeCatalogRequest(long Revision, int Page = 0, int PageSize = 8) : NodeCommand;
/// <summary>One bounded catalog page. Pages are never a second authority: the
/// Node remains authoritative and clients replace their prior catalog only
/// after every page for this revision has been validated.</summary>
public sealed record NodeCatalogPage(long Revision, int Page, int PageCount, int TotalEntries,
    string CatalogHash, ImmutableArray<ContentIdentity> Entries);
public sealed record NodePong(long ServerUnixMilliseconds);
public sealed record NodeControlError(string Code, string Message);
public sealed record LobbyLeft(Guid LobbyId);

public abstract record NodeCommand;
public sealed record LobbyCreate(string Name, LobbyVisibility Visibility, int PlayerLimit = 8, int ObserverLimit = 16,
    LobbySeatPolicy SeatPolicy = LobbySeatPolicy.ImmediateSeat, DuelQueuePolicy DuelQueuePolicy = DuelQueuePolicy.Fifo) : NodeCommand;
public sealed record LobbyList(int Offset = 0, int Limit = 16) : NodeCommand;
/// <summary>
/// Requests an immediate player seat chosen atomically by the Node. Optional
/// preferences narrow the candidate set; they never weaken lobby admission or
/// waitlist policy.
/// </summary>
public sealed record QuickPlayJoin(MatchMode? Mode = null, bool? AllowBots = null) : NodeCommand;
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
[method: JsonConstructor]
public sealed record LobbyConfigure(long ExpectedRevision, string MapKey, MatchMode Mode, int BotCount = 0,
    int? TimeLimitSeconds = null, int? PointGoal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyRulesOptions? Rules = null) : NodeCommand
{
    public LobbyConfigure(long expectedRevision, string mapKey, MatchMode mode,
        LobbyRulesOptions? rules)
        : this(expectedRevision, mapKey, mode, 0, null, null, rules) { }

    public LobbyConfigure(long expectedRevision, string mapKey, MatchMode mode, int botCount,
        LobbyRulesOptions? rules)
        : this(expectedRevision, mapKey, mode, botCount, null, null, rules) { }

    public LobbyRulesOptions NormalizeRules()
        => (Rules ?? LobbyRulesOptions.Empty).Normalize(Mode, TimeLimitSeconds, PointGoal);
}
public sealed record LobbyStart(long ExpectedRevision) : NodeCommand;
public sealed record LobbyRematch(long ExpectedRevision) : NodeCommand;
public sealed record LobbyReturn(long ExpectedRevision) : NodeCommand;
[method: JsonConstructor]
public sealed record NodeMatchHandoff(Guid MatchId, uint WireMatchId, string Host, ushort Port, string Ticket, ulong Nonce, bool Observer, Hunter Hunter,
    Guid AdmissionId = default, string AdmissionKey = "", bool UdpAuthenticationEnabled = true)
{
    // Source-level legacy/test handoffs are explicitly keyless. Production
    // Node code uses the full constructor and therefore keeps authentication
    // enabled by default; this overload prevents an omitted mode from being
    // mistaken for an authenticated handoff with missing key material.
    public NodeMatchHandoff(Guid matchId, uint wireMatchId, string host, ushort port,
        string ticket, ulong nonce, bool observer, Hunter hunter)
        : this(matchId, wireMatchId, host, port, ticket, nonce, observer, hunter,
            Guid.Empty, "", false) { }

    public void Validate()
    {
        ContractGuard.Id(MatchId);
        if (WireMatchId == 0 || Port == 0 || Nonce == 0 || String.IsNullOrWhiteSpace(Ticket)
            || (AdmissionId == Guid.Empty) != String.IsNullOrEmpty(AdmissionKey)
            || UdpAuthenticationEnabled && AdmissionId == Guid.Empty
            || !UdpAuthenticationEnabled && AdmissionId != Guid.Empty)
            throw new ArgumentException("Invalid match handoff.");
        if (AdmissionId != Guid.Empty) AdmissionKeyRules.Validate(AdmissionKey);
    }

    // AdmissionKey is intentionally omitted from diagnostics and exception text.
    public override string ToString()
        => $"NodeMatchHandoff {{ MatchId = {MatchId}, WireMatchId = {WireMatchId}, AdmissionId = {AdmissionId}, Observer = {Observer} }}";
}
public sealed record NodeMatchEnded(Guid MatchId, bool Interrupted);
/// <summary>Authenticated immutable Worker result relayed by Node without recalculation.</summary>
public sealed record NodeMatchCompletion(MatchCompletionSummary Summary);
public sealed record NodeMatchRejoin(Guid MatchId) : NodeCommand;
public sealed record NodeControlRequest(Guid RequestId, NodeCommand Command);
