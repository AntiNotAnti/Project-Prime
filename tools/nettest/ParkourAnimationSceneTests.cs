using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Positive animation inputs come only from actual accepted keyboard movement.
// Native matrices are borrowed as CPU presentation scratch, as in the existing
// crouch/slide asset probes. No controller, accepted state, cursor or clock writes.
internal static class ParkourAnimationSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Canonical = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static readonly FieldInfo[] Controllers = typeof(PlayerEntity).GetFields(Hidden)
        .Where(f => f.Name.StartsWith("_modMantle", StringComparison.Ordinal)
            || f.Name.StartsWith("_modGrapple", StringComparison.Ordinal)).ToArray();
    private static int _checks;
    private static string _scenario = "";
    private static readonly List<object> Rows = new();
    private static readonly HashSet<string> AllocationSamples = new();
    private static readonly HashSet<string> OwnerCues = new();

    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException(_scenario + ": " + label); }
    private static void Near(float actual, float expected, string label, float tolerance = .0006f)
        => Check(float.IsFinite(actual) && float.IsFinite(expected) && MathF.Abs(actual - expected) <= tolerance,
            $"{label} ({actual} versus {expected})");
    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y)
        && float.IsFinite(v.Z) && float.IsFinite(v.W);
    private static bool Finite(Matrix4 m) => Finite(m.Row0) && Finite(m.Row1) && Finite(m.Row2) && Finite(m.Row3);
    private static object? Field(PlayerEntity p, string name) => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(p);
    private static Matrix4[] Nodes(Model model) => model.Nodes.Select(n => n.Animation).ToArray();

    internal static int Run(string? output = null)
    {
        _checks = 0; Rows.Clear(); AllocationSamples.Clear(); OwnerCues.Clear(); var failures = new List<string>();
        int priorCap = FrameTiming.FrameRateCap;
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording, "fresh offline scene");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            FrameTiming.FrameRateCap = 60;
            Check(!FrameTiming.HighRefreshPresentation, "CPU native pose samples use completed fixed-tick endpoints");
            void Case(string name, Action action)
            {
                _scenario = name;
                try { action(); Console.WriteLine("PARKOUR ANIMATION SCENE PASS " + name); }
                catch (Exception error)
                { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR ANIMATION SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                Case(hunter + "/accepted-mantle", () => Mantle(hunter));
                Case(hunter + "/accepted-grapple", () => Grapple(hunter, MovementModel.Parkour));
            }
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/excluded-profile", () => Grapple(Hunter.Samus, model));
            Check(AllocationSamples.Count == 4, "both native LODs run actual mantle and grapple warmed allocation probes");
            Check(OwnerCues.Count == 14, "all seven native Hunters show real owner first-person cannon cues for mantle and grapple");
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally { FrameTiming.FrameRateCap = priorCap; }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures,
                scope = "CPU actual native seven-Hunter/two-LOD animation matrices and weighted palettes; genuine W/fresh Space/G accepted owner movement; initial placement only; no controller/cursor/clock injection, GL, screenshots, remote or replay qualification",
                rows = Rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR ANIMATION SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly PlayerEntity Player;
        internal readonly KeyboardState Keyboard;
        internal readonly ModelInstance[] Lods;
        internal Fixture(Hunter hunter, MovementModel model, bool mantle)
        {
            object?[] args = { hunter, model, null };
            Scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, args)!;
            try
            {
                Player = Scene.Players.Main!; Keyboard = (KeyboardState)args[2]!;
                Player.Controls.Grapple.Key = Keys.G;
                Vector3 seed = mantle ? new(-30, .5f, 14) : new(-42, .5f, 25);
                Player.Reposition(seed, Vector3.UnitZ, NodeRef.None);
                Player.PrevPosition = seed; Player.ModSetSpawnFacing(Vector3.UnitZ);
                for (int i = 0; i < 12; i++) Scene.OnSimulationFrame();
                Check(Player.Flags1.TestFlag(PlayerFlags1.Standing) && Player.Health > 0,
                    "initial labeled fixture settles on actual native floor");
                Lods = (ModelInstance[])Field(Player, "_bipedModelLods")!;
                Check(Lods.Length == 2 && !ReferenceEquals(Lods[0].Model, Lods[1].Model), "two distinct native LOD models loaded");
                foreach (var lod in Lods) ParkourPoseOverlay.Prewarm(lod.Model);
            }
            catch { MomentumSceneTests.Release(Scene); throw; }
        }
        internal ulong Tick(bool forward = false, bool jump = false, bool grapple = false)
        {
            SetKey(Keyboard, Keys.W, forward); SetKey(Keyboard, Keys.Space, jump); SetKey(Keyboard, Keys.G, grapple);
            ulong frame = Scene.FrameCount; Scene.OnSimulationFrame();
            Check(Player.Controls.MoveUp.IsDown == forward && Player.Controls.Jump.IsDown == jump
                && Player.Controls.Grapple.IsDown == grapple, "actual production keyboard levels match authored input");
            Check(Player.Health > 0 && Player.ModIsInPlay && !Player.IsAltForm && !NetSession.Active,
                "living genuine offline biped owns the keyboard movement");
            return frame;
        }
        public void Dispose() => MomentumSceneTests.Release(Scene);
    }

    private static void Mantle(Hunter hunter)
    {
        using var f = new Fixture(hunter, MovementModel.Parkour, mantle: true);
        Idle(f, "before-request");
        int contact = -1, request = -1, active = 0, completed = 0;
        bool finished = false; var changed = new bool[2];
        for (int tick = 0; tick < 110; tick++)
        {
            if (request < 0 && contact >= 0) request = contact + 5;
            ulong frame = f.Tick(forward: !finished, jump: tick == request);
            if (contact < 0 && FrontContact(f.Player)) contact = tick;
            if (f.Player.ModParkourMantleCompleted(frame)) { completed++; finished = true; }
            ParkourAnimationPose pose = Sample(f);
            if (f.Player.ModParkourMantleActive)
            {
                active++;
                Check(pose.MantleProgress is >= 0 and <= 1 && pose.GrappleAmount == 0,
                    "accepted W/fresh-Space mantle exposes its bounded actual mantle pose");
                FirstPersonPicture(f, pose, "mantle", tick);
                for (int lod = 0; lod < 2; lod++) changed[lod] |= Picture(f, lod, pose, "mantle", tick);
            }
        }
        Check(contact >= 0 && request > contact && active > 1 && completed == 1,
            "positive pose is earned after real front-face contact and completes a genuine bounded mantle");
        Check(changed.All(value => value), "actual accepted mantle visibly changes weighted matrices on each native LOD");
        Idle(f, "completed-mantle-retired");
    }

    private static bool FrontContact(PlayerEntity p)
    {
        float radius = Fixed.ToFloat(p.Values.BipedColRadius);
        foreach (CollisionResult hit in (CollisionResult[])Contacts.GetValue(p)!)
            if (hit.EntityCollision == null && hit.Field0 == 0 && hit.Slipperiness == 0
                && hit.Terrain != Terrain.Lava && !hit.Flags.TestFlag(CollisionFlags.Damaging)
                && hit.Plane.Z < -.999f && MathF.Abs(hit.Plane.W + 17) < .001f
                && MathF.Abs(Vector3.Dot(p.Position, hit.Plane.Xyz) - hit.Plane.W - radius) <= .03f)
                return true;
        return false;
    }

    private static void Grapple(Hunter hunter, MovementModel movement)
    {
        using var f = new Fixture(hunter, movement, mantle: false);
        Idle(f, "before-request"); var changed = new bool[2];
        for (int tick = 0; tick < 12; tick++)
        {
            f.Tick(grapple: true); ParkourAnimationPose pose = Sample(f);
            if (movement == MovementModel.Parkour)
            {
                Check(f.Player.ModGrappleState.Active && f.Player.ModGrappleOwnsMovement
                    && f.Player.ModGrappleForceCount > 0, "genuine fresh G accepts a static anchor and applies native force");
                Check(pose.Active && pose.GrappleAmount is > 0 and <= 1 && pose.MantleProgress == 0,
                    "accepted grapple exposes its own bounded actual draw pose");
                Check(float.IsFinite(pose.RopeDirection.LengthSquared) && pose.RopeDirection.LengthSquared > .5f,
                    "accepted grapple supplies a real finite rope direction");
            }
            else Check(!pose.Active && !f.Player.ModGrappleState.Active, "excluded native/historical movement yields no parkour picture");
            FirstPersonPicture(f, pose, "grapple", tick);
            for (int lod = 0; lod < 2; lod++) changed[lod] |= Picture(f, lod, pose, "grapple", tick);
        }
        if (movement == MovementModel.Parkour)
            Check(changed.All(value => value), "actual accepted grapple visibly changes weighted matrices on both LODs");
        else Check(changed.All(value => !value), "excluded movement remains exactly native in both LOD palettes");
        f.Tick();
        Check(!f.Player.ModGrappleOwnsMovement && !f.Player.ModGrappleState.Active,
            "actual physical release retires accepted grapple ownership");
        // Permit a bounded cosmetic exit if implemented, driven solely by real
        // completed scene ticks. No draw call advances the retirement clock.
        for (int tick = 0; tick < 24; tick++) f.Tick();
        Idle(f, "released-grapple-retired");
    }

    private static ParkourAnimationPose Sample(Fixture f)
    {
        Action unchanged = Guard(f); ParkourAnimationPose pose = f.Player.ModGetParkourAnimationPose();
        Check(pose == f.Player.ModGetParkourAnimationPose(), "repeated pose sampling without a native tick is identical");
        unchanged(); return pose;
    }

    private static void Idle(Fixture f, string label)
    {
        ParkourAnimationPose pose = Sample(f);
        Check(!pose.Active, label + " has no live parkour animation");
        FirstPersonPicture(f, pose, label, 0);
        for (int lod = 0; lod < 2; lod++)
            Check(!Picture(f, lod, pose, label, 0), label + " leaves the native weighted palette exact");
    }

    // The same accepted owner state is rendered twice without a simulation
    // tick. The explicitly presentation-only suppression seam removes just
    // the new pose, so this measures the actual owner renderer composition
    // instead of inferring a visible cue from helper offset math alone.
    private static void FirstPersonPicture(Fixture f, ParkourAnimationPose pose, string move, int tick)
    {
        PlayerEntity p = f.Player;
        Check(!ParkourPoseOverlay.SuppressForCheck, "real owner pose was not suppressed before the paired render probe");
        Action unchanged = Guard(f);
        var native = default(PlayerEntity.FirstPersonRenderPose);
        Matrix4 nativeView = default, nativeGun = default, nativeEffect = default;
        Vector3 nativeEye = default; float nativeFov = 0;
        try
        {
            ParkourPoseOverlay.SuppressForCheck = true;
            Check(p.ModGetParkourAnimationPose().GunOffset == Vector3.Zero,
                "paired suppression removes only the parkour render cue without changing accepted movement");
            Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out nativeView, out nativeEye, out nativeFov)
                && p.ModTryOwnerRenderPose(out native)
                && p.ModGetFirstPersonGunTransform(out nativeGun)
                && p.ModGetFirstPersonEffectTransform(out nativeEffect) && Finite(nativeGun) && Finite(nativeEffect),
                "actual owner first-person renderer supplies a native counterfactual cannon/effect picture");
            unchanged();
        }
        finally { ParkourPoseOverlay.SuppressForCheck = false; }
        Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out var view, out var eye, out var fov)
            && p.ModTryOwnerRenderPose(out var actual), "actual accepted owner prepares the live first-person parkour picture");
        // Assign separately because a short-circuit assertion is not a C#
        // definite-assignment guarantee for the pose used below.
        Check(p.ModTryOwnerRenderPose(out actual), "accepted owner render pose remains available");
        Check(view == nativeView && eye == nativeEye && fov == nativeFov,
            "parkour cannon cue leaves the rendered camera, view and FOV exact");
        Check(actual.GunFacing == native.GunFacing && actual.GunUp == native.GunUp,
            "owner cannon cue preserves native rendered weapon aim orientation");
        Matrix4 camera = Matrix4.Invert(view);
        Vector3 expected = camera.Row0.Xyz * pose.GunOffset.X + camera.Row1.Xyz * pose.GunOffset.Y
            - camera.Row2.Xyz * pose.GunOffset.Z;
        Vector3 measured = actual.GunPosition - native.GunPosition;
        Near((measured - expected).Length, 0, "actual owner cannon receives the declared camera-local parkour offset", .00003f);
        if (pose.GunOffset.LengthSquared > 1e-8f)
        {
            Check(measured.LengthSquared > 1e-8f, "earned accepted movement visibly displaces the actual first-person cannon");
            OwnerCues.Add(p.Hunter + "/" + move);
        }
        else Check(actual.GunPosition == native.GunPosition, "inactive or excluded movement leaves the owner cannon exactly native");
        bool gunReady = p.ModGetFirstPersonGunTransform(out var gun);
        bool effectReady = p.ModGetFirstPersonEffectTransform(out var effect);
        Check(gunReady && effectReady && Finite(gun) && Finite(effect),
            "actual shifted cannon and linked presentation effect transforms remain finite");
        Near((gun.Row3.Xyz - actual.GunPosition).Length, 0, "native cannon submission uses the prepared render-only gun origin");
        Near((effect.Row3.Xyz - nativeEffect.Row3.Xyz - measured).Length, 0,
            "linked muzzle/charge picture follows exactly the same render-only cannon translation", .00003f);
        Near((effect.Row3.Xyz - actual.GunPosition - actual.GunFacing * Fixed.ToFloat(p.Values.MuzzleOffset)).Length, 0,
            "linked effect remains at the rendered native barrel tip", .00003f);
        unchanged();
        Check(p.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out var repeatView, out var repeatEye, out var repeatFov)
            && p.ModTryOwnerRenderPose(out var repeat) && repeatView == view && repeatEye == eye && repeatFov == fov
            && repeat.GunPosition == actual.GunPosition && repeat.GunFacing == actual.GunFacing && repeat.GunUp == actual.GunUp,
            "repeated actual owner picture cannot accumulate cannon displacement or advance a native clock");
        unchanged();
        Rows.Add(new { probe = "owner-first-person", hunter = p.Hunter.ToString(), move, tick,
            mantle = pose.MantleProgress, grapple = pose.GrappleAmount,
            offsetX = measured.X, offsetY = measured.Y, offsetZ = measured.Z,
            expectedX = expected.X, expectedY = expected.Y, expectedZ = expected.Z });
    }

    private static bool Picture(Fixture f, int lod, ParkourAnimationPose pose, string move, int tick)
    {
        PlayerEntity p = f.Player; Model model = f.Lods[lod].Model;
        Matrix4[] entry = Nodes(model); float[] entryPalette = model.MatrixStackValues.ToArray();
        var entryFlags = model.Nodes.Select(n => (n.AnimIgnoreChild, n.AfterTransform)).ToArray();
        int Find(string name)
        {
            for (int i = 0; i < model.Nodes.Count; i++) if (model.Nodes[i].Name == name) return i;
            throw new InvalidOperationException($"{p.Hunter}/LOD{lod} lacks native bone {name}");
        }
        int spine = Find("Spine_1"), cannon = Find("R_elbow");
        int[][] chains = {
            new[] { Find("L_hip"), Find("L_knee"), Find("L_ankle") },
            new[] { Find("R_hip"), Find("R_knee"), Find("R_ankle") },
            new[] { Find("L_shoulder"), Find("L_elbow") },
            new[] { Find("R_shoulder"), cannon } };
        Matrix4 world = PlayerEntity.ModBuildBipedDrawTransform(p.Position, p.FacingVector,
            p.FacingVector.WithY(0), Metadata.HunterScales[p.Hunter], p.ModBipedBottom, out float pitch);
        Action unchanged = Guard(f);
        try
        {
            Canonical.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
            unchanged();
            Matrix4[] baseline = Nodes(model); float[] palette = model.MatrixStackValues.ToArray();
            Noop(model, world, default, "None");
            Noop(model, world, new(0, 0, Vector3.UnitZ, pose.Seconds), "zero mantle endpoint");
            Noop(model, world, new(1, 0, Vector3.UnitZ, pose.Seconds), "retired mantle endpoint");
            Noop(model, world, new(float.NaN, 0, Vector3.UnitZ, 0), "nonfinite mantle");
            Noop(model, world, new(.5f, float.NaN, Vector3.UnitZ, 0), "nonfinite grapple");
            Noop(model, world, new(.5f, 0, Vector3.UnitZ, double.PositiveInfinity), "nonfinite seconds");
            Matrix4[] posed; float[] posedPalette;
            using (ParkourPoseOverlay.Begin(model, world, pose))
            {
                posed = Nodes(model); posedPalette = model.MatrixStackValues.ToArray();
                Check(posed.All(Finite) && posedPalette.All(float.IsFinite), "actual posed native joints and submitted palette remain finite");
                Check(posed[cannon] == baseline[cannon], "canonical R_elbow shooting matrix is preserved exactly inside the picture");
                int weaponRoot = Find("R_shoulder");
                for (int index = 0; index < model.Nodes.Count; index++)
                    for (int ancestor = index; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                        if (ancestor == weaponRoot)
                        { Check(posed[index] == baseline[index], "entire native right shoulder/cannon branch remains exact"); break; }
                foreach (int[] chain in chains)
                    for (int i = 1; i < chain.Length; i++)
                        Near((posed[chain[i]].Row3.Xyz - posed[chain[i - 1]].Row3.Xyz).Length,
                            (baseline[chain[i]].Row3.Xyz - baseline[chain[i - 1]].Row3.Xyz).Length,
                            "posed limb preserves native joint segment length");
                for (int i = 0; i < posed.Length; i++)
                {
                    Near(posed[i].Row0.Xyz.Length, baseline[i].Row0.Xyz.Length, "native first axis scale preserved");
                    Near(posed[i].Row1.Xyz.Length, baseline[i].Row1.Xyz.Length, "native second axis scale preserved");
                    Near(posed[i].Row2.Xyz.Length, baseline[i].Row2.Xyz.Length, "native third axis scale preserved");
                }
                unchanged();
            }
            Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
                "scope disposal restores every native node and matrix palette value exactly");
            unchanged();
            using (ParkourPoseOverlay.Begin(model, world, pose))
                Check(Nodes(model).SequenceEqual(posed) && model.MatrixStackValues.SequenceEqual(posedPalette),
                    "repeated actual picture is identical without native animation or phase advancement");
            Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
                "repeated scope restores the exact same canonical skeleton and palette");
            try
            {
                using var scope = ParkourPoseOverlay.Begin(model, world, pose);
                throw new PictureFailure();
            }
            catch (PictureFailure) { }
            Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
                "aborted submission also restores every canonical node and palette value");
            unchanged();
            bool weightedChanged = !posedPalette.SequenceEqual(palette);
            Check(pose.Active || !weightedChanged && posed.SequenceEqual(baseline),
                "inactive pose leaves all native joints and weighted palette untouched");
            if (weightedChanged)
            {
                ScopeOwnership(model, world, pose, baseline, palette, posed, posedPalette);
                Model gun = ((ModelInstance)Field(p, "_gunModel")!).Model;
                ParkourPoseOverlay.Prewarm(gun);
                Noop(gun, world, pose, "unsupported native gun skeleton");
                if (p.Hunter == Hunter.Samus && AllocationSamples.Add(move + "/" + lod))
                    WarmedAllocation(model, world, pose, baseline, palette);
            }
            Rows.Add(new { hunter = p.Hunter.ToString(), lod, move, tick, frame = f.Scene.FrameCount,
                mantle = pose.MantleProgress, grapple = pose.GrappleAmount, pose.Seconds,
                ropeX = pose.RopeDirection.X, ropeY = pose.RopeDirection.Y, ropeZ = pose.RopeDirection.Z,
                weightedChanged, changedWeightedNodes = model.NodeMatrixIds.Count(index => posed[index] != baseline[index]),
                nodeCount = model.Nodes.Count, paletteMatrices = palette.Length / 16 });
            return weightedChanged;
        }
        finally
        {
            for (int i = 0; i < model.Nodes.Count; i++)
            { model.Nodes[i].Animation = entry[i]; model.Nodes[i].AnimIgnoreChild = entryFlags[i].AnimIgnoreChild; model.Nodes[i].AfterTransform = entryFlags[i].AfterTransform; }
            Array.Copy(entryPalette, (float[])model.MatrixStackValues, entryPalette.Length);
            MatrixPaletteRevision.Changed(model.MatrixStackValues);
            // This outer restore owns only the CPU probe's borrowed scratch;
            // production scopes above must already restore their own baseline.
            Check(model.MatrixStackValues.SequenceEqual(entryPalette), "CPU probe returns its borrowed native palette exactly");
        }
    }

    private sealed class PictureFailure : Exception { }

    private static void Noop(Model model, Matrix4 world, ParkourAnimationPose pose, string label)
    {
        Matrix4[] before = Nodes(model); float[] palette = model.MatrixStackValues.ToArray();
        using (ParkourPoseOverlay.Begin(model, world, pose))
            Check(Nodes(model).SequenceEqual(before) && model.MatrixStackValues.SequenceEqual(palette),
                label + " is an exact native pose no-op");
        Check(Nodes(model).SequenceEqual(before) && model.MatrixStackValues.SequenceEqual(palette),
            label + " disposal leaves every native node and palette value exact");
    }

    private static void ScopeOwnership(Model model, Matrix4 world, ParkourAnimationPose pose,
        Matrix4[] baseline, float[] palette, Matrix4[] picture, float[] picturePalette)
    {
        var first = ParkourPoseOverlay.Begin(model, world, pose);
        var copied = first;
        try
        {
            var nested = ParkourPoseOverlay.Begin(model, world, pose);
            nested.Dispose();
            Check(Nodes(model).SequenceEqual(picture) && model.MatrixStackValues.SequenceEqual(picturePalette),
                "nested same-model scope cannot compose twice or restore the active parent's picture");
        }
        finally { first.Dispose(); }
        copied.Dispose(); first.Dispose();
        Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
            "copied/double disposal remains an exact no-op after its owning scope restores");
        using (ParkourPoseOverlay.Begin(model, world, pose))
        {
            copied.Dispose();
            Check(Nodes(model).SequenceEqual(picture) && model.MatrixStackValues.SequenceEqual(picturePalette),
                "stale copied disposal cannot restore a newer scope's borrowed palette");
        }
        Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
            "newer scope restores correctly after stale disposal attempt");
    }

    private static void WarmedAllocation(Model model, Matrix4 world, ParkourAnimationPose pose,
        Matrix4[] baseline, float[] palette)
    {
        for (int i = 0; i < 256; i++) { using var warm = ParkourPoseOverlay.Begin(model, world, pose); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++) { using var sample = ParkourPoseOverlay.Begin(model, world, pose); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "20,000 warmed actual native pose scopes allocate zero managed bytes (" + allocated + ")");
        Check(Nodes(model).SequenceEqual(baseline) && model.MatrixStackValues.SequenceEqual(palette),
            "20,000 warmed scopes retain exact canonical pose and palette restoration");
        Rows.Add(new { probe = "warmed-native-overlay", loops = 20000, allocatedBytes = allocated,
            pose.MantleProgress, pose.GrappleAmount, nodeCount = model.Nodes.Count });
    }

    private static Action Guard(Fixture f)
    {
        PlayerEntity p = f.Player; Scene scene = f.Scene;
        var body = (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.Health, p.ModCrouched, p.ModSliding,
            p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset, p.IsAltForm, p.IsMorphing, p.IsUnmorphing);
        var collision = p.ModCaptureCollisionState();
        var camera = (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix);
        var anchors = (p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob"));
        var clocks = (scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames, FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha);
        object?[] controller = Controllers.Select(field => field.GetValue(p)).ToArray();
        var rope = p.ModGrappleState;
        var controls = (p.Controls.MoveUp.IsDown, p.Controls.Jump.IsDown, p.Controls.Jump.IsPressed,
            p.Controls.Grapple.IsDown, p.Controls.Grapple.IsPressed);
        Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var facts), "native precombat source facts available");
        var infos = new[] { ((ModelInstance)Field(p, "_bipedModel1")!).AnimInfo,
            ((ModelInstance)Field(p, "_bipedModel2")!).AnimInfo, ((ModelInstance)Field(p, "_gunModel")!).AnimInfo };
        var integers = infos.Select(i => i.Index.Concat(i.PrevIndex).Concat(i.Frame).Concat(i.FrameCount).Concat(i.Step).ToArray()).ToArray();
        var flags = infos.Select(i => i.Flags.ToArray()).ToArray();
        var groups = infos.Select(i => new object?[] { i.Node.Group, i.Material.Group, i.Texture.Group, i.Texcoord.Group }).ToArray();
        var slots = infos.Select(i => new[] { i.Node.Slot, i.Material.Slot, i.Texture.Slot, i.Texcoord.Slot }).ToArray();
        return () =>
        {
            Check(body == (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.Health, p.ModCrouched, p.ModSliding,
                p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset, p.IsAltForm, p.IsMorphing, p.IsUnmorphing)
                && p.ModCaptureCollisionState() == collision, "draw-only sample leaves actual body, native collision and stance exact");
            Check(camera == (p.CameraInfo.Position, p.CameraInfo.Target, p.CameraInfo.UpVector, p.CameraInfo.Fov, p.CameraInfo.ViewMatrix)
                && anchors.Equals((p.ModGunVector, Field(p, "_muzzlePos"), Field(p, "_aimPosition"), Field(p, "_walkViewBob"), Field(p, "_gunViewBob")))
                && p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var after) && after == facts,
                "draw-only sample leaves native camera, aim, muzzle and retained shot facts exact");
            Check(clocks == (scene.FrameCount, FrameTiming.TotalSteps, FrameTiming.TotalFrames, FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha)
                && controller.SequenceEqual(Controllers.Select(field => field.GetValue(p))) && rope.Equals(p.ModGrappleState)
                && controls == (p.Controls.MoveUp.IsDown, p.Controls.Jump.IsDown, p.Controls.Jump.IsPressed,
                    p.Controls.Grapple.IsDown, p.Controls.Grapple.IsPressed),
                "draw-only sample leaves simulation/presentation clocks, accepted controllers, rope and keyboard state exact");
            for (int index = 0; index < infos.Length; index++)
            {
                AnimationInfo info = infos[index];
                Check(info.Index.Concat(info.PrevIndex).Concat(info.Frame).Concat(info.FrameCount).Concat(info.Step).SequenceEqual(integers[index])
                    && info.Flags.SequenceEqual(flags[index]) && new[] { info.Node.Slot, info.Material.Slot, info.Texture.Slot, info.Texcoord.Slot }.SequenceEqual(slots[index])
                    && new object?[] { info.Node.Group, info.Material.Group, info.Texture.Group, info.Texcoord.Group }.SequenceEqual(groups[index]),
                    "canonical lower/upper/viewmodel native animation cursors and groups remain exact");
            }
        };
    }
}
