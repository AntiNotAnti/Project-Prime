using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MphRead;

/// <summary>
/// Strict JSON boundary for the retail oracle. Parsing is intentionally
/// hand-written instead of relying on permissive object deserialization:
/// dictionaries in normalized state are dynamic, while every envelope and
/// every metadata object has a closed set of fields.
/// </summary>
public static partial class OracleJson
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 2 * 1024 * 1024;
    public const int MaximumActions = 4096;
    public const int MaximumCheckpoints = 8192;
    public const int MaximumEvents = 16 * 1024;
    public const int MaximumStateFields = 256;
    public const int MaximumText = 1024;
    public const int MaximumPath = 256;
    public const string Amhe1Revision = "AMHE1";
    public const string DefaultProjectRevision = "8a501aa8846915fa6b6d9b315934bead565e9637";
    public const string Amhe1AnchorPath = "_bin/arm9.bin";
    public const string Amhe1AnchorSha256 =
        "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b";
    public const string Amhe1AggregateSha256 =
        "f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226";
    /// <summary>
    /// The complete private AMHE1 identity is owned by the shared cartridge
    /// catalog. Keeping this lookup here lets oracle code bind to the same
    /// frozen header/size/digest record used by the rest of the tools.
    /// </summary>
    public static CartridgeIdentity Amhe1RomIdentity
    {
        get
        {
            if (!CartridgeCatalog.Supported.TryGetIdentity("AMHE", 1,
                    out CartridgeIdentity? identity) || identity is null)
                throw new InvalidOperationException("The shared cartridge catalog is missing AMHE1.");
            return identity;
        }
    }

    private static readonly JsonSerializerOptions SerializationOptions = CreateSerializationOptions();
    private static readonly HashSet<string> RetailHunters = new(StringComparer.Ordinal)
    {
        "Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel"
    };
    private static readonly HashSet<string> StateRoots = new(StringComparer.Ordinal)
    {
        "position", "velocity", "facing", "aim", "health", "ammo", "hunter", "form",
        "weapon", "zoomState", "chargeState", "damageState", "freezeState", "knockbackState",
        "projectiles", "pickupStates", "objectiveState", "animationState", "value", "phase"
    };
    private static readonly HashSet<string> SupportedInvariants = new(StringComparer.Ordinal)
    {
        "finite", "nonNegative", "nonEmpty", "nonDecreasing"
    };

    [GeneratedRegex("^FID-[A-Z0-9]+(?:-[A-Z0-9]+)*-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScenarioIdPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.:/-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldPathPattern();

    public static OracleScenario ParseScenario(ReadOnlySpan<byte> utf8,
        string expectedProjectRevision = DefaultProjectRevision,
        string expectedReferenceRevision = Amhe1Revision)
    {
        using JsonDocument document = ParseDocument(utf8, "scenario");
        JsonElement root = RequireObject(document.RootElement, "$", "scenario");
        EnsureFields(root, "scenario", "schemaVersion", "id", "source", "hunter", "map",
            "seed", "actions", "checkpoints");
        int schema = Int32(Property(root, "schemaVersion", "scenario"), "scenario schemaVersion", 1, 1);
        string id = RequiredString(root, "id", "scenario id", 128);
        if (!ScenarioIdPattern().IsMatch(id)) Fail("Scenario id has an invalid format.");
        OracleSourceIdentity source = ParseSource(Property(root, "source", "scenario"),
            expectedProjectRevision, expectedReferenceRevision);
        string hunter = RequiredString(root, "hunter", "scenario hunter", 32);
        if (!RetailHunters.Contains(hunter)) Fail("Scenario hunter must be one of the seven retail Hunters.");
        string map = RequiredString(root, "map", "scenario map", 128);
        uint seed = UInt32(Property(root, "seed", "scenario"), "scenario seed");

        JsonElement actionsElement = Property(root, "actions", "scenario");
        if (actionsElement.ValueKind != JsonValueKind.Array) Fail("Scenario actions must be an array.");
        List<OracleAction> actions = new();
        foreach (JsonElement item in actionsElement.EnumerateArray())
        {
            if (actions.Count >= MaximumActions) Fail("Scenario actions exceed their bound.");
            JsonElement actionObject = RequireObject(item, $"$/actions/{actions.Count}", "action");
            EnsureFields(actionObject, "action", "at", "action", "values");
            int at = Int32(Property(actionObject, "at", "action"), "action at", 0, int.MaxValue);
            string action = RequiredString(actionObject, "action", "action name", 128);
            JsonElement valuesElement = Property(actionObject, "values", "action");
            IReadOnlyDictionary<string, double> values = ParseNumberObject(valuesElement,
                "action values", 32, 128);
            actions.Add(new OracleAction(at, action, values));
        }

        JsonElement checkpointsElement = Property(root, "checkpoints", "scenario");
        if (checkpointsElement.ValueKind != JsonValueKind.Array)
            Fail("Scenario checkpoints must be an array.");
        List<OracleCheckpointRequest> checkpoints = new();
        var checkpointIds = new HashSet<int>();
        int previousVBlank = -1;
        foreach (JsonElement item in checkpointsElement.EnumerateArray())
        {
            if (checkpoints.Count >= MaximumCheckpoints) Fail("Scenario checkpoints exceed their bound.");
            int vblank;
            double? elapsed;
            int? semanticEvent;
            if (item.ValueKind == JsonValueKind.Number)
            {
                vblank = Int32(item, "checkpoint VBlank", 0, int.MaxValue);
                elapsed = null;
                semanticEvent = null;
            }
            else
            {
                JsonElement checkpoint = RequireObject(item, $"$/checkpoints/{checkpoints.Count}", "checkpoint");
                EnsureFields(checkpoint, "checkpoint", "vblank", "elapsedMilliseconds", "semanticEventIndex");
                vblank = Int32(Property(checkpoint, "vblank", "checkpoint"), "checkpoint VBlank", 0, int.MaxValue);
                elapsed = OptionalFiniteNumber(checkpoint, "elapsedMilliseconds", "checkpoint elapsedMilliseconds", 0, double.MaxValue);
                semanticEvent = OptionalInt32(checkpoint, "semanticEventIndex", "checkpoint semanticEventIndex", 0, int.MaxValue);
            }
            if (!checkpointIds.Add(vblank)) Fail($"Duplicate checkpoint VBlank: {vblank}");
            if (vblank <= previousVBlank) Fail("Scenario checkpoints must be strictly ordered by VBlank.");
            previousVBlank = vblank;
            checkpoints.Add(new OracleCheckpointRequest(vblank, elapsed, semanticEvent));
        }
        if (checkpoints.Count == 0) Fail("Scenario requires at least one checkpoint.");
        return new OracleScenario(schema, id, source, hunter, map, seed, actions, checkpoints);
    }

    public static OracleArtifact ParseArtifact(ReadOnlySpan<byte> utf8,
        string expectedProjectRevision = DefaultProjectRevision,
        string expectedReferenceRevision = Amhe1Revision)
    {
        using JsonDocument document = ParseDocument(utf8, "artifact");
        JsonElement root = RequireObject(document.RootElement, "$", "artifact");
        EnsureFields(root, "artifact", "schemaVersion", "manifest", "checkpoints", "events");
        int schema = Int32(Property(root, "schemaVersion", "artifact"), "artifact schemaVersion", 1, 1);
        OracleArtifactManifest manifest = ParseManifest(Property(root, "manifest", "artifact"),
            expectedProjectRevision, expectedReferenceRevision);
        if (manifest.SchemaVersion != schema) Fail("Artifact and manifest schemas do not match.");

        JsonElement checkpointsElement = Property(root, "checkpoints", "artifact");
        if (checkpointsElement.ValueKind != JsonValueKind.Array) Fail("Artifact checkpoints must be an array.");
        var checkpoints = new List<OracleCheckpoint>();
        var checkpointIds = new HashSet<int>();
        int previousVBlank = -1;
        foreach (JsonElement item in checkpointsElement.EnumerateArray())
        {
            if (checkpoints.Count >= MaximumCheckpoints) Fail("Artifact checkpoints exceed their bound.");
            string path = $"$/checkpoints/{checkpoints.Count}";
            JsonElement checkpoint = RequireObject(item, path, "artifact checkpoint");
            EnsureFields(checkpoint, "artifact checkpoint", "vblank", "elapsedMilliseconds",
                "semanticEventIndex", "state");
            int vblank = Int32(Property(checkpoint, "vblank", "artifact checkpoint"), "checkpoint VBlank", 0, int.MaxValue);
            double elapsed = Number(Property(checkpoint, "elapsedMilliseconds", "artifact checkpoint"),
                "checkpoint elapsedMilliseconds", 0, double.MaxValue);
            int eventIndex = Int32(Property(checkpoint, "semanticEventIndex", "artifact checkpoint"),
                "checkpoint semanticEventIndex", 0, int.MaxValue);
            IReadOnlyDictionary<string, JsonElement> state = ParseState(
                Property(checkpoint, "state", "artifact checkpoint"));
            if (!checkpointIds.Add(vblank)) Fail($"Duplicate artifact checkpoint VBlank: {vblank}");
            if (vblank <= previousVBlank) Fail("Artifact checkpoints must be strictly ordered by VBlank.");
            previousVBlank = vblank;
            checkpoints.Add(new OracleCheckpoint(vblank, elapsed, eventIndex, state));
        }
        if (checkpoints.Count == 0) Fail("Artifact requires at least one checkpoint.");

        JsonElement eventsElement = Property(root, "events", "artifact");
        if (eventsElement.ValueKind != JsonValueKind.Array) Fail("Artifact events must be an array.");
        var events = new List<OracleEvent>();
        var eventIds = new HashSet<int>();
        int previousEventIndex = -1;
        foreach (JsonElement item in eventsElement.EnumerateArray())
        {
            if (events.Count >= MaximumEvents) Fail("Artifact events exceed their bound.");
            string path = $"$/events/{events.Count}";
            JsonElement eventObject = RequireObject(item, path, "artifact event");
            EnsureFields(eventObject, "artifact event", "index", "name", "vblank", "elapsedMilliseconds");
            int index = Int32(Property(eventObject, "index", "artifact event"), "event index", 0, int.MaxValue);
            string name = RequiredString(eventObject, "name", "event name", 256);
            int vblank = Int32(Property(eventObject, "vblank", "artifact event"), "event VBlank", 0, int.MaxValue);
            double elapsed = Number(Property(eventObject, "elapsedMilliseconds", "artifact event"),
                "event elapsedMilliseconds", 0, double.MaxValue);
            if (!eventIds.Add(index)) Fail($"Duplicate event index: {index}");
            if (index <= previousEventIndex) Fail("Artifact events must be strictly ordered by index.");
            previousEventIndex = index;
            events.Add(new OracleEvent(index, name, vblank, elapsed));
        }
        return new OracleArtifact(schema, manifest, checkpoints, events);
    }

    public static OracleNormalizer ParseNormalizer(ReadOnlySpan<byte> utf8,
        string expectedReferenceRevision = Amhe1Revision)
    {
        using JsonDocument document = ParseDocument(utf8, "normalizer");
        JsonElement root = RequireObject(document.RootElement, "$", "normalizer");
        EnsureFields(root, "normalizer", "schemaVersion", "revision", "fields");
        int schema = Int32(Property(root, "schemaVersion", "normalizer"), "normalizer schemaVersion", 1, 1);
        string revision = RequiredString(root, "revision", "normalizer revision", 64);
        if (!StringComparer.Ordinal.Equals(revision, expectedReferenceRevision))
            Fail("Normalizer reference revision does not match the active AMHE revision.");
        JsonElement fieldsElement = Property(root, "fields", "normalizer");
        if (fieldsElement.ValueKind != JsonValueKind.Array || fieldsElement.GetArrayLength() is < 1 or > 256)
            Fail("Normalizer fields must contain between 1 and 256 entries.");
        var fields = new List<OracleFieldRule>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in fieldsElement.EnumerateArray())
        {
            JsonElement field = RequireObject(item, $"$/fields/{fields.Count}", "normalizer field");
            EnsureFields(field, "normalizer field", "path", "mode", "absoluteTolerance", "invariant");
            string path = RequiredString(field, "path", "normalizer field path", MaximumPath);
            if (!FieldPathPattern().IsMatch(path) || path.Contains("..", StringComparison.Ordinal))
                Fail("Normalizer field path is not canonical.");
            if (!paths.Add(path)) Fail($"Duplicate normalizer field path: {path}");
            string modeText = RequiredString(field, "mode", "normalizer field mode", 32);
            if (!Enum.TryParse(modeText, ignoreCase: false, out OracleComparisonMode mode)
                || !Enum.IsDefined(mode))
                Fail("Normalizer field mode is invalid.");
            double? tolerance = OptionalFiniteNumber(field, "absoluteTolerance",
                "normalizer absoluteTolerance", 0, double.MaxValue);
            string? invariant = OptionalString(field, "invariant", "normalizer invariant", 128);
            if (invariant is not null && !SupportedInvariants.Contains(invariant))
                Fail("Normalizer invariant is unsupported by this schema version.");
            if (mode == OracleComparisonMode.Tolerant && tolerance is null)
                Fail("Tolerant normalizer fields require an absoluteTolerance.");
            if (mode != OracleComparisonMode.Tolerant && tolerance is not null)
                Fail("Only tolerant normalizer fields may declare absoluteTolerance.");
            if (mode == OracleComparisonMode.Invariant && invariant is null)
                Fail("Invariant normalizer fields require an invariant name.");
            if (mode != OracleComparisonMode.Invariant && invariant is not null)
                Fail("Only invariant normalizer fields may declare an invariant name.");
            fields.Add(new OracleFieldRule(path, mode, tolerance, invariant));
        }
        return new OracleNormalizer(schema, revision, fields);
    }

    public static OracleNormalizer DefaultNormalizer(string revision = Amhe1Revision)
        => new(SchemaVersion, revision,
            [
                new("position", OracleComparisonMode.Tolerant, .0001, null),
                new("velocity", OracleComparisonMode.Tolerant, .0001, null),
                new("facing", OracleComparisonMode.Tolerant, .0001, null),
                new("aim", OracleComparisonMode.Tolerant, .0001, null),
                new("health", OracleComparisonMode.Exact, null, null),
                new("ammo", OracleComparisonMode.Exact, null, null),
                new("hunter", OracleComparisonMode.Exact, null, null),
                new("form", OracleComparisonMode.Exact, null, null),
                new("weapon", OracleComparisonMode.Exact, null, null),
                new("phase", OracleComparisonMode.Invariant, null, "nonEmpty")
            ]);

    public static OracleArtifact LoadArtifact(string path,
        string expectedProjectRevision = DefaultProjectRevision,
        string expectedReferenceRevision = Amhe1Revision)
        => ParseArtifact(ReadRegularFile(path, "artifact"), expectedProjectRevision, expectedReferenceRevision);

    public static OracleScenario LoadScenario(string root, string relativePath,
        string expectedProjectRevision = DefaultProjectRevision,
        string expectedReferenceRevision = Amhe1Revision)
        => ParseScenario(ReadContainedFile(root, relativePath, "scenario"),
            expectedProjectRevision, expectedReferenceRevision);

    public static OracleArtifact LoadArtifact(string root, string relativePath,
        string expectedProjectRevision, string expectedReferenceRevision, bool relative)
        => ParseArtifact(ReadContainedFile(root, relativePath, "artifact"),
            expectedProjectRevision, expectedReferenceRevision);

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, SerializationOptions) + "\n";

    public static string SerializeScenario(OracleScenario value) => Serialize(value);
    public static string SerializeArtifact(OracleArtifact value) => Serialize(value);
    public static string SerializeNormalizer(OracleNormalizer value) => Serialize(value);
    public static string SerializeComparison(OracleComparison value) => Serialize(value);

    private static OracleSourceIdentity ParseSource(JsonElement element,
        string expectedProjectRevision, string expectedReferenceRevision)
    {
        JsonElement source = RequireObject(element, "$/source", "source identity");
        EnsureFields(source, "source identity", "projectRevision", "referenceRevision",
            "referenceAnchorPath", "referenceAnchorSha256", "referenceAggregateSha256");
        string project = RequiredString(source, "projectRevision", "source projectRevision", 64);
        if (!StringComparer.Ordinal.Equals(project, expectedProjectRevision))
            Fail("Source project revision does not match the active Project Prime revision.");
        string revision = RequiredString(source, "referenceRevision", "source referenceRevision", 64);
        if (!StringComparer.Ordinal.Equals(revision, expectedReferenceRevision)
            || !StringComparer.Ordinal.Equals(revision, Amhe1Revision))
            Fail("Only AMHE1 Revision 1 oracle observations are accepted.");
        string anchorPath = RequiredString(source, "referenceAnchorPath", "source referenceAnchorPath", MaximumPath);
        ValidateRelativePath(anchorPath, "source referenceAnchorPath");
        if (!StringComparer.Ordinal.Equals(anchorPath, Amhe1AnchorPath))
            Fail("Source anchor path does not identify AMHE1 arm9.bin.");
        string anchor = Digest(Property(source, "referenceAnchorSha256", "source identity"),
            "source referenceAnchorSha256");
        if (!StringComparer.OrdinalIgnoreCase.Equals(anchor, Amhe1AnchorSha256))
            Fail("Source AMHE1 anchor digest is not recognized.");
        string aggregate = Digest(Property(source, "referenceAggregateSha256", "source identity"),
            "source referenceAggregateSha256");
        if (!StringComparer.OrdinalIgnoreCase.Equals(aggregate, Amhe1AggregateSha256))
            Fail("Source AMHE1 aggregate digest is not recognized.");
        return new OracleSourceIdentity(project, revision, anchorPath,
            anchor.ToLowerInvariant(), aggregate.ToLowerInvariant());
    }

    private static OracleArtifactManifest ParseManifest(JsonElement element,
        string expectedProjectRevision, string expectedReferenceRevision)
    {
        JsonElement manifest = RequireObject(element, "$/manifest", "artifact manifest");
        EnsureFields(manifest, "artifact manifest", "schemaVersion", "scenarioId", "source",
            "oracleImplementationRevision", "romIdentity", "hostOs", "oracleExecutableIdentity",
            "scenarioHash", "seed", "runTimestamp", "result");
        int schema = Int32(Property(manifest, "schemaVersion", "artifact manifest"),
            "manifest schemaVersion", 1, 1);
        string scenarioId = RequiredString(manifest, "scenarioId", "manifest scenarioId", 128);
        if (!ScenarioIdPattern().IsMatch(scenarioId)) Fail("Manifest scenarioId has an invalid format.");
        OracleSourceIdentity source = ParseSource(Property(manifest, "source", "artifact manifest"),
            expectedProjectRevision, expectedReferenceRevision);
        string oracleRevision = RequiredString(manifest, "oracleImplementationRevision",
            "manifest oracleImplementationRevision", 128);
        string rom = RequiredString(manifest, "romIdentity", "manifest romIdentity", 256);
        ValidateRomIdentity(rom);
        string host = RequiredString(manifest, "hostOs", "manifest hostOs", 128);
        string executable = RequiredString(manifest, "oracleExecutableIdentity",
            "manifest oracleExecutableIdentity", 256);
        string scenarioHash = Digest(Property(manifest, "scenarioHash", "artifact manifest"),
            "manifest scenarioHash");
        uint seed = UInt32(Property(manifest, "seed", "artifact manifest"), "manifest seed");
        string timestampText = RequiredString(manifest, "runTimestamp", "manifest runTimestamp", 128);
        if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp)
            || timestamp.Offset != TimeSpan.Zero)
            Fail("Manifest runTimestamp must be an ISO-8601 UTC timestamp.");
        string result = RequiredString(manifest, "result", "manifest result", 32);
        if (result is not ("normalized" or "invalid" or "failed"))
            Fail("Manifest result is invalid.");
        return new OracleArtifactManifest(schema, scenarioId, source, oracleRevision, rom,
            host, executable, scenarioHash.ToLowerInvariant(), seed, timestamp, result);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseState(JsonElement element)
    {
        JsonElement state = RequireObject(element, "$/state", "checkpoint state");
        if (state.EnumerateObject().Count() > MaximumStateFields)
            Fail("Checkpoint state exceeds its field bound.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in state.EnumerateObject())
        {
            Text(property.Name, "state field", MaximumPath);
            if (!StateRoots.Contains(property.Name))
                Fail($"Checkpoint state field is not an approved semantic field: {property.Name}");
            ValidateValues(property.Value, $"$/state/{property.Name}", 0);
            values.Add(property.Name, property.Value.Clone());
        }
        return values;
    }

    private static IReadOnlyDictionary<string, double> ParseNumberObject(JsonElement element,
        string name, int maximumCount, int maximumKeyLength)
    {
        JsonElement value = RequireObject(element, $"$/{name}", name);
        if (value.EnumerateObject().Count() > maximumCount)
            Fail($"{name} exceeds its field bound.");
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            Text(property.Name, $"{name} key", maximumKeyLength);
            if (!values.TryAdd(property.Name, Number(property.Value, $"{name} value", double.MinValue, double.MaxValue)))
                Fail($"Duplicate {name} key: {property.Name}");
        }
        return values;
    }

    private static void ValidateValues(JsonElement element, string path, int depth)
    {
        if (depth > 32) Fail("Checkpoint state nesting exceeds its bound.");
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                _ = Number(element, "state number", double.MinValue, double.MaxValue);
                return;
            case JsonValueKind.String:
                Text(element.GetString(), "state string", MaximumText);
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return;
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) Fail($"Duplicate checkpoint state property at {path}: {property.Name}");
                    Text(property.Name, "state property", MaximumPath);
                    ValidateValues(property.Value, path + "/" + Escape(property.Name), depth + 1);
                }
                return;
            case JsonValueKind.Array:
                if (element.GetArrayLength() > 4096) Fail("Checkpoint state array exceeds its bound.");
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                    ValidateValues(item, path + "/" + index++, depth + 1);
                return;
            default:
                Fail($"Unsupported state value at {path}.");
                return;
        }
    }

    private static JsonDocument ParseDocument(ReadOnlySpan<byte> utf8, string kind)
    {
        if (utf8.Length is < 2 or > MaximumDocumentBytes)
            throw new InvalidDataException($"Oracle {kind} must be between 2 and {MaximumDocumentBytes} bytes.");
        try
        {
            JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            ValidateUniqueProperties(document.RootElement, "$", 0);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Oracle {kind} JSON is malformed or unsupported.", ex);
        }
    }

    private static void ValidateUniqueProperties(JsonElement element, string path, int depth)
    {
        if (depth > 64) Fail("Oracle JSON nesting exceeds its bound.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) Fail($"Duplicate oracle property at {path}: {property.Name}");
                ValidateUniqueProperties(property.Value, path + "/" + Escape(property.Name), depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
                ValidateUniqueProperties(item, path + "/" + index++, depth + 1);
        }
    }

    private static void EnsureFields(JsonElement objectValue, string kind, params string[] expected)
    {
        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (JsonProperty property in objectValue.EnumerateObject())
            if (!allowed.Contains(property.Name)) Fail($"Unknown {kind} field: {property.Name}");
        foreach (string name in expected)
            if (!objectValue.TryGetProperty(name, out _)) Fail($"Missing {kind} field: {name}");
    }

    private static JsonElement Property(JsonElement objectValue, string name, string kind)
        => objectValue.TryGetProperty(name, out JsonElement value)
            ? value : throw new InvalidDataException($"Missing {kind} field: {name}");

    private static JsonElement RequireObject(JsonElement element, string path, string kind)
        => element.ValueKind == JsonValueKind.Object
            ? element : throw new InvalidDataException($"{kind} at {path} must be an object.");

    private static string RequiredString(JsonElement objectValue, string name, string kind, int maximum)
    {
        JsonElement value = Property(objectValue, name, kind);
        if (value.ValueKind != JsonValueKind.String) Fail($"{kind} must be a string.");
        string? text = value.GetString();
        Text(text, kind, maximum);
        return text!;
    }

    private static string? OptionalString(JsonElement objectValue, string name, string kind, int maximum)
    {
        JsonElement value = Property(objectValue, name, kind);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) Fail($"{kind} must be a string or null.");
        string? text = value.GetString();
        Text(text, kind, maximum);
        return text;
    }

    private static double Number(JsonElement element, string kind, double minimum, double maximum)
    {
        double value = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out value)
            || !Double.IsFinite(value) || value < minimum || value > maximum)
            Fail($"{kind} must be a finite number in range.");
        return value;
    }

    private static double? OptionalFiniteNumber(JsonElement objectValue, string name, string kind,
        double minimum, double maximum)
    {
        JsonElement value = Property(objectValue, name, kind);
        return value.ValueKind == JsonValueKind.Null ? null : Number(value, kind, minimum, maximum);
    }

    private static int Int32(JsonElement element, string kind, int minimum, int maximum)
    {
        int value = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value)
            || value < minimum || value > maximum)
            Fail($"{kind} must be an integer in range.");
        return value;
    }

    private static int? OptionalInt32(JsonElement objectValue, string name, string kind,
        int minimum, int maximum)
    {
        JsonElement value = Property(objectValue, name, kind);
        return value.ValueKind == JsonValueKind.Null ? null : Int32(value, kind, minimum, maximum);
    }

    private static uint UInt32(JsonElement element, string kind)
    {
        uint value = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetUInt32(out value))
            Fail($"{kind} must be an unsigned integer.");
        return value;
    }

    private static string Digest(JsonElement element, string kind)
    {
        string value = RequiredString(element, kind);
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) Fail($"{kind} must be a SHA-256 digest.");
        return value;
    }

    private static void ValidateRomIdentity(string value)
    {
        if (value.StartsWith("unverified:", StringComparison.Ordinal))
        {
            if (value.Length <= "unverified:".Length)
                Fail("Manifest romIdentity must explain an unverified source.");
            return;
        }
        if (!value.StartsWith("sha256:", StringComparison.Ordinal)
            || value.Length != "sha256:".Length + 64
            || !value["sha256:".Length..].All(Uri.IsHexDigit))
            Fail("Manifest romIdentity must be sha256:<digest> or unverified:<reason>.");
    }

    private static string RequiredString(JsonElement element, string kind)
    {
        if (element.ValueKind != JsonValueKind.String) Fail($"{kind} must be a string.");
        string? value = element.GetString();
        Text(value, kind, 256);
        return value!;
    }

    private static void Text(string? value, string kind, int maximum)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(Char.IsControl))
            Fail($"{kind} is missing, too long, or contains control characters.");
    }

    private static void ValidateRelativePath(string path, string kind)
    {
        string normalized = path.Replace('\\', '/');
        if (!StringComparer.Ordinal.Equals(path, normalized) || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
            Fail($"{kind} must be a normalized relative path.");
    }

    private static byte[] ReadRegularFile(string path, string kind)
    {
        string full = Path.GetFullPath(path);
        FileInfo info = new(full);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is < 2 or > MaximumDocumentBytes)
            throw new InvalidDataException($"Oracle {kind} must be a regular file of at most {MaximumDocumentBytes} bytes.");
        return File.ReadAllBytes(full);
    }

    private static byte[] ReadContainedFile(string rootPath, string relativePath, string kind)
    {
        string root = Path.GetFullPath(rootPath);
        DirectoryInfo rootInfo = new(root);
        if (!rootInfo.Exists || rootInfo.LinkTarget != null
            || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Oracle {kind} root must be a regular directory.");
        string normalized = relativePath.Replace('\\', '/');
        ValidateRelativePath(normalized, $"Oracle {kind} path");
        string full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Oracle {kind} path escapes its root.");
        return ReadRegularFile(full, kind);
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static JsonSerializerOptions CreateSerializationOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    [DoesNotReturn]
    private static void Fail(string message) => throw new InvalidDataException(message);
}
