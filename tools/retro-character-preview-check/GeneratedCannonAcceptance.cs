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
        int normalChecks = _checks; Trooper();
        Console.WriteLine($"PASS: Trooper full35 source cut, local palettes and fixed frame ({_checks - normalChecks} checks)");
        int beforeFinal = _checks; FinalTrim();
        Console.WriteLine($"PASS: Final rawUV whole-triangle trim ({_checks - beforeFinal} checks)");
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

    private static void Trooper()
    {
        var source = Fixture(trooper: true); var reordered = Fixture(reordered: true, trooper: true);
        string original = Fingerprint(source), otherOriginal = Fingerprint(reordered);
        Check(source.Joints.Count == 35 && source.SourceRig!.Nodes.Count == 36 && reordered.Joints.Count == 35,
            "Trooper kernel fixture keeps all35 joints and an independent mesh node");
        Check(source.Primitives.All(p => p.JointPalette!.Any(j => j >= 32)),
            "physical palettes map5bit local slots to full joint ordinals beyond31");
        var cut = CharacterSourceCannonRecipe.SelectGeneratedCut(source, 23);
        var recut = CharacterSourceCannonRecipe.SelectGeneratedCut(reordered, 23);
        Check(cut.Selected[2].SequenceEqual(new[] { 44 }) && cut.Selected[5].SequenceEqual(new[] { 7, 1200 })
            && cut.Selected.Sum(a => a.Length) == 3 && cut.Selected.Zip(recut.Selected).All(p => p.First.SequenceEqual(p.Second)),
            "Trooper named23 elbow selects literal whole IDs independent of physical batch, full joint and node ordering");
        Check(cut.Primitives.Sum(p => p.Indices.Length) == 9 && !cut.Selected.SelectMany(a => a).Contains(404)
            && !cut.Selected.SelectMany(a => a).Contains(901),
            "Trooper any-positive influence still requires all3 corners and rejects zero-only descendant weight");
        foreach (var p in cut.Primitives)
        {
            var raw = source.Primitives.Single(q => p.SourceTriangleIds!.All(id => q.SourceTriangleIds!.Contains(id)));
            Check(ReferenceEquals(raw.Vertices, p.Vertices) && ReferenceEquals(raw.JointPalette, p.JointPalette)
                && raw.SourcePrimitiveOrdinal == p.SourcePrimitiveOrdinal && raw.SourceMaterialOrdinal == p.SourceMaterialOrdinal,
                "Trooper cut changes only complete index and sourceID subsets, preserving attributes and physical palettes");
        }
        M basis = CharacterSourceCannonRecipe.GeneratedBasis(source, source.SourceRig!.Nodes.Select(n => n.Local).ToArray(), 23, 34, "R_gun_LCTR");
        M reorderedBasis = CharacterSourceCannonRecipe.GeneratedBasis(reordered, reordered.SourceRig!.Nodes.Select(n => n.Local).ToArray(), 23, 34, "R_gun_LCTR");
        Check(basis == M.CreateTranslation(-1, -2, -5) && basis == reorderedBasis,
            "Trooper locator34 frame uses full inversebind hierarchy and has independent literal muzzle oracle");
        Check(Fingerprint(source) == original && Fingerprint(reordered) == otherOriginal,
            "Trooper selection and basis leave both complete source fixtures unchanged");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(source), "Trooper never borrows default Echoes elbow24");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(source, source.SourceRig.Nodes.Select(n => n.Local).ToArray()), "Trooper never borrows default Echoes locators");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(source, -1), "negative named elbow ID");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(source, source.SourceRig.Nodes.Select(n => n.Local).ToArray(), 23, 34), "same locatorID wrong source name");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(source, source.SourceRig.Nodes.Select(n => n.Local).ToArray(), 23, 33, "R_gun_LCTR"), "same source name wrong locatorID");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(Fixture(trooper: true, wrongElbow: true), 23), "Trooper wrong elbow sourceID");
        var outside = Fixture(trooper: true, tipOutside: true);
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(outside, outside.SourceRig!.Nodes.Select(n => n.Local).ToArray(), 23, 34, "R_gun_LCTR"), "Trooper tip must descend from elbow23");
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.GeneratedBasis(source, new M[35], 23, 34, "R_gun_LCTR"), "full35 joints still require all36 local node matrices");
        var badParts = source.Primitives.ToArray(); badParts[0] = badParts[0] with { JointPalette = new[] { 35, 0, 1, 2 } };
        Throws<InvalidDataException>(() => CharacterSourceCannonRecipe.SelectGeneratedCut(source with { Primitives = badParts }, 23), "local palette cannot escape full35 joints");
    }

    private static void FinalTrim()
    {
        Check(CharacterSourceCannonRecipe.FinalTrimUv(.489f, .764f)
            && !CharacterSourceCannonRecipe.FinalTrimUv(.489f, 1 - .764f), "Final selector uses original rawV, never the rendered copiedV flip");
        foreach (var uv in new[] { new UV(.49099997f,.764f), new UV(.48700002f,.764f), new UV(.489f,.76599997f), new UV(.489f,.7620001f) })
            Check(CharacterSourceCannonRecipe.FinalTrimUv(uv.X, uv.Y), "nearest interior floating sourceUV remains inside the strict trim box");
        foreach (var uv in new[] { new UV(.49100003f,.764f), new UV(.48699996f,.764f), new UV(.489f,.76600003f), new UV(.489f,.76199996f), new UV(float.NaN,.764f), new UV(.489f,float.PositiveInfinity) })
            Check(!CharacterSourceCannonRecipe.FinalTrimUv(uv.X, uv.Y), "nearest exterior/nonfinite sourceUV never satisfies the strict trim box");
        var fixture = Fixture(); var donor = fixture.Primitives[0]; var parts = new List<CharacterWeightedPrimitive>();
        CharacterWeightedPrimitive Part(int id, int material, int hitCorner = -1, UV? hit = null, bool outside = false)
        {
            var vertices = new[] { donor.Vertices[1], donor.Vertices[2], donor.Vertices[0] }.Select(v => v with { Texcoord = new UV(.1f,.2f) }).ToArray();
            if (hitCorner >= 0) vertices[hitCorner] = vertices[hitCorner] with { Texcoord = hit ?? new UV(.489f,.764f) };
            if (outside) vertices[0] = vertices[0] with { Weights = new W(1,0,0,0), PackedJoints = CharacterWeightedModelLoader.PackJoints(0,0,0,0) };
            return donor with { SourcePrimitiveOrdinal = id % 3, SourceMaterialOrdinal = material,
                SourceTriangleIds = new[] { id }, Vertices = vertices, Indices = new uint[] { 2,0,1 } };
        }
        for (int corner = 0; corner < 3; corner++) parts.Add(Part(1000 + corner, 5, corner));
        parts.Add(Part(1003,5,0,new(.489f,1-.764f))); parts.Add(Part(1004,4,1)); parts.Add(Part(1005,3,2));
        parts.Add(Part(1006,5,2,new(.49099997f,.764f))); parts.Add(Part(1007,5,0,new(.49100003f,.764f)));
        parts.Add(Part(1008,5,1,new(.489f,.77f))); parts.Add(Part(1009,5,2,outside:true));
        var source = fixture with { Primitives = parts }; string original = Fingerprint(source);
        var complete = CharacterSourceCannonRecipe.SelectGeneratedCut(source);
        var trimmed = CharacterSourceCannonRecipe.SelectGeneratedCut(source, finalTrim:true);
        Check(complete.Selected.Sum(a => a.Length) == 9 && trimmed.Selected.Sum(a => a.Length) == 5,
            "Final trim removes whole triangles on any matching corner only after the same complete elbow cut");
        Check(trimmed.Selected[5].SequenceEqual(new[] { 1003,1007,1008 }) && trimmed.Selected[4].SequenceEqual(new[] { 1004 })
            && trimmed.Selected[3].SequenceEqual(new[] { 1005 }) && !complete.Selected.SelectMany(a => a).Contains(1009),
            "Final material5-only trim retains literal original IDs across batches; other material hits and flippedV remain intact");
        foreach (var p in trimmed.Primitives)
        {
            var raw = parts.Single(q => q.SourceTriangleIds![0] == p.SourceTriangleIds![0]);
            Check(p.Indices.SequenceEqual(new uint[] { 2,0,1 }) && ReferenceEquals(p.Vertices, raw.Vertices) && ReferenceEquals(p.JointPalette, raw.JointPalette)
                && p.SourcePrimitiveOrdinal == raw.SourcePrimitiveOrdinal && p.SourceMaterialOrdinal == raw.SourceMaterialOrdinal,
                "Trim preserves complete corner winding, attributes, positive influences, local palette and physical material identity");
        }
        var reordered = CharacterSourceCannonRecipe.SelectGeneratedCut(source with { Primitives = parts.AsEnumerable().Reverse().ToArray() }, finalTrim:true);
        Check(trimmed.Selected.Zip(reordered.Selected).All(p => p.First.SequenceEqual(p.Second)), "Final globalID groups do not depend on physical batch ordering");
        Check(Fingerprint(source) == original && CharacterSourceCannonRecipe.SelectGeneratedCut(source).Selected.Zip(complete.Selected).All(p => p.First.SequenceEqual(p.Second)),
            "Final trim never mutates source inputs or changes the default untrimmed selector");
        int firstId = trimmed.Primitives[0].SourceTriangleIds![0]; int firstIndex = parts.FindIndex(p => p.SourceTriangleIds![0] == firstId);
        parts[firstIndex].SourceTriangleIds![0] = 99999; parts[firstIndex].Indices[0] = 1;
        Check(trimmed.Primitives[0].SourceTriangleIds![0] == firstId && trimmed.Primitives[0].Indices[0] == 2,
            "Trimmed IDs and index subsets detach from later caller mutation");
    }

    private static CharacterWeightedModelData Fixture(bool reordered = false, bool tipOutside = false, bool wrongElbow = false, bool cycle = false, bool trooper = false)
    {
        string[] order = reordered ? new[] { "mesh", "child", "elbow", "outside", "root", "tip" } : new[] { "outside", "tip", "mesh", "root", "elbow", "child" };
        var index = order.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        var parent = new Dictionary<string, string?> { ["root"] = cycle ? "child" : null, ["mesh"] = null, ["outside"] = "root", ["elbow"] = "root", ["tip"] = tipOutside ? "root" : "elbow", ["child"] = "elbow" };
        var names = new Dictionary<string, string> { ["root"] = "source_bone_0_root", ["mesh"] = "own_identity_mesh", ["outside"] = "source_bone_99_outside", ["elbow"] = wrongElbow ? "source_bone_99_R_elbow" : "source_bone_24_R_elbow", ["tip"] = "source_bone_32_GUN_Particle_LCTR", ["child"] = "source_bone_25_child" };
        var local = new Dictionary<string, V> { ["root"] = V.Zero, ["mesh"] = V.Zero, ["outside"] = new(-2, 0, 0), ["elbow"] = new(1, 2, 3), ["tip"] = new(0, 0, 2), ["child"] = new(0, 1, 0) };
        string[] extras = trooper ? Enumerable.Range(0, 35).Where(id => id is not (0 or 3 or 23 or 24 or 34)).Select(id => "extra" + id).ToArray() : [];
        if (trooper)
        {
            names["root"] = "source_bone_3_Root"; names["outside"] = "source_bone_0_outside";
            names["elbow"] = wrongElbow ? "source_bone_99_R_elbow" : "source_bone_23_R_elbow";
            names["tip"] = "source_bone_34_R_gun_LCTR"; names["child"] = "source_bone_24_child";
            foreach (string role in extras) { parent[role] = "root"; names[role] = "source_bone_" + role[5..] + "_own"; local[role] = V.Zero; }
            order = reordered ? extras.Reverse().Concat(order).ToArray() : order.Concat(extras).ToArray();
            index = order.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        }
        var nodes = new JsonArray();
        foreach (string role in order)
        {
            V t = local[role]; var n = new JsonObject { ["name"] = names[role], ["translation"] = new JsonArray(t.X, t.Y, t.Z),
                ["children"] = new JsonArray(order.Where(c => parent[c] == role).Select(c => JsonValue.Create(index[c])).ToArray()) };
            if (role == "mesh") { n["mesh"] = 0; n["skin"] = 0; } nodes.Add(n);
        }
        string[] jointOrder = reordered ? new[] { "root", "tip", "outside", "child", "elbow" } : new[] { "child", "outside", "elbow", "tip", "root" };
        if (trooper) jointOrder = extras.Concat(jointOrder).ToArray();
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
