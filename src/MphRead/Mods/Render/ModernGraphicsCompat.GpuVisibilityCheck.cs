#if !MPHREAD_SERVER
using System;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    /// <summary>
    /// Opt-in native fixture: executes the production depth copy, max reduction,
    /// visibility, compaction and indirect argument shaders, then reads results.
    /// These waits are confined to the diagnostics command, never gameplay.
    /// </summary>
    internal static void VerifyGpuVisibilityForCheck()
    {
        ModernGraphicsCompat s = Current;
        const int width = 15, height = 7, count = 3;
        s.EndActiveCorePass();
        s.ResetGpuVisibilityFrameState();
        s.EnsureGpuVisibilityPipelines();
        s.EnsureGpuHiZ(width, height);
        var depthDescriptor = new TextureDescriptor
        {
            Size = new Extent3D(width, height, 1),
            Format = Silk.NET.WebGPU.TextureFormat.Depth32float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            MipLevelCount = 1, SampleCount = 1,
            Dimension = TextureDimension.Dimension2D
        };
        var depth = s._api.DeviceCreateTexture(s._device.Device, in depthDescriptor);
        if (depth == null) throw new InvalidOperationException("Visibility fixture depth allocation failed.");
        TextureView* depthView = null;
        BindGroup* group = null;
        WgpuBuffer* candidates = null;
        WgpuBuffer* args = null;
        WgpuBuffer* compact = null;
        WgpuBuffer* counters = null;
        WgpuBuffer* uniforms = null;
        WgpuBuffer* denseArgs = null;
        WgpuBuffer* bucketCounts = null;
        try
        {
            depthView = s._api.TextureCreateView(depth, null);
            if (depthView == null) throw new InvalidOperationException("Visibility fixture depth view failed.");
            candidates = s.GpuVisibilityCheckBuffer(count * 64, BufferUsage.Storage);
            args = s.GpuVisibilityCheckBuffer(count * 20, BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopySrc);
            compact = s.GpuVisibilityCheckBuffer(count * 4, BufferUsage.Storage | BufferUsage.CopySrc);
            counters = s.GpuVisibilityCheckBuffer(16, BufferUsage.Storage | BufferUsage.CopySrc);
            uniforms = s.GpuVisibilityCheckBuffer(160, BufferUsage.Uniform);
            denseArgs = s.GpuVisibilityCheckBuffer(20, BufferUsage.Storage);
            bucketCounts = s.GpuVisibilityCheckBuffer(4, BufferUsage.Storage);
            var entries = stackalloc BindGroupEntry[8];
            entries[0] = new() { Binding = 0, Buffer = candidates, Size = count * 64 };
            entries[1] = new() { Binding = 1, Buffer = args, Size = count * 20 };
            entries[2] = new() { Binding = 2, Buffer = compact, Size = count * 4 };
            entries[3] = new() { Binding = 3, Buffer = counters, Size = 16 };
            entries[4] = new() { Binding = 4, Buffer = uniforms, Size = 160 };
            entries[5] = new() { Binding = 5, TextureView = (TextureView*)s._gpuHiZFullView };
            entries[6] = new() { Binding = 6, Buffer = denseArgs, Size = 20 };
            entries[7] = new() { Binding = 7, Buffer = bucketCounts, Size = 4 };
            group = s.CreateTrackedBindGroup(new BindGroupDescriptor
                { Layout = s._gpuVisibilityLayout, Entries = entries, EntryCount = 8 });

            uint[] words = new uint[count * 16];
            // Hidden .7 depth, self-visible .2 depth, and outside the frustum.
            Vector3[] centers = { new(0, 0, .4f), new(0, 0, -.6f), new(3, 0, 0) };
            for (int i = 0; i < count; i++)
            {
                int at = i * 16;
                Vector3 extent = new(.05f, .05f, 0);
                WriteGpuVec3(words, at, centers[i] - extent);
                WriteGpuVec3(words, at + 4, centers[i] + extent);
                words[at + 8] = 3; words[at + 9] = (uint)(10 + i);
                words[at + 13] = uint.MaxValue;
            }
            fixed (uint* p = words) s.WriteProfiledBuffer(candidates, 0, p, (nuint)(count * 64));
            uint[] u = new uint[GpuVisibilityUniformWords];
            WriteGpuMatrix(u, 0, Matrix4.Identity); WriteGpuMatrix(u, 16, Matrix4.Identity);
            WriteGpuFloat(u, 32, width); WriteGpuFloat(u, 33, height);
            WriteGpuFloat(u, 34, s._gpuHiZMipCount); WriteGpuFloat(u, 35, 1);
            WriteGpuFloat(u, 36, count); WriteGpuFloat(u, 37, GpuVisibilityDepthBias);
            fixed (uint* p = u) s.WriteProfiledBuffer(uniforms, 0, p, 160);
            uint* zero = stackalloc uint[4];
            zero[0] = zero[1] = zero[2] = zero[3] = 0;

            for (int frame = 0; frame < 2; frame++)
            {
                // A removed occluder clears depth in frame 2. Rebuilding must
                // restore the hidden packet without camera motion or offsets.
                var attachment = new RenderPassDepthStencilAttachment
                {
                    View = depthView, DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store, DepthClearValue = frame == 0 ? .2f : 1f,
                    StencilReadOnly = true
                };
                var descriptor = new RenderPassDescriptor { DepthStencilAttachment = &attachment };
                RenderPassEncoder* pass = s._api.CommandEncoderBeginRenderPass(s.BeginCommands(), in descriptor);
                if (pass == null) throw new InvalidOperationException("Visibility fixture clear pass failed.");
                s._api.RenderPassEncoderEnd(pass); s._api.RenderPassEncoderRelease(pass); s.EndCommands();
                s.BuildGpuHiZ(depthView, width, height);
                s.WriteProfiledBuffer(counters, 0, zero, 16);
                s.DispatchGpuCompute(s._gpuVisibilityPipeline, group, 1, 1);
                uint[] result = s.ReadGpuVisibilityCheckBuffer(args, count * 20);
                uint[] totals = s.ReadGpuVisibilityCheckBuffer(counters, 16);
                uint expectedVisible = frame == 0 ? 1u : 2u;
                if (result[1] != (frame == 0 ? 0u : 1u) || result[6] != 1 || result[11] != 0
                    || totals[0] != expectedVisible || totals[1] != 1 || totals[2] != (frame == 0 ? 1u : 0u))
                    throw new InvalidOperationException($"GPU current-depth visibility/indirect result failed in frame {frame}.");
                uint[] ids = s.ReadGpuVisibilityCheckBuffer(compact, count * 4);
                if (frame == 0 ? ids[0] != 11 : !((ids[0] == 10 && ids[1] == 11) || (ids[0] == 11 && ids[1] == 10)))
                    throw new InvalidOperationException("GPU visible-ID compaction failed.");
            }
            s.VerifyGpuHiZOddEdgesForCheck();
            Console.WriteLine("[renderwindowcheck] GPU current-depth visibility, indirect args, compaction, removed occluder and odd Hi-Z edges PASS");
        }
        finally
        {
            s.FlushCommands();
            if (group != null) s.ReleaseTrackedBindGroup(group);
            if (candidates != null) s._api.BufferRelease(candidates);
            if (args != null) s._api.BufferRelease(args);
            if (compact != null) s._api.BufferRelease(compact);
            if (counters != null) s._api.BufferRelease(counters);
            if (uniforms != null) s._api.BufferRelease(uniforms);
            if (denseArgs != null) s._api.BufferRelease(denseArgs);
            if (bucketCounts != null) s._api.BufferRelease(bucketCounts);
            s.ReleaseGpuHiZResources();
            if (depthView != null) s._api.TextureViewRelease(depthView);
            s._api.TextureRelease(depth);
            s.ResetGpuVisibilityFrameState();
        }
    }

    private WgpuBuffer* GpuVisibilityCheckBuffer(ulong size, BufferUsage usage)
    {
        var descriptor = new BufferDescriptor { Size = size, Usage = usage | BufferUsage.CopyDst };
        WgpuBuffer* buffer = _api.DeviceCreateBuffer(_device.Device, in descriptor);
        if (buffer == null) throw new InvalidOperationException("GPU visibility fixture buffer allocation failed.");
        return buffer;
    }

    private void VerifyGpuHiZOddEdgesForCheck()
    {
        const uint width = 15, height = 7;
        var descriptor = new TextureDescriptor
        {
            Size = new Extent3D(width, height, 1),
            Format = Silk.NET.WebGPU.TextureFormat.R32float,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            MipLevelCount = 1, SampleCount = 1,
            Dimension = TextureDimension.Dimension2D
        };
        var texture = _api.DeviceCreateTexture(_device.Device, in descriptor);
        if (texture == null) throw new InvalidOperationException("Odd Hi-Z fixture texture allocation failed.");
        TextureView* sourceView = null;
        BindGroup* group = null;
        WgpuBuffer* output = null;
        try
        {
            sourceView = _api.TextureCreateView(texture, null);
            if (sourceView == null) throw new InvalidOperationException("Odd Hi-Z fixture view allocation failed.");
            float[] source = new float[width * height];
            Array.Fill(source, .2f);
            source[^1] = 1f; // Both orphan axes must survive every reduction.
            var destination = new ImageCopyTexture { Texture = texture, Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { BytesPerRow = width * 4, RowsPerImage = height };
            var extent = new Extent3D(width, height, 1);
            FlushCommands();
            fixed (float* p = source)
                _api.QueueWriteTexture(_queue, in destination, p, (nuint)(source.Length * 4), in layout, in extent);
            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new() { Binding = 0, TextureView = sourceView };
            entries[1] = new() { Binding = 1, TextureView = (TextureView*)_gpuHiZMipViews[1] };
            group = CreateTrackedBindGroup(new BindGroupDescriptor
                { Layout = _gpuHiZReduceLayout, Entries = entries, EntryCount = 2 });
            DispatchGpuCompute(_gpuHiZReducePipeline, group, 1, 1);
            for (int mip = 2; mip < _gpuHiZMipCount; mip++)
                DispatchGpuCompute(_gpuHiZReducePipeline,
                    (BindGroup*)_gpuHiZReduceBindGroups[mip - 1], 1, 1);

            output = GpuVisibilityCheckBuffer(256, BufferUsage.CopySrc);
            var from = new ImageCopyTexture
                { Texture = _gpuHiZTexture, MipLevel = (uint)(_gpuHiZMipCount - 1), Aspect = TextureAspect.All };
            var to = new ImageCopyBuffer
                { Buffer = output, Layout = new TextureDataLayout { BytesPerRow = 256, RowsPerImage = 1 } };
            var one = new Extent3D(1, 1, 1);
            _api.CommandEncoderCopyTextureToBuffer(BeginCommands(), &from, &to, &one);
            EndCommands();
            uint[] result = ReadGpuVisibilityCheckBuffer(output, 256);
            if (BitConverter.UInt32BitsToSingle(result[0]) != 1f)
                throw new InvalidOperationException("GPU odd Hi-Z reduction lost the clear orphan corner.");
        }
        finally
        {
            FlushCommands();
            if (group != null) ReleaseTrackedBindGroup(group);
            if (output != null) _api.BufferRelease(output);
            if (sourceView != null) _api.TextureViewRelease(sourceView);
            _api.TextureRelease(texture);
        }
    }

    private uint[] ReadGpuVisibilityCheckBuffer(WgpuBuffer* source, uint size)
    {
        WgpuBuffer* readback = GpuVisibilityCheckBuffer(size, BufferUsage.MapRead);
        bool mapped = false;
        try
        {
            _api.CommandEncoderCopyBufferToBuffer(BeginCommands(), source, 0, readback, 0, size);
            EndCommands(); FlushCommands();
            _mapStatus = BufferMapAsyncStatus.Unknown;
            _api.BufferMapAsync(readback, MapMode.Read, 0, size,
                new PfnBufferMapCallback((status, _) => _mapStatus = status), null);
            _device.Native.DevicePoll(_device.Device, true, null);
            if (_mapStatus != BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"GPU visibility fixture readback failed: {_mapStatus}.");
            mapped = true;
            uint* data = (uint*)_api.BufferGetConstMappedRange(readback, 0, size);
            if (data == null) throw new InvalidOperationException("GPU visibility fixture readback is null.");
            var result = new uint[size / 4];
            for (int i = 0; i < result.Length; i++) result[i] = data[i];
            return result;
        }
        finally
        {
            if (mapped) _api.BufferUnmap(readback);
            _api.BufferRelease(readback);
        }
    }
}
#endif
