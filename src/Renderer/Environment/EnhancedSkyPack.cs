using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Imaging;

namespace MphRead
{
    /// <summary>A stable, human-authored identity for presentation-only sky content.</summary>
    public readonly struct EnhancedSkyAssetKey : IEquatable<EnhancedSkyAssetKey>,
        IComparable<EnhancedSkyAssetKey>
    {
        public const int MaximumLength = 256;
        public const int MaximumSegmentLength = 64;
        private readonly string? _value;

        private EnhancedSkyAssetKey(string value) => _value = value;

        public bool IsValid => _value is not null;
        public string Value => _value ?? String.Empty;

        public static EnhancedSkyAssetKey ForRoom(string room, string sky = "default")
            => Parse($"room/{room}/sky/{sky}");

        public static EnhancedSkyAssetKey ForCustom(params string[] path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Parse("custom/" + String.Join('/', path));
        }

        public static EnhancedSkyAssetKey Parse(string value)
        {
            if (!TryParse(value, out EnhancedSkyAssetKey key))
                throw new FormatException("Enhanced sky asset key is not in a supported canonical format.");
            return key;
        }

        public static bool TryParse(string? value, out EnhancedSkyAssetKey key)
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
            bool validShape = (segments.Length == 4
                    && EqualsIgnoreCase(segments[0], "room")
                    && EqualsIgnoreCase(segments[2], "sky"))
                || (segments.Length >= 2 && EqualsIgnoreCase(segments[0], "custom"));
            if (!validShape) return false;
            string[] canonical = new string[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i].ToLowerInvariant();
                if (segment.Length is 0 or > MaximumSegmentLength || !IsIdentifier(segment)) return false;
                canonical[i] = segment;
            }
            key = new EnhancedSkyAssetKey(String.Join('/', canonical));
            return true;
        }

        private static bool IsIdentifier(string value)
        {
            if (value is "." or ".." || value[0] is '.' or '-' or '_') return false;
            foreach (char character in value)
            {
                bool valid = character is >= 'a' and <= 'z' || character is >= '0' and <= '9'
                    || character is '.' or '-' or '_';
                if (!valid) return false;
            }
            return true;
        }

        private static bool EqualsIgnoreCase(string left, string right)
            => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        public bool Equals(EnhancedSkyAssetKey other)
            => String.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is EnhancedSkyAssetKey other && Equals(other);
        public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public int CompareTo(EnhancedSkyAssetKey other)
            => String.Compare(_value, other._value, StringComparison.Ordinal);
        public override string ToString() => Value;
        public static bool operator ==(EnhancedSkyAssetKey left, EnhancedSkyAssetKey right)
            => left.Equals(right);
        public static bool operator !=(EnhancedSkyAssetKey left, EnhancedSkyAssetKey right)
            => !left.Equals(right);
    }

    public static class EnhancedSkyPackLimits
    {
        public const int MaximumManifestBytes = 1024 * 1024;
        public const int MaximumSkies = 256;
        public const int MaximumLayersPerSky = 8;
        public const int MaximumRelativePathLength = 512;
        public const int MaximumTextureDimension = 8192;
        public const int MinimumLayerOrder = -64;
        public const int MaximumLayerOrder = 64;
        public const long MaximumTextureFileBytes = 64L * 1024 * 1024;
        public const long MaximumDecodedTextureBytes = 256L * 1024 * 1024;
        public const long MaximumAggregateEncodedBytes = 512L * 1024 * 1024;
        public const long MaximumAggregateDecodedBytes = 1024L * 1024 * 1024;
    }

    /// <summary>A validated PNG owned by one sky-pack resolver.</summary>
    public sealed class EnhancedSkyTextureAsset
    {
        private readonly byte[] _encodedPng;
        private readonly byte[] _rgba8;

        internal EnhancedSkyTextureAsset(string relativePath, int width, int height,
            ReadOnlySpan<byte> encodedPng, ReadOnlySpan<byte> rgba8)
        {
            int expectedBytes = RenderTexturePixels.ValidateRgba8ByteCount(width, height);
            if (rgba8.Length != expectedBytes)
                throw new ArgumentException("Decoded sky pixels must be tightly packed RGBA8.", nameof(rgba8));
            RelativePath = relativePath;
            Width = width;
            Height = height;
            _encodedPng = encodedPng.ToArray();
            _rgba8 = rgba8.ToArray();
            TextureIdentity = new TextureIdentity(this, variant: EnhancedSkyTextureKind.Rgba8);
            Pixels = new RenderTexturePixels(TextureIdentity, width, height, _rgba8,
                revision: StableRevision(_rgba8), onlyOpaque: IsOpaque(_rgba8));
        }

        public string RelativePath { get; }
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> EncodedPng => _encodedPng;
        /// <summary>Immutable-for-the-pack pixels retained for later backend upload.</summary>
        public ReadOnlyMemory<byte> Rgba8 => _rgba8;
        /// <summary>Stable for the lifetime of this immutable pack asset.</summary>
        public TextureIdentity TextureIdentity { get; }
        /// <summary>A backend-neutral upload descriptor using the same stable identity.</summary>
        public RenderTexturePixels Pixels { get; }
        public long DecodedRgba8Bytes => checked((long)Width * Height * 4);

        private static bool IsOpaque(ReadOnlySpan<byte> rgba8)
        {
            for (int alpha = 3; alpha < rgba8.Length; alpha += 4)
            {
                if (rgba8[alpha] != Byte.MaxValue) return false;
            }
            return true;
        }

        private static long StableRevision(ReadOnlySpan<byte> rgba8)
        {
            // Deterministic FNV-1a revision; this is an upload revision, not a trust hash.
            ulong hash = 14695981039346656037UL;
            foreach (byte value in rgba8)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return unchecked((long)hash);
        }
    }

    public enum EnhancedSkyTextureKind : byte
    {
        Rgba8
    }

    public enum EnhancedSkyBaseKind : byte
    {
        None,
        Background2D,
        Cubemap
    }

    public enum EnhancedSkyCompositionKind : byte
    {
        Nebula,
        Stars,
        AuthoredLayer
    }

    public enum EnhancedSkyOverlayBlend : byte
    {
        StraightAlpha
    }

    /// <summary>
    /// One resolved overlay in exact back-to-front composition order. Sky
    /// overlays use straight alpha and do not enter selective bloom in VE22.
    /// </summary>
    public sealed record EnhancedSkyCompositionEntry(
        string StableKey,
        EnhancedSkyCompositionKind Kind,
        EnhancedSkyTextureAsset Texture,
        int Order,
        float ScrollU,
        float ScrollV,
        float RotationSpeed,
        float Opacity,
        float Intensity,
        float TwinkleRate,
        EnhancedSkyOverlayBlend Blend,
        bool SelectiveBloom);

    public sealed record EnhancedSkyCubemap(
        EnhancedSkyTextureAsset PositiveX,
        EnhancedSkyTextureAsset NegativeX,
        EnhancedSkyTextureAsset PositiveY,
        EnhancedSkyTextureAsset NegativeY,
        EnhancedSkyTextureAsset PositiveZ,
        EnhancedSkyTextureAsset NegativeZ);

    public sealed record EnhancedSkyAnimatedLayer(
        EnhancedSkyTextureAsset Texture,
        int Order,
        float ScrollU,
        float ScrollV,
        float RotationSpeed,
        float Opacity,
        float Intensity);

    public sealed record EnhancedSkyStars(
        EnhancedSkyTextureAsset Texture,
        float Intensity,
        float TwinkleRate,
        float RotationSpeed);

    public sealed record EnhancedSkyNebula(
        EnhancedSkyTextureAsset Texture,
        float Opacity,
        float Intensity,
        float ScrollU,
        float ScrollV,
        float RotationSpeed);

    public sealed class EnhancedSkyReplacement
    {
        private readonly IReadOnlyList<EnhancedSkyCompositionEntry> _composition;

        internal EnhancedSkyReplacement(EnhancedSkyAssetKey key, EnhancedSkyTextureAsset? background,
            EnhancedSkyCubemap? cubemap, IReadOnlyList<EnhancedSkyAnimatedLayer> layers,
            EnhancedSkyStars? stars, EnhancedSkyNebula? nebula)
        {
            Key = key;
            Background = background;
            Cubemap = cubemap;
            Layers = layers;
            Stars = stars;
            Nebula = nebula;
            BaseKind = cubemap is not null ? EnhancedSkyBaseKind.Cubemap
                : background is not null ? EnhancedSkyBaseKind.Background2D
                : EnhancedSkyBaseKind.None;
            _composition = BuildComposition(layers, stars, nebula);
        }

        public EnhancedSkyAssetKey Key { get; }
        public EnhancedSkyTextureAsset? Background { get; }
        public EnhancedSkyCubemap? Cubemap { get; }
        public IReadOnlyList<EnhancedSkyAnimatedLayer> Layers { get; }
        public EnhancedSkyStars? Stars { get; }
        public EnhancedSkyNebula? Nebula { get; }
        /// <summary>Cubemap has deterministic precedence when both base assets exist.</summary>
        public EnhancedSkyBaseKind BaseKind { get; }
        public EnhancedSkyCubemap? BaseCubemap => BaseKind == EnhancedSkyBaseKind.Cubemap
            ? Cubemap : null;
        public EnhancedSkyTextureAsset? BaseBackground => BaseKind == EnhancedSkyBaseKind.Background2D
            ? Background : null;
        /// <summary>
        /// Exact overlay order: nebula, stars, then authored layers sorted by
        /// authored order and stable texture key.
        /// </summary>
        public IReadOnlyList<EnhancedSkyCompositionEntry> Composition => _composition;
        public bool SelectiveBloom => false;

        private static IReadOnlyList<EnhancedSkyCompositionEntry> BuildComposition(
            IReadOnlyList<EnhancedSkyAnimatedLayer> layers, EnhancedSkyStars? stars,
            EnhancedSkyNebula? nebula)
        {
            var entries = new List<EnhancedSkyCompositionEntry>(layers.Count + 2);
            if (nebula is not null)
            {
                entries.Add(new EnhancedSkyCompositionEntry("nebula",
                    EnhancedSkyCompositionKind.Nebula, nebula.Texture, 0,
                    nebula.ScrollU, nebula.ScrollV, nebula.RotationSpeed,
                    nebula.Opacity, nebula.Intensity, 0,
                    EnhancedSkyOverlayBlend.StraightAlpha, SelectiveBloom: false));
            }
            if (stars is not null)
            {
                entries.Add(new EnhancedSkyCompositionEntry("stars",
                    EnhancedSkyCompositionKind.Stars, stars.Texture, 0,
                    0, 0, stars.RotationSpeed, 1, stars.Intensity,
                    stars.TwinkleRate, EnhancedSkyOverlayBlend.StraightAlpha,
                    SelectiveBloom: false));
            }
            foreach (EnhancedSkyAnimatedLayer layer in layers)
            {
                entries.Add(new EnhancedSkyCompositionEntry(layer.Texture.RelativePath,
                    EnhancedSkyCompositionKind.AuthoredLayer, layer.Texture, layer.Order,
                    layer.ScrollU, layer.ScrollV, layer.RotationSpeed, layer.Opacity,
                    layer.Intensity, 0, EnhancedSkyOverlayBlend.StraightAlpha,
                    SelectiveBloom: false));
            }
            return Array.AsReadOnly(entries.ToArray());
        }
    }

    /// <summary>The original sky remains available regardless of pack state.</summary>
    public sealed class EnhancedSkyRequest
    {
        public EnhancedSkyRequest(EnhancedSkyAssetKey key, string originalPresentationIdentity)
        {
            if (!key.IsValid) throw new ArgumentException("A valid enhanced sky key is required.", nameof(key));
            ArgumentNullException.ThrowIfNull(originalPresentationIdentity);
            if (originalPresentationIdentity.Length is 0 or > 256
                || !String.Equals(originalPresentationIdentity, originalPresentationIdentity.Trim(),
                    StringComparison.Ordinal)
                || originalPresentationIdentity.Any(Char.IsControl))
            {
                throw new ArgumentException("Original sky identity is invalid.", nameof(originalPresentationIdentity));
            }
            Key = key;
            OriginalPresentationIdentity = originalPresentationIdentity;
        }

        public EnhancedSkyAssetKey Key { get; }
        public string OriginalPresentationIdentity { get; }
    }

    public sealed record ResolvedEnhancedSky(
        EnhancedSkyAssetKey Key,
        string OriginalPresentationIdentity,
        EnhancedSkyReplacement? Replacement)
    {
        public bool UsesEnhancement => Replacement is not null;
    }

    public sealed class EnhancedSkyResolver
    {
        private readonly IReadOnlyDictionary<EnhancedSkyAssetKey, EnhancedSkyReplacement> _skies;

        internal EnhancedSkyResolver(IReadOnlyDictionary<EnhancedSkyAssetKey, EnhancedSkyReplacement> skies)
            => _skies = skies;

        public int Count => _skies.Count;

        public ResolvedEnhancedSky Resolve(EnhancedSkyRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            _skies.TryGetValue(request.Key, out EnhancedSkyReplacement? replacement);
            return new ResolvedEnhancedSky(request.Key, request.OriginalPresentationIdentity, replacement);
        }

        public bool TryGetReplacement(EnhancedSkyAssetKey key, out EnhancedSkyReplacement? replacement)
        {
            if (!key.IsValid)
            {
                replacement = null;
                return false;
            }
            return _skies.TryGetValue(key, out replacement);
        }

        internal static EnhancedSkyResolver Empty { get; }
            = new(new Dictionary<EnhancedSkyAssetKey, EnhancedSkyReplacement>());
    }

    public sealed record EnhancedSkyManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public EnhancedSkyDeclaration[] Skies { get; init; } = Array.Empty<EnhancedSkyDeclaration>();
    }

    public sealed record EnhancedSkyDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public string? Background { get; init; }
        public EnhancedSkyCubemapDeclaration? Cubemap { get; init; }
        public EnhancedSkyLayerDeclaration[] Layers { get; init; } = Array.Empty<EnhancedSkyLayerDeclaration>();
        public EnhancedSkyStarsDeclaration? Stars { get; init; }
        public EnhancedSkyNebulaDeclaration? Nebula { get; init; }
    }

    public sealed record EnhancedSkyCubemapDeclaration
    {
        public string PositiveX { get; init; } = String.Empty;
        public string NegativeX { get; init; } = String.Empty;
        public string PositiveY { get; init; } = String.Empty;
        public string NegativeY { get; init; } = String.Empty;
        public string PositiveZ { get; init; } = String.Empty;
        public string NegativeZ { get; init; } = String.Empty;
    }

    public sealed record EnhancedSkyLayerDeclaration
    {
        public string Texture { get; init; } = String.Empty;
        public int Order { get; init; }
        public float ScrollU { get; init; }
        public float ScrollV { get; init; }
        public float RotationSpeed { get; init; }
        public float Opacity { get; init; } = 1;
        public float Intensity { get; init; } = 1;
    }

    public sealed record EnhancedSkyStarsDeclaration
    {
        public string Texture { get; init; } = String.Empty;
        public float Intensity { get; init; } = 1;
        public float TwinkleRate { get; init; }
        public float RotationSpeed { get; init; }
    }

    public sealed record EnhancedSkyNebulaDeclaration
    {
        public string Texture { get; init; } = String.Empty;
        public float Opacity { get; init; } = 1;
        public float Intensity { get; init; } = 1;
        public float ScrollU { get; init; }
        public float ScrollV { get; init; }
        public float RotationSpeed { get; init; }
    }

    public enum EnhancedSkyPackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManySkies,
        TooManyLayers,
        InvalidSky,
        InvalidLayerMetadata,
        InvalidStarsMetadata,
        InvalidNebulaMetadata,
        DuplicateSkyKey,
        UnsafeTexturePath,
        MissingTexture,
        TextureTooLarge,
        UnsupportedTexture,
        TextureDecodeFailed,
        InvalidCubemap,
        AggregateLimitExceeded
    }

    public sealed record EnhancedSkyPackIssue(
        EnhancedSkyPackIssueKind Kind,
        string Message,
        EnhancedSkyAssetKey? Key = null,
        string? RelativePath = null);

    public sealed record EnhancedSkyPackLoadResult(
        EnhancedSkyResolver Resolver,
        IReadOnlyList<EnhancedSkyPackIssue> Issues)
    {
        public bool HasIssues => Issues.Count != 0;
    }

    /// <summary>
    /// Loads a local optional sky pack once. Invalid assets are omitted and
    /// never prevent the original presentation sky from resolving.
    /// </summary>
    public static class EnhancedSkyPackLoader
    {
        public const string ManifestFileName = "skies.json";

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static EnhancedSkyPackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder = null)
        {
            var issues = new List<EnhancedSkyPackIssue>();
            if (!TryGetRoot(packRoot, out string root))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MissingManifest,
                    "Enhanced sky pack root is invalid."));
                return new EnhancedSkyPackLoadResult(EnhancedSkyResolver.Empty, issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] bytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return new EnhancedSkyPackLoadResult(EnhancedSkyResolver.Empty, issues);
                }
                if (!TryReadBoundedFile(manifestPath, EnhancedSkyPackLimits.MaximumManifestBytes,
                    out bytes))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and {EnhancedSkyPackLimits.MaximumManifestBytes} bytes."));
                    return new EnhancedSkyPackLoadResult(EnhancedSkyResolver.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return new EnhancedSkyPackLoadResult(EnhancedSkyResolver.Empty, issues);
            }

            List<EnhancedSkyDeclaration>? declarations = ParseManifest(bytes, issues);
            if (declarations is null)
                return new EnhancedSkyPackLoadResult(EnhancedSkyResolver.Empty, issues);

            var candidates = new List<(EnhancedSkyAssetKey Key, EnhancedSkyDeclaration Declaration)>();
            foreach (EnhancedSkyDeclaration declaration in declarations)
            {
                if (!EnhancedSkyAssetKey.TryParse(declaration.Key, out EnhancedSkyAssetKey key))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidSky,
                        "Sky declaration has an invalid asset key."));
                    continue;
                }
                candidates.Add((key, declaration));
            }
            HashSet<EnhancedSkyAssetKey> duplicates = candidates.GroupBy(candidate => candidate.Key)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            foreach (EnhancedSkyAssetKey duplicate in duplicates.OrderBy(key => key))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.DuplicateSkyKey,
                    "Duplicate sky key was ignored.", duplicate));
            }

            var cache = new Dictionary<string, EnhancedSkyTextureAsset?>(PathComparer());
            var budget = new SkyResourceBudget();
            var replacements = new Dictionary<EnhancedSkyAssetKey, EnhancedSkyReplacement>();
            foreach ((EnhancedSkyAssetKey key, EnhancedSkyDeclaration declaration) in candidates)
            {
                if (duplicates.Contains(key)) continue;
                EnhancedSkyTextureAsset? background = LoadTexture(root, key, declaration.Background,
                    decoder, cache, budget, issues);
                EnhancedSkyCubemap? cubemap = LoadCubemap(root, key, declaration.Cubemap,
                    decoder, cache, budget, issues);
                IReadOnlyList<EnhancedSkyAnimatedLayer> layers = LoadLayers(root, key,
                    declaration.Layers, decoder, cache, budget, issues);
                EnhancedSkyStars? stars = LoadStars(root, key, declaration.Stars,
                    decoder, cache, budget, issues);
                EnhancedSkyNebula? nebula = LoadNebula(root, key, declaration.Nebula,
                    decoder, cache, budget, issues);
                if (background is null && cubemap is null && layers.Count == 0
                    && stars is null && nebula is null)
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidSky,
                        "Sky has no valid enhanced presentation assets.", key));
                    continue;
                }
                replacements.Add(key, new EnhancedSkyReplacement(key, background, cubemap,
                    layers, stars, nebula));
            }
            return new EnhancedSkyPackLoadResult(new EnhancedSkyResolver(replacements), issues);
        }

        private static List<EnhancedSkyDeclaration>? ParseManifest(byte[] bytes,
            List<EnhancedSkyPackIssue> issues)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 20 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Manifest root must be an object.");
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "skies"))
                        throw new JsonException($"Unknown manifest property '{property.Name}'.");
                }
                if (!root.TryGetProperty("format", out JsonElement formatElement))
                {
                    issues.Add(new EnhancedSkyPackIssue(
                        EnhancedSkyPackIssueKind.UnsupportedManifestVersion,
                        "Manifest format 0 is not supported."));
                    return null;
                }
                int format = formatElement.GetInt32();
                if (format != EnhancedSkyManifest.CurrentFormat)
                {
                    issues.Add(new EnhancedSkyPackIssue(
                        EnhancedSkyPackIssueKind.UnsupportedManifestVersion,
                        $"Manifest format {format} is not supported."));
                    return null;
                }
                if (!root.TryGetProperty("skies", out JsonElement skies)
                    || skies.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("skies must be an array.");
                }

                var declarations = new List<EnhancedSkyDeclaration>();
                int count = 0;
                foreach (JsonElement entry in skies.EnumerateArray())
                {
                    count++;
                    if (count > EnhancedSkyPackLimits.MaximumSkies)
                    {
                        issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.TooManySkies,
                            $"Manifest exceeds {EnhancedSkyPackLimits.MaximumSkies} skies."));
                        return null;
                    }
                    try
                    {
                        EnhancedSkyDeclaration? declaration = entry.Deserialize<EnhancedSkyDeclaration>(_json);
                        if (declaration is null) throw new JsonException("Sky is null.");
                        declarations.Add(declaration);
                    }
                    catch (Exception error) when (error is JsonException or NotSupportedException)
                    {
                        issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidSky,
                            "Malformed sky entry was ignored."));
                    }
                }
                return declarations;
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or InvalidOperationException or FormatException or OverflowException)
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MalformedManifest,
                    "Enhanced sky manifest is malformed."));
                return null;
            }
        }

        private static EnhancedSkyCubemap? LoadCubemap(string root, EnhancedSkyAssetKey key,
            EnhancedSkyCubemapDeclaration? declaration, Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder,
            Dictionary<string, EnhancedSkyTextureAsset?> cache, SkyResourceBudget budget,
            List<EnhancedSkyPackIssue> issues)
        {
            if (declaration is null) return null;
            EnhancedSkyTextureAsset? px = LoadTexture(root, key, declaration.PositiveX, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset? nx = LoadTexture(root, key, declaration.NegativeX, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset? py = LoadTexture(root, key, declaration.PositiveY, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset? ny = LoadTexture(root, key, declaration.NegativeY, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset? pz = LoadTexture(root, key, declaration.PositiveZ, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset? nz = LoadTexture(root, key, declaration.NegativeZ, decoder, cache, budget, issues);
            EnhancedSkyTextureAsset?[] faces = [px, nx, py, ny, pz, nz];
            if (faces.Any(face => face is null))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidCubemap,
                    "Cubemap requires all six valid faces.", key));
                return null;
            }
            int size = px!.Width;
            if (faces.Any(face => face!.Width != size || face.Height != size))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidCubemap,
                    "Cubemap faces must be square and have identical dimensions.", key));
                return null;
            }
            return new EnhancedSkyCubemap(px, nx!, py!, ny!, pz!, nz!);
        }

        private static IReadOnlyList<EnhancedSkyAnimatedLayer> LoadLayers(string root,
            EnhancedSkyAssetKey key, EnhancedSkyLayerDeclaration[]? declarations,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder,
            Dictionary<string, EnhancedSkyTextureAsset?> cache, SkyResourceBudget budget,
            List<EnhancedSkyPackIssue> issues)
        {
            if (declarations is null)
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidSky,
                    "Sky layers must not be null.", key));
                return Array.Empty<EnhancedSkyAnimatedLayer>();
            }
            int count = Math.Min(declarations.Length, EnhancedSkyPackLimits.MaximumLayersPerSky);
            if (declarations.Length > count)
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.TooManyLayers,
                    $"Only the first {EnhancedSkyPackLimits.MaximumLayersPerSky} layers were considered.", key));
            }
            var layers = new List<(int Index, EnhancedSkyAnimatedLayer Layer)>(count);
            for (int i = 0; i < count; i++)
            {
                EnhancedSkyLayerDeclaration declaration = declarations[i];
                if (declaration is null || declaration.Order < EnhancedSkyPackLimits.MinimumLayerOrder
                    || declaration.Order > EnhancedSkyPackLimits.MaximumLayerOrder
                    || !InRange(declaration.ScrollU, -4, 4) || !InRange(declaration.ScrollV, -4, 4)
                    || !InRange(declaration.RotationSpeed, -4, 4)
                    || !InRange(declaration.Opacity, 0, 1) || !InRange(declaration.Intensity, 0, 8))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidLayerMetadata,
                        "Animated sky layer metadata is outside supported bounds.", key));
                    continue;
                }
                EnhancedSkyTextureAsset? texture = LoadTexture(root, key, declaration.Texture,
                    decoder, cache, budget, issues);
                if (texture is null) continue;
                layers.Add((i, new EnhancedSkyAnimatedLayer(texture, declaration.Order,
                    declaration.ScrollU, declaration.ScrollV, declaration.RotationSpeed,
                    declaration.Opacity, declaration.Intensity)));
            }
            return Array.AsReadOnly(layers.OrderBy(item => item.Layer.Order)
                .ThenBy(item => item.Layer.Texture.RelativePath, StringComparer.Ordinal)
                .ThenBy(item => item.Layer.ScrollU)
                .ThenBy(item => item.Layer.ScrollV)
                .ThenBy(item => item.Layer.RotationSpeed)
                .ThenBy(item => item.Layer.Opacity)
                .ThenBy(item => item.Layer.Intensity)
                .ThenBy(item => item.Index)
                .Select(item => item.Layer).ToArray());
        }

        private static EnhancedSkyStars? LoadStars(string root, EnhancedSkyAssetKey key,
            EnhancedSkyStarsDeclaration? declaration, Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder,
            Dictionary<string, EnhancedSkyTextureAsset?> cache, SkyResourceBudget budget,
            List<EnhancedSkyPackIssue> issues)
        {
            if (declaration is null) return null;
            if (!InRange(declaration.Intensity, 0, 8) || !InRange(declaration.TwinkleRate, 0, 16)
                || !InRange(declaration.RotationSpeed, -4, 4))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidStarsMetadata,
                    "Star metadata is outside supported bounds.", key));
                return null;
            }
            EnhancedSkyTextureAsset? texture = LoadTexture(root, key, declaration.Texture,
                decoder, cache, budget, issues);
            return texture is null ? null : new EnhancedSkyStars(texture, declaration.Intensity,
                declaration.TwinkleRate, declaration.RotationSpeed);
        }

        private static EnhancedSkyNebula? LoadNebula(string root, EnhancedSkyAssetKey key,
            EnhancedSkyNebulaDeclaration? declaration, Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder,
            Dictionary<string, EnhancedSkyTextureAsset?> cache, SkyResourceBudget budget,
            List<EnhancedSkyPackIssue> issues)
        {
            if (declaration is null) return null;
            if (!InRange(declaration.Opacity, 0, 1) || !InRange(declaration.Intensity, 0, 8)
                || !InRange(declaration.ScrollU, -4, 4) || !InRange(declaration.ScrollV, -4, 4)
                || !InRange(declaration.RotationSpeed, -4, 4))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.InvalidNebulaMetadata,
                    "Nebula metadata is outside supported bounds.", key));
                return null;
            }
            EnhancedSkyTextureAsset? texture = LoadTexture(root, key, declaration.Texture,
                decoder, cache, budget, issues);
            return texture is null ? null : new EnhancedSkyNebula(texture, declaration.Opacity,
                declaration.Intensity, declaration.ScrollU, declaration.ScrollV,
                declaration.RotationSpeed);
        }

        private static EnhancedSkyTextureAsset? LoadTexture(string root, EnhancedSkyAssetKey key,
            string? relativePath, Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder,
            Dictionary<string, EnhancedSkyTextureAsset?> cache, SkyResourceBudget budget,
            List<EnhancedSkyPackIssue> issues)
        {
            if (relativePath is null) return null;
            if (!TryNormalizePngPath(relativePath, out string normalized))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.UnsafeTexturePath,
                    "Sky texture path must be a contained relative PNG path.", key, relativePath));
                return null;
            }
            if (cache.TryGetValue(normalized, out EnhancedSkyTextureAsset? cached)) return cached;
            cache.Add(normalized, null);
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison()) || HasReparsePoint(root, normalized))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.UnsafeTexturePath,
                        "Sky texture escapes the pack or crosses a link.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.UnsafeTexturePath,
                    "Sky texture path is invalid.", key, normalized));
                return null;
            }

            byte[] encoded;
            try
            {
                if (!File.Exists(candidate))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MissingTexture,
                        "Sky texture was not found.", key, normalized));
                    return null;
                }
                if (!TryReadBoundedFile(candidate, EnhancedSkyPackLimits.MaximumTextureFileBytes,
                    out encoded))
                {
                    issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.TextureTooLarge,
                        "Sky texture exceeds its encoded byte limit.", key, normalized));
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.MissingTexture,
                    "Sky texture could not be read.", key, normalized));
                return null;
            }

            if (!TryReadPngDimensions(encoded, out int width, out int height))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.UnsupportedTexture,
                    "Sky texture does not have a valid PNG header.", key, normalized));
                return null;
            }
            long decodedBytes;
            try { decodedBytes = checked((long)width * height * 4); }
            catch (OverflowException) { decodedBytes = Int64.MaxValue; }
            if (width > EnhancedSkyPackLimits.MaximumTextureDimension
                || height > EnhancedSkyPackLimits.MaximumTextureDimension
                || decodedBytes > EnhancedSkyPackLimits.MaximumDecodedTextureBytes)
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.TextureTooLarge,
                    "Sky texture dimensions exceed the decoded resource limit.", key, normalized));
                return null;
            }
            byte[] rgba8;
            try
            {
                if (decoder is null)
                {
                    RgbaImage image = StbImageDecoder.DecodeRgba(encoded);
                    if (image.Width != width || image.Height != height)
                        throw new InvalidDataException("Decoded dimensions do not match PNG IHDR.");
                    rgba8 = ValidateRgba(image.Pixels.Span, width, height);
                }
                else
                {
                    DecodedRgbImage image = decoder(encoded);
                    if (image.Width != width || image.Height != height)
                        throw new InvalidDataException("Decoded dimensions do not match PNG IHDR.");
                    rgba8 = ExpandRgbToRgba(image.Pixels.Span, width, height);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.TextureDecodeFailed,
                    "Sky texture decoder rejected the PNG.", key, normalized));
                return null;
            }
            if (!budget.TryReserve(encoded.LongLength, decodedBytes))
            {
                issues.Add(new EnhancedSkyPackIssue(EnhancedSkyPackIssueKind.AggregateLimitExceeded,
                    "Sky texture would exceed the pack resource budget.", key, normalized));
                return null;
            }
            var asset = new EnhancedSkyTextureAsset(normalized, width, height, encoded, rgba8);
            cache[normalized] = asset;
            return asset;
        }

        private static byte[] ExpandRgbToRgba(ReadOnlySpan<byte> rgb, int width, int height)
        {
            int pixelCount = checked(width * height);
            if (rgb.Length != checked(pixelCount * 3))
                throw new InvalidDataException("Decoded PNG did not contain tightly packed RGB pixels.");
            byte[] rgba8 = GC.AllocateUninitializedArray<byte>(checked(pixelCount * 4));
            for (int source = 0, destination = 0; source < rgb.Length;
                source += 3, destination += 4)
            {
                rgba8[destination] = rgb[source];
                rgba8[destination + 1] = rgb[source + 1];
                rgba8[destination + 2] = rgb[source + 2];
                rgba8[destination + 3] = Byte.MaxValue;
            }
            return rgba8;
        }

        private static byte[] ValidateRgba(ReadOnlySpan<byte> rgba8, int width, int height)
        {
            if (rgba8.Length != checked(width * height * 4))
                throw new InvalidDataException("Decoded PNG did not contain tightly packed RGBA pixels.");
            return rgba8.ToArray();
        }

        private static bool InRange(float value, float minimum, float maximum)
            => float.IsFinite(value) && value >= minimum && value <= maximum;

        private static bool TryNormalizePngPath(string path, out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path)
                || path.Length > EnhancedSkyPackLimits.MaximumRelativePathLength
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.Any(Char.IsControl))
            {
                return false;
            }
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length is 0 or > 128 || segment is "." or "..")) return false;
            normalized = String.Join('/', segments);
            return true;
        }

        private static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            width = 0;
            height = 0;
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
                || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
                || !bytes[12..16].SequenceEqual("IHDR"u8)) return false;
            uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > Int32.MaxValue || rawHeight > Int32.MaxValue)
                return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
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

        private static bool TryGetRoot(string packRoot, out string root)
        {
            root = String.Empty;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
                return true;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException) { return false; }
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

        private sealed class SkyResourceBudget
        {
            private long _encoded;
            private long _decoded;

            public bool TryReserve(long encoded, long decoded)
            {
                if (encoded < 0 || decoded < 0
                    || encoded > EnhancedSkyPackLimits.MaximumAggregateEncodedBytes - _encoded
                    || decoded > EnhancedSkyPackLimits.MaximumAggregateDecodedBytes - _decoded) return false;
                _encoded += encoded;
                _decoded += decoded;
                return true;
            }
        }
    }
}
