using System;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Formats;
using Xunit;

public sealed class EnhancedMaterialPresentationTests
{
    [Fact]
    public void EffectKeyUsesCurrentAnimatedTextureAndFailsSoftForInvalidIdentity()
    {
        var material = (Material)RuntimeHelpers.GetUninitializedObject(typeof(Material));
        material.CurrentTextureId = 7;
        Assert.Equal(TextureAssetKey.ForEffect("missile", 7),
            ScenePresentation.GetEffectTextureAssetKey("MISSILE", material));
        material.CurrentTextureId = 8;
        Assert.Equal(TextureAssetKey.ForEffect("missile", 8),
            ScenePresentation.GetEffectTextureAssetKey("MISSILE", material));
        material.CurrentTextureId = -1;
        Assert.Null(ScenePresentation.GetEffectTextureAssetKey("missile", material));
        material.CurrentTextureId = 0;
        Assert.Null(ScenePresentation.GetEffectTextureAssetKey("", material));
        Assert.Null(ScenePresentation.GetEffectTextureAssetKey("../missile", material));
    }

    [Fact]
    public void ProductionEffectAndHunterDrawsForwardPersistentKeysToMaterialResolution()
    {
        string effects = Read("src/Client.Presentation/Rendering/EffectPresentation.cs");
        string players = Read("src/Client.Presentation/Presentation/Players/PresentationPlayerDraw.cs");
        string renderer = Read("src/Client.Presentation/Rendering/Renderer.cs");
        // Both node effects and billboard effects must carry the same effect namespace.
        Assert.Equal(2, effects.Split(
            "textureAssetKey: ScenePresentation.GetEffectTextureAssetKey(particle.Owner.EffectName, material)",
            StringSplitOptions.None).Length - 1);
        Assert.Contains("textureAssetKey: ScenePresentation.GetModelTextureAssetKey(\n                    particle.ParticleDefinition.Model, material, 0)", effects);
        Assert.Contains("TextureAssetKey? textureAssetKey = ScenePresentation.GetModelTextureAssetKey(\n                        model, material, initialRecolor);", players);
        Assert.Contains("SkinAppearanceResolution skin = ResolveSkinAppearance(\n                        canonicalRecolor, textureAssetKey, materialFeedback);", players);
        Assert.Contains("textureAssetKey: textureAssetKey", players);
        int start = renderer.IndexOf("public void AddRenderItem(RenderPrimitive type, float alpha", StringComparison.Ordinal);
        int end = renderer.IndexOf("// for Morph Ball trails", start, StringComparison.Ordinal);
        string particleSubmission = renderer[start..end];
        Assert.Contains("TextureAssetKey? textureAssetKey = null", particleSubmission);
        Assert.Contains("item.TextureAssetKey = textureAssetKey;", particleSubmission);
        Assert.Contains("AddRenderItem(item);", particleSubmission);
        Assert.Contains("ResolveEnhancedMaterial(item);", renderer);
        Assert.Contains("item.HasTexture && !bindingOverride.HasValue", renderer);
    }

    private static string Read(string path)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Game.sln")))
            root = Directory.GetParent(root)?.FullName;
        return File.ReadAllText(Path.Combine(root ?? throw new InvalidOperationException(
            "Repository root was not found."), path));
    }
}
