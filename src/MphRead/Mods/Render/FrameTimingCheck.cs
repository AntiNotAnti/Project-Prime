using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
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
            failures += RunHostCadenceCase() ? 0 : 1;
            failures += RunLiveReticleCadenceCase() ? 0 : 1;
            failures += RunDynamicCrosshairCameraPolicyCase() ? 0 : 1;
            failures += RunFirstPersonPresentationCase() ? 0 : 1;
            failures += HitReactionPresentationCheck.Run() ? 0 : 1;
            failures += RunFixedCameraTranslationCase() ? 0 : 1;
            failures += RunAngularCameraPresentationCase() ? 0 : 1;
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
                // After reset, existing completed poses stay current until
                // a new step supplies an interpolation interval.
                FrameTiming.Advance(FrameTiming.StepSeconds);
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
        /// Exercise the actual retained camera sampler with host clocks and
        /// achieved frame rates changing independently. Configuration/reset
        /// must not redisplay an older pose, and diagnostics cannot select it.
        /// </summary>
        private static bool RunHostCadenceCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            double priorCadence = FrameTiming.PresentationCadenceHz;
            try
            {
                FrameTiming.FrameRateCap = FrameTiming.DisplayRate;
                FrameTiming.SetPresentationCadence(120, presentationPaced: true);
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
                camera.Position = Vector3.UnitX;
                camera.Target = camera.Position - Vector3.UnitZ;
                camera.ModCaptureDrawState();
                float last = 1;
                bool monotonic = true, bounded = true, immutable = true;
                void Picture(double seconds, bool moving = true)
                {
                    int steps = FrameTiming.Advance(seconds);
                    for (int i = 0; i < steps; i++)
                    {
                        if (moving) camera.Position += new Vector3(.1f, 0, 0);
                        camera.Target = camera.Position - Vector3.UnitZ;
                        camera.ModCaptureDrawState();
                    }
                    Vector3 before = camera.Position;
                    Vector3 drawn = camera.ModGetDrawPosition(FrameTiming.PresentationAlpha);
                    monotonic &= drawn.X >= last - .00001f;
                    bounded &= drawn.X <= before.X + .00001f
                        && FrameTiming.PresentationAlpha is >= 0 and <= 1;
                    immutable &= camera.Position == before;
                    last = drawn.X;
                }
                Picture(1 / 240d);
                bool startup = FrameTiming.HighRefreshPresentation
                    && FrameTiming.PresentationAlpha == 1 && last == 1;

                // The old >75 Hz/two-second rule switched pose timelines in
                // these exact windows. Both are pictures on the same 120 Hz host.
                bool stable = true, fractional = false;
                foreach (double achieved in new[] { 76d, 74d, 76d, 74d })
                {
                    for (int i = 0; i < (int)(achieved * 3); i++)
                    {
                        Picture(1 / achieved);
                        stable &= FrameTiming.HighRefreshPresentation;
                        fractional |= FrameTiming.PresentationAlpha < .99;
                    }
                }
                double beforeDiagnosticsReset = FrameTiming.PresentationAlpha;
                FrameTiming.ResetDiagnostics();
                bool diagnosticsIndependent = FrameTiming.HighRefreshPresentation
                    && FrameTiming.PresentationAlpha == beforeDiagnosticsReset;
                FrameTiming.SetPresentationCadence(double.NaN, presentationPaced: true);
                FrameTiming.SetPresentationCadence(0, presentationPaced: true);
                bool unknownProbe = FrameTiming.PresentationCadenceHz == 120;

                // A genuine display change may move to current state. Enabling
                // the delayed timeline again must not move the camera backward.
                FrameTiming.SetPresentationCadence(60, presentationPaced: true);
                Picture(0);
                bool sixty = !FrameTiming.HighRefreshPresentation
                    && FrameTiming.PresentationAlpha == 1 && last == camera.Position.X;
                FrameTiming.SetPresentationCadence(120, presentationPaced: true);
                Picture(0);
                bool transition = FrameTiming.HighRefreshPresentation
                    && FrameTiming.PresentationAlpha == 1;
                for (int i = 0; i < 12; i++) Picture(1 / 240d);
                transition &= FrameTiming.PresentationAlpha < 1;

                // Loading/lifecycle reset must not re-use the pre-reset previous
                // history on draw-only frames. Resume smooths only new motion.
                FrameTiming.Reset();
                last = camera.Position.X;
                Picture(1 / 240d);
                bool resetCurrent = FrameTiming.PresentationAlpha == 1
                    && last == camera.Position.X && FrameTiming.HighRefreshPresentation;
                for (int i = 0; i < 12; i++) Picture(1 / 240d);
                Picture(2); // bounded stall recovery: exactly one completed step
                bool stall = FrameTiming.StepsThisFrame == 1;
                Picture(1 / 240d);
                FrameTiming.Reset();
                last = camera.Position.X;
                for (int i = 0; i < 12; i++) Picture(1 / 120d, moving: false);
                bool paused = last == camera.Position.X && FrameTiming.HighRefreshPresentation;
                FrameTiming.Reset();
                Picture(1 / 240d);
                bool resumed = last == camera.Position.X && FrameTiming.PresentationAlpha == 1;

                // A numeric 60 software clock remains current on a fast panel.
                // Desktop's FIFO-only fallback relinquishes its numeric clock
                // and really draws at 120. Hosts retaining a numeric deadline
                // instead declare the slower effective submission clock.
                FrameTiming.FrameRateCap = 60;
                FrameTiming.SetPresentationCadence(120, presentationPaced: false);
                Picture(0);
                bool software = FrameTiming.PresentationCadenceHz == 60
                    && !FrameTiming.HighRefreshPresentation;
                FrameTiming.ResetDiagnostics();
                for (int i = 0; i < 228; i++)
                {
                    Picture(1 / 76d);
                    software &= !FrameTiming.HighRefreshPresentation;
                }
                software &= FrameTiming.MeasuredFrameHz > 75 && FrameTiming.PresentationAlpha == 1;
                FrameTiming.SetPresentationCadence(75, presentationPaced: true);
                Picture(0);
                bool native75 = FrameTiming.HighRefreshPresentation;
                FrameTiming.SetPresentationCadence(120, presentationPaced: true);
                Picture(0);
                bool fifo = FrameTiming.PresentationCadenceHz == 120
                    && FrameTiming.HighRefreshPresentation && FrameTiming.PresentationAlpha == 1;
                FrameTiming.SetPresentationCadence(120, presentationPaced: true, numericBudget: true);
                Picture(0);
                bool fifoBudget = FrameTiming.PresentationCadenceHz == 60
                    && !FrameTiming.HighRefreshPresentation && FrameTiming.PresentationAlpha == 1;
                FrameTiming.SetPresentationCadence(double.NaN, presentationPaced: true, numericBudget: true);
                fifoBudget &= FrameTiming.PresentationCadenceHz == 60;
                FrameTiming.FrameRateCap = 120;
                FrameTiming.SetPresentationCadence(0, presentationPaced: true, numericBudget: true);
                Picture(0);
                bool cachedDisplay = FrameTiming.PresentationCadenceHz == 120
                    && FrameTiming.HighRefreshPresentation && FrameTiming.PresentationAlpha == 1;
                FrameTiming.FrameRateCap = 144;
                FrameTiming.SetPresentationCadence(0, presentationPaced: true, numericBudget: true);
                cachedDisplay &= FrameTiming.PresentationCadenceHz == 120;
                FrameTiming.FrameRateCap = FrameTiming.DisplayRate;
                FrameTiming.SetPresentationCadence(0, presentationPaced: true, numericBudget: true);
                cachedDisplay &= FrameTiming.PresentationCadenceHz == 120;
                FrameTiming.FrameRateCap = 120;
                FrameTiming.SetPresentationCadence(60, presentationPaced: true, numericBudget: true);
                Picture(0);
                fifoBudget &= FrameTiming.PresentationCadenceHz == 60
                    && !FrameTiming.HighRefreshPresentation && FrameTiming.PresentationAlpha == 1;
                bool ok = startup && stable && fractional && diagnosticsIndependent
                    && unknownProbe && sixty && transition && resetCurrent && stall
                    && paused && resumed && software && native75 && fifo && fifoBudget && cachedDisplay
                    && monotonic && bounded && immutable;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} host cadence and retained pose continuity"
                    + $" | startup={startup} windows={stable}/{fractional} diagnostics={diagnosticsIndependent}"
                    + $" | display={sixty}/{transition} reset={resetCurrent} stall={stall} pause={paused}/{resumed}"
                    + $" | software/native75/FIFO={software}/{native75}/{fifo} budget/cache={fifoBudget}/{cachedDisplay}"
                    + $" monotonic={monotonic} bounded={bounded} immutable={immutable}");
                return ok;
            }
            finally
            {
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.SetPresentationCadence(priorCadence, presentationPaced: priorCadence > 0);
                FrameTiming.Reset();
                FrameTiming.ResetDiagnostics();
            }
        }

        /// <summary>
        /// The real live reticle sampler must use the same continuity phase as
        /// the retained camera, including draw-only pictures after a reset.
        /// No HUD setup, native context or game assets are needed to sample it.
        /// </summary>
        private static bool RunLiveReticleCadenceCase()
        {
            int priorCap = FrameTiming.FrameRateCap;
            double priorCadence = FrameTiming.PresentationCadenceHz;
            bool priorPro = Features.ProHud, priorWeapon = Features.ProHudFixedWeapon;
            var priorGame = GameState.Current;
            var priorPlayers = PlayerEntity.LegacyRegistry;
            var priorRandom = Rng.Current;
            try
            {
                Features.ProHud = true;
                Features.ProHudFixedWeapon = false;
                var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
                    SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
                var player = scene.Players.Main;
                void Set(string name, object value) => typeof(PlayerEntity)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
                Set("_reticlePresentationValid", true);
                Set("_reticleHistoryFixedWeapon", false);
                Set("_reticleOlderPosition", new Vector2(.4f, .5f));
                Set("_reticlePreviousPosition", new Vector2(.4f, .5f));
                Set("_reticleCurrentPosition", new Vector2(.6f, .5f));
                MethodInfo sample = typeof(PlayerEntity).GetMethod("GetReticlePresentationPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var camera = new CameraInfo { Position = new Vector3(.4f, 0, 0), Target = -Vector3.UnitZ };
                camera.ModResetDrawState();
                camera.Position = new Vector3(.6f, 0, 0);
                camera.ModCaptureDrawState();
                bool SamePhase() => MathF.Abs(((Vector2)sample.Invoke(player, null)!).X
                    - camera.ModGetDrawPosition(FrameTiming.PresentationAlpha).X) < .00001f;
                FrameTiming.FrameRateCap = FrameTiming.DisplayRate;
                FrameTiming.SetPresentationCadence(120, presentationPaced: true);
                FrameTiming.Reset();
                FrameTiming.Advance(FrameTiming.StepSeconds * .4);
                bool startup = FrameTiming.Alpha < 1 && FrameTiming.PresentationAlpha == 1 && SamePhase();
                FrameTiming.Advance(FrameTiming.StepSeconds);
                bool fractional = FrameTiming.PresentationAlpha < 1 && SamePhase();
                FrameTiming.SetPresentationCadence(60, presentationPaced: true);
                FrameTiming.Advance(0);
                FrameTiming.SetPresentationCadence(120, presentationPaced: true);
                FrameTiming.Advance(0);
                bool transition = FrameTiming.PresentationAlpha == 1 && SamePhase();
                FrameTiming.Reset();
                FrameTiming.Advance(FrameTiming.StepSeconds * .4);
                bool resume = FrameTiming.PresentationAlpha == 1 && SamePhase();
                bool ok = startup && fractional && transition && resume;
                Console.WriteLine($"FRAMETIMING {(ok ? "ok  " : "FAIL")} live reticle/camera cadence"
                    + $" | startup={startup} fractional={fractional} transition={transition} resume={resume}");
                return ok;
            }
            finally
            {
                Features.ProHud = priorPro;
                Features.ProHudFixedWeapon = priorWeapon;
                GameState.Current = priorGame;
                PlayerEntity.LegacyRegistry = priorPlayers;
                Rng.Current = priorRandom;
                FrameTiming.FrameRateCap = priorCap;
                FrameTiming.SetPresentationCadence(priorCadence, presentationPaced: priorCadence > 0);
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
