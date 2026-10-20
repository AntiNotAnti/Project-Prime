using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// What <c>-frametimingcheck</c> runs: the accumulator on its own, against
    /// frame times chosen rather than measured.
    ///
    /// The half of the decoupling that can silently be wrong is arithmetic --
    /// a game that runs at 60.4 Hz instead of 60 loses a second every two and
    /// a half minutes, which is not visible in a screenshot and is fatal to a
    /// match. It needs no room, no window and no display, so it is checked
    /// here; <c>-maptest -drawrate N</c> checks the other half, that drawing
    /// more often does not change what the world does.
    /// </summary>
    public static class FrameTimingCheck
    {
        private sealed class Case
        {
            public string Name = "";
            public double Seconds;
            public Func<int, double> FrameTime = null!;
            public double ExpectedStepsPerSecond = FrameTiming.SimulationHz;
            public double TolerancePercent = 0.5;
            public int MaxStepsInOneFrame = FrameTiming.MaxCatchUpSteps;
        }

        public static int Run()
        {
            var rng = new Random(20260905);
            Case[] cases = new[]
            {
                new Case
                {
                    // The old behaviour, and the one that must not move.
                    Name = "60 Hz display",
                    Seconds = 120,
                    FrameTime = _ => 1 / 60.0,
                    MaxStepsInOneFrame = 1
                },
                new Case
                {
                    Name = "144 Hz display",
                    Seconds = 120,
                    FrameTime = _ => 1 / 144.0,
                    MaxStepsInOneFrame = 1
                },
                new Case
                {
                    Name = "240 Hz display",
                    Seconds = 120,
                    FrameTime = _ => 1 / 240.0,
                    MaxStepsInOneFrame = 1
                },
                new Case
                {
                    // Reported high-refresh ghosting was easiest to see here:
                    // nine pictures may be drawn between two simulation steps.
                    Name = "540 Hz display",
                    Seconds = 120,
                    FrameTime = _ => 1 / 540.0,
                    MaxStepsInOneFrame = 1
                },
                new Case
                {
                    Name = "165 Hz display (not a multiple of 60)",
                    Seconds = 300,
                    FrameTime = _ => 1 / 165.0,
                    MaxStepsInOneFrame = 1
                },
                new Case
                {
                    // A machine that cannot keep 60. The old loop ran the game
                    // in slow motion here; this must still deliver 60 steps a
                    // second of game for every second of wall clock.
                    Name = "40 Hz, a machine that cannot keep up",
                    Seconds = 120,
                    FrameTime = _ => 1 / 40.0,
                    MaxStepsInOneFrame = 2
                },
                new Case
                {
                    Name = "jittery 144 Hz",
                    Seconds = 300,
                    FrameTime = _ => 1 / 144.0 * (0.4 + rng.NextDouble() * 1.2),
                    // Jitter is symmetric, so the mean holds; allow a little
                    // more for the tail of one 300-second sample.
                    TolerancePercent = 1.0,
                    MaxStepsInOneFrame = 3
                },
                new Case
                {
                    Name = "vsync flipping between 144 and 72",
                    Seconds = 300,
                    FrameTime = i => (i % 7 == 0 ? 2 : 1) / 144.0,
                    MaxStepsInOneFrame = 2
                },
                new Case
                {
                    // Unlimited has no presentation deadline. Exercise the
                    // fixed-step arithmetic far beyond the old 500 FPS ceiling.
                    Name = "1500 Hz unlimited drawing",
                    Seconds = 120,
                    FrameTime = _ => 1 / 1500.0,
                    MaxStepsInOneFrame = 1
                }
            };

            int failures = 0;
            failures += RunCapPolicyCase() ? 0 : 1;
            foreach (Case test in cases)
            {
                failures += RunCase(test) ? 0 : 1;
            }
            failures += RunStallCase() ? 0 : 1;
            failures += RunPresentationAlphaCase() ? 0 : 1;
            failures += RunDynamicCrosshairCameraPolicyCase() ? 0 : 1;
            failures += RunFirstPersonPresentationCase() ? 0 : 1;
            failures += HitReactionPresentationCheck.Run() ? 0 : 1;
            failures += RunFixedCameraTranslationCase() ? 0 : 1;
            failures += RunAngularCameraPresentationCase() ? 0 : 1;
            failures += RunThirdPersonFormHandoffCase() ? 0 : 1;
            failures += RunThirdPersonSpawnAndNearWallCase() ? 0 : 1;
            failures += RunThirdPersonShotReticleCase() ? 0 : 1;
            failures += RunThirdPersonRapidTurnReticleCase() ? 0 : 1;
            failures += RunFastLateAimCase() ? 0 : 1;
            failures += RunLockjawNoiseCases();
            Console.WriteLine(failures == 0
                ? "FRAMETIMING all cases pass"
                : $"FRAMETIMING {failures} case(s) FAILED");
            return failures;
        }

        private static bool RunCapPolicyCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            try
            {
                FrameTiming.FrameRateCap = FrameTiming.Unlimited;
                bool ok = FrameTiming.FrameRateCap == FrameTiming.Unlimited
                    && FrameTiming.HighRefreshPresentation
                    && FrameTiming.ParseCap("unlimited", 144) == FrameTiming.Unlimited
                    && FrameTiming.ParseCap("UNCAPPED", 144) == FrameTiming.Unlimited
                    && FrameTiming.ParseCap(" -1 ", 144) == FrameTiming.Unlimited
                    && FrameTiming.ParseCap(FrameTiming.CapString(FrameTiming.Unlimited), 144)
                        == FrameTiming.Unlimited
                    && FrameTiming.ParseCap(FrameTiming.CapString(FrameTiming.DisplayRate), 144)
                        == FrameTiming.DisplayRate
                    && FrameTiming.ParseCap(FrameTiming.CapString(144), 60) == 144
                    && FrameTiming.ParseCap("500", 144) == 500
                    && FrameTiming.ParseSavedCap("500", 144) == FrameTiming.Unlimited
                    && FrameTiming.ParseSavedCap("240", 144) == 240
                    && FrameTiming.ParseSavedCap("invalid", 144) == 144
                    && FrameTiming.CapString(FrameTiming.Unlimited) == "unlimited";

                FrameTiming.FrameRateCap = -2;
                ok &= FrameTiming.FrameRateCap == FrameTiming.DisplayRate;

                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} "
                    + "unlimited cap round-trip and legacy Settings migration");
                return ok;
            }
            finally
            {
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
            }
        }

        private static bool RunCase(Case test)
        {
            FrameTiming.Reset();
            FrameTiming.ResetDiagnostics();
            double elapsed = 0;
            long steps = 0;
            int worstFrame = 0;
            int frame = 0;
            while (elapsed < test.Seconds)
            {
                double dt = test.FrameTime(frame++);
                elapsed += dt;
                int taken = FrameTiming.Advance(dt);
                steps += taken;
                if (taken > worstFrame)
                {
                    worstFrame = taken;
                }
            }
            double rate = steps / elapsed;
            double drift = Math.Abs(rate - test.ExpectedStepsPerSecond)
                / test.ExpectedStepsPerSecond * 100;
            bool ok = drift <= test.TolerancePercent
                && worstFrame <= test.MaxStepsInOneFrame
                && FrameTiming.DroppedSteps == 0;
            // Seconds of game per second of wall clock, which is the number a
            // player would feel: 1.00 is right, 0.97 is a match clock that
            // loses two minutes an hour.
            double gameSeconds = steps * FrameTiming.StepSeconds;
            Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} {test.Name}"
                + $" | {frame} frames over {elapsed:0.0} s"
                + $" | {steps} steps = {rate:0.000} Hz (drift {drift:0.000}%)"
                + $" | game ran {gameSeconds / elapsed:0.0000}x real time"
                + $" | worst frame {worstFrame} step(s)"
                + $" | dropped {FrameTiming.DroppedSteps}");
            return ok;
        }

        /// <summary>
        /// High-refresh presentation must interpolate only when there really
        /// are extra pictures between simulation steps. At 60 Hz the draw
        /// state stays current, while a 144 Hz presentation sees the
        /// accumulator's fractional remainder.
        /// </summary>
        private static bool RunPresentationAlphaCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            try
            {
                FrameTiming.FrameRateCap = 60;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
                FrameTiming.Advance(FrameTiming.StepSeconds * 0.4);
                bool sixtyCurrent = Math.Abs(FrameTiming.PresentationAlpha - 1.0) < 0.000001;

                FrameTiming.FrameRateCap = 144;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
                FrameTiming.Advance(FrameTiming.StepSeconds * 0.4);
                bool highRefreshFraction = Math.Abs(FrameTiming.PresentationAlpha - 0.4) < 0.000001;

                bool ok = sixtyCurrent && highRefreshFraction;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} presentation alpha"
                    + $" | 60 Hz={ (sixtyCurrent ? "current" : "interpolated") }"
                    + $" | 144 Hz={FrameTiming.PresentationAlpha:0.000}");
                return ok;
            }
            finally
            {
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
            }
        }

        /// <summary>
        /// Dynamic/Metroid keeps the arm cannon and crosshair visually dynamic,
        /// while both weapon styles keep the responsive raw-aim camera. Static
        /// must keep its crosshair welded to the firing ray.
        /// </summary>
        private static bool RunDynamicCrosshairCameraPolicyCase()
        {
            bool priorPro = Features.ProHud;
            bool priorWeapon = Features.ProHudFixedWeapon;
            try
            {
                Features.ProHud = true;
                Features.ProHudFixedWeapon = false;
                bool dynamicOk = Features.FixedCrosshair
                    && !Features.FixedWeapon
                    && !Features.FixedAimCamera
                    && Features.ResponsiveAimCamera
                    && PlayerEntity.ModDynamicReticlePresentation(
                        Features.ProHud, Features.FixedWeapon);

                Features.ProHudFixedWeapon = true;
                bool staticOk = Features.FixedCrosshair
                    && Features.FixedWeapon
                    && Features.FixedAimCamera
                    && Features.ResponsiveAimCamera
                    && !PlayerEntity.ModDynamicReticlePresentation(
                        Features.ProHud, Features.FixedWeapon);

                bool classicOk = !PlayerEntity.ModDynamicReticlePresentation(
                    proHud: false, fixedWeapon: false);
                bool ok = dynamicOk && staticOk && classicOk;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} Pro HUD dynamic crosshair camera"
                    + $" | dynamic={(dynamicOk ? "responsive camera / moving reticle" : "wrong policy")}"
                    + $" | static={(staticOk ? "responsive camera / fixed reticle" : "wrong policy")}");
                return ok;
            }
            finally
            {
                Features.ProHud = priorPro;
                Features.ProHudFixedWeapon = priorWeapon;
            }
        }

        /// <summary>
        /// A render-time camera rotation must preserve the arm cannon's
        /// camera-local transform exactly. This is the invariant that prevents
        /// the late-latched camera from running ahead of a 60 Hz viewmodel.
        /// </summary>
        private static bool RunFirstPersonPresentationCase()
        {
            Vector3 fromFacing = -Vector3.UnitZ;
            Vector3 up = Vector3.UnitY;
            Vector3 toFacing = -Vector3.UnitX;

            // In MPH's basis, right is cross(up, facing). For -Z that is -X;
            // after a 90-degree left turn to -X it is +Z.
            Vector3 gunOffset = new Vector3(-0.25f, 0.10f, -0.50f);
            Vector3 expectedOffset = new Vector3(-0.50f, 0.10f, 0.25f);

            bool offsetMapped = PlayerEntity.ModRotatePresentationVector(
                gunOffset, fromFacing, up, toFacing, up, out Vector3 mappedOffset);
            bool facingMapped = PlayerEntity.ModRotatePresentationVector(
                fromFacing, fromFacing, up, toFacing, up, out Vector3 mappedFacing);

            bool offsetOk = offsetMapped
                && (mappedOffset - expectedOffset).LengthSquared < 0.000001f;
            bool facingOk = facingMapped
                && (mappedFacing - toFacing).LengthSquared < 0.000001f;
            bool ok = offsetOk && facingOk;

            Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} first-person shared pose"
                + $" | offset error {(mappedOffset - expectedOffset).Length:0.000000}"
                + $" | facing error {(mappedFacing - toFacing).Length:0.000000}");
            return ok;
        }

        /// <summary>
        /// Fixed-crosshair first-person translation must stay between completed
        /// camera samples. A draw-only frame may smooth known motion, but it must
        /// never predict past the current 60 Hz state and then correct backward
        /// when the next simulation step arrives.
        /// </summary>
        private static bool RunFixedCameraTranslationCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            try
            {
                FrameTiming.FrameRateCap = 144;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();

                var camera = new CameraInfo
                {
                    Position = Vector3.Zero,
                    Target = -Vector3.UnitZ,
                    UpVector = Vector3.UnitY,
                    Fov = 78
                };
                camera.ModResetDrawState();

                camera.Position = new Vector3(1f, .10f, 0);
                camera.ModCaptureDrawState();
                camera.Position = new Vector3(2f, .20f, 0);
                camera.ModCaptureDrawState();

                Vector3 steady = camera.ModGetDrawPosition(0.5);
                bool steadyOk = (steady - new Vector3(1.5f, .15f, 0)).LengthSquared
                    < 0.00000001f;
                bool boundedOk = steady.X >= 1f && steady.X <= 2f;

                camera.Position = new Vector3(2.25f, .10f, 0);
                camera.ModCaptureDrawState();
                Vector3 decelerating = camera.ModGetDrawPosition(0.5);
                bool decelerationOk = (decelerating - new Vector3(2.125f, .15f, 0)).LengthSquared
                    < 0.00000001f;

                camera.Position = new Vector3(2f, .05f, 0);
                camera.ModCaptureDrawState();
                Vector3 reversed = camera.ModGetDrawPosition(0.5);
                bool reversalOk = (reversed - new Vector3(2.125f, .075f, 0)).LengthSquared
                    < 0.00000001f;

                // Teleport-sized camera motion rebases history rather than smearing.
                camera.Position = new Vector3(10f, 0, 0);
                camera.ModCaptureDrawState();
                Vector3 rebased = camera.ModGetDrawPosition(0.5);
                bool rebaseOk = (rebased - camera.Position).LengthSquared < 0.0000000001f;

                // At 60 Hz PresentationAlpha is current-state (1.0), so this path
                // adds no interpolation latency when there are no extra pictures.
                FrameTiming.FrameRateCap = 60;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
                camera.Position = new Vector3(11f, 0, 0);
                camera.ModCaptureDrawState();
                Vector3 sixty = camera.ModGetDrawPosition(FrameTiming.PresentationAlpha);
                bool sixtyOk = (sixty - camera.Position).LengthSquared < 0.0000000001f;

                bool ok = steadyOk && boundedOk && decelerationOk && reversalOk && rebaseOk && sixtyOk;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} fixed camera translation"
                    + $" | steady {steady.X:0.00000}/{steady.Y:0.00000}"
                    + $" | decel {decelerating.X:0.00000}/{decelerating.Y:0.00000}"
                    + $" | reverse {reversed.X:0.00000}/{reversed.Y:0.00000}"
                    + $" | rebase {rebased.X:0.00000}"
                    + $" | 60 Hz {sixty.X:0.00000}");
                return ok;
            }
            finally
            {
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
            }
        }

        /// <summary>
        /// Fast yaw must travel around the unit sphere. Blending two target
        /// points through world space can collapse the camera direction at the
        /// midpoint of a near-180 degree spin.
        /// </summary>
        private static bool RunAngularCameraPresentationCase()
        {
            Vector3 from = -Vector3.UnitZ;
            Vector3 to = -Vector3.UnitX;
            bool quarter = CameraInfo.ModInterpolateDirection(
                from, to, .5f, out Vector3 half);
            Vector3 expected = new Vector3(-1, 0, -1).Normalized();
            bool quarterOk = quarter && (half - expected).LengthSquared < 0.000001f;

            bool opposite = CameraInfo.ModInterpolateDirection(
                -Vector3.UnitZ, Vector3.UnitZ, .5f, out Vector3 oppositeHalf);
            bool oppositeOk = opposite
                && Single.IsFinite(oppositeHalf.X) && Single.IsFinite(oppositeHalf.Y)
                && Single.IsFinite(oppositeHalf.Z)
                && MathF.Abs(oppositeHalf.Length - 1f) < 0.00001f
                && MathF.Abs(Vector3.Dot(oppositeHalf, Vector3.UnitZ)) < 0.001f
                && MathF.Abs(oppositeHalf.Y) < 0.001f;

            var camera = new CameraInfo
            {
                Position = Vector3.Zero,
                Target = -Vector3.UnitZ,
                UpVector = Vector3.UnitY,
                Fov = 78
            };
            camera.ModResetDrawState();
            camera.Target = Vector3.UnitZ;
            camera.ModCaptureDrawState();
            bool pose = camera.ModGetFirstPersonDrawPose(.5,
                out Vector3 pos, out Vector3 target, out Vector3 up, out _);
            Vector3 poseFacing = target - pos;
            bool poseOk = pose && poseFacing.LengthSquared > .99f
                && Single.IsFinite(poseFacing.X) && Single.IsFinite(poseFacing.Y)
                && Single.IsFinite(poseFacing.Z) && up.LengthSquared > .99f;

            bool ok = quarterOk && oppositeOk && poseOk;
            Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} angular camera presentation"
                + $" | 90deg midpoint {half}"
                + $" | 180deg midpoint length {oppositeHalf.Length:0.00000}"
                + $" | pose direction length {poseFacing.Length:0.00000}");
            return ok;
        }

        /// <summary>
        /// A native morph switches its simulation camera from First to Third1/2,
        /// but the third-person biped renderer was showing a different eye.
        /// The world-view handoff must begin on the exact last picture, converge
        /// independently of refresh rate, support reversal and reset on a new
        /// presentation generation. This is presentation-only: no GL, player,
        /// simulation, assets, or networking are involved.
        /// </summary>
        private static bool RunThirdPersonFormHandoffCase()
        {
            static Vector3 Eye(Matrix4 view) => view.Inverted().Row3.Xyz;
            static bool Close(Matrix4 a, Matrix4 b) =>
                (Eye(a) - Eye(b)).LengthSquared < 0.000001f;
            const long generation = 42;
            Matrix4 shoulder = Matrix4.LookAt(
                new Vector3(0, 1.6f, 2.5f), new Vector3(0, 1.2f, 0), Vector3.UnitY);
            Matrix4 nativeMorphStart = Matrix4.LookAt(
                new Vector3(0, 1.4f, 0.1f), new Vector3(0, 1f, -1), Vector3.UnitY);
            Matrix4 ball = Matrix4.LookAt(
                new Vector3(1f, 1f, 2f), new Vector3(0, 0.5f, 0), Vector3.UnitY);
            Matrix4 backToBiped = Matrix4.LookAt(
                new Vector3(0.1f, 1.6f, 2.6f), new Vector3(0, 1.2f, 0), Vector3.UnitY);

            bool correct = true;
            foreach (int hz in new[] { 60, 120, 240 })
            {
                var bridge = new ThirdPersonFormCameraHandoff();
                const double firstTick = 100;
                correct &= Close(bridge.Present(shoulder,
                    CameraType.First, firstTick, generation, out bool initialBlend), shoulder)
                    && !initialBlend;
                // One native sim step later CameraType changes, but the
                // rendered eye must not snap back to the first-person origin.
                Matrix4 firstMorph = bridge.Present(nativeMorphStart,
                    CameraType.Third1, firstTick + 1, generation, out bool morphBlend);
                correct &= morphBlend && Close(firstMorph, shoulder);
                Matrix4 middle = default;
                Matrix4 last = firstMorph;
                for (int i = 1; i <= hz; i++)
                {
                    double tick = firstTick + 1 + i * (60d / hz);
                    Matrix4 current = bridge.Present(ball, CameraType.Third1,
                        tick, generation, out bool moving);
                    if (i == (int)(hz * ThirdPersonFormCameraHandoff.DurationTicks / 120d))
                        middle = current;
                    last = current;
                }
                correct &= Close(last, ball)
                    && Eye(middle).LengthSquared > 0.01f
                    && (Eye(middle) - Eye(shoulder)).LengthSquared > 0.0001f
                    && (Eye(middle) - Eye(ball)).LengthSquared > 0.0001f;

                // Reverse direction: the start point is the displayed ball
                // camera, not a newly sampled first-person eye.
                Matrix4 firstUnmorph = bridge.Present(nativeMorphStart,
                    CameraType.First, firstTick + 62, generation, out bool returnBlend);
                correct &= returnBlend && Close(firstUnmorph, ball);
                Matrix4 finalBiped = bridge.Present(backToBiped,
                    CameraType.First, firstTick + 62 + ThirdPersonFormCameraHandoff.DurationTicks,
                    generation, out bool finished);
                correct &= Close(finalBiped, backToBiped) && !finished;

                // A form reversal before easing is complete must start from
                // that exact in-flight presentation pose.
                Matrix4 beginMorph = bridge.Present(nativeMorphStart,
                    CameraType.Third2, firstTick + 90, generation, out bool switching);
                Matrix4 midwayMorph = bridge.Present(ball, CameraType.Third2,
                    firstTick + 93, generation, out _);
                Matrix4 reverse = bridge.Present(backToBiped,
                    CameraType.First, firstTick + 94, generation, out bool reversing);
                correct &= switching && reversing && Close(beginMorph, backToBiped)
                    && Close(reverse, midwayMorph);

                // A camera saved in the previous scene or a teleported actor
                // must not be blended into the new one.
                Matrix4 resetView = bridge.Present(ball, CameraType.Third1,
                    firstTick + 95, generation + 1, out bool resetBlend);
                correct &= Close(resetView, ball) && !resetBlend;
                Matrix4 farCamera = Matrix4.LookAt(new Vector3(500, 2, 500),
                    new Vector3(500, 1, 499), Vector3.UnitY);
                correct &= Close(bridge.Present(farCamera, CameraType.First,
                    firstTick + 96, generation + 1, out bool teleportBlend), farCamera)
                    && !teleportBlend;
            }

            Console.WriteLine("FRAMETIMING " + (correct ? "ok  " : "FAIL")
                + " third-person biped / Morph Ball view handoff (60/120/240Hz)");
            return correct;
        }

        /// <summary>
        /// The shoulder camera must render the first active spawned frame,
        /// without requiring movement or wall clearance. A wall may compress
        /// its offset; fading only the local biped preserves collision and
        /// never restores the first-person cannon. This test also exercises
        /// the already-resolved render eye overload and respawn lifetime reset.
        /// </summary>
        private static bool RunThirdPersonSpawnAndNearWallCase()
        {
            bool active = ThirdPersonCameraPolicy.ShowShoulder(
                true, CameraType.First, false);
            bool notAlt = !ThirdPersonCameraPolicy.ShowShoulder(
                true, CameraType.Third1, true)
                && !ThirdPersonCameraPolicy.ShowShoulder(
                    true, CameraType.Third2, true)
                && !ThirdPersonCameraPolicy.ShowShoulder(
                    false, CameraType.First, false);
            float[] distances = { 0f, 0.2f, 0.3f, 0.6f, 0.8f, 1.05f, 2.4f };
            float prior = 0f;
            bool continuous = true;
            foreach (float distance in distances)
            {
                float alpha = ThirdPersonCameraPolicy.LocalBodyOpacity(distance);
                continuous &= active && float.IsFinite(alpha)
                    && alpha >= prior && alpha is >= 0f and <= 1f;
                prior = alpha;
            }
            continuous &= ThirdPersonCameraPolicy.LocalBodyOpacity(0f) == 0f
                && ThirdPersonCameraPolicy.LocalBodyOpacity(2.4f) == 1f
                && ThirdPersonCameraPolicy.LocalBodyOpacity(0.8f) > 0f
                && ThirdPersonCameraPolicy.LocalBodyOpacity(0.8f) < 1f
                && ThirdPersonCameraPolicy.LocalBodyOpacity(float.NaN) == 0f;

            Matrix4 first = Matrix4.LookAt(new Vector3(0.5f, 1.7f, 2.4f),
                new Vector3(0, 1, 0), Vector3.UnitY);
            Vector3 firstEye = first.Inverted().Row3.Xyz;
            var handoff = new ThirdPersonFormCameraHandoff();
            Matrix4 firstPicture = handoff.Present(first, firstEye,
                CameraType.First, 150, 7, out bool blended, out Vector3 displayedEye);
            bool firstFrame = !blended
                && (displayedEye - firstEye).LengthSquared < 0.000001f
                && (firstPicture.Inverted().Row3.Xyz - firstEye).LengthSquared < 0.000001f;

            Matrix4 ball = Matrix4.LookAt(new Vector3(2f, 1f, 3f),
                new Vector3(0, .5f, 0), Vector3.UnitY);
            handoff.Present(ball, ball.Inverted().Row3.Xyz,
                CameraType.Third1, 151, 7, out bool switching, out _);
            handoff.Reset(); // Spawn or respawn into a fresh playable life
            Matrix4 next = Matrix4.LookAt(new Vector3(12, 2, 13),
                new Vector3(12, 1, 10), Vector3.UnitY);
            Vector3 nextEye = next.Inverted().Row3.Xyz;
            Matrix4 restored = handoff.Present(next, nextEye,
                CameraType.First, 151, 7, out bool staleBlend, out Vector3 actual);
            bool respawn = switching && !staleBlend
                && (actual - nextEye).LengthSquared < 0.000001f
                && (restored.Inverted().Row3.Xyz - nextEye).LengthSquared < 0.000001f;

            bool ok = active && notAlt && continuous && firstFrame && respawn;
            Console.WriteLine("FRAMETIMING " + (ok ? "ok  " : "FAIL")
                + " shoulder camera first playable frame, near-wall fade and respawn reset");
            return ok;
        }

        /// <summary>
        /// Check muzzle-ray projection into an independent shoulder camera
        /// without changing any simulated aim or spawning a beam. Contact
        /// projection must reflect the real world strike, not the centered
        /// first-person HUD guess. Keep camera release identical across Hz.
        /// </summary>
        private static bool RunThirdPersonShotReticleCase()
        {
            Vector3 muzzle = new(0, 0, 0);
            Vector3 aim = new(0, 0, -20);
            bool ray = ThirdPersonReticleProjection.TryShotRay(
                muzzle, aim, out Vector3 direction);
            bool aligned = ray && (direction + Vector3.UnitZ).LengthSquared < 0.000001f;
            Vector3 far = ThirdPersonReticleProjection.Target(
                muzzle, direction, 1f);
            Vector3 closeWall = ThirdPersonReticleProjection.Target(
                muzzle, direction, 3f / ThirdPersonReticleProjection.TraceRange);

            Matrix4 native = Matrix4.LookAt(Vector3.Zero,
                -Vector3.UnitZ, Vector3.UnitY);
            Vector3 shoulderEye = new(0.5f, 0.35f, 2.4f);
            Matrix4 shoulder = Matrix4.LookAt(shoulderEye,
                shoulderEye - Vector3.UnitZ, Vector3.UnitY);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(78f), 16f / 9f, 0.1f, 200f);

            bool first = ThirdPersonReticleProjection.TryProject(
                far, native, projection, out Vector2 nativeReticle);
            bool third = ThirdPersonReticleProjection.TryProject(
                far, shoulder, projection, out Vector2 thirdReticle);
            bool contact = ThirdPersonReticleProjection.TryProject(
                closeWall, shoulder, projection, out Vector2 wallReticle);
            bool parallax = first && third && contact
                && MathF.Abs(nativeReticle.X - 0.5f) < 0.00001f
                && MathF.Abs(nativeReticle.Y - 0.5f) < 0.00001f
                && thirdReticle.X < nativeReticle.X
                && wallReticle.X < thirdReticle.X
                && ThirdPersonReticleProjection.Finite(closeWall)
                && (closeWall - new Vector3(0, 0, -3)).LengthSquared < 0.000001f;

            // A 240Hz four-picture ease must match the 60Hz one-picture ease
            // over the identical wall-clock interval.
            float sixty = ThirdPersonReticleProjection.ReleaseFraction(1d);
            float quarter = ThirdPersonReticleProjection.ReleaseFraction(0.25d);
            float compounded = 1f - MathF.Pow(1f - quarter, 4f);
            bool easing = MathF.Abs(sixty - 0.15f) < 0.000001f
                && MathF.Abs(sixty - compounded) < 0.000001f
                && ThirdPersonReticleProjection.ReleaseFraction(0d) == 0
                && ThirdPersonReticleProjection.ReleaseFraction(double.NaN) == 0;

            bool safe = !ThirdPersonReticleProjection.TryShotRay(
                    muzzle, muzzle, out _)
                && !ThirdPersonReticleProjection.TryShotRay(
                    muzzle, new Vector3(float.NaN, 0, 0), out _)
                && !ThirdPersonReticleProjection.TryProject(
                    shoulderEye, shoulder, projection, out _)
                && ThirdPersonReticleProjection.TryProject(
                    new Vector3(-1000f, 0, -3f), shoulder, projection,
                    out Vector2 clamped)
                && clamped.X >= ThirdPersonReticleProjection.ScreenInset
                && clamped.X <= 1 - ThirdPersonReticleProjection.ScreenInset;

            bool success = aligned && parallax && easing && safe;
            Console.WriteLine("FRAMETIMING " + (success ? "ok  " : "FAIL")
                + " third-person muzzle-ray crosshair & release-rate invariants");
            return success;
        }

        /// <summary>
        /// High-speed mouse/controller turns rotate the draw camera after the
        /// last 60Hz shot ray was sampled. The old world-space projected target
        /// went off-screen, briefly resurrecting native reticle history. This
        /// regression compares an uncapped 180-degree turn at several picture
        /// cadences against a stationary viewport. The canonical shot ray
        /// remains fixed and the predicted HUD-only ray remains on-screen.
        /// </summary>
        private static bool RunThirdPersonRapidTurnReticleCase()
        {
            Vector3 origin = Vector3.Zero;
            Vector3 shot = -Vector3.UnitZ;
            Matrix4 simView = Matrix4.LookAt(origin, origin + shot, Vector3.UnitY);
            Vector3 simLocalRay = Matrix.Vec3MultMtx3(shot, simView);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(78f), 16f / 9f, .1f, 200f);

            bool stable = true, oldInvalidAtFastYaw = false;
            Vector2 baseline = default;
            int checkedDraws = 0;
            foreach (int rate in new[] { 60, 120, 240, 540 })
            {
                foreach (float yaw in new[] { 0f, 20f, 45f, 75f, 90f, 135f, 179f, -90f })
                {
                    float angle = MathHelper.DegreesToRadians(yaw);
                    Vector3 forward = new(-MathF.Sin(angle), 0, -MathF.Cos(angle));
                    Vector3 right = new(MathF.Cos(angle), 0, -MathF.Sin(angle));
                    // Camera position changes only in draw time. The sim shot
                    // remains along -Z for this entire simulated 60Hz tick.
                    Vector3 shoulder = right * .5f
                        + Vector3.UnitY * .35f - forward * 2.4f;
                    Matrix4 drawView = Matrix4.LookAt(
                        shoulder, shoulder + forward, Vector3.UnitY);
                    Matrix4 drawInverseRotation =
                        Matrix4.Transpose(drawView.ClearTranslation());
                    bool full = ThirdPersonReticleProjection.TryProjectDrawRay(
                        origin, shot, ThirdPersonReticleProjection.TraceRange,
                        origin, origin, simView, drawInverseRotation, drawView,
                        projection, out Vector2 projected);
                    bool cached = ThirdPersonReticleProjection.TryProjectCameraRay(
                        origin, simLocalRay, ThirdPersonReticleProjection.TraceRange,
                        origin, origin, drawInverseRotation, drawView,
                        projection, out Vector2 cachedPos);
                    stable &= full && cached
                        && (projected - cachedPos).LengthSquared < .00000001f
                        && projected.X is > .05f and < .95f
                        && projected.Y is > .05f and < .95f;
                    if (yaw == 0) baseline = projected;
                    else stable &= (projected - baseline).LengthSquared < .00001f;
                    if (MathF.Abs(yaw) >= 90)
                    {
                        oldInvalidAtFastYaw |= !ThirdPersonReticleProjection.TryProject(
                            origin + shot * ThirdPersonReticleProjection.TraceRange,
                            drawView, projection, out _);
                    }
                    checkedDraws++;
                }
            }

            // A fast running Hunter uses the same interpolated native eye as
            // the local third-person body, not the stale 60Hz muzzle position.
            Vector3 nativeEye = new(4f, .3f, 1f);
            Vector3 shiftedShoulder = nativeEye + new Vector3(.5f, .35f, 2.4f);
            Matrix4 shiftedView = Matrix4.LookAt(
                shiftedShoulder, shiftedShoulder - Vector3.UnitZ, Vector3.UnitY);
            bool translate = ThirdPersonReticleProjection.TryProjectCameraRay(
                origin, simLocalRay, ThirdPersonReticleProjection.TraceRange,
                origin, nativeEye, Matrix4.Transpose(shiftedView.ClearTranslation()),
                shiftedView, projection, out Vector2 translated);
            stable &= translate && (translated - baseline).LengthSquared < .00001f;

            // When a wall is closer than the shoulder eye, its true impact
            // cannot be divided by clip W. The same camera ray at a useful
            // forward distance is a safe single-HUD-marker fallback.
            Vector3 eyeBehindContact = new(0f, 0f, -4f);
            Matrix4 behindView = Matrix4.LookAt(
                eyeBehindContact, eyeBehindContact - Vector3.UnitZ, Vector3.UnitY);
            bool closeWall = ThirdPersonReticleProjection.TryProjectCameraRay(
                origin, simLocalRay, .1f, origin, origin,
                Matrix4.Transpose(behindView.ClearTranslation()),
                behindView, projection, out Vector2 close);
            stable &= closeWall && close.X is >= 0f and <= 1f
                && close.Y is >= 0f and <= 1f;

            bool rejected = !ThirdPersonReticleProjection.TryProjectCameraRay(
                origin, new Vector3(float.NaN, 0f, 0f), 96f,
                origin, origin, Matrix4.Identity, simView, projection, out _)
                && !ThirdPersonReticleProjection.TryProjectCameraRay(
                origin, simLocalRay, float.NaN,
                origin, origin, Matrix4.Identity, simView, projection, out _);
            bool ok = stable && oldInvalidAtFastYaw && rejected;
            Console.WriteLine("FRAMETIMING " + (ok ? "ok  " : "FAIL")
                + " third-person single reticle high-turn 60/120/240/540Hz"
                + " (" + checkedDraws + " draw pose fixtures)");
            return ok;
        }

        /// <summary>
        /// Normal aim remains fully late-latched. Only an extreme pointer/touch
        /// turn is spread across the unsimulated fraction of the current tick,
        /// and it must converge to the full input at the tick boundary.
        /// </summary>
        private static bool RunFastLateAimCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            try
            {
                FrameTiming.FrameRateCap = 144;
                FrameTiming.Reset();
                Vector2 normal = PlayerEntity.ModBoundLateAim(new Vector2(4, 0), .25);
                Vector2 start = PlayerEntity.ModBoundLateAim(new Vector2(20, 0), 0);
                Vector2 middle = PlayerEntity.ModBoundLateAim(new Vector2(20, 0), .5);
                Vector2 end = PlayerEntity.ModBoundLateAim(new Vector2(20, 0), 1);

                FrameTiming.FrameRateCap = 60;
                Vector2 sixty = PlayerEntity.ModBoundLateAim(new Vector2(20, 0), 0);

                bool ok = MathF.Abs(normal.X - 4) < .00001f
                    && MathF.Abs(start.X - 8) < .00001f
                    && MathF.Abs(middle.X - 14) < .00001f
                    && MathF.Abs(end.X - 20) < .00001f
                    && MathF.Abs(sixty.X - 20) < .00001f;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} fast late aim"
                    + $" | normal {normal.X:0.00}"
                    + $" | extreme {start.X:0.00}/{middle.X:0.00}/{end.X:0.00}"
                    + $" | 60 Hz {sixty.X:0.00}");
                return ok;
            }
            finally
            {
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
            }
        }

        /// <summary>
        /// A two-second stall -- a room finishing loading, a window being
        /// dragged, a debugger -- must come back with one step, not a hundred
        /// and twenty crammed into one frame.
        /// </summary>
        private static bool RunStallCase()
        {
            FrameTiming.Reset();
            FrameTiming.ResetDiagnostics();
            int worst = 0;
            for (int i = 0; i < 600; i++)
            {
                worst = Math.Max(worst, FrameTiming.Advance(1 / 144.0));
            }
            int afterStall = FrameTiming.Advance(2.0);
            for (int i = 0; i < 600; i++)
            {
                worst = Math.Max(worst, FrameTiming.Advance(1 / 144.0));
            }
            bool ok = afterStall == 1 && worst <= 1 && FrameTiming.Stalls == 1;
            Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} 2 s stall"
                + $" | {afterStall} step(s) on the stalled frame"
                + $" | {FrameTiming.Stalls} stall(s) seen"
                + $" | worst ordinary frame {worst} step(s)");
            return ok;
        }

        private static int RunLockjawNoiseCases()
        {
            const ulong tick = 100;
            const int segments = 10;
            const int axes = 3;
            float[] firstRender = new float[segments * axes];
            uint rngBefore = Rng.Rng1;
            bool repeatedRenderMatches = true;
            bool tickChanges = false;
            bool targetChanges = false;
            bool sourceChanges = false;
            bool ownerChanges = false;
            bool inBounds = true;

            for (int render = 0; render < 4; render++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    for (int axis = 0; axis < axes; axis++)
                    {
                        float value = LockjawTrailNoise.Sample(tick, 0, 2, 0, segment, axis);
                        int index = segment * axes + axis;
                        if (render == 0)
                        {
                            firstRender[index] = value;
                        }
                        else if (value != firstRender[index])
                        {
                            repeatedRenderMatches = false;
                        }
                        tickChanges |= LockjawTrailNoise.Sample(tick + 1, 0, 2, 0, segment, axis) != value;
                        targetChanges |= LockjawTrailNoise.Sample(tick, 0, 2, 1, segment, axis) != value;
                        sourceChanges |= LockjawTrailNoise.Sample(tick, 0, 1, 0, segment, axis) != value;
                        ownerChanges |= LockjawTrailNoise.Sample(tick, 1, 2, 0, segment, axis) != value;
                    }
                }
            }

            for (ulong sampleTick = 0; sampleTick < 128; sampleTick++)
            {
                for (int owner = 0; owner < 2; owner++)
                {
                    for (int source = 1; source <= 2; source++)
                    {
                        for (int target = 0; target < source; target++)
                        {
                            for (int segment = 0; segment < segments; segment++)
                            {
                                for (int axis = 0; axis < axes; axis++)
                                {
                                    float value = LockjawTrailNoise.Sample(sampleTick, owner, source, target, segment, axis);
                                    inBounds &= value >= -0.25f && value < 0.25f;
                                }
                            }
                        }
                    }
                }
            }

            int failures = 0;
            failures += ReportNoiseCase("repeated samples at one tick", repeatedRenderMatches);
            failures += ReportNoiseCase("next tick changes sequence", tickChanges);
            failures += ReportNoiseCase("different target changes sequence", targetChanges);
            failures += ReportNoiseCase("different source changes sequence", sourceChanges);
            failures += ReportNoiseCase("different owner changes sequence", ownerChanges);
            failures += ReportNoiseCase("offsets stay in [-0.25, 0.25)", inBounds);
            failures += ReportNoiseCase("global Rng1 unchanged", Rng.Rng1 == rngBefore);
            return failures;
        }

        private static int ReportNoiseCase(string name, bool passed)
        {
            Console.WriteLine($"FRAMETIMING {(passed ? "ok  " : "FAIL")} Lockjaw {name}");
            return passed ? 0 : 1;
        }

    }
}
