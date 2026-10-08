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
