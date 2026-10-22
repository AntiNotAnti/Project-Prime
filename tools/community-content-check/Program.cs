using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

// Real authenticated typed asset HTTP campaign. Never uses gameplay assets.
string root = Path.Combine(Path.GetTempPath(),
    "prime-typed-publish-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var stop = new CancellationTokenSource();
Task? running = null;
int checks = 0;
void Check(bool pass, string description)
{
    if (!pass) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}
try
{
    using var portProbe = new TcpListener(IPAddress.Loopback, 0);
    portProbe.Start();
    int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();
    string address = $"http://127.0.0.1:{port}/community/";
    const string ownerToken = "typed-community-publish-fixture-admin-2026";
    running = MapCommunityServer.ServeAsync(address, Path.Combine(root, "server"),
        ownerToken, stop.Token);
    using var creator = new MapCommunityClient(address, ownerToken);
    using var publicClient = new MapCommunityClient(address);
    string image = Path.Combine(root, "fixture.png");
    byte[] png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aegAAAABJRU5ErkJggg==");
    File.WriteAllBytes(image, png);
    Guid id = Guid.Parse("0fa17589-9c7a-4caf-87e4-6dd5db2cb351");
    var record = new CommunityContentRegistration("texture", id,
        MapCreatorCatalog.ServiceOwner, "Author texture", "Texture A",
        "Fixture", "Original tiny image", new[] { "hd" }, "image/png",
        CommunityContentVisibility.Published);
    var published = await creator.PublishContentAsync(image, record,
        CancellationToken.None);
    string hash = MapBuildFingerprint.HashFile(image);
    Check(published.LatestHash == hash
        && published.RevisionCount == 1
        && published.CurrentRevision?.ContentHash == hash,
        "typed PNG is authenticated, content-addressed and published");

    using (var anonymous = new HttpClient { BaseAddress = new Uri(address) })
    using (var rejected = await anonymous.PostAsync(
        "v3/content/texture/" + id + "/revisions",
        new ByteArrayContent(png)))
        Check(rejected.StatusCode == HttpStatusCode.Unauthorized,
            "uncredentialed binary publishing is prohibited");

    string download = Path.Combine(root, "content.part");
    long length = await publicClient.DownloadContentAsync(hash, download,
        png.Length, CancellationToken.None);
    Check(length == png.Length && File.ReadAllBytes(download).SequenceEqual(png),
        "public raw typed object streams exact SHA-256 bytes");

    byte[] changed = png.Concat(new byte[] { 1 }).ToArray();
    File.WriteAllBytes(image, changed);
    var next = await creator.PublishContentAsync(image, record with
    {
        Visibility = CommunityContentVisibility.Draft,
        ExpectedParentHash = hash
    }, CancellationToken.None);
    string draftHash = MapBuildFingerprint.HashFile(image);
    Check(next.LatestHash == draftHash && next.CurrentHash == hash,
        "private revision keeps previous published content pointer");
    var visitor = await publicClient.GetContentAsync("texture", id,
        CancellationToken.None);
    Check(visitor?.CurrentHash == hash && visitor.LatestHash == hash,
        "anonymous catalog cannot enumerate private typed revisions");

    using (var anonymous = new HttpClient { BaseAddress = new Uri(address) })
    using (var missing = await anonymous.GetAsync("v3/objects/" + draftHash))
        Check(missing.StatusCode == HttpStatusCode.NotFound,
            "draft raw object bytes cannot leak to the anonymous public");
    string draft = Path.Combine(root, "private.part");
    await creator.DownloadContentAsync(draftHash, draft,
        changed.Length, CancellationToken.None);
    Check(File.ReadAllBytes(draft).SequenceEqual(changed),
        "authorized creator can download exact private draft bytes");

    await creator.ArchiveContentAsync("texture", id, restore: false,
        CancellationToken.None);
    Check(await publicClient.GetContentAsync("texture", id,
        CancellationToken.None) == null,
        "archived typed content disappears from public catalog");
    await creator.ArchiveContentAsync("texture", id, restore: true,
        CancellationToken.None);
    Check((await publicClient.GetContentAsync("texture", id,
        CancellationToken.None))?.CurrentHash == hash,
        "restoring metadata never changes original immutable revision identity");

    // An older published content revision remains a valid public immutable
    // hash even after a newer published revision becomes current. Historical
    // presentation references must not break when authors publish updates.
    byte[] releasedBytes = png.Concat(new byte[] { 2 }).ToArray();
    File.WriteAllBytes(image, releasedBytes);
    var released = await creator.PublishContentAsync(image, record with
    {
        Visibility = CommunityContentVisibility.Published,
        ExpectedParentHash = draftHash
    }, CancellationToken.None);
    Check(released.RevisionCount == 3 && released.CurrentHash != hash,
        "later public revision advances current without changing historical blob identity");
    string older = Path.Combine(root, "older-published.part");
    await publicClient.DownloadContentAsync(hash, older, png.LongLength,
        CancellationToken.None);
    Check(File.ReadAllBytes(older).SequenceEqual(png),
        "older publicly listed immutable revision stays downloadable by its exact hash");

    string corrupt = Path.Combine(root, "not-a-png.png");
    File.WriteAllText(corrupt, "not a PNG file");
    bool refused = false;
    try
    {
        await creator.PublishContentAsync(corrupt, record with
        {
            ContentId = Guid.NewGuid(), ExpectedParentHash = null
        }, CancellationToken.None);
    }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "sandbox refuses invalid image signature before transmission");

    string malicious = Path.Combine(root, "active-material.json");
    File.WriteAllText(malicious, "{\"script\":\"fetch('https://bad.example')\"}");
    refused = false;
    try
    {
        await creator.PublishContentAsync(malicious, new CommunityContentRegistration(
            "material", Guid.NewGuid(), MapCreatorCatalog.ServiceOwner,
            "Unsafe", null, null, null, Array.Empty<string>(),
            "application/vnd.projectprime.material+json",
            CommunityContentVisibility.Published), CancellationToken.None);
    }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "executable descriptor fields are never accepted");

    string material = Path.Combine(root, "material.json");
    File.WriteAllText(material, "{\"name\":\"safe\",\"roughness\":0.6}");
    var materialItem = await creator.PublishContentAsync(material,
        new CommunityContentRegistration("material", Guid.NewGuid(),
            MapCreatorCatalog.ServiceOwner, "Safe material", null, "Fixture",
            null, Array.Empty<string>(), "application/vnd.projectprime.material+json",
            CommunityContentVisibility.Published), CancellationToken.None);
    Check(materialItem.Kind == "material"
        && materialItem.CurrentHash == MapBuildFingerprint.HashFile(material),
        "second original type, material descriptor, publishes with pinned identity");
    // The v3 pool has independent service/creator disk budgets. Sharing an
    // exact SHA across multiple projects reuses the same immutable object,
    // while a new distinct blob cannot exceed the configured quota.
    string quotaRoot = Path.Combine(root, "quota-store");
    var quota = new CommunityContentRegistry(quotaRoot,
        maximumStoredBytes: png.LongLength, maximumOwnerBytes: png.LongLength);
    var quotaItem = record with { ContentId = Guid.NewGuid(),
        ExpectedParentHash = null, Visibility = CommunityContentVisibility.Published };
    quota.RegisterVerifiedBlob(older, quotaItem);
    quota.RegisterVerifiedBlob(older, quotaItem with { ContentId = Guid.NewGuid() });
    Check(quota.ListVisible(MapCreatorCatalog.ServiceOwner).Length == 2,
        "same SHA is deduplicated across creator projects within bounded storage budget");
    bool quotaDenied = false;
    try
    {
        quota.RegisterVerifiedBlob(image, quotaItem with { ContentId = Guid.NewGuid() });
    }
    catch (InvalidDataException) { quotaDenied = true; }
    Check(quotaDenied,
        "new distinct asset exceeding creator or global bytes is refused before metadata publish");
    var recoveredQuota = new CommunityContentRegistry(quotaRoot,
        maximumStoredBytes: png.LongLength, maximumOwnerBytes: png.LongLength);
    Check(recoveredQuota.ListVisible(MapCreatorCatalog.ServiceOwner).Length == 2,
        "v3 content quota accounting recovers exact bytes and references after restart");

    // All creator upload slots can be busy without taking slots from the
    // lightweight catalog and published raw-download lanes. This is a real
    // HttpListener test with deliberately stalled request bodies.
    using (var http = new HttpClient { BaseAddress = new Uri(address),
        Timeout = TimeSpan.FromSeconds(25) })
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerToken);
        var bodies = new StalledAssetBody[3];
        var requests = new HttpRequestMessage[3];
        var runningUploads = new Task<HttpResponseMessage>[3];
        var cancellation = new CancellationTokenSource[3];
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var stalled = record with
                {
                    ContentId = Guid.NewGuid(),
                    Visibility = CommunityContentVisibility.Published,
                    ExpectedParentHash = null
                };
                bodies[i] = new StalledAssetBody(4096);
                requests[i] = new HttpRequestMessage(HttpMethod.Post,
                    "v3/content/texture/" + stalled.ContentId + "/revisions")
                { Content = bodies[i] };
                requests[i].Headers.TryAddWithoutValidation("X-Prime-Content-Metadata",
                    Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(stalled,
                        MapPackageReader.JsonOptions)));
                requests[i].Headers.TryAddWithoutValidation("X-Prime-SHA256",
                    new string('a', 64));
                bodies[i].Headers.ContentType = new MediaTypeHeaderValue("image/png");
                cancellation[i] = new CancellationTokenSource();
                runningUploads[i] = http.SendAsync(requests[i],
                    HttpCompletionOption.ResponseHeadersRead, cancellation[i].Token);
                await bodies[i].Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
            }
            bool saturated = false;
            for (int tries = 0; tries < 80 && !saturated; tries++)
            {
                using var response = await http.GetAsync("v2/metrics");
                response.EnsureSuccessStatusCode();
                using var metrics = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                saturated = metrics.RootElement.GetProperty("admission")
                    .GetProperty("uploads").GetProperty("active").GetInt32() == 3;
                if (!saturated) await Task.Delay(50);
            }
            Check(saturated,
                "three slow v3 asset bodies consume creator upload slots, not catalog capacity");
            using (var browse = await http.GetAsync("v3/content"))
                Check(browse.IsSuccessStatusCode,
                    "typed catalog stays available during saturated v3 asset uploads");
            using (var read = await http.GetAsync("v3/objects/" + hash))
                Check(read.IsSuccessStatusCode
                    && (await read.Content.ReadAsByteArrayAsync()).SequenceEqual(png),
                    "player-facing immutable asset download remains available during creator uploads");

            var extra = record with { ContentId = Guid.NewGuid() };
            using var fourth = new HttpRequestMessage(HttpMethod.Post,
                "v3/content/texture/" + extra.ContentId + "/revisions")
            { Content = new ByteArrayContent(png) };
            fourth.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            fourth.Headers.TryAddWithoutValidation("X-Prime-Content-Metadata",
                Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(extra,
                    MapPackageReader.JsonOptions)));
            fourth.Headers.TryAddWithoutValidation("X-Prime-SHA256",
                MapBuildFingerprint.HashFile(Path.Combine(root, "older-published.part")));
            using var busy = await http.SendAsync(fourth);
            Check(busy.StatusCode == HttpStatusCode.ServiceUnavailable
                && busy.Headers.RetryAfter != null,
                "fourth v3 upload is refused promptly with Retry-After, without starving browsing");
        }
        finally
        {
            foreach (var token in cancellation) token?.Cancel();
            foreach (var body in bodies) body?.Release();
            foreach (var upload in runningUploads)
                if (upload != null)
                {
                    try { using var response = await upload.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException
                        or TimeoutException or IOException) { }
                }
            foreach (var token in cancellation) token?.Dispose();
            foreach (var req in requests) req?.Dispose();
            foreach (var body in bodies) body?.Dispose();
        }
    }

    Console.WriteLine($"Community typed publishing checks: {checks} PASS.");
}
finally
{
    stop.Cancel();
    if (running != null)
    {
        try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}

internal sealed class StalledAssetBody(long bytes) : HttpContent
{
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override bool TryComputeLength(out long length)
    {
        length = bytes;
        return true;
    }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream,
        TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 137 }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Started.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
    }
    internal void Release() => _release.TrySetResult();
}
