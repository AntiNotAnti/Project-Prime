#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Social;

/// <summary>Native view of authenticated Party authority. No network session is changed here.</summary>
internal sealed class NativePartyPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativePartyAuthorityController _controller;
    private readonly bool _ownsController;
    private RmlUiDocumentToken _page, _confirmation;
    private NativePartyView _view = new(false, false, "", 0, null);
    private long _presented, _revision;
    private int _tab, _offset, _selected = -1;
    private bool _dirty = true, _disposed;
    private Pending? _pending;
    private string _travelStatus = "";
    private string _reservationPresented = "";
    private sealed record Pending(NativePartyAction Action, string Target, string Invite,
        string Self, long Revision);
    private sealed record Row(string Name, string Detail, string PrimeId, string InviteId);
    public bool Closed { get; private set; }
    public RmlUiDocumentToken Document => _page;

    public NativePartyPage(RmlUiHost host, RmlUiPageManager pages, INativePartyAuthority authority,
        NativePartyAuthorityController? controller = null)
    {
        _host = host; _pages = pages; _ownsController = controller == null;
        _controller = controller ?? new(authority);
    }

    public void Open(string invitePrimeId = "")
    {
        _page = _pages.OpenPage(new("native-party", "pages/social/native-party.rml", "party_refresh"));
        if (NativeSocialFriendsController.ValidPrimeId(invitePrimeId))
            _host.SetField(_page, "party_invite_id", invitePrimeId);
        _controller.Refresh();
        Refresh();
    }

    private Row[] Rows() => _view.Snapshot is not { } state ? Array.Empty<Row>() : _tab switch
    {
        0 => state.Members.Select(m => new Row(m.DisplayName,
            (m.PrimeId == state.LeaderPrimeId ? "LEADER // " : "MEMBER // ") + Presence(m.Presence),
            m.PrimeId, "")).ToArray(),
        1 => state.Incoming.Select(i => new Row(i.FromPrimeId, "INCOMING PARTY INVITATION",
            i.FromPrimeId, i.InviteId)).ToArray(),
        _ => state.Outgoing.Select(i => new Row(i.ToPrimeId, "OUTGOING PARTY INVITATION",
            i.ToPrimeId, i.InviteId)).ToArray()
    };
    private static string Presence(NativePresence value) => value switch
    {
        NativePresence.Online => "ONLINE", NativePresence.InLobby => "IN LOBBY",
        NativePresence.InMatch => "IN MATCH", NativePresence.Offline => "OFFLINE",
        _ => "PRESENCE NOT SHARED"
    };
    private Row? Selection => Rows().ElementAtOrDefault(_selected);
    private static NativePartyAction? Action(int index) => index switch
    {
        33 => NativePartyAction.Invite, 34 => NativePartyAction.AcceptInvite,
        35 => NativePartyAction.DeclineInvite, 36 => NativePartyAction.CancelInvite,
        37 => NativePartyAction.Leave, 38 => NativePartyAction.Disband,
        39 => NativePartyAction.Kick, 40 => NativePartyAction.Promote, _ => null
    };

    private static NativePartyLocalLobby? CurrentLobby()
        => NetSession.Active && NetSession.IsClient && NetSession.PersistentLobby
            && !NetSession.SemanticLegacyPlayback && NetSession.LocalSlot >= 0
            && NetSession.ServerSession is { Phase: SessionPhase.Lobby } session
            ? new(session.MatchId, session.AuthorityEpoch, session.Match.RoomKey, "") : null;

    public bool TryTakeJoin(out NativePartyJoinTarget target)
    {
        _view = _controller.Tick();
        return _controller.TryTakeJoin(out target);
    }
    public void ReportJoinRefused(string status)
    { _travelStatus = status; _dirty = true; Refresh(); }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || Closed || intent.Kind != RmlUiIntentKind.SocialAction
            || (intent.Document != _page && intent.Document != _confirmation) || !_pages.Accept(intent))
            return false;
        int action = intent.Argument;
        if (_pending != null && action is not (46 or 47 or 52)) return false;
        if (action == 52)
        { if (_pending != null) _pending = null; else Closed = true; }
        else if (action == 47) _pending = null;
        else if (action == 46 && _pending is { } pending)
        {
            _pending = null;
            if (_view.Snapshot is { } state && state.SelfPrimeId == pending.Self
                && state.ServerRevision == pending.Revision)
                _controller.Command(pending.Action, pending.Target, pending.Invite);
        }
        else if (action == 7)
        { _pending = null; _selected = -1; _controller.Refresh(); }
        else if (action == 8) { _pending = null; _selected = -1; _controller.Cancel(); }
        else if (action is 0 or 3 or 4 && !_view.Busy)
        { _tab = action == 0 ? 0 : action == 3 ? 1 : 2; _selected = -1; _offset = 0; }
        else if (action is 11 or 12 && !_view.Busy)
            _offset = Math.Clamp(_offset + (action == 11 ? -8 : 8), 0, Math.Max(0, ((Rows().Length - 1) / 8) * 8));
        else if (action is >= 13 and <= 20 && !_view.Busy)
            _selected = _offset + action - 13 < Rows().Length ? _offset + action - 13 : -1;
        else if (action is >= 41 and <= 45 || action == 53)
        {
            _travelStatus = "";
            _controller.Travel(action switch { 41 => NativePartyTravelAction.Publish,
                42 => NativePartyTravelAction.Follow, 43 => NativePartyTravelAction.JoinLeader,
                44 => NativePartyTravelAction.Decline, 45 => NativePartyTravelAction.CancelReservation,
                _ => NativePartyTravelAction.Reserve }, CurrentLobby());
        }
        else if (Action(action) is { } command)
        {
            string target = command == NativePartyAction.Invite
                ? _host.ReadField(_page, "party_invite_id", 128).Trim().ToUpperInvariant()
                : Selection?.PrimeId ?? "";
            string invite = Selection?.InviteId ?? "";
            if (_controller.Can(command, target, invite) && _view.Snapshot is { } state)
            {
                if (command is NativePartyAction.Leave or NativePartyAction.Disband
                    or NativePartyAction.Kick or NativePartyAction.Promote)
                    _pending = new(command, target, invite, state.SelfPrimeId, state.ServerRevision);
                else _controller.Command(command, target, invite);
            }
        }
        else return false;
        _dirty = true;
        if (!Closed) Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || Closed || _page == default) return;
        _view = _controller.Tick();
        string reservationText = ReservationText(_view.Snapshot);
        if (_reservationPresented != reservationText) { _reservationPresented = reservationText; _dirty = true; }
        if (_view.Revision != _presented)
        {
            _presented = _view.Revision; _dirty = true; _selected = -1;
            if (_pending is { } pending && (_view.Snapshot?.ServerRevision != pending.Revision
                || _view.Snapshot?.SelfPrimeId != pending.Self)) _pending = null;
        }
        if (!_dirty) return;
        _dirty = false;
        if (_pending != null && !_host.IsAlive(_confirmation))
            _confirmation = _pages.OpenModal(new("party-confirm", "pages/social/confirm.rml", "social_confirm_cancel"));
        else if (_pending == null && _host.IsAlive(_confirmation))
        { _pages.CloseModal(); _confirmation = default; }
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        NativePartySnapshot? state = _view.Snapshot;
        Text("party_self", state?.SelfPrimeId ?? "ACCOUNT NOT VERIFIED");
        Text("party_status", _travelStatus.Length == 0 ? _view.Status : _travelStatus);
        NativePartyTravel? travel = state?.Travel;
        string travelMap = travel == null ? "" : LauncherRoomDisplayName.Get(travel.RoomKey);
        Text("party_travel", travel == null ? "NO VERIFIED PARTY DESTINATION"
            : "LEADER LOBBY // " + travel.ServerName
                + (travelMap.Length == 0 ? "" : " // " + travelMap)
                + " // " + travel.SelfStatus.ToUpperInvariant());
        Text("party_reservation", reservationText);
        Bool("disabled:party_reserve", !_controller.CanTravel(NativePartyTravelAction.Reserve, CurrentLobby()));
        Bool("disabled:party_cancel_reservation", !_controller.CanTravel(NativePartyTravelAction.CancelReservation));
        Bool("disabled:party_publish", !_controller.CanTravel(NativePartyTravelAction.Publish, CurrentLobby()));
        Bool("disabled:party_follow", !_controller.CanTravel(NativePartyTravelAction.Follow));
        Bool("disabled:party_join_leader", !_controller.CanTravel(NativePartyTravelAction.JoinLeader));
        Bool("disabled:party_decline_travel", !_controller.CanTravel(NativePartyTravelAction.Decline));
        Text("party_summary", state == null ? "PARTY SERVICE UNAVAILABLE"
            : state.PartyId.Length == 0 ? "NO PARTY // INVITE A FRIEND TO CREATE ONE"
            : state.Members.Length + " / 8 MEMBERS // LEADER " + state.LeaderPrimeId);
        Row[] rows = Rows();
        if (state != null || !_view.Busy)
            _offset = Math.Clamp(_offset, 0, Math.Max(0, ((rows.Length - 1) / 8) * 8));
        Text("party_empty", rows.Length == 0 ? _view.Busy ? "VERIFYING…" : "NO ENTRIES" : "");
        Text("party_page", state == null ? "— / —"
            : (_offset / 8 + 1) + " / " + Math.Max(1, (rows.Length + 7) / 8));
        Text("party_selected", Selection?.Name ?? "SELECT A MEMBER OR INVITATION");
        Text("party_selected_id", Selection?.PrimeId ?? "");
        foreach (var (id, tab) in new[] { ("party_members", 0), ("party_incoming", 1), ("party_outgoing", 2) })
        { Bool("disabled:" + id, _view.Busy); Bool("class:" + id + ":selected", _tab == tab); }
        Bool("disabled:party_previous", _view.Busy || _offset == 0);
        Bool("disabled:party_next", _view.Busy || _offset + 8 >= rows.Length);
        Bool("disabled:party_refresh", _view.Busy);
        Bool("disabled:party_cancel", !_view.Busy);
        Bool("disabled:party_invite", _view.Busy || state == null
            || state.PartyId.Length != 0 && state.LeaderPrimeId != state.SelfPrimeId
            || state.Members.Length >= 8);
        foreach (var (index, command) in new[] { (34,NativePartyAction.AcceptInvite),
            (35,NativePartyAction.DeclineInvite),(36,NativePartyAction.CancelInvite),
            (37,NativePartyAction.Leave),(38,NativePartyAction.Disband),
            (39,NativePartyAction.Kick),(40,NativePartyAction.Promote) })
            Bool("disabled:party_command_" + index, !_controller.Can(command,
                Selection?.PrimeId ?? "", Selection?.InviteId ?? ""));
        for (int i = 0; i < 8; i++)
        {
            Row? row = rows.ElementAtOrDefault(_offset + i);
            Bool("visible:party_row_" + i, row != null);
            Bool("disabled:party_row_" + i, row == null || _view.Busy);
            Bool("class:party_row_" + i + ":selected", row != null && _selected == _offset + i);
            Text("party_name_" + i, row?.Name ?? "");
            Text("party_detail_" + i, row == null ? "" : row.Detail + " // " + row.PrimeId);
        }
        _pages.Present(_page, ++_revision, data);
        if (_host.IsAlive(_confirmation) && _pending is { } confirmation)
            _pages.Present(_confirmation, ++_revision, new Dictionary<string,RmlUiBindingValue>
            {
                ["social_confirmation_text"] = RmlUiBindingValue.FromText(confirmation.Action switch
                {
                    NativePartyAction.Leave => "LEAVE THIS PARTY? Leadership transfers to the next member if needed.",
                    NativePartyAction.Disband => "DISBAND THIS PARTY FOR ALL MEMBERS?",
                    NativePartyAction.Kick => "REMOVE " + confirmation.Target + " FROM THIS PARTY?",
                    _ => "MAKE " + confirmation.Target + " THE PARTY LEADER?"
                })
            });
    }

    public void Back()
    {
        if (_disposed || Closed) return;
        if (_pending != null) { _pending = null; _dirty = true; Refresh(); }
        else Closed = true;
    }

    private static string ReservationText(NativePartySnapshot? snapshot)
    {
        if (snapshot?.Reservation is not { } intent) return "NO GROUP SEAT INTENT";
        if (intent.CancellationPending) return "CANCELLATION PENDING // COMMITTED ADMISSION MAY SETTLE";
        if (!intent.Active || intent.ExpiresAt <= DateTimeOffset.UtcNow) return "GROUP SEAT INTENT CLOSED";
        if (NetSession.CaptureNativePartyReservation(intent.RequestId, snapshot.AccountWitness) is not { } live)
            return "GROUP SEAT INTENT PUBLISHED // WAITING FOR LIVE SERVER";
        return live.State switch
        {
            PartyReservationWireState.Reserved when NetSession.NativePartyReservationSeconds > 0
                => live.ReservedCount + " GROUP SEATS RESERVED // " + NetSession.NativePartyReservationSeconds + "s // NOT JOINED",
            PartyReservationWireState.Waiting => "WAITING FOR ALL PARTY SEATS // QUEUE " + live.Position,
            PartyReservationWireState.Activating => "VERIFYING BACKEND SEAT ACTIVATION",
            PartyReservationWireState.Verifying => "VERIFYING GROUP INTENT WITH SERVER",
            PartyReservationWireState.Admitting => "ADMISSION CLAIM IN PROGRESS // NOT JOINED",
            _ => "GROUP SEAT LEASE CLOSED // REFRESH PARTY"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _pending = null; if (_ownsController) _controller.Dispose();
        if (_host.IsAlive(_confirmation)) _pages.CloseModal();
        if (_host.IsAlive(_page) && _pages.Page == _page) _pages.ClosePage();
    }
}
#endif
