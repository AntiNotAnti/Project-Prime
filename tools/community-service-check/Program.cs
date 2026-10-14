using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// PR-07 loopback integration. Exercises the real HttpListener, not a
// mock of the service's metadata/transfer admission and quota ledger.
int checks = 0;
void Check(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
string root = Path.Combine(Path.GetTempPath(),
    "prime-community-service-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string store = Path.Combine(root, "storage");
Directory.CreateDirectory(store);
const string secret = "service-preflight-acceptance-secret-2026";
Task? running = null;
CancellationTokenSource? serverStop = null;
string Endpoint()
{
    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    return "http://127.0.0.1:" + ((IPEndPoint)probe.LocalEndpoint).Port + "/hub/";
}
async Task<(string Url, HttpClient Client)> StartAsync()
{
    string address = Endpoint();
    serverStop = new CancellationTokenSource();
    running = MapCommunityServer.ServeAsync(address, store, secret, serverStop.Token);
    var http = new HttpClient { BaseAddress = new Uri(address),
        Timeout = TimeSpan.FromSeconds(45) };
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", secret);
    var timeout = Stopwatch.StartNew();
    while (true)
    {
        try
        {
            using var response = await http.GetAsync("health");
            if (response.IsSuccessStatusCode) return (address, http);
        }
        catch (HttpRequestException) { }
        if (timeout.Elapsed > TimeSpan.FromSeconds(12))
            throw new TimeoutException("Community server did not become available.");
        await Task.Delay(50);
    }
}
async Task StopAsync()
{
    serverStop?.Cancel();
    if (running != null)
    {
        try { await running.WaitAsync(TimeSpan.FromSeconds(12)); }
        catch (OperationCanceledException) { }
    }
    serverStop?.Dispose();
    serverStop = null;
    running = null;
}
async Task<HttpResponseMessage> BeginAsync(HttpClient client, char hash, long size)
{
    var request = new MapUploadStartRequest(size, Listed: true, Draft: false);
    return await client.PostAsync("uploads/" + new string(hash, 64),
        new StringContent(JsonSerializer.Serialize(request,
            MapPackageReader.JsonOptions), Encoding.UTF8, "application/json"));
}
async Task<long> ReservedAsync(HttpClient client)
{
    using var response = await client.GetAsync("v2/metrics");
    response.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("uploadQuota")
        .GetProperty("reservedBytes").GetInt64();
}

try
{
    string firstAddress;
    {
        var started = await StartAsync();
        firstAddress = started.Url;
        using var http = started.Client;
        using var api = new MapCommunityClient(firstAddress, secret);
        var caps = await api.GetCapabilitiesAsync();
        Check(caps is { ApiVersion: 3, LegacyApiVersion: 2,
            SupportsSequentialResumableUploads: true,
            SupportsParallelUploadRanges: false,
            SupportsGenericContentCatalog: false }
            && caps.MaxArchiveBytes == MapPackageReader.MaxArchiveBytes
            && caps.MaxUploadChunkBytes == MapCommunityClient.UploadChunkBytes
            && caps.MaxConcurrentUploads == 3 && caps.MaxConcurrentDownloads == 8,
            "v3 preflight advertises actual legacy/resumable limits without enabling generic v3 content");

        using (var anonymous = new HttpClient { BaseAddress = new Uri(firstAddress) })
        using (var forbidden = await anonymous.GetAsync("v2/metrics"))
            Check(forbidden.StatusCode == HttpStatusCode.Forbidden,
                "moderator diagnostics cannot be read anonymously");

        var map = MapTemplates.Create("SERVICE_PREFLIGHT", true).Definition;
        map.BaseDirectory = root;
        Directory.CreateDirectory(Path.Combine(root, "preview"));
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aegAAAABJRU5ErkJggg==");
        File.WriteAllBytes(Path.Combine(root, "preview", "thumbnail.png"), png);
        map.Assets.Add(new MapAsset { Kind = "preview", Path = "preview/thumbnail.png" });
        string package = MapPackageBuilder.Build(map, Path.Combine(root, "source.ppmap"));
        var published = await api.UploadAsync(package, CancellationToken.None);
        Check(published.Hash == MapBuildFingerprint.HashFile(package),
            "unchanged Community publication retains exact package SHA-256");

        using (var catalog = await http.GetAsync("v2/maps?page=1&pageSize=1"))
        {
            Check(catalog.IsSuccessStatusCode
                && (await catalog.Content.ReadAsStringAsync()).Contains("SERVICE_PREFLIGHT")
                && catalog.Headers.ETag != null
                && catalog.Headers.TryGetValues("X-Total-Count", out var totals)
                && totals.Single() == "1",
                "v2 paginated catalog retains array response and supplies a stable ETag");
            using var conditional = new HttpRequestMessage(HttpMethod.Get, "v2/maps?page=1&pageSize=1");
            conditional.Headers.IfNoneMatch.Add(catalog.Headers.ETag!);
            using var unchanged = await http.SendAsync(conditional);
            Check(unchanged.StatusCode == HttpStatusCode.NotModified,
                "authorized catalog supports conditional GET without resending body");
        }
        using (var full = await http.GetAsync("packages/" + published.Hash))
        {
            Check(full.IsSuccessStatusCode && full.Headers.ETag != null
                && (await full.Content.ReadAsByteArrayAsync())
                    .SequenceEqual(File.ReadAllBytes(package)),
                "immutable package download remains byte-identical with ETag");
            using var conditional = new HttpRequestMessage(HttpMethod.Get,
                "packages/" + published.Hash);
            conditional.Headers.IfNoneMatch.Add(full.Headers.ETag!);
            using var cached = await http.SendAsync(conditional);
            Check(cached.StatusCode == HttpStatusCode.NotModified,
                "immutable package supports safe conditional GET");
        }
        using (var preview = await http.GetAsync("maps/" + published.Hash + "/preview"))
            Check(preview.IsSuccessStatusCode
                && preview.Content.Headers.ContentType?.MediaType == "image/png"
                && (await preview.Content.ReadAsByteArrayAsync()).SequenceEqual(png),
                "bounded preview fetch avoids a complete .ppmap download");

        var oversizedPreviewMap = MapTemplates.Create("SERVICE_BIG_PREVIEW", true).Definition;
        oversizedPreviewMap.BaseDirectory = root;
        byte[] bigPreview = new byte[1024 * 1024 + 1];
        Array.Copy(png, bigPreview, png.Length);
        File.WriteAllBytes(Path.Combine(root, "preview", "oversized.png"), bigPreview);
        oversizedPreviewMap.Assets.Add(new MapAsset
        {
            Kind = "preview", Path = "preview/oversized.png"
        });
        string secondPackage = MapPackageBuilder.Build(oversizedPreviewMap,
            Path.Combine(root, "oversized-preview.ppmap"));
        CommunityMap oversized = await api.UploadAsync(secondPackage,
            CancellationToken.None);
        using (var limited = await http.GetAsync("packages/" + oversized.Hash + "/preview"))
            Check(limited.StatusCode == HttpStatusCode.RequestEntityTooLarge,
                "embedded previews above 1 MiB are refused before client display");

        const long quotaSlice = 512L * 1024 * 1024;
        using (var first = await BeginAsync(http, 'a', quotaSlice))
            Check(first.IsSuccessStatusCode, "first 512 MiB session reserves future upload bytes");
        using (var second = await BeginAsync(http, 'b', quotaSlice))
            Check(second.IsSuccessStatusCode, "second 512 MiB session reserves a full GiB per creator");
        Check(await ReservedAsync(http) == 2 * quotaSlice,
            "quota counts expected session bytes, not current zero-length partial files");
        using (var denied = await BeginAsync(http, 'c', quotaSlice))
            Check(denied.StatusCode == HttpStatusCode.InsufficientStorage,
                "third 512 MiB upload is rejected by reserved creator quota before transfer");
    }
    await StopAsync();

    {
        var started = await StartAsync();
        using var http = started.Client;
        string address = started.Url;
        using (var persisted = await BeginAsync(http, 'c', 512L * 1024 * 1024))
            Check(persisted.StatusCode == HttpStatusCode.InsufficientStorage,
                "quota reservations are reconciled across a real Community restart");
        using (var deleted = await http.DeleteAsync("uploads/" + new string('a', 64)))
            Check(deleted.StatusCode == HttpStatusCode.NoContent,
                "deleting an abandoned upload releases its reserved storage");
        using (var admitted = await BeginAsync(http, 'c', 512L * 1024 * 1024))
            Check(admitted.IsSuccessStatusCode,
                "released quota admits a new full-size upload without restart");
        foreach (char id in new[] { 'b', 'c' })
            using (var deleted = await http.DeleteAsync("uploads/" + new string(id, 64)))
                Check(deleted.StatusCode == HttpStatusCode.NoContent,
                    "completed quota cleanup frees creator reservation");
        Check(await ReservedAsync(http) == 0, "quota ledger matches deleted sessions");

        // Block all transfer workers with slow sequential PUT bodies. The
        // metadata lane must still serve health, browse and authenticated
        // diagnostics quickly. This tests real network backpressure rather
        // than asking a mock semaphore for a slot.
        const long slowBytes = 512L * 1024;
        var bodies = new SlowBody[3];
        var sends = new Task<HttpResponseMessage>[3];
        var abort = new CancellationTokenSource[3];
        for (int i = 0; i < 3; i++)
        {
            char key = (char)('d' + i);
            using (var initiated = await BeginAsync(http, key, slowBytes))
                Check(initiated.IsSuccessStatusCode, "slow upload session admitted");
            bodies[i] = new SlowBody(slowBytes);
            abort[i] = new CancellationTokenSource();
            var request = new HttpRequestMessage(HttpMethod.Put,
                "uploads/" + new string(key, 64) + "?offset=0") { Content = bodies[i] };
            sends[i] = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                abort[i].Token);
            await bodies[i].Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }

        try
        {
            bool saturated = false;
            for (int tries = 0; tries < 100 && !saturated; tries++)
            {
                using var request = await http.GetAsync("v2/metrics");
                using var doc = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                saturated = doc.RootElement.GetProperty("admission")
                    .GetProperty("uploads").GetProperty("active").GetInt32() == 3;
                if (!saturated) await Task.Delay(50);
            }
            Check(saturated, "three deliberately stalled upload bodies occupy only the transfer lane");
            var stopwatch = Stopwatch.StartNew();
            using (var health = await http.GetAsync("health"))
                Check(health.IsSuccessStatusCode,
                    "health remains available while all transfer workers are stalled");
            using (var browse = await http.GetAsync("v2/maps"))
                Check(browse.IsSuccessStatusCode,
                    "catalog remains available while all transfer workers are stalled");
            Check(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                "metadata responses are not serialized behind long file transfers");

            // Player join/package downloads have their own four-slot lane
            // and must not be rejected because creator uploads are stalled.
            using (var playerDownload = await http.GetAsync("packages/" +
                MapBuildFingerprint.HashFile(Path.Combine(root, "source.ppmap"))))
                Check(playerDownload.IsSuccessStatusCode,
                    "lobby package download remains available during saturated uploads");

            using var fourthUpload = new HttpRequestMessage(HttpMethod.Put,
                "uploads/" + new string('g', 64) + "?offset=0")
            { Content = new ByteArrayContent(new byte[] { 7 }) };
            using var busy = await http.SendAsync(fourthUpload);
            Check(busy.StatusCode == HttpStatusCode.ServiceUnavailable
                && busy.Headers.RetryAfter != null,
                "fourth upload is refused promptly without queueing or starving downloads");
        }
        finally
        {
            foreach (var token in abort) token.Cancel();
            foreach (var body in bodies) body.Release();
            foreach (Task<HttpResponseMessage> task in sends)
            {
                try { using var response = await task.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception ex) when (ex is OperationCanceledException
                    or IOException or HttpRequestException or TimeoutException) { }
            }
            foreach (var token in abort) token.Dispose();
            foreach (var body in bodies) body.Dispose();
        }
    }
    await StopAsync();

    // Opt-in per-peer limits use actual socket peer IPs. No untrusted
    // X-Forwarded-For claims are accepted, and the limit defaults to off
    // for installations behind a shared-IP proxy.
    string? prior = Environment.GetEnvironmentVariable(
        "PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE");
    try
    {
        Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE", "60");
        var started = await StartAsync();
        using var client = started.Client;
        bool denied = false;
        // Crosses at most one fixed-minute rate window in this short local
        // campaign. 136 requests exceed two 60-request windows, so the result
        // is not sensitive to running exactly at a UTC minute boundary.
        for (int i = 0; i < 136; i++)
        {
            using var result = await client.GetAsync("health");
            if (result.StatusCode == HttpStatusCode.TooManyRequests)
            {
                denied = result.Headers.RetryAfter != null;
                break;
            }
            Check(result.IsSuccessStatusCode, "under-limit request accepted");
        }
        Check(denied, "opt-in per-IP rate limiting returns 429 and Retry-After");
        await StopAsync();
    }
    finally
    {
        Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE", prior);
    }

    Console.WriteLine($"Community service preflight/load checks: {checks} PASS.");
}
finally
{
    await StopAsync();
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}


internal sealed class SlowBody(long length) : HttpContent
{
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override bool TryComputeLength(out long size)
    {
        size = length;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream,
        TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 1 }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Started.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
    }

    internal void Release() => _release.TrySetResult();
}
