using MphRead.Hud.Network;
using Xunit;
namespace MphRead.Tests.Client;
public class NetworkHealthTests
{
    [Theory]
    [InlineData(20, 2, 10, 10, 0, NetworkHealthState.Good)]
    [InlineData(150, 2, 10, 10, 0, NetworkHealthState.HighLatency)]
    [InlineData(20, 30, 10, 10, 0, NetworkHealthState.Unstable)]
    [InlineData(20, 2, 10, 250, 0, NetworkHealthState.Unstable)]
    [InlineData(20, 2, 10, 10, 100, NetworkHealthState.Unstable)]
    [InlineData(150, 30, 1000, 10, 100, NetworkHealthState.Interrupted)]
    public void ExistingMetricsDetermineDisplayOnly(double rtt, double jitter, double silence, double gap, double queue, NetworkHealthState expected)
        => Assert.Equal(expected, NetworkHealthReading.Classify(rtt, jitter, silence, gap, queue));
}
