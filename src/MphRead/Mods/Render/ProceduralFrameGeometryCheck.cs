#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class ProceduralFrameGeometryCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[proceduralgeometrycheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        Vector3[] Points(int count)
        {
            var points = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                points[i * 2] = new Vector3(i + .25f, i + .5f, i + 32f);
                points[i * 2 + 1] = new Vector3(i + 10f, i + 20f, i + 30f);
            }
            return points;
        }
        bool Exact(ProceduralFrameGeometry geometry, RenderItem item, ProceduralFrameGeometry.Range range, bool particle)
        {
            for (int i = 0; i < range.Count; i++)
            {
                int at = (range.First + i) * ProceduralFrameGeometry.FloatsPerVertex;
                Vector3 position = item.Points[i * 2 + 1], uv = item.Points[i * 2];
                if (geometry.Vertices[at] != position.X || geometry.Vertices[at + 1] != position.Y
                    || geometry.Vertices[at + 2] != position.Z
                    || geometry.Vertices[at + 3] != uv.X * (particle ? item.ScaleS : 1)
                    || geometry.Vertices[at + 4] != uv.Y * (particle ? item.ScaleT : 1)
                    || geometry.Vertices[at + 5] != (particle ? 0 : uv.Z)) return false;
            }
            return true;
        }
        foreach (var type in new[] { RenderItemType.Particle, RenderItemType.TrailSingle,
            RenderItemType.TrailMulti, RenderItemType.TrailStack })
        {
            var coverage = new RenderItem { Type = type };
            Check(DepthCoveragePreparation.CanUse(coverage, true, false, true)
                && !DepthCoveragePreparation.CanUse(coverage, true, false)
                && !DepthCoveragePreparation.CanUse(coverage, false, false, true)
                && !DepthCoveragePreparation.CanUse(coverage, true, true, true),
                type + " coverage preparation requires enabled policy and alpha-only pass");
            coverage.OverrideColor = Vector4.One;
            Check(!DepthCoveragePreparation.CanUse(coverage, true, false, true),
                type + " override retains full preparation");
        }
        var geometry = new ProceduralFrameGeometry();
        var particle = new RenderItem { Type = RenderItemType.Particle, Points = Points(4), ScaleS = 2, ScaleT = .5f };
        Check(geometry.Add(particle) && geometry.TryGet(particle, out var particleRange)
            && particleRange.Mode == PrimitiveType.Quads && particleRange.First == 0 && particleRange.Count == 4
            && Exact(geometry, particle, particleRange, true)
            && particleRange.LastTexcoord == new Vector3(6.5f, 1.75f, 0),
            "particle packs native vertex sequence with UV scales and z zero");
        foreach (var entry in new[] {
            (RenderItemType.TrailSingle, 4, 0, PrimitiveType.QuadStrip),
            (RenderItemType.TrailMulti, 6, 12, PrimitiveType.QuadStrip),
            (RenderItemType.TrailStack, 8, 2, PrimitiveType.Quads) })
        {
            var item = new RenderItem { Type = entry.Item1, Points = Points(entry.Item2), ItemCount = entry.Item3,
                ScaleS = 99, ScaleT = 88 };
            int first = geometry.VertexCount;
            Check(geometry.Add(item) && geometry.TryGet(item, out var range)
                && range.Mode == entry.Item4 && range.First == first && range.Count == entry.Item2
                && Exact(geometry, item, range, false) && range.LastTexcoord == item.Points[(entry.Item2 - 1) * 2],
                entry.Item1 + " preserves native Quads/QuadStrip order and three-component UV without particle scaling");
        }
        int beforeDedup = geometry.VertexCount;
        float originalPosition = geometry.Vertices[0];
        particle.Points[1] = new Vector3(99);
        Check(geometry.Add(particle) && geometry.VertexCount == beforeDedup && geometry.Vertices[0] == originalPosition,
            "shared item is packed once across passes without aliasing later point edits");
        geometry.Clear();
        Check(!geometry.TryGet(particle, out _) && geometry.VertexCount == 0
            && geometry.Add(particle) && geometry.Vertices[0] == 99,
            "next frame clears ranges and captures fresh current points");

        var maximum = new RenderItem { Type = RenderItemType.TrailStack,
            ItemCount = ProceduralFrameGeometry.MaxVertices / 4, Points = Points(ProceduralFrameGeometry.MaxVertices) };
        geometry.Clear();
        Check(geometry.Add(maximum) && geometry.VertexCount == ProceduralFrameGeometry.MaxVertices
            && geometry.TryGet(maximum, out var maximumRange) && maximumRange.Count == ProceduralFrameGeometry.MaxVertices
            && Exact(geometry, maximum, maximumRange, false),
            "exact 16384-vertex boundary is accepted without truncation");
        float firstMaximum = geometry.Vertices[0], lastMaximum = geometry.Vertices[^1];
        Check(!geometry.Add(particle) && geometry.VertexCount == ProceduralFrameGeometry.MaxVertices
            && !geometry.TryGet(particle, out _) && geometry.Vertices[0] == firstMaximum && geometry.Vertices[^1] == lastMaximum,
            "capacity overflow falls back without publishing ranges or modifying packed vertices");
        geometry.Clear();
        Array.Fill(geometry.Vertices, 73f);
        var malformed = new[] {
            new RenderItem { Type = RenderItemType.Particle, Points = Points(3) },
            new RenderItem { Type = RenderItemType.TrailSingle, Points = Points(3) },
            new RenderItem { Type = RenderItemType.TrailMulti, ItemCount = 6, Points = Points(3) },
            new RenderItem { Type = RenderItemType.TrailMulti, ItemCount = 9, Points = Points(5) },
            new RenderItem { Type = RenderItemType.TrailMulti, ItemCount = 12, Points = Points(5) },
            new RenderItem { Type = RenderItemType.TrailStack, ItemCount = 0, Points = Points(4) },
            new RenderItem { Type = RenderItemType.TrailStack, ItemCount = int.MaxValue, Points = Points(4) },
            new RenderItem { Type = RenderItemType.TrailStack, ItemCount = 2, Points = Points(7) },
            new RenderItem { Type = RenderItemType.Mesh, Points = Points(4) } };
        foreach (RenderItem item in malformed)
        {
            bool unchanged = !geometry.Add(item) && !geometry.TryGet(item, out _) && geometry.VertexCount == 0;
            foreach (float value in geometry.Vertices) unchanged &= value == 73f;
            Check(unchanged, item.Type + " malformed/unsupported geometry rejects before any partial write");
        }
        geometry.Add(particle);
        geometry.Clear();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            geometry.Add(particle); geometry.Add(particle); geometry.TryGet(particle, out _); geometry.Clear();
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == start, "warmed frame packing, deduplication and clear allocate nothing");
        bool batching = OpenGlOptimizationPolicy.Enabled("PROCEDURAL_BATCH");
        var world = new RetainedRenderWorld();
        var a = new RenderItem { Type = RenderItemType.Particle, RenderMode = RenderMode.Translucent,
            Alpha = .6f, Diffuse = Vector3.One, HasTexture = true, TextureBindingId = 11,
            TexcoordMatrix = Matrix4.Identity, Transform = Matrix4.Identity, EmissiveIntensity = 1 };
        var b = new RenderItem { Type = RenderItemType.Particle, RenderMode = RenderMode.Translucent,
            Alpha = .6f, Diffuse = Vector3.One, HasTexture = true, TextureBindingId = 11,
            TexcoordMatrix = Matrix4.Identity, Transform = Matrix4.Identity, EmissiveIntensity = 1 };
        var items = new List<RenderItem> { a, b };
        void Capture() => world.Capture(Array.Empty<RenderItem>(), Array.Empty<RenderItem>(), items);
        bool Shared() => world.TranslucentBatches.Count == 1 && world.TranslucentBatches[0].Count == 2;
        Capture();
        Check(Shared() == batching, "adjacent particle material sharing follows procedural policy");
        b.Transform = Matrix4.CreateTranslation(2, 3, 4);
        b.LightInfo = new MphRead.LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector3.One);
        b.PolygonId = 27;
        Capture();
        Check(Shared() == batching && ReferenceEquals(world.Translucent[0].Item, a)
            && ReferenceEquals(world.Translucent[1].Item, b)
            && world.Translucent[1].Item.Transform == b.Transform
            && world.Translucent[1].Item.PolygonId == 27
            && world.Translucent[1].Item.LightInfo.Light1Vector == Vector3.UnitX,
            "shared material batching keeps current per-item pose light stencil and order");
        b.EmissiveIntensity = 2; Capture();
        Check(!Shared(), "emissive intensity change cannot inherit prior shared material");
        b.EmissiveIntensity = 1; b.Alpha = .7f; Capture();
        Check(!Shared(), "procedural alpha change splits shared material batch");
        b.Alpha = .6f; b.TexcoordMatrix = Matrix4.CreateTranslation(.25f, 0, 0); Capture();
        Check(!Shared(), "procedural texture matrix change splits shared material batch");
        b.TexcoordMatrix = Matrix4.Identity;
        foreach (RenderItemType type in new[] { RenderItemType.Particle, RenderItemType.TrailSingle,
            RenderItemType.TrailMulti, RenderItemType.TrailStack })
        {
            a.Type = b.Type = type; Capture();
            Check(Shared() == batching, type + " supports only policy-selected adjacent shared material");
            a.RetainedRoomOwned = b.RetainedRoomOwned = true;
            a.RenderMode = b.RenderMode = RenderMode.Normal;
            a.Alpha = b.Alpha = 1;
            a.PolygonId = b.PolygonId = 0;
            world.Capture(items, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(!world.Opaque[0].ReorderableOpaque && !world.Opaque[1].ReorderableOpaque
                && ReferenceEquals(world.Opaque[0].Item, a) && ReferenceEquals(world.Opaque[1].Item, b),
                type + " never becomes reorderable even when marked room-owned opaque");
            a.RetainedRoomOwned = b.RetainedRoomOwned = false;
            a.RenderMode = b.RenderMode = RenderMode.Translucent;
            a.Alpha = b.Alpha = .6f;
        }
        a.Type = b.Type = RenderItemType.Particle;
        a.OverrideColor = b.OverrideColor = Vector4.One; Capture();
        Check(!Shared() && !world.Translucent[0].Batchable && !world.Translucent[1].Batchable,
            "identical procedural overrides remain sharing barriers");
        a.OverrideColor = b.OverrideColor = null; b.ViewModel = true; Capture(); Check(!Shared(), "procedural viewmodel remains a sharing barrier");
        b.ViewModel = false; b.BillboardMode = BillboardMode.Sphere; Capture(); Check(!Shared(), "procedural billboard remains a sharing barrier");
        Check(!OpenGlOptimizationPolicy.Select(true, true, "1", true)
            && (!OpenGlOptimizationPolicy.ForceLegacy || !batching),
            "forced legacy disables procedural material batching even with explicit enable");
        var reuseA = new RenderItem { Type = RenderItemType.Particle, Transform = Matrix4.Identity,
            Diffuse = Vector3.One, LightInfo = new MphRead.LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector3.One) };
        var reuseB = new RenderItem { Type = RenderItemType.TrailStack, Transform = Matrix4.Identity,
            Diffuse = Vector3.One, LightInfo = reuseA.LightInfo, PolygonId = 27 };
        Check(ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB),
            "adjacent proven-material procedural objects may reuse identical state despite stencil-ID difference");
        void ReuseBoundary(Action<RenderItem> change, Action<RenderItem> restore, string name)
        {
            change(reuseB);
            Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), name + " prevents object state reuse");
            restore(reuseB);
            Check(ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), name + " restoration rebuilds exact reuse proof");
        }
        ReuseBoundary(item => item.Transform = Matrix4.CreateTranslation(.01f, 0, 0),
            item => item.Transform = Matrix4.Identity, "fresh changed pose");
        ReuseBoundary(item => item.Diffuse = Vector3.UnitX, item => item.Diffuse = Vector3.One, "diffuse color");
        ReuseBoundary(item => item.LightInfo = new MphRead.LightInfo(Vector3.UnitZ, Vector3.One, Vector3.UnitY, Vector3.One),
            item => item.LightInfo = reuseA.LightInfo, "light one vector");
        ReuseBoundary(item => item.LightInfo = new MphRead.LightInfo(Vector3.UnitX, Vector3.UnitX, Vector3.UnitY, Vector3.One),
            item => item.LightInfo = reuseA.LightInfo, "light one color");
        ReuseBoundary(item => item.LightInfo = new MphRead.LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitZ, Vector3.One),
            item => item.LightInfo = reuseA.LightInfo, "light two vector");
        ReuseBoundary(item => item.LightInfo = new MphRead.LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector3.UnitX),
            item => item.LightInfo = reuseA.LightInfo, "light two color");
        ReuseBoundary(item => item.ViewModel = true, item => item.ViewModel = false, "viewmodel");
        ReuseBoundary(item => item.BillboardMode = BillboardMode.Sphere, item => item.BillboardMode = BillboardMode.None, "sphere billboard");
        ReuseBoundary(item => item.BillboardMode = BillboardMode.Cylinder, item => item.BillboardMode = BillboardMode.None, "cylinder billboard");
        ReuseBoundary(item => item.WeightedSkinning = true, item => item.WeightedSkinning = false, "weighted skinning");
        ReuseBoundary(item => item.MatrixStackCount = 1, item => item.MatrixStackCount = 0, "matrix palette");
        ReuseBoundary(item => item.MatrixStackCount = -1, item => item.MatrixStackCount = 0, "invalid palette count");
        ReuseBoundary(item => item.Type = RenderItemType.Mesh, item => item.Type = RenderItemType.TrailStack, "nonprocedural mesh");
        foreach (RenderItemType type in new[] { RenderItemType.Particle, RenderItemType.TrailSingle,
            RenderItemType.TrailMulti, RenderItemType.TrailStack })
        {
            reuseA.Type = type;
            Check(ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), type + " permits exact zero-palette object reuse");
        }
        reuseA.MatrixStackCount = 1;
        Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), "first object palette also forbids reuse");
        reuseA.MatrixStackCount = 0; reuseA.ViewModel = true;
        Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), "first object viewmodel also forbids reuse");
        reuseA.ViewModel = false; reuseA.BillboardMode = BillboardMode.Sphere;
        Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), "first object billboard also forbids reuse");
        reuseA.BillboardMode = BillboardMode.None; reuseA.WeightedSkinning = true;
        Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), "first object weighted skinning also forbids reuse");
        reuseA.WeightedSkinning = false; reuseA.Type = RenderItemType.Mesh;
        Check(!ProceduralStateReuse.CanReuseObjectState(reuseA, reuseB), "first nonprocedural object also forbids reuse");
        return failures == 0 ? 0 : 1;
    }
}
#endif
