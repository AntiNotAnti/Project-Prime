#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    private readonly object _socialJoinGate = new();
    private SocialJoinCandidate? _socialJoin;
    private sealed class SocialJoinCandidate
    {
        internal readonly NativeSocialJoinTarget Target;
        internal readonly NetClientJoinOwnership Ownership = new();
        internal readonly CancellationTokenSource Cancel;
        internal LaunchPlan Plan;
        internal bool Ready;
        internal ushort MatchId;
        internal SocialJoinCandidate(NativeSocialJoinTarget target, CancellationToken token)
            => (Target, Cancel) = (target, CancellationTokenSource.CreateLinkedTokenSource(token));
    }
    private static bool CurrentSocialTarget(NativeSocialJoinTarget target)
        => HunterLicenseClient.IsNativeAccountLeaseCurrent(target.AccountWitness)
            && NativeSocialInvitationValidation.ValidTarget(target, DateTimeOffset.UtcNow)
            && target.Protocol == NetConfig.ProtocolVersion;
    private static bool AcceptedSocialSession(SocialJoinCandidate candidate)
        => NetSession.Active && NetSession.IsClient && NetSession.PersistentLobby && NetSession.LocalSlot >= 0
            && NetSession.ServerSession is { Phase: SessionPhase.Lobby } session
            && session.AuthorityEpoch == candidate.Target.AuthorityEpoch
            && session.Match.RoomKey == candidate.Target.RoomKey && session.MatchId == candidate.MatchId;

    public async Task<RmlUiJoinResult> JoinSocial(NativeSocialJoinTarget target, string playerName, CancellationToken token)
    {
        if (!GameFiles.Ready || !CurrentSocialTarget(target))
            return new(false, default, "SOCIAL ACCOUNT OR DESTINATION EXPIRED // REFRESH SOCIAL");
        SocialJoinCandidate candidate;
        lock (_socialJoinGate)
        {
            if (_socialJoin != null || NetSession.Active)
                return new(false, default, "LEAVE THE CURRENT SESSION BEFORE JOINING A FRIEND");
            _socialJoin = candidate = new(target, token);
        }
        bool success = false;
        try
        {
            var cancellation = candidate.Cancel.Token;
            var live = await ServerBrowserService.VerifySocialLobbyAsync(target.Host, target.Port,
                target.AuthorityEpoch, target.Protocol, target.RoomKey, cancellation).ConfigureAwait(false);
            if (!live.Verified) return new(false, default, live.Error);
            candidate.MatchId = live.Entry.Status.MatchId;
            cancellation.ThrowIfCancellationRequested();
            if (!CurrentSocialTarget(target)) return new(false, default, "SOCIAL DESTINATION EXPIRED");
            var hub = HubState.Capture();
            string name = String.IsNullOrWhiteSpace(playerName) ? hub.PlayerName : playerName.Trim();
            bool admitted = await Task.Run(() => NetLaunch.Connect(target.Host, target.Port, name,
                hub.DisplayHunter, color: hub.Suit, cancellationToken: cancellation, ownership: candidate.Ownership,
                initializeFence: initialize => HunterLicenseClient.ExecuteNativeLeaseActionAsync(
                    target.AccountWitness, initialize, cancellation).GetAwaiter().GetResult()), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            lock (_socialJoinGate)
            {
                if (!admitted || !ReferenceEquals(_socialJoin, candidate) || !candidate.Ownership.IsCurrent
                    || !CurrentSocialTarget(target) || !AcceptedSocialSession(candidate))
                    return new(false, default, admitted ? "SOCIAL DESTINATION CHANGED DURING ADMISSION" : NetLaunch.LastJoinError);
                candidate.Plan = new() { Kind = LaunchKind.Online, Hunter = NetSession.LocalHunter,
                    PlayerName = NetSession.PlayerName, RoomKey = "", Mode = GameMode.Battle, Port = target.Port,
                    Lobby = new LobbyContext(live.Entry.Name, target.Host + ":" + target.Port) };
                candidate.Ready = success = true;
                return new(true, candidate.Plan, "");
            }
        }
        catch (OperationCanceledException) { return new(false, default, "SOCIAL JOIN CANCELLED"); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(false, default, "SOCIAL DESTINATION COULD NOT BE VERIFIED // REFRESH SOCIAL"); }
        finally
        {
            if (!success)
            {
                lock (_socialJoinGate) if (ReferenceEquals(_socialJoin, candidate)) _socialJoin = null;
                candidate.Ownership.TryStopCurrent(); candidate.Cancel.Dispose();
            }
        }
    }

    public bool CanAcceptSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan)
    {
        lock (_socialJoinGate) return _socialJoin is { Ready: true } candidate
            && ReferenceEquals(candidate.Target, target) && candidate.Plan.Equals(plan)
            && plan.Lobby != null && ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)
            && !candidate.Cancel.IsCancellationRequested && candidate.Ownership.IsCurrent
            && CurrentSocialTarget(target) && AcceptedSocialSession(candidate);
    }
    public void DiscardSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan)
    {
        SocialJoinCandidate? candidate;
        lock (_socialJoinGate)
        {
            candidate = _socialJoin;
            if (candidate == null || !candidate.Ready || !ReferenceEquals(candidate.Target, target)
                || plan.Lobby == null || !ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)) return;
            _socialJoin = null; candidate.Cancel.Cancel();
        }
        candidate.Ownership.TryStopCurrent(); candidate.Cancel.Dispose();
    }
    public void CancelSocialJoin()
    {
        SocialJoinCandidate? candidate;
        lock (_socialJoinGate)
        {
            candidate = _socialJoin;
            if (candidate == null) return;
            _socialJoin = null; candidate.Cancel.Cancel();
        }
        candidate.Ownership.TryStopCurrent();
        if (candidate.Ready) candidate.Cancel.Dispose();
    }
    public void NoteSocialJoined(NativeSocialJoinTarget target)
    {
        SocialJoinCandidate? candidate;
        lock (_socialJoinGate)
        {
            candidate = _socialJoin;
            if (candidate == null || !candidate.Ready || !ReferenceEquals(candidate.Target, target)) return;
            _socialJoin = null;
        }
        candidate.Cancel.Dispose();
        // Accepted transport now belongs to the established lobby coordinator.
        // A later account retirement must not stop that accepted session.
        if (!candidate.Ownership.IsCurrent || !CurrentSocialTarget(target)) return;
        LauncherPrefs.PlayerName = candidate.Plan.PlayerName;
        LauncherPrefs.LastHunter = candidate.Plan.Hunter; LauncherPrefs.LastColor = NetSession.LocalColor;
        LauncherPrefs.ServerAddress = target.Host; LauncherPrefs.ServerPort = target.Port;
        LauncherPrefs.LastKind = (int)LaunchKind.Online; LauncherPrefs.Save();
    }
}
#endif
