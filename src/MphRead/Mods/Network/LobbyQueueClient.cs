using System;
using System.Collections.Generic;
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
    private readonly byte[] _buffer = new byte[128];
    private double _lastHello = -1, _lastPing = -1, _offerExpires, _lastServerPacket;
    private bool _welcomed;
    private uint _stateRevision, _offerRevision;
    private QueueStatePacket? _state;
    private QueueSeatOfferPacket? _offer;
    private ReceivedPacket? _welcome;
    internal NetTransport Transport => _transport ?? throw new ObjectDisposedException(nameof(LobbyQueueClient));
    internal QueueStatePacket? State => _state;
    internal QueueSeatOfferPacket? Offer => _offer;
    internal uint ClientId => _clientId;
    internal IPEndPoint Server => _server;
    public bool Admitted => _welcome.HasValue;
    public bool Ended => _state?.State is LobbyQueueWireState.Left or LobbyQueueWireState.Expired or LobbyQueueWireState.Rejected;
    public int Position => _state?.Position ?? 0;
    public int QueueLength => _state?.QueueLength ?? 0;
    public bool SeatAvailable => _offer.HasValue && RemainingOfferSeconds > 0 && !Admitted;
    public int RemainingOfferSeconds => Math.Max(0, (int)Math.Ceiling(_offerExpires - _clock.Elapsed.TotalSeconds));
    public string? Error { get; private set; }
    public string Status => _state?.State switch
    {
        LobbyQueueWireState.NextMatch => "NEXT MATCH",
        LobbyQueueWireState.Offered => "PLAYER SLOT AVAILABLE",
        LobbyQueueWireState.Disconnected => "RECONNECTING",
        LobbyQueueWireState.Left => "LEFT QUEUE",
        LobbyQueueWireState.Expired => "QUEUE EXPIRED",
        LobbyQueueWireState.Rejected => "QUEUE FULL",
        _ => _state.HasValue ? "WAITING FOR PLAYER SLOT" : "JOINING QUEUE"
    };
    public LobbyQueueClient(IPEndPoint server) : this(server, NetSession.ClientId) { }
    internal LobbyQueueClient(IPEndPoint server, uint clientId)
    {
        if (server == null || clientId == 0) throw new ArgumentException("A server and nonzero client identity are required.");
        _server = new IPEndPoint(server.Address, server.Port); _clientId = clientId; _nonce = NetConnection.NewId();
        _transport = new NetTransport(0); _transport.AnswerPingsImmediately(); Poll();
    }
    public void Poll()
    {
        if (_transport == null || Admitted || Error != null) return;
        double now = _clock.Elapsed.TotalSeconds;
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
                _welcomed = true;
                var join = new QueueJoinPacket(_nonce); join.Write(_buffer);
                _transport.Send(_server, PacketType.QueueJoin, _buffer.AsSpan(0, QueueJoinPacket.Size));
            }
            else if (packet.Type == PacketType.QueueState && QueueStatePacket.TryRead(packet.Payload, out var state)
                && (state.Revision == _stateRevision || SequenceMath.Newer(state.Revision, _stateRevision)))
            {
                if (state.State is LobbyQueueWireState.Waiting or LobbyQueueWireState.NextMatch or LobbyQueueWireState.Offered or LobbyQueueWireState.Disconnected
                    && (state.QueueId == 0 || state.Position == 0 || state.Position > state.QueueLength)) continue;
                _stateRevision = state.Revision; _state = state;
                if (state.State != LobbyQueueWireState.Offered && !SequenceMath.Newer(_offerRevision, state.Revision)) _offer = null;
            }
            else if (packet.Type == PacketType.QueueSeatOffer && QueueSeatOfferPacket.TryRead(packet.Payload, out var offer)
                && offer.QueueId != 0 && offer.OfferId != 0 && offer.AuthorityEpoch != 0
                && !SequenceMath.Newer(_stateRevision, offer.Revision) && SequenceMath.Newer(offer.Revision, _offerRevision))
            {
                _offerRevision = offer.Revision; _offer = offer; _offerExpires = now + offer.ExpiresInTicks / (double)Render.FrameTiming.SimulationHz;
            }
            else if (packet.Type == PacketType.Welcome && packet.Payload.Length == 17)
            {
                // Keep exact accepted bootstrap bytes; do not reset or reopen this socket on promotion.
                _welcome = new ReceivedPacket(_server, packet.Data.AsSpan(0, packet.Length).ToArray(), packet.Length,
                    packet.ArrivedAt, connectionId: packet.ConnectionId, sequence: packet.Sequence);
                break;
            }
            else if (packet.Type == PacketType.Bye) Error = "The server closed the queue connection.";
            else if (packet.Type == PacketType.Refused && packet.Payload.Length >= 1) Error = RefusedPacket.Read(packet.Payload).Describe("Server");
        }
        if (_welcomed && now - _lastServerPacket >= 10) Error = "The queue connection timed out.";
        if (!_welcomed && now >= 8) Error = "The server did not establish a queue connection.";
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
        if (!Admitted && _state?.QueueId is ulong id && id != 0)
        {
            var leave = new QueueLeavePacket(id); leave.Write(_buffer);
            _transport.Send(_server, PacketType.QueueLeave, _buffer.AsSpan(0, QueueLeavePacket.Size));
        }
        else _transport.Send(_server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        _transport.Dispose(); _transport = null;
    }
}
