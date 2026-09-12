using System;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class SdlGpuEnhancedSurfacePolicyTests
{
    [Theory]
    [InlineData(1920u, 1080u, 960u, 540u)]
    [InlineData(1919u, 1079u, 960u, 540u)]
    [InlineData(1u, 1u, 1u, 1u)]
    public void PlanUsesCeilHalfResolutionAndBoundedStorage(uint width,
        uint height, uint ssaoWidth, uint ssaoHeight)
    {
        SdlGpuEnhancedSurfacePlan plan = SdlGpuEnhancedSurfacePlan.Create(
            width, height);

        Assert.Equal(ssaoWidth, plan.SsaoWidth);
        Assert.Equal(ssaoHeight, plan.SsaoHeight);
        Assert.InRange(plan.EstimatedBytes, 1,
            SdlGpuEnhancedSurfacePlan.MaximumEstimatedBytes);
    }

    [Fact]
    public void PlanRejectsInvalidAndUnboundedConfigurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuEnhancedSurfacePlan.Create(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuEnhancedSurfacePlan.Create(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuEnhancedSurfacePlan.Create(16384, 16384));
    }

    [Fact]
    public void FailureCacheRetriesOnlyAfterConfigurationChanges()
    {
        var cache = new SdlGpuConfigurationFailureCache<
            SdlGpuEnhancedSurfaceConfiguration>();
        var failed = new SdlGpuEnhancedSurfaceConfiguration(1920, 1080);

        Assert.True(cache.ShouldAttempt(failed));
        cache.RecordFailure(failed);
        Assert.False(cache.ShouldAttempt(failed));
        Assert.True(cache.ShouldAttempt(new(1280, 720)));
        cache.RecordSuccess();
        Assert.True(cache.ShouldAttempt(failed));
    }

    [Fact]
    public void SurfaceCelAllowsMsaaAndLegacyFallbackRemainsExplicit()
    {
        Assert.True(SdlGpuSurfaceCelPolicy.UsesSurfaceData(
            GraphicsPreset.Enhanced, celEnabled: true, outline: 0.25f,
            surfaceAvailable: true));
        Assert.False(SdlGpuSurfaceCelPolicy.RequiresLegacyDepthSampling(
            GraphicsPreset.Enhanced, celEnabled: true, outline: 0.25f,
            surfaceAvailable: true, depthSampleable: true));
        Assert.True(SdlGpuSurfaceCelPolicy.RequiresLegacyDepthSampling(
            GraphicsPreset.Enhanced, celEnabled: true, outline: 0.25f,
            surfaceAvailable: false, depthSampleable: true));
        Assert.True(SdlGpuSurfaceCelPolicy.RequiresLegacyDepthSampling(
            GraphicsPreset.Original, celEnabled: true, outline: 0.25f,
            surfaceAvailable: true, depthSampleable: true));
    }

    [Fact]
    public void RelativeDepthEdgesAreScaleIndependent()
    {
        float near = SdlGpuSurfaceCelPolicy.RelativeDepthDifference(10, 11);
        float far = SdlGpuSurfaceCelPolicy.RelativeDepthDifference(100, 110);

        Assert.Equal(near, far, precision: 6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuSurfaceCelPolicy.RelativeDepthDifference(0, 1));
    }

    [Fact]
    public void IsolatedTiltedPlaneDoesNotSelfOcclude()
    {
        Vector3 center = new(0, 0, -5);
        Vector3 normal = new Vector3(0.3f, 0.8f, 0.5f).Normalized();
        Vector3 tangent = Vector3.Cross(normal, Vector3.UnitZ).Normalized();
        Vector3 sample = center + normal * 0.3f + tangent * 0.1f;
        Vector3 neighborOnPlane = center + tangent * 0.2f;

        Assert.False(SdlGpuSsaoReference.IsOccluding(center, normal,
            sample, neighborOnPlane, radius: 0.75f, bias: 0.02f));
    }

    [Fact]
    public void ViewReconstructionPreservesWorldRadiusAcrossDepthAndFov()
    {
        Vector3 offset = new(0.35f, 0.2f, 0);
        foreach (float fov in new[] { MathF.PI / 3, MathF.PI / 2 })
        foreach (float depth in new[] { 3f, 20f })
        {
            Vector3 center = new(0, 0, -depth);
            Vector3 sample = center + offset;
            Vector2 centerUv = SdlGpuSsaoReference.ProjectViewPosition(
                center, fov, 16f / 9);
            Vector2 sampleUv = SdlGpuSsaoReference.ProjectViewPosition(
                sample, fov, 16f / 9);
            Vector3 reconstructedCenter
                = SdlGpuSsaoReference.ReconstructViewPosition(centerUv,
                    depth, fov, 16f / 9);
            Vector3 reconstructedSample
                = SdlGpuSsaoReference.ReconstructViewPosition(sampleUv,
                    depth, fov, 16f / 9);

            Assert.Equal(offset.Length,
                (reconstructedSample - reconstructedCenter).Length,
                precision: 5);
        }
    }

    [Fact]
    public void CornerContactOccludesWithinBoundedHemisphere()
    {
        Vector3 center = new(0, 0, -5);
        Vector3 normal = Vector3.UnitY;
        Vector3 sample = center + new Vector3(0.25f, 0.25f, -0.1f);
        Vector3 corner = center + new Vector3(0.24f, 0.2f, 0);

        Assert.True(SdlGpuSsaoReference.IsOccluding(center, normal,
            sample, corner, radius: 0.75f, bias: 0.02f));
        Assert.False(SdlGpuSsaoReference.IsOccluding(center, normal,
            sample, center + new Vector3(2, 0.2f, 0),
            radius: 0.75f, bias: 0.02f));
    }
}
