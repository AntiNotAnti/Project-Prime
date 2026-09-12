using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Imaging;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Human-authored texture identity. Unlike <see cref="TextureIdentity"/>,
    /// this value is stable across process runs and contains no object identity.
    /// </summary>
    public readonly struct TextureAssetKey : IEquatable<TextureAssetKey>, IComparable<TextureAssetKey>
    {
        public const int MaximumLength = 256;
        public const int MaximumSegmentLength = 64;
        private readonly string? _value;

        private TextureAssetKey(string canonical) => _value = canonical;

        public bool IsValid => _value is not null;
        public string Value => _value ?? String.Empty;

        public static TextureAssetKey ForRoom(string room, int textureId, int paletteId)
            => Parse($"room/{room}/texture/{textureId}/palette/{paletteId}");

        public static TextureAssetKey ForModel(string model, int textureId, int paletteId, int recolorId)
            => Parse($"model/{model}/texture/{textureId}/palette/{paletteId}/recolor/{recolorId}");

        public static TextureAssetKey ForEffect(string effect, int textureId)
            => Parse($"effect/{effect}/texture/{textureId}");

        public static TextureAssetKey ForCustom(params string[] path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Parse("custom/" + String.Join('/', path));
        }

        public static TextureAssetKey Parse(string value)
        {
            if (!TryParse(value, out TextureAssetKey key))
            {
                throw new FormatException("Texture asset key is not in a supported canonical format.");
            }
            return key;
        }

        public static bool TryParse(string? value, out TextureAssetKey key)
        {
            key = default;
            if (String.IsNullOrEmpty(value) || value.Length > MaximumLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.Contains('\\') || value.Contains(':') || value.Contains("//", StringComparison.Ordinal))
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

            key = new TextureAssetKey(String.Join('/', canonical));
            return true;
        }

        private static bool HasSupportedShape(string[] segments)
        {
            if (segments.Length == 6 && EqualsIgnoreCase(segments[0], "room"))
            {
                return EqualsIgnoreCase(segments[2], "texture")
                    && EqualsIgnoreCase(segments[4], "palette");
            }
            if (segments.Length == 8 && EqualsIgnoreCase(segments[0], "model"))
            {
                return EqualsIgnoreCase(segments[2], "texture")
                    && EqualsIgnoreCase(segments[4], "palette")
                    && EqualsIgnoreCase(segments[6], "recolor");
            }
            if (segments.Length == 4 && EqualsIgnoreCase(segments[0], "effect"))
            {
                return EqualsIgnoreCase(segments[2], "texture");
            }
            return segments.Length >= 2 && EqualsIgnoreCase(segments[0], "custom");
        }

        private static bool IsNumericPosition(string[] segments, int index)
            => (EqualsIgnoreCase(segments[0], "room") && (index == 3 || index == 5))
                || (EqualsIgnoreCase(segments[0], "model") && (index == 3 || index == 5 || index == 7))
                || (EqualsIgnoreCase(segments[0], "effect") && index == 3);

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

        public bool Equals(TextureAssetKey other)
            => String.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is TextureAssetKey other && Equals(other);
        public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public int CompareTo(TextureAssetKey other) => String.Compare(_value, other._value, StringComparison.Ordinal);
        public override string ToString() => Value;
        public static bool operator ==(TextureAssetKey left, TextureAssetKey right) => left.Equals(right);
        public static bool operator !=(TextureAssetKey left, TextureAssetKey right) => !left.Equals(right);
    }

    public enum EnhancedTextureChannel
    {
        Albedo,
        Normal,
        Emissive
    }

    public static class EnhancementPackLimits
    {
        public const int MaximumManifestBytes = 1024 * 1024;
        public const int MaximumMaterials = 4096;
        public const long MaximumTextureFileBytes = 64L * 1024 * 1024;
        public const int MaximumTextureDimension = 8192;
        public const long MaximumDecodedTextureBytes = 256L * 1024 * 1024;
        public const long MaximumAggregateEncodedBytes = 256L * 1024 * 1024;
        public const long MaximumAggregateDecodedBytes = 512L * 1024 * 1024;
    }

    internal sealed class EnhancementResourceBudget
    {
        private readonly long _maximumEncoded;
        private readonly long _maximumDecoded;
        private long _encoded;
        private long _decoded;

        private EnhancementResourceBudget(long maximumEncoded, long maximumDecoded)
        {
            _maximumEncoded = maximumEncoded;
            _maximumDecoded = maximumDecoded;
        }

        internal static bool TryCreate(long maximumEncoded, long maximumDecoded,
            out EnhancementResourceBudget? budget)
        {
            if (maximumEncoded <= 0 || maximumDecoded <= 0)
            {
                budget = null;
                return false;
            }
            budget = new EnhancementResourceBudget(maximumEncoded, maximumDecoded);
            return true;
        }

        internal bool TryReserve(long encoded, long decoded)
        {
            if (encoded < 0 || decoded < 0 || encoded > _maximumEncoded - _encoded
                || decoded > _maximumDecoded - _decoded) return false;
            _encoded += encoded;
            _decoded += decoded;
            return true;
        }

        internal void Release(long encoded, long decoded)
        {
            if (encoded < 0 || decoded < 0 || encoded > _encoded
                || decoded > _decoded)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            _encoded -= encoded;
            _decoded -= decoded;
        }
    }

    public sealed record EnhancementManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public EnhancedMaterialDeclaration[] Materials { get; init; }
            = Array.Empty<EnhancedMaterialDeclaration>();
    }

    public sealed record EnhancedMaterialDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public string? Albedo { get; init; }
        public string? Normal { get; init; }
        public string? Emissive { get; init; }
        public float? SpecularStrength { get; init; }
        public float? Smoothness { get; init; }
        public float? ReflectionStrength { get; init; }
        public float[]? EmissionTint { get; init; }
        public float? EmissionStrength { get; init; }
    }

    public enum EnhancementPackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyMaterials,
        InvalidMaterial,
        DuplicateMaterialKey,
        UnsafeTexturePath,
        MissingTexture,
        TextureTooLarge,
        UnsupportedTexture,
        TextureDecodeFailed,
        AggregateLimitExceeded
    }

    public sealed record EnhancementPackIssue(
        EnhancementPackIssueKind Kind,
        string Message,
        TextureAssetKey? Key = null,
        string? RelativePath = null);

    /// <summary>A bounded, validated PNG owned by one enhancement resolver.</summary>
    public sealed class EnhancedTextureAsset
    {
        internal EnhancedTextureAsset(string relativePath, int width, int height,
            byte[] encodedSource, byte[] rgba8, bool cooked = false)
        {
            RelativePath = relativePath;
            Width = width;
            Height = height;
            EncodedSource = encodedSource;
            IsCooked = cooked;
            if (rgba8.Length != RenderTexturePixels.ValidateRgba8ByteCount(width, height))
                throw new ArgumentException("Decoded replacement pixels must be tightly packed RGBA8.", nameof(rgba8));
            Rgba8 = rgba8;
        }

        public string RelativePath { get; }
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> EncodedSource { get; }
        public ReadOnlyMemory<byte> EncodedPng => EncodedSource;
        public bool IsCooked { get; }
        /// <summary>
        /// Immutable-for-the-pack decoded pixels retained for backend upload.
        /// The manifest loader performs this conversion exactly once.
        /// </summary>
        public ReadOnlyMemory<byte> Rgba8 { get; }
        public long DecodedRgba8Bytes => checked((long)Width * Height * 4);

        internal TextureIdentity Identity(EnhancedTextureChannel channel)
            => new(this, variant: channel);
    }

    public sealed class EnhancedMaterialReplacement
    {
        internal EnhancedMaterialReplacement(TextureAssetKey key, EnhancedTextureAsset? albedo,
            EnhancedTextureAsset? normal, EnhancedTextureAsset? emissive,
            float? specularStrength, float? smoothness, float? reflectionStrength,
            Vector3? emissionTint, float? emissionStrength)
        {
            Key = key;
            Albedo = albedo;
            Normal = normal;
            Emissive = emissive;
            SpecularStrength = specularStrength;
            Smoothness = smoothness;
            ReflectionStrength = reflectionStrength;
            EmissionTint = emissionTint;
            EmissionStrength = emissionStrength;
        }

        public TextureAssetKey Key { get; }
        public EnhancedTextureAsset? Albedo { get; }
        public EnhancedTextureAsset? Normal { get; }
        public EnhancedTextureAsset? Emissive { get; }
        public float? SpecularStrength { get; }
        public float? Smoothness { get; }
        public float? ReflectionStrength { get; }
        public Vector3? EmissionTint { get; }
        public float? EmissionStrength { get; }

        internal EnhancedMaterial Apply(RenderMaterial original)
        {
            EnhancedMaterial fallback = EnhancedMaterial.FromOriginal(original);
            return fallback with
            {
                Albedo = Albedo?.Identity(EnhancedTextureChannel.Albedo) ?? fallback.Albedo,
                Normal = Normal?.Identity(EnhancedTextureChannel.Normal),
                Emissive = Emissive?.Identity(EnhancedTextureChannel.Emissive),
                SpecularStrength = SpecularStrength ?? fallback.SpecularStrength,
                Smoothness = Smoothness ?? fallback.Smoothness,
                ReflectionStrength = ReflectionStrength ?? fallback.ReflectionStrength,
                EmissionTint = EmissionTint ?? fallback.EmissionTint,
                EmissionStrength = EmissionStrength ?? fallback.EmissionStrength
            };
        }
    }

    public sealed class EnhancedTextureResolver
    {
        private readonly IReadOnlyDictionary<TextureAssetKey, EnhancedMaterialReplacement> _materials;

        internal EnhancedTextureResolver(IReadOnlyDictionary<TextureAssetKey, EnhancedMaterialReplacement> materials)
            => _materials = materials;

        public int Count => _materials.Count;

        public EnhancedMaterial Resolve(TextureAssetKey key, RenderMaterial original)
            => key.IsValid && _materials.TryGetValue(key, out EnhancedMaterialReplacement? replacement)
                ? replacement.Apply(original)
                : EnhancedMaterial.FromOriginal(original);

        public bool TryGetReplacement(TextureAssetKey key, out EnhancedMaterialReplacement? replacement)
        {
            if (!key.IsValid)
            {
                replacement = null;
                return false;
            }
            return _materials.TryGetValue(key, out replacement);
        }

        internal static EnhancedTextureResolver Empty { get; }
            = new(new Dictionary<TextureAssetKey, EnhancedMaterialReplacement>());
    }

    public sealed record EnhancementPackLoadResult(
        EnhancedTextureResolver Resolver,
        IReadOnlyList<EnhancementPackIssue> Issues)
    {
        public bool HasIssues => Issues.Count != 0;
    }

    /// <summary>
    /// Loads a local, optional enhancement pack once. Every failure is reported
    /// as an issue and resolves back to the original material rather than
    /// becoming a gameplay/startup failure.
    /// </summary>
    public static class EnhancementPackLoader
    {
        public const string ManifestFileName = "materials.json";

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static EnhancementPackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, RgbImage>? decoder = null)
        {
            var issues = new List<EnhancementPackIssue>();
            string root;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException)
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MissingManifest,
                    "Enhancement pack root is invalid."));
                return new EnhancementPackLoadResult(EnhancedTextureResolver.Empty, issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] manifestBytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return new EnhancementPackLoadResult(EnhancedTextureResolver.Empty, issues);
                }
                if (!TryReadBoundedFile(manifestPath, EnhancementPackLimits.MaximumManifestBytes,
                    out manifestBytes))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and {EnhancementPackLimits.MaximumManifestBytes} bytes."));
                    return new EnhancementPackLoadResult(EnhancedTextureResolver.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return new EnhancementPackLoadResult(EnhancedTextureResolver.Empty, issues);
            }

            List<EnhancedMaterialDeclaration>? declarations = ParseManifest(manifestBytes, issues);
            if (declarations is null)
            {
                return new EnhancementPackLoadResult(EnhancedTextureResolver.Empty, issues);
            }

            var candidates = new List<(TextureAssetKey Key, EnhancedMaterialDeclaration Declaration)>();
            foreach (EnhancedMaterialDeclaration declaration in declarations)
            {
                if (!TextureAssetKey.TryParse(declaration.Key, out TextureAssetKey key))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.InvalidMaterial,
                        "Material has an invalid texture asset key."));
                    continue;
                }
                candidates.Add((key, declaration));
            }

            HashSet<TextureAssetKey> duplicateKeys = candidates
                .GroupBy(candidate => candidate.Key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            foreach (TextureAssetKey duplicate in duplicateKeys)
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.DuplicateMaterialKey,
                    "Duplicate material key was ignored.", duplicate));
            }

            var assets = new Dictionary<string, EnhancedTextureAsset?>(PathComparer());
            var replacements = new Dictionary<TextureAssetKey, EnhancedMaterialReplacement>();
            _ = EnhancementResourceBudget.TryCreate(EnhancementPackLimits.MaximumAggregateEncodedBytes,
                EnhancementPackLimits.MaximumAggregateDecodedBytes, out EnhancementResourceBudget? budget);
            foreach ((TextureAssetKey key, EnhancedMaterialDeclaration declaration) in candidates)
            {
                if (duplicateKeys.Contains(key)) continue;

                EnhancedTextureAsset? albedo = LoadAsset(root, key, declaration.Albedo, decoder,
                    assets, issues, budget!);
                EnhancedTextureAsset? normal = LoadAsset(root, key, declaration.Normal, decoder,
                    assets, issues, budget!);
                EnhancedTextureAsset? emissive = LoadAsset(root, key, declaration.Emissive, decoder,
                    assets, issues, budget!);

                float? specular = ValidateUnit(declaration.SpecularStrength, "specularStrength", key, issues);
                float? smoothness = ValidateUnit(declaration.Smoothness, "smoothness", key, issues);
                float? reflection = ValidateUnit(declaration.ReflectionStrength, "reflectionStrength", key, issues);
                float? emissionStrength = ValidateRange(declaration.EmissionStrength, 0, 16,
                    "emissionStrength", key, issues);
                Vector3? emissionTint = ValidateTint(declaration.EmissionTint, key, issues);

                replacements.Add(key, new EnhancedMaterialReplacement(key, albedo, normal, emissive,
                    specular, smoothness, reflection, emissionTint, emissionStrength));
            }

            return new EnhancementPackLoadResult(new EnhancedTextureResolver(replacements), issues);
        }

        private static List<EnhancedMaterialDeclaration>? ParseManifest(byte[] bytes,
            List<EnhancementPackIssue> issues)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                int format = EnhancementManifest.CurrentFormat;
                IEnumerable<JsonElement> entries;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    entries = root.EnumerateArray();
                }
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("materials", out JsonElement materials))
                {
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name is not ("format" or "materials"))
                            throw new JsonException($"Unknown manifest property '{property.Name}'.");
                    }
                    if (root.TryGetProperty("format", out JsonElement formatElement))
                        format = formatElement.GetInt32();
                    if (materials.ValueKind != JsonValueKind.Array) throw new JsonException("materials must be an array.");
                    entries = materials.EnumerateArray();
                }
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("key", out _))
                {
                    // The plan's authoring example is a single material object;
                    // accept it as a one-entry pack as well as the versioned form.
                    entries = new[] { root };
                }
                else
                {
                    throw new JsonException("Manifest must be a material, an array, or a versioned materials object.");
                }

                if (format != EnhancementManifest.CurrentFormat)
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.UnsupportedManifestVersion,
                        $"Manifest format {format} is not supported."));
                    return null;
                }

                var result = new List<EnhancedMaterialDeclaration>();
                int entryCount = 0;
                foreach (JsonElement entry in entries)
                {
                    entryCount++;
                    if (entryCount > EnhancementPackLimits.MaximumMaterials)
                    {
                        issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.TooManyMaterials,
                            $"Manifest exceeds {EnhancementPackLimits.MaximumMaterials} materials."));
                        return null;
                    }
                    try
                    {
                        EnhancedMaterialDeclaration? declaration = entry.Deserialize<EnhancedMaterialDeclaration>(_json);
                        if (declaration is null) throw new JsonException("Material is null.");
                        result.Add(declaration);
                    }
                    catch (Exception error) when (error is JsonException or NotSupportedException)
                    {
                        issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.InvalidMaterial,
                            "Malformed material entry was ignored."));
                    }
                }
                return result;
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or InvalidOperationException or FormatException or OverflowException)
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MalformedManifest,
                    "Enhancement manifest is malformed."));
                return null;
            }
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

        private static EnhancedTextureAsset? LoadAsset(string root, TextureAssetKey key, string? relativePath,
            Func<ReadOnlyMemory<byte>, RgbImage>? decoder, Dictionary<string, EnhancedTextureAsset?> assets,
            List<EnhancementPackIssue> issues, EnhancementResourceBudget budget)
        {
            if (relativePath is null) return null;
            if (!TryNormalizeTexturePath(relativePath, out string normalized))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.UnsafeTexturePath,
                    "Texture path must be a contained relative PNG or PPTE path.",
                    key, relativePath));
                return null;
            }
            if (assets.TryGetValue(normalized, out EnhancedTextureAsset? existing)) return existing;
            // Cache failures too. A manifest cannot force repeated I/O or
            // decoding by referencing the same malformed asset many times.
            assets.Add(normalized, null);

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison()) || HasReparsePoint(root, normalized))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.UnsafeTexturePath,
                        "Texture path escapes the enhancement pack or crosses a link.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.UnsafeTexturePath,
                    "Texture path is invalid.", key, normalized));
                return null;
            }

            byte[] encoded;
            try
            {
                if (!File.Exists(candidate))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MissingTexture,
                        "Texture file was not found.", key, normalized));
                    return null;
                }
                if (!TryReadBoundedFile(candidate, EnhancementPackLimits.MaximumTextureFileBytes,
                    out encoded))
                {
                    issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.TextureTooLarge,
                        "Texture file exceeds the per-file encoded byte limit.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.MissingTexture,
                    "Texture file could not be read.", key, normalized));
                return null;
            }

            bool cooked = normalized.EndsWith(".ppte",
                StringComparison.OrdinalIgnoreCase);
            int width;
            int height;
            byte[] rgba8 = Array.Empty<byte>();
            if (cooked)
            {
                if (!EnhancedTextureCookedFormat.TryReadHeader(encoded,
                    out width, out height, out _))
                {
                    issues.Add(new EnhancementPackIssue(
                        EnhancementPackIssueKind.TextureDecodeFailed,
                        "Cooked texture header failed validation.", key,
                        normalized));
                    return null;
                }
            }
            else if (!TryReadPngDimensions(encoded, out width, out height))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.UnsupportedTexture,
                    "Texture is not a valid PNG header.", key, normalized));
                return null;
            }
            long decodedBytes;
            try { decodedBytes = checked((long)width * height * 4); }
            catch (OverflowException)
            {
                decodedBytes = Int64.MaxValue;
            }
            if (width > EnhancementPackLimits.MaximumTextureDimension
                || height > EnhancementPackLimits.MaximumTextureDimension
                || decodedBytes > EnhancementPackLimits.MaximumDecodedTextureBytes)
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.TextureTooLarge,
                    "Texture dimensions exceed the decoded resource limit.", key, normalized));
                return null;
            }
            if (!budget.TryReserve(encoded.LongLength, decodedBytes))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.AggregateLimitExceeded,
                    "Texture would exceed the enhancement pack aggregate memory limit.", key, normalized));
                return null;
            }
            try
            {
                if (cooked)
                {
                    if (!EnhancedTextureCookedFormat.TryDecode(encoded,
                        out int decodedWidth, out int decodedHeight, out rgba8)
                        || decodedWidth != width || decodedHeight != height)
                    {
                        throw new InvalidDataException(
                            "Cooked texture failed validation or decompression.");
                    }
                }
                else if (decoder == null)
                {
                    RgbaImage image = StbImageDecoder.DecodeRgba(encoded);
                    if (image.Width != width || image.Height != height)
                        throw new InvalidDataException("Decoded PNG dimensions do not match IHDR.");
                    rgba8 = ValidateRgba(image.Pixels.Span, width, height);
                }
                else
                {
                    RgbImage image = decoder(encoded);
                    if (image.Width != width || image.Height != height)
                        throw new InvalidDataException("Decoded PNG dimensions do not match IHDR.");
                    rgba8 = ExpandRgbToRgba(image.Pixels.Span, width, height);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                budget.Release(encoded.LongLength, decodedBytes);
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.TextureDecodeFailed,
                    cooked ? "Cooked texture failed validation or decompression."
                        : "Texture decoder rejected the PNG.", key, normalized));
                return null;
            }

            var asset = new EnhancedTextureAsset(normalized, width, height,
                encoded, rgba8, cooked);
            assets[normalized] = asset;
            return asset;
        }

        private static byte[] ExpandRgbToRgba(ReadOnlySpan<byte> rgb, int width, int height)
        {
            int pixelCount = checked(width * height);
            if (rgb.Length != checked(pixelCount * 3))
                throw new InvalidDataException("Decoded PNG did not contain tightly packed RGB pixels.");
            byte[] rgba = GC.AllocateUninitializedArray<byte>(checked(pixelCount * 4));
            for (int source = 0, destination = 0; source < rgb.Length;
                source += 3, destination += 4)
            {
                rgba[destination] = rgb[source];
                rgba[destination + 1] = rgb[source + 1];
                rgba[destination + 2] = rgb[source + 2];
                rgba[destination + 3] = 255;
            }
            return rgba;
        }

        private static byte[] ValidateRgba(ReadOnlySpan<byte> rgba, int width,
            int height)
        {
            if (rgba.Length != checked(width * height * 4))
                throw new InvalidDataException("Decoded PNG did not contain tightly packed RGBA pixels.");
            return rgba.ToArray();
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
            // Reject a file that grew after Length was observed without ever
            // allocating beyond the configured bound.
            if (stream.ReadByte() != -1)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
            return true;
        }

        private static bool TryNormalizeTexturePath(string path,
            out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path) || path.Length > 512
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || !(path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".ppte", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length is 0 or > 128 || segment is "." or "..")) return false;
            normalized = String.Join('/', segments);
            return true;
        }

        internal static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
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
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > Int32.MaxValue || rawHeight > Int32.MaxValue)
                return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
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

        private static float? ValidateUnit(float? value, string name, TextureAssetKey key,
            List<EnhancementPackIssue> issues)
            => ValidateRange(value, 0, 1, name, key, issues);

        private static float? ValidateRange(float? value, float minimum, float maximum, string name,
            TextureAssetKey key, List<EnhancementPackIssue> issues)
        {
            if (value is null) return null;
            if (!float.IsFinite(value.Value) || value.Value < minimum || value.Value > maximum)
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.InvalidMaterial,
                    $"Material {name} must be between {minimum} and {maximum}.", key));
                return null;
            }
            return value;
        }

        private static Vector3? ValidateTint(float[]? value, TextureAssetKey key,
            List<EnhancementPackIssue> issues)
        {
            if (value is null) return null;
            if (value.Length != 3 || value.Any(component => !float.IsFinite(component) || component < 0 || component > 8))
            {
                issues.Add(new EnhancementPackIssue(EnhancementPackIssueKind.InvalidMaterial,
                    "Material emissionTint must contain three values between 0 and 8.", key));
                return null;
            }
            return new Vector3(value[0], value[1], value[2]);
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
}
