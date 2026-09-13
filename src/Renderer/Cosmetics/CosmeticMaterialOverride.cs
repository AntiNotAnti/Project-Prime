using System;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// A presentation-only material layer. Null fields preserve the material
/// selected by the normal enhanced-texture pipeline.
/// </summary>
public readonly record struct CosmeticMaterialOverride(
    TextureIdentity? Albedo = null,
    TextureIdentity? Normal = null,
    TextureIdentity? Emissive = null,
    float? SpecularStrength = null,
    float? Smoothness = null,
    float? ReflectionStrength = null,
    Vector3? EmissionTint = null,
    float? EmissionStrength = null)
{
    public EnhancedMaterial Apply(EnhancedMaterial material)
        => material with
        {
            Albedo = Albedo ?? material.Albedo,
            Normal = Normal ?? material.Normal,
            Emissive = Emissive ?? material.Emissive,
            SpecularStrength = ResolveUnit(SpecularStrength,
                material.SpecularStrength),
            Smoothness = ResolveUnit(Smoothness, material.Smoothness),
            ReflectionStrength = ResolveUnit(ReflectionStrength,
                material.ReflectionStrength),
            EmissionTint = ResolveTint(EmissionTint, material.EmissionTint),
            EmissionStrength = ResolveRange(EmissionStrength,
                material.EmissionStrength, 0, 16)
        };

    private static float ResolveUnit(float? value, float fallback)
        => ResolveRange(value, fallback, 0, 1);

    private static float ResolveRange(float? value, float fallback, float min,
        float max)
        => value is float requested && float.IsFinite(requested)
            ? Math.Clamp(requested, min, max) : fallback;

    private static Vector3 ResolveTint(Vector3? value, Vector3 fallback)
    {
        if (value is not Vector3 requested
            || !float.IsFinite(requested.X)
            || !float.IsFinite(requested.Y)
            || !float.IsFinite(requested.Z))
        {
            return fallback;
        }
        return Vector3.Clamp(requested, Vector3.Zero, Vector3.One);
    }
}

[Flags]
public enum GameplayMaterialFeedback : byte
{
    None = 0,
    DamageFlash = 1 << 0,
    Frozen = 1 << 1,
    Cloaked = 1 << 2,
    DoubleDamage = 1 << 3,
    Selection = 1 << 4
}

/// <summary>
/// Resolves the material layers in one explicit order. Gameplay feedback is
/// intentionally allowed to suppress a skin layer so optional presentation
/// can never hide damage, ice, cloak, power-up, or selection state.
/// </summary>
public static class CosmeticMaterialPipeline
{
    public static EnhancedMaterial Resolve(EnhancedMaterial baseMaterial,
        CosmeticMaterialOverride? skin,
        GameplayMaterialFeedback gameplayFeedback = GameplayMaterialFeedback.None)
    {
        if (skin is null || gameplayFeedback != GameplayMaterialFeedback.None)
            return baseMaterial;
        return skin.Value.Apply(baseMaterial);
    }
}
