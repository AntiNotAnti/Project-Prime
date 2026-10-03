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
        int BindGroupsCreated, double BindGroupCreationMs);

    private bool _measurePerformance;
    private int _createdPipelines;
    private double _pipelineCreationMs, _longestPipelineCreationMs, _textureUploadMs;
    private long _textureUploadBytes;
    private int _surfaceAcquisitions, _queueSubmissions, _bufferWrites, _bindGroupsCreated;
    private long _bufferWriteBytes;
    private double _queueSubmitMs, _bufferWriteMs, _bindGroupCreationMs;
    private double _surfaceAcquireMs, _longestSurfaceAcquireMs;

    internal static void BeginPerformanceSample()
    {
        var s = Current;
        s._measurePerformance = true;
        s._createdPipelines = 0;
        s._pipelineCreationMs = s._longestPipelineCreationMs = s._textureUploadMs = 0;
        s._textureUploadBytes = 0;
        s._queueSubmissions = s._bufferWrites = s._bindGroupsCreated = 0;
        s._bufferWriteBytes = 0; s._queueSubmitMs = s._bufferWriteMs = s._bindGroupCreationMs = 0;
        s._surfaceAcquisitions = 0; s._surfaceAcquireMs = s._longestSurfaceAcquireMs = 0;
    }

    private long PerformanceStart() => _measurePerformance ? Stopwatch.GetTimestamp() : 0;

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
    }

    internal static PerformanceSample EndPerformanceSample()
    {
        var s = Current;
        s._measurePerformance = false;
        long textureBytes = 0, bufferBytes = 0;
        foreach (var texture in s._nativeTextures.Values)
        {
            int width = texture.Width, height = texture.Height;
            int pixelBytes = texture.Format == Silk.NET.WebGPU.TextureFormat.Rgba16float ? 8 : 4;
            for (int mip = 0; mip < texture.MipCount; mip++)
            {
                textureBytes += (long)width * height * pixelBytes;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
        }
        foreach (var geometry in s._geometryCache.Values)
            bufferBytes += (long)(geometry.VertexCapacity + geometry.IndexCapacity);
        foreach (var geometry in s._transientGeometry)
            bufferBytes += (long)(geometry.VertexCapacity + geometry.IndexCapacity);
        foreach (var (size, pool) in s._uniformPools)
            bufferBytes += (long)size * pool.Buffers.Count;
        foreach (var upload in s._uploadBuffers)
            bufferBytes += (long)upload.Capacity;
        return new(s._createdPipelines, s._pipelineCreationMs, s._longestPipelineCreationMs,
            s._textureUploadBytes, s._textureUploadMs, textureBytes, bufferBytes,
            s._surfaceAcquisitions, s._surfaceAcquireMs, s._longestSurfaceAcquireMs, s._vsync, s._presentMode.ToString(),
            s._queueSubmissions, s._queueSubmitMs, s._bufferWrites, s._bufferWriteBytes, s._bufferWriteMs,
            s._bindGroupsCreated, s._bindGroupCreationMs);
    }
}
#endif
