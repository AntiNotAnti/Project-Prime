using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class SdlGpuSceneAbiTests
{
    [Fact]
    public void SceneDrawConstantsHaveSeparateStageAndPaletteLayouts()
    {
        Type owner = typeof(SdlGpuSceneResources);
        Type legacy = GetNested(owner, "LegacyDrawConstants");
        Type vertex = GetNested(owner, "VertexDrawConstants");
        Type fragment = GetNested(owner, "FragmentMaterialConstants");
        Type palette = GetNested(owner, "MatrixPaletteConstants");

        int matrixBytes = Marshal.SizeOf<Matrix4>();
        Assert.Equal(3 * matrixBytes + 10 * 16, Marshal.SizeOf(vertex));
        Assert.Equal(matrixBytes + 23 * 16, Marshal.SizeOf(fragment));
        Assert.Equal(RenderFrame.MatrixStackFloats * sizeof(float), Marshal.SizeOf(palette));
        Assert.True(Marshal.SizeOf(vertex) < Marshal.SizeOf(legacy));
        Assert.True(Marshal.SizeOf(fragment) < Marshal.SizeOf(legacy));

        Assert.Equal(0, Marshal.OffsetOf(vertex, "Transform").ToInt32());
        Assert.Equal(2 * matrixBytes, Marshal.OffsetOf(vertex, "TextureMatrix").ToInt32());
        Assert.Equal(0, Marshal.OffsetOf(fragment, "TextureMatrix").ToInt32());
        Assert.Equal(0, Marshal.OffsetOf(palette, "MatrixStack").ToInt32());
        Assert.Null(vertex.GetField("MatrixStack",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(fragment.GetField("MatrixStack",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void SceneShaderDeclaresStageSpecificConstantBuffers()
    {
        string source = ReadRepositoryFile("src/Renderer/Shaders/scene.hlsl");

        Assert.Contains("cbuffer VertexDrawConstants : register(b1, space1)", source,
            StringComparison.Ordinal);
        Assert.Contains("cbuffer MatrixPaletteConstants : register(b2, space1)", source,
            StringComparison.Ordinal);
        Assert.Contains("cbuffer FragmentMaterialConstants : register(b1, space3)", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("cbuffer DrawConstants", source, StringComparison.Ordinal);
        Assert.Contains("row_major float4x4 matrixStack[31]", source,
            StringComparison.Ordinal);
        Assert.Contains("drawOptions.w > 0.5f", source, StringComparison.Ordinal);
        Assert.Contains("matrixStack[matrixIndex]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SdlSceneBindsSplitConstantsForHudWorldAndBloomDraws()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/SdlGpu/SdlGpuSceneResources.cs");

        Assert.Contains("samplers: 0, uniforms: 3", source, StringComparison.Ordinal);
        Assert.Contains("SDL_GPU_SHADERSTAGE_FRAGMENT,\n                    samplers: checked((uint)_sceneSamplerBindingCount), uniforms: 2",
            source, StringComparison.Ordinal);
        Assert.True(Count(source, "PushVertex(commandBuffer, 1, constants.Vertex)") >= 3);
        Assert.True(Count(source, "PushVertex(commandBuffer, 2, constants.Palette)") >= 3);
        Assert.Contains("if (draw.MatrixStackCount > 0)\n                PushVertex(commandBuffer, 2, constants.Palette)",
            source, StringComparison.Ordinal);
        Assert.Contains("PushVertex(commandBuffer, 2, IdentityPalette())", source,
            StringComparison.Ordinal);
        Assert.True(Count(source, "PushFragment(commandBuffer, 1, constants.Fragment)") >= 3);
        Assert.Contains("SplitDrawConstants(\n                        BuildHudDrawConstants",
            source, StringComparison.Ordinal);
        Assert.Contains("SplitDrawConstants(BuildLegacyDrawConstants",
            source, StringComparison.Ordinal);
    }

    private static Type GetNested(Type owner, string name)
        => owner.GetNestedType(name, BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing nested ABI type {name}.");

    private static int Count(string value, string token)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static string ReadRepositoryFile(string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        string testsDirectory = Path.GetDirectoryName(sourcePath)!;
        string repository = Path.GetFullPath(Path.Combine(testsDirectory, "../../.."));
        return File.ReadAllText(Path.Combine(repository, relativePath));
    }
}
