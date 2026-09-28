using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;

internal static class ModelCollisionChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string path = Path.Combine(root, "collision-model.obj");
        File.WriteAllText(path, "o Floor\nv 0 0 0\nv 2 0 0\nv 2 2 0\nv 0 2 0\nf 1 2 3\nf 1 3 4\n");
        var converted = ModelImportService.Import(path, new(ZUp: true, FlipUvVertical: false));
        check(converted.Meshes[0].Vertices.Any(v => v[2] == -2), "Z-up conversion preserves handedness");
        var simplified = ModelImportService.Import(path, new(Collision: ModelCollisionMode.Simplified));
        var proxy = simplified.Meshes.Single(m => m.CollisionOnly);
        check(proxy.Faces.Count == 1 && !simplified.Meshes[0].Solid, "lossless collision simplification merges coplanar triangles into a proxy");
        var definition = new MapDefinition { Spawns = new() { new() } }; definition.Materials.AddRange(simplified.Materials); definition.Geometry.AddRange(simplified.Meshes);
        var built = MapBuilder.Build(definition);
        check(built.Faces.Count == 2 && built.Solid.Count == 1, "collision proxy omitted from visuals and retained in runtime collision");
        File.WriteAllText(Path.Combine(root, "collision-model_collision.obj"), "v 0 0 0\nv 2 0 0\nv 0 2 0\nf 1 2 3\n");
        var companion = ModelImportService.Import(path, new(Scale: 2, Collision: ModelCollisionMode.Companion));
        check(companion.Meshes.Single(m => m.CollisionOnly).Vertices.Any(v => v[0] == 4), "companion collision uses selected import scale");
        File.Delete(Path.Combine(root, "collision-model_collision.obj"));
        bool missing = false;
        try { ModelImportService.Import(path, new(Collision: ModelCollisionMode.Companion)); } catch (FileNotFoundException) { missing = true; }
        check(missing, "missing companion fails without mutating the document");
        var flat = ModelImportService.Import(path, new(Collision: ModelCollisionMode.BoundingBoxes));
        check(!flat.Meshes.Any(m => m.CollisionOnly) && flat.Warnings.Any(w => w.Contains("zero-thickness")), "flat bounding boxes skipped with diagnostic");
        File.AppendAllText(path, "v 0 0 1\nf 1 5 2\n");
        var box = ModelImportService.Import(path, new(Collision: ModelCollisionMode.BoundingBoxes));
        check(box.Meshes.Single(m => m.CollisionOnly).Faces.Count == 6, "bounding box produces six collision faces");
    }
}
