using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

/// <summary>Frozen positional contracts for released checkpoint codecs. These
/// descriptions are trusted embedded data, never schemas supplied by a replay.
/// New fields keep their construction defaults; retired fields are consumed in
/// their original width. Object IDs always use the recording's own type table.</summary>
internal sealed partial class ReplayWorldLayout
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Lazy<ReplayWorldLayout[]> Layouts = new(Load);
    private readonly Catalog _catalog;
    private readonly Description _description;
    private readonly string _legacySchema;
    private readonly Dictionary<string, Type?> _types;
    internal Type[] ObjectTypes { get; }
    internal string Contract => _description.Contract;

    private ReplayWorldLayout(Catalog catalog, Description description)
    {
        _catalog = catalog; _description = description;
        _types = description.Types.ToDictionary(n => n, ResolveType, StringComparer.Ordinal);
        ObjectTypes = description.ObjectTypes.Select(n => _types[n]
            ?? throw new InvalidDataException($"Retired replay object type {n} needs a migration.")).ToArray();
        string Name(string n) => catalog.Types[n];
        string stableSchema = "world-codec-1|" + string.Join('|', description.Objects.OrderBy(p => Name(p.Key), StringComparer.Ordinal)
            .Select(p => p.Key + ":" + string.Join(',', Fields(p.Value).Select(f => f[0] + "." + f[1] + ":" + f[2]))))
            + string.Join('|', description.Values.OrderBy(p => Name(p.Key), StringComparer.Ordinal)
                .Select(p => p.Key + ":" + string.Join(',', Fields(p.Value).Select(f => f[1] + ":" + f[2]))));
        if (Hash(stableSchema) != description.Contract)
            throw new InvalidDataException("Archived replay layout fingerprint differs from its fields.");
        _legacySchema = "world-codec-1|" + string.Join('|', description.Objects.OrderBy(p => Name(p.Key), StringComparer.Ordinal)
            .Select(p => Name(p.Key) + ":" + string.Join(',', Fields(p.Value).Select(f => Name(f[0]) + "." + f[1] + ":" + f[2]))))
            + string.Join('|', description.Values.OrderBy(p => Name(p.Key), StringComparer.Ordinal)
                .Select(p => Name(p.Key) + ":" + string.Join(',', Fields(p.Value).Select(f => f[1] + ":" + f[2]))));
    }
    private static Type? ResolveType(string name) => ReplayWorldCheckpoint.ValueType(name) ?? Type.GetType(name, assemblyName =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName.Name),
        (assembly, typeName, ignoreCase) => assembly?.GetType(typeName, false, ignoreCase)
            ?? typeof(Scene).Assembly.GetType(typeName, false, ignoreCase)
            ?? typeof(OpenTK.Mathematics.Vector3).Assembly.GetType(typeName, false, ignoreCase)
            ?? typeof(LinkedList<>).Assembly.GetType(typeName, false, ignoreCase)
            ?? typeof(object).Assembly.GetType(typeName, false, ignoreCase), false);
    private static ReplayWorldLayout[] Load()
    {
        using var stream = typeof(Scene).Assembly.GetManifestResourceStream("MphRead.Mods.Replay.ReplayWorldLayouts.json")
            ?? throw new InvalidDataException("Historical replay layouts are missing from this build.");
        var catalog = JsonSerializer.Deserialize(stream, LayoutJsonContext.Default.Catalog)!;
        return catalog.Layouts.Select(d => new ReplayWorldLayout(catalog, d)).ToArray();
    }
    internal static ReplayWorldLayout? Find(string contract, string? producerBuild)
    {
        if (contract.Length != 64) return null;
        foreach (var layout in Layouts.Value)
        {
            if (contract == layout.Contract || contract == Hash(layout._legacySchema)) return layout;
            string schema = layout._legacySchema;
            if (producerBuild != null)
            {
                string text = producerBuild.Split('+')[0].TrimStart('v');
                if (Version.TryParse(text, out var version))
                {
                    var normalized = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
                    schema = schema.Replace("ProjectPrime, Version=1.0.0.0", "ProjectPrime, Version=" + normalized, StringComparison.Ordinal);
                }
                if (contract == Hash(schema)) return layout;
                if (layout._description.BuildBound && contract == Hash(producerBuild + schema["world-codec-1".Length..])) return layout;
            }
        }
        return null;
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal void ReadFields(BinaryReader reader, object target, Func<int, Type, object?> reference, ReplayReplicaState state)
    {
        foreach (var field in Fields(_description.Objects[target.GetType().ToString()]))
        {
            object? value = ReadValue(reader, field[2], reference);
            var current = Field(field);
            if (current != null) current.SetValue(target, value);
            else if (field[2] == "System.Byte" && field[1] is "_modPendingHomingTarget" or "_latchedHomingTarget")
            {
                // Historical target bytes used the same encoded slot but had no
                // life fence. Bind to the clip's detached lifecycle, never live state.
                byte encoded = (byte)value!; int slot = (encoded & 0x7f) - 1;
                var identity = slot >= 0 && slot < 8 && state.TryGetPlayer(slot, out var player)
                    ? new NetTargetIdentity(encoded, player.SlotGeneration, player.LifeId) : NetTargetIdentity.None;
                _types[field[0]]!.GetField(field[1], Instance | BindingFlags.DeclaredOnly)!.SetValue(target, identity);
            }
        }
    }
    private IEnumerable<string[]> Fields(int index) => _catalog.FieldSets[index].Select(i => _catalog.Fields[i]);
    private FieldInfo? Field(string[] field)
    {
        var type = _types.GetValueOrDefault(field[0]) ?? ResolveType(field[0]);
        string name = field[0] == "MphRead.Mods.Network.ShotKey" && field[1] == "<LaunchFrame>k__BackingField"
            ? "<ShotId>k__BackingField" : field[1];
        var current = type?.GetField(name, Instance | BindingFlags.DeclaredOnly);
        return current?.FieldType.ToString() == field[2] ? current : null;
    }
    internal object? ReadValue(BinaryReader reader, string name, Func<int, Type, object?> reference)
    {
        if (!_types.TryGetValue(name, out var type)) throw new InvalidDataException("Unknown historical replay value type.");
        if (name == "System.Object")
        {
            string actual = reader.ReadString();
            if (actual == name) throw new InvalidDataException("Invalid replay message parameter.");
            return ReadValue(reader, actual, reference);
        }
        if (type != null && Nullable.GetUnderlyingType(type) is Type underlying)
            return reader.ReadBoolean() ? ReadValue(reader, underlying.ToString(), reference) : null;
        if (_description.Values.TryGetValue(name, out var fields))
        {
            object? value = type == null ? null : Activator.CreateInstance(type);
            foreach (var field in Fields(fields))
            {
                object? child = ReadValue(reader, field[2], reference);
                if (value != null) Field(field)?.SetValue(value, child);
            }
            return value;
        }
        if (type == null) throw new InvalidDataException($"Unknown historical replay reference type {name}.");
        return ReplayWorldCheckpoint.ReadValue(reader, type, reference);
    }
    internal bool IsRetiredAnchor(ulong anchor, Type type)
    {
        // Only arrays belonging to removed/replaced replication fields may lose
        // their construction anchor. Everything else retains strict binding.
        foreach (var field in Fields(_description.Objects["MphRead.Mods.Network.PlayerReplicationBridge"]))
        {
            if (Field(field) != null || field[2] != type.ToString()) continue;
            string path = "root3/PlayerReplicationBridge." + field[1];
            ulong hash = 14695981039346656037;
            foreach (char c in path) { hash ^= c; hash *= 1099511628211; }
            if (anchor == hash) return true;
        }
        return false;
    }
    private sealed class Catalog
    {
        public Dictionary<string, string> Types { get; set; } = [];
        public string[][] Fields { get; set; } = [];
        public int[][] FieldSets { get; set; } = [];
        public Description[] Layouts { get; set; } = [];
    }
    private sealed class Description
    {
        public string Contract { get; set; } = "";
        public bool BuildBound { get; set; }
        public Dictionary<string, int> Objects { get; set; } = [];
        public Dictionary<string, int> Values { get; set; } = [];
        public string[] ObjectTypes { get; set; } = [];
        public string[] Types { get; set; } = [];
    }
    [JsonSerializable(typeof(Catalog))]
    private partial class LayoutJsonContext : JsonSerializerContext { }
}
