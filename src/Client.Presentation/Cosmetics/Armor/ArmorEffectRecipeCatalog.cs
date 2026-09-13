using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MphRead.Cosmetics.Presentation;

[Flags]
public enum ArmorEffectTechniques : byte
{
    None = 0,
    Fresnel = 1 << 0,
    TransparentShell = 1 << 1,
    ScanBands = 1 << 2,
    GhostShell = 1 << 3,
    SelectiveBloom = 1 << 4
}

/// <summary>
/// Client-owned authored recipe layered over the small shared catalog entry.
/// Particle source strings refer only to built-in presentation primitives or
/// existing game effects; they are not paths or reflection/type names.
/// </summary>
public sealed record ArmorEffectRecipe(
    ArmorEffectDefinition Definition,
    ArmorEffectTechniques Techniques,
    bool LocalLight = false,
    IReadOnlyList<string>? ParticleSprites = null)
{
    public int TypicalParticleCount
    {
        get
        {
            int total = 0;
            IReadOnlyList<CosmeticParticleDefinition>? particles
                = Definition.Particles;
            if (particles == null) return 0;
            for (int i = 0; i < particles.Count; i++)
            {
                CosmeticParticleDefinition particle = particles[i];
                total += particle.Count != 0 ? particle.Count
                    : (int)MathF.Ceiling(MathF.Max(0, particle.Rate) * 2);
            }
            return total;
        }
    }

    public int TotalRibbonSegments
    {
        get
        {
            int total = 0;
            IReadOnlyList<CosmeticRibbonDefinition>? ribbons
                = Definition.Ribbons;
            if (ribbons == null) return 0;
            for (int i = 0; i < ribbons.Count; i++)
                total += ribbons[i].Segments;
            return total;
        }
    }
}

public static class ArmorEffectRecipeCatalog
{
    private static readonly ReadOnlyCollection<ArmorEffectRecipe> _all
        = Array.AsReadOnly(new[]
        {
            Recipe(1, "lightning", "Lightning", CosmeticAltFormMode.RootOnly,
                C(.45f, .72f, 1), 1.8f,
                particles: new[] { P("lightning.arc", CosmeticAnchor.Chest, 8, 16) },
                ribbons: new[]
                {
                    R(CosmeticAnchor.LeftShoulder, CosmeticAnchor.RightShoulder, 6, 3),
                    R(CosmeticAnchor.RightShoulder, CosmeticAnchor.Weapon, 5, 2)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom,
                localLight: true),
            Recipe(2, "pestilence", "Pestilence", CosmeticAltFormMode.RootOnly,
                C(.25f, 1, .18f), 1.15f,
                particles: new[]
                {
                    P("spectral.wisp", CosmeticAnchor.Root, 5, 14),
                    P("quantum.pixels", CosmeticAnchor.Chest, 3, 8)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(3, "eclipse", "Eclipse", CosmeticAltFormMode.RootOnly,
                C(.28f, .18f, .75f), 1.1f,
                particles: new[] { P("spectral.vortex", CosmeticAnchor.Root, 4, 10) },
                distortion: D(.006f),
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.TransparentShell),
            Recipe(4, "inferno", "Inferno", CosmeticAltFormMode.Adapted,
                C(1, .32f, .04f), 1.9f,
                particles: new[]
                {
                    P("inferno.flame", CosmeticAnchor.Root, 5, 14),
                    P("inferno.embers", CosmeticAnchor.Chest, 6, 14)
                }, distortion: D(.01f),
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom,
                localLight: true),
            Recipe(5, "glacial", "Glacial", CosmeticAltFormMode.RootOnly,
                C(.28f, .75f, 1), 1.25f,
                particles: new[]
                {
                    P("spectral.wisp", CosmeticAnchor.Root, 4, 10),
                    P("glacial.crystals", CosmeticAnchor.Chest, 2, 6)
                }, attachments: new[]
                {
                    A("octolith_simple", CosmeticAnchor.LeftShoulder, .08f,
                        yaw: -25, roll: 18),
                    A("octolith_simple", CosmeticAnchor.RightShoulder, .08f,
                        yaw: 25, roll: -18)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(6, "void", "Void", CosmeticAltFormMode.RootOnly,
                C(.46f, .12f, .85f), 1.35f,
                particles: new[] { P("quantum.pixels", CosmeticAnchor.Root, 5, 14) },
                distortion: D(.014f),
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.TransparentShell),
            Recipe(7, "radiant", "Radiant", CosmeticAltFormMode.RootOnly,
                C(1, .78f, .22f), 1.65f,
                particles: new[] { P("lightning.ring", CosmeticAnchor.Chest, 4, 10) },
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom,
                localLight: true),
            Recipe(8, "phase", "Phase", CosmeticAltFormMode.RootOnly,
                C(.18f, .75f, 1), 1.35f,
                particles: new[] { P("phase.scan", CosmeticAnchor.Root, 3, 6) },
                distortion: D(.018f),
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.TransparentShell
                    | ArmorEffectTechniques.ScanBands | ArmorEffectTechniques.GhostShell
                    | ArmorEffectTechniques.SelectiveBloom),
            Recipe(9, "spectral", "Spectral", CosmeticAltFormMode.RootOnly,
                C(.58f, .82f, 1), 1.1f,
                particles: new[] { P("spectral.wisp", CosmeticAnchor.Root, 4, 10) },
                techniques: ArmorEffectTechniques.TransparentShell
                    | ArmorEffectTechniques.GhostShell | ArmorEffectTechniques.SelectiveBloom),
            Recipe(10, "spike", "Spike", CosmeticAltFormMode.Hidden,
                C(.65f, .22f, 1), 1.3f,
                particles: new[] { P("glacial.crystals", CosmeticAnchor.Chest, 3, 8) },
                attachments: new[]
                {
                    A("octolith_simple", CosmeticAnchor.LeftShoulder, .07f,
                        yaw: -35, roll: 22),
                    A("octolith_simple", CosmeticAnchor.RightShoulder, .07f,
                        yaw: 35, roll: -22),
                    A("octolith_simple", CosmeticAnchor.LeftHand, .055f,
                        pitch: 70),
                    A("octolith_simple", CosmeticAnchor.RightHand, .055f,
                        pitch: -70)
                },
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(11, "solar", "Solar", CosmeticAltFormMode.Adapted,
                C(1, .62f, .08f), 1.8f,
                particles: new[] { P("inferno.swirl", CosmeticAnchor.Root, 6, 16) },
                ribbons: new[] { R(CosmeticAnchor.Root, CosmeticAnchor.Chest, 8, 1.5f) },
                attachments: new[] { A("octolith_simple", CosmeticAnchor.Root,
                    .09f, y: .12f, yaw: 45) },
                distortion: D(.009f),
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom,
                localLight: true),
            Recipe(12, "lumen", "Lumen", CosmeticAltFormMode.Adapted,
                C(.82f, .94f, 1), 1.5f,
                particles: new[] { P("phase.orbit", CosmeticAnchor.Chest, 2, 6) },
                ribbons: new[]
                {
                    R(CosmeticAnchor.LeftShoulder, CosmeticAnchor.RightShoulder, 8, 1.2f),
                    R(CosmeticAnchor.LeftFoot, CosmeticAnchor.RightFoot, 8, 1)
                }, attachments: new[] { A("octolith_simple", CosmeticAnchor.Chest,
                    .065f, z: .12f, roll: 90) },
                techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(13, "corruption", "Corruption", CosmeticAltFormMode.RootOnly,
                C(.9f, .06f, .08f), 1.4f,
                particles: new[] { P("quantum.trail", CosmeticAnchor.Root, 4, 10) },
                ribbons: new[]
                {
                    R(CosmeticAnchor.Chest, CosmeticAnchor.LeftHand, 7, 1.5f),
                    R(CosmeticAnchor.Chest, CosmeticAnchor.RightHand, 7, 1.5f)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(14, "aurora", "Aurora", CosmeticAltFormMode.Adapted,
                C(.2f, 1, .72f), 1.25f,
                particles: new[] { P("spectral.wave", CosmeticAnchor.Root, 2, 5) },
                ribbons: new[]
                {
                    R(CosmeticAnchor.LeftShoulder, CosmeticAnchor.RightFoot, 10, 1),
                    R(CosmeticAnchor.RightShoulder, CosmeticAnchor.LeftFoot, 10, 1)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom),
            Recipe(15, "quantum", "Quantum", CosmeticAltFormMode.RootOnly,
                C(.66f, .25f, 1), 1.45f,
                particles: new[] { P("quantum.pixels", CosmeticAnchor.Root, 6, 16) },
                distortion: D(.012f),
                techniques: ArmorEffectTechniques.ScanBands
                    | ArmorEffectTechniques.TransparentShell | ArmorEffectTechniques.SelectiveBloom),
            Recipe(16, "thunderstorm", "Thunderstorm", CosmeticAltFormMode.RootOnly,
                C(.55f, .78f, 1), 2,
                particles: new[]
                {
                    P("lightning.burst", CosmeticAnchor.Chest, 8, 18),
                    P("lightning.swirl", CosmeticAnchor.Head, 2, 6)
                }, ribbons: new[]
                {
                    R(CosmeticAnchor.LeftShoulder, CosmeticAnchor.Weapon, 8, 3),
                    R(CosmeticAnchor.RightShoulder, CosmeticAnchor.LeftFoot, 8, 2),
                    R(CosmeticAnchor.Head, CosmeticAnchor.RightFoot, 8, 1)
                }, techniques: ArmorEffectTechniques.Fresnel | ArmorEffectTechniques.SelectiveBloom,
                localLight: true)
        });

    private static readonly IReadOnlyDictionary<ushort, ArmorEffectRecipe> _byId
        = new ReadOnlyDictionary<ushort, ArmorEffectRecipe>(
            _all.ToDictionary(recipe => recipe.Definition.Id));
    private static readonly IReadOnlyDictionary<string, ArmorEffectRecipe> _byKey
        = new ReadOnlyDictionary<string, ArmorEffectRecipe>(
            _all.ToDictionary(recipe => recipe.Definition.Key,
                StringComparer.Ordinal));

    static ArmorEffectRecipeCatalog()
    {
        foreach (ArmorEffectRecipe recipe in _all)
        {
            if (!CosmeticCatalog.BuiltIn.TryGetArmorEffect(recipe.Definition.Id,
                    out ArmorEffectDefinition definition)
                || !String.Equals(definition.Key, recipe.Definition.Key,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Armor recipe '{recipe.Definition.Key}' does not match the built-in catalog.");
            }
        }
    }

    public static IReadOnlyList<ArmorEffectRecipe> All => _all;

    public static ArmorEffectRecipe? Resolve(ushort id)
        => id == 0 ? null : _byId.GetValueOrDefault(id);

    public static ArmorEffectRecipe? Resolve(string? key)
        => key == null || String.Equals(key, CosmeticKeys.NoArmorEffect,
            StringComparison.Ordinal) ? null : _byKey.GetValueOrDefault(key);

    private static ArmorEffectRecipe Recipe(ushort id, string suffix,
        string displayName, CosmeticAltFormMode altFormMode,
        CosmeticColor emissionTint, float emissionStrength,
        CosmeticParticleDefinition[]? particles = null,
        CosmeticRibbonDefinition[]? ribbons = null,
        CosmeticAttachmentDefinition[]? attachments = null,
        CosmeticDistortionDefinition? distortion = null,
        ArmorEffectTechniques techniques = ArmorEffectTechniques.None,
        bool localLight = false)
    {
        string[]? particleSprites = particles?
            .Select(particle => particle.Kind).ToArray();
        CosmeticParticleDefinition[]? authoredParticles = particles?
            .Select(particle => particle with { Kind = "atlas" }).ToArray();
        var definition = new ArmorEffectDefinition(id,
            $"prime.armor_fx.{suffix}", displayName, altFormMode,
            new CosmeticMaterialDefinition(emissionTint, emissionStrength,
                SpecularStrength: .45f, Smoothness: .65f,
                ReflectionStrength: .15f), authoredParticles, ribbons,
            attachments, distortion);
        return new ArmorEffectRecipe(definition, techniques, localLight,
            particleSprites);
    }

    private static CosmeticColor C(float r, float g, float b) => new(r, g, b);

    private static CosmeticParticleDefinition P(string kind,
        CosmeticAnchor anchor, float rate, ushort count)
        => new(kind, anchor, rate, count);

    private static CosmeticRibbonDefinition R(CosmeticAnchor from,
        CosmeticAnchor to, byte segments, float rate)
        => new(from, to, segments, rate);

    private static CosmeticAttachmentDefinition A(string assetKey,
        CosmeticAnchor anchor, float scale, float x = 0, float y = 0,
        float z = 0, float pitch = 0, float yaw = 0, float roll = 0)
        => new(assetKey, anchor, scale, x, y, z, pitch, yaw, roll);

    private static CosmeticDistortionDefinition D(float strength)
        => new(strength, Duration: 1);
}
