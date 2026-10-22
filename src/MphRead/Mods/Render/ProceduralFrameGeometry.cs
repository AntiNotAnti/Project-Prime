using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Geometry only: material, pose, lights and stencil are still applied at each
// original submission. A frame's immutable points are packed once for all passes.
internal sealed class ProceduralFrameGeometry
{
    internal const int MaxVertices = 16384;
    internal const int FloatsPerVertex = 6;
    internal readonly record struct Range(PrimitiveType Mode, int First, int Count, Vector3 LastTexcoord);
    internal float[] Vertices { get; } = new float[MaxVertices * FloatsPerVertex];
    private readonly Dictionary<RenderItem, Range> _ranges = new();
    internal int VertexCount { get; private set; }
    internal void Clear() { _ranges.Clear(); VertexCount = 0; }
    internal bool TryGet(RenderItem item, out Range range) => _ranges.TryGetValue(item, out range);

    internal bool Add(RenderItem item)
    {
        if (_ranges.ContainsKey(item)) return true;
        int count;
        PrimitiveType mode;
        switch (item.Type)
        {
        case RenderItemType.Particle: count = 4; mode = PrimitiveType.Quads; break;
        case RenderItemType.TrailSingle: count = 4; mode = PrimitiveType.QuadStrip; break;
        case RenderItemType.TrailMulti:
            if (item.ItemCount < 8 || item.ItemCount % 2 != 0) return false;
            count = item.ItemCount / 2; mode = PrimitiveType.QuadStrip; break;
        case RenderItemType.TrailStack:
            if (item.ItemCount <= 0 || item.ItemCount > MaxVertices / 4) return false;
            count = item.ItemCount * 4; mode = PrimitiveType.Quads; break;
        default: return false;
        }
        if (count > MaxVertices - VertexCount || item.Points.Length / 2 < count) return false;
        int first = VertexCount;
        Vector3 uv = default;
        for (int i = 0; i < count; i++)
        {
            uv = item.Points[i * 2];
            if (item.Type == RenderItemType.Particle)
                uv = new Vector3(uv.X * item.ScaleS, uv.Y * item.ScaleT, 0);
            Vector3 vertex = item.Points[i * 2 + 1];
            int at = (first + i) * FloatsPerVertex;
            Vertices[at] = vertex.X; Vertices[at + 1] = vertex.Y; Vertices[at + 2] = vertex.Z;
            Vertices[at + 3] = uv.X; Vertices[at + 4] = uv.Y; Vertices[at + 5] = uv.Z;
        }
        VertexCount += count;
        _ranges.Add(item, new Range(mode, first, count, uv));
        return true;
    }
}
