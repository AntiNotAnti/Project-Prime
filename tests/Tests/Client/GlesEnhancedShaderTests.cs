using System;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead.Mods.Render;
using Xunit;

public sealed class GlesEnhancedShaderTests
{
    [Fact]
    public void VertexContractMatchesStableTangentPackingAndResolvesDifAmbPerVertex()
    {
        string source = GlesEnhancedShaders.VertexShader;

        Assert.Contains("layout(location = 5) in vec4 a_tangent", source);
        Assert.Contains("uniform mat4 mtx_stack[32]", source);
        Assert.Contains("out vec3 lighting_diffuse", source);
        Assert.Contains("out vec3 lighting_ambient", source);
        Assert.Contains("if (a_color_set > 0.5 && a_color.a == 0.0)", source);
        Assert.Contains("lighting_ambient = vec3(0.0)", source);
        Assert.Contains("transform_normal(a_normal, model_mtx)", source);
        Assert.Equal(5, GlesEnhancedShaderContract.TangentAttribute);
        Assert.Equal(32, GlesEnhancedShaderContract.MatrixStackCount);
    }

    [Fact]
    public void FragmentContractProvidesLinearPerPixelLightingAndThreeMaterialTextures()
    {
        string source = GlesEnhancedShaders.FragmentShader;

        Assert.Contains("uniform sampler2D albedo_tex", source);
        Assert.Contains("uniform sampler2D normal_tex", source);
        Assert.Contains("uniform sampler2D emissive_tex", source);
        Assert.Contains("visual_light_position_radius[8]", source);
        Assert.Contains("visual_light_color_intensity[8]", source);
        Assert.Contains("evaluate_room_lighting", source);
        Assert.Contains("attenuation *= attenuation", source);
        Assert.Contains("resolve_normal_map", source);
        Assert.Contains("srgb_to_linear(texture(emissive_tex", source);
        Assert.DoesNotContain("vertex_color.a == 0.0", source);

        int lighting = source.IndexOf("surface_color = max(surface_color + visual_lighting", StringComparison.Ordinal);
        int emission = source.IndexOf("result.rgb += material_emission", StringComparison.Ordinal);
        Assert.True(lighting >= 0 && emission > lighting,
            "Emission must be added after all lit-surface processing.");
        Assert.DoesNotContain("surface_color = clamp", source);
        Assert.DoesNotContain("result.rgb = clamp", source);

        Assert.Equal(0, GlesEnhancedShaderContract.AlbedoTextureUnit);
        Assert.Equal(1, GlesEnhancedShaderContract.NormalTextureUnit);
        Assert.Equal(2, GlesEnhancedShaderContract.EmissiveTextureUnit);
        Assert.Equal(8, GlesEnhancedShaderContract.MaximumPointLights);
        Assert.Contains(GlesEnhancedShaderContract.Uniform.EnhancedEmission, source);
        Assert.Contains(GlesEnhancedShaderContract.Uniform.VisualLightPositionRadius, source);
    }

    [Fact]
    public void ToneMapContractGradesDisplayLinearThenPerformsOneExplicitTransfer()
    {
        string source = GlesEnhancedShaders.ToneMapFragmentShader;

        int toneMap = source.IndexOf("tone_map_aces(source.rgb)", StringComparison.Ordinal);
        int grade = source.IndexOf("sample_color_grade_lut(display_linear)", StringComparison.Ordinal);
        int transfer = source.IndexOf("linear_to_srgb(display_linear)", StringComparison.Ordinal);
        Assert.True(toneMap >= 0 && grade > toneMap && transfer > grade);
        Assert.Equal(1, Count(source, "linear_to_srgb(display_linear)"));
        Assert.Contains("uniform bool apply_output_transfer", source);
        Assert.Contains("uniform bool apply_tone_map", source);
        Assert.Contains("? tone_map_aces(source.rgb) : clamp(source.rgb", source);
        Assert.Contains("const float dimension = 16.0", source);
        Assert.Contains("const float texture_width = 256.0", source);
        Assert.Equal(256, GlesEnhancedShaderContract.ColorGradeTextureWidth);
        Assert.Equal(16, GlesEnhancedShaderContract.ColorGradeTextureHeight);
        Assert.Contains(GlesEnhancedShaderContract.Uniform.ColorGradeLut, source);
        Assert.Contains(GlesEnhancedShaderContract.Uniform.ApplyOutputTransfer, source);
    }

    [Fact]
    public void CompositionDecodesAuthoredHudAndFadeBeforeLinearBlending()
    {
        string source = GlesEnhancedShaders.CompositionFragmentShader;

        Assert.Contains("srgb_to_linear(sampled.rgb)", source);
        Assert.Contains("srgb_to_linear(hud_color.rgb)", source);
        Assert.Contains("srgb_to_linear(fade_color.rgb)", source);
        Assert.DoesNotContain("linear_to_srgb", source);
        Assert.Equal(1, Count(GlesEnhancedShaders.ToneMapFragmentShader,
            "linear_to_srgb(display_linear)"));
    }

    [Fact]
    public void SoftParticlesRemainDisabledUntilDepthFeedbackIsRemoved()
    {
        Assert.False(GlesEnhancedShaderContract.SupportsSoftParticles);
        Assert.DoesNotContain("surface_depth", GlesEnhancedShaders.FragmentShader);
    }

    [Fact]
    public void RuntimeValidatesLinksAndDeletesPartialProgramsTransactionally()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/Gles/GlesEnhancedRuntime.cs");

        Assert.Contains("GetProgramParameterName.LinkStatus", source);
        Assert.Contains("GL.GetProgramInfoLog(program)", source);
        Assert.Contains("GL.DeleteProgram(scene)", source);
        Assert.Contains("GL.DeleteProgram(tone)", source);
        Assert.Contains("GL.DeleteProgram(composition)", source);
    }

    [Fact]
    public void RuntimeSetsCompleteSamplersAndLutClampLinearState()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/Gles/GlesEnhancedRuntime.cs");

        Assert.Contains("TextureParameterName.TextureMinFilter", source);
        Assert.Contains("TextureParameterName.TextureMagFilter", source);
        Assert.Contains("TextureParameterName.TextureWrapS", source);
        Assert.Contains("TextureParameterName.TextureWrapT", source);
        Assert.Contains("ConfigureCompleteSampler(linear: true, RepeatMode.Clamp, RepeatMode.Clamp)",
            source);
    }

    [Fact]
    public void RuntimeEstablishesFullscreenAndWorldRasterStateExplicitly()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/Gles/GlesEnhancedRuntime.cs");

        Assert.Contains("GL.Enable(EnableCap.DepthTest)", source);
        Assert.Contains("GL.DepthMask(true)", source);
        Assert.Contains("GL.Disable(EnableCap.CullFace)", source);
        Assert.Contains("private static void EnterFullscreen()", source);
        Assert.Contains("if (faceCulling) GL.Enable(EnableCap.CullFace)", source);
    }

    [Fact]
    public void RuntimeCacheIsBoundedAndDeletesOnlyForCurrentContext()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/Gles/GlesEnhancedRuntime.cs");

        Assert.Equal(2048, GlesEnhancedShaderContract.MaximumCachedTextures);
        Assert.Contains("_textures.Count < GlesEnhancedShaderContract.MaximumCachedTextures",
            source);
        Assert.Contains("if (_generation == GlEs.ContextGeneration)", source);
        Assert.Contains("GL.DeleteTexture(cached.Binding)", source);
        Assert.Contains("_textures.Clear()", source);
        Assert.Contains("EnhancedRuntime?.BeginResourceFrame()",
            ReadRepositoryFile("src/Renderer/Backends/Gles/GlesBackend.cs"));
        Assert.Contains("if (_pinnedTextures.Contains(identity)) continue", source);
        Assert.Contains("if (!TryEvictOldestTextureIfFull())", source);
        Assert.Contains("binding = fallback", source);
        Assert.Contains("TryResolve(lutIdentity, frame, _whiteTexture, out lutBinding)",
            source);
        Assert.Contains("gradeRequested, lutResolved", source);
    }

    [Fact]
    public void FullscreenShaderUsesUniversalPackedTexcoordAttribute()
    {
        string source = GlesEnhancedShaders.FullscreenVertexShader;
        string declaration = $"layout(location = {GlesEnhancedShaderContract.TexcoordAttribute}) "
            + "in vec3 a_texcoord";

        Assert.Contains(declaration, source);
        Assert.Contains("texcoord = a_texcoord.xy", source);
        Assert.DoesNotContain("layout(location = 1) in vec2 a_texcoord", source);
    }

    [Theory]
    [InlineData(0f, 4f, 1f)]
    [InlineData(1f, 4f, 0.5625f)]
    [InlineData(2f, 4f, 0.25f)]
    [InlineData(4f, 4f, 0f)]
    [InlineData(8f, 4f, 0f)]
    public void PointLightReferenceUsesQuadraticRadiusFalloff(
        float distance, float radius, float expected)
    {
        Assert.Equal(expected,
            GlesEnhancedShaderContract.PointLightAttenuation(distance, radius), 6);
    }

    [Fact]
    public void LutReferenceUsesPixelCentersAndAdjacentBlueSlices()
    {
        GlesEnhancedLutAddress black = GlesEnhancedShaderContract
            .AddressColorGradeLut(0, 0, 0);
        Assert.Equal(0.5f / 256, black.LowerU);
        Assert.Equal(0.5f / 16, black.LowerV);
        Assert.Equal(0, black.SliceBlend);

        GlesEnhancedLutAddress middle = GlesEnhancedShaderContract
            .AddressColorGradeLut(1, 1, 0.5f);
        Assert.Equal(0.5f, middle.SliceBlend, 6);
        Assert.True(middle.UpperU > middle.LowerU);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GlesEnhancedShaderContract.AddressColorGradeLut(0, 0, float.NaN));
    }

    [Fact]
    public void EnhancedProgramsExposeStandaloneSourcesWithoutPresetBranching()
    {
        Assert.StartsWith("#version 300 es", GlesEnhancedShaders.VertexShader);
        Assert.StartsWith("#version 300 es", GlesEnhancedShaders.FragmentShader);
        Assert.NotEqual(GlesEnhancedShaders.VertexShader,
            GlesEnhancedShaders.FullscreenVertexShader);
        Assert.DoesNotContain("GraphicsPreset", GlesEnhancedShaders.VertexShader);
        Assert.DoesNotContain("GraphicsPreset", GlesEnhancedShaders.FragmentShader);
    }

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
