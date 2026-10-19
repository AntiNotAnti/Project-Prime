using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MphRead.Entities;
namespace MphRead.Mods.Replay;
internal sealed record ReplayGraphDifference(string Path,int BytesA,int BytesB,int FirstByte,string HashA,string HashB,bool RenderCache);
internal sealed partial class ReplayWorldCheckpoint
{
    // These fields are written by ModGetFirstPersonRenderCameraPosition/TransformCamera,
    // not restored gameplay: one viewer has drawn and the private comparison has not.
    private static bool RenderCachePath(string path)=>path.EndsWith("._fpRenderPose",StringComparison.Ordinal)
        ||path.EndsWith("._fpRenderPoseValid",StringComparison.Ordinal);
    internal ReplayGraphDifference[] GraphDifferences(ReplayWorldCheckpoint other,int maximum=64)
    {
        var a=GraphParts();var b=other.GraphParts();var differences=new List<ReplayGraphDifference>();
        foreach(string path in a.Keys.Union(b.Keys).OrderBy(p=>p,StringComparer.Ordinal))
        {
            byte[] left=a.GetValueOrDefault(path)??Array.Empty<byte>(),right=b.GetValueOrDefault(path)??Array.Empty<byte>();
            if(left.AsSpan().SequenceEqual(right))continue;
            int first=0;while(first<Math.Min(left.Length,right.Length)&&left[first]==right[first])first++;
            differences.Add(new(path,left.Length,right.Length,first,Convert.ToHexString(SHA256.HashData(left)),Convert.ToHexString(SHA256.HashData(right)),RenderCachePath(path)));
            if(differences.Count>=maximum)break;
        }
        return differences.ToArray();
    }
    internal string ComparableGraphFingerprint()
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        foreach(var part in GraphParts().OrderBy(p=>p.Key,StringComparer.Ordinal))
        {if(RenderCachePath(part.Key))continue;writer.Write(part.Key);WriteBytes(writer,part.Value);}
        writer.Flush();return Convert.ToHexString(SHA256.HashData(output.GetBuffer().AsSpan(0,(int)output.Length)));
    }
    private Dictionary<string,byte[]> GraphParts()
    {
        var parts=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        using var input=_data.OpenRead();using var reader=new BinaryReader(input);
        long start=input.Position;
        reader.ReadUInt32();ushort version=reader.ReadUInt16();if(version!=Version)throw new InvalidDataException("Graph paths require this build's checkpoint layout.");
        reader.ReadByte();reader.ReadString();reader.ReadString();reader.ReadInt32();reader.ReadUInt64();reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt32();
        parts["Header"]=Slice(start,input.Position);
        parts["InitialDecoder"]=ReadBytes(reader,MaximumBytes);parts["CurrentDecoder"]=ReadBytes(reader,MaximumBytes);
        int count=Count(reader,MaximumObjects);
        for(int i=0;i<count;i++)
        {
            ushort id=reader.ReadUInt16();reader.ReadUInt64();var data=ReadBytes(reader,MaximumBytes);
            if(id>=ObjectTypes.Length)throw new InvalidDataException("Invalid graph type.");
            Type type=ObjectTypes[id];string path=$"Objects[{i}].{type}";
            using var nodeStream=new MemoryStream(data,false);using var node=new BinaryReader(nodeStream);
            if(type==typeof(ModelInstance)||type==typeof(WeaponInfo)||Collection(type)||!Fields.TryGetValue(type,out var fields))
            {parts[path]=data;continue;}
            int prefix=type==typeof(ItemInstanceEntity)||type==typeof(HalfturretEntity)?4:0;
            if(type==typeof(MphRead.Effects.SingleParticle))prefix=4;
            if(prefix>0){parts[path+".Construction"]=data.AsSpan(0,prefix).ToArray();nodeStream.Position=prefix;}
            foreach(var field in fields)
            {
                long at=nodeStream.Position;Skip(node,field.FieldType);
                parts[path+"."+field.Name]=data.AsSpan((int)at,(int)(nodeStream.Position-at)).ToArray();
            }
            if(nodeStream.Position!=data.Length)throw new InvalidDataException("Graph field path decoder differs from captured schema.");
        }
        string[] sections={"Assets","Cosmetics","BombWorld","Traversal","AuthorityForms","NativeTouch","PickupBindings"};
        foreach(string section in sections)parts[section]=ReadBytes(reader,MaximumBytes);
        parts["IndexedFirePresentation"]=new[]{reader.ReadByte()};
        if(input.Position!=input.Length)throw new InvalidDataException("Unexpected graph comparison tail.");
        return parts;
        byte[] Slice(long from,long to){long current=input.Position;input.Position=from;byte[] value=reader.ReadBytes((int)(to-from));input.Position=current;return value;}
    }
    private static void Skip(BinaryReader reader,Type type)
    {
        if(type==typeof(object)){if(!Types.TryGetValue(reader.ReadString(),out var actual))throw new InvalidDataException("Invalid dynamic graph value.");Skip(reader,actual);return;}
        if(Nullable.GetUnderlyingType(type) is{} underlying){if(reader.ReadBoolean())Skip(reader,underlying);return;}
        if(type==typeof(string)){if(reader.ReadBoolean())reader.ReadString();return;}
        if(type.IsEnum){Skip(reader,Enum.GetUnderlyingType(type));return;}
        if(type==typeof(bool)||type==typeof(byte)||type==typeof(sbyte)){reader.ReadByte();return;}
        if(type==typeof(short)||type==typeof(ushort)||type==typeof(char)){reader.ReadUInt16();return;}
        if(type==typeof(int)||type==typeof(uint)||type==typeof(float)||!type.IsValueType){reader.ReadUInt32();return;}
        if(type==typeof(long)||type==typeof(ulong)||type==typeof(double)){reader.ReadUInt64();return;}
        foreach(var field in ValueFields(type))Skip(reader,field.FieldType);
    }
}
