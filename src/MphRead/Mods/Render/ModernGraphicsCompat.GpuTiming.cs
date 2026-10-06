#if !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct GpuFrameSample(long FrameId, double Milliseconds);
    private sealed class GpuTimingSlot
    {
        internal QuerySet* Queries;
        internal WgpuBuffer* Resolve;
        internal WgpuBuffer* Readback;
        internal readonly GpuTimingReadbackLease Lease = new();
    }

    // Userdata is an opaque monotonically assigned token, never a pointer to a
    // GCHandle. Removing it during shutdown makes late callbacks harmless.
    private static readonly ConcurrentDictionary<nint, GpuTimingReadbackLease> GpuTimingCallbacks = new();
    private static long _gpuTimingCallbackSequence;
    private static readonly PfnBufferMapCallback GpuTimingMapCallback = new((status, userdata) =>
    {
        nint token = (nint)userdata;
        if (GpuTimingCallbacks.TryRemove(token, out var lease))
            lease.TryComplete(token, (int)status);
    });
    private readonly List<GpuTimingSlot> _gpuTimingSlots = new(8);
    private readonly GpuTimingSampleQueue<GpuFrameSample> _gpuTimingResults = new(64);
    private GpuTimingSlot? _recordingGpuTiming;
    private GpuTimingSlot? _submittedGpuTiming;
    private double _gpuNanosecondsPerTick;
    private long _gpuTimingDropped, _gpuTimingFailed;

    internal static bool GpuTimingAvailable => _current?._device.SupportsTimestampQueries == true;
    internal static long GpuTimingDroppedSamples => _current?._gpuTimingDropped ?? 0;
    internal static long GpuTimingFailedSamples => _current?._gpuTimingFailed ?? 0;

    internal static void BeginGpuFrameTiming(long frameId)
    {
        var s = Current;
        if (!GraphicsTimingPolicy.Enabled || !s._device.SupportsTimestampQueries || s._device.IsLost) return;
        s.PollGpuFrameTiming();
        if (s._recordingGpuTiming != null || s._submittedGpuTiming != null)
            throw new InvalidOperationException("GPU frame timing must end at a presentation boundary.");
        if (s._gpuTimingSlots.Count == 0) s.CreateGpuTimingSlots();
        foreach (var slot in s._gpuTimingSlots)
        {
            if (!slot.Lease.TryReserve(frameId)) continue;
            s._recordingGpuTiming = slot;
            try { s._api.CommandEncoderWriteTimestamp(s.BeginCommands(), slot.Queries, 0); }
            catch { s._recordingGpuTiming = null; slot.Lease.Cancel(); throw; }
            return;
        }
        // Profiling must never add a GPU wait to rescue a missing sample.
        s._gpuTimingDropped++;
    }

    internal static bool TryTakeGpuFrameSample(out GpuFrameSample sample)
    {
        if (_current == null) { sample = default; return false; }
        _current.PollGpuFrameTiming();
        return _current._gpuTimingResults.TryDequeue(out sample);
    }

    private void CreateGpuTimingSlots()
    {
        _gpuNanosecondsPerTick = ModernGraphicsNativeBridge.TimestampPeriod(_queue);
        if (!double.IsFinite(_gpuNanosecondsPerTick) || _gpuNanosecondsPerTick <= 0)
            throw new InvalidOperationException($"GPU timestamp period is invalid on {_device.Backend} / {_device.AdapterName}.");
        try
        {
            for (int i = 0; i < 8; i++)
            {
                var slot = new GpuTimingSlot();
                _gpuTimingSlots.Add(slot); // partial initialization is disposable
                var queries = new QuerySetDescriptor { Type = QueryType.Timestamp, Count = 2 };
                slot.Queries = _api.DeviceCreateQuerySet(_device.Device, &queries);
                var resolve = new BufferDescriptor { Size = 256, Usage = BufferUsage.QueryResolve | BufferUsage.CopySrc };
                slot.Resolve = _api.DeviceCreateBuffer(_device.Device, &resolve);
                var readback = new BufferDescriptor { Size = 16, Usage = BufferUsage.MapRead | BufferUsage.CopyDst };
                slot.Readback = _api.DeviceCreateBuffer(_device.Device, &readback);
                if (slot.Queries == null || slot.Resolve == null || slot.Readback == null)
                    throw new InvalidOperationException("Could not allocate the asynchronous GPU timestamp ring.");
            }
        }
        catch { DisposeGpuTiming(); throw; }
    }

    private void EndGpuFrameTimingBeforeSubmit()
    {
        var slot = _recordingGpuTiming;
        if (slot == null) return;
        _recordingGpuTiming = null;
        if (_device.IsLost) { slot.Lease.Cancel(); _gpuTimingFailed++; return; }
        try
        {
            var encoder = BeginCommands();
            _api.CommandEncoderWriteTimestamp(encoder, slot.Queries, 1);
            _api.CommandEncoderResolveQuerySet(encoder, slot.Queries, 0, 2, slot.Resolve, 0);
            _api.CommandEncoderCopyBufferToBuffer(encoder, slot.Resolve, 0, slot.Readback, 0, 16);
            slot.Lease.MarkSubmitted();
            _submittedGpuTiming = slot;
        }
        catch { slot.Lease.Cancel(); throw; }
    }

    private void QueueGpuFrameTimingReadback()
    {
        var slot = _submittedGpuTiming;
        if (slot == null) return;
        _submittedGpuTiming = null;
        if (_device.IsLost) { slot.Lease.Cancel(); _gpuTimingFailed++; return; }
        nint token = checked((nint)Interlocked.Increment(ref _gpuTimingCallbackSequence));
        slot.Lease.BeginMapping(token);
        GpuTimingCallbacks.TryAdd(token, slot.Lease);
        try { _api.BufferMapAsync(slot.Readback, MapMode.Read, 0, 16, GpuTimingMapCallback, (void*)token); }
        catch
        {
            GpuTimingCallbacks.TryRemove(token, out _);
            bool unmap = slot.Lease.CancelAndTakeUnmap((int)BufferMapAsyncStatus.Success,
                pendingMapAccepted: false, expectedToken: token);
            _gpuTimingFailed++;
            // Some native bindings can report failure after a synchronous map
            // callback. Unmap before a reusable slot can enter the next frame.
            try { if (unmap && !_device.IsLost) _api.BufferUnmap(slot.Readback); }
            catch (Exception cleanup) { Mods.DebugLog.Line("render", "GPU timing map cleanup failed: " + cleanup.Message); }
            throw;
        }
    }

    private void PollGpuFrameTiming()
    {
        if (_gpuTimingSlots.Count == 0 || _disposed || _device.IsLost) return;
        _device.Native.DevicePoll(_device.Device, false, null);
        if (_device.IsLost) return;
        foreach (var slot in _gpuTimingSlots)
        {
            if (!slot.Lease.TryGetCompletion(out long frameId, out int status)) continue;
            try
            {
                if ((BufferMapAsyncStatus)status == BufferMapAsyncStatus.Success)
                {
                    try
                    {
                        ulong* ticks = (ulong*)_api.BufferGetConstMappedRange(slot.Readback, 0, 16);
                        if (ticks != null && GpuTimingSamplePolicy.TryMilliseconds(ticks[0], ticks[1],
                            _gpuNanosecondsPerTick, out double ms))
                        {
                            if (_gpuTimingResults.Enqueue(new(frameId, ms))) _gpuTimingDropped++;
                        }
                        else _gpuTimingFailed++;
                    }
                    finally { if (!_device.IsLost) _api.BufferUnmap(slot.Readback); }
                }
                else _gpuTimingFailed++;
            }
            finally { slot.Lease.Finish(); }
        }
    }

    private void DisposeGpuTiming()
    {
        _recordingGpuTiming = _submittedGpuTiming = null;
        foreach (var slot in _gpuTimingSlots)
        {
            nint token = slot.Lease.MapToken;
            if (token != 0) GpuTimingCallbacks.TryRemove(token, out _);
            bool unmap = slot.Lease.CancelAndTakeUnmap((int)BufferMapAsyncStatus.Success);
            if (slot.Readback != null)
            {
                if (unmap && !_device.IsLost) _api.BufferUnmap(slot.Readback);
                _api.BufferRelease(slot.Readback);
            }
            if (slot.Resolve != null) _api.BufferRelease(slot.Resolve);
            if (slot.Queries != null) _api.QuerySetRelease(slot.Queries);
        }
        _gpuTimingSlots.Clear();
        _gpuTimingResults.Clear();
    }
}
#endif
