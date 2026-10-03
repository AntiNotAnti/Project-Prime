#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;
using OpenTK.Graphics.OpenGL;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;
internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct ResourceCounts(int Textures, int Renderbuffers, int Geometry,
        int Pipelines, int Programs, int Lists, int Views, int Samplers, int Buffers, int ShaderModules, int BindGroups, int Surfaces);
    private sealed class FrameBindGroupCacheEntry
    {
        internal BindGroup* Group;
        internal nint[] Resources = Array.Empty<nint>();
    }

    private int _liveBindGroups;
    private readonly List<FrameBindGroupCacheEntry> _frameBindGroups = new();
    private int _frameBindGroupCursor;

    private BindGroup* CreateTrackedBindGroup(in BindGroupDescriptor descriptor)
    {
        long start = PerformanceStart();
        BindGroup* group = _api.DeviceCreateBindGroup(_device.Device, descriptor);
        if (start != 0)
        {
            _bindGroupsCreated++;
            _bindGroupCreationMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        if (group != null) _liveBindGroups++;
        return group;
    }
    private void ReleaseTrackedBindGroup(BindGroup* group)
    {
        if (group == null) return;
        _api.BindGroupRelease(group);
        _liveBindGroups--;
    }

    /// <summary>
    /// Compatibility draws use a deterministic sequence of uniform buffers and
    /// material resources from frame to frame. Cache each sequence slot so the
    /// steady-state frame updates data rather than rebuilding native bind groups.
    /// A slot is replaced automatically when draw order or resources change.
    /// </summary>
    private BindGroup* FrameBindGroup(in BindGroupDescriptor descriptor, ReadOnlySpan<nint> resources)
    {
        int slot = _frameBindGroupCursor++;
        FrameBindGroupCacheEntry? cached = slot < _frameBindGroups.Count
            ? _frameBindGroups[slot] : null;
        if (cached != null && cached.Group != null
            && cached.Resources.AsSpan().SequenceEqual(resources))
        {
            return cached.Group;
        }

        if (cached?.Group != null)
            ReleaseTrackedBindGroup(cached.Group);

        BindGroup* group = CreateTrackedBindGroup(descriptor);
        if (cached == null)
        {
            cached = new FrameBindGroupCacheEntry();
            _frameBindGroups.Add(cached);
        }
        cached.Group = group;
        cached.Resources = resources.ToArray();
        return group;
    }

    private void DisposeFrameBindGroups()
    {
        foreach (FrameBindGroupCacheEntry cached in _frameBindGroups)
            if (cached.Group != null) ReleaseTrackedBindGroup(cached.Group);
        _frameBindGroups.Clear();
        _frameBindGroupCursor = 0;
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
            int samplers = (s._whiteSampler != null ? 1 : 0)
                + (s._blitNearestSampler != null ? 1 : 0)
                + (s._blitLinearSampler != null ? 1 : 0);
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
            foreach (var page in s._uniformArena) if (page.Buffer != 0) buffers++;
            foreach (var upload in s._uploadBuffers) if (upload.Buffer != 0) buffers++;
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
