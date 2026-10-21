using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterModelPack
{
    private readonly Dictionary<CharacterModelAsset, CharacterSourceAnimationArchive> _sourceAnimations = new(ReferenceEqualityComparer.Instance);
}

// Optional temporal input to the existing private source-rig owner. Exact
// generated Echoes/Prime bodies and their fixed cannons own immutable profiles. This
// grants no ball, selection, actor, retargeting or network authority.
internal sealed class CharacterSourceAnimationArchive
{
    private readonly string _stem;
    // Metadata-only pins measured through the verified G2ME01 archive recipe.
    // A self-consistent authored replacement cannot impersonate original motion.
    internal const string QualifiedArchiveSha256 = "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1";
    internal const string QualifiedCinfSha256 = "3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4";
    private readonly string _root, _modelSha;
    private readonly Dictionary<string, (string Sha, int Limit)> _inputs;
    internal string Sha256 { get; }
    internal RetroCharacterAnimationArchive.Snapshot Data { get; }
    internal RetroNativeSkeleton Skeleton { get; }
    internal uint RootBoneId { get; }
    internal uint ExternalParentId { get; }
    internal bool IsNormalBall => _stem == CharacterGeneratedNormalBallProfile.Stem;
    internal bool IsFinalBall => _stem == CharacterGeneratedFinalBallProfile.Stem;
    internal bool IsBall => IsNormalBall||IsFinalBall;
    internal int ExpectedJointCount => IsBall ? 2 : RootBoneId == 0 ? 39 : 35;
    private CharacterSourceAnimationArchive(string root, string modelSha, string sha, RetroCharacterAnimationArchive.Snapshot data,
        RetroNativeSkeleton skeleton, Dictionary<string, (string, int)> inputs, string stem, uint rootBoneId, uint externalParentId)
        => (_root, _modelSha, Sha256, Data, Skeleton, _inputs, _stem, RootBoneId, ExternalParentId)
            = (root, modelSha, sha, data, skeleton, inputs, stem, rootBoneId, externalParentId);
    internal static CharacterSourceAnimationArchive Load(string root, CharacterModelManifestEntry entry, string modelSha)
    {
        if (CharacterGeneratedNormalBallProfile.IsEntry(entry)) return LoadNormalBall(root,entry,modelSha);
        if (CharacterGeneratedFinalBallProfile.IsEntry(entry)) return LoadNormalBall(root,entry,modelSha,final:true);
        bool final = entry.SkinSource == "MP2DarkSamusFinal";
        var trooper = CharacterGeneratedTrooperProfile.FromSkin(entry.SkinSource);
        bool echoes = trooper == null;
        string Stem = trooper?.Id ?? (final ? "darksamus-overcharged-echoes" : "darksamus-echoes");
        string archivePin = trooper?.AnimationSha ?? (final ? "15f102868334b98e271050493bbfdd996ca3d7121a6f4b04d54f6308b5d07b0d" : QualifiedArchiveSha256);
        Require((entry.Part == CharacterModelPart.Biped || echoes && CharacterSourceCannonRecipe.IsGeneratedEntry(entry, modelSha)
                || final && CharacterSourceCannonRecipe.IsGeneratedFinalEntry(entry,modelSha)
                || trooper != null && CharacterSourceCannonRecipe.IsGeneratedTrooperEntry(entry, modelSha, trooper)) && entry.PoseSource == CharacterPoseSource.SourceRigPreview
            && entry.SourceAnimationArchive == Stem + ".animations.bin" && entry.SourceAnimationArchiveSha256 == archivePin
            && modelSha.Length == 64 && (trooper != null
                ? entry.Hunter == "Weavel" && entry.CharacterId == "trooper" && modelSha == trooper.ModelSha
                : entry.CharacterId == "darksamus" && entry.SkinSource == (final ? "MP2DarkSamusFinal" : "MP2DarkSamus")
                    && (!final || modelSha == CharacterModelPack.GeneratedFinalSourceGlbSha256)),
            "Only an exact verified generated body accepts imported animation resources.");
        var inputs = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        byte[] Read(string file, int maximum)
        {
            byte[] bytes = CharacterModelPack.ReadGraphInput(root, file, maximum);
            inputs.Add(file, (RetroAnimationGraphProof.Sha(bytes), maximum)); return bytes;
        }
        byte[] description = Read(Stem + ".ancs-graph.json", RetroAnimationGraphProof.MaximumProofBytes),
            source = Read(Stem + ".ancs.bin", RetroAnimationIndex.MaxAncsBytes),
            cinf = Read(Stem + ".cinf.bin", 1024 * 1024),
            archive = Read(entry.SourceAnimationArchive!, RetroCharacterAnimationArchive.MaximumBytes);
        var graph = RetroAnimationGraphProof.Verify(description, source);
        if (trooper == null) CharacterSourceGraphProof.RequireQualifiedSource(graph, Stem);
        else Require(RetroAnimationGraphProof.Sha(description) == trooper.GraphSha && graph.Format == 1
            && graph.SourceGameId == "GM8E01" && graph.SourceAncsId == 0x8169653a
            && graph.SourceAncsSha256 == CharacterGeneratedTrooperProfile.AncsSha
            && graph.SourceCharacterId == Stem && graph.SourceCharacterName == trooper.NativeName
            && graph.Cmdl == trooper.Cmdl && graph.Cskr == trooper.Cskr && graph.Cinf == 0x0c9669bc
            && graph.SourceSnapshotFile == Stem + ".ancs.bin" && !graph.NamedGraphSemanticsAccepted,
            "Exact retained Prime source graph required; no named graph evaluation.");
        Require(RetroAnimationGraphProof.Sha(archive) == entry.SourceAnimationArchiveSha256, "Imported animation bank changed.");
        var skeleton = RetroCharacterSource.ReadSkeleton(cinf);
        uint rootId = echoes ? 0u : 3u, externalParent = echoes ? 97u : 2u;
        Require(RetroAnimationGraphProof.Sha(cinf) == (echoes ? QualifiedCinfSha256 : CharacterGeneratedTrooperProfile.CinfSha)
            && graph.Cinf == (echoes ? 0x4abc8a50u : 0x0c9669bcu) && skeleton.Bones.Count == (echoes ? 39 : 35)
            && skeleton.Bones.TryGetValue(rootId, out var rootBone) && rootBone.Parent == externalParent && rootBone.Name == "root",
            "Imported animation bank requires the complete original source skeleton.");
        var data = RetroCharacterAnimationArchive.Read(archive, new(source, description, graph), cinf, echoes);
        return new(root, modelSha, entry.SourceAnimationArchiveSha256!, data,
            new(new ReadOnlyDictionary<uint,RetroNativeBone>(skeleton.Bones.ToDictionary(p=>p.Key,p=>p.Value))), inputs, Stem, rootId, externalParent);
    }
    private static CharacterSourceAnimationArchive LoadNormalBall(string root,CharacterModelManifestEntry entry,string modelSha,bool final=false)
    {
        string modelPin=final?CharacterGeneratedFinalBallProfile.ModelSha:CharacterGeneratedNormalBallProfile.ModelSha;
        string archivePin=final?CharacterGeneratedFinalBallProfile.AnimationSha:CharacterGeneratedNormalBallProfile.AnimationSha;
        string graphPin=final?CharacterGeneratedFinalBallProfile.GraphSha:CharacterGeneratedNormalBallProfile.GraphSha;
        Require((final?CharacterGeneratedFinalBallProfile.IsEntry(entry):CharacterGeneratedNormalBallProfile.IsEntry(entry))
            && String.Equals(modelSha,modelPin,StringComparison.OrdinalIgnoreCase),"Exact own ball animation/composite owner required.");
        string stem=final?CharacterGeneratedFinalBallProfile.Stem:CharacterGeneratedNormalBallProfile.Stem;
        var inputs=new Dictionary<string,(string Sha,int Limit)>(StringComparer.Ordinal);
        byte[] Read(string name,string pin,int maximum)
        {
            byte[] bytes=CharacterModelPack.ReadGraphInput(root,name,maximum);
            string sha=RetroAnimationGraphProof.Sha(bytes);Require(sha==pin,"Normal ball source input changed: "+name);
            inputs.Add(name,(sha,maximum));return bytes;
        }
        byte[] description=Read(stem+".ancs-graph.json",graphPin,RetroAnimationGraphProof.MaximumProofBytes),
            source=Read(stem+".ancs.bin",CharacterGeneratedNormalBallProfile.AncsSha,RetroAnimationIndex.MaxAncsBytes),
            cinf=Read(stem+".cinf.bin",CharacterGeneratedNormalBallProfile.CinfSha,1024*1024),
            archive=Read(stem+".animations.bin",archivePin,RetroCharacterAnimationArchive.MaximumBytes);
        if(final)
        {
            byte[] receipt=Read(stem+".composite.json",CharacterGeneratedFinalBallProfile.ProvenanceSha,CharacterModelPack.MaximumManifestBytes);
            using var document=System.Text.Json.JsonDocument.Parse(receipt);
            var sourceInputs=document.RootElement.GetProperty("sourceInputs");
            Require(sourceInputs.EnumerateObject().Count()==14,"Complete fourteen-input Final composite receipt required.");
            foreach(var input in sourceInputs.EnumerateObject())
            {
                string name=input.Name;int limit=name.EndsWith(".glb",StringComparison.Ordinal)?checked((int)CharacterModelPack.MaximumModelBytes)
                    :name.EndsWith(".materials.bin",StringComparison.Ordinal)?RetroCharacterMaterialArchive.MaxArchiveBytes
                    :name.EndsWith(".animations.bin",StringComparison.Ordinal)?RetroCharacterAnimationArchive.MaximumBytes:CharacterModelPack.MaximumManifestBytes;
                if(inputs.TryGetValue(name,out var existing))Require(existing.Sha==input.Value.GetString(),"Final composite input identity changed.");
                else Read(name,input.Value.GetString()!,limit);
            }
        }
        var graph=RetroAnimationGraphProof.Verify(description,source);
        var skeleton=RetroCharacterSource.ReadSkeleton(cinf);
        Require(graph.SourceGameId=="G2ME01" && graph.SourceAncsId==0x654a3f97 && graph.Cmdl==0x2f2c1e6f
            && graph.Cskr==0x4403b1b9 && graph.Cinf==0xc7dbc44a && !graph.NamedGraphSemanticsAccepted
            && skeleton.Bones.Count==2 && skeleton.Bones.TryGetValue(0,out var bone) && bone.Name=="root" && bone.Parent==97
            && skeleton.Bones.TryGetValue(1,out var child) && child.Parent==0 && child.Name=="Skeleton_Root",
            "Normal ball requires its complete own original ANCS/CINF hierarchy.");
        var data=RetroCharacterAnimationArchive.Read(archive,new(source,description,graph),cinf,echoes:true);
        Require(data.Roots.Count==2 && data.Roots.All(r=>r.CharacterDeclared&&r.StrictPlay),"Both original normal ball motion roots must remain captured.");
        return new(root,modelSha,archivePin,data,
            new(new ReadOnlyDictionary<uint,RetroNativeBone>(skeleton.Bones.ToDictionary(p=>p.Key,p=>p.Value))),inputs,stem,0,97);
    }
    internal void Revalidate(string root, CharacterModelManifestEntry entry, string modelSha)
    {
        Require(root == _root && modelSha == _modelSha
            && entry.SourceAnimationArchive == _stem + ".animations.bin" && entry.SourceAnimationArchiveSha256 == Sha256,
            "Imported animation bank owner changed.");
        foreach (var input in _inputs)
            Require(CharacterModelPack.HashGraphInput(root, input.Key, input.Value.Limit) == input.Value.Sha,
                "Imported animation bank source input changed.");
    }
    internal RetroCharacterAnimationArchive.Clip Open(int ordinal)
    {
        if ((uint)ordinal >= Data.Roots.Count || !Data.Roots[ordinal].CharacterDeclared)
            throw new NotSupportedException("Source root is outside the selected character's declared animation inventory.");
        var clip = Data.OpenStrictPrimitive(ordinal);
        if (!clip.RigidScale) throw new NotSupportedException("Nonrigid source scale remains unsupported by this source-rig profile.");
        return clip;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
