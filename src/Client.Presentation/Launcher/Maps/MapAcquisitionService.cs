using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record MapDownloadProgress(long Received, long Total, string Stage);

/// <summary>HTTPS-only exact package acquisition from the connected Node origin.</summary>
internal sealed class MapAcquisitionService : IDisposable
{
    private const int MaximumDownloadAttempts = 3;
    private const int MaximumRetryAfterSeconds = 30;
    private sealed record PartialMetadata(string StableId, string Version, string ContentHash,
        string ArtifactHash, long PackageSize);
    private static readonly Lazy<MapAcquisitionService> Process = new(() =>
        new MapAcquisitionService(null, MapPlatformService.Shared));
    private sealed class DownloadLock
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal int References;
    }

    private readonly struct DownloadLease : IDisposable
    {
        private readonly string _key;
        private readonly DownloadLock _entry;

        internal DownloadLease(string key, DownloadLock entry)
        {
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            _entry.Gate.Release();
            ReleaseDownloadReference(_key, _entry);
        }
    }

    // A service instance is not an ownership boundary: the launcher can have
    // more than one view/service during a transition. Keying the gate by the
    // exact artifact makes concurrent callers share one resumable partial and
    // prevents two writers from interleaving bytes or sidecars.
    private static readonly ConcurrentDictionary<string, DownloadLock> DownloadLocks = new(StringComparer.Ordinal);
    private static readonly object DownloadLocksSync = new();
    private readonly HttpClient _http;
    private readonly MapPlatformService _platform;
    private readonly IMapBuildScheduler _builds;

    internal static MapAcquisitionService Shared => Process.Value;

    public MapAcquisitionService(HttpMessageHandler? handler = null)
        : this(handler, MapPlatformService.Shared) { }

    private MapAcquisitionService(HttpMessageHandler? handler, MapPlatformService platform)
    {
        handler ??= new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _platform = platform;
        _builds = platform.Builds;
    }

    internal ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        => _platform.Catalog.RefreshAsync(cancellationToken);

    public bool IsInstalled(MapRequirement requirement)
    {
        requirement.Validate();
        InstalledMap? map = _platform.Catalog.Snapshot.Find(requirement.StableId,
            MapVersion.Parse(requirement.Version), requirement.ContentHash);
        return map?.BuildState == MapBuildState.Ready
            && map.ArtifactHash == requirement.ArtifactHash
            && map.PackageSize == requirement.PackageSize;
    }

    public async Task<InstalledMap> AcquireAsync(MapRequirement requirement, string controlEndpoint,
        IProgress<MapDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        requirement.Validate();
        MapCatalog catalog = _platform.Catalog;
        await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        InstalledMap? existing = catalog.Snapshot.Find(requirement.StableId,
            MapVersion.Parse(requirement.Version), requirement.ContentHash);
        if (IsReady(existing, requirement))
        {
            progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Ready"));
            return existing!;
        }

        // Keep ownership through install and compilation. A waiter that only
        // serialized the byte transfer could observe the just-installed map in
        // its transient NeedsBuild state and download the same artifact again.
        // The gate is still keyed by the exact requirement and cancellation is
        // scoped to each waiter, so a canceled caller never interrupts the
        // owner or publishes a half-built catalog entry.
        using (await AcquireDownloadLeaseAsync(requirement, cancellationToken)
            .ConfigureAwait(false))
        {
            // Another service instance may have completed the exact package
            // while this waiter was blocked. Refresh and recheck only after
            // ownership is acquired, before opening the shared partial or
            // starting a duplicate build.
            await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            existing = catalog.Snapshot.Find(requirement.StableId,
                MapVersion.Parse(requirement.Version), requirement.ContentHash);
            if (IsReady(existing, requirement))
            {
                progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Ready"));
                return existing!;
            }

            // A matching package in NeedsBuild/Building state is already the
            // artifact owner for this requirement; only the build below is
            // needed. A missing or stale artifact owns the shared download.
            if (existing == null || existing.ArtifactHash != requirement.ArtifactHash
                || existing.PackageSize != requirement.PackageSize)
            {
                Uri download = DownloadUri(controlEndpoint, requirement);
                Directory.CreateDirectory(MapStoragePaths.InstalledMaps);
                Directory.CreateDirectory(MapStoragePaths.MapCache);
                string partial = Path.Combine(MapStoragePaths.MapCache,
                    requirement.ArtifactHash.ToLowerInvariant() + MapBundle.Extension + ".partial");
                string sidecar = partial + ".json";
                bool installed = false;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(90));
                    await EnsureDownloadedAsync(requirement, download, partial, sidecar, progress,
                        deadline.Token).ConfigureAwait(false);

                    string artifactHash;
                    await using (var verify = new FileStream(partial, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        artifactHash = Convert.ToHexString(await SHA256.HashDataAsync(verify, deadline.Token))
                            .ToLowerInvariant();
                    if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(artifactHash),
                        Convert.FromHexString(requirement.ArtifactHash)))
                        throw new MapPackageException("MAP-DL-004", "Map download artifact hash does not match.");

                    progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Verifying"));
                    MapBundleReadResult bundle = new MapBundleReader(
                        new MapBundleReadOptions { AllowLegacyV1 = false }).Read(partial);
                    if (bundle.Manifest.StableId != requirement.StableId
                        || bundle.Manifest.Version.ToString() != requirement.Version
                        || bundle.Manifest.ContentHash != requirement.ContentHash
                        || bundle.ArtifactHash != requirement.ArtifactHash)
                        throw new MapPackageException("MAP-DL-005",
                            "Downloaded map identity does not match the lobby requirement.");
                    existing = await catalog.InstallAsync(partial, deadline.Token).ConfigureAwait(false);
                    installed = true;
                }
                catch (MapPackageException)
                {
                    // A complete but invalid artifact must not poison the stable
                    // resume path. Transient transport failures are still allowed
                    // to retain the partial and sidecar for the next attempt.
                    DeletePartial(partial, sidecar);
                    throw;
                }
                finally
                {
                    if (installed)
                    {
                        if (File.Exists(partial)) File.Delete(partial);
                        if (File.Exists(sidecar)) File.Delete(sidecar);
                    }
                }
            }

            if (existing == null)
                throw new MapPackageException("MAP-CAT-004",
                    "The requested map was not present after acquisition.");
            progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Compiling"));
            MapBuildResult result = await _builds.BuildAsync(existing.Project,
                new MapBuildOptions
                {
                    CacheDirectory = MapStoragePaths.MapCache,
                    BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash
                }, cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.ContentIdentity != existing.ContentIdentity)
            {
                string? detail = result.Diagnostics.FirstOrDefault(diagnostic =>
                    diagnostic.Severity == MapDiagnosticSeverity.Error)?.Message;
                throw new MapCompilationException("Downloaded map could not be prepared."
                    + (String.IsNullOrWhiteSpace(detail) ? "" : " " + detail),
                    result.Diagnostics);
            }
            string expectedMatch = MapRequirement.ComputeMatchContentHash(
                ContentEnvironment.GetContentIdentity().ContentHash, requirement.StableId,
                requirement.Version, requirement.ContentHash, Update.BuildVersion.Display,
                NetHeader.Version);
            if (expectedMatch != requirement.MatchContentHash)
                throw new MapCompilationException("Downloaded map is incompatible with this client build.");
            catalog.PublishBuildState(existing.ContentIdentity, MapBuildState.Ready,
                result.Statistics, result.Diagnostics);
            progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Ready"));
            return catalog.Snapshot.Find(existing.ContentIdentity) ?? existing;
        }
    }

    private static bool IsReady(InstalledMap? map, MapRequirement requirement)
        => map?.BuildState == MapBuildState.Ready
            && map.ArtifactHash == requirement.ArtifactHash
            && map.PackageSize == requirement.PackageSize;

    private static async ValueTask<DownloadLease> AcquireDownloadLeaseAsync(
        MapRequirement requirement, CancellationToken cancellationToken)
    {
        string key = string.Concat(requirement.StableId, "\n", requirement.Version,
            "\n", requirement.ContentHash.ToLowerInvariant(), "\n",
            requirement.ArtifactHash.ToLowerInvariant(), "\n", requirement.PackageSize);
        DownloadLock entry;
        lock (DownloadLocksSync)
        {
            entry = DownloadLocks.GetOrAdd(key, static _ => new DownloadLock());
            entry.References++;
        }
        bool acquired = false;
        try
        {
            // Cancellation belongs to this waiter only. The owner continues its
            // transfer and releases the gate for the next waiter.
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return new DownloadLease(key, entry);
        }
        finally
        {
            if (!acquired) ReleaseDownloadReference(key, entry);
        }
    }

    private static void ReleaseDownloadReference(string key, DownloadLock entry)
    {
        bool dispose = false;
        lock (DownloadLocksSync)
        {
            if (--entry.References == 0
                && DownloadLocks.TryGetValue(key, out DownloadLock? current)
                && ReferenceEquals(current, entry))
            {
                DownloadLocks.TryRemove(key, out _);
                dispose = true;
            }
        }
        // No future caller can obtain this entry after registry removal.
        if (dispose) entry.Gate.Dispose();
    }

    private async Task EnsureDownloadedAsync(MapRequirement requirement, Uri download,
        string partial, string sidecar, IProgress<MapDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!HasMatchingPartial(requirement, partial, sidecar))
        {
            DeletePartial(partial, sidecar);
            WritePartialMetadata(requirement, sidecar);
        }

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > requirement.PackageSize)
            {
                DeletePartial(partial, sidecar);
                WritePartialMetadata(requirement, sidecar);
                offset = 0;
            }
            if (offset == requirement.PackageSize) return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, download);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using HttpResponseMessage response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    DeletePartial(partial, sidecar);
                    WritePartialMetadata(requirement, sidecar);
                    if (attempt + 1 >= MaximumDownloadAttempts)
                        throw new MapPackageException("MAP-DL-008", "The Node rejected the map resume range.");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(response.StatusCode) && attempt + 1 < MaximumDownloadAttempts)
                    {
                        await DelayForRetryAsync(response, attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
                        throw new MapPackageException("MAP-DL-002", "Map download redirects are not permitted.");
                    response.EnsureSuccessStatusCode();
                }
                if (response.Content.Headers.ContentEncoding.Count != 0)
                    throw new MapPackageException("MAP-DL-006",
                        "Encoded map responses are not permitted; artifact bytes must be transferred exactly.");

                bool resumed = offset > 0;
                if (resumed && response.StatusCode == HttpStatusCode.OK)
                {
                    // A proxy ignored Range. Restart from a clean stable partial
                    // rather than appending a second copy of the artifact.
                    DeletePartial(partial, sidecar);
                    WritePartialMetadata(requirement, sidecar);
                    if (attempt + 1 >= MaximumDownloadAttempts)
                        throw new MapPackageException("MAP-DL-009", "The Node ignored the map resume contract.");
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange is not { } range
                        || range.Unit != "bytes" || range.From != offset
                        || range.Length != requirement.PackageSize || range.To < range.From)
                        throw new MapPackageException("MAP-DL-007", "The Node returned an invalid map Content-Range.");
                }
                else if (response.StatusCode != HttpStatusCode.OK)
                    throw new MapPackageException("MAP-DL-007", "The Node returned an invalid map response.");

                long received = offset;
                await using Stream source = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(partial,
                    resumed ? FileMode.OpenOrCreate : FileMode.Create, FileAccess.Write, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                output.Position = offset;
                byte[] buffer = new byte[64 * 1024];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    received = checked(received + read);
                    if (received > requirement.PackageSize)
                        throw new MapPackageException("MAP-DL-003", "Map download exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report(new(received, requirement.PackageSize, "Downloading"));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (received != requirement.PackageSize)
                    throw new IOException("Map download ended before its declared size.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsTransientException(exception)
                && attempt + 1 < MaximumDownloadAttempts)
            {
                await DelayForRetryAsync(null, attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool HasMatchingPartial(MapRequirement requirement, string partial, string sidecar)
    {
        if (!File.Exists(partial) || !File.Exists(sidecar)
            || new FileInfo(partial).Length > requirement.PackageSize) return false;
        try
        {
            if (new FileInfo(sidecar).Length > 4096) return false;
            PartialMetadata? metadata = JsonSerializer.Deserialize<PartialMetadata>(
                File.ReadAllBytes(sidecar));
            return metadata != null && metadata.PackageSize == requirement.PackageSize
                && metadata.StableId == requirement.StableId && metadata.Version == requirement.Version
                && metadata.ContentHash.Equals(requirement.ContentHash, StringComparison.OrdinalIgnoreCase)
                && metadata.ArtifactHash.Equals(requirement.ArtifactHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WritePartialMetadata(MapRequirement requirement, string sidecar)
    {
        string temporary = sidecar + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PartialMetadata(
                requirement.StableId, requirement.Version, requirement.ContentHash,
                requirement.ArtifactHash, requirement.PackageSize)));
            File.Move(temporary, sidecar, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void DeletePartial(string partial, string sidecar)
    {
        if (File.Exists(partial)) File.Delete(partial);
        if (File.Exists(sidecar)) File.Delete(sidecar);
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool IsTransientException(Exception exception)
        => exception is HttpRequestException or IOException or SocketException;

    private static async Task DelayForRetryAsync(HttpResponseMessage? response, int attempt,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = response?.Headers.RetryAfter?.Delta
            ?? (response?.Headers.RetryAfter?.Date is { } date
                ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
        delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0,
            MaximumRetryAfterSeconds * 1000));
        if (delay == TimeSpan.Zero)
            delay = TimeSpan.FromMilliseconds(attempt == 0 ? 150 : 400);
        delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    internal static Uri DownloadUri(string controlEndpoint, MapRequirement requirement)
    {
        if (!NodeEndpointContract.TryValidatePublicControlUri(controlEndpoint, out Uri control))
            throw new InvalidOperationException(
                "A secure Node control origin is required for map acquisition.");
        return new UriBuilder(control)
        {
            Scheme = Uri.UriSchemeHttps,
            Path = $"/v1/maps/{Uri.EscapeDataString(requirement.StableId)}/"
                + $"{Uri.EscapeDataString(requirement.Version)}/{requirement.ArtifactHash}",
            Query = "",
            Fragment = ""
        }.Uri;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
