using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedEnergyGpuIntegrationTests
{
    [Theory]
    [InlineData(GraphicsPreset.Original)]
    [InlineData(GraphicsPreset.Performance)]
    public void LegacyPresetsIgnoreForceFieldBloomAndDistortion(
        GraphicsPreset preset)
    {
        RenderFrame frame = Frame(preset);
        DrawSubmission draw = ForceFieldDraw(frame, stableKey: 7);

        frame.Add(draw);
        frame.Seal();

        Assert.False(draw.Material.BloomEligible);
        Assert.Equal(0, draw.Material.BloomStrength);
        Assert.False(frame.DistortionSubmissions.HasSources);
    }

    [Fact]
    public void EnhancedFrameFreezesForceFieldBloomAndExactDrawSource()
    {
        RenderFrame frame = Frame(GraphicsPreset.Enhanced);
        DrawSubmission draw = ForceFieldDraw(frame, stableKey: 11);
        draw.CullingMode = CullingMode.Front;
        draw.MatrixStackCount = 1;
        draw.MatrixStack[0] = 3;

        frame.Add(draw);
        frame.Seal();

        Assert.True(draw.Material.BloomEligible);
        Assert.InRange(draw.Material.BloomStrength, 0, 1);
        EnhancedDistortionSubmission source
            = Assert.Single(frame.DistortionSubmissions.Items);
        Assert.Same(draw, source.SourceDraw);
        Assert.Same(draw.GeometryIdentity, source.GeometryIdentity);
        Assert.Equal(CullingMode.Front, source.CullingMode);
        Assert.Equal(draw.Transform, source.Transform);
    }

    [Fact]
    public void SampledPresentationMetadataIsDeterministicAndKeyed()
    {
        Assert.True(WeaponBeamVisualProfiles.TryGet(BeamType.ShockCoil,
            out BeamVisualProfile beam));
        TimeSpan time = TimeSpan.FromSeconds(2.25);
        var first = new EnhancedBeamDrawState(beam, time, 19);
        var second = new EnhancedBeamDrawState(beam, time, 19);
        Assert.Equal(first, second);

        ulong key = EnhancedPresentationSourceKey.ForSubresource(41, 2, 3);
        Assert.Equal(key,
            EnhancedPresentationSourceKey.ForSubresource(41, 2, 3));
        Assert.NotEqual(key,
            EnhancedPresentationSourceKey.ForSubresource(41, 2, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedPresentationSourceKey.ForSubresource(41, -1, 0));
    }

    [Fact]
    public void BeamAndForceFieldShadersExposeSpecializedEnhancedBranches()
    {
        string scene = ReadShader("scene.hlsl");
        string vectors = ReadShader("distortion.hlsl");
        string warp = ReadShader("distortion_warp.hlsl");

        Assert.Contains("enhancedOptions.y > 0.5f", scene,
            StringComparison.Ordinal);
        Assert.Contains("beamCore.rgb", scene, StringComparison.Ordinal);
        Assert.Contains("enhancedOptions.z > 0.5f", scene,
            StringComparison.Ordinal);
        Assert.Contains("forceFieldEmission", scene,
            StringComparison.Ordinal);
        Assert.Contains("surfaceDepth - input.viewDepth", scene,
            StringComparison.Ordinal);
        Assert.Contains("distortionOptions.x", vectors,
            StringComparison.Ordinal);
        Assert.Contains("warpedUv", warp, StringComparison.Ordinal);
    }

    [Fact]
    public void DistortionFollowsSixWorldPassesAndWarpsBloomSource()
    {
        var graph = new RenderExecutionPlan();
        RenderGraphLite.Build(graph, new RenderGraphFeatures(
            DirectionalShadow: true, SurfaceData: true,
            AmbientOcclusion: true, Sky: true, Distortion: true,
            Bloom: true, OriginalHud: false, EnhancedOutput: true,
            SceneCapture: false, Visor: false, EnhancedHud: false));
        RenderGraphPassKind[] order = graph.Passes.ToArray()
            .Select(pass => pass.Kind).ToArray();
        int front = Array.IndexOf(order, RenderGraphPassKind.TransparentFront);
        int vectors = Array.IndexOf(order,
            RenderGraphPassKind.DistortionVectors);
        int bloom = Array.IndexOf(order, RenderGraphPassKind.BloomEmission);
        Assert.True(front >= 0 && vectors > front && bloom > vectors);

        string post = ReadRepositoryFile("src", "Renderer",
            "Backends", "SdlGpu", "SdlGpuPostResources.cs");
        int celPass = post.IndexOf("EncodeCel(commandBuffer",
            StringComparison.Ordinal);
        int sceneWarp = post.IndexOf("PostShader.DistortionWarp", celPass,
            StringComparison.Ordinal);
        int warpScene = post.IndexOf(
            "PostShader.DistortionWarp", StringComparison.Ordinal);
        int warpBloom = post.IndexOf("effectiveBloom = _distortedBloom",
            warpScene, StringComparison.Ordinal);
        int buildBloom = post.IndexOf("EncodeBloomPyramid(commandBuffer",
            warpBloom, StringComparison.Ordinal);
        Assert.True(celPass >= 0 && sceneWarp > celPass);
        Assert.True(warpScene >= 0 && warpBloom > warpScene
            && buildBloom > warpBloom);

        string shader = ReadShader("distortion.hlsl");
        Assert.Contains("distortionDrawOptions.x > 0.5f", shader,
            StringComparison.Ordinal);
        Assert.DoesNotContain("frameOptions.x > 0.5f", shader,
            StringComparison.Ordinal);
        Assert.False(SdlGpuDistortionTransformPolicy.UsesMatrixStack(0));
        Assert.True(SdlGpuDistortionTransformPolicy.UsesMatrixStack(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuDistortionTransformPolicy.UsesMatrixStack(32));
    }

    [Fact]
    public void AllEvidenceBackedTrailBuildersAttachBeamMetadata()
    {
        string presentation = ReadRepositoryFile("src", "Client",
            "Rendering", "Entities", "BeamProjectileEntityPresentation.cs");
        Assert.Equal(4, presentation.Split(
            "enhancedBeam: GetEnhancedBeamDrawState()",
            StringSplitOptions.None).Length - 1);
    }

    private static DrawSubmission ForceFieldDraw(RenderFrame frame,
        ulong stableKey)
    {
        DrawSubmission draw = frame.Acquire();
        draw.Primitive = RenderPrimitive.Mesh;
        draw.Alpha = 1;
        draw.GeometryIdentity = new object();
        draw.Transform = Matrix4.CreateTranslation(1, 2, 3);
        draw.EnhancedForceField = new EnhancedForceFieldDrawState(stableKey,
            EnhancedForceFieldProfiles.Default, TimeSpan.FromSeconds(1));
        return draw;
    }

    private static RenderFrame Frame(GraphicsPreset preset)
    {
        var frame = new RenderFrame(1, 4);
        var quality = new RenderQualitySnapshot(preset,
            TextureFilteringPreset.Original, AnisotropyLevel.Off,
            MsaaLevel.Off, Bloom: true, DynamicVisualLights: true);
        frame.CaptureState(Matrix4.Identity, Matrix4.Identity,
            Matrix4.Identity, Matrix4.Identity, Vector3.Zero,
            new Vector2i(640, 480), new Vector2i(640, 480), Vector4.One,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            false, Vector4.Zero, 0, 0,
            default(RenderFrameOptions) with { Quality = quality });
        return frame;
    }

    private static string ReadShader(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", name));

    private static string ReadRepositoryFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }
            .Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
