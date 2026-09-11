namespace ProjectPrime.Server.Shared;

/// <summary>
/// A read-only projection of the Node's infrastructure readiness. Capacity is
/// deliberately absent: a healthy Worker pool may be temporarily full while
/// the Node remains able to serve control traffic and report its state.
/// </summary>
public sealed record NodeReadinessResult(bool IsReady, bool MapsValid,
    bool WorkerSubsystemUsable, string Code)
{
    public static NodeReadinessResult Ready { get; } = new(true, true, true, "ready");
}
