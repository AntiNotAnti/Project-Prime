using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class WorkerNetworkHubTests
{
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 50000);

    [Fact]
    public void RoutesAreBoundedIsolatedAndRemovedWithoutClosingSocket()
    {
        var physical = new MemoryTransport(); var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using var first = hub.RegisterMatch(1, 2, 2); using var second = hub.RegisterMatch(2, 2, 2);
        ulong firstId = first.AllocateConnectionId(), secondId = second.AllocateConnectionId();
        Assert.NotEqual(firstId, secondId);
        router.Route = new(1, firstId, false);
        for (int i = 0; i < 3; i++) physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1));
        hub.Pump(); Assert.Equal(2, first.QueuedPackets); Assert.Equal(0, second.QueuedPackets); Assert.Equal(1, first.PacketsDropped);
        router.Route = new(2, firstId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 2 }, 1));
        hub.Pump(); Assert.Equal(1, hub.Metrics.PacketsRejected); Assert.Equal(0, second.QueuedPackets);
        first.Dispose(); Assert.False(physical.Disposed);
        router.Route = new(0, firstId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 3 }, 1));
        hub.Pump(); Assert.Equal(2, hub.Metrics.PacketsRejected);
        Assert.Throws<InvalidOperationException>(() => first.AllocateConnectionId());
        router.Route = new(2, secondId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 4 }, 1));
        hub.Pump(); Assert.Equal(4, Assert.Single(second.Drain()).Data[0]);
        second.RemoveConnection(secondId); Assert.NotEqual(secondId, second.AllocateConnectionId());
    }

    [Fact]
    public void QueuePressureEvictsUpdatesForReliableControlAndOwnsBytes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 2, 2); ulong id = match.AllocateConnectionId();
        byte[] update = Packet(NetMessageType.Snapshot, id);
        match.SendDatagram(Endpoint, update); match.SendDatagram(Endpoint, update);
        byte[] control = Packet(NetMessageType.Ack, id); match.SendDatagram(Endpoint, control); control[2] = 255;
        Assert.Equal(2, match.HeldOutgoingPackets); Assert.Equal(1, match.PacketsDropped);
        hub.Pump();
        Assert.Equal(NetMessageType.Ack, ReadHeader(physical.Sent[0]).Type);
        Assert.Equal(NetMessageType.Snapshot, ReadHeader(physical.Sent[1]).Type);
        Assert.Equal(2, match.Metrics.PacketsSent);
    }

    [Fact]
    public void SingleReaderAndConcurrentProducersStayBounded()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter { Route = new(1, 0, true) });
        using var match = hub.RegisterMatch(1, 8, 8);
        Parallel.For(0, 1000, i => match.SendDatagram(Endpoint, new byte[] { 1 }));
        Assert.Equal(8, match.HeldOutgoingPackets); Assert.Equal(992, match.PacketsDropped);
        physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1)); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 2 }, 1)); hub.Pump();
        using var reader = match.Drain().GetEnumerator(); Assert.True(reader.MoveNext());
        Assert.Throws<InvalidOperationException>(() => match.Drain().ToArray());
    }

    [Fact]
    public void LegacyJoinIsExplicitlySingleMatchAndReliableWelcomeCompletesThroughVirtualTransport()
    {
        using var physical = new NetTransport(0);
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new LegacySingleMatchRouter(1));
        using var match = hub.RegisterMatch(1);
        Assert.Throws<InvalidOperationException>(() => hub.RegisterMatch(2));
        var server = new ServerNetwork(match, "MP1 SANCTORUS", GameMode.Battle);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport, new(IPAddress.Loopback, hub.LocalPort), "virtual", Hunter.Samus);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 5 && client.Connection == null)
        { client.Poll(); hub.Pump(); server.Poll(1); hub.Pump(); Thread.Sleep(1); }
        Assert.NotNull(client.Connection); Assert.Equal(1, server.Count);
        Assert.True(client.Ready(client.Accepted.MatchId));
        while (timer.Elapsed.TotalSeconds < 5 && server.Peers[0]!.Connection.State != NetConnectionState.Ready)
        { client.Poll(); hub.Pump(); server.Poll(2); hub.Pump(); Thread.Sleep(1); }
        Assert.Equal(NetConnectionState.Ready, server.Peers[0]!.Connection.State);
        ulong id = client.Connection!.Id; server.Remove(0); Assert.Null(server.Find(id));
        Assert.True(match.Metrics.PacketsReceived > 0); Assert.True(match.Metrics.PacketsSent > 0);
    }

    [Fact]
    public void ReliableRetransmissionPreservesPayloadAfterOutboundDrop()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 1, 1); ulong id = match.AllocateConnectionId();
        var connection = new NetConnection(id, Endpoint, 1, 0);
        connection.Reliable.TryEnqueue(ReliableEventType.Disconnect, ReadOnlySpan<byte>.Empty, out uint eventId);
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id)); // Occupy the one control slot.
        connection.FlushReliable(match, 0); hub.Pump(); Assert.Equal(1, match.PacketsDropped);
        connection.FlushReliable(match, 10); hub.Pump();
        Assert.True(ReliableEventPacket.TryRead(physical.Sent[^1].AsSpan(NetHeader.Size), out uint resentId, out var type, out _));
        Assert.Equal(eventId, resentId); Assert.Equal(ReliableEventType.Disconnect, type);
        NetHeader sent = ReadHeader(physical.Sent[^1]);
        connection.TryReceive(new NetHeader(NetMessageType.Ack, NetHeaderFlags.HasAck, id, 1, sent.Sequence, 0), Endpoint, 11, out _);
        Assert.Equal(0, connection.Reliable.PendingCount);
    }

    [Fact]
    public void ActualRoutedJoinFramingPreservesFullWidthIdsAndRejectsUnknownOrLegacyRoutes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new RoutedMatchDatagramRouter());
        using var first = hub.RegisterMatch(70000); using var second = hub.RegisterMatch(80000);
        foreach (uint wire in new uint[] { 70000, 80000, 90000, 0 })
        {
            var join = new JoinPacket(NetHeader.Version, 42, Hunter.Samus, "routed", WireMatchId: wire);
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            join.Write(bytes.AsSpan(NetHeader.Size)); physical.Incoming.Enqueue(new(Endpoint, bytes, bytes.Length));
        }
        hub.Pump();
        Assert.Single(first.Drain()); Assert.Single(second.Drain()); Assert.Equal(2, hub.Metrics.PacketsRejected);
    }

    [Fact]
    public void GlobalRoutingBudgetBoundsUnknownFloodWithoutAllocatingRoutes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new RoutedMatchDatagramRouter());
        using var match = hub.RegisterMatch(1, maxConnections: 32);
        for (int i = 0; i < 1000; i++) physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, (ulong)(i + 1)), NetHeader.Size));
        hub.Pump(); Assert.Equal(WorkerNetworkHub.MaxRoutingAttemptsPerPump, hub.Metrics.PacketsRejected);
        Assert.Equal(1, hub.RoutingBudgetExhaustions); Assert.Equal(0, match.QueuedPackets);
        for (int i = 0; i < 32; i++) match.AllocateConnectionId();
        Assert.Throws<InvalidOperationException>(() => match.AllocateConnectionId());
    }

    private static byte[] Packet(NetMessageType type, ulong id)
    { byte[] result = new byte[NetHeader.Size]; new NetHeader(type, NetHeaderFlags.None, id, 1, 0, 0).Write(result); return result; }
    private static NetHeader ReadHeader(byte[] bytes) { Assert.True(NetHeader.TryRead(bytes, out var header)); return header; }
    private sealed class TestRouter : IWorkerDatagramRouter
    {
        public WorkerDatagramRoute Route;
        public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route) { route = Route; return true; }
    }
    private sealed class MemoryTransport : INetTransport
    {
        public ConcurrentQueue<ReceivedPacket> Incoming = new(); public List<byte[]> Sent = new(); public bool Disposed;
        public int LocalPort => 50001; public long PacketsDropped => 0; public int QueuedPackets => Incoming.Count;
        public int HeldIncomingPackets => 0; public int HeldOutgoingPackets => 0; public NetTrafficMetrics Metrics { get; } = new();
        public IEnumerable<ReceivedPacket> Drain() { while (Incoming.TryDequeue(out var item)) yield return item; }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes) => Sent.Add(bytes.ToArray());
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) => SendDatagram(target, bytes);
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes, long extraHoldTicks = 0) => throw new NotSupportedException();
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
}
