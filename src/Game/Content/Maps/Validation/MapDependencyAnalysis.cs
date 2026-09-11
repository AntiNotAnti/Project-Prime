using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

public enum MapDependencyKind
{
    BaseContent,
    ImportedGeometry,
    ImportedTexturePack,
    CustomTexture,
    PreviewImage
}

public sealed record MapDependency(
    MapDependencyKind Kind,
    string LogicalName,
    string? ResolvedPath,
    MapFileRole? ExpectedRole,
    string? Hash,
    bool Required,
    bool AffectsBuild);

public sealed record MapDependencyAnalysis(
    bool RequiresBaseContent,
    bool RequiresImportedGeometry,
    bool RequiresExternalTextures,
    ImmutableArray<MapDependency> Dependencies,
    ImmutableArray<MapDiagnostic> Diagnostics);

/// <summary>
/// The sole authority for deciding which external inputs a map build needs.
/// Consumers may present the diagnostics differently, but must not recreate
/// base-content or source-file rules independently.
/// </summary>
public static class MapDependencyAnalyzer
{
    public static MapDependencyAnalysis Analyze(MapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var dependencies = ImmutableArray.CreateBuilder<MapDependency>();
        var diagnostics = new MapDiagnosticBag();
        MapBundleReadResult? bundle = TryReadSourceBundle(project, diagnostics);
        bool requiresBase = false;
        bool requiresImportedGeometry = project.Map.Import != null;
        bool requiresExternalTextures = false;

        if (project.Authoring != null)
        {
            foreach (MapAuthoringMaterial material in project.Authoring.Materials
                .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                if (material.SourceMaterial.HasValue)
                {
                    requiresBase = true;
                    dependencies.Add(new(MapDependencyKind.BaseContent,
                        material.SourceRoom ?? project.Map.TextureSource, null, null, null,
                        Required: true, AffectsBuild: true));
                }
                if (string.IsNullOrWhiteSpace(material.CustomImage)) continue;
                requiresExternalTextures = true;
                AddFile(project, bundle, material.CustomImage!, MapDependencyKind.CustomTexture,
                    MapFileRole.Textures, "MAP-DEP-004", "custom material image", dependencies,
                    diagnostics, affectsBuild: true);
            }
        }

        MapImport? import = project.Map.Import;
        if (import != null)
        {
            if (string.IsNullOrWhiteSpace(import.Source))
                diagnostics.Add(new("MAP-DEP-001", MapDiagnosticSeverity.Error,
                    "Imported geometry source is required.", SourcePath: project.SourcePath));
            else
                AddFile(project, bundle, import.Source, MapDependencyKind.ImportedGeometry,
                    MapFileRole.Geometry, "MAP-DEP-002", "imported geometry source", dependencies,
                    diagnostics, affectsBuild: true);

            if (string.IsNullOrWhiteSpace(import.Textures))
            {
                requiresBase = true;
                dependencies.Add(new(MapDependencyKind.BaseContent, project.Map.TextureSource,
                    null, null, null, Required: true, AffectsBuild: true));
            }
            else
            {
                requiresExternalTextures = true;
                AddFile(project, bundle, import.Textures, MapDependencyKind.ImportedTexturePack,
                    MapFileRole.Textures, "MAP-DEP-003", "imported texture pack", dependencies,
                    diagnostics, affectsBuild: true);
            }
        }
        else if (project.Authoring == null)
        {
            // Legacy native brush recipes always borrow cartridge materials.
            requiresBase = true;
            dependencies.Add(new(MapDependencyKind.BaseContent, project.Map.TextureSource,
                null, null, null, Required: true, AffectsBuild: true));
        }

        if (!string.IsNullOrWhiteSpace(project.PreviewImage))
            AddFile(project, bundle, project.PreviewImage!, MapDependencyKind.PreviewImage,
                MapFileRole.Preview, "MAP-DEP-005", "preview image", dependencies,
                diagnostics, affectsBuild: false);

        return new(requiresBase, requiresImportedGeometry, requiresExternalTextures,
            dependencies.ToImmutable(), diagnostics.ToImmutable());
    }

    private static MapBundleReadResult? TryReadSourceBundle(MapProject project,
        MapDiagnosticBag diagnostics)
    {
        if (project.SourcePath == null || !MapBundle.Is(project.SourcePath)) return null;
        try { return new MapBundleReader().Read(project.SourcePath); }
        catch (Exception exception) when (exception is MapPackageException or IOException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(new("MAP-DEP-006", MapDiagnosticSeverity.Error,
                exception.Message, SourcePath: project.SourcePath));
            return null;
        }
    }

    private static void AddFile(MapProject project, MapBundleReadResult? bundle, string logicalName,
        MapDependencyKind kind, MapFileRole role, string code, string description,
        ImmutableArray<MapDependency>.Builder dependencies, MapDiagnosticBag diagnostics,
        bool affectsBuild)
    {
        try
        {
            if (bundle != null)
            {
                string canonical = MapBundlePath.Canonicalize(logicalName);
                MapManifestFile? file = bundle.Manifest.Files.FirstOrDefault(value =>
                    value.Path.Equals(canonical, StringComparison.Ordinal));
                if (file == null || file.Role != role)
                    throw new FileNotFoundException($"Bundle does not declare {description} '{logicalName}'.");
                dependencies.Add(new(kind, canonical, project.SourcePath + "::" + canonical,
                    role, file.Sha256.ToLowerInvariant(), Required: true, affectsBuild));
                return;
            }

            string baseDirectory = project.Map.BaseDirectory
                ?? (project.SourcePath == null ? Directory.GetCurrentDirectory()
                    : Path.GetDirectoryName(project.SourcePath)!);
            string fullPath = Path.GetFullPath(logicalName, baseDirectory);
            if (!File.Exists(fullPath)) throw new FileNotFoundException();
            dependencies.Add(new(kind, CanonicalDependencyName(project, fullPath), fullPath,
                role, MapJson.Sha256(File.ReadAllBytes(fullPath)), Required: true, affectsBuild));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or MapPackageException)
        {
            diagnostics.Add(new(code, MapDiagnosticSeverity.Error,
                $"{description} '{logicalName}' is missing or unreadable.",
                SourcePath: project.SourcePath,
                SuggestedAction: exception.Message.Length == 0 ? null : exception.Message));
        }
    }

    private static string CanonicalDependencyName(MapProject project, string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (project.Map.BaseDirectory is { } baseDirectory)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(baseDirectory), fullPath)
                .Replace('\\', '/');
            if (!relative.StartsWith("../", StringComparison.Ordinal) && relative != "..")
                return relative;
        }
        return Path.GetFileName(fullPath);
    }
}
