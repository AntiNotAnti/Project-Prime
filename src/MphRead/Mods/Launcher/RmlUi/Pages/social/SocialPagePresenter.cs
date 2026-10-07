using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Social;

/// <summary>Borrowed document composition; immutable row witnesses and all service mutations belong to SocialController.</summary>
public sealed class SocialPagePresenter : IDisposable
{
    private static readonly (int Argument, SocialCommand Command)[] Commands =
    {
        (21, SocialCommand.SendFriendRequest), (22, SocialCommand.AcceptFriendRequest),
        (23, SocialCommand.DeclineFriendRequest), (24, SocialCommand.CancelFriendRequest),
        (25, SocialCommand.RemoveFriend), (26, SocialCommand.BlockPlayer), (27, SocialCommand.UnblockPlayer),
        (28, SocialCommand.SendGameInvite), (29, SocialCommand.JoinFriend), (30, SocialCommand.AcceptGameInvite),
        (31, SocialCommand.DeclineGameInvite), (32, SocialCommand.CancelGameInvite), (33, SocialCommand.InviteToParty),
        (34, SocialCommand.AcceptPartyInvite), (35, SocialCommand.DeclinePartyInvite), (36, SocialCommand.CancelPartyInvite),
        (37, SocialCommand.LeaveParty), (38, SocialCommand.DisbandParty), (39, SocialCommand.KickPartyMember),
        (40, SocialCommand.PromotePartyMember), (41, SocialCommand.InvitePartyToLobby), (42, SocialCommand.FollowPartyTravel),
        (43, SocialCommand.JoinPartyLeader), (44, SocialCommand.DeclinePartyTravel), (45, SocialCommand.CancelReservation)
    };
    private static readonly string[] TabNames = { "friends", "players", "requests", "invites", "party", "recent", "blocked" };
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly SocialController _controller;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _document, _confirmation;
    private ulong _presentedVersion;
    private long _bindingVersion;
    private string _selfPrimeId = "";
    private bool _disposed;
    public RmlUiDocumentToken Document => _document;
    public SocialController Controller => _controller;
    public event Action? Closed;

    public SocialPagePresenter(RmlUiHost host, RmlUiPageManager pages, SocialController controller)
    {
        (_host, _pages, _controller) = (host ?? throw new ArgumentNullException(nameof(host)),
            pages ?? throw new ArgumentNullException(nameof(pages)), controller ?? throw new ArgumentNullException(nameof(controller)));
        _host.VerifyOwnerThread();
        _ = controller.Snapshot;
    }

    public void Open(bool refresh = true)
    {
        _host.VerifyOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_document == default)
        {
            _document = _pages.OpenPage(new("social", "pages/social/social.rml", "social_friends"));
            _retirement = _pages.Lifetime(_document).Register(OnRetired);
        }
        if (refresh) _controller.Refresh();
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (_disposed || intent.Kind != RmlUiIntentKind.SocialAction
            || (intent.Document != _document && intent.Document != _confirmation)) return false;
        if (!_pages.Accept(intent)) return true;
        if (intent.Document == _confirmation)
        {
            if (intent.Argument == 46) _controller.Confirm();
            else if (intent.Argument == 47 || intent.Argument == 52) _controller.DismissConfirmation();
            else return true;
            Refresh();
            return true;
        }
        int action = intent.Argument;
        if (action is >= 0 and <= 6) _controller.SetTab((SocialTab)action);
        else if (action is >= 13 and <= 20) _controller.SelectRow(action - 13);
        else if (Commands.FirstOrDefault(command => command.Argument == action) is var mapped && mapped.Argument != 0)
            _controller.Dispatch(_controller.Intent(mapped.Command));
        else switch (action)
        {
            case 7: _controller.Refresh(); break;
            case 8: _controller.Cancel(); break;
            case 9: _controller.Lookup(_host.ReadField(_document, "social_lookup_id")); break;
            case 10: _controller.SetFilter(_host.ReadField(_document, "social_filter")); break;
            case 11: _controller.ChangePage(-1); break;
            case 12: _controller.ChangePage(1); break;
            case 46: _controller.Confirm(); break;
            case 47: _controller.DismissConfirmation(); break;
            case 48: SetPrivacy(p => p with { Presence = (p.Presence + 1) % 3 }); break;
            case 49: SetPrivacy(p => p with { Activity = (p.Activity + 1) % 3 }); break;
            case 50: SetPrivacy(p => p with { Invites = (p.Invites + 1) % 3 }); break;
            case 51: SetPrivacy(p => p with { DoNotDisturb = !p.DoNotDisturb }); break;
            case 52: Back(); break;
            default: return false;
        }
        Refresh();
        return true;
    }

    private void SetPrivacy(Func<SocialPrivacy, SocialPrivacy> update) => _controller.SetPrivacy(update(_controller.Snapshot.Privacy));

    public bool Back()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return false;
        if (_controller.Snapshot.PendingConfirmation != null) _controller.DismissConfirmation();
        else if (_controller.Snapshot.Busy) _controller.Cancel();
        else Closed?.Invoke();
        Refresh();
        return true;
    }

    public void Refresh()
    {
        _host.VerifyOwnerThread();
        if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document) || _pages.Page != _document) { Dispose(); return; }
        _controller.Pump();
        SocialViewSnapshot snapshot = _controller.Snapshot;
        if (snapshot.PendingConfirmation is { } pending && !_host.IsAlive(_confirmation))
            _confirmation = _pages.OpenModal(new("social-confirm", "pages/social/confirm.rml", "social_confirm_cancel"));
        else if (snapshot.PendingConfirmation == null && _host.IsAlive(_confirmation))
        {
            _pages.CloseModal();
            _confirmation = default;
        }
        if (snapshot.Version == _presentedVersion) return;
        if (snapshot.Data.SelfPrimeId != _selfPrimeId)
        {
            _host.SetField(_document, "social_self_id", snapshot.Data.SelfPrimeId);
            _selfPrimeId = snapshot.Data.SelfPrimeId;
        }
        var bindings = new Dictionary<string, RmlUiBindingValue>();
        Text(bindings, "social_status", snapshot.Status);
        Text(bindings, "social_error", snapshot.CommandError);
        Text(bindings, "social_counts", $"{snapshot.OnlineCount} ONLINE // {snapshot.FriendsOnline} FRIENDS ONLINE // {snapshot.IncomingRequests} REQUESTS // {snapshot.IncomingInvites} INVITES");
        Text(bindings, "social_page_count", $"PAGE {snapshot.PageIndex + 1} OF {snapshot.PageCount} // {snapshot.TotalRows} RESULTS");
        Text(bindings, "social_empty", snapshot.Data.Connected ? "No players or invitations match this section and search."
            : "Social is offline. Refresh when connected; your privacy choices remain available.");
        Bool(bindings, "visible:social_empty", snapshot.VisibleRows.IsEmpty);
        Bool(bindings, "visible:social_error", snapshot.CommandError.Length > 0);
        Bool(bindings, "disabled:social_previous_page", snapshot.PageIndex == 0);
        Bool(bindings, "disabled:social_next_page", snapshot.PageIndex + 1 >= snapshot.PageCount);
        Bool(bindings, "disabled:social_refresh", snapshot.Busy && !snapshot.CanCancel);
        Bool(bindings, "disabled:social_cancel_refresh", !snapshot.CanCancel);
        Bool(bindings, "disabled:social_lookup", snapshot.Busy);
        for (int tab = 0; tab < TabNames.Length; tab++)
            Bool(bindings, "class:social_" + TabNames[tab] + ":selected", tab == (int)snapshot.Tab);
        for (int index = 0; index < SocialController.PageSize; index++)
        {
            bool visible = index < snapshot.VisibleRows.Length;
            Bool(bindings, "visible:social_row_" + index, visible);
            if (!visible) continue;
            SocialRow row = snapshot.VisibleRows[index];
            Text(bindings, "social_row_name_" + index, row.DisplayName.Length == 0 ? row.PrimeId : row.DisplayName);
            Text(bindings, "social_row_detail_" + index, row.Relation + " // " + row.Activity);
            Bool(bindings, "class:social_row_" + index + ":selected", row.Key == snapshot.SelectedRow?.Key);
        }
        SocialRow? selected = snapshot.SelectedRow;
        Text(bindings, "social_selected_name", selected?.DisplayName ?? "SELECT A PLAYER OR INVITATION");
        Text(bindings, "social_selected_prime_id", selected?.PrimeId ?? "");
        Text(bindings, "social_selected_relation", selected?.Relation ?? "");
        Text(bindings, "social_selected_activity", selected?.Activity ?? "");
        Text(bindings, "social_selected_detail", selected?.Detail ?? "Select a row to review the available actions.");
        Text(bindings, "social_selected_expiry", selected?.ExpiresAt is { } expiry ? "EXPIRES " + LocalTime(expiry) : "");
        foreach (var command in Commands)
        {
            bool available = snapshot.AvailableCommands.Contains(command.Command);
            Bool(bindings, "visible:social_command_" + command.Argument, available);
            Bool(bindings, "disabled:social_command_" + command.Argument, !available || snapshot.Busy);
        }
        Text(bindings, "social_party_summary", snapshot.Party is { } party
            ? $"{party.MemberCount} MEMBERS // {(party.IsLeader ? "YOU ARE PARTY LEADER" : "FOLLOWING PARTY LEADER")}" : "You are not in a party.");
        Text(bindings, "social_travel_summary", snapshot.Travel is { } travel
            ? $"{travel.ServerName} // {travel.RoomKey}\nLEADER {travel.LeaderName} // {travel.Reason}\nYOUR STATUS {travel.SelfStatus}\nEXPIRES {LocalTime(travel.ExpiresAt)}" : "No party travel is pending.");
        Text(bindings, "social_travel_members", snapshot.Travel is { } travelling
            ? String.Join("\n", travelling.Members.Select(member => member.DisplayName + (member.IsSelf ? " (YOU)" : "") + " // " + member.Status)) : "");
        Text(bindings, "social_reservation_summary", snapshot.Reservation is { } reservation
            ? $"{reservation.Status} // {reservation.RequestedCount} RESERVED SEATS\nEXPIRES {LocalTime(reservation.ExpiresAt)}" : "No party seats are reserved.");
        Text(bindings, "social_reserved_members", snapshot.Reservation is { } reserved
            ? String.Join("\n", reserved.Members.Select(member => member.DisplayName + (member.IsSelf ? " (YOU)" : "")
                + " // " + member.Status + (member.Slot is { } slot ? " // SLOT " + (slot + 1) : ""))) : "");
        Text(bindings, "social_session_notice", snapshot.IsSessionActive
            ? "Leave your current session before joining another lobby. Lobby invitations remain available when your session permits them." : "Join actions use the current verified player or party destination.");
        Text(bindings, "social_presence_value", PrivacyLabel(snapshot.Privacy.Presence, "HIDDEN"));
        Text(bindings, "social_activity_value", PrivacyLabel(snapshot.Privacy.Activity, "PRIVATE"));
        Text(bindings, "social_invites_value", PrivacyLabel(snapshot.Privacy.Invites, "NOBODY"));
        Text(bindings, "social_dnd_value", snapshot.Privacy.DoNotDisturb ? "ON" : "OFF");
        foreach (string id in new[] { "presence", "activity", "invites_privacy", "dnd" }) Bool(bindings, "disabled:social_" + id, snapshot.Busy);
        _pages.Present(_document, ++_bindingVersion, bindings);
        if (snapshot.PendingConfirmation is { } confirmation && _host.IsAlive(_confirmation))
            _pages.Present(_confirmation, ++_bindingVersion, new Dictionary<string, RmlUiBindingValue>
            {
                ["social_confirmation_text"] = RmlUiBindingValue.FromText(confirmation.Message),
                ["disabled:social_confirm_apply"] = RmlUiBindingValue.FromBoolean(snapshot.Busy)
            });
        _presentedVersion = snapshot.Version;
    }

    private static string PrivacyLabel(int value, string last) => value switch { 0 => "EVERYONE", 1 => "FRIENDS", 2 => last, _ => "UNKNOWN" };
    private static string LocalTime(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.InvariantCulture);
    private static void Text(Dictionary<string, RmlUiBindingValue> bindings, string id, string value) => bindings[id] = RmlUiBindingValue.FromText(value);
    private static void Bool(Dictionary<string, RmlUiBindingValue> bindings, string id, bool value) => bindings[id] = RmlUiBindingValue.FromBoolean(value);

    private void OnRetired() { _disposed = true; _controller.Dispose(); Closed = null; }
    public void Dispose()
    {
        _host.VerifyOwnerThread();
        _retirement.Dispose();
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        if (_pages.Page == _document && !_pages.ClosePage()) throw new InvalidOperationException("The Social page could not close.");
        _document = _confirmation = default;
        Closed = null;
    }
}
