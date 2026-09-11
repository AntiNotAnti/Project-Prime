using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    private readonly HttpClient _http;
    private readonly MapCatalog _catalog;

    public MapAcquisitionService(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = MapStoragePaths.InstalledMaps,
            ProjectDirectories = [MapStoragePaths.Projects, CustomRooms.MapDirectory],
            CacheDirectory = MapStoragePaths.MapCache
        }, new MapBundleReadOptions { AllowLegacyV1 = false });
    }

    internal ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        => _catalog.RefreshAsync(cancellationToken);

    public bool IsInstalled(MapRequirement requirement)
    {
        requirement.Validate();
        InstalledMap? map = _catalog.Snapshot.Find(requirement.StableId,
            MapVersion.Parse(requirement.Version), requirement.ContentHash);
        return map?.ArtifactHash == requirement.ArtifactHash
            && map.PackageSize == requirement.PackageSize;
    }

    public async Task<InstalledMap> AcquireAsync(MapRequirement requirement, string controlEndpoint,
        IProgress<MapDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        requirement.Validate();
        await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        InstalledMap? existing = _catalog.Snapshot.Find(requirement.StableId,
            MapVersion.Parse(requirement.Version), requirement.ContentHash);
        if (existing?.ArtifactHash != requirement.ArtifactHash
            || existing.PackageSize != requirement.PackageSize)
        {
            Uri download = DownloadUri(controlEndpoint, requirement);
            Directory.CreateDirectory(MapStoragePaths.InstalledMaps);
            // Keep the in-progress artifact outside the catalog root. A
            // valid .fpmap placed in InstalledMaps was discovered alongside
            // the atomically installed copy and could win identity
            // deduplication; cleanup then deleted the Project selected for
            // compilation.
            string temporary = Path.Combine(Path.GetTempPath(),
                "project-prime-map-download-" + Guid.NewGuid().ToString("N")
                + MapBundle.Extension);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                using var request = new HttpRequestMessage(HttpMethod.Get, download);
                using HttpResponseMessage response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
                    throw new MapPackageException("MAP-DL-002", "Map download redirects are not permitted.");
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentEncoding.Count != 0)
                    throw new MapPackageException("MAP-DL-006",
                        "Encoded map responses are not permitted; artifact bytes must be transferred exactly.");
                long? declared = response.Content.Headers.ContentLength;
                if (declared.HasValue && declared.Value != requirement.PackageSize)
                    throw new MapPackageException("MAP-DL-003", "Map download size does not match admission metadata.");
                long received = 0;
                string artifactHash;
                await using (Stream source = await response.Content
                    .ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        while (true)
                        {
                            int read = await source.ReadAsync(buffer, deadline.Token)
                                .ConfigureAwait(false);
                            if (read == 0) break;
                            received = checked(received + read);
                            if (received > requirement.PackageSize)
                                throw new MapPackageException("MAP-DL-003",
                                    "Map download exceeded its declared size.");
                            hash.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token)
                                .ConfigureAwait(false);
                            progress?.Report(new(received, requirement.PackageSize,
                                "Downloading"));
                        }
                        await output.FlushAsync(deadline.Token).ConfigureAwait(false);
                        artifactHash = Convert.ToHexString(hash.GetHashAndReset())
                            .ToLowerInvariant();
                    }
                }
                if (received != requirement.PackageSize)
                    throw new MapPackageException("MAP-DL-003", "Map download ended before its declared size.");
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(artifactHash),
                    Convert.FromHexString(requirement.ArtifactHash)))
                    throw new MapPackageException("MAP-DL-004", "Map download artifact hash does not match.");

                progress?.Report(new(received, requirement.PackageSize, "Verifying"));
                MapBundleReadResult bundle = new MapBundleReader(
                    new MapBundleReadOptions { AllowLegacyV1 = false }).Read(temporary);
                if (bundle.Manifest.StableId != requirement.StableId
                    || bundle.Manifest.Version.ToString() != requirement.Version
                    || bundle.Manifest.ContentHash != requirement.ContentHash
                    || bundle.ArtifactHash != requirement.ArtifactHash)
                    throw new MapPackageException("MAP-DL-005",
                        "Downloaded map identity does not match the lobby requirement.");
                existing = await _catalog.InstallAsync(temporary, deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Compiling"));
        MapBuildResult result = await new MapCompiler().CompileAsync(existing.Project,
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
        _catalog.PublishBuildState(existing.ContentIdentity, MapBuildState.Ready,
            result.Statistics, result.Diagnostics);
        progress?.Report(new(requirement.PackageSize, requirement.PackageSize, "Ready"));
        return _catalog.Snapshot.Find(existing.ContentIdentity) ?? existing;
    }

    internal static Uri DownloadUri(string controlEndpoint, MapRequirement requirement)
    {
        if (!Uri.TryCreate(controlEndpoint, UriKind.Absolute, out Uri? control)
            || control.Scheme != "wss" || !String.IsNullOrEmpty(control.UserInfo))
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
        _catalog.Dispose();
        _http.Dispose();
    }
}
