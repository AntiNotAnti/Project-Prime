using System;
using System.Net;

namespace MphRead.Mods.Network;

/// <summary>
/// Ownership-transfer handle for a server-assigned party seat. The transport
/// is already authenticated as a queue connection and carries the exact
/// Welcome that promoted it to gameplay.
/// </summary>
public sealed class PartyReservedAdmission : IDisposable
{
    private NetTransport? _transport;
    private ReceivedPacket? _welcome;

    internal IPEndPoint Server { get; }
    internal uint ClientId { get; }

    internal PartyReservedAdmission(
        NetTransport transport,
        IPEndPoint server,
        uint clientId,
        ReceivedPacket welcome)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Server = server ?? throw new ArgumentNullException(nameof(server));
        ClientId = clientId;
        _welcome = welcome;
    }

    internal (NetTransport Transport, IPEndPoint Server, uint ClientId, ReceivedPacket Welcome)
        Take()
    {
        if (_transport == null || _welcome is not ReceivedPacket welcome)
            throw new InvalidOperationException("Party admission was already consumed.");

        NetTransport transport = _transport;
        _transport = null;
        _welcome = null;
        return (transport, Server, ClientId, welcome);
    }

    public void Dispose()
    {
        if (_transport != null)
        {
            try
            {
                _transport.Send(
                    Server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
            }
            catch (ObjectDisposedException)
            {
            }
            _transport.Dispose();
        }
        _transport = null;
        _welcome = null;
    }
}
