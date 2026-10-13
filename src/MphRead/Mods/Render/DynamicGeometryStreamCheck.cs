#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class DynamicGeometryStreamCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[dynamicstreamcheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        var stream = new DynamicGeometryStream(1024);
        var batch = new LegacyGeometryBatch();
        void Add(PrimitiveType mode, int count)
        {
            batch.Begin(mode);
            for (int i = 0; i < count; i++)
                batch.AddVertex(Vector3.Zero, Vector4.One, Vector3.UnitZ, Vector3.Zero, true);
            batch.End();
        }
        var cases = new (PrimitiveType Mode, int Count, int[] Expected)[]
        {
            (PrimitiveType.Triangles, 4, new[] {0,1,2}),
            (PrimitiveType.Quads, 5, new[] {0,1,2,0,2,3}),
            (PrimitiveType.TriangleStrip, 5, new[] {0,1,2,2,1,3,2,3,4}),
            (PrimitiveType.QuadStrip, 6, new[] {0,1,3,0,3,2,2,3,5,2,5,4}),
            (PrimitiveType.TriangleFan, 4, new[] {0,1,2,0,2,3}),
            (PrimitiveType.Lines, 5, new[] {0,1,2,3}),
            (PrimitiveType.LineStrip, 4, new[] {0,1,1,2,2,3}),
            (PrimitiveType.LineLoop, 3, new[] {0,1,1,2,2,0})
        };
        foreach (var item in cases)
        {
            batch.Clear(); Add(item.Mode, item.Count);
            Check(stream.Prepare(batch).SequenceEqual(item.Expected), item.Mode + " exact indices and winding");
            batch.Clear(); Add(item.Mode, 0);
            Check(stream.Prepare(batch).IsEmpty, item.Mode + " empty primitive");
        }
        batch.Clear(); Add(PrimitiveType.Lines, 2); Add(PrimitiveType.Triangles, 3);
        Check(stream.Prepare(batch).SequenceEqual(new[] {2,3,4,0,1}), "mixed triangles precede lines with vertex offsets");
        Check(DynamicGeometryStream.NeedsPrimitiveBarrier(PrimitiveType.Lines, 3, 0)
            && DynamicGeometryStream.NeedsPrimitiveBarrier(PrimitiveType.TriangleStrip, 0, 2)
            && !DynamicGeometryStream.NeedsPrimitiveBarrier(PrimitiveType.Quads, 3, 0)
            && !DynamicGeometryStream.NeedsPrimitiveBarrier(PrimitiveType.LineLoop, 0, 2),
            "deferred batches preserve triangle/line ordering barriers");
        batch.Clear(); Add(PrimitiveType.TriangleStrip, 4); Add(PrimitiveType.TriangleStrip, 4);
        Check(stream.Prepare(batch).SequenceEqual(new[] {0,1,2,2,1,3,4,5,6,6,5,7}),
            "adjacent strips merge without connecting primitives or reversing winding");
        batch.Clear(); Add(PrimitiveType.TriangleFan, 4); Add(PrimitiveType.TriangleFan, 4);
        Check(stream.Prepare(batch).SequenceEqual(new[] {0,1,2,0,2,3,4,5,6,4,6,7}),
            "adjacent fans retain independent centers");
        batch.Clear(); Add(PrimitiveType.LineLoop, 3); Add(PrimitiveType.LineLoop, 3);
        Check(stream.Prepare(batch).SequenceEqual(new[] {0,1,1,2,2,0,3,4,4,5,5,3}),
            "adjacent line loops close independently");
        foreach (PrimitiveType lineMode in new[] { PrimitiveType.Lines, PrimitiveType.LineLoop, PrimitiveType.LineStrip })
            Check(DynamicGeometryStream.NeedsPrimitiveBarrier(lineMode, 3, 0)
                && !DynamicGeometryStream.NeedsPrimitiveBarrier(lineMode, 0, 2),
                lineMode + " cannot cross pending triangles");
        // Deferred dynamic vertices carry a snapshot of current color, including
        // alpha==0's lighting sentinel. Later Color calls cannot recolor them.
        batch.Clear();
        Vector4 firstColor = new(1f, 0.25f, 0.5f, 0f);
        batch.Begin(PrimitiveType.Triangles);
        for (int i = 0; i < 3; i++) batch.AddVertex(Vector3.Zero, firstColor, Vector3.UnitZ, Vector3.Zero, true);
        batch.End();
        Vector4 secondColor = new(0f, 1f, 0.25f, 0.5f);
        batch.Begin(PrimitiveType.Triangles);
        for (int i = 0; i < 3; i++) batch.AddVertex(Vector3.One, secondColor, Vector3.UnitX, Vector3.One, true);
        batch.End();
        int second = 3 * LegacyGeometryBatch.FloatsPerVertex;
        Check(batch.Vertices[3] == firstColor.X && batch.Vertices[6] == 0f && batch.Vertices[13] == 1f
            && batch.Vertices[second + 3] == secondColor.X && batch.Vertices[second + 6] == 0.5f
            && batch.Vertices[second + 13] == 1f && batch.Vertices[second + 7] == 1f,
            "deferred color alpha and normal are immutable per-vertex snapshots");
        Check(stream.Prepare(batch).SequenceEqual(new[] {0,1,2,3,4,5}),
            "same-state triangles preserve submission sequence");
        batch.Clear();
        Vector4 inheritedAtVertex = new(0.1f, 0.2f, 0.3f, 1f);
        Vector4 explicitColor = new(0.8f, 0.7f, 0.6f, 0f);
        Vector4 colorAtEnd = new(0.4f, 0.5f, 0.6f, 0.25f);
        batch.Begin(PrimitiveType.Triangles);
        batch.AddVertex(Vector3.Zero, inheritedAtVertex, Vector3.UnitZ, Vector3.Zero, false);
        batch.AddVertex(Vector3.One, explicitColor, Vector3.UnitZ, Vector3.Zero, true);
        batch.AddVertex(Vector3.UnitX, explicitColor, Vector3.UnitZ, Vector3.Zero, true);
        batch.End();
        // Old GLES imm_color is sampled at End even when a later Color call
        // happens after inherited vertices were emitted in the same primitive.
        batch.BakeInheritedColor(0, colorAtEnd);
        Check(batch.Vertices[3] == colorAtEnd.X && batch.Vertices[4] == colorAtEnd.Y
            && batch.Vertices[5] == colorAtEnd.Z && batch.Vertices[6] == colorAtEnd.W
            && batch.Vertices[13] == 1f,
            "inherited vertices resolve final primitive color including alpha");
        int explicitOffset = LegacyGeometryBatch.FloatsPerVertex;
        Check(batch.Vertices[explicitOffset + 3] == explicitColor.X
            && batch.Vertices[explicitOffset + 6] == explicitColor.W
            && batch.Vertices[explicitOffset + 13] == 1f,
            "inherited-color baking preserves explicitly colored vertices and lighting sentinel");
        int nextFirst = batch.VertexCount;
        batch.Begin(PrimitiveType.Triangles);
        for (int i = 0; i < 3; i++) batch.AddVertex(Vector3.One, inheritedAtVertex, Vector3.UnitY, Vector3.One, false);
        batch.End();
        Vector4 nextEndColor = new(0.9f, 0.2f, 0.1f, 0f);
        batch.BakeInheritedColor(nextFirst, nextEndColor);
        int nextOffset = nextFirst * LegacyGeometryBatch.FloatsPerVertex;
        Check(batch.Vertices[3] == colorAtEnd.X && batch.Vertices[6] == colorAtEnd.W
            && batch.Vertices[nextOffset + 3] == nextEndColor.X
            && batch.Vertices[nextOffset + 6] == 0f && batch.Vertices[nextOffset + 13] == 1f,
            "later primitive baking never recolors queued earlier primitives");
        batch.BakeInheritedColor(batch.VertexCount, Vector4.Zero);
        Check(batch.Vertices[nextOffset + 3] == nextEndColor.X,
            "empty inherited-color range leaves queued vertices unchanged");
        stream.Prepare(batch);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) stream.Prepare(batch);
        Check(GC.GetAllocatedBytesForCurrentThread() == start, "warmed scratch reuse allocates no arrays");
        var bounded = new DynamicGeometryStream(16);
        Check(bounded.Prepare(batch).SequenceEqual(batch.BuildIndexArray()) && bounded.ResidentBytes == 0,
            "oversize fallback preserves indices without retaining oversized memory");
        batch.Clear(); Add(PrimitiveType.Lines, 2);
        Check(bounded.Prepare(batch).SequenceEqual(new[] {0,1}) && bounded.ResidentBytes <= 16,
            "bounded scratch remains usable after fallback");
        bounded.Reset();
        Check(bounded.ResidentBytes == 0 && bounded.Prepare(batch).SequenceEqual(new[] {0,1}),
            "reset releases scratch and rebuilds correctly");
        bool rejected = false;
        try { batch.CopyIndicesTo(Span<int>.Empty); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "undersized copy rejected");
        return failures == 0 ? 0 : 1;
    }
}
#endif
