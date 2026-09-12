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
                AttachRuntimeContext(project, fullPath);
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
            packagedProject.DeclaredContentIdentity = new MapContentIdentity(
                bundle.Manifest.Identity, bundle.Manifest.ContentHash);
            AttachRuntimeContext(packagedProject, fullPath, fullPath);
            return packagedProject;
        }
        MapDefinition definition = bundle.IsLegacy
            ? MapDefinition.DeserializeLegacy(System.Text.Encoding.UTF8.GetString(recipe.Span), fullPath)
            : JsonSerializer.Deserialize(recipe.Span, MapJsonContext.Default.MapDefinition)
                ?? throw new MapValidationException("Map package recipe is null.");
        Attach(definition, fullPath, bundlePath: fullPath);
        MapProject project = MapProject.FromLegacy(definition, bundle.Manifest.StableId);
        return AttachRuntimeContext(project, fullPath, fullPath);
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
            AttachRuntimeContext(project, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// Restores non-serialized source context after load, undo/redo, recovery,
    /// autosave, or a temporary playtest snapshot. This is the only owner of
    /// authoring-path context attachment.
    /// </summary>
    public static MapProject AttachRuntimeContext(MapProject project,
        string? sourcePath, string? bundlePath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (sourcePath == null)
        {
            project.SourcePath = null;
            Attach(project.Map, null, null);
            return project;
        }
        string fullPath = Path.GetFullPath(sourcePath);
        project.SourcePath = fullPath;
        Attach(project.Map, fullPath, bundlePath == null
            ? null : Path.GetFullPath(bundlePath));
        return project;
    }

    private static void Attach(MapDefinition definition, string? sourcePath,
        string? bundlePath)
    {
        definition.SourcePath = sourcePath;
        definition.BaseDirectory = sourcePath == null
            ? null : Path.GetDirectoryName(sourcePath);
        definition.BundlePath = bundlePath;
        if (definition.Import != null)
        {
            definition.Import.BaseDirectory = definition.BaseDirectory;
            definition.Import.BundlePath = bundlePath;
        }
    }
}
