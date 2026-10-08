#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static bool _nativeSocialJoin;
    private static (bool Active, bool Lobby, bool Playing)? _nativeSocialSession;
    private static long _nativeNextSocialSummary;
    // The News route and Home teaser share the same bundled source; never invent event schedules.
    private static readonly NewsDispatch? _homeFeaturedDispatch = new BundledNewsProvider().Read().FirstOrDefault();

    private static void CancelNativeSocialJoin()
    {
        if (_nativeSocialJoin) _rmlMultiplayer?.Cancel();
        _nativeSocialJoin = false;
    }

    private static void TickNativeSocial()
    {
        var session = (NetSession.Active, NetSession.IsInLobby, NetSession.IsPlaying);
        if (_nativeSocialSession != session)
        {
            _nativeSocialSession = session;
            if (!RmlUiPrototype.CaptureRequested && !LauncherUiPerformance.Enabled) SocialRuntime.NotifySessionChanged();
        }
        long now = Environment.TickCount64;
        if (now >= _nativeNextSocialSummary)
        {
            _nativeNextSocialSummary = now + 250;
            SocialHomeSummary summary = SocialRuntime.Summary;
            string party = summary.Party is { } group
                ? $"PARTY // {group.MemberCount} MEMBERS" : "FRIENDS AND PARTY";
            string status = $"{summary.FriendsOnline} FRIENDS ONLINE // {summary.InvitationCount} INVITES";
            if (summary.DirectoryLoaded) status += $" // {summary.IncomingRequests} REQUESTS";
            if (summary.TravelPending) status += " // PARTY TRAVEL WAITING";
            if (summary.DoNotDisturb) status += " // DO NOT DISTURB";
            RmlUiPrototype.SetMenuText("home_party_state", party);
            RmlUiPrototype.SetMenuText("home_social_summary", status);
            RmlUiPrototype.SetMenuText("home_session_state", summary.Party == null ? "SOLO" : "PARTY");

            RmlUiPrototype.SetMenuBool("home_feature_available", _homeFeaturedDispatch != null);
            RmlUiPrototype.SetMenuText("home_feature_category", _homeFeaturedDispatch?.Category ?? "");
            RmlUiPrototype.SetMenuText("home_feature_title", _homeFeaturedDispatch?.Title ?? "");
            RmlUiPrototype.SetMenuText("home_feature_summary", _homeFeaturedDispatch?.Summary ?? "");

            int shownFriends = summary.DirectoryLoaded && !summary.Friends.IsDefault
                ? Math.Min(3, summary.Friends.Length) : 0;
            bool hasInvitations = summary.UnreadCount > 0;
            RmlUiPrototype.SetMenuBool("home_social_rail_visible",
                shownFriends > 0 || summary.Party != null || hasInvitations || summary.TravelPending);
            RmlUiPrototype.SetMenuBool("home_social_alert_visible", hasInvitations);
            RmlUiPrototype.SetMenuText("home_social_count", $"{summary.FriendsOnline} ONLINE");
            string alert = summary.InvitationCount > 0
                ? $"{summary.InvitationCount} INVITE{(summary.InvitationCount == 1 ? "" : "S")}" : "";
            if (summary.IncomingRequests > 0)
                alert += (alert.Length > 0 ? " // " : "")
                    + $"{summary.IncomingRequests} REQUEST{(summary.IncomingRequests == 1 ? "" : "S")}";
            RmlUiPrototype.SetMenuText("home_social_alert_text", alert);
            RmlUiPrototype.SetMenuBool("home_party_preview_visible", summary.Party != null);
            RmlUiPrototype.SetMenuText("home_party_preview_name", party);
            RmlUiPrototype.SetMenuText("home_party_preview_role", summary.TravelPending
                ? "PARTY TRAVEL AWAITING RESPONSE"
                : summary.Party is { IsLeader: true } ? "YOU ARE PARTY LEADER" : "PARTY MEMBER");
            RmlUiPrototype.SetMenuBool("home_friends_visible", shownFriends > 0);
            RmlUiPrototype.SetMenuText("home_friend_count", $"{summary.FriendsOnline} ONLINE");

            for (int i = 0; i < 3; i++)
            {
                bool visible = i < shownFriends;
                SocialHomeFriend? friend = visible ? summary.Friends[i] : null;
                string prefix = "home_friend" + i;
                RmlUiPrototype.SetMenuBool(prefix + "_visible", visible);
                RmlUiPrototype.SetMenuBool(prefix + "_joinable", friend?.Joinable ?? false);
                RmlUiPrototype.SetMenuText(prefix + "_name", friend?.DisplayName ?? "");
                RmlUiPrototype.SetMenuText(prefix + "_activity",
                    String.IsNullOrWhiteSpace(friend?.Activity) ? "ONLINE" : friend.Activity);
                RmlUiPrototype.SetMenuText(prefix + "_status", friend?.Joinable == true ? "JOINABLE" : "ONLINE");
            }
        }

        if (_nativeSocial?.Controller.TryTakeJoin(out SocialJoinRequest? request) != true || request == null) return;
        var presenter = _nativeSocial;
        var document = presenter.Document;
        _nativeSocialJoin = true;
        EnsureRmlMultiplayer().JoinSocial(request, error =>
        {
            _nativeSocialJoin = false;
            if (!ReferenceEquals(_nativeSocial, presenter) || !RmlUiPrototype.Runtime.IsAlive(document)) return;
            presenter.Controller.ReportJoinFailure(error);
            presenter.Refresh();
        });
    }
}
#endif
