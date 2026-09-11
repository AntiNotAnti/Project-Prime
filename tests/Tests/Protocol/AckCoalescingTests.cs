using System;
using System.Collections.Generic;
using System.Net;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class AckCoalescingTests
{
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 27888);
    private static readonly byte[] Key =
    {
        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
        0x10, 0x21, 0x32, 0x43, 0x54, 0x65, 0x76, 0x87,
        0x98, 0xA9, 0xBA, 0xCB, 0xDC, 0xED, 0xFE, 0x0F
    };

    [Fact]
    public void CarrierPiggybackDoesNotSuppressDeadlineFallbackOnVoidSink()
    {
        var connection = new NetConnection(42, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new CaptureSink();
        Assert.True(connection.TryReceive(
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                connection.Id, 10, 0, 0), Endpoint, 1, out _));

        connection.RequestAck(20);
        connection.Send(sink, NetMessageType.Input, new byte[] { 0x01 });

        Assert.Equal(0, connection.Metrics.PiggybackAcks);
        Assert.Equal(1, connection.Metrics.PiggybackAckAttempts);
        Assert.True(connection.AckPending);
        Assert.False(connection.FlushPendingAck(sink, 20, 1));
        Assert.True(connection.FlushPendingAck(sink, 21, 1));
        Assert.Equal(0, connection.Metrics.StandaloneAcks);
        Assert.Equal(1, connection.Metrics.StandaloneAckAttempts);
        Assert.Equal(1, connection.Metrics.AckDeadlineExpirations);
        Assert.Equal(NetMessageType.Ack, sink.Types[^1]);
        Assert.False(connection.AckPending);
    }

    [Fact]
    public void AcceptedCarrierClearsPendingGenerationWithoutStandaloneAck()
    {
        var connection = new NetConnection(43, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new AcceptedSink();
        Assert.True(connection.TryReceive(
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                connection.Id, 11, 0, 0), Endpoint, 1, out _));

        connection.RequestAck(30);
        connection.Send(sink, NetMessageType.Snapshot, new byte[] { 0x02 });

        Assert.Equal(1, sink.AcceptedCount);
        Assert.Equal(1, connection.Metrics.PiggybackAcks);
        Assert.Equal(1, connection.Metrics.PiggybackAckAttempts);
        Assert.False(connection.AckPending);
        Assert.False(connection.FlushPendingAck(sink, 31, 1));
        Assert.Equal(0, connection.Metrics.StandaloneAcks);
    }

    [Fact]
    public void TimeBasedDeadlineDoesNotBecomeTickDueAtNonzeroPollTick()
    {
        var connection = new NetConnection(47, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new AcceptedSink();
        Assert.True(connection.TryReceive(
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                connection.Id, 13, 0, 0), Endpoint, 1, out _));

        const double started = 10;
        connection.RequestAck(started);
        double due = started + NetConfig.SimulationTickSeconds;
        Assert.False(connection.FlushPendingAck(sink, 17, due - 0.000001));
        Assert.True(connection.AckPending);
        Assert.True(connection.FlushPendingAck(sink, 17, due));
        Assert.False(connection.AckPending);
        Assert.Equal(1, connection.Metrics.StandaloneAcks);
    }

    [Fact]
    public void ForcedFinalAckBeforeDeadlineIsAttemptedButNotCountedAsExpiration()
    {
        var connection = new NetConnection(48, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new AcceptedSink();
        Assert.True(connection.TryReceive(
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                connection.Id, 14, 0, 0), Endpoint, 1, out _));

        connection.RequestAck(10.0);
        Assert.True(connection.FlushPendingAck(sink, 99, 10.001, force: true));
        Assert.Equal(1, connection.Metrics.StandaloneAcks);
        Assert.Equal(1, connection.Metrics.StandaloneAckAttempts);
        Assert.Equal(0, connection.Metrics.AckDeadlineExpirations);
    }

    [Fact]
    public void RejectedAcceptedCarrierRetainsDeadlineForBoundedRetry()
    {
        var connection = new NetConnection(44, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new RejectingAcceptedSink();
        Assert.True(connection.TryReceive(
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                connection.Id, 12, 0, 0), Endpoint, 1, out _));

        connection.RequestAck(40);
        connection.Send(sink, NetMessageType.Input, new byte[] { 0x03 });
        Assert.True(connection.AckPending);
        Assert.True(connection.FlushPendingAck(sink, 41, 1));
        Assert.True(connection.AckPending);
        Assert.True(connection.FlushPendingAck(sink, 42, 1));
        Assert.Equal(0, connection.Metrics.StandaloneAcks);
        Assert.Equal(2, connection.Metrics.StandaloneAckAttempts);
        Assert.Equal(0, connection.Metrics.PiggybackAcks);
        Assert.Equal(1, connection.Metrics.PiggybackAckAttempts);
        Assert.Equal(3, sink.RejectedCount); // carrier plus two deadline attempts
    }

    [Fact]
    public void AuthenticatedReliableDuplicateRenewsAckWithoutRefreshingLiveness()
    {
        var connection = new NetConnection(45, Endpoint, 7, 10, Key,
            NetAuthDirection.ServerToClient, ackCoalescingEnabled: true);
        byte[] body = new byte[ReliableEventPacket.HeaderSize + 1];
        int bodyLength = ReliableEventPacket.Write(body, 9, ReliableEventType.Chat,
            new byte[] { 0x04 });
        var header = new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
            connection.Id, 1, 0, 0);
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(bodyLength)];
        Assert.Equal(datagram.Length, NetAuthentication.Sign(Key,
            NetAuthDirection.ClientToServer, header, body.AsSpan(0, bodyLength), datagram));

        Assert.True(connection.TryVerify(datagram, Endpoint,
            out NetConnection.VerifiedPacket first));
        Assert.True(connection.ApplyVerified(first, Endpoint, 11, out ReceiveResult result));
        Assert.Equal(ReceiveResult.Newest, result);
        connection.RequestAck(50);
        Assert.True(connection.FlushPendingAck(new CaptureSink(), 51, 1));

        Assert.True(connection.TryVerify(datagram, Endpoint,
            out NetConnection.VerifiedPacket duplicate));
        Assert.True(connection.ApplyVerified(duplicate, Endpoint, 99, out result));
        Assert.Equal(ReceiveResult.Duplicate, result);
        Assert.Equal(11, connection.LastReceived);
        connection.RequestAck(52);
        Assert.True(connection.FlushPendingAck(new CaptureSink(), 53, 1));
        Assert.Equal(0, connection.Metrics.StandaloneAcks);
        Assert.Equal(2, connection.Metrics.StandaloneAckAttempts);
    }

    [Fact]
    public void UnkeyedReliableDuplicateRenewsAckWithoutSecondDelivery()
    {
        var connection = new NetConnection(46, Endpoint, 7, 0,
            ackCoalescingEnabled: true);
        var sink = new CaptureSink();
        var eventHeader = new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
            connection.Id, 1, 0, 0);
        Assert.True(connection.TryReceive(eventHeader, Endpoint, 1, out ReceiveResult first));
        Assert.Equal(ReceiveResult.Newest, first);
        Assert.True(connection.Reliable.Receive(1));
        connection.RequestAck(60);
        Assert.True(connection.FlushPendingAck(sink, 61, 1));

        Assert.True(connection.TryReceive(eventHeader, Endpoint, 2, out ReceiveResult duplicate));
        Assert.Equal(ReceiveResult.Duplicate, duplicate);
        Assert.False(connection.Reliable.Receive(1));
        connection.RequestAck(62);
        Assert.True(connection.FlushPendingAck(sink, 63, 1));
        Assert.Equal(0, connection.Metrics.StandaloneAcks);
        Assert.Equal(2, connection.Metrics.StandaloneAckAttempts);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    public void ClientAckCoalescingIsAnExplicitOptIn(string? value, bool expected)
        => Assert.Equal(expected, NetLaunch.ParseOptIn(value));

    [Fact]
    public void ClientAckCoalescingSettingSurvivesReconnect()
    {
        using var transport = new NetTransport(0);
        using var client = new NetClient(transport, Endpoint, "PLAYER", MphRead.Hunter.Samus,
            nonce: 1, ackCoalescingEnabled: true);

        Assert.True(client.AckCoalescingEnabled);
        client.Reconnect(nonce: 2);
        Assert.True(client.AckCoalescingEnabled);
    }

    private class CaptureSink : INetDatagramSink
    {
        public List<NetMessageType> Types { get; } = new();

        public virtual void SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram)
        {
            Assert.True(NetHeader.TryRead(datagram, out NetHeader header));
            Types.Add(header.Type);
        }
    }

    private sealed class AcceptedSink : CaptureSink, IAcceptedNetDatagramSink
    {
        public int AcceptedCount { get; private set; }

        public bool TrySendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram,
            NetDeliveryClass deliveryClass)
        {
            SendDatagram(endpoint, datagram);
            AcceptedCount++;
            return true;
        }
    }

    private sealed class RejectingAcceptedSink : CaptureSink, IAcceptedNetDatagramSink
    {
        public int RejectedCount { get; private set; }

        public bool TrySendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram,
            NetDeliveryClass deliveryClass)
        {
            RejectedCount++;
            return false;
        }
    }
}
