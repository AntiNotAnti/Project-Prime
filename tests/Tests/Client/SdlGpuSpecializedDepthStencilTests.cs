using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

public sealed class SdlGpuSpecializedDepthStencilTests
{
    [Fact]
    public void SpecializedShaderPreservesTransformTexgenAndAlphaPredicates()
    {
        string shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "depth_stencil.hlsl"));
        Assert.Contains("matrixStack[matrixIndex]", shader,
            StringComparison.Ordinal);
        Assert.Contains("mul(stackMatrix, billboard)", shader,
            StringComparison.Ordinal);
        Assert.Contains("texgen == 2u", shader, StringComparison.Ordinal);
        Assert.Contains("texgen == 3u", shader, StringComparison.Ordinal);
        Assert.Contains("alpha != 1.0f", shader, StringComparison.Ordinal);
        Assert.Contains("alpha >= 1.0f", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateGgxBrdf", shader,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnhancedAlphaEffectsFallBackToTheFullCoverageShader()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/SdlGpu/SdlGpuSceneResources.cs");
        Assert.Contains("draw.Material.EnhancedBeam.HasValue", source,
            StringComparison.Ordinal);
        Assert.Contains("draw.SoftParticleProfile.HasValue", source,
            StringComparison.Ordinal);
        Assert.Contains("FullCoverageShader: fullCoverageShader", source,
            StringComparison.Ordinal);
        Assert.Contains("&& !sceneKey.FullCoverageShader", source,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        string testsDirectory = Path.GetDirectoryName(sourcePath)!;
        string repository = Path.GetFullPath(Path.Combine(testsDirectory,
            "../../.."));
        return File.ReadAllText(Path.Combine(repository, relativePath));
    }
}
