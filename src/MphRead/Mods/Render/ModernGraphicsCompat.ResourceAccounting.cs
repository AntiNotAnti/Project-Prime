#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;
using WgpuTexture = Silk.NET.WebGPU.Texture;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;
internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct TrackedStorage(long TextureBytes, long BufferCapacityBytes,
        long AtlasCapacityBytes, long AtlasLiveBytes, long VisibilityBufferBytes, long HiZBytes);

    internal readonly record struct BufferUploadBreakdown(long TransientGeometryBytes, long UniformBytes,
        long RetainedWorldUniformBytes, long RetainedPbrUniformBytes, long IndirectBytes,
        long VisibilityBytes, long AtlasBytes, long TextureStagingBytes, long OtherBytes);
    private readonly long[] _bufferUploadCategoryBytes = new long[9];
    private BufferUploadBreakdown BufferUploads() => new(_bufferUploadCategoryBytes[0],
        _bufferUploadCategoryBytes[1], _bufferUploadCategoryBytes[2], _bufferUploadCategoryBytes[3],
        _bufferUploadCategoryBytes[4], _bufferUploadCategoryBytes[5], _bufferUploadCategoryBytes[6],
        _bufferUploadCategoryBytes[7], _bufferUploadCategoryBytes[8]);

    // Opt-in sampling only. Keep normal gameplay writes free of classification
    // scans and expose the actual upload owner before proposing incremental work.
    private void RecordBufferUpload(nint buffer, long bytes)
    {
        int category = 8;
        foreach (var page in _geometryArena)
            if (page.Vertex == buffer || page.Index == buffer) { category = 0; goto found; }
        foreach (var page in _uniformArena)
            if (page.Buffer == buffer) { category = 1; goto found; }
        foreach (var page in _retainedUniformArena)
            if (page.Buffer == buffer) { category = 2; goto found; }
        foreach (var page in _retainedPbrUniformArena)
            if (page.Buffer == buffer) { category = 3; goto found; }
        foreach (var page in _retainedIndirectArena)
            if (page.Buffer == buffer) { category = 4; goto found; }
        if (buffer == (nint)_gpuVisibilityCandidateBuffer || buffer == (nint)_gpuVisibilityIndirectBuffer
            || buffer == (nint)_gpuVisibilityDenseIndirectBuffer || buffer == (nint)_gpuVisibilityBucketCountBuffer
            || buffer == (nint)_gpuVisibilityCompactBuffer || buffer == (nint)_gpuVisibilityCountersBuffer
            || buffer == (nint)_gpuVisibilityUniformBuffer) { category = 5; goto found; }
        foreach (var page in _retainedMultiDrawPages)
            if ((nint)page.Vertex == buffer || (nint)page.Index == buffer) { category = 6; goto found; }
        foreach (var upload in _uploadBuffers)
            if (upload.Buffer == buffer) { category = 7; goto found; }
        found: _bufferUploadCategoryBytes[category] += bytes;
    }

    internal static TrackedStorage TrackedStorageCapacity => Current.Storage();

    private long TextureBytes(WgpuTexture* texture)
    {
        if (texture == null) return 0;
        WgpuTextureFormat format = _api.TextureGetFormat(texture);
        bool compressed = format is WgpuTextureFormat.BC7RgbaUnorm or WgpuTextureFormat.BC7RgbaUnormSrgb
            or WgpuTextureFormat.Etc2Rgba8Unorm or WgpuTextureFormat.Etc2Rgba8UnormSrgb
            or WgpuTextureFormat.Astc4x4Unorm or WgpuTextureFormat.Astc4x4UnormSrgb;
        int unitBytes = compressed ? 16 : format == WgpuTextureFormat.Rgba16float ? 8 : 4;
        return TextureStorageMath.Bytes(checked((int)_api.TextureGetWidth(texture)),
            checked((int)_api.TextureGetHeight(texture)), checked((int)_api.TextureGetMipLevelCount(texture)),
            unitBytes, compressed);
    }

    private TrackedStorage Storage()
    {
        // Handle identity prevents a progressive upload already promoted into
        // the live dictionary, or an aliased fallback view, being counted twice.
        var seen = new HashSet<nint>();
        long textures = 0;
        void AddTexture(WgpuTexture* texture)
        {
            if (texture != null && seen.Add((nint)texture)) textures += TextureBytes(texture);
        }
        foreach (var texture in _nativeTextures.Values) AddTexture(texture.Texture);
        foreach (var pending in _pendingTextureUploads.Values)
        { AddTexture(pending.Native.Texture); AddTexture(pending.MipScratch); }
        foreach (var depth in _nativeRenderbuffers.Values) AddTexture(depth.Texture);
        AddTexture(_whiteTexture); AddTexture(_surfaceTexture.Texture);
        AddTexture(_surfaceDepth != null ? _surfaceDepth.Texture : null); AddTexture(_fallbackDepth != null ? _fallbackDepth.Texture : null);
        AddTexture(_gpuHiZTexture);

        long buffers = 0, atlas = 0, atlasLive = 0, visibility = 0;
        foreach (var geometry in _geometryCache.Values)
        {
            if (geometry.Vertex != null) buffers += (long)geometry.VertexCapacity;
            if (geometry.Index != null) buffers += (long)geometry.IndexCapacity;
        }
        foreach (var page in _geometryArena)
        {
            if (page.Vertex != 0) buffers += (long)page.VertexCapacity;
            if (page.Index != 0) buffers += (long)page.IndexCapacity;
        }
        foreach (var page in _uniformArena) if (page.Buffer != 0) buffers += (long)page.Capacity;
        foreach (var page in _retainedUniformArena) if (page.Buffer != 0) buffers += (long)page.Capacity;
        foreach (var page in _retainedPbrUniformArena) if (page.Buffer != 0) buffers += (long)page.Capacity;
        foreach (var page in _retainedIndirectArena) if (page.Buffer != 0) buffers += (long)page.Capacity;
        foreach (var upload in _uploadBuffers) if (upload.Buffer != 0) buffers += (long)upload.Capacity;
        foreach (var page in _retainedMultiDrawPages)
        {
            if (page.Vertex != null) atlas += (long)page.VertexCapacityBytes;
            if (page.Index != null) atlas += (long)page.IndexCapacityBytes;
            atlasLive += (long)page.Vertices.LiveUnits * LegacyGeometryBatch.FloatsPerVertex * sizeof(float)
                + (long)page.Indices.LiveUnits * sizeof(int);
        }
        if (_gpuVisibilityCandidateBuffer != null) visibility += (long)_gpuVisibilityCandidateCapacity;
        if (_gpuVisibilityIndirectBuffer != null) visibility += (long)_gpuVisibilityIndirectCapacity;
        if (_gpuVisibilityDenseIndirectBuffer != null) visibility += (long)_gpuVisibilityDenseIndirectCapacity;
        if (_gpuVisibilityBucketCountBuffer != null) visibility += (long)_gpuVisibilityBucketCountCapacity;
        if (_gpuVisibilityCompactBuffer != null) visibility += (long)_gpuVisibilityCompactCapacity;
        if (_gpuVisibilityCountersBuffer != null) visibility += (long)_gpuVisibilityCountersCapacity;
        if (_gpuVisibilityUniformBuffer != null) visibility += (long)_gpuVisibilityUniformCapacity;
        return new(textures, buffers + atlas + visibility, atlas, atlasLive, visibility, TextureBytes(_gpuHiZTexture));
    }

    private (int Textures, int Views, int Buffers, int Pipelines, int Samplers) AdditionalResourceCounts()
    {
        int textures = _gpuHiZTexture != null ? 1 : 0;
        int views = (_gpuHiZFullView != 0 ? 1 : 0) + _gpuHiZMipViews.Count;
        foreach (var pending in _pendingTextureUploads.Values)
        {
            if (pending.Native.Texture != null) textures++;
            if (pending.Native.View != null) views++;
            if (pending.Native.SampleView != null && pending.Native.SampleView != pending.Native.View) views++;
            if (pending.MipScratch != null) textures++;
            if (pending.MipScratchView != null) views++;
        }
        int samplers = 0;
        foreach (var pending in _pendingTextureUploads.Values)
            if (pending.Native.Sampler != null) samplers++;
        int buffers = 0;
        foreach (var page in _retainedPbrUniformArena) if (page.Buffer != 0) buffers++;
        foreach (var page in _retainedIndirectArena) if (page.Buffer != 0) buffers++;
        foreach (var page in _retainedMultiDrawPages)
        { if (page.Vertex != null) buffers++; if (page.Index != null) buffers++; }
        if (_gpuVisibilityCandidateBuffer != null) buffers++;
        if (_gpuVisibilityIndirectBuffer != null) buffers++;
        if (_gpuVisibilityDenseIndirectBuffer != null) buffers++;
        if (_gpuVisibilityBucketCountBuffer != null) buffers++;
        if (_gpuVisibilityCompactBuffer != null) buffers++;
        if (_gpuVisibilityCountersBuffer != null) buffers++;
        if (_gpuVisibilityUniformBuffer != null) buffers++;
        int pipelines = (_gpuVisibilityPipeline != null ? 1 : 0)
            + (_gpuHiZDepthPipeline != null ? 1 : 0) + (_gpuHiZReducePipeline != null ? 1 : 0);
        return (textures, views, buffers, pipelines, samplers);
    }
}
#endif
