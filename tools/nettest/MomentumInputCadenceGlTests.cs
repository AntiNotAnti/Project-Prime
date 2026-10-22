using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real local body/HUD/GL, synthetic keyboard script and virtual host cadences.
// No achieved display-Hz, physical keyboard, controller or Android claim.
internal static class MomentumInputCadenceGlTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, bool Grounded, bool Standing, long SolverSteps);
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
    internal static int Run(string? outputPath)
    {
        int priorCap = FrameTiming.FrameRateCap;
        string? directory = outputPath == null ? null : Path.GetDirectoryName(Path.GetFullPath(outputPath));
        var summaries = new List<object>();
        try
        {
            checks = 0;
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive, "ordinary offline GL process");
            Check(ServerSim.Available(out string reason), reason);
            if (directory != null) Directory.CreateDirectory(directory);
            var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(640, 480);
            settings.Title = "Project Prime Movement Virtual Cadence Acceptance";
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            GLFW.SwapInterval(0);
            string renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version);
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
                foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum })
                {
                    Tick[]? reference = null;
                    foreach (int hz in new[] { 60, 30, 120, 144, 240 })
                    {
                        var result = Trace(window, hunter, model, hz, directory);
                        if (reference == null) reference = result.Ticks;
                        else
                            for (int tick = 0; tick < reference.Length; tick++)
                                Check(result.Ticks[tick] == reference[tick], $"{hunter}/{model}/{hz} virtual host: actual body differs at fixed tick {tick}");
                        summaries.Add(new { hunter = hunter.ToString(), model = model.ToString(), virtualHostHz = hz,
                            simulationHz = 60, fixedTicks = result.Ticks.Length, result.Pictures, result.SolverSteps,
                            result.Airborne, result.Landed, result.InputTicks,
                            actualWallSeconds = result.Seconds, screenshot = result.Screenshot,
                            trace = Array.ConvertAll(result.Ticks, tick => new { position = V(tick.Position), speed = V(tick.Speed),
                                tick.Grounded, tick.Standing, tick.SolverSteps }) });
                        Console.WriteLine($"MOMENTUM INPUT CADENCE GL PASS {hunter}/{model} virtualHost={hz} fixedTicks=180 pictures={result.Pictures} solver={result.SolverSteps}");
                    }
                }
            if (outputPath != null) File.WriteAllText(Path.GetFullPath(outputPath), JsonSerializer.Serialize(new {
                checks, renderer, version, width = 640, height = 480,
                scope = "Actual local seven-Hunter Native/Momentum biped simulation, HUD/world GL and collision; synthetic keyboard script at fixed60Hz under virtual30/60/120/144/240 host cadences. Picture counts are actual draws, not measured display refresh or physical-device acceptance.", summaries }));
            Console.WriteLine($"MOMENTUM INPUT CADENCE GL PASS {checks} assertions, 70 rendered scenarios");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM INPUT CADENCE GL FAIL " + error); return 1; }
        finally { FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset(); PointerDevice.Reset(); }
    }
    private static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private sealed record Capture(Tick[] Ticks, int Pictures, long SolverSteps, bool Airborne, bool Landed, int InputTicks, double Seconds, string? Screenshot);
    private static Capture Trace(NativeWindow window, Hunter hunter, MovementModel model, int hz, string? directory)
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        var keyboard = SyntheticInput.CreateKeyboard();
        var key = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
        Scene? scene = null;
        try
        {
            GamepadManager.ClearAll(); PointerDevice.Reset();
            scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            scene.AddPlayer(hunter); scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 1);
            new MatchDefinition { Mode = GameMode.Battle, MovementModel = model, TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad(); scene.OnResize();
            var player = scene.Players.Main;
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveRight.Key = Keys.D;
            player.Controls.MoveDown.Key = Keys.S; player.Controls.Jump.Key = Keys.Space;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = false;
            void Draw()
            {
                scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "actual native world/HUD render remains active");
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
            }
            key(keyboard, Keys.Enter, true);
            for (int warm = 0; warm < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); warm++) { scene.OnSimulationFrame(); Draw(); }
            key(keyboard, Keys.Enter, false);
            Check(player.LoadFlags.TestFlag(LoadFlags.Spawned) && scene.CameraMode == CameraMode.Player && player.IsMainPlayer,
                "ordinary native shoot-to-spawn local player and camera");
            scene.CameraSequences.Current?.End();
            for (int warm = 0; warm < 120; warm++) { scene.OnSimulationFrame(); Draw(); }
            FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); FrameTiming.FrameRateCap = hz;
            var ticks = new Tick[180]; int simulated = 0, pictures = 0, inputTicks = 0;
            bool airborne = false, landed = false; long solverStart = scene.DeveloperMomentumSteps;
            Vector3 origin = player.Position; var clock = Stopwatch.StartNew();
            while (simulated < ticks.Length)
            {
                int due = FrameTiming.Advance(1.0 / hz);
                for (int step = 0; step < due && simulated < ticks.Length; step++)
                {
                    key(keyboard, Keys.W, simulated < 48 || simulated is >= 60 and < 84);
                    key(keyboard, Keys.D, simulated is >= 24 and < 60 || simulated is >= 84 and < 108);
                    key(keyboard, Keys.S, simulated is >= 108 and < 132);
                    key(keyboard, Keys.Space, simulated == 60);
                    if (player.Controls.MoveUp.IsDown || player.Controls.MoveRight.IsDown || player.Controls.MoveDown.IsDown) inputTicks++;
                    long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                    scene.OnSimulationFrame(); long delta = scene.DeveloperMomentumSteps - before;
                    Check(scene.FrameCount == frame + 1 && delta is >= 0 and <= 1, "exactly one actual fixed simulation tick and bounded solver count");
                    Check(player.ModIsInPlay && player.Health > 0 && scene.GameState.MovementModel == model, "live local rule-selected body remains active");
                    if (model == MovementModel.Native) Check(delta == 0, "Native actual body never invokes Momentum solver");
                    if (delta == 1)
                    {
                        var sample = scene.LastMomentumStep!.Value;
                        Check(sample.SimulationFrame == frame && sample.Slot == player.SlotIndex, "actual solver diagnostic belongs to fixed tick");
                        var expected = MomentumProfileV1.Step(new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30), sample.Input);
                        Check(sample.NativeVelocityAfter.X == expected.Velocity.X / 30 && sample.NativeVelocityAfter.Z == expected.Velocity.Z / 30,
                            "actual rendered biped uses v1 planar solver");
                        Check(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "actual rendered solver preserves vertical bits");
                    }
                    bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                    landed |= airborne && grounded; airborne |= !grounded;
                    ticks[simulated++] = new(player.Position, player.Speed, grounded, player.Flags1.TestFlag(PlayerFlags1.Standing), delta);
                }
                Vector3 beforePosition = player.Position, beforeSpeed = player.Speed;
                Draw(); pictures++;
                Check(player.Position == beforePosition && player.Speed == beforeSpeed, "actual draw preserves movement body and velocity");
            }
            Check(FrameTiming.DroppedSteps == 0 && FrameTiming.Stalls == 0 && inputTicks > 0 && (player.Position - origin).Length > .01f,
                "virtual host cadence retains every fixed step and actual native input movement");
            Check(airborne && landed, "actual native jump/air/landing under rendered cadence");
            long solverSteps = scene.DeveloperMomentumSteps - solverStart;
            Check(model == MovementModel.Native ? solverSteps == 0 : solverSteps > 0, "selected mode governs rendered actual solver");
            string? screenshot = directory == null || hunter != Hunter.Samus ? null : Path.Combine(directory, $"cadence-{hunter}-{model}-{hz}.png");
            if (screenshot != null) Check(ScreenCapture.Save(scene, screenshot), "actual scene framebuffer screenshot saved");
            return new(ticks, pictures, solverSteps, airborne, landed, inputTicks, clock.Elapsed.TotalSeconds, screenshot);
        }
        finally { FrameTiming.Reset(); if (scene != null) MomentumSceneTests.Release(scene); }
    }
}
