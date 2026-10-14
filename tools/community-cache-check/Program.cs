using System;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// Real process witness: the OS must enforce the archive read lease between
// two independently launched CLR processes, not just between C# objects.
if (args.Length > 0 && args[0] == "--hold-cache-lease")
{
    if (args.Length != 8) throw new ArgumentException("Invalid lease witness arguments.");
    var identity = new MapContentIdentity(Guid.Parse(args[2]), args[3],
        MapHash256.Parse(args[4]), MapHash256.Parse(args[5]), true);
    using var lease = new CommunityArchiveCache(args[1]).TryAcquire(identity)
        ?? throw new InvalidDataException("Cache lease witness could not verify exact archive.");
    File.WriteAllText(args[6], "ready");
    var timeout = Stopwatch.StartNew();
    while (!File.Exists(args[7]))
    {
        if (timeout.Elapsed > TimeSpan.FromSeconds(45))
            throw new TimeoutException("Parent did not release cache lease witness.");
        await Task.Delay(50);
    }
    return;
}

// Asset-free cache acceptance. Uses real .ppmap construction and the exact
// strict archive reader, without modifying installed or gameplay map files.
string root = Path.Combine(Path.GetTempPath(),
    "prime-community-cas-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool valid, string title)
{
    if (!valid) throw new InvalidOperationException(title);
    Console.WriteLine("PASS " + title);
    checks++;
}
try
{
    string first = MapPackageBuilder.Build(
        MapTemplates.Create("CAS_ARENA_A", true).Definition,
        Path.Combine(root, "source-a.ppmap"));
    string second = MapPackageBuilder.Build(
        MapTemplates.Create("CAS_ARENA_B", true).Definition,
        Path.Combine(root, "source-b.ppmap"));
    MapContentIdentity a = MapContentIdentity.FromPackage(first);
    MapContentIdentity b = MapContentIdentity.FromPackage(second);
    string store = Path.Combine(root, "cache");
    long aBytes = new FileInfo(first).Length, bBytes = new FileInfo(second).Length;
    long max = Math.Max(aBytes, bBytes);
    var cache = new CommunityArchiveCache(store, max);
    var secondProcess = new CommunityArchiveCache(store, max);

    cache.Promote(first, a);
    using (var held = cache.TryAcquire(a))
    {
        Check(held != null && held.Identity.Matches(a),
            "a verified archive can be acquired with an exact read lease");
        Check(File.ReadAllBytes(held!.Path).SequenceEqual(File.ReadAllBytes(first)),
            "cached archive retains byte-for-byte source identity");

        cache.Promote(second, b);
        (long before, long after, int deleted) = secondProcess.Prune();
        Check(before > max && after <= max && deleted == 1,
            "bounded LRU removes one unleased archive under disk pressure");
        using var secondReader = cache.TryAcquire(a);
        Check(cache.TryAcquire(b) == null && secondReader != null,
            "LRU preserves a leased older archive and removes an unleased newer one");
    }

    cache.Promote(second, b);
    using (var protectB = cache.TryAcquire(b))
    {
        Check(protectB != null, "evicted exact archive can be safely re-imported");
        secondProcess.Prune();
        using var confirmed = secondProcess.TryAcquire(b);
        Check(secondProcess.TryAcquire(a) == null && confirmed != null,
            "restarted cache instance honors live leases during eviction");
    }

    string cachedB = Path.Combine(store, "archives", b.PackageHash + ".ppmap");
    File.WriteAllBytes(cachedB, new byte[] { 0, 1, 2, 3 });
    Check(cache.TryAcquire(b) == null,
        "corrupted local object never enters the verified archive API");
    cache.Promote(second, b);
    using (var restored = cache.TryAcquire(b))
        Check(restored != null && restored.Identity.Matches(b),
            "corrupt cache object is quarantined and repaired from exact bytes");
    Check(Directory.GetFiles(Path.Combine(store, "quarantine"), "*.corrupt").Length == 1,
        "damaged historical cache bytes are quarantined, not used for gameplay");

    try
    {
        cache.Promote(first, b);
        throw new Exception("Promoted a mismatching source.");
    }
    catch (InvalidDataException) { checks++; Console.WriteLine("PASS staging identity mismatch rejected"); }

    using (var heldB = cache.TryAcquire(b))
    {
        try
        {
            cache.Promote(second, b);
            // Idempotent same-object promotion is permitted only when the
            // exclusive lock can be obtained; another live reader must not be
            // invalidated under that lock.
            throw new Exception("Promoted while another reader held the object.");
        }
        catch (IOException) { checks++; Console.WriteLine("PASS promotion respects read lease"); }
    }

    // Exercise the lock with a distinct process. A normal FileStream lease
    // must survive a simultaneous prune from the parent process.
    cache.Promote(first, a);
    cache.Promote(second, b);
    string ready = Path.Combine(root, "lease-child-ready");
    string release = Path.Combine(root, "lease-child-release");
    string processPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot locate cache acceptance executable.");
    var start = new ProcessStartInfo(processPath)
    {
        UseShellExecute = false,
        RedirectStandardError = true,
        WorkingDirectory = AppContext.BaseDirectory
    };
    if (Path.GetFileNameWithoutExtension(processPath).Equals(
        "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string argument in new[]
    {
        "--hold-cache-lease", store, a.MapId.ToString(),
        a.RoomKey, a.ContentHash.ToString(), a.PackageHash.ToString(),
        ready, release
    }) start.ArgumentList.Add(argument);
    using (Process child = Process.Start(start)
        ?? throw new IOException("Could not launch independent cache lease witness."))
    {
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (child.HasExited)
                    throw new IOException("Cache witness exited early: "
                        + await child.StandardError.ReadToEndAsync());
                if (deadline.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("Cache witness did not acquire its archive.");
                await Task.Delay(50);
            }
            var result = secondProcess.Prune();
            Check(result.AfterBytes <= max && secondProcess.TryAcquire(b) == null
                && File.Exists(Path.Combine(store, "archives", a.PackageHash + ".ppmap")),
                "real child-process read lease prevents parent-process LRU eviction");
        }
        finally
        {
            File.WriteAllText(release, "release");
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                throw;
            }
        }
        Check(child.ExitCode == 0, "independent lease witness cleanly releases its OS handle");
    }

    using var firstGate = await cache.AcquireDownloadGateAsync(a.PackageHash.ToString());
    cache.Promote(second, b);
    secondProcess.Prune();
    using (var heldDuringDownload = secondProcess.TryAcquire(a))
        Check(heldDuringDownload != null && secondProcess.TryAcquire(b) == null,
            "LRU preserves an archive while another process owns its download gate");

    Task<CommunityArchiveCache.DownloadGate> waiting =
        secondProcess.AcquireDownloadGateAsync(a.PackageHash.ToString());
    await Task.Delay(250);
    Check(!waiting.IsCompleted,
        "separate cache clients cannot both own the same hash download gate");
    firstGate.Dispose();
    using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    Check(next != null, "second downloader can resume after first releases gate");

    // The preceding quota prune intentionally evicted B while preserving A.
    // Re-import the exact immutable bytes before testing restart recovery;
    // recovery cannot succeed against a deliberately deleted archive.
    cache.Promote(second, b);
    using (var reopened = new CommunityArchiveCache(store, max).TryAcquire(b))
        Check(reopened != null, "verified archive can be recovered across cache instances");

    Check(Directory.EnumerateFiles(Path.Combine(store, "archives"), "*.ppmap")
        .All(p => Path.GetFileNameWithoutExtension(p).Length == 64),
        "content-addressed filenames never use user-supplied map names");

    Console.WriteLine($"Community archive cache checks: {checks} PASS.");
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}
