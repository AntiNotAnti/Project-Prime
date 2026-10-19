using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.Import;

// A bounded seven-pillar display slice. The actual disc/PAK owner creates the
// source receipt; Append rechecks every mutable model/transform against it.
public static class EchoesStaticVisualLayer
{
    public sealed record Recipe(uint Actor, uint Model);
    public sealed record Placement(uint Actor, uint Model, float[] Transform, Cmdl Geometry);
    public sealed record TextureSize(uint Resource, int Width, int Height);
    public static IReadOnlyList<Recipe> Spires { get; } = Array.AsReadOnly<Recipe>([
        new(0x1a1,0xd2bf0486),new(0x29b,0x5ff3725a),new(0x29c,0x05afc3e7),
        new(0x29d,0x15a9592a),new(0x29e,0xc9248b7f),new(0x29f,0x3ffb5910),new(0x2a0,0x3d8af464)]);
    const string ArchiveSha1="b8b92af37dc772ecd8234fb1ce664be0493188f6";
    const uint SpiresMrea=0x09749b2f;
    static readonly JsonSerializerOptions CompleteFields=new(){IncludeFields=true};
    public sealed class OwnedSpires
    {
        internal readonly MapDefinition Parent;
        internal readonly string CollisionHash;
        internal readonly string ParentHash;
        internal readonly Dictionary<uint,string> Integrity;
        internal readonly Dictionary<uint,TextureSize> Textures;
        public IReadOnlyList<Placement> Placements {get;}
        public string MreaSha256 {get;}
        public string VerifiedArchiveSha1=>ArchiveSha1;
        public string SourceGameId=>"G2ME01";
        internal OwnedSpires(MapDefinition parent,Placement[] placements,string mreaSha,Dictionary<uint,TextureSize> textures)
        {
            Parent=parent;CollisionHash=HashCollision(parent);ParentHash=Hash(Encoding.UTF8.GetBytes(parent.Serialize()));Placements=Array.AsReadOnly(placements);
            MreaSha256=mreaSha;Textures=textures;Integrity=placements.ToDictionary(p=>p.Actor,Fingerprint);
        }
        public MapDefinition CreateParent()=>MapProjectSerializer.Clone(Parent);
        public TextureSize Texture(uint id)=>Textures.TryGetValue(id,out var size)?size:throw new InvalidDataException("Texture is outside the verified static source receipt.");
    }
    public static OwnedSpires ReadVerifiedSpires(string imagePath)
    {
        using var disc=GameCubeDiscReader.Open(imagePath);
        Require(disc.GameId=="G2ME01"&&disc.Revision==0&&disc.DiscNumber==0&&disc.Game==ExternalGameKind.Echoes,"Static Spires requires original G2ME01 revision0 disc0.");
        var requirement=ExternalSourceRequirements.For(ExternalGameKind.Echoes).Single(x=>x.Path=="Metroid6.pak");
        var proof=ExternalSourceRequirements.Verify(disc,requirement);
        ValidateSourceProof(disc.GameId,disc.Revision,disc.DiscNumber,disc.Game,proof.Status,proof.ActualSha1);
        using var stream=disc.OpenRead(requirement.Path);using var pak=new RetroPakReader(stream);
        Require(pak.Contains(SpiresMrea)&&pak.Entry(SpiresMrea).Type=="MREA","Verified Spires MREA missing.");
        byte[] bytes=pak.ReadResource(SpiresMrea);var area=EchoesMreaSections.Parse(bytes);
        var scripts=EchoesScriptSource.Read(area);
        var placements=ReadSpires(scripts,id=>pak.Entry(id).Type,pak.ReadResource);
        var textures=new Dictionary<uint,TextureSize>();
        foreach(uint id in placements.SelectMany(p=>p.Geometry.Materials).SelectMany(m=>m.TexIds).Distinct())
        {
            Require(pak.Contains(id)&&pak.Entry(id).Type=="TXTR","Static material references a missing TXTR.");
            byte[] texture=pak.ReadResource(id);Require(texture.Length>=12,"Truncated static TXTR header.");
            int w=BinaryPrimitives.ReadUInt16BigEndian(texture.AsSpan(4)),h=BinaryPrimitives.ReadUInt16BigEndian(texture.AsSpan(6));
            Require(w is >=1 and <=4096&&h is >=1 and <=4096,"Static TXTR dimensions exceed bounds.");textures.Add(id,new(id,w,h));
        }
        var arena=EchoesArenaSources.All.Single(a=>a.Key=="M02_Spires");
        var originalActors=EchoesScriptSource.Read(area);
        var collision=EchoesLocalMapDraft.BuildArena(arena,EchoesCollisionSource.Read(area),originalActors,ArchiveSha1);
        var visuals=EchoesVisualLayer.CreatePreview(collision,EchoesWorldGeometrySource.Read(area));
        var parent=EchoesTunedCannons.CreateRoutedVariant(EchoesTunedCannons.CreateVariant(visuals,arena.Key),arena.Key);
        return new(parent,placements,Hash(bytes),textures);
    }
    internal static void ValidateSourceProof(string gameId,byte revision,byte discNumber,ExternalGameKind game,ExternalArchiveStatus status,string? sha)
    {
        Require(gameId=="G2ME01"&&revision==0&&discNumber==0&&game==ExternalGameKind.Echoes&&status==ExternalArchiveStatus.Verified&&sha==ArchiveSha1,
            "Static Spires requires exact original edition and verified Metroid6 PAK proof.");
    }
    internal static Placement[] ReadSpires(IReadOnlyList<EchoesScriptObject> objects,Func<uint,string> resourceType,Func<uint,byte[]> resource)
    {
        var output=new List<Placement>();
        foreach(var expected in Spires)
        {
            var matches=objects.Where(o=>o.Id==expected.Actor).ToArray();Require(matches.Length==1,"Static actor identity must occur exactly once.");var o=matches[0];
            Require(o.Type=="ACTR"&&o.Active&&o.Layer==0&&o.HasTransform,"Static actor must be active default-layer ACTR.");
            Require(o.ModelResourceId==expected.Model,"Static actor CMDL identity changed.");Require(resourceType(expected.Model)=="CMDL","Static actor model is not CMDL.");ValidateTransform(o.Transform);
            var model=Cmdl.Load(resource,expected.Model);Require(model.Echoes&&model.TriangleCount==86,"Verified Spires pillar geometry shape changed.");Validate(model);
            output.Add(new(expected.Actor,expected.Model,(float[])o.Transform.Clone(),model));
        }
        return output.ToArray();
    }
    public static Vector3 Place(float[] x,Vector3 p)
    {
        ValidateTransform(x);var v=new Vector3(p.X*x[6],p.Y*x[7],p.Z*x[8]);
        float ax=MathHelper.DegreesToRadians(x[3]),ay=MathHelper.DegreesToRadians(x[4]),az=MathHelper.DegreesToRadians(x[5]);
        float c=MathF.Cos(ax),s=MathF.Sin(ax);v=new(v.X,v.Y*c-v.Z*s,v.Y*s+v.Z*c);
        c=MathF.Cos(ay);s=MathF.Sin(ay);v=new(v.X*c+v.Z*s,v.Y,-v.X*s+v.Z*c);
        c=MathF.Cos(az);s=MathF.Sin(az);v=new(v.X*c-v.Y*s,v.X*s+v.Y*c,v.Z);v+=new Vector3(x[0],x[1],x[2]);
        Require(float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&v.LengthSquared<1e10f,"Placed source point exceeds bounds.");
        return new(v.X*EchoesLocalMapDraft.UnitsPerEchoesMetre,v.Z*EchoesLocalMapDraft.UnitsPerEchoesMetre,-v.Y*EchoesLocalMapDraft.UnitsPerEchoesMetre);
    }
    // MapMesh has one per-corner UV channel and no per-corner normal channel.
    // The complete model remains in OwnedSpires. Callers must supply an explicit
    // source UV/material binding; this API never claims complete TEV/normal fidelity.
    public static MapDefinition Append(OwnedSpires owned,MapDefinition parent,Func<Cmdl,int,int> resolveMaterial,Func<Cmdl,int,Cmdl.Corner,float[]> texelCoordinates)
    {
        VerifyFrozen(owned);
        Require(parent.LocalSourceOnly&&parent.Name==owned.Parent.Name&&parent.MapId==owned.Parent.MapId&&parent.FormatVersion==3,
            "Static visual layer requires the receipt-bound routed Spires parent.");
        Require(parent.Capabilities is {} caps&&caps.MinPlayers==1&&caps.MaxPlayers==1&&caps.SupportedModes.SequenceEqual(new[]{"Battle"}),"Static source capabilities must remain solo Battle.");
        Require(HashCollision(parent)==owned.CollisionHash,"Static source collision differs from the verified source.");
        Require(Hash(Encoding.UTF8.GetBytes(parent.Serialize()))==owned.ParentHash,"Static parent content differs from its verified source receipt.");
        var result=MapProjectSerializer.Clone(parent);result.MapId=Stable("echoes/static-source-visuals/spires/v1/"+parent.MapId.ToString("N"));
        result.Name="STATIC ECHOES SPIRES";result.InGameName="Spires (static visual playtest)";
        result.Description="Private source-world visuals plus seven source-transformed pillar CMDLs. Original collision retained; single-UV/flat-normal preview with fallback material, not faithful TEV shading.";
        foreach(var p in owned.Placements.OrderBy(p=>p.Actor))
        {
            var model=p.Geometry;var mesh=new MapMesh{Id=Stable(result.MapId.ToString("N")+"/actor/"+p.Actor.ToString("x8")),Label="Echoes static ACTR "+p.Actor.ToString("x8")+" CMDL "+p.Model.ToString("x8"),Layer="Echoes source statics",Solid=false,CollisionOnly=false,Shade=1};
            for(int i=0;i<model.PositionCount;i++){var v=Place(p.Transform,new(model.Positions[3*i],model.Positions[3*i+1],model.Positions[3*i+2]));mesh.Vertices.Add([v.X,v.Y,v.Z]);}
            for(int t=0;t<model.TriangleCount;t++)
            {
                int sourceMaterial=model.TriangleMaterial[t],mat=resolveMaterial(model,sourceMaterial);Require((uint)mat<result.Materials.Count,"Static source material mapping is missing.");
                int[] order=p.Transform[6]*p.Transform[7]*p.Transform[8]<0?[0,2,1]:[0,1,2];var corners=order.Select(i=>model.Triangles[3*t+i]).ToArray();
                var uv=corners.Select(c=>texelCoordinates(model,sourceMaterial,c)).ToArray();Require(uv.All(v=>v is{Length:2}&&v.All(x=>float.IsFinite(x)&&Math.Abs(x)<2048)),"Static per-corner UV mapping is invalid.");
                mesh.Faces.Add(corners.Select(c=>c.P).ToArray());mesh.FaceMaterials.Add(mat);mesh.FaceTexcoords.Add(uv.Select(v=>(float[])v.Clone()).ToArray());
            }
            result.Geometry.Add(mesh);
        }
        VerifyFrozen(owned);
        Require(Hash(Encoding.UTF8.GetBytes(parent.Serialize()))==owned.ParentHash,"Static parent changed during material/UV callbacks.");
        MapValidator.RequireRuntimeName(result.Name);return result;
    }
    public static float[] PrimarySourceUv(OwnedSpires owned,Cmdl model,int material,Cmdl.Corner corner)
    {
        var ids=model.Materials[material].TexIds;Require(ids.Length>0,"Primary static material has no source TXTR.");var size=owned.Texture(ids[0]);
        var uv=PrimaryUvArray(model,material);
        Require(corner.T0>=0&&2L*corner.T0+1<uv.Length,"Primary source UV index is missing.");
        return [uv[2*corner.T0]*size.Width,uv[2*corner.T0+1]*size.Height];
    }
    internal static float[] PrimaryUvArray(Cmdl model,int material)
    {
        Require((uint)material<model.Materials.Length,"Static source material index is invalid.");
        float[]? uv=(model.Materials[material].Flags&Cmdl.ShortUv)!=0?model.ShortUvs:model.Uv0;
        Require(uv!=null&&uv.Length%2==0&&uv.All(float.IsFinite),"Static material UV array is missing or invalid.");
        return uv!;
    }
    static void VerifyFrozen(OwnedSpires owned)
    {
        Require(owned.Placements.Count==7&&owned.Placements.Select(p=>p.Actor).Distinct().Count()==7,"Exactly seven receipt-bound pillar identities required.");
        foreach(var p in owned.Placements)Require(owned.Integrity.TryGetValue(p.Actor,out var expected)&&expected==Fingerprint(p),"Static geometry, material, corner attribute or transform changed after source verification.");
    }
    static string Fingerprint(Placement p)=>Hash(JsonSerializer.SerializeToUtf8Bytes(p,CompleteFields));
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string HashCollision(MapDefinition d)=>Hash(JsonSerializer.SerializeToUtf8Bytes(d.Geometry.OfType<MapMesh>().Where(m=>m.Solid&&m.CollisionOnly)));
    public static void Validate(Cmdl m)
    {
        Require(m.PositionCount is >=3 and <=100000&&m.Positions.Length%3==0&&m.TriangleCount is >=1 and <=100000&&m.Triangles.Length==3*m.TriangleCount,"Static model shape exceeds bounds.");
        Require(m.Positions.All(v=>float.IsFinite(v)&&Math.Abs(v)<100000)&&m.Uv0.Length%2==0&&m.Uv0.All(float.IsFinite)&&m.Normals.Length%3==0&&m.Normals.All(float.IsFinite),"Nonfinite or oversized static vertex/attribute.");
        Require(m.Materials.Length is >=1 and <=4096,"Static material count exceeds bounds.");
        Require(m.Triangles.All(c=>(uint)c.P<m.PositionCount&&c.N>=-1&&c.N<m.Normals.Length/3)&&m.TriangleMaterial.All(i=>(uint)i<m.Materials.Length),"Static corner or material index out of range.");
        for(int triangle=0;triangle<m.TriangleCount;triangle++)
        {
            float[] uv=PrimaryUvArray(m,m.TriangleMaterial[triangle]);
            for(int corner=0;corner<3;corner++)
            {int index=m.Triangles[3*triangle+corner].T0;Require(index>=-1&&index<uv.Length/2,"Static material-selected UV index out of range.");}
        }
    }
    static void ValidateTransform(float[] x)=>Require(x.Length==9&&x.All(v=>float.IsFinite(v)&&Math.Abs(v)<100000)&&x[6]!=0&&x[7]!=0&&x[8]!=0,"Static transform is nonfinite, singular or oversized.");
    static void Require(bool b,string s){if(!b)throw new InvalidDataException(s);}
    static Guid Stable(string s)=>new(SHA256.HashData(Encoding.UTF8.GetBytes(s)).AsSpan(0,16));
}
