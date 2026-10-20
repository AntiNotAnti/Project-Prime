using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Synthetic hierarchy checks; no assets, world simulation, renderer or GL.
internal static class TraversalLowerBlendTests
{
    private static int _checks;
    private readonly record struct Pose(Vector3 Scale, Vector3 Angles, Vector3 Position)
    {
        internal Matrix4 Matrix => Matrix4.CreateScale(Scale) * Matrix4.CreateRotationX(Angles.X)
            * Matrix4.CreateRotationY(Angles.Y) * Matrix4.CreateRotationZ(Angles.Z)
            * Matrix4.CreateTranslation(Position);
    }
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("TRAVERSAL LOWER BLEND: " + message); }
    private static bool Finite(Matrix4 m) => new[] { m.Row0, m.Row1, m.Row2, m.Row3 }
        .All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W));
    private static void Near(Matrix4 a, Matrix4 b, string message, float tolerance = .0001f)
        => Check(Finite(a) && (a.Row0 - b.Row0).Length <= tolerance
            && (a.Row1 - b.Row1).Length <= tolerance && (a.Row2 - b.Row2).Length <= tolerance
            && (a.Row3 - b.Row3).Length <= tolerance, message);
    private static void Set<T>(object value, string name, T field)
        => value.GetType().GetField(name)!.SetValue(value, field);

    private static Model Skeleton()
    {
        // Root -> joint -> tip, with a separate upper branch below root.
        var names = new[] { "root", "joint", "tip", "upper" };
        var parents = new short[] { -1, 0, 1, 0 };
        var children = new short[] { 1, 2, -1, -1 };
        var siblings = new short[] { -1, 3, -1, -1 };
        var nodes = new RawNode[names.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            object raw = default(RawNode);
            var bytes = new byte[64]; Encoding.ASCII.GetBytes(names[i]).CopyTo(bytes, 0);
            Set(raw, nameof(RawNode.Name), bytes);
            Set(raw, nameof(RawNode.ParentId), parents[i]);
            Set(raw, nameof(RawNode.ChildId), children[i]);
            Set(raw, nameof(RawNode.NextId), siblings[i]);
            nodes[i] = (RawNode)raw;
        }
        return new Model("traversal-lower-blend-synthetic", false, default, nodes,
            Array.Empty<RawMesh>(), Array.Empty<RawMaterial>(), Array.Empty<DisplayList>(),
            Array.Empty<IReadOnlyList<RenderInstruction>>(), new AnimationResults(),
            Array.Empty<Matrix4>(), Array.Empty<Recolor>(), Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
    }

    private static AnimationInfo Clip(params (string Name, Pose Value)[] poses)
    {
        var scales = new List<float>(); var rotations = new List<float>(); var translations = new List<float>();
        var curves = new Dictionary<string, NodeAnimation>();
        foreach (var (name, pose) in poses)
        {
            object curve = default(NodeAnimation);
            for (int axis = 0; axis < 3; axis++)
            {
                string suffix = new[] { "X", "Y", "Z" }[axis];
                Set(curve, "ScaleLutIndex" + suffix, (ushort)scales.Count);
                Set(curve, "RotateLutIndex" + suffix, (ushort)rotations.Count);
                Set(curve, "TranslateLutIndex" + suffix, (ushort)translations.Count);
                Set(curve, "ScaleLutLength" + suffix, (ushort)1);
                Set(curve, "RotateLutLength" + suffix, (ushort)1);
                Set(curve, "TranslateLutLength" + suffix, (ushort)1);
                scales.Add(pose.Scale[axis]); rotations.Add(pose.Angles[axis]); translations.Add(pose.Position[axis]);
            }
            curves.Add(name, (NodeAnimation)curve);
        }
        object raw = default(RawNodeAnimationGroup); Set(raw, nameof(RawNodeAnimationGroup.FrameCount), (uint)1);
        var info = new AnimationInfo();
        info.Node.Group = new NodeAnimationGroup((RawNodeAnimationGroup)raw, scales, rotations, translations, curves);
        info.Node.Slot = 0; info.FrameCount[0] = 1;
        return info;
    }
    private static Matrix4[] Native(Model model, AnimationInfo clip)
    {
        model.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, clip);
        return model.Nodes.Select(n => n.Animation).ToArray();
    }
    private static Pose Rigid(Vector3 angles, Vector3 position) => new(Vector3.One, angles, position);

    internal static int CheckMath()
    {
        _checks = 0;
        MatchingHierarchy(); MissingCurves(); UpperAndPostTransform(); DegenerateLocal(); Allocation();
        return _checks;
    }

    private static void MatchingHierarchy()
    {
        var model = Skeleton();
        var rootA = Rigid(new(.2f, -.4f, .1f), new(1, 2, 3));
        var rootB = Rigid(new(-.4f, .3f, .7f), new(-2, 1, 4));
        var jointA = Rigid(new(.3f, .1f, -.2f), new(0, 2, 0));
        var jointB = Rigid(new(-.2f, .5f, .4f), new(0, 2, 0));
        var tip = Rigid(Vector3.Zero, new(0, 1.25f, 0));
        var from = Clip(("root", rootA), ("joint", jointA), ("tip", tip));
        var to = Clip(("root", rootB), ("joint", jointB), ("tip", tip));
        var first = Native(model, from); var second = Native(model, to);
        foreach (float amount in new[] { 0f, .000001f, .25f, .5f, .75f, .999999f, 1f })
        {
            model.AnimateNodesBlended(0, from, null, to, null, amount);
            if (amount == 0 || amount == 1)
                for (int i = 0; i < model.Nodes.Count; i++)
                    Check(model.Nodes[i].Animation == (amount == 0 ? first[i] : second[i]), "exact endpoint uses native node content");
            float thigh = (model.Nodes[1].Animation.Row3.Xyz - model.Nodes[0].Animation.Row3.Xyz).Length;
            float shin = (model.Nodes[2].Animation.Row3.Xyz - model.Nodes[1].Animation.Row3.Xyz).Length;
            Check(MathF.Abs(thigh - 2) < .0001f && MathF.Abs(shin - 1.25f) < .0001f,
                "local rotation interpolation preserves both joint lengths through hierarchy");
            Near(model.Nodes[2].Animation, tip.Matrix * model.Nodes[1].Animation,
                "tip composes after the blended joint rather than blending world matrices");
        }
        // Distinct positive axis scales must survive decomposition/recomposition.
        var scaled = new Pose(new(1.5f, .75f, 2), new(.4f, -.7f, .2f), new(1, -2, 3));
        foreach (float amount in new[] { .000001f, .5f, .999999f })
            Near(Model.BlendLocalNode(scaled.Matrix, scaled.Matrix, amount), scaled.Matrix,
                "positive nonuniform local scale and native XYZ rotation round-trip without axis changes");
    }

    private static void MissingCurves()
    {
        foreach (bool absentFrom in new[] { true, false })
        {
            var model = Skeleton();
            var a = new Pose(new(2, .75f, 1.5f), new(.2f, -.3f, .4f), new(3, -1, 2));
            var b = new Pose(new(.8f, 1.6f, 1.1f), new(-.5f, .6f, -.2f), new(-2, 4, 1));
            var child = Rigid(new(.4f, -.2f, .3f), new(1, 2, -1));
            var tip = Rigid(new(.1f, .3f, -.4f), new(0, 1, 0));
            var fullA = Clip(("root", a), ("joint", child), ("tip", tip));
            var fullB = Clip(("root", b), ("joint", child), ("tip", tip));
            var missingA = Clip(("root", a), ("tip", tip));
            var missingB = Clip(("root", b), ("tip", tip));
            AnimationInfo from = absentFrom ? missingA : fullA, to = absentFrom ? fullB : missingB;
            var first = Native(model, from); var second = Native(model, to);
            foreach (float amount in new[] { 0f, .000001f, .25f, .5f, .75f, .999999f, 1f })
            {
                model.AnimateNodesBlended(0, from, null, to, null, amount);
                Matrix4 expected = new(Vector4.Lerp(first[1].Row0, second[1].Row0, amount),
                    Vector4.Lerp(first[1].Row1, second[1].Row1, amount),
                    Vector4.Lerp(first[1].Row2, second[1].Row2, amount),
                    Vector4.Lerp(first[1].Row3, second[1].Row3, amount));
                Near(model.Nodes[1].Animation, expected, "mixed missing curve retains each endpoint's inheritance and affine shear");
                Near(model.Nodes[2].Animation, tip.Matrix * expected, "matched descendant uses mixed-coverage parent's accepted picture");
                if (amount == 0 || amount == 1)
                    for (int i = 0; i < model.Nodes.Count; i++)
                        Check(model.Nodes[i].Animation == (amount == 0 ? first[i] : second[i]), "missing-curve exact endpoint remains native");
                if (amount < .00001f || amount > .99999f)
                    for (int i = 0; i < model.Nodes.Count; i++)
                        Near(model.Nodes[i].Animation, amount < .5f ? first[i] : second[i],
                            "missing-curve hierarchy approaches native endpoints continuously", .00005f);
            }
        }
    }

    private static void UpperAndPostTransform()
    {
        var model = Skeleton();
        var root = Rigid(new(.2f, -.3f, .1f), new(1, 2, 3));
        var joint = Rigid(new(.4f, .1f, -.2f), new(0, 2, 0));
        var tip = Rigid(Vector3.Zero, new(0, 1, 0));
        var from = Clip(("root", root), ("joint", joint), ("tip", tip));
        var to = Clip(("root", root), ("joint", joint), ("tip", tip));
        Matrix4 pitch = Matrix4.CreateRotationZ(.37f), sentinel = Matrix4.CreateTranslation(99, 88, 77);
        model.Nodes[1].AfterTransform = pitch;
        model.Nodes[3].AnimIgnoreChild = true;
        var native = Native(model, from);
        model.Nodes[3].Animation = sentinel;
        model.Nodes[0].AnimIgnoreChild = true;
        model.AnimateNodesBlended(0, from, null, to, null, .5f);
        Check(model.Nodes[3].Animation == sentinel, "ignored child branch is untouched by lower sampling");
        model.Nodes[0].AnimIgnoreChild = false;
        model.AnimateNodesBlended(0, from, null, to, null, .5f);
        for (int i = 0; i < model.Nodes.Count; i++) Near(model.Nodes[i].Animation, native[i], "native child-before-postpitch ordering is retained");
        Near(model.Nodes[2].Animation, tip.Matrix * joint.Matrix * root.Matrix,
            "descendant does not inherit parent's later postpitch twice");
        var upper = Rigid(new(-.6f, .2f, .5f), new(2, 1, -1));
        var upperClip = Clip(("upper", upper));
        model.AnimateNodes(3, false, Matrix4.Identity, Vector3.One, upperClip);
        Near(model.Nodes[3].Animation, upper.Matrix * model.Nodes[0].Animation,
            "subsequent native upper channel retains its own local curve and accepted parent");
    }

    private static void DegenerateLocal()
    {
        foreach (Vector3 scale in new[] { new Vector3(0, 1, 2), new Vector3(-1, 2, 3) })
        {
            Matrix4 a = new Pose(scale, new(.2f, .3f, -.4f), new(1, 2, 3)).Matrix;
            Matrix4 b = new Pose(Vector3.One, new(-.3f, .1f, .6f), new(-2, 1, 4)).Matrix;
            foreach (float amount in new[] { 0f, .000001f, .5f, .999999f, 1f })
            {
                Matrix4 actual = Model.BlendLocalNode(a, b, amount);
                Check(Finite(actual), "collapsed/reflected optional local curves remain finite");
                if (amount == 0 || amount == 1) Check(actual == (amount == 0 ? a : b), "degenerate exact endpoints preserve source matrices");
                else Near(actual, new Matrix4(Vector4.Lerp(a.Row0, b.Row0, amount), Vector4.Lerp(a.Row1, b.Row1, amount),
                    Vector4.Lerp(a.Row2, b.Row2, amount), Vector4.Lerp(a.Row3, b.Row3, amount)), "degenerate local fallback preserves affine endpoint content");
            }
        }
    }

    private static void Allocation()
    {
        var model = Skeleton();
        var first = Rigid(new(.2f, -.3f, .4f), new(1, 2, 3));
        var second = Rigid(new(-.4f, .2f, -.1f), new(-1, 3, 2));
        var joint = Rigid(Vector3.Zero, new(0, 2, 0));
        var tip = Rigid(Vector3.Zero, new(0, 1, 0));
        var from = Clip(("root", first), ("joint", joint), ("tip", tip));
        var to = Clip(("root", second), ("joint", joint), ("tip", tip));
        // Warm the exact native/partial/full entry paths before measuring.
        for (int i = 0; i < 2000; i++)
        {
            model.AnimateNodesBlended(0, from, null, to, null, 0);
            model.AnimateNodesBlended(0, from, null, to, null, .5f);
            model.AnimateNodesBlended(0, from, null, to, null, 1);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            model.AnimateNodesBlended(0, from, null, to, null, 0);
            model.AnimateNodesBlended(0, from, null, to, null, .5f);
            model.AnimateNodesBlended(0, from, null, to, null, 1);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "warmed native/partial/full hierarchy sampling allocates no managed memory: " + allocated);
        Check(Finite(model.Nodes[2].Animation), "allocation loop retains an observable finite terminal joint");
    }
}
