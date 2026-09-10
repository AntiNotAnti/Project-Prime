using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTK.Mathematics;

namespace MphRead
{
    public static class EnhancedColorGradePackLimits
    {
        public const int MaximumManifestBytes = 256 * 1024;
        public const int MaximumLuts = 256;
        public const int MaximumRelativePathLength = 512;
        public const long MaximumTextureFileBytes = 2L * 1024 * 1024;
        public const long MaximumAggregateEncodedBytes = 64L * 1024 * 1024;
        public const long MaximumAggregateDecodedBytes
            = MaximumLuts * EnhancedLutAddressing.TextureWidth
                * EnhancedLutAddressing.TextureHeight * 4L;
    }

    internal sealed class EnhancedColorGradeResourceBudget
    {
        private readonly long _maximumEncoded;
        private readonly long _maximumDecoded;
        private long _encoded;
        private long _decoded;

        private EnhancedColorGradeResourceBudget(long maximumEncoded, long maximumDecoded)
        {
            _maximumEncoded = maximumEncoded;
            _maximumDecoded = maximumDecoded;
        }

        internal static bool TryCreate(long maximumEncoded, long maximumDecoded,
            out EnhancedColorGradeResourceBudget? budget)
        {
            if (maximumEncoded <= 0 || maximumDecoded <= 0)
            {
                budget = null;
                return false;
            }
            budget = new EnhancedColorGradeResourceBudget(maximumEncoded, maximumDecoded);
            return true;
        }

        internal bool TryReserve(long encodedBytes, long decodedBytes)
        {
            if (encodedBytes < 0 || decodedBytes < 0
                || encodedBytes > _maximumEncoded - _encoded
                || decodedBytes > _maximumDecoded - _decoded)
            {
                return false;
            }
            _encoded += encodedBytes;
            _decoded += decodedBytes;
            return true;
        }
    }

    /// <summary>
    /// Immutable, backend-neutral 16-cube stored as a 256 by 16 RGBA8 strip.
    /// Values are display-linear numbers; the GPU upload must use an UNORM,
    /// non-sRGB format and must not generate mipmaps.
    /// </summary>
    public sealed class EnhancedColorGradeLut
    {
        private readonly byte[] _rgba8;

        internal EnhancedColorGradeLut(string? relativePath, ReadOnlySpan<byte> rgba8,
            bool isIdentityFallback)
        {
            int expected = checked(EnhancedLutAddressing.TextureWidth
                * EnhancedLutAddressing.TextureHeight * 4);
            if (rgba8.Length != expected)
                throw new ArgumentException("Color-grade LUT must contain exactly 256x16 RGBA8 pixels.",
                    nameof(rgba8));

            RelativePath = relativePath;
            IsIdentityFallback = isIdentityFallback;
            _rgba8 = rgba8.ToArray();
            TextureIdentity = new TextureIdentity(this, variant: EnhancedColorGradeTextureKind.Lut);
            Pixels = new RenderTexturePixels(TextureIdentity, EnhancedLutAddressing.TextureWidth,
                EnhancedLutAddressing.TextureHeight, _rgba8, StableRevision(_rgba8),
                onlyOpaque: true);
        }

        public string? RelativePath { get; }
        public bool IsIdentityFallback { get; }
        public int Width => EnhancedLutAddressing.TextureWidth;
        public int Height => EnhancedLutAddressing.TextureHeight;
        public ReadOnlyMemory<byte> Rgba8 => _rgba8;
        public TextureIdentity TextureIdentity { get; }
        public RenderTexturePixels Pixels { get; }

        private static long StableRevision(ReadOnlySpan<byte> bytes)
        {
            // FNV-1a is deterministic across processes and sufficient for the
            // immutable texture-revision contract; this is not a trust hash.
            ulong hash = 14695981039346656037UL;
            foreach (byte value in bytes)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return unchecked((long)hash);
        }

        internal static EnhancedColorGradeLut CreateIdentity()
        {
            byte[] rgba8 = new byte[checked(EnhancedLutAddressing.TextureWidth
                * EnhancedLutAddressing.TextureHeight * 4)];
            int offset = 0;
            for (int green = 0; green < EnhancedLutAddressing.Dimension; green++)
            {
                for (int blue = 0; blue < EnhancedLutAddressing.Dimension; blue++)
                {
                    for (int red = 0; red < EnhancedLutAddressing.Dimension; red++)
                    {
                        rgba8[offset++] = checked((byte)(red * 17));
                        rgba8[offset++] = checked((byte)(green * 17));
                        rgba8[offset++] = checked((byte)(blue * 17));
                        rgba8[offset++] = Byte.MaxValue;
                    }
                }
            }
            return new EnhancedColorGradeLut(relativePath: null, rgba8,
                isIdentityFallback: true);
        }

        private enum EnhancedColorGradeTextureKind : byte
        {
            Lut
        }
    }

    public readonly record struct EnhancedColorGradeSelection(
        EnhancedEnvironmentAssetKey? Key,
        EnhancedColorGradeLut Lut)
    {
        public bool UsesIdentityFallback => Lut.IsIdentityFallback;
    }

    /// <summary>
    /// Immutable backend-neutral color-grade state captured at the same time
    /// as the room environment. Identity fallback is represented as Disabled
    /// so backends can skip both the upload and the post pass.
    /// </summary>
    public readonly record struct RenderColorGradeState
    {
        public RenderColorGradeState(TextureIdentity lutTexture, float strength)
        {
            if (lutTexture.Source == null)
                throw new ArgumentException("Color-grade LUT identity must be initialized.",
                    nameof(lutTexture));
            if (!float.IsFinite(strength) || strength < 0 || strength > 1)
                throw new ArgumentOutOfRangeException(nameof(strength));
            LutTexture = lutTexture;
            Strength = strength;
        }

        public TextureIdentity? LutTexture { get; }
        public float Strength { get; }
        public bool Enabled => LutTexture.HasValue && Strength > 0;

        public static RenderColorGradeState Disabled => default;

        public static RenderColorGradeState FromSelection(
            EnhancedColorGradeSelection selection, float strength = 1)
        {
            if (!float.IsFinite(strength) || strength < 0 || strength > 1)
                throw new ArgumentOutOfRangeException(nameof(strength));
            return selection.UsesIdentityFallback || strength == 0
                ? Disabled
                : new RenderColorGradeState(selection.Lut.TextureIdentity, strength);
        }
    }

    /// <summary>Deterministic lookup with a built-in identity LUT fallback.</summary>
    public sealed class EnhancedColorGradeResolver
    {
        private readonly IReadOnlyDictionary<EnhancedEnvironmentAssetKey,
            EnhancedColorGradeLut> _luts;

        internal EnhancedColorGradeResolver(
            IReadOnlyDictionary<EnhancedEnvironmentAssetKey, EnhancedColorGradeLut> luts)
            => _luts = luts;

        public int Count => _luts.Count;
        public EnhancedColorGradeLut IdentityFallback => _identityFallback;

        public EnhancedColorGradeSelection Resolve(EnhancedEnvironment environment)
            => Resolve(environment.LutKey);

        public EnhancedColorGradeSelection Resolve(EnhancedEnvironmentAssetKey? key)
        {
            if (key.HasValue && key.Value.IsValid
                && _luts.TryGetValue(key.Value, out EnhancedColorGradeLut? lut))
            {
                return new EnhancedColorGradeSelection(key, lut);
            }
            return new EnhancedColorGradeSelection(key, _identityFallback);
        }

        public bool TryGet(EnhancedEnvironmentAssetKey key, out EnhancedColorGradeLut? lut)
        {
            if (!key.IsValid)
            {
                lut = null;
                return false;
            }
            return _luts.TryGetValue(key, out lut);
        }

        private static readonly EnhancedColorGradeLut _identityFallback
            = EnhancedColorGradeLut.CreateIdentity();

        internal static EnhancedColorGradeResolver Empty { get; }
            = new(new Dictionary<EnhancedEnvironmentAssetKey, EnhancedColorGradeLut>());
    }

    public sealed record EnhancedColorGradeManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public EnhancedColorGradeDeclaration[] Luts { get; init; }
            = Array.Empty<EnhancedColorGradeDeclaration>();
    }

    public sealed record EnhancedColorGradeDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public string Texture { get; init; } = String.Empty;
    }

    public enum EnhancedColorGradePackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyLuts,
        InvalidLut,
        DuplicateLutKey,
        UnsafeTexturePath,
        MissingTexture,
        TextureTooLarge,
        UnsupportedTexture,
        InvalidDimensions,
        TextureDecodeFailed,
        AggregateLimitExceeded
    }

    public sealed record EnhancedColorGradePackIssue(
        EnhancedColorGradePackIssueKind Kind,
        string Message,
        EnhancedEnvironmentAssetKey? Key = null,
        string? RelativePath = null);

    public sealed record EnhancedColorGradePackLoadResult(
        EnhancedColorGradeResolver Resolver,
        IReadOnlyList<EnhancedColorGradePackIssue> Issues)
    {
        public bool HasIssues => Issues.Count != 0;
    }

    /// <summary>
    /// Loads an optional color-grade pack once. Invalid declarations resolve
    /// to the built-in identity LUT and never prevent a room from loading.
    /// </summary>
    public static class EnhancedColorGradePackLoader
    {
        public const string ManifestFileName = "color-grades.json";

        private static readonly JsonSerializerOptions _json
            = new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };

        public static EnhancedColorGradePackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder = null)
            => Load(packRoot, decoder ?? DecodedRgbImage.DecodePng,
                EnhancedColorGradePackLimits.MaximumAggregateEncodedBytes,
                EnhancedColorGradePackLimits.MaximumAggregateDecodedBytes);

        internal static EnhancedColorGradePackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage> decoder,
            long maximumAggregateEncodedBytes, long maximumAggregateDecodedBytes)
        {
            ArgumentNullException.ThrowIfNull(decoder);
            var issues = new List<EnhancedColorGradePackIssue>();
            if (!TryGetRoot(packRoot, out string root))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.MissingManifest,
                    "Color-grade pack root is invalid."));
                return Result(EnhancedColorGradeResolver.Empty, issues);
            }
            if (!EnhancedColorGradeResourceBudget.TryCreate(maximumAggregateEncodedBytes,
                maximumAggregateDecodedBytes, out EnhancedColorGradeResourceBudget? budget))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.AggregateLimitExceeded,
                    "Color-grade pack resource budget is invalid."));
                return Result(EnhancedColorGradeResolver.Empty, issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] manifestBytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return Result(EnhancedColorGradeResolver.Empty, issues);
                }
                if (!TryReadBoundedFile(manifestPath,
                    EnhancedColorGradePackLimits.MaximumManifestBytes, out manifestBytes))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and "
                            + $"{EnhancedColorGradePackLimits.MaximumManifestBytes} bytes."));
                    return Result(EnhancedColorGradeResolver.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return Result(EnhancedColorGradeResolver.Empty, issues);
            }

            List<EnhancedColorGradeDeclaration>? declarations
                = ParseManifest(manifestBytes, issues);
            if (declarations is null)
                return Result(EnhancedColorGradeResolver.Empty, issues);

            var candidates = new List<(EnhancedEnvironmentAssetKey Key,
                EnhancedColorGradeDeclaration Declaration)>();
            foreach (EnhancedColorGradeDeclaration declaration in declarations)
            {
                if (!EnhancedEnvironmentAssetKey.TryParse(declaration.Key,
                        out EnhancedEnvironmentAssetKey key)
                    || !key.Value.StartsWith("luts/", StringComparison.Ordinal))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.InvalidLut,
                        "Color-grade LUT key must be a canonical luts/... environment asset key."));
                    continue;
                }
                candidates.Add((key, declaration));
            }

            HashSet<EnhancedEnvironmentAssetKey> duplicates = candidates
                .GroupBy(candidate => candidate.Key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            foreach (EnhancedEnvironmentAssetKey duplicate in duplicates.OrderBy(key => key.Value,
                StringComparer.Ordinal))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.DuplicateLutKey,
                    "Duplicate color-grade LUT key was ignored.", duplicate));
            }

            var assets = new Dictionary<string, EnhancedColorGradeLut?>(PathComparer());
            var luts = new Dictionary<EnhancedEnvironmentAssetKey, EnhancedColorGradeLut>();
            foreach ((EnhancedEnvironmentAssetKey key,
                EnhancedColorGradeDeclaration declaration) in candidates)
            {
                if (duplicates.Contains(key)) continue;
                EnhancedColorGradeLut? lut = LoadLut(root, key, declaration.Texture,
                    decoder, assets, budget!, issues);
                if (lut is not null) luts.Add(key, lut);
            }
            return Result(new EnhancedColorGradeResolver(luts), issues);
        }

        private static List<EnhancedColorGradeDeclaration>? ParseManifest(byte[] bytes,
            List<EnhancedColorGradePackIssue> issues)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 12 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Color-grade manifest must be an object.");
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "luts"))
                        throw new JsonException($"Unknown manifest property '{property.Name}'.");
                }
                if (!root.TryGetProperty("format", out JsonElement formatElement))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.UnsupportedManifestVersion,
                        "Color-grade manifest format 0 is not supported."));
                    return null;
                }
                int format = formatElement.GetInt32();
                if (format != EnhancedColorGradeManifest.CurrentFormat)
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.UnsupportedManifestVersion,
                        $"Color-grade manifest format {format} is not supported."));
                    return null;
                }
                if (!root.TryGetProperty("luts", out JsonElement luts)
                    || luts.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Color-grade manifest luts must be an array.");
                }

                var declarations = new List<EnhancedColorGradeDeclaration>();
                int count = 0;
                foreach (JsonElement entry in luts.EnumerateArray())
                {
                    count++;
                    if (count > EnhancedColorGradePackLimits.MaximumLuts)
                    {
                        issues.Add(new EnhancedColorGradePackIssue(
                            EnhancedColorGradePackIssueKind.TooManyLuts,
                            $"Color-grade manifest exceeds "
                                + $"{EnhancedColorGradePackLimits.MaximumLuts} LUTs."));
                        return null;
                    }
                    try
                    {
                        EnhancedColorGradeDeclaration? declaration
                            = entry.Deserialize<EnhancedColorGradeDeclaration>(_json);
                        if (declaration is null)
                            throw new JsonException("Color-grade LUT entry is null.");
                        declarations.Add(declaration);
                    }
                    catch (Exception error) when (error is JsonException or NotSupportedException)
                    {
                        issues.Add(new EnhancedColorGradePackIssue(
                            EnhancedColorGradePackIssueKind.InvalidLut,
                            "Malformed color-grade LUT entry was ignored."));
                    }
                }
                return declarations;
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or InvalidOperationException or FormatException or OverflowException)
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.MalformedManifest,
                    "Color-grade manifest is malformed."));
                return null;
            }
        }

        private static EnhancedColorGradeLut? LoadLut(string root,
            EnhancedEnvironmentAssetKey key, string relativePath,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage> decoder,
            Dictionary<string, EnhancedColorGradeLut?> assets,
            EnhancedColorGradeResourceBudget budget,
            List<EnhancedColorGradePackIssue> issues)
        {
            if (!TryNormalizePngPath(relativePath, out string normalized))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.UnsafeTexturePath,
                    "LUT path must be a contained relative PNG path.", key, relativePath));
                return null;
            }
            if (assets.TryGetValue(normalized, out EnhancedColorGradeLut? cached))
                return cached;
            // Cache failure as well as success so repeated declarations cannot
            // force repeated file I/O or decoding.
            assets.Add(normalized, null);

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = Path.TrimEndingDirectorySeparator(root)
                    + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison())
                    || HasReparsePoint(root, normalized))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.UnsafeTexturePath,
                        "LUT path escapes the color-grade pack or crosses a link.",
                        key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.UnsafeTexturePath,
                    "LUT path is invalid.", key, normalized));
                return null;
            }

            byte[] encoded;
            try
            {
                if (!File.Exists(candidate))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.MissingTexture,
                        "LUT texture was not found.", key, normalized));
                    return null;
                }
                if (!TryReadBoundedFile(candidate,
                    EnhancedColorGradePackLimits.MaximumTextureFileBytes, out encoded))
                {
                    issues.Add(new EnhancedColorGradePackIssue(
                        EnhancedColorGradePackIssueKind.TextureTooLarge,
                        "LUT texture exceeds its encoded byte limit.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.MissingTexture,
                    "LUT texture could not be read.", key, normalized));
                return null;
            }

            if (!TryReadPngDimensions(encoded, out int width, out int height))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.UnsupportedTexture,
                    "LUT texture does not have a valid PNG header.", key, normalized));
                return null;
            }
            if (width != EnhancedLutAddressing.TextureWidth
                || height != EnhancedLutAddressing.TextureHeight)
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.InvalidDimensions,
                    $"LUT texture must be exactly {EnhancedLutAddressing.TextureWidth}x"
                        + $"{EnhancedLutAddressing.TextureHeight} pixels.", key, normalized));
                return null;
            }

            DecodedRgbImage decoded;
            try
            {
                decoded = decoder(encoded);
                if (decoded.Width != width || decoded.Height != height)
                    throw new InvalidDataException("Decoded dimensions do not match PNG IHDR.");
            }
            catch (Exception error) when (error is not OutOfMemoryException
                and not StackOverflowException)
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.TextureDecodeFailed,
                    "LUT texture decoder rejected the PNG.", key, normalized));
                return null;
            }

            int rgbaLength = checked(width * height * 4);
            if (!budget.TryReserve(encoded.LongLength, rgbaLength))
            {
                issues.Add(new EnhancedColorGradePackIssue(
                    EnhancedColorGradePackIssueKind.AggregateLimitExceeded,
                    "LUT texture would exceed the color-grade pack resource budget.",
                    key, normalized));
                return null;
            }

            byte[] rgba8 = new byte[rgbaLength];
            ReadOnlySpan<byte> rgb = decoded.Pixels.Span;
            for (int source = 0, destination = 0; source < rgb.Length;
                source += 3, destination += 4)
            {
                rgba8[destination] = rgb[source];
                rgba8[destination + 1] = rgb[source + 1];
                rgba8[destination + 2] = rgb[source + 2];
                rgba8[destination + 3] = Byte.MaxValue;
            }
            var asset = new EnhancedColorGradeLut(normalized, rgba8,
                isIdentityFallback: false);
            assets[normalized] = asset;
            return asset;
        }

        private static EnhancedColorGradePackLoadResult Result(
            EnhancedColorGradeResolver resolver,
            List<EnhancedColorGradePackIssue> issues)
            => new(resolver, Array.AsReadOnly(issues.ToArray()));

        private static bool TryGetRoot(string packRoot, out string root)
        {
            root = String.Empty;
            try
            {
                root = Path.GetFullPath(packRoot
                    ?? throw new ArgumentNullException(nameof(packRoot)));
                return true;
            }
            catch (Exception error) when (error is ArgumentException
                or NotSupportedException or PathTooLongException or SecurityException)
            {
                return false;
            }
        }

        private static bool TryNormalizePngPath(string? path, out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path)
                || path.Length > EnhancedColorGradePackLimits.MaximumRelativePathLength
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.Any(Char.IsControl))
            {
                return false;
            }
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length is 0 or > 128
                || segment is "." or ".."))
            {
                return false;
            }
            normalized = String.Join('/', segments);
            return true;
        }

        private static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes,
            out int width, out int height)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            width = 0;
            height = 0;
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
                || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
                || !bytes[12..16].SequenceEqual("IHDR"u8))
            {
                return false;
            }
            uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > Int32.MaxValue
                || rawHeight > Int32.MaxValue)
            {
                return false;
            }
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static bool TryReadBoundedFile(string path, long maximumBytes,
            out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
            long length = stream.Length;
            if (length is <= 0 || length > maximumBytes || length > Int32.MaxValue)
                return false;
            bytes = GC.AllocateUninitializedArray<byte>((int)length);
            stream.ReadExactly(bytes);
            if (stream.ReadByte() == -1) return true;
            bytes = Array.Empty<byte>();
            return false;
        }

        private static void RejectDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new JsonException("Duplicate JSON property.");
                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    RejectDuplicateProperties(child);
            }
        }

        private static bool HasReparsePoint(string root, string normalized)
        {
            if (!Directory.Exists(root)
                || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
            string current = root;
            foreach (string part in normalized.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
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
    }

    /// <summary>CPU reference for the post-tone-map 16-cube shader contract.</summary>
    internal static class EnhancedColorGradeSampling
    {
        public static Vector3 Sample(EnhancedColorGradeLut lut, Vector3 displayColor,
            float strength)
        {
            ArgumentNullException.ThrowIfNull(lut);
            if (!IsUnit(displayColor))
                throw new ArgumentOutOfRangeException(nameof(displayColor));
            if (!float.IsFinite(strength) || strength < 0 || strength > 1)
                throw new ArgumentOutOfRangeException(nameof(strength));

            EnhancedLutSample address = EnhancedLutAddressing.Address(displayColor);
            Vector3 lower = Bilinear(lut, address.LowerUv);
            Vector3 upper = Bilinear(lut, address.UpperUv);
            Vector3 graded = Vector3.Lerp(lower, upper, address.SliceBlend);
            return Vector3.Lerp(displayColor, graded, strength);
        }

        private static Vector3 Bilinear(EnhancedColorGradeLut lut, Vector2 uv)
        {
            float pixelX = Math.Clamp(uv.X * lut.Width - 0.5f, 0, lut.Width - 1);
            float pixelY = Math.Clamp(uv.Y * lut.Height - 0.5f, 0, lut.Height - 1);
            int x0 = (int)MathF.Floor(pixelX);
            int y0 = (int)MathF.Floor(pixelY);
            int x1 = Math.Min(x0 + 1, lut.Width - 1);
            int y1 = Math.Min(y0 + 1, lut.Height - 1);
            float blendX = pixelX - x0;
            float blendY = pixelY - y0;
            ReadOnlySpan<byte> pixels = lut.Rgba8.Span;
            Vector3 top = Vector3.Lerp(Read(pixels, lut.Width, x0, y0),
                Read(pixels, lut.Width, x1, y0), blendX);
            Vector3 bottom = Vector3.Lerp(Read(pixels, lut.Width, x0, y1),
                Read(pixels, lut.Width, x1, y1), blendX);
            return Vector3.Lerp(top, bottom, blendY);
        }

        private static Vector3 Read(ReadOnlySpan<byte> pixels, int width, int x, int y)
        {
            int offset = checked((y * width + x) * 4);
            return new Vector3(pixels[offset] / 255f, pixels[offset + 1] / 255f,
                pixels[offset + 2] / 255f);
        }

        private static bool IsUnit(Vector3 value)
            => float.IsFinite(value.X) && value.X >= 0 && value.X <= 1
                && float.IsFinite(value.Y) && value.Y >= 0 && value.Y <= 1
                && float.IsFinite(value.Z) && value.Z >= 0 && value.Z <= 1;
    }
}
