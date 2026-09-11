using System.IO;

namespace MphRead.Mods.MapGen;

/// <summary>Compatibility facade for the historical Tools map-bundle command.</summary>
public static class MapBundleTools
{
    public static string Cook(MapDefinition definition, string recipePath, string? outputPath,
        bool verbose = true)
        => Cook(MapProject.FromLegacy(definition), recipePath, outputPath, verbose);

    public static string Cook(MapProject project, string recipePath, string? outputPath,
        bool verbose = true)
    {
        string path = outputPath ?? Path.Combine(CustomRooms.MapDirectory,
            Path.GetFileNameWithoutExtension(recipePath) + MapBundle.Extension);
        MapBundleWriteResult result = MapPackageBuilder.Cook(project, recipePath, path, verbose);
        if (verbose)
        {
            System.Console.WriteLine($"[mapbundle] {project.Map.Name} -> {path} "
                + $"({result.PackageSize / 1024} KiB, content "
                + $"{result.ContentIdentity.ContentHash[..12]}, artifact {result.ArtifactHash[..12]})");
        }
        return result.Path;
    }
}
