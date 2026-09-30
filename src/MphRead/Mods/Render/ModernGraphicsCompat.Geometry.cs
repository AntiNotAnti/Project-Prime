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
        }

        // Display lists own immutable arrays. Transient draws rent distinct buffers
        // until the frame is submitted, so queued draws retain their vertex data.
        private readonly Dictionary<(float[], int[]), NativeGeometry> _geometryCache = new();
        private readonly List<NativeGeometry> _transientGeometry = new();
        private int _transientGeometryCursor;
        private bool _drawingList;
        private bool _wireframe;
        private readonly Dictionary<int[], int[]> _wireframeIndices = new();

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
            var key = (vertices, indices);
            if (persistent && _geometryCache.TryGetValue(key, out NativeGeometry? found)) return found;
            NativeGeometry geometry;
            if (persistent) geometry = new();
            else
            {
                if (_transientGeometryCursor == _transientGeometry.Count) _transientGeometry.Add(new());
                geometry = _transientGeometry[_transientGeometryCursor++];
            }
            ulong vertexBytes = checked((ulong)vertices.Length * sizeof(float));
            ulong indexBytes = checked((ulong)indices.Length * sizeof(int));
            GrowBuffer(ref geometry.Vertex, ref geometry.VertexCapacity, vertexBytes, BufferUsage.Vertex);
            GrowBuffer(ref geometry.Index, ref geometry.IndexCapacity, indexBytes, BufferUsage.Index);
            fixed (float* ptr = vertices)
                _api.QueueWriteBuffer(_queue, geometry.Vertex, 0, ptr, (nuint)vertexBytes);
            fixed (int* ptr = indices)
                _api.QueueWriteBuffer(_queue, geometry.Index, 0, ptr, (nuint)indexBytes);
            if (persistent) _geometryCache.Add(key, geometry);
            return geometry;
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
            foreach (var geometry in _transientGeometry) ReleaseGeometry(geometry);
            _transientGeometry.Clear();
        }
    }
}
#endif
