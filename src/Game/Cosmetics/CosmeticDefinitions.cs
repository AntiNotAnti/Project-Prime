using System;
using System.Collections.Generic;

namespace MphRead.Cosmetics;

public enum CosmeticAltFormMode : byte
{
    Hidden,
    RootOnly,
    Adapted
}

public enum CosmeticAnchor : byte
{
    Root,
    Head,
    Chest,
    LeftShoulder,
    RightShoulder,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
    Weapon
}

public enum CosmeticQuality : byte
{
    Off,
    Reduced,
    Full
}

public enum DeathBodyMode : byte
{
    Default,
    HoldPose,
    Dissolve,
    Fade,
    Shatter,
    Burn,
    Implode,
    CustomAnimation
}

public readonly record struct CosmeticColor(float R, float G, float B)
{
    public bool IsValid => float.IsFinite(R) && float.IsFinite(G) && float.IsFinite(B)
        && R is >= 0 and <= 8 && G is >= 0 and <= 8 && B is >= 0 and <= 8;
}

public sealed record CosmeticMaterialDefinition(
    CosmeticColor? EmissionTint = null,
    float? EmissionStrength = null,
    float? SpecularStrength = null,
    float? Smoothness = null,
    float? ReflectionStrength = null);

public sealed record SkinMaterialOverride(
    string Source,
    string? Albedo = null,
    string? Normal = null,
    string? Emissive = null,
    float? SpecularStrength = null,
    float? Smoothness = null,
    float? ReflectionStrength = null,
    CosmeticColor? EmissionTint = null,
    float? EmissionStrength = null);

public sealed record TeamAccentDefinition(bool Enabled, string? Mask, float Strength);

public sealed record CosmeticParticleDefinition(
    string Kind,
    CosmeticAnchor Anchor = CosmeticAnchor.Root,
    float Rate = 0,
    ushort Count = 0);

public sealed record CosmeticRibbonDefinition(
    CosmeticAnchor From,
    CosmeticAnchor To,
    byte Segments,
    float Rate);

public sealed record CosmeticAttachmentDefinition(
    string Mesh,
    CosmeticAnchor Anchor,
    float Scale = 1,
    float OffsetX = 0,
    float OffsetY = 0,
    float OffsetZ = 0,
    float PitchDegrees = 0,
    float YawDegrees = 0,
    float RollDegrees = 0);

public sealed record CosmeticDistortionDefinition(float Strength, float Duration);

public sealed record SkinDefinition(
    ushort Id,
    string Key,
    string DisplayName,
    Hunter Hunter,
    byte BaseRecolor = 0,
    IReadOnlyList<SkinMaterialOverride>? Materials = null,
    TeamAccentDefinition? TeamAccent = null);

public sealed record ArmorEffectDefinition(
    ushort Id,
    string Key,
    string DisplayName,
    CosmeticAltFormMode AltFormMode = CosmeticAltFormMode.RootOnly,
    CosmeticMaterialDefinition? Material = null,
    IReadOnlyList<CosmeticParticleDefinition>? Particles = null,
    IReadOnlyList<CosmeticRibbonDefinition>? Ribbons = null,
    IReadOnlyList<CosmeticAttachmentDefinition>? Attachments = null,
    CosmeticDistortionDefinition? Distortion = null);

public sealed record DeathEffectDefinition(
    ushort Id,
    string Key,
    string DisplayName,
    float Duration,
    DeathBodyMode BodyMode,
    string? Animation = null,
    CosmeticMaterialDefinition? Material = null,
    IReadOnlyList<CosmeticParticleDefinition>? Particles = null,
    CosmeticDistortionDefinition? Distortion = null,
    Hunter? Hunter = null);
