using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using MphRead.Mods.UI.State;

namespace MphRead.Mods.UI.Adapters;

public sealed record LobbyMutationReceipt(LobbySnapshotPacket? Snapshot,
    LobbyFeedbackPacket? Feedback = null, LobbyChatPacket? Chat = null);

public interface ILobbyMutationTransport
{
    Task<LobbyMutationReceipt> SendAsync(LobbyRequestPacket request,
        CancellationToken cancellationToken);
    Task<LobbyMutationReceipt> SendChatAsync(LobbyChatRequestPacket request,
        CancellationToken cancellationToken);
}

public interface ILobbyUiPolicy
{
    IReadOnlyDictionary<UiLobbyAction, string> DisabledReasons(LobbySnapshotPacket snapshot,
        LobbySnapshotMember? localMember);
}

/// <summary>Maps protocol state to UI state; mutation completion requires server feedback/state.</summary>
public sealed class CoordinatorLobbyAdapter : ILobbyScreenController
{
    private readonly ClientSessionCoordinator _coordinator;
    private readonly ILobbyMutationTransport _transport;
    private readonly ILobbyUiPolicy? _policy;
    private readonly Action _leave;
    private readonly List<UiLobbyChatLine> _chat = [];
    private readonly LobbyMuteState _muteState = new();
    private UiLobbySnapshot? _snapshot;
    private (uint Session, uint Revision, uint Request) _lastChat;
    private int _requestId;

    public CoordinatorLobbyAdapter(ClientSessionCoordinator coordinator,
        ILobbyMutationTransport transport, ILobbyUiPolicy? policy = null, Action? leave = null)
    {
        _coordinator = coordinator;
        _transport = transport;
        _policy = policy;
        _leave = leave ?? coordinator.Leave;
        _coordinator.StateChanged += _ => Refresh();
        Refresh();
    }

    public UiLobbySnapshot? Snapshot => _snapshot;
    public event Action? Changed;

    public void Refresh()
    {
        NetClient? client = _coordinator.Session?.Client;
        LobbySnapshotPacket? wire = client?.LobbySnapshot;
        if (wire is null) return;
        if (_snapshot is not null && wire.SessionId != _snapshot.SessionId)
        {
            _chat.Clear();
            _lastChat = default;
        }
        bool chatChanged = AddChat(client!.LobbyChat);
        if (_snapshot is not null && wire.SessionId == _snapshot.SessionId
            && wire.Revision <= _snapshot.Revision)
        {
            if (chatChanged)
            {
                _snapshot = WithChatProjection(_snapshot);
                Changed?.Invoke();
            }
            return;
        }
        _snapshot = Map(wire, client.Connection?.Id ?? 0);
        Changed?.Invoke();
    }

    public async Task<UiActionResult> RequestAsync(UiLobbyCommand command,
        uint expectedRevision, CancellationToken cancellationToken)
    {
        UiLobbySnapshot current = _snapshot
            ?? throw new InvalidOperationException("Lobby state is unavailable.");
        if (current.Revision != expectedRevision)
            return UiActionResult.Failure("Lobby state changed. Review the latest state and try again.");
        if (command.Action == UiLobbyAction.LeaveServer)
        {
            _leave();
            _snapshot = null;
            Changed?.Invoke();
            return UiActionResult.Success();
        }
        uint requestId = unchecked((uint)Interlocked.Increment(ref _requestId));
        if (requestId == 0) requestId = unchecked((uint)Interlocked.Increment(ref _requestId));

        LobbyMutationReceipt receipt;
        if (command.Action is UiLobbyAction.SendLobbyChat or UiLobbyAction.SendTeamChat)
        {
            var chat = new LobbyChatRequestPacket(current.SessionId, current.Revision, requestId,
                command.Action == UiLobbyAction.SendTeamChat ? ChatScope.Team : ChatScope.Lobby,
                command.Text);
            receipt = await _transport.SendChatAsync(chat, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            LobbyRequestPacket request = BuildRequest(command, current, requestId);
            receipt = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        if (receipt.Feedback is { Accepted: false } feedback)
            return UiActionResult.Failure(feedback.Message);
        if (receipt.Snapshot is { } snapshot) _snapshot = Map(snapshot,
            _coordinator.Session?.Client.Connection?.Id ?? 0);
        if (AddChat(receipt.Chat) && _snapshot is not null)
            _snapshot = WithChatProjection(_snapshot);
        Changed?.Invoke();
        return UiActionResult.Success();
    }

    public void SetSenderMuted(ulong senderIdentity, bool muted)
    {
        if (!_muteState.SetMuted(senderIdentity, muted) || _snapshot is null) return;
        _snapshot = WithChatProjection(_snapshot);
        Changed?.Invoke();
    }

    private UiLobbySnapshot Map(LobbySnapshotPacket snapshot, ulong localConnection)
    {
        LobbySnapshotMember? local = null;
        var members = ImmutableArray.CreateBuilder<UiLobbyMember>(snapshot.Members.Length);
        foreach (LobbySnapshotMember member in snapshot.Members)
        {
            if (member.ConnectionId == localConnection) local = member;
            members.Add(MapMember(member, localConnection));
        }
        IReadOnlyDictionary<UiLobbyAction, string> reasons = BuildDisabled(snapshot, local);
        MatchRules rules = snapshot.Rules;
        string summary = $"{rules.RulesetPreset} · {rules.MaxPlayers} players · "
            + $"{(rules.TimeLimit is null ? "No limit" : $"{rules.TimeLimit:mm\\:ss}")} · "
            + $"FF {(rules.FriendlyFire ? "On" : "Off")} · Radar {(rules.PlayerRadar ? "On" : "Off")} · "
            + $"Spawn {rules.SpawnPolicy} · Late join {rules.LateJoinPolicy}";
        string policy = $"{snapshot.Policy} · {(snapshot.ReadyRequired ? "Ready required" : "Ready optional")}"
            + $" · Minimum {snapshot.MinimumPlayers}"
            + (snapshot.HostMayForceStart ? " · Host may force start" : string.Empty);
        return new UiLobbySnapshot(snapshot.SessionId, snapshot.Revision, snapshot.Phase.ToString(),
            policy, rules.RoomKey, rules.Mode.ToString(), summary,
            members.MoveToImmutable(), _muteState.Visible(_chat), reasons,
            snapshot.BotFillEnabled, snapshot.BotMinimumParticipants, snapshot.BotSkill,
            _muteState.MutedSenders);
    }

    internal static UiLobbyMember MapMember(LobbySnapshotMember member, ulong localConnection)
        => new(member.DisplayName, member.Hunter.ToString(), member.Team,
            member.Ready, member.Loading, member.Observer, member.DisconnectedGrace,
            member.Bot, member.Host, member.Admin,
            RatingEligible: member.Registered && !member.Observer, member.PingMs,
            Local: member.ConnectionId == localConnection);

    private IReadOnlyDictionary<UiLobbyAction, string> BuildDisabled(LobbySnapshotPacket snapshot,
        LobbySnapshotMember? local)
    {
        var reasons = new Dictionary<UiLobbyAction, string>();
        if (local is null)
        {
            foreach (UiLobbyAction action in Enum.GetValues<UiLobbyAction>())
                if (action != UiLobbyAction.LeaveServer)
                    reasons[action] = "The local lobby member is not present in the server snapshot.";
            return reasons;
        }
        if (local.Value.Observer)
        {
            reasons[UiLobbyAction.SetReady] = "Spectators do not ready for the match.";
            reasons[UiLobbyAction.SelectHunter] = "Spectators do not select an active Hunter.";
            reasons[UiLobbyAction.RequestTeam] = "Spectators are not assigned to a team.";
            reasons[UiLobbyAction.SendTeamChat] = "Spectators are not assigned to team chat.";
        }
        else if (!snapshot.Rules.Teams)
            reasons[UiLobbyAction.SendTeamChat] = "This match mode does not use team chat.";
        if (local.Value.DisconnectedGrace)
        {
            foreach (UiLobbyAction action in Enum.GetValues<UiLobbyAction>())
                if (action != UiLobbyAction.LeaveServer)
                    reasons[action] = "Controls are unavailable while reconnecting.";
        }
        foreach (UiLobbyAction action in Enum.GetValues<UiLobbyAction>())
        {
            LobbyPermissions required = RequiredPermission(action);
            if (required != LobbyPermissions.None && (snapshot.Permissions & required) == 0)
                reasons.TryAdd(action, "The server has not granted permission for this action.");
        }
        if (snapshot.Phase != LobbyPhase.Open)
        {
            const string locked = "The lobby is locked while the server starts a match.";
            foreach (UiLobbyAction action in Enum.GetValues<UiLobbyAction>())
                if (action is not (UiLobbyAction.SendLobbyChat or UiLobbyAction.SendTeamChat
                    or UiLobbyAction.LeaveServer))
                    reasons[action] = locked;
        }
        if (_policy is not null)
            foreach ((UiLobbyAction action, string reason) in _policy.DisabledReasons(snapshot, local))
                reasons[action] = reason;
        return reasons;
    }

    private static LobbyPermissions RequiredPermission(UiLobbyAction action) => action switch
    {
        UiLobbyAction.SetReady => LobbyPermissions.SetReady,
        UiLobbyAction.SelectHunter => LobbyPermissions.SelectHunter,
        UiLobbyAction.RequestTeam => LobbyPermissions.RequestTeam,
        UiLobbyAction.SetMap => LobbyPermissions.SetMap,
        UiLobbyAction.SetMode => LobbyPermissions.SetMode,
        UiLobbyAction.SetRule => LobbyPermissions.SetRule,
        UiLobbyAction.SetBotFillEnabled or UiLobbyAction.SetBotMinimumParticipants
            or UiLobbyAction.SetBotSkill => LobbyPermissions.SetRule,
        UiLobbyAction.StartMatch => LobbyPermissions.StartMatch,
        UiLobbyAction.ReturnToLobby => LobbyPermissions.ReturnToLobby,
        UiLobbyAction.Rematch => LobbyPermissions.Rematch,
        _ => LobbyPermissions.None
    };

    internal static LobbyRequestPacket BuildRequest(UiLobbyCommand command, UiLobbySnapshot snapshot,
        uint requestId)
    {
        LobbyRequestType type = command.Action switch
        {
            UiLobbyAction.SelectHunter => LobbyRequestType.SelectHunter,
            UiLobbyAction.RequestTeam => LobbyRequestType.RequestTeam,
            UiLobbyAction.SetMap => LobbyRequestType.SetMap,
            UiLobbyAction.SetMode => LobbyRequestType.SetMode,
            UiLobbyAction.SetRule => LobbyRequestType.SetRule,
            UiLobbyAction.SetBotFillEnabled => LobbyRequestType.SetBotFillEnabled,
            UiLobbyAction.SetBotMinimumParticipants => LobbyRequestType.SetBotMinimumParticipants,
            UiLobbyAction.SetBotSkill => LobbyRequestType.SetBotSkill,
            UiLobbyAction.SetReady => LobbyRequestType.SetReady,
            UiLobbyAction.StartMatch => LobbyRequestType.StartMatch,
            UiLobbyAction.ReturnToLobby => LobbyRequestType.ReturnToLobby,
            UiLobbyAction.Rematch => LobbyRequestType.Rematch,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        LobbyRuleField rule = type == LobbyRequestType.SetRule
            && Enum.TryParse(command.Rule, out LobbyRuleField parsed) ? parsed : LobbyRuleField.None;
        return new LobbyRequestPacket(snapshot.SessionId, snapshot.Revision, requestId,
            type, rule, command.Value, type == LobbyRequestType.SetMap ? command.Text : string.Empty);
    }

    private bool AddChat(LobbyChatPacket? packet)
    {
        if (packet is not { } chat) return false;
        var identity = (chat.SessionId, chat.Revision, chat.RequestId);
        if (identity == _lastChat) return false;
        _lastChat = identity;
        UiLobbyChatLine line = MapChat(chat);
        _chat.Add(line);
        _muteState.Observe(line);
        if (_chat.Count > 64) _chat.RemoveAt(0);
        return true;
    }

    internal static UiLobbyChatLine MapChat(in LobbyChatPacket chat)
        => new(chat.Name, chat.Text, chat.Scope == ChatScope.Team,
            (chat.Flags & LobbyChatSenderFlags.Host) != 0,
            (chat.Flags & LobbyChatSenderFlags.Admin) != 0, chat.SenderIdentity);

    private UiLobbySnapshot WithChatProjection(UiLobbySnapshot snapshot)
        => snapshot with { Chat = _muteState.Visible(_chat), MutedSenders = _muteState.MutedSenders };
}

internal sealed class LobbyMuteState
{
    private readonly Dictionary<ulong, string> _publishedSenders = [];
    private readonly HashSet<ulong> _muted = [];

    public ImmutableArray<UiMutedLobbySender> MutedSenders => _muted
        .OrderBy(identity => _publishedSenders[identity], StringComparer.OrdinalIgnoreCase)
        .ThenBy(identity => identity)
        .Select(identity => new UiMutedLobbySender(identity, _publishedSenders[identity]))
        .ToImmutableArray();

    public void Observe(UiLobbyChatLine line)
    {
        if (line.SenderIdentity != 0) _publishedSenders[line.SenderIdentity] = line.Name;
    }

    public bool SetMuted(ulong senderIdentity, bool muted)
    {
        if (senderIdentity == 0 || !_publishedSenders.ContainsKey(senderIdentity)) return false;
        return muted ? _muted.Add(senderIdentity) : _muted.Remove(senderIdentity);
    }

    public ImmutableArray<UiLobbyChatLine> Visible(IEnumerable<UiLobbyChatLine> chat)
        => chat.Where(line => !_muted.Contains(line.SenderIdentity)).ToImmutableArray();
}

public interface IPostMatchRatingSource
{
    Task<(int Delta, int Points)?> AwaitAsync(uint matchId, CancellationToken cancellationToken);
}

public interface IPostMatchActionSource
{
    Task<UiActionResult> InvokeAsync(PostMatchAction action, CancellationToken cancellationToken);
}

public sealed class CoordinatorPostMatchAdapter : IPostMatchScreenController
{
    private readonly ClientSessionCoordinator _coordinator;
    private readonly IPostMatchRatingSource? _rating;
    private readonly IPostMatchActionSource? _actions;

    public CoordinatorPostMatchAdapter(ClientSessionCoordinator coordinator,
        IPostMatchRatingSource? rating = null, IPostMatchActionSource? actions = null)
    {
        _coordinator = coordinator;
        _rating = rating;
        _actions = actions;
        Summary = Map(coordinator.Session?.Client.MatchSummary);
        coordinator.StateChanged += _ => { Summary = Map(coordinator.Session?.Client.MatchSummary); Changed?.Invoke(); };
    }

    public UiPostMatchSummary? Summary { get; private set; }
    public event Action? Changed;

    public async Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId,
        CancellationToken cancellationToken)
    {
        UiPostMatchSummary current = Summary is { MatchId: var id } && id == matchId
            ? Summary : throw new InvalidOperationException("Match summary changed.");
        if (_rating is null) return current with { RatingState = RatingUpdateState.Unavailable };
        (int Delta, int Points)? rating = await _rating.AwaitAsync(matchId, cancellationToken)
            .ConfigureAwait(false);
        Summary = rating is { } value
            ? current with { RatingState = RatingUpdateState.Updated,
                RatingDelta = value.Delta, RatingPoints = value.Points }
            : current with { RatingState = RatingUpdateState.Unavailable };
        Changed?.Invoke();
        return Summary;
    }

    public Task<UiActionResult> InvokeAsync(PostMatchAction action,
        CancellationToken cancellationToken)
    {
        if (action is PostMatchAction.Continue or PostMatchAction.ReturnToLobby)
            return Task.FromResult(_coordinator.ReturnToLobby()
                ? UiActionResult.Success() : UiActionResult.Failure("The server has not returned to the lobby yet."));
        if (action == PostMatchAction.LeaveServer)
        {
            _coordinator.Leave();
            return Task.FromResult(UiActionResult.Success());
        }
        return _actions?.InvokeAsync(action, cancellationToken)
            ?? Task.FromResult(UiActionResult.Failure("This server does not offer that action."));
    }

    private static UiPostMatchSummary? Map(MatchSummaryPacket? summary)
    {
        if (summary is null) return null;
        ImmutableArray<UiPostMatchRow> rows = summary.Rows.Take(MatchSummaryPacket.MaximumRows)
            .Select(row => new UiPostMatchRow(row.Placement, row.Name, row.Hunter.ToString(),
                row.Team, row.Bot, row.Points, row.Kills, row.Deaths, row.Assists, row.Damage,
                row.Headshots, row.ObjectivePrimary, row.ObjectiveSecondary, row.ObjectiveTertiary))
            .ToImmutableArray();
        RatingUpdateState rating = (summary.Flags & MatchSummaryFlags.RatingPending) != 0
            ? RatingUpdateState.Pending
            : (summary.Flags & MatchSummaryFlags.RatingEligible) != 0
                ? RatingUpdateState.Unavailable : RatingUpdateState.NotEligible;
        return new UiPostMatchSummary(summary.SessionId, summary.LobbyRevision, summary.MatchId,
            TimeSpan.FromSeconds(summary.DurationSeconds), summary.Map, summary.Mode.ToString(),
            summary.EndReason.ToString(), rows, rating);
    }
}

public sealed class AccountSessionUiAdapter : IAccountScreenController
{
    private readonly AccountSession _session;
    public AccountSessionUiAdapter(AccountSession session) => _session = session;
    public UiAccountSnapshot Snapshot { get; private set; } = new(UiAccountPhase.Guest,
        "Guest", string.Empty, false, false);

    public async Task<UiActionResult> RestoreAsync(CancellationToken cancellationToken)
    {
        Snapshot = Snapshot with { Phase = UiAccountPhase.Restoring, Message = "Restoring session…" };
        try
        {
            if (!await _session.RestoreAsync(cancellationToken).ConfigureAwait(false))
            {
                Snapshot = new(UiAccountPhase.Guest, "Guest", string.Empty, false, false,
                    "No saved session was found.");
                return UiActionResult.Success(Snapshot.Message);
            }
            return await LoadIdentityAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { return Fail(error, "Session restore"); }
    }

    public async Task<UiActionResult> SignInAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        Snapshot = Snapshot with { Phase = UiAccountPhase.SigningIn, Message = "Signing in…" };
        try
        {
            await _session.SignInAsync(email, password, cancellationToken).ConfigureAwait(false);
            return await LoadIdentityAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { return Fail(error, "Sign in"); }
    }

    public async Task<UiActionResult> SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.SignOutAsync(cancellationToken).ConfigureAwait(false);
            Snapshot = new(UiAccountPhase.Guest, "Guest", string.Empty, false, false);
            return UiActionResult.Success();
        }
        catch (Exception error) { return Fail(error, "Sign out"); }
    }

    private async Task<UiActionResult> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        AccountIdentity identity = _session.Identity
            ?? throw new InvalidOperationException("The restored account has no identity.");
        HunterLicense license = await _session.GetLicenseAsync(identity.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        Snapshot = new(UiAccountPhase.SignedIn, license.DisplayName, identity.PlayerId.ToString(),
            identity.EmailConfirmed, identity.EmailEligibleForOfficialPlay);
        return UiActionResult.Success();
    }

    private UiActionResult Fail(Exception error, string operation)
    {
        string message = AsyncScreenState.FriendlyFailure(error, operation);
        Snapshot = Snapshot with { Phase = error is System.Net.Http.HttpRequestException
            ? UiAccountPhase.Offline : UiAccountPhase.Failed, Message = message };
        return UiActionResult.Failure(message);
    }
}
