using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Imaging;
using OpenTK.Mathematics;

namespace MphRead
{
    public static class ImpactDecalProfilePackLimits
    {
        public const int MaximumManifestBytes = 256 * 1024;
        public const int MaximumProfiles = 32;
        public const int MaximumRelativePathLength = 512;
        public const int MaximumTextureDimension = 4096;
        public const long MaximumTextureFileBytes = 16L * 1024 * 1024;
        public const long MaximumDecodedTextureBytes = 64L * 1024 * 1024;
        public const long MaximumAggregateEncodedBytes = 64L * 1024 * 1024;
        public const long MaximumAggregateDecodedBytes = 128L * 1024 * 1024;
        public const float MinimumRadius = 0.01f;
        public const float MaximumRadius = 4;
        public const float MaximumLifetimeSeconds = 120;
    }

    /// <summary>
    /// One immutable, decoded presentation texture. Its identity and pixels can
    /// be captured directly into a later RenderFrame without retaining pack I/O.
    /// </summary>
    public sealed class ImpactDecalTextureAsset
    {
        private readonly byte[] _rgba8;

        internal ImpactDecalTextureAsset(string relativePath, int width, int height,
            ReadOnlySpan<byte> rgba8)
        {
            int expected = RenderTexturePixels.ValidateRgba8ByteCount(width, height);
            if (rgba8.Length != expected)
                throw new ArgumentException("Impact decal pixels must be tightly packed RGBA8.",
                    nameof(rgba8));

            RelativePath = relativePath;
            Width = width;
            Height = height;
            _rgba8 = rgba8.ToArray();
            TextureIdentity = new TextureIdentity(this,
                variant: ImpactDecalTextureKind.Albedo);
            (bool onlyOpaque, Vector3 flatColor) = Analyze(_rgba8);
            Pixels = new RenderTexturePixels(TextureIdentity, width, height, _rgba8,
                StableRevision(_rgba8), onlyOpaque, flatColor);
        }

        public string RelativePath { get; }
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> Rgba8 => _rgba8;
        public TextureIdentity TextureIdentity { get; }
        public RenderTexturePixels Pixels { get; }

        private static (bool OnlyOpaque, Vector3 FlatColor) Analyze(
            ReadOnlySpan<byte> rgba)
        {
            bool onlyOpaque = true;
            double red = 0;
            double green = 0;
            double blue = 0;
            double weight = 0;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                double alpha = rgba[i + 3] / 255d;
                onlyOpaque &= rgba[i + 3] == Byte.MaxValue;
                red += rgba[i] * alpha;
                green += rgba[i + 1] * alpha;
                blue += rgba[i + 2] * alpha;
                weight += alpha;
            }
            Vector3 flat = weight > 0
                ? new Vector3((float)(red / weight / 255d),
                    (float)(green / weight / 255d),
                    (float)(blue / weight / 255d))
                : Vector3.One;
            return (onlyOpaque, flat);
        }

        private static long StableRevision(ReadOnlySpan<byte> bytes)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte value in bytes)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return unchecked((long)hash);
        }

        private enum ImpactDecalTextureKind : byte
        {
            Albedo
        }
    }

    /// <summary>
    /// Authored presentation data selected after a beam has already resolved
    /// its visual impact style. It carries no damage or collision authority.
    /// </summary>
    public sealed class ImpactDecalProfile
    {
        internal ImpactDecalProfile(BeamImpactStyle style, ImpactDecalKind kind,
            ImpactDecalTextureAsset texture, float radius, TimeSpan lifetime,
            float opacity, Vector3 tint)
        {
            if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
            if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            ArgumentNullException.ThrowIfNull(texture);
            if (!float.IsFinite(radius) || radius < ImpactDecalProfilePackLimits.MinimumRadius
                || radius > ImpactDecalProfilePackLimits.MaximumRadius)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (lifetime <= TimeSpan.Zero
                || lifetime > TimeSpan.FromSeconds(
                    ImpactDecalProfilePackLimits.MaximumLifetimeSeconds))
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            if (!InUnitRange(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity));
            if (!InUnitRange(tint.X) || !InUnitRange(tint.Y) || !InUnitRange(tint.Z))
                throw new ArgumentOutOfRangeException(nameof(tint));

            Style = style;
            Kind = kind;
            Texture = texture;
            Radius = radius;
            Lifetime = lifetime;
            Opacity = opacity;
            Tint = tint;
        }

        public BeamImpactStyle Style { get; }
        public ImpactDecalKind Kind { get; }
        public ImpactDecalTextureAsset Texture { get; }
        public float Radius { get; }
        public TimeSpan Lifetime { get; }
        public float Opacity { get; }
        public Vector3 Tint { get; }
        public Vector4 Color => new(Tint, Opacity);

        private static bool InUnitRange(float value)
            => float.IsFinite(value) && value >= 0 && value <= 1;
    }

    public readonly record struct ImpactDecalProfileSelection(
        BeamImpactStyle Style, ImpactDecalProfile? Profile)
    {
        public bool UsesDecal => Profile is not null;
    }

    public sealed class ImpactDecalProfileCatalog
    {
        private readonly IReadOnlyDictionary<BeamImpactStyle, ImpactDecalProfile> _profiles;

        internal ImpactDecalProfileCatalog(
            IReadOnlyDictionary<BeamImpactStyle, ImpactDecalProfile> profiles)
            => _profiles = profiles;

        public int Count => _profiles.Count;

        public ImpactDecalProfileSelection Resolve(BeamImpactStyle style)
        {
            if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
            _profiles.TryGetValue(style, out ImpactDecalProfile? profile);
            return new ImpactDecalProfileSelection(style, profile);
        }

        public bool TryGetProfile(BeamImpactStyle style,
            out ImpactDecalProfile? profile)
        {
            if (!Enum.IsDefined(style))
            {
                profile = null;
                return false;
            }
            return _profiles.TryGetValue(style, out profile);
        }

        public static ImpactDecalProfileCatalog Empty { get; }
            = new(new Dictionary<BeamImpactStyle, ImpactDecalProfile>());
    }

    public enum ImpactDecalProfilePackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyProfiles,
        InvalidProfile,
        DuplicateStyle,
        UnsafeTexturePath,
        MissingTexture,
        TextureTooLarge,
        UnsupportedTexture,
        TextureDecodeFailed,
        AggregateLimitExceeded
    }

    public sealed record ImpactDecalProfilePackIssue(
        ImpactDecalProfilePackIssueKind Kind,
        string Message,
        BeamImpactStyle? Style = null,
        string? RelativePath = null);

    public sealed record ImpactDecalProfilePackLoadResult(
        ImpactDecalProfileCatalog Catalog,
        IReadOnlyList<ImpactDecalProfilePackIssue> Issues)
    {
        public bool HasIssues => Issues.Count != 0;
    }

    public sealed record ImpactDecalProfileManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public ImpactDecalProfileDeclaration[] Profiles { get; init; }
            = Array.Empty<ImpactDecalProfileDeclaration>();
    }

    public sealed record ImpactDecalProfileDeclaration
    {
        public string Style { get; init; } = String.Empty;
        public string Kind { get; init; } = String.Empty;
        public string Texture { get; init; } = String.Empty;
        public float Radius { get; init; }
        public float LifetimeSeconds { get; init; }
        public float Opacity { get; init; } = 1;
        public float[] Tint { get; init; } = Array.Empty<float>();
    }

    /// <summary>
    /// Strict, bounded loader for the optional local impact-decals.json pack.
    /// Invalid declarations are omitted; an absent style resolves to no decal.
    /// </summary>
    public static class ImpactDecalProfilePackLoader
    {
        public const string ManifestFileName = "impact-decals.json";

        private static readonly JsonSerializerOptions _json
            = new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };

        public static ImpactDecalProfilePackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, RgbaImage>? decoder = null)
        {
            var issues = new List<ImpactDecalProfilePackIssue>();
            decoder ??= StbImageDecoder.DecodeRgba;
            if (!TryGetRoot(packRoot, out string root))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.MissingManifest,
                    "Impact decal pack root is invalid.");
                return Result(issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] manifestBytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    Issue(issues, ImpactDecalProfilePackIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found.");
                    return Result(issues);
                }
                if (!TryReadBoundedFile(manifestPath,
                    ImpactDecalProfilePackLimits.MaximumManifestBytes,
                    out manifestBytes))
                {
                    Issue(issues, ImpactDecalProfilePackIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and "
                        + $"{ImpactDecalProfilePackLimits.MaximumManifestBytes} bytes.");
                    return Result(issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read.");
                return Result(issues);
            }

            List<ImpactDecalProfileDeclaration>? declarations
                = ParseManifest(manifestBytes, issues);
            if (declarations is null) return Result(issues);

            var candidates = new List<(BeamImpactStyle Style,
                ImpactDecalProfileDeclaration Declaration)>();
            foreach (ImpactDecalProfileDeclaration declaration in declarations)
            {
                if (!TryParseStyle(declaration.Style, out BeamImpactStyle style))
                {
                    Issue(issues, ImpactDecalProfilePackIssueKind.InvalidProfile,
                        "Impact decal profile has an invalid style.");
                    continue;
                }
                candidates.Add((style, declaration));
            }

            HashSet<BeamImpactStyle> duplicates = candidates
                .GroupBy(candidate => candidate.Style)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            foreach (BeamImpactStyle duplicate in duplicates.OrderBy(value => value))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.DuplicateStyle,
                    "Duplicate impact decal style was ignored.", duplicate);
            }

            var textures = new Dictionary<string, ImpactDecalTextureAsset?>(
                PathComparer());
            var budget = new ResourceBudget();
            var profiles = new Dictionary<BeamImpactStyle, ImpactDecalProfile>();
            foreach ((BeamImpactStyle style,
                ImpactDecalProfileDeclaration declaration) in candidates)
            {
                if (duplicates.Contains(style)) continue;
                if (!TryCreateProfile(root, style, declaration, decoder, textures,
                    budget, issues, out ImpactDecalProfile? profile))
                {
                    continue;
                }
                profiles.Add(style, profile!);
            }
            return new ImpactDecalProfilePackLoadResult(
                new ImpactDecalProfileCatalog(profiles), issues);
        }

        private static bool TryCreateProfile(string root, BeamImpactStyle style,
            ImpactDecalProfileDeclaration declaration,
            Func<ReadOnlyMemory<byte>, RgbaImage> decoder,
            Dictionary<string, ImpactDecalTextureAsset?> textures,
            ResourceBudget budget, List<ImpactDecalProfilePackIssue> issues,
            out ImpactDecalProfile? profile)
        {
            profile = null;
            if (!TryParseKind(declaration.Kind, out ImpactDecalKind kind)
                || !float.IsFinite(declaration.Radius)
                || declaration.Radius < ImpactDecalProfilePackLimits.MinimumRadius
                || declaration.Radius > ImpactDecalProfilePackLimits.MaximumRadius
                || !float.IsFinite(declaration.LifetimeSeconds)
                || declaration.LifetimeSeconds <= 0
                || declaration.LifetimeSeconds
                    > ImpactDecalProfilePackLimits.MaximumLifetimeSeconds
                || !InUnitRange(declaration.Opacity)
                || declaration.Tint is not { Length: 3 }
                || !declaration.Tint.All(InUnitRange))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.InvalidProfile,
                    "Impact decal metadata is outside supported bounds.", style);
                return false;
            }

            ImpactDecalTextureAsset? texture = LoadTexture(root, style,
                declaration.Texture, decoder, textures, budget, issues);
            if (texture is null) return false;
            try
            {
                profile = new ImpactDecalProfile(style, kind, texture,
                    declaration.Radius,
                    TimeSpan.FromSeconds(declaration.LifetimeSeconds),
                    declaration.Opacity, new Vector3(declaration.Tint[0],
                        declaration.Tint[1], declaration.Tint[2]));
                return true;
            }
            catch (Exception error) when (error is ArgumentException
                or OverflowException)
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.InvalidProfile,
                    "Impact decal metadata could not be represented safely.", style);
                return false;
            }
        }

        private static ImpactDecalTextureAsset? LoadTexture(string root,
            BeamImpactStyle style, string path,
            Func<ReadOnlyMemory<byte>, RgbaImage> decoder,
            Dictionary<string, ImpactDecalTextureAsset?> cache,
            ResourceBudget budget, List<ImpactDecalProfilePackIssue> issues)
        {
            if (!TryNormalizePngPath(path, out string normalized))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.UnsafeTexturePath,
                    "Texture path must be a contained relative PNG path.", style,
                    path);
                return null;
            }
            if (cache.TryGetValue(normalized, out ImpactDecalTextureAsset? cached))
                return cached;
            cache.Add(normalized, null);

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
                    Issue(issues,
                        ImpactDecalProfilePackIssueKind.UnsafeTexturePath,
                        "Texture escapes the pack or crosses a link.", style,
                        normalized);
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.UnsafeTexturePath,
                    "Texture path is invalid.", style, normalized);
                return null;
            }

            byte[] encoded;
            try
            {
                if (!File.Exists(candidate))
                {
                    Issue(issues, ImpactDecalProfilePackIssueKind.MissingTexture,
                        "Texture file was not found.", style, normalized);
                    return null;
                }
                if (!TryReadBoundedFile(candidate,
                    ImpactDecalProfilePackLimits.MaximumTextureFileBytes,
                    out encoded))
                {
                    Issue(issues, ImpactDecalProfilePackIssueKind.TextureTooLarge,
                        "Texture exceeds the encoded byte limit.", style,
                        normalized);
                    return null;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.MissingTexture,
                    "Texture file could not be read.", style, normalized);
                return null;
            }

            if (!TryReadPngDimensions(encoded, out int width, out int height))
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.UnsupportedTexture,
                    "Texture does not have a valid PNG header.", style,
                    normalized);
                return null;
            }
            long decodedBytes;
            try { decodedBytes = checked((long)width * height * 4); }
            catch (OverflowException) { decodedBytes = Int64.MaxValue; }
            if (width > ImpactDecalProfilePackLimits.MaximumTextureDimension
                || height > ImpactDecalProfilePackLimits.MaximumTextureDimension
                || decodedBytes
                    > ImpactDecalProfilePackLimits.MaximumDecodedTextureBytes)
            {
                Issue(issues, ImpactDecalProfilePackIssueKind.TextureTooLarge,
                    "Texture dimensions exceed the decoded resource limit.",
                    style, normalized);
                return null;
            }

            byte[] rgba8;
            try
            {
                RgbaImage decoded = decoder(encoded);
                if (decoded.Width != width || decoded.Height != height
                    || decoded.Pixels.Length != decodedBytes)
                    throw new InvalidDataException(
                        "Decoded pixels do not match PNG dimensions.");
                rgba8 = decoded.Pixels.ToArray();
            }
            catch (Exception error) when (error is not OutOfMemoryException
                and not StackOverflowException)
            {
                Issue(issues,
                    ImpactDecalProfilePackIssueKind.TextureDecodeFailed,
                    "Texture decoder rejected the PNG.", style, normalized);
                return null;
            }
            if (!budget.TryReserve(encoded.LongLength, decodedBytes))
            {
                Issue(issues,
                    ImpactDecalProfilePackIssueKind.AggregateLimitExceeded,
                    "Texture would exceed the pack resource budget.", style,
                    normalized);
                return null;
            }

            var asset = new ImpactDecalTextureAsset(normalized, width, height,
                rgba8);
            cache[normalized] = asset;
            return asset;
        }

        private static List<ImpactDecalProfileDeclaration>? ParseManifest(
            byte[] bytes, List<ImpactDecalProfilePackIssue> issues)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 12 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Manifest root must be an object.");
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "profiles"))
                        throw new JsonException(
                            $"Unknown manifest property '{property.Name}'.");
                }
                if (!root.TryGetProperty("format", out JsonElement format)
                    || format.ValueKind != JsonValueKind.Number
                    || !format.TryGetInt32(out int version)
                    || version != ImpactDecalProfileManifest.CurrentFormat)
                {
                    Issue(issues,
                        ImpactDecalProfilePackIssueKind.UnsupportedManifestVersion,
                        "Impact decal manifest version is not supported.");
                    return null;
                }
                if (!root.TryGetProperty("profiles", out JsonElement profiles)
                    || profiles.ValueKind != JsonValueKind.Array)
                    throw new JsonException("profiles must be an array.");

                var declarations = new List<ImpactDecalProfileDeclaration>();
                int count = 0;
                foreach (JsonElement element in profiles.EnumerateArray())
                {
                    count++;
                    if (count > ImpactDecalProfilePackLimits.MaximumProfiles)
                    {
                        Issue(issues,
                            ImpactDecalProfilePackIssueKind.TooManyProfiles,
                            $"Manifest exceeds "
                            + $"{ImpactDecalProfilePackLimits.MaximumProfiles} profiles.");
                        return null;
                    }
                    try
                    {
                        ImpactDecalProfileDeclaration? declaration
                            = element.Deserialize<ImpactDecalProfileDeclaration>(
                                _json);
                        if (declaration is null)
                            throw new JsonException("Profile is null.");
                        declarations.Add(declaration);
                    }
                    catch (Exception error) when (error is JsonException
                        or NotSupportedException)
                    {
                        Issue(issues,
                            ImpactDecalProfilePackIssueKind.InvalidProfile,
                            "Malformed impact decal profile was ignored.");
                    }
                }
                return declarations;
            }
            catch (Exception error) when (error is JsonException
                or NotSupportedException or InvalidOperationException
                or FormatException or OverflowException)
            {
                Issue(issues,
                    ImpactDecalProfilePackIssueKind.MalformedManifest,
                    "Impact decal manifest is malformed.");
                return null;
            }
        }

        private static bool TryParseStyle(string? value,
            out BeamImpactStyle style)
            => Enum.TryParse(value, ignoreCase: false, out style)
                && Enum.IsDefined(style)
                && String.Equals(Enum.GetName(style), value,
                    StringComparison.Ordinal);

        private static bool TryParseKind(string? value, out ImpactDecalKind kind)
            => Enum.TryParse(value, ignoreCase: false, out kind)
                && Enum.IsDefined(kind)
                && String.Equals(Enum.GetName(kind), value,
                    StringComparison.Ordinal);

        private static bool InUnitRange(float value)
            => float.IsFinite(value) && value >= 0 && value <= 1;

        private static bool TryNormalizePngPath(string? path,
            out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path)
                || path.Length > ImpactDecalProfilePackLimits.MaximumRelativePathLength
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':')
                || Path.IsPathRooted(path)
                || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.Any(Char.IsControl))
                return false;
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length is 0 or > 128
                || segment is "." or ".."))
                return false;
            normalized = String.Join('/', segments);
            return true;
        }

        private static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes,
            out int width, out int height)
        {
            ReadOnlySpan<byte> signature =
                [137, 80, 78, 71, 13, 10, 26, 10];
            width = 0;
            height = 0;
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
                || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
                || !bytes[12..16].SequenceEqual("IHDR"u8))
                return false;
            uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > Int32.MaxValue
                || rawHeight > Int32.MaxValue)
                return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static bool TryReadBoundedFile(string path, long maximumBytes,
            out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length is <= 0 || stream.Length > maximumBytes
                || stream.Length > Int32.MaxValue)
                return false;
            bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            stream.ReadExactly(bytes);
            if (stream.ReadByte() == -1) return true;
            bytes = Array.Empty<byte>();
            return false;
        }

        private static bool TryGetRoot(string? packRoot, out string root)
        {
            root = String.Empty;
            try
            {
                root = Path.GetFullPath(packRoot
                    ?? throw new ArgumentNullException(nameof(packRoot)));
                return true;
            }
            catch (Exception error) when (error is ArgumentException
                or NotSupportedException or PathTooLongException
                or SecurityException)
            {
                return false;
            }
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
                return true;
            string current = root;
            foreach (string part in normalized.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current)
                        .HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }
            return false;
        }

        private static bool IsFileError(Exception error)
            => error is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException;

        private static StringComparer PathComparer()
            => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static StringComparison PathComparison()
            => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static ImpactDecalProfilePackLoadResult Result(
            List<ImpactDecalProfilePackIssue> issues)
            => new(ImpactDecalProfileCatalog.Empty, issues);

        private static void Issue(List<ImpactDecalProfilePackIssue> issues,
            ImpactDecalProfilePackIssueKind kind, string message,
            BeamImpactStyle? style = null, string? relativePath = null)
            => issues.Add(new ImpactDecalProfilePackIssue(kind, message, style,
                relativePath));

        private sealed class ResourceBudget
        {
            private long _encoded;
            private long _decoded;

            public bool TryReserve(long encoded, long decoded)
            {
                if (encoded < 0 || decoded < 0
                    || encoded
                        > ImpactDecalProfilePackLimits.MaximumAggregateEncodedBytes
                            - _encoded
                    || decoded
                        > ImpactDecalProfilePackLimits.MaximumAggregateDecodedBytes
                            - _decoded)
                    return false;
                _encoded += encoded;
                _decoded += decoded;
                return true;
            }
        }
    }
}
