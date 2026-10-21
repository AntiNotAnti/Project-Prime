using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Render.Characters;

// Exact locally generated Rev2 resources. These IDs carry presentation authority only.
internal sealed record CharacterGeneratedDroneProfile(string Id, string Skin, uint Cmdl, uint Cskr,
    string ModelSha, string ProvenanceSha, string AnimationSha, string GraphSha, string MaterialSha)
{
    internal string Stem => Id + ".drone";
    internal const string AncsSha = "c047384a7de665b548904f50ecb7144354bf765fb986243bdfab1692ba453795";
    internal const string CinfSha = "c15a40b472878958c71a84fa7912ca7535aef9c9f5e3692b7738f1707ca00c9c";
    internal const int ClipOrdinal = 13;
    internal const uint ClipId = 0xb45b6445;
    // A closed primitive list; retaining a raw bank never grants its other roots.
    internal static bool MatchesClip(int ordinal,uint id,string sha) => (ordinal,id,sha) switch
    {
        (13,0xb45b6445,"d714b3f01328ad897a82ed67de2f3e20e0922283bd9e453c269939684cc46cb0") => true,
        (75,0xdbd60f49,"eb724be315c52be55b7d7213cbfe386f886c497fd86ad18bfce8912bc7643cce") => true,
        (73,0x90d23d02,"1ed47ddcdb8105a7f914c2b5de87658c32b9bf4daf8aec34dee19dbcbcd2991d") => true,
        (74,0x83618e64,"707500e064576b053e04d18c63ee5007ffea8810dc979a105df9ec5dcdebe35a") => true,
        (29,0x6d6b6f9f,"b9fd4217ace08e9431ead637825f8125e28978c212c465395e0c53edb6d8675c") => true,
        (4,0x1f21510a,"857458dfcc824d8d57a19762eb5f95e5f9eb26fcd2725a43c19e5013510012c7") => true,
        (3,0xaff82288,"fa217ba84b3b7d0d871fbdd9b3c2798951ed3d7e01ef430e08ca855571a046e1") => true,
        (2,0xfa0107c9,"898e9a5304cf9f38fbca896bc8a22b5acebadb8e3c8ec4ccc2a8baf783483fa7") => true,
        (5,0xe87fa8e3,"891ed408224bee08beeae347dc527c68028bfe694add7a861b5fcf6ae33680e3") => true,
        (6,0xab7bdfeb,"b1c6873384fe362de16cbd8c8df1e90b7c4612c8f613852729d713d536c8bbc8") => true,
        (1,0x28e34205,"bfa409602fa9b4858f2aded40ca7b81257b209d117b12a99305e7c10f9203c86") => true,
        _ => false
    };
    private static readonly CharacterGeneratedDroneProfile Alpha = new("trooper-drone-alpha", "MP1AlphaDrone", 0xef920604, 0x84bda9d2,
        "58ed9aba3bb4c8ffce76994db1035fe20914030cdce9549e7cc88c46b8ec9713", "2fa2cbc0d1e85bbe46c698fae9a866f243e55ce26c7db6c6e35172215e88a564",
        "d87948f0819c8003602058ee0062e03a9c6bf2881f6b21d41ac90c873e58c87c", "c2fa136f72e637bb732b1c5a12c5bc248c0bff0434c39b3e148694cd9e9c8126",
        "c6a208fc8ae2dd92d0998ce22bb43b500c9a19eb9c0c22fca38ed3cb21d6c297");
    private static readonly CharacterGeneratedDroneProfile Gamma = new("trooper-drone-gamma", "MP1GammaDrone", 0x0b6586cb, 0x604a291d,
        "0b0c40e8deb91adefcab6bb9709af2c9316154a9b17ec453dfcc14d68459fb36", "76ff8d171f473b11e85585427df708bb8685bc480e11ca41453a1c6a030d965f",
        "edb61a8f580c3ed9c217fb09e6d8872f9f3aa2e40c9fba341a831982b405448d", "23d26f38c297188dee331d74d64fb424aed99cf8505bbe723ccfb226ac05c05f",
        "d21d9f616b9e6c49b6f235859f4c9b09dde1a79a6e7310eb80f2b225ee513e58");
    internal static CharacterGeneratedDroneProfile? Find(string? id) => id switch
    { "trooper-drone-alpha" => Alpha, "trooper-drone-gamma" => Gamma, _ => null };
    internal static CharacterGeneratedDroneProfile? FromSkin(string? skin) => skin switch
    { "MP1AlphaDrone" => Alpha, "MP1GammaDrone" => Gamma, _ => null };
    internal bool IsEntry(CharacterModelManifestEntry e) => e.Hunter == "Weavel" && e.CharacterId == "trooper"
        && e.SkinSource == Skin && e.Part == CharacterModelPart.AlternateForm && e.Lod == 0
        && e.Model == Stem + ".source.glb" && e.Provenance == Stem + ".source.json" && e.ProvenanceSha256 == ProvenanceSha
        && e.SourceAnimationArchive == Stem + ".animations.bin" && e.SourceAnimationArchiveSha256 == AnimationSha
        && e.Skinning == CharacterSkinningMode.Weighted4 && e.PoseSource == CharacterPoseSource.SourceRigPreview
        && e.MobileModel == null && e.BoneMap is { Count: 0 } && e.SourceGraphProof == null
        && e.SourceCannonReceipt == null && e.SourceCannonSha256 == null;
    internal string ValidateEntry(JsonElement receipt, CharacterModelManifestEntry entry, CharacterModelAsset asset,
        PrivateCharacterAppearance appearance, string? verifiedSha)
    {
        string sha = verifiedSha ?? CharacterSourceProofIO.HashFile(asset.ModelPath, CharacterModelPack.MaximumModelBytes);
        if (!IsEntry(entry) || sha != ModelSha || appearance != new PrivateCharacterAppearance("trooper", Skin)
            || asset.Hunter != Hunter.Weavel || asset.Part != CharacterModelPart.AlternateForm
            || receipt.GetProperty("format").GetInt32() != 1
            || receipt.GetProperty("kind").GetString() != "generated-retro-drone-authoring-v1")
            throw new InvalidDataException("Exact generated Prime Rev2 drone profile required.");
        return sha;
    }
}

internal sealed partial class CharacterModelPack
{
    internal bool HasSourceDrone(CharacterModelAsset asset) => asset.Hunter == Hunter.Weavel
        && asset.Part == CharacterModelPart.AlternateForm && asset.Lod == 0
        && CharacterGeneratedDroneProfile.FromSkin(asset.PrivateAppearance?.SkinSource) is { } profile
        && _sourceProofs.TryGetValue(asset, out var proof) && proof.SourceCharacter == profile.Id
        && proof.ModelSha == profile.ModelSha && _sourceAnimations.TryGetValue(asset, out var bank) && bank.IsDrone;
}
