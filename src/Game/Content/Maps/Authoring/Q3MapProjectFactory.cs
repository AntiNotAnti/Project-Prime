using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.MapGen;

public enum Q3SourceReferenceMode
{
    CopyIntoProject,
    ReferenceExternally
}

/// <summary>Creates the read-only imported-geometry project used by the CLI, launcher, and editor.</summary>
public static class Q3MapProjectFactory
{
    public static MapProject Create(string sourcePath, string projectPath, string stableId,
        string displayName, string? mapName = null, string? textures = null,
        float unitsPerUnit = 28, bool keepClip = true, bool keepSky = false,
        bool keepSpawns = true, int patchLevel = 3, float textureScale = 24,
        Q3SourceReferenceMode sourceReferenceMode = Q3SourceReferenceMode.CopyIntoProject)
    {
        string source = Path.GetFullPath(sourcePath);
        string output = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(output)!;
        string sourceReference = MakeProjectReference(source, projectDirectory, "source",
            sourceReferenceMode);
        string? textureReference = string.IsNullOrWhiteSpace(textures) ? null
            : MakeProjectReference(Path.GetFullPath(textures,
                    Path.GetDirectoryName(source)!), projectDirectory,
                "textures", sourceReferenceMode);
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
                    Textures = textureReference,
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

    private static string MakeProjectReference(string source, string projectDirectory,
        string localDirectory, Q3SourceReferenceMode mode)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Q3 import source does not exist.", source);
        string relative = Path.GetRelativePath(projectDirectory, source).Replace('\\', '/');
        if (!relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal)) return relative;
        if (mode == Q3SourceReferenceMode.ReferenceExternally) return source;

        string destinationDirectory = Path.Combine(projectDirectory, localDirectory);
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        string sourceHash = Hash(source);
        if (File.Exists(destination) && Hash(destination) != sourceHash)
        {
            destination = Path.Combine(destinationDirectory,
                Path.GetFileNameWithoutExtension(source) + "-" + sourceHash[..12]
                + Path.GetExtension(source));
        }
        if (!File.Exists(destination))
        {
            string temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temporary, overwrite: false);
                File.Move(temporary, destination, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return Path.GetRelativePath(projectDirectory, destination).Replace('\\', '/');
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
