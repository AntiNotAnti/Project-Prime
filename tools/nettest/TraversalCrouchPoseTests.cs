using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// CPU native-asset pose measurements. Gameplay crouch comes only from real Ctrl
// input; model animation matrices are inspected/restored as presentation scratch.
internal static class TraversalCrouchPoseTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .0002f)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Matrix4 m) => Finite(m.Row0.Xyz) && float.IsFinite(m.Row0.W)
        && Finite(m.Row1.Xyz) && float.IsFinite(m.Row1.W) && Finite(m.Row2.Xyz)
        && float.IsFinite(m.Row2.W) && Finite(m.Row3.Xyz) && float.IsFinite(m.Row3.W);

    internal static int CheckMath()
    {
        _checks = 0;
        foreach (var setup in new[] {
            (Hip: new Vector3(0, 1, 0), Ankle: new Vector3(0, -1, 0), Thigh: 1.25f, Shin: 1.25f),
            (Hip: new Vector3(3, 5, -2), Ankle: new Vector3(4, 2, -1), Thigh: 2.5f, Shin: 2f) })
        foreach (Vector3 forward in new[] { Vector3.UnitZ, Vector3.UnitX, Vector3.Zero,
            (setup.Ankle - setup.Hip).Normalized() })
        {
            Vector3 knee = PlayerEntity.ModCrouchKnee(setup.Hip, setup.Ankle, setup.Thigh, setup.Shin, forward);
            Check(Finite(knee), "reachable knee and degenerate pole fallbacks stay finite");
            Near((knee - setup.Hip).Length, setup.Thigh, "inverse kinematics preserves thigh length");
            Near((setup.Ankle - knee).Length, setup.Shin, "inverse kinematics preserves shin length");
            Vector3 axis = (setup.Ankle - setup.Hip).Normalized();
            Vector3 pole = forward - axis * Vector3.Dot(forward, axis);
            if (pole.LengthSquared > .00001f)
                Check(Vector3.Dot(knee - setup.Hip, pole) > 0, "knee bends toward the supplied forward pole");
        }
        Vector3 origin = new(2, 3, 4);
        Check(PlayerEntity.ModCrouchKnee(origin, origin, 1, 1, Vector3.Zero) == origin,
            "coincident joints have finite deterministic fallback");
        Check(PlayerEntity.ModCrouchKnee(origin, origin + Vector3.UnitY, 0, 1, Vector3.UnitZ) == origin
            && PlayerEntity.ModCrouchKnee(origin, origin + Vector3.UnitY, 1, 0, Vector3.UnitZ) == origin,
            "zero-length leg bones have finite deterministic fallback");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (int source in new[] { 0, 1, 2, 3, 4 })
            {
                bool rejected = false;
                try { PlayerEntity.ModCrouchKnee(source == 0 ? new(invalid, 0, 0) : origin,
                    source == 1 ? new(0, invalid, 0) : origin + Vector3.UnitY,
                    source == 2 ? invalid : 1, source == 3 ? invalid : 1,
                    source == 4 ? new(0, 0, invalid) : Vector3.UnitZ); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "nonfinite inverse-kinematics inputs are rejected");
            }
        }
        return _checks;
    }

    internal static object Capture(Hunter hunter)
    {
        _checks = 0;
        object?[] arguments = { hunter, MovementModel.Traversal, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        var rows = new List<object>();
        try
        {
            PlayerEntity p = scene.Players.Main!;
            p.Controls.Crouch.Key = Keys.LeftControl;
            SetKey(keyboard, Keys.LeftControl, true);
            for (int index = 0; index < 8; index++) scene.OnSimulationFrame();
            Check(p.Controls.Crouch.IsDown && p.ModCrouched && !p.IsAltForm,
                "genuine production Ctrl input earns the pose's crouched body state");
            ModelInstance[] lods = (ModelInstance[])typeof(PlayerEntity).GetField("_bipedModelLods", Hidden)!.GetValue(p)!;
            var lower = (ModelInstance)typeof(PlayerEntity).GetField("_bipedModel1", Hidden)!.GetValue(p)!;
            var upper = (ModelInstance)typeof(PlayerEntity).GetField("_bipedModel2", Hidden)!.GetValue(p)!;
            var present = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
            for (int sample = 0; sample < 3; sample++)
            {
                SetKey(keyboard, Keys.W, sample > 0);
                for (int step = 0; step < 11; step++) scene.OnSimulationFrame();
                Check(p.ModCrouched && p.Controls.Crouch.IsDown && p.Health > 0,
                    "idle and moving native animation samples retain genuine crouch input");
                Matrix4 world = PlayerEntity.ModBuildBipedDrawTransform(p.Position, p.FacingVector,
                    p.FacingVector.WithY(0), Metadata.HunterScales[hunter], p.ModBipedBottom, out float pitch);
                float drop = Fixed.ToFloat(p.Values.MaxPickupHeight) - p.ModBipedTop;
                Check(drop > 0, "accepted physical stance supplies a real torso height reduction");
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    Model model = lods[lod].Model;
                    int Index(string name)
                    {
                        for (int index = 0; index < model.Nodes.Count; index++)
                            if (model.Nodes[index].Name == name) return index;
                        throw new InvalidOperationException($"{hunter}/LOD{lod} native skeleton lacks {name}");
                    }
                    int spine = Index("Spine_1");
                    int[] left = { Index("L_hip"), Index("L_knee"), Index("L_ankle") };
                    int[] right = { Index("R_hip"), Index("R_knee"), Index("R_ankle") };
                    Matrix4[] saved = model.Nodes.Select(node => node.Animation).ToArray();
                    try
                    {
                        // Animate the exact native lower/upper split before the pose helper,
                        // without writing any actor stance or private animation frame.
                        Node torso = model.Nodes[spine];
                        bool ignore = torso.AnimIgnoreChild; Matrix4? after = torso.AfterTransform;
                        torso.AnimIgnoreChild = true; torso.AfterTransform = Matrix4.CreateRotationZ(pitch);
                        try { model.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, lower.AnimInfo); }
                        finally { torso.AnimIgnoreChild = ignore; torso.AfterTransform = after; }
                        model.AnimateNodes(torso.ChildIndex, false, Matrix4.Identity, Vector3.One, upper.AnimInfo);
                        foreach (Node node in model.Nodes) node.Animation *= world;
                        Matrix4[] native = model.Nodes.Select(node => node.Animation).ToArray();
                        p.ModApplyCrouchPose(model, world);
                        Matrix4[] crouched = model.Nodes.Select(node => node.Animation).ToArray();
                        Check(native.All(Finite) && crouched.All(Finite), "all native and crouched node matrix components remain finite");
                        Near(native[spine].Row3.Y - crouched[spine].Row3.Y, drop,
                            "actual native torso lowers by the accepted physical height difference");
                        var legs = new List<object>();
                        foreach (int[] leg in new[] { left, right })
                        {
                            Vector3 hip = native[leg[0]].Row3.Xyz, knee = native[leg[1]].Row3.Xyz,
                                ankle = native[leg[2]].Row3.Xyz;
                            Vector3 crouchHip = crouched[leg[0]].Row3.Xyz, crouchKnee = crouched[leg[1]].Row3.Xyz,
                                crouchAnkle = crouched[leg[2]].Row3.Xyz;
                            Near((crouchKnee - crouchHip).Length, (knee - hip).Length,
                                "animated native thigh length survives crouch IK");
                            Near((crouchAnkle - crouchKnee).Length, (ankle - knee).Length,
                                "animated native shin length survives crouch IK");
                            Near(hip.Y - crouchHip.Y, drop, "animated hip follows torso drop");
                            int footNodes = 0;
                            for (int node = 0; node < model.Nodes.Count; node++)
                                for (int ancestor = node; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                                    if (ancestor == leg[2])
                                    {
                                        Check(native[node] == crouched[node], "animated ankle and every foot descendant remain planted exactly");
                                        footNodes++; break;
                                    }
                            Check(footNodes > 0, "native foot chain is represented by real ankle descendants");
                            legs.Add(new { side = model.Nodes[leg[0]].Name, thigh = (knee - hip).Length,
                                shin = (ankle - knee).Length, crouchedThigh = (crouchKnee - crouchHip).Length,
                                crouchedShin = (crouchAnkle - crouchKnee).Length, footNodes });
                        }
                        // The production presentation entry must compose native animation
                        // with this same helper, rather than bypassing or applying it twice.
                        present.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
                        Check(model.Nodes.Select(node => node.Animation).SequenceEqual(crouched),
                            "actual biped presentation entry applies the crouch helper exactly once");
                        Check(model.MatrixStackValues.All(float.IsFinite), "submitted native matrix stack remains finite");
                        rows.Add(new { sample, lod, model = model.Name, lowerFrame = lower.AnimInfo.NodeFrame,
                            upperFrame = upper.AnimInfo.NodeFrame, expectedDrop = drop,
                            torsoDrop = native[spine].Row3.Y - crouched[spine].Row3.Y, legs });
                    }
                    finally
                    {
                        for (int node = 0; node < model.Nodes.Count; node++) model.Nodes[node].Animation = saved[node];
                        model.UpdateMatrixStack();
                    }
                }
            }
            return new { hunter = hunter.ToString(), checks = _checks,
                scope = "Actual CPU native Hunter model and both LODs, native animation frame state, real Ctrl-derived crouch; no GL or screenshot qualification", rows };
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
