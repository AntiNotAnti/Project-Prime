using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Droid;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using GamepadState = MphRead.Mods.Input.GamepadState;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Synthetic provider acceptance through Scene's production control pipeline.
// Physical devices and Android's platform view are separate release acceptance.
internal static class MomentumInputSceneTests
{
    private const string Device = "fixture-momentum-controller";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, Vector2 Axes);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float a, float b, string label)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) < .00001f, $"{label}: {a} != {b}");
    private static void Publish(float x = 0, float y = 0, GamepadButtons buttons = 0)
        => GamepadManager.UpdateDevice(Device, new GamepadState { LeftX = x, LeftY = y, Buttons = buttons, Name = "Synthetic acceptance fixture" }, true,
            GamepadFamily.Generic, GamepadCapabilities.AnalogLeftStick | GamepadCapabilities.AnalogRightStick);
    private static void ResetSources()
    {
        GamepadManager.ClearAll(); GamepadContexts.Focused = true; GamepadContexts.MenuVisible = false;
        GamepadContexts.Capturing = false; GamepadContexts.TextEntryActive = false;
        PointerDevice.Reset();
    }
    internal static int Run(string? outputPath)
    {
        bool stylus = PointerInput.StylusMode, zone = StylusZone.Enabled;
        float left = StylusZone.Left, top = StylusZone.Top, width = StylusZone.Width;
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline live input fixture");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            var summaries = new List<object>();
            foreach (Hunter hunter in Hunters)
            {
                foreach (bool diagonal in new[] { false, true })
                {
                    Tick[] keyboard = Movement(hunter, false, diagonal ? 1 : 0, 1);
                    Tick[] controller = Movement(hunter, true, diagonal ? 1 : 0, 1);
                    for (int i = 0; i < keyboard.Length; i++)
                    {
                        Near(keyboard[i].Position.X, controller[i].Position.X, "digital/controller X");
                        Near(keyboard[i].Position.Y, controller[i].Position.Y, "digital/controller Y");
                        Near(keyboard[i].Position.Z, controller[i].Position.Z, "digital/controller Z");
                        Near(keyboard[i].Speed.X, controller[i].Speed.X, "digital/controller velocity X");
                        Near(keyboard[i].Speed.Z, controller[i].Speed.Z, "digital/controller velocity Z");
                    }
                    summaries.Add(new { hunter = hunter.ToString(), scenario = diagonal ? "full-diagonal-keyboard-controller" : "full-forward-keyboard-controller", ticks = keyboard.Length });
                }
                foreach (float magnitude in new[] { .25f, .5f, .75f })
                {
                    Tick[] ticks = Movement(hunter, true, 0, magnitude);
                    Near(ticks[0].Axes.Y, magnitude, "partial stick reaches solver unchanged");
                    summaries.Add(new { hunter = hunter.ToString(), scenario = "partial-forward", magnitude, ticks = ticks.Length,
                        displacement = (ticks[^1].Position - ticks[0].Position).Length, finalSpeed = ticks[^1].Speed.Length });
                }
                MixedSources(hunter);
                TouchStick(hunter);
                Lifecycle(hunter);
                StylusForms(hunter);
                Console.WriteLine("MOMENTUM INPUT PASS " + hunter);
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "Actual offline Scene fixed steps with synthetic KeyboardState, GamepadManager provider and desktop PointerDevice/StylusZone; no physical controller/touchscreen, Android view, GL or network acceptance",
                    conditions = "Explicit frozen Momentum match rule and diagnostics-only sampling; headless Roam/All with native intro skip; no owner or acceptance flags fabricated; fresh scenes per input trajectory",
                    exclusions = "Native Samus absolute-touch roll needs Player camera/HUD and GL; Android MotionEvent/view integration and physical acceptance deferred",
                    summaries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM INPUT PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM INPUT FAIL " + ex); return 1; }
        finally { ResetSources(); PointerInput.StylusMode = stylus; StylusZone.Enabled = zone; StylusZone.SetRect(left, top, width); }
    }
    private static Tick[] Movement(Hunter hunter, bool controller, float x, float y)
    {
        ResetSources();
        Scene scene = MomentumSceneTests.Load(hunter, true, out var keyboard, matchRule: true);
        try
        {
            if (controller)
            {
                Publish(); GamepadManager.SelectDevice(Device);
                // Isolated synthetic device runtime: explicitly neutral calibration
                // and zero deadzones make requested post-deadzone magnitude exact.
                GamepadManager.ReplaceRuntime(new GamepadRuntimeConfig());
                GamepadOptions.LeftInner = GamepadOptions.LeftOuter = 0;
            }
            for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
            var p = scene.Players.Main;
            Vector3 origin = p.Position; long before = scene.DeveloperMomentumSteps;
            var ticks = new Tick[24];
            for (int i = 0; i < ticks.Length; i++)
            {
                if (controller) Publish(x, y);
                else { Key(keyboard, Keys.W, y > 0); Key(keyboard, Keys.D, x > 0); }
                long count = scene.DeveloperMomentumSteps; scene.OnSimulationFrame();
                Check(scene.DeveloperMomentumSteps == count + 1, "one eligible solver step through production input");
                var sample = scene.LastMomentumStep!.Value;
                Vector2 axes = sample.Input.MoveAxes;
                if (controller)
                {
                    var state = GamepadManager.Snapshot.State;
                    var expected = GamepadAnalog.ApplyRadialDeadZone(state.LeftX, state.LeftY, 0, 0);
                    Near(axes.X, expected.X, "controller X through actual control resolver");
                    Near(axes.Y, expected.Y, "controller Y through actual control resolver");
                    Check(axes.Length <= 1.000001f, "controller radial diagonal never exceeds one");
                }
                Check(float.IsFinite(p.Position.X) && float.IsFinite(p.Speed.Y), "finite native body");
                ticks[i] = new(p.Position, p.Speed, axes);
            }
            Check(scene.DeveloperMomentumSteps > before && (p.Position - origin).Length > .01f, "synthetic input produces actual body movement");
            if (controller) { GamepadManager.RemoveDevice(Device); scene.OnSimulationFrame(); Check(!p.Controls.AnalogMoveActive, "disconnect clears analog magnitude"); }
            else { Key(keyboard, Keys.W, false); Key(keyboard, Keys.D, false); scene.OnSimulationFrame(); }
            Check(!p.Controls.MoveUp.IsDown && !p.Controls.MoveRight.IsDown, "released source leaves no ghost directions");
            return ticks;
        }
        finally { MomentumSceneTests.Release(scene); ResetSources(); }
    }
    private static void MixedSources(Hunter hunter)
    {
        ResetSources(); Scene scene = MomentumSceneTests.Load(hunter, true, out var keyboard, matchRule: true);
        try
        {
            var p = scene.Players.Main; Publish(); GamepadManager.SelectDevice(Device);
            GamepadManager.ReplaceRuntime(new GamepadRuntimeConfig());
            GamepadOptions.LeftInner = GamepadOptions.LeftOuter = 0;
            for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
            Vector3 start = p.Position;
            void Frame(float x, float y, bool keyboardRight, float expectedX, float expectedY)
            {
                Key(keyboard, Keys.D, keyboardRight); Publish(x, y);
                long count = scene.DeveloperMomentumSteps; scene.OnSimulationFrame();
                Check(scene.DeveloperMomentumSteps == count + 1, "mixed sources actual solver execution");
                var axes = scene.LastMomentumStep!.Value.Input.MoveAxes;
                Near(axes.X, expectedX, "mixed source solver strafe"); Near(axes.Y, expectedY, "mixed source solver forward");
                Check(p.Controls.MoveRight.IsDown == (keyboardRight || x > 0)
                    && p.Controls.MoveLeft.IsDown == (x < 0), "mixed source canonical directions retained");
            }
            for (int i = 0; i < 6; i++) Frame(.25f, .75f, false, .25f, .75f);
            for (int i = 0; i < 6; i++) Frame(-.25f, .75f, true, 1, .75f);
            Frame(-.25f, .75f, false, -.25f, .75f);
            Frame(.25f, -.75f, false, .25f, -.75f);
            Frame(0, 0, false, 0, 0);
            Check(!p.Controls.AnalogMoveActive && !p.Controls.MoveUp.IsDown && !p.Controls.MoveDown.IsDown,
                "mixed source release clears prior analog override and reversal direction");
            Check((p.Position - start).Length > .01f, "mixed asymmetric input drives native collision body");
        }
        finally { MomentumSceneTests.Release(scene); ResetSources(); }
    }
    private static void TouchStick(Hunter hunter)
    {
        ResetSources(); Scene scene = MomentumSceneTests.Load(hunter, true, out var keyboard, matchRule: true);
        try
        {
            var p = scene.Players.Main;
            var touch = new TouchControls(); touch.Layout(1000, 600, 1);
            // Production TouchControls is linked, not copied. The platform boundary
            // below mirrors GameView.cs direction -> synthetic keyboard adapter.
            // No Android MotionEvent or real screen is claimed by this CPU fixture.
            void Apply()
            {
                var dir = touch.Direction;
                Key(keyboard, Keys.W, (dir & TouchControls.Dir.Up) != 0);
                Key(keyboard, Keys.S, (dir & TouchControls.Dir.Down) != 0);
                Key(keyboard, Keys.A, (dir & TouchControls.Dir.Left) != 0);
                Key(keyboard, Keys.D, (dir & TouchControls.Dir.Right) != 0);
            }
            foreach (var axis in new[] { new Vector2(0, -1), new Vector2(1, -1), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1), new Vector2(-1, 1), new Vector2(-1, 0), new Vector2(-1, -1) })
            {
                touch.PointerDown(19, 100, 300);
                touch.PointerMove(19, 100 + axis.X * touch.StickRadius * .8f, 300 + axis.Y * touch.StickRadius * .8f, 100, 100);
                Apply(); scene.OnSimulationFrame();
                Check(p.Controls.MoveUp.IsDown == (axis.Y < 0) && p.Controls.MoveDown.IsDown == (axis.Y > 0)
                    && p.Controls.MoveRight.IsDown == (axis.X > 0) && p.Controls.MoveLeft.IsDown == (axis.X < 0),
                    "production touch model quantizes all eight directions through actual scene binds");
                var axes = scene.LastMomentumStep!.Value.Input.MoveAxes;
                Near(axes.X, axis.X, "touch digital solver X"); Near(axes.Y, -axis.Y, "touch digital solver Y");
                touch.PointerUp(19); Apply(); scene.OnSimulationFrame();
                Check(!p.Controls.MoveUp.IsDown && !p.Controls.MoveDown.IsDown && !p.Controls.MoveRight.IsDown && !p.Controls.MoveLeft.IsDown,
                    "touch release has no ghost directions");
            }
            touch.PointerDown(19, 100, 300); touch.PointerMove(19, 101, 300, 200, 200); Apply(); scene.OnSimulationFrame();
            Check(touch.Direction == TouchControls.Dir.None, "production touch deadzone remains digital neutral");
            touch.ReleaseEverything(); Apply(); scene.OnSimulationFrame();
            touch.PointerDown(19, 100, 300); touch.PointerMove(19, 100, 240, 300, 300); Apply();
            GamepadContexts.Focused = false; scene.OnSimulationFrame();
            Check(!p.Controls.MoveUp.IsDown, "focus suppresses model-derived touch keys");
            touch.ReleaseEverything(); Apply(); scene.OnSimulationFrame(); GamepadContexts.Focused = true;
            for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
            Check(!p.Controls.MoveUp.IsDown && !p.Controls.Jump.IsPressed, "touch focus release returns without ghost input");
            // Independent physical key is represented separately, then merged as
            // AndroidInput does: its hold wins when the touch source is neutral.
            Key(keyboard, Keys.D, true); scene.OnSimulationFrame(); Check(p.Controls.MoveRight.IsDown, "independent keyboard remains effective beside neutral touch");
            Key(keyboard, Keys.D, false); scene.OnSimulationFrame();
        }
        finally { MomentumSceneTests.Release(scene); ResetSources(); }
    }
    private static void Lifecycle(Hunter hunter)
    {
        ResetSources(); Scene scene = MomentumSceneTests.Load(hunter, true, out var keyboard, matchRule: true);
        try
        {
            var p = scene.Players.Main; Publish(); GamepadManager.SelectDevice(Device);
            GamepadManager.ReplaceRuntime(new GamepadRuntimeConfig());
            for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
            foreach (string surface in new[] { "focus", "menu", "text-entry" })
            {
                if (surface == "focus") GamepadContexts.Focused = false;
                else if (surface == "menu") GamepadContexts.MenuVisible = true;
                else GamepadContexts.TextEntryActive = true;
                Publish(.5f, .5f, GamepadButtons.A); scene.OnSimulationFrame();
                Check(!p.Controls.Jump.IsPressed && !p.Controls.AnalogMoveActive, surface + " suppresses controller gameplay");
                // Release while UI owns input, then regain: test no stale edge/axis,
                // rather than claiming held continuous sticks must be quarantined.
                Publish(); scene.OnSimulationFrame();
                GamepadContexts.Focused = true; GamepadContexts.MenuVisible = false; GamepadContexts.TextEntryActive = false;
                for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
                Check(!p.Controls.Jump.IsPressed && !p.Controls.AnalogMoveActive, surface + " returns without ghost jump or movement");
            }
            GamepadManager.RemoveDevice(Device);
            // Rebinding is on the real canonical keybind, not a direct IsDown write.
            p.Controls.MoveUp.Key = Keys.I; Key(keyboard, Keys.W, true); scene.OnSimulationFrame();
            Check(!p.Controls.MoveUp.IsDown, "old keyboard binding retired"); Key(keyboard, Keys.W, false);
            Key(keyboard, Keys.I, true); scene.OnSimulationFrame(); Check(p.Controls.MoveUp.IsDown, "new keyboard binding drives actual scene");
            Key(keyboard, Keys.I, false); scene.OnSimulationFrame();
            p.TakeDamage(10000, DamageFlags.IgnoreInvuln, null, null); Check(p.Health == 0, "actual native death");
            long count = scene.DeveloperMomentumSteps;
            for (int i = 0; i < 10; i++) scene.OnSimulationFrame();
            Check(scene.DeveloperMomentumSteps == count, "dead body has no solver ownership");
            Key(keyboard, Keys.Enter, true); for (int i = 0; i < 240; i++) scene.OnSimulationFrame(); Key(keyboard, Keys.Enter, false);
            scene.CameraSequences.Current?.End(); for (int i = 0; i < 120; i++) scene.OnSimulationFrame();
            Check(p.Health > 0 && p.ModMomentumEligible, "native keyboard respawn resumes eligible biped");
            Check(!p.Controls.MoveUp.IsDown && !p.Controls.Jump.IsPressed && !p.Controls.AnalogMoveActive, "respawn has no previous source ghost");
        }
        finally { MomentumSceneTests.Release(scene); ResetSources(); }
    }
    private static void StylusForms(Hunter hunter)
    {
        ResetSources(); Scene scene = MomentumSceneTests.Load(hunter, true, out _, matchRule: true);
        try
        {
            var p = scene.Players.Main; PointerInput.StylusMode = true; StylusZone.Enabled = true; StylusZone.SetRect(0, 0, 1);
            void Contact(bool down, bool accepts = true) => PointerDevice.Update(new PointerSample(PointerDeviceType.Touch, 77, 228, 166, down, down, true), 256, 192, acceptsInput: accepts);
            Contact(false); scene.OnSimulationFrame(); Contact(true); scene.OnSimulationFrame();
            Check(p.Controls.Morph.IsPressed && (p.IsMorphing || p.IsAltForm), "actual touch pointer DS button enters native morph");
            for (int i = 0; i < 100; i++) { Contact(true); scene.OnSimulationFrame(); }
            Check(p.IsAltForm && !p.IsUnmorphing, "held touch does not repeat morph");
            long count = scene.DeveloperMomentumSteps;
            Contact(false); scene.OnSimulationFrame(); Contact(false); scene.OnSimulationFrame();
            Check(scene.DeveloperMomentumSteps == count, "native alt retains solver ownership fence");
            // A contact begun on a UI surface cannot become a gameplay edge.
            Contact(true, false); scene.OnSimulationFrame(); Contact(true); scene.OnSimulationFrame();
            Check(!p.Controls.Morph.IsPressed && p.IsAltForm, "UI-owned touch held through return has no ghost morph");
            Contact(false); scene.OnSimulationFrame(); Contact(false); scene.OnSimulationFrame();
            Contact(true); scene.OnSimulationFrame(); Check(p.Controls.Morph.IsPressed, "released touch rearms canonical binding");
            Contact(false); for (int i = 0; i < 120; i++) scene.OnSimulationFrame();
            Check(!p.IsAltForm && p.ModMomentumEligible && scene.DeveloperMomentumSteps > count, "native unmorph returns Momentum after actual touch binding");
        }
        finally { MomentumSceneTests.Release(scene); PointerDevice.Reset(); PointerInput.StylusMode = false; }
    }
}
