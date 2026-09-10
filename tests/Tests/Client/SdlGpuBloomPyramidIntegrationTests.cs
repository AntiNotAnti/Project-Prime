using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using Xunit;

public sealed class SdlGpuBloomPyramidIntegrationTests
{
    [Fact]
    public void SdlPostResourcesOwnExactlyTwoTargetsPerFixedLevel()
    {
        FieldInfo[] textures = typeof(SdlGpuPostResources)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.Name.StartsWith("_bloom", StringComparison.Ordinal)
                && field.FieldType.IsPointer)
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "_bloomEighthA",
            "_bloomEighthB",
            "_bloomQuarterA",
            "_bloomQuarterB",
            "_bloomSixteenthA",
            "_bloomSixteenthB"
        }, textures.Select(field => field.Name));
    }

    [Fact]
    public void CanonicalShaderSupportsEveryPyramidOperationAndTwoInputs()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "bloom.hlsl"));

        Assert.Contains("Texture2D sourceTexture : register(t0, space2)", source,
            StringComparison.Ordinal);
        Assert.Contains("Texture2D secondaryTexture : register(t1, space2)", source,
            StringComparison.Ordinal);
        Assert.Contains("SamplerState secondarySampler : register(s1, space2)", source,
            StringComparison.Ordinal);
        Assert.Contains("float3 Downsample", source, StringComparison.Ordinal);
        Assert.Contains("sourcePixelsPerOutput = outputUvStep / texelSize.xy", source,
            StringComparison.Ordinal);
        Assert.Contains("footprintScale = sourcePixelsPerOutput * 0.25f", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("float2 offset = texelSize.xy * 0.5f", source,
            StringComparison.Ordinal);
        Assert.Contains("bloomOptions.w > 0.5f", source, StringComparison.Ordinal);
        Assert.Contains("bloomOptions.w > 1.5f", source, StringComparison.Ordinal);
        Assert.Contains("bloomOptions.w > 2.5f", source, StringComparison.Ordinal);
        Assert.Contains("deliberately no", source, StringComparison.Ordinal);
        Assert.Contains("whole-scene brightness threshold", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeStrengthRemainsRestrainedForHdrCombination()
    {
        Assert.InRange(SdlGpuPostResources.BloomCompositeStrength, 0.01f, 1f);
    }
}
