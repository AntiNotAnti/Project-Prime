using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.MapGen;

// Real package-builder/strict-reader acceptance for the PR-13 authoring
// additions. Everything here is original synthetic data, no game assets.
string root = Path.Combine(Path.GetTempPath(),
    "prime-radiant-package-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool ok, string label)
{
    if (!ok) throw new InvalidDataException("FAIL " + label);
    Console.WriteLine("PASS " + label);
    passed++;
}
try
{
    Headless.Enter();
    Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
    Directory.CreateDirectory(Paths.FileSystem);

    // The standard content-free fixture used elsewhere in the Community
    // checks: a fully synthetic 8x8 FPTX texture, never commercial assets.
    using (var tex = new BinaryWriter(File.Create(Path.Combine(root,"tile.tex"))))
    {
        tex.Write(Encoding.ASCII.GetBytes("FPTX"));
        tex.Write((ushort)1); tex.Write((ushort)1); tex.Write((ushort)0);
        tex.Write((ushort)8); tex.Write((ushort)8); tex.Write((ushort)1);
        tex.Write((ushort)0); tex.Write((ushort)32767); tex.Write(new byte[64]);
    }
    Guid mapId = Guid.Parse("8825a0ea-7253-41ea-a3c4-671d993f61a4");
    Guid patchId = Guid.Parse("143647b2-ea17-4bd7-9224-aa6ed51322b1");
    Guid boxId = Guid.Parse("c4a6dc93-0bb7-4b1f-9ec9-c22cd122d59f");
    var source = new MapDefinition
    {
        FormatVersion=2,MapId=mapId,Name="QUAKE_COMPAT",
        InGameName="Quake compatibility fixture",Version="1",
        Author="Project Prime CI",BaseDirectory=root
    };
    source.Materials.Add(new MapMaterial
    {
        Id = Guid.Parse("117621ca-a62d-4d77-bb4c-40be5c7a29cc"),
        Name="textures/arena/curve",Texture="tile.tex",TexScale=16f
    });
    source.Materials.Add(new MapMaterial
    {
        Id = Guid.Parse("268e12da-bf82-4d2c-abef-00dcdd2d6ed2"),
        Name="textures/arena/trim",Texture="tile.tex",TexScale=16f
    });
    source.Assets.Add(new MapAsset {Path="tile.tex",Kind="texture"});
    // One connected convex floor with two Quake face materials and explicit
    // source projection. The historical geometry compiler must produce the
    // same polygon/material/UV geometry from the packaged MapMesh.
    var floor = new MapBox
    {
        Id=boxId,Material=0,
        Transform=new MapTransform {
            Position=new[]{0f,-1f,0f},Scale=new[]{12f,1f,12f}
        },
        QuakeFaces=Enumerable.Range(0,6).Select(i=>new MapQuakeFace
        {
            Material=i==1?1:0,ShiftU=i==1?4f:0f,
            ShiftV=0,Rotation=0,ScaleU=1,ScaleV=1,
            UnitsPerPrimeUnit=16f
        }).ToList()
    };
    source.Geometry.Add(floor);
    var patch = new MapBezierPatch
    {
        Id=patchId,Material=0,Solid=false,
        Columns=3,Rows=3,Tessellation=2,
        ControlPoints=new List<float[]>()
    };
    for(int col=0;col<3;col++)
        for(int row=0;row<3;row++)
            patch.ControlPoints.Add(new[]{
                (float)(col-1), 2f+(col==1 && row==1?0.6f:0f),
                (float)(row-1), col/2f,row/2f
            });
    source.Geometry.Add(patch);
    source.Spawns.Add(new MapSpawn
    {
        Id=Guid.Parse("219dc3d7-9186-4284-af8c-c84b8a537cc4"),
        Position=new[]{-3f,1f,0f}
    });
    source.Spawns.Add(new MapSpawn
    {
        Id=Guid.Parse("a12abc9c-f9dd-4a02-a1e5-b6d0e2a412fe"),
        Position=new[]{3f,1f,0f}
    });

    string authored = source.Serialize();
    string authoredHash = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(authored)));
    string first = Path.Combine(root,"first.ppmap");
    string second = Path.Combine(root,"second.ppmap");

    await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(source),first);
    await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(source),second);
    string firstSha = MapBuildFingerprint.HashFile(first);
    Check(firstSha==MapBuildFingerprint.HashFile(second)
        && File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)),
        "normal scheduler produces deterministic exact-byte .ppmap for new editable sources");

    using(var reader = new MapPackageReader(first))
    {
        MapDefinition packaged = JsonSerializer.Deserialize<MapDefinition>(
            reader.ReadProject(),MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("No packaged project.");
        Check(reader.Manifest?.MapId == mapId
            && packaged.MapId == mapId && packaged.Name == source.Name,
            "strict reader retains original immutable MapId and room metadata");
        Check(packaged.Geometry.Count == 2
            && packaged.Geometry.All(x=>x is MapMesh)
            && packaged.Geometry.All(x=>x.QuakeFaces==null)
            && !reader.ReadProject().Contains("bezier-patch",StringComparison.Ordinal)
            && !reader.ReadProject().Contains("quakeFaces",StringComparison.OrdinalIgnoreCase),
            "distributed .ppmap contains only existing legacy MapMesh representation");
        MapMesh meshBox = (MapMesh)packaged.Geometry.Single(x=>x.Id==boxId);
        MapMesh meshPatch = (MapMesh)packaged.Geometry.Single(x=>x.Id==patchId);
        Check(meshBox.Faces.Count==6 && meshBox.Vertices.Count==8
            && meshBox.FaceMaterials.Distinct().Count()==2
            && meshBox.FaceTexcoords.Count==6,
            "packaged box retains shared vertices, exact face materials and explicit UVs");
        Check(meshPatch.Faces.Count==8
            && meshPatch.FaceTexcoords.Count==8 && !meshPatch.Solid
            && meshPatch.Vertices.Count<24,
            "packaged Bezier contains tessellated render-only triangles with shared topology");
        foreach(var original in source.Geometry)
        {
            var received=packaged.Geometry.Single(x=>x.Id==original.Id);
            var expectedFaces=GeometryCompiler.Compile(original,
                source.Materials[original.Material].TexScale);
            var outputFaces=GeometryCompiler.Compile(received,
                packaged.Materials[received.Material].TexScale);
            Check(expectedFaces.Count==outputFaces.Count
                && expectedFaces.Zip(outputFaces,(a,b)=>a.Material==b.Material
                    && a.Points.Length==b.Points.Length
                    && a.Points.Zip(b.Points,(x,y)=>(x-y).LengthSquared<1e-8f).All(v=>v)
                    && a.Texcoords.Zip(b.Texcoords,
                        (x,y)=>(x-y).LengthSquared<1e-8f).All(v=>v)).All(v=>v),
                "strict package readback preserves native geometry, UV and materials: "+original.Label);
        }
    }
    var identity=MapContentIdentity.FromPackage(first);
    Check(identity.MapId==mapId && identity.PackageHash.ToString()==firstSha,
        "package SHA-256 remains the exact immutable Community/map identity");

    // A failed next build must never replace an already verified archive.
    MapDefinition invalid=MapProjectSerializer.Clone(source);
    ((MapBezierPatch)invalid.Geometry.Single(x=>x.Id==patchId))
        .ControlPoints[4][0]=float.NaN;
    bool rejected=false;
    try { await MapBuildScheduler.Shared.PackageAsync(
        MapBuildSnapshot.Capture(invalid),first); }
    catch(Exception ex) when (ex is IOException or InvalidOperationException
        or ArgumentException or MapAuthoringException) { rejected=true; }
    Check(rejected && MapBuildFingerprint.HashFile(first)==firstSha,
        "invalid patch source fails closed without replacing an installed exact archive");
    Check(Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(source.Serialize())))==authoredHash,
        "packaging and failed recompile leave the editable authoring master untouched");

    Console.WriteLine("Real package acceptance: "+passed+" PASS; SHA256 "+firstSha);
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch(IOException){}
}
