using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MphRead.Mods.MapGen;

/// <summary>A single, typed Prime point entity. Trigger identity belongs to the
/// authoring adjunct: MapQ3Trigger.SourceEntity remains BSP provenance only.</summary>
public sealed record RadiantObjectRecord(Guid Id, MapSpawn? Spawn = null,
    MapItem? Item = null, MapJumpPad? JumpPad = null,
    MapNavigationLink? NavigationLink = null, MapQ3Trigger? Trigger = null);

/// <summary>The complete set of gameplay objects owned by an editable source.
/// Replacing the lists also applies source deletions, including the last object.</summary>
public sealed class RadiantObjectSnapshot
{
    private readonly List<MapSpawn> _spawns = new();
    private readonly List<MapItem> _items = new();
    private readonly List<MapJumpPad> _jumpPads = new();
    private readonly List<MapNavigationLink> _navigationLinks = new();
    private readonly List<MapQ3Trigger> _triggers = new();
    private readonly List<Guid> _triggerIds = new();
    public IReadOnlyList<MapSpawn> Spawns => _spawns;
    public IReadOnlyList<MapItem> Items => _items;
    public IReadOnlyList<MapJumpPad> JumpPads => _jumpPads;
    public IReadOnlyList<MapNavigationLink> NavigationLinks => _navigationLinks;
    public IReadOnlyList<MapQ3Trigger> Triggers => _triggers;
    public IReadOnlyList<Guid> TriggerIds => _triggerIds;

    public void Add(RadiantObjectRecord value, ISet<Guid> globalSeenIds)
    {
        if (value == null || value.Id == Guid.Empty
            || new object?[] { value.Spawn, value.Item, value.JumpPad,
                value.NavigationLink, value.Trigger }.Count(x => x != null) != 1)
            throw new InvalidDataException("Invalid typed Prime gameplay entity.");
        if ((value.Spawn != null && value.Spawn.Id != value.Id)
            || (value.Item != null && value.Item.Id != value.Id)
            || (value.JumpPad != null && value.JumpPad.Id != value.Id)
            || (value.NavigationLink != null && value.NavigationLink.Id != value.Id))
            throw new InvalidDataException("Prime entity payload and identity differ.");
        if (_spawns.Count + _items.Count + _jumpPads.Count >= 32767
            && (value.Spawn != null || value.Item != null || value.JumpPad != null))
            throw new InvalidDataException("Prime gameplay entity limit is 32767.");
        if (value.NavigationLink != null && _navigationLinks.Count >= 4096)
            throw new InvalidDataException("Prime navigation link limit is 4096.");
        if (value.Trigger != null && _triggers.Count >= 32768)
            throw new InvalidDataException("Prime traversal trigger limit is 32768.");
        if (!globalSeenIds.Add(value.Id))
            throw new InvalidDataException("Duplicate Prime object identity across geometry or gameplay entities: " + value.Id);
        if (value.Spawn != null) _spawns.Add(value.Spawn);
        if (value.Item != null) _items.Add(value.Item);
        if (value.JumpPad != null) _jumpPads.Add(value.JumpPad);
        if (value.NavigationLink != null) _navigationLinks.Add(value.NavigationLink);
        if (value.Trigger != null) { _triggers.Add(value.Trigger); _triggerIds.Add(value.Id); }
    }

    public void ApplyTo(MapDefinition definition)
    {
        definition.Spawns = _spawns.ToList();
        definition.Items = _items.ToList();
        definition.JumpPads = _jumpPads.ToList();
        definition.NavigationLinks = _navigationLinks.ToList();
        // Preserve an absent optional trigger list when source never owns one.
        definition.Q3Triggers = _triggers.Count == 0 && definition.Q3Triggers == null
            ? null : _triggers.ToList();
        if (definition.Q3Triggers != null) definition.FormatVersion = Math.Max(3, definition.FormatVersion);
    }
}

/// <summary>Lossless native gameplay field contract for the Prime Radiant
/// inspector. Positions use Quake (x,-z,y) axes and source scale. Directions use
/// the same axis rotation without scale. No custom Prime spawn has a Quake
/// player-height offset. Unknown classes/keys and malformed values fail closed.</summary>
public static class RadiantObjectInterchange
{
    // The native tokenizer has a fixed 1024-byte token buffer. Leave room for
    // its terminator and parser bookkeeping, including multibyte UTF-8 text.
    private const int MaximumNativeTokenBytes = 1000;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] SharedKeys = { "classname", "prime_id", "origin" };
    private static readonly string[] LabelKeys = { "prime_label", "prime_label_is_null", "prime_label_encoding" };
    public static bool IsPrimeClass(string? name) => name is "prime_spawn" or "prime_pickup"
        or "prime_jumppad" or "prime_navigation_link" or "prime_trigger";

    /// <summary>Append every owned gameplay list. The returned GUIDs must be
    /// persisted in the adjunct in Q3Triggers order, and supplied on later exports.
    /// They must never be derived from SourceEntity, which may repeat or change.</summary>
    public static Guid[] ExportAppender(MapDefinition map, StringBuilder output, float units,
        IReadOnlyList<Guid>? triggerIds = null)
    {
        RequireUnits(units);
        if (map.Spawns == null || map.Items == null || map.JumpPads == null || map.NavigationLinks == null)
            throw new InvalidDataException("Prime gameplay lists cannot be null.");
        if (map.Spawns.Any(x => x == null) || map.Items.Any(x => x == null)
            || map.JumpPads.Any(x => x == null) || map.NavigationLinks.Any(x => x == null)
            || map.Q3Triggers?.Any(x => x == null) == true)
            throw new InvalidDataException("Prime gameplay lists cannot contain null objects.");
        int triggerCount = map.Q3Triggers?.Count ?? 0;
        if (triggerIds != null && triggerIds.Count != triggerCount)
            throw new InvalidDataException("Traversal adjunct identities must match the complete trigger list.");
        Guid[] assigned = triggerIds?.ToArray()
            ?? Enumerable.Range(0, triggerCount).Select(_ => Guid.NewGuid()).ToArray();
        if (map.Spawns.Count + map.Items.Count + map.JumpPads.Count > 32767
            || map.NavigationLinks.Count > 4096 || triggerCount > 32768)
            throw new InvalidDataException("Prime gameplay object count exceeds authoring bounds.");
        var seen = new HashSet<Guid>();
        foreach (Guid id in map.Geometry.Select(x => x.Id).Concat(map.Brushes.Select(x => x.Id)))
            if (id == Guid.Empty || !seen.Add(id))
                throw new InvalidDataException("Geometry identities must be nonempty and unique.");
        foreach (Guid id in map.Materials.Select(x => x.Id).Where(id => id != Guid.Empty))
            if (!seen.Add(id)) throw new InvalidDataException("Material identity conflicts with another Prime object.");
        // Buffer until the entire snapshot validates so failure never appends
        // a deceptively usable prefix to the caller's source text.
        var buffer = new StringBuilder();
        foreach (MapSpawn value in map.Spawns)
        {
            var p = Entity("prime_spawn", value.Id, value.Position, units);
            Label(p, value.Label); p.Add("prime_team", N(value.Team)); Yaw(p, value.Yaw);
            Write(p);
        }
        foreach (MapItem value in map.Items)
        {
            var p = Entity("prime_pickup", value.Id, value.Position, units);
            Label(p, value.Label); p.Add("prime_type", value.Type);
            p.Add("prime_has_base", B(value.HasBase)); p.Add("prime_spawn_interval", N(value.SpawnInterval));
            Write(p);
        }
        foreach (MapJumpPad value in map.JumpPads)
        {
            var p = Entity("prime_jumppad", value.Id, value.Position, units);
            Label(p, value.Label);
            p.Add("prime_target_is_null", B(value.Target == null));
            p.Add("prime_target", Position(value.Target ?? new float[3], units));
            p.Add("prime_vector_is_null", B(value.Vector == null));
            p.Add("prime_vector", Direction(value.Vector ?? new float[3]));
            p.Add("prime_speed", N(value.Speed)); p.Add("prime_size", Size(value.Size, units));
            p.Add("prime_model_id", N(value.ModelId));
            p.Add("prime_cooldown_time", N(value.CooldownTime));
            p.Add("prime_control_lock_time", N(value.ControlLockTime)); Write(p);
        }
        foreach (MapNavigationLink value in map.NavigationLinks)
        {
            var p = Entity("prime_navigation_link", value.Id, value.From, units);
            p.Add("prime_to", Position(value.To, units)); p.Add("prime_kind", value.Kind.ToString());
            p.Add("prime_bidirectional", B(value.Bidirectional));
            p.Add("prime_from_node_kind", value.FromNodeKind.ToString());
            p.Add("prime_to_node_kind", value.ToNodeKind.ToString()); Write(p);
        }
        for (int index = 0; index < triggerCount; index++)
        {
            MapQ3Trigger value = map.Q3Triggers![index];
            var p = Entity("prime_trigger", assigned[index], value.Min, units);
            p.Add("prime_bounds_max", Position(value.Max, units));
            p.Add("prime_destination", Position(value.Destination, units)); p.Add("prime_kind", value.Kind);
            p.Add("prime_source_entity", N(value.SourceEntity)); Yaw(p, value.Yaw);
            p.Add("prime_gravity", N(value.Gravity)); p.Add("prime_cooldown_frames", N(value.CooldownFrames)); Write(p);
        }
        output.Append(buffer);
        return assigned;

        void Write(Dictionary<string, string> properties)
        {
            if (!TryParseEntity(properties, units, out RadiantObjectRecord? record, out string? error))
                throw new InvalidDataException(error);
            if (!seen.Add(record!.Id))
                throw new InvalidDataException("Duplicate Prime identity across gameplay or geometry: " + record.Id);
            buffer.AppendLine("{");
            foreach (var (key, value) in properties)
                buffer.Append('"').Append(key).Append("\" \"").Append(Escape(value)).AppendLine("\"");
            buffer.AppendLine("}");
        }
    }

    /// <summary>Parse one already tokenized entity; does not scan .map lines or
    /// invent missing IDs. Native creation/clone/save assigns prime_id first.</summary>
    public static bool TryParseEntity(IReadOnlyDictionary<string, string> entity, float units,
        out RadiantObjectRecord? record, out string? error)
    {
        record = null; error = null;
        try
        {
            RequireUnits(units);
            foreach (var (key, value) in entity) RequireNativeTokens(key, value);
            if (!entity.TryGetValue("classname", out string? kind) || !IsPrimeClass(kind))
                throw Invalid("Unsupported Prime gameplay classname.");
            string[] keys = kind switch
            {
                "prime_spawn" => LabelKeys.Concat(new[] { "prime_team", "prime_yaw", "angle", "prime_angle_baseline" }).ToArray(),
                "prime_pickup" => LabelKeys.Concat(new[] { "prime_type", "prime_has_base", "prime_spawn_interval" }).ToArray(),
                "prime_jumppad" => LabelKeys.Concat(new[] { "prime_target", "prime_target_is_null", "prime_vector",
                    "prime_vector_is_null", "prime_speed", "prime_size", "prime_model_id", "prime_cooldown_time", "prime_control_lock_time" }).ToArray(),
                "prime_navigation_link" => new[] { "prime_to", "prime_kind", "prime_bidirectional", "prime_from_node_kind", "prime_to_node_kind" },
                _ => new[] { "prime_bounds_max", "prime_destination", "prime_kind", "prime_source_entity", "prime_yaw", "angle", "prime_angle_baseline", "prime_gravity", "prime_cooldown_frames" }
            };
            var allowed = SharedKeys.Concat(keys).ToHashSet(StringComparer.Ordinal);
            foreach (string key in entity.Keys)
                if (!allowed.Contains(key)) throw Invalid("Unsupported " + kind + " property: " + key);
            if (!Guid.TryParse(Get("prime_id"), out Guid id) || id == Guid.Empty)
                throw Invalid("Prime gameplay prime_id must be a nonempty GUID. Save with Project Prime Radiant to assign new and cloned IDs.");
            float[] origin = World(Get("origin"), units, "origin");
            switch (kind)
            {
                case "prime_spawn":
                    int team = Int("prime_team", -1, -1, 3);
                    record = new(id, Spawn: new MapSpawn { Id = id, Position = origin,
                        Label = ReadLabel(), Team = team, Yaw = ReadYaw() });
                    break;
                case "prime_pickup":
                    string type = Get("prime_type", "MissileSmall");
                    if (!Enum.TryParse(type, true, out ItemType itemType) || !Enum.IsDefined(itemType)
                        || !MapBuilder.MultiplayerItems.Contains(itemType)
                        || !Enum.GetNames<ItemType>().Contains(type, StringComparer.OrdinalIgnoreCase))
                        throw Invalid("prime_type must name a supported multiplayer pickup.");
                    record = new(id, Item: new MapItem { Id = id, Position = origin, Label = ReadLabel(),
                        Type = type, HasBase = Bool("prime_has_base", true),
                        SpawnInterval = (ushort)Int("prime_spawn_interval", 300, 0, ushort.MaxValue) });
                    break;
                case "prime_jumppad":
                    bool targetNull = Bool("prime_target_is_null", false);
                    bool vectorNull = Bool("prime_vector_is_null", true);
                    float[] target = World(Get("prime_target", Position(new[] { origin[0], origin[1] + 4, origin[2] }, units)), units, "prime_target");
                    float[] vector = World(Get("prime_vector", "0 0 1"), 1, "prime_vector");
                    float speed = Float("prime_speed", 1);
                    float[] sizeQuake = Vec(Get("prime_size", Size(new[] { 1.6f, 1.8f, 1.6f }, units)), "prime_size");
                    float[] size = new[] { sizeQuake[0] / units, sizeQuake[2] / units, sizeQuake[1] / units };
                    if (size.Any(v => !float.IsFinite(v) || v <= 0 || v >= 524288))
                        throw Invalid("Jump pad size must have three positive bounded dimensions.");
                    if (targetNull == vectorNull || (!vectorNull && (vector.All(v => v == 0) || speed <= 0)))
                        throw Invalid("Jump pad needs exactly one non-null target or nonzero direction plus positive speed.");
                    record = new(id, JumpPad: new MapJumpPad { Id = id, Position = origin, Label = ReadLabel(),
                        Target = targetNull ? null : target, Vector = vectorNull ? null : vector, Speed = speed, Size = size,
                        ModelId = UInt("prime_model_id", 0), CooldownTime = (ushort)Int("prime_cooldown_time", 20, 0, ushort.MaxValue),
                        ControlLockTime = (ushort)Int("prime_control_lock_time", 30, 0, ushort.MaxValue) });
                    break;
                case "prime_navigation_link":
                    record = new(id, NavigationLink: new MapNavigationLink { Id = id, From = origin,
                        To = World(Get("prime_to", Position(new[] { origin[0] + 2, origin[1], origin[2] }, units)), units, "prime_to"),
                        Kind = EnumValue("prime_kind", MapNavigationLinkKind.Manual),
                        Bidirectional = Bool("prime_bidirectional", false),
                        FromNodeKind = EnumValue("prime_from_node_kind", MapNavigationAnchorKind.Auto),
                        ToNodeKind = EnumValue("prime_to_node_kind", MapNavigationAnchorKind.Auto) });
                    break;
                case "prime_trigger":
                    var trigger = new MapQ3Trigger { Min = origin,
                        Max = World(Get("prime_bounds_max", Position(origin.Select(v => v + 2).ToArray(), units)), units, "prime_bounds_max"),
                        Destination = World(Get("prime_destination", Position(new[] { origin[0], origin[1] + 4, origin[2] }, units)), units, "prime_destination"),
                        Kind = Get("prime_kind", "push"), SourceEntity = Int("prime_source_entity", 0, 0, 32767),
                        Yaw = ReadYaw(), Gravity = Float("prime_gravity", 50),
                        CooldownFrames = Int("prime_cooldown_frames", 30, 1, 600) };
                    if (!Q3Traversal.Valid(trigger))
                        throw Invalid("Trigger needs push/teleport kind, ordered Prime bounds, yaw 0–360 (exclusive), positive gravity at most 10000, and cooldown 1–600.");
                    if (trigger.Kind == "push" && !Q3Traversal.TryLaunch(trigger, out _))
                        throw Invalid("Push trigger destination must define a finite reachable launch apex.");
                    record = new(id, Trigger: trigger);
                    break;
            }
            return true;

            string Get(string key, string? fallback = null)
                => entity.TryGetValue(key, out string? value) ? value
                    : fallback ?? throw Invalid("Missing Prime property: " + key);
            string? ReadLabel()
            {
                string label = Get("prime_label", "");
                string encoding = Get("prime_label_encoding", "plain");
                if (encoding == "base64-utf8")
                {
                    try { label = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(label)); }
                    catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
                    { throw Invalid("prime_label must contain valid base64 UTF-8 text for its selected encoding."); }
                }
                else if (encoding != "plain") throw Invalid("prime_label_encoding must be plain or base64-utf8.");
                bool isNull = Bool("prime_label_is_null", !entity.ContainsKey("prime_label"));
                if (isNull && label.Length != 0) throw Invalid("A null Prime label must have an empty prime_label value.");
                return isNull ? null : label;
            }
            bool Bool(string key, bool fallback)
            {
                string text = Get(key, B(fallback));
                if (text is "1" or "true") return true;
                if (text is "0" or "false") return false;
                throw Invalid(key + " must be true/false or 1/0.");
            }
            int Int(string key, int fallback, int min, int max)
            {
                if (!int.TryParse(Get(key, N(fallback)), NumberStyles.AllowLeadingSign, Invariant, out int value)
                    || value < min || value > max) throw Invalid(key + " is outside its integer bounds.");
                return value;
            }
            uint UInt(string key, uint fallback)
            {
                if (!uint.TryParse(Get(key, N(fallback)), NumberStyles.None, Invariant, out uint value))
                    throw Invalid(key + " must be an unsigned 32-bit integer.");
                return value;
            }
            float Float(string key, float fallback)
            {
                if (!float.TryParse(Get(key, N(fallback)), NumberStyles.Float, Invariant, out float value)
                    || !float.IsFinite(value)) throw Invalid(key + " must be finite.");
                return value;
            }
            float ReadYaw()
            {
                float yaw = Float("prime_yaw", 0);
                // Native Radiant rotates its standard angle and may remove a
                // zero angle. An explicit export baseline distinguishes this
                // from direct edits to the exact native Prime yaw property.
                float baseline = Float("prime_angle_baseline", yaw - 90f);
                float angle = Float("angle", entity.ContainsKey("prime_angle_baseline") ? 0 : yaw - 90f);
                bool angleChanged = !EquivalentYaw(angle, baseline);
                bool primeChanged = !EquivalentYaw(yaw - 90f, baseline);
                if (angleChanged && primeChanged && !EquivalentYaw(angle, yaw - 90f))
                    throw Invalid("Facing changed in both angle and prime_yaw with conflicting results. Keep one edit or make both headings agree.");
                return angleChanged && !primeChanged ? NormalizeYaw(90f + angle) : yaw;
            }
            T EnumValue<T>(string key, T fallback) where T : struct, Enum
            {
                string text = Get(key, fallback.ToString());
                if (!Enum.TryParse(text, true, out T value) || !Enum.IsDefined(value)
                    || !Enum.GetNames<T>().Contains(text, StringComparer.OrdinalIgnoreCase))
                    throw Invalid(key + " must name a supported " + typeof(T).Name + ".");
                return value;
            }
        }
        catch (InvalidDataException ex) { error = ex.Message; return false; }
    }

    private static Dictionary<string, string> Entity(string kind, Guid id, float[] position, float units)
        => new(StringComparer.Ordinal) { ["classname"] = kind, ["prime_id"] = id.ToString(), ["origin"] = Position(position, units) };
    private static void Label(Dictionary<string, string> properties, string? value)
    {
        bool encoded = value?.Any(c => c == '"' || char.IsControl(c)) == true;
        properties.Add("prime_label", encoded ? Convert.ToBase64String(Encoding.UTF8.GetBytes(value!)) : value ?? "");
        properties.Add("prime_label_is_null", B(value == null));
        properties.Add("prime_label_encoding", encoded ? "base64-utf8" : "plain");
    }
    private static void Yaw(Dictionary<string, string> properties, float value)
    {
        properties.Add("prime_yaw", N(value));
        properties.Add("angle", N(value - 90f));
        properties.Add("prime_angle_baseline", N(value - 90f));
    }
    private static float NormalizeYaw(float value) => (value % 360f + 360f) % 360f;
    private static bool EquivalentYaw(float a, float b)
    {
        float difference = MathF.Abs(NormalizeYaw(a) - NormalizeYaw(b));
        return difference < 0.0001f || difference > 359.9999f;
    }
    private static string B(bool value) => value ? "true" : "false";
    private static string N<T>(T value) where T : IFormattable => value.ToString(null, Invariant);
    private static string N(float value) => value.ToString("R", Invariant);
    private static string Position(float[] value, float units)
    { RequireVector(value); return Join(value[0] * units, -value[2] * units, value[1] * units); }
    private static string Direction(float[] value)
    { RequireVector(value); return Join(value[0], -value[2], value[1]); }
    private static string Size(float[] value, float units)
    { RequireVector(value); return Join(value[0] * units, value[2] * units, value[1] * units); }
    private static string Join(float x, float y, float z) => N(x) + " " + N(y) + " " + N(z);
    private static float[] World(string text, float units, string key)
    {
        float[] q = Vec(text, key);
        float[] result = { q[0] / units, q[2] / units, -q[1] / units };
        if (result.Any(v => !float.IsFinite(v) || Math.Abs(v) >= 524288))
            throw Invalid(key + " exceeds Prime's finite coordinate bounds.");
        return result;
    }
    private static float[] Vec(string text, string key)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw Invalid(key + " must contain exactly three numbers.");
        var result = new float[3];
        for (int i = 0; i < 3; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, Invariant, out result[i]) || !float.IsFinite(result[i]))
                throw Invalid(key + " must contain finite numbers.");
        return result;
    }
    private static void RequireVector(float[]? value)
    {
        if (value?.Length != 3 || value.Any(v => !float.IsFinite(v)))
            throw Invalid("Prime vectors must contain exactly three finite values.");
    }
    private static void RequireUnits(float units)
    {
        if (!float.IsFinite(units) || units is < 1 or > 1024)
            throw Invalid("Prime source scale must be finite and between 1 and 1024.");
    }
    private static void RequireNativeTokens(string key, string? value)
    {
        if (Encoding.UTF8.GetByteCount(key) > MaximumNativeTokenBytes)
            throw Invalid("Prime property key exceeds the native Radiant 1000-byte UTF-8 token limit.");
        if (value == null) throw Invalid("Null Prime property: " + key);
        if (Encoding.UTF8.GetByteCount(value) > MaximumNativeTokenBytes)
            throw Invalid(key == "prime_label"
                ? "Prime label exceeds the native Radiant 1000-byte token limit after UTF-8 or label encoding. Shorten the label before saving; labels are never truncated."
                : "Prime property " + key + " exceeds the native Radiant 1000-byte UTF-8 token limit.");
    }
    private static string Escape(string value)
    {
        // The native Quake tokenizer treats backslashes literally and cannot
        // escape quotes. Exceptional labels use an explicit portable encoding.
        if (value.Any(c => c == '"' || char.IsControl(c)))
            throw Invalid("Prime source strings cannot contain raw quotes or control characters.");
        return value;
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
