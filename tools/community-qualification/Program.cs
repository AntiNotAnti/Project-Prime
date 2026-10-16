using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// CH-07: an original-algorithm versus current-algorithm benchmark on the same
// runtime, with deterministic inputs and equality checks. It does NOT claim
// to measure Android GPU, host matchmaking Ready, or internet CDN throughput.
string? outputPath = null;
int repetitions = 11;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
        outputPath = Path.GetFullPath(args[++i]);
    else if (args[i] == "--iterations" && i + 1 < args.Length
        && int.TryParse(args[++i], out int parsed) && parsed is >= 5 and <= 21)
        repetitions = parsed;
    else
        throw new ArgumentException(
            "Usage: community-qualification [--output FILE] [--iterations 5..21]");
}

var root = Path.Combine(Path.GetTempPath(),
    "prime-community-qualification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string title)
{
    if (!value) throw new InvalidDataException(title);
    checks++;
    Console.WriteLine("PASS " + title);
}
static Guid DeterministicId(int i)
{
    Span<byte> bytes = stackalloc byte[16];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, i + 1);
    BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 20261016);
    return new Guid(bytes);
}
static double Percentile(double[] input, double p)
{
    double[] sorted = input.OrderBy(x => x).ToArray();
    int index = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
    return Math.Round(sorted[index], 4);
}
static (double Milliseconds, long AllocatedBytes, long Fingerprint) Measure(Func<long> work)
{
    long start = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    long result = work();
    timer.Stop();
    return (timer.Elapsed.TotalMilliseconds,
        GC.GetAllocatedBytesForCurrentThread() - start, result);
}
var scenarios = new System.Collections.Generic.List<object>();
try
{
    foreach (var (mapCount, favoriteCount) in new[]
    {
        (100, 2000), (500, 5000), (2000, 10000)
    })
    {
        string folder = Path.Combine(root, "catalog-" + mapCount);
        Directory.CreateDirectory(folder);
        Guid[] mapIds = Enumerable.Range(0, mapCount)
            .Select(DeterministicId).ToArray();
        CommunityMap[] maps = mapIds.Select((id, i) => new CommunityMap(
            new string('a', 64), id, new string('b', 64),
            "QUAL_" + i, "Qualification Map " + i, "Benchmark Creator",
            "1", 1024)).ToArray();
        MapFavorite[] favorites = Enumerable.Range(0, favoriteCount)
            .Select(i => new MapFavorite("user-" + (i / mapCount),
                mapIds[i % mapCount], DateTimeOffset.UnixEpoch)).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "map_favorites.json"),
            JsonSerializer.SerializeToUtf8Bytes(favorites, MapPackageReader.JsonOptions));
        var catalog = new MapCreatorCatalog(folder,
            "community-qualification-fixture-secret-2026");

        long OldAlgorithm()
        {
            long checksum = 0;
            foreach (CommunityMap map in maps)
            {
                // Original implementation: two linear scans per map row.
                int total = favorites.Count(x => x.MapId == map.MapId);
                bool mine = favorites.Any(x =>
                    x.MapId == map.MapId && x.UserId == "user-0");
                checksum += total * 13L + (mine ? 1 : 0);
            }
            return checksum;
        }

        long NewAlgorithm()
        {
            long checksum = 0;
            foreach (CommunityMap map in maps)
            {
                CommunityMap found = catalog.Decorate(map, "user-0");
                checksum += found.FavoriteCount * 13L + (found.Favorited ? 1 : 0);
            }
            return checksum;
        }

        // Warm both paths before timing, then alternate order to avoid giving
        // either algorithm sole ownership of the warmed caches.
        Check(OldAlgorithm() == NewAlgorithm(),
            "favorite presentation equality at " + mapCount + " maps");
        for (int i = 0; i < 3; i++)
        {
            OldAlgorithm();
            NewAlgorithm();
        }
        double[] oldMs = new double[repetitions];
        double[] newMs = new double[repetitions];
        long[] oldAlloc = new long[repetitions];
        long[] newAlloc = new long[repetitions];
        for (int i = 0; i < repetitions; i++)
        {
            var first = (i & 1) == 0
                ? Measure(OldAlgorithm) : Measure(NewAlgorithm);
            var second = (i & 1) == 0
                ? Measure(NewAlgorithm) : Measure(OldAlgorithm);
            var before = (i & 1) == 0 ? first : second;
            var after = (i & 1) == 0 ? second : first;
            Check(before.Fingerprint == after.Fingerprint,
                "favorite count/membership parity, run " + i + ", maps " + mapCount);
            oldMs[i] = before.Milliseconds;
            newMs[i] = after.Milliseconds;
            oldAlloc[i] = before.AllocatedBytes;
            newAlloc[i] = after.AllocatedBytes;
        }
        scenarios.Add(new
        {
            maps = mapCount, favoriteRows = favoriteCount,
            comparison = "old linear scans vs current indexed decoration",
            original = new
            {
                p50Ms = Percentile(oldMs, .5),
                p95Ms = Percentile(oldMs, .95),
                allocatedBytes = oldAlloc
            },
            optimized = new
            {
                p50Ms = Percentile(newMs, .5),
                p95Ms = Percentile(newMs, .95),
                allocatedBytes = newAlloc
            }
        });
    }

    // Real, local Community HttpListener transaction, not a mock response.
    using var socket = new TcpListener(IPAddress.Loopback, 0);
    socket.Start();
    int port = ((IPEndPoint)socket.LocalEndpoint).Port;
    socket.Stop();
    string endpoint = "http://127.0.0.1:" + port + "/qualification/";
    const string ticket = "community-qualification-origin-secret-2026";
    var map = MapTemplates.Create("QUALIFICATION_ARENA", true).Definition;
    string package = MapPackageBuilder.Build(
        map, Path.Combine(root, "qualification.ppmap"));
    string hash = MapBuildFingerprint.HashFile(package);
    using var cancel = new CancellationTokenSource();
    Task service = MapCommunityServer.ServeAsync(endpoint,
        Path.Combine(root, "service"), ticket, cancel.Token);
    double[] catalogLatency;
    double[] packageLatency;
    try
    {
        using var publisher = new MapCommunityClient(endpoint, ticket);
        CommunityMap published = await publisher.UploadAsync(
            package, CancellationToken.None);
        Check(published.Hash == hash, "loopback publication preserved exact package SHA-256");

        using var http = new HttpClient { BaseAddress = new Uri(endpoint),
            Timeout = TimeSpan.FromSeconds(20) };
        async Task<double> RequestAsync(string relative, bool binary)
        {
            var timer = Stopwatch.StartNew();
            using HttpResponseMessage response = await http.GetAsync(
                relative, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            byte[] data = await response.Content.ReadAsByteArrayAsync();
            timer.Stop();
            if (binary)
            {
                string received = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
                Check(received == hash, "loopback exact package response hash");
            }
            else
                Check(JsonSerializer.Deserialize<CommunityMapProject[]>(
                    data, MapPackageReader.JsonOptions)?.Length == 1,
                    "loopback catalog returns one published project");
            return timer.Elapsed.TotalMilliseconds;
        }
        // Avoid counting runtime/JIT warmup against one particular sample.
        await RequestAsync("v2/maps?page=1&pageSize=24", false);
        await RequestAsync("packages/" + hash, true);
        catalogLatency = new double[repetitions];
        packageLatency = new double[repetitions];
        for (int i = 0; i < repetitions; i++)
        {
            catalogLatency[i] = await RequestAsync(
                "v2/maps?page=1&pageSize=24", false);
            packageLatency[i] = await RequestAsync(
                "packages/" + hash, true);
        }
    }
    finally
    {
        cancel.Cancel();
        try { await service; }
        catch (OperationCanceledException) { }
    }

    var report = new
    {
        schema = 1,
        suite = "community-qualification",
        timestampUtc = DateTimeOffset.UtcNow,
        runtime = RuntimeInformation.FrameworkDescription,
        operatingSystem = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.OSArchitecture.ToString(),
        repetitions, checks,
        favoriteIndex = scenarios,
        loopback = new
        {
            catalog = new { p50Ms = Percentile(catalogLatency, .5),
                p95Ms = Percentile(catalogLatency, .95), samplesMs = catalogLatency },
            immutablePackage = new { p50Ms = Percentile(packageLatency, .5),
                p95Ms = Percentile(packageLatency, .95), samplesMs = packageLatency },
            packageHash = hash,
            packageBytes = new FileInfo(package).Length
        },
        caveats = new[]
        {
            "Synthetic favorite-list input runs original algorithm and new implementation in one process.",
            "Loopback is not a production CDN, WAN, or large-map live server load test.",
            "No device FPS/frametime, Android low-storage, Netcode Ready, or real replay playback claim.",
            "Relative throughput and memory are machine-specific; preserve raw samples and compare matched runners."
        }
    };
    string json = JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outputPath != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + Environment.NewLine);
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}
