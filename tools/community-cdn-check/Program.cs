using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// PR-11 tests are local. No production CDN deployment or remote S3 bucket
// can be claimed from this synthetic staging adapter.
string root = Path.Combine(Path.GetTempPath(), "prime-cdn-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool valid, string label)
{
    if (!valid) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
string? prevEnabled = Environment.GetEnvironmentVariable(
    "PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED");
string? prevBase = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE");
string? prevHosts = Environment.GetEnvironmentVariable("PROJECT_PRIME_TRUSTED_CDN_HOSTS");
using var stop = new CancellationTokenSource();
Task? server = null;
try
{
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED", null);
    Check(CommunityCutoverPolicy.ObjectMirrorEnabled,
        "verified public object mirror is enabled by default when not explicitly disabled");
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED", "off");
    Check(!CommunityCutoverPolicy.ObjectMirrorEnabled,
        "server public object mirror can be rolled back with an explicit off setting");
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED", null);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_TRUSTED_CDN_HOSTS", null);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE",
        "https://cdn.projectprime.example/public/");
    Check(CommunityCutoverPolicy.TrustedCdnHosts.Contains("cdn.projectprime.example"),
        "locally configured HTTPS CDN base automatically trusts only that operator hostname");
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE",
        "http://not-trusted.example");
    Check(!CommunityCutoverPolicy.TrustedCdnHosts.Contains("not-trusted.example"),
        "insecure HTTP CDN base is not silently trusted");
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE", prevBase);

    string serviceRoot = Path.Combine(root, "service");
    Directory.CreateDirectory(serviceRoot);
    var map = MapTemplates.Create("CDN_ARENA", true).Definition;
    string source = MapPackageBuilder.Build(map, Path.Combine(root, "map.ppmap"));
    string hash = MapBuildFingerprint.HashFile(source);
    long bytes = new FileInfo(source).Length;
    var objects = new CommunityObjectStore(serviceRoot,
        "https://cdn.projectprime.example/public/");
    Check(objects.PublicLocation(hash, bytes, published: true) == null,
        "unmirrored archive is never advertised as publicly retrievable");
    Check(objects.MirrorPublic(source, hash, bytes),
        "first object copy is immutable and validated before publication");
    Check(!objects.MirrorPublic(source, hash, bytes),
        "repeated exact map hash does not upload or copy duplicate object bytes");
    Uri? valid = objects.PublicLocation(hash, bytes, published: true);
    Check(valid != null && valid.AbsoluteUri.EndsWith(hash + ".ppmap")
        && objects.PublicLocation(hash, bytes, published: false) == null,
        "only verified public objects have CDN locations");

    var trusted = new CommunityCdnPolicy(new[] { "cdn.projectprime.example" });
    Check(trusted.CanRead(valid, hash)
        && !new CommunityCdnPolicy(new[] { "different.example" }).CanRead(valid, hash),
        "client trust is determined by an explicit local allowlist");
    Check(!trusted.CanRead(new Uri("http://cdn.projectprime.example/" + hash + ".ppmap"), hash)
        && !trusted.CanRead(new Uri("https://cdn.projectprime.example/" + hash
            + ".ppmap?redirect=https://attacker.example"), hash),
        "HTTP, query redirects and untrusted hosts are rejected");

    var distribution = new CommunityPackageDistribution(1, hash, bytes,
        "packages/" + hash, valid!.ToString(), true);
    byte[] oldBytes = File.ReadAllBytes(source);
    string destination = Path.Combine(root, "test-cdn.ppmap");
    using (var fake = new HttpClient(new StaticResponseHandler(oldBytes)))
    {
        bool ok = await CommunityCdnDownloader.TryDownloadAsync(
            distribution, trusted, destination, CancellationToken.None, fake);
        Check(ok && File.ReadAllBytes(destination).SequenceEqual(oldBytes),
            "trusted CDN stream still verifies exact complete archive SHA-256");
    }
    File.Delete(destination);
    using (var fake = new HttpClient(new StaticResponseHandler(
        new byte[oldBytes.Length])))
        Check(!await CommunityCdnDownloader.TryDownloadAsync(
            distribution, trusted, destination, CancellationToken.None, fake)
            && !File.Exists(destination),
            "corrupt mirror response never creates a completed local archive");

    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    string address = $"http://127.0.0.1:{port}/hub/";
    const string ticket = "community-cdn-staging-test-secret";
    // Leave mirror unset: the default-on activation, not an opt-in flag,
    // must initialize verified public mirroring for this service.
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED", null);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE",
        "https://cdn.projectprime.example/public/");
    server = MapCommunityServer.ServeAsync(address, serviceRoot, ticket, stop.Token);
    using var creator = new MapCommunityClient(address, ticket);
    CommunityMap publicMap = await creator.UploadAsync(source, CancellationToken.None);
    var location = await creator.GetMapDistributionAsync(publicMap.Hash,
        CancellationToken.None);
    Check(location?.PackageHash == publicMap.Hash
        && location.PublicCdnUrl == valid?.ToString(),
        "service advertises only the verified published mirror object");

    // Unlike the earlier manual MirrorPublic unit check, this archive is
    // first published through the real server with NO prewritten mirror.
    // Its non-authoritative background copy must eventually become eligible
    // for an exact public CDN location, without blocking the origin publish.
    var freshPublic = MapTemplates.Create("CDN_AUTOMIRROR", true).Definition;
    string freshPackage = MapPackageBuilder.Build(freshPublic,
        Path.Combine(root, "auto-public.ppmap"));
    var freshPublished = await creator.UploadAsync(freshPackage, CancellationToken.None);
    CommunityPackageDistribution? freshDistribution = null;
    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
    {
        while (!timeout.IsCancellationRequested)
        {
            freshDistribution = await creator.GetMapDistributionAsync(
                freshPublished.Hash, timeout.Token);
            if (freshDistribution?.CdnEnabled == true) break;
            await Task.Delay(100, timeout.Token);
        }
    }
    Check(freshDistribution?.CdnEnabled == true
        && freshDistribution.PublicCdnUrl?.EndsWith(
            freshPublished.Hash + ".ppmap", StringComparison.Ordinal) == true,
        "newly published public map is mirrored automatically without changing its origin identity");

    using var client = new MapCommunityClient(address);
    string fallbackPath = Path.Combine(root, "origin-fallback.ppmap");
    await client.DownloadViaTrustedCdnOrOriginAsync(publicMap, fallbackPath,
        new CommunityCdnPolicy(Array.Empty<string>()), CancellationToken.None);
    Check(File.ReadAllBytes(fallbackPath).SequenceEqual(oldBytes),
        "untrusted remote CDN host falls back to legacy authenticated origin bytes");

    var privateMap = MapTemplates.Create("CDN_PRIVATE", true).Definition;
    string draftFile = MapPackageBuilder.Build(privateMap, Path.Combine(root, "draft.ppmap"));
    var draft = await creator.UploadAsync(draftFile, CancellationToken.None, draft: true);
    Check(await client.GetMapDistributionAsync(draft.Hash, CancellationToken.None) == null,
        "draft package cannot leak a globally public CDN object URL");

    var before = objects.Inspect(new[] { (hash, bytes) });
    Check(before.Verified == 1 && before.Corrupted == 0,
        "reconciliation reports existing verified objects without mutating origin");
    Console.WriteLine($"Community CDN/origin acceptance: {checks} PASS.");
}
finally
{
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED", prevEnabled);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE", prevBase);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_TRUSTED_CDN_HOSTS", prevHosts);
    stop.Cancel();
    if (server != null)
    {
        try { await server.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}

internal sealed class StaticResponseHandler(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
}
