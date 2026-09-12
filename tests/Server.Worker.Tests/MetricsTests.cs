using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class MetricsTests
    {
        [Fact]
        public void BoundedPercentileSamplerRetainsAFixedWindowAndSeparatesEvictions()
        {
            var sampler = new BoundedPercentileSampler(4);
            sampler.Record(Double.NaN);
            sampler.Record(-1);
            for (int value = 1; value <= 6; value++) sampler.Record(value);

            BoundedPercentileSnapshot snapshot = sampler.Snapshot();
            Assert.Equal(4, snapshot.Count);
            Assert.Equal(6, snapshot.TotalCount);
            Assert.Equal(2, snapshot.Evictions);
            Assert.Equal(4, snapshot.P50);
            Assert.Equal(6, snapshot.P95);
            Assert.Equal(6, snapshot.P99);
            Assert.Equal(6, snapshot.P999);
            Assert.Equal(6, snapshot.Max);

            sampler.Clear();
            Assert.Equal(0, sampler.Snapshot().Count);
            Assert.Equal(0, sampler.TotalCount);
            Assert.Equal(0, sampler.Evictions);
        }

        [Fact]
        public async Task BoundedPercentileSamplerTrySnapshotRemainsOrderedWhileRecording()
        {
            var sampler = new BoundedPercentileSampler(64);
            sampler.Record(0);
            Assert.True(sampler.TrySnapshot(out BoundedPercentileSnapshot initial));
            using var start = new ManualResetEventSlim(false);
            Task writer = Task.Run(() =>
            {
                start.Wait();
                for (int value = 1; value <= 200_000; value++) sampler.Record(value % 257);
            });
            start.Set();
            long previousTotal = initial.TotalCount;
            int stableSnapshots = 1;
            while (!writer.IsCompleted)
            {
                if (!sampler.TrySnapshot(out BoundedPercentileSnapshot snapshot)) continue;
                if (snapshot.Count == 0) continue;
                Assert.True(snapshot.P50 <= snapshot.P95 && snapshot.P95 <= snapshot.P99
                    && snapshot.P99 <= snapshot.P999 && snapshot.P999 <= snapshot.Max);
                Assert.True(snapshot.TotalCount >= previousTotal);
                previousTotal = snapshot.TotalCount;
                stableSnapshots++;
            }
            await writer;
            Assert.True(sampler.TrySnapshot(out BoundedPercentileSnapshot final));
            Assert.Equal(64, final.Count);
            Assert.Equal(200_001, final.TotalCount);
            Assert.True(stableSnapshots > 0);
        }

        [Fact]
        public void BoundedPercentileSamplerRecordingIsAllocationFreeAfterWarmup()
        {
            var sampler = new BoundedPercentileSampler(64);
            for (int value = 0; value < 64; value++) sampler.Record(value);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int value = 0; value < 100_000; value++) sampler.Record(value);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(before, after);
        }

        [Fact]
        public void RttStatisticsIgnoreInvalidSamplesAndUseExplicitFirstSample()
        {
            var metrics = new NetMetrics();
            Assert.Null(metrics.SilenceMs(Stopwatch.GetTimestamp()));
            metrics.RecordRtt(0);
            metrics.RecordRtt(80);
            metrics.RecordRtt(Double.NaN);
            metrics.RecordRtt(-1);
            Assert.Equal(2, metrics.Rtt.Count);
            Assert.Equal(40, metrics.Rtt.Mean);
            Assert.Equal(0, metrics.Rtt.Min);
            Assert.Equal(80, metrics.Rtt.Max);
            Assert.Equal(2, metrics.Rtt.Percentiles.Count);
            Assert.Equal(0, metrics.Rtt.Percentiles.P50);
            Assert.Equal(80, metrics.Rtt.Percentiles.Max);
            Assert.Equal(10, metrics.SmoothedRttMs);
            Assert.Equal(5, metrics.JitterMs);
        }

        [Fact]
        public void ArrivalAndWorkTimesUseMonotonicTimestamps()
        {
            var metrics = new NetMetrics();
            var packet = new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 1), new byte[10], 10);
            long later = packet.ReceivedAt + Stopwatch.Frequency;
            metrics.Receive(packet, later);
            Assert.Equal(1000, metrics.QueueAgeMs.Last, 6);
            Assert.Equal(10, metrics.BytesReceived);
            Assert.Equal(1, metrics.PacketsReceived);
            Assert.Equal(500, metrics.SilenceMs(later + Stopwatch.Frequency / 2)!.Value, 5);
            metrics.BeginWork(later);
            metrics.EndWork(later + Stopwatch.Frequency / 4);
            metrics.EndWork(later + Stopwatch.Frequency);
            Assert.Equal(1, metrics.WorkDurationMs.Count);
            Assert.Equal(250, metrics.WorkDurationMs.Last, 5);
        }

        [Fact]
        public void ReceiveIntervalsUsePacketArrivalTimesWhenPollBatchesPackets()
        {
            var metrics = new NetMetrics();
            long first = Stopwatch.Frequency;
            var firstPacket = new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 1), new byte[10], 10, first);
            var secondPacket = new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 1), new byte[10], 10,
                first + Stopwatch.Frequency / 2);
            var thirdPacket = new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 1), new byte[10], 10,
                first + Stopwatch.Frequency);
            metrics.Receive(firstPacket, first + Stopwatch.Frequency * 2);
            metrics.Receive(secondPacket, first + Stopwatch.Frequency * 2);
            metrics.Receive(thirdPacket, first + Stopwatch.Frequency * 2);

            Assert.Equal(3, metrics.PacketsReceived);
            Assert.Equal(2, metrics.PacketReceiveIntervalMs.Count);
            Assert.Equal(500, metrics.PacketReceiveIntervalMs.Last, 5);
            Assert.Equal(500, metrics.PacketReceiveIntervalMs.Percentiles.P50, 5);
        }

        [Fact]
        public void TransportCountersDoNotLoseConcurrentUpdates()
        {
            var metrics = new NetTrafficMetrics();
            Parallel.For(0, 10000, _ =>
            {
                metrics.Sent(50);
                metrics.Received(100);
                metrics.DropQueued();
                metrics.DropSimulated();
                metrics.Reject();
                metrics.SendFailed();
                metrics.ObserveQueueDepth(4);
                metrics.Flushed();
            });
            Assert.Equal(10000, metrics.PacketsSent);
            Assert.Equal(10000, metrics.PacketsReceived);
            Assert.Equal(500000, metrics.BytesSent);
            Assert.Equal(1000000, metrics.BytesReceived);
            Assert.Equal(10000, metrics.QueueDrops);
            Assert.Equal(10000, metrics.SimulatedDrops);
            Assert.Equal(10000, metrics.PacketsRejected);
            Assert.Equal(10000, metrics.SendErrors);
            Assert.Equal(4, metrics.QueueHighWater);
            Assert.Equal(10000, metrics.Flushes);
        }
    }
}
