using ProjectPrime.Server.Shared;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Node.Workers;

public sealed record WorkerLaunchOptions
{
    public required string FileName { get; init; }
    public WorkerContentIdentity? Content { get; init; }
    public string? ArtifactDirectory { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    public WorkerCapacity Capacity { get; init; } = new(8, 64, 0, 0);
    public int SnapshotRateHz { get; init; } = SnapshotCadence.DefaultRateHz;
    public bool AdaptiveTimingEnabled { get; init; }
    /// <summary>
    /// Enables the protocol-13 frame-denominator telemetry path. This is kept
    /// separate from the older RTT/input adaptive policy so it can be rolled
    /// back without silently accepting incompatible telemetry.
    /// </summary>
    public bool AdaptiveTimingV2Enabled { get; init; }
    public bool AdaptiveInputPlayoutEnabled { get; init; }
    public bool TransportQueueV2Enabled { get; init; }
    public bool TransportCriticalReserveEnabled { get; init; } = true;
    public int CriticalTransportReserve { get; init; } = 32;
    public bool WorkerGlobalNetworkBudgetEnabled { get; init; } = true;
    public int MaximumDatagramsPerPump { get; init; } = NetConfig.DefaultMaximumDatagramsPerPump;
    public bool ReliableAdaptiveRtoEnabled { get; init; }
    /// <summary>Experimental one-tick ACK coalescing; remains opt-in.</summary>
    public bool AckCoalescingEnabled { get; init; }
    /// <summary>Production Worker UDP admission authentication; false only for explicit test/legacy seams.</summary>
    public bool UdpAuthenticationEnabled { get; init; } = true;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int CommandCapacity { get; init; } = 64;
    public int EventCapacity { get; init; } = 256;

    internal string AdvertisedHost => Argument("--host") ?? "127.0.0.1";
    internal ushort RequestedPort => ushort.Parse(Argument("--port") ?? "0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Resolve a package-relative Worker executable from the Node application
    /// directory. Bare names remain PATH commands (for example <c>dotnet</c>),
    /// and absolute paths remain untouched. Arguments are deliberately not
    /// passed through this helper: they may contain paths, URLs, or opaque
    /// values whose spelling is part of their contract.
    /// </summary>
    public static string ResolveExecutableFileName(string fileName, string? applicationBaseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.IsPathRooted(fileName) || !LooksLikeRelativePath(fileName)) return fileName;

        string root = Path.GetFullPath(applicationBaseDirectory ?? AppContext.BaseDirectory);
        string resolved = Path.GetFullPath(Path.Combine(root, fileName));
        string relative = Path.GetRelativePath(root, resolved);
        if (relative is ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Relative Worker executable must remain under the Node application directory.", nameof(fileName));
        return resolved;
    }

    private static bool LooksLikeRelativePath(string value)
        => value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal)
            || value.StartsWith(".", StringComparison.Ordinal);

    private int? NumberArgument(string name)
    {
        string? value = Argument(name);
        if (value == null) return null;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number))
            throw new ArgumentException($"Worker option {name} must be an integer.");
        return number;
    }

    private string? Argument(string name)
    {
        var indexes = Arguments.Select((value, index) => (value, index)).Where(p => p.value == name).ToArray();
        if (indexes.Length > 1 || indexes.Length == 1 && indexes[0].index + 1 >= Arguments.Count)
            throw new ArgumentException("Duplicate or missing worker endpoint argument.");
        return indexes.Length == 0 ? null : Arguments[indexes[0].index + 1];
    }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FileName);
        Capacity.Validate();
        if (Uri.CheckHostName(AdvertisedHost) == UriHostNameType.Unknown) throw new ArgumentException("Invalid advertised Worker host.");
        _ = RequestedPort;
        Content?.Validate();
        if (ArtifactDirectory is { } artifacts && !Path.IsPathFullyQualified(artifacts))
            throw new ArgumentException("Worker artifact root must be absolute.");
        if (Capacity.ActiveMatches != 0 || Capacity.ActivePlayers != 0) throw new ArgumentException("New workers must start empty.");
        if (!SnapshotCadence.IsSupported(SnapshotRateHz)) throw new ArgumentException("Snapshot rate must be 30 or 60 Hz.");
        if (AdaptiveTimingV2Enabled && !AdaptiveTimingEnabled)
            throw new ArgumentException("Adaptive timing V2 requires adaptive timing.");
        if (AdaptiveInputPlayoutEnabled && !AdaptiveTimingEnabled)
            throw new ArgumentException("Adaptive input playout requires adaptive timing.");
        if (CriticalTransportReserve is < 0 or > 65536
            || MaximumDatagramsPerPump is < 1 or > 65536)
            throw new ArgumentException("Worker transport budgets must be bounded.");
        int? lanes = NumberArgument("--lanes");
        int? maxMatches = NumberArgument("--max-matches");
        int? maxMatchesPerLane = NumberArgument("--max-matches-per-lane");
        if (lanes is <= 0 || lanes > 64 || maxMatches is <= 0 || maxMatches > 1024
            || maxMatchesPerLane is <= 0 || maxMatchesPerLane > 1024)
            throw new ArgumentException("Worker capacity arguments must be positive and bounded.");
        if (maxMatches is { } configuredMatches && configuredMatches != Capacity.MatchLimit)
            throw new ArgumentException($"Node capacity MatchLimit {Capacity.MatchLimit} does not match Worker --max-matches {configuredMatches}.");
        if (maxMatchesPerLane is { } configuredPerLane && lanes is { } configuredLanes
            && maxMatches is { } configuredTotal && configuredTotal > configuredLanes * configuredPerLane)
            throw new ArgumentException("Worker --max-matches exceeds --lanes * --max-matches-per-lane.");
        if (maxMatches is { } configuredCapacity && Capacity.PlayerLimit > configuredCapacity * 32)
            throw new ArgumentException($"Node capacity PlayerLimit {Capacity.PlayerLimit} exceeds the Worker hard limit for {configuredCapacity} matches.");
        if (StartupTimeout > TimeSpan.FromHours(1) || HeartbeatTimeout > TimeSpan.FromHours(1) || ShutdownTimeout > TimeSpan.FromHours(1)
            || StartupTimeout <= TimeSpan.Zero || HeartbeatTimeout <= TimeSpan.Zero || ShutdownTimeout <= TimeSpan.Zero
            || CommandCapacity is < 1 or > 4096 || EventCapacity is < 1 or > 65536)
            throw new ArgumentException("Invalid worker lifecycle limits.");
        if (Arguments.Any(a => a is null || a.StartsWith("--node-", StringComparison.Ordinal)
            || a.StartsWith("--worker-", StringComparison.Ordinal) || a.StartsWith("--artifact-dir", StringComparison.Ordinal)))
            throw new ArgumentException("Worker identity arguments are owned by the manager.");
        if (Arguments.Contains("--snapshot-rate-hz", StringComparer.Ordinal)
            || Arguments.Contains("--adaptive-timing", StringComparer.Ordinal)
            || Arguments.Contains("--adaptive-timing-v2", StringComparer.Ordinal)
            || Arguments.Contains("--adaptive-input-playout", StringComparer.Ordinal)
            || Arguments.Contains("--transport-queue-v2", StringComparer.Ordinal)
            || Arguments.Contains("--transport-critical-reserve-enabled", StringComparer.Ordinal)
            || Arguments.Contains("--critical-transport-reserve", StringComparer.Ordinal)
            || Arguments.Contains("--worker-global-network-budget-enabled", StringComparer.Ordinal)
            || Arguments.Contains("--max-datagrams-per-pump", StringComparer.Ordinal)
            || Arguments.Contains("--reliable-adaptive-rto", StringComparer.Ordinal)
            || Arguments.Contains("--ack-coalescing", StringComparer.Ordinal)
            || Arguments.Contains("--udp-authentication", StringComparer.Ordinal))
            throw new ArgumentException("Node-owned networking options must use WorkerLaunchOptions properties.");
    }
}

public sealed record WorkerSnapshot(WorkerId WorkerId, Guid Incarnation, WorkerStatus Status,
    WorkerCapacity Capacity, WorkerHealth? Health, IReadOnlyDictionary<MatchId, MatchStatus> Matches,
    string? FailureReason, int? ProcessId);
