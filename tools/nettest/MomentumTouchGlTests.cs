using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Desktop synthetic pointer delivery into the real local Samus/HUD/GL path.
// This does not exercise Android MotionEvent, a physical pen or touch hardware.
internal static class MomentumTouchGlTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, PlayerFlags1 Flags1,
        PlayerFlags2 Flags2, NativeTouchReport Touch, int Charge);
    private sealed record Capture(List<Tick> Ticks, bool TouchObserved, bool ContinuedObserved,
        bool Moved, bool GestureBoost, bool ManualCharge, bool ManualBoost, long SolverSteps, int Pictures, List<object> Diagnostics);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }

    internal static int Run(string? outputPath)
    {
        bool oldClassic = InputSettings.ClassicMorphBallTouch, oldStylus = PointerInput.StylusMode;
        bool oldPenBoost = InputSettings.StylusMovementBoost, oldMouseBoost = InputSettings.MouseMovementBoost;
        bool oldZone = StylusZone.Wanted;
        float oldAspect = StylusZone.AspectCorrection;
        float oldLeft = StylusZone.Left, oldTop = StylusZone.Top, oldWidth = StylusZone.Width;
        var summaries = new List<object>();
        var failures = new List<string>();
        try
        {
            _checks = 0;
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive, "ordinary offline GL process, no authority impersonation");
            Check(ServerSim.Available(out string reason), reason);
            InputSettings.ClassicMorphBallTouch = true; PointerInput.StylusMode = true;
            var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(640, 480);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            foreach (string scenario in new[] { "pen-enabled", "touch-enabled", "pen-disabled-manual", "mouse-enabled", "mouse-disabled-manual", "bound-roll-manual" })
            {
                Capture? native = null, momentum = null;
                string? failure = null;
                var mismatches = new List<int>();
                try
                {
                    native = Trace(window, MovementModel.Native, scenario);
                    momentum = Trace(window, MovementModel.Momentum, scenario);
                    Check(native.Ticks.Count == momentum.Ticks.Count, scenario + " equal trace lengths");
                    for (int i = 0; i < native.Ticks.Count; i++)
                        if (native.Ticks[i] != momentum.Ticks[i]) mismatches.Add(i);
                    foreach (int i in mismatches.Take(8))
                    {
                        Tick n = native.Ticks[i], m = momentum.Ticks[i];
                        Console.Error.WriteLine($"TOUCH PARITY {scenario} tick={i}: Native={n}; Momentum={m}; deltaPosition={m.Position - n.Position}; deltaSpeed={m.Speed - n.Speed}");
                    }
                    Check(mismatches.Count == 0, $"{scenario} exact native alternate-form touch/boost parity; mismatches={mismatches.Count}, first={(mismatches.Count > 0 ? mismatches[0] : -1)}");
                    Check(native.Moved && momentum.Moved, scenario + " actual roll displacement");
                    Check(native.SolverSteps == 0 && momentum.SolverSteps == 0, scenario + " native form retains velocity ownership");
                    if (scenario != "bound-roll-manual")
                        Check(native.TouchObserved && momentum.TouchObserved, scenario + " real local native touch sample consumed");
                    if (scenario.EndsWith("enabled", StringComparison.Ordinal))
                        Check(native.ContinuedObserved && momentum.ContinuedObserved && native.GestureBoost && momentum.GestureBoost,
                            scenario + " permitted gesture reaches native Boosting");
                    else
                        Check(!native.GestureBoost && !momentum.GestureBoost
                            && native.ManualCharge && momentum.ManualCharge && native.ManualBoost && momentum.ManualBoost,
                            scenario + " disabled gesture preserves roll and manual charge/release");
                    Console.WriteLine("MOMENTUM TOUCH GL PASS " + scenario);
                }
                catch (Exception error)
                {
                    failure = error.ToString(); failures.Add(scenario + ": " + error.Message);
                    Console.Error.WriteLine("MOMENTUM TOUCH GL CASE FAIL " + scenario + ": " + error);
                }
                summaries.Add(new { scenario, failure, mismatchCount = mismatches.Count,
                    mismatches = mismatches.Take(8).Select(i => new { tick = i,
                        native = Sample(native!.Ticks[i]), momentum = Sample(momentum!.Ticks[i]) }),
                    native = Summary(native), momentum = Summary(momentum) });
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, failures,
                    scope = "desktop synthetic PointerDevice pen/touch and relative mouse samples plus keyboard roll/boost, real local Samus and native HUD/GL; paired Native/Momentum alternate forms; no physical device or Android MotionEvent acceptance", summaries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM TOUCH GL {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed scenarios");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM TOUCH GL FAIL " + error); return 1; }
        finally
        {
            PointerDevice.Reset(); PointerInput.StylusMode = oldStylus;
            InputSettings.ClassicMorphBallTouch = oldClassic;
            InputSettings.StylusMovementBoost = oldPenBoost; InputSettings.MouseMovementBoost = oldMouseBoost;
            StylusZone.AspectCorrection = oldAspect;
            StylusZone.SetRect(oldLeft, oldTop, oldWidth); StylusZone.Enabled = oldZone;
        }
    }

    private static object Sample(Tick tick) => new { position = new[] { tick.Position.X, tick.Position.Y, tick.Position.Z },
        speed = new[] { tick.Speed.X, tick.Speed.Y, tick.Speed.Z }, flags1 = tick.Flags1.ToString(),
        flags2 = tick.Flags2.ToString(), tick.Touch, tick.Charge };
    private static object? Summary(Capture? capture) => capture == null ? null : new {
        ticks = capture.Ticks.Count, capture.TouchObserved, capture.ContinuedObserved, capture.Moved,
        capture.GestureBoost, capture.ManualCharge, capture.ManualBoost, capture.SolverSteps, capture.Pictures,
        trace = capture.Diagnostics };

    private static Capture Trace(NativeWindow window, MovementModel model, string scenario)
    {
        PointerDevice.Reset();
        // Establish the client aspect before SetRect clamps its position and
        // before deriving absolute coordinates. The first scene otherwise used
        // the saved 16:9 aspect, whereas later traces inherited 4:3 from Update.
        PointerDevice.Update(new PointerSample(PointerDeviceType.Unknown, 0, 0, 0,
            false, false, true), 640, 480);
        bool bound = scenario == "bound-roll-manual", mouse = scenario.StartsWith("mouse", StringComparison.Ordinal);
        bool enabled = scenario.EndsWith("enabled", StringComparison.Ordinal);
        InputSettings.StylusMovementBoost = enabled; InputSettings.MouseMovementBoost = enabled;
        StylusZone.Enabled = !mouse && !bound; StylusZone.SetRect(.1f, .1f, .7f);
        var keyboard = SyntheticInput.CreateKeyboard();
        var key = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
            .CreateDelegate<Action<KeyboardState, Keys, bool>>();
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        Scene? scene = null; Action? restore = null; int pictures = 0;
        try
        {
            scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            scene.AddPlayer(Hunter.Samus); scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 1);
            new MatchDefinition { Mode = GameMode.Battle, MovementModel = model,
                TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad(); scene.OnResize();
            var player = scene.Players.Main;
            var bindings = player.Controls.All.Select(c => (Control: c, c.Type, c.Key, c.MouseButton)).ToArray();
            bool aim = player.Controls.MouseAim, keyboardAim = player.Controls.KeyboardAim;
            restore = () => { foreach (var b in bindings) { b.Control.Type = b.Type; b.Control.Key = b.Key; b.Control.MouseButton = b.MouseButton; }
                player.Controls.ClearAll(); player.Controls.MouseAim = aim; player.Controls.KeyboardAim = keyboardAim; };
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.Boost.Key = Keys.B; player.Controls.RollUp.Key = Keys.W;
            player.Controls.MouseAim = mouse; player.Controls.KeyboardAim = false;
            // Scene/control initialization may lazily load saved input options.
            // Declare the synthetic device policy after that legitimate load,
            // rather than letting only the first trace inherit saved settings.
            InputSettings.ClassicMorphBallTouch = true; PointerInput.StylusMode = true;
            InputSettings.StylusMovementBoost = enabled; InputSettings.MouseMovementBoost = enabled;
            PointerDevice.Reset();
            PointerDevice.Update(new PointerSample(PointerDeviceType.Unknown, 0, 0, 0,
                false, false, true), 640, 480);
            StylusZone.Enabled = !mouse && !bound; StylusZone.SetRect(.1f, .1f, .7f);
            void Step()
            {
                scene.OnSimulationFrame();
                DesktopGraphicsSession.Resize(window); scene.OnDrawFrame();
                Check(scene.OnRenderFrame(), "ordinary scene HUD/world render remains active");
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame(); pictures++;
                Check(InputSettings.ClassicMorphBallTouch && PointerInput.StylusMode
                    && InputSettings.StylusMovementBoost == enabled && InputSettings.MouseMovementBoost == enabled
                    && StylusZone.Wanted == (!mouse && !bound),
                    "effective device and boost policy remains the declared fixture configuration");
            }
            Check(scene.CameraMode == CameraMode.Player && player.IsMainPlayer && !player.IsBot,
                "real main-player camera and local Samus ownership");
            key(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Step();
            key(keyboard, Keys.Enter, false);
            Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "ordinary native shoot-to-spawn");
            scene.CameraSequences.Current?.End();
            for (int i = 0; i < 120; i++) Step();
            Check(player.Health > 0 && player.ModUsesClassicTouch, "actual local Classic touch owner alive");
            player.ModStartFormSwitch();
            for (int i = 0; i < 180 && player.IsMorphing; i++) Step();
            Check(player.IsAltForm && !player.IsMorphing, "real native Samus morph animation completes");
            for (int i = 0; i < 20; i++) Step();
            long before = scene.DeveloperMomentumSteps;
            var ticks = new List<Tick>(); var diagnostics = new List<object>(); Vector3 start = player.Position;
            bool observed = false, continued = false, moved = false, gesture = false, charge = false, manual = false;
            var chargeField = typeof(PlayerEntity).GetField("_boostCharge", Hidden)!;
            for (int tick = 0; tick < 160; tick++)
            {
                bool contact = !bound && tick < 24;
                float dsX = 128, dsY = tick < 4 ? 150 : 20;
                float x = mouse ? dsX / 255 * 640 : (StylusZone.Left + dsX / 255 * StylusZone.Width) * 640;
                float y = mouse ? dsY / 191 * 480 : (StylusZone.Top + dsY / 191 * StylusZone.Height) * 480;
                if (mouse)
                {
                    // Native relative sampling drops the movement that begins
                    // contact. Supply a continued physical-mouse drag as well,
                    // spanning multiple 30 Hz samples, rather than one jump.
                    x = tick < 4 ? 80 : tick < 6 ? 160 : 600;
                    y = 200;
                }
                var device = mouse ? PointerDeviceType.Mouse : scenario.StartsWith("touch", StringComparison.Ordinal)
                    ? PointerDeviceType.Touch : PointerDeviceType.Pen;
                PointerDevice.Update(new PointerSample(device, 1, x, y, contact, contact, true), 640, 480);
                key(keyboard, Keys.W, bound && tick < 100);
                key(keyboard, Keys.B, !enabled && tick is >= 40 and < 120);
                Step();
                Check(player.Health > 0 && player.IsAltForm && player.IsMainPlayer, "real local native form remains active");
                NativeTouchReport report = player.ModNativeTouchReport();
                if (!enabled && !bound)
                    Check(!report.Continued, "disabled device gesture permission is suppressed in the native report");
                int currentCharge = Convert.ToInt32(chargeField.GetValue(player));
                ticks.Add(new(player.Position, player.Speed, player.Flags1, player.Flags2, report, currentCharge));
                if (tick < 24 || tick % 20 == 0 || tick >= 118 && tick <= 122)
                    diagnostics.Add(new { tick, frame = scene.FrameCount, elapsed = scene.ElapsedTime,
                        rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2, sample = Sample(ticks[^1]),
                        facing = new[] { player.FacingVector.X, player.FacingVector.Y, player.FacingVector.Z },
                        rollBasis = new[] { "_altRollFbX", "_altRollFbZ", "_altRollLrX", "_altRollLrZ" }
                            .Select(name => Convert.ToSingle(typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player))).ToArray(),
                        pointer = new { device = PointerDevice.Current.Device.ToString(), PointerDevice.Current.X,
                            PointerDevice.Current.Y, PointerDevice.Current.InContact, StylusZone.Aiming,
                            StylusZone.Left, StylusZone.Top, StylusZone.Width, StylusZone.Height,
                            StylusZone.AspectCorrection, InputSettings.ClassicMorphBallTouch,
                            InputSettings.StylusMovementBoost, InputSettings.MouseMovementBoost } });
                observed |= report.Down && (report.Delta4X != 0 || report.Delta4Y != 0);
                continued |= report.Continued;
                moved |= (player.Position - start).LengthSquared > .001f;
                gesture |= tick < 40 && player.Flags1.TestFlag(PlayerFlags1.Boosting);
                charge |= tick is >= 40 and < 120 && currentCharge > 0;
                manual |= tick >= 120 && player.Flags1.TestFlag(PlayerFlags1.Boosting);
            }
            key(keyboard, Keys.W, false); key(keyboard, Keys.B, false);
            return new(ticks, observed, continued, moved, gesture, charge, manual,
                scene.DeveloperMomentumSteps - before, pictures, diagnostics);
        }
        finally
        {
            PointerDevice.Reset(); restore?.Invoke();
            if (scene != null) MomentumSceneTests.Release(scene);
        }
    }
}
