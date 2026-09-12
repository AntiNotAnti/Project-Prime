using System.Collections.Immutable;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class WorkerDiagnosticsTests
{
    [Fact]
    public void PerformanceFieldsRoundTripAndRejectInvalidBounds()
    {
        var lanes = ImmutableArray.Create(new WorkerLaneHealth(0, 1, 4, 0, 0, 1, 2, 3, 4, 4, 1, 7));
        var match = new WorkerMatchHealth(new(Guid.NewGuid()), new(1), 4, "Playing", "Running",
            4, 1, 1, 2, 3, 4, 5, 6, 7, 1, 0, 0) with
        {
            ObserverRetainedFrames = 7,
            ObserverRetainedBytes = 4096,
            ReplayQueueDepth = 2,
            ReplayQueueHighWater = 8,
            ReplayQueueOverflowed = true,
            StatusPublicationCount = 11,
            StatusPublicationCadenceHz = 10
        };
        var diagnostics = new WorkerDiagnostics(lanes, 10, 100, 1, 2, 3, 4, 5, 6, 7, 8, 9,
            ImmutableArray.Create(match), 1, false, 1000, 8, 9, 10, 11,
            12, 13, 14, 15, 16, 17,
            18, 17, 2.5, 14, 15,
            NetworkWakeups: 20, ImmediateRepumps: 3, IdleWaits: 17,
            ReceiveToRouteSampleCount: 4, ReceiveToRouteP50Milliseconds: 1,
            ReceiveToRouteP95Milliseconds: 2, ReceiveToRouteP99Milliseconds: 3,
            ReceiveToRouteP999Milliseconds: 4, ReceiveToRouteMaxMilliseconds: 5,
            OutboundEnqueueToSendSampleCount: 6, OutboundEnqueueToSendP50Milliseconds: 1,
            OutboundEnqueueToSendP95Milliseconds: 2, OutboundEnqueueToSendP99Milliseconds: 3,
            OutboundEnqueueToSendP999Milliseconds: 4, OutboundEnqueueToSendMaxMilliseconds: 5);
        var heartbeat = new WorkerHeartbeat(new(Guid.NewGuid()), Guid.NewGuid(), new(4, 32, 1, 1),
            new(WorkerStatus.Ready, 100, 3, 100, diagnostics));

        var decoded = Assert.IsType<WorkerHeartbeat>(WorkerIpcCodec.Decode(WorkerIpcCodec.Encode(heartbeat)));
        WorkerDiagnostics decodedDiagnostics = decoded.Health.Diagnostics!;
        Assert.Equal(1000, decodedDiagnostics.AllocationBytesPerSecond);
        Assert.Equal(8, decodedDiagnostics.NetworkLoopP99Milliseconds);
        Assert.Equal(9, decodedDiagnostics.NetworkLoopP999Milliseconds);
        Assert.Equal(10, decodedDiagnostics.NetworkQueueHighWater);
        Assert.Equal(11, decodedDiagnostics.NetworkLoopSampleCount);
        Assert.Equal(18, decodedDiagnostics.TimingTelemetryObservedConnections);
        Assert.Equal(17, decodedDiagnostics.TimingTelemetryStaleConnections);
        Assert.Equal(2.5, decodedDiagnostics.TimingTelemetryMaxAgeSeconds);
        Assert.Equal(14, decodedDiagnostics.TimingTelemetryStaleIntervals);
        Assert.Equal(15, decodedDiagnostics.TimingDownshiftBlocked);
        Assert.Equal(20, decodedDiagnostics.NetworkWakeups);
        Assert.Equal(3, decodedDiagnostics.ImmediateRepumps);
        Assert.Equal(17, decodedDiagnostics.IdleWaits);
        Assert.Equal(4, decodedDiagnostics.ReceiveToRouteSampleCount);
        Assert.Equal(6, decodedDiagnostics.OutboundEnqueueToSendSampleCount);
        Assert.Equal(4, decodedDiagnostics.Lanes[0].P999Milliseconds);
        Assert.Equal(7, decodedDiagnostics.Lanes[0].CommandQueueHighWater);
        Assert.Equal(7, decodedDiagnostics.Matches[0].AllocatedBytesPerSecond);
        Assert.Equal(7, decodedDiagnostics.Matches[0].ObserverRetainedFrames);
        Assert.Equal(4096, decodedDiagnostics.Matches[0].ObserverRetainedBytes);
        Assert.Equal(2, decodedDiagnostics.Matches[0].ReplayQueueDepth);
        Assert.Equal(8, decodedDiagnostics.Matches[0].ReplayQueueHighWater);
        Assert.True(decodedDiagnostics.Matches[0].ReplayQueueOverflowed);
        Assert.Equal(11, decodedDiagnostics.Matches[0].StatusPublicationCount);
        Assert.Equal(10, decodedDiagnostics.Matches[0].StatusPublicationCadenceHz);

        var invalid = diagnostics with { NetworkLoopP999Milliseconds = 1 };
        Assert.Throws<ArgumentException>(() => invalid.Validate());
        var invalidLane = diagnostics with
        {
            Lanes = ImmutableArray.Create(new WorkerLaneHealth(0, 1, 4, 0, 0, 1, 2, 3, 4, 5, 1, 7))
        };
        Assert.Throws<ArgumentException>(() => invalidLane.Validate());
        Assert.Throws<ArgumentException>(() => (diagnostics with
        {
            Matches = ImmutableArray.Create(match with { StatusPublicationCadenceHz = double.NaN })
        }).Validate());

        // V2 diagnostics count peers with no report as stale, so stale can
        // legitimately exceed the observed-report count.
        var missingTelemetry = diagnostics with
        {
            TimingTelemetryObservedConnections = 0,
            TimingTelemetryStaleConnections = 1
        };
        missingTelemetry.Validate();
    }

    [Fact]
    public void MaximumDiagnosticSampleFitsBoundedIpcFrame()
    {
        var lanes = Enumerable.Range(0, 64).Select(index => new WorkerLaneHealth(index, 1024, long.MaxValue,
            long.MaxValue, long.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue,
            double.MaxValue, long.MaxValue, long.MaxValue)).ToImmutableArray();
        var matches = Enumerable.Range(0, WorkerDiagnostics.MaximumMatchSamples).Select(index => new WorkerMatchHealth(new(Guid.NewGuid()), new((uint)index + 1),
            uint.MaxValue, new string('<', 32), new string('<', 32), long.MaxValue, long.MaxValue,
            double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue,
            double.MaxValue, double.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue)).ToImmutableArray();
        var diagnostic = new WorkerDiagnostics(lanes, 100, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, matches, 1024, true,
            double.MaxValue, double.MaxValue, double.MaxValue, long.MaxValue, long.MaxValue,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue,
            long.MaxValue, long.MaxValue, double.MaxValue, long.MaxValue, long.MaxValue);
        byte[] frame = WorkerIpcCodec.Encode(new WorkerHeartbeat(new(Guid.NewGuid()), Guid.NewGuid(), new(1024, 8192, 1024, 8192),
            new(WorkerStatus.Ready, long.MaxValue, double.MaxValue, long.MaxValue, diagnostic)));
        Assert.Contains("\\u003C", System.Text.Encoding.UTF8.GetString(frame));
        Assert.True(frame.Length <= WorkerIpcCodec.MaxFrameLength + 4 - 4 * 1024);
        Assert.True(frame.Length <= WorkerIpcCodec.MaxFrameLength + 4);
        var decoded = Assert.IsType<WorkerHeartbeat>(WorkerIpcCodec.Decode(frame));
        Assert.Equal(WorkerDiagnostics.MaximumMatchSamples, decoded.Health.Diagnostics!.Matches.Length);
        Assert.Equal(1024, decoded.Health.Diagnostics.TotalMatches);
        Assert.True(decoded.Health.Diagnostics.MatchesTruncated);
        Assert.Equal(new string('<', 32), decoded.Health.Diagnostics.Matches[0].Phase);
        Assert.Equal(new string('<', 32), decoded.Health.Diagnostics.Matches[0].State);
        Assert.Equal(long.MaxValue, decoded.Health.Diagnostics.TimingTelemetryObservedConnections);
        Assert.Equal(long.MaxValue, decoded.Health.Diagnostics.TimingDownshiftBlocked);
    }

    [Fact]
    public void LegacyDefaultedMatchSampleRemainsDecodable()
    {
        var lanes = Enumerable.Range(0, 64).Select(index => new WorkerLaneHealth(index, 1024, long.MaxValue,
            long.MaxValue, long.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue)).ToImmutableArray();
        var matches = Enumerable.Range(0, 128).Select(index => new WorkerMatchHealth(new(Guid.NewGuid()), new((uint)index + 1),
            uint.MaxValue, new string('p', 32), new string('s', 32))).ToImmutableArray();
        var diagnostic = new WorkerDiagnostics(lanes, 100, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, matches, 1024, true);

        byte[] frame = WorkerIpcCodec.Encode(new WorkerHeartbeat(new(Guid.NewGuid()), Guid.NewGuid(), new(1024, 8192, 1024, 8192),
            new(WorkerStatus.Ready, long.MaxValue, double.MaxValue, long.MaxValue, diagnostic)));
        var decoded = Assert.IsType<WorkerHeartbeat>(WorkerIpcCodec.Decode(frame));
        Assert.Equal(128, decoded.Health.Diagnostics!.Matches.Length);
        Assert.All(decoded.Health.Diagnostics.Matches, match => Assert.Equal(0, match.TickSamples));

        var extendedMatches = matches.Take(33)
            .Select(match => match with { ObserverRetainedFrames = 1 })
            .ToImmutableArray();
        Assert.Throws<ArgumentException>(() => (diagnostic with
        {
            Matches = extendedMatches,
            TotalMatches = extendedMatches.Length,
            MatchesTruncated = false
        }).Validate());
    }
}
