namespace ProjectPrime.Server.Shared;

/// <summary>
/// One control/gameplay reconnect contract. The Node session and the Worker
/// roster reservation must expire at the same policy boundary.
/// </summary>
public static class ReconnectPolicy
{
    public static readonly TimeSpan SessionGrace = TimeSpan.FromSeconds(45);
    public const int SimulationTicksPerSecond = 60;
    public static readonly uint WorkerReservationTicks = checked(
        (uint)(45 * SimulationTicksPerSecond));
}
