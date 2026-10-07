using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum MapDiffCategory { Object, Material, Spawn, Pickup, JumpPad, Capability, Asset, Navigation, Environment, Prefab }
public enum MapDiffChangeKind { Added, Removed, Modified }
public sealed record MapStructuralChange(MapDiffCategory Category, MapDiffChangeKind Kind, string Key,
    string Label, string Field, string? Before, string? After);
public sealed record MapStructuralDiffResult(IReadOnlyList<MapStructuralChange> Changes)
{
    public bool IsEmpty => Changes.Count == 0;
    public IReadOnlyList<MapStructuralChange> ForCategory(MapDiffCategory category) => Changes.Where(c => c.Category == category).ToArray();
}

/// <summary>Stable-identity semantic comparison of the same canonical map DTO graph used by the compiler.</summary>
public static class MapStructuralDiff
{
    public static MapStructuralDiffResult Compare(MapProject before, MapProject after) => Compare(before.Definition, after.Definition);
    public static MapStructuralDiffResult Compare(MapDefinition before, MapDefinition after)
    {
        var changes = new List<MapStructuralChange>();
        JsonNode? Node(object? value) => value == null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), MapPrefabService.PrefabJson);
        void Fields(MapDiffCategory category, string key, string label, JsonNode? a, JsonNode? b, string field = "")
        {
            if (JsonNode.DeepEquals(a, b)) return;
            if (a is JsonObject first && b is JsonObject second)
            {
                foreach (string property in first.Select(p => p.Key).Union(second.Select(p => p.Key)).OrderBy(x => x, StringComparer.Ordinal))
                    Fields(category, key, label, first[property], second[property], field.Length == 0 ? property : field + "." + property);
                return;
            }
            changes.Add(new(category, a == null ? MapDiffChangeKind.Added : b == null ? MapDiffChangeKind.Removed : MapDiffChangeKind.Modified,
                key, label, field, a?.ToJsonString(), b?.ToJsonString()));
        }
        void Collection<T>(MapDiffCategory category, IEnumerable<T> first, IEnumerable<T> second, Func<T, string> key, Func<T, string> label, Func<T, JsonNode?>? node = null)
        {
            var a = first.ToDictionary(key, StringComparer.Ordinal); var b = second.ToDictionary(key, StringComparer.Ordinal);
            foreach (string id in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool old = a.TryGetValue(id, out T? previous), next = b.TryGetValue(id, out T? current);
                Fields(category, id, label(next ? current! : previous!), old ? (node?.Invoke(previous!) ?? Node(previous)) : null, next ? (node?.Invoke(current!) ?? Node(current)) : null);
            }
        }
        JsonNode? ObjectNode(MapObject item, MapDefinition definition)
        {
            var node = Node(item.Value) as JsonObject;
            string Material(int slot) => slot >= 0 && slot < definition.Materials.Count ? definition.Materials[slot].Id.ToString("N") : "invalid:" + slot;
            if (item.Value is MapGeometry geometry) node!["material"] = Material(geometry.Material);
            if (item.Value is MapBrush brush) node!["material"] = Material(brush.Material);
            if (item.Value is MapMesh mesh) node!["faceMaterials"] = new JsonArray(mesh.FaceMaterials.Select(i => (JsonNode?)JsonValue.Create(Material(i))).ToArray());
            node!["objectType"] = item.Value.GetType().Name;
            return node;
        }
        MapDiffCategory Category(MapObject item) => item.Value switch { MapSpawn => MapDiffCategory.Spawn, MapItem => MapDiffCategory.Pickup,
            MapJumpPad => MapDiffCategory.JumpPad, MapNavigationLink => MapDiffCategory.Navigation, _ => MapDiffCategory.Object };
        Dictionary<string, MapObject> Objects(MapDefinition definition)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new Dictionary<string, MapObject>(StringComparer.Ordinal);
            foreach (var item in MapObjects.All(definition))
            {
                int index = counts.GetValueOrDefault(item.Kind); counts[item.Kind] = index + 1;
                string key = item.Id == Guid.Empty ? "legacy-" + item.Kind + "-" + index : item.Id.ToString("N");
                result.Add(key, item);
            }
            return result;
        }
        var oldObjects = Objects(before); var nextObjects = Objects(after);
        foreach (string id in oldObjects.Keys.Union(nextObjects.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var a = oldObjects.GetValueOrDefault(id); var b = nextObjects.GetValueOrDefault(id);
            Fields(Category(b ?? a!), id, (b ?? a!).Label, a == null ? null : ObjectNode(a, before), b == null ? null : ObjectNode(b, after));
        }
        Collection(MapDiffCategory.Material, before.Materials.Select((m, i) => (Material: m, Index: i)), after.Materials.Select((m, i) => (Material: m, Index: i)),
            p => p.Material.Id == Guid.Empty ? "legacy-material-" + p.Index : p.Material.Id.ToString("N"), p => p.Material.Name, p => Node(p.Material));
        Collection(MapDiffCategory.Asset, before.Assets, after.Assets, a => a.Path, a => a.Name ?? a.Path);
        JsonNode? PrefabNode(MapPrefabInstance instance)
        {
            var node = Node(instance)!.AsObject();
            foreach (string property in new[] { "members", "materials" })
                foreach (JsonNode? binding in node[property]!.AsArray()) { binding!.AsObject().Remove("baseline"); binding.AsObject().Remove("overrides"); }
            return node;
        }
        Collection(MapDiffCategory.Prefab, before.PrefabInstances, after.PrefabInstances, i => i.Id.ToString("N"), i => i.SourcePath, PrefabNode);
        Fields(MapDiffCategory.Capability, "capabilities", "Supported game modes", Node(before.Capabilities), Node(after.Capabilities));
        // Exclude collections already compared by stable identity; remaining canonical properties describe map environment/metadata.
        var oldEnvironment = Node(before)!.AsObject(); var nextEnvironment = Node(after)!.AsObject();
        foreach (string property in new[] { "geometry", "brushes", "spawns", "items", "jumpPads", "navigationLinks", "materials", "assets", "capabilities", "prefabInstances", "prefabSource" })
        { oldEnvironment.Remove(property); nextEnvironment.Remove(property); }
        Fields(MapDiffCategory.Environment, "map", after.InGameName ?? after.Name, oldEnvironment, nextEnvironment);
        return new(changes.AsReadOnly());
    }
}
