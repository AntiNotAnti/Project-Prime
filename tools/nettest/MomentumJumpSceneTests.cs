using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// CPU-only real offline native input/collision. Synthetic host schedules do not prove device or rendering parity.
internal static class MomentumJumpSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private readonly record struct Tick(float Y, float VerticalSpeed, bool Grounded, bool UsedJump, bool Pressed, float PlanarSpeed);
    private sealed record RunData(Tick[] Ticks, int[] AcceptedTicks, int[] RequestedTicks, int FirstLanding, float ApexRise,
        float Displacement, float MaximumPlanarSpeed, long SolverSteps, int LimitedSteps, int MovingLegalJumps, int NeutralGroundBrakingSteps, Dictionary<string, int> Phases);
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(float a, float b, string message)
        => Require(float.IsFinite(a) && MathF.Abs(a - b) <= .00001f, $"{message}: {a} != {b}");

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            var summaries = new List<object>();
            foreach (Hunter hunter in Hunters)
            {
                RunData calibration = Capture(hunter, false, 180, tick => tick == 0, false, false);
                Require(calibration.FirstLanding > 2 && calibration.AcceptedTicks.SequenceEqual(new[] { 1 }), "calibration genuine native jump/landing");
                int landing = calibration.FirstLanding;
                var cases = new (string Name, Func<int, bool> Jump, bool SpaceJump, int Length, int Expected)[] {
                    ("held-jump", tick => true, false, 240, 1),
                    ("press-before-landing", tick => tick == 0 || tick == landing - 2, false, 180, 1),
                    ("press-on-landing", tick => tick == 0 || tick == landing - 1, false, 180, 1),
                    ("press-after-landing", tick => tick == 0 || tick == landing, false, 180, 2),
                    ("manual-hop-chain", tick => tick % landing == 0, false, 400, 7),
                    ("space-jump-native", tick => tick == 0 || tick == 10 || tick == 20 || tick == 150, true, 240, 3)
                };
                foreach (var item in cases)
                {
                    RunData classic = Capture(hunter, false, item.Length, item.Jump, item.SpaceJump, false);
                    RunData momentum = Capture(hunter, true, item.Length, item.Jump, item.SpaceJump, false);
                    CompareVertical(classic, momentum, hunter + "/" + item.Name);
                    Require(classic.AcceptedTicks.Length == item.Expected, $"{hunter}/{item.Name} expected legal native impulse count {item.Expected}, got {classic.AcceptedTicks.Length}");
                    Require(momentum.SolverSteps > 0, "Momentum jump scene solver activated");
                    Require(momentum.Phases.GetValueOrDefault("LegalJump") == momentum.AcceptedTicks.Length, "diagnostic legal jumps agree with actual native impulses");
                    if (item.SpaceJump)
                    {
                        Require(!classic.Ticks[0].UsedJump && classic.Ticks[10].UsedJump, "native SpaceJump ground/air UsedJump distinction");
                        Require(!classic.AcceptedTicks.Contains(21), "native SpaceJump refuses third airborne impulse");
                    }
                    summaries.Add(Summary(hunter, item.Name, classic, momentum, item.SpaceJump));
                    Console.WriteLine($"MOMENTUM JUMP SCENE PASS {hunter}/{item.Name}");
                }
                RunData nativeTraversal = Capture(hunter, false, 240, tick => tick == 48 || tick == 140, false, true);
                RunData traversal = Capture(hunter, true, 240, tick => tick == 48 || tick == 140, false, true);
                Require(traversal.Displacement > 1 && nativeTraversal.Displacement > 1, "actual traversal controls cause real collision-body displacement");
                Require(traversal.MaximumPlanarSpeed <= MomentumProfileV1.GroundSpeed * MomentumProfileV1.RetentionMultiplierCap + .0001f, "ordinary traversal stays within v1 retention limit");
                Require(traversal.NeutralGroundBrakingSteps > 0, "ordinary neutral grounded motion receives friction");
                summaries.Add(Summary(hunter, "forward-diagonal-reversal-air-traversal", nativeTraversal, traversal, false));
                RunData movingClassic = Capture(hunter, false, 400, _ => false, false, false, movingHop: true);
                RunData movingHop = Capture(hunter, true, 400, _ => false, false, false, movingHop: true);
                Require(movingHop.AcceptedTicks.Length >= 4 && movingHop.MovingLegalJumps > 0 && movingHop.Displacement > 1,
                    "moving manual hop chain has actual nonzero legal-jump retention and traversal");
                Require(movingHop.MaximumPlanarSpeed <= MomentumProfileV1.GroundSpeed * MomentumProfileV1.RetentionMultiplierCap + .0001f, "moving manual hops remain bounded");
                summaries.Add(Summary(hunter, "moving-hop-retention", movingClassic, movingHop, false));
                summaries.Add(new { hunter = hunter.ToString(), scenario = "native-calibration", calibration.FirstLanding, calibration.ApexRise,
                    apexTick = Array.FindIndex(calibration.Ticks, tick => tick.Y == calibration.Ticks.Max(t => t.Y)) + 1,
                    maximumVerticalSpeed = calibration.Ticks.Max(tick => tick.VerticalSpeed), acceptedTicks = calibration.AcceptedTicks });
            }
            // Only the host's batching changes. Every step is still OnSimulationFrame at fixed 60 Hz.
            RunData reference = Capture(Hunter.Samus, true, 180, tick => tick == 0 || tick == 90, false, false);
            foreach (int hz in new[] { 30, 60, 90, 120, 144, 240 })
            {
                RunData scheduled = Capture(Hunter.Samus, true, 180, tick => tick == 0 || tick == 90, false, false, hz);
                Require(reference.Ticks.SequenceEqual(scheduled.Ticks) && reference.AcceptedTicks.SequenceEqual(scheduled.AcceptedTicks), "synthetic host cadence parity " + hz);
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, scope = "asset-backed offline CPU keyboard/input/collision; headless Roam with native intro skip; synthetic fixed-step host scheduling only; no physical device/render/bot/network acceptance",
                    profileVersion = MomentumProfileV1.MomentumProfileVersion, groundSpeed = MomentumProfileV1.GroundSpeed,
                    retentionMultiplier = MomentumProfileV1.RetentionMultiplierCap,
                    conditions = new { room = "MP3 PROVING GROUND", simulationHz = 60, intro = "native End after keyboard shoot-to-spawn", landingPressPolicy = "tick numbers 1-based; before/on/after native collision landing; no production buffering", manualHopSchedule = "one-tick pulses every native firstLanding ticks (first step after previous collision landing)", movingHopSchedule = "W except jump ticks; first Space pulse tick49, subsequent one-tick pulses on first observed grounded step with previous Space released; each run follows actual collision contact", traversal = "W ticks1-120; D ticks61-180; S ticks181-220; Space ticks49,141", syntheticHostHz = new[] {30,60,90,120,144,240} }, summaries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM JUMP SCENE PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM JUMP SCENE FAIL " + ex); return 1; }
    }

    private static object Summary(Hunter hunter, string scenario, RunData classic, RunData momentum, bool spaceJump)
        => new { hunter = hunter.ToString(), scenario, syntheticSpaceJumpAbility = spaceJump, ticks = classic.Ticks.Length,
            requestedPressTicks = classic.RequestedTicks, classicAcceptedTicks = classic.AcceptedTicks, momentumAcceptedTicks = momentum.AcceptedTicks,
            classicLandingTick = classic.FirstLanding, momentumLandingTick = momentum.FirstLanding,
            classicApexRise = classic.ApexRise, momentumApexRise = momentum.ApexRise,
            classicDisplacement = classic.Displacement, momentumDisplacement = momentum.Displacement,
            classicMaximumPlanarSpeed = classic.MaximumPlanarSpeed, momentumMaximumPlanarSpeed = momentum.MaximumPlanarSpeed,
            momentum.SolverSteps, momentum.LimitedSteps, momentum.MovingLegalJumps, momentum.NeutralGroundBrakingSteps, momentum.Phases };

    private static void CompareVertical(RunData classic, RunData momentum, string label)
    {
        Require(classic.AcceptedTicks.SequenceEqual(momentum.AcceptedTicks), label + " accepted native impulse ticks parity");
        Require(classic.RequestedTicks.SequenceEqual(momentum.RequestedTicks), label + " observed input edges parity");
        for (int i = 0; i < classic.Ticks.Length; i++)
        {
            Tick a = classic.Ticks[i], b = momentum.Ticks[i];
            Near(a.Y, b.Y, label + " Y " + i); Near(a.VerticalSpeed, b.VerticalSpeed, label + " vertical velocity " + i);
            Require(a.Grounded == b.Grounded && a.UsedJump == b.UsedJump, label + " native grounded/UsedJump flags parity " + i);
        }
    }

    private static RunData Capture(Hunter hunter, bool momentum, int length, Func<int, bool> jump, bool spaceJump, bool traversal, int hostHz = 60, bool movingHop = false)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out var keyboard);
        try
        {
            PlayerEntity player = scene.Players.Items[0];
            // The stock spawn never grants this dormant native ability. This fixture
            // enables only the ability bit; jump flags/impulses/collision remain native.
            if (spaceJump)
            {
                var abilities = typeof(PlayerEntity).GetField("_abilities", Hidden)!;
                abilities.SetValue(player, (AbilityFlags)abilities.GetValue(player)! | AbilityFlags.SpaceJump);
            }
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            var ticks = new Tick[length]; var accepted = new List<int>(); var requested = new List<int>();
            var phases = new Dictionary<string, int>();
            Vector3 origin = player.Position; float previousY = player.Speed.Y, maxPlanar = 0;
            bool airborne = false, jumpWasDown = false; int landing = -1, limited = 0, movingLegal = 0, groundBraking = 0;
            long startSteps = scene.DeveloperMomentumSteps;
            int index = 0; double accumulator = 0;
            while (index < length)
            {
                accumulator += 1d / hostHz;
                while (index < length && accumulator + 1e-9 >= 1d / 60)
                {
                    accumulator -= 1d / 60;
                    bool jumpDown = movingHop
                        ? index == 48 || index > 48 && player.Flags1.TestFlag(PlayerFlags1.Grounded) && !jumpWasDown
                        : jump(index);
                    setKey(keyboard, Keys.Space, jumpDown);
                    setKey(keyboard, Keys.W, movingHop ? !jumpDown : traversal && index < 120);
                    jumpWasDown = jumpDown;
                    setKey(keyboard, Keys.D, traversal && index >= 60 && index < 180);
                    setKey(keyboard, Keys.S, traversal && index >= 180 && index < 220);
                    ulong frame = scene.FrameCount; long before = scene.DeveloperMomentumSteps;
                    scene.OnSimulationFrame();
                    Require(!NetSession.Active && player.ModIsInPlay && !player.IsBot, "remains genuine offline living local player");
                    Require(scene.DeveloperMomentumSteps - before is >= 0 and <= 1, "one horizontal solver per native simulation tick");
                    bool pressed = player.Controls.Jump.IsPressed;
                    if (pressed) requested.Add(index + 1);
                    if (pressed && player.Speed.Y > .01f && player.Speed.Y > previousY + .01f) accepted.Add(index + 1);
                    previousY = player.Speed.Y;
                    bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                    if (airborne && grounded && landing < 0) landing = index + 1;
                    airborne |= !grounded;
                    float planar = new Vector2(player.Speed.X, player.Speed.Z).Length * 30;
                    maxPlanar = MathF.Max(maxPlanar, planar);
                    ticks[index] = new(player.Position.Y, player.Speed.Y, grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump), pressed, planar);
                    if (scene.DeveloperMomentumSteps > before)
                    {
                        var sample = scene.LastMomentumStep!.Value;
                        Require(sample.SimulationFrame == frame && sample.Slot == player.SlotIndex, "diagnostic belongs to actual tick/player");
                        string phase = sample.ContactPhase.ToString(); phases[phase] = phases.GetValueOrDefault(phase) + 1;
                        if (sample.SpeedLimited) limited++;
                        var expected = MomentumProfileV1.Step(new Vector3(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30), sample.Input);
                        Near(sample.NativeVelocityAfter.X, expected.Velocity.X / 30, "profile scene X result");
                        Near(sample.NativeVelocityAfter.Z, expected.Velocity.Z / 30, "profile scene Z result");
                        Require(sample.SpeedLimited == expected.SpeedLimited && sample.IncomingOverCap == expected.IncomingOverCap, "bounded diagnostics agree with measured profile result");
                        Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "horizontal adapter preserves native vertical bits");
                        float incoming = new Vector2(sample.NativeVelocityBefore.X, sample.NativeVelocityBefore.Z).Length * 30;
                        float outgoing = new Vector2(sample.NativeVelocityAfter.X, sample.NativeVelocityAfter.Z).Length * 30;
                        Require(outgoing <= MathF.Max(incoming, MomentumProfileV1.GroundSpeed * MomentumProfileV1.RetentionMultiplierCap) + .0001f, "ordinary steering cannot create over-limit velocity");
                        if (sample.ContactPhase == MomentumContactPhase.LegalJump)
                        {
                            Require(!sample.Input.Grounded, "accepted native legal jump bypasses ground friction");
                            if (sample.Input.MoveAxes == Vector2.Zero && incoming > .01f)
                            {
                                Near(sample.NativeVelocityAfter.X, sample.NativeVelocityBefore.X, "legal hop retains actual incoming X without steering");
                                Near(sample.NativeVelocityAfter.Z, sample.NativeVelocityBefore.Z, "legal hop retains actual incoming Z without steering");
                                movingLegal++;
                            }
                        }
                        if (sample.Input.Grounded && sample.Input.MoveAxes == Vector2.Zero && incoming > .01f && outgoing < incoming)
                            groundBraking++;
                    }
                    index++;
                }
            }
            Require(requested.Count > 0, "native controls observed requested jump edges");
            Require(accepted.Count > 0 && airborne && landing > 0, "actual native jump, airborne phase and solid landing");
            return new(ticks, accepted.ToArray(), requested.ToArray(), landing, ticks.Max(tick => tick.Y) - origin.Y,
                (player.Position - origin).Length, maxPlanar, scene.DeveloperMomentumSteps - startSteps, limited, movingLegal, groundBraking, phases);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
