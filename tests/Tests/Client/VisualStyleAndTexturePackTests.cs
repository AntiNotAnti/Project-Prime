using System;
using System.IO;
using System.Linq;
using MphRead.Mods;
using Xunit;

namespace MphRead.Tests;

public sealed class VisualStyleAndTexturePackTests
{
    [Theory]
    [InlineData("original", VisualStyle.Original)]
    [InlineData("cel shaded", VisualStyle.Cel)]
    [InlineData("flat", VisualStyle.Flat)]
    [InlineData("pixel", VisualStyle.Pixelated)]
    [InlineData("retro", VisualStyle.Retro)]
    public void VisualStyleTokensAreStable(string token, VisualStyle expected)
    {
        Assert.Equal(expected, RenderOptions.ParseVisualStyle(token));
        Assert.Equal(expected, RenderOptions.ParseVisualStyle(
            RenderOptions.FormatVisualStyle(expected)));
    }

    [Fact]
    public void VisualStylesReuseTheCapturedCelBandShaderSlot()
    {
        RenderFrameOptions options = FrameOptions();

        Assert.Equal(0, SdlGpuCelSurface.ShaderStyleCode(options));
        Assert.Equal(8, SdlGpuCelSurface.ShaderStyleCode(
            options with { VisualStyle = MphRead.Mods.VisualStyle.Cel }));
        Assert.Equal(-1, SdlGpuCelSurface.ShaderStyleCode(
            options with { VisualStyle = MphRead.Mods.VisualStyle.Flat }));
        Assert.Equal(-2, SdlGpuCelSurface.ShaderStyleCode(
            options with { VisualStyle = MphRead.Mods.VisualStyle.Pixelated }));
        Assert.Equal(-3, SdlGpuCelSurface.ShaderStyleCode(
            options with { VisualStyle = MphRead.Mods.VisualStyle.Retro }));
    }

    [Fact]
    public void TexturePackMigrationPreservesThePriorEnhancedDefault()
    {
        Assert.Equal(RenderOptions.DefaultTexturePackId,
            RenderOptions.ResolveTexturePackId(null, GraphicsPreset.Enhanced));
        Assert.Equal(RenderOptions.OriginalTexturePackId,
            RenderOptions.ResolveTexturePackId(null, GraphicsPreset.Original));
        Assert.Equal(RenderOptions.OriginalTexturePackId,
            RenderOptions.NormalizeTexturePackId("../outside"));
    }

    [Fact]
    public void TexturePackCatalogIsBoundedAndRequiresDirectManifests()
    {
        string temporary = Directory.CreateTempSubdirectory("prime-texture-packs-")
            .FullName;
        try
        {
            string enhancements = Directory.CreateDirectory(
                Path.Combine(temporary, "enhancements")).FullName;
            string installed = Directory.CreateDirectory(
                Path.Combine(enhancements, "clean_pack")).FullName;
            File.WriteAllText(Path.Combine(installed, "materials.json"), "{}");
            Directory.CreateDirectory(Path.Combine(enhancements, "no_manifest"));

            var options = TexturePackCatalog.Discover(temporary, "missing_pack");

            Assert.Contains(options, option => option.Id == "original");
            Assert.Contains(options, option => option.Id == "default");
            Assert.Contains(options, option => option.Id == "clean_pack"
                && option.Installed);
            Assert.DoesNotContain(options, option => option.Id == "no_manifest");
            Assert.Contains(options, option => option.Id == "missing_pack"
                && !option.Installed);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static RenderFrameOptions FrameOptions()
        => new(ShowTextures: true, ShowColors: true, Wireframe: false,
            FaceCulling: true, Filtering: true, Lighting: true, Fog: true,
            CelShading: false, CelBands: 8, CelEdge: .5f, VolumeEdges: 0,
            ShowInvisible: false, NoLines: false,
            Quality: new RenderQualitySnapshot(GraphicsPreset.Original,
                TextureFilteringPreset.Original, AnisotropyLevel.Off,
                MsaaLevel.Off, Bloom: false, DynamicVisualLights: false),
            VisualStyle: MphRead.Mods.VisualStyle.Original, EnhancedTextures: false);
}
