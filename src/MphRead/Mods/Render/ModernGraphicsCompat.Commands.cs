#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private sealed class UniformArenaPage
    {
        internal nint Buffer;
        internal ulong Capacity;
        internal byte[] Staging = Array.Empty<byte>();
        internal ulong Cursor;
        internal ulong DirtyStart = ulong.MaxValue;
        internal ulong DirtyEnd;
    }

    private readonly struct UniformAllocation
    {
        internal UniformAllocation(int page, nint buffer, ulong offset, ulong size)
        {
            Page = page;
            Buffer = buffer;
            Offset = offset;
            Size = size;
        }

        internal int Page { get; }
        internal nint Buffer { get; }
        internal ulong Offset { get; }
        internal ulong Size { get; }
    }

    private sealed class UploadBuffer
    {
        internal nint Buffer;
        internal ulong Capacity;
        internal byte[] Staging = Array.Empty<byte>();
    }

    private const ulong UniformAlignment = 256;
    private const ulong UniformArenaPageBytes = 4UL * 1024 * 1024;
    private readonly List<UniformArenaPage> _uniformArena = new();
    private int _uniformArenaPage;

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

    // End only the current compatibility render pass, keeping the command
    // encoder alive so callers can establish a hard state/viewmodel boundary
    // without paying for an additional QueueSubmit.
    internal static void BreakDrawPass() => Current.EndActiveCorePass();

    private static ulong AlignUniform(ulong value) =>
        checked((value + UniformAlignment - 1) & ~(UniformAlignment - 1));

    private UniformAllocation RentUniformBuffer(ulong size)
    {
        ulong reserved = AlignUniform(Math.Max(4UL, size));
        while (true)
        {
            if (_uniformArenaPage == _uniformArena.Count)
                _uniformArena.Add(new UniformArenaPage());

            UniformArenaPage page = _uniformArena[_uniformArenaPage];
            if (page.Buffer == 0)
            {
                page.Capacity = Math.Max(UniformArenaPageBytes, reserved);
                WgpuBuffer* buffer = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
                {
                    Size = page.Capacity,
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                });
                if (buffer == null)
                    throw new InvalidOperationException(
                        $"Could not allocate {page.Capacity} byte WebGPU uniform arena.");
                page.Buffer = (nint)buffer;
                page.Staging = new byte[checked((int)page.Capacity)];
            }

            ulong offset = AlignUniform(page.Cursor);
            if (offset + reserved <= page.Capacity)
            {
                page.Cursor = offset + reserved;
                return new UniformAllocation(_uniformArenaPage, page.Buffer, offset, size);
            }

            _uniformArenaPage++;
        }
    }

    private void WriteUniformBuffer(in UniformAllocation allocation, void* data, nuint size)
    {
        if ((ulong)size > allocation.Size)
            throw new ArgumentOutOfRangeException(nameof(size), "Uniform write exceeds its arena allocation.");
        UniformArenaPage page = _uniformArena[allocation.Page];
        int offset = checked((int)allocation.Offset);
        int count = checked((int)size);
        new ReadOnlySpan<byte>(data, count).CopyTo(page.Staging.AsSpan(offset, count));
        page.DirtyStart = Math.Min(page.DirtyStart, allocation.Offset);
        page.DirtyEnd = Math.Max(page.DirtyEnd, allocation.Offset + (ulong)size);
    }

    private void FlushUniformWrites()
    {
        for (int i = 0; i < _uniformArena.Count; i++)
        {
            UniformArenaPage page = _uniformArena[i];
            if (page.Buffer == 0 || page.DirtyStart == ulong.MaxValue
                || page.DirtyEnd <= page.DirtyStart)
            {
                continue;
            }

            ulong start = page.DirtyStart;
            ulong size = page.DirtyEnd - start;
            fixed (byte* basePtr = page.Staging)
            {
                WriteProfiledBuffer((WgpuBuffer*)page.Buffer, start,
                    basePtr + checked((int)start), checked((nuint)size));
            }
            page.DirtyStart = ulong.MaxValue;
            page.DirtyEnd = 0;
        }
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

        uint pixelBytes = native.Format == Silk.NET.WebGPU.TextureFormat.Rgba16float ? 8u : 4u;
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
        if (_measurePerformance) _stagedTextureUploads++;
        return true;
    }

    // Non-draw commands cannot be encoded while a render pass is open.
    // Copies, clears, readback and auxiliary passes therefore close the current
    // coalesced world pass before using the shared command encoder.
    private CommandEncoder* BeginCommands()
    {
        EndActiveCorePass();
        return BeginCommandEncoder();
    }

    private CommandEncoder* BeginCommandEncoder()
    {
        if (_commandEncoder == null)
            _commandEncoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
        return _commandEncoder;
    }

    private void RecordCommandOperation()
    {
        if (++_commandOperations >= CommandBatchOperations) FlushCommands();
    }

    private void EndCommands() => RecordCommandOperation();

    private void FlushCommands()
    {
        EndActiveCorePass();
        if (_commandEncoder == null) return;
        CommandBuffer* commands = _api.CommandEncoderFinish(_commandEncoder, new CommandBufferDescriptor());
        try
        {
            // Uniform writes target independent arena storage. Queue them
            // immediately before the submission that consumes the recorded
            // offsets, collapsing hundreds of tiny writes into one per page.
            FlushGeometryWrites();
            FlushUniformWrites();
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
        EndActiveCorePass();
        if (_commandEncoder != null) _api.CommandEncoderRelease(_commandEncoder);
        _commandEncoder = null;
        _commandOperations = 0;
    }

    // Call only at a completed public frame/readback boundary, never from a
    // texture upload nested inside an in-progress draw. Queue ordering permits
    // arena reuse after submission without waiting for GPU completion.
    private void ResetFrameBuffers()
    {
        foreach (UniformArenaPage page in _uniformArena)
        {
            page.Cursor = 0;
            page.DirtyStart = ulong.MaxValue;
            page.DirtyEnd = 0;
        }
        _uniformArenaPage = 0;
        foreach (var program in _generatedPrograms.Values) program.BindGroupCursor = 0;
        ResetGeometryArena();
        _uploadBufferCursor = 0;
        _frameBindGroupCursor = 0;
    }

    private void DisposeUniformBuffers()
    {
        foreach (UniformArenaPage page in _uniformArena)
            if (page.Buffer != 0) _api.BufferRelease((WgpuBuffer*)page.Buffer);
        _uniformArena.Clear();
        foreach (UploadBuffer upload in _uploadBuffers)
            if (upload.Buffer != 0) _api.BufferRelease((WgpuBuffer*)upload.Buffer);
        _uploadBuffers.Clear();
    }
}
#endif
