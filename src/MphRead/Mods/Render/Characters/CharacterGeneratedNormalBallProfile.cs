using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Render.Characters;

// Exact original normal Echoes ball. Final's procedural vein/heart construction
// is a different source policy and cannot borrow this capability.
internal static class CharacterGeneratedNormalBallProfile
{
    internal const string Stem = "darksamus-echoes.ball";
    internal const string MaterialId = "darksamus-echoes-ball";
    internal const string ModelSha = "eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9";
    internal const string AnimationSha = "a5e6e99d1b9fee0d9c7c82537811281373159756be58e6c9e76b409f02c38b12";
    internal const string GraphSha = "5685f7f5eff0efdfe4659e848305ceb7045f94065eab15b90dd26ba256dfbc8b";
    internal const string AncsSha = "46ddcf531037bd4ae3f1949f0ecc7febe6deede70e5d19ddd720e5a7cfa21ad8";
    internal const string CinfSha = "08ff767eecccfa572cf9424862ad8a059128eaf035bcc65c8f9faa77fc09cbce";
    // Independently measured importer outputs; remain closed until measured.
    internal const string ProvenanceSha = "7afe18290250fe745579a931a968204f3dd136da123c7ae3f89def8f6c2a924a";
    internal const string MaterialSha = "fadaf0787fa1c9df6b0cf5765db78547953908dc1b24c9b1cba76217c16746b5";
    internal const int ClipOrdinal = 1;
    internal const uint ClipId = 0x3bf596d0;
    internal const string ClipSha = "792bb9d335da503cf1fe0eb59c65a01f7c47ee2aebb48385e5d44c77736e470e";
    internal static bool IsEntry(CharacterModelManifestEntry e) => ProvenanceSha.Length == 64
        && e.Hunter == "Samus" && e.CharacterId == "darksamus" && e.SkinSource == "MP2DarkSamus"
        && e.Part == CharacterModelPart.AlternateForm && e.Lod == 0 && e.Skinning == CharacterSkinningMode.Weighted4
        && e.PoseSource == CharacterPoseSource.SourceRigPreview && e.Model == Stem + ".source.glb"
        && e.Provenance == Stem + ".source.json" && e.ProvenanceSha256 == ProvenanceSha
        && e.SourceAnimationArchive == Stem + ".animations.bin" && e.SourceAnimationArchiveSha256 == AnimationSha
        && e.MobileModel == null && e.BoneMap is {Count:0} && e.SourceCannonReceipt == null && e.SourceCannonSha256 == null
        && e.SourceGraphProof == null;
    internal static string Validate(JsonElement receipt,CharacterModelManifestEntry entry,CharacterModelAsset asset,
        PrivateCharacterAppearance appearance,string sourceCharacter,string sha)
    {
        if (!IsEntry(entry) || !String.Equals(sha,ModelSha,StringComparison.OrdinalIgnoreCase)
            || appearance != CharacterSourcePreviewSelection.Appearance || sourceCharacter != "darksamus-echoes"
            || asset.Part != CharacterModelPart.AlternateForm || asset.Hunter != Hunter.Samus
            || receipt.GetProperty("format").GetInt32() != 2 || receipt.GetProperty("sourcePart").GetString() != "ball"
            || receipt.GetProperty("presentationPart").GetString() != "alternateForm"
            || receipt.GetProperty("sourceBoneCount").GetInt32() != 2 || receipt.GetProperty("sourceTriangleCount").GetInt32() != 2220)
            throw new InvalidDataException("Exact generated normal Echoes ball/provenance/animation profile required.");
        return sha;
    }
}

internal sealed partial class CharacterModelPack
{
    internal bool HasSourceBall(CharacterModelAsset asset) => asset.Part == CharacterModelPart.AlternateForm
        && asset.PrivateAppearance == CharacterSourcePreviewSelection.Appearance && asset.Hunter == Hunter.Samus
        && _sourceProofs.TryGetValue(asset,out var proof) && proof.SourceCharacter == "darksamus-echoes"
        && String.Equals(proof.ModelSha,CharacterGeneratedNormalBallProfile.ModelSha,StringComparison.OrdinalIgnoreCase)
        && _sourceAnimations.ContainsKey(asset);
}
