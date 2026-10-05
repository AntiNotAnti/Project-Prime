using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public sealed record MapUploadStartRequest(long Bytes, bool Listed, bool Draft);
public sealed record MapUploadState(string PackageHash, long Bytes, long Offset, int ChunkBytes, bool Complete);

public sealed record CommunityMap(string Hash, Guid MapId, string ContentHash, string Name,
    string? DisplayName, string? Author, string? Version, long Bytes)
{
    public bool Listed { get; init; } = true;
    public string OwnerId { get; init; } = MapCreatorCatalog.ServiceOwner;
    public bool Draft { get; init; }
    public int FavoriteCount { get; init; }
    public bool Favorited { get; init; }
    public int MinimumProtocol { get; init; }
    public string[] SupportedModes { get; init; } = Array.Empty<string>();
    public int MinPlayers { get; init; } = 1;
    public int MaxPlayers { get; init; } = 8;
    public DateTimeOffset PublishedAt { get; init; }
    public override string ToString() => $"{DisplayName ?? Name} · {Author ?? "Unknown author"} · {Version ?? "1"} · {Bytes / 1024:N0} KiB · {(Hash is { Length: >=8 } ? Hash[..8] : "invalid ID")}";
}

/// <summary>Immutable, content-addressed packages. Network work never touches the active editor document.</summary>
public sealed class MapCommunityClient : IDisposable
{
    public const string DefaultAddress = "https://maps.rebooty.xyz/";
    private readonly HttpClient _http;
    private readonly Lazy<HttpClient> _directHttp;
    private volatile bool _preferDirectReads;

    public MapCommunityClient(string address, string? uploadToken = null)
    {
        var uri = new Uri(address.TrimEnd('/') + "/", UriKind.Absolute);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Use an HTTPS community address (HTTP is allowed only on localhost).");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use a community base address without credentials, query, or fragment.");
        _http = CreateHttpClient(uri, uploadToken, useProxy: true);
        _directHttp = new Lazy<HttpClient>(() => CreateHttpClient(uri, uploadToken, useProxy: false));
    }

    private static HttpClient CreateHttpClient(Uri uri, string? uploadToken, bool useProxy)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = useProxy
        })
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromMinutes(10)
        };
        if (!string.IsNullOrWhiteSpace(uploadToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken.Trim());
        return client;
    }
    public async Task<CommunityMap[]> BrowseAsync(CancellationToken token, bool mine = false, bool favorites = false, string? sort = null)
    {
        using var response = await GetReadAsync("maps?mine="+mine.ToString().ToLowerInvariant()+"&favorites="+favorites.ToString().ToLowerInvariant()+"&sort="+Uri.EscapeDataString(sort??"name"), HttpCompletionOption.ResponseHeadersRead, token);
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), data, 2 * 1024 * 1024, token);
        return JsonSerializer.Deserialize<CommunityMap[]>(data.ToArray(), MapPackageReader.JsonOptions)
            ?? Array.Empty<CommunityMap>();
    }
    public async Task<CommunityMapProject[]> BrowseProjectsAsync(CancellationToken token,
        bool mine = false, bool favorites = false, string? sort = null)
    {
        string resource = "v2/maps?mine=" + mine.ToString().ToLowerInvariant()
            + "&favorites=" + favorites.ToString().ToLowerInvariant()
            + "&sort=" + Uri.EscapeDataString(sort ?? "name");
        using var response = await GetReadAsync(resource,
            HttpCompletionOption.ResponseHeadersRead, token);
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token),
            data, 4 * 1024 * 1024, token);
        return JsonSerializer.Deserialize<CommunityMapProject[]>(
            data.ToArray(), MapPackageReader.JsonOptions)
            ?? Array.Empty<CommunityMapProject>();
    }

    public async Task<CommunityMapProject?> GetProjectAsync(Guid mapId,
        CancellationToken token)
    {
        using var response = await GetReadAsync("v2/maps/" + mapId,
            HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token),
            data, 512 * 1024, token);
        return JsonSerializer.Deserialize<CommunityMapProject>(
            data.ToArray(), MapPackageReader.JsonOptions);
    }

    public async Task<CommunityMapRevision[]> GetRevisionsAsync(Guid mapId,
        CancellationToken token)
    {
        using var response = await GetReadAsync("v2/maps/" + mapId + "/revisions",
            HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<CommunityMapRevision>();
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token),
            data, 4 * 1024 * 1024, token);
        return JsonSerializer.Deserialize<CommunityMapRevision[]>(
            data.ToArray(), MapPackageReader.JsonOptions)
            ?? Array.Empty<CommunityMapRevision>();
    }

    public async Task SetFavoriteAsync(Guid map,bool favorite,CancellationToken token)
    {
        using var request=new HttpRequestMessage(favorite?HttpMethod.Put:HttpMethod.Delete,"maps/"+map+"/favorite");
        using var response=await _http.SendAsync(request,token);EnsureSuccess(response);
    }
    public async Task ReportAsync(Guid map,MapReportRequest report,CancellationToken token)
    {
        using var content=new StringContent(JsonSerializer.Serialize(report,MapPackageReader.JsonOptions),System.Text.Encoding.UTF8,"application/json");
        using var response=await _http.PostAsync("maps/"+map+"/reports",content,token);EnsureSuccess(response);
    }
    public async Task SetVisibilityAsync(string hash,string visibility,CancellationToken token)
    {
        using var content=new StringContent(JsonSerializer.Serialize(visibility),System.Text.Encoding.UTF8,"application/json");
        using var response=await _http.PostAsync("packages/"+hash+"/visibility",content,token);EnsureSuccess(response);
    }
    public async Task<CommunityMap?> GetPackageAsync(string packageHash, CancellationToken token)
    {
        if (!ValidHash(packageHash)) throw new InvalidDataException("Invalid package hash.");
        using var response = await GetReadAsync("packages/"+packageHash+"/metadata", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token),data,65536,token);
        var result = JsonSerializer.Deserialize<CommunityMap>(data.ToArray(),MapPackageReader.JsonOptions);
        if (result?.Hash != packageHash) throw new InvalidDataException("Community returned different package metadata.");
        return result;
    }
    public const int UploadChunkBytes = 50 * 1024 * 1024;
    private const int MinimumAdaptiveChunkBytes = 1024 * 1024;

    public async Task<CommunityMap> UploadAsync(string path, CancellationToken token, bool listed = true, bool draft = false,
        Action<long, long>? progress = null)
    {
        using var package = new MapPackageReader(path);
        if (package.Manifest == null) throw new InvalidDataException("Build a current .ppmap package before sharing.");
        long bytes = new FileInfo(path).Length;
        if (bytes <= 0 || bytes > MapPackageReader.MaxArchiveBytes) throw new InvalidDataException("Package exceeds the map archive size limit.");
        string hash = MapBuildFingerprint.HashFile(path);

        // Resolve expired credentials before any upload body is streamed. An early
        // 401 can otherwise close the socket mid-write and hide the status needed
        // by the launcher's credential-refresh retry.
        using (var authorization = await GetReadAsync("maps?mine=true&pageSize=1",
            HttpCompletionOption.ResponseHeadersRead, token)) EnsureSuccess(authorization);

        MapUploadState? state = await BeginChunkedUploadAsync(hash, bytes, listed, draft, token).ConfigureAwait(false);
        if (state == null)
            return await UploadLegacyAsync(path, listed, draft, token, progress).ConfigureAwait(false);

        ValidateUploadState(state, hash, bytes);
        progress?.Invoke(state.Offset, bytes);
        int recoveries = 0;
        while (!state.Complete && state.Offset < bytes)
        {
            token.ThrowIfCancellationRequested();
            long offset = state.Offset;
            long length = Math.Min(state.ChunkBytes, bytes - offset);
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Put,
                    "uploads/" + hash + "?offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Content = new FileSegmentContent(path, offset, length);
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (!token.IsCancellationRequested && recoveries++ < 3)
                {
                    state = await GetUploadStateAsync(hash, token).ConfigureAwait(false);
                    ValidateUploadState(state, hash, bytes);
                    progress?.Invoke(state.Offset, bytes);
                    break;
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested && recoveries++ < 3)
                {
                    // HttpClient timeout rather than caller cancellation. The
                    // service may already have persisted a prefix of this chunk.
                    state = await GetUploadStateAsync(hash, token).ConfigureAwait(false);
                    ValidateUploadState(state, hash, bytes);
                    progress?.Invoke(state.Offset, bytes);
                    break;
                }
                using (response)
                {
                    // A deployment may have an older/lower reverse-proxy body
                    // cap than the service advertises. Smaller chunks are valid,
                    // so adapt instead of making the creator repack the map.
                    if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge && length > MinimumAdaptiveChunkBytes)
                    {
                        length = Math.Max(MinimumAdaptiveChunkBytes, length / 2);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        state = await GetUploadStateAsync(hash, token).ConfigureAwait(false);
                        ValidateUploadState(state, hash, bytes);
                        break;
                    }
                    if (response.StatusCode == HttpStatusCode.BadRequest && recoveries++ < 3)
                    {
                        var recovered = await GetUploadStateAsync(hash, token).ConfigureAwait(false);
                        ValidateUploadState(recovered, hash, bytes);
                        if (recovered.Offset > offset)
                        {
                            state = recovered;
                            progress?.Invoke(state.Offset, bytes);
                            break;
                        }
                    }
                    EnsureSuccess(response);
                    state = await ReadUploadStateAsync(response, token).ConfigureAwait(false);
                    ValidateUploadState(state, hash, bytes);
                    if (state.Offset <= offset && !state.Complete)
                        throw new InvalidDataException("Community upload did not advance.");
                    progress?.Invoke(state.Offset, bytes);
                    recoveries = 0;
                    break;
                }
            }
        }

        if (!state.Complete)
        {
            using var complete = await _http.PostAsync("uploads/" + hash + "/complete",
                new ByteArrayContent(Array.Empty<byte>()), token).ConfigureAwait(false);
            if (complete.StatusCode == HttpStatusCode.Conflict)
                throw new HttpRequestException("This map version already has different published contents. Increase the project's Version before publishing.", null, complete.StatusCode);
            EnsureSuccess(complete);
            using var data = new MemoryStream();
            await CopyBoundedAsync(await complete.Content.ReadAsStreamAsync(token), data, 64 * 1024, token).ConfigureAwait(false);
            var published = JsonSerializer.Deserialize<CommunityMap>(data.ToArray(), MapPackageReader.JsonOptions)
                ?? throw new InvalidDataException("The community returned no map identity.");
            progress?.Invoke(bytes, bytes);
            return ConfirmUpload(path, published);
        }

        var existing = await GetPackageAsync(hash, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The community reported a completed upload without package metadata.");
        progress?.Invoke(bytes, bytes);
        return ConfirmUpload(path, existing);
    }

    private async Task<MapUploadState?> BeginChunkedUploadAsync(string hash, long bytes, bool listed, bool draft, CancellationToken token)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new MapUploadStartRequest(bytes, listed, draft), MapPackageReader.JsonOptions),
            System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("uploads/" + hash, content, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return null;
        EnsureSuccess(response);
        return await ReadUploadStateAsync(response, token).ConfigureAwait(false);
    }

    private async Task<MapUploadState> GetUploadStateAsync(string hash, CancellationToken token)
    {
        using var response = await GetReadAsync("uploads/" + hash, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        EnsureSuccess(response);
        return await ReadUploadStateAsync(response, token).ConfigureAwait(false);
    }

    private static async Task<MapUploadState> ReadUploadStateAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), data, 64 * 1024, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MapUploadState>(data.ToArray(), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("The community returned no upload state.");
    }

    private static void ValidateUploadState(MapUploadState state, string hash, long bytes)
    {
        if (state.PackageHash != hash || state.Bytes != bytes || state.Offset < 0 || state.Offset > bytes
            || state.ChunkBytes <= 0 || state.ChunkBytes > UploadChunkBytes
            || state.Complete && state.Offset != bytes)
            throw new InvalidDataException("The community returned an invalid upload state.");
    }

    private async Task<CommunityMap> UploadLegacyAsync(string path, bool listed, bool draft, CancellationToken token,
        Action<long, long>? progress)
    {
        using var stream = File.OpenRead(path);
        progress?.Invoke(0, stream.Length);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage uploaded;
        try
        {
            uploaded = await _http.PostAsync("maps?listed="+listed.ToString().ToLowerInvariant()+"&draft="+draft.ToString().ToLowerInvariant(), content, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == null && !token.IsCancellationRequested)
        {
            throw new HttpRequestException($"Upload interrupted while sending {stream.Length / 1048576.0:N1} MiB. "
                + "This Community server does not advertise resumable uploads; refresh My Maps to check whether it arrived, then retry if missing. "
                + ex.GetBaseException().Message, ex);
        }
        using var response = uploaded;
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new HttpRequestException("This map version already has different published contents. Increase the project's Version before publishing.",null,response.StatusCode);
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), data, 64 * 1024, token).ConfigureAwait(false);
        var published = JsonSerializer.Deserialize<CommunityMap>(data.ToArray(), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("The community returned no map identity.");
        progress?.Invoke(stream.Length, stream.Length);
        return ConfirmUpload(path, published);
    }

    private static CommunityMap ConfirmUpload(string path, CommunityMap published)
    {
        var identity = MapContentIdentity.FromPackage(path);
        if (published.Hash != identity.PackageHash.ToString() || published.ContentHash != identity.ContentHash.ToString()
            || published.MapId != identity.MapId || published.Name != identity.RoomKey || published.Bytes != new FileInfo(path).Length)
            throw new InvalidDataException("The community did not confirm the exact uploaded package.");
        return published;
    }

    private sealed class FileSegmentContent : HttpContent
    {
        private readonly string _path;
        private readonly long _offset;
        private readonly long _length;
        public FileSegmentContent(string path, long offset, long length)
        {
            _path = path; _offset = offset; _length = length;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        protected override bool TryComputeLength(out long length) { length = _length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => CopyAsync(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => CopyAsync(stream, cancellationToken);
        private async Task CopyAsync(Stream destination, CancellationToken token)
        {
            await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            input.Seek(_offset, SeekOrigin.Begin);
            byte[] buffer = new byte[65536];
            long remaining = _length;
            while (remaining > 0)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Map package changed while it was being uploaded.");
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                remaining -= read;
            }
        }
    }

    private async Task<long> DownloadPackageAsync(string resource, string temporary, long? expectedBytes,
        CancellationToken token, Action<long, long>? progress = null)
    {
        const int maximumRetries = 4;
        long expected = expectedBytes ?? -1;
        int failures = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            long offset = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
            if (offset < 0 || offset > MapPackageReader.MaxArchiveBytes || expected >= 0 && offset > expected)
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                offset = 0;
            }
            if (expected >= 0 && offset == expected)
            {
                progress?.Invoke(offset, expected);
                return expected;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, resource);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await SendReadAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    expected = expectedBytes ?? -1;
                    if (++failures > maximumRetries) EnsureSuccess(response);
                    continue;
                }

                if (offset > 0 && response.StatusCode == HttpStatusCode.OK)
                {
                    // Older Community services do not understand Range. Restart
                    // from byte zero using this full response rather than appending
                    // it to a partial archive.
                    if (File.Exists(temporary)) File.Delete(temporary);
                    offset = 0;
                }
                else
                {
                    EnsureSuccess(response);
                    if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                        throw new InvalidDataException("Community did not honor the requested download range.");
                }

                long total = expected;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange
                        ?? throw new InvalidDataException("Community returned a ranged package without Content-Range.");
                    if (!range.HasRange || range.From != offset || !range.HasLength)
                        throw new InvalidDataException("Community returned an invalid package range.");
                    total = range.Length!.Value;
                }
                else if (response.Content.Headers.ContentLength is long length)
                {
                    total = length;
                }

                if (total <= 0 || total > MapPackageReader.MaxArchiveBytes)
                    throw new InvalidDataException("Package exceeds size limit.");
                if (expectedBytes is long exact && total != exact)
                    throw new InvalidDataException("Community package size does not match its listing.");
                expected = total;

                long maximum = expected - offset;
                await using (var output = new FileStream(temporary,
                    offset == 0 ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (offset > 0)
                    {
                        if (output.Length != offset) throw new InvalidDataException("Partial map download changed unexpectedly.");
                        output.Position = offset;
                    }
                    progress?.Invoke(offset, expected);
                    await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), output,
                        maximum, token, received => progress?.Invoke(offset + received, expected)).ConfigureAwait(false);
                    await output.FlushAsync(token).ConfigureAwait(false);
                }

                long completed = new FileInfo(temporary).Length;
                if (completed == expected)
                {
                    progress?.Invoke(completed, expected);
                    return expected;
                }
                if (completed > expected) throw new InvalidDataException("Downloaded package exceeds its declared size.");
                throw new EndOfStreamException($"Map download ended at {completed:N0} of {expected:N0} bytes.");
            }
            catch (Exception ex) when (!token.IsCancellationRequested && failures < maximumRetries
                && (ex is HttpRequestException || ex is TaskCanceledException
                    || ex is IOException && ex is not InvalidDataException))
            {
                failures++;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * failures), token).ConfigureAwait(false);
            }
        }
    }

    private async Task<long> DownloadVerifiedPackageAsync(string resource, string temporary, long? expectedBytes,
        string expectedHash, CancellationToken token, Action<long, long>? progress = null)
    {
        const int maximumIntegrityAttempts = 2;
        string actualHash = "";
        for (int attempt = 0; attempt < maximumIntegrityAttempts; attempt++)
        {
            long bytes = await DownloadPackageAsync(resource, temporary, expectedBytes, token, progress).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            actualHash = MapBuildFingerprint.HashFile(temporary);
            if (actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return bytes;

            // A same-length stale/corrupt response previously reached ZIP parsing and
            // surfaced as a misleading project.json error. Discard it and retry once
            // from byte zero before reporting the integrity failure.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        throw new InvalidDataException($"Downloaded package SHA-256 does not match the Community listing. "
            + $"Expected {expectedHash}, received {actualHash}. The download was discarded after a retry.");
    }

    public async Task<MapDefinition> InstallAsync(CommunityMap map, string library, CancellationToken token,
        Action<long, long>? progress = null)
    {
        if (map.MinimumProtocol > Network.NetConfig.ProtocolVersion) throw new InvalidDataException("Update Project Prime before installing this map.");
        if (!ValidHash(map.Hash) || !ValidHash(map.ContentHash) || map.MapId == Guid.Empty
            || map.Bytes <= 0 || map.Bytes > MapPackageReader.MaxArchiveBytes)
            throw new InvalidDataException("Invalid community map identity or size.");
        string temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            await DownloadVerifiedPackageAsync("packages/" + map.Hash, temporary, map.Bytes, map.Hash, token, progress).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using (var package = new MapPackageReader(temporary))
                if (package.Manifest?.Name != map.Name) throw new InvalidDataException("Map name does not match the listing.");
            return await Task.Run(() => MapPackageInstaller.Install(temporary, map.MapId,
                map.ContentHash, map.Hash, library), token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<PreparedMapInstallation> PrepareExactAsync(MapContentIdentity required, CancellationToken token,
        Action<string>? stage = null, Action<float>? progress = null)
    {
        if (!required.IsCustom || required.MapId == Guid.Empty || required.ContentHash.IsZero || required.PackageHash.IsZero)
            throw new InvalidDataException("Invalid required map identity.");
        string temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            stage?.Invoke("Downloading");
            await DownloadVerifiedPackageAsync("packages/" + required.PackageHash, temporary, null,
                required.PackageHash.ToString(), token,
                (received, total) => progress?.Invoke(total > 0 ? Math.Clamp((float)received / total, 0, 1) : 0)).ConfigureAwait(false);
            stage?.Invoke("Verifying");
            if (!MapContentIdentity.FromPackage(temporary).Matches(required)) throw new InvalidDataException("The community returned a different map package.");
            stage?.Invoke("Building");
            return await MapPackageInstaller.PrepareAsync(temporary, required, token).ConfigureAwait(false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Host services cache archives without publishing into their own running match.
    public async Task DownloadExactAsync(MapContentIdentity required, string destination, CancellationToken token)
    {
        if (!required.IsCustom || required.MapId == Guid.Empty || required.ContentHash.IsZero || required.PackageHash.IsZero)
            throw new InvalidDataException("Invalid required map identity.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            await DownloadVerifiedPackageAsync("packages/" + required.PackageHash, temporary, null,
                required.PackageHash.ToString(), token).ConfigureAwait(false);
            if (!MapContentIdentity.FromPackage(temporary).Matches(required)) throw new InvalidDataException("Community returned a different map package.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> GetReadAsync(string resource, HttpCompletionOption completionOption,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, resource);
        return await SendReadAsync(request, completionOption, token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendReadAsync(HttpRequestMessage request,
        HttpCompletionOption completionOption, CancellationToken token)
    {
        if (_preferDirectReads)
        {
            using var directRequest = CloneReadRequest(request);
            return await _directHttp.Value.SendAsync(directRequest, completionOption, token).ConfigureAwait(false);
        }

        try
        {
            return await _http.SendAsync(request, completionOption, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (!token.IsCancellationRequested && IsTlsFrameFailure(ex))
        {
            try
            {
                using var directRequest = CloneReadRequest(request);
                HttpResponseMessage response = await _directHttp.Value.SendAsync(
                    directRequest, completionOption, token).ConfigureAwait(false);
                _preferDirectReads = true;
                return response;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception direct) when (direct is HttpRequestException
                || direct is IOException || direct is TaskCanceledException)
            {
                throw CommunityTlsException(ex, direct);
            }
        }
    }

    private static HttpRequestMessage CloneReadRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            throw new InvalidOperationException("Only idempotent Community reads may use the direct-network fallback.");
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    internal static bool IsTlsFrameFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current.Message.Contains("Cannot determine the frame size or a corrupted frame was received",
                StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static HttpRequestException CommunityTlsException(HttpRequestException routedFailure, Exception directFailure)
    {
        return new HttpRequestException(
            "Community HTTPS handshake failed. Project Prime retried without the system proxy, but the direct HTTPS connection also failed. "
            + "Check VPN/proxy settings, antivirus HTTPS inspection, captive-portal login, or network filtering, then retry. "
            + "TLS certificate validation remains enabled. Direct error: " + directFailure.GetBaseException().Message,
            new AggregateException(routedFailure, directFailure));
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new HttpRequestException("Community authentication expired or was rejected. Project Prime will refresh your Hunter License credential; try again.", null, response.StatusCode);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new HttpRequestException("Your Hunter License does not have permission to modify or read this map version.", null, response.StatusCode);
        if ((int)response.StatusCode == 429)
            throw new HttpRequestException("The Community service is busy. Wait a moment and try again.", null, response.StatusCode);
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            throw new HttpRequestException("Community identity verification is temporarily unavailable.", null, response.StatusCode);
        if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
            throw new HttpRequestException("The map exceeds this service or HTTPS proxy's upload size limit. Reduce packaged assets or ask the server operator to increase the limit.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    public static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static async Task CopyBoundedAsync(Stream source, Stream destination, long maximum, CancellationToken token, Action<long>? progress = null)
    {
        byte[] buffer = new byte[65536]; long total = 0; int read;
        while ((read = await source.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > maximum) throw new InvalidDataException("Transfer exceeds the allowed size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            progress?.Invoke(total);
        }
    }
    public void Dispose()
    {
        _http.Dispose();
        if (_directHttp.IsValueCreated) _directHttp.Value.Dispose();
    }
}
