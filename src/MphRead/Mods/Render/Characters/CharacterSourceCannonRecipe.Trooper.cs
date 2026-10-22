using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Import;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterSourceCannonRecipe
{
    internal static bool IsGeneratedTrooperEntry(CharacterModelManifestEntry entry, string modelSha,
        CharacterGeneratedTrooperProfile profile)
        => entry.Part == CharacterModelPart.ViewModel && entry.Lod == 0 && entry.Hunter == "Weavel"
            && entry.CharacterId == "trooper" && entry.SkinSource == profile.Skin
            && entry.PoseSource == CharacterPoseSource.SourceRigPreview
            && entry.SourceCannonReceipt == profile.Id + ".cannon-source.json"
            && profile.CannonReceiptSha.Length == 64 && entry.SourceCannonSha256 == profile.CannonReceiptSha
            && String.Equals(modelSha, profile.ModelSha, StringComparison.OrdinalIgnoreCase);

    private static CharacterSourceCannonRecipe LoadGeneratedTrooper(string root, CharacterModelManifestEntry entry,
        string modelSha, CharacterGeneratedTrooperProfile profile)
    {
        Require(IsGeneratedTrooperEntry(entry, modelSha, profile), "Exact generated Trooper cannon profile required.");
        byte[] bytes = CharacterModelPack.ReadCannonInput(root, entry.SourceCannonReceipt!, 1024 * 1024);
        Require(Sha(bytes) == profile.CannonReceiptSha, "Generated Trooper cannon recipe changed.");
        using var document = JsonDocument.Parse(bytes); var r = document.RootElement;
        Require(r.GetProperty("format").GetInt32() == 2 && r.GetProperty("mode").GetString() == "generatedSourceCannonAuthoring"
            && r.GetProperty("presentationPart").GetString() == "viewModel" && r.GetProperty("sourceCharacter").GetString() == profile.Id
            && r.GetProperty("writerVersion").GetInt32() == RetroCharacterSourceGlbWriter.Version
            && r.GetProperty("materialPolicy").GetString() == RetroCharacterSourceGlbWriter.MaterialPolicy
            && r.GetProperty("sourceBasis").GetString() == "(-x,z,y)"
            && r.GetProperty("clipOrdinal").GetInt32() == 1 && r.GetProperty("clipResourceId").GetUInt32() == 0x5528972b
            && r.GetProperty("clipSeconds").GetDouble() == 0 && r.GetProperty("sourceElbowId").GetInt32() == 23
            && r.GetProperty("sourceElbowName").GetString() == "R_elbow"
            && r.GetProperty("sourceParticleLocatorId").GetInt32() == 34 && r.GetProperty("sourceParticleLocatorName").GetString() == "R_gun_LCTR"
            && r.GetProperty("sourceUnitsUnscaled").GetBoolean() && r.GetProperty("calibrationOnly").GetBoolean()
            && !r.GetProperty("gameplayMuzzleChanged").GetBoolean() && !r.GetProperty("publicSelectionEnabled").GetBoolean()
            && !r.GetProperty("networkReplayEnabled").GetBoolean() && !r.GetProperty("firstPersonVerified").GetBoolean(),
            "Trooper receipt must retain the original fixed authoring facts.");
        var inputs = r.GetProperty("sourceInputs").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        string[] names = [profile.Id + ".source.glb", profile.Id + ".source.json", profile.Id + ".ancs-graph.json",
            profile.Id + ".ancs.bin", profile.Id + ".cinf.bin", profile.Id + ".animations.bin"];
        Require(inputs.Keys.Order().SequenceEqual(names.Order()) && entry.Model == names[0] && entry.Provenance == names[1]
            && inputs[names[0]] == profile.ModelSha && inputs[names[1]] == profile.ProvenanceSha
            && inputs[names[2]] == profile.GraphSha && inputs[names[3]] == CharacterGeneratedTrooperProfile.AncsSha
            && inputs[names[4]] == CharacterGeneratedTrooperProfile.CinfSha && inputs[names[5]] == profile.AnimationSha,
            "Cannon cut must bind the same appearance's complete original source inputs.");
        float[] m = r.GetProperty("toCannon").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Require(m.Length == 16, "Complete source cannon frame required.");
        var basis = new Matrix4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
        CharacterSourceRig.RequireRigid(basis);
        int[][] triangles = r.GetProperty("triangles").EnumerateArray().Select(a => a.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToArray();
        Require(triangles.Length == 3 && triangles.Select(a => a.Length).SequenceEqual(new[] { 10, 32, 164 })
            && triangles.SelectMany(a => a).All(i => i is >= 0 and < 1970)
            && triangles.SelectMany(a => a).Distinct().Count() == 206, "Complete original Trooper elbow cut required.");
        var result = new CharacterSourceCannonRecipe(profile.CannonReceiptSha, basis, inputs, triangles, generated: true, trooper: profile);
        result.RevalidateGeneratedTrooper(root, entry, modelSha, profile); return result;
    }
    private void RevalidateGeneratedTrooper(string root, CharacterModelManifestEntry entry, string modelSha,
        CharacterGeneratedTrooperProfile profile)
    {
        Require(IsGeneratedTrooperEntry(entry, modelSha, profile) && ReceiptSha256 == profile.CannonReceiptSha
            && Sha(CharacterModelPack.ReadCannonInput(root, entry.SourceCannonReceipt!, 1024 * 1024)) == ReceiptSha256,
            "Same immutable generated Trooper cannon owner required.");
        foreach (var input in _inputs)
            Require((input.Key.EndsWith(".glb", StringComparison.Ordinal) ? modelSha
                : Sha(CharacterModelPack.ReadCannonInput(root, input.Key, input.Key.EndsWith(".animations.bin", StringComparison.Ordinal)
                    ? RetroCharacterAnimationArchive.MaximumBytes : input.Key.EndsWith(".ancs.bin", StringComparison.Ordinal)
                        ? RetroAnimationIndex.MaxAncsBytes : 1024 * 1024))) == input.Value,
                "Generated Trooper cannon source input changed: " + input.Key);
    }
    private CharacterWeightedModelData DeriveGeneratedTrooper(CharacterModelPack.SourcePreviewLease lease,
        CharacterModelAsset asset, CharacterWeightedModelData source)
    {
        lease.Require(asset);
        Require(ReferenceEquals(lease.SourceCannon, this) && asset.Part == CharacterModelPart.ViewModel
            && asset.PrivateAppearance == new PrivateCharacterAppearance("trooper", GeneratedTrooper!.Skin)
            && source.SourceCannon == null && source.SourceRig is { Nodes.Count: 36 } && source.Joints.Count == 35
            && lease.SourceAnimations is { RootBoneId: 3, ExternalParentId: 2 }, "Complete source Trooper cannon lease/rig required.");
        var clip = CharacterSourceClip.Load(lease, asset, source.SourceRig!, 1); var pose = clip.Sample(0);
        Require(clip.ResourceId == 0x5528972b && clip.ContentSha256 == "5b6540fbfdcbf37edd24133b4475468e0f0199a744e3159df2f0b942c498ccc7"
            && CharacterSourceClip.MatrixDistance(GeneratedBasis(source, pose, 23, 34, "R_gun_LCTR"), ToCannon) < .00001f,
            "Original Trooper gunloop pose must reproduce the fixed cannon frame.");
        var cut = SelectGeneratedCut(source, 23);
        Require(cut.Selected.Length == 3 && cut.Selected.Select((ids, material) => ids.SequenceEqual(_triangles[material])).All(v => v),
            "Original whole-triangle Trooper cut differs from its appearance receipt.");
        lease.Require(asset);
        return source with { Primitives = cut.Primitives, SourceCannon = this, SourceAuthoringPose = Array.AsReadOnly(pose.ToArray()) };
    }
}
