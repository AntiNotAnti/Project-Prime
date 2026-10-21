using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Mods.MapGen;

static void Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/pk3-conversion-bench -- --source FILE --room NAME [--map LEVEL] [--dependency FILE]... [--allow-relocated-miss]");
}

static long CopyBounded(string source, string destination, CancellationToken cancellation)
{
    long limit = Path.GetExtension(source).Equals(".bsp", StringComparison.OrdinalIgnoreCase)
        ? Pk3DiscoveryService.MaxEntryBytes : Pk3DiscoveryService.MaxArchiveBytes;
    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
        81920, FileOptions.SequentialScan);
    long expected = input.Length;
    if (expected > limit) throw new InvalidDataException("Benchmark input exceeds the importer size limit: " + source);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        81920, FileOptions.SequentialScan);
    byte[] buffer = new byte[81920];
    long copied = 0;
    int read;
    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
    {
        cancellation.ThrowIfCancellationRequested();
        copied += read;
        if (copied > limit) throw new InvalidDataException("Benchmark input grew beyond the importer size limit: " + source);
        output.Write(buffer, 0, read);
    }
    if (copied != expected) throw new IOException("Benchmark input changed while being copied: " + source);
    return copied;
}

static string[] PrepareJob(string source, IReadOnlyList<string> explicitDependencies,
    IReadOnlyList<string> sourceArchives, string destination, CancellationToken cancellation)
{
    Directory.CreateDirectory(destination);
    string sourceCopy = Path.Combine(destination, Path.GetFileName(source));
    CopyBounded(source, sourceCopy, cancellation);

    StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    string fullSource = Path.GetFullPath(source);
    var explicitSet = new HashSet<string>(explicitDependencies.Select(Path.GetFullPath), pathComparer);
    var dependencyCopies = new Dictionary<string, string>(pathComparer);
    string dependencyDirectory = Path.Combine(destination, "dependencies");
    var names = new HashSet<string>(pathComparer);
    string[] result = new string[explicitDependencies.Count];
    for (int i = 0; i < explicitDependencies.Count; i++)
    {
        cancellation.ThrowIfCancellationRequested();
        string original = Path.GetFullPath(explicitDependencies[i]);
        if (pathComparer.Equals(original, fullSource))
        {
            result[i] = sourceCopy;
            continue;
        }
        if (!dependencyCopies.TryGetValue(original, out string? copy))
        {
            string fileName = Path.GetFileName(original);
            if (!names.Add(fileName))
                fileName = $"dependency-{i:D4}-{fileName}";
            copy = Path.Combine(dependencyDirectory, fileName);
            CopyBounded(original, copy, cancellation);
            dependencyCopies.Add(original, copy);
        }
        result[i] = copy;
    }

    foreach (string archive in sourceArchives)
    {
        cancellation.ThrowIfCancellationRequested();
        string original = Path.GetFullPath(archive);
        if (pathComparer.Equals(original, fullSource) || explicitSet.Contains(original)) continue;
        string siblingCopy = Path.Combine(destination, Path.GetFileName(original));
        if (!File.Exists(siblingCopy)) CopyBounded(original, siblingCopy, cancellation);
    }
    return result;
}

static (double Milliseconds, long AllocatedBytes, long RetainedBytes) MeasureParse(
    string source, string map, CancellationToken cancellation)
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    long baseline = GC.GetTotalMemory(forceFullCollection: true);
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    Q3Bsp parsed = Q3Bsp.Load(source, map, cancellation);
    timer.Stop();
    long retained = Math.Max(0, GC.GetTotalMemory(forceFullCollection: true) - baseline);
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    GC.KeepAlive(parsed);
    return (timer.Elapsed.TotalMilliseconds, allocatedBytes, retained);
}

var dependencies = new List<string>();
string? source = null;
string? map = null;
string? room = null;
bool allowRelocatedMiss = false;
for (int i = 0; i < args.Length; i++)
{
    string value = args[i];
    if (value == "--source" && i + 1 < args.Length) source = args[++i];
    else if (value == "--map" && i + 1 < args.Length) map = args[++i];
    else if (value == "--room" && i + 1 < args.Length) room = args[++i];
    else if (value == "--dependency" && i + 1 < args.Length) dependencies.Add(args[++i]);
    else if (value == "--allow-relocated-miss") allowRelocatedMiss = true;
    else { Usage(); return 2; }
}
if (String.IsNullOrWhiteSpace(source) || String.IsNullOrWhiteSpace(room) || !File.Exists(source))
{
    Usage();
    return 2;
}

source = Path.GetFullPath(source);
dependencies = dependencies.Select(path => Path.GetFullPath(path)).ToList();
if (dependencies.Any(path => !File.Exists(path))) throw new FileNotFoundException("A dependency archive was not found.");
map ??= Q3Bsp.ListMaps(source).FirstOrDefault();
if (String.IsNullOrWhiteSpace(map)) throw new InvalidDataException("Source contains no BSP levels.");
IReadOnlyList<string> sourceArchives = MapTextureBake.DiscoverArchives(source, dependencies);
var parse = MeasureParse(source, map, CancellationToken.None);
string root = Path.Combine(Path.GetTempPath(), "prime-q3-bench-" + Guid.NewGuid().ToString("N"));
string? previousCache = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE");
string cacheRoot = Path.Combine(root, "conversion-cache");
PropertyInfo? cacheOverride = typeof(Pk3DiscoveryService).GetProperty("CacheDirectoryOverride",
    BindingFlags.Static | BindingFlags.NonPublic);
object? previousCacheOverride = cacheOverride?.GetValue(null);
using var cancellationSource = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};
Console.CancelKeyPress += cancel;
try
{
    Directory.CreateDirectory(root);
    Environment.SetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE", cacheRoot);
    cacheOverride?.SetValue(null, cacheRoot);
    string firstDirectory = Path.Combine(root, "job-a");
    string secondDirectory = Path.Combine(root, "job-b");
    string[] firstDependencies = PrepareJob(source, dependencies, sourceArchives, firstDirectory, cancellationSource.Token);
    string[] secondDependencies = PrepareJob(source, dependencies, sourceArchives, secondDirectory, cancellationSource.Token);
    string firstSource = Path.Combine(firstDirectory, Path.GetFileName(source));
    string secondSource = Path.Combine(secondDirectory, Path.GetFileName(source));
    string firstDestination = Path.Combine(root, "converted-a");
    string secondDestination = Path.Combine(root, "converted-b");
    var firstOptions = new Q3ImportService.Options(firstSource, map, room!, firstDestination, Dependencies: firstDependencies);
    var secondOptions = new Q3ImportService.Options(secondSource, map, room!, secondDestination, Dependencies: secondDependencies);
    IReadOnlyList<string> firstArchives = MapTextureBake.DiscoverArchives(firstSource, firstDependencies);
    IReadOnlyList<string> secondArchives = MapTextureBake.DiscoverArchives(secondSource, secondDependencies);

    string firstWarmupKey = Pk3DiscoveryService.Fingerprint(firstOptions, map, firstArchives, cancellationSource.Token);
    string secondWarmupKey = Pk3DiscoveryService.Fingerprint(secondOptions, map, secondArchives, cancellationSource.Token);
    if (!allowRelocatedMiss && firstWarmupKey != secondWarmupKey)
        throw new InvalidOperationException("Relocated input copies did not produce the same content key.");

    long firstAllocated = GC.GetAllocatedBytesForCurrentThread();
    var firstHashTimer = Stopwatch.StartNew();
    string firstKey = Pk3DiscoveryService.Fingerprint(firstOptions, map, firstArchives, cancellationSource.Token);
    firstHashTimer.Stop();
    long firstHashAllocated = GC.GetAllocatedBytesForCurrentThread() - firstAllocated;
    long secondAllocated = GC.GetAllocatedBytesForCurrentThread();
    var secondHashTimer = Stopwatch.StartNew();
    string secondKey = Pk3DiscoveryService.Fingerprint(secondOptions, map, secondArchives, cancellationSource.Token);
    secondHashTimer.Stop();
    long secondHashAllocated = GC.GetAllocatedBytesForCurrentThread() - secondAllocated;
    bool sameKey = firstKey == secondKey;
    if (!allowRelocatedMiss && !sameKey)
        throw new InvalidOperationException("Relocated input copies did not produce the same content key.");

    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    long coldAllocated = GC.GetAllocatedBytesForCurrentThread();
    var coldTimer = Stopwatch.StartNew();
    Q3ImportService.Result cold = Q3ImportService.Import(firstOptions, cancellationSource.Token);
    coldTimer.Stop();
    coldAllocated = GC.GetAllocatedBytesForCurrentThread() - coldAllocated;
    if (!cold.Succeeded) throw new InvalidOperationException("Cold conversion failed: " + String.Join("; ", cold.Diagnostics.Select(d => d.Message)));

    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    long warmAllocated = GC.GetAllocatedBytesForCurrentThread();
    var warmTimer = Stopwatch.StartNew();
    Q3ImportService.Result warm = Q3ImportService.Import(secondOptions, cancellationSource.Token);
    warmTimer.Stop();
    warmAllocated = GC.GetAllocatedBytesForCurrentThread() - warmAllocated;
    bool coldHit = cold.Diagnostics.Any(d => d.Message == "Q3 conversion cache hit.");
    bool warmHit = warm.Diagnostics.Any(d => d.Message == "Q3 conversion cache hit.");
    if (!warm.Succeeded)
        throw new InvalidOperationException("Second import failed: "
            + String.Join("; ", warm.Diagnostics.Select(d => d.Message)));
    if (!allowRelocatedMiss && !warmHit)
        throw new InvalidOperationException("Relocated warm import did not reuse the conversion artifact: "
            + String.Join("; ", warm.Diagnostics.Select(d => d.Message)));

    StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    long inputBytes = sourceArchives.Distinct(pathComparer).Sum(path => new FileInfo(path).Length);
    Console.WriteLine("Q3 conversion benchmark: one import and one identical import from a relocated job directory");
    Console.WriteLine($"Source and dependency files: {sourceArchives.Distinct(pathComparer).Count()}; {inputBytes:N0} bytes");
    Console.WriteLine($"BSP parse: {parse.Milliseconds:0.00} ms; {parse.AllocatedBytes:N0} allocated bytes; {parse.RetainedBytes:N0} retained managed bytes");
    Console.WriteLine($"Fingerprint A after warmup: {firstHashTimer.Elapsed.TotalMilliseconds:0.00} ms; {firstHashAllocated:N0} allocated bytes");
    Console.WriteLine($"Fingerprint B relocated after warmup: {secondHashTimer.Elapsed.TotalMilliseconds:0.00} ms; {secondHashAllocated:N0} allocated bytes; same key={sameKey}");
    Console.WriteLine($"Import A: {coldTimer.Elapsed.TotalMilliseconds:0.00} ms; {coldAllocated:N0} allocated bytes; cache hit={coldHit}");
    Console.WriteLine($"Import B relocated: {warmTimer.Elapsed.TotalMilliseconds:0.00} ms; {warmAllocated:N0} allocated bytes; cache hit={warmHit}");
}
finally
{
    Console.CancelKeyPress -= cancel;
    Environment.SetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE", previousCache);
    cacheOverride?.SetValue(null, previousCacheOverride);
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

return 0;
