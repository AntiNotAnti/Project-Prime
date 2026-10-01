#if !MPHREAD_SERVER
using System;
using Silk.NET.WebGPU;
using OpenTK.Graphics.OpenGL;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;
internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct ResourceCounts(int Textures, int Renderbuffers, int Geometry,
        int Pipelines, int Programs, int Lists, int Views, int Samplers, int Buffers, int ShaderModules, int BindGroups, int Surfaces);
    private int _liveBindGroups;
    private BindGroup* CreateTrackedBindGroup(in BindGroupDescriptor descriptor)
    {
        BindGroup* group = _api.DeviceCreateBindGroup(_device.Device, descriptor);
        if (group != null)
        {
            _liveBindGroups++;
            RecordBindGroupCreation();
        }
        return group;
    }
    private void ReleaseTrackedBindGroup(BindGroup* group)
    {
        if (group == null) return;
        _api.BindGroupRelease(group);
        _liveBindGroups--;
    }

    internal static ResourceCounts LiveResources
    {
        get
        {
            var s = Current;
            int textures = s._nativeTextures.Count + (s._whiteTexture != null ? 1 : 0)
                + (s._surfaceTexture.Texture != null ? 1 : 0) + (s._surfaceDepth != null ? 1 : 0)
                + (s._fallbackDepth != null ? 1 : 0) + s._nativeRenderbuffers.Count;
            int views = (s._whiteView != null ? 1 : 0) + (s._surfaceView != null ? 1 : 0)
                + (s._surfaceDepth != null ? 1 : 0) + s._nativeRenderbuffers.Count;
            int samplers = s._whiteSampler != null ? 1 : 0;
            foreach (var texture in s._nativeTextures.Values)
            {
                if (texture.View != null) views++;
                if (texture.SampleView != null && texture.SampleView != texture.View) views++;
                if (texture.Sampler != null) samplers++;
            }
            if (s._fallbackDepth != null)
            {
                views += s._fallbackDepth.SampleView == s._fallbackDepth.View ? 1 : 2;
                if (s._fallbackDepth.Sampler != null) samplers++;
            }
            int buffers = s._geometryCache.Count * 2;
            foreach (var pool in s._uniformPools.Values) buffers += pool.Buffers.Count;
            foreach (var geometry in s._transientGeometry)
                buffers += (geometry.Vertex != null ? 1 : 0) + (geometry.Index != null ? 1 : 0);
            int shaders = s._generatedPrograms.Count * 2 + (s._clearShader != null ? 1 : 0)
                + (s._worldShader != null ? 1 : 0) + (s._rttShader != null ? 1 : 0)
                + (s._shiftShader != null ? 1 : 0) + (s._celShader != null ? 1 : 0)
                + (s._playerOutlineShader != null ? 1 : 0) + (s._toneMapShader != null ? 1 : 0)
                + (s._uiShader != null ? 1 : 0);
            return new(textures, s._nativeRenderbuffers.Count, s._geometryCache.Count,
                s._pipelines.Count + s._corePipelines.Count + s._blitPipelines.Count,
                s._generatedPrograms.Count, s._lists.Count, views, samplers, buffers, shaders, s._liveBindGroups,
                s._device.Surface != null ? 1 : 0);
        }
    }

    // Prewarm only common states, on the owning render thread. The target views
    // are not consumed during pipeline creation; only their format/presence matter.
    internal static void PrewarmCommonPipelines()
    {
        var s = Current;
        s.SaveAttributes(AttribMask.AllAttribBits);
        try
        {
            s._enabled.Clear();
            s._maskRed = s._maskGreen = s._maskBlue = s._maskAlpha = true;
            s._depthWrite = true;
            s._depthFunction = DepthFunction.Lequal;
            s._blendSource = BlendingFactor.SrcAlpha;
            s._blendDestination = BlendingFactor.OneMinusSrcAlpha;
            s._blendEquation = BlendEquationMode.FuncAdd;
            s.Pipeline(PrimitiveTopology.TriangleList);
            foreach (var format in new[] { WgpuTextureFormat.Rgba8Unorm, WgpuTextureFormat.Rgba16float })
            {
                var target = new CoreTarget(null, null, format, s._whiteView, 1, 1);
                s._enabled.Add(EnableCap.DepthTest);
                s._enabled.Add(EnableCap.CullFace);
                s.CorePipeline(ModernProgramKind.World, PrimitiveTopology.TriangleList, target);
                s.CorePipeline(ModernProgramKind.DeferredPbr, PrimitiveTopology.TriangleList, target);
                s._enabled.Add(EnableCap.Blend);
                s.CorePipeline(ModernProgramKind.World, PrimitiveTopology.TriangleList, target);
                s._enabled.Clear();
                target = new CoreTarget(null, null, format, null, 1, 1);
                foreach (var kind in new[] { ModernProgramKind.Rtt, ModernProgramKind.Cel,
                    ModernProgramKind.PlayerOutline, ModernProgramKind.ToneMap, ModernProgramKind.PostProcess })
                    s.CorePipeline(kind, PrimitiveTopology.TriangleList, target);
            }
            s._device.ThrowIfFailed();
        }
        finally { s.RestoreAttributes(); }
    }
}
#endif
