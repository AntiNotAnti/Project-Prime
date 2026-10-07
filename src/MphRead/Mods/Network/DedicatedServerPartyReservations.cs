using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    public bool PartyReservationsEnabled { get; init; } = true;
    public double PartyReservationSeconds { get; init; } = 30;
    public int PartyReservationCapacity { get; init; } = 16;

    private sealed class PartyReservationPeerState
    {
        internal Guid RequestId;
        internal Guid PlayerId;
        internal uint ClientId;
        internal string CareerTicket = "";
        internal Task<PartyReservationValidation>? Validation;
        internal PartyReserveStatePacket? Published;
        internal double LastSeen;
        internal bool Rejected;
    }

    private sealed class PartySeatReservation
    {
        internal Guid RequestId;
        internal Guid ReservationId;
        internal ulong AuthorityEpoch;
        internal double ExpiresAt;
        internal readonly Dictionary<Guid, int> Slots = new();
        internal readonly HashSet<Guid> Admitted = new();
        internal Task<bool>? Activation;
        internal bool Active;
    }

    private readonly Dictionary<IPEndPoint, PartyReservationPeerState>
        _partyReservationPeers = new();
    private readonly Dictionary<Guid, PartySeatReservation>
        _partyReservations = new();
    private int _partyReservationAdmittingSlot = -1;

    private bool PartyReservationAvailable =>
        PartyReservationsEnabled
        && PartyReservationServerRelay.Enabled
        && PartyReservationSeconds is >= 10 and <= 60
        && PartyReservationCapacity is >= 1 and <= 64;

    private bool PartyReservationPeerActive(IPEndPoint endpoint)
        => _partyReservationPeers.ContainsKey(endpoint);

    private ushort PartyReservedSlotsMask()
    {
        ushort mask = 0;
        foreach (PartySeatReservation reservation in _partyReservations.Values)
        {
            foreach ((Guid playerId, int slot) in reservation.Slots)
            {
                if (!reservation.Admitted.Contains(playerId))
                    mask |= (ushort)(1 << slot);
            }
        }
        return mask;
    }

    private bool PartySlotReserved(int slot)
        => slot is >= 0 and < 8
            && (PartyReservedSlotsMask() & (1 << slot)) != 0;

    private void MaintainPartyReservations(double now)
    {
        if (!PartyReservationAvailable)
        {
            if (_partyReservations.Count > 0 || _partyReservationPeers.Count > 0)
                ClearPartyReservations("cancelled");
            return;
        }

        if (_phase != SessionPhase.Lobby || _authorityEpoch == 0)
        {
            if (_partyReservations.Count > 0)
                ClearPartyReservations("cancelled");
            return;
        }

        foreach (Guid requestId in _partyReservations.Keys.ToArray())
        {
            PartySeatReservation group = _partyReservations[requestId];
            if (group.AuthorityEpoch != _authorityEpoch || now >= group.ExpiresAt)
            {
                PublishReservationTerminal(
                    requestId, PartyReservationWireState.Expired);
                _partyReservations.Remove(requestId);
                _ = PartyReservationServerRelay.CancelAsync(
                    requestId, group.ReservationId, "expired");
                continue;
            }

            if (group.Activation is { IsCompleted: true } activation)
            {
                group.Activation = null;
                bool activated = false;
                try { activated = activation.GetAwaiter().GetResult(); }
                catch (Exception) { activated = false; }

                if (!activated)
                {
                    PublishReservationTerminal(
                        requestId, PartyReservationWireState.Rejected);
                    _partyReservations.Remove(requestId);
                    _ = PartyReservationServerRelay.CancelAsync(
                        requestId, group.ReservationId, "rejected");
                    continue;
                }
                group.Active = true;
            }
        }

        foreach ((IPEndPoint endpoint, PartyReservationPeerState state)
            in _partyReservationPeers.ToArray())
        {
            if (!_queuePeers.ContainsKey(endpoint))
            {
                _partyReservationPeers.Remove(endpoint);
                continue;
            }

            if (state.Validation is { IsCompleted: true } validationTask)
            {
                state.Validation = null;
                PartyReservationValidation validation;
                try { validation = validationTask.GetAwaiter().GetResult(); }
                catch (Exception)
                {
                    validation = PartyReservationValidation.Fail(
                        "reservation_validation_failed");
                }

                if (!validation.Valid
                    || validation.RequestId != state.RequestId
                    || validation.PlayerId == Guid.Empty
                    || validation.AuthorityEpoch != _authorityEpoch)
                {
                    state.Rejected = true;
                    PublishPartyReservationState(
                        endpoint, state.RequestId, Guid.Empty,
                        PartyReservationWireState.Rejected,
                        PartyReserveStatePacket.NoSlot, 0, 0, now);
                    continue;
                }

                state.PlayerId = validation.PlayerId;

                if (!_partyReservations.TryGetValue(
                    validation.RequestId, out PartySeatReservation? group))
                {
                    // A database row marked reserved but absent from server
                    // memory belongs to a previous authority process/lost
                    // allocator state. Never reconstruct a reservation from DB.
                    if (validation.ExistingReservationId != Guid.Empty)
                    {
                        state.Rejected = true;
                        PublishPartyReservationState(
                            endpoint, state.RequestId,
                            validation.ExistingReservationId,
                            PartyReservationWireState.Expired,
                            PartyReserveStatePacket.NoSlot,
                            (byte)validation.RequestedCount, 0, now);
                        _ = PartyReservationServerRelay.CancelAsync(
                            state.RequestId,
                            validation.ExistingReservationId,
                            "expired");
                        continue;
                    }

                    if (!TryAllocatePartyReservation(
                        validation, now, out group))
                    {
                        state.Rejected = true;
                        PublishPartyReservationState(
                            endpoint, state.RequestId, Guid.Empty,
                            PartyReservationWireState.Rejected,
                            PartyReserveStatePacket.NoSlot,
                            (byte)validation.RequestedCount, 0, now);
                        _ = PartyReservationServerRelay.CancelAsync(
                            state.RequestId, Guid.Empty, "rejected");
                        continue;
                    }
                }

                if (!group.Slots.ContainsKey(validation.PlayerId))
                {
                    state.Rejected = true;
                    PublishPartyReservationState(
                        endpoint, state.RequestId, group.ReservationId,
                        PartyReservationWireState.Rejected,
                        PartyReserveStatePacket.NoSlot,
                        (byte)group.Slots.Count,
                        (byte)group.Admitted.Count, now);
                }
            }

            if (state.PlayerId == Guid.Empty || state.Rejected)
                continue;

            if (_partyReservations.TryGetValue(
                state.RequestId, out PartySeatReservation? active)
                && active.Active
                && active.Slots.TryGetValue(state.PlayerId, out int slot)
                && !active.Admitted.Contains(state.PlayerId))
            {
                PublishPartyReservationState(
                    endpoint, active.RequestId, active.ReservationId,
                    PartyReservationWireState.Reserved, (byte)slot,
                    (byte)active.Slots.Count,
                    (byte)active.Admitted.Count, now);
            }
        }
    }

    private bool TryAllocatePartyReservation(
        PartyReservationValidation validation,
        double now,
        out PartySeatReservation reservation)
    {
        reservation = null!;
        if (_partyReservations.Count >= PartyReservationCapacity
            || validation.RequestedCount is < 1 or > 8
            || validation.Members.Length != validation.RequestedCount
            || _phase != SessionPhase.Lobby
            || validation.AuthorityEpoch != _authorityEpoch)
            return false;

        ushort unavailable = 0;
        foreach (Peer peer in _peers)
            unavailable |= (ushort)(1 << peer.SlotIndex);
        foreach (var bot in _bots)
            unavailable |= (ushort)(1 << bot.SlotIndex);
        unavailable |= _waitlist?.QueueReservedSlots ?? 0;
        unavailable |= PartyReservedSlotsMask();

        var slots = new List<int>(validation.RequestedCount);
        for (int slot = 0; slot < _maxPlayers
            && slots.Count < validation.RequestedCount; slot++)
        {
            if ((unavailable & (1 << slot)) == 0)
            {
                slots.Add(slot);
                unavailable |= (ushort)(1 << slot);
            }
        }

        if (slots.Count != validation.RequestedCount)
            return false;

        var group = new PartySeatReservation
        {
            RequestId = validation.RequestId,
            ReservationId = Guid.NewGuid(),
            AuthorityEpoch = _authorityEpoch,
            ExpiresAt = now + PartyReservationSeconds
        };

        for (int i = 0; i < validation.Members.Length; i++)
            group.Slots.Add(validation.Members[i], slots[i]);

        group.Activation = PartyReservationServerRelay.ActivateAsync(
            group.RequestId,
            group.ReservationId,
            group.AuthorityEpoch,
            group.Slots,
            (int)Math.Ceiling(PartyReservationSeconds));

        _partyReservations.Add(group.RequestId, group);
        reservation = group;
        Log($"party reservation {group.ReservationId} allocated "
            + $"{group.Slots.Count} seats for request {group.RequestId}");
        return true;
    }

    private bool HandlePartyReservationPacket(
        ReceivedPacket packet, QueuePeer queuePeer,
        LobbyQueueConnection owner, double now)
    {
        if (packet.Type is not (
            PacketType.PartyReserveClaim or PacketType.PartyReserveAccept))
            return false;

        queuePeer.LastSeen = now;

        if (!PartyReservationAvailable || _phase != SessionPhase.Lobby)
        {
            if (PartyReserveClaimPacket.TryRead(
                packet.Payload, out PartyReserveClaimPacket unavailableClaim))
            {
                PublishPartyReservationState(
                    packet.Sender, unavailableClaim.RequestId, Guid.Empty,
                    PartyReservationWireState.Rejected,
                    PartyReserveStatePacket.NoSlot, 0, 0, now);
            }
            return true;
        }

        if (packet.Type == PacketType.PartyReserveClaim)
        {
            if (!PartyReserveClaimPacket.TryRead(
                packet.Payload, out PartyReserveClaimPacket claim)
                || claim.ClientId != queuePeer.ClientId)
                return true;

            if (!_partyReservationPeers.TryGetValue(
                packet.Sender, out PartyReservationPeerState? state)
                || state.RequestId != claim.RequestId
                || state.ClientId != claim.ClientId
                || !String.Equals(
                    state.CareerTicket, claim.CareerTicket,
                    StringComparison.Ordinal))
            {
                state = new PartyReservationPeerState
                {
                    RequestId = claim.RequestId,
                    ClientId = claim.ClientId,
                    CareerTicket = claim.CareerTicket,
                    LastSeen = now
                };
                state.Validation = PartyReservationServerRelay.ValidateAsync(
                    claim.RequestId,
                    _authorityEpoch,
                    claim.ClientId,
                    claim.CareerTicket);
                _partyReservationPeers[packet.Sender] = state;

                PublishPartyReservationState(
                    packet.Sender, claim.RequestId, Guid.Empty,
                    PartyReservationWireState.Validating,
                    PartyReserveStatePacket.NoSlot, 0, 0, now);
            }
            else
            {
                state.LastSeen = now;
            }
            return true;
        }

        if (!PartyReserveAcceptPacket.TryRead(
            packet.Payload, out PartyReserveAcceptPacket accept)
            || accept.AuthorityEpoch != _authorityEpoch
            || !_partyReservationPeers.TryGetValue(
                packet.Sender, out PartyReservationPeerState? peerState)
            || peerState.RequestId != accept.RequestId
            || peerState.PlayerId == Guid.Empty
            || !_partyReservations.TryGetValue(
                accept.RequestId, out PartySeatReservation? group)
            || !group.Active
            || group.ReservationId != accept.ReservationId
            || group.AuthorityEpoch != accept.AuthorityEpoch
            || now >= group.ExpiresAt
            || group.Admitted.Contains(peerState.PlayerId)
            || !group.Slots.TryGetValue(peerState.PlayerId, out int reservedSlot)
            || !PhysicalSlotFree(reservedSlot))
            return true;

        byte[] hello = new byte[7];
        hello[0] = (byte)PacketType.Hello;
        hello[1] = NetConfig.ProtocolVersion;
        hello[2] = (byte)reservedSlot;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            hello.AsSpan(3), queuePeer.ClientId);

        _partyReservationAdmittingSlot = reservedSlot;
        _queueAdmitting = true;
        try
        {
            HandleHello(new ReceivedPacket(
                queuePeer.Endpoint,
                hello,
                hello.Length,
                connectionId: packet.ConnectionId),
                now,
                reservedSlot);
        }
        finally
        {
            _queueAdmitting = false;
            _partyReservationAdmittingSlot = -1;
        }

        if (Find(queuePeer.Endpoint)?.SlotIndex != reservedSlot)
            return true;

        group.Admitted.Add(peerState.PlayerId);
        _ = PartyReservationServerRelay.AdmittedAsync(
            group.RequestId,
            group.ReservationId,
            peerState.PlayerId);

        _partyReservationPeers.Remove(queuePeer.Endpoint);
        _queuePeers.Remove(queuePeer.Endpoint);

        if (group.Admitted.Count == group.Slots.Count)
        {
            Log($"party reservation {group.ReservationId} fully admitted");
            _partyReservations.Remove(group.RequestId);
        }

        return true;
    }

    private void PublishPartyReservationState(
        IPEndPoint endpoint,
        Guid requestId,
        Guid reservationId,
        PartyReservationWireState state,
        byte slot,
        byte required,
        byte admitted,
        double now)
    {
        uint ticks = 1;
        ulong epoch = 0;

        if (reservationId != Guid.Empty
            && _partyReservations.TryGetValue(
                requestId, out PartySeatReservation? group))
        {
            epoch = group.AuthorityEpoch;
            ticks = (uint)Math.Clamp(
                Math.Ceiling((group.ExpiresAt - now)
                    * Render.FrameTiming.SimulationHz),
                1,
                18000);
        }

        var packet = new PartyReserveStatePacket(
            requestId,
            reservationId,
            state,
            slot,
            required,
            admitted,
            epoch,
            ticks);

        if (_partyReservationPeers.TryGetValue(
            endpoint, out PartyReservationPeerState? peer)
            && peer.Published == packet)
            return;

        packet.Write(_scratch);
        _transport?.Send(
            endpoint,
            PacketType.PartyReserveState,
            _scratch.AsSpan(0, PartyReserveStatePacket.Size));

        if (peer != null)
            peer.Published = packet;
    }

    private void PublishReservationTerminal(
        Guid requestId, PartyReservationWireState state)
    {
        foreach ((IPEndPoint endpoint, PartyReservationPeerState peer)
            in _partyReservationPeers)
        {
            if (peer.RequestId != requestId) continue;
            PublishPartyReservationState(
                endpoint,
                requestId,
                Guid.Empty,
                state,
                PartyReserveStatePacket.NoSlot,
                0,
                0,
                _now);
        }
    }

    private void ClearPartyReservations(string status)
    {
        PartyReservationWireState wire = status == "expired"
            ? PartyReservationWireState.Expired
            : PartyReservationWireState.Cancelled;

        foreach (PartySeatReservation group in _partyReservations.Values)
        {
            PublishReservationTerminal(group.RequestId, wire);
            _ = PartyReservationServerRelay.CancelAsync(
                group.RequestId, group.ReservationId, status);
        }
        _partyReservations.Clear();
        _partyReservationPeers.Clear();
    }

    private void PartyReservationPeerRemoved(IPEndPoint endpoint)
        => _partyReservationPeers.Remove(endpoint);
}
