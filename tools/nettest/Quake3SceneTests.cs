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

// Actual offline local input and collision. Synthetic keyboard, native spawn/intro
// skip; no body/facing/velocity writes and no transport or recorder role changes.
internal static class Quake3SceneTests
{
    private static int _checks;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
        .CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private sealed record Tick(float Y, float VelocityY, bool Grounded, bool UsedJump);
    private sealed record Capture(Tick[] Ticks, long Steps, int AirGainSamples, int DiagonalSamples,
        int NativeYBitComparisons, bool Airborne, bool Landed, float Displacement, float PeakSpeed);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline non-recording process");
            Check(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            var rows = new List<object>();
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
                Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                Capture native = CaptureScene(hunter, MovementModel.Native, true);
                Check(native.Steps == 0 && native.NativeYBitComparisons == 0, hunter + " native excludes adapter");
                Capture jump = CaptureScene(hunter, MovementModel.Quake3, true);
                for (int i = 0; i < native.Ticks.Length; i++)
                {
                    Tick n = native.Ticks[i], q = jump.Ticks[i];
                    Check(MathF.Abs(n.Y - q.Y) <= .00001f && MathF.Abs(n.VelocityY - q.VelocityY) <= .00001f,
                        hunter + " jump-only native vertical parity tick " + i);
                    Check(n.Grounded == q.Grounded && n.UsedJump == q.UsedJump,
                        hunter + " native landing/jump flags preserved");
                }
                Check(jump.Airborne && jump.Landed, hunter + " actual jump/collision landing");
                Capture v1 = CaptureScene(hunter, MovementModel.Momentum, false);
                Capture practice = CaptureScene(hunter, MovementModel.Quake3, false);
                Check(practice.PeakSpeed > v1.PeakSpeed + .01f, hunter + " Q3 actual W+A gains beyond version one");
                Check(practice.Steps > 0 && practice.NativeYBitComparisons > 0,
                    hunter + " Quake3 solver executes and preserves native Y bits");
                Check(practice.AirGainSamples > 0 && practice.DiagonalSamples > 0,
                    hunter + " actual airborne W+A accelerates");
                Check(practice.Displacement > .01f && practice.Airborne && practice.Landed,
                    hunter + " controls move real body and native collision lands");
                rows.Add(new { hunter = hunter.ToString(), nativeJump = native, practiceJump = jump,
                    version1 = v1, practice });
                Console.WriteLine("QUAKE3 SCENE PASS " + hunter);
            }
            Exclusions();
            AllocationContract();
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    movementModel = MovementModel.Quake3.ToString(), profileVersion = Quake3MovementProfileV1.ProfileVersion,
                    scope = "Actual offline CPU scene, synthetic keyboard and native spawn/intro skip; no transforms/velocities written, no physical device, GL, online or replay qualification",
                    schedule = "jump-only: Space tick0; moving: W ticks0..95, A ticks49..95, Space tick48; release through tick179; fixed native simulation steps", rows },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"QUAKE3 SCENE PASS {_checks} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("QUAKE3 SCENE FAIL " + ex); return 1; }
    }

    private static void AllocationContract()
    {
        float ground = Quake3MovementProfileV1.GroundSpeed;
        var tuning = Quake3MovementProfileV1.Tuning;
        Check(Quake3MovementProfileV1.ProfileVersion == 1 && ground == MomentumProfileV1.GroundSpeed,
            "Q3 v1 uses immutable native-calibrated speed");
        Check(tuning.GroundAcceleration == 10 && tuning.GroundFriction == 6
            && MathF.Abs(tuning.StopSpeed - ground * 100f / 320) < .000001f
            && tuning.AirAcceleration == 1 && tuning.AirWishSpeedCap == ground,
            "Q3 immutable 10/6/100:320/1/full-wish contract");
        var perpendicular = new MomentumInput(new Vector2(-1, 0), Vector3.UnitZ, -Vector3.UnitX,
            ground, false, 1f / 60);
        Vector3 incoming = new(0, -3, ground * 2);
        var aboveCap = Quake3MovementProfileV1.Step(incoming, perpendicular);
        Check(new Vector2(aboveCap.Velocity.X, aboveCap.Velocity.Z).Length > ground * 2
            && !aboveCap.SpeedLimited && !aboveCap.IncomingOverCap,
            "perpendicular air input still gains above the V1 total-speed cap");
        Check(BitConverter.SingleToInt32Bits(aboveCap.Velocity.Y) == BitConverter.SingleToInt32Bits(incoming.Y),
            "canonical profile preserves Y bits above prior total-speed cap");
        Check(Quake3MovementProfileV1.Step(incoming, perpendicular with { DesiredSpeed = 1 }).Velocity
            == aboveCap.Velocity, "external desired speed cannot mutate the canonical profile");
        var input = new MomentumInput(new Vector2(-1, 1), Vector3.UnitZ, -Vector3.UnitX,
            Quake3MovementProfileV1.GroundSpeed, false, 1f / 60);
        Vector3 speed = new(0, 3, 17);
        for (int i = 0; i < 10000; i++) speed = Quake3MovementProfileV1.Step(speed, input).Velocity;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) speed = Quake3MovementProfileV1.Step(speed, input).Velocity;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && float.IsFinite(speed.LengthSquared), "immutable Quake3 solver zero allocation");
    }

    private static void Exclusions()
    {
        Scene scene = Load(Hunter.Samus, MovementModel.Quake3, out _);
        try
        {
            var player = scene.Players.Main!;
            Check(player.ModMomentumEligible, "normal Q3 biped admitted");
            var flags = player.Flags2;
            var property = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;
            try
            {
                foreach (var flag in new[] { PlayerFlags2.BipedLock, PlayerFlags2.BipedStuck })
                { property.SetValue(player, flags | flag); Check(!player.ModMomentumEligible, "native exclusion " + flag); }
            }
            finally { property.SetValue(player, flags); }
            foreach (var probe in new (string Field, object Value)[] {
                ("_field6D0", true), ("_frozenTimer", 4), ("_health", 0), ("_slipperiness", 1),
                ("_accelerationTimer", 4), ("_jumpPadControlLock", 4), ("_jumpPadControlLockMin", 4) })
            {
                var field = typeof(PlayerEntity).GetField(probe.Field, BindingFlags.Instance | BindingFlags.NonPublic)!;
                object old = field.GetValue(player)!;
                try { field.SetValue(player, Convert.ChangeType(probe.Value, field.FieldType));
                    Check(!player.ModMomentumEligible, "native exclusion " + probe.Field); }
                finally { field.SetValue(player, old); }
            }
            player.ModStartFormSwitch();
            Check(player.IsMorphing && !player.ModMomentumEligible, "native morph bypass");
            for (int i = 0; i < 100 && player.IsMorphing; i++) scene.OnSimulationFrame();
            Check(player.IsAltForm && !player.ModMomentumEligible, "native alt bypass");
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static Capture CaptureScene(Hunter hunter, MovementModel model, bool jumpOnly)
    {
        bool practice = model == MovementModel.Quake3;
        Scene scene = Load(hunter, model, out KeyboardState keyboard);
        try
        {
            Check(scene.DeveloperMomentumPracticeTuning == null && !scene.DeveloperMomentumActive,
                "normal match rule requires no developer opt-in or private preset");
            Check(scene.GameState.MovementModel == model && !NetSession.Active,
                "actual normal offline match owns selected model");
            var player = scene.Players.Main!;
            Vector3 start = player.Position;
            long initial = scene.DeveloperMomentumSteps;
            int airGain = 0, diagonal = 0, yBits = 0;
            bool airborne = false, landed = false;
            float peak = 0;
            var ticks = new Tick[180];
            for (int i = 0; i < ticks.Length; i++)
            {
                SetKey(keyboard, Keys.W, !jumpOnly && i < 96);
                SetKey(keyboard, Keys.A, !jumpOnly && i >= 49 && i < 96);
                SetKey(keyboard, Keys.Space, i == (jumpOnly ? 0 : 48));
                long before = scene.DeveloperMomentumSteps;
                scene.OnSimulationFrame();
                long delta = scene.DeveloperMomentumSteps - before;
                Check(delta is 0 or 1, "at most one solver application");
                if (delta == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Check(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "native Y exact at adapter");
                    yBits++;
                    float incoming = new Vector2(sample.NativeVelocityBefore.X, sample.NativeVelocityBefore.Z).Length;
                    float outgoing = new Vector2(sample.NativeVelocityAfter.X, sample.NativeVelocityAfter.Z).Length;
                    if (!sample.Input.Grounded && outgoing > incoming + .000001f) airGain++;
                    if (sample.Input.MoveAxes.X < 0 && sample.Input.MoveAxes.Y > 0) diagonal++;
                    if (practice)
                    {
                        var tuning = Quake3MovementProfileV1.Tuning;
                        Check(sample.Tuning == tuning && sample.Input.DesiredSpeed == Quake3MovementProfileV1.GroundSpeed,
                            "diagnostics use selected immutable rates and calibrated ground speed");
                        Vector3 expected = Quake3MovementProfileV1.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0,
                            sample.NativeVelocityBefore.Z * 30), sample.Input).Velocity;
                        Check(MathF.Abs(sample.NativeVelocityAfter.X - expected.X / 30) <= .00001f
                            && MathF.Abs(sample.NativeVelocityAfter.Z - expected.Z / 30) <= .00001f,
                            "actual adapter matches immutable match-rule solver exactly");
                        Check(!sample.SpeedLimited && !sample.IncomingOverCap,
                            "Quake3 uncapped profile does not report v1 cap application");
                    }
                }
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                landed |= airborne && grounded; airborne |= !grounded;
                Check(float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Y)
                    && float.IsFinite(player.Position.Z) && float.IsFinite(player.Speed.LengthSquared), "finite native collision/body state");
                peak = MathF.Max(peak, new Vector2(player.Speed.X, player.Speed.Z).Length * 30);
                ticks[i] = new(player.Position.Y, player.Speed.Y, grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump));
            }
            return new(ticks, scene.DeveloperMomentumSteps - initial, airGain, diagonal, yBits,
                airborne, landed, (player.Position - start).Length, peak);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
    private static Scene Load(Hunter hunter, MovementModel model, out KeyboardState keyboard)
    {
        const string room = "MP3 PROVING GROUND";
        bool momentum = model != MovementModel.Native;
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard();
        Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            scene.AddPlayer(hunter);
            scene.AddRoom(room, GameMode.Battle, playerCount: 1);
            // Fixtures select an explicit default so a preceding rule-selected
            // scene cannot leak its configuration into the next Native/developer run.
            new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                MovementModel = model,
                TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            if (momentum)
            {
                // Diagnostic sampling alone: do not fabricate movement ownership.
                typeof(Scene).GetField("_developerMomentumDiagnostics", (BindingFlags.Instance | BindingFlags.NonPublic))!.SetValue(scene, true);
            }
            scene.OnLoad();
            // Headless retains its Roam presentation camera; enable the actual local
            // keyboard/body input path without constructing a window or HUD textures.
            var inputMode = typeof(Scene).GetField("_inputMode", (BindingFlags.Instance | BindingFlags.NonPublic))!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            var player = scene.Players.Items[0];
            Check(!player.IsBot && player.Hunter == hunter && NetHooks.LocalSlot == 0 && !NetSession.Active, "actual offline local Hunter");
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveDown.Key = Keys.S;
            player.Controls.MoveLeft.Key = Keys.A; player.Controls.MoveRight.Key = Keys.D;
            player.Controls.Jump.Key = Keys.Space; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", (BindingFlags.Instance | BindingFlags.NonPublic) | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            setKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) scene.OnSimulationFrame();
            setKey(keyboard, Keys.Enter, false);
            Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "native offline shoot-to-spawn activated");
            // Roam does not advance the player-camera introduction. Use its normal
            // skip/end operation; script sequence ownership remains a production gate.
            scene.CameraSequences.Current?.End();
            for (int i = 0; i < 120; i++) scene.OnSimulationFrame();
            Check(ReferenceEquals(player, scene.Players.Main) && player.ModIsInPlay && player.Health > 0 && !player.IsAltForm, "settled living biped");
            if (momentum) Check(player.ModMomentumEligible,
                $"settled offline biped is eligible (active={scene.MomentumMovementActive}, input={scene.DeveloperMomentumInputAllowed}, flags={player.Flags1}/{player.Flags2})");
            return scene;
        }
        catch { if (scene != null) MomentumSceneTests.Release(scene); throw; }
    }

}
