using System;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Render;
using Xunit;

public sealed class GlesEnhancedPolicyTests
{
    private static readonly GlesEnhancedCapabilities FullySupported = new(
        MajorVersion: 3,
        MinorVersion: 0,
        FragmentTextureUnits: GlesEnhancedPolicy.MinimumFragmentTextureUnits,
        FragmentUniformVectors: GlesEnhancedPolicy.MinimumFragmentUniformVectors,
        FloatingPointColorBufferExtension: true,
        FloatingPointTargetProbeSucceeded: true);

    [Theory]
    [InlineData(GraphicsPreset.Original)]
    [InlineData(GraphicsPreset.Performance)]
    public void LegacyPresetsNeverSelectEnhancedFeatures(GraphicsPreset preset)
    {
        GlesEnhancedPlan plan = Resolve(preset, FullySupported);

        Assert.Equal(GlesEnhancedMode.Legacy, plan.Mode);
        Assert.Equal(GlesEnhancedFallbackReason.PresetDoesNotRequestEnhanced,
            plan.FallbackReason);
        Assert.False(plan.UsesEnhancedLightingAndMaterials);
        Assert.False(plan.UsesFloatingPointSceneTarget);
        Assert.False(plan.ToneMap);
        Assert.False(plan.ColorGrade);
    }

    [Fact]
    public void EnhancedWithAllCapabilitiesSelectsFloatingPointToneAndGrade()
    {
        GlesEnhancedPlan plan = Resolve(GraphicsPreset.Enhanced, FullySupported);

        Assert.Equal(GlesEnhancedMode.EnhancedFloatingPoint, plan.Mode);
        Assert.Equal(GlesEnhancedFallbackReason.None, plan.FallbackReason);
        Assert.True(plan.UsesEnhancedLightingAndMaterials);
        Assert.True(plan.UsesFloatingPointSceneTarget);
        Assert.True(plan.ToneMap);
        Assert.True(plan.ColorGrade);
    }

    [Theory]
    [InlineData(false, true,
        (int)GlesEnhancedFallbackReason.FloatingPointExtensionUnavailable)]
    [InlineData(true, false, (int)GlesEnhancedFallbackReason.FloatingPointProbeFailed)]
    public void FloatingPointCapabilityFailureKeepsEnhancedLightingAndMaterials(
        bool extension, bool probe, int reasonValue)
    {
        GlesEnhancedCapabilities capabilities = FullySupported with
        {
            FloatingPointColorBufferExtension = extension,
            FloatingPointTargetProbeSucceeded = probe
        };

        GlesEnhancedPlan plan = Resolve(GraphicsPreset.Enhanced, capabilities);

        Assert.Equal(GlesEnhancedMode.EnhancedLdr, plan.Mode);
        Assert.Equal((GlesEnhancedFallbackReason)reasonValue, plan.FallbackReason);
        Assert.True(plan.UsesEnhancedLightingAndMaterials);
        Assert.False(plan.UsesFloatingPointSceneTarget);
        Assert.False(plan.ToneMap);
        Assert.False(plan.ColorGrade);
    }

    [Fact]
    public void EnhancedShaderRequirementsFailDeterministicallyToLegacy()
    {
        AssertLegacy(
            FullySupported with { MajorVersion = 2, MinorVersion = 9 },
            GlesEnhancedFallbackReason.UnsupportedGlesVersion);
        AssertLegacy(
            FullySupported with { MajorVersion = 3, MinorVersion = -1 },
            GlesEnhancedFallbackReason.UnsupportedGlesVersion);
        AssertLegacy(FullySupported with
        {
            FragmentTextureUnits = GlesEnhancedPolicy.MinimumFragmentTextureUnits - 1
        }, GlesEnhancedFallbackReason.InsufficientTextureUnits);
        AssertLegacy(FullySupported with
        {
            FragmentUniformVectors = GlesEnhancedPolicy.MinimumFragmentUniformVectors - 1
        }, GlesEnhancedFallbackReason.InsufficientFragmentUniformVectors);
    }

    [Fact]
    public void AllocationFailureIsStickyOnlyForExactContextAndDimensions()
    {
        var cache = new GlesFloatingPointFailureCache();
        var original = new GlesFloatingPointConfiguration(1, 1280, 720);

        Assert.True(cache.ShouldAttempt(original));
        cache.RecordFailure(original);
        GlesEnhancedPlan repeated = GlesEnhancedPolicy.Resolve(
            GraphicsPreset.Enhanced, FullySupported, original, cache);
        Assert.Equal(GlesEnhancedMode.EnhancedLdr, repeated.Mode);
        Assert.Equal(GlesEnhancedFallbackReason.CachedFloatingPointAllocationFailure,
            repeated.FallbackReason);
        Assert.False(cache.ShouldAttempt(original));

        var resized = new GlesFloatingPointConfiguration(1, 1920, 1080);
        GlesEnhancedPlan resizedPlan = GlesEnhancedPolicy.Resolve(
            GraphicsPreset.Enhanced, FullySupported, resized, cache);
        Assert.Equal(GlesEnhancedMode.EnhancedFloatingPoint, resizedPlan.Mode);
        Assert.Null(cache.FailedConfiguration);

        cache.RecordFailure(resized);
        var replacementContext = new GlesFloatingPointConfiguration(2, 1920, 1080);
        GlesEnhancedPlan replacementPlan = GlesEnhancedPolicy.Resolve(
            GraphicsPreset.Enhanced, FullySupported, replacementContext, cache);
        Assert.Equal(GlesEnhancedMode.EnhancedFloatingPoint, replacementPlan.Mode);
        Assert.Null(cache.FailedConfiguration);
    }

    [Fact]
    public void SuccessfulAllocationClearsPriorFailure()
    {
        var cache = new GlesFloatingPointFailureCache();
        var configuration = new GlesFloatingPointConfiguration(4, 640, 360);
        cache.RecordFailure(configuration);

        cache.RecordSuccess();

        Assert.Null(cache.FailedConfiguration);
        Assert.True(cache.ShouldAttempt(configuration));
    }

    [Fact]
    public void InvalidFloatingPointConfigurationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GlesFloatingPointConfiguration(0, 640, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GlesFloatingPointConfiguration(1, 0, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GlesFloatingPointConfiguration(1, 640, -1));
    }

    [Fact]
    public void EnhancedTargetFailureRetriesOnlyAfterResizeOrNewContext()
    {
        var cache = new GlesEnhancedTargetFailureCache();
        var failed = new GlesEnhancedTargetConfiguration(1, 640, 360, 1280, 720);
        cache.RecordFailure(failed);

        Assert.False(cache.ShouldAttempt(failed));
        Assert.True(cache.ShouldAttempt(failed with { DrawableWidth = 1920 }));
        cache.RecordFailure(failed);
        Assert.True(cache.ShouldAttempt(failed with { ContextGeneration = 2 }));
    }

    [Fact]
    public void ImmutableUploadKeyIncludesContextIdentityAndRevision()
    {
        var identity = new TextureIdentity(new object(), textureId: 3);
        var original = new GlesImmutableTextureKey(1, identity, 7);

        Assert.Equal(original, new GlesImmutableTextureKey(1, identity, 7));
        Assert.NotEqual(original, original with { ContextGeneration = 2 });
        Assert.NotEqual(original, original with { Revision = 8 });
        Assert.NotEqual(original, original with
        {
            Identity = new TextureIdentity(new object(), textureId: 3)
        });
    }

    [Fact]
    public void EnhancedCompositionOrderKeepsCaptureAndOverlaysSdrSafe()
    {
        Assert.True(GlesEnhancedCompositionStage.ToneMapOrLdrBypass
            < GlesEnhancedCompositionStage.HudScene);
        Assert.True(GlesEnhancedCompositionStage.HudScene
            < GlesEnhancedCompositionStage.SceneCaptureTransfer);
        Assert.True(GlesEnhancedCompositionStage.SceneCaptureTransfer
            < GlesEnhancedCompositionStage.Disruption);
        Assert.True(GlesEnhancedCompositionStage.Fade
            < GlesEnhancedCompositionStage.FinalOutputTransfer);
    }

    [Fact]
    public void ConsecutiveFramesReestablishDepthAfterFullscreenPresentation()
    {
        GlesEnhancedRasterState firstWorld = GlesEnhancedRasterState.EnterWorld();
        GlesEnhancedRasterState present = GlesEnhancedRasterState.LeavePresent(
            faceCulling: true);
        GlesEnhancedRasterState secondWorld = GlesEnhancedRasterState.EnterWorld();

        Assert.True(firstWorld.DepthTest);
        Assert.True(present.DepthTest);
        Assert.True(secondWorld.DepthTest);
        Assert.False(secondWorld.Blend);
    }

    [Fact]
    public void FullFrameOfPinnedTexturesRejectsOverflowAllocation()
    {
        int capacity = GlesEnhancedShaderContract.MaximumCachedTextures;

        Assert.False(GlesEnhancedTextureCachePolicy.CanAllocate(
            cachedCount: capacity, unpinnedCount: 0));
        Assert.True(GlesEnhancedTextureCachePolicy.CanAllocate(
            cachedCount: capacity, unpinnedCount: 1));
        Assert.True(GlesEnhancedTextureCachePolicy.CanAllocate(
            cachedCount: capacity - 1, unpinnedCount: 0));
    }

    [Fact]
    public void UncachedLutAtFullPinnedCapacityDisablesColorGrade()
    {
        int capacity = GlesEnhancedShaderContract.MaximumCachedTextures;
        bool lutResolved = GlesEnhancedTextureCachePolicy.CanAllocate(
            cachedCount: capacity, unpinnedCount: 0);

        Assert.False(lutResolved);
        Assert.False(GlesEnhancedColorGradePolicy.ShouldEnable(
            requested: true, lutResolved: lutResolved));
        Assert.True(GlesEnhancedColorGradePolicy.ShouldEnable(
            requested: true, lutResolved: true));
    }

    private static GlesEnhancedPlan Resolve(GraphicsPreset preset,
        GlesEnhancedCapabilities capabilities)
        => GlesEnhancedPolicy.Resolve(preset, capabilities,
            new GlesFloatingPointConfiguration(1, 1280, 720),
            new GlesFloatingPointFailureCache());

    private static void AssertLegacy(GlesEnhancedCapabilities capabilities,
        GlesEnhancedFallbackReason reason)
    {
        GlesEnhancedPlan plan = Resolve(GraphicsPreset.Enhanced, capabilities);
        Assert.Equal(GlesEnhancedMode.Legacy, plan.Mode);
        Assert.Equal(reason, plan.FallbackReason);
        Assert.False(plan.UsesEnhancedLightingAndMaterials);
    }
}
