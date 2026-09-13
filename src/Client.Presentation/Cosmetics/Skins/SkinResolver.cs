using System;
using MphRead.Cosmetics;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

public interface ICosmeticTextureResolver
{
    bool TryResolve(string assetPath, out TextureIdentity identity);
}

public sealed class SkinResolver
{
    private readonly ICosmeticTextureResolver? _textures;

    public SkinResolver(ICosmeticTextureResolver? textures = null)
        => _textures = textures;

    public bool TryResolve(SkinDefinition skin, TextureAssetKey source,
        out CosmeticMaterialOverride material)
    {
        ArgumentNullException.ThrowIfNull(skin);
        material = default;
        if (!source.IsValid || skin.Materials == null) return false;
        for (int i = 0; i < skin.Materials.Count; i++)
        {
            SkinMaterialOverride candidate = skin.Materials[i];
            if (!String.Equals(candidate.Source, source.Value, StringComparison.Ordinal))
                continue;
            material = new CosmeticMaterialOverride(
                Albedo: Texture(candidate.Albedo),
                Normal: Texture(candidate.Normal),
                Emissive: Texture(candidate.Emissive),
                SpecularStrength: candidate.SpecularStrength,
                Smoothness: candidate.Smoothness,
                ReflectionStrength: candidate.ReflectionStrength,
                EmissionTint: candidate.EmissionTint is { } tint
                    ? new Vector3(tint.R, tint.G, tint.B) : null,
                EmissionStrength: candidate.EmissionStrength);
            return true;
        }
        return false;
    }

    private TextureIdentity? Texture(string? path)
    {
        if (path == null || _textures == null) return null;
        return _textures.TryResolve(path, out TextureIdentity identity)
            ? identity : null;
    }
}

/// <summary>
/// Presentation-owned skin selection. PlayerEntity.Recolor remains the
/// canonical gameplay-compatible recolor and is never used as a skin ID.
/// </summary>
public sealed class SkinPresentation
{
    private readonly CosmeticCatalog _catalog;
    private readonly SkinResolver _resolver;

    public SkinPresentation(CosmeticCatalog? catalog = null,
        SkinResolver? resolver = null)
    {
        _catalog = catalog ?? CosmeticCatalog.BuiltIn;
        _resolver = resolver ?? new SkinResolver();
    }

    public SkinDefinition? Definition { get; private set; }
    public ushort SkinId => Definition?.Id ?? 0;

    public void Select(ushort skinId, Hunter hunter)
    {
        Definition = skinId != 0 && _catalog.TryGetSkin(skinId,
            out SkinDefinition definition) && definition.Hunter == hunter
                ? definition : null;
        if (skinId != 0 && Definition == null)
            Mods.DebugLog.Line("cosmetics/skin",
                $"Skin {skinId} is unavailable for {hunter}; using base appearance.");
    }

    public int ResolveRecolor(int canonicalRecolor)
        => Definition?.BaseRecolor ?? canonicalRecolor;

    public CosmeticMaterialOverride? ResolveMaterial(TextureAssetKey? source,
        GameplayMaterialFeedback feedback)
    {
        if (Definition == null || source is not TextureAssetKey key
            || feedback != GameplayMaterialFeedback.None
            || !_resolver.TryResolve(Definition, key, out CosmeticMaterialOverride material))
        {
            return null;
        }
        return material;
    }

    /// <summary>
    /// Resolves a skin against canonical team presentation facts. Team modes
    /// always retain the gameplay-selected recolor; authored accent metadata
    /// permits only surface channels that cannot replace that base color.
    /// Strong-team mode additionally removes cosmetic emission.
    /// </summary>
    public SkinAppearanceResolution ResolveAppearance(int canonicalRecolor,
        TextureAssetKey? source, GameplayMaterialFeedback feedback,
        bool teamMode, bool forceStrongTeamColors)
    {
        CosmeticMaterialOverride? material = ResolveMaterial(source, feedback);
        if (!teamMode || Definition == null)
        {
            return new SkinAppearanceResolution(
                ResolveRecolor(canonicalRecolor), material, 0, null);
        }

        TeamAccentDefinition? accent = Definition.TeamAccent;
        float strength = forceStrongTeamColors
            ? 1 : accent is { Enabled: true } ? accent.Strength : 0;
        if (accent is not { Enabled: true } && !forceStrongTeamColors)
        {
            return new SkinAppearanceResolution(canonicalRecolor, null, 0, null);
        }

        if (material is CosmeticMaterialOverride cosmetic)
        {
            // A replacement albedo could erase the canonical team palette.
            // Strong-team mode also suppresses glow that could overpower it.
            material = cosmetic with
            {
                Albedo = null,
                Emissive = forceStrongTeamColors ? null : cosmetic.Emissive,
                EmissionTint = forceStrongTeamColors ? null : cosmetic.EmissionTint,
                EmissionStrength = forceStrongTeamColors ? null
                    : cosmetic.EmissionStrength
            };
        }
        return new SkinAppearanceResolution(canonicalRecolor, material,
            Math.Clamp(strength, 0, 1), accent?.Mask);
    }
}

public readonly record struct SkinAppearanceResolution(
    int Recolor,
    CosmeticMaterialOverride? Material,
    float TeamAccentStrength,
    string? TeamAccentMask);
