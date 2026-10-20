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

// CPU measurements of the loaded native assets. Only initial lane placement and
// presentation scratch are fixture-owned; movement/stance/animation clocks are
// earned with actual keyboard ticks. This does not claim world-space foot locks.
internal static class TraversalCrouchGaitTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Canonical = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
    private static readonly MethodInfo Visual = typeof(PlayerEntity).GetMethod("UpdateHunterBipedVisual", Hidden)!;
    private static readonly MethodInfo LowerPose = typeof(PlayerEntity).GetMethod("ModCrouchLowerPose", Hidden)!;
    private static readonly PropertyInfo Active = typeof(PlayerEntity).GetProperty("ModCrouchGaitActive", Hidden)!;
    private static readonly FieldInfo Gait = typeof(PlayerEntity).GetField("_modCrouchGait", Hidden)!;
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .0003f)
        => Check(float.IsFinite(a) && float.IsFinite(b) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");
    private static bool Finite(Matrix4 m) => new[] { m.Row0, m.Row1, m.Row2, m.Row3 }
        .All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W));
    private static float Wrap(float frame, int count) => (frame % count + count) % count;

    internal static int CheckMath()
    {
        _checks = 0;
        var clock = new TraversalGaitPresentation();
        Check(!clock.Ready && !clock.Moving && !clock.Gliding, "fresh gait has no retained travel or slide");
        clock.Capture(10, Vector3.Zero, true, false, 19, 20, 2, 4);
        Near(clock.SampleFrame(1, 20), 19, "initial gait starts at the actual native phase");
        float cycle = TraversalMovementProfileV4.GroundSpeed * TraversalGaitPresentation.StrideScale * 20 / 30;
        Vector3 position = Vector3.Zero; float expected = 19;
        bool wrapped = false;
        for (ulong tick = 11; tick < 211; tick++)
        {
            Vector3 direction = tick < 70 ? Vector3.UnitZ : tick < 140 ? -Vector3.UnitZ : Vector3.UnitX;
            position += direction * .02f;
            float prior = expected;
            expected = Wrap(expected + .02f / cycle * 20, 20);
            wrapped |= expected < prior;
            clock.Capture(tick, position, true, false, (int)(tick % 20), 20, 6, 12);
            Check(clock.Ready && clock.Moving && !clock.Gliding, "ordinary travel stays a walk in all directions");
            Near(clock.SampleFrame(1, 20), expected, "gait phase follows accepted distance across direction changes", .001f);
            Near(clock.SampleFrame(.5f, 20), Wrap(prior + .01f / cycle * 20, 20),
                "fractional sampling follows the forward arc across a loop seam", .001f);
            float sampled = clock.SampleFrame(.5f, 20);
            for (int repeat = 0; repeat < 3; repeat++)
                Near(clock.SampleFrame(.5f, 20), sampled, "repeated pictures do not advance travel phase");
        }
        Check(wrapped, "the tested real-distance trace crosses an actual cycle boundary");
        float before = clock.SampleFrame(1, 20);
        clock.Capture(210, new(999, 999, 999), false, true, 0, 1, 100, 100);
        Near(clock.SampleFrame(1, 20), before, "duplicate frame capture cannot replace an accepted gait tick");
        Check(clock.Moving && !clock.Gliding, "duplicate capture cannot change slide classification");
        clock.Capture(211, position.AddY(2), true, false, 0, 20, 10, 20);
        Check(!clock.Moving && !clock.Gliding, "vertical motion alone cannot spend horizontal gait phase");
        Near(clock.SampleFrame(1, 20), before, "stationary walking clip does not cycle feet");
        Near(clock.SampleBob(.25f), 7, "camera bob interpolates captured endpoints");
        Near(clock.SampleBob(.25f, true), 14, "gun bob uses its separate captured endpoints");
        Near(clock.SampleBob(float.NaN), 10, "nonfinite picture alpha falls back to completed camera bob");
        Near(clock.SampleBob(-1), 6, "negative alpha clamps to prior bob");
        Near(clock.SampleBob(2), 10, "oversized alpha clamps to completed bob");
        position = position.AddY(2) + Vector3.UnitZ * .02f;
        clock.Capture(212, position, true, true, 4, 20, 0, 0);
        Check(clock.Gliding && clock.Moving, "accepted slide is visually classified as a moving glide");
        Near(clock.SampleFrame(1, 20), before, "slide travel does not advance a walking cycle");
        position += Vector3.UnitZ * .1f;
        clock.Capture(213, position, true, false, 5, 20, 0, 0);
        Check(clock.Gliding, "fast low travel can show a glide without granting a physical slide");
        clock.Capture(220, position, true, false, 7, 20, 3, 9);
        Near(clock.SampleFrame(.5f, 20), 7, "missing simulation ticks reseed from the actual native frame");
        clock.Capture(221, position + Vector3.UnitZ * 5, true, false, 3, 20, 5, 15);
        Check(!clock.Moving, "teleport-sized discontinuity is not spent as walking distance");
        Near(clock.SampleFrame(1, 20), 3, "teleport reseeds the completed native phase");
        clock.Capture(222, new(float.NaN, 0, 0), true, false, 2, 20, 2, 6);
        Near(clock.SampleFrame(.5f, 20), 2, "nonfinite position reseeds without poisoning the cosmetic clock");
        clock.Capture(223, Vector3.Zero, false, false, 8, 20, 0, 0);
        clock.Capture(224, Vector3.UnitZ * .02f, true, false, 4, 20, 0, 0);
        Near(clock.SampleFrame(0, 20), 4, "entering a walk begins from that native clip phase");
        Check(float.IsFinite(clock.SampleFrame(float.NaN, 20)) && clock.SampleFrame(1, 0) == 0,
            "invalid picture alpha and empty clips have finite frame fallbacks");
        clock.Reset(); Check(!clock.Ready && !clock.Moving && !clock.Gliding, "reset clears every cosmetic clock classification");
        clock.Capture(1, Vector3.Zero, true, false, 6, 20, 2, 4);
        Near(clock.SampleFrame(1, 20), 6, "reset starts from the new accepted native clip");

        float sum = 0;
        for (ulong tick = 0; tick < 10000; tick++) Warm(tick);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 10000; tick < 110000; tick++) Warm(tick);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0 && float.IsFinite(sum) && sum > 0,
            "warmed gait capture and repeated sampling allocate zero managed bytes");
        return _checks;

        void Warm(ulong tick)
        {
            float x = tick % 200 < 100 ? tick % 100 * .02f : (100 - tick % 100) * .02f;
            clock.Capture(tick, new(x, 0, 0), true, false, (int)(tick % 20), 20, 1, 2);
            sum += clock.SampleFrame(.25f, 20) + clock.SampleFrame(.75f, 20)
                + clock.SampleBob(.25f) + clock.SampleBob(.75f, true);
        }
    }

    internal static object Capture(Hunter hunter)
    {
        _checks = 0; var rows = new List<object>();
        foreach (var direction in new[] { (Keys.W, PlayerAnimation.WalkForward), (Keys.S, PlayerAnimation.WalkBackward),
            (Keys.A, PlayerAnimation.WalkLeft), (Keys.D, PlayerAnimation.WalkRight) })
            rows.Add(CaptureDirection(hunter, direction.Item1, direction.Item2, slide: false));
        rows.Add(CaptureDirection(hunter, Keys.W, PlayerAnimation.WalkForward, slide: true));
        return new { hunter = hunter.ToString(), checks = _checks,
            scope = "Real Ctrl and directional movement; full loaded native clip cycles on both LODs; measured drift, no world-foot-lock claim; CPU only", rows };
    }

    private sealed class Envelope
    {
        internal Vector3 Min = new(float.PositiveInfinity), Max = new(float.NegativeInfinity);
        internal void Add(Vector3 v) { Min = Vector3.ComponentMin(Min, v); Max = Vector3.ComponentMax(Max, v); }
    }

    private static object CaptureDirection(Hunter hunter, Keys key, PlayerAnimation clip, bool slide)
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
            // One initial fixture placement on the empty front-floor lane. All
            // subsequent travel, speed, crouch and slide state come from input.
            Vector3 seed = new(0, .5f, 12);
            p.Reposition(seed, Vector3.UnitZ, NodeRef.None); p.PrevPosition = seed;
            for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            Check(p.Speed.X == 0 && p.Speed.Z == 0 && p.Flags1.TestFlag(PlayerFlags1.Standing),
                "gait lane begins stationary on actual floor support");
            if (slide)
            {
                SetKey(keyboard, key, true);
                for (int tick = 0; tick < 32; tick++) scene.OnSimulationFrame();
                Check(p.Speed.Xz.Length * 30 >= TraversalMovementProfileV4.SlideMinimumSpeed,
                    "ordinary held W earns slide entry before the fresh Ctrl edge");
            }
            SetKey(keyboard, Keys.LeftControl, true);
            for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
            SetKey(keyboard, key, true);
            if (!slide) for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            var lower = (ModelInstance)Field(p, "_bipedModel1")!;
            var upper = (ModelInstance)Field(p, "_bipedModel2")!;
            var lods = (ModelInstance[])Field(p, "_bipedModelLods")!;
            int count = lower.AnimInfo.Node.Group!.FrameCount;
            Check(lower.AnimInfo.NodeIndex == (int)clip && count > 1 && count < 150,
                "actual directional keyboard input selects a finite native walking clip");
            var canonicalFrames = new HashSet<int>(); var visualFrames = new HashSet<int>();
            var nativeBounds = new Envelope[2, 2]; var visualBounds = new Envelope[2, 2];
            var previousRaw = new Vector3?[2, 2]; var previousVisual = new Vector3?[2, 2];
            var neutralFeet = new Vector3[2, 2];
            var nativeLift = new float[2, 2]; var visualLift = new float[2, 2];
            var firstSlide = new Vector3?[2, 6];
            float maxDrift = 0, maxTravel = 0, maxThighError = 0, maxShinError = 0;
            int correctedBob = 0, samples = 0;
            Vector3 previousPosition = p.Position;
            for (int lod = 0; lod < 2; lod++) for (int side = 0; side < 2; side++)
            { nativeBounds[lod, side] = new(); visualBounds[lod, side] = new(); }
            int limit = slide ? 24 : count * 4 + 32;
            for (int tick = 0; tick < limit; tick++)
            {
                if (tick > 0) scene.OnSimulationFrame();
                Check(p.ModCrouched && p.Controls.Crouch.IsDown && p.Health > 0 && !p.IsAltForm
                    && p.Flags1.TestFlag(PlayerFlags1.Standing) && p.ModSliding == slide,
                    "measured gait retains genuine low stance and actual supported movement");
                Check((bool)Active.GetValue(p)!, "actual completed movement activates the render-only gait");
                var gait = (TraversalGaitPresentation)Gait.GetValue(p)!;
                Check(gait.Moving && gait.Gliding == slide, "actual low movement has the expected walking/gliding classification");
                canonicalFrames.Add(lower.AnimInfo.NodeFrame);
                float travel = (p.Position - previousPosition).Xz.Length;
                maxTravel = Math.Max(maxTravel, travel);
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    Model model = lods[lod].Model;
                    int Find(string name) => model.Nodes.Select((n, i) => (n, i)).First(pair => pair.n.Name == name).i;
                    int spine = Find("Spine_1");
                    int[][] legs = { new[] { Find("L_hip"), Find("L_knee"), Find("L_ankle") },
                        new[] { Find("R_hip"), Find("R_knee"), Find("R_ankle") } };
                    Matrix4 world = PlayerEntity.ModBuildBipedDrawTransform(p.Position, p.FacingVector,
                        p.FacingVector.WithY(0), Metadata.HunterScales[hunter], p.ModBipedBottom, out float pitch);
                    Matrix4[] saved = model.Nodes.Select(n => n.Animation).ToArray();
                    try
                    {
                        Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
                        Matrix4[] canonical = model.Nodes.Select(n => n.Animation).ToArray();
                        float[] canonicalStack = model.MatrixStackValues.ToArray();
                        Action unchanged = Guard(scene, p, lower.AnimInfo, upper.AnimInfo);
                        object?[] lowerArgs = { model, null };
                        var scratch = (AnimationInfo)LowerPose.Invoke(p, lowerArgs)!;
                        var sample = (AnimationSamples.Sample)lowerArgs[1]!;
                        Check(!ReferenceEquals(scratch, lower.AnimInfo), "picture phase belongs to independent scratch animation info");
                        Check(scratch.NodeIndex == (slide ? (int)PlayerAnimation.Idle : (int)clip),
                            "slide uses native idle lower body while walking retains the selected directional clip");
                        if (slide) Check(sample.FromFrame == 0 && sample.Fraction == 0,
                            "held W slide has a stable frame-zero lower pose");
                        else visualFrames.Add(sample.FromFrame);
                        if (tick == 0)
                        {
                            var idle = new AnimationInfo();
                            idle.Node.Group = model.AnimationGroups.Node[(int)PlayerAnimation.Idle];
                            Raw(model, model.Nodes[spine], world, pitch, idle, new(0, 0, 0), upper.AnimInfo);
                            for (int side = 0; side < 2; side++)
                                neutralFeet[lod, side] = model.Nodes[legs[side][2]].Animation.Row3.Xyz - world.Row3.Xyz;
                        }
                        Raw(model, model.Nodes[spine], world, pitch, scratch, sample, upper.AnimInfo);
                        Matrix4[] raw = model.Nodes.Select(n => n.Animation).ToArray();
                        Visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, 1f });
                        Matrix4[] picture = model.Nodes.Select(n => n.Animation).ToArray();
                        Check(picture.All(Finite) && model.MatrixStackValues.All(float.IsFinite),
                            "entire native gait produces finite joints and submitted matrices");
                        for (int side = 0; side < 2; side++)
                        {
                            int[] leg = legs[side];
                            Vector3 hip = raw[leg[0]].Row3.Xyz, knee = raw[leg[1]].Row3.Xyz, ankle = raw[leg[2]].Row3.Xyz;
                            Vector3 h = picture[leg[0]].Row3.Xyz, k = picture[leg[1]].Row3.Xyz, a = picture[leg[2]].Row3.Xyz;
                            float thighError = MathF.Abs((k - h).Length - (knee - hip).Length);
                            float shinError = MathF.Abs((a - k).Length - (ankle - knee).Length);
                            maxThighError = Math.Max(maxThighError, thighError); maxShinError = Math.Max(maxShinError, shinError);
                            Near((k - h).Length, (knee - hip).Length, "full-cycle crouch gait preserves native thigh length");
                            Near((a - k).Length, (ankle - knee).Length, "full-cycle crouch gait preserves native shin length");
                            Vector3 nativeLocal = ankle - world.Row3.Xyz, visualLocal = a - world.Row3.Xyz;
                            nativeBounds[lod, side].Add(nativeLocal); visualBounds[lod, side].Add(visualLocal);
                            nativeLift[lod, side] = Math.Max(nativeLift[lod, side], nativeLocal.Y - neutralFeet[lod, side].Y);
                            visualLift[lod, side] = Math.Max(visualLift[lod, side], visualLocal.Y - neutralFeet[lod, side].Y);
                            if (previousVisual[lod, side] is { } prior && previousRaw[lod, side] is { } priorRaw)
                            {
                                float drift = (a - prior).Xz.Length; maxDrift = Math.Max(maxDrift, drift);
                                float bound = travel + (nativeLocal - priorRaw).Xz.Length * TraversalGaitPresentation.StrideScale;
                                Check(drift <= bound + .0005f,
                                    "adjacent foot drift is bounded by accepted body travel plus the shortened native excursion");
                            }
                            previousVisual[lod, side] = a; previousRaw[lod, side] = nativeLocal;
                            if (slide) for (int joint = 0; joint < 3; joint++)
                            {
                                Vector3 local = picture[leg[joint]].Row3.Xyz - world.Row3.Xyz;
                                int slot = side * 3 + joint;
                                if (firstSlide[lod, slot] is { } initial)
                                    Near((local - initial).Length, 0, "held W glide does not pedal hips, knees or feet");
                                firstSlide[lod, slot] = local;
                            }
                            Vector3 offset = a - ankle;
                            for (int node = 0; node < model.Nodes.Count; node++)
                                for (int ancestor = node; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                                    if (ancestor == leg[2])
                                    {
                                        Matrix4 expectedFoot = raw[node] * Matrix4.CreateTranslation(offset);
                                        Near((picture[node].Row3.Xyz - expectedFoot.Row3.Xyz).Length, 0,
                                            "foot descendants follow one coherent ankle translation");
                                        Check(picture[node].Row0 == raw[node].Row0 && picture[node].Row1 == raw[node].Row1
                                            && picture[node].Row2 == raw[node].Row2,
                                            "shorter gait retains each native foot's orientation and shape");
                                        break;
                                    }
                        }
                        float[] pictureStack = model.MatrixStackValues.ToArray();
                        Visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, 1f });
                        Check(model.Nodes.Select(n => n.Animation).SequenceEqual(picture)
                            && model.MatrixStackValues.SequenceEqual(pictureStack), "repeated gait picture is exactly identical");
                        unchanged();
                        Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
                        Check(model.Nodes.Select(n => n.Animation).SequenceEqual(canonical)
                            && model.MatrixStackValues.SequenceEqual(canonicalStack),
                            "canonical native nodes and matrix stack restore exactly after each gait picture");
                        unchanged();
                    }
                    finally
                    {
                        for (int node = 0; node < saved.Length; node++) model.Nodes[node].Animation = saved[node];
                        model.UpdateMatrixStack();
                    }
                }
                Action cameraUnchanged = Guard(scene, p, lower.AnimInfo, upper.AnimInfo);
                Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out Matrix4 view, out Vector3 eye, out float fov)
                    && p.ModTryOwnerRenderPose(out _), "actual walking/gliding camera and gun accept a rendered picture");
                p.ModTryOwnerRenderPose(out var ownerPose);
                Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out Matrix4 repeatView, out Vector3 repeatEye, out float repeatFov)
                    && p.ModTryOwnerRenderPose(out var repeatPose) && view == repeatView && eye == repeatEye && fov == repeatFov
                    && ownerPose.GunPosition == repeatPose.GunPosition && ownerPose.GunFacing == repeatPose.GunFacing,
                    "repeated crouch gait camera and gun sampling cannot advance the picture");
                cameraUnchanged();
                float bob = (float)typeof(PlayerEntity).GetMethod("ModCrouchDrawBobCorrection", Hidden)!
                    .Invoke(p, new object[] { 1d, false })!;
                if (p.CameraInfo.ModHasLiveDrawHistory(1))
                {
                    float expectedBob = gait.SampleBob(1) * (slide ? -1 : TraversalGaitPresentation.WalkingBobScale - 1);
                    Near(bob, expectedBob, "render camera removes only the declared captured walking/glide bob contribution");
                    Near(gait.SampleBob(1), (float)Field(p, "_modTraversalCameraWalkBob")!,
                        "gait uses the exact bob contribution already applied by the native camera tick");
                    Near(eye.Y, p.CameraInfo.Position.Y + expectedBob,
                        "actual rendered eye applies the reduced bob while leaving native CameraInfo unchanged");
                    float gunBob = (float)typeof(PlayerEntity).GetMethod("ModCrouchDrawBobCorrection", Hidden)!
                        .Invoke(p, new object[] { 1d, true })!;
                    Near(gunBob, gait.SampleBob(1, true) * (slide ? -1 : TraversalGaitPresentation.WalkingBobScale - 1),
                        "cannon receives its own captured render-only bob reduction");
                    if (MathF.Abs(bob) > .000001f) correctedBob++;
                }
                previousPosition = p.Position; samples++;
                if (!slide && canonicalFrames.Count == count && visualFrames.Count == count) break;
            }
            Check(slide || canonicalFrames.Count == count && visualFrames.Count == count,
                "actual keyboard trace covers every native frame and every retimed visual frame in the complete loop");
            Check(correctedBob > 0, "actual movement exercised a nonzero camera bob correction");
            var feet = new List<object>();
            for (int lod = 0; lod < 2; lod++) for (int side = 0; side < 2; side++)
            {
                Vector3 rawRange = nativeBounds[lod, side].Max - nativeBounds[lod, side].Min;
                Vector3 visualRange = visualBounds[lod, side].Max - visualBounds[lod, side].Min;
                if (!slide)
                {
                    Check(rawRange.Xz.Length > .001f, "complete real native walk has a measurable foot excursion");
                    Near(visualRange.X, rawRange.X * TraversalGaitPresentation.StrideScale, "full-cycle lateral stride is shorter");
                    Near(visualRange.Z, rawRange.Z * TraversalGaitPresentation.StrideScale, "full-cycle longitudinal stride is shorter");
                    Check(visualRange.Y <= rawRange.Y + .0005f, "lower swing cannot increase the full-cycle foot-height envelope");
                    Near(visualLift[lod, side], nativeLift[lod, side] * TraversalGaitPresentation.SwingLiftScale,
                        "full-cycle peak swing above the native neutral foot is reduced to the declared lift");
                }
                feet.Add(new { lod, side, nativeRange = new[] { rawRange.X, rawRange.Y, rawRange.Z },
                    visualRange = new[] { visualRange.X, visualRange.Y, visualRange.Z },
                    nativeLift = nativeLift[lod, side], visualLift = visualLift[lod, side] });
            }
            return new { direction = key.ToString(), slide, nativeClip = clip.ToString(), frameCount = count, samples,
                nativeFrames = canonicalFrames.Count, retimedFrames = visualFrames.Count, maxFootWorldDrift = maxDrift,
                maxAcceptedTravel = maxTravel, maxThighError, maxShinError, correctedBobPictures = correctedBob, feet };
        }
        finally { FrameTiming.FrameRateCap = priorCap; MomentumSceneTests.Release(scene); }
    }

    private static object? Field(PlayerEntity p, string name) => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(p);

    private static Action Guard(Scene scene, PlayerEntity p, params AnimationInfo[] infos)
    {
        var collision = p.ModCaptureCollisionState();
        var body = (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
            p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset);
        var camera = (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix);
        var anchors = (p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob"));
        var clock = (scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
            FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha, Gait.GetValue(p));
        Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var facts), "native source facts exist before gait sampling");
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
                "gait picture leaves collision, velocity, native source flags, stance, eye and camera exact");
            Check(anchors.Equals((p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob")))
                && clock.Equals((scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
                    FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha, Gait.GetValue(p)))
                && p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var after) && after == facts,
                "gait picture leaves aim, muzzle, native bob, source facts and all frame/gait clocks exact");
            for (int index = 0; index < infos.Length; index++)
            {
                AnimationInfo i = infos[index];
                Check(i.Index.Concat(i.PrevIndex).Concat(i.Frame).Concat(i.FrameCount).Concat(i.Step).SequenceEqual(integers[index])
                    && i.Flags.SequenceEqual(flags[index]) && new[] { i.Node.Slot, i.Material.Slot, i.Texture.Slot, i.Texcoord.Slot }.SequenceEqual(slots[index])
                    && new object?[] { i.Node.Group, i.Material.Group, i.Texture.Group, i.Texcoord.Group }.SequenceEqual(groups[index]),
                    "lower and upper canonical animation groups, frames, flags, slots and steps remain exact");
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
        bool walkingUpper = upper.NodeIndex is (int)PlayerAnimation.WalkForward or (int)PlayerAnimation.WalkBackward
            or (int)PlayerAnimation.WalkLeft or (int)PlayerAnimation.WalkRight;
        model.AnimateNodes(spine.ChildIndex, false, Matrix4.Identity, Vector3.One,
            walkingUpper ? lower : upper, walkingUpper ? sample : null);
        foreach (Node node in model.Nodes) node.Animation *= world;
    }
}
