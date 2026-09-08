using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class MetricsTests
    {
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
            });
            Assert.Equal(10000, metrics.PacketsSent);
            Assert.Equal(10000, metrics.PacketsReceived);
            Assert.Equal(500000, metrics.BytesSent);
            Assert.Equal(1000000, metrics.BytesReceived);
            Assert.Equal(10000, metrics.QueueDrops);
            Assert.Equal(10000, metrics.SimulatedDrops);
            Assert.Equal(10000, metrics.PacketsRejected);
            Assert.Equal(10000, metrics.SendErrors);
        }
    }
}
