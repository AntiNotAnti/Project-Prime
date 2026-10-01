using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;

internal static class ModelImportChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string path = Path.Combine(root, "model.obj");
        File.WriteAllText(Path.Combine(root,"model.mtl"), "newmtl red\nKd 1 0 0\n");
        string source = "mtllib model.mtl\no Floor\nv 0 0 0\nv 2 0 0\nv 2 2 0\nv 1 1 0\nv 0 2 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt .5 .5\nvt 0 1\nusemtl red\nf -5/-5 -4/-4 -3/-3 -2/-2 -1/-1\n";
        File.WriteAllText(path, source);
        var result = ModelImportService.Import(path, new()); var mesh = result.Meshes.Single();
        check(mesh.Faces.Count == 3 && !mesh.Solid, "concave OBJ triangulated with visual-only default");
        check(mesh.FaceTexcoords.All(uv => uv?.Length == 3), "OBJ face-corner UVs preserved");
        check(mesh.FaceMaterials.All(i => result.Materials[i].Name == "red"), "OBJ material mapping retained");
        check(result.Assets.Count == 1 && MapTexturePack.Load(result.Assets.Single().Value,"color").Entries.Count == 1, "MTL diffuse color baked");
        File.WriteAllText(path,"v 0 0 0\nv 2 0 0\nv 2 2 .35\nv 0 2 0\nf 1 2 3 4\n");
        var warped=ModelImportService.Import(path,new());
        check(warped.Meshes.Single().Faces.Count==2
            &&warped.Warnings.Any(w=>w.Contains("Non-planar OBJ polygons",StringComparison.Ordinal)),
            "non-planar OBJ quad auto-triangulates instead of rejecting the model");
        string longTexture="textures/model-"+new string('a',64)+".tex";
        check(MapPackageReader.CanonicalName(longTexture)==longTexture,
            "content-addressed model texture path is package-safe");
        File.WriteAllText(path,source);
        foreach (string bad in new[] { "f 0 1 2", "f 1 2 99", "mtllib ../secret.mtl", "mtllib /secret.mtl", "v NaN 0 0", "f 1/99 2/1 3/1" })
        {
            File.WriteAllText(path, source + bad + "\n"); bool rejected = false;
            try { ModelImportService.Import(path, new()); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "unsafe OBJ rejected: " + bad);
        }
        File.WriteAllText(path, source);
        var definition = new MapDefinition { Name = "MODEL_CHECK" };
        definition.Materials.AddRange(result.Materials); definition.Geometry.Add(mesh);
        var document = new MapDocument(new MapProject(definition));
        int previous = mesh.FaceMaterials[0];
        document.EditObjects("Paint faces", new[] { mesh.Id }, d => MapMeshEditing.AssignMaterial((MapMesh)d.Geometry.Single(), new[] { 0, 1 }, 0));
        check(((MapMesh)document.Project.Definition.Geometry.Single()).FaceMaterials.Take(2).All(i => i == 0), "multi-face assignment");
        document.History.Undo();
        check(((MapMesh)document.Project.Definition.Geometry.Single()).FaceMaterials[0] == previous, "face assignment undo");
        document.History.Redo();
        check(((MapMesh)document.Project.Definition.Geometry.Single()).FaceMaterials[0] == 0, "face assignment redo");
        var scene = MapViewportScene.Create(definition);
        check(scene.Faces.All(f => f.Texcoords?.Length == f.Points.Length), "viewport retains UVs");
        var edited = (MapMesh)document.Project.Definition.Geometry.Single();
        var originalUv = edited.FaceTexcoords[0]![0].ToArray();
        MapMeshEditing.TransformUv(edited, new[] { 0 }, new(2, 3), new(4, 5), 0, 16);
        check(edited.FaceTexcoords[0]![0][0] == originalUv[0] * 2 + 4 && edited.FaceTexcoords[0]![0][1] == originalUv[1] * 3 + 5,
            "explicit UV scale and offset");
        check(MapMeshEditing.ConnectedFaces(edited, 0).Length == 3, "connected paint visits adjacent triangles once");
        string beforeChecker = document.Project.Definition.Serialize();
        check(MapViewportMaterials.Checker.Width == 64 && document.Project.Definition.Serialize() == beforeChecker, "checker is presentation-only");
        File.WriteAllText(path, source + "o Other\nf 1 2 4\n");
        var grouped = ModelImportService.Import(path, new(VisualCollision: true));
        check(grouped.Meshes.Count == 2 && grouped.Meshes.All(m => m.Solid), "OBJ groups and explicit visual collision");
        File.WriteAllText(path, source + new string('x', 16385));
        bool longLine = false;
        try { ModelImportService.Import(path, new()); } catch (InvalidDataException) { longLine = true; }
        check(longLine, "OBJ line length limit");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var projected = ModelImportService.Import(path, new());
        check(projected.Meshes[0].FaceTexcoords[0] == null && projected.Warnings.Count > 0, "missing UVs retain projection with warning");
        Directory.CreateDirectory(Path.Combine(root, "textures"));
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jM1sAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(root, "textures", "tile.png"), png);
        File.WriteAllText(Path.Combine(root, "model.mtl"), "newmtl red\nmap_Kd -s 1 1 1 textures/TILE.png\nnewmtl other\nmap_Kd textures/TILE.png\n");
        File.WriteAllText(path, source + "usemtl other\nf 1/1 2/2 4/4\n");
        var textured = ModelImportService.Import(path, new());
        check(textured.Assets.Count == 1 && textured.Materials.Single(m => m.Name == "red").Texture == textured.Materials.Single(m => m.Name == "other").Texture,
            "PNG texture import resolves case-insensitive names and deduplicates source content");
        check(MapTexturePack.Load(textured.Assets.Single().Value, "image").Entries.Single().Width == 64,
            "source image bakes to runtime texture");
        File.WriteAllText(path, source);
        byte[] tga=TestTga();
        File.WriteAllBytes(Path.Combine(root,"textures","tile.tga"),tga);
        File.WriteAllText(Path.Combine(root,"model.mtl"),"newmtl red\nmap_Kd -o 0 0 0 textures/tile.tga\n");
        var tgaTextured=ModelImportService.Import(path,new());
        check(tgaTextured.Assets.Count==1&&MapTexturePack.Load(tgaTextured.Assets.Single().Value,"tga").Entries.Single().Width==64,
            "OBJ imports TGA diffuse textures while tolerating standard map_Kd options");
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 5000);
        File.WriteAllBytes(Path.Combine(root, "textures", "tile.png"), png);
        File.WriteAllText(Path.Combine(root, "model.mtl"), "newmtl red\nmap_Kd textures/tile.png\n");
        bool oversizedImage = false;
        try { ModelImportService.Import(path, new()); } catch (InvalidDataException) { oversizedImage = true; }
        check(oversizedImage, "oversized image rejected before native decode");
        string hex = new string('a',64); var hash = MapHash256.Parse(hex);
        Span<byte> bytes = stackalloc byte[32]; hash.Write(bytes);
        check(MapHash256.Read(bytes) == hash && hash.ToString() == hex, "binary hash round trip");
        check(!MapHash256.TryParse(new string('z',64), out _) && !MapHash256.TryParse("00", out _), "invalid hashes rejected");
        var identity = new MapContentIdentity(Guid.NewGuid(), "ROOM", hash, hash, true);
        check(!identity.Matches(identity with { MapId = Guid.NewGuid() }) && !identity.Matches(identity with { ContentHash = default })
            && !identity.Matches(identity with { PackageHash = default }) && !identity.Matches(MapContentIdentity.BuiltIn("ROOM")), "room key alone never establishes identity");
    }

    private static byte[] TestTga()
    {
        using var stream=new MemoryStream();
        using var writer=new BinaryWriter(stream);
        writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)2);
        writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((byte)0);
        writer.Write((ushort)0);writer.Write((ushort)0);
        writer.Write((ushort)2);writer.Write((ushort)2);
        writer.Write((byte)24);writer.Write((byte)0x20);
        for(int i=0;i<4;i++){writer.Write((byte)16);writer.Write((byte)32);writer.Write((byte)240);}
        return stream.ToArray();
    }
}
