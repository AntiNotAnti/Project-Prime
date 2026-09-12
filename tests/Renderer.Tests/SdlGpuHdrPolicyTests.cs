using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using SDL;
using Xunit;

public sealed class SdlGpuHdrPolicyTests
{
    private const SDL_GPUTextureFormat Ldr
        = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM;

    [Fact]
    public void NonMacPlatformsAllowSdlToSelectTheGpuDriver()
    {
        Assert.Null(SdlGpuDevice.PreferredDriverNameForPlatform(isMacOS: false));
        Assert.Equal("metal", SdlGpuDevice.PreferredDriverNameForPlatform(isMacOS: true));
    }

    [Fact]
    public void EnhancedUsesFloatSceneOnlyWhenRequiredUsageIsSupported()
    {
        SdlGpuSceneColorPlan hdr = SdlGpuHdrPolicy.Resolve(
            GraphicsPreset.Enhanced, Ldr, hdrColorTargetAndSamplerSupported: true);
        SdlGpuSceneColorPlan fallback = SdlGpuHdrPolicy.Resolve(
            GraphicsPreset.Enhanced, Ldr, hdrColorTargetAndSamplerSupported: false);

        Assert.True(hdr.UsesHdr);
        Assert.Equal(SdlGpuHdrPolicy.HdrFormat, hdr.Format);
        Assert.Equal(SdlGpuHdrFallbackReason.None, hdr.FallbackReason);
        Assert.False(fallback.UsesHdr);
        Assert.Equal(Ldr, fallback.Format);
        Assert.Equal(SdlGpuHdrFallbackReason.UnsupportedRenderTarget,
            fallback.FallbackReason);
    }

    [Theory]
    [InlineData(GraphicsPreset.Original)]
    [InlineData(GraphicsPreset.Performance)]
    public void NonEnhancedPresetsKeepTheLdrScene(GraphicsPreset preset)
    {
        SdlGpuSceneColorPlan plan = SdlGpuHdrPolicy.Resolve(preset, Ldr,
            hdrColorTargetAndSamplerSupported: true);

        Assert.False(plan.UsesHdr);
        Assert.Equal(Ldr, plan.Format);
        Assert.Equal(SdlGpuHdrFallbackReason.PresetDoesNotRequestHdr,
            plan.FallbackReason);
    }

    [Fact]
    public void OutputTransferPerformsExactlyOneGammaConversion()
    {
        SdlGpuOutputTransferPolicy unorm = SdlGpuOutputTransferPolicy.Resolve(
            GraphicsPreset.Enhanced, swapchainIsSrgb: false);
        SdlGpuOutputTransferPolicy srgb = SdlGpuOutputTransferPolicy.Resolve(
            GraphicsPreset.Enhanced, swapchainIsSrgb: true);

        Assert.True(unorm.ToneMap);
        Assert.True(unorm.ShaderConvertsLinearToSrgb);
        Assert.True(unorm.DisplayAssetsConvertSrgbToLinear);
        Assert.True(srgb.ToneMap);
        Assert.False(srgb.ShaderConvertsLinearToSrgb);
        Assert.True(srgb.DisplayAssetsConvertSrgbToLinear);

        Assert.True(SdlGpuDevice.IsSrgbFormat(
            SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM_SRGB));
        Assert.True(SdlGpuDevice.IsSrgbFormat(
            SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM_SRGB));
        Assert.False(SdlGpuDevice.IsSrgbFormat(Ldr));
    }

    [Fact]
    public void SceneCaptureBranchesBeforeVisorAndHudRemainsCrisp()
    {
        SdlGpuColorPipelineStep[] steps
            = SdlGpuColorPipelinePlan.EnhancedSteps.ToArray();

        Assert.Equal(new[]
        {
            SdlGpuColorPipelineStep.SceneEffects,
            SdlGpuColorPipelineStep.ToneMap,
            SdlGpuColorPipelineStep.ColorGrade,
            SdlGpuColorPipelineStep.SceneCapture,
            SdlGpuColorPipelineStep.Visor,
            SdlGpuColorPipelineStep.HudScene,
            SdlGpuColorPipelineStep.Overlays,
            SdlGpuColorPipelineStep.OutputTransfer,
            SdlGpuColorPipelineStep.FinalCapture
        }, steps);
    }

    [Fact]
    public void FailedHdrConfigurationIsNotRetriedUntilConfigurationChanges()
    {
        SdlGpuHdrConfiguration configuration = new(GraphicsPreset.Enhanced,
            1280, 720, 1920, 1080, RequestedSamples: 4,
            CelDepthSampling: false, BloomRequested: true);
        SdlGpuHdrConfiguration? failed = null;

        Assert.True(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed, configuration));
        failed = configuration; // policy-simulated allocation failure
        Assert.False(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed, configuration));
        Assert.False(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed, configuration));
        Assert.True(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed,
            configuration with { SceneWidth = 1600 }));
        Assert.True(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed,
            configuration with { RequestedSamples = 2 }));
        Assert.True(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed,
            configuration with { BloomRequested = false }));
    }

    [Fact]
    public void PerFrameBloomEligibilityDoesNotInvalidateFailedHdrConfiguration()
    {
        const bool qualityBloomRequested = true;
        bool[] perFrameEligibleSubmission = { true, false, true, false };
        SdlGpuHdrConfiguration failed = new(GraphicsPreset.Enhanced,
            1280, 720, 1920, 1080, RequestedSamples: 4,
            CelDepthSampling: false,
            BloomRequested: qualityBloomRequested);

        foreach (bool eligible in perFrameEligibleSubmission)
        {
            // Eligibility controls whether this frame emits/allocates bloom,
            // but it is deliberately absent from the persistent failure key.
            SdlGpuHdrConfiguration current = ConfigurationForFrame(eligible);
            Assert.Equal(failed, current);
            Assert.False(SdlGpuHdrFailurePolicy.ShouldAttemptHdr(failed,
                current));
        }

        static SdlGpuHdrConfiguration ConfigurationForFrame(bool _)
            => new(GraphicsPreset.Enhanced, 1280, 720, 1920, 1080,
                RequestedSamples: 4, CelDepthSampling: false,
                BloomRequested: qualityBloomRequested);
    }

    [Fact]
    public void TranslucentHudAndFadeBlendIdenticallyForSrgbAndUnormOutputs()
    {
        Vector3 sceneLinear = new(0.08f, 0.25f, 0.6f);
        Vector3 hudSrgb = new(0.9f, 0.35f, 0.1f);
        Vector3 fadeSrgb = new(0.04f, 0.12f, 0.8f);
        Vector3 afterHud = SdlGpuDisplayCompositionMath.BlendAuthoredSrgb(
            sceneLinear, hudSrgb, 0.35f);
        Vector3 afterFade = SdlGpuDisplayCompositionMath.BlendAuthoredSrgb(
            afterHud, fadeSrgb, 0.45f);

        Vector3 unormStored = SdlGpuDisplayCompositionMath.ShaderOutput(
            afterFade, swapchainIsSrgb: false);
        Vector3 srgbShaderOutput = SdlGpuDisplayCompositionMath.ShaderOutput(
            afterFade, swapchainIsSrgb: true);
        Vector3 srgbStored = EnhancedColorMath.LinearToSrgb(srgbShaderOutput);

        AssertVectorNear(unormStored, srgbStored);
    }

    [Fact]
    public void OpaqueAuthoredHudRetainsItsDisplayColor()
    {
        Vector3 authoredSrgb = new(0.15f, 0.5f, 0.95f);
        Vector3 composed = SdlGpuDisplayCompositionMath.BlendAuthoredSrgb(
            new Vector3(0.8f, 0.1f, 0.3f), authoredSrgb, alpha: 1);

        AssertVectorNear(authoredSrgb,
            SdlGpuDisplayCompositionMath.ShaderOutput(composed,
                swapchainIsSrgb: false));
        AssertVectorNear(authoredSrgb,
            EnhancedColorMath.LinearToSrgb(
                SdlGpuDisplayCompositionMath.ShaderOutput(composed,
                    swapchainIsSrgb: true)));
    }

    [Fact]
    public void EnhancedSceneReadbackRequiresSdrConversionAndByteFormat()
    {
        Assert.True(SdlGpuCaptureColorPolicy.RequiresSdrSceneConversion(
            GraphicsPreset.Enhanced, CaptureTargetKind.SceneTarget));
        Assert.True(SdlGpuCaptureColorPolicy.RequiresSdrSceneConversion(
            GraphicsPreset.Enhanced, CaptureTargetKind.ThumbnailTarget));
        Assert.False(SdlGpuCaptureColorPolicy.RequiresSdrSceneConversion(
            GraphicsPreset.Enhanced, CaptureTargetKind.FinalPresentedFrame));
        Assert.False(SdlGpuCaptureColorPolicy.RequiresSdrSceneConversion(
            GraphicsPreset.Original, CaptureTargetKind.SceneTarget));
        Assert.Equal(Ldr, SdlGpuCaptureColorPolicy.ReadbackFormat(Ldr));
        Assert.NotEqual(SdlGpuHdrPolicy.HdrFormat,
            SdlGpuCaptureColorPolicy.ReadbackFormat(Ldr));
    }

    [Fact]
    public void LdrAllocationFallbackRetainsHighestSupportedRequestedSampleCount()
    {
        SdlGpuSampleNegotiation hdr = SdlGpuMsaaPolicy.Resolve(requested: 4,
            celDepthSampling: false, colorSupports2: false, colorSupports4: false,
            depthSupports2: true, depthSupports4: true);
        SdlGpuSampleNegotiation ldr = SdlGpuMsaaPolicy.Resolve(requested: 4,
            celDepthSampling: false, colorSupports2: true, colorSupports4: true,
            depthSupports2: true, depthSupports4: true);

        Assert.Equal(1, hdr.Effective);
        Assert.Equal(4, ldr.Effective);
        Assert.Equal(SdlGpuMsaaFallbackReason.None, ldr.Reason);
    }

    [Fact]
    public void FixedExposureIsValidatedCapturedAndReset()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        Capture(frame, exposure: 1.25f);
        Assert.Equal(1.25f, frame.Exposure);
        frame.Reset();
        Assert.Equal(EnhancedColorMath.DefaultExposure, frame.Exposure);

        Assert.Throws<ArgumentOutOfRangeException>(() => Capture(frame, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Capture(frame, float.NaN));
    }

    [Fact]
    public void ColorGradeStateIsValidatedCapturedAndReset()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        var identity = new TextureIdentity(new object(), variant: "lut");
        var state = new RenderColorGradeState(identity, 0.75f);

        frame.CaptureColorGrade(state);
        Assert.True(frame.ColorGrade.Enabled);
        Assert.Equal(identity, frame.ColorGrade.LutTexture);
        Assert.Equal(0.75f, frame.ColorGrade.Strength);
        frame.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            frame.CaptureColorGrade(RenderColorGradeState.Disabled));

        frame.Reset();
        Assert.False(frame.ColorGrade.Enabled);
        Assert.Null(frame.ColorGrade.LutTexture);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RenderColorGradeState(identity, -0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RenderColorGradeState(identity, 1.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RenderColorGradeState(identity, float.NaN));
    }

    [Fact]
    public void ColorGradeShaderUsesExactStripAddressingWithoutTransferFunctions()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "color_grade.hlsl"));

        Assert.Contains("float2(256.0f, 16.0f)", source, StringComparison.Ordinal);
        Assert.Contains("blueSlice * LutDimension + red * (LutDimension - 1.0f) + 0.5f",
            source, StringComparison.Ordinal);
        Assert.Contains("green * (LutDimension - 1.0f) + 0.5f",
            source, StringComparison.Ordinal);
        Assert.Contains("lerp(lower, upper, frac(blue))", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LinearToSRGB", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SRGBToLinear", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneShaderPreservesLegacyNormalAndInterpolatesResolvedDifAmbValues()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "scene.hlsl"));

        Assert.Contains("normalize(mul((float3x3)modelMatrix, input.normal))", source,
            StringComparison.Ordinal);
        Assert.Contains("float3 lightingDiffuse : TEXCOORD4", source,
            StringComparison.Ordinal);
        Assert.Contains("float3 lightingAmbient : TEXCOORD5", source,
            StringComparison.Ordinal);
        Assert.Contains("output.lightingDiffuse = resolvedDiffuse", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("input.vertexColor.a == 0.0f", source,
            StringComparison.Ordinal);
        Assert.Contains("? max(surfaceColor + visualLighting, 0.0f)", source,
            StringComparison.Ordinal);
    }

    private static void Capture(RenderFrame frame, float exposure)
    {
        frame.CaptureState(Matrix4.Identity, Matrix4.Identity, Matrix4.Identity,
            Matrix4.Identity, Vector3.Zero, new Vector2i(640, 480),
            new Vector2i(640, 480), Vector4.UnitW, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, hasFog: false, Vector4.Zero, 0, 0,
            default, exposure);
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0, 0.00001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0, 0.00001f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0, 0.00001f);
    }
}
