using System;
using System.Linq;
using MphRead;
using Xunit;

public sealed class RenderGraphLiteTests
{
    private static readonly RenderGraphPassKind[] SixWorldPasses =
    {
        RenderGraphPassKind.Opaque,
        RenderGraphPassKind.Decal,
        RenderGraphPassKind.TransparentStencil,
        RenderGraphPassKind.DepthRebuild,
        RenderGraphPassKind.TransparentBehind,
        RenderGraphPassKind.TransparentFront
    };

    [Fact]
    public void EnhancedPlanPreservesWorldAndCaptureOrder()
    {
        var plan = new RenderExecutionPlan();
        RenderGraphLite.Build(plan, new RenderGraphFeatures(
            DirectionalShadow: true, SurfaceData: true, AmbientOcclusion: true,
            Sky: true, Distortion: true, Bloom: true, OriginalHud: false,
            EnhancedOutput: true, SceneCapture: true, Visor: true,
            EnhancedHud: true, SceneReadback: true, FinalReadback: true,
            Reconstruction: true));

        RenderGraphPassKind[] kinds = plan.Passes.ToArray()
            .Select(pass => pass.Kind).ToArray();
        int opaque = Array.IndexOf(kinds, RenderGraphPassKind.Opaque);
        Assert.Equal(SixWorldPasses, kinds.Skip(opaque).Take(6));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.Sky) < opaque);
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.DistortionVectors)
            > Array.IndexOf(kinds, RenderGraphPassKind.TransparentFront));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.SceneCaptureBase)
            < Array.IndexOf(kinds, RenderGraphPassKind.Visor));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.ScenePostProcess)
            < Array.IndexOf(kinds, RenderGraphPassKind.Reconstruction));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.Reconstruction)
            < Array.IndexOf(kinds, RenderGraphPassKind.SceneCaptureBase));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.SceneCaptureTransfer)
            < Array.IndexOf(kinds, RenderGraphPassKind.Overlay));
        Assert.True(Array.IndexOf(kinds, RenderGraphPassKind.FinalTransfer)
            < Array.IndexOf(kinds, RenderGraphPassKind.SceneReadback));
        Assert.Equal(RenderGraphPassKind.FinalReadback, kinds[^1]);

        RenderGraphPass enhancedHud = plan.Passes.ToArray().Single(
            pass => pass.Kind == RenderGraphPassKind.EnhancedHud);
        Assert.True(enhancedHud.Reads.HasFlag(RenderGraphResource.DisplayLinear));
        Assert.True(enhancedHud.Reads.HasFlag(RenderGraphResource.SceneCaptureLinear));
        Assert.True(enhancedHud.Writes.HasFlag(RenderGraphResource.SceneCaptureLinear));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalAndPerformanceUseTheBoundedDirectCompositePlan(bool bloom)
    {
        var plan = new RenderExecutionPlan();
        RenderGraphLite.Build(plan, new RenderGraphFeatures(
            DirectionalShadow: false, SurfaceData: false, AmbientOcclusion: false,
            Sky: false, Distortion: false, Bloom: bloom, OriginalHud: true,
            EnhancedOutput: false, SceneCapture: false, Visor: false,
            EnhancedHud: false));

        RenderGraphPassKind[] kinds = plan.Passes.ToArray()
            .Select(pass => pass.Kind).ToArray();
        Assert.DoesNotContain(RenderGraphPassKind.SurfaceData, kinds);
        Assert.DoesNotContain(RenderGraphPassKind.SceneCaptureBase, kinds);
        Assert.DoesNotContain(RenderGraphPassKind.FinalTransfer, kinds);
        Assert.Equal(RenderGraphPassKind.Overlay, kinds[^1]);
        Assert.Equal(bloom, kinds.Contains(RenderGraphPassKind.BloomEmission));
    }

    [Fact]
    public void RebuildDoesNotRetainOptionalPasses()
    {
        var plan = new RenderExecutionPlan();
        RenderGraphLite.Build(plan, new RenderGraphFeatures(true, true, true,
            true, true, true, false, true, true, true, true));
        int expandedCount = plan.Count;

        RenderGraphLite.Build(plan, new RenderGraphFeatures(false, false, false,
            false, false, false, true, false, false, false, false));

        Assert.True(plan.Count < expandedCount);
        Assert.DoesNotContain(plan.Passes.ToArray(),
            pass => pass.Kind == RenderGraphPassKind.DirectionalShadow);
        Assert.Equal(RenderGraphPassKind.Overlay, plan[plan.Count - 1].Kind);
    }

    [Fact]
    public void ValidationRejectsReadBeforeProduce()
    {
        RenderGraphPass[] invalid =
        {
            new(RenderGraphPassKind.Opaque,
                RenderGraphResource.SceneCaptureLinear,
                RenderGraphResource.SceneColor)
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RenderGraphLite.Validate(invalid, RenderGraphLite.ImportedResources));
        Assert.Contains("reads unavailable resources", error.Message);
    }

    [Fact]
    public void WorldPassesDescribeDistinctDepthStencilOwnership()
    {
        var plan = new RenderExecutionPlan();
        RenderGraphLite.Build(plan, new RenderGraphFeatures(false, false, false,
            true, false, false, true, false, false, false, false));

        RenderGraphPass opaque = plan.Passes.ToArray().Single(
            pass => pass.Kind == RenderGraphPassKind.Opaque);
        RenderGraphPass stencil = plan.Passes.ToArray().Single(
            pass => pass.Kind == RenderGraphPassKind.TransparentStencil);
        RenderGraphPass rebuild = plan.Passes.ToArray().Single(
            pass => pass.Kind == RenderGraphPassKind.DepthRebuild);
        Assert.True(opaque.Reads.HasFlag(RenderGraphResource.SceneColor));
        Assert.True(opaque.Writes.HasFlag(RenderGraphResource.SceneDepth));
        Assert.True(opaque.Writes.HasFlag(RenderGraphResource.SceneStencil));
        Assert.Equal(RenderGraphResource.SceneStencil, stencil.Writes);
        Assert.Equal(RenderGraphResource.SceneDepth, rebuild.Writes);
        Assert.True(rebuild.Reads.HasFlag(RenderGraphResource.SceneStencil));
    }

    [Fact]
    public void FeatureValidationRejectsImpossibleAoAndCapturePlans()
    {
        var plan = new RenderExecutionPlan();
        Assert.Throws<ArgumentException>(() => RenderGraphLite.Build(plan,
            new RenderGraphFeatures(false, false, true, false, false, false,
                true, false, false, false, false)));
        Assert.Throws<ArgumentException>(() => RenderGraphLite.Build(plan,
            new RenderGraphFeatures(false, false, false, false, false, false,
                true, false, true, false, false)));
        Assert.Throws<ArgumentException>(() => RenderGraphLite.Build(plan,
            new RenderGraphFeatures(false, false, false, false, false, false,
                true, false, false, false, false, Reconstruction: true)));
    }
}
