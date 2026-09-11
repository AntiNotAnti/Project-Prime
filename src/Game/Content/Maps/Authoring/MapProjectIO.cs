using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public static class MapProjectIO
{
    public static MapProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (MapBundle.Is(fullPath))
            return LoadBundle(fullPath);

        byte[] bytes = File.ReadAllBytes(fullPath);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("stableId", out _)
                && document.RootElement.TryGetProperty("map", out _))
            {
                MapProject project = JsonSerializer.Deserialize(bytes, MapJsonContext.Default.MapProject)
                    ?? throw new MapValidationException("Map project is null.");
                project.SourcePath = fullPath;
                Attach(project.Map, fullPath, bundlePath: null);
                return project;
            }
        }
        catch (JsonException)
        {
            // Legacy parser below retains comments and trailing-comma support.
        }
        MapDefinition legacy = MapDefinition.Load(fullPath);
        return MapProject.FromLegacy(legacy);
    }

    internal static MapProject LoadBundle(string path, MapBundleReadResult? verifiedBundle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        MapBundleReadResult bundle = verifiedBundle ?? new MapBundleReader().Read(fullPath);
        ReadOnlyMemory<byte> recipe = bundle.ReadDeclaredFile(bundle.Manifest.Recipe);
        using JsonDocument document = MapJson.ParseStrict(recipe.Span);
        if (document.RootElement.TryGetProperty("stableId", out _)
            && document.RootElement.TryGetProperty("map", out _))
        {
            MapProject packagedProject = JsonSerializer.Deserialize(recipe.Span,
                MapJsonContext.Default.MapProject)
                ?? throw new MapValidationException("Map package project is null.");
            packagedProject.SourcePath = fullPath;
            packagedProject.DeclaredContentIdentity = new MapContentIdentity(
                bundle.Manifest.Identity, bundle.Manifest.ContentHash);
            Attach(packagedProject.Map, fullPath, bundlePath: fullPath);
            return packagedProject;
        }
        MapDefinition definition = bundle.IsLegacy
            ? MapDefinition.DeserializeLegacy(System.Text.Encoding.UTF8.GetString(recipe.Span), fullPath)
            : JsonSerializer.Deserialize(recipe.Span, MapJsonContext.Default.MapDefinition)
                ?? throw new MapValidationException("Map package recipe is null.");
        Attach(definition, fullPath, bundlePath: fullPath);
        MapProject project = MapProject.FromLegacy(definition, bundle.Manifest.StableId);
        project.SourcePath = fullPath;
        return project;
    }

    public static void Save(MapProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(project, MapJsonContext.Default.MapProject);
        string temporary = fullPath + ".save-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
            project.SourcePath = fullPath;
            Attach(project.Map, fullPath, bundlePath: null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Attach(MapDefinition definition, string sourcePath, string? bundlePath)
    {
        definition.SourcePath = sourcePath;
        definition.BaseDirectory = Path.GetDirectoryName(sourcePath);
        definition.BundlePath = bundlePath;
        if (definition.Import != null)
        {
            definition.Import.BaseDirectory = definition.BaseDirectory;
            definition.Import.BundlePath = bundlePath;
        }
    }
}
