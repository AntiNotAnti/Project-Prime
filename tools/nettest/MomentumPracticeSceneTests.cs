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
internal static class MomentumPracticeSceneTests
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
                Capture native = CaptureScene(hunter, false, false, true);
                Capture jump = CaptureScene(hunter, true, true, true);
                for (int i = 0; i < native.Ticks.Length; i++)
                {
                    Tick n = native.Ticks[i], q = jump.Ticks[i];
                    Check(MathF.Abs(n.Y - q.Y) <= .00001f && MathF.Abs(n.VelocityY - q.VelocityY) <= .00001f,
                        hunter + " jump-only native vertical parity tick " + i);
                    Check(n.Grounded == q.Grounded && n.UsedJump == q.UsedJump,
                        hunter + " native landing/jump flags preserved");
                }
                Check(jump.Airborne && jump.Landed, hunter + " actual jump/collision landing");
                Capture v1 = CaptureScene(hunter, true, false, false);
                Capture practice = CaptureScene(hunter, true, true, false);
                Check(practice.Steps > 0 && practice.NativeYBitComparisons > 0,
                    hunter + " practice solver executes and preserves native Y bits");
                Check(practice.AirGainSamples > 0 && practice.DiagonalSamples > 0,
                    hunter + " actual airborne W+A accelerates");
                Check(practice.Displacement > .01f && practice.Airborne && practice.Landed,
                    hunter + " controls move real body and native collision lands");
                rows.Add(new { hunter = hunter.ToString(), nativeJump = native, practiceJump = jump,
                    version1 = v1, practice });
                Console.WriteLine("MOMENTUM PRACTICE SCENE PASS " + hunter);
            }
            RetiredRuleFallback();
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "Actual offline CPU scene, synthetic keyboard and native spawn/intro skip; no transforms/velocities written, no physical device, GL, online or replay qualification",
                    schedule = "jump-only: Space tick0; moving: W ticks0..95, A ticks49..95, Space tick48; release through tick179; fixed native simulation steps", rows },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM PRACTICE SCENE PASS {_checks} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM PRACTICE SCENE FAIL " + ex); return 1; }
    }

    private static void RetiredRuleFallback()
    {
        Scene scene = MomentumSceneTests.Load(Hunter.Samus, true, out KeyboardState keyboard,
            practiceTuning: MomentumPracticeTuning.Quake3Default);
        try
        {
            // Synthetic policy seam: select the supported offline v1 rule on a
            // real already-spawned scene, then retire the private draft. This is
            // not lobby admission or a real bot lifecycle acceptance claim.
            scene.GameState.MovementModel = MovementModel.Momentum;
            scene.SideScene = true;
            Check(!scene.DeveloperMomentumActive, "real scene draft retires on side-scene exclusion");
            scene.SideScene = false;
            Check(!scene.DeveloperMomentumActive && scene.MomentumMovementActive,
                "supported rule resumes without reactivating retired draft");
            SetKey(keyboard, Keys.W, true);
            long before = scene.DeveloperMomentumSteps;
            scene.OnSimulationFrame();
            Check(scene.DeveloperMomentumSteps == before + 1, "supported rule executes after draft retirement");
            var sample = scene.LastMomentumStep!.Value;
            Check(sample.Tuning == MomentumProfileV1.Tuning, "retired private rates never leak into v1 fallback");
            var player = scene.Players.Main!;
            bool wasBot = player.IsBot;
            try
            {
                // Targeted synthetic identity seam only, no server/ready role,
                // native AI input, spawn or accepted bot lifecycle fabricated.
                player.IsBot = true;
                var apply = typeof(PlayerEntity).GetMethod("ModTryApplyMomentum",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check((bool)apply.Invoke(player, new object[] { false })!, "supported offline bot policy can execute");
                Check(scene.LastMomentumStep!.Value.Tuning == MomentumProfileV1.Tuning,
                    "bot policy consumes v1 even when scene retains retired private draft");
            }
            finally { player.IsBot = wasBot; }
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static Capture CaptureScene(Hunter hunter, bool momentum, bool practice, bool jumpOnly)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out KeyboardState keyboard,
            practiceTuning: practice ? MomentumPracticeTuning.Quake3Default : null);
        try
        {
            Check(Equals(scene.DeveloperMomentumPracticeTuning, practice ? MomentumPracticeTuning.Quake3Default : null),
                "scene owns exactly selected optional preset");
            Check(scene.GameState.MovementModel == MovementModel.Native && !NetSession.Active,
                "private preset never selects admitted match rule");
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
                        var preset = MomentumPracticeTuning.Quake3Default;
                        Check(sample.Tuning == preset.Tuning && sample.Input.DesiredSpeed == preset.GroundSpeed,
                            "diagnostics use selected immutable rates and calibrated ground speed");
                        Vector3 expected = preset.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0,
                            sample.NativeVelocityBefore.Z * 30), sample.Input);
                        Check(MathF.Abs(sample.NativeVelocityAfter.X - expected.X / 30) <= .00001f
                            && MathF.Abs(sample.NativeVelocityAfter.Z - expected.Z / 30) <= .00001f,
                            "actual adapter matches selected private solver exactly");
                        Check(!sample.SpeedLimited && !sample.IncomingOverCap,
                            "private uncapped preset does not report v1 cap application");
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
}
