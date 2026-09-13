using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead
{
    /// <summary>
    /// Human-authored identity for an optional presentation model. The key is
    /// independent of runtime object identity and is stable across processes.
    /// </summary>
    public readonly struct PresentationModelAssetKey : IEquatable<PresentationModelAssetKey>,
        IComparable<PresentationModelAssetKey>
    {
        public const int MaximumLength = 256;
        public const int MaximumSegmentLength = 64;
        private readonly string? _value;

        private PresentationModelAssetKey(string canonical) => _value = canonical;

        public bool IsValid => _value is not null;
        public string Value => _value ?? String.Empty;

        public static PresentationModelAssetKey ForFirstPersonWeapon(string weapon)
            => Parse($"model/weapon/{weapon}/first-person");

        public static PresentationModelAssetKey ForHunter(string hunter, int recolorId)
            => Parse($"model/hunter/{hunter}/recolor/{recolorId}");

        public static PresentationModelAssetKey ForPickup(string pickup)
            => Parse($"model/pickup/{pickup}");

        public static PresentationModelAssetKey ForDoor(string door)
            => Parse($"model/door/{door}");

        public static PresentationModelAssetKey ForProp(string prop)
            => Parse($"model/prop/{prop}");

        public static PresentationModelAssetKey ForEnvironment(string room, string model)
            => Parse($"model/environment/{room}/{model}");

        public static PresentationModelAssetKey ForCustom(params string[] path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Parse("custom/" + String.Join('/', path));
        }

        public static PresentationModelAssetKey Parse(string value)
        {
            if (!TryParse(value, out PresentationModelAssetKey key))
                throw new FormatException("Presentation model asset key is not in a supported canonical format.");
            return key;
        }

        public static bool TryParse(string? value, out PresentationModelAssetKey key)
        {
            key = default;
            if (String.IsNullOrEmpty(value) || value.Length > MaximumLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.Contains('\\') || value.Contains(':')
                || value.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = value.Split('/');
            if (!HasSupportedShape(segments)) return false;
            string[] canonical = new string[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length is 0 or > MaximumSegmentLength) return false;
                if (IsNumericPosition(segments, i))
                {
                    if (!Int32.TryParse(segment, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int number) || number < 0)
                    {
                        return false;
                    }
                    canonical[i] = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    string lowered = segment.ToLowerInvariant();
                    if (!IsIdentifier(lowered)) return false;
                    canonical[i] = lowered;
                }
            }
            key = new PresentationModelAssetKey(String.Join('/', canonical));
            return true;
        }

        private static bool HasSupportedShape(string[] segments)
        {
            if (segments.Length >= 2 && EqualsIgnoreCase(segments[0], "custom")) return true;
            if (segments.Length < 3 || !EqualsIgnoreCase(segments[0], "model")) return false;
            if (segments.Length == 4 && EqualsIgnoreCase(segments[1], "weapon"))
                return EqualsIgnoreCase(segments[3], "first-person");
            if (segments.Length == 5 && EqualsIgnoreCase(segments[1], "hunter"))
                return EqualsIgnoreCase(segments[3], "recolor");
            if (segments.Length == 3)
                return EqualsIgnoreCase(segments[1], "pickup")
                    || EqualsIgnoreCase(segments[1], "door")
                    || EqualsIgnoreCase(segments[1], "prop");
            return segments.Length == 4 && EqualsIgnoreCase(segments[1], "environment");
        }

        private static bool IsNumericPosition(string[] segments, int index)
            => segments.Length == 5 && EqualsIgnoreCase(segments[0], "model")
                && EqualsIgnoreCase(segments[1], "hunter") && index == 4;

        private static bool IsIdentifier(string value)
        {
            if (value is "." or ".." || value[0] is '.' or '-' or '_') return false;
            foreach (char character in value)
            {
                bool valid = character is >= 'a' and <= 'z'
                    || character is >= '0' and <= '9'
                    || character is '.' or '-' or '_';
                if (!valid) return false;
            }
            return true;
        }

        private static bool EqualsIgnoreCase(string left, string right)
            => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        public bool Equals(PresentationModelAssetKey other)
            => String.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is PresentationModelAssetKey other && Equals(other);
        public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public int CompareTo(PresentationModelAssetKey other)
            => String.Compare(_value, other._value, StringComparison.Ordinal);
        public override string ToString() => Value;
        public static bool operator ==(PresentationModelAssetKey left, PresentationModelAssetKey right)
            => left.Equals(right);
        public static bool operator !=(PresentationModelAssetKey left, PresentationModelAssetKey right)
            => !left.Equals(right);
    }

    public static class PresentationModelPackLimits
    {
        public const int MaximumManifestBytes = 256 * 1024;
        public const int MaximumModels = 512;
        public const int MaximumRelativePathLength = 512;
        public const int MaximumIdentityLength = 256;
        public const int MaximumMetadataLength = 256;
        public const long MaximumModelFileBytes = 256L * 1024 * 1024;
        public const long MaximumAggregateModelBytes = 1024L * 1024 * 1024;
    }

    /// <summary>
    /// The original identities remain authoritative even when presentation
    /// geometry is replaced. In particular, this descriptor does not allow an
    /// enhancement pack to substitute simulation, collision, or animation data.
    /// </summary>
    public sealed class PresentationModelRequest
    {
        public PresentationModelRequest(PresentationModelAssetKey key, string originalPresentationIdentity,
            string simulationIdentity, string collisionIdentity, string animationIdentity)
        {
            if (!key.IsValid) throw new ArgumentException("A valid presentation model key is required.", nameof(key));
            Key = key;
            OriginalPresentationIdentity = ValidateIdentity(originalPresentationIdentity,
                nameof(originalPresentationIdentity));
            SimulationIdentity = ValidateIdentity(simulationIdentity, nameof(simulationIdentity));
            CollisionIdentity = ValidateIdentity(collisionIdentity, nameof(collisionIdentity));
            AnimationIdentity = ValidateIdentity(animationIdentity, nameof(animationIdentity));
        }

        public PresentationModelAssetKey Key { get; }
        public string OriginalPresentationIdentity { get; }
        public string SimulationIdentity { get; }
        public string CollisionIdentity { get; }
        public string AnimationIdentity { get; }

        private static string ValidateIdentity(string value, string parameter)
        {
            ArgumentNullException.ThrowIfNull(value, parameter);
            if (value.Length is 0 or > PresentationModelPackLimits.MaximumIdentityLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.Any(Char.IsControl))
            {
                throw new ArgumentException("Model identity is empty, too long, or contains control characters.", parameter);
            }
            return value;
        }
    }

    public sealed record PresentationModelReplacementMetadata(
        string? DisplayName,
        string? Author,
        string? License);

    public sealed class PresentationModelReplacement
    {
        internal PresentationModelReplacement(PresentationModelAssetKey key, string relativePath,
            string fullPath, long encodedBytes, PresentationModelReplacementMetadata? metadata)
        {
            Key = key;
            RelativePath = relativePath;
            FullPath = fullPath;
            EncodedBytes = encodedBytes;
            Metadata = metadata;
        }

        public PresentationModelAssetKey Key { get; }
        public string RelativePath { get; }
        public string FullPath { get; }
        public long EncodedBytes { get; }
        public PresentationModelReplacementMetadata? Metadata { get; }
    }

    public sealed class ResolvedPresentationModel
    {
        internal ResolvedPresentationModel(PresentationModelRequest original,
            PresentationModelReplacement? replacement)
        {
            Key = original.Key;
            OriginalPresentationIdentity = original.OriginalPresentationIdentity;
            SimulationIdentity = original.SimulationIdentity;
            CollisionIdentity = original.CollisionIdentity;
            AnimationIdentity = original.AnimationIdentity;
            Replacement = replacement;
        }

        public PresentationModelAssetKey Key { get; }
        public string OriginalPresentationIdentity { get; }
        public string SimulationIdentity { get; }
        public string CollisionIdentity { get; }
        public string AnimationIdentity { get; }
        public PresentationModelReplacement? Replacement { get; }
        public bool UsesReplacement => Replacement is not null;
    }

    public sealed class PresentationModelResolver
    {
        private readonly IReadOnlyDictionary<PresentationModelAssetKey, PresentationModelReplacement> _models;

        internal PresentationModelResolver(
            IReadOnlyDictionary<PresentationModelAssetKey, PresentationModelReplacement> models)
            => _models = models;

        public int Count => _models.Count;

        public ResolvedPresentationModel Resolve(PresentationModelRequest original)
        {
            ArgumentNullException.ThrowIfNull(original);
            _models.TryGetValue(original.Key, out PresentationModelReplacement? replacement);
            return new ResolvedPresentationModel(original, replacement);
        }

        public bool TryGetReplacement(PresentationModelAssetKey key,
            out PresentationModelReplacement? replacement)
        {
            if (!key.IsValid)
            {
                replacement = null;
                return false;
            }
            return _models.TryGetValue(key, out replacement);
        }

        internal static PresentationModelResolver Empty { get; }
            = new(new Dictionary<PresentationModelAssetKey, PresentationModelReplacement>());
    }

    public sealed record PresentationModelManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public PresentationModelDeclaration[] Models { get; init; }
            = Array.Empty<PresentationModelDeclaration>();
    }

    public sealed record PresentationModelDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public string Asset { get; init; } = String.Empty;
        public PresentationModelMetadataDeclaration? Metadata { get; init; }
    }

    public sealed record PresentationModelMetadataDeclaration
    {
        public string? DisplayName { get; init; }
        public string? Author { get; init; }
        public string? License { get; init; }
    }

    public enum PresentationModelPackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyModels,
        InvalidModel,
        InvalidMetadata,
        DuplicateModelKey,
        UnsafeModelPath,
        MissingModel,
        ModelTooLarge,
        UnsupportedModel,
        AggregateLimitExceeded
    }

    public sealed record PresentationModelPackIssue(
        PresentationModelPackIssueKind Kind,
        string Message,
        PresentationModelAssetKey? Key = null,
        string? RelativePath = null);

    public sealed record PresentationModelPackLoadResult(
        PresentationModelResolver Resolver,
        IReadOnlyList<PresentationModelPackIssue> Issues)
    {
        public bool HasIssues => Issues.Count != 0;
    }

    /// <summary>
    /// Loads descriptors for an optional, local presentation-model pack. No
    /// model is decoded here. Malformed entries are omitted so callers always
    /// retain the original presentation model.
    /// </summary>
    public static class PresentationModelPackLoader
    {
        public const string ManifestFileName = "models.json";
        private const uint GlbMagic = 0x46546C67;
        private const uint SupportedGlbVersion = 2;

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static PresentationModelPackLoadResult Load(string packRoot)
        {
            var issues = new List<PresentationModelPackIssue>();
            if (!TryGetRoot(packRoot, out string root))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MissingManifest,
                    "Presentation model pack root is invalid."));
                return new PresentationModelPackLoadResult(PresentationModelResolver.Empty, issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] manifestBytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return new PresentationModelPackLoadResult(PresentationModelResolver.Empty, issues);
                }
                if (!TryReadBoundedFile(manifestPath, PresentationModelPackLimits.MaximumManifestBytes,
                    out manifestBytes))
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and {PresentationModelPackLimits.MaximumManifestBytes} bytes."));
                    return new PresentationModelPackLoadResult(PresentationModelResolver.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return new PresentationModelPackLoadResult(PresentationModelResolver.Empty, issues);
            }

            List<PresentationModelDeclaration>? declarations = ParseManifest(manifestBytes, issues);
            if (declarations is null)
                return new PresentationModelPackLoadResult(PresentationModelResolver.Empty, issues);

            var candidates = new List<(PresentationModelAssetKey Key, PresentationModelDeclaration Declaration)>();
            foreach (PresentationModelDeclaration declaration in declarations)
            {
                if (!PresentationModelAssetKey.TryParse(declaration.Key, out PresentationModelAssetKey key))
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.InvalidModel,
                        "Model has an invalid presentation asset key."));
                    continue;
                }
                candidates.Add((key, declaration));
            }

            HashSet<PresentationModelAssetKey> duplicates = candidates
                .GroupBy(candidate => candidate.Key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            foreach (PresentationModelAssetKey duplicate in duplicates.OrderBy(key => key))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.DuplicateModelKey,
                    "Duplicate model key was ignored.", duplicate));
            }

            var replacements = new Dictionary<PresentationModelAssetKey, PresentationModelReplacement>();
            var assets = new Dictionary<string, LoadedAsset?>(PathComparer());
            long aggregateBytes = 0;
            foreach ((PresentationModelAssetKey key, PresentationModelDeclaration declaration) in candidates)
            {
                if (duplicates.Contains(key)) continue;
                LoadedAsset? asset = LoadAsset(root, key, declaration.Asset, assets, issues,
                    ref aggregateBytes);
                if (asset is null) continue;
                PresentationModelReplacementMetadata? metadata = ValidateMetadata(key,
                    declaration.Metadata, issues);
                replacements.Add(key, new PresentationModelReplacement(key, asset.RelativePath,
                    asset.FullPath, asset.EncodedBytes, metadata));
            }

            return new PresentationModelPackLoadResult(new PresentationModelResolver(replacements), issues);
        }

        private static List<PresentationModelDeclaration>? ParseManifest(byte[] bytes,
            List<PresentationModelPackIssue> issues)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Manifest root must be an object.");
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "models"))
                        throw new JsonException($"Unknown manifest property '{property.Name}'.");
                }
                if (!root.TryGetProperty("format", out JsonElement formatElement)
                    || formatElement.GetInt32() != PresentationModelManifest.CurrentFormat)
                {
                    int format = formatElement.ValueKind == JsonValueKind.Undefined
                        ? 0 : formatElement.GetInt32();
                    issues.Add(new PresentationModelPackIssue(
                        PresentationModelPackIssueKind.UnsupportedManifestVersion,
                        $"Manifest format {format} is not supported."));
                    return null;
                }
                if (!root.TryGetProperty("models", out JsonElement models)
                    || models.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("models must be an array.");
                }

                var result = new List<PresentationModelDeclaration>();
                int count = 0;
                foreach (JsonElement entry in models.EnumerateArray())
                {
                    count++;
                    if (count > PresentationModelPackLimits.MaximumModels)
                    {
                        issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.TooManyModels,
                            $"Manifest exceeds {PresentationModelPackLimits.MaximumModels} models."));
                        return null;
                    }
                    try
                    {
                        PresentationModelDeclaration? declaration =
                            entry.Deserialize<PresentationModelDeclaration>(_json);
                        if (declaration is null) throw new JsonException("Model is null.");
                        result.Add(declaration);
                    }
                    catch (Exception error) when (error is JsonException or NotSupportedException)
                    {
                        issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.InvalidModel,
                            "Malformed model entry was ignored."));
                    }
                }
                return result;
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or InvalidOperationException or FormatException or OverflowException)
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MalformedManifest,
                    "Presentation model manifest is malformed."));
                return null;
            }
        }

        private static LoadedAsset? LoadAsset(string root, PresentationModelAssetKey key,
            string relativePath, Dictionary<string, LoadedAsset?> assets,
            List<PresentationModelPackIssue> issues, ref long aggregateBytes)
        {
            if (!TryNormalizeRelativePath(relativePath, out string normalized))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.UnsafeModelPath,
                    "Model path must be a contained relative path.", key, relativePath));
                return null;
            }
            if (!normalized.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.UnsupportedModel,
                    "Only self-contained glTF 2.0 GLB assets are supported.", key, normalized));
                return null;
            }
            if (assets.TryGetValue(normalized, out LoadedAsset? loaded)) return loaded;
            assets.Add(normalized, null); // Cache failures so manifests cannot force repeated I/O.

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison()) || HasReparsePoint(root, normalized))
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.UnsafeModelPath,
                        "Model path escapes the pack or crosses a link.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.UnsafeModelPath,
                    "Model path is invalid.", key, normalized));
                return null;
            }

            try
            {
                if (!File.Exists(candidate))
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MissingModel,
                        "Model file was not found.", key, normalized));
                    return null;
                }
                using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, options: FileOptions.SequentialScan);
                long length = stream.Length;
                if (length < 12 || length > PresentationModelPackLimits.MaximumModelFileBytes)
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.ModelTooLarge,
                        "Model file is empty, truncated, or exceeds the per-file byte limit.", key, normalized));
                    return null;
                }
                Span<byte> header = stackalloc byte[12];
                stream.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != GlbMagic
                    || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != SupportedGlbVersion
                    || BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != length)
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.UnsupportedModel,
                        "Model must have a valid glTF 2.0 GLB header and declared length.", key, normalized));
                    return null;
                }
                if (length > PresentationModelPackLimits.MaximumAggregateModelBytes - aggregateBytes)
                {
                    issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.AggregateLimitExceeded,
                        "Model would exceed the enhancement pack aggregate byte limit.", key, normalized));
                    return null;
                }
                aggregateBytes += length;
                loaded = new LoadedAsset(normalized, candidate, length);
                assets[normalized] = loaded;
                return loaded;
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.MissingModel,
                    "Model file could not be read.", key, normalized));
                return null;
            }
        }

        private static PresentationModelReplacementMetadata? ValidateMetadata(
            PresentationModelAssetKey key, PresentationModelMetadataDeclaration? declaration,
            List<PresentationModelPackIssue> issues)
        {
            if (declaration is null) return null;
            if (!TryMetadataValue(declaration.DisplayName, out string? displayName)
                || !TryMetadataValue(declaration.Author, out string? author)
                || !TryMetadataValue(declaration.License, out string? license))
            {
                issues.Add(new PresentationModelPackIssue(PresentationModelPackIssueKind.InvalidMetadata,
                    "Replacement metadata contains an empty, oversized, or control-character value.", key));
                return null;
            }
            return new PresentationModelReplacementMetadata(displayName, author, license);
        }

        private static bool TryMetadataValue(string? value, out string? validated)
        {
            validated = value;
            return value is null || (value.Length is > 0 and <= PresentationModelPackLimits.MaximumMetadataLength
                && String.Equals(value, value.Trim(), StringComparison.Ordinal)
                && !value.Any(Char.IsControl));
        }

        private static bool TryGetRoot(string packRoot, out string root)
        {
            root = String.Empty;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
                return true;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException)
            {
                return false;
            }
        }

        private static bool TryNormalizeRelativePath(string path, out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path)
                || path.Length > PresentationModelPackLimits.MaximumRelativePathLength
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || path.Any(Char.IsControl))
            {
                return false;
            }
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length is 0 or > 128 || segment is "." or "..")) return false;
            normalized = String.Join('/', segments);
            return true;
        }

        private static bool TryReadBoundedFile(string path, long maximumBytes, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
            long length = stream.Length;
            if (length is <= 0 || length > maximumBytes || length > Int32.MaxValue) return false;
            bytes = GC.AllocateUninitializedArray<byte>((int)length);
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
            return true;
        }

        private static void RejectDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property.");
                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
            }
        }

        private static bool HasReparsePoint(string root, string normalized)
        {
            if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) return true;
            string current = root;
            foreach (string part in normalized.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
            }
            return false;
        }

        private static StringComparer PathComparer()
            => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static StringComparison PathComparison()
            => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static bool IsFileError(Exception error)
            => error is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or PathTooLongException or SecurityException;

        private sealed record LoadedAsset(string RelativePath, string FullPath, long EncodedBytes);
    }
}
