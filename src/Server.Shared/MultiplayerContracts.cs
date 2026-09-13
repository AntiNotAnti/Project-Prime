namespace ProjectPrime.Server.Shared;

/// <summary>
/// Monotonic identity for one admission/handoff attempt.  Zero is reserved for
/// an omitted value in source-compatible legacy DTO constructors; any
/// production lifecycle message must carry a non-zero value.
/// </summary>
public readonly record struct HandoffGeneration(ulong Value)
{
    public static HandoffGeneration Initial => new(1);

    public void Validate()
    {
        if (Value == 0) throw new ArgumentException("Handoff generation is required.");
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Monotonic epoch for the authoritative lifecycle of a match/lobby.  It is
/// deliberately distinct from a handoff generation: reconnects may advance a
/// handoff without creating a new simulation lifecycle epoch.
/// </summary>
public readonly record struct MatchLifecycleEpoch(ulong Value)
{
    public static MatchLifecycleEpoch Initial => new(1);

    public void Validate()
    {
        if (Value == 0) throw new ArgumentException("Match lifecycle epoch is required.");
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Finite Node-owned lifecycle states for one frozen match epoch.</summary>
public enum MatchLifecycleState
{
    PreparingWorker,
    InstallingAdmissions,
    ReadyToCommit,
    Running,
    Retiring,
    PreparingContinuation,
    PostMatch,
    Failed,
    Retired
}

/// <summary>Canonical bounds shared by Node, Worker and client contracts.</summary>
public static class MultiplayerLimits
{
    public const int MaxPlayers = 8;
    public const int MaximumPlayers = MaxPlayers;
    public const int MaxObservers = 16;
    public const int MaximumObservers = MaxObservers;
    public const int MaxHumanConnections = 24;
    public const int MaximumHumanConnections = MaxHumanConnections;
    public const int MaxWorkerMatchConnections = MaxHumanConnections;
    public const int MaximumWorkerMatchConnections = MaxWorkerMatchConnections;

    public const int MaxAdmissionLeasesPerMatch = 64;
    public const int MaximumAdmissionLeasesPerMatch = MaxAdmissionLeasesPerMatch;
    public const int MaxHandoffQueue = 64;
    public const int MaximumHandoffQueue = MaxHandoffQueue;
    public const int MaxLifecycleQueue = 32;
    public const int MaximumLifecycleQueue = MaxLifecycleQueue;
}

/// <summary>
/// Finite, stable control-plane failure categories.  These values are safe to
/// expose in diagnostics and are intentionally independent of exception text.
/// </summary>
public enum MatchControlFailure
{
    Unknown = 0,
    WorkerUnavailable = 1,
    WorkerBusy = 2,
    PlacementFailed = 3,
    AdmissionUnavailable = 4,
    AdmissionTimeout = 5,
    MatchUnavailable = 6,
    TransitionTimeout = 7,
    SessionExpired = 8,
    ServerDraining = 9,
    // More specific protocol-facing categories remain finite and stable for
    // callers that need to distinguish a rejected request from availability.
    NotFound = 10,
    NotActive = 11,
    InvalidRequest = 12,
    Capacity = 13,
    StaleGeneration = 14,
    AdmissionRejected = 15,
    CancelRejected = 16,
    Timeout = 17,
    Conflict = 18
}

public static class MatchControlFailureCodes
{
    public static string Code(this MatchControlFailure failure) => failure switch
    {
        MatchControlFailure.WorkerUnavailable => "worker_unavailable",
        MatchControlFailure.WorkerBusy => "worker_busy",
        MatchControlFailure.PlacementFailed => "placement_failed",
        MatchControlFailure.AdmissionUnavailable => "admission_unavailable",
        MatchControlFailure.AdmissionTimeout => "admission_timeout",
        MatchControlFailure.MatchUnavailable => "match_unavailable",
        MatchControlFailure.TransitionTimeout => "transition_timeout",
        MatchControlFailure.SessionExpired => "session_expired",
        MatchControlFailure.ServerDraining => "server_draining",
        MatchControlFailure.NotFound => "match_not_found",
        MatchControlFailure.NotActive => "match_not_active",
        MatchControlFailure.InvalidRequest => "invalid_request",
        MatchControlFailure.Capacity => "capacity",
        MatchControlFailure.StaleGeneration => "stale_generation",
        MatchControlFailure.AdmissionRejected => "admission_rejected",
        MatchControlFailure.CancelRejected => "cancel_rejected",
        MatchControlFailure.Timeout => "timeout",
        MatchControlFailure.Conflict => "conflict",
        _ => "unknown"
    };

    public static MatchControlFailure Parse(string? code) => code switch
    {
        "worker_unavailable" => MatchControlFailure.WorkerUnavailable,
        "worker_busy" => MatchControlFailure.WorkerBusy,
        "placement_failed" => MatchControlFailure.PlacementFailed,
        "admission_unavailable" => MatchControlFailure.AdmissionUnavailable,
        "admission_timeout" => MatchControlFailure.AdmissionTimeout,
        "match_unavailable" => MatchControlFailure.MatchUnavailable,
        "transition_timeout" => MatchControlFailure.TransitionTimeout,
        "session_expired" => MatchControlFailure.SessionExpired,
        "server_draining" => MatchControlFailure.ServerDraining,
        "match_not_found" => MatchControlFailure.NotFound,
        "match_not_active" => MatchControlFailure.NotActive,
        "invalid_request" => MatchControlFailure.InvalidRequest,
        "capacity" => MatchControlFailure.Capacity,
        "stale_generation" => MatchControlFailure.StaleGeneration,
        "admission_rejected" => MatchControlFailure.AdmissionRejected,
        "cancel_rejected" => MatchControlFailure.CancelRejected,
        "timeout" => MatchControlFailure.Timeout,
        "conflict" => MatchControlFailure.Conflict,
        _ => MatchControlFailure.Unknown
    };
}

/// <summary>Boundary exception carrying a finite machine-readable failure.</summary>
public class MatchControlException : Exception
{
    public MatchControlFailure Failure { get; }
    public string Code => Failure.Code();

    public MatchControlException(MatchControlFailure failure, string? message = null, Exception? inner = null)
        : base(message ?? failure.Code(), inner) => Failure = failure;
}
