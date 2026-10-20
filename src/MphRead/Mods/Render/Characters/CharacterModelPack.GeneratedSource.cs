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

    private static string ValidateGeneratedSourceProvenance(JsonElement receipt, CharacterModelManifestEntry entry,
        CharacterModelAsset asset, PrivateCharacterAppearance appearance, string sourceCharacter, string? verifiedModelSha)
    {
        bool Text(string name, string value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.String && p.GetString() == value;
        bool Number(string name, long value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long n) && n == value;
        bool Flag(string name, bool value) => receipt.TryGetProperty(name, out var p)
            && p.ValueKind == (value ? JsonValueKind.True : JsonValueKind.False);
        if (asset.Hunter != Hunter.Samus || !(asset.Part == CharacterModelPart.Biped || CharacterSourceCannonRecipe.IsGeneratedEntry(entry, verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, MaximumModelBytes))) || asset.Lod != 0
            || asset.Skinning != CharacterSkinningMode.Weighted4 || asset.PoseSource != CharacterPoseSource.SourceRigPreview
            || appearance != new PrivateCharacterAppearance("darksamus", "MP2DarkSamus") || sourceCharacter != "darksamus-echoes"
            || entry.MobileModel != null || entry.BoneMap.Count != 0 || entry.SourceAnimationArchive == null
            || !Text("kind", "generated-retro-source-rig-preview-v1") || !Number("writerVersion", RetroCharacterSourceGlbWriter.Version)
            || !Text("materialPolicy", RetroCharacterSourceGlbWriter.MaterialPolicy) || !Text("sourceBasis", "(-x,z,y)")
            || !Text("sourceCharacter", sourceCharacter) || !Text("sourceCharacterId", sourceCharacter)
            || !Text("sourceNativeName", "Dark_Samus") || !Text("sourcePart", "body") || !Text("sourceGameId", "G2ME01")
            || !Text("nativeHunter", "Samus") || !Text("characterId", appearance.CharacterId) || !Text("skinSource", appearance.SkinSource)
            || !Text("presentationPart", "biped") || !Flag("visualOnly", true)
            || !Number("sourceAncs", 0x39900a11) || !Number("sourceCmdl", 0xd2c3805b) || !Number("sourceCskr", 0xb9ec2f8d)
            || !Number("sourceCinf", 0x4abc8a50) || !Number("sourcePositionCount", 4245) || !Number("sourceTriangleCount", 6340)
            || !Number("sourceMaterialCount", 7) || !Number("sourceBoneCount", 39)
            || !Text("sourceAncsSha256", "7933e22a780b7f2cbee01be3ee5a4d48e6f08eb39df81608d638bee1729a5f7b")
            || !Text("sourceCmdlSha256", "477f7555d4101771e4349939916b770a379ea29175668ea7621c88e772a72958")
            || !Text("sourceCskrSha256", "acf1236458ec5d4ea57b1bee8f6053efb76c7514467b18c660bd6572fb2762ff")
            || !Text("sourceCinfSha256", "3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4")
            || !Text("sourceAnimationArchiveSha256", "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1")
            || entry.SourceAnimationArchiveSha256 != "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1")
            throw new InvalidDataException("Generated source preview does not match the qualified normal Echoes profile.");
        foreach (string key in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled", "firstPersonVerified",
            "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" })
            if (!Flag(key, false)) throw new InvalidDataException("Generated source preview cannot claim unperformed acceptance.");
        foreach (string key in new[] { "sourceCmdlSha256", "sourceCskrSha256" })
            if (!receipt.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || !ValidSha256(value.GetString()))
                throw new InvalidDataException("Generated source resource hash is missing.");
        if (receipt.TryGetProperty("sourceNativeKitSha256", out _) || receipt.TryGetProperty("sourceMappingSha256", out _))
            throw new InvalidDataException("Generated source preview cannot invent native retargeting inputs.");
        string sha = verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, MaximumModelBytes);
        if (!String.Equals(sha, GeneratedNormalSourceGlbSha256, StringComparison.OrdinalIgnoreCase)
            || !Text("sourceGlbSha256", GeneratedNormalSourceGlbSha256))
            throw new InvalidDataException("Generated source geometry does not match the reproducible qualified writer output.");
        return sha;
    }
}
