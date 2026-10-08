#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static bool _nativeSocialJoin;
    private static (bool Active, bool Lobby, bool Playing)? _nativeSocialSession;
    private static long _nativeNextSocialSummary;

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
        string status = $"{summary.FriendsOnline} FRIENDS ONLINE // {summary.IncomingGameInvites + summary.IncomingPartyInvites} INVITES";
        if (summary.DirectoryLoaded) status += $" // {summary.IncomingRequests} REQUESTS";
        if (summary.TravelPending) status += " // PARTY TRAVEL WAITING";
        if (summary.DoNotDisturb) status += " // DO NOT DISTURB";
        RmlUiPrototype.SetMenuText("home_party_state", party);
        RmlUiPrototype.SetMenuText("home_social_summary", status);
        RmlUiPrototype.SetMenuText("home_friends_online", summary.FriendsOnline.ToString());
        RmlUiPrototype.SetMenuText("home_invites_count", summary.InvitationCount.ToString());
        RmlUiPrototype.SetMenuText("home_requests_count", summary.IncomingRequests.ToString());

        int friendRows = Math.Min(summary.Friends.Length, 3);
        RmlUiPrototype.SetMenuBool("home_social_empty", friendRows == 0);
        RmlUiPrototype.SetMenuText("home_social_empty_text",
            summary.DirectoryLoaded
                ? "NO FRIENDS ONLINE // SOCIAL LINK STANDING BY"
                : "SOCIAL DIRECTORY SYNCING // LINK STANDING BY");
        for (int i = 0; i < 3; i++)
        {
            bool visible = i < friendRows;
            RmlUiPrototype.SetMenuBool($"home_friend{i}_visible", visible);
            if (!visible) continue;

            SocialHomeFriend friend = summary.Friends[i];
            string friendState = String.IsNullOrWhiteSpace(friend.Activity)
                ? "ONLINE"
                : friend.Activity.Trim().ToUpperInvariant();
            if (friend.Joinable) friendState += " // JOINABLE";
            RmlUiPrototype.SetMenuText($"home_friend{i}_name",
                friend.DisplayName.Trim().ToUpperInvariant());
            RmlUiPrototype.SetMenuText($"home_friend{i}_state", friendState);
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
