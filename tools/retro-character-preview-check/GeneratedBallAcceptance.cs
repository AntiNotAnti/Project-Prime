using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Render.Characters;
using M = OpenTK.Mathematics.Matrix4;
using V = OpenTK.Mathematics.Vector3;
using NM = System.Numerics.Matrix4x4;
using NV = System.Numerics.Vector3;
using UV = System.Numerics.Vector2;
using W = System.Numerics.Vector4;

// Invented geometry and hierarchy. Fit is a numerical operation, never source authority.
internal static class GeneratedBallAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        var (source, pose) = Fixture(false); var (reordered, otherPose) = Fixture(true);
        string before = Fingerprint(source, pose), otherBefore = Fingerprint(reordered, otherPose);
        Check(source.Joints.Count == 2 && source.SourceRig!.Nodes.Count == 3 && source.Primitives.Count == 2,
            "complete two-joint hierarchy includes independent mesh node and physical batches");
        NM expectedSkin = NM.CreateTranslation(-5, 0, -4) * NM.CreateTranslation(4, -2, 1)
            * NM.CreateRotationY(MathF.PI / 2) * NM.CreateTranslation(1, 2, 3);
        NV[] points = Offsets().Select(v => NV.Transform(v + new NV(5, 0, 4), expectedSkin)).ToArray();
        NV center = (points.Aggregate(new NV(float.PositiveInfinity), NV.Min)
            + points.Aggregate(new NV(float.NegativeInfinity), NV.Max)) * .5f;
        float radius = points.Max(p => NV.Distance(p, center));
        Check(MathF.Abs(radius - 3) < .000002f, "independent posed geometry retains literal radius3");
        foreach (float nativeRadius in new[] { .25f, .5f, 1f, 4f })
        {
            M fit = CharacterSourceBallPose.Fit(source, pose, nativeRadius);
            M otherFit = CharacterSourceBallPose.Fit(reordered, otherPose, nativeRadius);
            NM expected = NM.CreateTranslation(-center) * NM.CreateScale(nativeRadius / radius);
            Check(Difference(fit, expected) < .00002f && Difference(otherFit, expected) < .00002f,
                "independent root rotation, child translation and inverse-bind sphere oracle");
            Check(fit.M11 > 0 && fit.M11 == fit.M22 && fit.M22 == fit.M33 && fit.M12 == 0 && fit.M13 == 0
                && fit.M21 == 0 && fit.M23 == 0 && fit.M31 == 0 && fit.M32 == 0,
                "fit has positive uniform scale without adding rotation or reflection");
            V[] fitted = points.Select(p => V.TransformPosition(new(p.X, p.Y, p.Z), fit)).ToArray();
            Check(MathF.Abs(fitted.Max(v => v.Length) - nativeRadius) < .00002f
                && (fitted.Aggregate(new V(float.PositiveInfinity), V.ComponentMin)
                    + fitted.Aggregate(new V(float.NegativeInfinity), V.ComponentMax)).Length < .00002f,
                "maximum vertex radius equals native radius and posed bounds center is zero");
        }
        Check(Fingerprint(source, pose) == before && Fingerprint(reordered, otherPose) == otherBefore,
            "fit preserves complete source geometry, physical palettes, hierarchy, inverse binds and input pose");
        Failures();
        Console.WriteLine($"PASS: generated source ball full-rig centered sphere fit ({_checks} checks)");
    }

    private static void Failures()
    {
        var (source, pose) = Fixture(false);
        foreach (float r in new[] { 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1001 })
            Refuse(() => CharacterSourceBallPose.Fit(source, pose, r), "invalid native radius");
        Refuse(() => CharacterSourceBallPose.Fit(source with { SourceRig = null }, pose, 1), "missing complete rig");
        Refuse(() => CharacterSourceBallPose.Fit(source with { Primitives = Array.Empty<CharacterWeightedPrimitive>() }, pose, 1), "empty geometry");
        Refuse(() => CharacterSourceBallPose.Fit(source, pose[..2], 1), "partial source pose");
        Refuse(() => CharacterSourceBallPose.Fit(source with { Joints = source.Joints.Take(1).ToArray() }, pose, 1), "partial inverse binds");
        M[] scaled = pose.ToArray(); scaled[0] = M.CreateScale(2) * scaled[0];
        Refuse(() => CharacterSourceBallPose.Fit(source, scaled, 1), "scaled source hierarchy");
        M[] movedMesh = pose.ToArray(); movedMesh[2] = M.CreateTranslation(1, 0, 0);
        Refuse(() => CharacterSourceBallPose.Fit(source, movedMesh, 1), "moving mesh ancestor");
        void Part(Func<CharacterWeightedPrimitive, CharacterWeightedPrimitive> change, string why)
        {
            var parts = source.Primitives.Select(change).ToArray();
            Refuse(() => CharacterSourceBallPose.Fit(source with { Primitives = parts }, pose, 1), why);
        }
        void Vertex(Func<CharacterWeightedVertex, CharacterWeightedVertex> change, string why)
            => Part(p => p with { Vertices = p.Vertices.Select(change).ToArray() }, why);
        Part(p => p with { JointPalette = null }, "missing physical palette");
        Part(p => p with { JointPalette = Array.Empty<int>() }, "empty physical palette");
        Part(p => p with { JointPalette = new int[33] }, "physical palette budget");
        Part(p => p with { JointPalette = new[] { -1 } }, "negative global joint");
        Part(p => p with { JointPalette = new[] { 2 } }, "global joint outside full rig");
        Vertex(v => v with { PackedJoints = -1 }, "negative packed joints");
        Vertex(v => v with { PackedJoints = 1 << 20 }, "packed bits beyond four local slots");
        Vertex(v => v with { PackedJoints = 31 }, "positive influence outside physical palette");
        Vertex(v => v with { Weights = W.Zero }, "zero weight sum");
        Vertex(v => v with { Weights = new(.5f, 0, 0, 0) }, "nonunit weights");
        foreach (float w in new[] { -.1f, float.NaN, float.PositiveInfinity })
            Vertex(v => v with { Weights = new(w, 0, 0, 0) }, "invalid source weights");
        foreach (float p in new[] { float.NaN, float.PositiveInfinity })
            Vertex(v => v with { Position = new(p, 0, 0) }, "nonfinite source position");
        Vertex(v => v with { Position = new(5, 0, 4) }, "degenerate posed sphere");
    }

    private static NV[] Offsets() => new[] { new NV(-2, 0, 0), new NV(2, 0, 0), new NV(0, 3, 0), new NV(0, -3, 0) };
    private static (CharacterWeightedModelData, M[]) Fixture(bool reordered)
    {
        string[] order = reordered ? new[] { "child", "mesh", "root" } : new[] { "root", "child", "mesh" };
        string[] jointOrder = reordered ? new[] { "child", "root" } : new[] { "root", "child" };
        var index = order.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        string Name(string role) => role == "root" ? "source_bone_0_root" : role == "child" ? "source_bone_1_Skeleton_Root" : "own_mesh";
        V Local(string role) => role == "root" ? new(1, 2, 3) : role == "child" ? new(4, -2, 1) : V.Zero;
        var nodes = new JsonArray();
        foreach (string role in order)
        {
            V t = Local(role); var n = new JsonObject { ["name"] = Name(role), ["translation"] = new JsonArray(t.X, t.Y, t.Z) };
            if (role == "root") n["children"] = new JsonArray(index["child"]);
            if (role == "mesh") { n["mesh"] = 0; n["skin"] = 0; }
            nodes.Add(n);
        }
        using var json = JsonDocument.Parse(new JsonObject { ["nodes"] = nodes,
            ["joints"] = new JsonArray(jointOrder.Select(r => JsonValue.Create(index[r])).ToArray()) }.ToJsonString());
        var elements = json.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var rig = CharacterSourceRig.Read(elements, json.RootElement.GetProperty("joints").EnumerateArray().ToArray(), new() { elements[index["mesh"]] });
        var joints = jointOrder.Select(r => new CharacterWeightedJoint(Name(r), Name(r), M.CreateTranslation(r == "root" ? new V(-1, -2, -3) : new V(-5, 0, -4)))).ToArray();
        var vertices = Offsets().Select(p => new CharacterWeightedVertex(p + new NV(5, 0, 4), NV.UnitY, UV.Zero, new W(1, 0, 0, 0), 0)).ToArray();
        CharacterWeightedPrimitive Part(uint[] indices) => new("own_ball", vertices.ToArray(), indices, JointPalette: new[] { Array.IndexOf(jointOrder, "child") });
        var parts = new[] { Part(new uint[] { 0, 1, 2 }), Part(new uint[] { 0, 2, 3 }) };
        M[] pose = order.Select(r => r == "root" ? M.CreateRotationY(MathF.PI / 2) * M.CreateTranslation(Local(r)) : M.CreateTranslation(Local(r))).ToArray();
        return (new(reordered ? parts.Reverse().ToArray() : parts, joints, rig), pose);
    }

    private static float Difference(M a, NM b)
    {
        float[] aa = Values(a), bb = { b.M11,b.M12,b.M13,b.M14,b.M21,b.M22,b.M23,b.M24,b.M31,b.M32,b.M33,b.M34,b.M41,b.M42,b.M43,b.M44 };
        return aa.Zip(bb, (x, y) => MathF.Abs(x - y)).Max();
    }
    private static float[] Values(M m) => new[] { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 };
    private static string Fingerprint(CharacterWeightedModelData source, M[] pose)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var node in source.SourceRig!.Nodes) { writer.Write(node.Name); writer.Write(node.Parent); foreach (float f in Values(node.Local)) writer.Write(f); }
        foreach (var joint in source.Joints) { writer.Write(joint.SourceNode); writer.Write(joint.TargetNode); foreach (float f in Values(joint.InverseBind)) writer.Write(f); }
        foreach (var p in source.Primitives)
        {
            foreach (int j in p.JointPalette!) writer.Write(j); foreach (uint i in p.Indices) writer.Write(i);
            foreach (var v in p.Vertices) { foreach (float f in new[] { v.Position.X,v.Position.Y,v.Position.Z,v.Normal.X,v.Normal.Y,v.Normal.Z,v.Texcoord.X,v.Texcoord.Y,v.Weights.X,v.Weights.Y,v.Weights.Z,v.Weights.W }) writer.Write(f); writer.Write(v.PackedJoints); }
        }
        foreach (var matrix in pose) foreach (float f in Values(matrix)) writer.Write(f);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); _checks++; }
    private static void Refuse(Action action, string why)
    { try { action(); } catch (InvalidDataException) { _checks++; return; } throw new InvalidOperationException("Expected invalid source ball: " + why); }
}
