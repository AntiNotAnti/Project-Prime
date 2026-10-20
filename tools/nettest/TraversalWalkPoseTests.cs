using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Requires the dedicated walk integration, _modCrouchWalk and
// _modCrouchWalkReach. Never run against the older retimed native-walk pose.
// Actual low keyboard travel and loaded native skeletons, both LODs. The only
// gameplay fixture write is initial lane placement. Model nodes are restored
// after every CPU picture. Straight, fully blended support intervals are the
// only world-foot-lock claim; turning and stop fades have separate bounds.
internal static class TraversalWalkPoseTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int PhaseBins = 20;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Canonical = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
    private static readonly MethodInfo Visual = typeof(PlayerEntity).GetMethod("UpdateHunterBipedVisual", Hidden)!;
    private static readonly MethodInfo LowerPose = typeof(PlayerEntity).GetMethod("ModCrouchLowerPose", Hidden)!;
    private static readonly FieldInfo Walk = typeof(PlayerEntity).GetField("_modCrouchWalk", Hidden)
        ?? throw new InvalidOperationException("Walk pose tests require the dedicated walk presentation integration.");
    private static readonly FieldInfo Reach = typeof(PlayerEntity).GetField("_modCrouchWalkReach", Hidden)
        ?? throw new InvalidOperationException("Walk pose tests require the shared LOD0 native-idle walk reach.");
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label, float tolerance = .0005f)
        => Check(float.IsFinite(actual) && float.IsFinite(expected) && MathF.Abs(actual - expected) <= tolerance,
            $"{label}: {actual} != {expected}");
    private static bool Finite(Matrix4 m) => new[] { m.Row0, m.Row1, m.Row2, m.Row3 }
        .All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W));
    private static object? Field(PlayerEntity p, string name) => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(p);
    private static TraversalWalkSample Sample(PlayerEntity p) => ((TraversalWalkPresentation)Walk.GetValue(p)!).Sample(1);
    private static float LocalPhase(float phase, int side)
    { float p = phase + side * .5f; return p - MathF.Floor(p); }

    internal static object Capture(Hunter hunter)
    {
        _checks = 0; var rows = new List<object>();
        foreach (var direction in new[] { (Keys.W, Keys.S, PlayerAnimation.WalkForward),
            (Keys.S, Keys.W, PlayerAnimation.WalkBackward), (Keys.A, Keys.D, PlayerAnimation.WalkLeft),
            (Keys.D, Keys.A, PlayerAnimation.WalkRight) })
            rows.Add(CaptureDirection(hunter, direction.Item1, direction.Item2, direction.Item3));
        rows.Add(CaptureDirection(hunter, Keys.W, Keys.S, PlayerAnimation.WalkForward, uphill: true));
        return new { hunter = hunter.ToString(), checks = _checks,
            scope = "Dedicated low footwork on an authored flat floor and walkable uphill ramp; real Ctrl+W/S/A/D; complete procedural phases independent of native clip frames; both loaded native LODs; straight fully blended support-foot drift only; CPU pictures, no GPU/network/remote-smoothed claim", rows };
    }

    private sealed class PoseMetrics
    {
        internal readonly HashSet<int>[] Bins = { new(), new() };
        internal readonly int[] PlantedPairs = new int[2];
        internal readonly float[] MaxDrift = new float[2], MaxLift = new float[2];
        internal readonly Vector3?[] PreviousAnkle = new Vector3?[2];
        internal readonly float[] PreviousPhase = new float[2];
        internal readonly bool[] PreviousSupport = new bool[2];
        internal float MaxThighError, MaxShinError;
    }

    private static object CaptureDirection(Hunter hunter, Keys key, Keys opposite, PlayerAnimation nativeClip, bool uphill = false)
    {
        object?[] arguments = { hunter, MovementModel.Traversal, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        int priorCap = FrameTiming.FrameRateCap;
        try
        {
            FrameTiming.FrameRateCap = 60;
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            Vector3 initial = uphill ? new(-30, 1.5f, -4) : new(0, .5f, 12);
            p.Reposition(initial, Vector3.UnitZ, NodeRef.None); p.PrevPosition = initial;
            for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            Check(p.Speed.Xz.Length == 0 && p.Flags1.TestFlag(PlayerFlags1.Standing),
                "initial empty-floor lane has real stationary support");
            // The low stance precedes movement, so there is no running Ctrl edge
            // from which an actual slide can be earned.
            SetKey(keyboard, Keys.LeftControl, true);
            for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
            Check(p.ModCrouched && !p.ModSliding && Sample(p).Amount == 0,
                "real stationary Ctrl reaches the low pose without inventing walk or slide travel");
            SetKey(keyboard, key, true);
            var lower = (ModelInstance)Field(p, "_bipedModel1")!;
            var upper = (ModelInstance)Field(p, "_bipedModel2")!;
            var lods = (ModelInstance[])Field(p, "_bipedModelLods")!;
            Check(lods.Length == 2, "fixture exercises both native Hunter LODs");
            var metrics = new[] { new PoseMetrics(), new PoseMetrics() };
            var nativeFrames = new HashSet<int>();
            var trace = new List<object>();
            float previousPhase = 0; int wraps = 0, nativeCount = 0, movingTicks = 0, nonflatSamples = 0;
            Vector3 previousPosition = p.Position;
            for (int tick = 0; tick < (uphill ? 130 : 360); tick++)
            {
                scene.OnSimulationFrame();
                Check(p.Controls.Crouch.IsDown && p.ModCrouched && !p.ModSliding
                    && p.Flags1.TestFlag(PlayerFlags1.Standing) && p.Health > 0 && !p.IsAltForm,
                    "all measured walking ticks retain actual supported low movement without slides");
                Check(lower.AnimInfo.NodeIndex == (int)nativeClip, "native input still selects its original directional walk clip");
                nativeCount = lower.AnimInfo.Node.Group!.FrameCount;
                Check(nativeCount > 1 && nativeCount < 150, "original native directional clock remains finite");
                Check(lower.AnimInfo.NodeFrame >= 0 && lower.AnimInfo.NodeFrame < nativeCount,
                    "native clock coverage records actual frames inside the untouched directional clip");
                nativeFrames.Add(lower.AnimInfo.NodeFrame);
                TraversalWalkSample sample = Sample(p);
                float reach = (float)Reach.GetValue(p)!;
                Check(float.IsFinite(reach) && reach > .01f && float.IsFinite(sample.Phase)
                    && sample.Phase >= 0 && sample.Phase <= 1 && sample.Amount >= 0 && sample.Amount <= 1,
                    "actual completed movement publishes a finite shared native-idle reach and walk sample");
                Check((p.Position - previousPosition).Xz.Length > .00001f,
                    "walking phase evidence belongs to accepted physical travel");
                if (sample.Normal.Y > .5f && sample.Normal.Y < .99f) nonflatSamples++;
                if (sample.Phase < previousPhase) wraps++;
                previousPhase = sample.Phase;
                movingTicks++;
                Near(sample.Amount, Math.Min(movingTicks / 6f, 1), "actual walk enters its visual amount in six completed moving ticks");
                for (int lod = 0; lod < lods.Length; lod++)
                    MeasurePicture(scene, p, lower, upper, lods[lod].Model, metrics[lod], sample, reach,
                        plantCheck: true, requireIdleTarget: true);
                trace.Add(new { tick, phase = sample.Phase, amount = sample.Amount,
                    position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, nativeFrame = lower.AnimInfo.NodeFrame });
                previousPosition = p.Position;
                if (tick >= 120 && wraps >= 3 && nativeFrames.Count == nativeCount
                    && metrics.All(m => m.Bins.All(b => b.Count == PhaseBins))) break;
            }
            Check(wraps >= 3 && metrics.All(m => m.Bins.All(b => b.Count == PhaseBins)),
                "actual poses cover every procedural phase bin and multiple complete cycles for both feet on both LODs");
            Check(nativeFrames.Count == nativeCount,
                "the unchanged native directional animation clock independently visits its complete original clip");
            if (uphill)
                Check(nonflatSamples > 80 && p.Position.Z < 6 && p.Position.Y > initial.Y + 1,
                    "actual uphill low walking captures sustained fresh nonflat support and stays before the real crest");
            for (int lod = 0; lod < 2; lod++) for (int side = 0; side < 2; side++)
            {
                Check(metrics[lod].PlantedPairs[side] > 30,
                    "each LOD foot has many measured consecutive fully blended support samples");
                Check(metrics[lod].MaxDrift[side] <= .0006f,
                    "actual straight support ankles remain planted in world space within matrix precision");
                Check(metrics[lod].MaxLift[side] > (float)Reach.GetValue(p)! * TraversalWalkPresentation.FootLift * .8f,
                    "actual complete swing reaches a measurable low lift rather than dragging the foot");
            }

            // Deliberate reversal is a bounded turn, never a planted-foot claim.
            SetKey(keyboard, key, false); SetKey(keyboard, opposite, true);
            TraversalWalkSample previous = Sample(p);
            for (int tick = 0; tick < 14; tick++)
            {
                scene.OnSimulationFrame(); TraversalWalkSample current = Sample(p);
                Check(p.ModCrouched && !p.ModSliding && p.Flags1.TestFlag(PlayerFlags1.Standing),
                    "direction change remains an actual supported crouch walk");
                float angle = MathF.Acos(Math.Clamp(Vector3.Dot(previous.Direction, current.Direction), -1, 1));
                Check(float.IsFinite(angle) && angle <= MathF.PI * .35f + .0001f,
                    "actual accepted-direction reversal uses the bounded visual turn");
                for (int lod = 0; lod < lods.Length; lod++)
                    MeasurePicture(scene, p, lower, upper, lods[lod].Model, metrics[lod], current,
                        (float)Reach.GetValue(p)!, plantCheck: false, requireIdleTarget: true);
                previous = current;
            }
            SetKey(keyboard, opposite, false);
            int fading = 0;
            for (int tick = 0; tick < 36; tick++)
            {
                scene.OnSimulationFrame(); TraversalWalkSample current = Sample(p);
                Check(current.Amount <= previous.Amount + .00001f
                    && previous.Amount - current.Amount <= 1f / 6 + .00001f,
                    "actual stop amount cannot snap or reverse its six-tick fade");
                if (current.Amount > 0 && current.Amount < 1) fading++;
                for (int lod = 0; lod < lods.Length; lod++)
                    MeasurePicture(scene, p, lower, upper, lods[lod].Model, metrics[lod], current,
                        (float)Reach.GetValue(p)!, plantCheck: false, requireIdleTarget: current.Amount > 0);
                previous = current;
            }
            Check(fading >= 4 && previous.Amount == 0 && p.Speed.Xz.Length < .0001f && !p.ModSliding,
                "actual stop shows intermediate footwork pictures then reaches native stationary low rest");
            return new { direction = key.ToString(), lane = uphill ? "walkable-uphill" : "flat",
                placement = new[] { initial.X, initial.Y, initial.Z }, samples = movingTicks,
                nonflatSamples, nativeFrames = nativeFrames.Count,
                nativeFrameCount = nativeCount, proceduralWraps = wraps, stopFadePictures = fading,
                lods = metrics.Select((m, lod) => new { lod, phaseBins = m.Bins.Select(b => b.Count).ToArray(),
                    plantedPairs = m.PlantedPairs, maxWorldSupportDrift = m.MaxDrift, maxLowSwingLift = m.MaxLift,
                    m.MaxThighError, m.MaxShinError }).ToArray(), trace };
        }
        finally { FrameTiming.FrameRateCap = priorCap; MomentumSceneTests.Release(scene); }
    }

    private static void MeasurePicture(Scene scene, PlayerEntity p, ModelInstance lower, ModelInstance upper,
        Model model, PoseMetrics metrics, TraversalWalkSample walk, float reach, bool plantCheck, bool requireIdleTarget)
    {
        int Find(string name) => model.Nodes.Select((n, i) => (n, i)).First(pair => pair.n.Name == name).i;
        int spine = Find("Spine_1");
        int[][] legs = { new[] { Find("L_hip"), Find("L_knee"), Find("L_ankle") },
            new[] { Find("R_hip"), Find("R_knee"), Find("R_ankle") } };
        Matrix4 world = PlayerEntity.ModBuildBipedDrawTransform(p.Position, p.FacingVector,
            p.FacingVector.WithY(0), Metadata.HunterScales[p.Hunter], p.ModBipedBottom, out float pitch);
        Matrix4[] saved = model.Nodes.Select(n => n.Animation).ToArray();
        try
        {
            Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
            Matrix4[] canonical = model.Nodes.Select(n => n.Animation).ToArray();
            float[] canonicalStack = model.MatrixStackValues.ToArray();
            Action unchanged = Guard(scene, p, lower.AnimInfo, upper.AnimInfo);
            var idle = new AnimationInfo();
            idle.Node.Group = model.AnimationGroups.Node[(int)PlayerAnimation.Idle];
            idle.Index[0] = (int)PlayerAnimation.Idle; idle.Frame[0] = 0;
            idle.FrameCount[0] = idle.Node.Group.FrameCount; idle.Node.Slot = 0;
            Raw(model, model.Nodes[spine], world, pitch, idle, new(0, 0, 0), upper.AnimInfo);
            Matrix4[] neutral = model.Nodes.Select(n => n.Animation).ToArray();
            if (requireIdleTarget)
            {
                object?[] args = { model, null };
                AnimationInfo scratch = (AnimationInfo)LowerPose.Invoke(p, args)!;
                var cursor = (AnimationSamples.Sample)args[1]!;
                Check(!ReferenceEquals(scratch, lower.AnimInfo) && scratch.NodeIndex == (int)PlayerAnimation.Idle
                    && cursor.FromFrame == 0 && cursor.Fraction == 0,
                    "dedicated footwork samples an independent native Idle0 lower skeleton");
            }
            Visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, 1f });
            Matrix4[] picture = model.Nodes.Select(n => n.Animation).ToArray();
            Check(picture.All(Finite) && model.MatrixStackValues.All(float.IsFinite),
                "dedicated low footwork produces finite native joints and submitted matrices");
            // At zero weight the lower channel resumes its actual native
            // idle frame. Noxus/Trace animate that spine, so Idle0 is no longer
            // the unposed arm reference. Exact native retirement is checked below.
            Matrix4[] reference = requireIdleTarget ? neutral : canonical;
            int cannon = Find("R_elbow");
            Near((picture[cannon].Row0.Xyz.Normalized() - reference[cannon].Row0.Xyz.Normalized()).Length, 0,
                "low walking retains the native weapon arm first aim axis");
            Near((picture[cannon].Row1.Xyz.Normalized() - reference[cannon].Row1.Xyz.Normalized()).Length, 0,
                "low walking retains the native weapon arm second aim axis");
            Near((picture[cannon].Row2.Xyz.Normalized() - reference[cannon].Row2.Xyz.Normalized()).Length, 0,
                "low walking retains the native weapon arm third aim axis");
            for (int side = 0; side < 2; side++)
            {
                int[] leg = legs[side];
                Vector3 h = picture[leg[0]].Row3.Xyz, k = picture[leg[1]].Row3.Xyz, a = picture[leg[2]].Row3.Xyz;
                float thigh = (reference[leg[1]].Row3.Xyz - reference[leg[0]].Row3.Xyz).Length;
                float shin = (reference[leg[2]].Row3.Xyz - reference[leg[1]].Row3.Xyz).Length;
                if (requireIdleTarget && side == 0 && ReferenceEquals(model, lower.Model))
                    Near(reach, thigh + shin, "shared phase calibration is the canonical LOD0 native-idle world leg reach");
                metrics.MaxThighError = Math.Max(metrics.MaxThighError, MathF.Abs((k - h).Length - thigh));
                metrics.MaxShinError = Math.Max(metrics.MaxShinError, MathF.Abs((a - k).Length - shin));
                Near((k - h).Length, thigh, "dedicated gait preserves this LOD's native thigh length");
                Near((a - k).Length, shin, "dedicated gait preserves this LOD's native shin length");
                if (requireIdleTarget)
                {
                    Vector3 anchor = neutral[leg[2]].Row3.Xyz;
                    Vector2 step = TraversalWalkPresentation.Step(walk.Phase, side == 1, reach);
                    Vector3 displacement = walk.Direction * step.X;
                    // Phase spends horizontal accepted distance. Preserve that
                    // horizontal step and solve plane height, then lift vertically.
                    displacement.Y = -(displacement.X * walk.Normal.X + displacement.Z * walk.Normal.Z) / walk.Normal.Y;
                    displacement.Y += step.Y;
                    Vector3 expected = anchor + displacement * walk.Amount;
                    Near((a - expected).Length, 0,
                        "actual ankle matches the declared completed-distance low target about its own LOD neutral anchor", .0006f);
                    float lift = Vector3.Dot(a - anchor, walk.Normal);
                    Check(lift >= -.0006f && lift <= reach * TraversalWalkPresentation.FootLift + .0006f,
                        "actual ankle stays on its neutral plane or inside the declared low swing envelope");
                    Check(Vector3.Dot(k - anchor, walk.Normal) >= .015f - .0006f,
                        "actual knee clears the accepted foot plane while preserving segment lengths");
                    metrics.MaxLift[side] = Math.Max(metrics.MaxLift[side], lift);
                }
                float phase = LocalPhase(walk.Phase, side);
                bool support = walk.Amount == 1 && phase <= TraversalWalkPresentation.SupportFraction;
                if (plantCheck && walk.Amount == 1)
                    metrics.Bins[side].Add(Math.Clamp((int)(phase * PhaseBins), 0, PhaseBins - 1));
                if (plantCheck && support && metrics.PreviousSupport[side]
                    && phase >= metrics.PreviousPhase[side] && metrics.PreviousAnkle[side] is { } prior)
                {
                    float drift = (a - prior).Length;
                    metrics.MaxDrift[side] = Math.Max(metrics.MaxDrift[side], drift);
                    metrics.PlantedPairs[side]++;
                    Near(drift, 0, "straight actual full-blend support ankle cancels accepted body travel", .0006f);
                }
                metrics.PreviousAnkle[side] = a; metrics.PreviousPhase[side] = phase;
                metrics.PreviousSupport[side] = plantCheck && support;
                for (int node = 0; node < model.Nodes.Count; node++)
                    for (int ancestor = node; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                        if (ancestor == leg[2])
                        {
                            Near((picture[node].Row3.Xyz - a).Length,
                                (reference[node].Row3.Xyz - reference[leg[2]].Row3.Xyz).Length,
                                "foot descendants retain rigid offsets about the posed ankle");
                            Near(picture[node].Row0.Xyz.Length, reference[node].Row0.Xyz.Length, "foot shape retains its first axis scale");
                            Near(picture[node].Row1.Xyz.Length, reference[node].Row1.Xyz.Length, "foot shape retains its second axis scale");
                            Near(picture[node].Row2.Xyz.Length, reference[node].Row2.Xyz.Length, "foot shape retains its third axis scale");
                            break;
                        }
            }
            if (!requireIdleTarget)
                Check(picture.SequenceEqual(canonical), "zero walk amount returns to the exact native idle crouch picture");
            float[] pictureStack = model.MatrixStackValues.ToArray();
            Visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, 1f });
            Check(model.Nodes.Select(n => n.Animation).SequenceEqual(picture)
                && model.MatrixStackValues.SequenceEqual(pictureStack), "repeated dedicated-footwork picture is exactly identical");
            unchanged();
            Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
            Check(model.Nodes.Select(n => n.Animation).SequenceEqual(canonical)
                && model.MatrixStackValues.SequenceEqual(canonicalStack), "six-argument canonical native nodes and stack restore exactly");
            unchanged();
            Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out var view, out var eye, out var fov)
                && p.ModTryOwnerRenderPose(out _), "actual low walking camera and cannon accept a picture");
            p.ModTryOwnerRenderPose(out var beforeOwner);
            Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out var repeatView, out var repeatEye, out var repeatFov)
                && p.ModTryOwnerRenderPose(out var afterOwner) && view == repeatView && eye == repeatEye && fov == repeatFov
                && beforeOwner.GunPosition == afterOwner.GunPosition && beforeOwner.GunFacing == afterOwner.GunFacing,
                "repeat visual walking cannot add camera or weapon bob or advance owner picture state");
            if (p.CameraInfo.ModHasLiveDrawHistory(1))
            {
                var gait = (TraversalGaitPresentation)Field(p, "_modCrouchGait")!;
                float nativeBobCorrection = gait.SampleBob(1) * (TraversalGaitPresentation.WalkingBobScale - 1) * walk.Amount;
                Near(eye.Y, p.CameraInfo.Position.Y + nativeBobCorrection,
                    "procedural foot phase adds no extra eye bob beyond the declared native walking-bob reduction");
            }
            unchanged();
        }
        finally
        {
            for (int index = 0; index < saved.Length; index++) model.Nodes[index].Animation = saved[index];
            model.UpdateMatrixStack();
        }
    }

    private static Action Guard(Scene scene, PlayerEntity p, params AnimationInfo[] infos)
    {
        var collision = p.ModCaptureCollisionState();
        var body = (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
            p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset);
        var camera = (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix);
        var anchors = (p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob"));
        var clocks = (scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
            FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
            Field(p, "_modCrouchGait"), Walk.GetValue(p), Reach.GetValue(p),
            Field(p, "_modSlidePresentation"), Field(p, "_modCrouchPresentation"));
        Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var facts), "native source facts exist before walk sampling");
        var integers = infos.Select(i => i.Index.Concat(i.PrevIndex).Concat(i.Frame).Concat(i.FrameCount).Concat(i.Step).ToArray()).ToArray();
        var flags = infos.Select(i => i.Flags.ToArray()).ToArray();
        var groups = infos.Select(i => new object?[] { i.Node.Group, i.Material.Group, i.Texture.Group, i.Texcoord.Group }).ToArray();
        var slots = infos.Select(i => new[] { i.Node.Slot, i.Material.Slot, i.Texture.Slot, i.Texcoord.Slot }).ToArray();
        return () =>
        {
            Check(p.ModCaptureCollisionState() == collision
                && body == (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
                    p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset)
                && camera == (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix),
                "walk picture leaves physical collision, velocity, flags, stance, eye and native camera exact");
            Check(anchors.Equals((p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob")))
                && clocks.Equals((scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
                    FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
                    Field(p, "_modCrouchGait"), Walk.GetValue(p), Reach.GetValue(p),
                    Field(p, "_modSlidePresentation"), Field(p, "_modCrouchPresentation")))
                && p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var after) && after == facts,
                "walk picture leaves aim, muzzle, native bob, source facts, native clocks and completed gait/walk captures exact");
            for (int index = 0; index < infos.Length; index++)
            {
                AnimationInfo info = infos[index];
                Check(info.Index.Concat(info.PrevIndex).Concat(info.Frame).Concat(info.FrameCount).Concat(info.Step).SequenceEqual(integers[index])
                    && info.Flags.SequenceEqual(flags[index]) && new[] { info.Node.Slot, info.Material.Slot, info.Texture.Slot, info.Texcoord.Slot }.SequenceEqual(slots[index])
                    && new object?[] { info.Node.Group, info.Material.Group, info.Texture.Group, info.Texcoord.Group }.SequenceEqual(groups[index]),
                    "both canonical animation group/frame/step/flag/slot cursors remain exact");
            }
        };
    }

    private static void Raw(Model model, Node spine, Matrix4 world, float pitch,
        AnimationInfo lower, AnimationSamples.Sample sample, AnimationInfo upper)
    {
        bool ignore = spine.AnimIgnoreChild; Matrix4? after = spine.AfterTransform;
        spine.AnimIgnoreChild = true; spine.AfterTransform = Matrix4.CreateRotationZ(pitch);
        try { model.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, lower, sample); }
        finally { spine.AnimIgnoreChild = ignore; spine.AfterTransform = after; }
        model.AnimateNodes(spine.ChildIndex, false, Matrix4.Identity, Vector3.One, upper);
        foreach (Node node in model.Nodes) node.Animation *= world;
    }
}
