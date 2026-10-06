using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed partial class MapDocument
{
    public MapPrefabService.InstanceResult InsertPrefab(string path, string destinationRoot, MapTransform? transform = null)
    {
        using var prepared = PreparePrefabInsertAsync(path, destinationRoot, transform).GetAwaiter().GetResult();
        return ApplyPreparedPrefabEdit(prepared);
    }
    public MapPrefabService.InstanceResult UpdatePrefab(Guid id, string destinationRoot)
    {
        using var prepared = PreparePrefabUpdateAsync(id, destinationRoot).GetAwaiter().GetResult();
        return ApplyPreparedPrefabEdit(prepared);
    }
    public void DetachPrefab(Guid id) => PrefabEdit("Detach prefab", d => MapPrefabService.Detach(d, id));
    public void SetPrefabTransform(Guid id, MapTransform transform) => PrefabEdit("Transform prefab", d => MapPrefabService.SetTransform(d, id, transform));

    private void PrefabEdit(string label, Action<MapDefinition> operation)
    {
        var before = Project.Definition;
        var after = MapSnapshotCopy.Copy(before);
        operation(after); // All validation/filesystem preparation finishes before entering the history transaction.
        var command = CreatePrefabCommand(label, before, after);
        if (command != null) History.Execute(command);
    }

    private IMapEditCommand? CreatePrefabCommand(string label, MapDefinition before, MapDefinition after)
    {
        var beforeObjects = CaptureObjects(before, null).ToDictionary(x => x.Id);
        var afterObjects = CaptureObjects(after, null).ToDictionary(x => x.Id);
        var ids = beforeObjects.Keys.Union(afterObjects.Keys)
            .Where(id => beforeObjects.GetValueOrDefault(id)?.Json != afterObjects.GetValueOrDefault(id)?.Json).ToHashSet();
        string Json<T>(T value) => JsonSerializer.Serialize(value, MapPrefabService.PrefabJson);
        var materials = before.Materials.Select((m, index) => (m, index)).ToDictionary(x => x.index, x => Json(x.m));
        var nextMaterials = after.Materials.Select((m, index) => (m, index)).ToDictionary(x => x.index, x => Json(x.m));
        var materialIndices = materials.Keys.Union(nextMaterials.Keys).Where(i => materials.GetValueOrDefault(i) != nextMaterials.GetValueOrDefault(i)).ToArray();
        var instances = before.PrefabInstances.ToDictionary(x => x.Id, Json);
        var nextInstances = after.PrefabInstances.ToDictionary(x => x.Id, Json);
        var instanceIds = instances.Keys.Union(nextInstances.Keys).Where(id => instances.GetValueOrDefault(id) != nextInstances.GetValueOrDefault(id)).ToArray();
        var assets = before.Assets.ToDictionary(x => x.Path, Json, StringComparer.Ordinal);
        var nextAssets = after.Assets.ToDictionary(x => x.Path, Json, StringComparer.Ordinal);
        var assetPaths = assets.Keys.Union(nextAssets.Keys).Where(path => assets.GetValueOrDefault(path) != nextAssets.GetValueOrDefault(path)).ToArray();
        if (ids.Count + materialIndices.Length + instanceIds.Length + assetPaths.Length == 0) return null;
        return new PrefabCommand(this, label, ids.ToArray(),
            beforeObjects.Values.Where(x => ids.Contains(x.Id)).ToArray(), afterObjects.Values.Where(x => ids.Contains(x.Id)).ToArray(),
            materialIndices.ToDictionary(i => i, i => materials.GetValueOrDefault(i)), materialIndices.ToDictionary(i => i, i => nextMaterials.GetValueOrDefault(i)),
            instanceIds.ToDictionary(id => id, id => instances.GetValueOrDefault(id)), instanceIds.ToDictionary(id => id, id => nextInstances.GetValueOrDefault(id)),
            assetPaths.ToDictionary(p => p, p => assets.GetValueOrDefault(p)), assetPaths.ToDictionary(p => p, p => nextAssets.GetValueOrDefault(p)),
            before.PrefabInstances.Select((i, index) => (i.Id, index)).ToDictionary(p => p.Id, p => p.index),
            after.PrefabInstances.Select((i, index) => (i.Id, index)).ToDictionary(p => p.Id, p => p.index));
    }

    private sealed class PrefabCommand : IMapEditCommand
    {
        private readonly MapDocument _document;
        private readonly Guid[] _ids;
        private readonly ObjectValue[] _before, _after;
        private readonly Dictionary<int, string?> _beforeMaterials, _afterMaterials;
        private readonly Dictionary<Guid, string?> _beforeInstances, _afterInstances;
        private readonly Dictionary<string, string?> _beforeAssets, _afterAssets;
        private readonly Dictionary<Guid, int> _beforeInstanceIndices, _afterInstanceIndices;
        public string Label { get; }
        public long ApproximateBytes { get; }
        public MapDocumentChange Change { get; }
        public IEnumerable<string> RetainedAssets => _beforeAssets.Concat(_afterAssets).Where(p => p.Value != null).Select(p => p.Key);
        public PrefabCommand(MapDocument document, string label, Guid[] ids, ObjectValue[] before, ObjectValue[] after,
            Dictionary<int, string?> beforeMaterials, Dictionary<int, string?> afterMaterials,
            Dictionary<Guid, string?> beforeInstances, Dictionary<Guid, string?> afterInstances,
            Dictionary<string, string?> beforeAssets, Dictionary<string, string?> afterAssets,
            Dictionary<Guid, int> beforeInstanceIndices, Dictionary<Guid, int> afterInstanceIndices)
        {
            _document = document; Label = label; _ids = ids; _before = before; _after = after;
            _beforeMaterials = beforeMaterials; _afterMaterials = afterMaterials;
            _beforeInstances = beforeInstances; _afterInstances = afterInstances; _beforeAssets = beforeAssets; _afterAssets = afterAssets;
            _beforeInstanceIndices = beforeInstanceIndices; _afterInstanceIndices = afterInstanceIndices;
            ApproximateBytes = before.Concat(after).Sum(v => v.Bytes) + 512 + 4L * beforeMaterials.Values.Concat(afterMaterials.Values)
                .Concat(beforeInstances.Values).Concat(afterInstances.Values).Concat(beforeAssets.Values).Concat(afterAssets.Values).Sum(v => v?.Length ?? 0);
            if (ApproximateBytes > 64L * 1024 * 1024) throw new InvalidOperationException("Prefab edit exceeds its history budget.");
            Change = new(MapChangeDomain.Geometry | MapChangeDomain.Entity | MapChangeDomain.Material | MapChangeDomain.Navigation | MapChangeDomain.Metadata, Array.AsReadOnly(ids));
        }
        public void Execute() => Apply(_after, _afterMaterials, _afterInstances, _afterAssets, _afterInstanceIndices);
        public void Undo() => Apply(_before, _beforeMaterials, _beforeInstances, _beforeAssets, _beforeInstanceIndices);
        private void Apply(ObjectValue[] values, Dictionary<int, string?> materials, Dictionary<Guid, string?> instances, Dictionary<string, string?> assets, Dictionary<Guid, int> instanceIndices)
        {
            var definition = _document.Project.Definition;
            // Decode everything first: a damaged retained command cannot partially mutate the document.
            var decodedMaterials = materials.ToDictionary(p => p.Key, p => p.Value == null ? null : JsonSerializer.Deserialize<MapMaterial>(p.Value, MapPrefabService.PrefabJson)!);
            var decodedInstances = instances.Where(p => p.Value != null).Select(p => JsonSerializer.Deserialize<MapPrefabInstance>(p.Value!, MapPrefabService.PrefabJson)!).ToArray();
            var decodedAssets = assets.Where(p => p.Value != null).Select(p => JsonSerializer.Deserialize<MapAsset>(p.Value!, MapPrefabService.PrefabJson)!).ToArray();
            foreach (Guid id in _ids) MapPrefabService.RemoveObject(definition, id);
            foreach (var item in values.OrderBy(v => v.Index)) item.Insert(definition);
            foreach (var pair in decodedMaterials.OrderByDescending(p => p.Key).Where(p => p.Value == null)) definition.Materials.RemoveAt(pair.Key);
            foreach (var pair in decodedMaterials.OrderBy(p => p.Key).Where(p => p.Value != null))
            { if (pair.Key == definition.Materials.Count) definition.Materials.Add(pair.Value!); else definition.Materials[pair.Key] = pair.Value!; }
            definition.PrefabInstances.RemoveAll(i => instances.ContainsKey(i.Id));
            foreach (var instance in decodedInstances.OrderBy(i => instanceIndices[i.Id]))
                definition.PrefabInstances.Insert(Math.Min(instanceIndices[instance.Id], definition.PrefabInstances.Count), instance);
            definition.Assets.RemoveAll(a => assets.ContainsKey(a.Path)); definition.Assets.AddRange(decodedAssets);
        }
    }
}
