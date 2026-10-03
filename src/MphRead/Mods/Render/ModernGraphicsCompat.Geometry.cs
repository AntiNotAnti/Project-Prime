#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        private sealed class NativeGeometry
        {
            internal WgpuBuffer* Vertex;
            internal WgpuBuffer* Index;
            internal ulong VertexCapacity;
            internal ulong IndexCapacity;
            internal ulong VertexOffset;
            internal ulong IndexOffset;
        }

        private sealed class GeometryArenaPage
        {
            internal nint Vertex;
            internal nint Index;
            internal ulong VertexCapacity;
            internal ulong IndexCapacity;
            internal byte[] VertexStaging = Array.Empty<byte>();
            internal byte[] IndexStaging = Array.Empty<byte>();
            internal ulong VertexCursor;
            internal ulong IndexCursor;
            internal ulong VertexDirtyStart = ulong.MaxValue;
            internal ulong VertexDirtyEnd;
            internal ulong IndexDirtyStart = ulong.MaxValue;
            internal ulong IndexDirtyEnd;
        }

        private const ulong GeometryVertexPageBytes = 2UL * 1024 * 1024;
        private const ulong GeometryIndexPageBytes = 512UL * 1024;

        // Display lists own immutable GPU buffers. Dynamic immediate geometry is
        // packed into a few frame-arena pages so hundreds of draws do not become
        // hundreds of WebGPU buffer objects and QueueWriteBuffer calls.
        private readonly Dictionary<(float[], int[]), NativeGeometry> _geometryCache = new();
        private readonly List<NativeGeometry> _transientGeometry = new();
        private readonly List<GeometryArenaPage> _geometryArena = new();
        private int _transientGeometryCursor;
        private int _geometryArenaPage;
        private bool _drawingList;
        private bool _wireframe;
        private readonly Dictionary<int[], int[]> _wireframeIndices = new();

        private static ulong AlignGeometry(ulong value, ulong alignment) =>
            checked((value + alignment - 1) & ~(alignment - 1));

        private int[] WireframeIndices(int[] triangles)
        {
            if (_drawingList && _wireframeIndices.TryGetValue(triangles, out var cached)) return cached;
            var edges = new int[checked(triangles.Length * 2)];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int at = i * 2;
                edges[at] = triangles[i]; edges[at + 1] = triangles[i + 1];
                edges[at + 2] = triangles[i + 1]; edges[at + 3] = triangles[i + 2];
                edges[at + 4] = triangles[i + 2]; edges[at + 5] = triangles[i];
            }
            if (_drawingList) _wireframeIndices.Add(triangles, edges);
            return edges;
        }

        private NativeGeometry PrepareGeometry(float[] vertices, int[] indices, bool cache = true)
        {
            bool persistent = cache && _drawingList;
            if (!persistent)
                return PrepareGeometry((ReadOnlySpan<float>)vertices, (ReadOnlySpan<int>)indices);

            var key = (vertices, indices);
            if (_geometryCache.TryGetValue(key, out NativeGeometry? found)) return found;
            var geometry = new NativeGeometry();
            ulong vertexBytes = checked((ulong)vertices.Length * sizeof(float));
            ulong indexBytes = checked((ulong)indices.Length * sizeof(int));
            GrowBuffer(ref geometry.Vertex, ref geometry.VertexCapacity, vertexBytes, BufferUsage.Vertex);
            GrowBuffer(ref geometry.Index, ref geometry.IndexCapacity, indexBytes, BufferUsage.Index);
            fixed (float* ptr = vertices)
                _api.QueueWriteBuffer(_queue, geometry.Vertex, 0, ptr, (nuint)vertexBytes);
            fixed (int* ptr = indices)
                _api.QueueWriteBuffer(_queue, geometry.Index, 0, ptr, (nuint)indexBytes);
            geometry.VertexOffset = geometry.IndexOffset = 0;
            _geometryCache.Add(key, geometry);
            return geometry;
        }

        private GeometryArenaPage RentGeometryPage(ulong vertexBytes, ulong indexBytes,
            out ulong vertexOffset, out ulong indexOffset)
        {
            while (true)
            {
                if (_geometryArenaPage == _geometryArena.Count)
                    _geometryArena.Add(new GeometryArenaPage());
                GeometryArenaPage page = _geometryArena[_geometryArenaPage];

                if (page.Vertex == 0)
                {
                    page.VertexCapacity = Math.Max(GeometryVertexPageBytes, AlignGeometry(vertexBytes, 16));
                    WgpuBuffer* vertex = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
                    {
                        Size = page.VertexCapacity,
                        Usage = BufferUsage.Vertex | BufferUsage.CopyDst
                    });
                    if (vertex == null)
                        throw new InvalidOperationException(
                            $"Could not allocate {page.VertexCapacity} byte WebGPU vertex arena.");
                    page.Vertex = (nint)vertex;
                    page.VertexStaging = new byte[checked((int)page.VertexCapacity)];
                }
                if (page.Index == 0)
                {
                    page.IndexCapacity = Math.Max(GeometryIndexPageBytes, AlignGeometry(indexBytes, 4));
                    WgpuBuffer* index = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
                    {
                        Size = page.IndexCapacity,
                        Usage = BufferUsage.Index | BufferUsage.CopyDst
                    });
                    if (index == null)
                        throw new InvalidOperationException(
                            $"Could not allocate {page.IndexCapacity} byte WebGPU index arena.");
                    page.Index = (nint)index;
                    page.IndexStaging = new byte[checked((int)page.IndexCapacity)];
                }

                vertexOffset = AlignGeometry(page.VertexCursor, 16);
                indexOffset = AlignGeometry(page.IndexCursor, 4);
                if (vertexOffset + vertexBytes <= page.VertexCapacity
                    && indexOffset + indexBytes <= page.IndexCapacity)
                {
                    page.VertexCursor = vertexOffset + vertexBytes;
                    page.IndexCursor = indexOffset + indexBytes;
                    return page;
                }

                _geometryArenaPage++;
            }
        }

        private NativeGeometry PrepareGeometry(ReadOnlySpan<float> vertices, ReadOnlySpan<int> indices)
        {
            if (_transientGeometryCursor == _transientGeometry.Count) _transientGeometry.Add(new());
            NativeGeometry geometry = _transientGeometry[_transientGeometryCursor++];
            ulong vertexBytes = checked((ulong)vertices.Length * sizeof(float));
            ulong indexBytes = checked((ulong)indices.Length * sizeof(int));
            GeometryArenaPage page = RentGeometryPage(vertexBytes, indexBytes,
                out ulong vertexOffset, out ulong indexOffset);

            geometry.Vertex = (WgpuBuffer*)page.Vertex;
            geometry.Index = (WgpuBuffer*)page.Index;
            geometry.VertexCapacity = vertexBytes;
            geometry.IndexCapacity = indexBytes;
            geometry.VertexOffset = vertexOffset;
            geometry.IndexOffset = indexOffset;

            if (vertexBytes > 0)
            {
                fixed (float* ptr = vertices)
                {
                    new ReadOnlySpan<byte>(ptr, checked((int)vertexBytes)).CopyTo(
                        page.VertexStaging.AsSpan(checked((int)vertexOffset), checked((int)vertexBytes)));
                }
                page.VertexDirtyStart = Math.Min(page.VertexDirtyStart, vertexOffset);
                page.VertexDirtyEnd = Math.Max(page.VertexDirtyEnd, vertexOffset + vertexBytes);
            }
            if (indexBytes > 0)
            {
                fixed (int* ptr = indices)
                {
                    new ReadOnlySpan<byte>(ptr, checked((int)indexBytes)).CopyTo(
                        page.IndexStaging.AsSpan(checked((int)indexOffset), checked((int)indexBytes)));
                }
                page.IndexDirtyStart = Math.Min(page.IndexDirtyStart, indexOffset);
                page.IndexDirtyEnd = Math.Max(page.IndexDirtyEnd, indexOffset + indexBytes);
            }
            return geometry;
        }

        private void FlushGeometryWrites()
        {
            foreach (GeometryArenaPage page in _geometryArena)
            {
                if (page.Vertex != 0 && page.VertexDirtyStart != ulong.MaxValue
                    && page.VertexDirtyEnd > page.VertexDirtyStart)
                {
                    ulong start = page.VertexDirtyStart;
                    ulong size = page.VertexDirtyEnd - start;
                    fixed (byte* ptr = page.VertexStaging)
                        WriteProfiledBuffer((WgpuBuffer*)page.Vertex, start,
                            ptr + checked((int)start), checked((nuint)size));
                    page.VertexDirtyStart = ulong.MaxValue;
                    page.VertexDirtyEnd = 0;
                }
                if (page.Index != 0 && page.IndexDirtyStart != ulong.MaxValue
                    && page.IndexDirtyEnd > page.IndexDirtyStart)
                {
                    ulong start = page.IndexDirtyStart;
                    ulong size = page.IndexDirtyEnd - start;
                    fixed (byte* ptr = page.IndexStaging)
                        WriteProfiledBuffer((WgpuBuffer*)page.Index, start,
                            ptr + checked((int)start), checked((nuint)size));
                    page.IndexDirtyStart = ulong.MaxValue;
                    page.IndexDirtyEnd = 0;
                }
            }
        }

        private void ResetGeometryArena()
        {
            foreach (GeometryArenaPage page in _geometryArena)
            {
                page.VertexCursor = page.IndexCursor = 0;
                page.VertexDirtyStart = page.IndexDirtyStart = ulong.MaxValue;
                page.VertexDirtyEnd = page.IndexDirtyEnd = 0;
            }
            _geometryArenaPage = 0;
            _transientGeometryCursor = 0;
        }

        private void GrowBuffer(ref WgpuBuffer* buffer, ref ulong capacity, ulong size, BufferUsage usage)
        {
            if (capacity >= size) return;
            if (buffer != null) _api.BufferRelease(buffer);
            capacity = Math.Max(size, capacity * 2);
            buffer = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = capacity, Usage = usage | BufferUsage.CopyDst
            });
        }

        private void ReleaseGeometry(NativeGeometry geometry)
        {
            if (geometry.Vertex != null) _api.BufferRelease(geometry.Vertex);
            if (geometry.Index != null) _api.BufferRelease(geometry.Index);
            geometry.Vertex = geometry.Index = null;
            geometry.VertexCapacity = geometry.IndexCapacity = 0;
            geometry.VertexOffset = geometry.IndexOffset = 0;
        }

        private void ReleaseListGeometry(int list)
        {
            if (!_lists.TryGetValue(list, out GeometryList? geometry)) return;
            if (_wireframeIndices.Remove(geometry.Triangles, out var edges)
                && _geometryCache.Remove((geometry.Vertices, edges), out var wireframe))
                ReleaseGeometry(wireframe);
            if (_geometryCache.Remove((geometry.Vertices, geometry.Triangles), out NativeGeometry? triangles))
                ReleaseGeometry(triangles);
            if (_geometryCache.Remove((geometry.Vertices, geometry.Lines), out NativeGeometry? lines))
                ReleaseGeometry(lines);
        }

        private void DisposeGeometry()
        {
            foreach (NativeGeometry geometry in _geometryCache.Values) ReleaseGeometry(geometry);
            _geometryCache.Clear();
            _wireframeIndices.Clear();
            _transientGeometry.Clear();
            foreach (GeometryArenaPage page in _geometryArena)
            {
                if (page.Vertex != 0) _api.BufferRelease((WgpuBuffer*)page.Vertex);
                if (page.Index != 0) _api.BufferRelease((WgpuBuffer*)page.Index);
            }
            _geometryArena.Clear();
        }
    }
}
#endif
