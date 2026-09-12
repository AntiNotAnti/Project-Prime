using System;
using System.Collections.Generic;
using System.Linq;
using MphRead;
using MphRead.Mods;
using Xunit;

public sealed class VisualEnhancementTests
{
    [Fact]
    public void VisualLightsUseStableBoundedDeterministicAdmission()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        for (int i = 0; i < RenderFrame.MaximumVisualLights; i++)
        {
            Assert.True(frame.AddVisualLight(Light(i, i)));
        }

        // Equal priority loses on deterministic camera relevance rather than
        // content arrival order when the bound is reached.
        Assert.False(frame.AddVisualLight(Light(99, 0)));
        Assert.Equal(RenderFrame.MaximumVisualLights, frame.VisualLights.Count);
        Assert.Equal(RenderFrame.MaximumVisualLights - 1,
            frame.VisualLights[0].Position.X);

        Assert.True(frame.AddVisualLight(Light(100, 100)));
        Assert.Equal(100, frame.VisualLights[0].Position.X);
        Assert.Equal(RenderFrame.MaximumVisualLights - 1,
            frame.VisualLights[1].Position.X);
        Assert.Equal(RenderFrame.MaximumVisualLights,
            frame.VisualLights.Count);

        frame.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            frame.AddVisualLight(Light(101, 101)));

        frame.Reset();
        Assert.Empty(frame.VisualLights);
        Assert.True(frame.AddVisualLight(Light(200, 1)));
    }

    [Fact]
    public void VisualLightViewIsReadOnlyAndValuesAreSnapshots()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        RenderVisualLight light = Light(1, 4);
        frame.AddVisualLight(light);

        Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<RenderVisualLight>>(
            frame.VisualLights);
        Assert.True(((System.Collections.Generic.IList<RenderVisualLight>)frame.VisualLights).IsReadOnly);
        Assert.Equal(light, frame.VisualLights[0]);
    }

    [Fact]
    public void BloomMetadataRequiresExplicitEnergyEligibilityWithoutChangingPasses()
    {
        var frame = new RenderFrame(capacity: 6, maximumCapacity: 6);

        DrawSubmission unlitModel = frame.Acquire();
        unlitModel.Primitive = RenderPrimitive.Mesh;
        unlitModel.Emission = OpenTK.Mathematics.Vector3.Zero;
        frame.Add(unlitModel);

        DrawSubmission emittingModel = frame.Acquire();
        emittingModel.Primitive = RenderPrimitive.Mesh;
        emittingModel.Emission = new OpenTK.Mathematics.Vector3(.25f, .5f, .1f);
        frame.Add(emittingModel);

        DrawSubmission particle = frame.Acquire();
        particle.Primitive = RenderPrimitive.Particle;
        particle.RenderMode = RenderMode.Translucent;
        frame.Add(particle);

        DrawSubmission trail = frame.Acquire();
        trail.Primitive = RenderPrimitive.TrailMulti;
        trail.RenderMode = RenderMode.Translucent;
        frame.Add(trail);

        DrawSubmission fuzzball = frame.Acquire();
        fuzzball.Primitive = RenderPrimitive.Particle;
        fuzzball.RenderMode = RenderMode.Translucent;
        fuzzball.BloomEligible = true;
        fuzzball.BloomStrength = RenderMaterial.SingleParticleBloomStrength(SingleType.Fuzzball);
        frame.Add(fuzzball);

        DrawSubmission energyTrail = frame.Acquire();
        energyTrail.Primitive = RenderPrimitive.TrailSingle;
        energyTrail.RenderMode = RenderMode.Translucent;
        energyTrail.BloomEligible = true;
        energyTrail.BloomStrength = RenderMaterial.TrailBloomStrength;
        frame.Add(energyTrail);

        Assert.False(unlitModel.BloomEligible);
        Assert.Equal(0, unlitModel.Material.BloomStrength);
        Assert.True(emittingModel.BloomEligible);
        Assert.Equal(.5f, emittingModel.Material.BloomStrength);
        Assert.False(particle.BloomEligible);
        Assert.Equal(0, particle.BloomStrength);
        Assert.False(trail.BloomEligible);
        Assert.Equal(0, trail.BloomStrength);
        Assert.True(fuzzball.BloomEligible);
        Assert.Equal(RenderMaterial.ParticleBloomStrength, fuzzball.BloomStrength);
        Assert.True(energyTrail.BloomEligible);
        Assert.Equal(RenderMaterial.TrailBloomStrength, energyTrail.BloomStrength);
        Assert.Equal(0, RenderMaterial.SingleParticleBloomStrength(SingleType.Death));
        Assert.Equal(RenderPassKind.TransparentStencil, particle.Pass);
        Assert.Equal(RenderPassKind.TransparentStencil, trail.Pass);
        Assert.Equal(RenderPassKind.TransparentStencil, fuzzball.Pass);
        Assert.Equal(RenderPassKind.TransparentStencil, energyTrail.Pass);
    }

    [Fact]
    public void LargeTransparentRgbaPixelsRemainBoundedAndOpaqueMetadataIsPreserved()
    {
        const int width = 4096;
        const int height = 1;
        byte[] pixels = new byte[width * height * 4];
        pixels[0] = 255;
        pixels[1] = 64;
        pixels[2] = 16;
        pixels[3] = 0;

        TextureIdentity identity = new(new object());
        var record = new RenderTexturePixels(identity, width, height, pixels,
            revision: 7, onlyOpaque: false,
            alphaWeightedFlatColor: new OpenTK.Mathematics.Vector3(.2f, .3f, .4f));

        Assert.Equal(width, record.Width);
        Assert.Equal(height, record.Height);
        Assert.False(record.OnlyOpaque);
        Assert.Equal(7, record.Revision);
        Assert.Equal(new OpenTK.Mathematics.Vector3(.2f, .3f, .4f),
            record.AlphaWeightedFlatColor);
        Assert.Equal(pixels, record.Rgba8.ToArray());
    }

    [Fact]
    public void TextureContractRejectsMalformedAndOverBudgetDimensionsDeterministically()
    {
        TextureIdentity identity = new(new object());
        Assert.Throws<ArgumentException>(() => new RenderTexturePixels(
            identity, 2, 2, new byte[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RenderTexturePixels.ValidateRgba8ByteCount(RenderTexturePixels.MaximumDimension + 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RenderTexturePixels.ValidateRgba8ByteCount(RenderTexturePixels.MaximumDimension,
                RenderTexturePixels.MaximumDimension));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RenderTexturePixels.ValidateRgba8ByteCount(Int32.MaxValue, Int32.MaxValue));
    }

    [Fact]
    public void SdlVisualLightFrameConstantsHaveFixedCountAndMatchingAbi()
    {
        Assert.Equal(RenderFrame.MaximumVisualLights,
            SdlGpuVisualLightPolicy.MaximumLights);
        Assert.Equal(144, SdlGpuSceneVertexFrameConstants.AbiByteSize);
        Assert.Equal(1792, SdlGpuSceneFragmentFrameConstants.AbiByteSize);
        Assert.Equal(RenderFrame.MaximumVisualLights * 4,
            SdlGpuSceneFragmentFrameConstants.VisualLightFloatCount);

        var lights = new List<RenderVisualLight>
        {
            new(new OpenTK.Mathematics.Vector3(1, 2, 3),
                new OpenTK.Mathematics.Vector3(.25f, .5f, .75f), 4, .6f, 2),
            new(new OpenTK.Mathematics.Vector3(5, 6, 7),
                OpenTK.Mathematics.Vector3.One, 8, .2f, 1)
        };
        SdlGpuSceneFragmentFrameConstants enabled = SdlGpuSceneFragmentFrameConstants.Create(
            OpenTK.Mathematics.Vector4.Zero, OpenTK.Mathematics.Vector4.Zero,
            OpenTK.Mathematics.Vector4.Zero, new OpenTK.Mathematics.Vector3(9, 8, 7),
            lights, enabled: true);
        SdlGpuSceneFragmentFrameConstants disabled = SdlGpuSceneFragmentFrameConstants.Create(
            OpenTK.Mathematics.Vector4.Zero, OpenTK.Mathematics.Vector4.Zero,
            OpenTK.Mathematics.Vector4.Zero, OpenTK.Mathematics.Vector3.Zero,
            lights, enabled: false);

        Assert.Equal(2, enabled.VisualLightCount);
        Assert.Equal(new OpenTK.Mathematics.Vector4(1, 2, 3, 4),
            enabled.GetPositionRadius(0));
        Assert.Equal(new OpenTK.Mathematics.Vector4(.25f, .5f, .75f, .6f),
            enabled.GetColorIntensity(0));
        Assert.Equal(3u, enabled.GetTileMask(0, 0));
        Assert.Equal(new OpenTK.Mathematics.Vector4(9, 8, 7, 1),
            enabled.CameraWorldPosition);
        Assert.Equal(0, disabled.VisualLightCount);
        Assert.Equal(1f, SdlGpuVisualLightPolicy.DistanceAttenuation(0, 10));
        Assert.Equal(.25f, SdlGpuVisualLightPolicy.DistanceAttenuation(5, 10));
        Assert.Equal(0f, SdlGpuVisualLightPolicy.DistanceAttenuation(10, 10));
        Assert.True(SdlGpuEnhancedLightingPolicy.UsesPerPixelLighting(GraphicsPreset.Enhanced));
        Assert.False(SdlGpuEnhancedLightingPolicy.UsesPerPixelLighting(GraphicsPreset.Original));
        Assert.False(SdlGpuEnhancedLightingPolicy.UsesPerPixelLighting(GraphicsPreset.Performance));
        Assert.Equal(11.75f,
            SdlGpuEnhancedLightingPolicy.SmoothnessExponent(
                SdlGpuEnhancedLightingPolicy.DefaultSmoothness));
        Assert.True(SdlGpuEnhancedLightingPolicy.NormalizedBlinnPhong(1, .25f) > 0);
        Assert.Equal(0f, SdlGpuEnhancedLightingPolicy.NormalizedBlinnPhong(-1, .25f));
        Assert.True(float.IsFinite(
            SdlGpuEnhancedLightingPolicy.NormalizedBlinnPhong(float.NaN, float.NaN)));
    }

    [Fact]
    public void SdlForwardTilesConservativelyCullDistantScreenLights()
    {
        var lights = new List<RenderVisualLight>
        {
            new(new OpenTK.Mathematics.Vector3(0, 0, -10),
                OpenTK.Mathematics.Vector3.One, radius: 1, intensity: 1,
                priority: 1)
        };
        SdlGpuSceneFragmentFrameConstants constants
            = SdlGpuSceneFragmentFrameConstants.Create(
                OpenTK.Mathematics.Vector4.Zero,
                OpenTK.Mathematics.Vector4.Zero,
                OpenTK.Mathematics.Vector4.Zero,
                OpenTK.Mathematics.Vector3.Zero, lights, enabled: true,
                viewport: new OpenTK.Mathematics.Vector2(1920, 1080),
                view: OpenTK.Mathematics.Matrix4.Identity,
                projection: OpenTK.Mathematics.Matrix4.CreatePerspectiveFieldOfView(
                    MathF.PI / 2, 16f / 9f, .1f, 100));

        Assert.Equal(0u, constants.GetTileMask(0, 0));
        Assert.Equal(1u, constants.GetTileMask(8, 4));
    }

    [Fact]
    public void SdlBloomPlanRequiresExplicitEligibilityAndHasFixedCompositeOrder()
    {
        RenderMaterial brightButIneligible = new()
        {
            Diffuse = OpenTK.Mathematics.Vector3.One,
            BloomEligible = false,
            BloomStrength = 1
        };
        RenderMaterial eligible = brightButIneligible;
        eligible.BloomEligible = true;
        eligible.BloomStrength = .2f;
        Assert.False(SdlGpuBloomPlan.IsEligible(brightButIneligible));
        Assert.True(SdlGpuBloomPlan.IsEligible(eligible));
        Assert.Equal(.2f, SdlGpuBloomPlan.Strength(eligible));
        Assert.Equal(OpenTK.Mathematics.Vector3.Zero,
            SdlGpuBloomPlan.ApplyFogVisibility(OpenTK.Mathematics.Vector3.One, 1));

        TextureIdentity emissive = new(new object());
        RenderMaterial mapped = brightButIneligible;
        mapped.Enhanced = new EnhancedMaterial(null, null, emissive, 0, .25f,
            0, new OpenTK.Mathematics.Vector3(1, .5f, .25f), 2);
        Assert.True(SdlGpuBloomPlan.IsEligible(mapped, GraphicsPreset.Enhanced));
        Assert.False(SdlGpuBloomPlan.IsEligible(mapped, GraphicsPreset.Original));
        Assert.Equal(1, SdlGpuBloomPlan.Strength(mapped, GraphicsPreset.Enhanced));
        OpenTK.Mathematics.Vector3 mappedColor = SdlGpuEmissionPolicy.Map(
            new OpenTK.Mathematics.Vector3(.5f, .25f, 1),
            mapped.Enhanced.Value.EmissionTint,
            mapped.Enhanced.Value.EmissionStrength);
        OpenTK.Mathematics.Vector3 expectedMapped
            = EnhancedColorMath.SrgbToLinear(
                new OpenTK.Mathematics.Vector3(.5f, .25f, 1))
                * new OpenTK.Mathematics.Vector3(1, .5f, .25f) * 2;
        Assert.Equal(expectedMapped, mappedColor);

        mapped.BloomEligible = true;
        mapped.BloomStrength = .2f;
        mapped.Enhanced = mapped.Enhanced.Value with { EmissionStrength = 0 };
        Assert.False(SdlGpuBloomPlan.IsEligible(mapped, GraphicsPreset.Enhanced));
        Assert.True(SdlGpuBloomPlan.IsEligible(mapped, GraphicsPreset.Original));
        Assert.Equal(OpenTK.Mathematics.Vector3.Zero,
            SdlGpuEmissionPolicy.Map(OpenTK.Mathematics.Vector3.One,
                OpenTK.Mathematics.Vector3.One, float.NaN));
        Assert.Equal(0, SdlGpuSceneSamplerAbi.Albedo);
        Assert.Equal(1, SdlGpuSceneSamplerAbi.Normal);
        Assert.Equal(2, SdlGpuSceneSamplerAbi.Emissive);
        Assert.Equal(3, SdlGpuSceneSamplerAbi.Reflection);
        Assert.Equal(4, SdlGpuSceneSamplerAbi.AmbientOcclusion);
        Assert.Equal(5, SdlGpuSceneSamplerAbi.Shadow);
        Assert.Equal(6, SdlGpuSceneSamplerAbi.SurfaceData);
        Assert.Equal(7, SdlGpuSceneSamplerAbi.Count);
        Assert.Equal(8, SdlGpuSceneSamplerAbi.BindingCountForDriver("direct3d12"));
        Assert.Equal(8, SdlGpuSceneSamplerAbi.BindingCountForDriver("DIRECT3D12"));
        Assert.Equal(7, SdlGpuSceneSamplerAbi.BindingCountForDriver("vulkan"));
        Assert.Equal(7, SdlGpuSceneSamplerAbi.BindingCountForDriver("metal"));

        RenderQualitySnapshot enhanced = new(GraphicsPreset.Enhanced,
            TextureFilteringPreset.Enhanced, AnisotropyLevel.X4, MsaaLevel.X4,
            Bloom: true, DynamicVisualLights: true);
        RenderFrame enabledFrame = FrameWithQuality(enhanced);
        DrawSubmission emitting = enabledFrame.Acquire();
        emitting.Emission = new OpenTK.Mathematics.Vector3(.1f, .2f, .1f);
        enabledFrame.Add(emitting);
        SdlGpuBloomPlan enabled = SdlGpuBloomPlan.Create(enabledFrame,
            1920, 1080, effectiveSamples: 4);

        Assert.True(enabled.Enabled);
        Assert.True(enabled.UsesResolve);
        Assert.Equal(480u, enabled.BlurWidth);
        Assert.Equal(270u, enabled.BlurHeight);
        PipelineKey matching = PipelineKey.From(emitting.Material,
            emitting.Primitive, emitting.Pass, sampleCount: 4);
        PipelineKey wrong = PipelineKey.From(emitting.Material,
            emitting.Primitive, emitting.Pass, sampleCount: 2);
        Assert.True(enabled.MatchesPipeline(matching));
        Assert.False(enabled.MatchesPipeline(wrong));
        Assert.Equal(0, enabled.StepIndex(SdlGpuBloomStep.Cel));
        Assert.Equal(1, enabled.StepIndex(SdlGpuBloomStep.BloomComposite));
        Assert.Equal(2, enabled.StepIndex(SdlGpuBloomStep.Disruption));
        Assert.Equal(3, enabled.StepIndex(SdlGpuBloomStep.SceneComposite));
        Assert.Equal(4, enabled.StepIndex(SdlGpuBloomStep.Overlays));

        RenderFrame disabledFrame = FrameWithQuality(enhanced with { Bloom = false });
        DrawSubmission disabledEmission = disabledFrame.Acquire();
        disabledEmission.Emission = OpenTK.Mathematics.Vector3.One;
        disabledFrame.Add(disabledEmission);
        SdlGpuBloomPlan disabled = SdlGpuBloomPlan.Create(disabledFrame,
            1920, 1080, effectiveSamples: 4);
        Assert.False(disabled.Enabled);
        Assert.False(disabled.UsesResolve);
        Assert.Equal(0u, disabled.BlurWidth);
        Assert.Equal(-1, disabled.StepIndex(SdlGpuBloomStep.BloomComposite));
        Assert.Equal(4, disabled.StepIndex(SdlGpuBloomStep.Overlays));
    }

    private static RenderFrame FrameWithQuality(RenderQualitySnapshot quality)
    {
        var frame = new RenderFrame(capacity: 2, maximumCapacity: 2);
        frame.CaptureState(OpenTK.Mathematics.Matrix4.Identity,
            OpenTK.Mathematics.Matrix4.Identity, OpenTK.Mathematics.Matrix4.Identity,
            OpenTK.Mathematics.Matrix4.Identity,
            new OpenTK.Mathematics.Vector3(3, 4, 5),
            new OpenTK.Mathematics.Vector2i(1920, 1080),
            new OpenTK.Mathematics.Vector2i(1920, 1080),
            OpenTK.Mathematics.Vector4.UnitW,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.Zero,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.Zero,
            hasFog: false, OpenTK.Mathematics.Vector4.Zero, 0, 0,
            default(RenderFrameOptions) with { Quality = quality });
        Assert.Equal(new OpenTK.Mathematics.Vector3(3, 4, 5), frame.CameraWorldPosition);
        return frame;
    }

    private static RenderVisualLight Light(float x, int priority)
        => new(new OpenTK.Mathematics.Vector3(x, 0, 0),
            OpenTK.Mathematics.Vector3.One, 1, .5f, priority);
}
