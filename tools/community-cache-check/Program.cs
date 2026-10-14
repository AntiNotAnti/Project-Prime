using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

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
        Check(cache.TryAcquire(b) == null && cache.TryAcquire(a) != null,
            "LRU preserves a leased older archive and removes an unleased newer one");
        // The temporary second lease is intentionally released below in a
        // separate statement to avoid holding an accidental eviction guard.
    }

    cache.Promote(second, b);
    using (var protectB = cache.TryAcquire(b))
    {
        Check(protectB != null, "evicted exact archive can be safely re-imported");
        secondProcess.Prune();
        Check(secondProcess.TryAcquire(a) == null && secondProcess.TryAcquire(b) != null,
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

    using var firstGate = await cache.AcquireDownloadGateAsync(a.PackageHash.ToString());
    Task<CommunityArchiveCache.DownloadGate> waiting =
        secondProcess.AcquireDownloadGateAsync(a.PackageHash.ToString());
    await Task.Delay(250);
    Check(!waiting.IsCompleted,
        "separate cache clients cannot both own the same hash download gate");
    firstGate.Dispose();
    using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    Check(next != null, "second downloader can resume after first releases gate");

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
