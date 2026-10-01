using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public static class MapMaterialEditing
{
    public static Guid[] Usages(MapDefinition definition, int material) => definition.Geometry
        .Where(g => g is MapMesh m ? Enumerable.Range(0, m.Faces.Count).Any(f =>
            (f < m.FaceMaterials.Count ? m.FaceMaterials[f] : m.Material) == material) : g.Material == material).Select(g => g.Id)
        .Concat(definition.Brushes.Where(b => b.Material == material).Select(b => b.Id)).ToArray();

    private static void Remap(MapDefinition definition, Func<int, int> map)
    {
        foreach (var g in definition.Geometry)
        {
            g.Material = map(g.Material);
            if (g is MapMesh mesh) for (int i = 0; i < mesh.FaceMaterials.Count; i++) mesh.FaceMaterials[i] = map(mesh.FaceMaterials[i]);
        }
        foreach (var b in definition.Brushes) b.Material = map(b.Material);
        if (definition.Import is { } import)
        {
            import.DefaultMaterial = map(import.DefaultMaterial);
            foreach (string key in import.ShaderMaterials.Keys.ToArray()) import.ShaderMaterials[key] = map(import.ShaderMaterials[key]);
            foreach (var replacement in import.MaterialReplacements.Where(r => !r.TargetSource)) replacement.Target = map(replacement.Target);
        }
        if (definition.NativeRoom is { } native)
            foreach (var replacement in native.MaterialReplacements) replacement.Target = map(replacement.Target);
    }
    public static void Replace(MapDefinition definition, int source, int target)
    {
        if (source < 0 || source >= definition.Materials.Count || target < 0 || target >= definition.Materials.Count) throw new ArgumentOutOfRangeException();
        Remap(definition, i => i == source ? target : i);
    }
    public static void DeleteUnused(MapDefinition definition, int material)
    {
        if (material < 0 || material >= definition.Materials.Count || definition.Materials.Count <= 1) throw new InvalidOperationException("Keep at least one material.");
        bool used = false;
        Remap(definition, i => { if (i == material) used = true; return i; });
        if (used) throw new InvalidOperationException("This material is referenced. Replace its usages before deleting it.");
        Guid removed = definition.Materials[material].Id;
        definition.Materials.RemoveAt(material);
        int Shift(int i) => i > material ? i - 1 : i;
        Remap(definition, Shift);
        foreach (var source in definition.ModelSources)
        {
            foreach (string key in source.MaterialMappings.Where(p => p.Value == removed).Select(p => p.Key).ToArray())
            { source.MaterialMappings.Remove(key); source.MaterialBaselines.Remove(key); }
            foreach (var item in source.Objects)
            {
                item.BaseMaterial = item.BaseMaterial == material ? -1 : Shift(item.BaseMaterial);
                foreach (string face in item.FaceMaterials.Keys.ToArray())
                    item.FaceMaterials[face] = item.FaceMaterials[face] == material ? -1 : Shift(item.FaceMaterials[face]);
            }
        }
    }
}
