using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace MphRead.Mods.MapGen;

public enum ModelCollisionMode { None, Visual, Simplified, BoundingBoxes, Companion }
public sealed record ModelImportSettings(float Scale = 1, bool FlipWinding = false, bool VisualCollision = false,
    bool ZUp = false, bool FlipUvVertical = true, ModelCollisionMode Collision = ModelCollisionMode.None);
public sealed record ImportedModel(IReadOnlyList<MapMesh> Meshes, IReadOnlyList<MapMaterial> Materials,
    IReadOnlyDictionary<string, byte[]> Assets, IReadOnlyList<string> Warnings)
{
    public IReadOnlyDictionary<string,string> AssetSources { get; init; } = new Dictionary<string,string>();
}
public interface IModelImporter
{
    bool CanImport(string extension);
    ImportedModel Import(string path, ModelImportSettings settings, CancellationToken cancellation = default);
}
public static class ModelImportService
{
    private static readonly IModelImporter[] Importers = { new ObjModelImporter() };
    public static ImportedModel Import(string path, ModelImportSettings settings, CancellationToken cancellation = default)
    {
        var importer = Importers.FirstOrDefault(i => i.CanImport(Path.GetExtension(path)))
            ?? throw new InvalidDataException("Unsupported model format.");
        var model = importer.Import(path, settings with { VisualCollision = settings.VisualCollision || settings.Collision == ModelCollisionMode.Visual }, cancellation);
        return ModelCollisionImport.Add(model, path, settings, cancellation);
    }
}
