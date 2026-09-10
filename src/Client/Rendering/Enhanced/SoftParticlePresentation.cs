using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead
{
    /// <summary>
    /// Stable presentation identity for one element in an authored effect.
    /// Effect and element order are content identities; runtime particle
    /// instances, textures, and RenderPrimitive values deliberately are not.
    /// </summary>
    public readonly struct SoftParticlePresentationKey
        : IEquatable<SoftParticlePresentationKey>, IComparable<SoftParticlePresentationKey>
    {
        private readonly string? _value;

        private SoftParticlePresentationKey(int effectId, int elementIndex)
        {
            EffectId = effectId;
            ElementIndex = elementIndex;
            _value = $"effect/{effectId.ToString(CultureInfo.InvariantCulture)}"
                + $"/element/{elementIndex.ToString(CultureInfo.InvariantCulture)}";
        }

        public int EffectId { get; }
        public int ElementIndex { get; }
        public string Value => _value ?? String.Empty;
        public bool IsValid => _value is not null;

        public static SoftParticlePresentationKey Create(int effectId, int elementIndex)
        {
            if (effectId <= 0) throw new ArgumentOutOfRangeException(nameof(effectId));
            if (elementIndex < 0) throw new ArgumentOutOfRangeException(nameof(elementIndex));
            return new SoftParticlePresentationKey(effectId, elementIndex);
        }

        public static bool TryParse(string? value, out SoftParticlePresentationKey key)
        {
            key = default;
            if (String.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('/');
            if (parts.Length != 4
                || !String.Equals(parts[0], "effect", StringComparison.Ordinal)
                || !String.Equals(parts[2], "element", StringComparison.Ordinal)
                || !TryParseCanonicalInteger(parts[1], positive: true, out int effectId)
                || !TryParseCanonicalInteger(parts[3], positive: false, out int elementIndex))
            {
                return false;
            }

            var parsed = new SoftParticlePresentationKey(effectId, elementIndex);
            if (!String.Equals(value, parsed.Value, StringComparison.Ordinal)) return false;
            key = parsed;
            return true;
        }

        private static bool TryParseCanonicalInteger(string value, bool positive,
            out int result)
        {
            result = 0;
            if (value.Length == 0 || value.Length > 10
                || value.Length > 1 && value[0] == '0')
            {
                return false;
            }
            foreach (char character in value)
            {
                if (character is < '0' or > '9') return false;
            }
            return Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                out result) && (positive ? result > 0 : result >= 0);
        }

        public int CompareTo(SoftParticlePresentationKey other)
        {
            int effect = EffectId.CompareTo(other.EffectId);
            return effect != 0 ? effect : ElementIndex.CompareTo(other.ElementIndex);
        }

        public bool Equals(SoftParticlePresentationKey other)
            => EffectId == other.EffectId && ElementIndex == other.ElementIndex
                && IsValid == other.IsValid;
        public override bool Equals(object? obj)
            => obj is SoftParticlePresentationKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(EffectId, ElementIndex, IsValid);
        public override string ToString() => Value;
        public static bool operator ==(SoftParticlePresentationKey left,
            SoftParticlePresentationKey right) => left.Equals(right);
        public static bool operator !=(SoftParticlePresentationKey left,
            SoftParticlePresentationKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Explicit opt-in depth-fade settings. Absence of a profile means the
    /// particle remains hard, which is the safe default for projectiles and
    /// precise gameplay indicators.
    /// </summary>
    public readonly record struct SoftParticleProfile
    {
        public const float MinimumFadeDistance = 1f / 4096;
        public const float MaximumFadeDistance = 64;

        public SoftParticleProfile(float fadeDistance)
        {
            if (!float.IsFinite(fadeDistance)
                || fadeDistance < MinimumFadeDistance
                || fadeDistance > MaximumFadeDistance)
            {
                throw new ArgumentOutOfRangeException(nameof(fadeDistance));
            }
            FadeDistance = fadeDistance;
        }

        public float FadeDistance { get; }
        internal bool IsInitialized
            => float.IsFinite(FadeDistance)
                && FadeDistance >= MinimumFadeDistance
                && FadeDistance <= MaximumFadeDistance;
    }

    public readonly record struct SoftParticleDescriptor
    {
        public SoftParticleDescriptor(SoftParticlePresentationKey key,
            SoftParticleProfile profile)
        {
            if (!key.IsValid) throw new ArgumentException("Particle key is not initialized.", nameof(key));
            if (!profile.IsInitialized)
                throw new ArgumentException("Soft-particle profile is not initialized.", nameof(profile));
            Key = key;
            Profile = profile;
        }

        public SoftParticlePresentationKey Key { get; }
        public SoftParticleProfile Profile { get; }
    }

    /// <summary>Pure positive-linear-depth reference used by future GPU code.</summary>
    public static class SoftParticleDepthFadePolicy
    {
        public const float BackgroundSurfaceDepth = 0;

        public static float Evaluate(float particleLinearDepth,
            float surfaceLinearDepth, SoftParticleProfile? profile)
        {
            if (!float.IsFinite(particleLinearDepth) || particleLinearDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(particleLinearDepth));
            if (!float.IsFinite(surfaceLinearDepth) || surfaceLinearDepth < 0)
                throw new ArgumentOutOfRangeException(nameof(surfaceLinearDepth));

            if (!profile.HasValue) return 1;
            if (!profile.Value.IsInitialized)
                throw new ArgumentException("Soft-particle profile is not initialized.", nameof(profile));
            if (surfaceLinearDepth == BackgroundSurfaceDepth) return 1;

            return Math.Clamp((surfaceLinearDepth - particleLinearDepth)
                / profile.Value.FadeDistance, 0, 1);
        }
    }

    public enum SoftParticleManifestIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyProfiles,
        InvalidProfile,
        DuplicateProfile
    }

    public sealed record SoftParticleManifestIssue(
        SoftParticleManifestIssueKind Kind,
        string Message,
        string? Key = null);

    public sealed class SoftParticleProfileCatalog
    {
        private readonly IReadOnlyDictionary<SoftParticlePresentationKey,
            SoftParticleProfile> _profiles;
        private readonly IReadOnlyList<SoftParticleDescriptor> _descriptors;

        internal SoftParticleProfileCatalog(
            IReadOnlyDictionary<SoftParticlePresentationKey, SoftParticleProfile> profiles,
            IReadOnlyList<SoftParticleDescriptor> descriptors)
        {
            _profiles = profiles;
            _descriptors = descriptors;
        }

        public static SoftParticleProfileCatalog Empty { get; } = new(
            new Dictionary<SoftParticlePresentationKey, SoftParticleProfile>(),
            Array.Empty<SoftParticleDescriptor>());

        public int Count => _profiles.Count;
        public IReadOnlyList<SoftParticleDescriptor> Descriptors => _descriptors;

        public bool TryResolve(SoftParticlePresentationKey key,
            out SoftParticleProfile profile)
        {
            if (!key.IsValid)
            {
                profile = default;
                return false;
            }
            return _profiles.TryGetValue(key, out profile);
        }
    }

    public sealed class SoftParticleProfileLoadResult
    {
        internal SoftParticleProfileLoadResult(SoftParticleProfileCatalog catalog,
            IReadOnlyList<SoftParticleManifestIssue> issues)
        {
            Catalog = catalog;
            Issues = issues;
        }

        public SoftParticleProfileCatalog Catalog { get; }
        public IReadOnlyList<SoftParticleManifestIssue> Issues { get; }
    }

    /// <summary>
    /// Strict, bounded, optional local manifest. Invalid content fails soft to
    /// an empty catalog, so no particle becomes soft without a valid opt-in.
    /// </summary>
    public static class SoftParticleProfileLoader
    {
        public const string ManifestFileName = "soft-particles.json";
        public const int CurrentFormat = 1;
        public const int MaximumManifestBytes = 128 * 1024;
        public const int MaximumProfiles = 1024;

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static SoftParticleProfileLoadResult Load(string packRoot)
        {
            var issues = new List<SoftParticleManifestIssue>();
            string root;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
            }
            catch (Exception error) when (IsPathError(error))
            {
                issues.Add(new SoftParticleManifestIssue(
                    SoftParticleManifestIssueKind.MissingManifest,
                    "Soft-particle manifest root is invalid."));
                return Result(SoftParticleProfileCatalog.Empty, issues);
            }

            string path = Path.Combine(root, ManifestFileName);
            try
            {
                if (!File.Exists(path))
                {
                    issues.Add(new SoftParticleManifestIssue(
                        SoftParticleManifestIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return Result(SoftParticleProfileCatalog.Empty, issues);
                }
                if (!TryReadBounded(path, out byte[] bytes))
                {
                    issues.Add(new SoftParticleManifestIssue(
                        SoftParticleManifestIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and {MaximumManifestBytes} bytes."));
                    return Result(SoftParticleProfileCatalog.Empty, issues);
                }
                return Parse(bytes, issues);
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new SoftParticleManifestIssue(
                    SoftParticleManifestIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return Result(SoftParticleProfileCatalog.Empty, issues);
            }
        }

        internal static SoftParticleProfileLoadResult Parse(ReadOnlyMemory<byte> bytes,
            List<SoftParticleManifestIssue>? issues = null)
        {
            issues ??= new List<SoftParticleManifestIssue>();
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 8 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Soft-particle manifest must be an object.");
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "profiles"))
                        throw new JsonException($"Unknown manifest property '{property.Name}'.");
                }
                if (!root.TryGetProperty("format", out JsonElement formatElement))
                    throw new JsonException("Soft-particle manifest format is required.");
                int format = formatElement.GetInt32();
                if (format != CurrentFormat)
                {
                    issues.Add(new SoftParticleManifestIssue(
                        SoftParticleManifestIssueKind.UnsupportedManifestVersion,
                        $"Soft-particle manifest format {format} is not supported."));
                    return Result(SoftParticleProfileCatalog.Empty, issues);
                }
                if (!root.TryGetProperty("profiles", out JsonElement profiles)
                    || profiles.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Soft-particle manifest profiles must be an array.");
                }

                var parsed = new List<SoftParticleDescriptor>();
                int count = 0;
                foreach (JsonElement element in profiles.EnumerateArray())
                {
                    if (++count > MaximumProfiles)
                    {
                        issues.Add(new SoftParticleManifestIssue(
                            SoftParticleManifestIssueKind.TooManyProfiles,
                            $"Soft-particle manifest exceeds {MaximumProfiles} profiles."));
                        return Result(SoftParticleProfileCatalog.Empty, issues);
                    }
                    try
                    {
                        SoftParticleDeclaration declaration
                            = element.Deserialize<SoftParticleDeclaration>(_json)
                                ?? throw new JsonException("Soft-particle profile is null.");
                        if (!SoftParticlePresentationKey.TryParse(declaration.Key,
                            out SoftParticlePresentationKey key))
                        {
                            throw new FormatException("Soft-particle key is invalid.");
                        }
                        parsed.Add(new SoftParticleDescriptor(key,
                            new SoftParticleProfile(declaration.FadeDistance)));
                    }
                    catch (Exception error) when (error is JsonException or FormatException
                        or ArgumentException or OverflowException)
                    {
                        issues.Add(new SoftParticleManifestIssue(
                            SoftParticleManifestIssueKind.InvalidProfile,
                            "Malformed soft-particle profile was ignored.", TryGetKey(element)));
                    }
                }

                var duplicates = new HashSet<SoftParticlePresentationKey>();
                var seen = new HashSet<SoftParticlePresentationKey>();
                foreach (SoftParticleDescriptor descriptor in parsed)
                {
                    if (!seen.Add(descriptor.Key)) duplicates.Add(descriptor.Key);
                }
                parsed.RemoveAll(descriptor => duplicates.Contains(descriptor.Key));
                parsed.Sort((left, right) => left.Key.CompareTo(right.Key));

                var resolved = new Dictionary<SoftParticlePresentationKey,
                    SoftParticleProfile>(parsed.Count);
                foreach (SoftParticleDescriptor descriptor in parsed)
                    resolved.Add(descriptor.Key, descriptor.Profile);
                var orderedDuplicates = new List<SoftParticlePresentationKey>(duplicates);
                orderedDuplicates.Sort();
                foreach (SoftParticlePresentationKey duplicate in orderedDuplicates)
                {
                    issues.Add(new SoftParticleManifestIssue(
                        SoftParticleManifestIssueKind.DuplicateProfile,
                        "Duplicate soft-particle profiles were ignored.", duplicate.Value));
                }
                return Result(new SoftParticleProfileCatalog(resolved,
                    parsed.AsReadOnly()), issues);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException
                or FormatException or OverflowException)
            {
                issues.Add(new SoftParticleManifestIssue(
                    SoftParticleManifestIssueKind.MalformedManifest,
                    "Soft-particle manifest is malformed."));
                return Result(SoftParticleProfileCatalog.Empty, issues);
            }
        }

        private static string? TryGetKey(JsonElement element)
        {
            try
            {
                return element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("key", out JsonElement key)
                    && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
            }
            catch (InvalidOperationException)
            {
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
                foreach (JsonElement child in element.EnumerateArray())
                    RejectDuplicateProperties(child);
            }
        }

        private static bool TryReadBounded(string path, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumManifestBytes) return false;
            bytes = new byte[(int)stream.Length];
            int total = 0;
            while (total < bytes.Length)
            {
                int read = stream.Read(bytes, total, bytes.Length - total);
                if (read == 0) return false;
                total += read;
            }
            return stream.ReadByte() == -1;
        }

        private static SoftParticleProfileLoadResult Result(
            SoftParticleProfileCatalog catalog, List<SoftParticleManifestIssue> issues)
            => new(catalog, issues.AsReadOnly());

        private static bool IsPathError(Exception error)
            => error is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException;
        private static bool IsFileError(Exception error)
            => IsPathError(error) || error is IOException or UnauthorizedAccessException;

        private sealed record SoftParticleDeclaration
        {
            public string Key { get; init; } = String.Empty;
            public float FadeDistance { get; init; }
        }
    }
}
