namespace MphRead.Mods.MapGen;

/// <summary>
/// Optional service preflight for typed read-only v3 metadata clients.
/// Published map transfer routes remain v2/legacy. Generic asset publishing
/// is disabled until the separate type-specific PR-09 validation pipeline.
/// </summary>
public sealed record MapCommunityServiceCapabilities
{
    public int ApiVersion { get; init; } = 3;
    public int LegacyApiVersion { get; init; } = 2;
    public string[] SupportedKinds { get; init; } = CommunityContentKinds.Supported;
    public bool SupportsSequentialResumableUploads { get; init; } = true;
    public bool SupportsRangeDownloads { get; init; } = true;
    public bool SupportsParallelUploadRanges { get; init; }
    public bool SupportsGenericContentCatalog { get; init; } = true;
    public bool SupportsGenericContentPublishing { get; init; } = false;
    public long MaxArchiveBytes { get; init; } = MapPackageReader.MaxArchiveBytes;
    public int MaxUploadChunkBytes { get; init; } = MapCommunityClient.UploadChunkBytes;
    public int RecommendedUploadChunkBytes { get; init; } = 4 * 1024 * 1024;
    public long MaxExpandedPackageBytes { get; init; } = MapPackageReader.MaxExpandedBytes;
    public long MaxEntryBytes { get; init; } = MapPackageReader.MaxEntryBytes;
    public int MaxPackageEntries { get; init; } = MapPackageReader.MaxEntries;
    public long PublishedStorageLimitBytes { get; init; }
    public long MaxPartialReservationBytes { get; init; } = MapCommunityUploadReservations.MaxTotalBytes;
    public int MaxPartialUploadSessions { get; init; } = MapCommunityUploadReservations.MaxTotalSessions;
    public int MaxConcurrentMetadataRequests { get; init; } =
        MapCommunityServiceAdmission.MetadataConcurrency;
    public int MaxConcurrentTransfers { get; init; } =
        MapCommunityServiceAdmission.UploadConcurrency
        + MapCommunityServiceAdmission.DownloadConcurrency;
    public int MaxConcurrentUploads { get; init; } =
        MapCommunityServiceAdmission.UploadConcurrency;
    public int MaxConcurrentDownloads { get; init; } =
        MapCommunityServiceAdmission.DownloadConcurrency;
    public int MaxConcurrentVerificationJobs { get; init; } =
        MapCommunityServiceAdmission.VerificationConcurrency;
    public int MaximumPreviewBytes { get; init; } = 1024 * 1024;
    public string[] SupportedReadRoutes { get; init; } =
        new[] { "/maps", "/v2/maps", "/packages/{sha256}",
            "/maps/{sha256}/preview", "/v3/content",
            "/v3/content/{kind}/{contentId}",
            "/v3/content/{kind}/{contentId}/revisions" };
}
