using FruityPrime.Server.Shared;
using System.Reflection;

namespace FruityPrime.Server.Worker;

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
    public static string ActualBuildVersion { get; } = typeof(WorkerOptions).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(WorkerOptions).Assembly.GetName().Version!.ToString();
    public string BuildVersion { get; init; } = ActualBuildVersion;
    public byte ProtocolVersion { get; init; } = MphRead.Mods.Network.NetHeader.Version;
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
            || string.IsNullOrWhiteSpace(BuildVersion) || BuildVersion.Length > 128 || BuildVersion != ActualBuildVersion || ProtocolVersion != MphRead.Mods.Network.NetHeader.Version)
            throw new ArgumentException("Invalid Worker configuration.");
    }
}
