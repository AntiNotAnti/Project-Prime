using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Reusable selections stored as ordinary map-definition JSON plus local texture assets.</summary>
public static partial class MapPrefabService
{
    public sealed record InsertResult(IReadOnlyList<Guid> ObjectIds, IReadOnlyList<string> GeneratedAssets, Guid InstanceId = default);

    public static void Save(MapDefinition source, ISet<Guid> selected, string path)
    {
        if (selected.Count == 0) throw new InvalidOperationException("Select at least one object to save a prefab.");
        string full = Path.GetFullPath(path);
        string root = Path.GetDirectoryName(full) ?? throw new IOException("Prefab path has no directory.");
        Directory.CreateDirectory(root);

        var prefab = MapProjectSerializer.Clone(source);
        prefab.Name = "PREFAB";
        prefab.InGameName = null;
        prefab.Author = null;
        prefab.Version = null;
        prefab.Description = null;
        prefab.Import = null;
        prefab.Collision = null;
        prefab.Preview = null;
        prefab.Audio = null;
        prefab.Capabilities = null;
        prefab.PrefabInstances.Clear();
        prefab.ModelSources.Clear();
        // Re-saving a library file preserves its identity, independent of its parent map.
        prefab.PrefabSource = File.Exists(full)
            ? MapDefinition.Load(full).PrefabSource ?? new MapPrefabSource()
            : new MapPrefabSource();
        prefab.MapId = prefab.PrefabSource.Id;
        prefab.Brushes.RemoveAll(x => !selected.Contains(x.Id));
        prefab.Geometry.RemoveAll(x => !selected.Contains(x.Id));
        prefab.Spawns.RemoveAll(x => !selected.Contains(x.Id));
        prefab.Items.RemoveAll(x => !selected.Contains(x.Id));
        prefab.JumpPads.RemoveAll(x => !selected.Contains(x.Id));
        prefab.NavigationLinks.RemoveAll(x => !selected.Contains(x.Id));
        var objects = MapObjects.All(prefab).ToArray();
        if (objects.Length == 0 || objects.Length > MaxMembers || objects.Any(o => o.Id == Guid.Empty)
            || objects.Select(o => o.Id).Distinct().Count() != objects.Length)
            throw new InvalidDataException("Prefab selection requires 1–4096 objects with unique stable IDs. Open the project in the editor first to assign legacy IDs.");

        var used = prefab.Geometry.Select(g => g.Material).Concat(prefab.Brushes.Select(b => b.Material))
            .Concat(prefab.Geometry.OfType<MapMesh>().SelectMany(m => m.FaceMaterials))
            .Distinct().Where(i => i >= 0 && i < source.Materials.Count).OrderBy(i => i).ToArray();
        var remap = used.Select((old, index) => (old, index)).ToDictionary(x => x.old, x => x.index);
        prefab.Materials = used.Select(i => MapSnapshotCopy.Copy(source.Materials[i])).Cast<MapMaterial>().ToList();
        for (int i = 0; i < prefab.Materials.Count; i++)
            if (prefab.Materials[i].Id == Guid.Empty) prefab.Materials[i].Id = StableSourceId(prefab.PrefabSource.Id, "material", used[i]);
        foreach (var geometry in prefab.Geometry) { geometry.Material = remap[geometry.Material]; if (geometry is MapMesh mesh) mesh.FaceMaterials = mesh.FaceMaterials.Select(i => remap[i]).ToList(); }
        foreach (var brush in prefab.Brushes) brush.Material = remap[brush.Material];

        prefab.Assets.Clear();
        string assetRoot = Path.Combine(root, Path.GetFileNameWithoutExtension(full) + ".assets");
        foreach (var material in prefab.Materials)
        {
            void CopyChannel(string? original, Action<string> assign)
            {
                if (String.IsNullOrEmpty(original)) return;
                byte[] bytes = MapAssets.Read(source, original);
                Directory.CreateDirectory(assetRoot);
                string relative = Path.Combine(Path.GetFileName(assetRoot), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() + Path.GetExtension(original).ToLowerInvariant())
                    .Replace('\\', '/');
                AtomicFile.Write(Path.Combine(root, relative), bytes);
                assign(relative);
                if (!prefab.Assets.Any(a => a.Path == relative)) prefab.Assets.Add(new() { Path = relative, Kind = "texture", Name = material.Name });
            }
            CopyChannel(material.Texture, value => material.Texture = value);
            CopyChannel(material.Albedo, value => material.Albedo = value);
            CopyChannel(material.Normal, value => material.Normal = value);
            CopyChannel(material.SpecularRoughness, value => material.SpecularRoughness = value);
            CopyChannel(material.Emissive, value => material.Emissive = value);
            if (material.Animation != null)
                for (int i = 0; i < material.Animation.FlipbookFrames.Count; i++)
                { int frame = i; CopyChannel(material.Animation.FlipbookFrames[i], value => material.Animation.FlipbookFrames[frame] = value); }
        }
        prefab.BaseDirectory = root;
        prefab.SourcePath = full;
        prefab.BundlePath = null;
        prefab.PrefabSource.Revision = Revision(prefab);
        prefab.Save(full);
    }

    public static InsertResult Insert(MapDefinition destination, string prefabPath, string destinationRoot,
        float[]? offset = null)
    {
        var result = InsertLinked(destination, prefabPath, destinationRoot,
            new MapTransform { Position = offset ?? new[] { 1f, 0f, 1f } });
        return new(result.ObjectIds, result.GeneratedAssets, result.InstanceId);
    }
}
