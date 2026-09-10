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
    public bool AdaptiveInputPlayoutEnabled { get; init; }
    public bool TransportQueueV2Enabled { get; init; }
    public bool ReliableAdaptiveRtoEnabled { get; init; }
    public WorkerLagCompensationMode LagCompensationMode { get; init; } = WorkerLagCompensationMode.Players;
    internal MphRead.DeveloperValidationFixtureId ValidationFixture { get; init; }
    public string? ReplayDirectory { get; init; }
    public string? ArtifactDirectory { get; init; }
    public int MaximumArtifactFiles { get; init; } = 4096;
    public long MaximumArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long ArtifactReservationBytes { get; init; } = 64L * 1024 * 1024;
    public void Validate()
    {
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
            || ArtifactDirectory != null && !Path.IsPathFullyQualified(ArtifactDirectory)
            || ReplayDirectory != null && !Path.IsPathFullyQualified(ReplayDirectory)
            || MinimumMemoryHeadroomBytes < 0 || string.IsNullOrWhiteSpace(AdvertisedHost)
            || AdvertisedHost.Length > 253 || AdvertisedHost.Any(char.IsWhiteSpace) || Uri.CheckHostName(AdvertisedHost) == UriHostNameType.Unknown
            || !SnapshotCadence.IsSupported(SnapshotRateHz)
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
