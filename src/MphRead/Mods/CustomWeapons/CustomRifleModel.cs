using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

internal enum CustomRiflePart : byte { Body, Bolt, Magazine }
internal readonly record struct CustomRifleBox(Vector3 Center,Vector3 Half,byte Material,CustomRiflePart Part);

// Built-in presentation data only. This is not a creator package decoder or a
// gameplay definition. The catalogue verifies its exact bytes before decoding.
internal sealed class CustomRifleModel
{
    internal const int MaximumBytes=16384,MaximumBoxes=32;
    internal const string Resource="Weapons/RiftlineM7/model-v1.json";
    internal const string Digest="3dffd6317ba8fc2d861e045f808aaa4cfdb80b251ff354f7b7fb379bf199e752";
    private readonly Vector4[] _palette;
    private readonly CustomRifleBox[][] _lods;
    internal Vector3 Muzzle { get; }
    internal ReadOnlySpan<CustomRifleBox> Lod(int lod)=>_lods[Math.Clamp(lod,0,1)];
    internal Vector4 Color(byte material)=>_palette[material];
    private CustomRifleModel(Vector4[] palette,CustomRifleBox[][] lods,Vector3 muzzle)
    {_palette=palette;_lods=lods;Muzzle=muzzle;}

    internal static CustomRifleModel? LoadBuiltin()
    {
        // Dedicated/headless matches do not even open a presentation resource.
        if(Mods.Headless.Active)return null;
        using var stream=typeof(CustomRifleModel).Assembly.GetManifestResourceStream(Resource);
        if(stream==null)throw new InvalidDataException("Missing original M7 model.");
        if(stream.Length is <=0 or >MaximumBytes)throw new InvalidDataException("M7 resource size.");
        var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
        return DecodeVerified(bytes);
    }
    internal static CustomRifleModel DecodeVerified(ReadOnlyMemory<byte> bytes)
    {
        if(bytes.Length is <=0 or >MaximumBytes
            ||Convert.ToHexStringLower(SHA256.HashData(bytes.Span))!=Digest)
            throw new InvalidDataException("M7 resource digest/length.");
        return Decode(bytes);
    }
    internal static CustomRifleModel Decode(ReadOnlyMemory<byte> bytes)
    {
        if(bytes.Length is <=0 or >MaximumBytes)throw new InvalidDataException("M7 model size.");
        try
        {
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=8});
            var root=doc.RootElement;Fields(root,"schema","basis","palette","muzzle","lods");
            if(root.GetProperty("schema").GetInt32()!=1||root.GetProperty("basis").GetString()!="+Z forward,+Y up,metres")
                throw new InvalidDataException("M7 schema/basis.");
            var colors=root.GetProperty("palette");Array(colors,6,6);var palette=new Vector4[6];int c=0;
            foreach(var row in colors.EnumerateArray())
            {
                var v=Vector(row);if(v.X<0||v.Y<0||v.Z<0||v.X>1||v.Y>1||v.Z>1)
                    throw new InvalidDataException("M7 palette.");
                palette[c++]=new(v,1);
            }
            var muzzle=Vector(root.GetProperty("muzzle"));
            if(MathF.Abs(muzzle.X)>.4f||MathF.Abs(muzzle.Y)>.4f||muzzle.Z is <.5f or >2.5f)
                throw new InvalidDataException("M7 muzzle socket.");
            var tiers=root.GetProperty("lods");Array(tiers,2,2);var lods=new CustomRifleBox[2][];int tier=0;
            foreach(var rows in tiers.EnumerateArray())
            {
                Array(rows,3,MaximumBoxes);var boxes=new CustomRifleBox[rows.GetArrayLength()];int b=0;bool body=false;float end=float.MinValue;
                foreach(var row in rows.EnumerateArray())
                {
                    Fields(row,"center","half","material","part");
                    var center=Vector(row.GetProperty("center"));var half=Vector(row.GetProperty("half"));
                    if(half.X is <.001f or >1||half.Y is <.001f or >1||half.Z is <.001f or >1
                        ||MathF.Abs(center.X)+half.X>2.5f||MathF.Abs(center.Y)+half.Y>2.5f||MathF.Abs(center.Z)+half.Z>2.5f)
                        throw new InvalidDataException("M7 box bounds.");
                    int material=row.GetProperty("material").GetInt32();if((uint)material>=6)throw new InvalidDataException("M7 material.");
                    var part=row.GetProperty("part").GetString() switch
                    {"body"=>CustomRiflePart.Body,"bolt"=>CustomRiflePart.Bolt,"magazine"=>CustomRiflePart.Magazine,_=>throw new InvalidDataException("M7 rigid group.")};
                    body|=part==CustomRiflePart.Body;end=MathF.Max(end,center.Z+half.Z);
                    boxes[b++]=new(center,half,(byte)material,part);
                }
                if(!body||muzzle.Z<end-.05f||muzzle.Z>end+.2f)throw new InvalidDataException("M7 muzzle/geometry alignment.");
                lods[tier++]=boxes;
            }
            if(lods[1].Length>lods[0].Length)throw new InvalidDataException("M7 LOD budget.");
            return new(palette,lods,muzzle);
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or FormatException or OverflowException)
        {throw new InvalidDataException("Invalid original M7 model.",error);}
    }
    private static void Fields(JsonElement value,params string[] expected)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("M7 object.");
        ulong seen=0;
        foreach(var field in value.EnumerateObject())
        {
            int index=System.Array.IndexOf(expected,field.Name);
            if(index<0||(seen&(1UL<<index))!=0)throw new InvalidDataException("M7 unknown/duplicate field.");
            seen|=1UL<<index;
        }
        if(seen!=(1UL<<expected.Length)-1)throw new InvalidDataException("M7 missing field.");
    }
    private static void Array(JsonElement value,int min,int max)
    {if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()<min||value.GetArrayLength()>max)throw new InvalidDataException("M7 array budget.");}
    private static Vector3 Vector(JsonElement value)
    {
        Array(value,3,3);var vector=new Vector3(value[0].GetSingle(),value[1].GetSingle(),value[2].GetSingle());
        if(!float.IsFinite(vector.X)||!float.IsFinite(vector.Y)||!float.IsFinite(vector.Z))throw new InvalidDataException("M7 nonfinite coordinate.");
        return vector;
    }
}
