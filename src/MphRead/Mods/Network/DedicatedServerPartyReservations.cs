using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    /// <summary>Opt in explicitly. Backend capability verification is also required.</summary>
    public bool PartyReservationsEnabled { get; init; }
        = String.Equals(Environment.GetEnvironmentVariable("PROJECT_PRIME_PARTY_RESERVATIONS_V1"), "1", StringComparison.Ordinal);
    private SocialPartyReservationRelay? _partyBackend;
    private bool _partyCapability;
    private double _nextPartyCapability;
    private PartyWork? _partyCapabilityWork;
    private readonly List<PartyWork> _partyWorks = new();
    private readonly List<PartyValidation> _partyValidations = new();
    private readonly Dictionary<string,PartyGroup> _partyGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<LobbyQueueConnection,double> _partyRequestAfter = new();
    private readonly List<LobbyQueueConnection> _partyExpiredRequestKeys = new(16);
    private readonly List<PartyValidation> _partyValidationScan = new(8);
    private readonly List<KeyValuePair<string, PartyGroup>> _partyGroupScan = new(8);

    private sealed class PartyWork
    {
        internal readonly CancellationTokenSource Cancel = new();
        internal readonly Task<PartyReservationReceipt> Task;
        internal PartyWork(Func<CancellationToken,Task<PartyReservationReceipt>> work, double seconds)
        {
            Cancel.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, .05, 3.5)));
            Task = System.Threading.Tasks.Task.Run(async () =>
            {
                try { return await work(Cancel.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                { return PartyReservationReceipt.Refused; }
                finally { Cancel.Dispose(); }
            });
        }
        internal void Stop() { try { Cancel.Cancel(); } catch (ObjectDisposedException) { } }
        internal PartyReservationReceipt Result => Task.IsCompletedSuccessfully ? Task.Result : PartyReservationReceipt.Refused;
    }
    private sealed class PartyReceiver
    {
        internal PartyReservationRequestPacket Identity;
        internal IPEndPoint Endpoint = null!;
        internal LobbyQueueConnection Owner;
        internal QueuePeer? Queue;
        internal SocialPeerWitness? Peer;
        internal uint Revision;
        internal ulong Lease;
        internal PartyPublication? Published;
    }
    private readonly record struct PartyPublication(PartyReservationWireState State, string Reservation,
        byte Slot, byte Count, double Deadline, ulong Queue, ushort Position, ushort Length);
    private sealed class PartyValidation
    {
        internal PartyReceiver Receiver = null!;
        internal PartyWork Work = null!;
        internal double Deadline;
    }
    private sealed class PartyMember
    {
        internal Guid Player;
        internal int Slot = -1;
        internal PartyReceiver? Receiver;
        internal PartyWork? Claim;
        internal bool ClaimRequested;
        internal ulong ClaimNonce;
        internal double ClaimDeadline;
        internal PartyWork? Acknowledge;
        internal double AcknowledgeUntil, AcknowledgeAfter;
        internal bool Acknowledged;
        internal bool Admitted, Declined;
    }
    private sealed class PartyGroup
    {
        internal PartyReceiver Leader = null!;
        internal string Reservation = Guid.NewGuid().ToString("D");
        internal ulong Queue;
        internal double Deadline;
        internal PartyWork? Activation;
        internal bool Activated, Cancelled;
        internal Guid LeaderPlayer;
        internal PartyWork? Cancellation;
        internal double CancelUntil, CancelAfter;
        internal bool CancellationAcknowledged;
        internal readonly List<PartyMember> Members = new();
    }
    private bool PartyAvailable => PartyReservationsEnabled && _partyCapability && _waitlist != null
        && SessionPolicy == ServerSessionPolicy.Lobby && _phase == SessionPhase.Lobby;
    private PartyWork? BeginPartyWork(Func<CancellationToken,Task<PartyReservationReceipt>> work, double seconds)
    {
        if (seconds <= .05 || _partyWorks.Count(w => !w.Task.IsCompleted) >= 8) return null;
        var pending = new PartyWork(work, seconds); _partyWorks.Add(pending); return pending;
    }
    private PartyWork? ReportParty(PartyReservationRequestPacket identity, string action, string reservation,
        double seconds, IReadOnlyList<(Guid Player,int Slot)>? assignments = null,
        DateTimeOffset expiry = default, int slot = -1, ulong connection = 0, ulong claim = 0, bool self = false)
    {
        if (_partyBackend == null) return null;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3.5, seconds));
        return BeginPartyWork(token => _partyBackend.ReportAsync(identity, action, reservation,
            deadline, token, assignments, expiry, slot, connection, claim, self), seconds);
    }
    private bool CurrentPartyReceiver(PartyReceiver receiver)
    {
        if (receiver.Identity.MatchId != _matchId || receiver.Identity.AuthorityEpoch != _authorityEpoch
            || SessionPolicy != ServerSessionPolicy.Lobby || _phase != SessionPhase.Lobby) return false;
        if (receiver.Queue is { } queue)
            return _queuePeers.TryGetValue(receiver.Endpoint, out var current) && ReferenceEquals(current, queue)
                && queue.ClientId == receiver.Identity.ClientId && queue.Nonce == receiver.Identity.QueueNonce
                && _transport?.QueueConnectionId(receiver.Endpoint) == receiver.Owner.ConnectionId;
        return receiver.Peer is { } peer && CurrentSocialPeer(peer)
            && peer.Peer.ClientId == receiver.Identity.ClientId && peer.Ticket == receiver.Identity.CareerTicket;
    }
    private void PublishParty(PartyReceiver receiver, PartyReservationWireState state, string reservation,
        int slot, int count, double deadline, ulong queueId = 0, int position = 0, int length = 0)
    {
        if (_transport == null || !CurrentPartyReceiver(receiver)) return;
        var publication = new PartyPublication(state, reservation, (byte)(slot < 0 ? 255 : slot),
            (byte)Math.Clamp(count, 0, 8), deadline, queueId, (ushort)position, (ushort)length);
        if (receiver.Published == publication || receiver.Revision == uint.MaxValue) return;
        uint ticks = (uint)Math.Clamp(Math.Ceiling((deadline - _now) * Render.FrameTiming.SimulationHz), 0, 3600);
        if (state is PartyReservationWireState.Reserved or PartyReservationWireState.Admitting && ticks == 0) return;
        var p = new PartyReservationStatePacket(1, receiver.Identity.OperationNonce, ++receiver.Revision,
            receiver.Identity.RequestId, reservation, receiver.Identity.PartyRevision, receiver.Identity.TravelId,
            receiver.Identity.TravelRevision, receiver.Identity.MatchId, receiver.Identity.AuthorityEpoch,
            state, publication.Slot, publication.Count, ticks, receiver.Lease, queueId,
            publication.Position, publication.Length);
        p.Write(_scratch); _transport.Send(receiver.Endpoint, PacketType.PartyReservationState, _scratch.AsSpan(0, p.EncodedSize));
        receiver.Published = publication;
    }
    private static bool SamePartyWitness(PartyReservationRequestPacket a, PartyReservationRequestPacket b)
        => a.RequestId == b.RequestId && a.PartyId == b.PartyId && a.PartyRevision == b.PartyRevision
        && a.TravelId == b.TravelId && a.TravelRevision == b.TravelRevision
        && a.MatchId == b.MatchId && a.AuthorityEpoch == b.AuthorityEpoch;
    private void BeginPartyRequest(ReceivedPacket packet, QueuePeer? queued, double now)
    {
        if (!PartyAvailable || !PartyReservationRequestPacket.TryRead(packet.Payload, out var identity)
            || identity.MatchId != _matchId || identity.AuthorityEpoch != _authorityEpoch || packet.ConnectionId == 0) return;
        var receiver = new PartyReceiver { Identity = identity, Endpoint = packet.Sender,
            Owner = LobbyQueueConnection.FromEstablished(packet.Sender, packet.ConnectionId), Queue = queued };
        if (queued != null)
        {
            if (identity.Purpose != PartyReservationPurpose.MemberAttach || identity.ClientId != queued.ClientId
                || identity.QueueNonce != queued.Nonce || queued.QueueId != 0 || now >= queued.CreatedAt + 4.5
                || queued.PartyRequest.Length != 0 && queued.PartyRequest != identity.RequestId
                || !_partyGroups.TryGetValue(identity.RequestId, out var pendingGroup) || pendingGroup.Cancelled
                || !SamePartyWitness(identity, pendingGroup.Leader.Identity)) return;
        }
        else
        {
            Peer? peer = Find(packet.Sender);
            if (identity.Purpose != PartyReservationPurpose.LeaderCreate || peer == null || peer.ClientId != identity.ClientId
                || peer.CareerTicket != identity.CareerTicket || peer.Spectating
                || _transport?.ConnectionStats(peer.EndPoint)?.ConnectionId != packet.ConnectionId) return;
            receiver.Peer = new(peer, _slotGenerations[peer.SlotIndex], packet.ConnectionId,
                _matchId, _authorityEpoch, _phase, peer.CareerTicket);
        }
        var previous = _partyValidations.Find(v => v.Receiver.Owner == receiver.Owner);
        if (previous != null) return; // one verification per incarnation; reliable retransmits are idempotent
        if (_partyGroups.TryGetValue(identity.RequestId, out var group))
        {
            if (!SamePartyWitness(identity, group.Leader.Identity)) return;
            PartyReceiver? existing = queued == null ? group.Leader : group.Members.Select(m => m.Receiver)
                .FirstOrDefault(r => r?.Owner == receiver.Owner);
            if (existing != null)
            {
                if (existing.Identity.OperationNonce == identity.OperationNonce) PublishPartyGroup(group, now);
                return;
            }
        }
        double seconds = queued == null ? 3.5 : Math.Min(3.5, queued.CreatedAt + 4.5 - now);
        if (_partyRequestAfter.TryGetValue(receiver.Owner, out double next) && now < next) return;
        if (_partyGroups.Count >= WaitlistCapacity + 8) return;
        _partyRequestAfter[receiver.Owner] = now + 1;
        PartyWork? work = ReportParty(identity, "validate", PartyReservationWire.ZeroId, seconds);
        if (work == null) return;
        if (queued != null) queued.PartyPending = true;
        _partyValidations.Add(new() { Receiver = receiver, Work = work, Deadline = now + seconds });
        PublishParty(receiver, PartyReservationWireState.Verifying, PartyReservationWire.ZeroId, -1, 0, now + seconds);
    }
    private void CompletePartyValidation(PartyValidation pending, double now)
    {
        PartyReceiver receiver = pending.Receiver;
        PartyReservationReceipt receipt = pending.Work.Result;
        if (receiver.Queue != null) receiver.Queue.PartyPending = false;
        bool valid = now < pending.Deadline && CurrentPartyReceiver(receiver) && receipt.Ok
            && receipt.Player != Guid.Empty && receipt.Leader != Guid.Empty
            && receipt.Members.Length is >= 1 and <= 7 && receipt.Roster.Length is >= 2 and <= 8
            && receipt.Members.All(receipt.Roster.Contains) && receipt.Roster.Contains(receipt.Leader)
            && receipt.ExpiresAt > DateTimeOffset.UtcNow;
        if (valid && receiver.Queue == null)
        {
            valid = receipt.Player == receipt.Leader && receiver.Peer != null;
            if (valid)
            {
                _verifiedSocialPeers[receipt.Player] = receiver.Peer!;
                valid = receipt.Roster.Except(receipt.Members).All(id => _verifiedSocialPeers.TryGetValue(id, out var peer) && CurrentSocialPeer(peer))
                    && receipt.Members.All(id => !_verifiedSocialPeers.TryGetValue(id, out var peer) || !CurrentSocialPeer(peer));
            }
            if (valid && !_partyGroups.ContainsKey(receiver.Identity.RequestId))
            {
                RefreshWaitlist(now);
                double deadline = now + Math.Min(60, (receipt.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                if (_waitlist!.TryJoinGroup(receiver.Owner, receiver.Identity.RequestId, receipt.Members.Length,
                    _matchId, _authorityEpoch, deadline, now, out var entry))
                {
                    var group = new PartyGroup { Leader = receiver, LeaderPlayer = receipt.Player, Queue = entry.QueueId, Deadline = entry.ExpiresAt };
                    foreach (Guid id in receipt.Members) group.Members.Add(new() { Player = id });
                    _partyGroups.Add(receiver.Identity.RequestId, group); return;
                }
                valid = false;
            }
        }
        else if (valid && _partyGroups.TryGetValue(receiver.Identity.RequestId, out var group)
            && !group.Cancelled && SamePartyWitness(receiver.Identity, group.Leader.Identity))
        {
            PartyMember? member = group.Members.Find(m => m.Player == receipt.Player && !m.Admitted && !m.Declined);
            if (member != null && member.Receiver == null && receipt.Leader == group.LeaderPlayer)
            {
                receiver.Lease = NetConnection.NewId(); member.Receiver = receiver;
                receiver.Queue!.PartyRequest = receiver.Identity.RequestId;
                receiver.Queue.Owner = receiver.Owner;
                PublishPartyGroup(group, now); return;
            }
            valid = false;
        }
        else valid = false;
        if (!valid) PublishParty(receiver, PartyReservationWireState.Rejected, PartyReservationWire.ZeroId, -1, 0, now);
    }
    private void PublishPartyGroup(PartyGroup group, double now)
    {
        bool held = _waitlist!.TryGetGroup(group.Queue, out var entry);
        int count = group.Members.Count(m => !m.Admitted && (!m.Declined || m.ClaimRequested));
        PartyReservationWireState state = group.Cancelled ? PartyReservationWireState.Cancelled
            : count == 0 ? PartyReservationWireState.Expired
            : group.Activated ? PartyReservationWireState.Reserved
            : held && entry.Slots != 0 ? PartyReservationWireState.Activating : PartyReservationWireState.Waiting;
        string id = group.Activated ? group.Reservation : PartyReservationWire.ZeroId;
        PublishParty(group.Leader, state, id, -1, count, group.Deadline, group.Queue, held ? entry.Position : 0, held ? entry.QueueLength : 0);
        foreach (var member in group.Members)
            if (member.Receiver is { } receiver && !member.Admitted)
                PublishParty(receiver, member.ClaimRequested ? PartyReservationWireState.Admitting
                    : member.Declined ? PartyReservationWireState.Cancelled : state,
                    id, member.Slot, count, member.ClaimRequested ? Math.Min(group.Deadline, member.ClaimDeadline) : group.Deadline,
                    group.Queue, held ? entry.Position : 0, held ? entry.QueueLength : 0);
    }
    private void HandlePartyAction(ReceivedPacket packet, double now)
    {
        if (!PartyReservationActionPacket.TryRead(packet.Payload, out var action)
            || !_partyGroups.TryGetValue(action.RequestId, out var group)
            || action.MatchId != _matchId || action.AuthorityEpoch != _authorityEpoch) return;
        PartyMember? member = group.Members.Find(m => m.Receiver?.Owner == LobbyQueueConnection.FromEstablished(packet.Sender, packet.ConnectionId));
        PartyReceiver? receiver = member?.Receiver;
        bool leader = group.Leader.Owner == LobbyQueueConnection.FromEstablished(packet.Sender, packet.ConnectionId);
        if (leader) receiver = group.Leader;
        if (receiver == null || !CurrentPartyReceiver(receiver) || action.OperationNonce != receiver.Identity.OperationNonce
            || action.ExpectedRevision != receiver.Revision || action.MemberLeaseNonce != receiver.Lease
            || action.ReservationId != (group.Activated ? group.Reservation : PartyReservationWire.ZeroId)) return;
        if (action.Action == PartyReservationWireAction.CancelGroup)
        { if (leader) CancelPartyGroup(group, now); return; }
        if (member == null || member.Admitted || member.Declined) return;
        if (action.Action == PartyReservationWireAction.DeclineSelf)
        {
            if (!group.Activated) return; // Pending cohort stays leader-owned and immutable.
            member.Declined = true;
            ReportParty(receiver.Identity, "cancel", group.Reservation, 3, self: true);
            if (!member.ClaimRequested && member.Slot >= 0) _waitlist!.ReleaseGroupSlot(group.Queue, member.Slot, now);
            PublishPartyGroup(group, now); return;
        }
        if (action.Action != PartyReservationWireAction.Accept || !group.Activated || group.Cancelled
            || member.ClaimRequested || member.Slot < 0 || now >= group.Deadline) return;
        member.ClaimNonce = NetConnection.NewId();
        member.ClaimRequested = true;
        member.ClaimDeadline = Math.Min(group.Deadline, now + 2);
        member.Claim = ReportParty(receiver.Identity, "claim_admission", group.Reservation,
            member.ClaimDeadline - now, slot: member.Slot, connection: receiver.Owner.ConnectionId, claim: member.ClaimNonce);
        PublishPartyGroup(group, now);
    }
    private void CancelPartyGroup(PartyGroup group, double now)
    {
        if (group.Cancelled) return;
        group.Cancelled = true;
        group.CancelUntil = now + 10;
        // Already committed or in-flight claims retain only their short bounded
        // holds until settlement; all other unadmitted seats release now.
        foreach (var member in group.Members)
            if (!member.Admitted && !member.ClaimRequested && member.Slot >= 0)
            { _waitlist!.ReleaseGroupSlot(group.Queue, member.Slot, now); member.Declined = true; }
        if (!group.Members.Any(m => m.ClaimRequested)) _waitlist!.CancelGroup(group.Queue, now);
        PublishPartyGroup(group, now);
    }
    private void CompletePartyClaim(PartyGroup group, PartyMember member, double now)
    {
        PartyWork? work = member.Claim;
        if (work?.Task.IsCompleted == false) work.Stop();
        member.Claim = null;
        member.ClaimRequested = false;
        PartyReceiver? receiver = member.Receiver;
        var result = work?.Result ?? PartyReservationReceipt.Refused;
        bool admitted = false;
        if (result.Ok && receiver?.Queue is { } queue && CurrentPartyReceiver(receiver)
            && now < member.ClaimDeadline && result.ClaimExpiresAt > DateTimeOffset.UtcNow)
        {
            admitted = _waitlist!.TryAcceptGroup(group.Queue, member.Slot, _matchId, _authorityEpoch, now, slot =>
            {
                byte[] hello = new byte[7]; hello[0] = (byte)PacketType.Hello; hello[1] = NetConfig.ProtocolVersion; hello[2] = (byte)slot;
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(3), receiver.Identity.ClientId);
                _queueAdmitting = true;
                try { HandleHello(new ReceivedPacket(queue.Endpoint, hello, hello.Length, connectionId: receiver.Owner.ConnectionId), now, slot); }
                finally { _queueAdmitting = false; }
                return Find(queue.Endpoint)?.SlotIndex == slot;
            });
            if (admitted)
            {
                member.Admitted = true; _queuePeers.Remove(queue.Endpoint);
                member.AcknowledgeUntil = now + 8;
                // First ack is sent immediately. A lost committed reply can be
                // retried idempotently, without reopening or removing the peer.
                member.Acknowledge = ReportParty(receiver.Identity, "admitted", group.Reservation,
                    Math.Min(3.5, member.AcknowledgeUntil - now), slot: member.Slot,
                    connection: receiver.Owner.ConnectionId, claim: member.ClaimNonce);
            }
        }
        if (!admitted)
        {
            member.Declined = true;
            _waitlist!.ReleaseGroupSlot(group.Queue, member.Slot, now);
            if (receiver != null) PublishParty(receiver, PartyReservationWireState.Expired, group.Reservation, -1, 0, now);
        }
    }
    private void MaintainPartyReservations(double now)
    {
        _partyWorks.RemoveAll(w => w.Task.IsCompleted);
        _partyExpiredRequestKeys.Clear();
        foreach (var budget in _partyRequestAfter)
            if (now >= budget.Value + 10) _partyExpiredRequestKeys.Add(budget.Key);
        foreach (var key in _partyExpiredRequestKeys) _partyRequestAfter.Remove(key);
        _partyExpiredRequestKeys.Clear();
        if (PartyReservationsEnabled && CareerReportOutbox.Enabled && !_controlPlaneOnlyForTests && _waitlist != null)
        {
            _partyBackend ??= new(SocialMembershipHttp,
                BackendServiceConfiguration.Capture().Function("social-party-reservation-server"), CareerReportOutbox.AuthorizeSocialReport);
            if (_partyCapabilityWork?.Task.IsCompleted == true)
            { _partyCapability = _partyCapabilityWork.Result.Capability; _partyCapabilityWork = null; _nextPartyCapability = now + (_partyCapability ? 30 : 10); }
            if (_partyCapabilityWork == null && now >= _nextPartyCapability)
                _partyCapabilityWork = BeginPartyWork(_partyBackend.CapabilityAsync, 3);
        }
        else _partyCapability = false;
        _partyValidationScan.Clear();
        _partyValidationScan.AddRange(_partyValidations);
        foreach (var validation in _partyValidationScan)
            if (validation.Work.Task.IsCompleted || now >= validation.Deadline || !CurrentPartyReceiver(validation.Receiver))
            {
                _partyValidations.Remove(validation);
                if (!validation.Work.Task.IsCompleted) validation.Work.Stop();
                CompletePartyValidation(validation, now);
            }
        _partyValidationScan.Clear();
        _partyGroupScan.Clear();
        foreach (var pair in _partyGroups) _partyGroupScan.Add(pair);
        foreach (var pair in _partyGroupScan)
        {
            PartyGroup group = pair.Value;
            bool exists = _waitlist != null && _waitlist.TryGetGroup(group.Queue, out _);
            bool allAdmitted = group.Members.All(m => m.Admitted);
            if (!group.Cancelled && !allAdmitted && (!CurrentPartyReceiver(group.Leader) || !exists || now >= group.Deadline)) CancelPartyGroup(group, now);
            if (group.Cancelled && !group.CancellationAcknowledged)
            {
                if (group.Cancellation?.Task.IsCompleted == true)
                {
                    group.CancellationAcknowledged = group.Cancellation.Result.Ok;
                    group.Cancellation = null; group.CancelAfter = now + .5;
                }
                if (!group.CancellationAcknowledged && group.Cancellation == null && now >= group.CancelAfter && now < group.CancelUntil)
                    // The known real ID also matches an activation that committed
                    // remotely before its response arrived locally.
                    group.Cancellation = ReportParty(group.Leader.Identity, "cancel", group.Reservation, Math.Min(3, group.CancelUntil - now));
            }
            if (!group.Cancelled && _waitlist!.TryGetGroup(group.Queue, out var entry))
            {
                group.Deadline = Math.Min(group.Deadline, entry.ExpiresAt);
                if (!group.Activated && group.Activation == null && entry.Slots != 0)
                {
                    var assignments = new List<(Guid,int)>(); int index = 0;
                    for (int slot = 0; slot < 8; ++slot) if ((entry.Slots & (1 << slot)) != 0)
                    { group.Members[index].Slot = slot; assignments.Add((group.Members[index++].Player, slot)); }
                    group.Activation = ReportParty(group.Leader.Identity, "activate", group.Reservation,
                        Math.Min(3.5, group.Deadline - now), assignments,
                        DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, group.Deadline - now)));
                    if (group.Activation == null) CancelPartyGroup(group, now);
                }
                if (!group.Activated && group.Activation?.Task.IsCompleted == true)
                {
                    var response = group.Activation.Result;
                    double deadline = now + Math.Min(group.Deadline - now, (response.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                    if (response.Ok && _waitlist.ActivateGroup(group.Queue, _matchId, _authorityEpoch, deadline, now))
                    { group.Activated = true; group.Deadline = deadline; }
                    else CancelPartyGroup(group, now);
                }
            }
            foreach (var member in group.Members)
            {
                if (member.Admitted && !member.Acknowledged)
                {
                    if (member.Acknowledge?.Task.IsCompleted == true)
                    { member.Acknowledged = member.Acknowledge.Result.Ok; member.Acknowledge = null; member.AcknowledgeAfter = now + .25; }
                    if (!member.Acknowledged && member.Acknowledge == null && now >= member.AcknowledgeAfter && now < member.AcknowledgeUntil
                        && member.Receiver is { } ackReceiver)
                        member.Acknowledge = ReportParty(ackReceiver.Identity, "admitted", group.Reservation,
                            Math.Min(3.5, member.AcknowledgeUntil - now), slot: member.Slot,
                            connection: ackReceiver.Owner.ConnectionId, claim: member.ClaimNonce);
                }
                if (member.ClaimRequested && member.Claim == null && now < member.ClaimDeadline && member.Receiver is { } claimReceiver)
                    member.Claim = ReportParty(claimReceiver.Identity, "claim_admission", group.Reservation,
                        member.ClaimDeadline - now, slot: member.Slot, connection: claimReceiver.Owner.ConnectionId, claim: member.ClaimNonce);
                if (member.ClaimRequested && (member.Claim?.Task.IsCompleted == true || now >= member.ClaimDeadline)) CompletePartyClaim(group, member, now);
                else if (!member.Admitted && !member.Declined && member.Receiver is { } receiver && !CurrentPartyReceiver(receiver))
                { member.Declined = true; if (member.Slot >= 0) _waitlist!.ReleaseGroupSlot(group.Queue, member.Slot, now); }
            }
            PublishPartyGroup(group, now);
            if ((group.Cancelled || group.Members.All(m => m.Admitted || m.Declined)) && group.Members.All(m => !m.ClaimRequested)
                && (!group.Cancelled || group.CancellationAcknowledged || now >= group.CancelUntil)
                && group.Members.All(m => !m.Admitted || m.Acknowledged || now >= m.AcknowledgeUntil))
            { _waitlist!.CancelGroup(group.Queue, now); _partyGroups.Remove(pair.Key); }
        }
        _partyGroupScan.Clear();
    }
    private bool CurrentPartyQueue(QueuePeer peer, double now)
        => peer.PartyRequest.Length != 0 && _partyGroups.TryGetValue(peer.PartyRequest, out var group)
        && now < group.Deadline && group.Members.Any(m => ReferenceEquals(m.Receiver?.Queue, peer)
            && !m.Admitted && (!m.Declined || m.ClaimRequested));
    private void ShutdownPartyReservations()
    {
        foreach (var work in _partyWorks) work.Stop();
        _partyWorks.Clear(); _partyValidations.Clear(); _partyGroups.Clear(); _partyRequestAfter.Clear(); _partyCapability = false;
        _partyCapabilityWork = null; _partyBackend = null; _verifiedSocialPeers.Clear();
        _partyExpiredRequestKeys.Clear(); _partyValidationScan.Clear(); _partyGroupScan.Clear();
    }
}
