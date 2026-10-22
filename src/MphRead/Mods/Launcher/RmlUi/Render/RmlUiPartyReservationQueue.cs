#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    private static long _nextPartyAttempt;
    public NativePartyQueueSnapshot CapturePartyQueue()
    {
        lock (_partyJoinGate)
        {
            if (_partyJoin is not { Ready: false, Queue: not null } candidate) return NativePartyQueueSnapshot.Empty;
            if (!AuthenticatedNativePartyAuthority.IsCurrent(candidate.Target)
                || !NativePartyTravelValidation.ValidTarget(candidate.Target, DateTimeOffset.UtcNow))
            { candidate.Cancel.Cancel(); candidate.QueueSnapshot = NativePartyQueueSnapshot.Empty; }
            else if (candidate.QueueSnapshot.RemainingSeconds != candidate.Queue.RemainingPartySeconds)
                candidate.QueueSnapshot = candidate.QueueSnapshot with { RemainingSeconds = candidate.Queue.RemainingPartySeconds };
            return candidate.QueueSnapshot;
        }
    }
    public bool CommandPartyQueue(NativePartyQueueSnapshot expected, NativePartyQueueCommand command)
    {
        lock (_partyJoinGate)
        {
            if (!Enum.IsDefined(command) || _partyJoin is not { Ready: false, Queue: not null } candidate
                || !ReferenceEquals(expected, candidate.QueueSnapshot) || expected.AttemptId <= 0 || !expected.Active
                || candidate.Cancel.IsCancellationRequested || !AuthenticatedNativePartyAuthority.IsCurrent(candidate.Target)
                || !NativePartyTravelValidation.ValidTarget(candidate.Target, DateTimeOffset.UtcNow)
                || candidate.QueueCommand != null) return false;
            if (command == NativePartyQueueCommand.Leave) { candidate.Cancel.Cancel(); return true; }
            if (command == NativePartyQueueCommand.Accept && !expected.CanAccept
                || command == NativePartyQueueCommand.Decline && !expected.CanDecline) return false;
            candidate.QueueCommand = (command, expected.Revision); return true;
        }
    }
    private bool RunPartyQueue(PartyJoinCandidate candidate, NativePartyReservation reservation,
        string ticket, string name, HubSnapshot hub, CancellationToken token)
    {
        NativePartyJoinTarget target = candidate.Target;
        var request = new PartyReservationRequestPacket(1, PartyReservationPurpose.MemberAttach,
            NetConnection.NewId(), NetSession.ClientId, 1, reservation.RequestId, target.PartyId,
            (ulong)reservation.PartyRevision, reservation.TravelId, (uint)reservation.TravelRevision,
            reservation.MatchId, reservation.AuthorityEpoch, ticket);
        LobbyQueueClient? created = null;
        bool CurrentAccount(Func<bool> effect) => HunterLicenseClient.ExecuteNativeLeaseActionAsync(
            target.AccountWitness, effect, token).GetAwaiter().GetResult();
        if (!CurrentAccount(() => { created = new LobbyQueueClient(new IPEndPoint(IPAddress.Parse(target.Host), target.Port), NetSession.ClientId, request); return true; }))
            return false;
        using var queue = created!;
        lock (_partyJoinGate)
        {
            if (!ReferenceEquals(_partyJoin, candidate) || token.IsCancellationRequested) return false;
            candidate.Queue = queue;
            candidate.AttemptId = Interlocked.Increment(ref _nextPartyAttempt);
            PublishPartyQueue(candidate, queue);
        }
        try
        {
            while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < reservation.ExpiresAt
                && NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)
                && AuthenticatedNativePartyAuthority.IsCurrent(target))
            {
                bool current = CurrentAccount(() =>
                {
                  queue.Poll();
                  lock (_partyJoinGate)
                  {
                    if (!ReferenceEquals(_partyJoin, candidate)) return false;
                    if (candidate.QueueCommand is { } command)
                    {
                        candidate.QueueCommand = null;
                        if (queue.PartyState?.Revision != command.Revision) candidate.QueueError = "PARTY SEAT CHANGED // REVIEW CURRENT OFFER";
                        else if (command.Action == NativePartyQueueCommand.Accept) queue.AcceptPartySeat();
                        else if (command.Action == NativePartyQueueCommand.Decline)
                        { queue.DeclinePartySeat(); candidate.QueueError = "PARTY SEAT DECLINED"; return false; }
                    }
                    PublishPartyQueue(candidate, queue);
                    return true;
                  }
                });
                if (!current) return false;
                if (queue.Admitted)
                    return NetLaunch.Connect(target.Host, target.Port, name, hub.DisplayHunter, color: hub.Suit,
                        cancellationToken: token, queuedAdmission: queue, ownership: candidate.Ownership,
                        initializeFence: CurrentAccount);
                if (queue.Ended || queue.Error != null)
                { candidate.QueueError = queue.Error ?? queue.Status; return false; }
                Thread.Sleep(10);
            }
            candidate.QueueError = "PARTY QUEUE CANCELLED OR EXPIRED";
            return false;
        }
        finally
        {
            lock (_partyJoinGate)
            { candidate.Queue = null; candidate.QueueSnapshot = NativePartyQueueSnapshot.Empty; candidate.QueueCommand = null; }
        }
    }
    private static void PublishPartyQueue(PartyJoinCandidate candidate, LobbyQueueClient queue)
    {
        var phase = queue.PartyState is { } state ? state.State switch
        {
            PartyReservationWireState.Waiting => NativePartyQueuePhase.Waiting,
            PartyReservationWireState.Activating => NativePartyQueuePhase.Activating,
            PartyReservationWireState.Reserved => NativePartyQueuePhase.Reserved,
            PartyReservationWireState.Admitting => NativePartyQueuePhase.Admitting,
            PartyReservationWireState.Expired => NativePartyQueuePhase.Expired,
            PartyReservationWireState.Rejected => NativePartyQueuePhase.Rejected,
            PartyReservationWireState.Cancelled => NativePartyQueuePhase.Cancelled,
            _ => NativePartyQueuePhase.Verifying
        } : NativePartyQueuePhase.Verifying;
        if (queue.Ended && phase is not (NativePartyQueuePhase.Rejected or NativePartyQueuePhase.Cancelled)) phase = NativePartyQueuePhase.Expired;
        var snapshot = new NativePartyQueueSnapshot(candidate.AttemptId, queue.PartyState?.Revision ?? 0,
            phase, queue.Status, queue.Position, queue.QueueLength, queue.PartyState?.ReservedCount ?? 0,
            queue.RemainingPartySeconds, queue.SeatAvailable && !queue.Ended, queue.SeatAvailable && !queue.Ended);
        // Only the root presentation owner replaces countdown-only snapshots.
        // A worker tick cannot invalidate the displayed exact-reference command.
        if (candidate.QueueSnapshot != (snapshot with { RemainingSeconds = candidate.QueueSnapshot.RemainingSeconds }))
            candidate.QueueSnapshot = snapshot;
    }
}
#endif
