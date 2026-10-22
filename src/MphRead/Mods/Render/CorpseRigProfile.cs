using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MphRead.Formats;

namespace MphRead.Mods.Render;

// Native node indices, palette slots and physical body indices are separate.
// These signatures describe native name/parent/palette layouts, not package
// authenticity, skinning bind offsets or qualification for physical retargeting.
internal sealed class CorpseRigProfile
{
    private readonly record struct Layout(Hunter Hunter,string Prefix,int Palette,string Signature);
    private static readonly Layout[] Layouts =
    {
        new(Hunter.Samus, "Samus", 17, "8d569b6076040601b166030673d9b9d3b831db95e17730f984d643648b14c778"),
        new(Hunter.Kanden, "Kanden", 16, "c32a6e35e83d9fa29f77d516cab98339345ceb59e8df06c1b83d22a73e004446"),
        new(Hunter.Trace, "Trace", 17, "567abe4f5ff0c09b59de3293c59c5f695e85220d92d41a1449ea1e4cfe188013"),
        new(Hunter.Sylux, "Sylux", 17, "8d569b6076040601b166030673d9b9d3b831db95e17730f984d643648b14c778"),
        new(Hunter.Noxus, "Nox", 14, "2b9af7a3ff2c7f014490275686ac486b2ea9fb45631673525a7a09f86a3ebc60"),
        new(Hunter.Spire, "Spire", 13, "f8c9fc62650f78946b699163e8bac4d6727ab37604e37e62e04de7e4e2429cb4"),
        new(Hunter.Weavel, "Weavel", 17, "8d569b6076040601b166030673d9b9d3b831db95e17730f984d643648b14c778"),
    };
    private static readonly ConditionalWeakTable<Model,CorpseRigProfile> Cache=new();
    internal static CorpseRigProfile For(Model model)=>Cache.GetValue(model,m=>new(m));
    internal bool ProceduralSupported {get;}
    internal bool PhysicalQualified=>false; // CW-09 primitive pilot is not a skinning qualification.
    internal Hunter Hunter {get;}
    internal int Lod {get;}=-1;
    internal int Head {get;}=-1;
    internal int Spine {get;}=-1;
    internal int LeftHip {get;}=-1;
    internal int LeftKnee {get;}=-1;
    internal int LeftAnkle {get;}=-1;
    internal int RightHip {get;}=-1;
    internal int RightKnee {get;}=-1;
    internal int RightAnkle {get;}=-1;
    internal bool WeightedHead {get;}
    internal int PaletteCount {get;}
    private CorpseRigProfile(Model model)
    {
        foreach(var layout in Layouts)
        {
            int lod=model.Name==layout.Prefix+"_lod0"?0:model.Name==layout.Prefix+"_lod1"?1:-1;
            if(lod<0)continue;
            Hunter=layout.Hunter;Lod=lod;PaletteCount=model.NodeMatrixIds.Count;
            if(model.Nodes.Count!=19||PaletteCount!=layout.Palette||Signature(model)!=layout.Signature)return;
            int Find(string name){for(int i=0;i<model.Nodes.Count;i++)if(model.Nodes[i].Name==name)return i;return -1;}
            Head=Find("Head_1");Spine=Find("Spine_2");
            LeftHip=Find("L_hip");LeftKnee=Find("L_knee");LeftAnkle=Find("L_ankle");
            RightHip=Find("R_hip");RightKnee=Find("R_knee");RightAnkle=Find("R_ankle");
            if(Head<0||Spine<0||LeftHip<0||LeftKnee<0||LeftAnkle<0||RightHip<0||RightKnee<0||RightAnkle<0)return;
            foreach(int node in model.NodeMatrixIds)if(node==Head)WeightedHead=true;
            ProceduralSupported=true;return;
        }
    }
    private static string Signature(Model model)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> integer=stackalloc byte[4];
        foreach(var node in model.Nodes)
        {
            byte[] name=Encoding.UTF8.GetBytes(node.Name);
            BinaryPrimitives.WriteInt32LittleEndian(integer,name.Length);hash.AppendData(integer);hash.AppendData(name);
            BinaryPrimitives.WriteInt32LittleEndian(integer,node.ParentIndex);hash.AppendData(integer);
        }
        foreach(int node in model.NodeMatrixIds)
        {BinaryPrimitives.WriteInt32LittleEndian(integer,node);hash.AppendData(integer);}
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
