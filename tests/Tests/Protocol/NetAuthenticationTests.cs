using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Transport impairment")]
public sealed class NetAuthenticationTests
{
    private static readonly byte[] Key =
    {
        0, 1, 2, 3, 4, 5, 6, 7,
        8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23,
        24, 25, 26, 27, 28, 29, 30, 31
    };

    private static readonly IPEndPoint EndpointA = new(IPAddress.Loopback, 31001);
    private static readonly IPEndPoint EndpointB = new(IPAddress.Loopback, 31002);
    private static readonly IPEndPoint EndpointC = new(IPAddress.Loopback, 31003);

    [Fact]
    public void SignVerifyUsesStableVectorAndSeparatesDirectionAndKey()
    {
        var header = new NetHeader(NetMessageType.Event, NetHeaderFlags.HasAck,
            0x0102030405060708, 0x11223344, 0x55667788, 0x99AABBCC);
        byte[] payload = { 0xA5, 0x00, 0x7F };
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payload.Length)];

        Assert.True(NetAuthentication.TrySign(Key, NetAuthDirection.ClientToServer,
            header, payload, datagram, out int length));
        Assert.Equal(datagram.Length, length);
        Assert.Equal("131F57FCC611248DB9E9480B9EF0B644",
            Convert.ToHexString(datagram[^NetAuthentication.TagSize..]));
        Assert.True(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            datagram, out NetHeader decoded, out ReadOnlySpan<byte> decodedPayload));
        Assert.Equal(header, decoded);
        Assert.Equal(payload, decodedPayload.ToArray());

        byte[] wrongKey = (byte[])Key.Clone();
        wrongKey[0] ^= 0xFF;
        Assert.False(NetAuthentication.TryVerify(wrongKey, NetAuthDirection.ClientToServer,
            datagram, out _, out _));
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ServerToClient,
            datagram, out _, out _));
    }

    [Fact]
    public void HeaderPayloadAndMalformedFramingAreRejectedWithoutAValidTag()
    {
        byte[] datagram = Sign(NetMessageType.Input, NetHeaderFlags.None, 7, 1,
            payload: new byte[] { 1, 2, 3 });

        byte[] headerTampered = (byte[])datagram.Clone();
        headerTampered[12] ^= 1;
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            headerTampered, out _, out _));

        byte[] payloadTampered = (byte[])datagram.Clone();
        payloadTampered[NetHeader.Size] ^= 1;
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            payloadTampered, out _, out _));

        byte[] empty = Sign(NetMessageType.Input, NetHeaderFlags.None, 7, 2,
            payload: ReadOnlySpan<byte>.Empty);
        Assert.Equal(NetHeader.Size + NetAuthentication.TagSize, empty.Length);
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            empty.AsSpan(0, empty.Length - 1), out _, out _));

        byte[] maximum = Sign(NetMessageType.Input, NetHeaderFlags.None, 7, 3,
            payload: new byte[NetAuthentication.MaximumPayloadSize]);
        Assert.Equal(NetConfig.MaxPacketSize, maximum.Length);
        Assert.True(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            maximum, out _, out ReadOnlySpan<byte> maximumPayload));
        Assert.Equal(NetAuthentication.MaximumPayloadSize, maximumPayload.Length);
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            new byte[NetConfig.MaxPacketSize + 1], out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetAuthentication.AuthenticatedSize(NetAuthentication.MaximumPayloadSize + 1));

        byte[] malformedHeader = SignWithHeader(
            new NetHeader(NetMessageType.Input, NetHeaderFlags.None, 0, 4, 0, 0),
            ReadOnlySpan<byte>.Empty);
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            malformedHeader, out _, out _));
    }

    [Fact]
    public void RoutedJoinFitsAuthenticatedMtuAndRejectsOneByteOverBudget()
    {
        string ticket = "a." + new string('A', JoinPacket.MaxRoutedTicketBytes - 4) + ".a";
        Assert.Equal(JoinPacket.MaxRoutedTicketBytes, ticket.Length);
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "PLAYER",
            Ticket: ticket, WireMatchId: 77, AdmissionId: Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        Assert.Equal(NetAuthentication.MaximumPayloadSize, join.EncodedSize);

        byte[] payload = new byte[join.EncodedSize];
        join.Write(payload);
        Assert.True(JoinPacket.TryRead(payload, out JoinPacket decoded));
        Assert.Equal(join, decoded);
        Assert.True(JoinPacket.TryReadAdmissionId(payload, out Guid admissionId));
        Assert.Equal(join.AdmissionId, admissionId);

        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payload.Length)];
        Assert.Equal(NetConfig.MaxPacketSize, NetAuthentication.Sign(Key,
            NetAuthDirection.ClientToServer,
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0),
            payload, datagram));
        Assert.True(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            datagram, out _, out ReadOnlySpan<byte> verifiedPayload));
        Assert.Equal(payload, verifiedPayload.ToArray());
        Assert.False(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
            datagram.AsSpan(0, NetConfig.MaxPacketSize - 1), out _, out _));

        var over = join with { Ticket = "a." + new string('A', JoinPacket.MaxRoutedTicketBytes - 3) + ".a" };
        Assert.Equal(JoinPacket.MaxRoutedTicketBytes + 1, over.Ticket.Length);
        Assert.Equal(JoinPacket.MaxPayloadBytes + 1, over.EncodedSize);
        Assert.False(JoinPacket.TryRead(new byte[JoinPacket.MaxPayloadBytes + 1], out _));
        Assert.Throws<ArgumentException>(() => over.Write(new byte[over.EncodedSize]));
    }

    [Fact]
    public void AuthenticatedWelcomeWithWrongMatchCannotPublishPartialConnection()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        IPEndPoint server = new(IPAddress.Loopback, serverTransport.LocalPort);
        IPEndPoint clientEndpoint = new(IPAddress.Loopback, clientTransport.LocalPort);
        Guid admissionId = Guid.NewGuid();
        using var client = new NetClient(clientTransport, server, "PLAYER", Hunter.Samus,
            nonce: 123, wireMatchId: 9, admissionId: admissionId, authKey: Key,
            udpAuthenticationEnabled: true);

        var welcome = new JoinAcceptedPacket(123, 0, 10, 1, 60,
            MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS"));
        byte[] welcomePayload = new byte[JoinAcceptedPacket.Size];
        welcome.Write(welcomePayload);
        byte[] body = new byte[ReliableEventPacket.HeaderSize + welcomePayload.Length];
        ReliableEventPacket.Write(body, 1, ReliableEventType.Welcome, welcomePayload);
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(body.Length)];
        NetAuthentication.Sign(Key, NetAuthDirection.ServerToClient,
            new NetHeader(NetMessageType.Accepted, NetHeaderFlags.None, 77, 0, 0, 0),
            body, datagram);
        serverTransport.SendDatagram(clientEndpoint, datagram);

        // UDP delivery is asynchronous even on loopback; poll a bounded
        // window so this assertion tests the rejection rather than a race
        // between SendDatagram and the socket receive loop.
        for (int i = 0; i < 500 && client.Rejected == 0; i++)
        {
            client.Poll();
            if (client.Rejected == 0) { Thread.Sleep(1); }
        }

        Assert.Null(client.Connection);
        Assert.Equal(1, client.Rejected);
        Assert.Null(client.Failure);
    }

    [Fact]
    public void AuthenticatedBootstrapIgnoresUnsignedDiscoveryReplies()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        IPEndPoint server = new(IPAddress.Loopback, serverTransport.LocalPort);
        IPEndPoint clientEndpoint = new(IPAddress.Loopback, clientTransport.LocalPort);
        using var client = new NetClient(clientTransport, server, "PLAYER", Hunter.Samus,
            nonce: 123, wireMatchId: 9, admissionId: Guid.NewGuid(), authKey: Key,
            udpAuthenticationEnabled: true);

        var status = new ServerStatusPacket
        {
            Match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle, PlayerCount = 1, RoomKey = "MP1 SANCTORUS",
                NextRoomKey = "MP1 SANCTORUS", MatchId = 9,
                Flags = MatchStatePacket.FlagInProgress
            },
            MaxPlayers = 8, Protocol = NetHeader.Version - 1, ServerName = "SPOOF",
            Family = NetWireFamily.Authoritative
        };
        byte[] datagram = new byte[1 + ServerStatusPacket.Size];
        datagram[0] = (byte)PacketType.StatusReply;
        status.Write(datagram.AsSpan(1));
        serverTransport.SendDatagram(clientEndpoint, datagram);

        for (int i = 0; i < 500 && client.Rejected == 0; i++)
        {
            client.Poll();
            if (client.Rejected == 0) { Thread.Sleep(1); }
        }

        Assert.Equal(1, client.Rejected);
        Assert.Null(client.Connection);
        Assert.Null(client.Failure);
    }

    [Fact]
    public void AuthenticatedReconnectRetiresTheOldConnectionAndItsKey()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        IPEndPoint server = new(IPAddress.Loopback, serverTransport.LocalPort);
        IPEndPoint clientEndpoint = new(IPAddress.Loopback, clientTransport.LocalPort);
        using var client = new NetClient(clientTransport, server, "PLAYER", Hunter.Samus,
            nonce: 123, wireMatchId: 9, admissionId: Guid.NewGuid(), authKey: Key,
            udpAuthenticationEnabled: true);

        var welcome = new JoinAcceptedPacket(123, 0, 9, 1, 60,
            MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS"));
        byte[] welcomePayload = new byte[JoinAcceptedPacket.Size];
        welcome.Write(welcomePayload);
        byte[] body = new byte[ReliableEventPacket.HeaderSize + welcomePayload.Length];
        ReliableEventPacket.Write(body, 1, ReliableEventType.Welcome, welcomePayload);
        byte[] accepted = new byte[NetAuthentication.AuthenticatedSize(body.Length)];
        NetAuthentication.Sign(Key, NetAuthDirection.ServerToClient,
            new NetHeader(NetMessageType.Accepted, NetHeaderFlags.None, 77, 0, 0, 0),
            body, accepted);
        serverTransport.SendDatagram(clientEndpoint, accepted);
        for (int i = 0; i < 500 && client.Connection == null; i++)
        {
            client.Poll();
            if (client.Connection == null) { Thread.Sleep(1); }
        }

        NetConnection old = Assert.IsType<NetConnection>(client.Connection);
        byte[] oldSessionPacket = SignWithKey(Key, NetAuthDirection.ServerToClient,
            new NetHeader(NetMessageType.Ack, NetHeaderFlags.None, old.Id, 1, 0, 0),
            ReadOnlySpan<byte>.Empty);
        byte[] replacementKey = (byte[])Key.Clone();
        replacementKey[0] ^= 0xFF;
        client.Reconnect(nonce: 456, admissionId: Guid.NewGuid(), authKey: replacementKey);

        Assert.Equal(NetConnectionState.Disconnecting, old.State);
        Assert.False(old.TryVerify(oldSessionPacket, server, out _));
        Assert.Null(client.Connection);
    }

    [Fact]
    public void ConnectionDirectionsMutuallyAuthenticateAndRejectReflectedDatagrams()
    {
        const ulong connectionId = 71;
        var client = new NetConnection(connectionId, EndpointB, 9, 0, Key,
            NetAuthDirection.ClientToServer);
        var server = new NetConnection(connectionId, EndpointA, 9, 0, Key,
            NetAuthDirection.ServerToClient);
        var clientSink = new CaptureSink();
        var serverSink = new CaptureSink();

        byte[] input = { 0x19, 0x2A };
        client.Send(clientSink, NetMessageType.Input, input);
        Assert.True(server.TryVerify(clientSink.Datagram, EndpointA,
            out NetConnection.VerifiedPacket fromClient));
        Assert.Equal(NetMessageType.Input, fromClient.Header.Type);
        Assert.Equal(input, fromClient.Payload.ToArray());
        Assert.True(server.ApplyVerified(fromClient, EndpointA, 1, out ReceiveResult result));
        Assert.Equal(ReceiveResult.Newest, result);

        server.Send(serverSink, NetMessageType.Event, new byte[] { 0x44 });
        Assert.True(client.TryVerify(serverSink.Datagram, EndpointB,
            out NetConnection.VerifiedPacket fromServer));
        Assert.True(client.ApplyVerified(fromServer, EndpointB, 1, out result));
        Assert.Equal(ReceiveResult.Newest, result);

        // A client-signed datagram cannot be reflected back into a client
        // connection, even though the connection ID and key are correct.
        Assert.False(client.TryVerify(clientSink.Datagram, EndpointB, out _));
        Assert.Equal(1, client.Metrics.AuthFailures);
    }

    [Fact]
    public void ForgedAckAndDefaultVerifiedTokenCannotMutateConnectionState()
    {
        var connection = Receiver();
        Assert.True(connection.Reliable.TryEnqueue(ReliableEventType.WorldEvent,
            new byte[] { 0x55 }, out uint eventId));
        connection.Reliable.MarkSent(eventId, 77, 0);
        int pendingBefore = connection.Reliable.PendingCount;
        double lastBefore = connection.LastReceived;

        byte[] forged = Sign(NetMessageType.Event, NetHeaderFlags.HasAck,
            connection.Id, 1, ack: 77, payload: new byte[] { 0x01 });
        forged[^1] ^= 1;
        Assert.False(connection.TryVerify(forged, EndpointA, out _));
        Assert.Equal(pendingBefore, connection.Reliable.PendingCount);
        Assert.Equal(lastBefore, connection.LastReceived);
        Assert.Equal(0, connection.Metrics.AuthenticatedPackets);

        NetConnection.VerifiedPacket defaultToken = default;
        Assert.False(connection.ApplyVerified(defaultToken, EndpointA, 1, out _));
        Assert.Equal(pendingBefore, connection.Reliable.PendingCount);

        byte[] valid = Sign(NetMessageType.Event, NetHeaderFlags.HasAck,
            connection.Id, 1, ack: 77, payload: new byte[] { 0x01 });
        Assert.True(connection.TryVerify(valid, EndpointA,
            out NetConnection.VerifiedPacket verified));
        Assert.True(connection.ApplyVerified(verified, EndpointA, 1,
            out ReceiveResult result));
        Assert.Equal(ReceiveResult.Newest, result);
        Assert.Equal(0, connection.Reliable.PendingCount);
    }

    [Fact]
    public void ReplayCannotRefreshLivenessAndOnlyNewerAuthenticatedPacketsRebind()
    {
        var connection = Receiver();
        byte[] first = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 1, payload: new byte[] { 0x10 });
        Assert.True(VerifyAndApply(connection, first, EndpointA, 1,
            out ReceiveResult result));
        Assert.Equal(ReceiveResult.Newest, result);

        double lastAfterFirst = connection.LastReceived;
        Assert.False(VerifyAndApply(connection, first, EndpointA, 2, out _));
        Assert.Equal(lastAfterFirst, connection.LastReceived);
        Assert.Equal(1, connection.Metrics.AuthReplayRejects);

        byte[] invalidRebind = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 2, payload: new byte[] { 0x20 });
        invalidRebind[^1] ^= 1;
        Assert.False(connection.TryVerify(invalidRebind, EndpointB, out _));
        Assert.Equal(EndpointA, connection.Endpoint);
        Assert.Equal(lastAfterFirst, connection.LastReceived);
        Assert.Equal(1, connection.Metrics.UnauthenticatedRebindAttempts);

        byte[] newer = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 3, payload: new byte[] { 0x30 });
        Assert.True(VerifyAndApply(connection, newer, EndpointB, 3,
            out result));
        Assert.Equal(ReceiveResult.Newest, result);
        Assert.Equal(EndpointB, connection.Endpoint);
        Assert.Equal(3, connection.LastReceived);
        Assert.Equal(1, connection.Metrics.AuthenticatedRebinds);

        // Sequence 2 is valid and authenticated, but is out of order after
        // sequence 3. It cannot prove a new endpoint owns the connection.
        byte[] reordered = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 2, payload: new byte[] { 0x20 });
        Assert.False(VerifyAndApply(connection, reordered, EndpointC, 4,
            out result));
        Assert.Equal(ReceiveResult.OutOfOrder, result);
        Assert.Equal(EndpointB, connection.Endpoint);
        Assert.Equal(3, connection.LastReceived);
    }

    [Fact]
    public void AuthenticatedReceiveOrderingNeverWrapsAtTheSerialHalfRange()
    {
        var connection = Receiver();
        byte[] high = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 0x80000001u, payload: new byte[] { 0x01 });
        Assert.True(VerifyAndApply(connection, high, EndpointA, 1, out ReceiveResult result));
        Assert.Equal(ReceiveResult.Newest, result);

        byte[] reordered = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 0x80000000u, payload: new byte[] { 0x02 });
        Assert.True(VerifyAndApply(connection, reordered, EndpointA, 2, out result));
        Assert.Equal(ReceiveResult.OutOfOrder, result);

        byte[] capturedLow = Sign(NetMessageType.Input, NetHeaderFlags.HasAck,
            connection.Id, 0, ack: uint.MaxValue, payload: new byte[] { 0x03 });
        Assert.False(VerifyAndApply(connection, capturedLow, EndpointB, 3, out result));
        Assert.Equal(ReceiveResult.TooOld, result);
        Assert.Equal(EndpointA, connection.Endpoint);
        Assert.Equal(1, connection.LastReceived);
    }

    [Fact]
    public void OldSessionKeyIsRejectedBeforeAnyConnectionMutation()
    {
        var connection = Receiver();
        byte[] oldKey = (byte[])Key.Clone();
        oldKey[^1] ^= 1;
        byte[] datagram = SignWithKey(oldKey, NetAuthDirection.ClientToServer,
            new NetHeader(NetMessageType.Input, NetHeaderFlags.None,
                connection.Id, 1, 0, 0), new byte[] { 0x01 });

        Assert.False(connection.TryVerify(datagram, EndpointA, out _));
        Assert.Equal(0, connection.LastReceived);
        Assert.Equal(EndpointA, connection.Endpoint);
        Assert.Equal(1, connection.Metrics.AuthFailures);
    }

    [Fact]
    public void KeepaliveCountersAreFreshAndNeverAckOrRebind()
    {
        var connection = Receiver();
        byte[] first = KeepAlive(connection.Id, 1);
        Assert.True(VerifyAndApply(connection, first, EndpointA, 1,
            out ReceiveResult result));
        Assert.Equal(ReceiveResult.TooOld, result);
        Assert.Equal(1, connection.LastReceived);

        Assert.False(VerifyAndApply(connection, first, EndpointA, 2, out _));
        Assert.Equal(1, connection.LastReceived);
        Assert.Equal(1, connection.Metrics.KeepAliveReplayRejects);

        byte[] second = KeepAlive(connection.Id, 2);
        Assert.True(VerifyAndApply(connection, second, EndpointA, 2,
            out _));
        Assert.Equal(2, connection.LastReceived);

        byte[] wrongEndpoint = KeepAlive(connection.Id, 3);
        Assert.True(connection.TryVerify(wrongEndpoint, EndpointB,
            out NetConnection.VerifiedPacket verified));
        Assert.False(connection.ApplyVerified(verified, EndpointB, 3, out _));
        Assert.Equal(EndpointA, connection.Endpoint);
        Assert.Equal(2, connection.LastReceived);

        // Keepalives do not consume the sequenced receive window.
        byte[] sequenced = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 1, payload: new byte[] { 0x99 });
        Assert.True(VerifyAndApply(connection, sequenced, EndpointA, 3,
            out result));
        Assert.Equal(ReceiveResult.Newest, result);
    }

    [Fact]
    public void AuthenticatedKeepaliveCounterNeverWrapsAtTheSerialHalfRange()
    {
        var connection = Receiver();
        byte[] high = KeepAlive(connection.Id, 0x8000000000000001ul);
        Assert.True(VerifyAndApply(connection, high, EndpointA, 1, out _));

        byte[] capturedLow = KeepAlive(connection.Id, 1);
        Assert.False(VerifyAndApply(connection, capturedLow, EndpointA, 2, out _));
        Assert.Equal(1, connection.LastReceived);
        Assert.Equal(1, connection.Metrics.KeepAliveReplayRejects);
    }

    [Fact]
    public void KeyedConnectionRejectsLegacyReceiveAndErasesKeyOnDisconnect()
    {
        var connection = Receiver();
        Assert.False(connection.TryReceive(
            new NetHeader(NetMessageType.Input, NetHeaderFlags.None, connection.Id, 1, 0, 0),
            EndpointB, 1, out _));
        Assert.Equal(EndpointA, connection.Endpoint);
        Assert.Equal(0, connection.LastReceived);

        byte[] datagram = Sign(NetMessageType.Input, NetHeaderFlags.None,
            connection.Id, 1, payload: new byte[] { 0x01 });
        connection.Disconnect();
        Assert.False(connection.TryVerify(datagram, EndpointA, out _));
        Assert.Equal(NetConnectionState.Disconnecting, connection.State);
    }

    [Fact]
    public void AuthenticationHotPathStaysWithinBoundedAllocationBudget()
    {
        byte[] payload = { 1, 2, 3, 4 };
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payload.Length)];
        var header = new NetHeader(NetMessageType.Input, NetHeaderFlags.None,
            71, 1, 0, 0);
        for (int i = 0; i < 8; i++)
        {
            NetAuthentication.Sign(Key, NetAuthDirection.ClientToServer,
                header, payload, datagram);
            Assert.True(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
                datagram, out _, out _));
            header = header with { Sequence = header.Sequence + 1 };
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            NetAuthentication.Sign(Key, NetAuthDirection.ClientToServer,
                header, payload, datagram);
            Assert.True(NetAuthentication.TryVerify(Key, NetAuthDirection.ClientToServer,
                datagram, out _, out _));
            header = header with { Sequence = header.Sequence + 1 };
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AuthenticatedSequenceBoundaryRetiresBeforeWrapAndBlocksOtherSends()
    {
        var connection = Receiver();
        connection.SetNextSequenceForTesting(uint.MaxValue);

        Assert.Throws<InvalidOperationException>(() =>
            connection.CreateHeader(NetMessageType.Input));
        Assert.True(connection.IsSequenceExhausted);
        Assert.Equal(NetConnectionState.Disconnecting, connection.State);

        Assert.True(connection.Reliable.TryEnqueue(ReliableEventType.WorldEvent,
            new byte[] { 0x01 }, out _));
        var sink = new CaptureSink();
        connection.FlushReliable(sink, 1);
        Assert.Empty(sink.Datagram);
        Assert.Equal(1, connection.Reliable.PendingCount);
        Assert.Throws<InvalidOperationException>(() => connection.CreateKeepAliveDescriptor());

        // The unkeyed compatibility seam retains legacy uint behavior until
        // established bootstrap authentication is wired in B2.
        var legacy = new NetConnection(72, EndpointA, 9, 0);
        legacy.SetNextSequenceForTesting(uint.MaxValue);
        Assert.Equal(uint.MaxValue, legacy.CreateHeader(NetMessageType.Input).Sequence);
        Assert.Equal(0u, legacy.CreateHeader(NetMessageType.Input).Sequence);
    }

    [Fact]
    public void TransportPreservesKeepaliveCountersAcrossRepublishAndRetiresExhaustion()
    {
        using var transport = new NetTransport(0);
        using var receiverA = ReceiverSocket();
        using var receiverB = ReceiverSocket();
        var endpointA = (IPEndPoint)receiverA.Client.LocalEndPoint!;
        var endpointB = (IPEndPoint)receiverB.Client.LocalEndPoint!;
        var descriptor = new NetKeepAliveDescriptor(endpointA, 9001, 1,
            Key, NetAuthDirection.ServerToClient);

        transport.SetKeepAliveDescriptors(new[] { descriptor });
        AssertCounter(receiverA, Key, 1);
        AssertCounter(receiverA, Key, 2);

        // The caller's counter is stale, but the transport-owned counter is 3.
        transport.SetKeepAliveDescriptors(new[] { descriptor with { Endpoint = endpointB } });
        AssertCounter(receiverB, Key, 3);

        transport.SetKeepAliveDescriptors(new[]
        {
            descriptor with { Endpoint = endpointB, Counter = ulong.MaxValue }
        });
        AssertCounter(receiverB, Key, ulong.MaxValue);

        // Republishing the same connection/key cannot revive an exhausted
        // counter. A fresh key is a new generation and starts at its descriptor.
        transport.SetKeepAliveDescriptors(new[] { descriptor with { Endpoint = endpointB } });
        Assert.Throws<SocketException>(() => Receive(receiverB));

        byte[] freshKey = (byte[])Key.Clone();
        freshKey[0] ^= 1;
        transport.SetKeepAliveDescriptors(new[]
        {
            new NetKeepAliveDescriptor(endpointB, 9001, 1, freshKey,
                NetAuthDirection.ServerToClient)
        });
        AssertCounter(receiverB, freshKey, 1);
        transport.Dispose();
        transport.Dispose();
    }

    private static NetConnection Receiver()
        => new(71, EndpointA, 9, 0, Key, NetAuthDirection.ServerToClient);

    private static bool VerifyAndApply(NetConnection connection, byte[] datagram,
        IPEndPoint sender, double now, out ReceiveResult result)
    {
        if (!connection.TryVerify(datagram, sender,
            out NetConnection.VerifiedPacket verified))
        {
            result = ReceiveResult.TooOld;
            return false;
        }
        return connection.ApplyVerified(verified, sender, now, out result);
    }

    private static byte[] Sign(NetMessageType type, NetHeaderFlags flags,
        ulong connectionId, uint sequence, uint ack = 0, uint ackBits = 0,
        ReadOnlySpan<byte> payload = default)
        => SignWithKey(Key, NetAuthDirection.ClientToServer,
            new NetHeader(type, flags, connectionId, sequence, ack, ackBits), payload);

    private static byte[] SignWithKey(byte[] key, NetAuthDirection direction,
        NetHeader header, ReadOnlySpan<byte> payload)
    {
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payload.Length)];
        Assert.Equal(datagram.Length, NetAuthentication.Sign(key, direction, header,
            payload, datagram));
        return datagram;
    }

    private static byte[] SignWithHeader(NetHeader header, ReadOnlySpan<byte> payload)
        => SignWithKey(Key, NetAuthDirection.ClientToServer, header, payload);

    private static byte[] KeepAlive(ulong connectionId, ulong counter)
    {
        byte[] payload = new byte[NetAuthentication.CounterSize];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, counter);
        return Sign(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced,
            connectionId, 0, payload: payload);
    }

    private static UdpClient ReceiverSocket()
    {
        var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        receiver.Client.ReceiveTimeout = 1800;
        return receiver;
    }

    private static byte[] Receive(UdpClient receiver)
    {
        IPEndPoint sender = new(IPAddress.Any, 0);
        return receiver.Receive(ref sender);
    }

    private static void AssertCounter(UdpClient receiver, byte[] key, ulong expected)
    {
        byte[] datagram = Receive(receiver);
        Assert.True(NetAuthentication.TryVerify(key, NetAuthDirection.ServerToClient,
            datagram, out NetHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(NetMessageType.KeepAlive, header.Type);
        Assert.Equal(NetHeaderFlags.Unsequenced, header.Flags);
        Assert.Equal(8, payload.Length);
        Assert.Equal(expected, BinaryPrimitives.ReadUInt64LittleEndian(payload));
    }

    private sealed class CaptureSink : INetDatagramSink
    {
        public IPEndPoint? Target { get; private set; }
        public byte[] Datagram { get; private set; } = Array.Empty<byte>();

        public void SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram)
        {
            Target = new IPEndPoint(endpoint.Address, endpoint.Port);
            Datagram = datagram.ToArray();
        }
    }
}
