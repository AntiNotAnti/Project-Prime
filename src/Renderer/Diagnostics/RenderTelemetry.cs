using System;
using System.Diagnostics;

namespace MphRead;

/// <summary>
/// A completed graphics-frame sample.  The counters intentionally describe
/// work that the backend actually encoded, rather than estimating GPU work
/// from the sealed frame.  A zero or null device metric means that SDL did not
/// expose that metric on the active backend.
/// </summary>
public readonly record struct RenderTelemetrySnapshot(
    long FrameNumber,
    bool Attempted,
    bool Encoded,
    bool Submitted,
    bool FenceCommitted,
    int RenderPassCount,
    int IndexedDrawCount,
    int PipelineBindCount,
    int SamplerBindCount,
    int TextureBindCount,
    long VertexUniformBytes,
    long FragmentUniformBytes,
    long UploadScheduledBytes,
    long UploadCommittedBytes,
    int DeviceCacheEntryCount,
    int SessionCacheEntryPeak,
    double? GpuFrameMilliseconds,
    string GpuFrameTimeStatus,
    long? GpuMemoryBytes,
    string GpuMemoryStatus)
{
    /// <summary>Non-indexed SDL draw calls encoded for this frame.</summary>
    public int PrimitiveDrawCount { get; init; }

    /// <summary>All SDL draw API calls, indexed and non-indexed.</summary>
    public int DrawCallCount => checked(IndexedDrawCount + PrimitiveDrawCount);

    /// <summary>
    /// Whether this frame collected the optional native-call timing fields.
    /// Existing counters remain available when this is false.
    /// </summary>
    public bool NativeTimingEnabled { get; init; }

    public long VertexUniformPushCalls { get; init; }
    public long VertexUniformRequestedBytes { get; init; }
    public long VertexUniformAlignedBytes { get; init; }
    public long VertexUniformNativeTicks { get; init; }
    public long FragmentUniformPushCalls { get; init; }
    public long FragmentUniformRequestedBytes { get; init; }
    public long FragmentUniformAlignedBytes { get; init; }
    public long FragmentUniformNativeTicks { get; init; }
    public long VertexBufferBindCalls { get; init; }
    public long VertexBufferBindNativeTicks { get; init; }
    public long IndexBufferBindCalls { get; init; }
    public long IndexBufferBindNativeTicks { get; init; }
    public long IndexedDrawNativeCalls { get; init; }
    public long IndexedDrawNativeTicks { get; init; }
    public long DescriptorBearingDraws { get; init; }
    public long PipelineBindNativeCalls { get; init; }
    public long PipelineBindNativeTicks { get; init; }
    public long SamplerBindNativeCalls { get; init; }
    public long SamplerBindNativeTicks { get; init; }

    public static RenderTelemetrySnapshot Empty(long frameNumber = 0)
        => new(frameNumber, false, false, false, false, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, null,
            "unavailable: SDL GPU timestamp queries are not exposed",
            null, "unavailable: SDL GPU memory budgeting is not exposed");
}

/// <summary>
/// Optional backend-facing telemetry channel.  It is deliberately separate
/// from <see cref="IRenderBackend"/> so small fake backends and Android GLES
/// remain valid without manufacturing SDL-only metrics.
/// </summary>
public interface IRenderBackendTelemetry
{
    RenderTelemetrySnapshot Telemetry { get; }
}

/// <summary>
/// Graphics-thread-owned bounded telemetry accumulator.  Begin/Mark/Complete
/// are not synchronized: ownership is explicit and avoids atomics, locks, and
/// per-frame allocations on the render thread.  Completed samples are retained
/// in a fixed ring so a baseline tool can drain them later.
/// </summary>
public sealed class RenderTelemetryAccumulator
{
    public const int DefaultCapacity = 240;

    private readonly RenderTelemetrySnapshot[] _completed;
    private int _next;
    private int _count;
    private long _nextFrameNumber;
    private int _sessionCacheEntryPeak;
    private FrameCounters _current;
    private bool _active;

    public int Capacity => _completed.Length;
    public int Count => _count;
    public bool IsFrameActive => _active;
    public long CurrentUploadScheduledBytes => _current.UploadScheduledBytes;
    public long DroppedSamples { get; private set; }
    public RenderTelemetrySnapshot Latest { get; private set; }
    public bool NativeTimingEnabled { get; set; }

    public RenderTelemetryAccumulator(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _completed = new RenderTelemetrySnapshot[capacity];
        NativeTimingEnabled = RenderTelemetryConfiguration.NativeTimingEnabled;
        Latest = RenderTelemetrySnapshot.Empty();
    }

    public void BeginFrame()
    {
        if (_active) throw new InvalidOperationException("A telemetry frame is already active.");
        _active = true;
        _current = new FrameCounters(++_nextFrameNumber);
        _current.NativeTimingEnabled = NativeTimingEnabled;
        _current.SessionCacheEntryPeak = _sessionCacheEntryPeak;
    }

    public void MarkAttempted()
    {
        RequireActive();
        _current.Attempted = true;
    }

    public void MarkEncoded()
    {
        RequireActive();
        _current.Encoded = true;
    }

    public void MarkSubmitted()
    {
        RequireActive();
        _current.Submitted = true;
    }

    public void MarkFenceCommitted()
    {
        RequireActive();
        _current.FenceCommitted = true;
    }

    public void RenderPass()
    {
        RequireActive();
        _current.RenderPassCount = checked(_current.RenderPassCount + 1);
    }

    public void IndexedDraw()
    {
        RequireActive();
        _current.IndexedDrawCount = checked(_current.IndexedDrawCount + 1);
    }

    /// <summary>Counts one SDL_DrawGPUPrimitives API call.</summary>
    public void PrimitiveDraw()
    {
        RequireActive();
        _current.PrimitiveDrawCount = checked(_current.PrimitiveDrawCount + 1);
    }

    /// <summary>Counts one pipeline bind API call, including redundant binds.</summary>
    public void PipelineBind()
    {
        RequireActive();
        _current.PipelineBindCount = checked(_current.PipelineBindCount + 1);
    }

    /// <summary>
    /// Counts one SDL sampler-binding API call. The argument records the
    /// number of texture/sampler descriptors carried by that call.
    /// </summary>
    public void SamplerBind(int descriptorCount = 1)
    {
        RequireActive();
        if (descriptorCount < 0) throw new ArgumentOutOfRangeException(nameof(descriptorCount));
        _current.SamplerBindCount = checked(_current.SamplerBindCount + 1);
        _current.TextureBindCount = checked(_current.TextureBindCount + descriptorCount);
    }

    /// <summary>
    /// Counts bytes submitted to a transfer/copy command.  Bytes are scheduled
    /// when encoded and become committed only after the SDL fence is committed.
    /// </summary>
    public void UploadScheduled(long bytes)
    {
        RequireActive();
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        _current.UploadScheduledBytes = checked(_current.UploadScheduledBytes + bytes);
    }

    public void UploadCommitted(long bytes)
    {
        RequireActive();
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        _current.UploadCommittedBytes = checked(_current.UploadCommittedBytes + bytes);
    }

    /// <summary>Commits all uploads encoded for the active frame at its fence.</summary>
    public void CommitScheduledUploads()
    {
        RequireActive();
        _current.UploadCommittedBytes = _current.UploadScheduledBytes;
    }

    public void VertexUniform(long bytes)
    {
        RequireActive();
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        _current.VertexUniformBytes = checked(_current.VertexUniformBytes + bytes);
    }

    /// <summary>
    /// Records a vertex-uniform push after the native call has returned. The
    /// raw timestamp delta is deliberately supplied by the caller so no
    /// telemetry work is performed inside the measured interval.
    /// </summary>
    public void VertexUniformNative(long requestedBytes, long nativeTicks)
    {
        VertexUniform(requestedBytes);
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.VertexUniformPushCalls = checked(_current.VertexUniformPushCalls + 1);
        _current.VertexUniformRequestedBytes = checked(
            _current.VertexUniformRequestedBytes + requestedBytes);
        _current.VertexUniformAlignedBytes = checked(
            _current.VertexUniformAlignedBytes + AlignUniformBytes(requestedBytes));
        _current.VertexUniformNativeTicks = checked(
            _current.VertexUniformNativeTicks + nativeTicks);
    }

    public void FragmentUniform(long bytes)
    {
        RequireActive();
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        _current.FragmentUniformBytes = checked(_current.FragmentUniformBytes + bytes);
    }

    /// <summary>Records a fragment-uniform push and its optional native delta.</summary>
    public void FragmentUniformNative(long requestedBytes, long nativeTicks)
    {
        FragmentUniform(requestedBytes);
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.FragmentUniformPushCalls = checked(_current.FragmentUniformPushCalls + 1);
        _current.FragmentUniformRequestedBytes = checked(
            _current.FragmentUniformRequestedBytes + requestedBytes);
        _current.FragmentUniformAlignedBytes = checked(
            _current.FragmentUniformAlignedBytes + AlignUniformBytes(requestedBytes));
        _current.FragmentUniformNativeTicks = checked(
            _current.FragmentUniformNativeTicks + nativeTicks);
    }

    public void VertexBufferBindNative(long nativeTicks)
    {
        RequireActive();
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.VertexBufferBindCalls = checked(_current.VertexBufferBindCalls + 1);
        _current.VertexBufferBindNativeTicks = checked(
            _current.VertexBufferBindNativeTicks + nativeTicks);
    }

    public void IndexBufferBindNative(long nativeTicks)
    {
        RequireActive();
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.IndexBufferBindCalls = checked(_current.IndexBufferBindCalls + 1);
        _current.IndexBufferBindNativeTicks = checked(
            _current.IndexBufferBindNativeTicks + nativeTicks);
    }

    public void IndexedDrawNative(long nativeTicks, bool descriptorBearing)
    {
        IndexedDraw();
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.IndexedDrawNativeCalls = checked(_current.IndexedDrawNativeCalls + 1);
        _current.IndexedDrawNativeTicks = checked(
            _current.IndexedDrawNativeTicks + nativeTicks);
        if (descriptorBearing)
            _current.DescriptorBearingDraws = checked(_current.DescriptorBearingDraws + 1);
    }

    public void PipelineBindNative(long nativeTicks)
    {
        PipelineBind();
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.PipelineBindNativeCalls = checked(_current.PipelineBindNativeCalls + 1);
        _current.PipelineBindNativeTicks = checked(
            _current.PipelineBindNativeTicks + nativeTicks);
    }

    public void SamplerBindNative(int descriptorCount, long nativeTicks)
    {
        SamplerBind(descriptorCount);
        if (!_current.NativeTimingEnabled) return;
        if (nativeTicks < 0) throw new ArgumentOutOfRangeException(nameof(nativeTicks));
        _current.SamplerBindNativeCalls = checked(_current.SamplerBindNativeCalls + 1);
        _current.SamplerBindNativeTicks = checked(
            _current.SamplerBindNativeTicks + nativeTicks);
    }

    public void DeviceCacheEntryCount(int count)
    {
        RequireActive();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _current.DeviceCacheEntryCount = Math.Max(_current.DeviceCacheEntryCount, count);
    }

    public void SessionCacheEntryPeak(int count)
    {
        RequireActive();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _sessionCacheEntryPeak = Math.Max(_sessionCacheEntryPeak, count);
        _current.SessionCacheEntryPeak = _sessionCacheEntryPeak;
    }

    public RenderTelemetrySnapshot CompleteFrame()
    {
        RequireActive();
        RenderTelemetrySnapshot snapshot = _current.ToSnapshot();
        Latest = snapshot;
        if (_count == _completed.Length) DroppedSamples++;
        else _count++;
        _completed[_next] = snapshot;
        _next = (_next + 1) % _completed.Length;
        _active = false;
        _current = default;
        return snapshot;
    }

    public bool TryDequeue(out RenderTelemetrySnapshot snapshot)
    {
        if (_count == 0)
        {
            snapshot = default;
            return false;
        }
        int oldest = (_next - _count + _completed.Length) % _completed.Length;
        snapshot = _completed[oldest];
        _count--;
        return true;
    }

    private void RequireActive()
    {
        if (!_active) throw new InvalidOperationException("No telemetry frame is active.");
    }

    private struct FrameCounters
    {
        public FrameCounters(long frameNumber) => FrameNumber = frameNumber;

        public long FrameNumber;
        public bool Attempted;
        public bool Encoded;
        public bool Submitted;
        public bool FenceCommitted;
        public int RenderPassCount;
        public int IndexedDrawCount;
        public int PrimitiveDrawCount;
        public int PipelineBindCount;
        public int SamplerBindCount;
        public int TextureBindCount;
        public long VertexUniformBytes;
        public long FragmentUniformBytes;
        public long UploadScheduledBytes;
        public long UploadCommittedBytes;
        public int DeviceCacheEntryCount;
        public int SessionCacheEntryPeak;
        public bool NativeTimingEnabled;
        public long VertexUniformPushCalls;
        public long VertexUniformRequestedBytes;
        public long VertexUniformAlignedBytes;
        public long VertexUniformNativeTicks;
        public long FragmentUniformPushCalls;
        public long FragmentUniformRequestedBytes;
        public long FragmentUniformAlignedBytes;
        public long FragmentUniformNativeTicks;
        public long VertexBufferBindCalls;
        public long VertexBufferBindNativeTicks;
        public long IndexBufferBindCalls;
        public long IndexBufferBindNativeTicks;
        public long IndexedDrawNativeCalls;
        public long IndexedDrawNativeTicks;
        public long DescriptorBearingDraws;
        public long PipelineBindNativeCalls;
        public long PipelineBindNativeTicks;
        public long SamplerBindNativeCalls;
        public long SamplerBindNativeTicks;

        public RenderTelemetrySnapshot ToSnapshot()
            => new RenderTelemetrySnapshot(FrameNumber, Attempted, Encoded,
                Submitted, FenceCommitted,
                RenderPassCount, IndexedDrawCount, PipelineBindCount,
                SamplerBindCount, TextureBindCount, VertexUniformBytes,
                FragmentUniformBytes, UploadScheduledBytes,
                UploadCommittedBytes, DeviceCacheEntryCount, SessionCacheEntryPeak,
                null, "unavailable: SDL GPU timestamp queries are not exposed",
                null, "unavailable: SDL GPU memory budgeting is not exposed")
            {
                PrimitiveDrawCount = this.PrimitiveDrawCount,
                NativeTimingEnabled = this.NativeTimingEnabled,
                VertexUniformPushCalls = this.VertexUniformPushCalls,
                VertexUniformRequestedBytes = this.VertexUniformRequestedBytes,
                VertexUniformAlignedBytes = this.VertexUniformAlignedBytes,
                VertexUniformNativeTicks = this.VertexUniformNativeTicks,
                FragmentUniformPushCalls = this.FragmentUniformPushCalls,
                FragmentUniformRequestedBytes = this.FragmentUniformRequestedBytes,
                FragmentUniformAlignedBytes = this.FragmentUniformAlignedBytes,
                FragmentUniformNativeTicks = this.FragmentUniformNativeTicks,
                VertexBufferBindCalls = this.VertexBufferBindCalls,
                VertexBufferBindNativeTicks = this.VertexBufferBindNativeTicks,
                IndexBufferBindCalls = this.IndexBufferBindCalls,
                IndexBufferBindNativeTicks = this.IndexBufferBindNativeTicks,
                IndexedDrawNativeCalls = this.IndexedDrawNativeCalls,
                IndexedDrawNativeTicks = this.IndexedDrawNativeTicks,
                DescriptorBearingDraws = this.DescriptorBearingDraws,
                PipelineBindNativeCalls = this.PipelineBindNativeCalls,
                PipelineBindNativeTicks = this.PipelineBindNativeTicks,
                SamplerBindNativeCalls = this.SamplerBindNativeCalls,
                SamplerBindNativeTicks = this.SamplerBindNativeTicks
            };
    }

    private static long AlignUniformBytes(long bytes)
        => checked((bytes + 255) & ~255L);
}

/// <summary>
/// Process-scoped opt-in used while the SDL backend constructs its
/// graphics-thread-owned telemetry accumulator. It keeps the existing backend
/// ownership graph intact while allowing renderbench to select native timing.
/// </summary>
public static class RenderTelemetryConfiguration
{
    public static bool NativeTimingEnabled { get; set; }
}

/// <summary>
/// Thread-local bridge used only while SDL encodes a frame.  SDL GPU is
/// graphics-thread-affine, so this does not turn telemetry into shared mutable
/// render state and lets low-level helpers count bytes without allocating.
/// </summary>
internal static class SdlGpuTelemetryContext
{
    [ThreadStatic]
    private static RenderTelemetryAccumulator? _current;

    public static RenderTelemetryAccumulator? Current => _current;

    public static void Set(RenderTelemetryAccumulator? accumulator)
        => _current = accumulator;

    public static void VertexUniform(long bytes) => _current?.VertexUniform(bytes);
    public static void VertexUniformNative(long bytes, long nativeTicks)
        => _current?.VertexUniformNative(bytes, nativeTicks);
    public static void FragmentUniform(long bytes) => _current?.FragmentUniform(bytes);
    public static void FragmentUniformNative(long bytes, long nativeTicks)
        => _current?.FragmentUniformNative(bytes, nativeTicks);
    public static void RenderPass() => _current?.RenderPass();
    public static void IndexedDraw() => _current?.IndexedDraw();
    public static void IndexedDrawNative(long nativeTicks, bool descriptorBearing)
        => _current?.IndexedDrawNative(nativeTicks, descriptorBearing);
    public static void PrimitiveDraw() => _current?.PrimitiveDraw();
    public static void PipelineBind() => _current?.PipelineBind();
    public static void PipelineBindNative(long nativeTicks)
        => _current?.PipelineBindNative(nativeTicks);
    public static void SamplerBind(int count) => _current?.SamplerBind(count);
    public static void SamplerBindNative(int count, long nativeTicks)
        => _current?.SamplerBindNative(count, nativeTicks);
    public static void VertexBufferBindNative(long nativeTicks)
        => _current?.VertexBufferBindNative(nativeTicks);
    public static void IndexBufferBindNative(long nativeTicks)
        => _current?.IndexBufferBindNative(nativeTicks);
    public static void UploadScheduled(long bytes) => _current?.UploadScheduled(bytes);
    public static void UploadCommitted(long bytes) => _current?.UploadCommitted(bytes);

    /// <summary>Returns zero when native timing is disabled or no frame is active.</summary>
    public static long BeginNativeCall()
    {
        RenderTelemetryAccumulator? current = _current;
        return current != null && current.IsFrameActive && current.NativeTimingEnabled
            ? Stopwatch.GetTimestamp() : 0;
    }

    /// <summary>Completes a native interval without touching the clock when disabled.</summary>
    public static long EndNativeCall(long start)
        => start == 0 ? 0 : Stopwatch.GetTimestamp() - start;
}
