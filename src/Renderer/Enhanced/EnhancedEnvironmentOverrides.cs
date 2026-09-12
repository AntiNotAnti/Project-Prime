using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTK.Mathematics;

namespace MphRead
{
    public enum EnhancedEnvironmentIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedManifestVersion,
        TooManyRooms,
        InvalidRoom,
        DuplicateRoom
    }

    public sealed record EnhancedEnvironmentIssue(
        EnhancedEnvironmentIssueKind Kind,
        string Message,
        string? RoomKey = null);

    public sealed class EnhancedEnvironmentLoadResult
    {
        internal EnhancedEnvironmentLoadResult(EnhancedEnvironmentOverrides overrides,
            IReadOnlyList<EnhancedEnvironmentIssue> issues)
        {
            Overrides = overrides;
            Issues = issues;
        }

        public EnhancedEnvironmentOverrides Overrides { get; }
        public IReadOnlyList<EnhancedEnvironmentIssue> Issues { get; }
    }

    /// <summary>Immutable, fail-soft room overrides layered over metadata defaults.</summary>
    public sealed class EnhancedEnvironmentOverrides
    {
        private readonly IReadOnlyDictionary<string, EnhancedEnvironmentOverride> _rooms;

        internal EnhancedEnvironmentOverrides(
            IReadOnlyDictionary<string, EnhancedEnvironmentOverride> rooms)
        {
            _rooms = rooms;
        }

        public static EnhancedEnvironmentOverrides Empty { get; }
            = new(new Dictionary<string, EnhancedEnvironmentOverride>(StringComparer.Ordinal));

        public int Count => _rooms.Count;

        public EnhancedEnvironment Resolve(RoomMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return Resolve(metadata.Name, EnhancedEnvironment.FromRoomMetadata(metadata));
        }

        public EnhancedEnvironment Resolve(string roomKey, EnhancedEnvironment fallback)
        {
            if (String.IsNullOrWhiteSpace(roomKey))
            {
                throw new ArgumentException("A stable room key is required.", nameof(roomKey));
            }
            if (!fallback.IsInitialized)
            {
                throw new ArgumentException("Fallback environment must be initialized.",
                    nameof(fallback));
            }
            return _rooms.TryGetValue(roomKey, out EnhancedEnvironmentOverride? value)
                ? value.Apply(fallback)
                : fallback;
        }
    }

    public static class EnhancedEnvironmentOverrideLoader
    {
        public const string ManifestFileName = "environments.json";
        public const int CurrentFormat = 1;
        public const int MaximumManifestBytes = 256 * 1024;
        public const int MaximumRooms = 512;
        public const int MaximumRoomKeyLength = 128;

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static EnhancedEnvironmentLoadResult Load(string packRoot)
        {
            var issues = new List<EnhancedEnvironmentIssue>();
            string root;
            try
            {
                root = Path.GetFullPath(packRoot ?? throw new ArgumentNullException(nameof(packRoot)));
            }
            catch (Exception error) when (IsPathError(error))
            {
                issues.Add(new EnhancedEnvironmentIssue(
                    EnhancedEnvironmentIssueKind.MissingManifest,
                    "Enhanced environment root is invalid."));
                return Result(EnhancedEnvironmentOverrides.Empty, issues);
            }

            string manifestPath = Path.Combine(root, ManifestFileName);
            byte[] bytes;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new EnhancedEnvironmentIssue(
                        EnhancedEnvironmentIssueKind.MissingManifest,
                        $"{ManifestFileName} was not found."));
                    return Result(EnhancedEnvironmentOverrides.Empty, issues);
                }
                if (!TryReadBounded(manifestPath, out bytes))
                {
                    issues.Add(new EnhancedEnvironmentIssue(
                        EnhancedEnvironmentIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and {MaximumManifestBytes} bytes."));
                    return Result(EnhancedEnvironmentOverrides.Empty, issues);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                issues.Add(new EnhancedEnvironmentIssue(
                    EnhancedEnvironmentIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return Result(EnhancedEnvironmentOverrides.Empty, issues);
            }

            return Parse(bytes, issues);
        }

        internal static EnhancedEnvironmentLoadResult Parse(ReadOnlyMemory<byte> bytes,
            List<EnhancedEnvironmentIssue>? issues = null)
        {
            issues ??= new List<EnhancedEnvironmentIssue>();
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = 12 });
                RejectDuplicateProperties(document.RootElement);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Environment manifest must be an object.");
                }
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("format" or "rooms"))
                    {
                        throw new JsonException($"Unknown manifest property '{property.Name}'.");
                    }
                }
                int format = root.TryGetProperty("format", out JsonElement formatElement)
                    ? formatElement.GetInt32() : CurrentFormat;
                if (format != CurrentFormat)
                {
                    issues.Add(new EnhancedEnvironmentIssue(
                        EnhancedEnvironmentIssueKind.UnsupportedManifestVersion,
                        $"Environment manifest format {format} is not supported."));
                    return Result(EnhancedEnvironmentOverrides.Empty, issues);
                }
                if (!root.TryGetProperty("rooms", out JsonElement rooms)
                    || rooms.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Environment manifest rooms must be an array.");
                }

                var parsed = new List<EnhancedEnvironmentOverride>();
                int entryCount = 0;
                foreach (JsonElement element in rooms.EnumerateArray())
                {
                    entryCount++;
                    if (entryCount > MaximumRooms)
                    {
                        issues.Add(new EnhancedEnvironmentIssue(
                            EnhancedEnvironmentIssueKind.TooManyRooms,
                            $"Environment manifest exceeds {MaximumRooms} rooms."));
                        return Result(EnhancedEnvironmentOverrides.Empty, issues);
                    }
                    try
                    {
                        EnvironmentRoomDeclaration? declaration
                            = element.Deserialize<EnvironmentRoomDeclaration>(_json);
                        parsed.Add(CreateOverride(declaration
                            ?? throw new JsonException("Room override is null.")));
                    }
                    catch (Exception error) when (error is JsonException or FormatException
                        or ArgumentException or OverflowException)
                    {
                        string? room = TryGetRoomKey(element);
                        issues.Add(new EnhancedEnvironmentIssue(
                            EnhancedEnvironmentIssueKind.InvalidRoom,
                            "Malformed room environment override was ignored.", room));
                    }
                }

                var duplicateRooms = new HashSet<string>(StringComparer.Ordinal);
                var seenRooms = new HashSet<string>(StringComparer.Ordinal);
                foreach (EnhancedEnvironmentOverride room in parsed)
                {
                    if (!seenRooms.Add(room.RoomKey))
                    {
                        duplicateRooms.Add(room.RoomKey);
                    }
                }

                var result = new Dictionary<string, EnhancedEnvironmentOverride>(StringComparer.Ordinal);
                foreach (EnhancedEnvironmentOverride room in parsed)
                {
                    if (duplicateRooms.Contains(room.RoomKey))
                    {
                        continue;
                    }
                    result.Add(room.RoomKey, room);
                }
                foreach (string room in duplicateRooms)
                {
                    issues.Add(new EnhancedEnvironmentIssue(
                        EnhancedEnvironmentIssueKind.DuplicateRoom,
                        "Duplicate room environment overrides were ignored.", room));
                }
                return Result(new EnhancedEnvironmentOverrides(result), issues);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException
                or FormatException or OverflowException)
            {
                issues.Add(new EnhancedEnvironmentIssue(
                    EnhancedEnvironmentIssueKind.MalformedManifest,
                    "Enhanced environment manifest is malformed."));
                return Result(EnhancedEnvironmentOverrides.Empty, issues);
            }
        }

        private static EnhancedEnvironmentOverride CreateOverride(
            EnvironmentRoomDeclaration declaration)
        {
            if (String.IsNullOrWhiteSpace(declaration.Room)
                || declaration.Room.Length > MaximumRoomKeyLength
                || !String.Equals(declaration.Room, declaration.Room.Trim(),
                    StringComparison.Ordinal)
                || HasControlCharacter(declaration.Room))
            {
                throw new ArgumentException("Room key is invalid.");
            }

            Vector3? fogColor = null;
            if (declaration.FogColor is not null)
            {
                if (declaration.FogColor.Length != 3)
                {
                    throw new ArgumentException("Fog color must contain three components.");
                }
                fogColor = new Vector3(declaration.FogColor[0], declaration.FogColor[1],
                    declaration.FogColor[2]);
            }
            EnhancedEnvironmentAssetKey? lut = ParseAssetKey(declaration.LutKey);
            EnhancedEnvironmentAssetKey? reflection = ParseAssetKey(
                declaration.ReflectionProbeKey);

            // Applying to Neutral exercises all scalar/color validation without
            // baking Neutral's values into fields omitted by the override.
            var result = new EnhancedEnvironmentOverride(declaration.Room,
                fogColor, declaration.FogDensity, declaration.FogHeight,
                declaration.FogFalloff, declaration.Exposure, lut, reflection,
                declaration.PrimaryShadowLightIndex);
            _ = result.Apply(EnhancedEnvironment.Neutral);
            return result;
        }

        private static EnhancedEnvironmentAssetKey? ParseAssetKey(string? value)
        {
            if (value is null)
            {
                return null;
            }
            if (!EnhancedEnvironmentAssetKey.TryParse(value,
                out EnhancedEnvironmentAssetKey key))
            {
                throw new FormatException("Environment asset key is unsafe.");
            }
            return key;
        }

        private static bool HasControlCharacter(string value)
        {
            foreach (char character in value)
            {
                if (Char.IsControl(character))
                {
                    return true;
                }
            }
            return false;
        }

        private static string? TryGetRoomKey(JsonElement element)
        {
            try
            {
                return element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("room", out JsonElement room)
                    && room.ValueKind == JsonValueKind.String
                    ? room.GetString() : null;
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
                    if (!names.Add(property.Name))
                    {
                        throw new JsonException("Duplicate JSON property.");
                    }
                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    RejectDuplicateProperties(child);
                }
            }
        }

        private static bool TryReadBounded(string path, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumManifestBytes)
            {
                return false;
            }
            bytes = new byte[(int)stream.Length];
            int total = 0;
            while (total < bytes.Length)
            {
                int read = stream.Read(bytes, total, bytes.Length - total);
                if (read == 0)
                {
                    return false;
                }
                total += read;
            }
            return stream.ReadByte() == -1;
        }

        private static EnhancedEnvironmentLoadResult Result(
            EnhancedEnvironmentOverrides overrides,
            List<EnhancedEnvironmentIssue> issues)
            => new(overrides, issues.AsReadOnly());

        private static bool IsPathError(Exception error)
            => error is ArgumentException or NotSupportedException
                or PathTooLongException or SecurityException;

        private static bool IsFileError(Exception error)
            => IsPathError(error) || error is IOException or UnauthorizedAccessException;

        private sealed record EnvironmentRoomDeclaration
        {
            public string Room { get; init; } = String.Empty;
            public float[]? FogColor { get; init; }
            public float? FogDensity { get; init; }
            public float? FogHeight { get; init; }
            public float? FogFalloff { get; init; }
            public float? Exposure { get; init; }
            public string? LutKey { get; init; }
            public string? ReflectionProbeKey { get; init; }
            public int? PrimaryShadowLightIndex { get; init; }
        }
    }

    internal sealed record EnhancedEnvironmentOverride(
        string RoomKey,
        Vector3? FogColor,
        float? FogDensity,
        float? FogHeight,
        float? FogFalloff,
        float? Exposure,
        EnhancedEnvironmentAssetKey? LutKey,
        EnhancedEnvironmentAssetKey? ReflectionProbeKey,
        int? PrimaryShadowLightIndex)
    {
        public EnhancedEnvironment Apply(EnhancedEnvironment fallback)
            => new(
                FogColor ?? fallback.FogColor,
                FogDensity ?? fallback.FogDensity,
                FogHeight ?? fallback.FogHeight,
                FogFalloff ?? fallback.FogFalloff,
                Exposure ?? fallback.Exposure,
                LutKey ?? fallback.LutKey,
                ReflectionProbeKey ?? fallback.ReflectionProbeKey,
                PrimaryShadowLightIndex ?? fallback.PrimaryShadowLightIndex);
    }
}
