using System;
using System.IO;

namespace MphRead.Mods.MapGen;

/// <summary>Creates the read-only imported-geometry project used by the CLI, launcher, and editor.</summary>
public static class Q3MapProjectFactory
{
    public static MapProject Create(string sourcePath, string projectPath, string stableId,
        string displayName, string? mapName = null, string? textures = null,
        float unitsPerUnit = 28, bool keepClip = true, bool keepSky = false,
        bool keepSpawns = true, int patchLevel = 3, float textureScale = 24)
    {
        string source = Path.GetFullPath(sourcePath);
        string output = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(output)!;
        string relative = Path.GetRelativePath(projectDirectory, source).Replace('\\', '/');
        string sourceReference = relative.StartsWith("../", StringComparison.Ordinal) ? source : relative;
        var project = new MapProject
        {
            StableId = stableId,
            Metadata = new MapProjectMetadata { Name = displayName, Author = Environment.UserName },
            Environment = new MapEnvironment(),
            SupportedModes = [MapMode.Battle, MapMode.Survival],
            Map = new MapDefinition
            {
                Name = displayName.ToUpperInvariant(),
                InGameName = displayName,
                TextureSource = "MP3 PROVING GROUND",
                Import = new MapImport
                {
                    Source = sourceReference,
                    MapName = mapName,
                    Textures = textures,
                    UnitsPerUnit = unitsPerUnit,
                    KeepClip = keepClip,
                    KeepSky = keepSky,
                    KeepSpawns = keepSpawns,
                    PatchLevel = patchLevel,
                    TexScale = textureScale
                }
            }
        };
        project.Map.Materials.Add(new MapMaterial { Name = "default", SourceMaterial = 1 });
        return project;
    }
}
