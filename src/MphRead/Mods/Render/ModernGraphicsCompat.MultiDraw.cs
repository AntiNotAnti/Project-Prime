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
        internal uint VertexCursor;
        internal uint IndexCursor;
    }

    private readonly record struct RetainedMultiDrawEntry(
        RetainedMultiDrawPage Page,
        uint FirstIndex,
        int BaseVertex,
        uint IndexCount);

    private const ulong RetainedMultiDrawVertexPageBytes = 16UL * 1024 * 1024;
    private const ulong RetainedMultiDrawIndexPageBytes = 4UL * 1024 * 1024;
    private readonly List<RetainedMultiDrawPage> _retainedMultiDrawPages = new();
    private readonly Dictionary<GeometryList, RetainedMultiDrawEntry>
        _retainedMultiDrawEntries = new();
    private readonly Dictionary<GeometryList, bool>
        _retainedMultiDrawExplicitNormals = new();
    private uint[] _retainedMultiDrawUniformScratch = Array.Empty<uint>();
    private long _retainedMultiDrawCalls;
    private long _retainedMultiDrawLogicalDraws;
    private long _retainedMultiDrawFallbackBatches;
    private long _retainedMultiDrawAtlasBytes;

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
            ulong usedVertexBytes = checked(
                (ulong)candidate.VertexCursor
                * (ulong)strideFloats * sizeof(float));
            ulong usedIndexBytes = checked(
                (ulong)candidate.IndexCursor * sizeof(int));
            if (usedVertexBytes + vertexBytes <= candidate.VertexCapacityBytes
                && usedIndexBytes + indexBytes <= candidate.IndexCapacityBytes)
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
                IndexCapacityBytes = indexCapacity
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

        uint baseVertex = page.VertexCursor;
        uint firstIndex = page.IndexCursor;
        ulong vertexOffset = checked(
            (ulong)baseVertex * (ulong)strideFloats * sizeof(float));
        ulong indexOffset = checked((ulong)firstIndex * sizeof(int));
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
        page.VertexCursor = checked(baseVertex + vertexCount);
        page.IndexCursor = checked(firstIndex + indexCount);

        var entry = new RetainedMultiDrawEntry(
            page, firstIndex, checked((int)baseVertex), indexCount);
        _retainedMultiDrawEntries.Add(geometry, entry);
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

        _device.Native.RenderPassEncoderMultiDrawIndexedIndirect(
            pass, _gpuVisibilityIndirectBuffer,
            checked((ulong)firstSlot * RetainedIndexedIndirectBytes),
            checked((uint)count));
        _retainedMultiDrawCalls++;
        _retainedMultiDrawLogicalDraws += count;
        _retainedIndirectDraws += count;
        _gpuVisibilityIndirectDraws += count;
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
        _retainedMultiDrawEntries.Remove(geometry);
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
        _retainedMultiDrawUniformScratch = Array.Empty<uint>();
    }
}
#endif
