using System.Text;
using MphRead.Droid;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
string temporary = new DirectoryInfo(Path.GetTempPath()).ResolveLinkTarget(returnFinalTarget: true)?.FullName
    ?? (Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath());
string root = Path.Combine(temporary, "prime-rmlui-assets-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string Cache(string name) => Path.Combine(root, name);
bool Failed(Action action)
{
    try { action(); return false; }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException)
    { return true; }
}
bool NoStage(string cache) => !Directory.Exists(cache)
    || !Directory.EnumerateDirectories(cache, ".rmlui-stage-*").Any();

try
{
    var assets = new FakeAssets();
    var first = RmlUiAndroidApkAssets.Extract(assets, Cache("valid"));
    Check(!first.Reused && first.Files.Count == assets.Files.Count && first.InventoryFingerprint.Length == 64,
        "fresh APK inventory publishes one fingerprinted root");
    Check(first.TotalBytes == assets.Files.Values.Sum(bytes => bytes.LongLength)
        && first.Files.Select(file => file.Path).SequenceEqual(first.Files.Select(file => file.Path).Order(StringComparer.Ordinal)),
        "inventory is deterministic and reports actual streamed bytes");
    foreach (var file in first.Files)
        Check(File.ReadAllBytes(Path.Combine(first.Root, file.Path)).SequenceEqual(assets.Files[file.Path]),
            "RML/RCSS/font/license/manifest bytes survive copy and readback: " + file.Path);
    Check(File.Exists(Path.Combine(first.Root, ".prime-rmlui-extraction.json")) && NoStage(Cache("valid")),
        "published receipt exists and fresh staging owner is gone");
    assets.Reverse = true;
    var reused = RmlUiAndroidApkAssets.Extract(assets, Cache("valid"));
    Check(reused.Reused && reused.Root == first.Root && reused.InventoryFingerprint == first.InventoryFingerprint,
        "reordered APK inventory reuses only a fully verified identical root");
    Check(reused.InventoryFingerprint == "158ea4965bc16474c0af9b3bb1c276ce94401d567f2973bb010a5eb7e37b85b3",
        "frozen synthetic inventory fingerprint preserves codec/order/hash framing");
    var changed = new FakeAssets(); changed.Files["prime_home.rml"] = Encoding.UTF8.GetBytes("<rml>changed package</rml>");
    var second = RmlUiAndroidApkAssets.Extract(changed, Cache("valid"));
    Check(second.InventoryFingerprint != first.InventoryFingerprint && second.Root != first.Root
        && Directory.Exists(first.Root), "package content change publishes a new immutable inventory without deleting a live root");

    byte[] tampered = assets.Files["prime_home.rml"].ToArray(); tampered[0] ^= 1;
    File.WriteAllBytes(Path.Combine(first.Root, "prime_home.rml"), tampered);
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("valid"))) && NoStage(Cache("valid"))
        && File.ReadAllBytes(Path.Combine(first.Root, "prime_home.rml")).SequenceEqual(tampered),
        "same-length SHA tampering fails closed without replacing an existing host's root");

    var extra = RmlUiAndroidApkAssets.Extract(assets, Cache("extra"));
    Directory.CreateDirectory(Path.Combine(extra.Root, "unpackaged-empty-directory"));
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("extra"))), "unexpected empty cache directories are rejected");
    var receipt = RmlUiAndroidApkAssets.Extract(assets, Cache("receipt"));
    File.AppendAllText(Path.Combine(receipt.Root, ".prime-rmlui-extraction.json"), " ");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("receipt"))), "receipt bytes are verified against the APK-derived inventory");

    foreach (string segment in new[] { "..", ".", "../escape.rml", "bad\\path", "/absolute", "bad:name", "bad\0name" })
    {
        var invalid = new FakeAssets { RootOverride = new[] { segment } };
        string cache = Cache("path-" + checks);
        Check(Failed(() => RmlUiAndroidApkAssets.Extract(invalid, cache)) && NoStage(cache),
            "unsafe APK path segment rejected before publication");
    }
    var collision = new FakeAssets(); collision.Files["Prime_Home.rml"] = new byte[] { 1 };
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(collision, Cache("case"))), "case-colliding APK paths are rejected across filesystems");
    var missing = new FakeAssets(); missing.Files.Remove("prime_home.rml");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(missing, Cache("home"))), "missing native home cannot publish a misleading package root");

    var limits = RmlUiAndroidApkAssets.Limits.Default;
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("count"), limits with { Files = 1 })), "file-count budget enforced");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("entries"), limits with { Entries = 1 })), "inventory-entry budget enforced");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("depth"), limits with { Depth = 1 })), "directory-depth budget enforced");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("file-bytes"), limits with { FileBytes = 4 })), "per-file streamed byte budget enforced");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("total-bytes"), limits with { TotalBytes = 20 })), "aggregate streamed byte budget enforced");
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, "relative-cache")), "relative writable cache roots are rejected");
    var cancellation = new CancellationToken(canceled: true);
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("canceled"), cancellation: cancellation))
        && NoStage(Cache("canceled")), "canceled extraction leaves no staged or published native root");
    using var midstreamCancellation = new CancellationTokenSource();
    var interrupted = new FakeAssets { CancelDuringRead = midstreamCancellation };
    interrupted.Files["prime_home.rml"] = new byte[128 * 1024];
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(interrupted, Cache("midstream-canceled"),
            cancellation: midstreamCancellation.Token))
        && interrupted.ReadsBeforeCancellation == 2 && NoStage(Cache("midstream-canceled"))
        && !Directory.EnumerateDirectories(Cache("midstream-canceled"), "rmlui-*").Any(),
        "cancellation after a partial streamed copy retires staging without publishing a native root");
    var broken = new FakeAssets { ThrowDuringRead = true };
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(broken, Cache("broken"))) && NoStage(Cache("broken"))
        && !Directory.EnumerateDirectories(Cache("broken"), "rmlui-*").Any(), "partial stream failure cleans fresh staging before atomic publication");

    string outside = Cache("outside"); Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(outside, "sentinel"), "unchanged");
    string linkedCache = Cache("linked-cache"); Directory.CreateSymbolicLink(linkedCache, outside);
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, linkedCache))
        && Directory.EnumerateFileSystemEntries(outside).Count() == 1, "symlink cache root is rejected without writing through it");
    var linked = RmlUiAndroidApkAssets.Extract(assets, Cache("linked-published"));
    Directory.Delete(Path.Combine(linked.Root, "pages"), recursive: true);
    Directory.CreateSymbolicLink(Path.Combine(linked.Root, "pages"), outside);
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("linked-published")))
        && File.ReadAllText(Path.Combine(outside, "sentinel")) == "unchanged", "symlink in a published asset tree fails cache verification");
    var fileLink = RmlUiAndroidApkAssets.Extract(assets, Cache("linked-file"));
    File.Delete(Path.Combine(fileLink.Root, "prime_home.rml"));
    File.CreateSymbolicLink(Path.Combine(fileLink.Root, "prime_home.rml"), Path.Combine(outside, "sentinel"));
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, Cache("linked-file")))
        && File.ReadAllText(Path.Combine(outside, "sentinel")) == "unchanged", "published file symlink is rejected before hash read");
    string dangling = Cache("dangling-cache"); Directory.CreateSymbolicLink(dangling, Cache("does-not-exist"));
    Check(Failed(() => RmlUiAndroidApkAssets.Extract(assets, dangling)), "dangling cache symlink cannot become a staging parent");
    Check(Directory.EnumerateDirectories(root).All(NoStage), "all failed extractions leave their owned staging trees retired");
    Console.WriteLine($"RMLUI ANDROID APK ASSETS PASS {checks} checks; inventory {reused.InventoryFingerprint}; fake APK reader + local filesystem, no Android/APK/native/device acceptance.");
}
finally
{
    // Directory.Delete does not recurse through Unix directory symlinks.
    Directory.Delete(root, recursive: true);
}

internal sealed class FakeAssets : IRmlUiAndroidPackageAssets
{
    internal readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal)
    {
        ["prime_home.rml"] = Encoding.UTF8.GetBytes("<rml>native home</rml>"),
        ["pages/home/home.rml"] = Encoding.UTF8.GetBytes("<rml>shared page</rml>"),
        ["themes/prime.rcss"] = Encoding.UTF8.GetBytes("body { color: #fff; }"),
        ["fonts/Rajdhani-SemiBold.ttf"] = new byte[] { 0, 1, 2, 3 },
        ["fonts/Rajdhani-Bold.ttf"] = new byte[] { 1, 2, 3, 4 },
        ["fonts/JetBrainsMono-Regular.ttf"] = new byte[] { 2, 3, 4, 5 },
        ["fonts/NotoSansJP-Regular.ttf"] = new byte[] { 3, 4, 5, 6 },
        ["licenses/RmlUi-LICENSE.txt"] = Encoding.UTF8.GetBytes("fixture license"),
        ["native-manifests/arm64-v8a/PRIME-RMLUI.json"] = Encoding.UTF8.GetBytes("{\"fixture\":\"arm64\"}"),
        ["native-manifests/x86_64/PRIME-RMLUI.json"] = Encoding.UTF8.GetBytes("{\"fixture\":\"x64\"}")
    };
    internal bool Reverse, ThrowDuringRead;
    internal CancellationTokenSource? CancelDuringRead;
    internal int ReadsBeforeCancellation;
    internal string[]? RootOverride;
    public IReadOnlyList<string> List(string directory)
    {
        if (directory == "rmlui" && RootOverride != null) return RootOverride;
        string prefix = directory == "rmlui" ? "" : directory["rmlui/".Length..] + "/";
        var names = Files.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..].Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
        return Reverse ? names.Reverse().ToArray() : names;
    }
    public Stream Open(string path)
    {
        byte[] bytes = Files[path["rmlui/".Length..]];
        if (ThrowDuringRead) return new BrokenStream(bytes);
        if (CancelDuringRead != null && path == "rmlui/prime_home.rml")
            return new CancelingStream(bytes, () => { ReadsBeforeCancellation++; if (ReadsBeforeCancellation == 2) CancelDuringRead.Cancel(); });
        return new MemoryStream(bytes, writable: false);
    }
    private sealed class CancelingStream(byte[] bytes, Action afterRead) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count); afterRead(); return read;
        }
    }
    private sealed class BrokenStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private bool _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read) throw new IOException("Fake APK stream interrupted.");
            _read = true; return base.Read(buffer, offset, count);
        }
    }
}
