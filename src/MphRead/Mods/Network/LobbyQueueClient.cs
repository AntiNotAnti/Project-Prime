using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

namespace MphRead.Mods.Network;

/// <summary>Queue-only connection, driven by the caller's UI/control poll. It owns
/// no gameplay slot until a validated offer is accepted and normal Welcome arrives.</summary>
public sealed class LobbyQueueClient : IDisposable
{
    private NetTransport? _transport;
    private readonly IPEndPoint _server;
    private readonly uint _clientId;
    private readonly ulong _nonce;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _buffer;
    private readonly PartyReservationRequestPacket? _partyRequest;
    private PartyReservationStatePacket? _partyState;
    private double _partyExpires;
    private ulong _partyConnectionId;
    private bool _partyAccepted, _partyDeclined, _partyExpired;
    private ulong _acceptedPartyLease;
    private string? _acceptedPartyReservation;
    private double _lastHello = -1, _lastPing = -1, _offerExpires, _lastServerPacket;
    private bool _welcomed;
    private uint _stateRevision, _offerRevision;
    private QueueStatePacket? _state;
    private QueueSeatOfferPacket? _offer;
    private ReceivedPacket? _welcome;
    internal NetTransport Transport => _transport ?? throw new ObjectDisposedException(nameof(LobbyQueueClient));
    internal QueueStatePacket? State => _state;
    internal PartyReservationStatePacket? PartyState => _partyState;
    internal bool IsPartyReservation => _partyRequest.HasValue;
    internal QueueSeatOfferPacket? Offer => _offer;
    internal uint ClientId => _clientId;
    internal IPEndPoint Server => _server;
    public bool Admitted => _welcome.HasValue;
    public bool Ended => IsPartyReservation
        ? _partyExpired || _partyState?.State is PartyReservationWireState.Expired or PartyReservationWireState.Rejected or PartyReservationWireState.Cancelled
        : _state?.State is LobbyQueueWireState.Left or LobbyQueueWireState.Expired or LobbyQueueWireState.Rejected;
    public int Position => IsPartyReservation ? _partyState?.Position ?? 0 : _state?.Position ?? 0;
    public int QueueLength => IsPartyReservation ? _partyState?.QueueLength ?? 0 : _state?.QueueLength ?? 0;
    public bool SeatAvailable => IsPartyReservation ? CanActOnPartySeat : _offer.HasValue && RemainingOfferSeconds > 0 && !Admitted;
    public int RemainingPartySeconds => _partyState.HasValue ? Math.Max(0, (int)Math.Ceiling(_partyExpires - _clock.Elapsed.TotalSeconds)) : 0;
    private bool CanActOnPartySeat => _transport != null && !Admitted && !Ended && !_partyAccepted && !_partyDeclined
        && _partyState is { State: PartyReservationWireState.Reserved, SelfSlot: <= 7, MemberLeaseNonce: not 0 }
        && RemainingPartySeconds > 0;
    public int RemainingOfferSeconds => Math.Max(0, (int)Math.Ceiling(_offerExpires - _clock.Elapsed.TotalSeconds));
    public string? Error { get; private set; }
    public string Status => IsPartyReservation ? PartyStatus : _state?.State switch
    {
        LobbyQueueWireState.NextMatch => "NEXT MATCH",
        LobbyQueueWireState.Offered => "PLAYER SLOT AVAILABLE",
        LobbyQueueWireState.Disconnected => "RECONNECTING",
        LobbyQueueWireState.Left => "LEFT QUEUE",
        LobbyQueueWireState.Expired => "QUEUE EXPIRED",
        LobbyQueueWireState.Rejected => "QUEUE FULL",
        _ => _state.HasValue ? "WAITING FOR PLAYER SLOT" : "JOINING QUEUE"
    };
    private string PartyStatus => _partyExpired ? "PARTY RESERVATION EXPIRED" : _partyState?.State switch
    {
        PartyReservationWireState.Verifying => "VERIFYING PARTY RESERVATION",
        PartyReservationWireState.Waiting => "WAITING FOR PARTY SLOTS",
        PartyReservationWireState.Activating => "RESERVING PARTY SLOTS",
        PartyReservationWireState.Reserved => _partyAccepted ? "ACCEPTING PARTY SLOT" : "PARTY SLOT AVAILABLE",
        PartyReservationWireState.Admitting => "JOINING PARTY LOBBY",
        PartyReservationWireState.Expired => "PARTY RESERVATION EXPIRED",
        PartyReservationWireState.Rejected => "PARTY RESERVATION REJECTED",
        PartyReservationWireState.Cancelled => "PARTY RESERVATION CANCELLED",
        _ => "CONNECTING TO PARTY RESERVATION"
    };
    public LobbyQueueClient(IPEndPoint server) : this(server, NetSession.ClientId) { }
    internal LobbyQueueClient(IPEndPoint server, uint clientId) : this(server, clientId, null) { }
    internal LobbyQueueClient(IPEndPoint server, uint clientId, PartyReservationRequestPacket request)
        : this(server, clientId, (PartyReservationRequestPacket?)request) { }
    private LobbyQueueClient(IPEndPoint server, uint clientId, PartyReservationRequestPacket? request)
    {
        if (server == null || clientId == 0) throw new ArgumentException("A server and nonzero client identity are required.");
        _server = new IPEndPoint(server.Address, server.Port); _clientId = clientId; _nonce = NetConnection.NewId();
        if (request is PartyReservationRequestPacket identity)
        {
            identity = identity with { QueueNonce = _nonce };
            if (identity.Purpose != PartyReservationPurpose.MemberAttach || identity.ClientId != clientId || !identity.Validate())
                throw new ArgumentException("A valid member reservation bound to this client is required.", nameof(request));
            _partyRequest = identity;
        }
        _buffer = new byte[_partyRequest.HasValue ? PartyReservationRequestPacket.MaximumSize : 128];
        _transport = new NetTransport(0); _transport.AnswerPingsImmediately(); Poll();
    }
    public void Poll()
    {
        if (_transport == null || Admitted || Error != null || (IsPartyReservation && Ended)) return;
        double now = _clock.Elapsed.TotalSeconds;
        if (IsPartyReservation && _partyState.HasValue && now >= _partyExpires)
        {
            // A delayed newer publication cannot resurrect an elapsed local lease.
            _partyExpired = true; return;
        }
        if (!_welcomed && now - _lastHello >= .5)
        {
            _lastHello = now;
            var hello = new QueueHelloPacket(NetConfig.ProtocolVersion, _clientId, _nonce);
            hello.Write(_buffer); _transport.Send(_server, PacketType.QueueHello, _buffer.AsSpan(0, QueueHelloPacket.Size));
        }
        if (_welcomed && now - _lastPing >= 1)
        { _lastPing = now; _transport.Send(_server, PacketType.Ping, ReadOnlySpan<byte>.Empty); }
        foreach (var packet in _transport.Drain())
        {
            if (!packet.Sender.Equals(_server)) continue;
            _lastServerPacket = now;
            if (packet.Type == PacketType.QueueWelcome && QueueWelcomePacket.TryRead(packet.Payload, out var welcome)
                && welcome.ClientId == _clientId && welcome.ClientNonce == _nonce)
            {
                if (_partyRequest.HasValue)
                {
                    if (packet.ConnectionId == 0 || (_partyConnectionId != 0 && packet.ConnectionId != _partyConnectionId)) continue;
                    _partyConnectionId = packet.ConnectionId;
                }
                _welcomed = true;
                if (_partyRequest is PartyReservationRequestPacket groupRequest)
                {
                    groupRequest.Write(_buffer);
                    _transport.Send(_server, PacketType.PartyReservationRequest, _buffer.AsSpan(0, groupRequest.EncodedSize));
                }
                else
                {
                    var join = new QueueJoinPacket(_nonce); join.Write(_buffer);
                    _transport.Send(_server, PacketType.QueueJoin, _buffer.AsSpan(0, QueueJoinPacket.Size));
                }
            }
            else if (IsPartyReservation && packet.Type == PacketType.PartyReservationState
                && packet.ConnectionId == _partyConnectionId && _partyConnectionId != 0
                && PartyReservationStatePacket.TryRead(packet.Payload, out var partyState))
            {
                if (!MatchesPartyState(partyState) || partyState.Revision <= (_partyState?.Revision ?? 0)) continue;
                if (partyState.State == PartyReservationWireState.Admitting
                    && (!_partyAccepted || partyState.MemberLeaseNonce != _acceptedPartyLease || partyState.ReservationId != _acceptedPartyReservation)) continue;
                if (_partyAccepted && (partyState.MemberLeaseNonce != _acceptedPartyLease || partyState.ReservationId != _acceptedPartyReservation))
                    _partyAccepted = false;
                _partyState = partyState;
                _partyExpires = now + partyState.RemainingTicks / (double)Render.FrameTiming.SimulationHz;
                if (Ended) break;
            }
            else if (!IsPartyReservation && packet.Type == PacketType.QueueState && QueueStatePacket.TryRead(packet.Payload, out var state)
                && (state.Revision == _stateRevision || SequenceMath.Newer(state.Revision, _stateRevision)))
            {
                if (state.State is LobbyQueueWireState.Waiting or LobbyQueueWireState.NextMatch or LobbyQueueWireState.Offered or LobbyQueueWireState.Disconnected
                    && (state.QueueId == 0 || state.Position == 0 || state.Position > state.QueueLength)) continue;
                _stateRevision = state.Revision; _state = state;
                if (state.State != LobbyQueueWireState.Offered && !SequenceMath.Newer(_offerRevision, state.Revision)) _offer = null;
            }
            else if (!IsPartyReservation && packet.Type == PacketType.QueueSeatOffer && QueueSeatOfferPacket.TryRead(packet.Payload, out var offer)
                && offer.QueueId != 0 && offer.OfferId != 0 && offer.AuthorityEpoch != 0
                && !SequenceMath.Newer(_stateRevision, offer.Revision) && SequenceMath.Newer(offer.Revision, _offerRevision))
            {
                _offerRevision = offer.Revision; _offer = offer; _offerExpires = now + offer.ExpiresInTicks / (double)Render.FrameTiming.SimulationHz;
            }
            else if (packet.Type == PacketType.Welcome && packet.Payload.Length == 17
                && (!IsPartyReservation || CanReceivePartyWelcome(packet)))
            {
                // Keep exact accepted bootstrap bytes; do not reset or reopen this socket on promotion.
                _welcome = new ReceivedPacket(_server, packet.Data.AsSpan(0, packet.Length).ToArray(), packet.Length,
                    packet.ArrivedAt, connectionId: packet.ConnectionId, sequence: packet.Sequence);
                break;
            }
            else if (packet.Type == PacketType.Bye) Error = "The server closed the queue connection.";
            else if (packet.Type == PacketType.Refused && packet.Payload.Length >= 1) Error = RefusedPacket.Read(packet.Payload).Describe("Server");
        }
        if (IsPartyReservation && _partyState.HasValue && !Ended && now >= _partyExpires) _partyExpired = true;
        if (IsPartyReservation && now >= 5 && (_partyState == null || _partyState.Value.State == PartyReservationWireState.Verifying))
            Error = "The server did not verify the party reservation in time.";
        if (_welcomed && now - _lastServerPacket >= 10) Error = "The queue connection timed out.";
        if (!_welcomed && now >= 8) Error = "The server did not establish a queue connection.";
    }
    private bool MatchesPartyState(PartyReservationStatePacket state)
        => _partyRequest is PartyReservationRequestPacket request && state.OperationNonce == request.OperationNonce
            && state.RequestId == request.RequestId && state.PartyRevision == request.PartyRevision
            && state.TravelId == request.TravelId && state.TravelRevision == request.TravelRevision
            && state.MatchId == request.MatchId && state.AuthorityEpoch == request.AuthorityEpoch;
    private bool CanReceivePartyWelcome(ReceivedPacket packet)
    {
        if (!_partyAccepted || Ended || RemainingPartySeconds == 0 || packet.ConnectionId != _partyConnectionId
            || _partyState is not PartyReservationStatePacket state
            || state.State is not (PartyReservationWireState.Reserved or PartyReservationWireState.Admitting)
            || state.MemberLeaseNonce != _acceptedPartyLease || state.ReservationId != _acceptedPartyReservation) return false;
        // Welcome can overtake the reliable Admitting state. The explicit accepted
        // lease plus this actual matching admission is sufficient for handoff.
        var payload = packet.Payload;
        return payload[0] == state.SelfSlot && BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]) == _clientId
            && BinaryPrimitives.ReadUInt16LittleEndian(payload[5..]) == state.MatchId
            && BinaryPrimitives.ReadUInt64LittleEndian(payload[7..]) == state.AuthorityEpoch
            && BinaryPrimitives.ReadUInt16LittleEndian(payload[15..]) != 0;
    }
    public bool AcceptPartySeat()
    {
        if (!CanActOnPartySeat || _partyState is not PartyReservationStatePacket state) return false;
        SendPartyAction(state, PartyReservationWireAction.Accept);
        _partyAccepted = true; _acceptedPartyLease = state.MemberLeaseNonce; _acceptedPartyReservation = state.ReservationId;
        return true;
    }
    public bool DeclinePartySeat()
    {
        if (!CanActOnPartySeat || _partyState is not PartyReservationStatePacket state) return false;
        SendPartyAction(state, PartyReservationWireAction.DeclineSelf); _partyDeclined = true;
        return true;
    }
    private void SendPartyAction(PartyReservationStatePacket state, PartyReservationWireAction action)
    {
        var packet = new PartyReservationActionPacket(PartyReservationWire.Version, action, state.OperationNonce,
            state.Revision, state.RequestId, state.ReservationId, state.MemberLeaseNonce, state.MatchId, state.AuthorityEpoch);
        packet.Write(_buffer); _transport!.Send(_server, PacketType.PartyReservationAction, _buffer.AsSpan(0, packet.EncodedSize));
    }
    public bool AcceptSeat()
    {
        if (!SeatAvailable || _transport == null || _offer is not QueueSeatOfferPacket offer) return false;
        var packet = new QueueAcceptPacket(offer.QueueId, offer.OfferId, offer.MatchId, offer.AuthorityEpoch);
        packet.Write(_buffer); _transport.Send(_server, PacketType.QueueAccept, _buffer.AsSpan(0, QueueAcceptPacket.Size)); return true;
    }
    public bool DeclineSeat()
    {
        if (!SeatAvailable || _transport == null || _offer is not QueueSeatOfferPacket offer) return false;
        var packet = new QueueDeclinePacket(offer.QueueId, offer.OfferId, offer.MatchId, offer.AuthorityEpoch);
        packet.Write(_buffer); _transport.Send(_server, PacketType.QueueDecline, _buffer.AsSpan(0, QueueDeclinePacket.Size)); return true;
    }
    internal (NetTransport Transport, IPEndPoint Server, uint ClientId, ReceivedPacket Welcome) TakeAdmission()
    {
        if (_transport == null || _welcome is not ReceivedPacket welcome) throw new InvalidOperationException("Queue admission is not ready.");
        var transport = _transport; _transport = null;
        return (transport, _server, _clientId, welcome);
    }
    public void Dispose()
    {
        if (_transport == null) return;
        if (!Admitted && IsPartyReservation)
        {
            if (_partyState is PartyReservationStatePacket partyState)
            {
                try { SendPartyAction(partyState, PartyReservationWireAction.DeclineSelf); }
                catch (ObjectDisposedException) { } // best effort during exact-owner teardown
            }
        }
        else if (!Admitted && _state?.QueueId is ulong id && id != 0)
        {
            var leave = new QueueLeavePacket(id); leave.Write(_buffer);
            _transport.Send(_server, PacketType.QueueLeave, _buffer.AsSpan(0, QueueLeavePacket.Size));
        }
        else _transport.Send(_server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        _transport.Dispose(); _transport = null;
    }
}
