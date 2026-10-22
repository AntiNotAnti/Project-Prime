using System;
using System.IO;
using System.Linq;
using System.Text;
using MphRead;
using MphRead.Formats.Collision;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

static class TraversalSurfaceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var definition = new MapDefinition { Name = "SURF AUTHORING CHECK", FormatVersion = 2, MapId = Guid.NewGuid() };
        var wedge = new MapWedge { SurfSurface = true };
        definition.Geometry.Add(wedge);
        definition.Materials.Add(new MapMaterial());
        check(!new MapDefinition().TraversalTraining && !new MapBox().SurfSurface,
            "ordinary maps and geometry do not opt into surf surfaces");
        check(MapValidator.Validate(definition, checkSources: false).Diagnostics.Any(d =>
                d.ObjectId == wedge.Id && d.Message.Contains("traversal-training")),
            "source validation rejects tagged geometry without training opt-in");
        definition.TraversalTraining = true;
        check(!MapValidator.Validate(definition, checkSources: false).Diagnostics.Any(d =>
                d.Message.Contains("traversal-training")),
            "source validation accepts explicit training opt-in");
        var copy = MapProjectSerializer.Clone(definition);
        check(copy.TraversalTraining && copy.Geometry.Single().SurfSurface,
            "project snapshots preserve training opt-in and surface tags");
        check(definition.Serialize().Contains("\"traversalTraining\": true")
            && definition.Serialize().Contains("\"surfSurface\": true"),
            "source JSON carries explicit authoring settings");
        var faces = GeometryCompiler.Compile(wedge, 16);
        check(faces.Count > 0 && faces.All(face => face.SurfSurface),
            "geometry compiler preserves the authored surf tag");
        var mesh = MapMeshEditing.Convert(wedge, 16);
        check(mesh.SurfSurface && MapMeshEditing.Clone(mesh).SurfSurface,
            "mesh conversion and duplication preserve the surf tag");
        var plainMesh = MapMeshEditing.Clone(mesh); plainMesh.SurfSurface = false;
        bool refusedJoin = false;
        try { MapMeshEditing.Join(new[] { mesh, plainMesh }); }
        catch (InvalidOperationException) { refusedJoin = true; }
        check(refusedJoin, "joining tagged and untagged meshes cannot erase surface behavior");

        BuiltFace Floor(float x, bool surf) => new(
            new[] { new Vector3(x, 0, 0), new Vector3(x, 0, 4),
                new Vector3(x + 4, 0, 4), new Vector3(x + 4, 0, 0) },
            new Vector2[4], Vector3.UnitY, 0, 1) { SurfSurface = surf };
        var editors = MapCollisionOptimizer.FromFaces(new[] { Floor(0, true), Floor(4, false) });
        check(editors[0].SurfSurface && !editors[1].SurfSurface
            && editors[0].Flags.HasFlag(CollisionFlags.TraversalSurfSurface),
            "compiled collision editors encode only tagged faces");
        var optimized = MapCollisionOptimizer.Optimize(editors, 0);
        check(optimized.Editors.Count == 2 && optimized.Editors.Count(e => e.SurfSurface) == 1,
            "collision optimization preserves boundaries between tagged and ordinary faces");
        byte[] bytes = MapCollisionPacker.Pack(optimized.Editors);
        var header = Read.ReadStruct<CollisionHeader>(bytes);
        var packed = Collision.ReadMphCollision(header, bytes, -1);
        check(packed.Data.Count == 2 && packed.Data.Count(face =>
                face.Flags.HasFlag(CollisionFlags.TraversalSurfSurface)) == 1,
            "runtime collision packing preserves the surface tag without tagging neighbors");
        var compiled = new BuiltMap(new MapDefinition());
        compiled.Solid.Add(Floor(0, true));
        var invalid = new MapValidationResult(); MapBudgetValidator.Analyze(compiled, invalid);
        check(invalid.Diagnostics.Any(d => d.Message.Contains("traversal-training")),
            "compiled imported tags also require training opt-in");

        const string obj = "usemtl metal_surf_slip2\nv 0 0 0\nv 0 0 4\nv 4 0 4\nv 4 0 0\nf 1 2 3 4\n";
        var imported = CollisionObj.Read(Encoding.UTF8.GetBytes(obj), "surf.obj", zUp: false).Faces.Single();
        check(imported.SurfSurface && imported.Slipperiness == 2
            && CollisionObj.MaterialName(imported) == "metal_surf_slip2",
            "authored collision OBJ imports and exports surf separately from slipperiness");

        var training = MapDefinition.Load(TrainingMapPath());
        var validation = MapValidator.Validate(training, checkSources: false);
        check(validation.IsValid, "training map passes authored validation: "
            + string.Join("; ", validation.Diagnostics.Where(d => d.Severity == MapDiagnosticSeverity.Error)
                .Select(d => d.Code + " " + d.Message)));
        check(training.Spawns.Count == 8 && !validation.Diagnostics.Any(d => d.Code == "FP-MAP-002"),
            "all training spawns have valid positions and headroom outside bounded slope solids");
        var taggedSlope = training.Geometry.Single(g => g.Label == "Tagged surf descent");
        var controlSlope = training.Geometry.Single(g => g.Label == "Untagged descent control");
        var taggedFaces = GeometryCompiler.Compile(taggedSlope, 4);
        var controlFaces = GeometryCompiler.Compile(controlSlope, 4);
        var taggedPlane = taggedFaces.Single(f => f.Normal.Y is > .05f and < .8f);
        var controlPlane = controlFaces.Single(f => f.Normal.Y is > .05f and < .8f);
        check(taggedFaces.Count == 5 && controlFaces.Count == 5
            && taggedSlope.SurfSurface && !controlSlope.SurfSurface
            && taggedPlane.Normal == controlPlane.Normal
            && taggedPlane.Points.Select(p => p.Yz).SequenceEqual(controlPlane.Points.Select(p => p.Yz)),
            "bounded tagged and control wedges retain identical descent planes");
    }

    private static string TrainingMapPath()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "maps", "traversal-training", "map.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("The authored traversal-training map is required for this check.");
    }
}
