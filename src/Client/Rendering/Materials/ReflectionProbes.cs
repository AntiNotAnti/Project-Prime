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
    /// <summary>Persistent identity for an authored or generated reflection probe.</summary>
    public readonly struct ReflectionProbeKey : IEquatable<ReflectionProbeKey>
    {
        public const int MaximumLength = 192;
        private readonly string? _value;
        private ReflectionProbeKey(string value) => _value = value;

        public bool IsValid => _value is not null;
        public string Value => _value ?? String.Empty;

        public static ReflectionProbeKey ForRoom(string room, string name)
            => Parse($"room/{room}/reflection/{name}");
        public static ReflectionProbeKey ForGeneric(string name)
            => Parse($"generic/reflection/{name}");

        public static ReflectionProbeKey Parse(string value)
        {
            if (!TryParse(value, out ReflectionProbeKey key))
                throw new FormatException("Reflection probe key is invalid.");
            return key;
        }

        public static bool TryParse(string? value, out ReflectionProbeKey key)
        {
            key = default;
            if (String.IsNullOrEmpty(value) || value.Length > MaximumLength
                || !String.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.Contains("//", StringComparison.Ordinal)
                || value.Contains('\\') || value.Contains(':')) return false;
            string[] parts = value.Split('/');
            bool shape = parts.Length == 4
                    && EqualsIgnoreCase(parts[0], "room")
                    && EqualsIgnoreCase(parts[2], "reflection")
                || parts.Length == 3
                    && EqualsIgnoreCase(parts[0], "generic")
                    && EqualsIgnoreCase(parts[1], "reflection");
            if (!shape) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].ToLowerInvariant();
                if (!IsStableSegment(parts[i])) return false;
            }
            key = new ReflectionProbeKey(String.Join('/', parts));
            return true;
        }

        internal static bool TryNormalizeRoom(string? room, out string canonical)
        {
            canonical = room?.ToLowerInvariant() ?? String.Empty;
            return room is not null && String.Equals(room, room.Trim(), StringComparison.Ordinal)
                && IsStableSegment(canonical);
        }

        private static bool IsStableSegment(string value)
        {
            if (value.Length is 0 or > 64 || value[0] is '.' or '-' or '_'
                || value is "." or "..") return false;
            return value.All(character => character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9' || character is '.' or '-' or '_');
        }

        private static bool EqualsIgnoreCase(string left, string right)
            => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        public bool Equals(ReflectionProbeKey other)
            => String.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ReflectionProbeKey other && Equals(other);
        public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => Value;
        public static bool operator ==(ReflectionProbeKey left, ReflectionProbeKey right) => left.Equals(right);
        public static bool operator !=(ReflectionProbeKey left, ReflectionProbeKey right) => !left.Equals(right);
    }

    public enum ReflectionCubemapFace
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    public sealed class ReflectionProbeFaceAsset
    {
        internal ReflectionProbeFaceAsset(string path, int dimension, byte[] png,
            ReadOnlyMemory<byte> rgba8)
        {
            RelativePath = path;
            Dimension = dimension;
            EncodedPng = png;
            int expected = checked(dimension * dimension * 4);
            if (rgba8.Length != expected)
                throw new ArgumentException("Reflection probe face must contain RGBA8 pixels.", nameof(rgba8));
            Rgba8 = rgba8.ToArray();
        }
        public string RelativePath { get; }
        public int Dimension { get; }
        public ReadOnlyMemory<byte> EncodedPng { get; }
        /// <summary>Immutable decoded pixels retained for backend upload.</summary>
        public ReadOnlyMemory<byte> Rgba8 { get; }
        public long DecodedRgba8Bytes => checked((long)Dimension * Dimension * 4);
    }

    public sealed class ReflectionCubemapAsset
    {
        private readonly IReadOnlyDictionary<ReflectionCubemapFace, ReflectionProbeFaceAsset> _faces;
        internal ReflectionCubemapAsset(ReflectionProbeKey key,
            IReadOnlyDictionary<ReflectionCubemapFace, ReflectionProbeFaceAsset> faces)
        {
            Key = key;
            _faces = faces;
            Dimension = faces[ReflectionCubemapFace.PositiveX].Dimension;
        }
        public ReflectionProbeKey Key { get; }
        public int Dimension { get; }
        public IReadOnlyDictionary<ReflectionCubemapFace, ReflectionProbeFaceAsset> Faces => _faces;
    }

    public sealed record GeneratedStaticReflectionProbe(
        ReflectionProbeKey Key,
        int Resolution,
        Vector3 CapturePosition);

    public enum ReflectionProbeSourceKind
    {
        None,
        AuthoredCubemap,
        GeneratedStatic,
        GenericFallback
    }

    public readonly record struct ReflectionProbeSelection(
        ReflectionProbeSourceKind Kind,
        ReflectionProbeKey Key,
        ReflectionCubemapAsset? Cubemap,
        GeneratedStaticReflectionProbe? Generated)
    {
        public static ReflectionProbeSelection None { get; }
            = new(ReflectionProbeSourceKind.None, default, null, null);
    }

    /// <summary>Backend-neutral reflection sampling values derived from one enhanced material.</summary>
    public readonly record struct ReflectionSamplingPolicy(
        bool Enabled,
        float Strength,
        float Smoothness,
        float Roughness,
        float MipLevel)
    {
        public const float MaximumSubtleStrength = 0.35f;

        public static ReflectionSamplingPolicy FromMaterial(EnhancedMaterial material, int mipCount)
        {
            float strength = float.IsFinite(material.ReflectionStrength)
                ? Math.Clamp(material.ReflectionStrength, 0, MaximumSubtleStrength) : 0;
            float smoothness = float.IsFinite(material.Smoothness)
                ? Math.Clamp(material.Smoothness, 0, 1) : 0;
            float roughness = 1 - smoothness;
            float mip = Math.Max(0, mipCount - 1) * roughness;
            return new ReflectionSamplingPolicy(strength > 0 && mipCount > 0,
                strength, smoothness, roughness, mip);
        }
    }

    public sealed class ReflectionProbeResolver
    {
        private readonly IReadOnlyDictionary<string, RoomReflectionProbeOptions> _rooms;
        private readonly ReflectionCubemapAsset? _generic;

        internal ReflectionProbeResolver(IReadOnlyDictionary<string, RoomReflectionProbeOptions> rooms,
            ReflectionCubemapAsset? generic)
        {
            _rooms = rooms;
            _generic = generic;
        }

        public ReflectionProbeSelection Resolve(string room)
        {
            if (ReflectionProbeKey.TryNormalizeRoom(room, out string canonical)
                && _rooms.TryGetValue(canonical, out RoomReflectionProbeOptions? options))
            {
                if (options.Authored is not null)
                    return new ReflectionProbeSelection(ReflectionProbeSourceKind.AuthoredCubemap,
                        options.Authored.Key, options.Authored, null);
                if (options.Generated is not null)
                    return new ReflectionProbeSelection(ReflectionProbeSourceKind.GeneratedStatic,
                        options.Generated.Key, null, options.Generated);
            }
            if (_generic is not null)
                return new ReflectionProbeSelection(ReflectionProbeSourceKind.GenericFallback,
                    _generic.Key, _generic, null);
            return ReflectionProbeSelection.None;
        }

        public ReflectionProbeSelection Resolve(string room,
            EnhancedEnvironmentAssetKey? requested)
        {
            if (requested.HasValue
                && ReflectionProbeKey.TryParse(requested.Value.Value,
                    out ReflectionProbeKey requestedKey)
                && ReflectionProbeKey.TryNormalizeRoom(room, out string canonical))
            {
                if (_rooms.TryGetValue(canonical,
                    out RoomReflectionProbeOptions? options))
                {
                    if (options.Authored?.Key == requestedKey)
                    {
                        return new ReflectionProbeSelection(
                            ReflectionProbeSourceKind.AuthoredCubemap,
                            requestedKey, options.Authored, null);
                    }
                    if (options.Generated?.Key == requestedKey)
                    {
                        return new ReflectionProbeSelection(
                            ReflectionProbeSourceKind.GeneratedStatic,
                            requestedKey, null, options.Generated);
                    }
                }
                if (_generic?.Key == requestedKey)
                {
                    return new ReflectionProbeSelection(
                        ReflectionProbeSourceKind.GenericFallback,
                        requestedKey, _generic, null);
                }
            }
            return Resolve(room);
        }

        /// <summary>
        /// Resolve a cube the current runtime can actually upload. Generated
        /// descriptors remain visible through <see cref="Resolve(string)"/>,
        /// but until an offline capture source exists they fail softly to the
        /// generic authored cube rather than synthesizing environment data.
        /// </summary>
        public ReflectionProbeSelection ResolveUploadable(string room)
            => ResolveUploadable(Resolve(room));

        public ReflectionProbeSelection ResolveUploadable(string room,
            EnhancedEnvironmentAssetKey? requested)
            => ResolveUploadable(Resolve(room, requested));

        private ReflectionProbeSelection ResolveUploadable(
            ReflectionProbeSelection selected)
        {
            if (selected.Kind != ReflectionProbeSourceKind.GeneratedStatic)
                return selected;
            if (_generic is not null)
            {
                return new ReflectionProbeSelection(ReflectionProbeSourceKind.GenericFallback,
                    _generic.Key, _generic, null);
            }
            return ReflectionProbeSelection.None;
        }

        public int RoomCount => _rooms.Count;
        internal static ReflectionProbeResolver Empty { get; }
            = new(new Dictionary<string, RoomReflectionProbeOptions>(), null);
    }

    internal sealed record RoomReflectionProbeOptions(
        ReflectionCubemapAsset? Authored,
        GeneratedStaticReflectionProbe? Generated);

    public static class ReflectionProbePackLimits
    {
        public const int MaximumManifestBytes = 512 * 1024;
        public const int MaximumRooms = 256;
        public const long MaximumFaceFileBytes = 32L * 1024 * 1024;
        public const int MaximumFaceDimension = 2048;
        public const long MaximumAggregateEncodedBytes = 256L * 1024 * 1024;
        public const long MaximumAggregateDecodedBytes = 512L * 1024 * 1024;
    }

    public sealed record ReflectionProbeManifest
    {
        public const int CurrentFormat = 1;
        public int Format { get; init; } = CurrentFormat;
        public AuthoredReflectionProbeDeclaration? GenericFallback { get; init; }
        public RoomReflectionProbeDeclaration[] Rooms { get; init; }
            = Array.Empty<RoomReflectionProbeDeclaration>();
    }

    public sealed record RoomReflectionProbeDeclaration
    {
        public string Room { get; init; } = String.Empty;
        public AuthoredReflectionProbeDeclaration? Authored { get; init; }
        public GeneratedReflectionProbeDeclaration? GeneratedStatic { get; init; }
    }

    public sealed record AuthoredReflectionProbeDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public ReflectionCubemapFacesDeclaration? Faces { get; init; }
    }

    public sealed record GeneratedReflectionProbeDeclaration
    {
        public string Key { get; init; } = String.Empty;
        public int Resolution { get; init; } = 256;
        public float[]? CapturePosition { get; init; }
    }

    public sealed record ReflectionCubemapFacesDeclaration
    {
        public string PositiveX { get; init; } = String.Empty;
        public string NegativeX { get; init; } = String.Empty;
        public string PositiveY { get; init; } = String.Empty;
        public string NegativeY { get; init; } = String.Empty;
        public string PositiveZ { get; init; } = String.Empty;
        public string NegativeZ { get; init; } = String.Empty;
    }

    public enum ReflectionProbePackIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedVersion,
        TooManyRooms,
        DuplicateRoom,
        InvalidKey,
        InvalidGeneratedDescriptor,
        UnsafePath,
        MissingFace,
        UnsupportedFace,
        FaceTooLarge,
        DecodeFailed,
        FaceDimensionMismatch,
        AggregateLimitExceeded
    }

    public sealed record ReflectionProbePackIssue(
        ReflectionProbePackIssueKind Kind,
        string Message,
        string? Room = null,
        string? RelativePath = null);

    public sealed record ReflectionProbePackLoadResult(
        ReflectionProbeResolver Resolver,
        IReadOnlyList<ReflectionProbePackIssue> Issues);

    public static class ReflectionProbePackLoader
    {
        public const string ManifestFileName = "reflection-probes.json";
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static ReflectionProbePackLoadResult Load(string packRoot,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage>? decoder = null)
        {
            var issues = new List<ReflectionProbePackIssue>();
            decoder ??= DecodedRgbImage.DecodePng;
            string root;
            byte[] json;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
                string path = Path.Combine(root, ManifestFileName);
                if (!File.Exists(path))
                {
                    Issue(issues, ReflectionProbePackIssueKind.MissingManifest, "Reflection probe manifest was not found.");
                    return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
                }
                if (!TryReadBounded(path, ReflectionProbePackLimits.MaximumManifestBytes, out json))
                {
                    Issue(issues, ReflectionProbePackIssueKind.ManifestTooLarge, "Reflection probe manifest exceeds its byte limit.");
                    return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                Issue(issues, ReflectionProbePackIssueKind.MissingManifest, "Reflection probe manifest could not be read.");
                return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
            }

            ReflectionProbeManifest manifest;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicateProperties(document.RootElement);
                manifest = document.RootElement.Deserialize<ReflectionProbeManifest>(_json)
                    ?? throw new JsonException("Manifest is null.");
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or InvalidOperationException or FormatException or OverflowException)
            {
                Issue(issues, ReflectionProbePackIssueKind.MalformedManifest, "Reflection probe manifest is malformed.");
                return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
            }
            if (manifest.Format != ReflectionProbeManifest.CurrentFormat)
            {
                Issue(issues, ReflectionProbePackIssueKind.UnsupportedVersion, "Reflection probe manifest version is unsupported.");
                return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
            }
            if (manifest.Rooms is null || manifest.Rooms.Length > ReflectionProbePackLimits.MaximumRooms)
            {
                Issue(issues, ReflectionProbePackIssueKind.TooManyRooms, "Reflection probe manifest exceeds its room limit.");
                return new ReflectionProbePackLoadResult(ReflectionProbeResolver.Empty, issues);
            }

            var faceCache = new Dictionary<string, ReflectionProbeFaceAsset?>(PathComparer());
            var admitted = new HashSet<string>(PathComparer());
            long encodedBytes = 0;
            long decodedBytes = 0;
            ReflectionCubemapAsset? generic = LoadCubemap(root, manifest.GenericFallback, null,
                decoder, faceCache, admitted, issues, ref encodedBytes, ref decodedBytes,
                requireGenericKey: true);

            var normalizedRooms = new List<(string Room, RoomReflectionProbeDeclaration Declaration)>();
            foreach (RoomReflectionProbeDeclaration room in manifest.Rooms)
            {
                if (!ReflectionProbeKey.TryNormalizeRoom(room.Room, out string canonical))
                {
                    Issue(issues, ReflectionProbePackIssueKind.InvalidKey, "Room identity is invalid.", room.Room);
                    continue;
                }
                normalizedRooms.Add((canonical, room));
            }
            HashSet<string> duplicates = normalizedRooms.GroupBy(item => item.Room, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            foreach (string duplicate in duplicates)
                Issue(issues, ReflectionProbePackIssueKind.DuplicateRoom, "Duplicate room was ignored.", duplicate);

            var rooms = new Dictionary<string, RoomReflectionProbeOptions>(StringComparer.Ordinal);
            foreach ((string room, RoomReflectionProbeDeclaration declaration) in normalizedRooms)
            {
                if (duplicates.Contains(room)) continue;
                ReflectionCubemapAsset? authored = LoadCubemap(root, declaration.Authored, room,
                    decoder, faceCache, admitted, issues, ref encodedBytes, ref decodedBytes,
                    requireGenericKey: false);
                GeneratedStaticReflectionProbe? generated = LoadGenerated(declaration.GeneratedStatic, room, issues);
                rooms.Add(room, new RoomReflectionProbeOptions(authored, generated));
            }
            return new ReflectionProbePackLoadResult(new ReflectionProbeResolver(rooms, generic), issues);
        }

        private static ReflectionCubemapAsset? LoadCubemap(string root,
            AuthoredReflectionProbeDeclaration? declaration, string? room,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage> decoder,
            Dictionary<string, ReflectionProbeFaceAsset?> cache, HashSet<string> admitted,
            List<ReflectionProbePackIssue> issues, ref long aggregateEncoded, ref long aggregateDecoded,
            bool requireGenericKey)
        {
            if (declaration is null) return null;
            if (!ReflectionProbeKey.TryParse(declaration.Key, out ReflectionProbeKey key)
                || requireGenericKey != key.Value.StartsWith("generic/", StringComparison.Ordinal)
                || !requireGenericKey && !String.Equals(key.Value.Split('/')[1], room, StringComparison.Ordinal))
            {
                Issue(issues, ReflectionProbePackIssueKind.InvalidKey, "Reflection probe key does not match its scope.", room);
                return null;
            }
            if (declaration.Faces is null)
            {
                Issue(issues, ReflectionProbePackIssueKind.MissingFace, "Cubemap face declarations are missing.", room);
                return null;
            }
            var paths = new Dictionary<ReflectionCubemapFace, string>
            {
                [ReflectionCubemapFace.PositiveX] = declaration.Faces.PositiveX,
                [ReflectionCubemapFace.NegativeX] = declaration.Faces.NegativeX,
                [ReflectionCubemapFace.PositiveY] = declaration.Faces.PositiveY,
                [ReflectionCubemapFace.NegativeY] = declaration.Faces.NegativeY,
                [ReflectionCubemapFace.PositiveZ] = declaration.Faces.PositiveZ,
                [ReflectionCubemapFace.NegativeZ] = declaration.Faces.NegativeZ
            };
            var faces = new Dictionary<ReflectionCubemapFace, ReflectionProbeFaceAsset>();
            foreach ((ReflectionCubemapFace face, string path) in paths)
            {
                ReflectionProbeFaceAsset? asset = LoadFace(root, path, room, decoder, cache, issues);
                if (asset is null) return null;
                faces.Add(face, asset);
            }
            int dimension = faces.Values.First().Dimension;
            if (faces.Values.Any(face => face.Dimension != dimension))
            {
                Issue(issues, ReflectionProbePackIssueKind.FaceDimensionMismatch,
                    "All cubemap faces must have equal square dimensions.", room);
                return null;
            }
            ReflectionProbeFaceAsset[] newAssets = faces.Values.Distinct()
                .Where(face => !admitted.Contains(face.RelativePath)).ToArray();
            long newEncoded = newAssets.Sum(face => (long)face.EncodedPng.Length);
            long newDecoded = newAssets.Sum(face => face.DecodedRgba8Bytes);
            if (newEncoded > ReflectionProbePackLimits.MaximumAggregateEncodedBytes - aggregateEncoded
                || newDecoded > ReflectionProbePackLimits.MaximumAggregateDecodedBytes - aggregateDecoded)
            {
                Issue(issues, ReflectionProbePackIssueKind.AggregateLimitExceeded,
                    "Cubemap exceeds the pack aggregate resource budget.", room);
                return null;
            }
            aggregateEncoded += newEncoded;
            aggregateDecoded += newDecoded;
            foreach (ReflectionProbeFaceAsset face in newAssets) admitted.Add(face.RelativePath);
            return new ReflectionCubemapAsset(key, faces);
        }

        private static GeneratedStaticReflectionProbe? LoadGenerated(
            GeneratedReflectionProbeDeclaration? declaration, string room,
            List<ReflectionProbePackIssue> issues)
        {
            if (declaration is null) return null;
            if (!ReflectionProbeKey.TryParse(declaration.Key, out ReflectionProbeKey key)
                || !key.Value.StartsWith($"room/{room}/reflection/", StringComparison.Ordinal)
                || declaration.Resolution is < 16 or > 2048
                || (declaration.Resolution & (declaration.Resolution - 1)) != 0)
            {
                Issue(issues, ReflectionProbePackIssueKind.InvalidGeneratedDescriptor,
                    "Generated-static probe key or resolution is invalid.", room);
                return null;
            }
            Vector3 position = Vector3.Zero;
            if (declaration.CapturePosition is not null)
            {
                float[] values = declaration.CapturePosition;
                if (values.Length != 3 || values.Any(value => !float.IsFinite(value) || MathF.Abs(value) > 1_000_000))
                {
                    Issue(issues, ReflectionProbePackIssueKind.InvalidGeneratedDescriptor,
                        "Generated-static capture position is invalid.", room);
                    return null;
                }
                position = new Vector3(values[0], values[1], values[2]);
            }
            return new GeneratedStaticReflectionProbe(key, declaration.Resolution, position);
        }

        private static ReflectionProbeFaceAsset? LoadFace(string root, string path, string? room,
            Func<ReadOnlyMemory<byte>, DecodedRgbImage> decoder,
            Dictionary<string, ReflectionProbeFaceAsset?> cache, List<ReflectionProbePackIssue> issues)
        {
            if (!TryNormalizePngPath(path, out string normalized))
            {
                Issue(issues, ReflectionProbePackIssueKind.UnsafePath, "Cubemap face path is unsafe.", room, path);
                return null;
            }
            if (cache.TryGetValue(normalized, out ReflectionProbeFaceAsset? cached)) return cached;
            cache.Add(normalized, null);
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison()) || HasLink(root, normalized))
                {
                    Issue(issues, ReflectionProbePackIssueKind.UnsafePath, "Cubemap face crosses a link or pack boundary.", room, normalized);
                    return null;
                }
                if (!File.Exists(candidate))
                {
                    Issue(issues, ReflectionProbePackIssueKind.MissingFace, "Cubemap face was not found.", room, normalized);
                    return null;
                }
                if (!TryReadBounded(candidate, ReflectionProbePackLimits.MaximumFaceFileBytes, out byte[] png))
                {
                    Issue(issues, ReflectionProbePackIssueKind.FaceTooLarge, "Cubemap face exceeds its file limit.", room, normalized);
                    return null;
                }
                if (!TryPngDimension(png, out int width, out int height) || width != height)
                {
                    Issue(issues, ReflectionProbePackIssueKind.UnsupportedFace, "Cubemap face must be a square PNG.", room, normalized);
                    return null;
                }
                if (width > ReflectionProbePackLimits.MaximumFaceDimension)
                {
                    Issue(issues, ReflectionProbePackIssueKind.FaceTooLarge, "Cubemap face exceeds its dimension limit.", room, normalized);
                    return null;
                }
                DecodedRgbImage decoded;
                try
                {
                    decoded = decoder(png);
                    if (decoded.Width != width || decoded.Height != height)
                        throw new InvalidDataException("Decoded dimensions differ from IHDR.");
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                {
                    Issue(issues, ReflectionProbePackIssueKind.DecodeFailed, "Cubemap face failed to decode.", room, normalized);
                    return null;
                }
                ReadOnlySpan<byte> rgb = decoded.Pixels.Span;
                byte[] rgba = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
                for (int source = 0, destination = 0; source < rgb.Length;
                    source += 3, destination += 4)
                {
                    rgba[destination] = rgb[source];
                    rgba[destination + 1] = rgb[source + 1];
                    rgba[destination + 2] = rgb[source + 2];
                    rgba[destination + 3] = 255;
                }
                var asset = new ReflectionProbeFaceAsset(normalized, width, png, rgba);
                cache[normalized] = asset;
                return asset;
            }
            catch (Exception error) when (IsFileError(error))
            {
                Issue(issues, ReflectionProbePackIssueKind.MissingFace, "Cubemap face could not be read.", room, normalized);
                return null;
            }
        }

        private static bool TryReadBounded(string path, long maximum, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            if (stream.Length is <= 0 || stream.Length > maximum || stream.Length > Int32.MaxValue) return false;
            bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            stream.ReadExactly(bytes);
            if (stream.ReadByte() == -1) return true;
            bytes = Array.Empty<byte>();
            return false;
        }

        private static bool TryNormalizePngPath(string? path, out string normalized)
        {
            normalized = String.Empty;
            if (String.IsNullOrWhiteSpace(path) || path.Length > 512
                || !String.Equals(path, path.Trim(), StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = path.Split('/');
            if (parts.Any(part => part.Length is 0 or > 128 || part is "." or "..")) return false;
            normalized = String.Join('/', parts);
            return true;
        }

        private static bool TryPngDimension(ReadOnlySpan<byte> bytes, out int width, out int height)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            width = height = 0;
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
                || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
                || !bytes[12..16].SequenceEqual("IHDR"u8)) return false;
            uint w = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            uint h = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
            if (w == 0 || h == 0 || w > Int32.MaxValue || h > Int32.MaxValue) return false;
            width = (int)w;
            height = (int)h;
            return true;
        }

        private static bool HasLink(string root, string path)
        {
            if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) return true;
            string current = root;
            foreach (string part in path.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
            }
            return false;
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
                foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
        }

        private static void Issue(List<ReflectionProbePackIssue> issues, ReflectionProbePackIssueKind kind,
            string message, string? room = null, string? path = null)
            => issues.Add(new ReflectionProbePackIssue(kind, message, room, path));
        private static StringComparer PathComparer() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static StringComparison PathComparison() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or SecurityException;
    }
}
