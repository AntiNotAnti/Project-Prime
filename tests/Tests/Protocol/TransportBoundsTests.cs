using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;
using Xunit.Abstractions;

namespace MphRead.Tests
{
    // NetLag is process-wide command-line configuration, so these tests cannot
    // overlap tests that create ordinary, unimpaired transports.
    [CollectionDefinition("Transport impairment", DisableParallelization = true)]
    public sealed class TransportImpairmentCollection { }

    [Collection("Transport impairment")]
    public sealed class TransportBoundsTests
    {
        private readonly ITestOutputHelper _output;
        public TransportBoundsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void InboxKeepsNewestPacketsAndEachPollHasFixedWorkBudget()
        {
            using var transport = new NetTransport(0);
            const int total = NetTransport.MaxQueuedPackets + 100;
            for (int i = 0; i < total; i++)
            {
                transport.EnqueueForPlayback(BitConverter.GetBytes(i), sizeof(int));
            }
            Assert.Equal(NetTransport.MaxQueuedPackets, transport.QueuedPackets);
            Assert.Equal(100, transport.PacketsDropped);
            Assert.Equal(100, transport.Metrics.QueueDrops);
            ReceivedPacket[] first = transport.Drain().ToArray();
            Assert.Equal(NetTransport.MaxPacketsPerDrain, first.Length);
            Assert.Equal(100, BitConverter.ToInt32(first[0].Data));
            Assert.Equal(100 + first.Length - 1, BitConverter.ToInt32(first[^1].Data));
            Assert.Equal(NetTransport.MaxQueuedPackets - first.Length, transport.QueuedPackets);
        }

        [Fact]
        public async Task PollTerminatesUnderConcurrentProducerFloodAndAccountsForEveryPacket()
        {
            using var transport = new NetTransport(0);
            byte[] packet = { 1 };
            const int produced = 200000;
            var time = Stopwatch.StartNew();
            Task[] writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < produced / 4; i++)
                {
                    transport.EnqueueForPlayback(packet, packet.Length);
                }
            })).ToArray();
            int consumed = 0;
            int polls = 0;
            while (!writers.All(task => task.IsCompleted) || transport.QueuedPackets > 0)
            {
                int count = transport.Drain().Count();
                Assert.InRange(count, 0, NetTransport.MaxPacketsPerDrain);
                Assert.InRange(transport.QueuedPackets, 0, NetTransport.MaxQueuedPackets);
                consumed += count;
                polls++;
                Assert.True(time.Elapsed.TotalSeconds < 15, "Flood stalled the consumer.");
            }
            await Task.WhenAll(writers);
            Assert.Equal(produced, consumed + transport.PacketsDropped);
            Assert.Equal(transport.PacketsDropped, transport.Metrics.QueueDrops);
            _output.WriteLine($"{produced} queued datagrams from 4 producers: {time.Elapsed.TotalMilliseconds:0.0} ms; "
                + $"{polls} polls; {consumed} consumed; {transport.PacketsDropped} dropped; capacity {NetTransport.MaxQueuedPackets}.");
        }

        [Fact]
        public void DelayedQueuesStayBoundedAndDisposalReleasesTheirPackets()
        {
            Assert.True(NetLag.Configure("10000"));
            using var transport = new NetTransport(0);
            try
            {
                byte[] packet = { 1, 2 };
                var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
                for (int i = 0; i < NetTransport.MaxQueuedPackets + 100; i++)
                {
                    transport.SendDatagram(endpoint, packet);
                }
                Assert.Equal(NetTransport.MaxQueuedPackets, transport.HeldOutgoingPackets);
                Assert.Equal(100, transport.PacketsDropped);
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                // Batch with receive progress checks so this exercises the application
                // queue rather than depending on the operating system's UDP capacity.
                for (int batch = 0; batch < 24; batch++)
                {
                    long before = transport.Metrics.PacketsReceived;
                    for (int i = 0; i < 100; i++)
                    {
                        socket.Send(packet, endpoint);
                    }
                    Assert.True(SpinWait.SpinUntil(() => transport.Metrics.PacketsReceived >= before + 100, 1000));
                }
                Assert.True(SpinWait.SpinUntil(() => transport.HeldIncomingPackets == NetTransport.MaxQueuedPackets, 1000));
                Assert.Equal(0, transport.QueuedPackets);
                Assert.Empty(transport.Drain());
                Assert.True(SpinWait.SpinUntil(() => transport.PacketsDropped == 100 + 2400 - NetTransport.MaxQueuedPackets, 1000));
                var shutdown = Stopwatch.StartNew();
                transport.Dispose();
                transport.Dispose();
                Assert.True(shutdown.Elapsed.TotalSeconds < 2);
                Assert.Equal(0, transport.QueuedPackets);
                Assert.Equal(0, transport.HeldIncomingPackets);
                Assert.Equal(0, transport.HeldOutgoingPackets);
                transport.SendDatagram(endpoint, packet);
                transport.EnqueueForPlayback(packet, packet.Length);
                Assert.Empty(transport.Drain());
                Assert.Equal(0, transport.HeldOutgoingPackets);
                _output.WriteLine($"Delayed flood: inbox 0, incoming/outgoing capped at {NetTransport.MaxQueuedPackets}; "
                    + $"drops {transport.PacketsDropped}; shutdown {shutdown.Elapsed.TotalMilliseconds:0.0} ms.");
            }
            finally
            {
                NetLag.Configure("0");
            }
        }

        [Fact]
        public void DelayedPromotionHonorsInboxCapacityAndPollBudget()
        {
            Assert.True(NetLag.Configure("200"));
            using var transport = new NetTransport(0);
            try
            {
                byte[] packet = { 1 };
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
                for (int i = 0; i < 300; i++)
                {
                    socket.Send(packet, endpoint);
                }
                Assert.True(SpinWait.SpinUntil(() => transport.HeldIncomingPackets == 300, 3000));
                for (int i = 0; i < NetTransport.MaxQueuedPackets; i++)
                {
                    transport.EnqueueForPlayback(packet, packet.Length);
                }
                Thread.Sleep(150); // All 100 ms delayed arrivals are now eligible.
                Assert.Equal(NetTransport.MaxPacketsPerDrain, transport.Drain().Count());
                Assert.Equal(300 - NetTransport.MaxPacketsPerDrain, transport.HeldIncomingPackets);
                Assert.Equal(NetTransport.MaxQueuedPackets - NetTransport.MaxPacketsPerDrain, transport.QueuedPackets);
                Assert.Equal(NetTransport.MaxPacketsPerDrain, transport.PacketsDropped);
            }
            finally
            {
                NetLag.Configure("0");
            }
        }

        [Fact]
        public void DelayedOnlyArrivalExposesAbsoluteDeadlineWithoutBusyReadyState()
        {
            Assert.True(NetLag.Configure("200"));
            using var transport = new NetTransport(0);
            try
            {
                byte[] packet = { 1 };
                using var socket = new UdpClient();
                var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
                socket.Send(packet, endpoint);
                Assert.True(SpinWait.SpinUntil(() => transport.HeldIncomingPackets == 1, 1000));
                Assert.False(transport.HasReadyNetworkWork);
                long now = Stopwatch.GetTimestamp();
                long deadline = transport.NextNetworkDeadlineTimestamp;
                Assert.InRange(deadline, now, now + Stopwatch.Frequency);
            }
            finally
            {
                NetLag.Configure("0");
            }
        }

        [Fact]
        public void DelayedArrivalWakesAnIdleOwnerToRecomputeItsDeadline()
        {
            Assert.True(NetLag.Configure("200"));
            using var wake = new AutoResetEvent(false);
            using var transport = (INetTransport)Activator.CreateInstance(
                typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
            try
            {
                transport.SetNetworkWake(() => { wake.Set(); });
                byte[] packet = { 1 };
                using var socket = new UdpClient();
                var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
                socket.Send(packet, endpoint);

                Assert.True(wake.WaitOne(1000));
                Assert.Equal(1, transport.HeldIncomingPackets);
                Assert.False(transport.HasReadyNetworkWork);
                long now = Stopwatch.GetTimestamp();
                Assert.InRange(transport.NextNetworkDeadlineTimestamp,
                    now, now + Stopwatch.Frequency);
            }
            finally
            {
                NetLag.Configure("0");
            }
        }

        [Fact]
        public void AttachingAnIdleOwnerToExistingDelayedArrivalSignalsItsDeadline()
        {
            Assert.True(NetLag.Configure("200"));
            using var transport = (INetTransport)Activator.CreateInstance(
                typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
            using var wake = new AutoResetEvent(false);
            try
            {
                byte[] packet = { 1 };
                using var socket = new UdpClient();
                var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
                socket.Send(packet, endpoint);
                Assert.True(SpinWait.SpinUntil(() => transport.HeldIncomingPackets == 1, 1000));
                transport.SetNetworkWake(() => { wake.Set(); });
                Assert.True(wake.WaitOne(1000));
                Assert.False(transport.HasReadyNetworkWork);
                Assert.True(transport.NextNetworkDeadlineTimestamp > Stopwatch.GetTimestamp());
            }
            finally
            {
                NetLag.Configure("0");
            }
        }

        [Fact]
        public void InvalidDatagramsAndPlaybackLengthsNeverEnterQueues()
        {
            using var transport = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            transport.SendDatagram(endpoint, ReadOnlySpan<byte>.Empty);
            transport.SendDatagram(endpoint, new byte[NetConfig.MaxPacketSize + 1]);
            transport.EnqueueForPlayback(new byte[1], -1);
            transport.EnqueueForPlayback(new byte[1], 2);
            transport.EnqueueForPlayback(new byte[NetConfig.MaxPacketSize + 1], NetConfig.MaxPacketSize + 1);
            Assert.Equal(5, transport.Metrics.PacketsRejected);
            using var socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Send(Array.Empty<byte>(), endpoint);
            socket.Send(new byte[4096], endpoint);
            Assert.True(SpinWait.SpinUntil(() => transport.Metrics.PacketsRejected == 7, 3000));
            Assert.Equal(2, transport.Metrics.PacketsReceived);
            Assert.Equal(4096, transport.Metrics.BytesReceived);
            Assert.Empty(transport.Drain());
        }

        [Fact]
        public void WorkerKeepsFullPlayerAndObserverCapacityAliveUsingOwnedImmutableDatagrams()
        {
            using var transport = new NetTransport(0);
            var receivers = new UdpClient[NetTransport.MaxKeepAlives];
            var entries = new NetKeepAlive[receivers.Length];
            try
            {
                for (int i = 0; i < receivers.Length; i++)
                {
                    receivers[i] = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    receivers[i].Client.ReceiveTimeout = 3000;
                    byte[] packet = new byte[NetHeader.Size];
                    new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced, (ulong)i + 1, 0, 0, 0).Write(packet);
                    entries[i] = new NetKeepAlive((IPEndPoint)receivers[i].Client.LocalEndPoint!, packet);
                }
                transport.SetKeepAlives(entries);
                Assert.Throws<ArgumentException>(() => transport.SetKeepAlives([entries[0], default]));
                Assert.Throws<ArgumentOutOfRangeException>(() => transport.SetKeepAlives(new NetKeepAlive[NetTransport.MaxKeepAlives + 1]));
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].Endpoint.Port = 9;
                    // ReadOnlyMemory wraps caller storage; publication must have copied it.
                    Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(entries[i].Datagram, out ArraySegment<byte> storage));
                    Array.Clear(storage.Array!);
                }
                Array.Clear(entries);
                // No transport Drain or connection Poll is called. A second full
                // worker interval proves copies remain valid after caller mutation.
                for (int round = 0; round < 2; round++)
                {
                    for (int i = 0; i < receivers.Length; i++)
                    {
                        var sender = new IPEndPoint(IPAddress.Any, 0);
                        byte[] packet = receivers[i].Receive(ref sender);
                        Assert.True(NetHeader.TryRead(packet, out NetHeader header));
                        Assert.Equal(NetMessageType.KeepAlive, header.Type);
                        Assert.Equal(NetHeaderFlags.Unsequenced, header.Flags);
                        Assert.Equal((ulong)i + 1, header.ConnectionId);
                        Assert.Equal(0u, header.Sequence);
                        Assert.Equal(0u, header.AckBits);
                    }
                }
                transport.SetKeepAlives(ReadOnlySpan<NetKeepAlive>.Empty);
            }
            finally
            {
                foreach (UdpClient? receiver in receivers) { receiver?.Dispose(); }
            }
        }

        [Fact]
        public void ImpairmentConfigurationRejectsNonfiniteAndExtraFieldsAndSupportsJitterOnly()
        {
            try
            {
                Assert.False(NetLag.ConfigureLoss("NaN"));
                Assert.False(NetLag.ConfigureLoss("Infinity"));
                Assert.False(NetLag.Configure("1:2:3"));
                Assert.True(NetLag.Configure("0:10"));
                Assert.True(NetLag.Active);
                Assert.NotNull(NetLag.Describe());
            }
            finally
            {
                NetLag.Configure("0");
                NetLag.ConfigureLoss("0");
            }
        }
    }
}
