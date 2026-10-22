#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MphRead.Droid;

/// <summary>The packaged read-only asset namespace, not an arbitrary disk tree.</summary>
internal interface IRmlUiAndroidPackageAssets
{
    IReadOnlyList<string> List(string directory);
    Stream Open(string path);
}

#if ANDROID
internal sealed class RmlUiAndroidPackageAssets : IRmlUiAndroidPackageAssets
{
    private readonly Android.Content.Res.AssetManager _assets;
    internal RmlUiAndroidPackageAssets(Android.Content.Res.AssetManager assets)
        => _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    public IReadOnlyList<string> List(string directory) => _assets.List(directory) ?? Array.Empty<string>();
    public Stream Open(string path) => _assets.Open(path);
}
#endif

internal sealed record RmlUiAndroidExtractedFile(string Path, long Bytes, string Sha256);
internal sealed record RmlUiAndroidExtractedAssets(string Root, string InventoryFingerprint,
    IReadOnlyList<RmlUiAndroidExtractedFile> Files, long TotalBytes, bool Reused);

/// <summary>
/// Copies APK assets into a fresh owned staging directory, verifies disk bytes,
/// then atomically publishes a contained native filesystem root. No native UI,
/// Activity, EGL, Scene, session pump, package build tool or shell is invoked.
/// Caller supplies an app-private cache directory and retires old roots only
/// after all native documents using them have shut down.
/// </summary>
internal static class RmlUiAndroidApkAssets
{
    internal readonly record struct Limits(int Files, int Entries, int Depth, long FileBytes, long TotalBytes)
    {
        internal static readonly Limits Default = new(4096, 8192, 16, 32L * 1024 * 1024, 128L * 1024 * 1024);
        internal void Validate()
        {
            if (Files < 1 || Files > Default.Files || Entries < 1 || Entries > Default.Entries
                || Depth < 1 || Depth > Default.Depth || FileBytes < 1 || FileBytes > Default.FileBytes
                || TotalBytes < 1 || TotalBytes > Default.TotalBytes)
                throw new ArgumentOutOfRangeException(nameof(Limits));
        }
    }

    private const string PackageRoot = "rmlui";
    private const string ReceiptName = ".prime-rmlui-extraction.json";
    private const string StagePrefix = ".rmlui-stage-";
    private sealed record Receipt(int Version, string InventoryFingerprint, long TotalBytes,
        IReadOnlyList<RmlUiAndroidExtractedFile> Files);

    internal static RmlUiAndroidExtractedAssets Extract(IRmlUiAndroidPackageAssets assets, string cacheRoot,
        Limits? requestedLimits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Path.IsPathRooted(cacheRoot))
            throw new ArgumentException("An absolute app-private cache directory is required.", nameof(cacheRoot));
        Limits limits = requestedLimits ?? Limits.Default;
        limits.Validate();
        string cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        NoLinks(cache);
        Directory.CreateDirectory(cache);
        NoLinks(cache);
        string stage = Path.Combine(cache, StagePrefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(stage) || Directory.Exists(stage)) throw new IOException("RmlUi staging owner already exists.");
        Directory.CreateDirectory(stage);
        NoLinks(stage);
        bool moved = false;
        try
        {
            var inventory = new List<RmlUiAndroidExtractedFile>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int entries = 0;
            long total = 0;
            Walk("", assets.List(PackageRoot), 0);
            if (inventory.Count == 0 || !inventory.Any(file => file.Path == "prime_home.rml"))
                throw new InvalidDataException("The APK lacks the native RmlUi home document.");
            var ordered = inventory.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
            string fingerprint = Fingerprint(ordered);
            byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new Receipt(1, fingerprint, total, ordered),
                new JsonSerializerOptions { WriteIndented = true });
            WriteNew(Path.Combine(stage, ReceiptName), receipt);
            // Verify the complete private tree, including the receipt, before
            // the atomic rename can make it available to a native host.
            VerifyPublished(stage, ordered, receipt, limits, cancellation);
            string destination = Path.Combine(cache, "rmlui-" + fingerprint);
            bool reused;
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                VerifyPublished(destination, ordered, receipt, limits, cancellation);
                reused = true;
            }
            else
            {
                cancellation.ThrowIfCancellationRequested();
                NoLinks(cache); NoLinks(stage);
                // Cancellation before this publication point retires staging.
                // A later cancellation can leave this verified immutable root;
                // never delete it while another native host may be using it.
                try { Directory.Move(stage, destination); moved = true; reused = false; }
                catch (IOException) when (Directory.Exists(destination))
                {
                    // Another extractor may have atomically published the same
                    // read-only package. Reuse only after verifying every byte.
                    VerifyPublished(destination, ordered, receipt, limits, cancellation);
                    reused = true;
                }
                VerifyPublished(destination, ordered, receipt, limits, cancellation);
            }
            return new(destination, fingerprint, Array.AsReadOnly(ordered), total, reused);

            void Walk(string relative, IReadOnlyList<string> children, int depth)
            {
                cancellation.ThrowIfCancellationRequested();
                if (depth > limits.Depth || children.Count > limits.Entries)
                    throw new InvalidDataException("RmlUi APK inventory exceeds its directory budget.");
                var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in children.OrderBy(name => name, StringComparer.Ordinal))
                {
                    cancellation.ThrowIfCancellationRequested();
                    Segment(name);
                    if (!local.Add(name) || ++entries > limits.Entries)
                        throw new InvalidDataException("RmlUi APK inventory contains a duplicate or too many entries.");
                    string path = relative.Length == 0 ? name : relative + "/" + name;
                    if (Encoding.UTF8.GetByteCount(path) > 512 || path.Equals(ReceiptName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("RmlUi APK path exceeds its budget or uses a reserved name.");
                    string source = PackageRoot + "/" + path;
                    IReadOnlyList<string> nested = assets.List(source);
                    if (nested.Count > 0) { Walk(path, nested, depth + 1); continue; }
                    if (inventory.Count >= limits.Files || !names.Add(path))
                        throw new InvalidDataException("RmlUi APK inventory contains a duplicate or too many files.");
                    string target = Contained(stage, path);
                    NoLinks(target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    NoLinks(target);
                    using Stream input = assets.Open(source);
                    NoLinks(target);
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[64 * 1024];
                    long length = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (count < 0 || length + count > limits.FileBytes || total + count > limits.TotalBytes)
                            throw new InvalidDataException("RmlUi APK asset bytes exceed their permitted budget.");
                        output.Write(buffer, 0, count); hash.AppendData(buffer, 0, count);
                        length += count; total += count;
                    }
                    output.Flush(flushToDisk: true); output.Dispose();
                    string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                    if (HashFile(target, length, cancellation) != digest)
                        throw new InvalidDataException("RmlUi APK asset failed SHA-256 disk readback.");
                    inventory.Add(new(path, length, digest));
                }
            }
        }
        finally
        {
            if (!moved && Directory.Exists(stage)) DeleteStage(stage);
        }
    }

    private static void Segment(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128 || name is "." or "..")
            throw new InvalidDataException("Invalid RmlUi APK path segment.");
        foreach (char value in name)
            if (!char.IsAsciiLetterOrDigit(value) && value is not ('.' or '_' or '-'))
                throw new InvalidDataException("Invalid RmlUi APK path segment.");
    }
    private static string Contained(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("RmlUi APK path escapes its owned cache.");
        return full;
    }
    private static void NoLinks(string path)
    {
        for (FileSystemInfo? entry = new DirectoryInfo(path); entry != null; entry = ((DirectoryInfo)entry).Parent)
        {
            entry.Refresh();
            if (entry.LinkTarget != null || entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("RmlUi asset cache cannot traverse symbolic links.");
            if (File.Exists(entry.FullName) && !Directory.Exists(entry.FullName))
            {
                if (entry.FullName != path) throw new InvalidDataException("RmlUi asset parent is not a directory.");
                var file = new FileInfo(entry.FullName);
                if (file.LinkTarget != null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("RmlUi asset cache cannot traverse symbolic links.");
            }
        }
    }
    private static string Fingerprint(IReadOnlyList<RmlUiAndroidExtractedFile> files)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true);
        writer.Write("PP-RMLUI-APK-INVENTORY-1"); writer.Write(files.Count);
        foreach (var file in files) { writer.Write(file.Path); writer.Write(file.Bytes); writer.Write(file.Sha256); }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }
    private static string HashFile(string path, long expectedLength, CancellationToken cancellation)
    {
        NoLinks(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != expectedLength) throw new InvalidDataException("RmlUi cached asset length differs from its APK inventory.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long length = 0; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (length + count > expectedLength) throw new InvalidDataException("RmlUi cached asset changed during verification.");
            hash.AppendData(buffer, 0, count); length += count;
        }
        if (length != expectedLength) throw new InvalidDataException("RmlUi cached asset changed during verification.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static void WriteNew(string path, byte[] contents)
    {
        NoLinks(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(contents); output.Flush(flushToDisk: true);
    }
    private static void VerifyPublished(string root, IReadOnlyList<RmlUiAndroidExtractedFile> files,
        byte[] receipt, Limits limits, CancellationToken cancellation)
    {
        NoLinks(root);
        if (!Directory.Exists(root)) throw new InvalidDataException("RmlUi published asset root is not a directory.");
        var expected = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            int slash = file.Path.LastIndexOf('/');
            while (slash >= 0)
            {
                string directory = file.Path[..slash];
                directories.Add(directory); slash = directory.LastIndexOf('/');
            }
        }
        expected.Add(ReceiptName);
        int seen = 0, entries = 0;
        Walk(root, "", 0);
        if (seen != expected.Count) throw new InvalidDataException("RmlUi cache inventory differs from its APK.");
        foreach (var file in files)
            if (HashFile(Contained(root, file.Path), file.Bytes, cancellation) != file.Sha256)
                throw new InvalidDataException("RmlUi cache SHA-256 differs from its APK inventory.");
        string receiptHash = Convert.ToHexString(SHA256.HashData(receipt)).ToLowerInvariant();
        if (HashFile(Contained(root, ReceiptName), receipt.Length, cancellation) != receiptHash)
            throw new InvalidDataException("RmlUi cache extraction receipt differs from its APK inventory.");
        void Walk(string directory, string relative, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth > limits.Depth) throw new InvalidDataException("RmlUi cache exceeds its directory budget.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > limits.Entries + 1) throw new InvalidDataException("RmlUi cache exceeds its entry budget.");
                NoLinks(entry);
                string name = Path.GetFileName(entry);
                string path = relative.Length == 0 ? name : relative + "/" + name;
                if (Directory.Exists(entry))
                {
                    if (!directories.Contains(path)) throw new InvalidDataException("Unexpected directory in RmlUi cache.");
                    Walk(entry, path, depth + 1);
                }
                else if (!expected.Contains(path)) throw new InvalidDataException("Unexpected file in RmlUi cache.");
                else seen++;
            }
        }
    }
    private static void DeleteStage(string root)
    {
        // Only our fresh staging tree is removed; published roots are never
        // replaced or deleted while a native host might still read them.
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        { Directory.Delete(root); return; }
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                DeleteStage(entry);
            else if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry);
            else File.Delete(entry);
        }
        Directory.Delete(root);
    }
}
#endif
