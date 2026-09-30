#if !MPHREAD_SERVER
using System;
using System.Diagnostics;
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct PerformanceSample(int PipelinesCreated, double PipelineCreationMs,
        double LongestPipelineCreationMs, long TextureUploadBytes, double TextureUploadSubmissionMs,
        long TrackedTextureStorageBytes, long PooledBufferStorageBytes);

    private bool _measurePerformance;
    private int _createdPipelines;
    private double _pipelineCreationMs, _longestPipelineCreationMs, _textureUploadMs;
    private long _textureUploadBytes;

    internal static void BeginPerformanceSample()
    {
        var s = Current;
        s._measurePerformance = true;
        s._createdPipelines = 0;
        s._pipelineCreationMs = s._longestPipelineCreationMs = s._textureUploadMs = 0;
        s._textureUploadBytes = 0;
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
        return new(s._createdPipelines, s._pipelineCreationMs, s._longestPipelineCreationMs,
            s._textureUploadBytes, s._textureUploadMs, textureBytes, bufferBytes);
    }
}
#endif
