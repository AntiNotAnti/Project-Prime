#if MPHREAD_AVALONIA
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Queue-transport client for one member of an authoritative party reservation.
/// No gameplay slot exists until the dedicated server validates the social
/// request, assigns this Hunter License UUID a seat, and normal Welcome arrives.
/// </summary>
internal sealed class PartyReservationClient : IDisposable
{
    private NetTransport? _transport;
    private readonly IPEndPoint _server;
    private readonly Guid _requestId;
    private readonly uint _clientId;
    private readonly ulong _nonce;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _buffer = new byte[1024];

    private bool _welcomed;
    private double _lastAccept = -1;
    private double _lastHello = -1;
    private double _lastClaim = -1;
    private double _lastPing = -1;
    private double _lastServerPacket;
    private PartyReserveStatePacket? _state;
    private ReceivedPacket? _welcome;

    internal NetTransport Transport =>
        _transport ?? throw new ObjectDisposedException(nameof(PartyReservationClient));
    internal IPEndPoint Server => _server;
    internal uint ClientId => _clientId;
    internal Guid RequestId => _requestId;
    internal bool Admitted => _welcome.HasValue;
    internal string? Error { get; private set; }

    internal string Status => _state?.State switch
    {
        PartyReservationWireState.Validating => "VALIDATING PARTY RESERVATION",
        PartyReservationWireState.Reserved => "PARTY SLOT RESERVED",
        PartyReservationWireState.Rejected => "PARTY RESERVATION REJECTED",
        PartyReservationWireState.Expired => "PARTY RESERVATION EXPIRED",
        PartyReservationWireState.Cancelled => "PARTY RESERVATION CANCELLED",
        _ => _welcomed ? "ESTABLISHING PARTY RESERVATION" : "CONTACTING SERVER"
    };

    private PartyReservationClient(
        IPEndPoint server, Guid requestId, uint clientId)
    {
        if (server == null || requestId == Guid.Empty || clientId == 0)
            throw new ArgumentException("Server, reservation request and client identity are required.");

        _server = new IPEndPoint(server.Address, server.Port);
        _requestId = requestId;
        _clientId = clientId;
        _nonce = NetConnection.NewId();
        _transport = new NetTransport(0);
        _transport.AnswerPingsImmediately();
        Poll();
    }

    internal static async Task<PartyReservationClient?> ConnectAsync(
        string host, int port, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (String.IsNullOrWhiteSpace(host)
            || port is < 1 or > 65535
            || requestId == Guid.Empty)
            return null;

        IPAddress? address = null;
        if (!IPAddress.TryParse(host.Trim(), out address))
        {
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                    host.Trim(), cancellationToken).ConfigureAwait(false);
                address = addresses.FirstOrDefault(ip =>
                    ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault();
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException
                or OperationCanceledException)
            {
                return null;
            }
        }
        if (address == null)
            return null;

        var client = new PartyReservationClient(
            new IPEndPoint(address, port), requestId, NetSession.ClientId);

        bool admitted = await client.WaitForAdmissionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (admitted)
            return client;

        client.Dispose();
        return null;
    }

    private void Poll()
    {
        if (_transport == null || Admitted || Error != null)
            return;

        double now = _clock.Elapsed.TotalSeconds;

        if (!_welcomed && now - _lastHello >= .5)
        {
            _lastHello = now;
            var hello = new QueueHelloPacket(
                NetConfig.ProtocolVersion, _clientId, _nonce);
            hello.Write(_buffer);
            _transport.Send(
                _server,
                PacketType.QueueHello,
                _buffer.AsSpan(0, QueueHelloPacket.Size));
        }

        if (_welcomed)
        {
            if (now - _lastPing >= 1)
            {
                _lastPing = now;
                _transport.Send(
                    _server, PacketType.Ping, ReadOnlySpan<byte>.Empty);
            }

            if (now - _lastClaim >= .5
                && HunterLicenseClient.TryGetCareerTicket(
                    _clientId, out string careerTicket))
            {
                _lastClaim = now;
                var claim = new PartyReserveClaimPacket(
                    _requestId, _clientId, careerTicket);
                int length = claim.Write(_buffer);
                _transport.Send(
                    _server,
                    PacketType.PartyReserveClaim,
                    _buffer.AsSpan(0, length));
            }
        }

        foreach (ReceivedPacket packet in _transport.Drain())
        {
            if (!packet.Sender.Equals(_server))
                continue;

            _lastServerPacket = now;

            if (packet.Type == PacketType.QueueWelcome
                && QueueWelcomePacket.TryRead(packet.Payload, out var welcome)
                && welcome.ClientId == _clientId
                && welcome.ClientNonce == _nonce)
            {
                _welcomed = true;
                continue;
            }

            if (packet.Type == PacketType.PartyReserveState
                && PartyReserveStatePacket.TryRead(packet.Payload, out var state)
                && state.RequestId == _requestId)
            {
                _state = state;
                if (state.State == PartyReservationWireState.Reserved)
                {
                    if (now - _lastAccept >= .5)
                    {
                        _lastAccept = now;
                        var accept = new PartyReserveAcceptPacket(
                            state.RequestId,
                            state.ReservationId,
                            state.AuthorityEpoch);
                        accept.Write(_buffer);
                        _transport.Send(
                            _server,
                            PacketType.PartyReserveAccept,
                            _buffer.AsSpan(0, PartyReserveAcceptPacket.Size));
                    }
                }
                else if (state.State == PartyReservationWireState.Rejected)
                    Error = "The server could not reserve enough party slots.";
                else if (state.State == PartyReservationWireState.Expired)
                    Error = "The party reservation expired.";
                else if (state.State == PartyReservationWireState.Cancelled)
                    Error = "The party reservation was cancelled.";
                continue;
            }

            if (packet.Type == PacketType.Welcome && packet.Payload.Length == 17)
            {
                _welcome = new ReceivedPacket(
                    _server,
                    packet.Data.AsSpan(0, packet.Length).ToArray(),
                    packet.Length,
                    packet.ArrivedAt,
                    connectionId: packet.ConnectionId,
                    sequence: packet.Sequence);
                break;
            }

            if (packet.Type == PacketType.Bye)
                Error = "The server closed the party reservation connection.";
            else if (packet.Type == PacketType.Refused && packet.Payload.Length >= 1)
                Error = RefusedPacket.Read(packet.Payload).Describe("Server");
        }

        if (_welcomed && now - _lastServerPacket >= 12)
            Error = "The party reservation connection timed out.";
        if (!_welcomed && now >= 8)
            Error = "The server did not establish a reservation connection.";
    }

    private async Task<bool> WaitForAdmissionAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
            && !Admitted && Error == null)
        {
            Poll();
            if (Admitted || Error != null)
                break;
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return Admitted && !cancellationToken.IsCancellationRequested;
    }

    internal PartyReservedAdmission TakeAdmission()
    {
        if (_transport == null || _welcome is not ReceivedPacket welcome)
            throw new InvalidOperationException(
                "Party reservation admission is not ready.");

        NetTransport transport = _transport;
        _transport = null;
        return new PartyReservedAdmission(
            transport,
            _server,
            _clientId,
            welcome);
    }

    public void Dispose()
    {
        if (_transport == null)
            return;

        _transport.Send(_server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        _transport.Dispose();
        _transport = null;
    }
}
#endif
