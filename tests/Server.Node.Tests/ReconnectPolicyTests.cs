using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class ReconnectPolicyTests
{
    [Fact]
    public void NodeAndWorkerGraceShareOnePolicy()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), ReconnectPolicy.SessionGrace);
        Assert.Equal(ReconnectPolicy.SessionGrace, NodeSessionManager.DisconnectGrace);
        Assert.Equal((uint)(ReconnectPolicy.SessionGrace.TotalSeconds
            * ReconnectPolicy.SimulationTicksPerSecond), ReconnectPolicy.WorkerReservationTicks);
    }
}
