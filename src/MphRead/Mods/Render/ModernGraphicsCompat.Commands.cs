#if !MPHREAD_SERVER
using System.Collections.Generic;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private sealed class UniformPool
    {
        internal readonly List<nint> Buffers = new();
        internal int Cursor;
    }

    private readonly Dictionary<ulong, UniformPool> _uniformPools = new();
    private const int CommandBatchOperations = 256;

    private CommandEncoder* _commandEncoder;
    private int _commandOperations;

    internal static void SubmitPending() => Current.FlushCommands();

    private WgpuBuffer* RentUniformBuffer(ulong size)
    {
        if (!_uniformPools.TryGetValue(size, out var pool))
            _uniformPools.Add(size, pool = new());
        if (pool.Cursor == pool.Buffers.Count)
            pool.Buffers.Add((nint)_api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            { Size = size, Usage = BufferUsage.Uniform | BufferUsage.CopyDst }));
        return (WgpuBuffer*)pool.Buffers[pool.Cursor++];
    }

    // Each operation owns a complete pass. A shared encoder batches native
    // submissions while preserving attachment transitions between those passes.
    private CommandEncoder* BeginCommands()
    {
        if (_commandEncoder == null)
            _commandEncoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
        return _commandEncoder;
    }

    private void EndCommands()
    {
        // A frame can contain hundreds of tiny compatibility passes. Retaining
        // more of them in one encoder cuts QueueSubmit/finish churn while each
        // draw still owns its pass and its pooled buffers remain distinct until
        // the frame boundary.
        if (++_commandOperations >= CommandBatchOperations) FlushCommands();
    }

    private void FlushCommands()
    {
        if (_commandEncoder == null) return;
        CommandBuffer* commands = _api.CommandEncoderFinish(_commandEncoder, new CommandBufferDescriptor());
        try
        {
            _api.QueueSubmit(_queue, 1, &commands);
            RecordCommandSubmission();
        }
        finally
        {
            _api.CommandBufferRelease(commands);
            DiscardCommands();
        }
    }

    private void DiscardCommands()
    {
        if (_commandEncoder != null) _api.CommandEncoderRelease(_commandEncoder);
        _commandEncoder = null;
        _commandOperations = 0;
    }

    // Call only at a completed public frame/readback boundary, never from a
    // texture upload nested inside an in-progress draw. Queue ordering permits
    // reuse after submission without waiting for GPU completion.
    private void ResetFrameBuffers()
    {
        foreach (var pool in _uniformPools.Values) pool.Cursor = 0;
        _transientGeometryCursor = 0;
    }

    private void DisposeUniformBuffers()
    {
        foreach (var pool in _uniformPools.Values)
            foreach (nint buffer in pool.Buffers) _api.BufferRelease((WgpuBuffer*)buffer);
        _uniformPools.Clear();
    }
}
#endif
