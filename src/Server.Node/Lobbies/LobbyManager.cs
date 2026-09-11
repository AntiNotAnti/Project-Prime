using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Security.Cryptography;
using MphRead.Identity;
using System.Text;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Node.Lobbies.Queue;
using MphRead;
using Microsoft.Extensions.Logging;

namespace ProjectPrime.Server.Node.Lobbies;

public sealed record LobbyIdentity(Guid SessionId, Guid? PlayerId, string DisplayName, Guid? GuestSessionId = null)
{
    // Four-argument form keeps the identity tag explicit for guest callers while
    // retaining the existing (session, player, display name) account shape.
    public LobbyIdentity(Guid sessionId, Guid? playerId, Guid? guestSessionId, string displayName)
        : this(sessionId, playerId, displayName, guestSessionId) { }
    public HumanIdentityKey IdentityKey => HumanIdentityValidation.Require(PlayerId, GuestSessionId);
    public bool IsGuest => GuestSessionId.HasValue;
    public void Validate()
    {
        if (SessionId == Guid.Empty) throw new ArgumentException("Session identity is required.");
        IdentityKey.ToString();
        if (DisplayName is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(DisplayName)
            || DisplayName.Any(ch => ch < 32 || ch > 126))
            throw new ArgumentException("Invalid display name.");
    }
}
public sealed class LobbyCommandException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }

/// <summary>Serialized lightweight lobby authority. No gameplay Scene or simulation ownership.</summary>
public sealed partial class LobbyManager
{
    private sealed class Lobby(Guid id, LobbyCreate command, Guid owner)
    {
        public Guid Id = id;
        public LobbyCreate Rules = command;
        public Guid Owner = owner;
        public long Revision;
        public LobbyPhase Phase = LobbyPhase.Open;
        public string MapKey = "";
        public MatchMode Mode = MatchMode.Battle;
        public int BotCount;
        // Host rules have one owner. Legacy snapshot fields are projections
        // generated from this value and are never stored independently.
        public LobbyRulesOptions HostRules = LobbyRulesOptions.Empty;
        public Guid? MatchId;
        public Dictionary<Guid, LobbyMember> Members = [];
        public Queue<LobbyChatEntry> Chat = [];
        public LobbyWaitlist Waitlist = null!;
        public LobbySnapshot Snapshot(HumanIdentityKey? self = null,
            MapRequirement? requiredMap = null) => new(Id, Rules.Name, Rules.Visibility, Owner, Phase, Revision,
            Rules.PlayerLimit, Rules.ObserverLimit, Members.Values.ToImmutableArray(), Chat.ToImmutableArray(), MapKey, Mode, MatchId, BotCount,
            HostRules.TimeLimitSeconds, Rules.SeatPolicy, Rules.DuelQueuePolicy, Waitlist.Snapshot(self),
            HostRules.LegacyPointGoal(Mode), HostRules, requiredMap);
    }
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Lobby> _lobbies = [];
    private readonly Dictionary<Guid, Guid> _membership = [];
    private readonly Dictionary<Guid, DateTimeOffset> _sessionResumeDeadlines = [];
    public void SetSessionResumeDeadline(Guid sessionId, DateTimeOffset? deadline)
    {
        lock (_gate)
        {
            if (deadline is { } expires) _sessionResumeDeadlines[sessionId] = expires;
            else _sessionResumeDeadlines.Remove(sessionId);
        }
    }
    private void PruneExpiredSessionMembership()
    {
        var now = RoundClock.GetUtcNow();
        foreach (var session in _sessionResumeDeadlines.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            LeaveCore(session);
    }
    private readonly Dictionary<Guid, Guid> _queueSessions = [];
    private readonly Dictionary<HumanIdentityKey, Guid> _queueIdentities = [];
    private readonly int _maximumLobbies;
    private readonly int _maximumWaitlistPerLobby;
    private readonly bool _quickPlayV2Enabled;
    private readonly ILogger<LobbyManager>? _logger;
    private bool _admissionClosed;
    public void CloseAdmission() { lock (_gate) _admissionClosed = true; }
    private readonly ConcurrentDictionary<Guid, LobbySnapshot> _notifications = [];
    private readonly Channel<bool> _notificationSignal = Channel.CreateBounded<bool>(1);
    public async IAsyncEnumerable<LobbySnapshot> ReadNotifications([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var _ in _notificationSignal.Reader.ReadAllAsync(cancellationToken))
            foreach (var pair in _notifications.ToArray())
                if (_notifications.TryRemove(pair)) yield return pair.Value;
    }
    public LobbyManager(int maximumLobbies = 256, TimeProvider? clock = null,
        int maximumWaitlistPerLobby = LobbyWaitlist.DefaultMaximumEntries, TimeSpan? offerWindow = null,
        int postMatchVoteSeconds = 15, bool quickPlayV2Enabled = true, ILogger<LobbyManager>? logger = null)
    {
        if (maximumLobbies is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumLobbies));
        if (maximumWaitlistPerLobby is < 1 or > LobbyWaitlist.MaximumEntriesLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumWaitlistPerLobby));
        TimeSpan window = offerWindow ?? TimeSpan.FromSeconds(15);
        if (window < TimeSpan.FromSeconds(10) || window > TimeSpan.FromSeconds(20))
            throw new ArgumentOutOfRangeException(nameof(offerWindow), "Seat offer window must be between 10 and 20 seconds.");
        if (postMatchVoteSeconds is < 5 or > 30) throw new ArgumentOutOfRangeException(nameof(postMatchVoteSeconds));
        PostMatchVoteSeconds = postMatchVoteSeconds;
        _maximumLobbies = maximumLobbies; _maximumWaitlistPerLobby = maximumWaitlistPerLobby;
        _quickPlayV2Enabled = quickPlayV2Enabled; _logger = logger;
        RoundClock = clock ?? TimeProvider.System; OfferWindow = window;
    }
    public int PostMatchVoteSeconds { get; }
    public int MaximumWaitlistPerLobby => _maximumWaitlistPerLobby;
    public int MaxWaitlistPerLobby => _maximumWaitlistPerLobby;
    public TimeSpan OfferWindow { get; }
    public int Count { get { lock (_gate) return _lobbies.Count; } }
    public LobbySnapshot? ForSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (_membership.TryGetValue(sessionId, out var memberLobby) && _lobbies.TryGetValue(memberLobby, out var memberTarget))
                return memberTarget.Snapshot(IdentityForSession(memberTarget, sessionId),
                    RequirementFor(memberTarget));
            if (_queueSessions.TryGetValue(sessionId, out var queueLobby) && _lobbies.TryGetValue(queueLobby, out var queueTarget))
                return queueTarget.Snapshot(IdentityForSession(queueTarget, sessionId),
                    RequirementFor(queueTarget));
            return null;
        }
    }

    /// <summary>Returns sessions that should receive a lobby snapshot. The
    /// queue is intentionally included so queued-only users can keep their
    /// lobby view during reconnect grace.</summary>
    public IReadOnlyCollection<Guid> Recipients(Guid lobbyId)
    {
        lock (_gate)
        {
            if (!_lobbies.TryGetValue(lobbyId, out var lobby)) return [];
            return lobby.Members.Keys.Concat(lobby.Waitlist.ActiveEntries().Select(e => e.SessionId)).Distinct().ToArray();
        }
    }

    public LobbyWaitlistMetrics? WaitlistMetrics(Guid lobbyId)
    {
        lock (_gate) return _lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Waitlist.Metrics() : null;
    }

    /// <summary>Runs deterministic offer expiry/advancement at an injected
    /// clock boundary. The Node reaper may call this without creating a queue
    /// worker or timer per lobby.</summary>
    public void PruneWaitlists()
    {
        lock (_gate) ExpireOffersAndAdvanceAll();
    }
    public object Execute(LobbyIdentity identity, NodeCommand command)
    {
        identity.Validate();
        lock (_gate)
        {
            ExpireOffersAndAdvanceAll();
            if (TryExecuteRoundCommand(identity, command, out var roundResponse)) return roundResponse;
            switch (command)
            {
                case LobbyList list:
                    if (list.Offset < 0 || list.Limit is < 1 or > NodeControlCodec.MaximumLobbyListEntries)
                        throw Error("invalid", "Invalid page bounds.");
                    var rows = _lobbies.Values.Where(l => l.Rules.Visibility == LobbyVisibility.Public)
                        .OrderBy(l => l.Id).Skip(list.Offset).Take(list.Limit + 1).ToArray();
                    return new LobbyListSnapshot(rows.Take(list.Limit).Select(l => new LobbyListEntry(l.Id, l.Rules.Name,
                        l.Phase, l.Members.Values.Count(m => !m.Observer), l.Rules.PlayerLimit,
                        l.Members.Values.Count(m => m.Observer), l.Revision, l.Waitlist.Count, l.Rules.ObserverLimit, l.BotCount,
                        l.MapKey, l.Mode, l.HostRules.TimeLimitSeconds, l.HostRules.LegacyPointGoal(l.Mode),
                        l.HostRules.ObjectiveTimeGoalSeconds, l.Rules.SeatPolicy)).ToImmutableArray(),
                        rows.Length > list.Limit ? list.Offset + list.Limit : null);
                case LobbyCreate create:
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    RequireUnjoined(identity.SessionId);
                    if (!Text(create.Name, 64) || !Enum.IsDefined(create.Visibility)
                        || create.PlayerLimit is < 1 or > 8 || create.ObserverLimit is < 0 or > 16
                        || !Enum.IsDefined(create.SeatPolicy) || !Enum.IsDefined(create.DuelQueuePolicy))
                        throw Error("invalid", "Invalid lobby settings.");
                    if (create.DuelQueuePolicy != DuelQueuePolicy.Fifo)
                        throw Error("unsupported", "FIFO is the only implemented Duel queue policy.");
                    if (_lobbies.Count >= _maximumLobbies) throw Error("capacity", "Lobby capacity reached.");
                    var created = new Lobby(Guid.NewGuid(), create, identity.SessionId);
                    created.Waitlist = new LobbyWaitlist(_maximumWaitlistPerLobby);
                    _lobbies.Add(created.Id, created);
                    return Join(created, identity, false);
                case QuickPlayJoin quickPlay:
                    if (!_quickPlayV2Enabled) throw Error("unsupported", "Node Quick Play is disabled.");
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    RequireUnjoined(identity.SessionId);
                    if (quickPlay.Mode is { } preferredMode && !Enum.IsDefined(preferredMode))
                        throw Error("invalid", "Invalid Quick Play mode preference.");
                    return QuickPlay(identity, quickPlay);
                case LobbyJoin join:
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    RequireUnjoined(identity.SessionId);
                    if (!_lobbies.TryGetValue(join.LobbyId, out var target)) throw Error("not_found", "Lobby not found.");
                    Revision(target, join.ExpectedRevision);
                    if (target.Phase != LobbyPhase.Open) throw Error("phase", "Lobby roster is frozen.");
                    if (!join.Observer && target.Waitlist.Find(identity.IdentityKey) is not null)
                        throw Error("already_queued", "Cancel the waitlist entry before joining as a player.");
                    return Join(target, identity, join.Observer);
                case LobbyQueueJoin queueJoin:
                    return QueueJoin(identity, queueJoin);
                case LobbyQueueLeave queueLeave:
                    return QueueLeave(identity, queueLeave);
                case LobbyQueueAccept queueAccept:
                    return QueueAccept(identity, queueAccept);
                case LobbyQueueDecline queueDecline:
                    return QueueDecline(identity, queueDecline);
                case LobbyLeave leave:
                    var leaving = RequireLobby(identity.SessionId);
                    Revision(leaving, leave.ExpectedRevision);
                    var left = leaving.Id;
                    LeaveCore(identity.SessionId);
                    return new LobbyLeft(left);
                default:
                    var lobby = RequireLobby(identity.SessionId);
                    var member = lobby.Members[identity.SessionId];
                    long revision = command switch
                    {
                        LobbySetReady c => c.ExpectedRevision, LobbySelectHunter c => c.ExpectedRevision,
                        LobbyRequestTeam c => c.ExpectedRevision, LobbyChat c => c.ExpectedRevision,
                        LobbyConfigure c => c.ExpectedRevision, _ => -1
                    };
                    Revision(lobby, revision);
                    if (lobby.Phase != LobbyPhase.Open && command is not LobbyChat) throw Error("phase", "Lobby settings are frozen.");
                    switch (command)
                    {
                        case LobbyConfigure configure:
                            if (lobby.Owner != identity.SessionId) throw Error("owner", "Only the owner may configure the lobby.");
                            if (!Text(configure.MapKey, 128) || !Enum.IsDefined(configure.Mode)) throw Error("invalid", "Invalid map or mode.");
                            if (configure.BotCount < 0 || configure.BotCount + lobby.Members.Values.Count(m => !m.Observer) > lobby.Rules.PlayerLimit)
                                throw Error("invalid", "Invalid bot count or player capacity.");
                            LobbyRulesOptions normalized;
                            try
                            {
                                // Normalize before any waitlist, map, readiness,
                                // or revision state is changed. This is the one
                                // compatibility merge point for old clients.
                                normalized = configure.NormalizeRules();
                                _ = normalized.ToMatchRules(configure.Mode, configure.MapKey, lobby.Rules.PlayerLimit);
                            }
                            catch (ArgumentException ex)
                            { throw Error("invalid", ex.Message); }
                            lobby.Waitlist.DeferOffers();
                            Round(lobby.Id).AwaitingMapReadiness = false;
                            Round(lobby.Id).Resolved = null;
                            lobby.MapKey = configure.MapKey; lobby.Mode = configure.Mode;
                            lobby.BotCount = configure.BotCount; lobby.HostRules = normalized;
                            foreach (var item in lobby.Members.ToArray()) lobby.Members[item.Key] = item.Value with { Ready = false };
                            AdvanceWaitlist(lobby);
                            break;
                        case LobbySetReady ready:
                            if (member.Observer) throw Error("role", "Observers cannot ready.");
                            if (member.Ready == ready.Ready) return SnapshotFor(lobby, identity.IdentityKey);
                            lobby.Members[identity.SessionId] = member with { Ready = ready.Ready };
                            break;
                        case LobbySelectHunter hunter:
                            if (member.Observer || !Enum.IsDefined(hunter.Hunter) || hunter.Hunter > Hunter.Guardian)
                                throw Error("invalid", "Invalid hunter selection.");
                            if (member.Hunter == hunter.Hunter) return SnapshotFor(lobby, identity.IdentityKey);
                            lobby.Members[identity.SessionId] = member with { Hunter = hunter.Hunter, Ready = false };
                            break;
                        case LobbyRequestTeam team:
                            if (member.Observer || team.Team > 1) throw Error("invalid", "Invalid team.");
                            if (member.Team == team.Team) return SnapshotFor(lobby, identity.IdentityKey);
                            lobby.Members[identity.SessionId] = member with { Team = team.Team, Ready = false };
                            break;
                        case LobbyChat chat:
                            if (!Text(chat.Text, 256)) throw Error("invalid", "Invalid chat text.");
                            lobby.Chat.Enqueue(new(lobby.Revision + 1, member.SessionId, member.DisplayName, chat.Text));
                            while (lobby.Chat.Count > 16) lobby.Chat.Dequeue();
                            break;
                        default: throw Error("unsupported", "Unsupported command.");
                    }
                    return Publish(lobby);
            }
        }
    }
    public void Disconnect(Guid sessionId) { lock (_gate) LeaveCore(sessionId); }
    public MatchSpec PrepareMatch(Guid ownerSession, long expectedRevision, ContentIdentity content, NodeId nodeId, Guid incarnation)
    {
        lock (_gate)
        {
            var lobby = RequireLobby(ownerSession);
            ExpireOffersAndAdvance(lobby);
            if (_admissionClosed) throw Error("draining", "Node is draining.");
            Revision(lobby, expectedRevision);
            if (lobby.Owner != ownerSession) throw Error("owner", "Only the owner may start a match.");
            if (lobby.Phase != LobbyPhase.Open || lobby.MapKey != content.MapKey) throw Error("phase", "Lobby is not configured for this content.");
            ValidateRoundStart(lobby.Id);
            return PrepareMatchCore(lobby, content, nodeId, incarnation, requireReady: true);
        }
    }
    private MatchSpec PrepareMatchCore(Lobby lobby, ContentIdentity content, NodeId nodeId, Guid incarnation, bool requireReady)
    {
            if (requireReady)
            {
                Round(lobby.Id).Resolved = null;
                Round(lobby.Id).AwaitingMapReadiness = false;
            }
            var players = lobby.Members.Values.Where(m => !m.Observer).ToArray();
            if (players.Length == 0 || requireReady && players.Any(m => !m.Ready)) throw Error("not_ready", "All players must be ready.");
            var seats = ImmutableArray.CreateBuilder<RosterSeat>();
            foreach (var member in players)
            {
                seats.Add(ToRosterSeat((byte)seats.Count, member, SeatRole.Player));
            }
            for (int bot = 0; bot < lobby.BotCount; bot++)
                seats.Add(new((byte)seats.Count, null, null, "Bot" + (bot + 1), Hunter.Samus, (byte)((players.Length + bot) % 2), SeatRole.Bot, false));
            if (lobby.Mode.IsTeamMode() && seats.Select(s => s.Team).Distinct().Count() != 2)
                throw Error("teams", "Both teams need players.");
            // MatchSpec is immutable. Offers that have not been accepted before
            // this boundary are returned to FIFO and receive fresh offer IDs at
            // the next eligible boundary; accepting them after start is stale.
            lobby.Waitlist.DeferOffers();
            int observerSeat = 8;
            foreach (var member in lobby.Members.Values.Where(m => m.Observer))
                seats.Add(ToRosterSeat((byte)observerSeat++, member, SeatRole.Observer));
            // HostRules is immutable and was normalized at configuration time;
            // every start/rematch/continuation derives the exact same Game
            // rules from this canonical value.
            var rules = lobby.HostRules.ToMatchRules(lobby.Mode, lobby.MapKey, lobby.Rules.PlayerLimit);
            var spec = new MatchSpec(new(Guid.NewGuid()), new(lobby.Id), nodeId, incarnation,
                rules, content,
                lobby.Members.Values.Any(m => m.GuestSessionId.HasValue)
                    ? MatchTrustClass.Practice : MatchTrustClass.Community,
                null, null, seats.ToImmutable(), lobby.BotCount == 0 ? BotFillPolicy.Disabled : BotFillPolicy.FillVacancies,
                lobby.Rules.ObserverLimit > 0 ? ObserverPolicy.Allowed : ObserverPolicy.Disabled,
                ReplayPolicy.Record, TelemetryPolicy.Record,
                BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)));
            spec = ApplyRoundIdentity(lobby.Id, spec);
            spec.Validate();
            lobby.MatchId = spec.MatchId.Value; lobby.Phase = LobbyPhase.StartingMatch; Publish(lobby);
            return spec;
    }
    public bool MatchReady(MatchPlacement placement)
    {
        lock (_gate)
        {
            var lobby = _lobbies.Values.SingleOrDefault(l => l.MatchId == placement.MatchId.Value);
            if (lobby == null || lobby.Phase != LobbyPhase.StartingMatch) return false;
            lobby.Phase = LobbyPhase.InMatch; ExpireOffersAndAdvance(lobby); Publish(lobby); return true;
        }
    }
    public bool MatchEnded(MatchId matchId, bool interrupted)
    {
        lock (_gate)
        {
            var lobby = _lobbies.Values.SingleOrDefault(l => l.MatchId == matchId.Value);
            if (lobby == null || lobby.Phase is not (LobbyPhase.StartingMatch or LobbyPhase.InMatch)) return false;
            lobby.Phase = LobbyPhase.PostMatch; OnRoundEnded(lobby, interrupted); ExpireOffersAndAdvance(lobby); Publish(lobby); return true;
        }
    }
    public LobbySnapshot ReturnToLobby(Guid ownerSession, long expectedRevision)
    {
        lock (_gate)
        {
            var lobby = RequireLobby(ownerSession); Revision(lobby, expectedRevision);
            if (lobby.Owner != ownerSession) throw Error("owner", "Only the owner may reopen the lobby.");
            if (lobby.Phase != LobbyPhase.PostMatch) throw Error("phase", "Match has not ended.");
            if (!Round(lobby.Id).Options.IsEmpty) throw Error("vote_pending", "The Node ballot controls continuation.");
            ReopenCore(lobby);
            ExpireOffersAndAdvance(lobby);
            return Publish(lobby);
        }
    }
    private static void Revision(Lobby lobby, long expected)
    { if (lobby.Revision != expected) throw Error("stale_revision", "Lobby changed; use the latest snapshot."); }

    private LobbySnapshot QueueJoin(LobbyIdentity identity, LobbyQueueJoin command)
    {
        if (_admissionClosed) throw Error("draining", "Node is draining.");
        Lobby lobby = ResolveQueueLobby(identity, command.LobbyId, allowMissing: false);
        Revision(lobby, command.ExpectedRevision);
        if (lobby.Phase == LobbyPhase.Closing) throw Error("phase", "Lobby is closing.");
        if (command.RequestedTeam is > 1 || !Enum.IsDefined(command.RequestedRole))
            throw Error("invalid", "Invalid waitlist request.");
        if (command.RequestedRole != LobbyQueueRequestedRole.Player)
            throw Error("role", "The waitlist reserves player seats; use lobby.join to spectate.");
        if (FindMemberLobbyForIdentity(identity.IdentityKey) is { } memberLobby && memberLobby.Id != lobby.Id)
            throw Error("already_joined", "This identity already belongs to another lobby.");
        if (_membership.TryGetValue(identity.SessionId, out Guid sessionLobby) && sessionLobby != lobby.Id)
            throw Error("already_joined", "This session already belongs to another lobby.");
        if (_queueIdentities.TryGetValue(identity.IdentityKey, out Guid existingLobby) && existingLobby != lobby.Id)
            throw Error("queue_hop", "Leave the current waitlist before joining another.");
        if (_queueSessions.TryGetValue(identity.SessionId, out Guid existingSessionLobby))
        {
            if (existingSessionLobby != lobby.Id)
                throw Error("queue_hop", "Leave the current waitlist before joining another.");
            Entry? sessionEntry = lobby.Waitlist.Find(identity.SessionId);
            if (sessionEntry is not null)
            {
                if (sessionEntry.Identity != identity.IdentityKey)
                    throw Error("duplicate_identity", "This session already has a waitlist identity.");
                return SnapshotFor(lobby, identity.IdentityKey);
            }
        }
        LobbyMember? member = lobby.Members.Values.FirstOrDefault(m => m.IdentityKey == identity.IdentityKey);
        if (lobby.Members.TryGetValue(identity.SessionId, out LobbyMember? sessionMember)
            && sessionMember.IdentityKey != identity.IdentityKey)
            throw Error("duplicate_identity", "This session already has a lobby identity.");
        if (member is not null && member.SessionId != identity.SessionId)
            throw Error("duplicate_identity", "This human identity already belongs to a lobby session.");
        if (member is not null && !member.Observer)
            throw Error("role", "Active players cannot waitlist a second seat.");

        if (lobby.Waitlist.Find(identity.IdentityKey) is { } existing)
        {
            if (existing.SessionId != identity.SessionId)
                throw Error("duplicate_identity", "This human identity is already queued.");
            return SnapshotFor(lobby, identity.IdentityKey);
        }

        Entry entry;
        try
        {
            entry = lobby.Waitlist.Enqueue(identity.SessionId, identity.IdentityKey, identity.DisplayName,
                command.RequestedRole, command.RequestedTeam, RoundClock.GetUtcNow());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase))
        { throw Error("capacity", "Waitlist capacity reached."); }
        _queueSessions[identity.SessionId] = lobby.Id;
        _queueIdentities[identity.IdentityKey] = lobby.Id;
        AdvanceWaitlist(lobby);
        Publish(lobby);
        return SnapshotFor(lobby, identity.IdentityKey);
    }

    private LobbySnapshot QueueLeave(LobbyIdentity identity, LobbyQueueLeave command)
    {
        Lobby lobby = ResolveQueueLobby(identity, command.LobbyId, allowMissing: false);
        Revision(lobby, command.ExpectedRevision);
        Entry entry = lobby.Waitlist.Find(identity.IdentityKey) is { } queued && queued.SessionId == identity.SessionId
            ? queued : throw Error("not_queued", "This identity is not queued for this session.");
        lobby.Waitlist.RecordCancelled(entry, RoundClock.GetUtcNow());
        RemoveQueueIndexes(entry);
        AdvanceWaitlist(lobby);
        lobby.Waitlist.EvictTerminalEntries();
        Publish(lobby);
        return SnapshotFor(lobby, identity.IdentityKey);
    }

    private LobbySnapshot QueueAccept(LobbyIdentity identity, LobbyQueueAccept command)
    {
        Lobby lobby = ResolveQueueLobby(identity, command.LobbyId, allowMissing: false);
        Revision(lobby, command.ExpectedRevision);
        if (command.OfferId == Guid.Empty) throw Error("stale_offer", "The seat offer is invalid.");
        Entry entry = lobby.Waitlist.Find(identity.IdentityKey) is { } offered && offered.SessionId == identity.SessionId
            ? offered : throw Error("stale_offer", "The seat offer is absent or changed.");
        SeatReservation reservation;
        try { reservation = entry.RequireOffer(command.OfferId); }
        catch (InvalidOperationException) { throw Error("stale_offer", "The seat offer is absent or changed."); }
        if (!LobbySeatPolicyResolver.CanOffer(lobby.Phase, reservation.Policy))
            throw Error("stale_offer", "The lobby moved past this seat offer.");
        if (reservation.IsExpired(RoundClock.GetUtcNow()))
            throw Error("stale_offer", "The seat offer expired.");
        if (entry.RequestedRole != LobbyQueueRequestedRole.Player)
            throw Error("role", "Only player-seat offers can be accepted.");
        if (lobby.Members.TryGetValue(identity.SessionId, out var member) && !member.Observer)
            throw Error("already_joined", "This session already occupies a player seat.");
        if (PlayerSeatsAvailable(lobby, entry) <= 0)
            throw Error("capacity", "The player seat is no longer available.");

        if (member is not null)
        {
            lobby.Members[identity.SessionId] = member with
            {
                Observer = false,
                Ready = false,
                Team = AllocateTeam(lobby, entry.RequestedTeam)
            };
        }
        else
        {
            if (_membership.ContainsKey(identity.SessionId)) throw Error("already_joined", "Session already belongs to a lobby.");
            lobby.Members.Add(identity.SessionId, new(identity.SessionId,
                identity.PlayerId, identity.DisplayName, Hunter.Samus,
                AllocateTeam(lobby, entry.RequestedTeam), false, false, identity.GuestSessionId));
            _membership[identity.SessionId] = lobby.Id;
        }
        lobby.Waitlist.RecordAccepted(entry, RoundClock.GetUtcNow());
        RemoveQueueIndexes(entry);
        AdvanceWaitlist(lobby);
        lobby.Waitlist.EvictTerminalEntries();
        Publish(lobby);
        return SnapshotFor(lobby, identity.IdentityKey);
    }

    private LobbySnapshot QueueDecline(LobbyIdentity identity, LobbyQueueDecline command)
    {
        Lobby lobby = ResolveQueueLobby(identity, command.LobbyId, allowMissing: false);
        Revision(lobby, command.ExpectedRevision);
        if (command.OfferId == Guid.Empty) throw Error("stale_offer", "The seat offer is invalid.");
        Entry entry = lobby.Waitlist.Find(identity.IdentityKey) is { } offered && offered.SessionId == identity.SessionId
            ? offered : throw Error("stale_offer", "The seat offer is absent or changed.");
        try { entry.RequireOffer(command.OfferId); }
        catch (InvalidOperationException) { throw Error("stale_offer", "The seat offer is absent or changed."); }
        lobby.Waitlist.RecordDeclined(entry, RoundClock.GetUtcNow());
        RemoveQueueIndexes(entry);
        AdvanceWaitlist(lobby);
        lobby.Waitlist.EvictTerminalEntries();
        Publish(lobby);
        return SnapshotFor(lobby, identity.IdentityKey);
    }

    private Lobby ResolveQueueLobby(LobbyIdentity identity, Guid requestedLobbyId, bool allowMissing)
    {
        Guid lobbyId = requestedLobbyId;
        if (lobbyId == Guid.Empty)
        {
            if (_membership.TryGetValue(identity.SessionId, out Guid memberLobby)) lobbyId = memberLobby;
            else if (_queueSessions.TryGetValue(identity.SessionId, out Guid queueLobby)) lobbyId = queueLobby;
        }
        if (lobbyId == Guid.Empty || !_lobbies.TryGetValue(lobbyId, out Lobby? lobby))
        {
            if (allowMissing) throw Error("not_found", "Lobby not found.");
            throw Error("not_found", "Lobby not found.");
        }
        return lobby;
    }

    private Lobby? FindMemberLobbyForIdentity(HumanIdentityKey identity)
        => _lobbies.Values.FirstOrDefault(l => l.Members.Values.Any(m => m.IdentityKey == identity));

    private void ExpireOffersAndAdvanceAll()
    {
        foreach (Lobby lobby in _lobbies.Values.ToArray())
        {
            if (ExpireOffersAndAdvance(lobby)) Publish(lobby);
        }
    }

    private bool ExpireOffersAndAdvance(Lobby lobby)
    {
        int expired = lobby.Waitlist.ExpireOffers(RoundClock.GetUtcNow());
        foreach (Entry entry in lobby.Waitlist.Entries.Where(e => e.State == LobbyQueueEntryState.Expired).ToArray())
            RemoveQueueIndexes(entry);
        bool offered = AdvanceWaitlist(lobby);
        lobby.Waitlist.EvictTerminalEntries();
        return expired != 0 || offered;
    }

    private bool AdvanceWaitlist(Lobby lobby)
    {
        DateTimeOffset now = RoundClock.GetUtcNow();
        LobbySeatPolicy policy = LobbySeatPolicyResolver.Effective(lobby.Phase, lobby.Mode, lobby.Rules.SeatPolicy);
        if (!LobbySeatPolicyResolver.CanOffer(lobby.Phase, policy)) return false;
        int seats = PlayerSeatsAvailable(lobby);
        bool changed = false;
        foreach (Entry entry in lobby.Waitlist.QueuedEntries().Take(seats).ToArray())
        {
            Guid offerId;
            do { offerId = Guid.NewGuid(); }
            while (offerId == Guid.Empty || lobby.Waitlist.ActiveEntries().Any(e => e.OfferId == offerId));
            entry.Offer(new SeatReservation(offerId, entry.SessionId, entry.Identity, entry.QueueSequence,
                now + OfferWindow, policy));
            changed = true;
        }
        return changed;
    }

    private static int PlayerSeatsAvailable(Lobby lobby, Entry? accepted = null)
    {
        int active = lobby.Members.Values.Count(m => !m.Observer);
        int capacity = Math.Max(0, lobby.Rules.PlayerLimit - lobby.BotCount);
        int offered = lobby.Waitlist.ActiveEntries().Count(e => e.State == LobbyQueueEntryState.SeatOffered && !ReferenceEquals(e, accepted));
        return Math.Max(0, capacity - active - offered);
    }

    private static byte AllocateTeam(Lobby lobby, byte? requested)
    {
        if (!lobby.Mode.IsTeamMode()) return 0;
        int team0 = lobby.Members.Values.Count(m => !m.Observer && m.Team == 0);
        int team1 = lobby.Members.Values.Count(m => !m.Observer && m.Team == 1);
        if (lobby.BotCount > 0)
        {
            for (int bot = 0; bot < lobby.BotCount; bot++)
                if ((lobby.Members.Values.Count(m => !m.Observer) + bot) % 2 == 0) team0++; else team1++;
        }
        byte preferred = requested is 0 or 1 ? requested.Value : (byte)0;
        if (team0 == team1) return preferred;
        return (byte)(team0 < team1 ? 0 : 1);
    }

    private static HumanIdentityKey? IdentityForSession(Lobby lobby, Guid sessionId)
    {
        if (lobby.Members.TryGetValue(sessionId, out var member)) return member.IdentityKey;
        return lobby.Waitlist.Find(sessionId)?.Identity;
    }

    private LobbySnapshot SnapshotFor(Lobby lobby, HumanIdentityKey identity)
        => lobby.Snapshot(identity, RequirementFor(lobby));

    private MapRequirement? RequirementFor(Lobby lobby)
        => String.IsNullOrWhiteSpace(lobby.MapKey) ? null : ContentCatalog?.RequiredMap(lobby.MapKey);

    private void RemoveQueueIndexes(Entry entry)
    {
        if (_queueSessions.TryGetValue(entry.SessionId, out Guid lobbyId) &&
            _lobbies.TryGetValue(lobbyId, out Lobby? lobby) && lobby.Waitlist.Find(entry.Identity) is null)
            _queueSessions.Remove(entry.SessionId);
        if (_queueIdentities.TryGetValue(entry.Identity, out lobbyId) &&
            _lobbies.TryGetValue(lobbyId, out lobby) && lobby.Waitlist.Find(entry.Identity) is null)
            _queueIdentities.Remove(entry.Identity);
    }

    private LobbySnapshot Join(Lobby lobby, LobbyIdentity identity, bool observer)
    {
        identity.Validate();
        if (FindMemberLobbyForIdentity(identity.IdentityKey) is { } existing)
            throw Error(existing.Id == lobby.Id ? "duplicate_identity" : "already_joined",
                "This human identity already belongs to a lobby session.");
        if (_queueIdentities.ContainsKey(identity.IdentityKey) && !observer)
            throw Error("already_queued", "Cancel the waitlist entry before joining as a player.");
        int count = lobby.Members.Values.Count(m => m.Observer == observer);
        int capacity = observer ? lobby.Rules.ObserverLimit : lobby.Rules.PlayerLimit - lobby.BotCount;
        if (!observer)
            capacity -= lobby.Waitlist.ActiveEntries().Count(entry => entry.State == LobbyQueueEntryState.SeatOffered);
        if (count >= capacity) throw Error("capacity", "Lobby role capacity reached.");
        lobby.Members.Add(identity.SessionId, new(identity.SessionId, identity.PlayerId, identity.DisplayName, Hunter.Samus, 0, false, observer, identity.GuestSessionId));
        _membership.Add(identity.SessionId, lobby.Id);
        return Publish(lobby);
    }

    private LobbySnapshot QuickPlay(LobbyIdentity identity, QuickPlayJoin command)
    {
        // Quick Play is join-now only. Any active queue entry or seat offer
        // owns the next available seat, so such a lobby is never eligible.
        Lobby[] candidates = _lobbies.Values
            .Where(lobby => lobby.Rules.Visibility == LobbyVisibility.Public
                && lobby.Phase == LobbyPhase.Open
                && lobby.Waitlist.Count == 0
                && PlayerSeatsAvailable(lobby) > 0
                && (command.Mode is null || lobby.Mode == command.Mode)
                && (command.AllowBots is not false || lobby.BotCount == 0))
            .OrderByDescending(lobby => lobby.Members.Values.Count(member => !member.Observer))
            .ThenBy(lobby => lobby.BotCount)
            .ThenBy(lobby => lobby.Waitlist.Count)
            .ThenByDescending(lobby => lobby.Members.Values.Count(member => !member.Observer && member.Ready))
            .ThenBy(lobby => lobby.Id)
            .ToArray();

        if (candidates.Length == 0)
        {
            _logger?.LogInformation("Quick Play found no immediate seat among {CandidateCount} candidates", 0);
            throw Error("no_match", "No public lobby has an immediate player seat.");
        }

        Lobby selected = candidates[0];
        _logger?.LogInformation(
            "Quick Play considered {CandidateCount} lobbies and selected {LobbyId}: humans={Humans}, bots={Bots}, waitlist={Waitlist}, ready={Ready}",
            candidates.Length, selected.Id,
            selected.Members.Values.Count(member => !member.Observer), selected.BotCount,
            selected.Waitlist.Count, selected.Members.Values.Count(member => !member.Observer && member.Ready));
        LobbySnapshot joined = Join(selected, identity, observer: false);
        _logger?.LogInformation("Quick Play joined lobby {LobbyId} at revision {Revision}", selected.Id, joined.Revision);
        return joined;
    }

    private static RosterSeat ToRosterSeat(byte seatId, LobbyMember member, SeatRole role)
    {
        HumanIdentityKey key = member.IdentityKey;
        return new(seatId,
            key.Kind == HumanIdentityKind.Registered ? new PlayerId(key.Value) : null,
            key.Kind == HumanIdentityKind.Guest ? key.Value : null,
            member.DisplayName, member.Hunter, member.Team, role, false);
    }
    private void LeaveCore(Guid sessionId)
    {
        _sessionResumeDeadlines.Remove(sessionId);
        Lobby? queuedLobby = null;
        bool hadQueue = _queueSessions.TryGetValue(sessionId, out Guid queueLobbyId)
            && _lobbies.TryGetValue(queueLobbyId, out queuedLobby);
        if (hadQueue && queuedLobby is not null && queuedLobby.Waitlist.Find(sessionId) is { } queued)
        {
            queuedLobby.Waitlist.RecordCancelled(queued, RoundClock.GetUtcNow());
            RemoveQueueIndexes(queued);
            queuedLobby.Waitlist.EvictTerminalEntries();
        }
        if (!_membership.Remove(sessionId, out Guid lobbyId))
        {
            if (hadQueue && queuedLobby is not null && queuedLobby.Members.Count > 0)
            {
                AdvanceWaitlist(queuedLobby); Publish(queuedLobby);
            }
            return;
        }
        var lobby = _lobbies[lobbyId];
        lobby.Members.Remove(sessionId);
        if (lobby.Members.Count == 0)
        {
            foreach (Entry entry in lobby.Waitlist.ActiveEntries().ToArray())
            {
                lobby.Waitlist.RecordCancelled(entry, RoundClock.GetUtcNow());
                RemoveQueueIndexes(entry);
            }
            _queueSessions.Where(pair => pair.Value == lobbyId).Select(pair => pair.Key).ToArray()
                .ToList().ForEach(id => _queueSessions.Remove(id));
            _queueIdentities.Where(pair => pair.Value == lobbyId).Select(pair => pair.Key).ToArray()
                .ToList().ForEach(id => _queueIdentities.Remove(id));
            OnLobbyRemoved(lobbyId); _lobbies.Remove(lobbyId); _notifications.TryRemove(lobbyId, out _); return;
        }
        if (lobby.Owner == sessionId) lobby.Owner = lobby.Members.Keys.First();
        ResolveBallot(lobby, Round(lobby.Id));
        ExpireOffersAndAdvance(lobby);
        Publish(lobby);
    }
    private LobbySnapshot Publish(Lobby lobby)
    {
        lobby.Revision++;
        var snapshot = lobby.Snapshot(requiredMap: RequirementFor(lobby));
        // One latest snapshot per bounded lobby, one wake signal. A slow network
        // consumer cannot run callbacks under the authority lock or grow a queue.
        _notifications[lobby.Id] = snapshot;
        _notificationSignal.Writer.TryWrite(true);
        return snapshot;
    }
    private Lobby RequireLobby(Guid sessionId) => _membership.TryGetValue(sessionId, out var id) ? _lobbies[id] : throw Error("not_joined", "Join a lobby first.");
    private void RequireUnjoined(Guid id) { if (_membership.ContainsKey(id)) throw Error("already_joined", "Leave the current lobby first."); }
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl) && Encoding.UTF8.GetByteCount(text) <= maximum;
    private static LobbyCommandException Error(string code, string message) => new(code, message);
}
