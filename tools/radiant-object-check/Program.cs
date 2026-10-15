using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MphRead;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Refused(Action action, string name)
{
    bool refused = false;
    try { action(); } catch (InvalidDataException) { refused = true; }
    Check(refused, name);
}
Dictionary<string, string>[] Entities(string source)
{
    var result = new List<Dictionary<string, string>>();
    Dictionary<string, string>? current = null;
    foreach (string raw in source.Split('\n'))
    {
        string line = raw.Trim();
        if (line == "{") current = new(StringComparer.Ordinal);
        else if (line == "}") { result.Add(current!); current = null; }
        else if (line.Length != 0)
        {
            var match = Regex.Match(line, """^"([^"]+)" "([^"]*)"$""");
            if (!match.Success) throw new InvalidDataException("Unportable point entity source: " + line);
            current!.Add(match.Groups[1].Value, match.Groups[2].Value);
        }
    }
    return result.ToArray();
}
RadiantObjectRecord Parse(IReadOnlyDictionary<string, string> properties, float units = 16)
{
    if (!RadiantObjectInterchange.TryParseEntity(properties, units, out var value, out string? error))
        throw new InvalidDataException(error);
    return value!;
}
string Owned(MapDefinition map) => JsonSerializer.Serialize(new
{ map.Spawns, map.Items, map.JumpPads, map.NavigationLinks, map.Q3Triggers });

var map = new MapDefinition { MapId = Guid.NewGuid(), Name = "POINTCONTRACT", Author = "Retained author",
    KillHeight = -80, Description = "Nonowned native fields remain intact." };
float[] originalYaws = { -720.125f, .00001f, 90, 360.125f, 1e20f };
for (int team = -1; team <= 3; team++)
    map.Spawns.Add(new() { Id = Guid.NewGuid(), Team = team, Yaw = originalYaws[team + 1],
        Position = new[] { 1f + team, 2f, 3f }, Label = team switch
        { -1 => null, 0 => "", 1 => "Plain \\ label", _ => "Unicode λ and \"quotes\"\nline\ttab" } });
foreach (ItemType type in MapBuilder.MultiplayerItems)
    map.Items.Add(new() { Id = Guid.NewGuid(), Type = type.ToString(), Position = new[] { 4f, 5f, 6f },
        Label = "Pickup " + type, HasBase = (int)type % 2 == 0, SpawnInterval = (ushort)((int)type * 211) });
map.JumpPads.Add(new() { Id = Guid.NewGuid(), Position = new[] { 1f, 2f, 3f }, Target = new[] { 4f, 8f, 6f },
    Vector = null, Speed = -9.75f, Size = new[] { 1.25f, 2.5f, 3.75f }, ModelId = uint.MaxValue,
    CooldownTime = ushort.MaxValue, ControlLockTime = 0, Label = "" });
map.JumpPads.Add(new() { Id = Guid.NewGuid(), Position = new[] { 7f, 2f, 9f }, Target = null,
    Vector = new[] { 1.5f, 2.5f, -3.5f }, Speed = 42.125f, Size = new[] { 2f, 3f, 4f },
    ModelId = 13, CooldownTime = 0, ControlLockTime = ushort.MaxValue, Label = null });
foreach (MapNavigationLinkKind kind in Enum.GetValues<MapNavigationLinkKind>())
    foreach (MapNavigationAnchorKind anchor in Enum.GetValues<MapNavigationAnchorKind>())
        map.NavigationLinks.Add(new() { Id = Guid.NewGuid(), Kind = kind,
            From = new[] { 1f, 2f, 3f }, To = new[] { 7f, 8f, 9f }, Bidirectional = (int)anchor % 2 == 0,
            FromNodeKind = anchor, ToNodeKind = anchor });
map.Q3Triggers = new()
{
    new() { Kind = "teleport", SourceEntity = 7, Min = new[] { 1f, 2f, 3f }, Max = new[] { 4f, 5f, 6f },
        Destination = new[] { 7f, 8f, 9f }, Yaw = .00001f, Gravity = 123.125f, CooldownFrames = 600 },
    new() { Kind = "push", SourceEntity = 7, Min = new[] { 0f, 0f, 0f }, Max = new[] { 2f, 2f, 2f },
        Destination = new[] { 4f, 8f, 4f }, Yaw = 359.875f, Gravity = 50, CooldownFrames = 1 }
};
map.FormatVersion = 3;
string expected = Owned(map);
var source = new StringBuilder();
Guid[] triggerIds = RadiantObjectInterchange.ExportAppender(map, source, 16);
var entities = Entities(source.ToString());
var snapshot = new RadiantObjectSnapshot();
var seen = new HashSet<Guid>();
foreach (var properties in entities) snapshot.Add(Parse(properties), seen);
var parsed = new MapDefinition { Author = map.Author, Description = map.Description, KillHeight = map.KillHeight };
snapshot.ApplyTo(parsed);
Check(Owned(parsed) == expected, "every native gameplay field roundtrips exactly, including every pickup and navigation enum");
Check(snapshot.TriggerIds.SequenceEqual(triggerIds) && triggerIds[0] != triggerIds[1],
    "duplicate BSP provenance never collapses persistent trigger identities");
Check(!JsonSerializer.Serialize(parsed.Q3Triggers).Contains("\"Id\"", StringComparison.Ordinal),
    "trigger identities do not alter the runtime JSON schema");
var again = new StringBuilder();
RadiantObjectInterchange.ExportAppender(map, again, 16, triggerIds);
Check(again.ToString() == source.ToString(), "adjunct trigger identity mapping makes subsequent exports stable");
var spawn = entities.First(p => p["classname"] == "prime_spawn");
var vectorPad = entities.First(p => p["classname"] == "prime_jumppad" && p["prime_vector_is_null"] == "false");
Check(spawn["origin"] == "0 -48 32" && Parse(spawn).Spawn!.Position[1] == 2,
    "custom Prime player origin has no stock Quake 24-unit offset");
Check(vectorPad["prime_vector"] == "1.5 3.5 2.5", "launch directions rotate axes without source unit scaling");
Check(entities.First(p => p["classname"] == "prime_jumppad")["prime_size"] == "20 60 40",
    "positive jump trigger dimensions swap axes and use source unit scale");
Check(entities.Any(p => p.GetValueOrDefault("prime_label_encoding") == "base64-utf8"),
    "quotes and control characters have native tokenizer-safe explicit label encoding");
foreach (var (label, name) in new[]
{
    (new string('a', 1000), "ASCII"),
    (new string('λ', 500), "multibyte Unicode"),
    (string.Concat(Enumerable.Repeat("\U0001F600", 250)), "supplementary Unicode"),
    (new string('"', 750), "base64 label encoding")
})
{
    var boundedMap = new MapDefinition();
    boundedMap.Spawns.Add(new() { Id = Guid.NewGuid(), Label = label });
    var boundedSource = new StringBuilder();
    RadiantObjectInterchange.ExportAppender(boundedMap, boundedSource, 16);
    var properties = Entities(boundedSource.ToString()).Single();
    Check(Encoding.UTF8.GetByteCount(properties["prime_label"]) == 1000
        && Parse(properties).Spawn!.Label == label,
        name + " label at the 1000-byte native token bound roundtrips without truncation");
}
foreach (var (label, encoded, name) in new[]
{
    (new string('a', 1001), false, "ASCII"),
    (new string('λ', 501), false, "multibyte Unicode"),
    (new string('"', 751), true, "base64 label encoding")
})
{
    var oversizedMap = new MapDefinition();
    oversizedMap.Spawns.Add(new() { Id = Guid.NewGuid(), Label = "Valid preceding entity" });
    oversizedMap.Spawns.Add(new() { Id = Guid.NewGuid(), Label = label });
    var unchangedOutput = new StringBuilder("existing source");
    string? exportError = null;
    try { RadiantObjectInterchange.ExportAppender(oversizedMap, unchangedOutput, 16); }
    catch (InvalidDataException ex) { exportError = ex.Message; }
    Check(exportError?.Contains("label exceeds the native Radiant 1000-byte", StringComparison.Ordinal) == true
        && exportError.Contains("Shorten the label", StringComparison.Ordinal)
        && unchangedOutput.ToString() == "existing source" && oversizedMap.Spawns[1].Label == label,
        name + " oversized label export fails actionably before appending or truncating any source");
    var oversizedProperties = new Dictionary<string, string>
    {
        ["classname"] = "prime_spawn", ["prime_id"] = Guid.NewGuid().ToString(), ["origin"] = "0 0 0",
        ["prime_label_is_null"] = "false", ["prime_label_encoding"] = encoded ? "base64-utf8" : "plain",
        ["prime_label"] = encoded ? Convert.ToBase64String(Encoding.UTF8.GetBytes(label)) : label
    };
    Check(!RadiantObjectInterchange.TryParseEntity(oversizedProperties, 16, out _, out string? parseError)
        && parseError?.Contains("label exceeds the native Radiant 1000-byte", StringComparison.Ordinal) == true,
        name + " oversized native source token is refused during parse");
}
var oversizedKey = new Dictionary<string, string>(spawn) { [new string('k', 1001)] = "value" };
Check(!RadiantObjectInterchange.TryParseEntity(oversizedKey, 16, out _, out string? keyError)
    && keyError?.Contains("property key exceeds the native Radiant 1000-byte", StringComparison.Ordinal) == true,
    "property key byte bounds are checked before unsupported-key handling");
var oversizedValue = new Dictionary<string, string>(spawn) { ["prime_team"] = new string('1', 1001) };
Check(!RadiantObjectInterchange.TryParseEntity(oversizedValue, 16, out _, out string? valueError)
    && valueError?.Contains("Prime property prime_team exceeds the native Radiant 1000-byte", StringComparison.Ordinal) == true,
    "nonlabel property values share the native token byte bound");
foreach (var p in entities.Where(p => p.GetValueOrDefault("prime_label") == "")) p.Remove("prime_label");
var stripped = new RadiantObjectSnapshot();
var strippedIds = new HashSet<Guid>();
foreach (var p in entities) stripped.Add(Parse(p), strippedIds);
var strippedMap = new MapDefinition(); stripped.ApplyTo(strippedMap);
Check(Owned(strippedMap) == expected, "native dropping empty key values preserves null and empty label distinctions");

foreach (string kind in new[] { "prime_spawn", "prime_pickup", "prime_jumppad", "prime_navigation_link", "prime_trigger" })
{
    var fresh = new Dictionary<string, string> { ["classname"] = kind, ["prime_id"] = Guid.NewGuid().ToString(),
        ["origin"] = "800 -1120 960" };
    Check(Parse(fresh).Id != Guid.Empty, "new " + kind + " supports native creation defaults at a nonzero position");
    fresh["unrepresented"] = "must fail";
    Check(!RadiantObjectInterchange.TryParseEntity(fresh, 16, out _, out _), "unknown properties never disappear from " + kind);
}
var rotate = new Dictionary<string, string>(spawn);
rotate["angle"] = "45";
Check(Parse(rotate).Spawn!.Yaw == 135, "standard native yaw gizmo changes Prime spawn facing");
rotate["prime_yaw"] = "271";
Check(!RadiantObjectInterchange.TryParseEntity(rotate, 16, out _, out _), "conflicting Prime and native facing edits are refused");
rotate["angle"] = rotate["prime_angle_baseline"];
Check(Parse(rotate).Spawn!.Yaw == 271, "direct exact Prime facing edits remain available in the Inspector");
rotate = new(spawn); rotate.Remove("angle");
Check(Parse(rotate).Spawn!.Yaw == 90, "native omission of zero standard angle still applies a yaw change");

void InvalidProperty(string classname, string key, string value, string name)
{
    var properties = new Dictionary<string, string>(entities.First(p => p["classname"] == classname));
    properties[key] = value;
    Check(!RadiantObjectInterchange.TryParseEntity(properties, 16, out _, out string? error)
        && !string.IsNullOrWhiteSpace(error), name);
}
InvalidProperty("prime_spawn", "prime_id", "", "empty persistent IDs require a native save");
InvalidProperty("prime_spawn", "prime_team", "4", "spawn team bounds are strict");
InvalidProperty("prime_spawn", "origin", "8388608 0 0", "world coordinates at the Prime limit are refused");
InvalidProperty("prime_spawn", "prime_yaw", "NaN", "nonfinite Prime facing is refused");
InvalidProperty("prime_pickup", "prime_type", "EnergyTank", "unsupported capacity pickups are refused");
InvalidProperty("prime_pickup", "prime_type", "15", "numeric pickup enum aliases are refused");
InvalidProperty("prime_pickup", "prime_spawn_interval", "65536", "pickup interval cannot overflow native ushort");
InvalidProperty("prime_pickup", "prime_has_base", "yes", "unknown boolean representations are refused");
InvalidProperty("prime_jumppad", "prime_model_id", "4294967296", "jump model ID cannot overflow native uint");
InvalidProperty("prime_jumppad", "prime_cooldown_time", "-1", "negative native jump cooldown is refused");
InvalidProperty("prime_jumppad", "prime_control_lock_time", "65536", "jump control lock cannot overflow native ushort");
InvalidProperty("prime_jumppad", "prime_size", "1 0 1", "zero jump trigger dimensions are refused");
InvalidProperty("prime_jumppad", "prime_vector_is_null", "false", "simultaneous target and vector modes are refused");
InvalidProperty("prime_jumppad", "prime_target_is_null", "true", "missing both launch modes is refused");
InvalidProperty("prime_jumppad", "prime_speed", "Infinity", "nonfinite launch speed is refused even when inactive");
var zeroVector = new Dictionary<string, string>(vectorPad); zeroVector["prime_vector"] = "0 0 0";
Check(!RadiantObjectInterchange.TryParseEntity(zeroVector, 16, out _, out _), "zero active launch direction is refused");
InvalidProperty("prime_navigation_link", "prime_kind", "Unknown", "unknown navigation kinds are refused");
InvalidProperty("prime_navigation_link", "prime_from_node_kind", "1", "numeric navigation node kind aliases are refused");
InvalidProperty("prime_navigation_link", "prime_to_node_kind", "Unknown", "unknown destination node kinds are refused");
InvalidProperty("prime_trigger", "prime_source_entity", "32768", "trigger source provenance bounds are strict");
InvalidProperty("prime_trigger", "prime_gravity", "0", "zero trigger gravity is refused");
InvalidProperty("prime_trigger", "prime_cooldown_frames", "601", "trigger cooldown bounds are strict");
InvalidProperty("prime_trigger", "prime_bounds_max", "0 0 0", "inverted trigger bounds are refused");
InvalidProperty("prime_trigger", "prime_kind", "kill", "unsupported trigger kinds are refused");
InvalidProperty("prime_spawn", "prime_label_encoding", "unknown", "unknown label encodings are refused");
var invalidLabel = new Dictionary<string, string>(spawn) { ["prime_label_encoding"] = "base64-utf8", ["prime_label"] = "/w==" };
Check(!RadiantObjectInterchange.TryParseEntity(invalidLabel, 16, out _, out _), "invalid UTF-8 labels are refused");
var unsupported = new Dictionary<string, string>(spawn) { ["classname"] = "prime_future_object" };
Check(!RadiantObjectInterchange.TryParseEntity(unsupported, 16, out _, out _), "unsupported Prime classes are never discarded");
Check(!RadiantObjectInterchange.TryParseEntity(spawn, float.NaN, out _, out _), "untrusted nonfinite unit scales are refused");
Refused(() => new RadiantObjectSnapshot().Add(Parse(spawn), new HashSet<Guid> { Guid.Parse(spawn["prime_id"]) }),
    "geometry and gameplay cannot share a persistent identity");
var duplicateMap = MapProjectSerializer.Clone(map); duplicateMap.Items[0].Id = duplicateMap.Spawns[0].Id;
var untouched = new StringBuilder("prefix");
Refused(() => RadiantObjectInterchange.ExportAppender(duplicateMap, untouched, 16, triggerIds),
    "exports reject cross-type duplicate identities");
Check(untouched.ToString() == "prefix", "failed exports append no partial snapshot");
Refused(() => RadiantObjectInterchange.ExportAppender(map, new(), 16, new[] { triggerIds[0], triggerIds[0] }),
    "adjunct trigger identity collisions are refused");
Refused(() => RadiantObjectInterchange.ExportAppender(map, new(), 16, Array.Empty<Guid>()),
    "adjunct trigger identity count must match the full list");
var materialCollision = MapProjectSerializer.Clone(map);
materialCollision.Materials.Add(new() { Id = materialCollision.Spawns[0].Id });
Refused(() => RadiantObjectInterchange.ExportAppender(materialCollision, new(), 16, triggerIds),
    "gameplay IDs cannot reuse native material IDs");
var empty = new RadiantObjectSnapshot(); empty.ApplyTo(parsed);
Check(parsed.Spawns.Count + parsed.Items.Count + parsed.JumpPads.Count + parsed.NavigationLinks.Count
    + parsed.Q3Triggers!.Count == 0, "missing prior IDs delete the last object in every owned list");
Check(parsed.Author == map.Author && parsed.Description == map.Description && parsed.KillHeight == map.KillHeight,
    "applying gameplay snapshot preserves nonowned native fields");
var noTriggers = new MapDefinition(); empty.ApplyTo(noTriggers);
Check(noTriggers.Q3Triggers == null, "absent optional runtime trigger list stays absent");

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var definitions = XDocument.Load(Path.Combine(root, "tools/netradiant-gamepack/entities.ent"));
var declared = definitions.Root!.Elements("point").Where(e => RadiantObjectInterchange.IsPrimeClass((string?)e.Attribute("name"))).ToArray();
Check(declared.Length == 5 && declared.All(e => e.Elements().Any(p => (string?)p.Attribute("key") == "prime_id")),
    "every Prime class has a typed native Inspector definition and durable identity field");
var declaredPickups = definitions.Root.Elements("list").Single(e => (string?)e.Attribute("name") == "prime_pickup_type")
    .Elements("item").Select(e => (string)e.Attribute("value")!).ToHashSet();
Check(declaredPickups.SetEquals(MapBuilder.MultiplayerItems.Select(t => t.ToString())),
    "native pickup choices cover the complete runtime multiplayer set");
Console.WriteLine($"{checks} Prime gameplay contract checks passed.");
