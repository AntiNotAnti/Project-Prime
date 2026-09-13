using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Update;

/// <summary>The transport-independent channel understood by the client.</summary>
public enum UpdateChannel
{
    Stable
}

/// <summary>Signed metadata for one official Project Prime release.</summary>
public sealed record UpdateManifest(
    int SchemaVersion,
    string Channel,
    string Version,
    DateTimeOffset PublishedUtc,
    IReadOnlyList<UpdatePackage> Packages);

/// <summary>One platform package described by an <see cref="UpdateManifest"/>.</summary>
public sealed record UpdatePackage(string Rid, string FileName, long Size, string Sha256);

/// <summary>A release package's explicit managed-file ownership list.</summary>
public sealed record ReleaseFilesManifest(
    int SchemaVersion,
    string Version,
    IReadOnlyList<ReleaseFile> Files);

/// <summary>A normalized path and its expected bytes in a desktop package.</summary>
public sealed record ReleaseFile(string Path, string Sha256);

/// <summary>Thrown when signed metadata is malformed or outside its contract.</summary>
public sealed class UpdateManifestValidationException : FormatException
{
    public UpdateManifestValidationException(string message) : base(message) { }
}

/// <summary>
/// Files that belong to a player's installation rather than a release.
/// Packages may carry templates such as <c>paths.txt</c>, but the transaction
/// engine must never install, replace, or remove these paths.
/// </summary>
public static class UpdatePathPolicy
{
    private static readonly string[] PlayerDirectoryNames =
        ["files", "content", "settings", "saves", "screenshots", "_screenshots",
            "logs", "replays", "_replays"];

    public static bool IsNeverManaged(string? path)
    {
        if (String.IsNullOrWhiteSpace(path)) return false;
        string normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Equals("paths.txt", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("controls.txt", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("launcher.txt", StringComparison.OrdinalIgnoreCase))
            return true;
        string[] parts = normalized.Split('/');
        return parts.Any(part => PlayerDirectoryNames.Contains(part,
            StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Bounded validation shared by the player and release tooling.</summary>
public static class UpdateManifestValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxSignatureBytes = 256;
    public const int MaxPackages = 16;
    public const long MaxPackageBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxFileNameBytes = 240;

    private static readonly string[] SupportedRids =
    ["win-x64", "linux-x64", "osx-x64", "osx-arm64", "android"];

    public static IReadOnlyList<string> SupportedRuntimeIdentifiers => SupportedRids;

    public static UpdateManifest Validate(UpdateManifest manifest)
    {
        if (manifest == null)
            throw new UpdateManifestValidationException("manifest is null");
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new UpdateManifestValidationException("unknown update manifest schema");
        if (!String.Equals(manifest.Channel, "stable", StringComparison.Ordinal))
            throw new UpdateManifestValidationException("only the stable update channel is supported");
        if (!IsExactVersion(manifest.Version) || !Version.TryParse(manifest.Version, out _))
            throw new UpdateManifestValidationException("manifest version must be X.Y.Z");
        if (manifest.PublishedUtc == default || manifest.PublishedUtc.Offset != TimeSpan.Zero
            || manifest.PublishedUtc == DateTimeOffset.UnixEpoch)
            throw new UpdateManifestValidationException("publishedUtc must be a UTC timestamp");
        if (manifest.Packages == null || manifest.Packages.Count == 0
            || manifest.Packages.Count > MaxPackages)
        {
            throw new UpdateManifestValidationException("manifest package count is invalid");
        }

        var rids = new HashSet<string>(StringComparer.Ordinal);
        foreach (UpdatePackage package in manifest.Packages)
        {
            if (package == null || !rids.Add(package.Rid))
                throw new UpdateManifestValidationException("manifest contains a duplicate or null RID");
            if (!SupportedRids.Contains(package.Rid, StringComparer.Ordinal))
                throw new UpdateManifestValidationException($"unsupported package RID '{package.Rid}'");
            if (!IsSafeBareFileName(package.FileName))
                throw new UpdateManifestValidationException($"unsafe package filename '{package.FileName}'");
            if (package.Size <= 0 || package.Size > MaxPackageBytes)
                throw new UpdateManifestValidationException("package size is outside the allowed bounds");
            if (!IsSha256(package.Sha256))
                throw new UpdateManifestValidationException($"invalid SHA-256 for {package.Rid}");
        }
        return manifest;
    }

    public static bool TryValidate(UpdateManifest? manifest, out string error)
    {
        try
        {
            Validate(manifest!);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool IsExactVersion(string? value)
    {
        if (String.IsNullOrEmpty(value)) return false;
        int first = value.IndexOf('.');
        int second = first < 0 ? -1 : value.IndexOf('.', first + 1);
        if (first <= 0 || second <= first + 1 || second == value.Length - 1
            || value.IndexOf('.', second + 1) >= 0)
            return false;
        return IsNumber(value.AsSpan(0, first))
            && IsNumber(value.AsSpan(first + 1, second - first - 1))
            && IsNumber(value.AsSpan(second + 1));
    }

    private static bool IsNumber(ReadOnlySpan<char> value)
    {
        if (value.Length == 0 || (value.Length > 1 && value[0] == '0')) return false;
        foreach (char c in value)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }

    internal static bool IsSha256(string? value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value)
        {
            if (!((c is >= '0' and <= '9') || (c is >= 'a' and <= 'f')
                || (c is >= 'A' and <= 'F')))
                return false;
        }
        return true;
    }

    internal static bool IsSafeBareFileName(string? value)
    {
        if (String.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > MaxFileNameBytes
            || value is "." or ".." || value.Contains('/') || value.Contains('\\')
            || value.Contains(':') || value.Any(char.IsControl))
            return false;
        if (value.EndsWith('.') || value.EndsWith(' '))
            return false;
        if (value.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
            return false;
        string stem = value.Split('.')[0];
        return !IsReservedWindowsName(stem);
    }

    internal static bool IsReservedWindowsName(string value)
    {
        if (value.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        if ((value.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && value.Length == 4 && value[3] is >= '1' and <= '9')
            return true;
        return false;
    }
}

/// <summary>Deterministic JSON codec. Signatures cover these exact bytes.</summary>
public static class UpdateManifestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] Serialize(UpdateManifest manifest)
    {
        UpdateManifestValidator.Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static UpdateManifest Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > UpdateManifestValidator.MaxManifestBytes)
            throw new UpdateManifestValidationException("manifest metadata is too large or empty");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
            RejectUnknownProperties(document.RootElement,
                ["schemaVersion", "channel", "version", "publishedUtc", "packages"]);
            UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(utf8, Options);
            return UpdateManifestValidator.Validate(manifest!);
        }
        catch (JsonException ex)
        {
            throw new UpdateManifestValidationException($"invalid update manifest JSON: {ex.Message}");
        }
    }

    private static void RejectUnknownProperties(JsonElement root, params string[] allowedValues)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new UpdateManifestValidationException("manifest root must be an object");
        var allowed = new HashSet<string>(allowedValues, StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new UpdateManifestValidationException($"unknown manifest property '{property.Name}'");
        }
        if (root.TryGetProperty("packages", out JsonElement packages)
            && packages.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement package in packages.EnumerateArray())
            {
                RejectUnknownProperties(package, ["rid", "fileName", "size", "sha256"]);
            }
        }
    }
}

/// <summary>Validation and deterministic codec for a package's file ownership.</summary>
public static class ReleaseFilesManifestValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxManifestBytes = 256 * 1024;
    public const int MaxFiles = 100_000;
    public const int MaxPathBytes = 1024;
    public const long MaxExtractedBytes = 8L * 1024 * 1024 * 1024;
    public const long MaxFileBytes = 512L * 1024 * 1024;

    public static ReleaseFilesManifest Validate(ReleaseFilesManifest manifest,
        bool requireHashes = true)
    {
        if (manifest == null || manifest.SchemaVersion != CurrentSchemaVersion)
            throw new UpdateManifestValidationException("unknown release-files schema");
        if (!UpdateManifestValidator.IsExactVersion(manifest.Version))
            throw new UpdateManifestValidationException("release-files version must be X.Y.Z");
        if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > MaxFiles)
            throw new UpdateManifestValidationException("release-files count is invalid");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ReleaseFile file in manifest.Files)
        {
            if (file == null || !IsSafeRelativePath(file.Path))
                throw new UpdateManifestValidationException("release-files contains an unsafe path");
            if (UpdatePathPolicy.IsNeverManaged(file.Path))
                throw new UpdateManifestValidationException(
                    $"release-files contains a player-owned path '{file.Path}'");
            if (!seen.Add(file.Path))
                throw new UpdateManifestValidationException("release-files contains a duplicate path");
            paths.Add(file.Path);
            if (requireHashes && !UpdateManifestValidator.IsSha256(file.Sha256))
                throw new UpdateManifestValidationException($"invalid hash for release file '{file.Path}'");
            // The manifest has no file sizes; the aggregate cap is enforced by
            // the archive verifier while it reads bytes.
            total += file.Path.Length;
            if (total > (long)MaxFiles * MaxPathBytes)
                throw new UpdateManifestValidationException("release-files metadata is too large");
        }
        foreach (string path in paths)
        {
            int slash = path.IndexOf('/');
            while (slash > 0)
            {
                if (paths.Contains(path[..slash]))
                    throw new UpdateManifestValidationException(
                        $"release-files contains an ancestor-file conflict for '{path}'");
                slash = path.IndexOf('/', slash + 1);
            }
        }
        return manifest;
    }

    public static bool IsSafeRelativePath(string? path)
    {
        if (String.IsNullOrEmpty(path) || Encoding.UTF8.GetByteCount(path) > MaxPathBytes
            || path.StartsWith('/') || path.StartsWith('\\')
            || path.Contains(':') || path.Contains('\0') || path.Any(char.IsControl)
            || path.Contains('\\'))
            return false;
        string[] parts = path.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or "..")) return false;
        if (parts[0].Equals(".update", StringComparison.OrdinalIgnoreCase)
            || path.Equals("release-files.json", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (string part in parts)
        {
            if (part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0
                || UpdateManifestValidator.IsReservedWindowsName(part.Split('.')[0]))
                return false;
            if (part.EndsWith('.') || part.EndsWith(' '))
                return false;
        }
        return true;
    }
}

public static class ReleaseFilesJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] Serialize(ReleaseFilesManifest manifest)
    {
        ReleaseFilesManifestValidator.Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static ReleaseFilesManifest Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > ReleaseFilesManifestValidator.MaxManifestBytes)
            throw new UpdateManifestValidationException("release-files metadata is too large or empty");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
            UpdateManifestJsonHelper.RejectUnknown(document.RootElement,
                ["schemaVersion", "version", "files"]);
            if (!document.RootElement.TryGetProperty("files", out JsonElement files)
                || files.ValueKind != JsonValueKind.Array)
                throw new UpdateManifestValidationException("release-files must contain an array");
            foreach (JsonElement file in files.EnumerateArray())
                UpdateManifestJsonHelper.RejectUnknown(file, ["path", "sha256"]);
            ReleaseFilesManifest? manifest = JsonSerializer.Deserialize<ReleaseFilesManifest>(utf8, Options);
            return ReleaseFilesManifestValidator.Validate(manifest!);
        }
        catch (JsonException ex)
        {
            throw new UpdateManifestValidationException($"invalid release-files JSON: {ex.Message}");
        }
    }
}

internal static class UpdateManifestJsonHelper
{
    internal static void RejectUnknown(JsonElement root, params string[] allowedValues)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new UpdateManifestValidationException("metadata entry must be an object");
        var allowed = new HashSet<string>(allowedValues, StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new UpdateManifestValidationException($"unknown metadata property '{property.Name}'");
        }
    }
}
