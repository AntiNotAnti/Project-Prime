using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.MapGen;

// Local transfer/verification stage only. No ZIP, map compiler or GPU work.
int mib = 32, repeat = 5;
string? report = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--mib" && i + 1 < args.Length) mib = int.Parse(args[++i]);
    else if (args[i] == "--repeat" && i + 1 < args.Length) repeat = int.Parse(args[++i]);
    else if (args[i] == "--output" && i + 1 < args.Length) report = Path.GetFullPath(args[++i]);
    else throw new ArgumentException("Usage: map-download-check [--mib 1..128] [--repeat 1..20] [--output FILE]");
}
if (mib is < 1 or > 128 || repeat is < 1 or > 20) throw new ArgumentOutOfRangeException();
string root = Path.Combine(Path.GetTempPath(), "prime-download-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
int port = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();
using var server = new HttpListener();
string address = $"http://localhost:{port}/";
server.Prefixes.Add(address);
server.Start();
byte[] payload = new byte[mib * 1024 * 1024];
new Random(20261021).NextBytes(payload);
string hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
string destination = Path.Combine(root, "download.part");
int requests = 0, mode = 0, checks = 0;
long rangeStart = -1;
void Check(bool value, string message)
{
    if (!value) throw new InvalidDataException(message);
    checks++;
}
Task serving = Task.Run(async () =>
{
    try
    {
        while (server.IsListening)
        {
            var context = await server.GetContextAsync();
            int request = Interlocked.Increment(ref requests);
            long offset = 0;
            string? range = context.Request.Headers["Range"];
            if (range != null) offset = long.Parse(range["bytes=".Length..].TrimEnd('-'));
            rangeStart = offset;
            var response = context.Response;
            response.StatusCode = offset == 0 ? 200 : 206;
            if (offset > 0) response.Headers["Content-Range"] = $"bytes {offset}-{payload.Length - 1}/{payload.Length}";
            response.ContentLength64 = payload.Length - offset;
            try
            {
                // One intentionally corrupt byte, with the original exact length.
                if (mode == 1 || mode == 2 && request == 1)
                {
                    response.OutputStream.WriteByte((byte)(payload[(int)offset] ^ 0x80));
                    offset++;
                }
                while (offset < payload.Length)
                {
                    int length = (int)Math.Min(65536, payload.Length - offset);
                    await response.OutputStream.WriteAsync(payload.AsMemory((int)offset, length));
                    offset += length;
                    if (mode == 3) await Task.Delay(2);
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException) { }
            finally { response.Close(); }
        }
    }
    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
});
try
{
    using var client = new MapCommunityClient(address);
    // Prime JIT and transport before collecting repeated same-machine samples.
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(MapBuildFingerprint.HashFile(destination) == hash, "cold transfer exact hash");
    File.Delete(destination);
    var samples = new List<double>();
    var cpu = new List<double>();
    for (int i = 0; i < repeat; i++)
    {
        var before = Process.GetCurrentProcess().TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        long bytes = await client.DownloadContentAsync(hash, destination, payload.Length, default);
        samples.Add(watch.Elapsed.TotalMilliseconds);
        cpu.Add((Process.GetCurrentProcess().TotalProcessorTime - before).TotalMilliseconds);
        Check(bytes == payload.Length && MapBuildFingerprint.HashFile(destination) == hash, "measured transfer exact hash");
        File.Delete(destination);
    }
    // Existing complete files must be verified even when no HTTP is needed.
    File.WriteAllBytes(destination, payload);
    requests = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(requests == 0, "complete exact file reuses bytes without HTTP");
    // A corrupt persisted prefix is never trusted merely because offsets match.
    File.WriteAllBytes(destination, payload.AsSpan(0, payload.Length / 2).ToArray());
    requests = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(rangeStart == payload.Length / 2 && requests == 1 && MapBuildFingerprint.HashFile(destination) == hash,
        "resume hashes complete prefix and ranged suffix");
    using (var file = File.OpenWrite(destination)) file.WriteByte((byte)(payload[0] ^ 0x80));
    requests = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(requests == 1 && rangeStart == 0 && MapBuildFingerprint.HashFile(destination) == hash,
        "corrupt complete local file redownloads from zero");
    File.WriteAllBytes(destination, payload.AsSpan(0, payload.Length / 2).ToArray());
    using (var file = File.OpenWrite(destination)) file.WriteByte((byte)(payload[0] ^ 0x80));
    requests = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(requests == 2 && rangeStart == 0 && MapBuildFingerprint.HashFile(destination) == hash,
        "corrupt persisted prefix discards and restarts");
    File.Delete(destination);
    mode = 2; requests = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(requests == 2 && MapBuildFingerprint.HashFile(destination) == hash, "corrupt response retries exact hash");
    File.Delete(destination);
    mode = 1; requests = 0;
    bool rejected = false;
    try { await client.DownloadContentAsync(hash, destination, payload.Length, default); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && requests == 2 && !File.Exists(destination), "persistent hash mismatch discards all bytes");
    mode = 3;
    using (var cancel = new CancellationTokenSource())
    {
        bool cancelled = false;
        try
        {
            await client.DownloadContentAsync(hash, destination, payload.Length, cancel.Token,
                (received, total) => { if (received > total / 4) cancel.Cancel(); });
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && File.Exists(destination) && new FileInfo(destination).Length < payload.Length,
            "cancellation leaves bounded partial file");
    }
    mode = 0;
    await client.DownloadContentAsync(hash, destination, payload.Length, default);
    Check(MapBuildFingerprint.HashFile(destination) == hash, "cancelled prefix resumes with exact identity");
    double Percentile(double percent) => samples.Order().ElementAt(Math.Clamp((int)Math.Ceiling(samples.Count * percent) - 1, 0, samples.Count - 1));
    string json = JsonSerializer.Serialize(new
    {
        schemaVersion = 1, fixture = "loopback-streamed-verification", mib, repeat, checks,
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        cpuCount = Environment.ProcessorCount, packageHash = hash,
        elapsedMs = samples, processCpuMs = cpu,
        p50Ms = Percentile(.5), p95Ms = Percentile(.95), p99Ms = Percentile(.99),
        medianMiBPerSecond = mib / (Percentile(.5) / 1000),
        peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
        limitations = "Synthetic loopback transfer stage with warm OS cache. Includes fixture server CPU/memory; excludes ZIP/compiler/GPU. Small sample percentiles are descriptive only."
    }, new JsonSerializerOptions { WriteIndented = true });
    if (report != null) { Directory.CreateDirectory(Path.GetDirectoryName(report)!); File.WriteAllText(report, json + Environment.NewLine); }
    Console.WriteLine(json);
}
finally
{
    server.Stop();
    await serving;
    Directory.Delete(root, true);
}
