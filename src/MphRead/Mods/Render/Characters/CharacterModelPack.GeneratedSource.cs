using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterModelPack
{
    // A reproducible importer output pin, independently qualified against the
    // owned source. This is separate from the legacy manually authored GLB.
    internal const string GeneratedNormalSourceGlbSha256 = "38b03806faf22851efd3caeb4fcd22b900556b5fc4b21a2051b075b63f6fb8ca";

    internal const string GeneratedFinalSourceGlbSha256 = "ff1866fd7c92c7aad0326381e059119febd0993d63aacc85b45400b019af5164";

    private static string ValidateGeneratedSourceProvenance(JsonElement receipt, CharacterModelManifestEntry entry,
        CharacterModelAsset asset, PrivateCharacterAppearance appearance, string sourceCharacter, string? verifiedModelSha)
    {
        if (asset.Part == CharacterModelPart.AlternateForm)
            return CharacterGeneratedNormalBallProfile.Validate(receipt,entry,asset,appearance,sourceCharacter,
                verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath,MaximumModelBytes));
        bool final = sourceCharacter == "darksamus-overcharged-echoes";
        string modelPin = final ? GeneratedFinalSourceGlbSha256 : GeneratedNormalSourceGlbSha256;
        string bankPin = final ? "15f102868334b98e271050493bbfdd996ca3d7121a6f4b04d54f6308b5d07b0d" : "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1";
        bool Text(string name, string value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.String && p.GetString() == value;
        bool Number(string name, long value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long n) && n == value;
        bool Flag(string name, bool value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == (value ? JsonValueKind.True : JsonValueKind.False);
        string modelSha = verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, MaximumModelBytes);
        bool cannon = final ? CharacterSourceCannonRecipe.IsGeneratedFinalEntry(entry,modelSha) : CharacterSourceCannonRecipe.IsGeneratedEntry(entry,modelSha);
        if (asset.Hunter != Hunter.Samus || !(asset.Part == CharacterModelPart.Biped || cannon) || asset.Lod != 0
            || asset.Skinning != CharacterSkinningMode.Weighted4 || asset.PoseSource != CharacterPoseSource.SourceRigPreview
            || appearance != new PrivateCharacterAppearance("darksamus", final ? "MP2DarkSamusFinal" : "MP2DarkSamus")
            || sourceCharacter is not ("darksamus-echoes" or "darksamus-overcharged-echoes")
            || final && (entry.SourceGraphProof != null || !cannon && (entry.SourceCannonReceipt != null || entry.SourceCannonSha256 != null))
            || entry.MobileModel != null || entry.BoneMap.Count != 0 || entry.SourceAnimationArchive == null
            || !Text("kind", "generated-retro-source-rig-preview-v1") || !Number("writerVersion", RetroCharacterSourceGlbWriter.Version)
            || !Text("materialPolicy", RetroCharacterSourceGlbWriter.MaterialPolicy) || !Text("sourceBasis", "(-x,z,y)")
            || !Text("sourceCharacter", sourceCharacter) || !Text("sourceCharacterId", sourceCharacter)
            || !Text("sourceNativeName", final ? "Battle_3" : "Dark_Samus") || !Text("sourcePart", "body") || !Text("sourceGameId", "G2ME01")
            || !Text("nativeHunter", "Samus") || !Text("characterId", appearance.CharacterId) || !Text("skinSource", appearance.SkinSource)
            || !Text("presentationPart", "biped") || !Flag("visualOnly", true)
            || !Number("sourceAncs", 0x39900a11) || !Number("sourceCmdl", final ? 0xfaa12c05 : 0xd2c3805b) || !Number("sourceCskr", final ? 0x918e83d3 : 0xb9ec2f8d)
            || !Number("sourceCinf", 0x4abc8a50) || !Number("sourcePositionCount", final ? 4849 : 4245) || !Number("sourceTriangleCount", final ? 12734 : 6340)
            || !Number("sourceMaterialCount", final ? 9 : 7) || !Number("sourceBoneCount", 39)
            || !Text("sourceAncsSha256", "7933e22a780b7f2cbee01be3ee5a4d48e6f08eb39df81608d638bee1729a5f7b")
            || !Text("sourceCmdlSha256", final ? "f49d5a84271eb08ed780170887e4301af428303c24fda87a55ac60cf865b17db" : "477f7555d4101771e4349939916b770a379ea29175668ea7621c88e772a72958")
            || !Text("sourceCskrSha256", final ? "ae7f5ddf6bef6c9c8aa5903757e7e6ba8e0bd70693806898d7e6f8531dc646a1" : "acf1236458ec5d4ea57b1bee8f6053efb76c7514467b18c660bd6572fb2762ff")
            || !Text("sourceCinfSha256", "3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4")
            || !Text("sourceAnimationArchiveSha256", bankPin)
            || entry.SourceAnimationArchiveSha256 != bankPin)
            throw new InvalidDataException("Generated source preview does not match the qualified generated Echoes profile.");
        foreach (string key in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled", "firstPersonVerified",
            "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" })
            if (!Flag(key, false)) throw new InvalidDataException("Generated source preview cannot claim unperformed acceptance.");
        foreach (string key in new[] { "sourceCmdlSha256", "sourceCskrSha256" })
            if (!receipt.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || !ValidSha256(value.GetString()))
                throw new InvalidDataException("Generated source resource hash is missing.");
        if (receipt.TryGetProperty("sourceNativeKitSha256", out _) || receipt.TryGetProperty("sourceMappingSha256", out _))
            throw new InvalidDataException("Generated source preview cannot invent native retargeting inputs.");
        string sha = verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, MaximumModelBytes);
        if (!String.Equals(sha, modelPin, StringComparison.OrdinalIgnoreCase)
            || !Text("sourceGlbSha256", modelPin))
            throw new InvalidDataException("Generated source geometry does not match the reproducible qualified writer output.");
        return sha;
    }
}
