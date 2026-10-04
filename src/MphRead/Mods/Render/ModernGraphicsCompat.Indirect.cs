#if !MPHREAD_SERVER
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MphRead;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private sealed class RetainedIndirectPage
    {
        internal nint Buffer;
        internal ulong Capacity;
        internal byte[] Staging = Array.Empty<byte>();
        internal ulong DirtyStart = ulong.MaxValue;
        internal ulong DirtyEnd;
    }

    private readonly struct RetainedIndirectAllocation
    {
        internal RetainedIndirectAllocation(
            int page, nint buffer, ulong offset)
        {
            Page = page;
            Buffer = buffer;
            Offset = offset;
        }

        internal int Page { get; }
        internal nint Buffer { get; }
        internal ulong Offset { get; }
    }

    // WebGPU DrawIndexedIndirect is exactly five 32-bit values:
    // indexCount, instanceCount, firstIndex, baseVertex, firstInstance.
    private const ulong RetainedIndexedIndirectBytes = 20;
    private const ulong RetainedIndirectPageBytes = 256UL * 1024;
    private readonly List<RetainedIndirectPage> _retainedIndirectArena = new();
    private int _retainedIndirectCursor;
    private int _retainedIndirectHighWater;
    private long _retainedIndirectDraws;

    internal static long RetainedIndirectDraws =>
        _current?._retainedIndirectDraws ?? 0;
    internal static int RetainedIndirectHighWater =>
        _current?._retainedIndirectHighWater ?? 0;
    internal static bool RetainedIndirectEnabled =>
        _current?.UseRetainedIndirectDraws ?? false;

    private bool UseRetainedIndirectDraws
    {
        get
        {
#if ANDROID
            // Mobile tilers currently do better with the existing direct calls.
            // Keep the argument arena available for future compute compaction.
            return false;
#else
            // DX12/Vulkan benefit from moving the indexed draw parameters into
            // stable GPU-visible storage. Metal keeps direct draws until its
            // indirect path wins the frozen benchmark.
            return _device.Backend is GraphicsBackend.DirectX12
                or GraphicsBackend.Vulkan;
#endif
        }
    }

    private RetainedIndirectAllocation RentRetainedIndexedIndirect(
        uint indexCount)
    {
        int slotsPerPage = checked((int)Math.Max(
            1UL, RetainedIndirectPageBytes / RetainedIndexedIndirectBytes));
        int slot = _retainedIndirectCursor++;
        _retainedIndirectHighWater = Math.Max(
            _retainedIndirectHighWater, _retainedIndirectCursor);
        int pageIndex = slot / slotsPerPage;
        int slotInPage = slot % slotsPerPage;
        while (_retainedIndirectArena.Count <= pageIndex)
            _retainedIndirectArena.Add(new RetainedIndirectPage());

        RetainedIndirectPage page = _retainedIndirectArena[pageIndex];
        if (page.Buffer == 0)
        {
            page.Capacity = Math.Max(
                RetainedIndirectPageBytes,
                checked((ulong)slotsPerPage * RetainedIndexedIndirectBytes));
            WgpuBuffer* buffer = _api.DeviceCreateBuffer(
                _device.Device, new BufferDescriptor
                {
                    Size = page.Capacity,
                    Usage = BufferUsage.Indirect | BufferUsage.CopyDst
                });
            if (buffer == null)
                throw new InvalidOperationException(
                    $"Could not allocate {page.Capacity} byte retained indirect arena.");
            page.Buffer = (nint)buffer;
            page.Staging = new byte[checked((int)page.Capacity)];
        }

        ulong offset = checked(
            (ulong)slotInPage * RetainedIndexedIndirectBytes);
        Span<byte> args = page.Staging.AsSpan(
            checked((int)offset), checked((int)RetainedIndexedIndirectBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(args.Slice(0, 4), indexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(args.Slice(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(args.Slice(8, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(args.Slice(12, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(args.Slice(16, 4), 0);
        page.DirtyStart = Math.Min(page.DirtyStart, offset);
        page.DirtyEnd = Math.Max(
            page.DirtyEnd, offset + RetainedIndexedIndirectBytes);
        return new RetainedIndirectAllocation(
            pageIndex, page.Buffer, offset);
    }

    private bool TryDrawRetainedIndexedIndirect(
        RenderPassEncoder* pass, uint indexCount, RenderItem item)
    {
        if (!item.RetainedRoomOwned || !UseRetainedIndirectDraws)
            return false;

        // When the pre-world compute pass prepared this exact persistent room
        // packet, consume its GPU-written 0/1 instanceCount directly. Shadow
        // runs before that pass and therefore naturally takes the CPU argument
        // fallback instead of seeing stale visibility from the prior frame.
        if (TryGpuVisibilityIndirect(pass, item))
            return true;

        RetainedIndirectAllocation allocation =
            RentRetainedIndexedIndirect(indexCount);
        _api.RenderPassEncoderDrawIndexedIndirect(
            pass, (WgpuBuffer*)allocation.Buffer, allocation.Offset);
        _retainedIndirectDraws++;
        return true;
    }

    private void FlushRetainedIndirectWrites()
    {
        for (int i = 0; i < _retainedIndirectArena.Count; i++)
        {
            RetainedIndirectPage page = _retainedIndirectArena[i];
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

    private void ResetRetainedIndirectArena()
    {
        foreach (RetainedIndirectPage page in _retainedIndirectArena)
        {
            page.DirtyStart = ulong.MaxValue;
            page.DirtyEnd = 0;
        }
        _retainedIndirectCursor = 0;
    }

    private void DisposeRetainedIndirectArena()
    {
        foreach (RetainedIndirectPage page in _retainedIndirectArena)
        {
            if (page.Buffer != 0)
                _api.BufferRelease((WgpuBuffer*)page.Buffer);
        }
        _retainedIndirectArena.Clear();
        _retainedIndirectCursor = 0;
        _retainedIndirectHighWater = 0;
    }
}
#endif
