#if !MPHREAD_SERVER
using System;
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

    private sealed class UploadBuffer
    {
        internal nint Buffer;
        internal ulong Capacity;
        internal byte[] Staging = Array.Empty<byte>();
    }

    private readonly Dictionary<ulong, UniformPool> _uniformPools = new();
    private readonly List<UploadBuffer> _uploadBuffers = new();
    private int _uploadBufferCursor;

    // A complete frame is normally well below this count. Keeping the encoder
    // alive longer matters on Metal/Vulkan/DX12 because each QueueSubmit carries
    // native driver scheduling overhead. Real ordering hazards still flush
    // explicitly at readback/present/recovery boundaries.
    private const int CommandBatchOperations = 1024;
    private const ulong MaximumStagedTextureUploadBytes = 4UL * 1024 * 1024;

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

    private UploadBuffer RentUploadBuffer(ulong size)
    {
        if (_uploadBufferCursor == _uploadBuffers.Count)
            _uploadBuffers.Add(new UploadBuffer());
        UploadBuffer upload = _uploadBuffers[_uploadBufferCursor++];
        if (upload.Buffer == 0 || upload.Capacity < size)
        {
            if (upload.Buffer != 0)
                _api.BufferRelease((WgpuBuffer*)upload.Buffer);
            ulong capacity = Math.Max(4096UL, upload.Capacity);
            while (capacity < size)
                capacity = checked(capacity * 2);
            WgpuBuffer* buffer = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = capacity,
                Usage = BufferUsage.CopySrc | BufferUsage.CopyDst
            });
            if (buffer == null)
                throw new InvalidOperationException($"Could not allocate {capacity} byte WebGPU upload buffer.");
            upload.Buffer = (nint)buffer;
            upload.Capacity = capacity;
        }
        if ((ulong)upload.Staging.LongLength < size)
            upload.Staging = new byte[checked((int)size)];
        return upload;
    }

    /// <summary>
    /// Preserve texture-write ordering without forcing all earlier draws through
    /// QueueSubmit. QueueWriteBuffer targets an independent staging resource,
    /// then CopyBufferToTexture is recorded after the draws that must see the old
    /// image. This is the common dynamic UI/video path. Very large uploads retain
    /// the old direct queue path to avoid keeping giant staging allocations alive.
    /// </summary>
    private bool TryStageTextureUpload(NativeTexture native, byte[] data,
        int x, int y, int width, int height)
    {
        if (_commandEncoder == null || data.Length == 0)
            return false;

        uint pixelBytes = native.Format == TextureFormat.Rgba16float ? 8u : 4u;
        uint rowBytes = checked((uint)width * pixelBytes);
        uint paddedRow = (rowBytes + 255u) & ~255u;
        ulong uploadBytes = checked((ulong)paddedRow * (uint)height);
        if (uploadBytes > MaximumStagedTextureUploadBytes)
            return false;

        UploadBuffer upload = RentUploadBuffer(uploadBytes);
        if (paddedRow == rowBytes)
        {
            data.AsSpan(0, checked((int)(rowBytes * (uint)height)))
                .CopyTo(upload.Staging);
        }
        else
        {
            Span<byte> staging = upload.Staging.AsSpan(0, checked((int)uploadBytes));
            staging.Clear();
            ReadOnlySpan<byte> source = data;
            for (int row = 0; row < height; row++)
            {
                source.Slice(checked(row * (int)rowBytes), checked((int)rowBytes))
                    .CopyTo(staging.Slice(checked(row * (int)paddedRow), checked((int)rowBytes)));
            }
        }

        fixed (byte* ptr = upload.Staging)
            WriteProfiledBuffer((WgpuBuffer*)upload.Buffer, 0, ptr, checked((nuint)uploadBytes));

        var sourceCopy = new ImageCopyBuffer
        {
            Buffer = (WgpuBuffer*)upload.Buffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = paddedRow,
                RowsPerImage = (uint)height
            }
        };
        var destination = new ImageCopyTexture
        {
            Texture = native.Texture,
            Origin = new Origin3D((uint)x, (uint)y, 0),
            Aspect = TextureAspect.All,
            MipLevel = 0
        };
        var extent = new Extent3D((uint)width, (uint)height, 1);
        _api.CommandEncoderCopyBufferToTexture(BeginCommands(), &sourceCopy, &destination, &extent);
        EndCommands();
        return true;
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
            long start = PerformanceStart();
            _api.QueueSubmit(_queue, 1, &commands);
            if (start != 0)
            {
                _queueSubmissions++;
                _queueSubmitMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
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
        foreach (var program in _generatedPrograms.Values) program.BindGroupCursor = 0;
        _transientGeometryCursor = 0;
        _uploadBufferCursor = 0;
        _frameBindGroupCursor = 0;
    }

    private void DisposeUniformBuffers()
    {
        foreach (var pool in _uniformPools.Values)
            foreach (nint buffer in pool.Buffers) _api.BufferRelease((WgpuBuffer*)buffer);
        _uniformPools.Clear();
        foreach (UploadBuffer upload in _uploadBuffers)
            if (upload.Buffer != 0) _api.BufferRelease((WgpuBuffer*)upload.Buffer);
        _uploadBuffers.Clear();
    }
}
#endif
