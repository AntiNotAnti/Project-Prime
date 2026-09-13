using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Cosmetics;

/// <summary>
/// Validated immutable cosmetic metadata. Numeric IDs are compact, published
/// wire identities; stable keys remain the persistence boundary. Published
/// IDs must never be reassigned.
/// </summary>
public sealed class CosmeticCatalog
{
    public const ushort CurrentVersion = 1;

    private readonly FrozenDictionary<ushort, SkinDefinition> _skinsById;
    private readonly FrozenDictionary<ushort, ArmorEffectDefinition> _armorById;
    private readonly FrozenDictionary<ushort, DeathEffectDefinition> _deathsById;

    public ushort CatalogVersion { get; }
    public string CatalogHash { get; }
    public IReadOnlyDictionary<string, SkinDefinition> Skins { get; }
    public IReadOnlyDictionary<string, ArmorEffectDefinition> ArmorEffects { get; }
    public IReadOnlyDictionary<string, DeathEffectDefinition> DeathEffects { get; }

    public static CosmeticCatalog Empty { get; } = new([], [], []);
    public static CosmeticCatalog BuiltIn { get; } = CreateBuiltIn();

    public CosmeticCatalog(
        IEnumerable<SkinDefinition> skins,
        IEnumerable<ArmorEffectDefinition> armorEffects,
        IEnumerable<DeathEffectDefinition> deathEffects,
        ushort catalogVersion = CurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(skins);
        ArgumentNullException.ThrowIfNull(armorEffects);
        ArgumentNullException.ThrowIfNull(deathEffects);
        if (catalogVersion == 0) throw new ArgumentOutOfRangeException(nameof(catalogVersion));
        CatalogVersion = catalogVersion;

        SkinDefinition[] skinValues = skins.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        ArmorEffectDefinition[] armorValues = armorEffects.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        DeathEffectDefinition[] deathValues = deathEffects.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        if (skinValues.Length > CosmeticPackageLimits.MaximumEntriesPerKind
            || armorValues.Length > CosmeticPackageLimits.MaximumEntriesPerKind
            || deathValues.Length > CosmeticPackageLimits.MaximumEntriesPerKind)
            throw new ArgumentException("A cosmetic category exceeds the catalog entry limit.");

        ValidateSkins(skinValues);
        ValidateArmor(armorValues);
        ValidateDeaths(deathValues);
        Skins = skinValues.ToFrozenDictionary(value => value.Key, StringComparer.Ordinal);
        ArmorEffects = armorValues.ToFrozenDictionary(value => value.Key, StringComparer.Ordinal);
        DeathEffects = deathValues.ToFrozenDictionary(value => value.Key, StringComparer.Ordinal);
        _skinsById = skinValues.ToFrozenDictionary(value => value.Id);
        _armorById = armorValues.ToFrozenDictionary(value => value.Id);
        _deathsById = deathValues.ToFrozenDictionary(value => value.Id);
        CatalogHash = ComputeHash(catalogVersion, skinValues, armorValues, deathValues);
    }

    public static string DefaultSkinKey(Hunter hunter) => CosmeticKeys.DefaultSkin(hunter);

    public bool TryGetSkin(string key, out SkinDefinition definition)
        => Skins.TryGetValue(key, out definition!);

    public bool TryGetSkin(ushort id, out SkinDefinition definition)
    {
        if (id != 0 && _skinsById.TryGetValue(id, out definition!)) return true;
        definition = null!;
        return false;
    }

    public bool TryGetArmorEffect(string key, out ArmorEffectDefinition definition)
        => ArmorEffects.TryGetValue(key, out definition!);

    public bool TryGetArmorEffect(ushort id, out ArmorEffectDefinition definition)
    {
        if (id != 0 && _armorById.TryGetValue(id, out definition!)) return true;
        definition = null!;
        return false;
    }

    public bool TryGetDeathEffect(string key, out DeathEffectDefinition definition)
        => DeathEffects.TryGetValue(key, out definition!);

    public bool TryGetDeathEffect(ushort id, out DeathEffectDefinition definition)
    {
        if (id != 0 && _deathsById.TryGetValue(id, out definition!)) return true;
        definition = null!;
        return false;
    }

    public bool TryResolve(CosmeticLoadout loadout, Hunter hunter,
        out CosmeticLoadoutIds ids, out CosmeticLoadoutIssue issue)
    {
        ids = default;
        issue = CosmeticLoadoutIssue.None;
        if (hunter > Hunter.Guardian || !CosmeticId.IsValid(loadout.SkinKey))
        {
            issue = CosmeticLoadoutIssue.InvalidSkin;
            return false;
        }

        ushort skinId;
        if (String.Equals(loadout.SkinKey, DefaultSkinKey(hunter), StringComparison.Ordinal)) skinId = 0;
        else if (!TryGetSkin(loadout.SkinKey, out SkinDefinition skin))
        {
            issue = CosmeticLoadoutIssue.InvalidSkin;
            return false;
        }
        else if (skin.Hunter != hunter)
        {
            issue = CosmeticLoadoutIssue.SkinHunterMismatch;
            return false;
        }
        else skinId = skin.Id;

        ushort armorId;
        if (String.Equals(loadout.ArmorEffectKey, CosmeticKeys.NoArmorEffect,
            StringComparison.Ordinal)) armorId = 0;
        else if (!CosmeticId.IsValid(loadout.ArmorEffectKey)
            || !TryGetArmorEffect(loadout.ArmorEffectKey, out ArmorEffectDefinition armor))
        {
            issue = CosmeticLoadoutIssue.InvalidArmorEffect;
            return false;
        }
        else armorId = armor.Id;

        ushort deathId;
        if (String.Equals(loadout.DeathEffectKey, CosmeticKeys.DefaultDeathEffect,
            StringComparison.Ordinal)) deathId = 0;
        else if (!CosmeticId.IsValid(loadout.DeathEffectKey)
            || !TryGetDeathEffect(loadout.DeathEffectKey, out DeathEffectDefinition death))
        {
            issue = CosmeticLoadoutIssue.InvalidDeathEffect;
            return false;
        }
        else if (death.Hunter is Hunter deathHunter && deathHunter != hunter)
        {
            issue = CosmeticLoadoutIssue.DeathEffectHunterMismatch;
            return false;
        }
        else deathId = death.Id;

        ids = new CosmeticLoadoutIds(skinId, armorId, deathId);
        return true;
    }

    public bool IsValid(CosmeticLoadoutIds ids, Hunter hunter)
        => hunter <= Hunter.Guardian
            && (ids.SkinId == 0 || TryGetSkin(ids.SkinId, out SkinDefinition skin) && skin.Hunter == hunter)
            && (ids.ArmorEffectId == 0 || _armorById.ContainsKey(ids.ArmorEffectId))
            && (ids.DeathEffectId == 0 || TryGetDeathEffect(ids.DeathEffectId,
                out DeathEffectDefinition death)
                && (death.Hunter == null || death.Hunter == hunter));

    /// <summary>Fail-soft client resolution. Each unavailable optional slot falls back independently.</summary>
    public CosmeticLoadoutIds Sanitize(CosmeticLoadoutIds ids, Hunter hunter)
        => new(
            ids.SkinId != 0 && TryGetSkin(ids.SkinId, out SkinDefinition skin) && skin.Hunter == hunter
                ? ids.SkinId : (ushort)0,
            ids.ArmorEffectId != 0 && _armorById.ContainsKey(ids.ArmorEffectId)
                ? ids.ArmorEffectId : (ushort)0,
            ids.DeathEffectId != 0 && TryGetDeathEffect(ids.DeathEffectId,
                out DeathEffectDefinition death)
                && (death.Hunter == null || death.Hunter == hunter)
                ? ids.DeathEffectId : (ushort)0);

    private static void ValidateSkins(SkinDefinition[] values)
    {
        ValidateIdentities(values.Select(value => (value.Id, value.Key)), "skin", "prime.skin.");
        foreach (SkinDefinition value in values)
        {
            if (value.Hunter > Hunter.Guardian || !ValidLabel(value.DisplayName)
                || value.Materials?.Count > CosmeticPackageLimits.MaximumMaterialOverrides
                || value.Materials?.Any(material => !ValidMaterial(material)) == true
                || value.TeamAccent is { } accent
                    && (!float.IsFinite(accent.Strength) || accent.Strength is < 0 or > 1
                        || accent.Mask != null && !CosmeticPath.IsValidRelative(accent.Mask)))
                throw new ArgumentException($"Invalid cosmetic skin definition '{value.Key}'.", nameof(values));
        }
    }

    private static void ValidateArmor(ArmorEffectDefinition[] values)
    {
        ValidateIdentities(values.Select(value => (value.Id, value.Key)), "armor effect", "prime.armor_fx.");
        foreach (ArmorEffectDefinition value in values)
        {
            if (!ValidLabel(value.DisplayName) || !Enum.IsDefined(value.AltFormMode)
                || !ValidMaterial(value.Material)
                || value.Particles?.Count > CosmeticPackageLimits.MaximumParticleEmitters
                || value.Particles?.Any(particle => !ValidParticle(particle)) == true
                || value.Ribbons?.Count > CosmeticPackageLimits.MaximumRibbons
                || value.Ribbons?.Any(ribbon => !ValidRibbon(ribbon)) == true
                || value.Attachments?.Count > CosmeticPackageLimits.MaximumAttachments
                || value.Attachments?.Any(attachment => !ValidAttachment(attachment)) == true
                || !ValidDistortion(value.Distortion))
                throw new ArgumentException($"Invalid cosmetic armor effect definition '{value.Key}'.", nameof(values));
        }
    }

    private static void ValidateDeaths(DeathEffectDefinition[] values)
    {
        ValidateIdentities(values.Select(value => (value.Id, value.Key)), "death effect", "prime.death.");
        foreach (DeathEffectDefinition value in values)
        {
            if (!ValidLabel(value.DisplayName) || !Enum.IsDefined(value.BodyMode)
                || !float.IsFinite(value.Duration) || value.Duration is <= 0 or > CosmeticPackageLimits.MaximumDeathDurationSeconds
                || value.Hunter is Hunter hunter && hunter > Hunter.Guardian
                || value.Animation != null && !CosmeticPath.IsValidRelative(value.Animation)
                || !ValidMaterial(value.Material)
                || value.Particles?.Count > CosmeticPackageLimits.MaximumParticleEmitters
                || value.Particles?.Any(particle => !ValidParticle(particle)) == true
                || !ValidDistortion(value.Distortion))
                throw new ArgumentException($"Invalid cosmetic death effect definition '{value.Key}'.", nameof(values));
        }
    }

    private static void ValidateIdentities(IEnumerable<(ushort Id, string Key)> values,
        string kind, string prefix)
    {
        var ids = new HashSet<ushort>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((ushort id, string key) in values)
        {
            if (id == 0) throw new ArgumentException($"Authored {kind} ID zero is reserved for fallback.");
            if (!CosmeticId.IsValid(key) || !key.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException($"Invalid {kind} key '{key}'.");
            if (!ids.Add(id)) throw new ArgumentException($"Duplicate {kind} ID {id}.");
            if (!keys.Add(key)) throw new ArgumentException($"Duplicate {kind} key '{key}'.");
        }
    }

    private static bool ValidLabel(string? value)
        => value is { Length: >= 1 and <= 96 } && !value.Any(char.IsControl);

    private static bool ValidMaterial(SkinMaterialOverride value)
        => CosmeticPath.IsValidAssetKey(value.Source)
            && ValidOptionalPath(value.Albedo) && ValidOptionalPath(value.Normal) && ValidOptionalPath(value.Emissive)
            && ValidUnit(value.SpecularStrength) && ValidUnit(value.Smoothness)
            && ValidUnit(value.ReflectionStrength) && ValidEmission(value.EmissionStrength)
            && (!value.EmissionTint.HasValue || value.EmissionTint.Value.IsValid);

    private static bool ValidMaterial(CosmeticMaterialDefinition? value)
        => value == null || ValidUnit(value.SpecularStrength) && ValidUnit(value.Smoothness)
            && ValidUnit(value.ReflectionStrength) && ValidEmission(value.EmissionStrength)
            && (!value.EmissionTint.HasValue || value.EmissionTint.Value.IsValid);

    private static bool ValidParticle(CosmeticParticleDefinition value)
        => CosmeticPath.IsValidToken(value.Kind) && Enum.IsDefined(value.Anchor)
            && float.IsFinite(value.Rate) && value.Rate is >= 0 and <= 1024
            && value.Count <= 512;

    private static bool ValidRibbon(CosmeticRibbonDefinition value)
        => Enum.IsDefined(value.From) && Enum.IsDefined(value.To)
            && value.Segments is >= 1 and <= 64 && float.IsFinite(value.Rate)
            && value.Rate is >= 0 and <= 120;

    private static bool ValidAttachment(CosmeticAttachmentDefinition value)
        => CosmeticPath.IsValidRelative(value.Mesh) && Enum.IsDefined(value.Anchor)
            && float.IsFinite(value.Scale) && value.Scale is >= .01f and <= 4
            && BoundedOffset(value.OffsetX) && BoundedOffset(value.OffsetY)
            && BoundedOffset(value.OffsetZ) && BoundedAngle(value.PitchDegrees)
            && BoundedAngle(value.YawDegrees) && BoundedAngle(value.RollDegrees);

    private static bool BoundedOffset(float value)
        => float.IsFinite(value) && value is >= -4 and <= 4;

    private static bool BoundedAngle(float value)
        => float.IsFinite(value) && value is >= -360 and <= 360;

    private static bool ValidDistortion(CosmeticDistortionDefinition? value)
        => value == null || float.IsFinite(value.Strength) && value.Strength is >= 0 and <= 1
            && float.IsFinite(value.Duration) && value.Duration is >= 0 and <= CosmeticPackageLimits.MaximumDeathDurationSeconds;

    private static bool ValidOptionalPath(string? value)
        => value == null || CosmeticPath.IsValidRelative(value);

    private static bool ValidUnit(float? value)
        => !value.HasValue || float.IsFinite(value.Value) && value.Value is >= 0 and <= 1;

    private static bool ValidEmission(float? value)
        => !value.HasValue || float.IsFinite(value.Value) && value.Value is >= 0 and <= 8;

    private static string ComputeHash(ushort version, SkinDefinition[] skins,
        ArmorEffectDefinition[] armor, DeathEffectDefinition[] deaths)
    {
        var canonical = new StringBuilder().Append("cosmetic-catalog\0").Append(version).Append('\n');
        foreach (SkinDefinition value in skins.OrderBy(value => value.Id))
            canonical.Append("skin\0").Append(value.Id).Append('\0').Append(value.Key).Append('\0')
                .Append((byte)value.Hunter).Append('\n');
        foreach (ArmorEffectDefinition value in armor.OrderBy(value => value.Id))
            canonical.Append("armor\0").Append(value.Id).Append('\0').Append(value.Key).Append('\n');
        foreach (DeathEffectDefinition value in deaths.OrderBy(value => value.Id))
            canonical.Append("death\0").Append(value.Id).Append('\0').Append(value.Key)
                .Append('\0').Append(value.Hunter is Hunter hunter ? (byte)hunter : -1)
                .Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static CosmeticCatalog CreateBuiltIn()
    {
        SkinDefinition[] skins =
        [
            new(BuiltInCosmeticIds.SkinSamusObsidian, "prime.skin.samus.obsidian", "Obsidian Prime", Hunter.Samus,
                BaseRecolor: 1),
            new(BuiltInCosmeticIds.SkinSamusSolar, "prime.skin.samus.solar", "Solar Prime", Hunter.Samus,
                BaseRecolor: 2)
        ];
        ArmorEffectDefinition[] armor =
        [
            new(BuiltInCosmeticIds.ArmorLightning, "prime.armor_fx.lightning", "Lightning"),
            new(BuiltInCosmeticIds.ArmorPestilence, "prime.armor_fx.pestilence", "Pestilence"),
            new(BuiltInCosmeticIds.ArmorEclipse, "prime.armor_fx.eclipse", "Eclipse"),
            new(BuiltInCosmeticIds.ArmorInferno, "prime.armor_fx.inferno", "Inferno"),
            new(BuiltInCosmeticIds.ArmorGlacial, "prime.armor_fx.glacial", "Glacial"),
            new(BuiltInCosmeticIds.ArmorVoid, "prime.armor_fx.void", "Void"),
            new(BuiltInCosmeticIds.ArmorRadiant, "prime.armor_fx.radiant", "Radiant"),
            new(BuiltInCosmeticIds.ArmorPhase, "prime.armor_fx.phase", "Phase"),
            new(BuiltInCosmeticIds.ArmorSpectral, "prime.armor_fx.spectral", "Spectral"),
            new(BuiltInCosmeticIds.ArmorSpike, "prime.armor_fx.spike", "Spike"),
            new(BuiltInCosmeticIds.ArmorSolar, "prime.armor_fx.solar", "Solar"),
            new(BuiltInCosmeticIds.ArmorLumen, "prime.armor_fx.lumen", "Lumen"),
            new(BuiltInCosmeticIds.ArmorCorruption, "prime.armor_fx.corruption", "Corruption"),
            new(BuiltInCosmeticIds.ArmorAurora, "prime.armor_fx.aurora", "Aurora"),
            new(BuiltInCosmeticIds.ArmorQuantum, "prime.armor_fx.quantum", "Quantum"),
            new(BuiltInCosmeticIds.ArmorThunderstorm, "prime.armor_fx.thunderstorm", "Thunderstorm")
        ];
        DeathEffectDefinition[] deaths =
        [
            new(BuiltInCosmeticIds.DeathQuantum, "prime.death.quantum", "Quantum Disintegration", 1.15f,
                DeathBodyMode.Dissolve,
                Material: new(new(.7f, .2f, 1), 2),
                Particles: [new("quantum.pixels", Count: 80)],
                Distortion: new(.015f, .45f)),
            new(BuiltInCosmeticIds.DeathSpectral, "prime.death.spectral", "Spectral Fade", 1.1f,
                DeathBodyMode.Fade,
                Material: new(new(.7f, .95f, 1), 1),
                Particles: [new("spectral.wisp", Count: 48)]),
            new(BuiltInCosmeticIds.DeathInfernoBurnout, "prime.death.inferno_burnout", "Inferno Burnout", 1.25f,
                DeathBodyMode.Burn,
                Material: new(new(1, .28f, .03f), 2.4f),
                Particles: [new("inferno.embers", Count: 64)],
                Distortion: new(.012f, .5f)),
            new(BuiltInCosmeticIds.DeathSamusBackwardCollapse,
                "prime.death.samus_backward_collapse", "Samus Backward Collapse", .9f,
                DeathBodyMode.CustomAnimation,
                Animation: "death-animations/samus-backward-collapse.pda",
                Hunter: Hunter.Samus)
        ];
        return new CosmeticCatalog(skins, armor, deaths);
    }
}

public static class BuiltInCosmeticIds
{
    public const ushort Default = 0;
    public const ushort SkinSamusObsidian = 1;
    public const ushort SkinSamusSolar = 2;
    public const ushort ArmorLightning = 1;
    public const ushort ArmorPestilence = 2;
    public const ushort ArmorEclipse = 3;
    public const ushort ArmorInferno = 4;
    public const ushort ArmorGlacial = 5;
    public const ushort ArmorVoid = 6;
    public const ushort ArmorRadiant = 7;
    public const ushort ArmorPhase = 8;
    public const ushort ArmorSpectral = 9;
    public const ushort ArmorSpike = 10;
    public const ushort ArmorSolar = 11;
    public const ushort ArmorLumen = 12;
    public const ushort ArmorCorruption = 13;
    public const ushort ArmorAurora = 14;
    public const ushort ArmorQuantum = 15;
    public const ushort ArmorThunderstorm = 16;
    public const ushort DeathQuantum = 1;
    public const ushort DeathSpectral = 2;
    public const ushort DeathInfernoBurnout = 3;
    public const ushort DeathSamusBackwardCollapse = 4;
}

internal static class CosmeticPath
{
    internal static bool IsValidAssetKey(string? value)
        => value is { Length: >= 1 and <= CosmeticPackageLimits.MaximumRelativePathLength }
            && value[0] != '/' && value[^1] != '/' && !value.Contains("//", StringComparison.Ordinal)
            && !value.Contains('\\') && !value.Contains(':')
            && value.Split('/').All(segment => segment is not "" and not "." and not ".."
                && !segment.Any(char.IsControl));

    internal static bool IsValidRelative(string? value) => IsValidAssetKey(value);

    internal static bool IsValidToken(string? value)
        => value is { Length: >= 1 and <= 64 }
            && value[0] != '.' && value[^1] != '.'
            && !value.Contains("..", StringComparison.Ordinal)
            && value.All(character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '-' or '.');
}
