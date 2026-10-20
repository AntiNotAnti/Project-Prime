using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
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
        CheckPresentationTimeline();
        _checks += TraversalCrouchGaitTests.CheckMath();
        _checks += TraversalSlidePresentationTests.CheckMath();
        _checks += TraversalWalkPresentationTests.CheckMath();
        return _checks;
    }

    private static void CheckPresentationTimeline()
    {
        Check(TraversalCrouchPresentation.TransitionTicks == 6, "cosmetic transition uses six completed simulation ticks");
        var empty = new TraversalCrouchPresentation();
        Check(empty.Sample(.5f, false) == 0 && empty.Sample(.5f, true) == 1,
            "uncaptured timeline uses the accepted stance fallback");
        Check(empty.SampleCanonical(.5f, false) == 0 && empty.SampleCanonical(.5f, true) == 1,
            "uncaptured canonical timeline uses the accepted stance fallback");
        foreach (bool target in new[] { true, false })
        {
            var timeline = new TraversalCrouchPresentation();
            timeline.Capture(10, !target);
            float prior = timeline.Sample(1, target);
            for (ulong tick = 1; tick <= TraversalCrouchPresentation.TransitionTicks; tick++)
            {
                timeline.Capture(10 + tick, target);
                Near(timeline.Sample(0, !target), prior, "successive pictures join at the previous completed tick");
                float previousPicture = prior;
                foreach (float alpha in new[] { 0f, .125f, .5f, .875f, 1f })
                {
                    float picture = timeline.Sample(alpha, !target);
                    Check(float.IsFinite(picture) && picture is >= 0 and <= 1,
                        "every intermediate cosmetic sample is finite and bounded");
                    Check(target ? picture >= previousPicture : picture <= previousPicture,
                        "entering and leaving crouch are monotonic within each tick");
                    Check(timeline.Sample(alpha, target) == picture && timeline.Sample(alpha, !target) == picture,
                        "repeat sampling and fallback choice cannot advance an initialized timeline");
                    float canonical = timeline.SampleCanonical(alpha, !target);
                    float previousCanonical = tick == 1 ? (target ? 0 : 1) : (target ? 1 : 0);
                    Near(canonical, previousCanonical * (1 - alpha) + (target ? 1 : 0) * alpha,
                        "canonical stance interpolates only accepted previous and current levels");
                    Check(timeline.SampleCanonical(alpha, target) == canonical && canonical is >= 0 and <= 1,
                        "canonical sampling is bounded, repeatable and independent of fallback");
                    previousPicture = picture;
                }
                prior = timeline.Sample(1, !target);
                if (tick == 1) Check(target ? prior < timeline.SampleCanonical(1, target)
                    : prior > timeline.SampleCanonical(1, target),
                    "first cosmetic endpoint eases while accepted canonical stance changes immediately");
                float capturedCanonical = timeline.SampleCanonical(.5f, target);
                timeline.Capture(10 + tick, !target);
                Check(timeline.Sample(1, target) == prior,
                    "a second capture in one simulation tick cannot reverse or refresh the transition");
                Check(timeline.SampleCanonical(.5f, target) == capturedCanonical,
                    "duplicate capture cannot replace accepted canonical endpoints");
                Check(timeline.Sample(-1, target) == timeline.Sample(0, target)
                    && timeline.Sample(2, target) == prior, "finite presentation alpha clamps to the tick interval");
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Check(timeline.Sample(invalid, target) == prior
                        && timeline.SampleCanonical(invalid, !target) == (target ? 1 : 0),
                        "nonfinite alpha selects both completed cosmetic and canonical ticks");
            }
            Near(prior, target ? 1 : 0, "transition reaches its endpoint in six completed ticks");
            timeline.Capture(100, !target);
            Check(timeline.Sample(0, target) == (!target ? 1 : 0)
                && timeline.Sample(1, target) == (!target ? 1 : 0)
                && timeline.SampleCanonical(.5f, target) == (!target ? 1 : 0),
                "a skipped simulation frame reseeds both cosmetic endpoints");
            timeline.Capture(3, target);
            Check(timeline.Sample(.5f, !target) == (target ? 1 : 0)
                && timeline.SampleCanonical(.5f, !target) == (target ? 1 : 0),
                "a backwards frame discontinuity snaps to the new accepted stance");
            timeline.Reset();
            Check(timeline.Sample(.5f, false) == 0 && timeline.Sample(.5f, true) == 1
                && timeline.SampleCanonical(.5f, false) == 0 && timeline.SampleCanonical(.5f, true) == 1,
                "reset removes all retained cosmetic stance");
            timeline.Capture(1, target);
            Check(timeline.Sample(.5f, !target) == (target ? 1 : 0), "capture after reset starts from accepted stance");
        }
        var reversing = new TraversalCrouchPresentation();
        reversing.Capture(0, false); reversing.Capture(1, true); reversing.Capture(2, true);
        float reversalStart = reversing.Sample(1, true);
        reversing.Capture(3, false);
        Near(reversing.Sample(0, false), reversalStart, "reversal starts at the previous cosmetic endpoint");
        float previousReversal = reversalStart;
        foreach (float alpha in new[] { 0f, .25f, .5f, .75f, 1f })
        {
            Near(reversing.SampleCanonical(alpha, true), 1 - alpha,
                "release reversal interpolates the accepted one-to-zero stance exactly");
            float picture = reversing.Sample(alpha, false);
            Check(picture is >= 0 and <= 1 && picture <= previousReversal,
                "interrupted crouch reverses monotonically without cosmetic overshoot");
            previousReversal = picture;
        }
        var warmed = new TraversalCrouchPresentation(); float sum = 0;
        for (ulong tick = 0; tick < 10000; tick++)
        { warmed.Capture(tick, tick % 12 < 6); sum += warmed.Sample(.5f, false); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong tick = 10000; tick < 110000; tick++)
        { warmed.Capture(tick, tick % 12 < 6); sum += warmed.Sample(.25f, false) + warmed.Sample(.75f, true)
            + warmed.SampleCanonical(.25f, false) + warmed.SampleCanonical(.75f, true); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && float.IsFinite(sum) && sum > 0,
            "warmed capture and repeated picture sampling allocate no managed memory");
    }

    private static object CheckFirstPersonEyeTransition(Scene scene, PlayerEntity player, KeyboardState keyboard)
    {
        int priorCap = FrameTiming.FrameRateCap;
        bool priorCrosshair = Features.FixedCrosshair;
        var rows = new List<object>();
        try
        {
            Features.FixedCrosshair = true;
            FrameTiming.FrameRateCap = 144;
            FrameTiming.Reset();
            SetKey(keyboard, Keys.LeftControl, false);
            for (int tick = 0; tick < 20; tick++) scene.OnSimulationFrame();
            Check(!player.ModCrouched && player.CameraType == CameraType.First,
                "first-person eye fixture begins from actual standing keyboard input");
            Check(player.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out _, out Vector3 standingEye, out _)
                && standingEye == player.CameraInfo.Position, "settled standing render eye is exactly the canonical camera");
            var timeline = new TraversalCrouchPresentation(); timeline.Capture(0, false);
            float eyeDrop = Fixed.ToFloat(player.Values.AimYOffset) - TraversalStance.EyeOffset(
                player.ModBipedBottom, Fixed.ToFloat(player.Values.MaxPickupHeight),
                Fixed.ToFloat(player.Values.BipedColRadius), Fixed.ToFloat(player.Values.AimYOffset), true);
            Check(eyeDrop > 0, "native accepted crouch has a positive eye-height difference");
            float previousRenderedY = standingEye.Y;
            var muzzleField = typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!;
            var aimField = typeof(PlayerEntity).GetField("_aimPosition", Hidden)!;
            for (int tick = 0; tick < 16; tick++)
            {
                bool held = tick < 8;
                SetKey(keyboard, Keys.LeftControl, held); scene.OnSimulationFrame();
                Check(player.ModCrouched == held && player.Controls.Crouch.IsDown == held,
                    "real Ctrl transition updates the accepted physical stance immediately");
                timeline.Capture((ulong)tick + 1, held);
                var collision = player.ModCaptureCollisionState();
                var camera = (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.UpVector,
                    player.CameraInfo.Fov, player.CameraInfo.ViewMatrix);
                var shape = (player.ModCrouched, player.ModSliding, player.ModBipedBottom, player.ModBipedTop, player.ModBipedEyeOffset);
                var anchors = ((Vector3)muzzleField.GetValue(player)!, (Vector3)aimField.GetValue(player)!, player.ModGunVector);
                Check(player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var sourceFacts),
                    "actual Ctrl eye transition retains canonical source-time facts");
                foreach (double alpha in new[] { 0d, .25, .5, .75, 1d })
                {
                    Check(player.ModPrepareFirstPersonRenderPose(alpha, 0, 0, 0, 0,
                        out Matrix4 view, out Vector3 eye, out float fov) && player.ModTryOwnerRenderPose(out _),
                        "actual first-person render preparation accepts each fractional Ctrl-transition picture");
                    Check(player.ModTryOwnerRenderPose(out var pose) && Finite(view) && Finite(eye)
                        && Finite(pose.GunPosition) && float.IsFinite(fov), "eased render camera and gun composition remain finite");
                    Vector3 nativeDrawEye = player.CameraInfo.ModGetDrawPosition(alpha, fenceLiveClock: true);
                    float expectedY = nativeDrawEye.Y + (timeline.SampleCanonical((float)alpha, held)
                        - timeline.Sample((float)alpha, held)) * eyeDrop;
                    Near(eye.Y, expectedY, "rendered eye replaces only the canonical stance interpolation contribution");
                    Check(held ? eye.Y <= previousRenderedY + .0002f : eye.Y >= previousRenderedY - .0002f,
                        "stationary rendered eye enters and leaves crouch monotonically across intermediate pictures");
                    previousRenderedY = eye.Y;
                    Check(player.ModPrepareFirstPersonRenderPose(alpha, 0, 0, 0, 0,
                        out Matrix4 repeatedView, out Vector3 repeatedEye, out float repeatedFov)
                        && player.ModTryOwnerRenderPose(out var repeatedPose)
                        && repeatedView == view && repeatedEye == eye && repeatedFov == fov
                        && repeatedPose.GunPosition == pose.GunPosition && repeatedPose.GunFacing == pose.GunFacing
                        && repeatedPose.GunUp == pose.GunUp,
                        "repeating the same eye picture cannot advance cosmetic camera or arm-cannon motion");
                    Check(player.ModCaptureCollisionState() == collision
                        && camera == (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.UpVector,
                            player.CameraInfo.Fov, player.CameraInfo.ViewMatrix)
                        && shape == (player.ModCrouched, player.ModSliding, player.ModBipedBottom, player.ModBipedTop, player.ModBipedEyeOffset)
                        && anchors == ((Vector3)muzzleField.GetValue(player)!, (Vector3)aimField.GetValue(player)!, player.ModGunVector)
                        && player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var afterFacts) && afterFacts == sourceFacts,
                        "repeated eased eye pictures leave canonical camera, physical shape, muzzle and shot facts exact");
                    rows.Add(new { tick, held, alpha, renderedEyeY = eye.Y, nativeEyeY = nativeDrawEye.Y, expectedY });
                }
            }
            Check(player.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0, out _, out Vector3 finalEye, out _)
                && finalEye == player.CameraInfo.Position && !player.ModCrouched,
                "completed real release restores the exact normal standing camera endpoint");
            rows.Add(CheckEyeHistoryFences(scene, player, keyboard, eyeDrop));
            rows.Add(CheckLowRoofEye(scene, player, keyboard, eyeDrop));
            return new { scenario = "real-Ctrl-first-person-eye-easing", rows };
        }
        finally
        {
            Features.FixedCrosshair = priorCrosshair; FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset();
            player.ModInvalidateFirstPersonRenderPose();
        }
    }

    private static object CheckEyeHistoryFences(Scene scene, PlayerEntity player, KeyboardState keyboard, float eyeDrop)
    {
        var rows = new List<object>();
        var validHistory = typeof(CameraInfo).GetField("_drawStateValid", Hidden)!;
        foreach (string mode in new[] { "missing-camera-history", "presentation-reset", "discarded-interval" })
        {
            SetKey(keyboard, Keys.LeftControl, false);
            for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
            Check(!player.ModCrouched, "history-fence fixture earns a standing baseline by releasing actual Ctrl");
            SetKey(keyboard, Keys.LeftControl, true); scene.OnSimulationFrame();
            Check(player.ModCrouched, "history-fence fixture begins during a genuine newly accepted crouch transition");
            var timeline = new TraversalCrouchPresentation(); timeline.Capture(0, false); timeline.Capture(1, true);
            // Only presentation history is invalidated. Canonical camera, actor,
            // stance and weapon facts continue to come from the actual input tick.
            if (mode == "missing-camera-history") validHistory.SetValue(player.CameraInfo, false);
            else if (mode == "presentation-reset") FrameTiming.Reset();
            else FrameTiming.Advance(double.NaN);
            var camera = (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.UpVector,
                player.CameraInfo.Fov, player.CameraInfo.ViewMatrix);
            var collision = player.ModCaptureCollisionState();
            var shape = (player.ModCrouched, player.ModSliding, player.ModBipedTop, player.ModBipedEyeOffset);
            Vector3 muzzle = (Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(player)!;
            Vector3 aimPosition = (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(player)!;
            Check(player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var facts),
                "history invalidation retains actual source-time facts");
            float previousY = float.PositiveInfinity;
            foreach (double alpha in new[] { 0d, .25, .5, .75, 1d })
            {
                Check(!player.CameraInfo.ModHasLiveDrawHistory(alpha), "invalidated live camera history cannot interpolate old endpoints");
                Check(player.ModPrepareFirstPersonRenderPose(alpha, 0, 0, 0, 0,
                    out Matrix4 view, out Vector3 eye, out float fov) && player.ModTryOwnerRenderPose(out _),
                    "first-person pose remains available without usable camera history");
                Check(player.ModTryOwnerRenderPose(out var pose) && Finite(view) && Finite(eye) && Finite(pose.GunPosition),
                    "history-fenced rendered camera and gun stay finite");
                float expectedY = camera.Position.Y + (mode == "missing-camera-history"
                    ? (1 - timeline.Sample((float)alpha, true)) * eyeDrop : 0);
                Near(eye.Y, expectedY, "missing history uses current canonical stance; a new generation discards old cosmetic easing");
                Check(eye.Y <= previousY + .0002f, "history fallback cannot reverse crouch eye motion as picture alpha advances");
                previousY = eye.Y;
                Check(player.ModPrepareFirstPersonRenderPose(alpha, 0, 0, 0, 0,
                    out Matrix4 repeatedView, out Vector3 repeatedEye, out float repeatedFov)
                    && player.ModTryOwnerRenderPose(out var repeatedPose) && repeatedView == view && repeatedEye == eye
                    && repeatedFov == fov && repeatedPose.GunPosition == pose.GunPosition
                    && repeatedPose.GunFacing == pose.GunFacing && repeatedPose.GunUp == pose.GunUp,
                    "repeated history-fallback pictures neither advance nor mutate camera/gun presentation");
                Check(camera == (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.UpVector,
                        player.CameraInfo.Fov, player.CameraInfo.ViewMatrix)
                    && player.ModCaptureCollisionState() == collision
                    && shape == (player.ModCrouched, player.ModSliding, player.ModBipedTop, player.ModBipedEyeOffset)
                    && (Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(player)! == muzzle
                    && (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(player)! == aimPosition
                    && player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var afterFacts) && afterFacts == facts,
                    "history-fallback rendering preserves canonical camera, physical shape, muzzle and source facts");
                rows.Add(new { mode, alpha, eyeY = eye.Y, expectedY });
            }
            for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
            Check(player.CameraInfo.ModHasLiveDrawHistory(.5), "completed live ticks recover camera history in the new generation");
            Check(player.ModPrepareFirstPersonRenderPose(.5, 0, 0, 0, 0, out _, out Vector3 recoveredEye, out _)
                && recoveredEye == player.CameraInfo.Position, "recovered settled crouch has no stale eye offset");
        }
        return new { scenario = "first-person-eye-history-fences", rows };
    }

    private static object CheckLowRoofEye(Scene scene, PlayerEntity player, KeyboardState keyboard, float eyeDrop)
    {
        SetKey(keyboard, Keys.LeftControl, false);
        for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
        Vector3 initial = player.Position;
        try
        {
            // As in the authored collision suites, initial lane placement is an
            // explicit fixture seam. Actual Ctrl earns the physical low stance;
            // neither stance, velocity nor camera fields are fabricated.
            player.ModPlaceAt(new(30, .5f, 0));
            SetKey(keyboard, Keys.LeftControl, true); scene.OnSimulationFrame();
            Check(player.ModCrouched && player.Position.Y + player.ModBipedTop <= 1.2f + .0002f,
                "real Ctrl earns a body that fits below the authored 1.2-unit tunnel roof");
            var timeline = new TraversalCrouchPresentation(); timeline.Capture(0, false); timeline.Capture(1, true);
            float unconstrainedEye = player.CameraInfo.Position.Y + (1 - timeline.Sample(1, true)) * eyeDrop;
            Check(unconstrainedEye > 1.2f, "initial cosmetic eye easing would cross the real roof without its collision clamp");
            var camera = (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.ViewMatrix);
            var collision = player.ModCaptureCollisionState();
            var anchors = ((Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(player)!,
                (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(player)!, player.ModGunVector);
            Check(player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var facts),
                "low-roof fixture retains actual canonical shot facts");
            Check(player.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0,
                out Matrix4 view, out Vector3 eye, out float fov) && player.ModTryOwnerRenderPose(out _),
                "cosmetic eye prepares below the physical low ceiling");
            Check(player.ModTryOwnerRenderPose(out var pose) && Finite(view) && eye.Y <= 1.2f - .005f
                && eye.Y >= player.CameraInfo.Position.Y && eye.Y < unconstrainedEye,
                "read-only cosmetic trace clamps the view below the roof while canonical low eye remains valid");
            for (int picture = 0; picture < 3; picture++)
                Check(player.ModPrepareFirstPersonRenderPose(1, 0, 0, 0, 0,
                    out Matrix4 repeatedView, out Vector3 repeatedEye, out float repeatedFov)
                    && player.ModTryOwnerRenderPose(out var repeatedPose) && repeatedView == view && repeatedEye == eye
                    && repeatedFov == fov && repeatedPose.GunPosition == pose.GunPosition
                    && camera == (player.CameraInfo.Position, player.CameraInfo.Target, player.CameraInfo.ViewMatrix)
                    && player.ModCaptureCollisionState() == collision
                    && anchors == ((Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(player)!,
                        (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(player)!, player.ModGunVector)
                    && player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector, out var afterFacts) && afterFacts == facts,
                    "repeated low-roof traces preserve canonical camera, collider and weapon facts");
            return new { scenario = "real-low-roof-cosmetic-eye-clamp", roofY = 1.2f, unconstrainedEye, renderedEyeY = eye.Y };
        }
        finally
        {
            player.ModPlaceAt(initial); SetKey(keyboard, Keys.LeftControl, false);
            for (int tick = 0; tick < 8; tick++) scene.OnSimulationFrame();
        }
    }

    internal static object Capture(Hunter hunter) => new
    {
        hunter = hunter.ToString(),
        canonical = CaptureCanonical(hunter),
        gait = TraversalCrouchGaitTests.Capture(hunter),
        slide = TraversalSlidePoseTests.Capture(hunter),
        slideFeedback = TraversalSlideFeedbackSceneTests.Capture(hunter)
    };

    private static object CaptureCanonical(Hunter hunter)
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
            rows.Add(CheckFirstPersonEyeTransition(scene, p, keyboard));
            SetKey(keyboard, Keys.LeftControl, true);
            for (int index = 0; index < 8; index++) scene.OnSimulationFrame();
            Check(p.Controls.Crouch.IsDown && p.ModCrouched && !p.IsAltForm,
                "genuine production Ctrl input earns the pose's crouched body state");
            ModelInstance[] lods = (ModelInstance[])typeof(PlayerEntity).GetField("_bipedModelLods", Hidden)!.GetValue(p)!;
            var lower = (ModelInstance)typeof(PlayerEntity).GetField("_bipedModel1", Hidden)!.GetValue(p)!;
            var upper = (ModelInstance)typeof(PlayerEntity).GetField("_bipedModel2", Hidden)!.GetValue(p)!;
            var present = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation", Hidden)!;
            var visual = typeof(PlayerEntity).GetMethod("UpdateHunterBipedPose", Hidden)!;
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
                        float[] canonicalStack = model.MatrixStackValues.ToArray();
                        var collision = p.ModCaptureCollisionState();
                        var eye = (p.ModBipedEyeOffset, p.CameraInfo.Position, p.CameraInfo.Target);
                        var stance = (p.ModCrouched, p.ModSliding, p.ModBipedBottom, p.ModBipedTop);
                        Vector3 muzzle = (Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(p)!;
                        Vector3 aimPosition = (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(p)!;
                        Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var sourceFacts),
                            "accepted current shot facts are available before cosmetic blending");
                        var blends = new List<object>();
                        foreach (float amount in new[] { 0f, .2f, .5f, .8f, 1f })
                        {
                            visual.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null, amount, false });
                            Matrix4[] blended = model.Nodes.Select(node => node.Animation).ToArray();
                            Check(blended.All(Finite) && model.MatrixStackValues.All(float.IsFinite),
                                "partial visual stance produces finite native nodes and submitted matrices");
                            Near(native[spine].Row3.Y - blended[spine].Row3.Y, drop * amount,
                                "partial visual torso drop follows only the cosmetic blend amount");
                            foreach (int[] leg in new[] { left, right })
                            {
                                Near((blended[leg[1]].Row3.Xyz - blended[leg[0]].Row3.Xyz).Length,
                                    (native[leg[1]].Row3.Xyz - native[leg[0]].Row3.Xyz).Length,
                                    "partial crouch preserves the animated native thigh length");
                                Near((blended[leg[2]].Row3.Xyz - blended[leg[1]].Row3.Xyz).Length,
                                    (native[leg[2]].Row3.Xyz - native[leg[1]].Row3.Xyz).Length,
                                    "partial crouch preserves the animated native shin length");
                                for (int node = 0; node < model.Nodes.Count; node++)
                                    for (int ancestor = node; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                                        if (ancestor == leg[2])
                                        { Check(blended[node] == native[node], "partial visual ankle and foot descendants stay exactly planted"); break; }
                            }
                            if (amount == 0) Check(blended.SequenceEqual(native), "zero blend restores the exact native animated endpoint");
                            if (amount == 1) Check(blended.SequenceEqual(crouched), "full blend retains the exact accepted crouch endpoint");
                            Check(p.ModCaptureCollisionState() == collision && eye == (p.ModBipedEyeOffset, p.CameraInfo.Position, p.CameraInfo.Target)
                                && stance == (p.ModCrouched, p.ModSliding, p.ModBipedBottom, p.ModBipedTop),
                                "partial visual pose cannot alter accepted collision, stance or eye state");
                            Check(p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var afterFacts) && afterFacts == sourceFacts
                                && (Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(p)! == muzzle
                                && (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(p)! == aimPosition,
                                "partial visual pose cannot alter retained source facts or native shot anchors");
                            present.Invoke(p, new object?[] { model, model.Nodes[spine], world, pitch, null, null });
                            Check(model.Nodes.Select(node => node.Animation).SequenceEqual(crouched)
                                && model.MatrixStackValues.SequenceEqual(canonicalStack),
                                "canonical presentation exactly restores accepted current nodes and matrix stack after a partial picture");
                            Check(p.ModCaptureCollisionState() == collision
                                && eye == (p.ModBipedEyeOffset, p.CameraInfo.Position, p.CameraInfo.Target)
                                && stance == (p.ModCrouched, p.ModSliding, p.ModBipedBottom, p.ModBipedTop)
                                && p.ModTryCaptureCustomPrecombatFacts(p.ModGunVector, out var restoredFacts) && restoredFacts == sourceFacts
                                && (Vector3)typeof(PlayerEntity).GetField("_muzzlePos", Hidden)!.GetValue(p)! == muzzle
                                && (Vector3)typeof(PlayerEntity).GetField("_aimPosition", Hidden)!.GetValue(p)! == aimPosition,
                                "canonical restoration leaves accepted collision, eye and shot facts unchanged");
                            blends.Add(new { amount, torsoDrop = native[spine].Row3.Y - blended[spine].Row3.Y });
                        }
                        rows.Add(new { sample, lod, model = model.Name, lowerFrame = lower.AnimInfo.NodeFrame,
                            upperFrame = upper.AnimInfo.NodeFrame, expectedDrop = drop,
                            torsoDrop = native[spine].Row3.Y - crouched[spine].Row3.Y, legs, blends });
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
