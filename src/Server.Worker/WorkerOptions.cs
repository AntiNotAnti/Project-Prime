using ProjectPrime.Server.Shared;
using System.Net;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Worker;

public enum WorkerLagCompensationMode
{
    Off,
    Players,
    Dynamic
}

public sealed record WorkerOptions
{
    public WorkerId WorkerId { get; init; } = new(Guid.NewGuid());
    public Guid Incarnation { get; init; } = Guid.NewGuid();
    public int SimulationLanes { get; init; } = 1;
    public int MaxMatches { get; init; } = 1;
    public int MaxMatchesPerLane { get; init; } = 1;
    public int CommandCapacity { get; init; } = 128;
    public int CompletedHistoryCapacity { get; init; } = 4096;
    public TimeSpan AdmissionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CreationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public double PlacementP99Milliseconds { get; init; } = 12;
    public double PlacementCpuPercent { get; init; } = 95;
    public long MinimumMemoryHeadroomBytes { get; init; } = 64L * 1024 * 1024;
    public string AdvertisedHost { get; init; } = "127.0.0.1";
    public static string ActualBuildVersion { get; } = BuildIdentity.Display;
    public string BuildVersion { get; init; } = ActualBuildVersion;
    public byte ProtocolVersion { get; init; } = MphRead.Mods.Network.NetHeader.Version;
    public int SnapshotRateHz { get; init; } = SnapshotCadence.DefaultRateHz;
    public bool AdaptiveTimingEnabled { get; init; }
    /// <summary>Protocol-13 frame-based timing telemetry; disabled by default for rollback.</summary>
    public bool AdaptiveTimingV2Enabled { get; init; }
    public bool AdaptiveInputPlayoutEnabled { get; init; }
    public bool TransportQueueV2Enabled { get; init; }
    // Enabled by default for the hardened path; false restores the pre-N4
    // admission behavior while retaining validation of the configured reserve.
    public bool TransportCriticalReserveEnabled { get; init; } = true;
    public int CriticalTransportReserve { get; init; } = 32;
    // Enabled by default; false restores independent per-match flush budgets.
    public bool WorkerGlobalNetworkBudgetEnabled { get; init; } = true;
    public int MaximumDatagramsPerPump { get; init; } = WorkerNetworkHub.DefaultMaximumDatagramsPerPump;
    public bool ReliableAdaptiveRtoEnabled { get; init; }
    /// <summary>Experimental one-tick ACK coalescing; remains opt-in.</summary>
    public bool AckCoalescingEnabled { get; init; }
    /// <summary>Per-handoff UDP MACs are the production default.</summary>
    public bool UdpAuthenticationEnabled { get; init; } = true;
    public ObserverOptions Observers { get; init; } = new();
    public WorkerLagCompensationMode LagCompensationMode { get; init; } = WorkerLagCompensationMode.Players;
    internal MphRead.DeveloperValidationFixtureId ValidationFixture { get; init; }
    /// <summary>Explicit developer-only choreography for rendered headshot validation.</summary>
    internal bool HeadshotValidationScenario { get; init; }
    internal int HeadshotScenarioSeconds { get; init; } = 15;
    public string? ReplayDirectory { get; init; }
    public string? ArtifactDirectory { get; init; }
    /// <summary>Developer/soak-only delay applied before terminal artifact work.</summary>
    public int ArtifactDelayMilliseconds { get; init; }
    /// <summary>Developer/soak-only deterministic probability of failing the
    /// required report artifact while retaining the terminal lifecycle.</summary>
    public double ArtifactFailureRate { get; init; }
    public int ArtifactFailureSeed { get; init; }
    public int MaximumArtifactFiles { get; init; } = 4096;
    public long MaximumArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long ArtifactReservationBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>Bounded independent artifact consumers. Artifact work never
    /// runs on a simulation lane and defaults to two concurrent matches.</summary>
    public int ArtifactConcurrency { get; init; } = 2;
    /// <summary>Maximum terminal jobs waiting or executing in the artifact
    /// pipeline. This is separate from <see cref="MaxMatches"/>.</summary>
    public int ArtifactQueueCapacity { get; init; } = 64;
    /// <summary>Maximum required-artifact reservations held by admitted
    /// matches before their terminal job is created.</summary>
    public int ArtifactReservationCapacity { get; init; } = 64;
    /// <summary>Operational bounds for independent artifact operations. A
    /// report/replay writer may outlive its deadline when it cannot be
    /// cancelled; the owning pipeline retains the job until it actually
    /// finishes.</summary>
    public TimeSpan ArtifactReportTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ArtifactReplayTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ArtifactTelemetryTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ArtifactJobTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ArtifactShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal WorkerArtifactOperations? ArtifactOperations { get; init; }
    public void Validate()
    {
        Observers.Validate();
        if (WorkerId.Value == Guid.Empty || Incarnation == Guid.Empty || SimulationLanes is < 1 or > 64
            || MaxMatches is < 1 or > 1024 || MaxMatchesPerLane is < 1 or > 1024
            || MaxMatches > SimulationLanes * MaxMatchesPerLane || CommandCapacity is < 1 or > 65536
            || CompletedHistoryCapacity < MaxMatches || CompletedHistoryCapacity > 100000
            || AdmissionTimeout < TimeSpan.FromSeconds(1) || AdmissionTimeout > TimeSpan.FromMinutes(5)
            || CreationTimeout < TimeSpan.FromMilliseconds(1) || CreationTimeout > TimeSpan.FromMinutes(5)
            || !double.IsFinite(PlacementP99Milliseconds) || PlacementP99Milliseconds <= 0
            || !double.IsFinite(PlacementCpuPercent) || PlacementCpuPercent is <= 0 or > 100
            || MaximumArtifactFiles is < 1 or > 100000 || MaximumArtifactBytes < 1
            || ArtifactReservationBytes < 1 || ArtifactReservationBytes > MaximumArtifactBytes
            || ArtifactConcurrency is < 1 or > 8
            || ArtifactQueueCapacity is < 1 or > 4096
            || ArtifactReservationCapacity is < 1 or > 4096
            || ArtifactDelayMilliseconds is < 0 or > 120000
            || !double.IsFinite(ArtifactFailureRate) || ArtifactFailureRate is < 0 or > 1
            // A reservation is one terminal job and must always have a
            // bounded channel slot available. Consumers are not part of this
            // admission guarantee: a simultaneous terminal burst can fill the
            // channel before any consumer dequeues a job.
            || ArtifactReservationCapacity > ArtifactQueueCapacity
            || ArtifactReportTimeout < TimeSpan.FromMilliseconds(1) || ArtifactReportTimeout > TimeSpan.FromMinutes(10)
            || ArtifactReplayTimeout < TimeSpan.FromMilliseconds(1) || ArtifactReplayTimeout > TimeSpan.FromMinutes(10)
            || ArtifactTelemetryTimeout < TimeSpan.FromMilliseconds(1) || ArtifactTelemetryTimeout > TimeSpan.FromMinutes(10)
            || ArtifactJobTimeout < TimeSpan.FromMilliseconds(1) || ArtifactJobTimeout > TimeSpan.FromMinutes(15)
            || ArtifactShutdownTimeout < TimeSpan.FromMilliseconds(1) || ArtifactShutdownTimeout > TimeSpan.FromMinutes(10)
            || CriticalTransportReserve is < 0 or > 65536
            || MaximumDatagramsPerPump is < 1 or > 65536
            || ArtifactDirectory != null && !Path.IsPathFullyQualified(ArtifactDirectory)
            || ReplayDirectory != null && !Path.IsPathFullyQualified(ReplayDirectory)
            || MinimumMemoryHeadroomBytes < 0 || string.IsNullOrWhiteSpace(AdvertisedHost)
            || AdvertisedHost.Length > 253 || AdvertisedHost.Any(char.IsWhiteSpace) || Uri.CheckHostName(AdvertisedHost) == UriHostNameType.Unknown
            || !SnapshotCadence.IsSupported(SnapshotRateHz)
            || AdaptiveTimingV2Enabled && !AdaptiveTimingEnabled
            || AdaptiveInputPlayoutEnabled && !AdaptiveTimingEnabled
            || !Enum.IsDefined(LagCompensationMode)
            || !Enum.IsDefined(ValidationFixture)
            || string.IsNullOrWhiteSpace(BuildVersion) || BuildVersion.Length > 128 || BuildVersion != ActualBuildVersion || ProtocolVersion != MphRead.Mods.Network.NetHeader.Version)
            throw new ArgumentException("Invalid Worker configuration.");
        if (ValidationFixture != MphRead.DeveloperValidationFixtureId.None
            && (SimulationLanes != 1 || MaxMatches != 1 || MaxMatchesPerLane != 1
                || !IPAddress.TryParse(AdvertisedHost, out IPAddress? host)
                || !IPAddress.IsLoopback(host)))
            throw new ArgumentException("Developer validation fixture requires an isolated single-match loopback Worker.");
        if (HeadshotValidationScenario
            && (ValidationFixture != MphRead.DeveloperValidationFixtureId.Unit1Rm1Dynamic
                || HeadshotScenarioSeconds is < 12 or > 60))
            throw new ArgumentException("Headshot validation requires the isolated Unit1 RM1 developer fixture and a 12-60 second duration.");
    }

    internal (bool LagCompEnabled, bool ProjectileCatchUpEnabled, bool HistoricalDynamicCollisionEnabled)
        ResolveLagCompensation() => LagCompensationMode switch
        {
            WorkerLagCompensationMode.Off => (false, false, false),
            WorkerLagCompensationMode.Players => (true, true, false),
            WorkerLagCompensationMode.Dynamic => (true, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(LagCompensationMode))
        };

    public static WorkerLagCompensationMode ParseLagCompensationMode(string value) => value switch
    {
        "off" => WorkerLagCompensationMode.Off,
        "players" => WorkerLagCompensationMode.Players,
        "dynamic" => WorkerLagCompensationMode.Dynamic,
        _ => throw new ArgumentException("Lag compensation mode must be off, players, or dynamic.")
    };

    internal static MphRead.DeveloperValidationFixtureId ParseValidationFixture(string value)
        => MphRead.DeveloperValidationFixtures.Parse(value);
}
