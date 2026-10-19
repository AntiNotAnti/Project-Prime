#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Social;

/// <summary>
/// Friends, game invitations and privacy use the existing authenticated owners.
/// Group membership and travel open the native Party page.
/// </summary>
internal sealed class NativeSocialFriendsPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly RmlUiHost _host;
    private readonly NativeSocialFriendsController _controller;
    private readonly NativePresenceController? _presence;
    private readonly Func<NativePartyLocalLobby?> _localLobby;
    private RmlUiDocumentToken _page, _confirmation;
    private long _revision, _lastDataRevision, _lastPresenceRevision;
    private bool _closed, _disposed;
    private NativeSocialJoinTarget? _networkJoin;
    private string _joinStatus = "";
    public bool JoinPending => _networkJoin != null;
    public bool Closed => _closed;
    public bool OpenPartyRequested { get; private set; }
    public string PartyInvitePrimeId { get; private set; } = "";
    public NativeSocialFriendsPage(RmlUiHost host, RmlUiPageManager pages,
        INativeSocialFriendsService service, NativePresenceController? presence = null,
        Func<NativePartyLocalLobby?>? localLobby = null)
    {
        _host = host; _pages = pages;
        _controller = new NativeSocialFriendsController(service);
        _presence = presence;
        _localLobby = localLobby ?? CurrentSocialFriendsService.CaptureLocalLobby;
    }

    public void Open()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSocialFriendsPage));
        _page = _pages.OpenPage(new("social-friends", "pages/social/social.rml", "social_filter"));
        _controller.Refresh();
        Refresh();
    }

    private static NativeSocialFriendAction? Action(int index) => index switch
    {
        21 => NativeSocialFriendAction.SendRequest,
        22 => NativeSocialFriendAction.AcceptRequest,
        23 => NativeSocialFriendAction.DeclineRequest,
        24 => NativeSocialFriendAction.CancelRequest,
        25 => NativeSocialFriendAction.RemoveFriend,
        26 => NativeSocialFriendAction.BlockPlayer,
        27 => NativeSocialFriendAction.UnblockPlayer,
        _ => null
    };

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || _closed || intent.Kind != RmlUiIntentKind.SocialAction
            || (intent.Document != _page && intent.Document != _confirmation)
            || !_pages.Accept(intent)) return false;
        int action = intent.Argument;
        if (_networkJoin != null && action is not (8 or 52)) return false;
        if (action == 8 && _networkJoin != null)
        {
            _networkJoin = null; _joinStatus = "SOCIAL JOIN CANCELLED";
            _lastDataRevision = -1; Refresh(); return true;
        }
        if (_controller.Current().Confirmation.Length != 0
            && action is not (46 or 47 or 52))
            return false;
        if (action == 52)
        {
            if (_controller.Current().Confirmation.Length != 0)
                _controller.Dismiss();
            else _closed = true;
        }
        else if (action == 4 || action == 33 && _controller.Current().Selected is { Relation: NativeSocialRelation.Friend })
        {
            PartyInvitePrimeId = action == 33 ? _controller.Current().Selected!.PrimeId : "";
            OpenPartyRequested = true;
        }
        else if (action == 46) _controller.Confirm();
        else if (action == 47) _controller.Dismiss();
        else if (action == 7) { _controller.Refresh(); _presence?.Refresh(); }
        else if (action == 8) _controller.Cancel();
        else if (action == 9) _controller.Lookup(_host.ReadField(_page, "social_lookup_id", 128));
        else if (action == 10) _controller.Search(_host.ReadField(_page, "social_filter", 256));
        else if (action is 11 or 12) _controller.Page(action == 11 ? -1 : 1);
        else if (action is >= 13 and <= 20) _controller.Select(action - 13);
        else if (action is 0 or 1 or 2 or 3 or 5 or 6) _controller.SetTab(action);
        else if (action is >= 28 and <= 32)
        {
            _joinStatus = "";
            _controller.LobbyCommand((NativeSocialLobbyAction)(action - 28),
                _localLobby(), _presence?.Current().Directory);
        }
        else if (action is >= 48 and <= 51 && _presence?.Current() is { Directory: { } directory } presence
            && !presence.Busy && directory.SelfPrimeId == _controller.Current().SelfPrimeId)
        {
            static string Next(string value, string first, string second, string third)
                => value == first ? second : value == second ? third : first;
            NativeSocialPrivacy privacy = directory.Privacy;
            _presence.SetPrivacy(action switch
            {
                48 => privacy with { Presence = Next(privacy.Presence, "friends", "everyone", "hidden") },
                49 => privacy with { Activity = Next(privacy.Activity, "friends", "everyone", "private") },
                50 => privacy with { Invites = Next(privacy.Invites, "friends", "everyone", "nobody") },
                _ => privacy with { DoNotDisturb = !privacy.DoNotDisturb }
            });
        }
        else if (Action(action) is { } operation) _controller.Command(operation);
        else return false;

        if (!_closed) Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _closed || _page == default) return;
        NativeSocialView snapshot = _controller.Tick();
        if (_networkJoin != null && !_controller.IsJoinCurrent(_networkJoin))
        { _networkJoin = null; _joinStatus = "SOCIAL ACCOUNT OR DESTINATION CHANGED // REFRESH SOCIAL"; _lastDataRevision = -1; }
        NativePresenceView? presence = _presence?.Current();
        // No RML binding churn when a network operation has not changed.
        if (snapshot.Revision == _lastDataRevision && (presence?.Revision ?? 0) == _lastPresenceRevision) return;
        _lastDataRevision = snapshot.Revision;
        _lastPresenceRevision = presence?.Revision ?? 0;
        NativePresenceDirectory? directory = presence?.Directory is { } verified
            && verified.SelfPrimeId == snapshot.SelfPrimeId ? verified : null;
        if (snapshot.Confirmation.Length > 0 && !_host.IsAlive(_confirmation))
            _confirmation = _pages.OpenModal(new("social-friends-confirm",
                "pages/social/confirm.rml", "social_confirm_cancel"));
        else if (snapshot.Confirmation.Length == 0 && _host.IsAlive(_confirmation))
        {
            _pages.CloseModal();
            _confirmation = default;
        }
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
        void Text(string id, string text) => data[id] = RmlUiBindingValue.FromText(text);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("social_self_id", snapshot.Connected ? snapshot.SelfPrimeId : "NOT CONNECTED");
        Text("social_counts", snapshot.Connected
            ? snapshot.Total + " " + snapshot.Tab + " // SERVER VERIFIED"
                + (snapshot.InvitationsAvailable ? "" : " // GAME INVITES NOT SYNCED") : "ACCOUNT UNAVAILABLE");
        Text("social_status", _networkJoin != null ? "VERIFYING DESTINATION AND SERVER ADMISSION" :
            _joinStatus.Length != 0 ? _joinStatus : snapshot.Status);
        Text("social_error", snapshot.Status.StartsWith("SOCIAL UNAVAILABLE",
            StringComparison.Ordinal) ? snapshot.Status : "");
        Bool("visible:social_error", snapshot.Status.StartsWith("SOCIAL UNAVAILABLE", StringComparison.Ordinal));
        Text("social_empty", snapshot.Total == 0
            ? snapshot.Busy ? "SYNCING ACCOUNT…" : snapshot.Connected
                ? "NO " + snapshot.Tab + " FOUND" : "SOCIAL SERVICE UNAVAILABLE" : "");
        Text("social_page_count", $"{snapshot.Page + 1} / {snapshot.Pages}");
        Bool("disabled:social_previous_page", snapshot.Busy || snapshot.Page == 0);
        Bool("disabled:social_next_page", snapshot.Busy || snapshot.Page + 1 == snapshot.Pages);
        Bool("disabled:social_cancel_refresh", !snapshot.Busy && _networkJoin == null);
        Bool("disabled:social_lookup", snapshot.Busy);
        Bool("disabled:social_refresh", snapshot.Busy);
        Bool("disabled:social_search", snapshot.Busy);
        Bool("disabled:social_friends", snapshot.Busy);
        Bool("disabled:social_players", snapshot.Busy);
        Bool("disabled:social_requests", snapshot.Busy);
        Bool("disabled:social_blocked", snapshot.Busy);
        foreach (string tab in new[] { "friends", "players", "requests", "invites", "recent", "blocked" })
            Bool("class:social_" + tab + ":selected", snapshot.Tab.Equals(tab, StringComparison.OrdinalIgnoreCase));

        Bool("disabled:social_recent", snapshot.Busy || !snapshot.RecentAvailable);
        Bool("disabled:social_invites", snapshot.Busy || !snapshot.InvitationsAvailable);
        foreach (string id in new[] { "social_presence", "social_activity", "social_invites_privacy", "social_dnd" })
            Bool("disabled:" + id, directory == null || presence!.Busy);
        for (int i = 28; i <= 32; i++) Bool("disabled:social_command_" + i,
            !_controller.CanLobby((NativeSocialLobbyAction)(i - 28), _localLobby(), directory));
        Bool("disabled:social_party", snapshot.Busy);
        Bool("disabled:social_open_party", snapshot.Busy || _networkJoin != null);
        Bool("disabled:social_command_33", snapshot.Busy
            || snapshot.Selected is not { Relation: NativeSocialRelation.Friend });

        NativeSocialInvitation? invitation = snapshot.SelectedInvitation;
        NativeSocialRelation? relation = snapshot.Selected?.Relation;
        // Relevant commands stay in place while a request is pending. Their
        // existing Can/CanLobby bindings independently control availability.
        for (int i = 21; i <= 33; i++)
            Bool("visible:social_command_" + i, i switch
            {
                21 => relation == NativeSocialRelation.Player,
                22 or 23 => relation == NativeSocialRelation.IncomingRequest,
                24 => relation == NativeSocialRelation.OutgoingRequest,
                25 or 28 or 29 or 33 => relation == NativeSocialRelation.Friend,
                26 => relation.HasValue && relation != NativeSocialRelation.Blocked,
                27 => relation == NativeSocialRelation.Blocked,
                30 => invitation is { Incoming: true, Status: "pending" or "accepted" },
                31 => invitation is { Incoming: true, Status: "pending" },
                32 => invitation is { Incoming: false, Status: "pending" },
                _ => false
            });
        Text("social_command_30", invitation is { Incoming: true, Status: "accepted" }
            ? "JOIN LOBBY" : "ACCEPT GAME INVITE");
        Text("social_selected_name", invitation?.DisplayName ?? snapshot.Selected?.DisplayName ?? "SELECT A PLAYER OR INVITATION");
        Text("social_selected_prime_id", invitation?.PrimeId ?? snapshot.Selected?.PrimeId ?? "");
        Text("social_selected_relation", invitation != null ? (invitation.Incoming ? "INCOMING" : "OUTGOING") + " GAME INVITE // " + invitation.Status.ToUpperInvariant()
            : snapshot.Selected?.Relation.ToString().ToUpperInvariant() ?? "");
        NativePresencePerson? selectedPresence = directory?.People.FirstOrDefault(p => p.PrimeId == snapshot.Selected?.PrimeId);
        Text("social_selected_activity", directory == null ? "PRESENCE UNAVAILABLE"
            : selectedPresence == null ? "PRESENCE NOT SHARED" : selectedPresence.Activity.ToUpperInvariant()
                + (selectedPresence.RoomKey.Length == 0 ? "" : " // " + selectedPresence.RoomKey));
        Text("social_selected_detail", invitation != null ? invitation.ServerName + " // " + invitation.RoomKey : snapshot.Selected == null
            ? "Choose a verified player from Friends, Requests, Blocked or Prime ID lookup."
            : "Social mutations are accepted by the authenticated server.");
        Text("social_selected_expiry", invitation == null ? "" : "EXPIRES " + invitation.ExpiresAt.ToLocalTime().ToString("g"));
        Text("social_party_summary", "OPEN PARTY TO VERIFY MEMBERSHIP AND INVITATIONS");
        Text("social_session_notice", presence?.Status ?? "PRESENCE UNAVAILABLE");
        Text("social_presence_value", directory?.Privacy.Presence.ToUpperInvariant() ?? "UNAVAILABLE");
        Text("social_activity_value", directory?.Privacy.Activity.ToUpperInvariant() ?? "UNAVAILABLE");
        Text("social_invites_value", directory?.Privacy.Invites.ToUpperInvariant() ?? "UNAVAILABLE");
        Text("social_dnd_value", directory == null ? "UNAVAILABLE" : directory.Privacy.DoNotDisturb ? "ON" : "OFF");
        for (int i = 21; i <= 27; i++)
            Bool("disabled:social_command_" + i,
                snapshot.Busy || !_controller.Can(Action(i)!.Value));
        for (int i = 0; i < 8; i++)
        {
            NativeSocialInvitation? inviteRow = i < snapshot.InvitationRows.Length ? snapshot.InvitationRows[i] : null;
            NativeSocialPerson? row = i < snapshot.Rows.Length ? snapshot.Rows[i] : null;
            bool found = row != null || inviteRow != null;
            Bool("visible:social_row_" + i, found);
            Bool("disabled:social_row_" + i, !found || snapshot.Busy);
            Bool("class:social_row_" + i + ":selected",
                inviteRow != null ? inviteRow.InviteId == invitation?.InviteId : row != null && row.PrimeId == snapshot.Selected?.PrimeId);
            Text("social_row_name_" + i, inviteRow?.DisplayName ?? row?.DisplayName ?? "");
            Text("social_row_detail_" + i,
                inviteRow != null ? (inviteRow.Incoming ? "INCOMING" : "OUTGOING") + " // " + inviteRow.RoomKey + " // " + inviteRow.Status.ToUpperInvariant() : row is { } person ? person.Relation + " // " + person.PrimeId
                    + (person.RecentLastSeen is { } seen ? " // " + seen.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) + " // " + person.RecentEncounters + " ENCOUNTERS" : "")
                    + (directory?.People.FirstOrDefault(p => p.PrimeId == person.PrimeId) is { } activity
                        ? " // " + activity.Activity.ToUpperInvariant() : "") : "");
        }
        if (_networkJoin != null)
        {
            foreach (string id in new[] { "social_friends", "social_players", "social_requests", "social_invites", "social_party",
                "social_blocked", "social_previous_page", "social_next_page", "social_search", "social_lookup", "social_refresh",
                "social_presence", "social_activity", "social_invites_privacy", "social_dnd", "social_open_party" }) Bool("disabled:" + id, true);
            for (int i = 0; i < 8; i++) Bool("disabled:social_row_" + i, true);
            for (int i = 21; i <= 33; i++) Bool("disabled:social_command_" + i, true);
        }
        _pages.Present(_page, ++_revision, data);
        if (_host.IsAlive(_confirmation))
            _pages.Present(_confirmation, ++_revision, new Dictionary<string, RmlUiBindingValue>
            {
                ["social_confirmation_text"] =
                    RmlUiBindingValue.FromText(snapshot.Confirmation),
                ["disabled:social_confirm_apply"] = RmlUiBindingValue.FromBoolean(snapshot.Busy)
            });
    }

    public void Back()
    {
        if (_disposed || _closed) return;
        if (_controller.Current().Confirmation.Length != 0) _controller.Dismiss();
        else _closed = true;
        if (!_closed) Refresh();
    }

    public bool TryTakeJoin(out NativeSocialJoinTarget target)
    {
        target = null!;
        if (_disposed || _closed || _networkJoin != null || !_controller.TryTakeJoin(out target)) return false;
        _networkJoin = target; _joinStatus = ""; _lastDataRevision = -1; Refresh(); return true;
    }
    public bool IsJoinCurrent(NativeSocialJoinTarget target) => !_disposed && !_closed
        && _host.IsAlive(_page) && _pages.Page == _page && ReferenceEquals(_networkJoin, target)
        && _controller.IsJoinCurrent(target);
    public void JoinCompleted(NativeSocialJoinTarget target, bool accepted, string message)
    {
        if (_disposed || _closed || !_host.IsAlive(_page) || _pages.Page != _page
            || !ReferenceEquals(_networkJoin, target)) return;
        _networkJoin = null; _joinStatus = message; _lastDataRevision = -1;
        if (accepted) _closed = true;
        else Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        if (_host.IsAlive(_confirmation)) _pages.CloseModal();
        if (_host.IsAlive(_page) && _pages.Page == _page) _pages.ClosePage();
    }
}

internal sealed partial class CurrentSocialFriendsService : INativeSocialFriendsService
{
    public bool IsCurrent(NativeSocialDirectory directory)
        => HunterLicenseClient.IsNativeAccountLeaseCurrent(directory.AccountWitness);

    public async Task<NativeSocialMutation> ExecuteForAsync(NativeSocialFriendAction action,
        string primeId, string expectedSelf, CancellationToken token)
    {
        SocialMutationResult result = await SocialClient.MutateExpectedAsync(
            action, primeId, expectedSelf, token).ConfigureAwait(false);
        return new(result.Success, result.Status);
    }

    public async Task<NativeSocialDirectory> ReadAsync(CancellationToken token)
        => await ReadDirectoryAsync(null, token).ConfigureAwait(false);

    private async Task<NativeSocialDirectory> ReadDirectoryAsync(object? expectedLease, CancellationToken token)
    {
        SocialSnapshot state = expectedLease == null
            ? await SocialClient.LoadAsync(token).ConfigureAwait(false)
            : await SocialClient.LoadForAsync(expectedLease, token).ConfigureAwait(false);
        var list = new List<NativeSocialPerson>();
        static void Append(List<NativeSocialPerson> rows,
            IEnumerable<SocialPlayer>? people, NativeSocialRelation relation)
        {
            if (people == null) return;
            foreach (SocialPlayer item in people.Take(1024))
                rows.Add(new(item.PrimeId, item.DisplayName, relation));
        }
        Append(list, state.Friends, NativeSocialRelation.Friend);
        Append(list, state.IncomingRequests, NativeSocialRelation.IncomingRequest);
        Append(list, state.OutgoingRequests, NativeSocialRelation.OutgoingRequest);
        Append(list, state.Blocked, NativeSocialRelation.Blocked);
        var directory = new NativeSocialDirectory(state.Self.PrimeId, list.ToArray()) { AccountWitness = state.AccountWitness };
        try
        {
            var recent = await AuthenticatedNativePartyAuthority.ReadRecentAsync(state.AccountWitness!, state.Self.PrimeId, token).ConfigureAwait(false);
            directory = directory with { RecentAvailable = true, RecentPlayers = recent };
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            if (!IsCurrent(directory)) throw new InvalidOperationException("Social account changed.");
        }
        try
        {
            var invites = await SocialClient.InvokeInvitationAsync("snapshot", state.AccountWitness!, token).ConfigureAwait(false);
            if (invites.Snapshot is not { Incoming: not null, Outgoing: not null } snapshot
                || snapshot.Incoming.Length + snapshot.Outgoing.Length > 1024)
                throw new InvalidOperationException("Invitation snapshot is invalid.");
            var rows = snapshot.Incoming.Select(i => i.Parse(true))
                .Concat(snapshot.Outgoing.Select(i => i.Parse(false))).ToArray();
            if (rows.Any(i => !NativeSocialInvitationValidation.Valid(i))
                || rows.Select(i => i.InviteId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length)
                throw new InvalidOperationException("Invitation snapshot is invalid.");
            var verified = directory with { InvitationsAvailable = true, Invitations = rows };
            SocialClient.PublishInvitations(verified);
            return verified;
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            if (!IsCurrent(directory)) throw new InvalidOperationException("Social account changed.");
            SocialClient.PublishInvitations(directory);
            return directory;
        }
    }

    public async Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken token)
    {
        SocialLookupResult result = await SocialClient.LookupAsync(primeId, token)
            .ConfigureAwait(false);
        return result.Found && result.Player is { } person
            ? new(person.PrimeId, person.DisplayName, NativeSocialRelation.Player) : null;
    }

    public async Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action,
        string primeId, CancellationToken token)
    {
        SocialMutationResult result = await (action switch
        {
            NativeSocialFriendAction.SendRequest => SocialClient.SendFriendRequestAsync(primeId, token),
            NativeSocialFriendAction.AcceptRequest => SocialClient.AcceptFriendRequestAsync(primeId, token),
            NativeSocialFriendAction.DeclineRequest => SocialClient.DeclineFriendRequestAsync(primeId, token),
            NativeSocialFriendAction.CancelRequest => SocialClient.CancelFriendRequestAsync(primeId, token),
            NativeSocialFriendAction.RemoveFriend => SocialClient.RemoveFriendAsync(primeId, token),
            NativeSocialFriendAction.BlockPlayer => SocialClient.BlockPlayerAsync(primeId, token),
            NativeSocialFriendAction.UnblockPlayer => SocialClient.UnblockPlayerAsync(primeId, token),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }).ConfigureAwait(false);
        return new(result.Success, result.Status);
    }
}
#endif
