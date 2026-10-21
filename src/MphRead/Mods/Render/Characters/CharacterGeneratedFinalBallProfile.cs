using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Render.Characters;

// Closed separately from the original normal ball and Final's cannon. Pins
// were measured against independently decoded original source geometry.
internal static class CharacterGeneratedFinalBallProfile
{
    internal const string Stem="darksamus-overcharged-echoes.ball";
    internal const string MaterialId="darksamus-overcharged-echoes-ball";
    internal const string ModelSha="57158cb41d062bab7b93920016855c8fd7a8b899a48c8dde23e4838e4b95beab";
    internal const string ProvenanceSha="8eba953c52a647a6e4c8bea6c66b289bd5f4bc95abe692782959248522bb4a55";
    internal const string MaterialSha="28d5d167d48281c98234335eb8b6e766942d2832f6dbbcb35ffeb8854827e1eb";
    internal const string AnimationSha="05d491daf8b02ade697cbc225bdc145406d996a35bbb69fc52ea2e922b51f362";
    internal const string GraphSha="18b40d1d94ae6600cdcbd501040f0d4dd66b56ab7e34c7f1294a6481d36a8a40";
    internal static bool IsEntry(CharacterModelManifestEntry e) => ModelSha.Length==64 && ProvenanceSha.Length==64
        &&e.Hunter=="Samus"&&e.CharacterId=="darksamus"&&e.SkinSource=="MP2DarkSamusFinal"
        &&e.Part==CharacterModelPart.AlternateForm&&e.Lod==0&&e.Skinning==CharacterSkinningMode.Weighted4
        &&e.PoseSource==CharacterPoseSource.SourceRigPreview&&e.Model==Stem+".composite.glb"
        &&e.Provenance==Stem+".composite.json"&&e.ProvenanceSha256==ProvenanceSha
        &&e.SourceAnimationArchive==Stem+".animations.bin"&&e.SourceAnimationArchiveSha256==AnimationSha
        &&e.MobileModel==null&&e.BoneMap is {Count:0}&&e.SourceCannonReceipt==null&&e.SourceCannonSha256==null&&e.SourceGraphProof==null;
    internal static string Validate(JsonElement receipt,CharacterModelManifestEntry entry,CharacterModelAsset asset,
        PrivateCharacterAppearance appearance,string sourceCharacter,string sha)
    {
        if(!IsEntry(entry)||!String.Equals(sha,ModelSha,StringComparison.OrdinalIgnoreCase)
            ||appearance!=new PrivateCharacterAppearance("darksamus","MP2DarkSamusFinal")||sourceCharacter!="darksamus-overcharged-echoes"
            ||asset.Part!=CharacterModelPart.AlternateForm||asset.Hunter!=Hunter.Samus
            ||receipt.GetProperty("kind").GetString()!="generated-final-ball-composite-v1"
            ||receipt.GetProperty("sourceGlbSha256").GetString()!=ModelSha||receipt.GetProperty("materialArchiveSha256").GetString()!=MaterialSha
            ||receipt.GetProperty("sourceTriangleCount").GetInt32()!=8170||receipt.GetProperty("sourceMaterialCount").GetInt32()!=6
            ||receipt.GetProperty("sourceBoneCount").GetInt32()!=2||!receipt.GetProperty("staticSourceBake").GetBoolean()
            ||receipt.GetProperty("selection").GetProperty("policy").GetString()!=Import.RetroFinalBallSource.Policy)
            throw new InvalidDataException("Exact own generated Final composite ball and complete source inputs required.");
        return sha;
    }
}
