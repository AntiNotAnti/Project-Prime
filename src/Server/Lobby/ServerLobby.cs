using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Identity;

namespace MphRead.Mods.Network;

/// <summary>
/// Server-owned lobby/session authority. It deliberately does not own an active
/// MatchRuntime or gameplay entities.
/// </summary>
public sealed class ServerLobby
{
    public const uint DefaultReconnectGraceTicks = 30 * 60;

    private readonly LobbyAuthority _authority = new();
    private readonly Dictionary<ulong, byte> _slots = new();
    private readonly Dictionary<ulong, LobbyReconnectReservation> _reservations = new();
    private readonly Dictionary<ulong, LobbyPeerProjection> _projections = new();
    private readonly Dictionary<ulong, ulong> _senderIdentities = new();
    private readonly Func<string, bool> _mapAllowed;
    private readonly Func<MatchMode, bool> _modeAllowed;
    private readonly Func<string, MatchMode, bool> _selectionAllowed;
    private uint _nextReservationExpiry;
    private bool _hasReservationExpiry;
    private int _botMinimumWhenEnabled;
    private MatchRules? _pendingStart;

    public LobbyRuntime Runtime { get; }
    public LobbyPolicy Policy => Runtime.Policy;
    public bool AdmissionOpen => Runtime.Phase == LobbyPhase.Open;
    public bool TournamentRosterLocked { get; set; }
    public bool TournamentStartAllowed { get; set; } = true;
    public Func<MatchRules, bool>? StartGate { get; set; }
    public MatchSummaryPacket? LastMatchSummary { get; private set; }
    public uint ReconnectGraceTicks { get; }
    public BotFillPolicy BotPolicy { get; private set; }

    public ServerLobby(uint sessionId, LobbyPolicy policy, MatchRules initialRules,
        Func<string, bool>? mapAllowed = null, Func<MatchMode, bool>? modeAllowed = null,
        Func<string, MatchMode, bool>? selectionAllowed = null,
        uint reconnectGraceTicks = DefaultReconnectGraceTicks, BotFillPolicy? botPolicy = null)
    {
        if (reconnectGraceTicks == 0 || reconnectGraceTicks >= 0x80000000u)
            throw new ArgumentOutOfRangeException(nameof(reconnectGraceTicks));
        Runtime = new LobbyRuntime(sessionId, policy, initialRules);
        _mapAllowed = mapAllowed ?? (map => String.Equals(map, initialRules.RoomKey, StringComparison.OrdinalIgnoreCase));
        _modeAllowed = modeAllowed ?? (_ => true);
        _selectionAllowed = selectionAllowed ?? ((map, mode) => _mapAllowed(map) && _modeAllowed(mode));
        ReconnectGraceTicks = reconnectGraceTicks;
        botPolicy ??= new BotFillPolicy();
        botPolicy.Validate(initialRules.MaxPlayers);
        _botMinimumWhenEnabled = botPolicy.MinimumParticipants == 0
            ? initialRules.MaxPlayers : botPolicy.MinimumParticipants;
        BotPolicy = BotsForbidden(initialRules) ? botPolicy with { MinimumParticipants = 0 } : botPolicy;
    }

    public LobbyAdmissionResult AddPeer(ServerPeer peer, uint tick = 0, bool admin = false,
        byte starTier = 0, ulong previousConnectionId = 0, bool reconnectAuthorized = false,
        bool? hostAuthorized = null)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return Admit(new LobbyAdmissionRequest(peer.Connection.Id, peer.PlayerId, peer.Name, peer.Hunter,
            peer.IsObserver, peer.IsBot, admin, Ping(peer), starTier,
            peer.IsObserver ? byte.MaxValue : peer.TeamIndex, previousConnectionId, reconnectAuthorized,
            hostAuthorized), tick);
    }

    public LobbyAdmissionResult Admit(in LobbyAdmissionRequest request, uint tick = 0)
    {
        ExpireReservations(tick);
        if (!LobbyAdmission.IsValid(request, out string reason))
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Invalid, reason);
        if (request.Bot && BotsForbidden(Runtime.Draft.Current))
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Invalid,
                "Bots are disabled in Ranked and Duel lobbies.");
        if (Runtime.Draft.Current.RankingEligibility == RankingEligibility.VerifiedServerOnly
            && !request.Observer && !request.PlayerId.HasValue)
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Invalid,
                "Ranked lobbies require a verified player identity.");
        if (Runtime.TryFind(request.ConnectionId, out _))
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Conflict, "The connection is already admitted.");
        if (!AdmissionOpen)
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.WrongPhase, "Lobby admission is closed while a match is starting.");

        LobbyReconnectReservation reservation = default;
        bool reconnecting = request.PreviousConnectionId != 0
            && _reservations.TryGetValue(request.PreviousConnectionId, out reservation);
        if (reconnecting && !LobbyAdmission.ReconnectMatches(request, reservation))
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Conflict, "Reconnect proof did not match the reserved lobby member.");

        byte slot;
        LobbyPlayer player;
        ulong senderIdentity;
        if (reconnecting)
        {
            slot = reservation.Slot;
            LobbyPlayer prior = reservation.Player;
            bool host = Runtime.HostConnectionId == 0 && !prior.Observer && !prior.Bot;
            player = prior with
            {
                PlayerId = request.PlayerId,
                ConnectionId = request.ConnectionId,
                DisplayName = request.DisplayName,
                Host = host,
                Admin = prior.Admin || request.Admin,
                PingMs = request.PingMs,
                StarTier = request.StarTier,
                Loading = false,
                DisconnectedGrace = false
            };
            if (!_senderIdentities.TryGetValue(request.PreviousConnectionId, out senderIdentity))
                return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Conflict,
                    "The reconnect sender identity is no longer active.");
        }
        else
        {
            if (!TryAllocateSlot(request.Observer, out slot))
                return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Capacity, "The lobby is full.");
            byte team = request.Observer ? byte.MaxValue : ResolveJoiningTeam(slot, request.RequestedTeam);
            bool host = request.HostAuthorized
                ?? LobbyHostPolicy.ShouldAssignInitialHost(Runtime, request);
            player = new LobbyPlayer(request.PlayerId, request.ConnectionId, request.DisplayName,
                request.Hunter, team, request.Bot, request.Observer, request.Bot,
                host, request.Admin,
                request.PingMs, request.StarTier);
            do { senderIdentity = NetConnection.NewIdentity(); }
            while (senderIdentity == 0 || _senderIdentities.ContainsValue(senderIdentity));
        }
        LobbyPlayer disconnected = default;
        bool removedReservation = reconnecting
            && Runtime.TryFind(request.PreviousConnectionId, out disconnected)
            && disconnected.DisconnectedGrace
            && Runtime.TryRemove(request.PreviousConnectionId);
        if (reconnecting && !removedReservation)
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Conflict, "The reconnect reservation is no longer active.");
        if (!Runtime.TryAdd(player))
        {
            if (removedReservation) Runtime.TryAdd(disconnected);
            return LobbyAdmissionResult.Reject(LobbyAdmissionCode.Conflict, "The server rejected the lobby member state.");
        }

        if (reconnecting)
        {
            _reservations.Remove(request.PreviousConnectionId);
            _slots.Remove(request.PreviousConnectionId);
            _senderIdentities.Remove(request.PreviousConnectionId);
        }
        _slots[request.ConnectionId] = slot;
        _senderIdentities[request.ConnectionId] = senderIdentity;
        if (!request.Bot) _projections[request.ConnectionId] = new LobbyPeerProjection();
        return new(reconnecting ? LobbyAdmissionCode.Reconnected : LobbyAdmissionCode.Accepted,
            slot, reconnecting ? "Lobby session restored." : "Lobby admission accepted.");
    }

    public bool RemovePeer(ServerPeer peer, uint tick, bool reserveReconnect = true)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return Remove(peer.Connection.Id, tick, reserveReconnect);
    }

    public bool Remove(ulong connectionId, uint tick, bool reserveReconnect = true)
    {
        if (!Runtime.TryFind(connectionId, out LobbyPlayer player)) return false;
        byte slot = _slots.TryGetValue(connectionId, out byte assigned) ? assigned : byte.MaxValue;
        if (reserveReconnect && !player.Bot && !player.Observer)
        {
            if (!Runtime.TrySetConnectionStatus(connectionId, loading: false, disconnectedGrace: true)
                || !Runtime.TryFind(connectionId, out LobbyPlayer disconnected)) return false;
            uint expires = unchecked(tick + ReconnectGraceTicks);
            _reservations[connectionId] = new(disconnected, slot, expires);
            if (!_hasReservationExpiry || Sequence32.IsNewer(_nextReservationExpiry, expires))
            {
                _nextReservationExpiry = expires;
                _hasReservationExpiry = true;
            }
        }
        else
        {
            _slots.Remove(connectionId);
            _senderIdentities.Remove(connectionId);
            if (!Runtime.TryRemove(connectionId)) return false;
        }
        _projections.Remove(connectionId);
        _authority.Forget(connectionId);
        return true;
    }

    public LobbyCommandResult ReceiveRequest(ServerPeer peer, in LobbyRequestPacket request)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return ReceiveRequest(peer.Connection.Id, request);
    }

    public LobbyCommandResult ReceiveRequest(ulong connectionId, in LobbyRequestPacket request)
    {
        try
        {
            Span<byte> canonical = stackalloc byte[LobbyRequestPacket.Size];
            request.Write(canonical);
        }
        catch (ArgumentException exception)
        {
            return Result(request.RequestId, LobbyFeedbackCode.InvalidRequest, exception.Message);
        }
        LobbyPermissions permission = PermissionFor(request.Type);
        bool needsOpen = request.Type is not (LobbyRequestType.ReturnToLobby or LobbyRequestType.Rematch);
        LobbyAuthorityDecision decision = _authority.Validate(Runtime, connectionId, request.SessionId,
            request.Revision, request.RequestId, permission, needsOpen);
        if (!decision.Accepted) return Result(request.RequestId, decision.Code, decision.Message);
        if (!Runtime.TryFind(connectionId, out LobbyPlayer actor))
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted, "The sender is not a lobby member.");

        try
        {
            return request.Type switch
            {
                LobbyRequestType.SetReady => UpdatePlayer(request, actor with { Ready = request.Value != 0 }),
                LobbyRequestType.SelectHunter => SelectHunter(request, actor),
                LobbyRequestType.RequestTeam => RequestTeam(request, actor),
                LobbyRequestType.SetMap => SetMap(request),
                LobbyRequestType.SetMode => SetMode(request),
                LobbyRequestType.SetRule => SetRule(request),
                LobbyRequestType.SetBotFillEnabled or LobbyRequestType.SetBotMinimumParticipants
                    or LobbyRequestType.SetBotSkill => SetBotPolicy(request),
                LobbyRequestType.StartMatch => Start(request, connectionId, force: request.Value != 0,
                    LobbyCommandAction.StartMatch),
                LobbyRequestType.ReturnToLobby => ReturnToLobby(request),
                LobbyRequestType.Rematch => Start(request, connectionId, force: false, LobbyCommandAction.Rematch),
                _ => Result(request.RequestId, LobbyFeedbackCode.InvalidRequest, "Unknown lobby request.")
            };
        }
        catch (ArgumentException exception)
        {
            return Result(request.RequestId, LobbyFeedbackCode.Conflict, exception.Message);
        }
    }

    public LobbyChatDispatch ReceiveChat(ServerPeer peer, in LobbyChatRequestPacket request)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return ReceiveChat(peer.Connection.Id, request);
    }

    public LobbyChatDispatch ReceiveChat(ulong connectionId, in LobbyChatRequestPacket request)
    {
        try
        {
            Span<byte> canonical = stackalloc byte[LobbyChatRequestPacket.Size];
            request.Write(canonical);
        }
        catch (ArgumentException exception)
        {
            return ChatResult(request.RequestId, LobbyFeedbackCode.InvalidRequest, exception.Message);
        }
        LobbyAuthorityDecision decision = _authority.Validate(Runtime, connectionId, request.SessionId,
            request.Revision, request.RequestId, LobbyPermissions.None, openPhaseRequired: false);
        if (!decision.Accepted) return ChatResult(request.RequestId, decision.Code, decision.Message);
        if (!Runtime.TryFind(connectionId, out LobbyPlayer speaker))
            return ChatResult(request.RequestId, LobbyFeedbackCode.NotPermitted, "The sender is not a lobby member.");
        if (request.Scope == ChatScope.Match)
            return ChatResult(request.RequestId, LobbyFeedbackCode.WrongPhase, "Match chat is unavailable in the lobby.");
        if (request.Scope == ChatScope.Team && (speaker.Observer || !Runtime.Draft.Current.Teams))
            return ChatResult(request.RequestId, LobbyFeedbackCode.NotPermitted, "Team chat requires an active player in a team mode.");

        byte slot = speaker.Observer ? byte.MaxValue : _slots[connectionId];
        if (!_senderIdentities.TryGetValue(connectionId, out ulong senderIdentity))
            return ChatResult(request.RequestId, LobbyFeedbackCode.Conflict,
                "The server chat identity is unavailable.");
        LobbyChatSenderFlags flags = (speaker.Observer ? LobbyChatSenderFlags.Observer : 0)
            | (speaker.Host ? LobbyChatSenderFlags.Host : 0) | (speaker.Admin ? LobbyChatSenderFlags.Admin : 0);
        var chat = new LobbyChatPacket(Runtime.SessionId, Runtime.Revision, request.RequestId,
            connectionId, senderIdentity, slot, request.Scope, flags, speaker.DisplayName, request.Text);
        var recipients = ImmutableArray.CreateBuilder<ulong>();
        foreach (LobbyPlayer member in Runtime.Players)
            if (request.Scope != ChatScope.Team || member.Team == speaker.Team) recipients.Add(member.ConnectionId);
        if (request.Scope == ChatScope.Lobby)
            foreach (LobbyPlayer observer in Runtime.Observers) recipients.Add(observer.ConnectionId);
        return new(new LobbyFeedbackPacket(Runtime.SessionId, Runtime.Revision, request.RequestId,
            LobbyFeedbackCode.None, ""), chat, recipients.ToImmutable());
    }

    /// <summary>Returns only the newest projection until the caller confirms reliable admission.</summary>
    public bool PendingSnapshot(ulong connectionId, out LobbySnapshotPacket? snapshot)
    {
        snapshot = null;
        if (!_projections.TryGetValue(connectionId, out LobbyPeerProjection? state)
            || state.PublishedRevision == Runtime.Revision) return false;
        snapshot = CreateSnapshot(connectionId);
        return true;
    }

    public void MarkSnapshotPublished(ulong connectionId, uint revision)
    {
        if (_projections.TryGetValue(connectionId, out LobbyPeerProjection? state)
            && (revision == Runtime.Revision || Sequence32.IsNewer(Runtime.Revision, revision)))
            state.PublishedRevision = revision;
    }

    public bool PendingMatchSummary(ulong connectionId, out MatchSummaryPacket? summary)
    {
        summary = LastMatchSummary;
        return summary != null && _projections.TryGetValue(connectionId, out LobbyPeerProjection? state)
            && state.PublishedSummaryMatchId != summary.MatchId;
    }

    public void MarkMatchSummaryPublished(ulong connectionId, uint matchId)
    {
        if (_projections.TryGetValue(connectionId, out LobbyPeerProjection? state)
            && LastMatchSummary?.MatchId == matchId) state.PublishedSummaryMatchId = matchId;
    }

    public void ResetPublication(ulong connectionId)
    {
        if (_projections.TryGetValue(connectionId, out LobbyPeerProjection? state))
        {
            state.PublishedRevision = 0;
            state.PublishedSummaryMatchId = 0;
        }
    }

    public bool TryConsumeStart(out MatchRules? frozenRules)
    {
        frozenRules = _pendingStart;
        _pendingStart = null;
        return frozenRules != null;
    }

    /// <summary>Server/admin start seam for an explicit force confirmation.</summary>
    public LobbyStartDecision CanStart(ulong connectionId, bool force = false)
        => LobbyStartPolicy.Evaluate(Runtime, connectionId, force, TournamentStartAllowed, StartGate);

    public bool TryStart(ulong connectionId, bool force, out MatchRules? frozenRules)
    {
        frozenRules = null;
        LobbyStartDecision decision = CanStart(connectionId, force);
        if (!decision.Allowed || !Runtime.TryBeginStarting(connectionId, force) || !Runtime.TryLock()) return false;
        frozenRules = _pendingStart = Runtime.Draft.Freeze();
        return true;
    }

    /// <summary>Queues the same authoritative transition for trusted tournament control.</summary>
    public bool TryStartAsServer(out MatchRules? frozenRules)
    {
        frozenRules = null;
        LobbyStartDecision decision = LobbyStartPolicy.EvaluateServer(Runtime, StartGate);
        if (!decision.Allowed || !Runtime.TryBeginStartingByServer() || !Runtime.TryLock()) return false;
        frozenRules = _pendingStart = Runtime.Draft.Freeze();
        return true;
    }

    public bool EnterAfterMatch(MatchSummaryPacket summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.SessionId != Runtime.SessionId || summary.MatchId == 0) return false;
        if (LastMatchSummary != null && !Sequence32.IsNewer(summary.MatchId, LastMatchSummary.MatchId)) return false;
        Runtime.TrySetCompletedMatch(summary.MatchId);
        _pendingStart = null;
        if (LobbyStartPolicy.UsesLobbyAfterMatch(Policy.Kind))
        {
            Runtime.TryReopen();
            ClearHumanReady();
        }
        LastMatchSummary = summary with { LobbyRevision = Runtime.Revision };
        return true;
    }

    public bool EnterAfterMatch(MatchResult result, MatchSummaryFlags flags = MatchSummaryFlags.None,
        MatchReportV1? report = null)
        => EnterAfterMatch(LobbyMatchSummary.Create(Runtime.SessionId, Runtime.Revision, result, flags, report));

    /// <summary>VoteLobbyHold migration seam when a result summary is retained elsewhere.</summary>
    public bool EnterIntermissionLobby(uint completedMatchId)
    {
        if (!LobbyStartPolicy.UsesLobbyAfterMatch(Policy.Kind) || completedMatchId == 0) return false;
        Runtime.TrySetCompletedMatch(completedMatchId);
        bool changed = Runtime.TryReopen();
        ClearHumanReady();
        _pendingStart = null;
        return changed || Runtime.LastCompletedMatchId == completedMatchId;
    }

    /// <summary>Applies a map-rotation or trusted-operator selection before clients edit the draft.</summary>
    public bool TrySetServerRules(MatchRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!_mapAllowed(rules.RoomKey) || !_modeAllowed(rules.Mode)
            || !_selectionAllowed(rules.RoomKey, rules.Mode)) return false;
        bool changed = Runtime.TrySetRules(rules);
        if (changed)
        {
            if (BotsForbidden(rules) && BotPolicy.MinimumParticipants != 0)
                BotPolicy = BotPolicy with { MinimumParticipants = 0 };
            ClearHumanReady();
        }
        return changed || Runtime.Draft.Current == rules;
    }

    public void SyncBots(ReadOnlySpan<BotParticipant?> bots)
    {
        Span<ulong> present = stackalloc ulong[LobbyRuntime.MaximumPlayers];
        int presentCount = 0;
        bool botsForbidden = BotsForbidden(Runtime.Draft.Current);
        foreach (BotParticipant? bot in bots)
        {
            if (bot == null || botsForbidden) continue;
            present[presentCount++] = bot.Identity;
            if (Runtime.TryFind(bot.Identity, out LobbyPlayer current))
            {
                var updated = current with { Hunter = bot.Hunter, Team = bot.TeamIndex, Ready = true };
                Runtime.TryUpdate(updated);
            }
            else if (AdmissionOpen)
            {
                Admit(new LobbyAdmissionRequest(bot.Identity, null, bot.Name, bot.Hunter,
                    Bot: true, RequestedTeam: bot.TeamIndex));
            }
        }
        for (int index = Runtime.Players.Count - 1; index >= 0; index--)
        {
            LobbyPlayer player = Runtime.Players[index];
            if (player.Bot && !present[..presentCount].Contains(player.ConnectionId))
                Remove(player.ConnectionId, 0, reserveReconnect: false);
        }
    }

    public LobbySnapshotPacket CreateSnapshot(ulong recipientConnectionId = 0)
    {
        var members = ImmutableArray.CreateBuilder<LobbySnapshotMember>(Runtime.Players.Count + Runtime.Observers.Count);
        foreach (LobbyPlayer player in Runtime.Players)
            members.Add(ToSnapshotMember(player, _slots[player.ConnectionId]));
        foreach (LobbyPlayer observer in Runtime.Observers)
            members.Add(ToSnapshotMember(observer, byte.MaxValue));
        return new(Runtime.SessionId, Runtime.Revision, Runtime.Phase, Runtime.Policy.Kind,
            Runtime.Policy.ReadyRequired, Runtime.Policy.MinimumPlayers, Runtime.Policy.HostMayForceStart,
            Runtime.PermissionsFor(recipientConnectionId), BotPolicy.MinimumParticipants != 0,
            (byte)BotPolicy.MinimumParticipants, (byte)BotPolicy.Skill, Runtime.LastCompletedMatchId,
            Runtime.Draft.Current, members.MoveToImmutable());
    }

    public void RefreshPeer(ServerPeer peer, bool admin = false, byte starTier = 0)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (!Runtime.TryFind(peer.Connection.Id, out LobbyPlayer player)) return;
        Runtime.TryUpdate(player with
        {
            PingMs = Ping(peer),
            Admin = player.Admin || admin,
            StarTier = starTier
        });
        Runtime.TrySetConnectionStatus(peer.Connection.Id,
            loading: peer.Connection.State == NetConnectionState.Loading,
            disconnectedGrace: false);
    }

    public void ExpireReservations(uint tick)
    {
        if (!_hasReservationExpiry
            || tick != _nextReservationExpiry && !Sequence32.IsNewer(tick, _nextReservationExpiry)) return;
        foreach ((ulong connectionId, LobbyReconnectReservation reservation) in _reservations.ToArray())
            if (tick == reservation.ExpiresAtTick || Sequence32.IsNewer(tick, reservation.ExpiresAtTick))
            {
                _reservations.Remove(connectionId);
                _slots.Remove(connectionId);
                _senderIdentities.Remove(connectionId);
                Runtime.TryRemove(connectionId);
            }
        _hasReservationExpiry = false;
        uint nearest = UInt32.MaxValue;
        foreach (LobbyReconnectReservation reservation in _reservations.Values)
        {
            uint distance = unchecked(reservation.ExpiresAtTick - tick);
            if (!_hasReservationExpiry || distance < nearest)
            {
                nearest = distance;
                _nextReservationExpiry = reservation.ExpiresAtTick;
                _hasReservationExpiry = true;
            }
        }
    }

    private LobbyCommandResult SelectHunter(in LobbyRequestPacket request, LobbyPlayer actor)
    {
        if (request.Value < 0 || request.Value > (int)Hunter.Guardian)
            return Result(request.RequestId, LobbyFeedbackCode.InvalidRequest, "Select a concrete supported Hunter.");
        return UpdatePlayer(request, actor with { Hunter = (Hunter)request.Value, Ready = false });
    }

    private LobbyCommandResult RequestTeam(in LobbyRequestPacket request, LobbyPlayer actor)
    {
        if (!Runtime.Draft.Current.Teams || request.Value is < 0 or > 1)
            return Result(request.RequestId, LobbyFeedbackCode.Unsupported, "Manual teams are available only in team modes.");
        if (ConfigurationLocked || Runtime.Draft.Current.TeamBalancePolicy == TeamBalancePolicy.Locked)
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted, "Tournament or ruleset team assignments are locked.");
        return UpdatePlayer(request, actor with { Team = (byte)request.Value, Ready = false });
    }

    private LobbyCommandResult SetMap(in LobbyRequestPacket request)
    {
        if (ConfigurationLocked)
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted, "Tournament lobby settings are locked.");
        if (!_mapAllowed(request.Text)
            || !_selectionAllowed(request.Text, Runtime.Draft.Current.Mode))
            return Result(request.RequestId, LobbyFeedbackCode.Unsupported,
                "That map and mode combination is unavailable on this server.");
        bool changed = Runtime.TrySetSelection(new LobbySelection(request.Text, Runtime.Draft.Current.Mode));
        if (changed) ClearHumanReady();
        return Mutation(request, changed);
    }

    private LobbyCommandResult SetMode(in LobbyRequestPacket request)
    {
        if (ConfigurationLocked)
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted, "Tournament lobby settings are locked.");
        var mode = (MatchMode)request.Value;
        if (!_modeAllowed(mode)
            || !_selectionAllowed(Runtime.Draft.Current.RoomKey, mode))
            return Result(request.RequestId, LobbyFeedbackCode.Unsupported,
                "That map and mode combination is unavailable on this server.");
        bool changed = Runtime.TrySetSelection(new LobbySelection(Runtime.Draft.Current.RoomKey, mode));
        if (changed)
        {
            AssignTeamsForMode();
            ClearHumanReady();
        }
        return Mutation(request, changed);
    }

    private LobbyCommandResult SetRule(in LobbyRequestPacket request)
    {
        if (ConfigurationLocked)
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted, "Tournament lobby settings are locked.");
        MatchRules rules = Runtime.Draft.Current;
        MatchRules changed = request.Rule switch
        {
            LobbyRuleField.MaxPlayers => rules.With(maxPlayers: request.Value),
            LobbyRuleField.TimeLimitSeconds => request.Value == 0
                ? rules.With(clearTimeLimit: true) : rules.With(timeLimit: TimeSpan.FromSeconds(request.Value)),
            LobbyRuleField.ScoreGoal => rules.With(scoreGoal: request.Value),
            LobbyRuleField.ObjectiveTimeSeconds => rules.With(objectiveTimeGoal: TimeSpan.FromSeconds(request.Value)),
            LobbyRuleField.StartingLives => rules.With(startingLives: request.Value),
            LobbyRuleField.FriendlyFire => rules.With(friendlyFire: request.Value != 0),
            LobbyRuleField.AffinityWeapons => rules.With(affinityWeapons: request.Value != 0),
            LobbyRuleField.PlayerRadar => rules.With(playerRadar: request.Value != 0,
                radarPolicy: request.Value != 0 ? RadarPolicy.Enabled : RadarPolicy.Disabled),
            LobbyRuleField.OctolithReset => rules.With(octolithReset: request.Value != 0),
            LobbyRuleField.DamageLevel => rules.With(damageLevel: request.Value),
            LobbyRuleField.SpawnPolicy => rules.With(spawnPolicy: (SpawnPolicy)request.Value),
            LobbyRuleField.OvertimePolicy => rules.With(overtimePolicy: (OvertimePolicy)request.Value),
            LobbyRuleField.LateJoinPolicy => rules.With(lateJoinPolicy: (LateJoinPolicy)request.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported lobby rule field.")
        };
        if (changed.MaxPlayers < Runtime.Players.Count
            || _slots.Any(entry => entry.Value != byte.MaxValue && entry.Value >= changed.MaxPlayers)
            || _reservations.Values.Any(value => !value.Player.Observer && value.Slot >= changed.MaxPlayers))
            return Result(request.RequestId, LobbyFeedbackCode.Capacity, "Remove players before lowering the lobby capacity.");
        if (changed.MaxPlayers < BotPolicy.MinimumParticipants)
            return Result(request.RequestId, LobbyFeedbackCode.Capacity,
                "Lower the bot fill target before lowering the lobby capacity.");
        bool applied = Runtime.TrySetRules(changed);
        if (applied)
        {
            _botMinimumWhenEnabled = Math.Min(_botMinimumWhenEnabled, changed.MaxPlayers);
            if (BotPolicy.MinimumParticipants > changed.MaxPlayers)
                BotPolicy = BotPolicy with { MinimumParticipants = changed.MaxPlayers };
            ClearHumanReady();
        }
        return Mutation(request, applied);
    }

    private LobbyCommandResult SetBotPolicy(in LobbyRequestPacket request)
    {
        if (BotsForbidden(Runtime.Draft.Current))
            return Result(request.RequestId, LobbyFeedbackCode.NotPermitted,
                "Bots are disabled in Ranked and Duel lobbies.");
        BotFillPolicy changed = request.Type switch
        {
            LobbyRequestType.SetBotFillEnabled => BotPolicy with
            {
                MinimumParticipants = request.Value == 0 ? 0
                    : Math.Min(_botMinimumWhenEnabled, Runtime.Draft.Current.MaxPlayers)
            },
            LobbyRequestType.SetBotMinimumParticipants => BotPolicy with
            {
                MinimumParticipants = request.Value
            },
            LobbyRequestType.SetBotSkill => BotPolicy with { Skill = request.Value },
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        changed.Validate(Runtime.Draft.Current.MaxPlayers);
        if (changed == BotPolicy) return Mutation(request, changed: false);
        if (changed.MinimumParticipants != 0) _botMinimumWhenEnabled = changed.MinimumParticipants;
        BotPolicy = changed;
        if (!Runtime.TryMarkPolicyChanged())
            throw new InvalidOperationException("The lobby bot policy changed outside the open phase.");
        ClearHumanReady();
        return Mutation(request, changed: true);
    }

    private LobbyCommandResult Start(in LobbyRequestPacket request, ulong connectionId, bool force,
        LobbyCommandAction action)
    {
        if (action == LobbyCommandAction.Rematch && LastMatchSummary == null)
            return Result(request.RequestId, LobbyFeedbackCode.WrongPhase, "Rematch is available after a completed match.");
        LobbyStartDecision decision = CanStart(connectionId, force);
        if (!decision.Allowed)
            return Result(request.RequestId, StartFeedback(decision.Block), decision.Message);
        if (!TryStart(connectionId, force, out MatchRules? frozen))
            return Result(request.RequestId, LobbyFeedbackCode.Conflict, "The lobby start could not be committed.");
        return Result(request.RequestId, LobbyFeedbackCode.None, "", action, frozen);
    }

    private LobbyCommandResult ReturnToLobby(in LobbyRequestPacket request)
    {
        if (!LobbyStartPolicy.UsesLobbyAfterMatch(Policy.Kind))
            return Result(request.RequestId, LobbyFeedbackCode.Unsupported, "This server rotates without an intermission lobby.");
        bool changed = Runtime.TryReopen();
        ClearHumanReady();
        _pendingStart = null;
        return Result(request.RequestId, LobbyFeedbackCode.None, "",
            changed ? LobbyCommandAction.ReturnToLobby : LobbyCommandAction.None);
    }

    private LobbyCommandResult UpdatePlayer(in LobbyRequestPacket request, LobbyPlayer player)
        => Mutation(request, Runtime.TryUpdate(player));

    private LobbyCommandResult Mutation(in LobbyRequestPacket request, bool changed)
        => changed
            ? Result(request.RequestId, LobbyFeedbackCode.None, "")
            : Result(request.RequestId, LobbyFeedbackCode.Conflict, "The lobby already has that value.");

    private LobbyCommandResult Result(uint requestId, LobbyFeedbackCode code, string message,
        LobbyCommandAction action = LobbyCommandAction.None, MatchRules? frozen = null)
        => new(new LobbyFeedbackPacket(Runtime.SessionId, Runtime.Revision, requestId, code, message), action, frozen);

    private LobbyChatDispatch ChatResult(uint requestId, LobbyFeedbackCode code, string message)
        => new(new LobbyFeedbackPacket(Runtime.SessionId, Runtime.Revision, requestId, code, message),
            null, ImmutableArray<ulong>.Empty);

    private static LobbyPermissions PermissionFor(LobbyRequestType type) => type switch
    {
        LobbyRequestType.SetReady => LobbyPermissions.SetReady,
        LobbyRequestType.SelectHunter => LobbyPermissions.SelectHunter,
        LobbyRequestType.RequestTeam => LobbyPermissions.RequestTeam,
        LobbyRequestType.SetMap => LobbyPermissions.SetMap,
        LobbyRequestType.SetMode => LobbyPermissions.SetMode,
        LobbyRequestType.SetRule => LobbyPermissions.SetRule,
        LobbyRequestType.SetBotFillEnabled or LobbyRequestType.SetBotMinimumParticipants
            or LobbyRequestType.SetBotSkill => LobbyPermissions.SetRule,
        LobbyRequestType.StartMatch => LobbyPermissions.StartMatch,
        LobbyRequestType.ReturnToLobby => LobbyPermissions.ReturnToLobby,
        LobbyRequestType.Rematch => LobbyPermissions.Rematch,
        _ => LobbyPermissions.None
    };

    private static LobbyFeedbackCode StartFeedback(LobbyStartBlock block) => block switch
    {
        LobbyStartBlock.WrongPhase => LobbyFeedbackCode.WrongPhase,
        LobbyStartBlock.TooFewPlayers => LobbyFeedbackCode.Capacity,
        LobbyStartBlock.NotHost or LobbyStartBlock.TournamentLocked => LobbyFeedbackCode.NotPermitted,
        LobbyStartBlock.LobbyDisabled => LobbyFeedbackCode.Unsupported,
        _ => LobbyFeedbackCode.Conflict
    };

    private bool ConfigurationLocked => TournamentRosterLocked
        || Runtime.Draft.Current.RankingEligibility == RankingEligibility.VerifiedServerOnly;

    private static bool BotsForbidden(MatchRules rules)
        => rules.RankingEligibility == RankingEligibility.VerifiedServerOnly
            || rules.RulesetPreset == RulesetPreset.Duel;

    private bool TryAllocateSlot(bool observer, out byte slot)
    {
        if (observer)
        {
            slot = byte.MaxValue;
            return Runtime.Observers.Count + _reservations.Values.Count(value => value.Player.Observer)
                < LobbyRuntime.MaximumObservers;
        }
        int capacity = Runtime.Draft.Current.MaxPlayers;
        for (byte candidate = 0; candidate < capacity; candidate++)
        {
            if (_slots.ContainsValue(candidate)
                || _reservations.Values.Any(value => !value.Player.Observer && value.Slot == candidate)) continue;
            slot = candidate;
            return true;
        }
        slot = byte.MaxValue;
        return false;
    }

    private byte ResolveJoiningTeam(byte slot, byte requested)
    {
        if (!Runtime.Draft.Current.Teams) return slot;
        if (!TournamentRosterLocked && requested is 0 or 1) return requested;
        Span<byte> teams = stackalloc byte[LobbyRuntime.MaximumPlayers];
        teams.Fill(TeamAllocator.Unassigned);
        foreach (LobbyPlayer player in Runtime.Players)
            if (_slots.TryGetValue(player.ConnectionId, out byte occupied)) teams[occupied] = player.Team;
        return TeamAllocator.Select(teams[..Runtime.Draft.Current.MaxPlayers], (byte)(slot & 1));
    }

    private void AssignTeamsForMode()
    {
        if (Runtime.Draft.Current.Teams)
        {
            Span<byte> teams = stackalloc byte[LobbyRuntime.MaximumPlayers];
            teams.Fill(TeamAllocator.Unassigned);
            foreach (LobbyPlayer player in Runtime.Players)
                teams[_slots[player.ConnectionId]] = player.Team is 0 or 1 ? player.Team : (byte)(_slots[player.ConnectionId] & 1);
            TeamAllocator.Rebalance(teams[..Runtime.Draft.Current.MaxPlayers]);
            foreach (LobbyPlayer player in Runtime.Players.ToArray())
                Runtime.TryUpdate(player with { Team = teams[_slots[player.ConnectionId]] });
        }
        else
        {
            foreach (LobbyPlayer player in Runtime.Players.ToArray())
                Runtime.TryUpdate(player with { Team = _slots[player.ConnectionId] });
        }
    }

    private void ClearHumanReady()
    {
        foreach (LobbyPlayer player in Runtime.Players.ToArray())
            if (!player.Bot && player.Ready) Runtime.TryUpdate(player with { Ready = false });
    }

    private static LobbySnapshotMember ToSnapshotMember(LobbyPlayer player, byte slot)
    {
        LobbyMemberFlags flags = (player.Ready ? LobbyMemberFlags.Ready : 0)
            | (player.Observer ? LobbyMemberFlags.Observer : 0)
            | (player.Host ? LobbyMemberFlags.Host : 0)
            | (player.Admin ? LobbyMemberFlags.Admin : 0)
            | (player.Loading ? LobbyMemberFlags.Loading : 0)
            | (player.DisconnectedGrace ? LobbyMemberFlags.DisconnectedGrace : 0);
        LobbyWireIdentityKind identity = player.Bot ? LobbyWireIdentityKind.Bot
            : player.Authenticated ? LobbyWireIdentityKind.Registered : LobbyWireIdentityKind.Guest;
        return new(slot, player.ConnectionId, player.DisplayName, player.Hunter, player.Team,
            identity, flags, player.PingMs, player.StarTier);
    }

    private static ushort Ping(ServerPeer peer) => peer.Connection.Metrics.Rtt.Count == 0
        ? (ushort)0 : (ushort)Math.Clamp(Math.Round(peer.Connection.Metrics.SmoothedRttMs), 1, UInt16.MaxValue);
}
