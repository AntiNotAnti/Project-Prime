using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

/// <summary>Applies detached importer output in one document transaction; never reads external files.</summary>
public static class ModelReimport
{
    private static string Hash(object? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string MaterialHash(MapMaterial material) => Hash(new { material.Name, material.Texture, material.SourceMaterial, material.TexScale });
    public static string NormalizedHash(ImportedModel model) => Hash(new
    {
        Meshes = model.Meshes.Select(m => new { m.Label, m.Vertices, m.Faces, m.FaceMaterials, m.FaceTexcoords, m.Material, m.Solid, m.CollisionOnly, m.Terrain, m.Damaging, m.Slipperiness, m.ReflectBeams, m.IgnorePlayers, m.IgnoreBeams, m.IgnoreScan }),
        Materials = model.Materials.Select(MaterialHash),
        Assets = model.Assets.OrderBy(a => a.Key).Select(a => new { a.Key, Hash = Convert.ToHexString(SHA256.HashData(a.Value)) })
    });
    private static string FaceKey(MapMesh mesh, int index) => Hash(mesh.Faces[index].Select(i => mesh.Vertices[i]).ToArray());
    private static int FaceMaterial(MapMesh mesh, int index) => index < mesh.FaceMaterials.Count ? mesh.FaceMaterials[index] : mesh.Material;
    private static string FaceUvHash(MapMesh mesh, int index) => Hash(index < mesh.FaceTexcoords.Count ? mesh.FaceTexcoords[index] : null!);

    public static MapModelSource Apply(MapDefinition definition, ImportedModel imported, string path,
        string sourceHash, ModelImportSettings settings, Guid? sourceId = null)
    {
        var previous = sourceId.HasValue ? definition.ModelSources.Single(s => s.Id == sourceId) : null;
        string normalized = NormalizedHash(imported);
        if (previous != null && previous.NormalizedHash == normalized && previous.Source == path && previous.Settings == settings)
            return previous;
        var source = new MapModelSource { Id = previous?.Id ?? Guid.NewGuid(), Source = path,
            SourceHash = sourceHash, NormalizedHash = normalized, Settings = settings };
        var materials = new int[imported.Materials.Count];
        var materialNames = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < materials.Length; i++)
        {
            var material = imported.Materials[i];
            if (material.Id == Guid.Empty) material.Id = Guid.NewGuid();
            string name = material.Name ?? "Material";
            materialNames.TryGetValue(name, out int ordinal); materialNames[name] = ordinal + 1;
            string key = name + "#" + ordinal;
            int existing = previous != null && previous.MaterialMappings.TryGetValue(key, out var id)
                ? definition.Materials.FindIndex(m => m.Id == id) : -1;
            string baseline = MaterialHash(material);
            if (existing >= 0)
            {
                // Preserve a material edited in Studio. Unedited source materials follow external changes.
                if (previous!.MaterialBaselines.TryGetValue(key, out string? oldHash)
                    && MaterialHash(definition.Materials[existing]) == oldHash)
                { material.Id = definition.Materials[existing].Id; definition.Materials[existing] = material; }
                materials[i] = existing;
            }
            else { materials[i] = definition.Materials.Count; definition.Materials.Add(material); }
            source.MaterialMappings[key] = definition.Materials[materials[i]].Id;
            source.MaterialBaselines[key] = baseline;
        }
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mesh in imported.Meshes)
        {
            names.TryGetValue(mesh.Label, out int ordinal); names[mesh.Label] = ordinal + 1;
            string key = mesh.Label + "#" + ordinal;
            var oldRecord = previous?.Objects.FirstOrDefault(o => o.Key == key);
            var old = oldRecord == null ? null : definition.Geometry.OfType<MapMesh>().FirstOrDefault(m => m.Id == oldRecord.Id);
            if (!mesh.CollisionOnly) mesh.Layer = "Imported Models / " + Path.GetFileName(path);
            mesh.Material = materials[mesh.Material];
            for (int f = 0; f < mesh.FaceMaterials.Count; f++) mesh.FaceMaterials[f] = materials[mesh.FaceMaterials[f]];
            var record = new MapModelObject { Key = key, Id = old?.Id ?? mesh.Id, BaseMaterial = mesh.Material };
            // Ambiguous coincident faces deliberately receive source values rather than guessing an override.
            var oldFaces = old?.Faces.Select((_, f) => (Key: FaceKey(old, f), Index: f)).GroupBy(x => x.Key)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Index);
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                string face = FaceKey(mesh, f);
                record.FaceMaterials[face] = FaceMaterial(mesh, f);
                record.FaceUvs[face] = FaceUvHash(mesh, f);
                if (old != null && oldFaces!.TryGetValue(face, out int oldFace))
                {
                    if (old.FaceUv.TryGetValue(oldFace, out var projected)) mesh.FaceUv[f] = projected;
                    if (oldRecord!.FaceMaterials.TryGetValue(face, out int baseline) && FaceMaterial(old, oldFace) != baseline)
                        mesh.FaceMaterials[f] = FaceMaterial(old, oldFace);
                    if (oldRecord.FaceUvs.TryGetValue(face, out var uvHash) && FaceUvHash(old, oldFace) != uvHash)
                        mesh.FaceTexcoords[f] = oldFace < old.FaceTexcoords.Count ? old.FaceTexcoords[oldFace] : null;
                }
            }
            if (old != null)
            {
                mesh.Id = old.Id; mesh.Label = old.Label; mesh.Transform = old.Transform;
                mesh.Hidden = old.Hidden; mesh.Locked = old.Locked; mesh.Layer = old.Layer;
                mesh.Uv = old.Uv; mesh.Damaging = old.Damaging; mesh.Terrain = old.Terrain; mesh.Shade = old.Shade;
                if (old.Material != oldRecord!.BaseMaterial) mesh.Material = old.Material;
                // An explicit change to the collision import option takes precedence over an editor override.
                if (previous!.Settings.VisualCollision == settings.VisualCollision && previous.Settings.Collision == settings.Collision) mesh.Solid = old.Solid;
                definition.Geometry.Remove(old);
            }
            definition.Geometry.Add(mesh); source.Objects.Add(record);
        }
        if (previous != null)
        {
            var retained = source.Objects.Select(o => o.Id).ToHashSet();
            var removed = previous.Objects.Where(o => !retained.Contains(o.Id)).Select(o => o.Id).ToHashSet();
            definition.Geometry.RemoveAll(m => removed.Contains(m.Id));
            definition.ModelSources.Remove(previous);
        }
        foreach (var asset in imported.Assets.Keys)
            if (!definition.Assets.Any(a => a.Path == asset)) definition.Assets.Add(new() { Path = asset, Kind = "texture", SourcePath = imported.AssetSources.GetValueOrDefault(asset) });
        definition.ModelSources.Add(source);
        return source;
    }
}
