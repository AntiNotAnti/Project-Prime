using System;
using System.IO;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class SdlGpuEnhancedSurfaceIntegrationTests
{
    [Fact]
    public void SurfaceShaderPacksMappedWorldNormalLinearDepthAndSmoothness()
    {
        string source = ReadShader("surface.hlsl");

        Assert.Contains("Runtime pairs this fragment stage", source,
            StringComparison.Ordinal);
        Assert.Contains("clip(ResolveAlpha(input) == 1.0f", source,
            StringComparison.Ordinal);
        Assert.Contains("EncodeOctNormal(normal)", source,
            StringComparison.Ordinal);
        Assert.Contains("max(input.viewDepth, 0.0f)", source,
            StringComparison.Ordinal);
        Assert.Contains("saturate(specular.a)", source,
            StringComparison.Ordinal);
        Assert.Contains("ResolveNormalMap", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnhancedCelUsesRelativeDepthAndNormalWithLegacyPathIntact()
    {
        string source = ReadShader("cel.hlsl");

        Assert.Contains("SurfaceEdgeAt", source, StringComparison.Ordinal);
        Assert.Contains("abs(neighbor.z - center.z) / max(center.z", source,
            StringComparison.Ordinal);
        Assert.Contains("dot(centerNormal", source, StringComparison.Ordinal);
        Assert.Contains("KinkAbs", source, StringComparison.Ordinal);
        Assert.Contains("EdgeAt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SsaoHasEightRotatedSamplesAndTwoBilateralDirections()
    {
        string source = ReadShader("ssao.hlsl");

        Assert.Contains("float3 kernel[8]", source, StringComparison.Ordinal);
        Assert.Contains("CoordinateRotation", source, StringComparison.Ordinal);
        Assert.Contains("RawOcclusion", source, StringComparison.Ordinal);
        Assert.Contains("Bilateral", source, StringComparison.Ordinal);
        Assert.Contains("ReconstructViewPosition", source,
            StringComparison.Ordinal);
        Assert.Contains("inverseProjection", source,
            StringComparison.Ordinal);
        Assert.Contains("hemisphereSeparation", source,
            StringComparison.Ordinal);
        Assert.Equal(new[]
        {
            SdlGpuSsaoPassKind.Raw,
            SdlGpuSsaoPassKind.BilateralHorizontal,
            SdlGpuSsaoPassKind.BilateralVertical
        }, SdlGpuSsaoPassPlan.Sequence.ToArray());
    }

    [Fact]
    public void TranslucentForegroundCannotInheritOccludedBackgroundAo()
    {
        Vector3 ambient = new(0.4f, 0.3f, 0.2f);
        Vector3 direct = new(0.5f, 0.4f, 0.3f);
        float backgroundAo = 0.2f;

        Vector3 opaque = SdlGpuAmbientOcclusionPolicy.Compose(ambient,
            direct, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            SdlGpuAmbientOcclusionPolicy.UsesForPass(RenderPassKind.Opaque)
                ? backgroundAo : 1);
        Vector3 translucent = SdlGpuAmbientOcclusionPolicy.Compose(ambient,
            direct, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            SdlGpuAmbientOcclusionPolicy.UsesForPass(
                RenderPassKind.TransparentFront) ? backgroundAo : 1);

        Assert.Equal(ambient * backgroundAo + direct, opaque);
        Assert.Equal(ambient + direct, translucent);
        Assert.False(SdlGpuAmbientOcclusionPolicy.UsesForPass(
            RenderPassKind.Decal));
        Assert.False(SdlGpuAmbientOcclusionPolicy.UsesForPass(
            RenderPassKind.TransparentStencil));
        Assert.False(SdlGpuAmbientOcclusionPolicy.UsesForPass(
            RenderPassKind.TransparentBehind));
    }

    [Fact]
    public void AmbientOcclusionCannotDarkenNonAmbientTerms()
    {
        Vector3 ambient = new(0.4f, 0.3f, 0.2f);
        Vector3 directional = new(0.1f, 0.2f, 0.3f);
        Vector3 point = new(0.2f, 0.1f, 0.05f);
        Vector3 reflection = new(0.05f, 0.08f, 0.12f);
        Vector3 emission = new(0.7f, 0.4f, 0.2f);

        Vector3 occluded = SdlGpuAmbientOcclusionPolicy.Compose(ambient,
            directional, point, reflection, emission, 0.25f);
        Assert.Equal(ambient * 0.25f + directional + point + reflection
            + emission, occluded);

        string scene = ReadShader("scene.hlsl");
        Assert.Contains("ambientColor * ambientLight * ambientOcclusion",
            scene, StringComparison.Ordinal);
        Assert.Contains("result.rgb += EvaluateReflection", scene,
            StringComparison.Ordinal);
        Assert.Contains("result.rgb += materialEmission", scene,
            StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SdlGpuAmbientOcclusionPolicy.Compose(ambient, directional, point,
                reflection, emission, float.NaN));
    }

    [Fact]
    public void ShadowFogAndSoftParticlesPreserveThirdBundleContracts()
    {
        string scene = ReadShader("scene.hlsl");
        string shadow = ReadShader("shadow.hlsl");
        Assert.Contains("visibility / 9.0f", scene, StringComparison.Ordinal);
        Assert.Contains("ambientColor * ambientLight * ambientOcclusion", scene,
            StringComparison.Ordinal);
        Assert.Contains("float enabledDensity", scene, StringComparison.Ordinal);
        Assert.Contains("if (enhancedPerPixel)", scene, StringComparison.Ordinal);
        Assert.Contains("surfaceDepth - input.viewDepth", scene, StringComparison.Ordinal);
        Assert.True(scene.IndexOf("result.a *= depthFade", StringComparison.Ordinal)
            < scene.IndexOf("if (renderOptions.z > 0.0f)", StringComparison.Ordinal));
        Assert.Contains("SV_Depth", shadow, StringComparison.Ordinal);
        Assert.Contains("clip(alpha == 1.0f", shadow, StringComparison.Ordinal);
        Assert.Contains("if (texgen == 2u)", shadow, StringComparison.Ordinal);
        Assert.Contains("mul(lightView, stackMatrix)", shadow, StringComparison.Ordinal);
        Assert.Contains("else if (texgen == 3u)", shadow, StringComparison.Ordinal);
        Assert.Contains("dot(input.position", shadow, StringComparison.Ordinal);

        var plan = new RenderExecutionPlan();
        RenderGraphLite.Build(plan, new RenderGraphFeatures(
            DirectionalShadow: true, SurfaceData: true,
            AmbientOcclusion: true, Sky: true, Distortion: true,
            Bloom: true, OriginalHud: false, EnhancedOutput: true,
            SceneCapture: false, Visor: false, EnhancedHud: false));
        RenderGraphPassKind[] order = plan.Passes.ToArray()
            .Select(pass => pass.Kind).ToArray();
        int shadowPass = Array.IndexOf(order,
            RenderGraphPassKind.DirectionalShadow);
        int surfacePass = Array.IndexOf(order, RenderGraphPassKind.SurfaceData);
        int opaque = Array.IndexOf(order, RenderGraphPassKind.Opaque);
        int decal = Array.IndexOf(order, RenderGraphPassKind.Decal);
        int stencil = Array.IndexOf(order,
            RenderGraphPassKind.TransparentStencil);
        int rebuild = Array.IndexOf(order, RenderGraphPassKind.DepthRebuild);
        int behind = Array.IndexOf(order,
            RenderGraphPassKind.TransparentBehind);
        int front = Array.IndexOf(order, RenderGraphPassKind.TransparentFront);
        Assert.True(shadowPass >= 0 && surfacePass > shadowPass);
        Assert.True(opaque > surfacePass && decal > opaque && stencil > decal
            && rebuild > stencil && behind > rebuild && front > behind);
    }

    private static string ReadShader(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", name));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

}
