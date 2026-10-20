using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Render.Characters;
using M = OpenTK.Mathematics.Matrix4;
using V = OpenTK.Mathematics.Vector3;
using NV = System.Numerics.Vector3;
using UV = System.Numerics.Vector2;
using W = System.Numerics.Vector4;

// Invented source identities, weights and geometry; no source assets or leases.
internal static class GeneratedCannonAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        var source = Fixture(); string before = Fingerprint(source);
        var cut = CharacterSourceCannonRecipe.SelectGeneratedCut(source);
        Check(cut.Selected.Length == 6 && cut.Selected[2].SequenceEqual(new[] { 44 })
            && cut.Selected[5].SequenceEqual(new[] { 7, 1200 }) && cut.Selected.Where((_, i) => i is not (2 or 5)).All(a => a.Length == 0),
            "literal global source IDs selected by original material across physical batches");
        Check(cut.Primitives.Count == 3 && cut.Primitives.Sum(p => p.Indices.Length) == 9,
            "only three complete source triangles survive; empty material batch omitted");
        var first = cut.Primitives.Single(p => p.SourceTriangleIds!.SequenceEqual(new[] { 7 }));
        Check(first.SourcePrimitiveOrdinal == 8 && first.SourceMaterialOrdinal == 5
            && first.Indices.SequenceEqual(new uint[] { 2, 0, 1 }),
            "primitive ordinal8/material5/ID7 remain distinct and exact corner winding survives");
        Check(first.Vertices[0].Weights.X == .000001f && first.Vertices[0].Weights.Y > .99f,
            "ANY strictly positive descendant influence keeps a vertex despite dominant outside influence");
        Check(!cut.Selected.SelectMany(a => a).Contains(404) && !cut.Selected.SelectMany(a => a).Contains(901),
            "one outside-only corner rejects a triangle and zero descendant weight does not qualify");
        foreach (var p in cut.Primitives)
        {
            var original = source.Primitives.Single(q => p.SourceTriangleIds!.All(id => q.SourceTriangleIds!.Contains(id)));
            Check(p.SourcePrimitiveOrdinal == original.SourcePrimitiveOrdinal && p.SourceMaterialOrdinal == original.SourceMaterialOrdinal
                && ReferenceEquals(p.Vertices, original.Vertices) && ReferenceEquals(p.JointPalette, original.JointPalette)
                && !ReferenceEquals(p.Indices, original.Indices) && !ReferenceEquals(p.SourceTriangleIds, original.SourceTriangleIds),
                "cut reuses captured attributes/palette unchanged and owns copied index/ID subsets");
        }
        Check(Fingerprint(source) == before, "selection leaves all source geometry, influences, identities and hierarchy unchanged");
        Throws<NotSupportedException>(() => ((IList<CharacterWeightedPrimitive>)cut.Primitives).Clear(), "retained primitive collection is readonly");
        var reordered = Fixture(reordered: true); var recut = CharacterSourceCannonRecipe.SelectGeneratedCut(reordered);
        Check(cut.Selected.Zip(recut.Selected).All(p => p.First.SequenceEqual(p.Second)),
            "reordering nodes, full joint order, local palettes and physical batches preserves grouped global IDs");
        Check(recut.Primitives.Single(p => p.SourceTriangleIds!.Contains(7)).Indices.SequenceEqual(new uint[] { 2, 0, 1 }),
            "reordered batches still retain exact within-triangle source order");
        var basis = CharacterSourceCannonRecipe.GeneratedBasis(source, source.SourceRig!.Nodes.Select(n => n.Local).ToArray());
        var otherBasis = CharacterSourceCannonRecipe.GeneratedBasis(reordered, reordered.SourceRig!.Nodes.Select(n => n.Local).ToArray());
        Check(basis == M.CreateTranslation(-1, -2, -5) && otherBasis == basis,
            "named locator lookup plus complete inverse-bind evaluation is independent of node/joint array positions");
        source.Primitives[0].SourceTriangleIds![0] = 9999; source.Primitives[0].Indices[0] = 8;
        Check(cut.Selected[5].SequenceEqual(new[] { 7, 1200 }) && first.SourceTriangleIds!.SequenceEqual(new[] { 7 })
            && first.Indices.SequenceEqual(new uint[] { 2, 0, 1 }), "returned source-ID groups and index subsets detach from later caller mutations");
        Frames(); Failures();
        Console.WriteLine($"PASS: generated cannon source IDs, descendant cut and fixed frame ({_checks} checks)");
    }

    private static void Frames()
    {
        M forward = CharacterSourceCannonRecipe.SourceFrame(new(1, 2, 3), new(1, 2, 5));
        Check(forward == M.CreateTranslation(-1, -2, -5), "literal +Z barrel oracle has identity axes and muzzle translation");
        M right = CharacterSourceCannonRecipe.SourceFrame(V.Zero, V.UnitX);
        M expected = new(0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, -1, 1);
        Check(right == expected && V.TransformPosition(V.UnitX, right) == V.Zero
            && V.TransformPosition(V.Zero, right) == -V.UnitZ && V.TransformPosition(V.UnitX + V.UnitY, right) == V.UnitY,
            "literal +X barrel oracle preserves right-handed axes, source up and muzzle origin");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SourceFrame(V.One, V.One), "coincident locators");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SourceFrame(V.Zero, V.UnitY), "vertical barrel has ambiguous source up");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SourceFrame(new(invalid, 0, 0), V.UnitZ), "nonfinite source frame");
        var outside = Fixture(tipOutside: true);
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(outside, outside.SourceRig!.Nodes.Select(n => n.Local).ToArray()), "muzzle must descend from named elbow");
        var model = Fixture(); Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(model, new M[1]), "complete pose extent");
    }

    private static void Failures()
    {
        void Bad(Func<CharacterWeightedPrimitive, CharacterWeightedPrimitive> edit, string why)
        { var s = Fixture(); var parts = s.Primitives.ToArray(); parts[0] = edit(parts[0]); Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(s with { Primitives = parts }), why); }
        void BadVertex(Func<CharacterWeightedVertex, CharacterWeightedVertex> edit, string why)
        { Bad(p => { var v = p.Vertices.ToArray(); v[0] = edit(v[0]); return p with { Vertices = v }; }, why); }
        Bad(p => p with { SourceMaterialOrdinal = -1 }, "negative original material");
        Bad(p => p with { SourceMaterialOrdinal = 256 }, "original material budget");
        Bad(p => p with { SourceTriangleIds = null }, "missing global source IDs");
        Bad(p => p with { SourceTriangleIds = new[] { 7, 404 } }, "source ID extent differs from triangle count");
        Bad(p => p with { SourceTriangleIds = new[] { -1, 404, 901 } }, "negative global ID");
        Bad(p => p with { SourceTriangleIds = new[] { 7, 7, 901 } }, "duplicate ID even on dropped triangle");
        Bad(p => p with { SourceTriangleIds = new[] { 1200, 404, 901 } }, "duplicate ID across physical partitions");
        Bad(p => p with { Indices = p.Indices[..^1] }, "partial source triangle");
        Bad(p => p with { Indices = new uint[] { 99, 0, 1, 3, 5, 4, 6, 7, 8 } }, "source index extent");
        Bad(p => p with { JointPalette = null }, "missing local palette");
        Bad(p => p with { JointPalette = Array.Empty<int>() }, "empty local palette");
        Bad(p => p with { JointPalette = Enumerable.Range(0, 33).ToArray() }, "local palette budget");
        Bad(p => p with { JointPalette = new[] { 0, 0, 2, 3 } }, "duplicate full joint in palette");
        Bad(p => p with { JointPalette = new[] { -1, 0, 2, 3 } }, "negative full joint");
        Bad(p => p with { JointPalette = new[] { 5, 0, 2, 3 } }, "full joint outside complete rig");
        BadVertex(v => v with { PackedJoints = -1 }, "negative packed local slots");
        BadVertex(v => v with { PackedJoints = 1 << 20 }, "bits beyond four5-bit local slots");
        BadVertex(v => v with { PackedJoints = 31 }, "positive local slot outside palette");
        BadVertex(v => v with { Weights = W.Zero }, "zero positive weight sum");
        BadVertex(v => v with { Weights = new(.5f, 0, 0, 0) }, "nonunit source weights");
        foreach (float invalid in new[] { -.1f, float.NaN, float.PositiveInfinity })
            BadVertex(v => v with { Weights = new(invalid, 1, 0, 0) }, "invalid positive source weight");
        BadVertex(v => v with { Weights = new(.5f, .5f, 0, 0), PackedJoints = CharacterWeightedModelLoader.PackJoints(1, 1, 0, 0) }, "duplicate positive joint influence");
        var source = Fixture(); Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(source with { SourceRig = null }), "missing whole hierarchy");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(Fixture(wrongElbow: true)), "same elbow suffix with wrong source ID cannot qualify");
        Throws<InvalidDataException>(() => Fixture(cycle: true), "malformed ancestry rejected by actual rig reader");
    }

    private static CharacterWeightedModelData Fixture(bool reordered = false, bool tipOutside = false, bool wrongElbow = false, bool cycle = false)
    {
        string[] order = reordered ? new[] { "mesh", "child", "elbow", "outside", "root", "tip" } : new[] { "outside", "tip", "mesh", "root", "elbow", "child" };
        var index = order.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        var parent = new Dictionary<string, string?> { ["root"] = cycle ? "child" : null, ["mesh"] = null, ["outside"] = "root", ["elbow"] = "root", ["tip"] = tipOutside ? "root" : "elbow", ["child"] = "elbow" };
        var names = new Dictionary<string, string> { ["root"] = "source_bone_0_root", ["mesh"] = "own_identity_mesh", ["outside"] = "source_bone_99_outside", ["elbow"] = wrongElbow ? "source_bone_99_R_elbow" : "source_bone_24_R_elbow", ["tip"] = "source_bone_32_GUN_Particle_LCTR", ["child"] = "source_bone_25_child" };
        var local = new Dictionary<string, V> { ["root"] = V.Zero, ["mesh"] = V.Zero, ["outside"] = new(-2, 0, 0), ["elbow"] = new(1, 2, 3), ["tip"] = new(0, 0, 2), ["child"] = new(0, 1, 0) };
        var nodes = new JsonArray();
        foreach (string role in order)
        {
            V t = local[role]; var n = new JsonObject { ["name"] = names[role], ["translation"] = new JsonArray(t.X, t.Y, t.Z),
                ["children"] = new JsonArray(order.Where(c => parent[c] == role).Select(c => JsonValue.Create(index[c])).ToArray()) };
            if (role == "mesh") { n["mesh"] = 0; n["skin"] = 0; } nodes.Add(n);
        }
        string[] jointOrder = reordered ? new[] { "root", "tip", "outside", "child", "elbow" } : new[] { "child", "outside", "elbow", "tip", "root" };
        using var json = JsonDocument.Parse(new JsonObject { ["nodes"] = nodes, ["joints"] = new JsonArray(jointOrder.Select(n => JsonValue.Create(index[n])).ToArray()) }.ToJsonString());
        var nodeElements = json.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var rig = CharacterSourceRig.Read(nodeElements, json.RootElement.GetProperty("joints").EnumerateArray().ToArray(), new() { nodeElements[index["mesh"]] });
        V Global(string role) => parent[role] is { } p ? local[role] + Global(p) : local[role];
        var joints = jointOrder.Select(role => new CharacterWeightedJoint(names[role], names[role], M.CreateTranslation(-Global(role)))).ToArray();
        var globalJoint = jointOrder.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        string[] palette = reordered ? new[] { "elbow", "tip", "child", "outside" } : new[] { "outside", "child", "tip", "elbow" };
        var slot = palette.Select((role, i) => (role, i)).ToDictionary(p => p.role, p => p.i);
        CharacterWeightedVertex Vertex(int tag, string a, float wa = 1, string b = "outside", float wb = 0) => new(new NV(tag, tag + 1, tag + 2), new NV(0, 1, 0), new UV(tag * .125f, tag * .25f), new W(wa, wb, 0, 0), CharacterWeightedModelLoader.PackJoints(slot[a], slot[b], 0, 0));
        CharacterWeightedPrimitive Part(int primitive, int material, int[] ids, CharacterWeightedVertex[] vertices, uint[] indices) => new("own_material_" + material, vertices, indices, JointPalette: palette.Select(r => globalJoint[r]).ToArray()) { SourcePrimitiveOrdinal = primitive, SourceMaterialOrdinal = material, SourceTriangleIds = ids };
        var a = Part(8, 5, new[] { 7, 404, 901 }, new[] { Vertex(0, "child", .000001f, "outside", .999999f), Vertex(1, "elbow"), Vertex(2, "tip"), Vertex(3, "child"), Vertex(4, "outside"), Vertex(5, "tip"), Vertex(6, "child", 0, "outside", 1), Vertex(7, "elbow"), Vertex(8, "tip") }, new uint[] { 2, 0, 1, 3, 5, 4, 6, 7, 8 });
        var b = Part(8, 5, new[] { 1200 }, new[] { Vertex(9, "child"), Vertex(10, "elbow"), Vertex(11, "tip") }, new uint[] { 0, 1, 2 });
        var c = Part(3, 2, new[] { 44 }, new[] { Vertex(12, "tip"), Vertex(13, "tip"), Vertex(14, "tip") }, new uint[] { 0, 1, 2 });
        var d = Part(17, 0, new[] { 55 }, new[] { Vertex(15, "outside"), Vertex(16, "outside"), Vertex(17, "outside") }, new uint[] { 0, 1, 2 });
        return new(reordered ? new[] { d, c, b, a } : new[] { a, b, c, d }, joints, rig);
    }
    private static string Fingerprint(CharacterWeightedModelData source)
    {
        using var bytes = new MemoryStream(); using var w = new BinaryWriter(bytes);
        foreach (var p in source.Primitives)
        {
            w.Write(p.SourcePrimitiveOrdinal); w.Write(p.SourceMaterialOrdinal); foreach (int id in p.SourceTriangleIds!) w.Write(id); foreach (int j in p.JointPalette!) w.Write(j); foreach (uint i in p.Indices) w.Write(i);
            foreach (var v in p.Vertices) { foreach (float x in new[] { v.Position.X, v.Position.Y, v.Position.Z, v.Normal.X, v.Normal.Y, v.Normal.Z, v.Texcoord.X, v.Texcoord.Y, v.Weights.X, v.Weights.Y, v.Weights.Z, v.Weights.W }) w.Write(x); w.Write(v.PackedJoints); }
        }
        foreach (var n in source.SourceRig!.Nodes) { w.Write(n.Name); w.Write(n.Parent); }
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); _checks++; }
    private static void Throws<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
}
