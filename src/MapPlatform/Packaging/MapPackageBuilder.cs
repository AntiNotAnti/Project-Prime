using System.Text.Json;

namespace MphRead.Mods.MapGen;

/// <summary>Creates a portable v2 source package without writing into base content.</summary>
public static class MapPackageBuilder
{
    public static MapBundleWriteResult Cook(MapProject project, string projectPath,
        string destinationPath, bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        MapDiagnostic[] invalid = new MapValidator().ValidateProject(project)
            .Where(value => value.Severity == MapDiagnosticSeverity.Error).ToArray();
        if (invalid.Length != 0)
            throw new MapValidationException("Map source must pass validation before packaging.", invalid);
        MapProject inside = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(project, MapJsonContext.Default.MapProject),
            MapJsonContext.Default.MapProject)
            ?? throw new MapPackageException("MAP-PKG-012", "Could not copy the map project.");
        MapImport? import = inside.Map.Import;
        var files = new List<MapPackageFile>();
        if (inside.Authoring != null)
        {
            string baseDirectory = project.Map.BaseDirectory
                ?? Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            int assetIndex = 0;
            foreach (MapAuthoringMaterial material in inside.Authoring.Materials
                .Where(value => !string.IsNullOrWhiteSpace(value.CustomImage))
                .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                MapAuthoringMaterial sourceMaterial = project.Authoring!.Materials
                    .Single(value => value.Id == material.Id);
                string sourcePath = Path.GetFullPath(sourceMaterial.CustomImage!, baseDirectory);
                if (!File.Exists(sourcePath))
                    throw new MapDependencyException($"Custom material image '{sourceMaterial.CustomImage}' is missing.");
                string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                string packagePath = $"textures/{assetIndex++:000}-{SafeName(material.Id)}{extension}";
                files.Add(new(packagePath, MapFileRole.Textures, File.ReadAllBytes(sourcePath)));
                material.CustomImage = packagePath;
            }
        }
        if (import != null)
        {
            string source = project.Map.Import?.Resolve()
                ?? throw new MapDependencyException($"Imported geometry '{import.Source}' is missing.");
            string mapName = import.MapName ?? Path.GetFileNameWithoutExtension(source);
            byte[] geometry = Q3Bsp.Trim(Q3Bsp.ReadLevel(source, import.MapName));
            string geometryPath = $"geometry/{mapName}.bsp";
            files.Add(new(geometryPath, MapFileRole.Geometry, geometry));
            import.Source = geometryPath;
            import.MapName = mapName;

            string? textureSource = project.Map.Import?.ResolveTextures();
            if (textureSource == null && !string.IsNullOrWhiteSpace(import.Textures))
                textureSource = Q3Import.BakeTextures(Q3Bsp.Load(source, project.Map.Import?.MapName),
                    project.Map.Import!, verbose);
            if (textureSource != null)
            {
                string texturePath = "textures/" + Path.GetFileName(textureSource);
                files.Add(new(texturePath, MapFileRole.Textures,
                    File.ReadAllBytes(textureSource)));
                import.Textures = texturePath;
            }
            else if (!string.IsNullOrWhiteSpace(import.Textures))
                throw new MapDependencyException($"Texture data '{import.Textures}' is missing.");
        }

        string? previewPath = null;
        if (!string.IsNullOrWhiteSpace(project.PreviewImage))
        {
            string baseDirectory = project.Map.BaseDirectory
                ?? Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            string sourcePreview = Path.GetFullPath(project.PreviewImage, baseDirectory);
            if (!File.Exists(sourcePreview))
                throw new MapDependencyException($"Preview image '{project.PreviewImage}' is missing.");
            previewPath = "preview.png";
            files.Add(new(previewPath, MapFileRole.Preview, File.ReadAllBytes(sourcePreview)));
            inside.PreviewImage = previewPath;
        }

        const string recipePath = "map.json";
        files.Add(new(recipePath, MapFileRole.Recipe,
            JsonSerializer.SerializeToUtf8Bytes(inside, MapJsonContext.Default.MapProject)));
        var manifest = new MapManifest
        {
            StableId = inside.StableId,
            Version = inside.Version,
            Name = inside.Metadata.Name,
            Author = inside.Metadata.Author,
            Description = inside.Metadata.Description,
            SupportedModes = [.. inside.SupportedModes],
            Redistribution = inside.Metadata.Redistribution,
            Recipe = recipePath,
            Preview = previewPath
        };
        string destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".cook-" + Guid.NewGuid().ToString("N") + MapBundle.Extension;
        try
        {
            MapBundleWriteResult result = new MapBundleWriter().WriteAsync(temporary, manifest, files,
                cancellationToken).GetAwaiter().GetResult();
            MapBundleValidationResult validation = new MapBundleValidator(
                new MapBundleReadOptions { AllowLegacyV1 = false }).Validate(result.Path);
            if (!validation.IsValid)
                throw new MapValidationException("Cooked map package failed final verification: "
                    + string.Join("; ", validation.Diagnostics
                        .Where(value => value.Severity == MapDiagnosticSeverity.Error)
                        .Select(value => $"{value.Code}: {value.Message}")),
                    validation.Diagnostics);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            temporary = "";
            return result with { Path = destination };
        }
        finally
        {
            if (temporary.Length != 0 && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SafeName(string value)
        => new(value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
            ? char.ToLowerInvariant(character) : '-').ToArray());
}
