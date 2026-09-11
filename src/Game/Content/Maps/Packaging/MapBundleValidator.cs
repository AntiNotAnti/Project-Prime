using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public sealed class MapBundleValidator
{
    private readonly MapBundleReader _reader;

    public MapBundleValidator(MapBundleReadOptions? options = null)
        => _reader = new MapBundleReader(options);

    public MapBundleValidationResult Validate(string bundlePath)
    {
        try
        {
            MapBundleReadResult bundle = _reader.Read(bundlePath);
            MapDiagnostic packageDiagnostic = bundle.IsLegacy
                ? new("MAP-PKG-001", MapDiagnosticSeverity.Warning,
                    "Legacy .fpmap v1 bundle accepted through the migration adapter.", SourcePath: bundlePath,
                    SuggestedAction: "Re-export the map as .fpmap v2.")
                : new("MAP-PKG-000", MapDiagnosticSeverity.Info,
                    "Map bundle is valid.", SourcePath: bundlePath);
            // The explicit reader establishes that this is a package. Incoming
            // downloads intentionally use temporary suffixes such as
            // ".fpmap.partial", so semantic validation must not rediscover the
            // content type from the filename.
            MapProject project = MapProjectIO.LoadBundle(bundlePath, bundle);
            var diagnostics = new List<MapDiagnostic> { packageDiagnostic };
            diagnostics.AddRange(new MapValidator().ValidateProject(project));
            if (!bundle.IsLegacy)
            {
                ValidateManifestProject(bundlePath, bundle, project, diagnostics);
                ValidateReferences(bundlePath, bundle, project, diagnostics);
            }
            bool valid = diagnostics.All(value => value.Severity != MapDiagnosticSeverity.Error);
            return new MapBundleValidationResult(valid, valid ? bundle : null, [.. diagnostics]);
        }
        catch (MapPackageException exception)
        {
            return new MapBundleValidationResult(false, null,
                [new MapDiagnostic(exception.Code, MapDiagnosticSeverity.Error, exception.Message,
                    SourcePath: bundlePath)]);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
            or FormatException or ArgumentException or InvalidOperationException
            or System.IO.IOException)
        {
            return new MapBundleValidationResult(false, null,
                [new MapDiagnostic("MAP-PKG-012", MapDiagnosticSeverity.Error,
                    exception.Message, SourcePath: bundlePath)]);
        }
    }

    private static void ValidateManifestProject(string bundlePath, MapBundleReadResult bundle,
        MapProject project, List<MapDiagnostic> diagnostics)
    {
        MapManifest manifest = bundle.Manifest;
        bool modesMatch = manifest.SupportedModes.OrderBy(value => value)
            .SequenceEqual(project.SupportedModes.OrderBy(value => value));
        if (project.StableId != manifest.StableId || project.Version != manifest.Version
            || project.Metadata.Name != manifest.Name || project.Metadata.Author != manifest.Author
            || project.Metadata.Description != manifest.Description
            || project.Metadata.Redistribution != manifest.Redistribution || !modesMatch)
            diagnostics.Add(new("MAP-PKG-009", MapDiagnosticSeverity.Error,
                "Map recipe identity and metadata do not match manifest.json.",
                SourcePath: bundlePath,
                SuggestedAction: "Re-export the package from its authoritative map project."));
        if (project.PreviewImage != manifest.Preview)
            diagnostics.Add(new("MAP-PKG-006", MapDiagnosticSeverity.Error,
                "Map recipe preview does not match the manifest preview path.",
                SourcePath: bundlePath));
    }

    private static void ValidateReferences(string bundlePath, MapBundleReadResult bundle,
        MapProject project, List<MapDiagnostic> diagnostics)
    {
        IReadOnlyDictionary<string, MapManifestFile> declared = bundle.Manifest.Files
            .ToDictionary(value => value.Path, StringComparer.Ordinal);
        Check(project.Map.Import?.Source, MapFileRole.Geometry, "geometry");
        Check(project.Map.Import?.Textures, MapFileRole.Textures, "texture pack");
        foreach (MapAuthoringMaterial material in project.Authoring?.Materials ?? [])
            Check(material.CustomImage, MapFileRole.Textures,
                $"material '{material.Id}' image");
        Check(project.PreviewImage, MapFileRole.Preview, "preview");

        void Check(string? path, MapFileRole role, string description)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string canonical;
            try { canonical = MapBundlePath.Canonicalize(path); }
            catch (MapPackageException exception)
            {
                diagnostics.Add(new(exception.Code, MapDiagnosticSeverity.Error,
                    $"Invalid {description} path: {exception.Message}", SourcePath: bundlePath));
                return;
            }
            if (!declared.TryGetValue(canonical, out MapManifestFile? file) || file.Role != role)
                diagnostics.Add(new("MAP-PKG-006", MapDiagnosticSeverity.Error,
                    $"The recipe {description} '{canonical}' is not declared with role {role}.",
                    SourcePath: bundlePath));
        }
    }
}

public sealed record MapBundleValidationResult(
    bool IsValid,
    MapBundleReadResult? Bundle,
    ImmutableArray<MapDiagnostic> Diagnostics);
