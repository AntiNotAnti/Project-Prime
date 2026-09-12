using System;
using System.IO;
using MphRead;
using MphRead.Mods;
using Xunit;

public sealed class SdlGpuReconstructionTests
{
    [Fact]
    public void ReconstructionIsEnhancedOnlyAndOnlyWhenScaling()
    {
        Assert.False(SdlGpuReconstructionPlan.Create(GraphicsPreset.Original,
            960, 540, 1920, 1080).Enabled);
        Assert.False(SdlGpuReconstructionPlan.Create(GraphicsPreset.Performance,
            960, 540, 1920, 1080).Enabled);
        Assert.False(SdlGpuReconstructionPlan.Create(GraphicsPreset.Enhanced,
            1920, 1080, 1920, 1080).Enabled);

        SdlGpuReconstructionPlan plan = SdlGpuReconstructionPlan.Create(
            GraphicsPreset.Enhanced, 960, 540, 1920, 1080);
        Assert.True(plan.Enabled);
        Assert.InRange(plan.Sharpness, 0, .25f);
    }

    [Fact]
    public void ReconstructionRejectsEmptyTargets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuReconstructionPlan.Create(GraphicsPreset.Enhanced,
                0, 540, 1920, 1080));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuReconstructionPlan.Create(GraphicsPreset.Enhanced,
                960, 540, 0, 1080));
    }

    [Fact]
    public void ShaderCombinesWindowedSincReconstructionWithBoundedSharpening()
    {
        string shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "reconstruction.hlsl"));
        Assert.Contains("Lanczos2", shader, StringComparison.Ordinal);
        Assert.Contains("neighborhoodMin", shader, StringComparison.Ordinal);
        Assert.Contains("clamp(sharpened, neighborhoodMin, neighborhoodMax)",
            shader, StringComparison.Ordinal);
    }
}
