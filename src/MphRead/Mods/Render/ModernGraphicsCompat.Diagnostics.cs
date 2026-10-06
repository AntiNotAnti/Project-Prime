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
        int Pipelines, int Programs, int Lists, int Views, int Samplers, int Buffers, int ShaderModules, int BindGroups, int Surfaces,
        int QuerySets = 0);
    private sealed class FrameBindGroupCacheEntry
    {
        internal BindGroup* Group;
        internal nint[] Resources = Array.Empty<nint>();
        internal long TextureRevision;
    }

    // Native handle addresses can be reused after sampler/view release.
    // A revision prevents a cached bind group from retaining an older resource.
    private long _textureBindingRevision;
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
            && cached.TextureRevision == _textureBindingRevision
            && cached.Resources.AsSpan().SequenceEqual(resources))
        {
            return cached.Group;
        }

        if (cached != null && cached.Group != null)
            ReleaseTrackedBindGroup(cached.Group);

        BindGroup* group = CreateTrackedBindGroup(descriptor);
        if (cached == null)
        {
            cached = new FrameBindGroupCacheEntry();
            _frameBindGroups.Add(cached);
        }
        cached.Group = group;
        cached.Resources = resources.ToArray();
        cached.TextureRevision = _textureBindingRevision;
        return group;
    }

    private void DisposeFrameBindGroups()
    {
        foreach (FrameBindGroupCacheEntry cached in _frameBindGroups)
            if (cached.Group != null) ReleaseTrackedBindGroup(cached.Group);
        _frameBindGroups.Clear();
        _frameBindGroupCursor = 0;
    }

    // Run once after the public frame's final submission, before its cursors
    // reset. Chunk submissions must preserve every slot used earlier in the
    // same frame. wgpu retains submitted command resources until GPU completion.
    private void TrimUnusedFrameBindGroups()
    {
        FrameResourceCache.TrimUnused(_frameBindGroups, _frameBindGroupCursor,
            this, static (renderer, cached) => renderer.ReleaseTrackedBindGroup(cached.Group));
        FrameResourceCache.TrimUnused(_retainedWorldBindGroups, _retainedUniformSlotCursor,
            this, static (renderer, cached) => renderer.ReleaseTrackedBindGroup(cached.Group));
        FrameResourceCache.TrimUnused(_retainedPbrBindGroups, _retainedPbrUniformSlotCursor,
            this, static (renderer, cached) => renderer.ReleaseTrackedBindGroup(cached.Group));
        foreach (GeneratedProgram program in _generatedPrograms.Values)
            FrameResourceCache.TrimUnused(program.BindGroups, program.BindGroupCursor,
                this, static (renderer, cached) => renderer.ReleaseTrackedBindGroup(cached.Group));
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
            // Pending uploads, HiZ and retained/visibility pools are counted by
            // the shared accounting helper; count only the remaining owners here.
            var additional = s.AdditionalResourceCounts();
            textures += additional.Textures; views += additional.Views; samplers += additional.Samplers;
            int buffers = additional.Buffers;
            foreach (var geometry in s._geometryCache.Values)
            {
                if (geometry.Vertex != null) buffers++;
                if (geometry.Index != null) buffers++;
            }
            foreach (var page in s._uniformArena) if (page.Buffer != 0) buffers++;
            foreach (var page in s._retainedUniformArena) if (page.Buffer != 0) buffers++;
            foreach (var upload in s._uploadBuffers) if (upload.Buffer != 0) buffers++;
            foreach (var page in s._geometryArena)
            {
                if (page.Vertex != 0) buffers++;
                if (page.Index != 0) buffers++;
            }
            int querySets = 0;
            // Timestamp slots are owned separately from the shared pools.
            foreach (var slot in s._gpuTimingSlots)
            {
                if (slot.Resolve != null) buffers++;
                if (slot.Readback != null) buffers++;
                if (slot.Queries != null) querySets++;
            }
            int shaders = s._generatedPrograms.Count * 2 + (s._clearShader != null ? 1 : 0)
                + (s._worldShader != null ? 1 : 0) + (s._rttShader != null ? 1 : 0)
                + (s._shiftShader != null ? 1 : 0) + (s._celShader != null ? 1 : 0)
                + (s._playerOutlineShader != null ? 1 : 0) + (s._toneMapShader != null ? 1 : 0)
                + (s._uiShader != null ? 1 : 0);
            return new(textures, s._nativeRenderbuffers.Count, s._geometryCache.Count,
                s._pipelines.Count + s._corePipelines.Count + s._blitPipelines.Count + additional.Pipelines,
                s._generatedPrograms.Count, s._lists.Count, views, samplers, buffers, shaders, s._liveBindGroups,
                s._device.Surface != null ? 1 : 0, querySets);
        }
    }

    // Content-free native regression: exercise the allocations introduced by
    // retained indirect/multi-draw/visibility work, then return to the same state.
    // Kept separate from the old mip/resize lifetime loop, which never uses them.
    internal static void ValidateResourceAccountingForCheck()
    {
        var s = Current;
        if (s._retainedIndirectArena.Count != 0 || s._retainedPbrUniformArena.Count != 0
            || s._retainedMultiDrawPages.Count != 0 || s._gpuVisibilityPipeline != null
            || s._gpuHiZTexture != null)
            throw new InvalidOperationException("Resource accounting check requires fresh retained GPU pools.");
        var before = LiveResources;
        var storageBefore = EndPerformanceSample();
        BeginPerformanceSample();
        try
        {
            s.RentRetainedIndexedIndirect(3);
            s.RentRetainedPbrUniformSlot((ulong)GeneratedShaderLayouts.Get(ModernProgramKind.DeferredPbrMrt).Size);
            s.EnsureRetainedMultiDrawEntry(new GeometryList
            {
                Vertices = new float[LegacyGeometryBatch.FloatsPerVertex * 3],
                Triangles = new[] { 0, 1, 2 }
            });
            s.EnsureGpuVisibilityPipelines();
            s.GrowBuffer(ref s._gpuVisibilityCandidateBuffer, ref s._gpuVisibilityCandidateCapacity, 256, BufferUsage.Storage);
            s.GrowBuffer(ref s._gpuVisibilityIndirectBuffer, ref s._gpuVisibilityIndirectCapacity, 256, BufferUsage.Storage | BufferUsage.Indirect);
            s.GrowBuffer(ref s._gpuVisibilityDenseIndirectBuffer, ref s._gpuVisibilityDenseIndirectCapacity, 256, BufferUsage.Storage | BufferUsage.Indirect);
            s.GrowBuffer(ref s._gpuVisibilityBucketCountBuffer, ref s._gpuVisibilityBucketCountCapacity, 256, BufferUsage.Storage | BufferUsage.Indirect);
            s.GrowBuffer(ref s._gpuVisibilityCompactBuffer, ref s._gpuVisibilityCompactCapacity, 256, BufferUsage.Storage);
            s.GrowBuffer(ref s._gpuVisibilityCountersBuffer, ref s._gpuVisibilityCountersCapacity, 256, BufferUsage.Storage);
            s.GrowBuffer(ref s._gpuVisibilityUniformBuffer, ref s._gpuVisibilityUniformCapacity, 256, BufferUsage.Uniform);
            s.EnsureGpuHiZ(4, 4);
            s._device.ThrowIfFailed();
            var after = LiveResources;
            var storageAfter = EndPerformanceSample();
            long expectedBufferBytes = (long)(RetainedIndirectPageBytes + UniformArenaPageBytes
                + RetainedMultiDrawVertexPageBytes + RetainedMultiDrawIndexPageBytes) + 7 * 256;
            if (after.Buffers - before.Buffers != 11 || after.Pipelines - before.Pipelines != 3
                || after.Textures - before.Textures != 1 || after.Views - before.Views != 4
                || storageAfter.PipelinesCreated != 3
                || storageAfter.PooledBufferStorageBytes - storageBefore.PooledBufferStorageBytes != expectedBufferBytes
                || storageAfter.TrackedTextureStorageBytes - storageBefore.TrackedTextureStorageBytes != 84)
                throw new InvalidOperationException($"Retained GPU resource accounting failed: {before} -> {after}.");
            if (EstimateTextureStorageBytes(WgpuTextureFormat.BC7RgbaUnorm, 5, 3, 3) != 64
                || EstimateTextureStorageBytes(WgpuTextureFormat.Rgba16float, 4, 4, 3) != 168)
                throw new InvalidOperationException("Nominal texture-storage format/mip accounting failed.");
        }
        finally
        {
            EndPerformanceSample();
            s.DisposeGpuVisibility();
            s.DisposeRetainedMultiDraw();
            s.DisposeRetainedIndirectArena();
            foreach (var page in s._retainedPbrUniformArena)
                if (page.Buffer != 0) s._api.BufferRelease((Silk.NET.WebGPU.Buffer*)page.Buffer);
            s._retainedPbrUniformArena.Clear();
            s._retainedPbrUniformSlotCursor = s._retainedPbrUniformSlotHighWater = 0;
            s._retainedPbrUniformSlotSize = 0;
        }
        if (LiveResources != before)
            throw new InvalidOperationException($"Retained GPU resource-accounting check did not release its allocations: {before} -> {LiveResources}.");
        Console.WriteLine("[renderwindowcheck] retained GPU resource counts/storage and compute pipeline timing PASS");
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
                var mrtTarget = new CoreTarget(null, s._whiteView, format, s._whiteView, 1, 1,
                    WgpuTextureFormat.Depth24PlusStencil8,
                    null, s._whiteView, null, s._whiteView);
                s.CorePipeline(ModernProgramKind.DeferredPbrMrt,
                    PrimitiveTopology.TriangleList, mrtTarget);
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
