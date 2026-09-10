using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead;

public sealed record FidelityReferenceIdentity(string ContentVersion, string AnchorPath, string AnchorSha256)
{
    public static FidelityReferenceIdentity Amhe1 { get; } = new("AMHE1", "_bin/arm9.bin",
        "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b");
}

public sealed record FidelityManifestLimits(int MaxFiles = 10_000,
    long MaxTotalBytes = 2L * 1024 * 1024 * 1024, long MaxFileBytes = 512L * 1024 * 1024,
    int MaxRelativePathLength = 512)
{
    public void Validate()
    {
        if (MaxFiles is < 1 or > 1_000_000 || MaxTotalBytes is < 1 or > 64L * 1024 * 1024 * 1024
            || MaxFileBytes < 1 || MaxFileBytes > MaxTotalBytes || MaxRelativePathLength is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(FidelityManifestLimits), "Fidelity manifest limits are invalid.");
    }
}

public sealed record FidelityManifestFile(string Path, long Size, string Sha256, string ContentType);
public sealed record FidelityReferenceManifest(int SchemaVersion, string ContentVersion,
    string AggregateSha256, long TotalBytes, IReadOnlyList<FidelityManifestFile> Files);

public static class FidelityManifestBuilder
{
    public const int SchemaVersion = 1;

    public static FidelityReferenceManifest Build(string referenceDirectory,
        FidelityReferenceIdentity? identity = null, FidelityManifestLimits? limits = null)
    {
        identity ??= FidelityReferenceIdentity.Amhe1;
        limits ??= new FidelityManifestLimits();
        limits.Validate();
        ValidateIdentity(identity);
        string root = Path.GetFullPath(referenceDirectory);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || rootInfo.LinkTarget != null || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The fidelity reference must be an existing, non-symbolic directory.");

        var paths = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootInfo);
        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget != null || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Symbolic links are not allowed in fidelity references: {Relative(root, entry.FullName)}");
                if (StringComparer.Ordinal.Equals(entry.Name, ".DS_Store")) continue;
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((DirectoryInfo)entry);
                    continue;
                }
                if (entry is not FileInfo file)
                    throw new InvalidDataException($"Unsupported reference entry: {Relative(root, entry.FullName)}");
                string relative = Relative(root, file.FullName);
                if (relative.Length > limits.MaxRelativePathLength)
                    throw new InvalidDataException($"Reference path exceeds {limits.MaxRelativePathLength} characters.");
                paths.Add(relative);
                if (paths.Count > limits.MaxFiles)
                    throw new InvalidDataException($"Reference contains more than {limits.MaxFiles} files.");
            }
        }
        paths.Sort(StringComparer.Ordinal);
        var files = new List<FidelityManifestFile>(paths.Count);
        long totalBytes = 0;
        foreach (string relative in paths)
        {
            string fullPath = ResolveContained(root, relative);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Reference changed while it was being read: {relative}");
            long size = info.Length;
            if (size < 0 || size > limits.MaxFileBytes || totalBytes > limits.MaxTotalBytes - size)
                throw new InvalidDataException("Reference content exceeds the configured byte bounds.");
            totalBytes += size;
            string digest;
            using (FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       128 * 1024, FileOptions.SequentialScan))
                digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            files.Add(new FidelityManifestFile(relative, size, digest, Classify(relative)));
        }
        FidelityManifestFile? anchor = files.SingleOrDefault(file =>
            StringComparer.Ordinal.Equals(file.Path, NormalizeRelative(identity.AnchorPath)));
        if (anchor == null || !StringComparer.OrdinalIgnoreCase.Equals(anchor.Sha256, identity.AnchorSha256))
            throw new InvalidDataException($"Unknown {identity.ContentVersion} reference identity: {identity.AnchorPath} is missing or has the wrong SHA-256.");

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(aggregate, $"fidelity-manifest\0{SchemaVersion}\0{identity.ContentVersion}\0");
        foreach (FidelityManifestFile file in files)
        {
            Append(aggregate, file.Path); Append(aggregate, "\0");
            Append(aggregate, file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)); Append(aggregate, "\0");
            Append(aggregate, file.Sha256); Append(aggregate, "\0");
            Append(aggregate, file.ContentType); Append(aggregate, "\n");
        }
        return new FidelityReferenceManifest(SchemaVersion, identity.ContentVersion,
            Convert.ToHexStringLower(aggregate.GetHashAndReset()), totalBytes, files);
    }

    public static string Serialize(FidelityReferenceManifest manifest) => JsonSerializer.Serialize(manifest,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + "\n";

    private static void ValidateIdentity(FidelityReferenceIdentity identity)
    {
        if (String.IsNullOrWhiteSpace(identity.ContentVersion) || identity.ContentVersion.Length > 64
            || String.IsNullOrWhiteSpace(identity.AnchorPath) || identity.AnchorSha256.Length != 64
            || !identity.AnchorSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("The fidelity reference identity is invalid.", nameof(identity));
        _ = NormalizeRelative(identity.AnchorPath);
    }

    private static string Relative(string root, string path)
    {
        string relative = NormalizeRelative(Path.GetRelativePath(root, Path.GetFullPath(path)));
        _ = ResolveContained(root, relative);
        return relative;
    }

    private static string NormalizeRelative(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (String.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized)
            || normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Fidelity reference paths must be normalized relative paths.");
        return normalized;
    }

    private static string ResolveContained(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Fidelity reference path escapes the reference root.");
        return full;
    }

    private static string Classify(string path)
    {
        string name = Path.GetFileName(path);
        if (path == "_bin/arm9.bin") return "nds-arm9";
        if (name.StartsWith("overlay", StringComparison.OrdinalIgnoreCase)) return "nds-overlay";
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".bin" => "binary", ".json" => "json", ".txt" => "text", ".png" => "png",
            ".jpg" or ".jpeg" => "jpeg", ".wav" => "wave-audio", ".dat" => "data", _ => "unknown"
        };
    }

    private static void Append(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));
}
