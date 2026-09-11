using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Computes the logical identity of editable map source plus every external
/// dependency that can affect compilation. Physical paths and timestamps are
/// deliberately excluded.
/// </summary>
public static class MapProjectContentHasher
{
    public static string Compute(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        byte[] canonical = CanonicalProject(project);
        var hashes = new List<byte>(canonical);
        foreach ((string name, string hash) in Dependencies(project)
            .OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            hashes.AddRange(Encoding.UTF8.GetBytes(name));
            hashes.Add(0);
            hashes.AddRange(Convert.FromHexString(hash));
        }
        return MapJson.Sha256(CollectionsMarshal.AsSpan(hashes));
    }

    public static string ComputeCanonicalProject(MapProject project)
        => MapJson.Sha256(CanonicalProject(project));

    private static byte[] CanonicalProject(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return MapJson.Canonicalize(
            JsonSerializer.SerializeToUtf8Bytes(project, MapJsonContext.Default.MapProject));
    }

    public static IReadOnlyList<(string Name, string Hash)> Dependencies(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var dependencies = new List<(string Name, string Hash)>();
        MapBundleReadResult? sourceBundle = project.SourcePath != null
            && MapBundle.Is(project.SourcePath) ? new MapBundleReader().Read(project.SourcePath) : null;
        if (project.Authoring != null)
        {
            foreach (string image in project.Authoring.Materials
                .Select(material => material.CustomImage)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                byte[] bytes;
                string name;
                if (sourceBundle != null)
                {
                    bytes = sourceBundle.ReadDeclaredFile(image);
                    name = MapBundlePath.Canonicalize(image);
                }
                else
                {
                    string fullPath = ResolveProjectFile(project, image, "custom material image");
                    bytes = File.ReadAllBytes(fullPath);
                    name = CanonicalDependencyName(project, fullPath);
                }
                dependencies.Add((name, MapJson.Sha256(bytes)));
            }
        }

        MapImport? import = project.Map.Import;
        if (import == null) return dependencies;
        if (import.BundlePath != null)
        {
            MapBundleReadResult bundle = sourceBundle ?? new MapBundleReader().Read(import.BundlePath);
            dependencies.AddRange(bundle.Manifest.Files
                .Where(file => file.Role is MapFileRole.Geometry or MapFileRole.Textures)
                .Select(file => (file.Path, file.Sha256.ToLowerInvariant())));
            return dependencies;
        }
        string? source = import.Resolve();
        if (source == null)
            throw MissingDependency(project, import.Source, "imported geometry", "MAP-DEP-001");
        dependencies.Add((CanonicalDependencyName(project, source),
            MapJson.Sha256(File.ReadAllBytes(source))));
        if (!string.IsNullOrEmpty(import.Textures))
        {
            string? textures = import.ResolveTextures();
            if (textures == null)
                throw MissingDependency(project, import.Textures, "texture data", "MAP-MAT-005");
            dependencies.Add((CanonicalDependencyName(project, textures),
                MapJson.Sha256(File.ReadAllBytes(textures))));
        }
        return dependencies;
    }

    private static string ResolveProjectFile(MapProject project, string path, string description)
    {
        string baseDirectory = project.Map.BaseDirectory
            ?? (project.SourcePath == null ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(project.SourcePath)!);
        string fullPath = Path.GetFullPath(path, baseDirectory);
        if (!File.Exists(fullPath)) throw MissingDependency(project, path, description, "MAP-MAT-005");
        return fullPath;
    }

    private static MapDependencyException MissingDependency(MapProject project, string path,
        string description, string code)
        => new($"{description} '{path}' is missing.",
            [new(code, MapDiagnosticSeverity.Error, $"{description} '{path}' is missing.",
                SourcePath: project.SourcePath)]);

    private static string CanonicalDependencyName(MapProject project, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? baseDirectory = project.Map.BaseDirectory;
        if (baseDirectory != null)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(baseDirectory), fullPath)
                .Replace('\\', '/');
            if (!relative.StartsWith("../", StringComparison.Ordinal) && relative != "..")
                return relative;
        }
        return Path.GetFileName(fullPath);
    }
}
