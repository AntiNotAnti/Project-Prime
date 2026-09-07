using System;
using System.Diagnostics;
using System.Net;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ConnectionTests
    {
        [Fact]
        public void HeaderRoundTripsAndRejectsMalformedEnvelopes()
        {
            var header = new NetHeader(NetMessageType.Event, NetHeaderFlags.HasAck,
                0x123456789ABCDEF, UInt32.MaxValue, 5, 0x80000001);
            byte[] bytes = new byte[NetHeader.Size];
            header.Write(bytes);
            Assert.True(NetHeader.TryRead(bytes, out NetHeader decoded));
            Assert.Equal(header, decoded);
            for (int size = 0; size < bytes.Length; size++)
            {
                Assert.False(NetHeader.TryRead(bytes.AsSpan(0, size), out _));
            }
            bytes[3] = 255;
            Assert.False(NetHeader.TryRead(bytes, out _));
            header.Write(bytes);
            bytes[2] = 255;
            Assert.False(NetHeader.TryRead(bytes, out _));
            header.Write(bytes);
            bytes[0] ^= 1;
            Assert.False(NetHeader.TryRead(bytes, out _));
            Assert.False(NetHeader.TryRead(new byte[NetConfig.MaxPacketSize + 1], out _));
        }

        [Fact]
        public void JoinPreservesNonceIdentityAndRejectsBadHunter()
        {
            Guid capability = Guid.NewGuid();
            var join = new JoinPacket(NetHeader.Version, 42, Hunter.Trace, "TRACE", 99,
                OwnerCapability: capability);
            byte[] bytes = new byte[JoinPacket.Size];
            join.Write(bytes);
            Assert.Equal(50, bytes.Length);
            Assert.True(JoinPacket.TryRead(bytes, out JoinPacket decoded));
            Assert.Equal(join, decoded);
            Assert.Equal(capability, decoded.OwnerCapability);
            bytes[9] = 255;
            Assert.False(JoinPacket.TryRead(bytes, out _));
            join.Write(bytes);
            bytes[10] = 10;
            Assert.False(JoinPacket.TryRead(bytes, out _));
            Assert.False(JoinPacket.TryRead(bytes.AsSpan(1), out _));
            byte[] missingCapability = new byte[JoinPacket.Size - JoinPacket.OwnerCapabilitySize];
            Assert.False(JoinPacket.TryRead(missingCapability, out _));
        }

        [Fact]
        public void LoadingKeepaliveDoesNotAdmitGameplayOrAcceptOldReady()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
            var connection = new NetConnection(42, endpoint, 7, 0);
            var keepalive = new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced, 42, 0, 0, 0);
            Assert.True(connection.TryReceive(keepalive, endpoint, 20, out _));
            Assert.Equal(20, connection.LastReceived);
            Assert.Equal(NetConnectionState.Loading, connection.State);
            Assert.False(connection.StartPlaying());
            Assert.False(connection.Ready(6));
            Assert.True(connection.Ready(7));
            Assert.True(connection.StartPlaying());
            connection.BeginLoading(8);
            Assert.False(connection.Ready(7));
            Assert.Equal(NetConnectionState.Loading, connection.State);
        }

        [Fact]
        public void OnlyNewSessionTrafficCanRebindAnEndpoint()
        {
            var first = new IPEndPoint(IPAddress.Loopback, 12345);
            var second = new IPEndPoint(IPAddress.Loopback, 12346);
            var connection = new NetConnection(42, first, 7, 0);
            var header = new NetHeader(NetMessageType.Input, NetHeaderFlags.None, 42, 10, 0, 0);
            Assert.True(connection.TryReceive(header, first, 1, out ReceiveResult result));
            Assert.Equal(ReceiveResult.Newest, result);
            Assert.False(connection.TryReceive(header, second, 2, out _));
            Assert.False(connection.TryReceive(header with { ConnectionId = 41, Sequence = 11 }, second, 2, out _));
            Assert.True(connection.TryReceive(header with { Sequence = 11 }, second, 2, out _));
            Assert.Equal(second, connection.Endpoint);
            Assert.False(connection.TryReceive(header, first, 3, out _));
        }

        [Fact]
        public void ClockUnwrapsServerTicksAndRejectsOldReplies()
        {
            var clock = new NetClock();
            long time = Stopwatch.Frequency * 10;
            Assert.True(clock.Observe(time, time + Stopwatch.Frequency / 10, UInt32.MaxValue - 2));
            double estimate = clock.EstimateServerTick(time + Stopwatch.Frequency / 20);
            Assert.Equal(UInt32.MaxValue - 2, estimate, 4);
            Assert.True(clock.Observe(time + Stopwatch.Frequency, time + Stopwatch.Frequency * 11 / 10, 57));
            Assert.False(clock.Observe(time + Stopwatch.Frequency, time, 58));
            Assert.False(clock.Observe(time, time + Stopwatch.Frequency / 10, UInt32.MaxValue - 10));
            Assert.Equal(100, clock.Metrics.Rtt.Mean, 4);
        }

        [Fact]
        public void RateLimitBoundsBurstsAndRefillsWithoutClockRollback()
        {
            var limit = new NetRateLimit(2, 3, 0);
            Assert.True(limit.Take(0));
            Assert.True(limit.Take(0));
            Assert.True(limit.Take(0));
            Assert.False(limit.Take(0));
            Assert.False(limit.Take(0.49));
            Assert.True(limit.Take(0.5));
            Assert.False(limit.Take(0.4));
            Assert.False(limit.Take(Double.NaN));
        }

        [Fact]
        public void ReliableEventsSurviveLossReorderingAndLostAcksExactlyOnce()
        {
            var sender = new ReliableChannel(UInt32.MaxValue - 10);
            var receiver = new ReliableChannel();
            var acks = new ReceiveWindow();
            var random = new Random(500);
            for (int i = 0; i < ReliableChannel.Capacity; i++)
            {
                Assert.True(sender.TryEnqueue(ReliableEventType.MatchState, new byte[] { (byte)i }, out _));
            }
            Assert.False(sender.TryEnqueue(ReliableEventType.MatchState, new byte[1], out _));
            int delivered = 0;
            uint sequence = UInt32.MaxValue - 100;
            var queued = new System.Collections.Generic.List<(double At, uint Seq, uint Id)>();
            for (int tick = 0; tick < 3000 && sender.PendingCount > 0; tick++)
            {
                double now = tick / 60.0;
                for (int budget = 0; budget < ReliableChannel.Capacity
                    && sender.TryGetDue(now, out uint id, out _, out _); budget++)
                {
                    sender.MarkSent(id, sequence, now);
                    if (random.Next(100) >= 30)
                    {
                        queued.Add((now + random.NextDouble() * 0.3, sequence, id));
                    }
                    sequence++;
                }
                for (int i = queued.Count - 1; i >= 0; i--)
                {
                    var packet = queued[i];
                    if (packet.At > now)
                    {
                        continue;
                    }
                    queued.RemoveAt(i);
                    acks.Record(packet.Seq);
                    if (receiver.Receive(packet.Id))
                    {
                        delivered++;
                    }
                    if (random.Next(100) >= 30)
                    {
                        sender.Acknowledge(acks.Ack, acks.AckBits);
                    }
                }
            }
            Assert.Equal(ReliableChannel.Capacity, delivered);
            Assert.Equal(0, sender.PendingCount);
            Assert.True(sender.Retransmissions > 0);
        }

        [Fact]
        public void ReliableIdSpanCannotEvictAnUndeliveredOldMessage()
        {
            var sender = new ReliableChannel();
            var receiver = new ReliableChannel();
            sender.TryEnqueue(ReliableEventType.Welcome, default, out uint oldest);
            for (uint i = 1; i < ReliableChannel.EventWindowCapacity; i++)
            {
                Assert.True(sender.TryEnqueue(ReliableEventType.MatchState, default, out uint id));
                Assert.True(receiver.Receive(id));
                sender.MarkSent(id, i, i);
                sender.Acknowledge(i, 0);
            }
            Assert.Equal(1, sender.PendingCount);
            Assert.False(sender.TryEnqueue(ReliableEventType.MatchState, default, out _));
            Assert.True(receiver.Receive(oldest));
            Assert.False(receiver.Receive(oldest));
            sender.MarkSent(oldest, 100, 100);
            sender.Acknowledge(100, 0);
            Assert.True(sender.TryEnqueue(ReliableEventType.MatchState, default, out _));
        }
    }
}
