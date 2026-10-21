using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

// Reproducible outputs independently measured through the pinned GM8E01 Rev2
// importer. A structurally valid replacement is not a generated source profile.
// These are local presentation facts, never character/gameplay or wire IDs.
internal sealed record CharacterGeneratedTrooperProfile(string Id, string Skin, string NativeName,
    uint Cmdl, uint Cskr, string ModelSha, string ProvenanceSha, string AnimationSha,
    string GraphSha, string MaterialSha)
{
    internal const string AncsSha = "7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4";
    internal const string CinfSha = "937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9";
    // Filled from independently compared actual importer output before admission.
    internal string CannonReceiptSha => Id switch
    {
        "trooper-power" => "2b128462891c9aa85ebae92b68306345386c7dcaeabdee043ff522de69a4ecc5",
        "trooper-ice" => "2074a896f47f0bd72402076afa1347287958bfef5682a225a287e4a83afe9363",
        "trooper-wave" => "1c1cb397a741ebab61d1a36d785c194b4bad7f764c9d6684d86ec9f50e42c79b",
        "trooper-plasma" => "1f8a1dc3eaf7d5a43cdc3d7e2fde28cdff63b95040f066fad57e1eb2d86584c1",
        _ => ""
    };
    private static readonly CharacterGeneratedTrooperProfile Power = new("trooper-power", "MP1PowerTrooper", "power_trooper", 0xf8691b86, 0x9346b450,
        "54f298805409fa12f34c34203cf7db2f5085f5b5ec4410ac04b3d329da71f3e8", "512da9abe64956aa8d7765611cef9dc596eea96c5403353013cfad8bbe4717c3",
        "a1b05e777345420456b1c2ace0c961a07bb2c54992536d30ef656e78593257b8", "303b654c5fbe3d98165ee0ef13e62a8acf9117915c687868b124c5ba6b69f0a0",
        "50f5acc05dc966847c96f05e9bafa045777510e77d6e3a3f224227020a9c553a");
    private static readonly CharacterGeneratedTrooperProfile Ice = new("trooper-ice", "MP1IceTrooper", "ice_trooper", 0x45e6de5b, 0x2ec9718d,
        "c3f7c8f9c77824937522db1199970a5f9d0be94e8dbfdbe35a66b7c5adfac307", "0aaa87d7b142f8ce16e71d9bf7e5741c254720cf758e6818a651055c66078364",
        "63ef357c9c0c945bb3ff97bf23b891cc028cd3de27b45dd9148d574cf927ce44", "fe009241436842decaeb9c578cc23bab5f8c4f5cbfa7af924c14384d976cf3ad",
        "16f6f537954145940030874afb6d8dae3187de08e4f5997568085c3ad96701f0");
    private static readonly CharacterGeneratedTrooperProfile Wave = new("trooper-wave", "MP1WaveTrooper", "wave_trooper", 0x5eb5adfa, 0x359a022c,
        "6a307e91a5bfdd82a0f1ae904b3179917fc7f3da70e2c3546ae2feffec32653b", "25ed588b1413e63b6a6631a9c2797d41b35173f8de70d0994ddbdc5dda046c81",
        "6b95ba35dce96cd3fbfd6de5709b51bef89bbea1777ae3725556d9f484f648d7", "76e049702e73ca0907d4ff8e80331b7bbcd77070f39b23f0be7a85af2c8f7355",
        "d84ae5a8ecfe71703020dfc0348fcac23713726e5af03c9fc26942654c1d2c6b");
    private static readonly CharacterGeneratedTrooperProfile Plasma = new("trooper-plasma", "MP1PlasmaTrooper", "plasma_trooper", 0x202ac74c, 0x4b05689a,
        "7877c9f7b7a27dc8c804c04ea5dc03e364504e71f3c7e744d58e95e688e4e96f", "29806c36a3283672d6930d7a84e4a7ab5a48b5d95c66ef53846b2b55c2ccd858",
        "3ee7cd22d0c64374c28c08ba321c4b9d62d8b3ee653c04cd922836e089aeb7b2", "0a759359ccdf81f510962282f0458f3b341331743678952b649f08c07d84a9f5",
        "0eb01263a7dad10519af2c70e3158fd623b3a56bf18251c625e7761e651a5933");

    internal static CharacterGeneratedTrooperProfile? Find(string? id) => id switch
    { "trooper-power" => Power, "trooper-ice" => Ice, "trooper-wave" => Wave, "trooper-plasma" => Plasma, _ => null };
    internal static CharacterGeneratedTrooperProfile? FromSkin(string? skin) => skin switch
    { "MP1PowerTrooper" => Power, "MP1IceTrooper" => Ice, "MP1WaveTrooper" => Wave, "MP1PlasmaTrooper" => Plasma, _ => null };

    internal string ValidateEntry(JsonElement receipt, CharacterModelManifestEntry entry, CharacterModelAsset asset,
        PrivateCharacterAppearance appearance, string? verifiedModelSha)
    {
        string sha = verifiedModelSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, CharacterModelPack.MaximumModelBytes);
        bool cannon = CharacterSourceCannonRecipe.IsGeneratedTrooperEntry(entry, sha, this);
        // The retained authoring provenance remains truthful: runtimeInstall is
        // false and its unqualified fields never self-grant actor authority.
        // Exact whole-file qualification covers every original source fact.
        if (entry.Provenance != Id + ".source.json" || entry.ProvenanceSha256 != ProvenanceSha
            || asset.Hunter != Hunter.Weavel || !(asset.Part == CharacterModelPart.Biped || cannon) || asset.Lod != 0
            || asset.Skinning != CharacterSkinningMode.Weighted4 || asset.PoseSource != CharacterPoseSource.SourceRigPreview
            || appearance != new PrivateCharacterAppearance("trooper", Skin) || entry.MobileModel != null
            || entry.BoneMap.Count != 0 || !cannon && (entry.SourceCannonReceipt != null || entry.SourceCannonSha256 != null)
            || entry.SourceGraphProof != null || entry.SourceAnimationArchive != Id + ".animations.bin"
            || entry.SourceAnimationArchiveSha256 != AnimationSha || sha != ModelSha
            || receipt.GetProperty("format").GetInt32() != 1
            || receipt.GetProperty("kind").GetString() != "generated-retro-trooper-authoring-v1")
            throw new InvalidDataException("Exact generated Prime Rev2 Trooper body/provenance/animation profile required.");
        return sha;
    }
}
