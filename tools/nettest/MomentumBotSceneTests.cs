using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Offline practice bot created before native load/spawn. Every measured input is
// authored by the actual AI decision/motor pipeline, with no bot control or speed
// writes. This is CPU/world acceptance, not a live UDP or rendered-device test.
internal static class MomentumBotSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
    private static void Near(float actual, float expected, string message)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .00001f, $"{message}: {actual} != {expected}");
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static int Run(string? outputPath)
    {
        var results = new List<object>(); var failures = new List<string>();
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay bot fixture");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            foreach (Hunter hunter in Hunters)
            foreach (bool momentum in new[] { false, true })
            foreach (int level in new[] { 0, 1, 2, 3 })
            {
                string label = $"{hunter}/{(momentum ? "Momentum" : "Native")}/level-{level}";
                try { results.Add(Capture(hunter, momentum, level)); Console.WriteLine("MOMENTUM BOT SCENE PASS " + label); }
                catch (Exception ex) { failures.Add(label + ": " + ex.Message); results.Add(new { label, failure = ex.ToString() }); Console.Error.WriteLine("MOMENTUM BOT SCENE CASE FAIL " + label + ": " + ex.Message); }
            }
            Write(outputPath, results, failures.Count == 0 ? null : string.Join("\n", failures));
            Console.WriteLine($"MOMENTUM BOT SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions, {failures.Count} failed cases");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) { Write(outputPath, results, ex.ToString()); Console.Error.WriteLine("MOMENTUM BOT SCENE FAIL " + ex); return 1; }
    }

    private static Scene Load(Hunter hunter, bool momentum, int level, out KeyboardState keyboard)
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard(); Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            scene.AddPlayer(Hunter.Samus); scene.AddPlayer(hunter);
            var bot = scene.Players.Items[1]; bot.IsBot = true; bot.BotLevel = level;
            scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 2);
            new MatchDefinition { RoomKey = "MP3 PROVING GROUND", Mode = GameMode.Battle,
                MovementModel = momentum ? MovementModel.Momentum : MovementModel.Native,
                TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad(); scene.CameraSequences.Current?.End();
            SetInputMode(scene, "All");
            var local = scene.Players.Items[0];
            foreach (var control in local.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            local.Controls.Shoot.Key = Keys.Enter; local.Controls.MouseAim = false; local.Controls.KeyboardAim = true;
            Check(!NetSession.Active && bot.IsBot && bot.Hunter == hunter && bot.BotLevel == level,
                "native offline practice bot identity configured before load");
            return scene;
        }
        catch { if (scene != null) MomentumSceneTests.Release(scene); throw; }
    }
    private static void SetInputMode(Scene scene, string mode)
    {
        FieldInfo field = typeof(Scene).GetField("_inputMode", Hidden)!;
        field.SetValue(scene, Enum.Parse(field.FieldType, mode));
    }

    private static object Capture(Hunter hunter, bool momentum, int level)
    {
        Scene scene = Load(hunter, momentum, level, out var keyboard);
        try
        {
            var bot = scene.Players.Items[1]; var local = scene.Players.Items[0];
            var set = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            int botSteps = 0, partial = 0, diagonal = 0, focusedSteps = 0, directMoving = 0, solverPartial = 0, solverDiagonal = 0;
            int activeMovedTicks = 0, analogClearTransition = 0, analogClearDead = 0;
            float maximumInput = 0, maximumSpeed = 0, totalTravel = 0;
            bool spawned = false, introEnded = false; var trace = new List<object>();
            void Step(int tick, bool captureTrace)
            {
                ulong frame = scene.FrameCount; long beforeSteps = scene.DeveloperMomentumSteps;
                Vector3 before = bot.Position; bool living = bot.Health > 0 && bot.ModIsInPlay;
                bool deadBefore = bot.Health == 0, transitionBefore = bot.IsMorphing || bot.IsUnmorphing;
                scene.OnSimulationFrame();
                Check(scene.FrameCount == frame + 1 && Finite(bot.Position) && Finite(bot.Speed), "finite actual fixed-step bot state");
                spawned |= bot.Health > 0 && bot.ModIsInPlay;
                float displacement = (bot.Position - before).Length;
                if (living && bot.Health > 0 && displacement > .00001f && displacement < 4)
                { activeMovedTicks++; totalTravel += displacement; }
                var c = bot.Controls;
                float radial = MathF.Sqrt(c.AnalogMoveX * c.AnalogMoveX + c.AnalogMoveY * c.AnalogMoveY);
                if (bot.Health > 0 && !bot.IsMorphing && !bot.IsUnmorphing && !bot.IsAltForm && c.AnalogMoveActive)
                {
                    Check(radial <= 1.00001f, "AI motor preserves unit radial analog input");
                    maximumInput = MathF.Max(maximumInput, radial);
                    if (radial > .001f && radial < .999f) partial++;
                    if (MathF.Abs(c.AnalogMoveX) > .001f && MathF.Abs(c.AnalogMoveY) > .001f) diagonal++;
                }
                if (transitionBefore && (bot.IsMorphing || bot.IsUnmorphing))
                { Check(!c.AnalogMoveActive, "native transition clears bot analog input"); analogClearTransition++; }
                if (deadBefore && bot.Health == 0)
                { Check(!c.AnalogMoveActive, "dead bot has no active analog input"); analogClearDead++; }
                if (!momentum) Check(scene.DeveloperMomentumSteps == 0, "Native bot never invokes Momentum solver");
                if (momentum && scene.DeveloperMomentumSteps > beforeSteps
                    && scene.LastMomentumStep is { } sample && sample.Slot == bot.SlotIndex && sample.SimulationFrame == frame)
                {
                    botSteps++;
                    Check(scene.MomentumOwnsPlayer(bot) && !NetHooks.IsPuppet(bot), "real offline bot owns its solver application");
                    var expected = MomentumProfileV1.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30), sample.Input);
                    Near(sample.NativeVelocityAfter.X, expected.Velocity.X / 30, "bot canonical solver X");
                    Near(sample.NativeVelocityAfter.Z, expected.Velocity.Z / 30, "bot canonical solver Z");
                    Check(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "bot solver preserves native Y bits");
                    float x = c.MoveRight.IsDown ? c.AnalogScaleX(1) : c.MoveLeft.IsDown ? -c.AnalogScaleX(-1) : 0;
                    float y = c.MoveUp.IsDown ? c.AnalogScaleY(1) : c.MoveDown.IsDown ? -c.AnalogScaleY(-1) : 0;
                    Near(sample.Input.MoveAxes.X, x, "bot genuine signed analog X reaches solver");
                    Near(sample.Input.MoveAxes.Y, y, "bot genuine signed analog Y reaches solver");
                    float wish = sample.Input.MoveAxes.Length;
                    if (wish > .001f && wish < .999f) solverPartial++;
                    if (MathF.Abs(sample.Input.MoveAxes.X) > .001f && MathF.Abs(sample.Input.MoveAxes.Y) > .001f) solverDiagonal++;
                    Check(sample.SpeedLimited == expected.SpeedLimited && sample.IncomingOverCap == expected.IncomingOverCap, "bot uses same legitimate incoming-speed budget as human solver");
                    double incoming = (double)sample.NativeVelocityBefore.X * sample.NativeVelocityBefore.X + (double)sample.NativeVelocityBefore.Z * sample.NativeVelocityBefore.Z;
                    double outgoing = (double)sample.NativeVelocityAfter.X * sample.NativeVelocityAfter.X + (double)sample.NativeVelocityAfter.Z * sample.NativeVelocityAfter.Z;
                    double cap = MomentumProfileV1.RetentionSpeedCap / 30;
                    Check(outgoing <= Math.Max(incoming, cap * cap) + .00000001, "bot cannot add speed beyond regular cap or legitimate incoming residual");
                    maximumSpeed = MathF.Max(maximumSpeed, MathF.Sqrt((float)outgoing));
                    if (!scene.DeveloperMomentumInputAllowed) focusedSteps++;
                    if (MathF.Abs(bot.Speed.X - sample.NativeVelocityAfter.X) <= .00001f
                        && MathF.Abs(bot.Speed.Z - sample.NativeVelocityAfter.Z) <= .00001f && outgoing > .000001)
                        directMoving++;
                }
                if (captureTrace && tick % 12 == 0)
                    trace.Add(new { tick, frame, position = V(bot.Position), speed = V(bot.Speed), analog = new[] { c.AnalogMoveX, c.AnalogMoveY },
                        c.AnalogMoveActive, bot.IsAltForm, bot.IsMorphing, bot.IsUnmorphing, bot.Health, botSteps,
                        localInputAllowed = scene.DeveloperMomentumInputAllowed });
            }
            for (int tick = 0; tick < 720; tick++)
            {
                // Only the real local human's keyboard is driven to native spawn.
                // Bot controls, AI buttons and velocity are never overridden.
                set(keyboard, Keys.Enter, !local.LoadFlags.TestFlag(LoadFlags.Spawned));
                if (tick == 180 || tick == 420) SetInputMode(scene, "CameraOnly");
                if (tick == 300) SetInputMode(scene, "All");
                Step(tick, captureTrace: true);
                if (!introEnded && local.LoadFlags.TestFlag(LoadFlags.Spawned) && scene.CameraSequences.Current is { } intro)
                { intro.End(); introEnded = true; }
            }
            set(keyboard, Keys.Enter, false);
            Check(spawned && activeMovedTicks > 0 && totalTravel > .1f, "actual native AI spawn and earned movement activated");
            Check(partial > 0 && diagonal > 0, $"actual AI decisions produce partial analog magnitudes and diagonals (partial={partial}, diagonal={diagonal})");
            if (momentum) Check(botSteps > 0 && directMoving > 0 && focusedSteps > 0 && solverPartial > 0 && solverDiagonal > 0,
                $"bot solver causes actual planar motion and remains active while local input is suppressed (steps={botSteps}, direct={directMoving}, focus={focusedSteps}, partial={solverPartial}, diagonal={solverDiagonal})");
            SetInputMode(scene, "CameraOnly");
            // Explicit native form request fixture, not an AI decision claim.
            for (int i = 0; i < 600 && (bot.Health == 0 || bot.IsMorphing || bot.IsUnmorphing); i++) Step(i, false);
            Check(bot.Health > 0, "living native bot available for form fixture");
            bool originalAlt = bot.IsAltForm;
            bot.ModStartFormSwitch();
            Check(bot.IsMorphing || bot.IsUnmorphing || bot.IsAltForm != originalAlt, "native bot form endpoint accepts transition fixture");
            for (int i = 0; i < 120 && (bot.IsMorphing || bot.IsUnmorphing); i++) Step(i, false);
            Check(bot.IsAltForm != originalAlt && analogClearTransition > 0, "native bot form transition completes with analog input cleared");
            bot.TakeDamage(10000, DamageFlags.IgnoreInvuln, null, null);
            Check(bot.Health == 0, "native lethal damage enters bot death");
            for (int i = 0; i < 600 && bot.Health == 0; i++) Step(i, false);
            Check(bot.Health > 0 && bot.ModIsInPlay && analogClearDead > 0, "native AI respawn completes after dead input clearing");
            for (int i = 0; i < 24; i++) Step(i, false);
            return new { hunter = hunter.ToString(), mode = momentum ? "Momentum" : "Native", level,
                botSteps, partial, diagonal, solverPartial, solverDiagonal, focusedSteps, directMoving, activeMovedTicks, totalTravel,
                maximumInput, maximumSpeed, analogClearTransition, analogClearDead, trace };
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static void Write(string? outputPath, List<object> results, string? failure)
    {
        if (outputPath == null) return;
        string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, failure,
            scope = "asset-backed offline CPU native AI decisions/motor -> actual controls -> ordinary Native/Momentum physics; no bot Controls/Speed writes during measurement; no UDP/render/device acceptance",
            conditions = new { hunters = 7, botLevels = new[] { 0, 1, 2, 3 }, simulationHz = 60, measuredTicks = 720,
                lifecycle = "explicit native form/death endpoint fixtures; AI owns respawn and all movement inputs",
                navigation = "ordinary real map AI traversal; no dedicated ledge/stopping-distance acceptance claimed" }, results },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
