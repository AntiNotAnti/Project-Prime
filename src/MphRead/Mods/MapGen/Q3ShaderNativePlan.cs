using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// A per-compile native animation plan for the imported FPTX shader order.
/// No authoring source, bundled archive or map format is mutated.
/// </summary>
internal static class Q3ShaderNativePlan
{
    internal const int MaxImportedTracks = 16;

    internal static IReadOnlyList<MapMaterial> ForImport(
        MapDefinition definition, MapTexturePack pack)
    {
        string? path = definition.Import?.Resolve();
        var scripts = path == null
            ? new Dictionary<string, Q3ShaderProgram>(StringComparer.OrdinalIgnoreCase)
            : Q3ShaderStageParser.ReadArchives(MapTextureBake.DiscoverArchives(path));
        return Build(definition.Materials,
            pack.Entries.Select(e => e.Name).ToArray(), scripts);
    }

    internal static IReadOnlyList<MapMaterial> Build(
        IReadOnlyList<MapMaterial> authored,
        IReadOnlyList<string> importedNames,
        IReadOnlyDictionary<string, Q3ShaderProgram> scripts)
    {
        var plan = new List<MapMaterial>(importedNames.Count + authored.Count);
        for (int i = 0; i < importedNames.Count; i++)
        {
            string name = importedNames[i];
            plan.Add(new MapMaterial {
                Name = name.Length <= 30 ? name : name[^30..]
            });
        }
        plan.AddRange(authored);
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3_NATIVE_TCMOD") == "0")
            return plan;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (MapMaterial material in authored) used.Add(material.Name);

        int imported = 0;
        for (int i = 0; i < importedNames.Count; i++)
        {
            string name = plan[i].Name;
            if (!MapUvAnimation.NativeNameFits(name)
                || !used.Add(name)
                || !scripts.TryGetValue(importedNames[i], out Q3ShaderProgram? shader)
                || !shader.TryNativeUv(out MapMaterialAnimation animation))
                continue;

            plan[i] = new MapMaterial { Name = name, Animation = animation };
            MapMaterial[] animated = plan.Where(MapUvAnimation.IsAnimated).ToArray();
            if (!MapUvAnimation.TryGetGroupFrameCount(animated, out int group)
                || MapUvAnimation.ScaleEntryCount(animated, group) > MapUvAnimation.MaxLutEntries
                || MapUvAnimation.RotationEntryCount(animated, group) > MapUvAnimation.MaxLutEntries
                || MapUvAnimation.TranslationEntryCount(animated, group) > MapUvAnimation.MaxLutEntries
                || ++imported > MaxImportedTracks)
            {
                // An upstream shader must not compromise authored materials,
                // overload native LUTs or spill over the 16-bit address space.
                plan[i] = new MapMaterial { Name = name };
                --imported;
            }
        }
        return plan;
    }
}
