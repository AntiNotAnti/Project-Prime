using System.Collections.Immutable;
using FruityPrime.Server.Shared;
using Xunit;

namespace FruityPrime.Server.Shared.Tests;

public sealed class WorkerDiagnosticsTests
{
    [Fact]
    public void MaximumDiagnosticSampleFitsBoundedIpcFrame()
    {
        var lanes = Enumerable.Range(0, 64).Select(index => new WorkerLaneHealth(index, 1024, long.MaxValue,
            long.MaxValue, long.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue)).ToImmutableArray();
        var matches = Enumerable.Range(0, 128).Select(index => new WorkerMatchHealth(new(Guid.NewGuid()), new((uint)index + 1),
            uint.MaxValue, new string('p', 32), new string('s', 32))).ToImmutableArray();
        var diagnostic = new WorkerDiagnostics(lanes, 100, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
            long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, matches, 1024, true);
        byte[] frame = WorkerIpcCodec.Encode(new WorkerHeartbeat(new(Guid.NewGuid()), Guid.NewGuid(), new(1024, 8192, 1024, 8192),
            new(WorkerStatus.Ready, long.MaxValue, double.MaxValue, long.MaxValue, diagnostic)));
        Assert.True(frame.Length <= WorkerIpcCodec.MaxFrameLength + 4);
        var decoded = Assert.IsType<WorkerHeartbeat>(WorkerIpcCodec.Decode(frame));
        Assert.Equal(128, decoded.Health.Diagnostics!.Matches.Length);
        Assert.Equal(1024, decoded.Health.Diagnostics.TotalMatches);
        Assert.True(decoded.Health.Diagnostics.MatchesTruncated);
    }
}
