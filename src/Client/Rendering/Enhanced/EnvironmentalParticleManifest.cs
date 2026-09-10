using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods;
using OpenTK.Mathematics;

namespace MphRead
{
    public enum EnvironmentalParticleManifestIssueKind
    {
        MissingManifest,
        ManifestTooLarge,
        MalformedManifest,
        UnsupportedVersion,
        TooManyRooms,
        TooManyLayers,
        DuplicateRoom,
        InvalidRoom,
        InvalidLayer
    }

    public sealed record EnvironmentalParticleManifestIssue(
        EnvironmentalParticleManifestIssueKind Kind,
        string Message,
        string? Room = null,
        string? Layer = null);

    /// <summary>Strict opt-in room mapping. An absent room has no atmosphere.</summary>
    public sealed class EnvironmentalParticleCatalog
    {
        private readonly IReadOnlyDictionary<string,
            IReadOnlyList<EnvironmentalParticleLayerDescriptor>> _rooms;

        internal EnvironmentalParticleCatalog(
            IReadOnlyDictionary<string,
                IReadOnlyList<EnvironmentalParticleLayerDescriptor>> rooms)
        {
            _rooms = rooms;
        }

        public static EnvironmentalParticleCatalog Empty { get; } = new(
            new Dictionary<string,
                IReadOnlyList<EnvironmentalParticleLayerDescriptor>>(
                    StringComparer.Ordinal));

        public int RoomCount => _rooms.Count;

        public bool TryResolve(string? roomArchive,
            out IReadOnlyList<EnvironmentalParticleLayerDescriptor> layers)
        {
            if (roomArchive != null && _rooms.TryGetValue(roomArchive, out layers!))
                return true;
            layers = Array.Empty<EnvironmentalParticleLayerDescriptor>();
            return false;
        }
    }

    public sealed record EnvironmentalParticleCatalogLoadResult(
        EnvironmentalParticleCatalog Catalog,
        IReadOnlyList<EnvironmentalParticleManifestIssue> Issues);

    /// <summary>
    /// Bounded local loader for enhancements/default/environmental-particles.json.
    /// Invalid authored entries fail soft and no built-in room guesses are supplied.
    /// </summary>
    public static class EnvironmentalParticleManifestLoader
    {
        public const string ManifestFileName = "environmental-particles.json";
        public const int MaximumManifestBytes = 128 * 1024;
        public const int MaximumRooms = 128;
        public const int MaximumStableKeyLength = 96;

        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 24
        };

        public static EnvironmentalParticleCatalogLoadResult Load(string packRoot)
        {
            if (String.IsNullOrWhiteSpace(packRoot))
                throw new ArgumentException("A pack root is required.", nameof(packRoot));
            var issues = new List<EnvironmentalParticleManifestIssue>();
            string root = Path.GetFullPath(packRoot);
            string path = Path.Combine(root, ManifestFileName);
            if (!File.Exists(path))
            {
                issues.Add(new EnvironmentalParticleManifestIssue(
                    EnvironmentalParticleManifestIssueKind.MissingManifest,
                    $"{ManifestFileName} was not found."));
                return new EnvironmentalParticleCatalogLoadResult(
                    EnvironmentalParticleCatalog.Empty, issues);
            }

            byte[] bytes;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.SequentialScan);
                if (stream.Length is <= 0 or > MaximumManifestBytes)
                {
                    issues.Add(new EnvironmentalParticleManifestIssue(
                        EnvironmentalParticleManifestIssueKind.ManifestTooLarge,
                        $"{ManifestFileName} must be between 1 and "
                            + $"{MaximumManifestBytes} bytes."));
                    return new EnvironmentalParticleCatalogLoadResult(
                        EnvironmentalParticleCatalog.Empty, issues);
                }
                bytes = new byte[(int)stream.Length];
                stream.ReadExactly(bytes);
            }
            catch (IOException)
            {
                issues.Add(new EnvironmentalParticleManifestIssue(
                    EnvironmentalParticleManifestIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return new EnvironmentalParticleCatalogLoadResult(
                    EnvironmentalParticleCatalog.Empty, issues);
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new EnvironmentalParticleManifestIssue(
                    EnvironmentalParticleManifestIssueKind.MissingManifest,
                    $"{ManifestFileName} could not be read."));
                return new EnvironmentalParticleCatalogLoadResult(
                    EnvironmentalParticleCatalog.Empty, issues);
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(bytes,
                    new JsonDocumentOptions { MaxDepth = _options.MaxDepth }))
                {
                    RejectDuplicateProperties(document.RootElement);
                }
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(bytes, _options);
                if (manifest == null)
                    throw new JsonException("Manifest root is null.");
                if (manifest.Version != 1)
                {
                    issues.Add(new EnvironmentalParticleManifestIssue(
                        EnvironmentalParticleManifestIssueKind.UnsupportedVersion,
                        $"Unsupported environmental particle manifest version "
                            + $"{manifest.Version}."));
                    return new EnvironmentalParticleCatalogLoadResult(
                        EnvironmentalParticleCatalog.Empty, issues);
                }
                if (manifest.Rooms == null)
                    throw new JsonException("Manifest rooms are required.");
                return Build(manifest, issues);
            }
            catch (JsonException error)
            {
                issues.Add(new EnvironmentalParticleManifestIssue(
                    EnvironmentalParticleManifestIssueKind.MalformedManifest,
                    $"Malformed environmental particle manifest: {error.Message}"));
                return new EnvironmentalParticleCatalogLoadResult(
                    EnvironmentalParticleCatalog.Empty, issues);
            }
        }

        private static EnvironmentalParticleCatalogLoadResult Build(Manifest manifest,
            List<EnvironmentalParticleManifestIssue> issues)
        {
            RoomEntry[] rooms = manifest.Rooms ?? Array.Empty<RoomEntry>();
            if (rooms.Length > MaximumRooms)
            {
                issues.Add(new EnvironmentalParticleManifestIssue(
                    EnvironmentalParticleManifestIssueKind.TooManyRooms,
                    $"A manifest may contain at most {MaximumRooms} rooms."));
                return new EnvironmentalParticleCatalogLoadResult(
                    EnvironmentalParticleCatalog.Empty, issues);
            }

            var resolved = new Dictionary<string,
                IReadOnlyList<EnvironmentalParticleLayerDescriptor>>(
                    StringComparer.Ordinal);
            foreach (RoomEntry room in rooms)
            {
                if (!IsStableToken(room.Room))
                {
                    issues.Add(new EnvironmentalParticleManifestIssue(
                        EnvironmentalParticleManifestIssueKind.InvalidRoom,
                        "Room archive must be a bounded canonical token.", room.Room));
                    continue;
                }
                if (resolved.ContainsKey(room.Room!))
                {
                    issues.Add(new EnvironmentalParticleManifestIssue(
                        EnvironmentalParticleManifestIssueKind.DuplicateRoom,
                        "Duplicate environmental particle room entry.", room.Room));
                    continue;
                }
                LayerEntry[] authored = room.Layers ?? Array.Empty<LayerEntry>();
                if (authored.Length > EnvironmentalParticleRuntime.MaximumLayerCount)
                {
                    issues.Add(new EnvironmentalParticleManifestIssue(
                        EnvironmentalParticleManifestIssueKind.TooManyLayers,
                        $"A room may contain at most "
                            + $"{EnvironmentalParticleRuntime.MaximumLayerCount} layers.",
                        room.Room));
                    continue;
                }

                var layers = new List<EnvironmentalParticleLayerDescriptor>(authored.Length);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (LayerEntry entry in authored)
                {
                    try
                    {
                        if (!IsStableToken(entry.Key) || !keys.Add(entry.Key!))
                            throw new ArgumentException("Layer key is invalid or duplicated.");
                        if (!Enum.TryParse(entry.Kind, ignoreCase: false,
                            out EnvironmentalParticleKind kind)
                            || !Enum.IsDefined(kind))
                            throw new ArgumentException("Particle kind is invalid.");
                        Vector3 minimum = Vector3Value(entry.BoundsMinimum,
                            nameof(entry.BoundsMinimum));
                        Vector3 maximum = Vector3Value(entry.BoundsMaximum,
                            nameof(entry.BoundsMaximum));
                        Vector3 drift = Vector3Value(entry.DriftVelocity,
                            nameof(entry.DriftVelocity));
                        Vector4 tint = Vector4Value(entry.Tint, nameof(entry.Tint));
                        if (!double.IsFinite(entry.MinimumLifetimeSeconds)
                            || !double.IsFinite(entry.MaximumLifetimeSeconds)
                            || entry.MinimumLifetimeSeconds <= 0
                            || entry.MaximumLifetimeSeconds <= 0)
                            throw new ArgumentOutOfRangeException(nameof(entry.MinimumLifetimeSeconds));
                        TimeSpan minimumLifetime
                            = TimeSpan.FromSeconds(entry.MinimumLifetimeSeconds);
                        TimeSpan maximumLifetime
                            = TimeSpan.FromSeconds(entry.MaximumLifetimeSeconds);
                        if (entry.EmissionRatePerSecond
                            > EnvironmentalParticleRuntime.MaximumEmissionRatePerSecond)
                            throw new ArgumentOutOfRangeException(nameof(entry.EmissionRatePerSecond));
                        layers.Add(new EnvironmentalParticleLayerDescriptor(entry.Key!,
                            entry.SpawnRegionKey, kind, minimum, maximum,
                            entry.MaximumParticles, entry.EmissionRatePerSecond,
                            minimumLifetime, maximumLifetime, entry.MinimumSize,
                            entry.MaximumSize, drift, tint, entry.EmissiveStrength));
                    }
                    catch (Exception error) when (error is ArgumentException
                        or OverflowException)
                    {
                        issues.Add(new EnvironmentalParticleManifestIssue(
                            EnvironmentalParticleManifestIssueKind.InvalidLayer,
                            $"Invalid environmental particle layer: {error.Message}",
                            room.Room, entry.Key));
                    }
                }
                resolved.Add(room.Room!, Array.AsReadOnly(layers.ToArray()));
            }

            return new EnvironmentalParticleCatalogLoadResult(
                new EnvironmentalParticleCatalog(resolved), issues);
        }

        private static bool IsStableToken(string? value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > MaximumStableKeyLength)
                return false;
            foreach (char character in value)
                if (!(character is >= 'a' and <= 'z'
                    || character is >= 'A' and <= 'Z'
                    || character is >= '0' and <= '9'
                    || character is '-' or '_')) return false;
            return true;
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

        private static Vector3 Vector3Value(float[]? values, string parameterName)
            => values is { Length: 3 }
                ? new Vector3(values[0], values[1], values[2])
                : throw new ArgumentException("Expected exactly three components.",
                    parameterName);

        private static Vector4 Vector4Value(float[]? values, string parameterName)
            => values is { Length: 4 }
                ? new Vector4(values[0], values[1], values[2], values[3])
                : throw new ArgumentException("Expected exactly four components.",
                    parameterName);

        private sealed class Manifest
        {
            public int Version { get; set; }
            public RoomEntry[]? Rooms { get; set; }
        }

        private sealed class RoomEntry
        {
            public string? Room { get; set; }
            public LayerEntry[]? Layers { get; set; }
        }

        private sealed class LayerEntry
        {
            public string? Key { get; set; }
            public ulong SpawnRegionKey { get; set; }
            public string? Kind { get; set; }
            public float[]? BoundsMinimum { get; set; }
            public float[]? BoundsMaximum { get; set; }
            public int MaximumParticles { get; set; }
            public float EmissionRatePerSecond { get; set; }
            public double MinimumLifetimeSeconds { get; set; }
            public double MaximumLifetimeSeconds { get; set; }
            public float MinimumSize { get; set; }
            public float MaximumSize { get; set; }
            public float[]? DriftVelocity { get; set; }
            public float[]? Tint { get; set; }
            public float EmissiveStrength { get; set; }
        }
    }

    internal static class EnvironmentalParticlePresentationClock
    {
        public readonly record struct FrameTime(
            TimeSpan SchedulingTime,
            TimeSpan SamplingTime);

        public static TimeSpan FromSimulationTick(ulong tick)
        {
            const ulong tickRate = SimTicks.Hz;
            UInt128 timeTicks = (UInt128)tick * TimeSpan.TicksPerSecond / tickRate;
            return timeTicks > (UInt128)TimeSpan.MaxValue.Ticks
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks((long)timeTicks);
        }

        public static FrameTime Capture(ulong tick, float renderFraction)
        {
            if (!float.IsFinite(renderFraction)
                || renderFraction < 0 || renderFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(renderFraction));
            TimeSpan schedulingTime = FromSimulationTick(tick);
            decimal sampleTicks = ((decimal)tick + (decimal)renderFraction)
                * TimeSpan.TicksPerSecond / SimTicks.Hz;
            TimeSpan samplingTime = sampleTicks >= TimeSpan.MaxValue.Ticks
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks(decimal.ToInt64(decimal.Floor(sampleTicks)));
            return new FrameTime(schedulingTime, samplingTime);
        }
    }

    internal sealed class EnvironmentalParticlePresentationState
    {
        private readonly EnvironmentalParticleCatalog _catalog;
        private readonly int _capacity;
        private string? _roomKey;
        private IReadOnlyList<EnvironmentalParticleLayerDescriptor> _layers
            = Array.Empty<EnvironmentalParticleLayerDescriptor>();
        private EnvironmentalParticleRuntime? _runtime;
        private ulong? _lastTick;

        public EnvironmentalParticlePresentationState(
            EnvironmentalParticleCatalog catalog, int capacity)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (capacity < 1
                || capacity > EnvironmentalParticleRuntime.MaximumRoomCapacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public void ConfigureRoom(string roomArchive)
        {
            if (String.IsNullOrWhiteSpace(roomArchive))
                throw new ArgumentException("A room archive is required.",
                    nameof(roomArchive));
            _roomKey = roomArchive;
            _catalog.TryResolve(roomArchive, out _layers);
            Reset();
        }

        public EnvironmentalParticleSnapshot? Prepare(GraphicsPreset preset,
            ulong presentationTick, float renderFraction = 0)
        {
            if (preset != GraphicsPreset.Enhanced || _roomKey == null
                || _layers.Count == 0)
            {
                Reset();
                return null;
            }

            EnvironmentalParticlePresentationClock.FrameTime time
                = EnvironmentalParticlePresentationClock.Capture(
                    presentationTick, renderFraction);
            if (_runtime == null)
            {
                _runtime = new EnvironmentalParticleRuntime(_capacity);
                _runtime.EnterRoom(_roomKey, _layers, TimeSpan.Zero);
            }
            else if (_lastTick.HasValue && presentationTick < _lastTick.Value)
            {
                _runtime.Reset(TimeSpan.Zero);
            }
            _lastTick = presentationTick;
            return _runtime.Advance(time.SchedulingTime, time.SamplingTime);
        }

        public void Reset()
        {
            _runtime = null;
            _lastTick = null;
        }
    }
}
