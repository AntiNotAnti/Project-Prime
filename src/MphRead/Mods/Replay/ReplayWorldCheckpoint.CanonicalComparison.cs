using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MphRead.Entities;
using MphRead.Effects;
namespace MphRead.Mods.Replay;
internal sealed partial class ReplayWorldCheckpoint
{
    private sealed record ComparisonCapsule(Node[] Nodes,Dictionary<string,byte[]> Sections);
    private ComparisonCapsule ComparisonValues()
    {
        using var stream=_data.OpenRead();using var reader=new BinaryReader(stream);
        var (version, declared)=ReadWorldHeader(reader);reader.ReadString();reader.ReadString();reader.ReadInt32();reader.ReadUInt64();reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt32();
        var sections=new Dictionary<string,byte[]>{{"Header",ReadSlice(reader,0)},{"InitialDecoder",ReadBytes(reader,MaximumBytes)},{"CurrentDecoder",ReadBytes(reader,MaximumBytes)}};
        var nodes=new Node[Count(reader,MaximumObjects)];
        for(int i=0;i<nodes.Length;i++)nodes[i]=new(reader.ReadUInt16(),reader.ReadUInt64(),ReadBytes(reader,MaximumBytes));
        foreach(string name in new[]{"Assets","Cosmetics","BombWorld","Traversal","AuthorityForms","NativeTouch","PickupBindings"})sections[name]=ReadBytes(reader,MaximumBytes);
        sections["Grapple"]=version>=11?ReadBytes(reader,184):Array.Empty<byte>();
        sections["CustomWeapons"]=version==CustomVersion?ReadBytes(reader,CustomWorldAppendixSize):Array.Empty<byte>();
        sections["IndexedFirePresentation"]=new[]{reader.ReadByte()};return new(nodes,sections);
    }
    internal ReplayGraphDifference[] CanonicalGraphDifferences(ReplayWorldCheckpoint other,int maximum=64)
    {
        var a=ComparisonValues();var b=other.ComparisonValues();var differences=new List<ReplayGraphDifference>();
        var forward=new Dictionary<int,int>();var reverse=new Dictionary<int,int>();var pending=new Queue<(int A,int B,string Path)>();
        void Difference(string path,byte[] left,byte[] right)
        {
            if(left.AsSpan().SequenceEqual(right)||differences.Count>=maximum)return;
            int at=0;while(at<Math.Min(left.Length,right.Length)&&left[at]==right[at])at++;
            differences.Add(new(path,left.Length,right.Length,at,Convert.ToHexString(SHA256.HashData(left)),Convert.ToHexString(SHA256.HashData(right)),false));
        }
        void Link(int left,int right,string path)
        {
            if(left<0||right<0){if(left!=right)Difference(path,BitConverter.GetBytes(left),BitConverter.GetBytes(right));return;}
            if(left>=a.Nodes.Length||right>=b.Nodes.Length)throw new InvalidDataException("Invalid comparison graph reference.");
            if(forward.TryGetValue(left,out int paired)){if(paired!=right)Difference(path+".Alias",BitConverter.GetBytes(paired),BitConverter.GetBytes(right));return;}
            if(reverse.TryGetValue(right,out int prior)){Difference(path+".Alias",BitConverter.GetBytes(left),BitConverter.GetBytes(prior));return;}
            forward.Add(left,right);reverse.Add(right,left);pending.Enqueue((left,right,path));
        }
        void Value(BinaryReader left,BinaryReader right,Type type,string path)
        {
            if(type==typeof(object))
            {
                string la=left.ReadString(),lb=right.ReadString();if(la!=lb){Difference(path+".Type",System.Text.Encoding.UTF8.GetBytes(la),System.Text.Encoding.UTF8.GetBytes(lb));Skip(left,Types[la]);Skip(right,Types[lb]);return;}
                Value(left,right,Types[la],path);return;
            }
            if(Nullable.GetUnderlyingType(type) is{} inner)
            {bool la=left.ReadBoolean(),lb=right.ReadBoolean();if(la!=lb){Difference(path,new[]{(byte)(la?1:0)},new[]{(byte)(lb?1:0)});if(la)Skip(left,inner);if(lb)Skip(right,inner);return;}if(la)Value(left,right,inner,path);return;}
            if(!type.IsValueType&&type!=typeof(string)){Link(left.ReadInt32(),right.ReadInt32(),path);return;}
            if(type.IsValueType&&!type.IsPrimitive&&!type.IsEnum)
            {foreach(var field in ValueFields(type))Value(left,right,field.FieldType,path+"."+field.Name);return;}
            long l=left.BaseStream.Position,r=right.BaseStream.Position;Skip(left,type);Skip(right,type);
            Difference(path,ReadSlice(left,l),ReadSlice(right,r));
        }
        for(int root=0;root<6;root++)Link(root,root,"Roots["+root+"]");
        while(pending.Count>0)
        {
            var pair=pending.Dequeue();var na=a.Nodes[pair.A];var nb=b.Nodes[pair.B];
            if(na.Type!=nb.Type){Difference(pair.Path+".Type",BitConverter.GetBytes(na.Type),BitConverter.GetBytes(nb.Type));continue;}
            Type type=ObjectTypes[na.Type];string path=pair.Path+":"+type;
            using var sa=new MemoryStream(na.Data,false);using var sb=new MemoryStream(nb.Data,false);using var la=new BinaryReader(sa);using var lb=new BinaryReader(sb);
            if(type==typeof(ModelInstance)||type==typeof(WeaponInfo)){Difference(path,na.Data,nb.Data);continue;}
            if(Collection(type))
            {
                int dimensions=type.IsArray?type.GetArrayRank():1;int length=1;
                bool same=true;for(int i=0;i<dimensions;i++){int ca=la.ReadInt32(),cb=lb.ReadInt32();if(ca!=cb){Difference(path+".Count["+i+"]",BitConverter.GetBytes(ca),BitConverter.GetBytes(cb));same=false;}length=checked(length*ca);}
                if(!same)continue;
                for(int i=0;i<length;i++)Value(la,lb,Element(type),path+"["+i+"]");continue;
            }
            if(type==typeof(ItemInstanceEntity)||type==typeof(HalfturretEntity)||type==typeof(SingleParticle))Difference(path+".Construction",la.ReadBytes(4),lb.ReadBytes(4));
            foreach(var field in Fields[type])
            {
                // PlayerDraw writes _modelTransform only when a third-person body is submitted;
                // a first-person view retains whichever prior auxiliary body was last drawn.
                if(field.Name is "_fpRenderPose" or "_fpRenderPoseValid"||type==typeof(PlayerEntity)&&field.Name=="_modelTransform"){Skip(la,field.FieldType);Skip(lb,field.FieldType);continue;}
                Value(la,lb,field.FieldType,path+"."+field.Name);
            }
        }
        foreach(var section in a.Sections)
        {
            if(section.Key!="Assets"){Difference(section.Key,section.Value,b.Sections[section.Key]);continue;}
            var assetsA=ReplayAssetCheckpoint.CanonicalStateWithoutDrawCaches(section.Value);var assetsB=ReplayAssetCheckpoint.CanonicalStateWithoutDrawCaches(b.Sections[section.Key]);
            foreach(string key in assetsA.Keys.Union(assetsB.Keys).OrderBy(k=>k,StringComparer.Ordinal))
                Difference("Assets."+key,assetsA.GetValueOrDefault(key)??Array.Empty<byte>(),assetsB.GetValueOrDefault(key)??Array.Empty<byte>());
        }
        return differences.ToArray();
    }
    private static byte[] ReadSlice(BinaryReader reader,long from)
    {long end=reader.BaseStream.Position;reader.BaseStream.Position=from;byte[] bytes=reader.ReadBytes(checked((int)(end-from)));reader.BaseStream.Position=end;return bytes;}
}
