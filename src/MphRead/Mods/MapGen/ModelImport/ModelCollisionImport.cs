using System;
using MphRead.Formats.Collision;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

public static class ModelCollisionImport
{
    public static string? FindCompanion(string path)
    {
        string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path));
        return new[] { stem + "_collision.obj", stem + ".col.obj" }.FirstOrDefault(File.Exists);
    }

    public static ImportedModel Add(ImportedModel model, string path, ModelImportSettings settings, CancellationToken cancellation)
    {
        if (!Enum.IsDefined(settings.Collision)) throw new InvalidDataException("Unknown collision import mode.");
        if (settings.Collision is ModelCollisionMode.None or ModelCollisionMode.Visual) return model;
        var warnings = model.Warnings.ToList(); var meshes = model.Meshes.ToList();
        foreach (var mesh in meshes) mesh.Solid = false;
        var groups = new List<(string Name, IReadOnlyList<BuiltFace> Faces)>();
        if (settings.Collision == ModelCollisionMode.Companion)
        {
            string companion = FindCompanion(path) ?? throw new FileNotFoundException("No _collision.obj or .col.obj companion found.");
            if (File.GetAttributes(companion).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Companion collision cannot be a symbolic link.");
            // Validate source geometry and input budgets before the established collision reader handles surface semantics.
            new ObjModelImporter().Import(companion, settings, cancellation);
            byte[] bytes = File.ReadAllBytes(companion);
            var collision = CollisionObj.Read(bytes, Path.GetFileName(companion), settings.ZUp);
            var faces = new List<BuiltFace>();
            foreach (var face in collision.Faces)
            {
                var points = face.Points.Select(p => p * settings.Scale).ToArray();
                if (settings.FlipWinding) Array.Reverse(points);
                Vector3 normal = settings.FlipWinding ? -face.Normal : face.Normal;
                faces.Add(new BuiltFace(points, new Vector2[points.Length], normal, 0, 1) { Terrain = face.Terrain, Damaging = face.Damaging, Slipperiness = face.Slipperiness, ReflectBeams = face.ReflectBeams,
                    IgnorePlayers = face.IgnorePlayers, IgnoreBeams = face.IgnoreBeams, IgnoreScan = face.IgnoreScan });
            }
            groups.Add(("Companion collision", faces));
        }
        else foreach (var mesh in model.Meshes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (settings.Collision == ModelCollisionMode.BoundingBoxes)
            {
                var points = mesh.Vertices.Select(MapBuilder.ToVector).ToArray();
                Vector3 min = points.Aggregate(Vector3.ComponentMin), max = points.Aggregate(Vector3.ComponentMax);
                Vector3 size = max - min;
                if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                { warnings.Add(mesh.Label + ": zero-thickness bounding box skipped; choose visual collision for open surfaces."); continue; }
                groups.Add((mesh.Label + " collision", GeometryCompiler.Compile(new MapBox
                { Transform = new() { Position = new[] { (min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2 }, Scale = new[] { size.X, size.Y, size.Z } } }, 16)));
            }
            else groups.Add((mesh.Label + " collision", GeometryCompiler.Compile(mesh, 16)));
        }
        foreach (var group in groups)
        {
            cancellation.ThrowIfCancellationRequested();
            var editors = MapCollisionOptimizer.FromFaces(group.Faces);
            var optimized = MapCollisionOptimizer.Optimize(editors, settings.Collision == ModelCollisionMode.Simplified ? 0 : ushort.MaxValue - 1);
            // Keep differing collision behaviors as separate editable proxy meshes.
            foreach (var behavior in optimized.Editors.GroupBy(e => (e.Terrain, e.Damaging, e.Flags, e.Slipperiness)))
            {
                var mesh = new MapMesh { Label = group.Name + " " + behavior.Key.Terrain + " " + (ushort)behavior.Key.Flags,
                    CollisionOnly = true, Solid = true, Layer = "Collision", Terrain = behavior.Key.Terrain.ToString(), Damaging = behavior.Key.Damaging,
                    Slipperiness = behavior.Key.Slipperiness, ReflectBeams = behavior.Key.Flags.HasFlag(CollisionFlags.ReflectBeams),
                    IgnorePlayers = behavior.Key.Flags.HasFlag(CollisionFlags.IgnorePlayers), IgnoreBeams = behavior.Key.Flags.HasFlag(CollisionFlags.IgnoreBeams),
                    IgnoreScan = behavior.Key.Flags.HasFlag(CollisionFlags.IgnoreScan) };
                foreach (var editor in behavior)
                {
                    int start = mesh.Vertices.Count;
                    mesh.Vertices.AddRange(editor.Points.Select(p => new[] { p.X, p.Y, p.Z }));
                    // Runtime mesh faces have a 32-corner limit. Fan triangulation is safe for these convex optimizer outputs.
                    if (editor.Points.Count <= 32) AddFace(Enumerable.Range(start, editor.Points.Count).ToArray());
                    else for (int i = 1; i < editor.Points.Count - 1; i++) AddFace(new[] { start, start + i, start + i + 1 });
                }
                void AddFace(int[] face) { mesh.Faces.Add(face); mesh.FaceMaterials.Add(0); mesh.FaceTexcoords.Add(null); }
                meshes.Add(mesh);
            }
            warnings.Add($"{group.Name}: {optimized.OriginalFaces} → {optimized.OptimizedFaces} collision faces. Check one-sided winding and run collision health / Auto-Heal after placement.");
        }
        if (meshes.Where(m => m.Solid).Sum(m => m.Faces.Count) > 10000) warnings.Add("More than 10,000 collision faces; inspect collision budgets before publishing.");
        return model with { Meshes = meshes, Warnings = warnings };
    }
}
