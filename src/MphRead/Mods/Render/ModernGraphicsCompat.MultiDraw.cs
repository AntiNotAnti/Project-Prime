#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private sealed class RetainedMultiDrawPage
    {
        internal WgpuBuffer* Vertex;
        internal WgpuBuffer* Index;
        internal ulong VertexCapacityBytes;
        internal ulong IndexCapacityBytes;
        internal RetainedAtlasRanges Vertices = null!;
        internal RetainedAtlasRanges Indices = null!;
        internal int LiveEntries;
        internal RetainedAtlasPageOwnership Ownership = null!;
    }

    private readonly record struct RetainedMultiDrawEntry(
        RetainedMultiDrawPage Page,
        uint FirstIndex,
        int BaseVertex,
        uint IndexCount,
        uint VertexCount,
        ulong AllocationBytes);

    private readonly record struct RetainedDenseMultiDrawBucket(
        int Id,
        uint DenseBase,
        uint MaxCount,
        RetainedMultiDrawPage Page);

    private const ulong RetainedMultiDrawVertexPageBytes = 16UL * 1024 * 1024;
    private const ulong RetainedMultiDrawIndexPageBytes = 4UL * 1024 * 1024;
    private readonly List<RetainedMultiDrawPage> _retainedMultiDrawPages = new();
    private readonly Dictionary<GeometryList, RetainedMultiDrawEntry>
        _retainedMultiDrawEntries = new();
    private readonly Dictionary<GeometryList, bool>
        _retainedMultiDrawExplicitNormals = new();
    private readonly Dictionary<RenderItem, RetainedDenseMultiDrawBucket>
        _retainedDenseMultiDrawBuckets = new();
    private int _retainedDenseBucketCount;
    private uint _retainedDenseRecordCount;
    private uint[] _retainedMultiDrawUniformScratch = Array.Empty<uint>();
    private long _retainedMultiDrawCalls;
    private long _retainedMultiDrawLogicalDraws;
    private long _retainedMultiDrawFallbackBatches;
    private long _retainedMultiDrawAtlasBytes;
    private long _retainedDenseMultiDrawCalls;
    private long _retainedDenseMultiDrawCandidates;

    internal static bool RetainedStateMultiDrawEnabled =>
        _current?.UseRetainedStateMultiDraw ?? false;
    internal static long RetainedMultiDrawCalls =>
        _current?._retainedMultiDrawCalls ?? 0;
    internal static long RetainedMultiDrawLogicalDraws =>
        _current?._retainedMultiDrawLogicalDraws ?? 0;
    internal static long RetainedMultiDrawFallbackBatches =>
        _current?._retainedMultiDrawFallbackBatches ?? 0;
    internal static long RetainedMultiDrawAtlasBytes =>
        _current?._retainedMultiDrawAtlasBytes ?? 0;
    // Keep the original counter as cumulative uploaded bytes. Reserved and live
    // bytes answer different questions and must not be conflated with uploads.
    internal static long RetainedMultiDrawAtlasReservedBytes =>
        _current?.RetainedAtlasReservedBytes ?? 0;
    internal static long RetainedMultiDrawAtlasLiveBytes =>
        _current?.RetainedAtlasLiveBytes ?? 0;
    internal static int RetainedMultiDrawAtlasPages =>
        _current?._retainedMultiDrawPages.Count ?? 0;
    private long RetainedAtlasReservedBytes
    {
        get
        {
            long bytes = 0;
            foreach (var page in _retainedMultiDrawPages)
                bytes = checked(bytes + (long)page.Ownership.ReservedBytes);
            return bytes;
        }
    }
    private long RetainedAtlasLiveBytes
    {
        get
        {
            long bytes = 0;
            foreach (var page in _retainedMultiDrawPages)
                bytes = checked(bytes + (long)page.Ownership.LiveBytes);
            return bytes;
        }
    }
    internal static bool RetainedDenseMultiDrawEnabled =>
        _current?.UseRetainedDenseMultiDraw ?? false;
    internal static long RetainedDenseMultiDrawCalls =>
        _current?._retainedDenseMultiDrawCalls ?? 0;
    internal static long RetainedDenseMultiDrawCandidates =>
        _current?._retainedDenseMultiDrawCandidates ?? 0;

    private bool UseRetainedStateMultiDraw
    {
        get
        {
#if ANDROID
            return false;
#else
            return UseGpuVisibility
                && _device.SupportsMultiDrawIndirect
                && (_device.Backend is GraphicsBackend.DirectX12
                    or GraphicsBackend.Vulkan);
#endif
        }
    }

    private bool UseRetainedDenseMultiDraw =>
        UseRetainedStateMultiDraw
        && _device.SupportsMultiDrawIndirectCount;

    private RetainedMultiDrawEntry EnsureRetainedMultiDrawEntry(
        GeometryList geometry)
    {
        if (_retainedMultiDrawEntries.TryGetValue(
            geometry, out RetainedMultiDrawEntry found))
        {
            return found;
        }

        int strideFloats = LegacyGeometryBatch.FloatsPerVertex;
        if (geometry.Vertices.Length == 0
            || geometry.Vertices.Length % strideFloats != 0
            || geometry.Triangles.Length == 0)
        {
            throw new InvalidOperationException(
                "Retained multi-draw geometry is not a complete indexed mesh.");
        }

        uint vertexCount = checked(
            (uint)(geometry.Vertices.Length / strideFloats));
        uint indexCount = checked((uint)geometry.Triangles.Length);
        ulong vertexBytes = checked(
            (ulong)geometry.Vertices.Length * sizeof(float));
        ulong indexBytes = checked(
            (ulong)geometry.Triangles.Length * sizeof(int));

        RetainedMultiDrawPage? page = null;
        for (int i = 0; i < _retainedMultiDrawPages.Count; i++)
        {
            RetainedMultiDrawPage candidate = _retainedMultiDrawPages[i];
            if (candidate.Vertices.CanRent(vertexCount)
                && candidate.Indices.CanRent(indexCount))
            {
                page = candidate;
                break;
            }
        }

        if (page == null)
        {
            ulong vertexCapacity = Math.Max(
                RetainedMultiDrawVertexPageBytes, vertexBytes);
            ulong indexCapacity = Math.Max(
                RetainedMultiDrawIndexPageBytes, indexBytes);
            page = new RetainedMultiDrawPage
            {
                VertexCapacityBytes = vertexCapacity,
                IndexCapacityBytes = indexCapacity,
                Vertices = new(checked((uint)(vertexCapacity / ((ulong)strideFloats * sizeof(float))))),
                Indices = new(checked((uint)(indexCapacity / sizeof(int)))),
                Ownership = new(checked(vertexCapacity + indexCapacity))
            };
            page.Vertex = _api.DeviceCreateBuffer(
                _device.Device, new BufferDescriptor
                {
                    Size = vertexCapacity,
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst
                });
            page.Index = _api.DeviceCreateBuffer(
                _device.Device, new BufferDescriptor
                {
                    Size = indexCapacity,
                    Usage = BufferUsage.Index | BufferUsage.CopyDst
                });
            if (page.Vertex == null || page.Index == null)
            {
                if (page.Vertex != null) _api.BufferRelease(page.Vertex);
                if (page.Index != null) _api.BufferRelease(page.Index);
                throw new InvalidOperationException(
                    "Could not allocate retained multi-draw geometry page.");
            }
            _retainedMultiDrawPages.Add(page);
        }

        uint baseVertex = page.Vertices.Rent(vertexCount);
        uint firstIndex = page.Indices.Rent(indexCount);
        ulong vertexOffset = checked(
            (ulong)baseVertex * (ulong)strideFloats * sizeof(float));
        ulong indexOffset = checked((ulong)firstIndex * sizeof(int));
        try
        {
            fixed (float* vertexPtr = geometry.Vertices)
            {
                WriteProfiledBuffer(page.Vertex, vertexOffset,
                    vertexPtr, checked((nuint)vertexBytes));
            }
            fixed (int* indexPtr = geometry.Triangles)
            {
                WriteProfiledBuffer(page.Index, indexOffset,
                    indexPtr, checked((nuint)indexBytes));
            }
        }
        catch
        {
            page.Vertices.Return(baseVertex, vertexCount);
            page.Indices.Return(firstIndex, indexCount);
            if (page.LiveEntries == 0)
            {
                _retainedMultiDrawPages.Remove(page);
                _api.BufferRelease(page.Vertex);
                _api.BufferRelease(page.Index);
            }
            throw;
        }
        page.LiveEntries++;

        ulong allocationBytes = checked(vertexBytes + indexBytes);
        var entry = new RetainedMultiDrawEntry(
            page, firstIndex, checked((int)baseVertex), indexCount, vertexCount, allocationBytes);
        _retainedMultiDrawEntries.Add(geometry, entry);
        page.Ownership.Retain(allocationBytes);
        _retainedMultiDrawAtlasBytes += checked(
            (long)(vertexBytes + indexBytes));
        return entry;
    }

    private bool TryPrepareRetainedMultiDrawCandidate(
        GeometryList geometry,
        out uint firstIndex, out int baseVertex)
    {
        firstIndex = 0;
        baseVertex = 0;
        if (!UseRetainedStateMultiDraw)
            return false;
        RetainedMultiDrawEntry entry =
            EnsureRetainedMultiDrawEntry(geometry);
        firstIndex = entry.FirstIndex;
        baseVertex = entry.BaseVertex;
        return true;
    }

    private bool TryGpuVisibilityAtlasGeometry(
        RenderItem item, GeometryList geometry,
        out RetainedMultiDrawEntry entry)
    {
        entry = default;
        return UseRetainedStateMultiDraw
            && _gpuVisibilityPrepared
            && _gpuVisibilitySlots.ContainsKey(item)
            && _retainedMultiDrawEntries.TryGetValue(geometry, out entry);
    }

    private bool RetainedMultiDrawGeometryHasExplicitNormals(
        GeometryList geometry)
    {
        if (_retainedMultiDrawExplicitNormals.TryGetValue(
            geometry, out bool cached))
        {
            return cached;
        }

        int stride = LegacyGeometryBatch.FloatsPerVertex;
        bool explicitNormals = geometry.Vertices.Length % stride == 0;
        for (int i = 14; explicitNormals && i < geometry.Vertices.Length;
            i += stride)
        {
            explicitNormals = geometry.Vertices[i] > 0.5f;
        }
        _retainedMultiDrawExplicitNormals[geometry] = explicitNormals;
        return explicitNormals;
    }

    private static bool SameFloatBits(float a, float b) =>
        BitConverter.SingleToUInt32Bits(a)
            == BitConverter.SingleToUInt32Bits(b);

    private static bool SameVector3Bits(Vector3 a, Vector3 b) =>
        SameFloatBits(a.X, b.X)
        && SameFloatBits(a.Y, b.Y)
        && SameFloatBits(a.Z, b.Z);

    private static bool SameMatrixBits(Matrix4 a, Matrix4 b) =>
        SameFloatBits(a.M11, b.M11)
        && SameFloatBits(a.M12, b.M12)
        && SameFloatBits(a.M13, b.M13)
        && SameFloatBits(a.M14, b.M14)
        && SameFloatBits(a.M21, b.M21)
        && SameFloatBits(a.M22, b.M22)
        && SameFloatBits(a.M23, b.M23)
        && SameFloatBits(a.M24, b.M24)
        && SameFloatBits(a.M31, b.M31)
        && SameFloatBits(a.M32, b.M32)
        && SameFloatBits(a.M33, b.M33)
        && SameFloatBits(a.M34, b.M34)
        && SameFloatBits(a.M41, b.M41)
        && SameFloatBits(a.M42, b.M42)
        && SameFloatBits(a.M43, b.M43)
        && SameFloatBits(a.M44, b.M44);

    internal static bool RetainedMultiDrawDynamicStateEquivalent(
        RenderItem a, RenderItem b) =>
        SameRetainedMultiDrawDynamicState(a, b);

    private static bool SameRetainedMultiDrawDynamicState(
        RenderItem a, RenderItem b)
    {
        if (!SameVector3Bits(
                a.LightInfo.Light1Vector, b.LightInfo.Light1Vector)
            || !SameVector3Bits(
                a.LightInfo.Light1Color, b.LightInfo.Light1Color)
            || !SameVector3Bits(
                a.LightInfo.Light2Vector, b.LightInfo.Light2Vector)
            || !SameVector3Bits(
                a.LightInfo.Light2Color, b.LightInfo.Light2Color)
            || a.MatrixStackCount != b.MatrixStackCount)
        {
            return false;
        }

        if (a.MatrixStackCount > 0)
        {
            int count = checked(a.MatrixStackCount * 16);
            if (count > a.MatrixStack.Length
                || count > b.MatrixStack.Length)
            {
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (!SameFloatBits(
                    a.MatrixStack[i], b.MatrixStack[i]))
                {
                    return false;
                }
            }
            return true;
        }
        return SameMatrixBits(a.Transform, b.Transform);
    }

    private void PrepareRetainedDenseMultiDrawBuckets(
        IReadOnlyList<RetainedDrawPacket> packets)
    {
        _retainedDenseMultiDrawBuckets.Clear();
        _retainedDenseBucketCount = 0;
        _retainedDenseRecordCount = 0;
        if (!UseRetainedDenseMultiDraw)
            return;

        int runStart = -1;
        int runCount = 0;
        int previousSlot = -1;
        RetainedDrawPacket firstPacket = default;
        RetainedMultiDrawPage? runPage = null;

        void FlushRun()
        {
            if (runStart < 0 || runCount < 2 || runPage == null)
            {
                runStart = -1;
                runCount = 0;
                previousSlot = -1;
                runPage = null;
                return;
            }

            int bucketId = _retainedDenseBucketCount++;
            uint denseBase = _retainedDenseRecordCount;
            uint maxCount = checked((uint)runCount);
            _retainedDenseRecordCount =
                checked(_retainedDenseRecordCount + maxCount);
            var bucket = new RetainedDenseMultiDrawBucket(
                bucketId, denseBase, maxCount, runPage);
            for (int i = 0; i < runCount; i++)
            {
                RenderItem item = packets[runStart + i].Item;
                _retainedDenseMultiDrawBuckets[item] = bucket;
                int slot = _gpuVisibilitySlots[item];
                int at = slot * GpuVisibilityCandidateWords;
                _gpuVisibilityCandidateWords[at + 13] =
                    checked((uint)bucketId);
                _gpuVisibilityCandidateWords[at + 14] = denseBase;
            }

            runStart = -1;
            runCount = 0;
            previousSlot = -1;
            runPage = null;
        }

        for (int packetIndex = 0;
            packetIndex < packets.Count; packetIndex++)
        {
            RetainedDrawPacket packet = packets[packetIndex];
            RenderItem item = packet.Item;
            RetainedMultiDrawEntry entry = default;
            bool candidate =
                _gpuVisibilitySlots.TryGetValue(item, out int slot)
                && _lists.TryGetValue(
                    packet.Mesh.ListId, out GeometryList? geometry)
                && RetainedMultiDrawGeometryHasExplicitNormals(geometry)
                && _retainedMultiDrawEntries.TryGetValue(
                    geometry, out entry);

            if (!candidate)
            {
                FlushRun();
                continue;
            }

            bool compatible = runStart >= 0
                && slot == previousSlot + 1
                && packet.BatchState == firstPacket.BatchState
                && SameRetainedMultiDrawDynamicState(
                    firstPacket.Item, item)
                && ReferenceEquals(runPage, entry.Page);
            if (!compatible)
            {
                FlushRun();
                runStart = packetIndex;
                runCount = 1;
                firstPacket = packet;
                runPage = entry.Page;
            }
            else
            {
                runCount++;
            }
            previousSlot = slot;
        }
        FlushRun();
    }

    private bool TryGetDenseMultiDrawBucket(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count,
        out RetainedDenseMultiDrawBucket bucket)
    {
        bucket = default;
        if (!UseRetainedDenseMultiDraw
            || count < 2
            || !_retainedDenseMultiDrawBuckets.TryGetValue(
                packets[start].Item, out bucket)
            || bucket.MaxCount != checked((uint)count))
        {
            return false;
        }

        for (int i = 1; i < count; i++)
        {
            if (!_retainedDenseMultiDrawBuckets.TryGetValue(
                    packets[start + i].Item,
                    out RetainedDenseMultiDrawBucket next)
                || next != bucket)
            {
                return false;
            }
        }
        return true;
    }

    private void EncodeRetainedStateMultiDraw(
        RenderPassEncoder* pass,
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count, int firstSlot)
    {
        if (TryGetDenseMultiDrawBucket(
                packets, start, count,
                out RetainedDenseMultiDrawBucket dense)
            && _gpuVisibilityDenseIndirectBuffer != null
            && _gpuVisibilityBucketCountBuffer != null)
        {
            _device.Native.RenderPassEncoderMultiDrawIndexedIndirectCount(
                pass,
                _gpuVisibilityDenseIndirectBuffer,
                checked((ulong)dense.DenseBase
                    * RetainedIndexedIndirectBytes),
                _gpuVisibilityBucketCountBuffer,
                checked((ulong)dense.Id * sizeof(uint)),
                dense.MaxCount);
            _retainedDenseMultiDrawCalls++;
            _retainedDenseMultiDrawCandidates += count;
        }
        else
        {
            _device.Native.RenderPassEncoderMultiDrawIndexedIndirect(
                pass, _gpuVisibilityIndirectBuffer,
                checked((ulong)firstSlot
                    * RetainedIndexedIndirectBytes),
                checked((uint)count));
        }

        _retainedMultiDrawCalls++;
        _retainedMultiDrawLogicalDraws += count;
        _retainedIndirectDraws += count;
        _gpuVisibilityIndirectDraws += count;
    }

    private bool TryGetConsecutiveVisibilityRange(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count, out int firstSlot,
        out RetainedMultiDrawPage? page)
    {
        firstSlot = -1;
        page = null;
        for (int i = 0; i < count; i++)
        {
            RetainedDrawPacket packet = packets[start + i];
            if (!_gpuVisibilitySlots.TryGetValue(
                    packet.Item, out int slot)
                || (i > 0 && slot != firstSlot + i)
                || !_lists.TryGetValue(
                    packet.Mesh.ListId, out GeometryList? geometry)
                || !RetainedMultiDrawGeometryHasExplicitNormals(geometry)
                || !_retainedMultiDrawEntries.TryGetValue(
                    geometry, out RetainedMultiDrawEntry entry))
            {
                return false;
            }
            if (i == 0)
            {
                firstSlot = slot;
                page = entry.Page;
            }
            else if (!ReferenceEquals(page, entry.Page))
            {
                return false;
            }
        }
        return firstSlot >= 0 && page != null;
    }

    internal static bool TryDrawRetainedWorldMultiDraw(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count,
        RetainedWorldTextureSet textures,
        bool showTextures, bool useLighting, bool faceCulling,
        WorldRenderPassKind passKind)
    {
        if (_current == null || count < 2)
            return false;
        return Current.TryDrawRetainedWorldMultiDrawCore(
            packets, start, count, textures,
            showTextures, useLighting, faceCulling, passKind);
    }

    private bool TryDrawRetainedWorldMultiDrawCore(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count,
        RetainedWorldTextureSet textures,
        bool showTextures, bool useLighting, bool faceCulling,
        WorldRenderPassKind passKind)
    {
        if (!UseRetainedStateMultiDraw
            || passKind is not (WorldRenderPassKind.Opaque
                or WorldRenderPassKind.RebuildDepth)
            || !_retainedWorldFrameReady
            || _retainedWorldFrameTemplate == null
            || _retainedWorldOffsets == null
            || _gpuVisibilityIndirectBuffer == null
            || !TryGetConsecutiveVisibilityRange(
                packets, start, count,
                out int firstSlot, out RetainedMultiDrawPage? page))
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        CoreTarget target = ResolveDrawTarget();
        if (_retainedWorldFrameFramebuffer != _resources.DrawFramebuffer
            || _retainedWorldFrameWidth != target.Width
            || _retainedWorldFrameHeight != target.Height)
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        GeneratedProgram generated =
            GeneratedShader(ModernProgramKind.World);
        if (_retainedMultiDrawUniformScratch.Length
            != generated.Words.Length)
        {
            _retainedMultiDrawUniformScratch =
                new uint[generated.Words.Length];
        }

        RenderItem first = packets[start].Item;
        if (!packets[start].ReorderableOpaque
            || !RetainedWorldPacketEligibleForPass(first, passKind))
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        _currentColor = new Vector4(first.Diffuse, 1f);
        if (faceCulling && first.CullingMode != CullingMode.Neither)
        {
            _enabled.Add(EnableCap.CullFace);
            _cullFace = first.CullingMode == CullingMode.Back
                ? TriangleFace.Back : TriangleFace.Front;
        }
        else
        {
            _enabled.Remove(EnableCap.CullFace);
        }
        BindRetainedWorldTextures(
            textures, first.XRepeat, first.YRepeat);

        Array.Copy(_retainedWorldFrameTemplate,
            generated.Words, generated.Words.Length);
        PatchRetainedWorldUniformWords(
            generated, target, first, textures,
            showTextures, useLighting,
            projectionOverride: null, Matrix4.Identity,
            outlineMask: false);
        Array.Copy(generated.Words,
            _retainedMultiDrawUniformScratch,
            generated.Words.Length);

        for (int i = 1; i < count; i++)
        {
            RetainedDrawPacket packet = packets[start + i];
            RenderItem item = packet.Item;
            if (!packet.ReorderableOpaque
                || !RetainedWorldPacketEligibleForPass(
                    item, passKind))
            {
                _retainedMultiDrawFallbackBatches++;
                return false;
            }
            Array.Copy(_retainedWorldFrameTemplate,
                generated.Words, generated.Words.Length);
            PatchRetainedWorldUniformWords(
                generated, target, item, textures,
                showTextures, useLighting,
                projectionOverride: null, Matrix4.Identity,
                outlineMask: false);
            if (!generated.Words.AsSpan().SequenceEqual(
                _retainedMultiDrawUniformScratch))
            {
                _retainedMultiDrawFallbackBatches++;
                return false;
            }
        }

        Array.Copy(_retainedMultiDrawUniformScratch,
            generated.Words, generated.Words.Length);
        int retainedSlot =
            UploadRetainedWorldUniformWords(generated);
        CorePipelineRecord pipeline = CorePipeline(
            ModernProgramKind.World,
            PrimitiveTopology.TriangleList, target);
        BindGroup* bindGroup = RetainedWorldBindGroup(
            generated, pipeline.Layout, retainedSlot);

        RenderPassEncoder* pass =
            CoreRenderPass(target, 1, pipeline.Pipeline);
        _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
        _api.RenderPassEncoderSetBindGroup(
            pass, 0, bindGroup, 0, null);
        _api.RenderPassEncoderSetVertexBuffer(
            pass, 0, page!.Vertex, 0, page.VertexCapacityBytes);
        _api.RenderPassEncoderSetIndexBuffer(
            pass, page.Index, IndexFormat.Uint32,
            0, page.IndexCapacityBytes);
        _api.RenderPassEncoderSetViewport(
            pass, 0, 0, target.Width, target.Height, 0, 1);
        ApplyScissor(pass, target.Width, target.Height);
        if (_enabled.Contains(EnableCap.StencilTest)
            && target.HasDepth)
        {
            _api.RenderPassEncoderSetStencilReference(
                pass, (uint)_stencilReference);
        }

        EncodeRetainedStateMultiDraw(
            pass, packets, start, count, firstSlot);
        if (_measurePerformance) _coreDraws += count;
        RecordCommandOperation();

        RetainedDrawPacket last = packets[start + count - 1];
        if (_lists.TryGetValue(
                last.Mesh.ListId, out GeometryList? lastGeometry))
        {
            if (lastGeometry.EndNormal is Vector3 normal)
                _currentNormal = normal;
            if (lastGeometry.EndColor is Vector4 color)
                _currentColor = color;
        }
        return true;
    }


    internal static bool TryDrawRetainedPbrMultiDraw(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count,
        RetainedWorldTextureSet textures,
        bool showTextures, bool faceCulling,
        Matrix4 projection)
    {
        if (_current == null || count < 2)
            return false;
        return Current.TryDrawRetainedPbrMultiDrawCore(
            packets, start, count, textures,
            showTextures, faceCulling, projection);
    }

    private bool TryDrawRetainedPbrMultiDrawCore(
        IReadOnlyList<RetainedDrawPacket> packets,
        int start, int count,
        RetainedWorldTextureSet textures,
        bool showTextures, bool faceCulling,
        Matrix4 projection)
    {
        if (!UseRetainedStateMultiDraw
            || !_retainedPbrFrameReady
            || _retainedPbrFrameTemplate == null
            || _retainedPbrOffsets == null
            || _gpuVisibilityIndirectBuffer == null
            || !TryGetConsecutiveVisibilityRange(
                packets, start, count,
                out int firstSlot, out RetainedMultiDrawPage? page))
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        CoreTarget target = ResolveDrawTarget();
        if (target.ColorTargetCount < 3
            || _retainedPbrFrameFramebuffer != _resources.DrawFramebuffer
            || _retainedPbrFrameWidth != target.Width
            || _retainedPbrFrameHeight != target.Height)
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        GeneratedProgram generated =
            GeneratedShader(ModernProgramKind.DeferredPbrMrt);
        if (_retainedMultiDrawUniformScratch.Length
            != generated.Words.Length)
        {
            _retainedMultiDrawUniformScratch =
                new uint[generated.Words.Length];
        }

        RenderItem first = packets[start].Item;
        if (!packets[start].ReorderableOpaque
            || !RetainedDeferredPbrPacketEligible(first))
        {
            _retainedMultiDrawFallbackBatches++;
            return false;
        }

        _currentColor = new Vector4(first.Diffuse, 1f);
        if (faceCulling && first.CullingMode != CullingMode.Neither)
        {
            _enabled.Add(EnableCap.CullFace);
            _cullFace = first.CullingMode == CullingMode.Back
                ? TriangleFace.Back : TriangleFace.Front;
        }
        else
        {
            _enabled.Remove(EnableCap.CullFace);
        }
        BindRetainedWorldTextures(
            textures, first.XRepeat, first.YRepeat);

        Array.Copy(_retainedPbrFrameTemplate,
            generated.Words, generated.Words.Length);
        PatchRetainedDeferredPbrWords(
            generated, target, first, textures,
            showTextures, projection, Matrix4.Identity);
        Array.Copy(generated.Words,
            _retainedMultiDrawUniformScratch,
            generated.Words.Length);

        for (int i = 1; i < count; i++)
        {
            RetainedDrawPacket packet = packets[start + i];
            RenderItem item = packet.Item;
            if (!packet.ReorderableOpaque
                || !RetainedDeferredPbrPacketEligible(item))
            {
                _retainedMultiDrawFallbackBatches++;
                return false;
            }
            Array.Copy(_retainedPbrFrameTemplate,
                generated.Words, generated.Words.Length);
            PatchRetainedDeferredPbrWords(
                generated, target, item, textures,
                showTextures, projection, Matrix4.Identity);
            if (!generated.Words.AsSpan().SequenceEqual(
                _retainedMultiDrawUniformScratch))
            {
                _retainedMultiDrawFallbackBatches++;
                return false;
            }
        }

        Array.Copy(_retainedMultiDrawUniformScratch,
            generated.Words, generated.Words.Length);
        int retainedSlot =
            UploadRetainedPbrUniformWords(generated);
        CorePipelineRecord pipeline = CorePipeline(
            ModernProgramKind.DeferredPbrMrt,
            PrimitiveTopology.TriangleList, target);
        BindGroup* bindGroup = RetainedPbrBindGroup(
            generated, pipeline.Layout, retainedSlot);

        RenderPassEncoder* pass =
            CoreRenderPass(target, 3, pipeline.Pipeline);
        _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
        _api.RenderPassEncoderSetBindGroup(
            pass, 0, bindGroup, 0, null);
        _api.RenderPassEncoderSetVertexBuffer(
            pass, 0, page!.Vertex, 0, page.VertexCapacityBytes);
        _api.RenderPassEncoderSetIndexBuffer(
            pass, page.Index, IndexFormat.Uint32,
            0, page.IndexCapacityBytes);
        _api.RenderPassEncoderSetViewport(
            pass, 0, 0, target.Width, target.Height, 0, 1);
        ApplyScissor(pass, target.Width, target.Height);

        EncodeRetainedStateMultiDraw(
            pass, packets, start, count, firstSlot);
        if (_measurePerformance) _coreDraws += count;
        RecordCommandOperation();

        RetainedDrawPacket last = packets[start + count - 1];
        if (_lists.TryGetValue(
                last.Mesh.ListId, out GeometryList? lastGeometry))
        {
            if (lastGeometry.EndNormal is Vector3 normal)
                _currentNormal = normal;
            if (lastGeometry.EndColor is Vector4 color)
                _currentColor = color;
        }
        return true;
    }

    private void ReleaseRetainedMultiDrawGeometry(
        GeometryList geometry)
    {
        if (_retainedMultiDrawEntries.TryGetValue(geometry, out RetainedMultiDrawEntry entry))
        {
            // Submit every command referencing this span before a future
            // QueueWriteBuffer can reuse it. Queue ordering retains in-flight data.
            FlushCommands();
            _retainedMultiDrawEntries.Remove(geometry);
            RetainedMultiDrawPage page = entry.Page;
            page.Vertices.Return(checked((uint)entry.BaseVertex), entry.VertexCount);
            page.Indices.Return(entry.FirstIndex, entry.IndexCount);
            bool empty = page.Ownership.Release(entry.AllocationBytes);
            if ((--page.LiveEntries == 0) != empty)
                throw new InvalidOperationException("Retained atlas ranges and allocation ownership disagree.");
            if (empty)
            {
                // Release native reference ownership, never BufferDestroy.
                // Submitted commands retain their native resource references.
                _retainedMultiDrawPages.Remove(page);
                if (page.Vertex != null) _api.BufferRelease(page.Vertex);
                if (page.Index != null) _api.BufferRelease(page.Index);
                page.Vertex = page.Index = null;
            }
            // Prepared buckets are frame-local and can still refer to the
            // returned span or a retired page. Rebuild them before future draws.
            _retainedDenseMultiDrawBuckets.Clear();
            _retainedDenseBucketCount = 0;
            _retainedDenseRecordCount = 0;
        }
        _retainedMultiDrawExplicitNormals.Remove(geometry);
    }

    private void DisposeRetainedMultiDraw()
    {
        foreach (RetainedMultiDrawPage page in _retainedMultiDrawPages)
        {
            if (page.Vertex != null)
                _api.BufferRelease(page.Vertex);
            if (page.Index != null)
                _api.BufferRelease(page.Index);
        }
        _retainedMultiDrawPages.Clear();
        _retainedMultiDrawEntries.Clear();
        _retainedMultiDrawExplicitNormals.Clear();
        _retainedDenseMultiDrawBuckets.Clear();
        _retainedDenseBucketCount = 0;
        _retainedDenseRecordCount = 0;
        _retainedMultiDrawUniformScratch = Array.Empty<uint>();
    }
}
#endif
