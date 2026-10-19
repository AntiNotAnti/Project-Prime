using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Asset-backed offline CPU acceptance. No transport, authority impersonation, GL or replay acceptance.
internal static class MomentumSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static bool _admissionChecked;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly string[] Scenarios = { "forward-release", "reversal", "diagonal", "jump-only", "air-input", "idle" };
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, bool Grounded, bool UsedJump);
    private sealed record Capture(Tick[] Ticks, long Steps, int DirectComparisons, int MovingDirectComparisons, bool InputObserved, bool Moved, bool Airborne, bool Landed);
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string label)
        => Require(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .00001f, $"{label}: {actual} != {expected}");

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0; _admissionChecked = false;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            var summaries = new List<object>();
            foreach (Hunter hunter in Hunters)
            {
                bool different = false;
                foreach (string scenario in Scenarios)
                {
                    Capture native = RunScenario(hunter, scenario, false);
                    Capture momentum = RunScenario(hunter, scenario, true);
                    Require(native.Steps == 0, "Classic never invokes developer solver");
                    Require(momentum.Steps > 0, "Momentum solver activated");
                    Require(momentum.DirectComparisons > 0, "solver output survives native postprocessing on clear fixture ticks");
                    if (scenario != "idle") Require(native.InputObserved && momentum.InputObserved, "actual requested controls observed");
                    if (scenario is "forward-release" or "reversal" or "diagonal" or "air-input")
                        {
                        Require(native.Moved && momentum.Moved, "real input causes displacement");
                        Require(momentum.MovingDirectComparisons > 0, "nonzero solver velocity survives native postprocessing");
                    }
                    if (scenario is "jump-only" or "air-input")
                        Require(native.Airborne && native.Landed && momentum.Airborne && momentum.Landed, "native jump and collision landing activated");
                    for (int i = 0; i < native.Ticks.Length; i++)
                    {
                        Tick n = native.Ticks[i], m = momentum.Ticks[i];
                        different |= MathF.Abs(n.Speed.X - m.Speed.X) > .0001f || MathF.Abs(n.Speed.Z - m.Speed.Z) > .0001f;
                        if (scenario == "jump-only")
                        {
                            Near(m.Position.Y, n.Position.Y, $"{hunter} jump Y position {i}");
                            Near(m.Speed.Y, n.Speed.Y, $"{hunter} jump Y velocity {i}");
                            Require(m.Grounded == n.Grounded && m.UsedJump == n.UsedJump, "native jump flags parity");
                        }
                    }
                    summaries.Add(new { hunter = hunter.ToString(), scenario, momentum.Steps, momentum.DirectComparisons, momentum.MovingDirectComparisons });
                    Console.WriteLine($"MOMENTUM SCENE PASS {hunter}/{scenario}");
                }
                Require(different, hunter + " Momentum differs from Classic under planar input");
            }
            GateChecks();
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "asset-backed offline CPU simulation/input/collision; Roam camera with native intro skip; no GL/network/replay acceptance", checks = _checks, summaries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM SCENE PASS {_checks} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM SCENE FAIL " + ex); return 1; }
    }

    internal static Scene Load(Hunter hunter, bool momentum, out KeyboardState keyboard, string room = "MP3 PROVING GROUND")
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard();
        Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            scene.AddPlayer(hunter);
            scene.AddRoom(room, GameMode.Battle, playerCount: 1);
            if (momentum)
            {
                if (!_admissionChecked)
                {
                    scene.SideScene = true;
                    Require(!scene.TryEnableDeveloperMomentum(true), "side scene opt-in refused");
                    scene.SideScene = false;
                    scene.GameState.Mode = GameMode.SinglePlayer;
                    Require(!scene.TryEnableDeveloperMomentum(true), "campaign opt-in refused");
                    scene.GameState.Mode = GameMode.Battle;
                    _admissionChecked = true;
                }
                Require(scene.TryEnableDeveloperMomentum(captureDiagnostics: true), "explicit pre-load developer opt-in");
                Require(!scene.TryEnableDeveloperMomentum(true), "pre-step repeated opt-in refused");
            }
            scene.OnLoad();
            // Headless retains its Roam presentation camera; enable the actual local
            // keyboard/body input path without constructing a window or HUD textures.
            var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            var player = scene.Players.Items[0];
            Require(!player.IsBot && player.Hunter == hunter && NetHooks.LocalSlot == 0 && !NetSession.Active, "actual offline local Hunter");
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveDown.Key = Keys.S;
            player.Controls.MoveLeft.Key = Keys.A; player.Controls.MoveRight.Key = Keys.D;
            player.Controls.Jump.Key = Keys.Space; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            setKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) scene.OnSimulationFrame();
            setKey(keyboard, Keys.Enter, false);
            Require(player.LoadFlags.TestFlag(LoadFlags.Spawned), "native offline shoot-to-spawn activated");
            // Roam does not advance the player-camera introduction. Use its normal
            // skip/end operation; script sequence ownership remains a production gate.
            scene.CameraSequences.Current?.End();
            for (int i = 0; i < 120; i++) scene.OnSimulationFrame();
            Require(ReferenceEquals(player, scene.Players.Main) && player.ModIsInPlay && player.Health > 0 && !player.IsAltForm, "settled living biped");
            if (momentum) Require(player.ModMomentumEligible,
                $"settled offline biped is eligible (active={scene.DeveloperMomentumActive}, input={scene.DeveloperMomentumInputAllowed}, flags={player.Flags1}/{player.Flags2})");
            return scene;
        }
        catch { if (scene != null) Release(scene); throw; }
    }

    internal static void Release(Scene scene)
    {
        var commit = PostMatchCommitContext.Capture(scene.GameState, 0, SaveWhen.Never, 0);
        try { scene.DoCleanup(); }
        finally
        {
            try { scene.UnloadGl(); }
            finally { commit.Commit(); commit.Release(); ForegroundSceneBinding.TryRelease(commit); }
        }
    }

    private static Capture RunScenario(Hunter hunter, string scenario, bool momentum)
    {
        Scene scene = Load(hunter, momentum, out var keyboard);
        try
        {
            var player = scene.Players.Items[0];
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            Keys[] keys = { Keys.W, Keys.S, Keys.A, Keys.D, Keys.Space };
            var ticks = new Tick[180];
            Vector3 start = player.Position;
            long firstSteps = scene.DeveloperMomentumSteps;
            int direct = 0, movingDirect = 0; bool input = false, moved = false, airborne = false, landed = false;
            for (int i = 0; i < ticks.Length; i++)
            {
                foreach (Keys key in keys) setKey(keyboard, key, Down(scenario, i, key));
                long before = scene.DeveloperMomentumSteps;
                ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                long count = scene.DeveloperMomentumSteps - before;
                Require(count >= 0 && count <= 1, "at most one solver application per simulation tick");
                if (count == 1)
                {
                    Require(scene.LastMomentumStep.HasValue, "diagnostic sample accompanies solver invocation");
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.Slot == player.SlotIndex && sample.SimulationFrame == frame, "solver sample belongs to current local simulation tick");
                    Vector3 expected = MomentumProfileV1.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30), sample.Input).Velocity;
                    Near(sample.NativeVelocityAfter.X, expected.X / 30, "solver X adapter");
                    Near(sample.NativeVelocityAfter.Z, expected.Z / 30, "solver Z adapter");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y), "adapter preserves native Y bits");
                    // A genuine clear tick: collision/postprocessing leaves both planar components unchanged.
                    // Require many such ticks separately; contact-dependent changes remain native-owned.
                    if (MathF.Abs(player.Speed.X - sample.NativeVelocityAfter.X) <= .00001f
                        && MathF.Abs(player.Speed.Z - sample.NativeVelocityAfter.Z) <= .00001f)
                    {
                        direct++;
                        if (MathF.Abs(sample.NativeVelocityAfter.X) + MathF.Abs(sample.NativeVelocityAfter.Z) > .001f) movingDirect++;
                    }
                }
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                landed |= airborne && grounded; airborne |= !grounded;
                input |= player.Controls.MoveUp.IsDown || player.Controls.MoveDown.IsDown || player.Controls.MoveRight.IsDown || player.Controls.Jump.IsDown;
                moved |= (player.Position - start).Length > .001f;
                ticks[i] = new(player.Position, player.Speed, grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump));
            }
            return new(ticks, scene.DeveloperMomentumSteps - firstSteps, direct, movingDirect, input, moved, airborne, landed);
        }
        finally { Release(scene); }
    }

    private static bool Down(string scenario, int tick, Keys key) => scenario switch
    {
        "forward-release" => tick < 48 && key == Keys.W,
        "reversal" => tick < 32 ? key == Keys.W : tick < 64 ? key == Keys.D : tick < 96 && key == Keys.S,
        "diagonal" => tick < 48 && (key == Keys.W || key == Keys.D),
        "jump-only" => tick == 0 && key == Keys.Space,
        "air-input" => tick == 0 ? key == Keys.Space : tick < 24 ? key == Keys.W : tick < 48 && key == Keys.D,
        _ => false
    };

    private static void GateChecks()
    {
        Scene late = Load(Hunter.Samus, false, out _);
        try { Require(!late.TryEnableDeveloperMomentum(true), "first opt-in after simulation began is refused"); }
        finally { Release(late); }
        Scene scene = Load(Hunter.Samus, true, out _);
        try
        {
            var player = scene.Players.Items[0];
            Require(!scene.TryEnableDeveloperMomentum(true), "late/repeated opt-in refused");
            Require(player.ModMomentumEligible, "baseline is eligible before individual bypass probes");
            void FieldGate(string name, object value)
            {
                FieldInfo field = typeof(PlayerEntity).GetField(name, Hidden)!;
                object old = field.GetValue(player)!;
                try
                {
                    field.SetValue(player, Convert.ChangeType(value, field.FieldType));
                    Require(!player.ModMomentumEligible, name + " retains native movement ownership");
                }
                finally { field.SetValue(player, old); }
            }
            PlayerFlags2 flags = player.Flags2;
            var flagsProperty = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;
            try
            {
                flagsProperty.SetValue(player, flags | PlayerFlags2.BipedLock); Require(!player.ModMomentumEligible, "BipedLock bypass");
                flagsProperty.SetValue(player, flags | PlayerFlags2.BipedStuck); Require(!player.ModMomentumEligible, "BipedStuck bypass");
            }
            finally { flagsProperty.SetValue(player, flags); }
            FieldGate("_field6D0", true); FieldGate("_frozenTimer", 4); FieldGate("_health", 0); FieldGate("_slipperiness", 1);
            FieldGate("_accelerationTimer", 4); FieldGate("_jumpPadControlLock", 4); FieldGate("_jumpPadControlLockMin", 4);
            FieldInfo inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            object oldInputMode = inputMode.GetValue(scene)!;
            long steps = scene.DeveloperMomentumSteps;
            try
            {
                inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "CameraOnly"));
                scene.OnSimulationFrame();
                Require(scene.DeveloperMomentumSteps == steps && !player.ModMomentumEligible, "actual scene input suppression bypass");
            }
            finally { inputMode.SetValue(scene, oldInputMode); }
            scene.OnSimulationFrame();
            Require(player.ModMomentumEligible, "input eligibility resumes through native input path");
            player.ModStartFormSwitch();
            Require(player.IsMorphing && !player.ModMomentumEligible, "actual morph transition bypass");
            for (int i = 0; i < 100 && player.IsMorphing; i++) scene.OnSimulationFrame();
            Require(player.IsAltForm && !player.ModMomentumEligible, "actual alt form bypass");
            scene.SideScene = true;
            scene.OnSimulationFrame();
            scene.SideScene = false;
            Require(!scene.DeveloperMomentumActive, "unsafe scene change permanently retires developer opt-in");
        }
        finally { Release(scene); }
    }
}
