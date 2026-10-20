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

// Asset-backed CPU regression. The caller invokes
// Capture for all seven native Hunters. Initial placement is the only physical
// fixture write: actual W/Ctrl ticks earn the slide and its presentation clocks.
internal static class TraversalSlidePoseTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Canonical = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
    private static readonly MethodInfo Visual = typeof(PlayerEntity).GetMethod("UpdateHunterBipedVisual", Hidden)!;
    private static readonly MethodInfo LowerPose = typeof(PlayerEntity).GetMethod("ModCrouchLowerPose", Hidden)!;
    private static readonly FieldInfo Slide = typeof(PlayerEntity).GetField("_modSlidePresentation", Hidden)!;
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .0005f)
        => Check(float.IsFinite(a) && float.IsFinite(b) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");
    private static bool Finite(Matrix4 m) => new[] { m.Row0, m.Row1, m.Row2, m.Row3 }
        .All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W));
    private static object? Field(PlayerEntity p, string name) => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(p);
    private static TraversalSlidePresentation Clock(PlayerEntity p) => (TraversalSlidePresentation)Slide.GetValue(p)!;
    private static Matrix4[] Nodes(Model model) => model.Nodes.Select(n => n.Animation).ToArray();

    internal static object Capture(Hunter hunter)
    {
        _checks = 0;
        Check(hunter is >= Hunter.Samus and <= Hunter.Weavel, "fixture requires one of the seven native Hunters");
        object?[] arguments = { hunter, MovementModel.Traversal, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        int priorCap = FrameTiming.FrameRateCap;
        var rows = new List<object>();
        try
        {
            FrameTiming.FrameRateCap = 60;
            Check(!FrameTiming.HighRefreshPresentation, "CPU pose fixture samples completed picture endpoints");
            PlayerEntity p = scene.Players.Main!;
            p.Controls.Crouch.Key = Keys.LeftControl;
            Vector3 seed = new(0, .5f, 12);
            p.Reposition(seed, Vector3.UnitZ, NodeRef.None); p.PrevPosition = seed;
            for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            Check(p.Speed.X == 0 && p.Speed.Z == 0 && p.Flags1.TestFlag(PlayerFlags1.Standing),
                "explicit initial flat lane settles stationary on real support");
            SetKey(keyboard, Keys.W, true);
            for (int tick = 0; tick < 32; tick++) scene.OnSimulationFrame();
            Check(p.Speed.Xz.Length * PlayerEntity.NativeVelocityUnitsPerSecond >= TraversalMovementProfileV5.SlideMinimumSpeed,
                "actual W acceleration earns the speed required for a physical slide");
            Check(!p.ModSliding && !p.ModCrouched && Clock(p).Sample(1) == 0,
                "ordinary movement has not fabricated a low stance or slide timeline");
            var lods = (ModelInstance[])Field(p, "_bipedModelLods")!;
            Check(lods.Length == 2, "both native LODs are present");
            Check(TraversalSlidePresentation.ExitTicks == TraversalCrouchPresentation.TransitionTicks,
                "slide recovery ends with the accepted crouch recovery duration");
            var steadyLegs = new Vector3[2][];
            SetKey(keyboard, Keys.LeftControl, true);
            float previous = 0;
            for (int tick = 1; tick <= 14; tick++)
            {
                scene.OnSimulationFrame();
                Check(p.ModSliding && p.ModCrouched && p.Controls.Crouch.IsDown
                    && p.Controls.MoveUp.IsDown && p.Health > 0 && !p.IsAltForm
                    && p.Flags1.TestFlag(PlayerFlags1.Standing), "held W/Ctrl produces a genuine living grounded physical slide");
                if (tick == 1) Check(p.Controls.Crouch.IsPressed, "physical slide begins with an actual fresh Ctrl edge");
                var clock = Clock(p); float amount = clock.Sample(1);
                Near(clock.Sample(0), previous, "captured entry retains the previous completed envelope endpoint");
                Check(amount >= previous && amount is > 0 and <= 1, "actual slide entry is monotonic and bounded");
                Check(tick < TraversalSlidePresentation.EntryTicks
                    ? clock.Phase == TraversalSlidePhase.Entry && amount < 1
                    : clock.Phase == TraversalSlidePhase.Glide && amount == 1,
                    "actual input reaches full glide on the sixth captured tick and holds it");
                for (int lod = 0; lod < lods.Length; lod++)
                    rows.Add(Picture(scene, p, lods[lod].Model, hunter, lod, tick,
                        tick >= 8, steadyLegs));
                previous = amount;
            }
            // Real raw mouse samples change the body basis while slide
            // momentum continues on the flat lane. No facing, velocity, pose,
            // or cosmetic clock is fixture-authored for this comparison.
            Vector3 firstFacing = p.FacingVector.WithY(0).Normalized();
            Vector2 worldHeading = Clock(p).SampleDirection(1);
            p.Controls.MouseAim = true; p.Controls.KeyboardAim = false;
            var mouse = (MouseState)typeof(Scene).GetField("_mouseState", Hidden)!.GetValue(scene)!;
            var input = (PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input", Hidden)!.GetValue(p)!;
            var setPointer = typeof(MouseState).GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!
                .CreateDelegate<Action<MouseState, Vector2>>();
            Vector2 pointer = mouse.Position;
            for (int tick = 0; tick < 24; tick++)
            {
                Vector3 before = p.Position;
                pointer.X += 16; setPointer(mouse, pointer);
                scene.OnSimulationFrame();
                Check(p.ModSliding && input.MouseDeltaX == 16 && input.MouseDeltaY == 0,
                    "actual gathered mouse yaw turns during a genuinely held physical slide");
                Vector2 travel = (p.Position - before).Xz;
                Check(travel.LengthSquared > 1e-8f, "aim-turn slide continues measurable accepted world travel");
                travel.Normalize();
                Vector2 heading = Clock(p).SampleDirection(1);
                Check(Vector2.Dot(heading, travel) > .99f && Vector2.Dot(heading, worldHeading) > .99f,
                    "world slide heading follows momentum independently of actual aim yaw");
                Action unchanged = Guard(scene, p);
                Check(Clock(p).SampleDirection(.5f) == Clock(p).SampleDirection(.5f),
                    "repeated world-heading pictures remain read-only during real aim turns");
                unchanged();
                if (tick is 11 or 23)
                    for (int lod = 0; lod < lods.Length; lod++)
                        rows.Add(Picture(scene, p, lods[lod].Model, hunter, lod, tick,
                            true, steadyLegs, turning: true));
            }
            p.Controls.MouseAim = false; p.Controls.KeyboardAim = true;
            Check(Vector3.Dot(firstFacing, p.FacingVector.WithY(0).Normalized()) < .95f,
                "actual mouse input turned the body enough to expose a body-relative heading bug");
            SetKey(keyboard, Keys.LeftControl, false);
            for (int tick = 1; tick <= TraversalSlidePresentation.ExitTicks; tick++)
            {
                scene.OnSimulationFrame();
                Check(!p.ModSliding && !p.Controls.Crouch.IsDown && p.Controls.MoveUp.IsDown,
                    "actual Ctrl release cancels physical slide while forward input remains held");
                var clock = Clock(p); float amount = clock.Sample(1);
                Near(clock.Sample(0), previous, "actual release preserves the previous visual endpoint");
                Check(amount < previous && amount >= 0, "actual release exits monotonically without a new input edge");
                Check(tick < TraversalSlidePresentation.ExitTicks
                    ? clock.Phase == TraversalSlidePhase.Exit && amount > 0
                    : clock.Phase == TraversalSlidePhase.None && amount == 0,
                    "actual release retires the captured exit on the sixth tick");
                float crouchAmount = (float)typeof(PlayerEntity).GetProperty("ModCrouchVisualAmount", Hidden)!.GetValue(p)!;
                Check((crouchAmount > 0) == (amount > 0),
                    "visible slide recovery and crouch recovery end together without a hidden extra exit tail");
                Action unchanged = Guard(scene, p);
                foreach (float alpha in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    float sample = Clock(p).Sample(alpha);
                    Near(Clock(p).Sample(alpha), sample, "repeated actual exit samples are read-only");
                    Check(float.IsFinite(sample) && sample is >= 0 and <= 1, "actual exit interpolation stays finite and bounded");
                }
                unchanged(); previous = amount;
                rows.Add(new { phase = "release", tick, amount });
            }
            return new { hunter = hunter.ToString(), checks = _checks,
                scope = "Actual W32/Ctrl slide; both loaded native LODs; CPU geometry and source-invariance only", rows };
        }
        finally { FrameTiming.FrameRateCap = priorCap; MomentumSceneTests.Release(scene); }
    }

    private static object Picture(Scene scene, PlayerEntity p, Model model, Hunter hunter,
        int lod, int tick, bool steady, Vector3[][] steadyLegs, bool turning = false)
    {
        int Find(string name) => model.Nodes.Select((n, i) => (n, i)).First(pair => pair.n.Name == name).i;
        int spine = Find("Spine_1"), cannon = Find("R_elbow");
        int[] joints = { Find("L_hip"), Find("L_knee"), Find("L_ankle"),
            Find("R_hip"), Find("R_knee"), Find("R_ankle") };
        Matrix4 world = PlayerEntity.ModBuildBipedDrawTransform(p.Position, p.FacingVector,
            p.FacingVector.WithY(0), Metadata.HunterScales[hunter], p.ModBipedBottom, out float pitch);
        Matrix4[] saved = Nodes(model);
        var lower = (ModelInstance)Field(p, "_bipedModel1")!;
        var upper = (ModelInstance)Field(p, "_bipedModel2")!;
        try
        {
            Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
            Matrix4[] canonical = Nodes(model); float[] canonicalStack = model.MatrixStackValues.ToArray();
            Action unchanged = Guard(scene, p, lower.AnimInfo, upper.AnimInfo);
            object?[] lowerArguments = { model, null };
            var scratch = (AnimationInfo)LowerPose.Invoke(p, lowerArguments)!;
            var sample = (AnimationSamples.Sample)lowerArguments[1]!;
            Check(!ReferenceEquals(scratch, lower.AnimInfo) && scratch.NodeIndex == (int)PlayerAnimation.Idle
                && sample.FromFrame == 0 && sample.Fraction == 0,
                "actual physical glide samples independent native idle lower-body curves");
            bool walkingUpper = upper.AnimInfo.NodeIndex is (int)PlayerAnimation.WalkForward
                or (int)PlayerAnimation.WalkBackward or (int)PlayerAnimation.WalkLeft or (int)PlayerAnimation.WalkRight;
            Check(walkingUpper, "actual held forward input supplies the walking upper-channel comparison");
            // Measure the native Idle0 skeleton actually selected for this
            // visual pose. Kanden's animated walking clip changes joint offsets,
            // so the unrelated canonical Walk frame is not its length baseline.
            Raw(model, model.Nodes[spine], world, pitch, scratch, sample, upper.AnimInfo);
            Matrix4[] neutral = Nodes(model);
            float crouch = (float)typeof(PlayerEntity).GetProperty("ModCrouchVisualAmount", Hidden)!.GetValue(p)!;
            void Draw() => Visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, crouch });
            Draw(); Matrix4[] picture = Nodes(model); float[] pictureStack = model.MatrixStackValues.ToArray();
            Check(picture.All(Finite) && pictureStack.All(float.IsFinite), "actual slide geometry and submitted matrix stack are finite");
            Near(OrientationError(picture[cannon], neutral[cannon]), 0,
                "slide counter-lean retains actual native upper-channel cannon aim through every entry/turn sample");
            float maxLengthError = 0;
            for (int side = 0; side < 2; side++)
                for (int segment = 0; segment < 2; segment++)
                {
                    int a = joints[side * 3 + segment], b = joints[side * 3 + segment + 1];
                    float nativeLength = (neutral[b].Row3.Xyz - neutral[a].Row3.Xyz).Length;
                    float slideLength = (picture[b].Row3.Xyz - picture[a].Row3.Xyz).Length;
                    Check(nativeLength > 0, "native leg segment has a positive measurable length");
                    Near(slideLength, nativeLength, "visual slide preserves each native thigh and shin length");
                    maxLengthError = Math.Max(maxLengthError, MathF.Abs(slideLength - nativeLength));
                }
            Draw();
            Check(Nodes(model).SequenceEqual(picture) && model.MatrixStackValues.SequenceEqual(pictureStack),
                "repeated visual pictures cannot advance clocks or accumulate a pose deformation");
            unchanged();
            float leftAdvance = 0, rightAdvance = 0, torsoRotation = 0;
            if (steady)
            {
                Vector3[] local = joints.Select(index => picture[index].Row3.Xyz - world.Row3.Xyz).ToArray();
                if (!turning)
                {
                    if (steadyLegs[lod] is { } first)
                        for (int joint = 0; joint < local.Length; joint++)
                            Near((local[joint] - first[joint]).Length, 0, "steady glide does not pedal either leg as native clocks advance");
                    else steadyLegs[lod] = local;
                }

                // Cosmetic comparison seam only: replace the presentation
                // envelope with zero around the SAME production Visual call.
                // Both calls keep the same idle lower curves and native upper
                // animation/aim channel. Restore the captured timeline
                // in finally. Physical timers, speed, and node poses are never
                // fixture-authored; every node is composed by production code.
                object capturedClock = Slide.GetValue(p)!;
                Matrix4[] control;
                try
                {
                    Slide.SetValue(p, default(TraversalSlidePresentation));
                    Draw(); control = Nodes(model);
                }
                finally { Slide.SetValue(p, capturedClock); }
                Draw();
                Check(Nodes(model).SequenceEqual(picture), "restoring the cosmetic timeline restores the identical slide picture");
                Vector2 heading = Clock(p).SampleDirection(1);
                Vector3 travel = new(heading.X, 0, heading.Y);
                leftAdvance = Vector3.Dot(picture[joints[2]].Row3.Xyz - control[joints[2]].Row3.Xyz, travel);
                rightAdvance = Vector3.Dot(picture[joints[5]].Row3.Xyz - control[joints[5]].Row3.Xyz, travel);
                Check(leftAdvance > .005f && rightAdvance < -.005f && leftAdvance - rightAdvance > .02f,
                    "steady glide extends the left ankle and tucks the right ankle asymmetrically along travel");
                torsoRotation = OrientationError(picture[spine], control[spine]);
                Check(torsoRotation > .01f, "steady glide visibly rotates the torso into back lean");
                Near(OrientationError(picture[cannon], control[cannon]), 0,
                    "cannon orientation stays independent of torso back lean on identical native aim curves");
                unchanged();
            }
            Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
            Check(Nodes(model).SequenceEqual(canonical) && model.MatrixStackValues.SequenceEqual(canonicalStack),
                "canonical nodes and matrix stack restore exactly after slide and cosmetic control pictures");
            unchanged();
            bool prepared = p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out Matrix4 view, out Vector3 eye, out float fov);
            bool ownerReady = p.ModTryOwnerRenderPose(out var owner);
            Check(prepared && ownerReady, "actual sliding camera and cannon accept a presentation picture");
            Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out Matrix4 repeatedView, out Vector3 repeatedEye, out float repeatedFov)
                && p.ModTryOwnerRenderPose(out var repeatedOwner) && view == repeatedView && eye == repeatedEye
                && fov == repeatedFov && owner.GunPosition == repeatedOwner.GunPosition
                && owner.GunFacing == repeatedOwner.GunFacing && owner.GunUp == repeatedOwner.GunUp,
                "repeated camera/cannon pictures are identical without moving source-time anchors");
            unchanged();
            return new { phase = "entry-glide", lod, tick, amount = Clock(p).Sample(1), maxLengthError,
                steady, turning, leftAdvance, rightAdvance, torsoRotation };
        }
        finally
        {
            for (int node = 0; node < saved.Length; node++) model.Nodes[node].Animation = saved[node];
            model.UpdateMatrixStack();
        }
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

    private static float OrientationError(Matrix4 a, Matrix4 b)
        => Math.Max((a.Row0.Xyz.Normalized() - b.Row0.Xyz.Normalized()).Length,
            Math.Max((a.Row1.Xyz.Normalized() - b.Row1.Xyz.Normalized()).Length,
                (a.Row2.Xyz.Normalized() - b.Row2.Xyz.Normalized()).Length));

    private static Action Guard(Scene scene, PlayerEntity p, params AnimationInfo[] infos)
    {
        var collision = p.ModCaptureCollisionState();
        var body = (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
            p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset);
        var camera = (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix);
        var anchors = (p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob"));
        var clocks = (scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
            FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
            Field(p, "_modCrouchGait"), Slide.GetValue(p), Field(p, "_modCrouchPresentation"),
            stance: (Field(p, "_modSlideTicks"), Field(p, "_modSlideEntryTicks"), Field(p, "_modStanceFrame")));
        Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var facts), "actual slide has canonical source-time weapon facts");
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
                "slide pictures preserve collision, velocity, flags, physical stance and source camera exactly");
            Check(anchors.Equals((p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob")))
                && clocks.Equals((scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames,
                    FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
                    Field(p, "_modCrouchGait"), Slide.GetValue(p), Field(p, "_modCrouchPresentation"),
                    (Field(p, "_modSlideTicks"), Field(p, "_modSlideEntryTicks"), Field(p, "_modStanceFrame"))))
                && p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var after) && after == facts,
                "slide pictures preserve muzzle, aim, bob, source facts and simulation/presentation clocks exactly");
            for (int index = 0; index < infos.Length; index++)
            {
                AnimationInfo info = infos[index];
                Check(info.Index.Concat(info.PrevIndex).Concat(info.Frame).Concat(info.FrameCount).Concat(info.Step).SequenceEqual(integers[index])
                    && info.Flags.SequenceEqual(flags[index]) && new[] { info.Node.Slot, info.Material.Slot, info.Texture.Slot, info.Texcoord.Slot }.SequenceEqual(slots[index])
                    && new object?[] { info.Node.Group, info.Material.Group, info.Texture.Group, info.Texcoord.Group }.SequenceEqual(groups[index]),
                    "visual slide keeps both native animation channels and all canonical cursors exact");
            }
        };
    }
}
