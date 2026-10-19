using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Exercise the real first-person pose path without assets or a GL window.</summary>
internal static class HitReactionPresentationCheck
{
    internal static bool Run()
    {
        int priorCap = FrameTiming.FrameRateCap;
        bool priorPro = Features.ProHud, priorFixed = Features.FixedCrosshair;
        bool priorWeapon = Features.FixedWeapon, priorProWeapon = Features.ProHudFixedWeapon;
        var priorGame = GameState.Current;
        var priorPlayers = PlayerEntity.LegacyRegistry;
        var priorRandom = Rng.Current;
        bool ok = true;
        void Check(bool value, string label)
        {
            ok &= value;
            Console.WriteLine($"FRAMETIMING {(value ? "ok  " : "FAIL")} hit presentation | {label}");
        }
        try
        {
            Features.ProHud = false;
            Features.FixedCrosshair = true;
            Features.FixedWeapon = true;
            var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
                SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
            scene.SetFreeCamera(false);
            Check(scene.CameraMode == CameraMode.Player,
                "initial POV request selects player camera even when free-camera flag is already false");
            var player = scene.Players.Main;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!
                .SetValue(player, Metadata.PlayerValues[(int)Hunter.Samus]);
            void Set(string name, Vector3 value) => typeof(PlayerEntity)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
            void Pose(Vector3 aim, Vector3 cameraFacing, float fov = 78)
            {
                Set("_gunVec1", aim);
                Set("_aimVec", aim);
                Set("_upVector", Vector3.UnitY);
                var basis = EntityBase.GetTransformMatrix(aim, Vector3.UnitY);
                Set("_gunDrawPos", Vector3.TransformVector(new Vector3(.2f, -.15f, .5f), basis));
                player.CameraInfo.Position = Vector3.Zero;
                player.CameraInfo.Target = cameraFacing;
                player.CameraInfo.UpVector = Vector3.UnitY;
                player.CameraInfo.Fov = fov;
                player.CameraInfo.Update();
                player.CameraInfo.ModCaptureDrawState();
                player.ModCaptureFirstPersonDrawState();
            }
            (Vector3 Cannon, Vector3 Camera, float Fov, Vector3 Position, Vector3 GunPosition) Render(double alpha, float pointer = 0)
            {
                if (!player.ModPrepareFirstPersonRenderPose(alpha, pointer, 0, 0, 0,
                        out var view, out Vector3 position, out float fov)
                    || !player.ModGetFirstPersonGunTransform(out var gun))
                    throw new InvalidOperationException("First-person pose was unavailable.");
                return (Vector3.TransformVector(gun.Row2.Xyz, view).Normalized(),
                    -view.Inverted().Row2.Xyz.Normalized(), fov, position, gun.Row3.Xyz);
            }
            bool Near(Vector3 a, Vector3 b) => (a - b).Length < .0001f;
            Vector3 forward = -Vector3.UnitZ;
            Vector3 shaken = new Vector3(.3f, .1f, -1).Normalized();
            foreach (int rate in new[] { 120, 144, 240 })
            foreach (int policy in new[] { 0, 1, 2 })
            {
                Features.ProHud = policy == 1;
                Features.FixedCrosshair = policy != 2;
                Features.ProHudFixedWeapon = false;
                FrameTiming.FrameRateCap = rate;
                FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
                void MovingPose(float x, float gunX, float fov)
                {
                    Set("_gunVec1", forward); Set("_aimVec", forward); Set("_upVector", Vector3.UnitY);
                    var position = new Vector3(x, 0, 0);
                    Set("_gunDrawPos", position + new Vector3(gunX, -.15f, -.5f));
                    player.CameraInfo.Position = position;
                    player.CameraInfo.Target = position + forward;
                    player.CameraInfo.UpVector = Vector3.UnitY;
                    player.CameraInfo.Fov = fov;
                    player.CameraInfo.Update();
                    player.CameraInfo.ModCaptureDrawState();
                    player.ModCaptureFirstPersonDrawState();
                }
                MovingPose(0, .2f, 78);
                player.CameraInfo.ModResetDrawState(); player.ModResetFirstPersonDrawState();
                MovingPose(2, .8f, 38);
                var current = Render(1);
                Check(Near(Render(.5).Position, new Vector3(1, 0, 0))
                    && !Near(Render(.5).GunPosition, current.GunPosition),
                    $"{rate} Hz camera policy {policy} exercises fractional movement and cannon offsets");
                FrameTiming.Reset();
                var recovered = Render(.5);
                Check(Near(recovered.Position, current.Position) && Near(recovered.GunPosition, current.GunPosition)
                    && recovered.Fov == current.Fov,
                    $"{rate} Hz camera policy {policy} rejects pre-focus camera and cannon history together");
                Check(Near(player.CameraInfo.ModGetDrawPosition(.5), new Vector3(1, 0, 0)),
                    $"{rate} Hz camera policy {policy} retains explicit replay camera sampling across a live reset");
                player.ModPrepareObservedFirstPersonViewmodel(.5, current.Position, current.Position + forward,
                    Vector3.UnitY, current.Fov, Matrix4.LookAt(current.Position, current.Position + forward, Vector3.UnitY));
                Check(player.ModGetFirstPersonGunTransform(out var observed)
                    && !Near(observed.Row3.Xyz, current.GunPosition),
                    $"{rate} Hz camera policy {policy} retains explicit observed cannon interpolation across a live reset");
                MovingPose(3, 1, 34);
                Check(Near(Render(.5).Position, Render(1).Position)
                    && Near(Render(.5).GunPosition, Render(1).GunPosition),
                    $"{rate} Hz camera policy {policy} first post-focus capture stays current");
                MovingPose(4, 1.2f, 30);
                Check(Near(Render(.5).Position, new Vector3(3.5f, 0, 0))
                    && !Near(Render(.5).GunPosition, Render(1).GunPosition),
                    $"{rate} Hz camera policy {policy} consecutive post-focus captures resume together");
                FrameTiming.Advance(double.NaN);
                Check(Near(Render(.5).Position, Render(1).Position)
                    && Near(Render(.5).GunPosition, Render(1).GunPosition),
                    $"{rate} Hz camera policy {policy} discarded intervals fence camera and cannon");
            }
            Features.ProHud = false; Features.FixedCrosshair = true;
            // Exercise the returned render FOV, not just CameraInfo's helper:
            // responsive first person previously bypassed the sampled scope
            // FOV while already sampling camera translation and cannon offsets.
            foreach (int rate in new[] { 60, 120, 144, 240 })
            foreach (bool proHud in new[] { false, true })
            {
                Features.ProHud = proHud;
                Features.ProHudFixedWeapon = false;
                FrameTiming.FrameRateCap = rate;
                FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
                Pose(forward, forward, 78);
                player.CameraInfo.ModResetDrawState();
                player.ModResetFirstPersonDrawState();
                Pose(forward, forward, 38);
                float canonicalFov = player.CameraInfo.Fov;
                Vector3 canonicalPosition = player.Position;
                Vector3 canonicalCamera = player.CameraInfo.Position;
                Vector3 canonicalTarget = player.CameraInfo.Target;
                Vector3 canonicalAim = player.ModGunVector;
                uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
                bool transition = true;
                foreach (double alpha in new[] { 0d, .25, .5, .75, 1d })
                {
                    float expectedFov = rate == 60 ? 38 : 78 - 40 * (float)alpha;
                    transition &= MathF.Abs(Render(alpha).Fov - expectedFov) < .0001f;
                }
                Check(transition, $"{rate} Hz {(proHud ? "Pro HUD Dynamic" : "fixed crosshair")} scope uses the shared render fraction");
                Pose(forward, forward, 48);
                Check(MathF.Abs(Render(.5).Fov - (rate == 60 ? 48 : 43)) < .0001f,
                    $"{rate} Hz scope reversal stays between completed FOV endpoints");
                Pose(forward, forward, 38);
                var inputBefore = Render(.5);
                var inputAfter = Render(.5, 10);
                Check(!Near(inputBefore.Camera, inputAfter.Camera)
                    && MathF.Abs(inputBefore.Fov - inputAfter.Fov) < .0001f,
                    $"{rate} Hz scope sampling keeps pending local aim responsive");
                Check(player.CameraInfo.Fov == canonicalFov && player.Position == canonicalPosition
                    && player.CameraInfo.Position == canonicalCamera && player.CameraInfo.Target == canonicalTarget
                    && player.ModGunVector == canonicalAim && scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2,
                    $"{rate} Hz scope draws preserve canonical FOV, position, aim and both RNG streams");

                // Existing spawn/rejoin/seek reset and teleport owners must
                // immediately rebase a scope rather than carrying its old zoom.
                player.CameraInfo.Fov = 78;
                player.CameraInfo.ModResetDrawState();
                Check(MathF.Abs(Render(0).Fov - 78) < .0001f,
                    $"{rate} Hz explicit camera lifetime reset rebases scope");
                Pose(forward, forward, 38);
                player.CameraInfo.Position = new Vector3(10, 0, 0);
                player.CameraInfo.Target = player.CameraInfo.Position + forward;
                player.CameraInfo.Fov = 78;
                player.CameraInfo.Update();
                player.CameraInfo.ModCaptureDrawState();
                player.ModCaptureFirstPersonDrawState();
                Check(MathF.Abs(Render(0).Fov - 78) < .0001f,
                    $"{rate} Hz camera teleport rebases scope");

                if (rate > 60)
                {
                    Pose(forward, forward, 78);
                    player.CameraInfo.ModResetDrawState();
                    Pose(forward, forward, 38);
                    FrameTiming.Reset();
                    Check(MathF.Abs(Render(.5).Fov - 38) < .0001f,
                        $"{rate} Hz focus clock reset rejects old live FOV immediately");
                    Check(MathF.Abs(player.CameraInfo.ModGetDrawFov(.5) - 58) < .0001f,
                        $"{rate} Hz explicit replay FOV sampling retains its existing reset owner");
                    Pose(forward, forward, 34);
                    Check(MathF.Abs(Render(.5).Fov - 34) < .0001f,
                        $"{rate} Hz first post-focus capture stays on current FOV");
                    Pose(forward, forward, 30);
                    Check(MathF.Abs(Render(.5).Fov - 32) < .0001f,
                        $"{rate} Hz consecutive post-focus capture resumes scope sampling");
                    FrameTiming.Advance(double.NaN);
                    Check(MathF.Abs(Render(.5).Fov - 30) < .0001f,
                        $"{rate} Hz discarded clock interval rejects old live FOV");
                }
            }
            Features.ProHud = false;
            foreach (int rate in new[] { 120, 144, 240 })
            {
                FrameTiming.FrameRateCap = rate;
                FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
                Pose(forward, forward);
                player.ModResetFirstPersonDrawState();
                Vector3 before = Render(1).Cannon;
                // A hit changes the view, not the firing ray. This is the same
                // relationship CameraInfo.Update produces when it applies shake.
                Pose(forward, shaken);
                var start = Render(0);
                var middle = Render(.5);
                var end = Render(1);
                Check(Near(start.Cannon, before), $"{rate} Hz hit starts at previous cannon pose");
                Check(!Near(middle.Cannon, start.Cannon) && !Near(middle.Cannon, end.Cannon)
                    && Vector3.Dot(middle.Cannon, (start.Cannon + end.Cannon).Normalized()) > .99999f,
                    $"{rate} Hz cannon rotates between hit samples");
                var tinyInput = Render(.5, .001f);
                Check((tinyInput.Camera - middle.Camera).Length < .0001f,
                    $"{rate} Hz tiny aim input preserves hit shake");
                Pose(forward, forward);
                Check(Near(Render(0).Cannon, end.Cannon), $"{rate} Hz hit recovery has no boundary snap");

                // Interpolating camera-local effects must not delay a real turn.
                player.ModResetFirstPersonDrawState();
                Pose(-Vector3.UnitX, -Vector3.UnitX);
                var turn = Render(0);
                Check(Near(turn.Camera, -Vector3.UnitX) && Near(turn.Cannon, before),
                    $"{rate} Hz camera and cannon turn immediately together");

                Features.ProHud = true;
                Features.ProHudFixedWeapon = false;
                // Reproduce the v0.1.34 regression: Dynamic keeps an eased
                // simulation camera (forward) while raw mouse aim has already
                // turned left. Presentation must follow raw aim immediately
                // without destroying the Dynamic simulation state.
                Pose(forward, forward);
                Set("_facingVector", forward);
                Set("_gunVec1", -Vector3.UnitX);
                Set("_aimVec", -Vector3.UnitX);
                var dynamicStart = Render(0);
                var dynamicMiddle = Render(.5);
                var dynamicEnd = Render(1);
                Check(Near(dynamicStart.Camera, -Vector3.UnitX)
                    && Near(dynamicMiddle.Camera, -Vector3.UnitX)
                    && Near(dynamicEnd.Camera, -Vector3.UnitX),
                    $"{rate} Hz Pro HUD Dynamic renders raw aim without legacy camera lag");
                var dynamicLate = Render(.5, 10);
                Check(!Near(dynamicLate.Camera, dynamicMiddle.Camera),
                    $"{rate} Hz Pro HUD Dynamic late-latches pending mouse aim");
                Check(player.CameraInfo.Facing == forward,
                    $"{rate} Hz Pro HUD Dynamic leaves eased simulation camera untouched");
                Features.ProHud = false;
            }
            // Spectator/replay POV does not late-latch local input, but its
            // arm cannon must still consume the same fractional camera pose.
            FrameTiming.FrameRateCap = 144;
            FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
            Pose(forward, forward);
            player.ModResetFirstPersonDrawState();
            Pose(shaken, shaken);
            player.CameraInfo.ModGetFirstPersonDrawPose(.5,
                out Vector3 observedPosition, out Vector3 observedTarget,
                out Vector3 observedUp, out float observedFov);
            Matrix4 observedView = Matrix4.LookAt(
                observedPosition, observedTarget, observedUp);
            bool observedPrepared = player.ModPrepareObservedFirstPersonViewmodel(.5,
                observedPosition, observedTarget, observedUp,
                observedFov, observedView);
            bool observedGunReady = player.ModGetFirstPersonGunTransform(out Matrix4 observedGun);
            Check(observedPrepared && observedGunReady,
                "144 Hz observed POV prepares a fractional arm-cannon pose");
            Vector3 observedCannon = observedGunReady
                ? Vector3.TransformVector(observedGun.Row2.Xyz, observedView).Normalized()
                : Vector3.Zero;
            Vector3 observedCamera = -observedView.Inverted().Row2.Xyz.Normalized();
            Check(Vector3.Dot(observedCannon, observedCamera) > .95f,
                "observed POV camera and arm cannon share one presentation basis");

            FrameTiming.FrameRateCap = 60;
            FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
            Pose(forward, forward);
            player.ModResetFirstPersonDrawState();
            Pose(forward, shaken);
            Check(Near(Render(0).Cannon, Render(1).Cannon), "60 Hz keeps the current pose");
            Vector3 ObservedCannon(double alpha)
            {
                player.ModPrepareObservedFirstPersonViewmodel(alpha,
                    Vector3.Zero, forward, Vector3.UnitY, 78,
                    Matrix4.LookAt(Vector3.Zero, forward, Vector3.UnitY));
                player.ModGetFirstPersonGunTransform(out var gun);
                return gun.Row2.Xyz.Normalized();
            }
            Vector3 observedStart = ObservedCannon(0), observedEnd = ObservedCannon(1);
            Check(!Near(observedStart, observedEnd)
                && !Near(ObservedCannon(.5), observedStart)
                && !Near(ObservedCannon(.5), observedEnd),
                "60 Hz observed slow motion interpolates cannon between recorded poses");
            // Network aim may correct the camera position after Update cached
            // Facing. Capturing the cannon must use the final camera endpoints.
            Pose(forward, forward);
            player.CameraInfo.Position = new Vector3(.2f, .1f, 0);
            player.ModResetFirstPersonDrawState();
            var correctedView = Matrix4.LookAt(player.CameraInfo.Position,
                player.CameraInfo.Target, Vector3.UnitY);
            player.ModPrepareObservedFirstPersonViewmodel(1, player.CameraInfo.Position,
                player.CameraInfo.Target, Vector3.UnitY, 78, correctedView);
            player.ModGetFirstPersonGunTransform(out var correctedGun);
            Check(Near(correctedGun.Row2.Xyz.Normalized(), forward),
                "observed cannon uses corrected camera endpoints instead of stale facing");
            // A remote turn arrives after movement has cached its weapon basis.
            // The refreshed basis must keep the cannon offset fixed across yaw.
            Vector3 GunOffset(Vector3 direction)
            {
                player.ModSetFacing(direction);
                player.ModRefreshObservedAimBasis();
                player.CameraInfo.Position = Vector3.Zero;
                player.CameraInfo.Target = direction;
                player.CameraInfo.UpVector = Vector3.UnitY;
                player.CameraInfo.Update();
                typeof(PlayerEntity).GetMethod("UpdateAimVecs", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(player, null);
                player.ModResetFirstPersonDrawState();
                var view = Matrix4.LookAt(Vector3.Zero, direction, Vector3.UnitY);
                player.ModPrepareObservedFirstPersonViewmodel(1, Vector3.Zero, direction,
                    Vector3.UnitY, 78, view);
                player.ModGetFirstPersonGunTransform(out var gun);
                return Vector3.TransformPosition(gun.Row3.Xyz, view);
            }
            Check(Near(GunOffset(-Vector3.UnitZ), GunOffset(-Vector3.UnitX)),
                "remote yaw rebuild keeps cannon camera-local offset stable");
            Vector3 beforeCorrection = player.CameraInfo.Position;
            Vector3 beforeDirection = player.CameraInfo.Target - beforeCorrection;
            var correction = new Vector3(.4f, -.2f, .1f);
            player.ModTranslateReplayPresentation(correction);
            Check(Near(player.CameraInfo.Position, beforeCorrection + correction)
                && Near(player.CameraInfo.Target - player.CameraInfo.Position, beforeDirection),
                "replica position correction moves camera without changing its aim");
            Vector3 savedTarget = player.CameraInfo.Target;
            Vector3 savedAim = player.ModGunVector;
            uint savedRng = scene.Random.Rng2;
            Render(.25, 10); Render(.75, -10);
            Check(player.CameraInfo.Target == savedTarget && player.ModGunVector == savedAim
                && scene.Random.Rng2 == savedRng, "draws preserve simulation aim, hit shake and RNG");
            return ok;
        }
        finally
        {
            Features.ProHud = priorPro; Features.FixedCrosshair = priorFixed;
            Features.FixedWeapon = priorWeapon; Features.ProHudFixedWeapon = priorProWeapon;
            FrameTiming.FrameRateCap = priorCap;
            FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
            GameState.Current = priorGame; PlayerEntity.LegacyRegistry = priorPlayers; Rng.Current = priorRandom;
        }
    }
}
