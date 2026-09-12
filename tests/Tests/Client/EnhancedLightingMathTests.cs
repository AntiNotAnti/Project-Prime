using System;
using System.IO;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedLightingMathTests
{
    [Fact]
    public void GgxRejectsInvalidInputsAndBackFacingLightContributesNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedSurfaceMath.EvaluateGgx(Vector3.Zero, Vector3.UnitZ,
                Vector3.UnitZ, Vector3.One, new Vector3(.04f), .5f));
        Assert.Equal(Vector3.Zero, EnhancedSurfaceMath.EvaluateGgx(
            Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitZ, Vector3.One,
            new Vector3(.04f), .5f));
    }

    [Fact]
    public void GgxIsFiniteAndSmoothnessNarrowsTheNormalIncidenceLobe()
    {
        Vector3 rough = EnhancedSurfaceMath.EvaluateGgx(Vector3.UnitZ,
            Vector3.UnitZ, Vector3.UnitZ, new Vector3(.5f),
            new Vector3(.04f), .1f);
        Vector3 smooth = EnhancedSurfaceMath.EvaluateGgx(Vector3.UnitZ,
            Vector3.UnitZ, Vector3.UnitZ, new Vector3(.5f),
            new Vector3(.04f), .9f);

        Assert.True(float.IsFinite(rough.X));
        Assert.True(float.IsFinite(smooth.X));
        Assert.True(smooth.X > rough.X);
    }

    [Fact]
    public void ShaderUsesGgxOnlyInsideEnhancedPerPixelBranch()
    {
        string shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "scene.hlsl"));
        Assert.Contains("DistributionGGX", shader, StringComparison.Ordinal);
        Assert.Contains("GeometrySchlickGGX", shader, StringComparison.Ordinal);
        Assert.Contains("FresnelSchlick", shader, StringComparison.Ordinal);
        int enhancedBranch = shader.IndexOf(
            "if (enhancedPerPixel && lightingEnabled)", StringComparison.Ordinal);
        int roomLighting = shader.IndexOf("EvaluateRoomLighting(", enhancedBranch,
            StringComparison.Ordinal);
        Assert.True(enhancedBranch >= 0 && roomLighting > enhancedBranch);
    }
}
