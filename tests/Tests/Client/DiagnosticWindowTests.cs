using System;
using System.Diagnostics;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class DiagnosticWindowTests
    {
        private static readonly NetDiagnosticCounters Initial = new(1, 1,
            NetConnectionState.Playing, true, 100, 10, 1000, 2000);

        [Fact]
        public void RatesUseMeasuredWallTimeAndReportSequenceGapsSeparately()
        {
            var window = new NetDiagnosticWindow();
            Assert.False(window.TrySample(0, Initial, out _));
            Assert.False(window.TrySample(Stopwatch.Frequency / 2, Initial, out _));
            var next = Initial with { SnapshotSequence = 200, Snapshots = 90, BytesReceived = 5096, BytesSent = 10192 };
            Assert.True(window.TrySample(Stopwatch.Frequency * 2, next, out NetDiagnosticRates rates));
            Assert.Equal(40, rates.SnapshotHz);
            Assert.Equal(2048, rates.BytesReceivedPerSecond);
            Assert.Equal(4096, rates.BytesSentPerSecond);
            Assert.Equal(20, rates.MissingOrStaleSnapshots);
            Assert.Equal(100u, rates.SnapshotSequenceSpan);
            Assert.Equal(20, rates.MissingOrStalePercent);
        }

        [Fact]
        public void SnapshotSequenceWrapDoesNotInventBillionsOfMissingPackets()
        {
            var window = new NetDiagnosticWindow();
            var first = Initial with { SnapshotSequence = UInt32.MaxValue - 29 };
            Assert.False(window.TrySample(0, first, out _));
            var next = first with { SnapshotSequence = 30, Snapshots = 68 };
            Assert.True(window.TrySample(Stopwatch.Frequency, next, out NetDiagnosticRates rates));
            Assert.Equal(60u, rates.SnapshotSequenceSpan);
            Assert.Equal(2, rates.MissingOrStaleSnapshots);
            Assert.Equal(58, rates.SnapshotHz);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ConnectionMatchLoadingAndCounterResetsStartANewWindow(int change)
        {
            var window = new NetDiagnosticWindow();
            window.TrySample(0, Initial, out _);
            NetDiagnosticCounters next = change switch
            {
                0 => Initial with { ConnectionId = 2 },
                1 => Initial with { MatchId = 2, SnapshotSequence = 0 },
                2 => Initial with { State = NetConnectionState.Loading, HasSnapshot = false },
                _ => Initial with { BytesReceived = 0, BytesSent = 0 }
            };
            Assert.False(window.TrySample(Stopwatch.Frequency * 10, next, out _));
            Assert.True(window.TrySample(Stopwatch.Frequency * 11, next, out NetDiagnosticRates rates));
            Assert.Equal(0, rates.SnapshotHz);
            Assert.Null(rates.MissingOrStalePercent);
            Assert.Equal(0, rates.BytesReceivedPerSecond);
        }

        [Fact]
        public void UnknownOrInconsistentSequencesDoNotBecomeZeroPercentLossClaims()
        {
            var window = new NetDiagnosticWindow();
            window.TrySample(0, Initial, out _);
            Assert.True(window.TrySample(Stopwatch.Frequency, Initial, out NetDiagnosticRates silent));
            Assert.Null(silent.MissingOrStalePercent);
            var inconsistent = Initial with { Snapshots = 30, SnapshotSequence = 101 };
            Assert.True(window.TrySample(Stopwatch.Frequency * 2, inconsistent, out NetDiagnosticRates invalid));
            Assert.Equal(20, invalid.SnapshotHz);
            Assert.Null(invalid.MissingOrStalePercent);
        }

        [Fact]
        public void EstimatedAgeHandlesClockEpochWrapAndRetainsClockUncertainty()
        {
            Assert.Equal(100, NetDiagnosticWindow.EstimatedSnapshotAgeMs(4294967298.0, UInt32.MaxValue - 3)!.Value, 6);
            Assert.Equal(100, NetDiagnosticWindow.EstimatedSnapshotAgeMs(2, UInt32.MaxValue - 3)!.Value, 6);
            Assert.Equal(-100, NetDiagnosticWindow.EstimatedSnapshotAgeMs(94, 100)!.Value, 6);
            Assert.Null(NetDiagnosticWindow.EstimatedSnapshotAgeMs(Double.NaN, 100));
        }

        [Fact]
        public void PerFrameWindowChecksDoNotAllocate()
        {
            var window = new NetDiagnosticWindow();
            window.TrySample(0, Initial, out _);
            for (int i = 0; i < 100; i++) window.TrySample(1, Initial, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) window.TrySample(1, Initial, out _);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
