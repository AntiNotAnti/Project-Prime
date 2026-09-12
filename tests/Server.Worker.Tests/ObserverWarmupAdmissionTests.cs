using System.Net;
using MphRead;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Worker.Tests;

public sealed class ObserverWarmupAdmissionTests
{
    [Fact]
    public void DelayedObserverConnectsDuringWarmupWithoutReceivingLiveSnapshotBaseline()
    {
        using var transport = new MemoryTransport();
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var server = new ServerNetwork(transport, rules, observers: new ObserverOptions(1, 30));
        server.Poll(1);
        server.CaptureObserverSnapshot(new byte[] { 1 });
        server.CaptureObserverWorld(new byte[] { 2 });
        server.CommitObserverTick(1);

        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "WATCH", Observer: true);
        byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
        join.Write(bytes.AsSpan(NetHeader.Size));
        transport.Incoming.Enqueue(new(new IPEndPoint(IPAddress.Loopback, 50002), bytes, bytes.Length));

        for (uint tick = 2; tick < 12; tick++) server.Poll(tick);

        Assert.Equal(1, server.ObserverCount);
        ServerPeer observer = Assert.Single(server.ObserverPeers.ToArray(), peer => peer != null)!;
        Assert.True(observer.ObserverNeedsBaseline);
        Assert.Null(observer.ObserverCursor);
    }

    private sealed class MemoryTransport : INetTransport
    {
        public Queue<ReceivedPacket> Incoming { get; } = new();
        public int LocalPort => 50001;
        public long PacketsDropped => 0;
        public int QueuedPackets => Incoming.Count;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();
        public IEnumerable<ReceivedPacket> Drain()
        {
            while (Incoming.TryDequeue(out ReceivedPacket item)) yield return item;
        }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes, long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
