#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Social;

/// <summary>
/// The original Social presentation uses one authenticated service. This
/// first parity slice restores friends/requests/blocks only. Party/presence
/// actions are disabled until their authority and privacy services return.
/// </summary>
internal sealed class NativeSocialFriendsPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly RmlUiHost _host;
    private readonly NativeSocialFriendsController _controller;
    private RmlUiDocumentToken _page, _confirmation;
    private long _revision;
    private bool _closed, _disposed;
    public bool Closed => _closed;
    public NativeSocialFriendsPage(RmlUiHost host, RmlUiPageManager pages,
        INativeSocialFriendsService service)
    {
        _host = host; _pages = pages;
        _controller = new NativeSocialFriendsController(service);
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
        if (_controller.Current().Confirmation.Length != 0
            && action is not (46 or 47 or 52))
            return false;
        if (action == 52)
        {
            if (_controller.Current().Confirmation.Length != 0)
                _controller.Dismiss();
            else _closed = true;
        }
        else if (action == 46) _controller.Confirm();
        else if (action == 47) _controller.Dismiss();
        else if (action == 7) _controller.Refresh();
        else if (action == 8) _controller.Cancel();
        else if (action == 9) _controller.Lookup(_host.ReadField(_page, "social_lookup_id", 128));
        else if (action == 10) _controller.Search(_host.ReadField(_page, "social_filter", 256));
        else if (action is 11 or 12) _controller.Page(action == 11 ? -1 : 1);
        else if (action is >= 13 and <= 20) _controller.Select(action - 13);
        else if (action is 0 or 1 or 2 or 6) _controller.SetTab(action);
        else if (Action(action) is { } operation) _controller.Command(operation);
        else return false;

        if (!_closed) Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _closed || _page == default) return;
        NativeSocialView snapshot = _controller.Tick();
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
            ? snapshot.Total + " " + snapshot.Tab + " // SERVER VERIFIED" : "ACCOUNT UNAVAILABLE");
        Text("social_status", snapshot.Status);
        Text("social_error", snapshot.Status.StartsWith("SOCIAL UNAVAILABLE",
            StringComparison.Ordinal) ? snapshot.Status : "");
        Text("social_empty", snapshot.Total == 0
            ? snapshot.Busy ? "SYNCING ACCOUNT…" : snapshot.Connected
                ? "NO " + snapshot.Tab + " FOUND" : "SOCIAL SERVICE UNAVAILABLE" : "");
        Text("social_page_count", $"{snapshot.Page + 1} / {snapshot.Pages}");
        Bool("disabled:social_previous_page", snapshot.Busy || snapshot.Page == 0);
        Bool("disabled:social_next_page", snapshot.Busy || snapshot.Page + 1 == snapshot.Pages);
        Bool("disabled:social_cancel_refresh", !snapshot.Busy);
        Bool("disabled:social_lookup", snapshot.Busy);
        Bool("disabled:social_refresh", snapshot.Busy);
        Bool("disabled:social_search", snapshot.Busy);
        Bool("disabled:social_friends", snapshot.Busy);
        Bool("disabled:social_players", snapshot.Busy);
        Bool("disabled:social_requests", snapshot.Busy);
        Bool("disabled:social_blocked", snapshot.Busy);
        foreach (string tab in new[] { "friends", "players", "requests", "blocked" })
            Bool("class:social_" + tab + ":selected", snapshot.Tab.Equals(tab, StringComparison.OrdinalIgnoreCase));

        // Authentication and authorization for invites, party and privacy
        // have not been migrated. Explicitly disable them instead of showing
        // misleading friend counts or accepted-looking buttons.
        foreach (string id in new[] { "social_invites", "social_party", "social_recent",
            "social_presence", "social_activity", "social_invites_privacy", "social_dnd" })
            Bool("disabled:" + id, true);
        for (int i = 28; i <= 45; i++) Bool("disabled:social_command_" + i, true);

        Text("social_selected_name", snapshot.Selected?.DisplayName ?? "SELECT A PLAYER");
        Text("social_selected_prime_id", snapshot.Selected?.PrimeId ?? "");
        Text("social_selected_relation", snapshot.Selected?.Relation.ToString().ToUpperInvariant() ?? "");
        Text("social_selected_activity", "PRESENCE STATUS NOT CONNECTED");
        Text("social_selected_detail", snapshot.Selected == null
            ? "Choose a verified player from Friends, Requests, Blocked or Prime ID lookup."
            : "Social mutations are accepted by the authenticated server.");
        Text("social_selected_expiry", "");
        Text("social_party_summary", "PARTY AND PRESENCE SYNC // NOT YET MIGRATED");
        Text("social_travel_summary", "PARTY TRAVEL UNAVAILABLE IN THIS SLICE");
        Text("social_travel_members", "");
        Text("social_reservation_summary", "");
        Text("social_reserved_members", "");
        Text("social_session_notice", "GAME AND PARTY INVITES REQUIRE THE UPCOMING AUTHORITATIVE SERVICE PORT.");
        Text("social_presence_value", "UNAVAILABLE");
        Text("social_activity_value", "UNAVAILABLE");
        Text("social_invites_value", "UNAVAILABLE");
        Text("social_dnd_value", "UNAVAILABLE");
        Bool("disabled:social_confirm_apply", snapshot.Busy);
        for (int i = 21; i <= 27; i++)
            Bool("disabled:social_command_" + i,
                snapshot.Busy || !_controller.Can(Action(i)!.Value));
        for (int i = 0; i < 8; i++)
        {
            bool found = i < snapshot.Rows.Length;
            NativeSocialPerson? row = found ? snapshot.Rows[i] : null;
            Bool("visible:social_row_" + i, found);
            Bool("disabled:social_row_" + i, !found || snapshot.Busy);
            Bool("class:social_row_" + i + ":selected",
                found && row!.PrimeId == snapshot.Selected?.PrimeId);
            Text("social_row_name_" + i, row?.DisplayName ?? "");
            Text("social_row_detail_" + i,
                row is { } person ? person.Relation + " // " + person.PrimeId : "");
        }
        _pages.Present(_page, ++_revision, data);
        if (_host.IsAlive(_confirmation))
            _pages.Present(_confirmation, ++_revision, new Dictionary<string, RmlUiBindingValue>
            {
                ["social_confirmation_text"] =
                    RmlUiBindingValue.FromText(snapshot.Confirmation)
            });
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

internal sealed class CurrentSocialFriendsService : INativeSocialFriendsService
{
    public async Task<NativeSocialDirectory> ReadAsync(CancellationToken token)
    {
        SocialSnapshot state = await SocialClient.LoadAsync(token).ConfigureAwait(false);
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
        return new(state.Self.PrimeId, list.ToArray());
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
