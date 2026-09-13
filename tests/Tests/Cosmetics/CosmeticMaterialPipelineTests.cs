using MphRead;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Cosmetics;

public sealed class CosmeticMaterialPipelineTests
{
    private static readonly EnhancedMaterial BaseMaterial = new(
        Albedo: null, Normal: null, Emissive: null,
        SpecularStrength: .2f, Smoothness: .25f, ReflectionStrength: .1f,
        EmissionTint: Vector3.One, EmissionStrength: .3f);

    [Fact]
    public void SkinOverridesOnlyAuthoredEnhancedMaterialChannels()
    {
        var skin = new CosmeticMaterialOverride(
            Smoothness: .8f,
            ReflectionStrength: .6f,
            EmissionTint: new Vector3(.1f, .2f, .9f),
            EmissionStrength: 1.2f);

        EnhancedMaterial resolved = CosmeticMaterialPipeline.Resolve(BaseMaterial, skin);

        Assert.Equal(BaseMaterial.SpecularStrength, resolved.SpecularStrength);
        Assert.Equal(.8f, resolved.Smoothness);
        Assert.Equal(.6f, resolved.ReflectionStrength);
        Assert.Equal(new Vector3(.1f, .2f, .9f), resolved.EmissionTint);
        Assert.Equal(1.2f, resolved.EmissionStrength);
    }

    [Theory]
    [InlineData(GameplayMaterialFeedback.DamageFlash)]
    [InlineData(GameplayMaterialFeedback.Frozen)]
    [InlineData(GameplayMaterialFeedback.Cloaked)]
    [InlineData(GameplayMaterialFeedback.DoubleDamage)]
    [InlineData(GameplayMaterialFeedback.Selection)]
    public void GameplayFeedbackDominatesSkinMaterial(
        GameplayMaterialFeedback feedback)
    {
        var skin = new CosmeticMaterialOverride(Smoothness: 1,
            ReflectionStrength: 1, EmissionTint: Vector3.UnitX,
            EmissionStrength: 16);

        Assert.Equal(BaseMaterial,
            CosmeticMaterialPipeline.Resolve(BaseMaterial, skin, feedback));
    }

    [Fact]
    public void MalformedNumericOverridesFailSoftAndFiniteValuesAreBounded()
    {
        var skin = new CosmeticMaterialOverride(
            SpecularStrength: float.NaN,
            Smoothness: 5,
            ReflectionStrength: -5,
            EmissionTint: new Vector3(float.PositiveInfinity),
            EmissionStrength: 50);

        EnhancedMaterial resolved = CosmeticMaterialPipeline.Resolve(BaseMaterial, skin);

        Assert.Equal(BaseMaterial.SpecularStrength, resolved.SpecularStrength);
        Assert.Equal(1, resolved.Smoothness);
        Assert.Equal(0, resolved.ReflectionStrength);
        Assert.Equal(BaseMaterial.EmissionTint, resolved.EmissionTint);
        Assert.Equal(16, resolved.EmissionStrength);
    }

    [Fact]
    public void TeamResolutionRetainsCanonicalRecolorAndStrongModeSuppressesGlow()
    {
        TextureAssetKey source = TextureAssetKey.ForModel("samus", 3, 0, 0);
        var skin = new SkinDefinition(1, "prime.skin.samus.test", "Test",
            Hunter.Samus, BaseRecolor: 4,
            Materials:
            [
                new SkinMaterialOverride(source.Value, Smoothness: .8f,
                    EmissionTint: new CosmeticColor(.9f, .2f, .1f),
                    EmissionStrength: 2)
            ],
            TeamAccent: new TeamAccentDefinition(true, "masks/team.png", .6f));
        var presentation = new SkinPresentation(new CosmeticCatalog([skin], [], []));
        presentation.Select(1, Hunter.Samus);

        SkinAppearanceResolution solo = presentation.ResolveAppearance(
            canonicalRecolor: 2, source, GameplayMaterialFeedback.None,
            teamMode: false, forceStrongTeamColors: false);
        Assert.Equal(4, solo.Recolor);
        Assert.Equal(2, solo.Material!.Value.EmissionStrength);

        SkinAppearanceResolution team = presentation.ResolveAppearance(
            canonicalRecolor: 2, source, GameplayMaterialFeedback.None,
            teamMode: true, forceStrongTeamColors: false);
        Assert.Equal(2, team.Recolor);
        Assert.Equal(.6f, team.TeamAccentStrength);
        Assert.Equal("masks/team.png", team.TeamAccentMask);
        Assert.Equal(2, team.Material!.Value.EmissionStrength);

        SkinAppearanceResolution strong = presentation.ResolveAppearance(
            canonicalRecolor: 2, source, GameplayMaterialFeedback.None,
            teamMode: true, forceStrongTeamColors: true);
        Assert.Equal(2, strong.Recolor);
        Assert.Equal(1, strong.TeamAccentStrength);
        Assert.Null(strong.Material!.Value.EmissionStrength);
        Assert.Null(strong.Material.Value.EmissionTint);
        Assert.Equal(.8f, strong.Material.Value.Smoothness);
    }

    [Fact]
    public void TeamSkinWithoutAccentFallsBackToCanonicalAppearance()
    {
        TextureAssetKey source = TextureAssetKey.ForModel("samus", 3, 0, 0);
        var skin = new SkinDefinition(1, "prime.skin.samus.test", "Test",
            Hunter.Samus, BaseRecolor: 4,
            Materials: [new SkinMaterialOverride(source.Value, Smoothness: .8f)]);
        var presentation = new SkinPresentation(new CosmeticCatalog([skin], [], []));
        presentation.Select(1, Hunter.Samus);

        SkinAppearanceResolution team = presentation.ResolveAppearance(
            canonicalRecolor: 2, source, GameplayMaterialFeedback.None,
            teamMode: true, forceStrongTeamColors: false);

        Assert.Equal(2, team.Recolor);
        Assert.Null(team.Material);
        Assert.Equal(0, team.TeamAccentStrength);
    }
}
