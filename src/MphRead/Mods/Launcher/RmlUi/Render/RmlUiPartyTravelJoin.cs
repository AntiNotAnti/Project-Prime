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
    private readonly object _partyJoinGate = new();
    private PartyJoinCandidate? _partyJoin;
    private sealed class PartyJoinCandidate
    {
        internal readonly NativePartyJoinTarget Target;
        internal readonly NetClientJoinOwnership Ownership = new();
        internal readonly CancellationTokenSource Cancel;
        internal LaunchPlan Plan;
        internal bool Ready;
        internal LobbyQueueClient? Queue;
        internal NativePartyQueueSnapshot QueueSnapshot = NativePartyQueueSnapshot.Empty;
        internal (NativePartyQueueCommand Action, uint Revision)? QueueCommand;
        internal long AttemptId;
        internal ushort ExpectedMatch;
        internal string QueueError = "";
        internal PartyJoinCandidate(NativePartyJoinTarget target, CancellationToken token)
            => (Target, Cancel) = (target, CancellationTokenSource.CreateLinkedTokenSource(token));
    }

    public async Task<RmlUiJoinResult> JoinParty(NativePartyJoinTarget target, string playerName, CancellationToken token)
    {
        if (!GameFiles.Ready || !NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)
            || !AuthenticatedNativePartyAuthority.IsCurrent(target))
            return new(false, default, "PARTY ACCOUNT OR DESTINATION CHANGED // REFRESH PARTY");
        PartyJoinCandidate candidate;
        lock (_partyJoinGate)
        {
            if (_partyJoin != null || NetSession.Active)
                return new(false, default, "LEAVE THE CURRENT SESSION BEFORE JOINING THE PARTY");
            _partyJoin = candidate = new(target, token);
        }
        bool success = false;
        try
        {
            CancellationToken cancellation = candidate.Cancel.Token;
            var authority = new AuthenticatedNativePartyAuthority();
            var verified = await authority.TravelAsync(new(NativePartyTravelAction.Follow,
                target.SelfPrimeId, target.PartyId, target.PartyRevision, target.TravelId, target.TravelRevision)
                    { AccountWitness = target.AccountWitness }, cancellation).ConfigureAwait(false);
            if (verified.JoinTarget is not { } current || !SameDestination(target, current)
                || !AuthenticatedNativePartyAuthority.IsCurrent(target))
                return new(false, default, "PARTY DESTINATION CHANGED // REFRESH PARTY");
            var live = await ServerBrowserService.VerifySocialLobbyAsync(target.Host, target.Port,
                target.AuthorityEpoch, target.Protocol, target.RoomKey, cancellation).ConfigureAwait(false);
            if (!live.Verified) return new(false, default, live.Error);
            NativePartyReservation? reservation = current.Reservation ?? target.Reservation;
            if (live.Entry.Status.PartyReservationsV1 || reservation != null)
            {
                if (!live.Entry.Status.PartyReservationsV1)
                    return new(false, default, "GROUP RESERVATIONS ARE UNAVAILABLE // REFRESH PARTY");
                var fresh = await authority.ReadReservationAsync(target, live.Entry.Status.MatchId, cancellation).ConfigureAwait(false);
                if (reservation != null && (fresh == null || fresh.RequestId != reservation.RequestId))
                    return new(false, default, "PARTY RESERVATION CHANGED // REFRESH PARTY");
                reservation = fresh;
                if (reservation != null && (!reservation.Active || reservation.ExpiresAt <= DateTimeOffset.UtcNow))
                    return new(false, default, "PARTY RESERVATION EXPIRED // REFRESH PARTY");
            }
            cancellation.ThrowIfCancellationRequested();
            if (!AuthenticatedNativePartyAuthority.IsCurrent(target)
                || !NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow))
                return new(false, default, "PARTY ACCOUNT OR DESTINATION EXPIRED");
            HubSnapshot hub = HubState.Capture();
            string name = String.IsNullOrWhiteSpace(playerName) ? hub.PlayerName : playerName.Trim();
            bool admitted;
            if (reservation != null)
            {
                string ticket = await AuthenticatedNativePartyAuthority.WaitForPartyTicket(target.AccountWitness, cancellation).ConfigureAwait(false);
                candidate.ExpectedMatch = reservation.MatchId;
                admitted = await Task.Run(() => RunPartyQueue(candidate, reservation, ticket, name, hub, cancellation), cancellation).ConfigureAwait(false);
            }
            else admitted = await Task.Run(() => NetLaunch.Connect(target.Host, target.Port, name,
                hub.DisplayHunter, color: hub.Suit, cancellationToken: cancellation,
                ownership: candidate.Ownership, initializeFence: initialize => HunterLicenseClient.ExecuteNativeLeaseActionAsync(
                    target.AccountWitness, initialize, cancellation).GetAwaiter().GetResult()), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            lock (_partyJoinGate)
            {
                if (!admitted || !ReferenceEquals(_partyJoin, candidate) || !candidate.Ownership.IsCurrent
                    || !AcceptedPartySession(target, candidate.ExpectedMatch) || !AuthenticatedNativePartyAuthority.IsCurrent(target)
                    || !NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow))
                    return new(false, default, admitted ? "PARTY DESTINATION CHANGED DURING ADMISSION"
                        : candidate.QueueError.Length == 0 ? NetLaunch.LastJoinError : candidate.QueueError);
                candidate.Plan = new() { Kind = LaunchKind.Online, Hunter = NetSession.LocalHunter,
                    PlayerName = NetSession.PlayerName, RoomKey = "", Mode = GameMode.Battle, Port = target.Port,
                    Lobby = new LobbyContext(live.Entry.Name, target.Host + ":" + target.Port) };
                candidate.Ready = success = true;
                return new(true, candidate.Plan, "");
            }
        }
        catch (OperationCanceledException) { return new(false, default, "PARTY JOIN CANCELLED"); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(false, default, "PARTY DESTINATION COULD NOT BE VERIFIED // REFRESH PARTY"); }
        finally
        {
            if (!success)
            {
                lock (_partyJoinGate) if (ReferenceEquals(_partyJoin, candidate)) _partyJoin = null;
                candidate.Ownership.TryStopCurrent();
                candidate.Cancel.Dispose();
            }
        }
    }

    private static bool SameDestination(NativePartyJoinTarget a, NativePartyJoinTarget b)
        => a.SelfPrimeId == b.SelfPrimeId && a.PartyId == b.PartyId && a.PartyRevision == b.PartyRevision
        && a.TravelId == b.TravelId && a.TravelRevision == b.TravelRevision && a.LobbyId == b.LobbyId
        && a.Host == b.Host && a.Port == b.Port && a.Protocol == b.Protocol
        && a.AuthorityEpoch == b.AuthorityEpoch && a.RoomKey == b.RoomKey
        && a.Reservation?.RequestId == b.Reservation?.RequestId
        && a.Reservation?.OperationGeneration == b.Reservation?.OperationGeneration
        && a.Reservation?.PartyRevision == b.Reservation?.PartyRevision
        && a.Reservation?.MatchId == b.Reservation?.MatchId;
    private static bool AcceptedPartySession(NativePartyJoinTarget target, ushort expectedMatch = 0)
        => NetSession.Active && NetSession.IsClient && NetSession.PersistentLobby && NetSession.LocalSlot >= 0
        && NetSession.ServerSession is { Phase: SessionPhase.Lobby } session
        && session.AuthorityEpoch == target.AuthorityEpoch && session.Match.RoomKey == target.RoomKey
        && (expectedMatch == 0 || session.MatchId == expectedMatch);

    public bool CanAcceptPartyJoin(NativePartyJoinTarget target, LaunchPlan plan)
    {
        lock (_partyJoinGate) return _partyJoin is { Ready: true } candidate
            && ReferenceEquals(candidate.Target, target) && candidate.Plan.Equals(plan)
            && plan.Lobby != null && ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)
            && !candidate.Cancel.IsCancellationRequested && candidate.Ownership.IsCurrent
            && NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)
            && AuthenticatedNativePartyAuthority.IsCurrent(target) && AcceptedPartySession(target, candidate.ExpectedMatch);
    }

    public void DiscardPartyJoin(NativePartyJoinTarget target, LaunchPlan plan)
    {
        PartyJoinCandidate? candidate;
        lock (_partyJoinGate)
        {
            candidate = _partyJoin;
            if (candidate == null || !candidate.Ready || !ReferenceEquals(candidate.Target, target)
                || plan.Lobby == null || !ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)) return;
            _partyJoin = null;
            candidate.Cancel.Cancel();
        }
        candidate.Ownership.TryStopCurrent();
        // The worker disposes its CTS on failure; successful DTOs have no worker left.
        if (candidate.Ready) candidate.Cancel.Dispose();
    }
    public void CancelPartyJoin()
    {
        PartyJoinCandidate? candidate;
        lock (_partyJoinGate)
        {
            candidate = _partyJoin;
            if (candidate == null) return;
            _partyJoin = null;
            candidate.Cancel.Cancel();
        }
        candidate.Ownership.TryStopCurrent();
        if (candidate.Ready) candidate.Cancel.Dispose();
    }
    public void NotePartyJoined(NativePartyJoinTarget target)
    {
        PartyJoinCandidate? candidate;
        bool current;
        lock (_partyJoinGate)
        {
            candidate = _partyJoin;
            if (candidate == null || !ReferenceEquals(candidate.Target, target) || !candidate.Ready) return;
            // Root calls this only after AcceptJoin succeeds. Ownership must
            // detach even if auth retires at that boundary; subsequent Cancel
            // must never tear down the accepted lobby owner's session.
            current = candidate.Ownership.IsCurrent && AuthenticatedNativePartyAuthority.IsCurrent(target);
            _partyJoin = null;
        }
        candidate.Cancel.Dispose();
        if (!current) return;
        LauncherPrefs.PlayerName = candidate.Plan.PlayerName;
        LauncherPrefs.LastHunter = candidate.Plan.Hunter; LauncherPrefs.LastColor = NetSession.LocalColor;
        LauncherPrefs.ServerAddress = target.Host; LauncherPrefs.ServerPort = target.Port;
        LauncherPrefs.LastKind = (int)LaunchKind.Online; LauncherPrefs.Save();
        _ = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                var authority = new AuthenticatedNativePartyAuthority();
                for (int attempt = 0; attempt < 3; ++attempt)
                {
                    if (!candidate.Ownership.IsCurrent || !AcceptedPartySession(target, candidate.ExpectedMatch)
                        || !AuthenticatedNativePartyAuthority.IsCurrent(target)) return;
                    if (await authority.NoteJoinedAsync(target, timeout.Token).ConfigureAwait(false)) return;
                    await Task.Delay(1000, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
        });
    }
}
#endif
