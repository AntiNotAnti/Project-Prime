using System;
using System.Diagnostics;
using System.Collections.Generic;
using MphRead.Mods.Cosmetics;
using OpenTK.Graphics.OpenGL;

namespace MphRead;

public partial class Scene
{
    private sealed record WorldShaderProgram(int Program, ShaderLocations Locations,
        CosmeticUniforms? Cosmetics, int Skin, int OutlineMask, int OutlineColor, int Sampler);
    private WorldShaderProgram? _fullWorldProgram, _nativeWorldProgram, _plainWorldProgram, _unlitWorldProgram;
    private static readonly bool _worldShaderVariantsEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("SHADER_VARIANTS");
    private static readonly bool _plainWorldShaderEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("PLAIN_SHADER");
    private static readonly bool _unlitWorldShaderEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("UNLIT_SHADER");

    private WorldShaderProgram SaveWorldProgram() => new(_shaderProgramId, _shaderLocations,
        _cosmeticUniforms, _texturedPlayerSkinUniform, _playerOutlineMaskUniform, _playerOutlineColorUniform, _worldSampler);

    private void UseWorldProgram(WorldShaderProgram program)
    {
        _shaderProgramId = program.Program; _shaderLocations = program.Locations;
        _cosmeticUniforms = program.Cosmetics; _texturedPlayerSkinUniform = program.Skin;
        _playerOutlineMaskUniform = program.OutlineMask; _playerOutlineColorUniform = program.OutlineColor;
        _worldSampler = program.Sampler;
        GL.UseProgram(_shaderProgramId);
    }

    private void PrewarmWorldShaderVariants()
    {
#if !MPHREAD_SERVER
        if (!_worldShaderVariantsEnabled) return;
        _fullWorldProgram = SaveWorldProgram();
        _nativeWorldProgram = PrewarmWorldShaderVariant(Shaders.NativeFragmentShader);
        if (_plainWorldShaderEnabled)
        {
            _plainWorldProgram = PrewarmWorldShaderVariant(Shaders.NativeNoCosmeticsFragmentShader);
            if (_unlitWorldShaderEnabled)
                _unlitWorldProgram = PrewarmWorldShaderVariant(Shaders.NativeNoCosmeticsFragmentShader, Shaders.SelectedUnlitVertexShader);
        }
#endif
    }

    private WorldShaderProgram? PrewarmWorldShaderVariant(string fragmentSource, string? vertexSource = null)
    {
#if !MPHREAD_SERVER
        int vertex = 0, fragment = 0, program = 0;
        long start = Stopwatch.GetTimestamp();
        try
        {
#if ANDROID && PROJECTPRIME_ANDROID_SHADER_BINARY_CACHE
            bool fromBinary = Mods.Render.AndroidShaderProgramBinaryCache.TryLoad(
                vertexSource ?? Shaders.SelectedVertexShader, fragmentSource, out program);
            if (!fromBinary)
            {
#endif
            vertex = GL.CreateShader(ShaderType.VertexShader);
            fragment = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(vertex, vertexSource ?? Shaders.SelectedVertexShader);
            GL.ShaderSource(fragment, fragmentSource);
            Mods.Render.OpenGlFailureInjection.ShaderObject("variant-vertex", vertex);
            Mods.Render.OpenGlFailureInjection.ShaderObject("variant-fragment", fragment);
            GL.CompileShader(vertex); GL.CompileShader(fragment);
            GL.GetShader(vertex, ShaderParameter.CompileStatus, out int vs);
            GL.GetShader(fragment, ShaderParameter.CompileStatus, out int fs);
            if (vs == 0 || fs == 0) return null; // Canonical program remains available.
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
            Mods.Render.OpenGlFailureInjection.ShaderObject("variant-program", program);
 #if ANDROID && PROJECTPRIME_ANDROID_SHADER_BINARY_CACHE
            Mods.Render.AndroidShaderProgramBinaryCache.EnableRetrieval(program);
#endif
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) return null;
            GL.DetachShader(program, vertex); GL.DetachShader(program, fragment);
#if ANDROID && PROJECTPRIME_ANDROID_SHADER_BINARY_CACHE
            Mods.Render.AndroidShaderProgramBinaryCache.Save(
                vertexSource ?? Shaders.SelectedVertexShader, fragmentSource, program);
            }
#endif
            _shaderProgramId = program;
            // Auxiliary HUD/post locations belong to shared programs; copy them
            // before replacing only the world locations in the new bundle.
            _shaderLocations = _fullWorldProgram!.Locations.Copy();
            InitWorldUniformLocations();
            float[] toon = new float[Metadata.ToonTable.Count * 3];
            for (int i = 0; i < Metadata.ToonTable.Count; i++)
            { toon[i * 3] = Metadata.ToonTable[i].X; toon[i * 3 + 1] = Metadata.ToonTable[i].Y; toon[i * 3 + 2] = Metadata.ToonTable[i].Z; }
            GL.Uniform3(_shaderLocations.ToonTable, Metadata.ToonTable.Count, toon);
            SetShaderFog();
            Mods.Render.OpenGlFailureInjection.ShaderObject("variant-initialized", program);
            WorldShaderProgram initialized = SaveWorldProgram();
            program = 0; // Scene now owns the successfully initialized variant.
            return initialized;
        }
        finally
        {
            UseWorldProgram(_fullWorldProgram!);
            if (program != 0) GL.DeleteProgram(program);
            if (vertex != 0) GL.DeleteShader(vertex);
            if (fragment != 0) GL.DeleteShader(fragment);
            string variant = vertexSource != null ? "plain-unlit" : ReferenceEquals(fragmentSource, Shaders.NativeNoCosmeticsFragmentShader) ? "plain" : "native";
            Console.WriteLine($"[world-shaders] variant={variant} elapsedMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}");
        }
#else
        return null;
#endif
    }

    private void SelectWorldShaderVariant(bool allowPlain = false)
    {
        if (_fullWorldProgram == null) return;
        WorldShaderProgram selected = !Mods.RenderOptions.AdvancedMaterials && !_editorMaterialPreview
            && _nativeWorldProgram != null ? _nativeWorldProgram : _fullWorldProgram;
        if (allowPlain && ReferenceEquals(selected, _nativeWorldProgram) && CanUsePlainWorldFrame())
        {
            selected = _plainWorldProgram!;
            Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.PlainShaderFrames);
            if (_unlitWorldProgram != null && CanUseUnlitWorldFrame())
            {
                selected = _unlitWorldProgram;
                Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.UnlitShaderFrames);
            }
        }
        if (selected.Program == _shaderProgramId) return;
        UseWorldProgram(selected);
        ResetCosmeticUniforms();
        // Refresh persistent values even when this picture skips TransformCamera.
        GL.UniformMatrix4(_shaderLocations.ViewMatrix, false, ref _viewMatrix);
        GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, false, ref _perspectiveMatrix);
        SetShaderFog();
        Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.WorldShaderVariantSwitches);
    }

    internal static bool PlainWorldShaderEligible(IReadOnlyList<Mods.Render.RetainedDrawPacket> packets)
    {
        for (int i = 0; i < packets.Count; i++)
        {
            CosmeticSurface surface = packets[i].Item.Cosmetics;
            if (!PlainCosmeticEligible(surface)) return false;
        }
        return true;
    }

    internal static bool PlainCosmeticEligible(CosmeticSurface surface)
        => surface.Skin == 0 && surface.Effect == 0 && surface.Dissolve <= 0;

    internal static bool PlainWorldItemsEligible(IReadOnlyList<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
            if (!PlainCosmeticEligible(items[i].Cosmetics)) return false;
        return true;
    }

    private bool CanUsePlainWorldFrame()
    {
        if (_plainWorldProgram == null || Mods.RenderOptions.AdvancedMaterials
            || Mods.RenderOptions.Shadows != Mods.ShadowQuality.Off
            || GameState.MenuPause || _editorMaterialPreview || _drawingPlayerOutlineMask
            || !PlainWorldShaderEligible(_retainedRenderWorld.Opaque)
            || !PlainWorldShaderEligible(_retainedRenderWorld.Decals)
            || !PlainWorldShaderEligible(_retainedRenderWorld.Translucent)
            || !PlainWorldItemsEligible(_previewItems)) return false;
        // Outlines replay this queue, including geometry culled from retained
        // world packets. Preview geometry has its own separately pooled list.
        foreach (RenderItem item in _usedRenderItems)
            if (!PlainCosmeticEligible(item.Cosmetics)) return false;
        return true;
    }

    internal static bool UnlitWorldShaderEligible(IReadOnlyList<Mods.Render.RetainedDrawPacket> packets)
    {
        for (int i = 0; i < packets.Count; i++)
            if (packets[i].Item.Lighting) return false;
        return true;
    }

    internal static bool UnlitWorldItemsEligible(IReadOnlyList<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].Lighting) return false;
        return true;
    }

    private bool CanUseUnlitWorldFrame()
    {
        // Plain eligibility already excludes editor preview and shadow replay.
        // HUD explicitly disables lighting; previews use the same global option.
        if (!LightingOn) return true;
        if (!UnlitWorldShaderEligible(_retainedRenderWorld.Opaque)
            || !UnlitWorldShaderEligible(_retainedRenderWorld.Decals)
            || !UnlitWorldShaderEligible(_retainedRenderWorld.Translucent)
            || !UnlitWorldItemsEligible(_previewItems)) return false;
        foreach (RenderItem item in _usedRenderItems)
            if (item.Lighting) return false;
        return true;
    }

    private void ReleaseWorldShaderVariants()
    {
        if (_fullWorldProgram != null && _fullWorldProgram.Program != _shaderProgramId)
            GL.DeleteProgram(_fullWorldProgram.Program);
        if (_nativeWorldProgram != null && _nativeWorldProgram.Program != _shaderProgramId)
            GL.DeleteProgram(_nativeWorldProgram.Program);
        if (_plainWorldProgram != null && _plainWorldProgram.Program != _shaderProgramId)
            GL.DeleteProgram(_plainWorldProgram.Program);
        if (_unlitWorldProgram != null && _unlitWorldProgram.Program != _shaderProgramId)
            GL.DeleteProgram(_unlitWorldProgram.Program);
        _fullWorldProgram = _nativeWorldProgram = _plainWorldProgram = _unlitWorldProgram = null;
    }
}
