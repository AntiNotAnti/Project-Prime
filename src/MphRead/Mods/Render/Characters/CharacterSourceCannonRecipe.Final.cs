using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Mods.Import;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterSourceCannonRecipe
{
    internal const string FinalReceiptFile = "darksamus-overcharged-echoes.cannon-source.json";
    // Independently measured importer output; admission stays closed until pinned.
    internal const string QualifiedFinalReceiptSha256 = "46c828de8c0c7f6ec6102216400891c30475036bc2aa7da0f7b6b752abf0752c";
    internal static bool IsGeneratedFinalEntry(CharacterModelManifestEntry e,string sha) =>
        QualifiedFinalReceiptSha256.Length == 64 && e.Part == CharacterModelPart.ViewModel && e.Lod == 0
        && e.Hunter == "Samus" && e.CharacterId == "darksamus" && e.SkinSource == "MP2DarkSamusFinal"
        && e.PoseSource == CharacterPoseSource.SourceRigPreview && e.SourceCannonReceipt == FinalReceiptFile
        && e.SourceCannonSha256 == QualifiedFinalReceiptSha256
        && String.Equals(sha,CharacterModelPack.GeneratedFinalSourceGlbSha256,StringComparison.OrdinalIgnoreCase);

    private static CharacterSourceCannonRecipe LoadGeneratedFinal(string root,CharacterModelManifestEntry entry,string sha)
    {
        Require(IsGeneratedFinalEntry(entry,sha),"Exact independent generated Final cannon profile required.");
        byte[] bytes = CharacterModelPack.ReadCannonInput(root,FinalReceiptFile,1024*1024);
        Require(Sha(bytes)==QualifiedFinalReceiptSha256,"Final cannon recipe changed.");
        using var document=JsonDocument.Parse(bytes);var r=document.RootElement;
        Require(r.GetProperty("format").GetInt32()==2 && r.GetProperty("mode").GetString()=="generatedSourceCannonAuthoring"
            && r.GetProperty("presentationPart").GetString()=="viewModel" && r.GetProperty("sourceCharacter").GetString()=="darksamus-overcharged-echoes"
            && r.GetProperty("writerVersion").GetInt32()==RetroCharacterSourceGlbWriter.Version
            && r.GetProperty("materialPolicy").GetString()==RetroCharacterSourceGlbWriter.MaterialPolicy && r.GetProperty("sourceBasis").GetString()=="(-x,z,y)"
            && r.GetProperty("clipOrdinal").GetInt32()==32 && r.GetProperty("clipResourceId").GetUInt32()==0xd12fdfa3 && r.GetProperty("clipSeconds").GetDouble()==0
            && r.GetProperty("sourceElbowId").GetInt32()==24 && r.GetProperty("sourceElbowName").GetString()=="R_elbow"
            && r.GetProperty("sourceParticleLocatorId").GetInt32()==32 && r.GetProperty("sourceParticleLocatorName").GetString()=="GUN_Particle_LCTR"
            && r.GetProperty("sourceUnitsUnscaled").GetBoolean() && r.GetProperty("calibrationOnly").GetBoolean()
            && !r.GetProperty("gameplayMuzzleChanged").GetBoolean() && !r.GetProperty("publicSelectionEnabled").GetBoolean()
            && !r.GetProperty("networkReplayEnabled").GetBoolean() && !r.GetProperty("firstPersonVerified").GetBoolean(),
            "Final receipt must preserve its own fixed original authoring facts.");
        var inputs=r.GetProperty("sourceInputs").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
        const string stem="darksamus-overcharged-echoes";
        string[] names=[stem+".source.glb",stem+".source.json",stem+".ancs-graph.json",stem+".ancs.bin",stem+".cinf.bin",stem+".animations.bin"];
        Require(inputs.Keys.Order().SequenceEqual(names.Order()) && entry.Model==names[0] && entry.Provenance==names[1]
            && String.Equals(inputs[names[0]],sha,StringComparison.OrdinalIgnoreCase) && inputs[names[1]]==entry.ProvenanceSha256
            && inputs[names[5]]=="15f102868334b98e271050493bbfdd996ca3d7121a6f4b04d54f6308b5d07b0d",
            "Final cannon must bind its own complete original body inputs.");
        float[] m=r.GetProperty("toCannon").EnumerateArray().Select(v=>v.GetSingle()).ToArray();Require(m.Length==16,"Complete Final cannon frame required.");
        var basis=new Matrix4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);CharacterSourceRig.RequireRigid(basis);
        int[][] triangles=r.GetProperty("triangles").EnumerateArray().Select(a=>a.EnumerateArray().Select(n=>n.GetInt32()).ToArray()).ToArray();
        Require(triangles.Length==9 && triangles.Select(a=>a.Length).SequenceEqual(new[]{0,0,0,392,66,314,0,21,0})
            && triangles.SelectMany(a=>a).All(i=>i is >=0 and <12734) && triangles.SelectMany(a=>a).Distinct().Count()==793,
            "Complete independent Final shell cut required.");
        var result=new CharacterSourceCannonRecipe(QualifiedFinalReceiptSha256,basis,inputs,triangles,generated:true,final:true);
        result.RevalidateGeneratedFinal(root,entry,sha);return result;
    }
    private void RevalidateGeneratedFinal(string root,CharacterModelManifestEntry entry,string sha)
    {
        Require(IsGeneratedFinalEntry(entry,sha) && ReceiptSha256==QualifiedFinalReceiptSha256
            && Sha(CharacterModelPack.ReadCannonInput(root,FinalReceiptFile,1024*1024))==ReceiptSha256,"Same immutable Final cannon owner required.");
        foreach(var input in _inputs)
            Require((input.Key.EndsWith(".glb",StringComparison.Ordinal)?sha.ToLowerInvariant():Sha(CharacterModelPack.ReadCannonInput(root,input.Key,
                input.Key.EndsWith(".animations.bin",StringComparison.Ordinal)?RetroCharacterAnimationArchive.MaximumBytes
                :input.Key.EndsWith(".ancs.bin",StringComparison.Ordinal)?RetroAnimationIndex.MaxAncsBytes:1024*1024)))==input.Value,
                "Final cannon source input changed: "+input.Key);
    }
    private CharacterWeightedModelData DeriveGeneratedFinal(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterWeightedModelData source)
    {
        lease.Require(asset);
        Require(ReferenceEquals(lease.SourceCannon,this) && asset.Part==CharacterModelPart.ViewModel
            && asset.PrivateAppearance==new PrivateCharacterAppearance("darksamus","MP2DarkSamusFinal")
            && source.SourceCannon==null && source.SourceRig is {Nodes.Count:40} && source.Joints.Count==39
            && lease.SourceAnimations is {RootBoneId:0,ExternalParentId:97},"Complete own Final source cannon rig/lease required.");
        var clip=CharacterSourceClip.Load(lease,asset,source.SourceRig!,32);var pose=clip.Sample(0);
        Require(clip.Name=="A_MissileBarrage_dark_samus" && clip.ResourceId==0xd12fdfa3
            && clip.ContentSha256=="4bdb85ff6b36f10c5b330158fa7968f2af9203f293c00b8fb7796d32306ed197"
            && CharacterSourceClip.MatrixDistance(GeneratedBasis(source,pose),ToCannon)<.00001f,"Own Final fixed source frame must reproduce.");
        var cut=SelectGeneratedCut(source,finalTrim:true);
        Require(cut.Selected.Length==9 && cut.Selected.Select((a,i)=>a.SequenceEqual(_triangles[i])).All(v=>v),"Own Final trimmed global triangle IDs must reproduce.");
        lease.Require(asset);return source with {Primitives=cut.Primitives,SourceCannon=this,SourceAuthoringPose=Array.AsReadOnly(pose.ToArray())};
    }
}
