#if !MPHREAD_SERVER
using System;
using System.Diagnostics;
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct PerformanceSample(int PipelinesCreated, double PipelineCreationMs,
        double LongestPipelineCreationMs, long TextureUploadBytes, double TextureUploadSubmissionMs,
        long TrackedTextureStorageBytes, long PooledBufferStorageBytes,
        int SurfaceAcquisitions, double SurfaceAcquireMs, double LongestSurfaceAcquireMs,
        bool RequestedVSync, string PresentMode,
        int QueueSubmissions, double QueueSubmitMs, int BufferWrites, long BufferWriteBytes, double BufferWriteMs,
        int BindGroupsCreated, double BindGroupCreationMs,
        int CoreDraws, int CoreRenderPasses, int StagedTextureUploads,
        int RetainedGeometryPromotions, long RetainedGeometryBytes,
        TrackedStorage Storage, string StorageCoverage, BufferUploadBreakdown BufferUploads,
        long AtlasCumulativeUploadedBytes, long AtlasLiveBytes, long AtlasReservedBytes, int AtlasPages,
        int PresentCalls, double PresentCallMs);

    private bool _measurePerformance;
    private int _createdPipelines;
    private double _pipelineCreationMs, _longestPipelineCreationMs, _textureUploadMs;
    private long _textureUploadBytes;
    private int _surfaceAcquisitions, _queueSubmissions, _bufferWrites, _bindGroupsCreated;
    private int _coreDraws, _coreRenderPasses, _stagedTextureUploads;
    private int _retainedGeometryPromotions;
    private long _retainedGeometryBytes;
    private long _bufferWriteBytes;
    private double _queueSubmitMs, _bufferWriteMs, _bindGroupCreationMs;
    private double _surfaceAcquireMs, _longestSurfaceAcquireMs;
    private int _presentCalls;
    private double _presentCallMs;

    internal static void BeginPerformanceSample()
    {
        var s = Current;
        s._measurePerformance = true;
        s._createdPipelines = 0;
        s._pipelineCreationMs = s._longestPipelineCreationMs = s._textureUploadMs = 0;
        s._textureUploadBytes = 0;
        s._queueSubmissions = s._bufferWrites = s._bindGroupsCreated = 0;
        s._coreDraws = s._coreRenderPasses = s._stagedTextureUploads = 0;
        s._retainedGeometryPromotions = 0;
        s._retainedGeometryBytes = 0;
        s._bufferWriteBytes = 0; s._queueSubmitMs = s._bufferWriteMs = s._bindGroupCreationMs = 0;
        s._surfaceAcquisitions = 0; s._surfaceAcquireMs = s._longestSurfaceAcquireMs = 0;
        Array.Clear(s._bufferUploadCategoryBytes);
        s._presentCalls = 0; s._presentCallMs = 0;
    }

    private long PerformanceStart() => _measurePerformance ? Stopwatch.GetTimestamp() : 0;

    private void RecordPresentTime(long start)
    {
        if (start == 0) return;
        _presentCalls++;
        _presentCallMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void RecordPipelineCreation(long start)
    {
        if (start == 0) return;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _createdPipelines++;
        _pipelineCreationMs += ms;
        _longestPipelineCreationMs = Math.Max(_longestPipelineCreationMs, ms);
    }

    private void WriteProfiledBuffer(Silk.NET.WebGPU.Buffer* buffer, ulong offset, void* data, nuint size)
    {
        long start = PerformanceStart();
        _api.QueueWriteBuffer(_queue, buffer, offset, data, size);
        if (start == 0) return;
        _bufferWrites++; _bufferWriteBytes += (long)size;
        _bufferWriteMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        RecordBufferUpload((nint)buffer, checked((long)size));
    }

    internal static PerformanceSample EndPerformanceSample(bool includeResourceStorage = true)
    {
        var s = Current;
        s._measurePerformance = false;
        TrackedStorage storage = default;
        if (includeResourceStorage)
        {
            storage = s.Storage();
            // Timing slots own buffers outside Storage()'s retained pools.
            long timingBufferBytes = 0;
            foreach (var slot in s._gpuTimingSlots)
            {
                if (slot.Resolve != null)
                    timingBufferBytes = checked(timingBufferBytes + (long)s._api.BufferGetSize(slot.Resolve));
                if (slot.Readback != null)
                    timingBufferBytes = checked(timingBufferBytes + (long)s._api.BufferGetSize(slot.Readback));
            }
            storage = storage with { BufferCapacityBytes = checked(storage.BufferCapacityBytes + timingBufferBytes) };
        }
        return new(s._createdPipelines, s._pipelineCreationMs, s._longestPipelineCreationMs,
            s._textureUploadBytes, s._textureUploadMs, storage.TextureBytes, storage.BufferCapacityBytes,
            s._surfaceAcquisitions, s._surfaceAcquireMs, s._longestSurfaceAcquireMs, s._vsync, s._presentMode.ToString(),
            s._queueSubmissions, s._queueSubmitMs, s._bufferWrites, s._bufferWriteBytes, s._bufferWriteMs,
            s._bindGroupsCreated, s._bindGroupCreationMs,
            s._coreDraws, s._coreRenderPasses, s._stagedTextureUploads,
            s._retainedGeometryPromotions, s._retainedGeometryBytes, storage,
            includeResourceStorage
                ? "logical native texture capacity (depth24plus estimated 4B/pixel), native buffer capacity including GPU timing buffers; excludes driver padding/private memory and managed staging"
                : "not sampled (includeResourceStorage=false); zero storage fields do not indicate zero allocation",
            s.BufferUploads(),
            s._retainedMultiDrawAtlasBytes, s.RetainedAtlasLiveBytes, s.RetainedAtlasReservedBytes,
            s._retainedMultiDrawPages.Count, s._presentCalls, s._presentCallMs);
    }

    // Nominal tracked allocation sizes, not driver residency/VRAM. Depth24Plus
    // storage is implementation-defined; use a four-byte depth/stencil estimate.
    // Only the currently acquired swapchain image is tracked, not the entire
    // backend-owned swapchain or driver allocations.
    internal static long EstimateTextureStorageBytes(Silk.NET.WebGPU.TextureFormat format,
        int width, int height, int mipCount)
    {
        bool blockCompressed = format is
            Silk.NET.WebGPU.TextureFormat.BC7RgbaUnorm
            or Silk.NET.WebGPU.TextureFormat.BC7RgbaUnormSrgb
            or Silk.NET.WebGPU.TextureFormat.Etc2Rgba8Unorm
            or Silk.NET.WebGPU.TextureFormat.Etc2Rgba8UnormSrgb
            or Silk.NET.WebGPU.TextureFormat.Astc4x4Unorm
            or Silk.NET.WebGPU.TextureFormat.Astc4x4UnormSrgb;
        int unitBytes = blockCompressed ? 16 : format == Silk.NET.WebGPU.TextureFormat.Rgba16float ? 8 : 4;
        return TextureStorageMath.Bytes(width, height, mipCount, unitBytes, blockCompressed);
    }
}
#endif
