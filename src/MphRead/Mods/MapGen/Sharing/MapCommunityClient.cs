using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

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
    public MapCommunityClient(string address, string? uploadToken = null)
    {
        var uri = new Uri(address.TrimEnd('/') + "/", UriKind.Absolute);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Use an HTTPS community address (HTTP is allowed only on localhost).");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use a community base address without credentials, query, or fragment.");
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(3) };
        if (!string.IsNullOrWhiteSpace(uploadToken)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken.Trim());
    }
    public async Task<CommunityMap[]> BrowseAsync(CancellationToken token, bool mine = false, bool favorites = false, string? sort = null)
    {
        using var response = await _http.GetAsync("maps?mine="+mine.ToString().ToLowerInvariant()+"&favorites="+favorites.ToString().ToLowerInvariant()+"&sort="+Uri.EscapeDataString(sort??"name"), HttpCompletionOption.ResponseHeadersRead, token);
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), data, 2 * 1024 * 1024, token);
        return JsonSerializer.Deserialize<CommunityMap[]>(data.ToArray(), MapPackageReader.JsonOptions)
            ?? Array.Empty<CommunityMap>();
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
        using var response = await _http.GetAsync("packages/"+packageHash+"/metadata", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token),data,65536,token);
        var result = JsonSerializer.Deserialize<CommunityMap>(data.ToArray(),MapPackageReader.JsonOptions);
        if (result?.Hash != packageHash) throw new InvalidDataException("Community returned different package metadata.");
        return result;
    }
    public async Task<CommunityMap> UploadAsync(string path, CancellationToken token, bool listed = true, bool draft = false)
    {
        using var package = new MapPackageReader(path);
        if (package.Manifest == null) throw new InvalidDataException("Build a current .ppmap package before sharing.");
        // Resolve expired credentials before streaming a body. An early 401 can
        // otherwise close the socket mid-write and hide the status needed by
        // the launcher's credential-refresh retry.
        using (var authorization = await _http.GetAsync("maps?mine=true&pageSize=1",
            HttpCompletionOption.ResponseHeadersRead, token)) EnsureSuccess(authorization);
        using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage uploaded;
        try
        {
            uploaded = await _http.PostAsync("maps?listed="+listed.ToString().ToLowerInvariant()+"&draft="+draft.ToString().ToLowerInvariant(), content, token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == null && !token.IsCancellationRequested)
        {
            throw new HttpRequestException($"Upload interrupted while sending {stream.Length / 1048576.0:N1} MiB. "
                + "Refresh My Maps to check whether it arrived, then retry if missing. "
                + ex.GetBaseException().Message, ex);
        }
        using var response = uploaded;
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new HttpRequestException("This map version already has different published contents. Increase the project's Version before publishing.",null,response.StatusCode);
        EnsureSuccess(response);
        using var data = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), data, 64 * 1024, token);
        var published = JsonSerializer.Deserialize<CommunityMap>(data.ToArray(), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("The community returned no map identity.");
        var identity = MapContentIdentity.FromPackage(path);
        if (published.Hash != identity.PackageHash.ToString() || published.ContentHash != identity.ContentHash.ToString()
            || published.MapId != identity.MapId || published.Name != identity.RoomKey || published.Bytes != new FileInfo(path).Length)
            throw new InvalidDataException("The community did not confirm the exact uploaded package.");
        return published;
    }
    public async Task<MapDefinition> InstallAsync(CommunityMap map, string library, CancellationToken token)
    {
        if (map.MinimumProtocol > Network.NetConfig.ProtocolVersion) throw new InvalidDataException("Update Project Prime before installing this map.");
        if (!ValidHash(map.Hash) || !ValidHash(map.ContentHash) || map.MapId == Guid.Empty
            || map.Bytes <= 0 || map.Bytes > MapPackageReader.MaxArchiveBytes)
            throw new InvalidDataException("Invalid community map identity or size.");
        string temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            using (var response = await _http.GetAsync("maps/" + map.Hash, HttpCompletionOption.ResponseHeadersRead, token))
            {
                EnsureSuccess(response);
                await using var output = File.Create(temporary);
                await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), output, map.Bytes, token);
                if (output.Length != map.Bytes) throw new InvalidDataException("Incomplete map download.");
            }
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
            using (var response = await _http.GetAsync("maps/" + required.PackageHash, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                EnsureSuccess(response);
                if (response.Content.Headers.ContentLength > MapPackageReader.MaxArchiveBytes) throw new InvalidDataException("Package exceeds size limit.");
                await using var output = File.Create(temporary);
                await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), output, MapPackageReader.MaxArchiveBytes, token,
                    bytes => progress?.Invoke(response.Content.Headers.ContentLength is long size && size > 0 ? Math.Clamp((float)bytes / size, 0, 1) : 0)).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is { } length && output.Length != length) throw new InvalidDataException("Incomplete map download.");
            }
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
            using var response = await _http.GetAsync("packages/" + required.PackageHash, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            EnsureSuccess(response);
            if (response.Content.Headers.ContentLength > MapPackageReader.MaxArchiveBytes) throw new InvalidDataException("Package exceeds size limit.");
            await using (var output = File.Create(temporary))
            {
                await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(token), output, MapPackageReader.MaxArchiveBytes, token).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is long length && output.Length != length) throw new InvalidDataException("Incomplete map download.");
            }
            if (!MapContentIdentity.FromPackage(temporary).Matches(required)) throw new InvalidDataException("Community returned a different map package.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
    public void Dispose() => _http.Dispose();
}
