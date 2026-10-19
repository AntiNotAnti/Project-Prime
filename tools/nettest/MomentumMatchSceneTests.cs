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

// Actual offline match-rule adapter, using native scene input/collision. UDP workers are separate acceptance.
internal static class MomentumMatchSceneTests
{
    private static int _checks;
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, uint Flags1, uint Flags2, long SolverCount);
    private sealed record RunData(Tick[] Ticks, long SolverSteps, int InputTicks, float Displacement);
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0; Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            Require(new MatchDefinition().MovementModel == MovementModel.Native, "match default remains Native");
            var summaries = new List<object>();
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                RunData native = Capture(hunter, false, true);
                RunData developer = Capture(hunter, true, false);
                RunData rule = Capture(hunter, true, true);
                Require(native.SolverSteps == 0, "Native rule never invokes Momentum solver");
                Require(rule.SolverSteps > 0 && rule.SolverSteps == developer.SolverSteps, "real match rule activates the same solver cadence");
                Require(rule.Ticks.SequenceEqual(developer.Ticks), hunter + " authorized match-rule trajectory exactly equals developer trajectory");
                Require(!native.Ticks.Select(tick => tick.Speed).SequenceEqual(rule.Ticks.Select(tick => tick.Speed)), "Momentum changes actual planar movement relative to Native");
                Require(native.InputTicks > 0 && rule.InputTicks > 0 && rule.Displacement > 1, "actual keyboard controls move the collision body");
                summaries.Add(new { hunter = hunter.ToString(), nativeSteps = native.SolverSteps,
                    developerSteps = developer.SolverSteps, ruleSteps = rule.SolverSteps,
                    rule.InputTicks, nativeDisplacement = native.Displacement, ruleDisplacement = rule.Displacement, ticks = rule.Ticks.Length });
                // A fresh default/developer fixture must not inherit the previous match's Momentum rule.
                RunData nativeAfterRule = Capture(hunter, false, false);
                Require(nativeAfterRule.Ticks.SequenceEqual(native.Ticks), "Native default fixture remains exact after a Momentum match scene retires");
                Console.WriteLine($"MOMENTUM MATCH SCENE PASS {hunter}");
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, scope = "asset-backed offline match-rule selection through native scene simulation/input/collision; no live UDP, remote, bot, replay, physical-device or rendered-camera acceptance",
                    conditions = "same seeded native room and spawn; match definition applied after AddRoom/before OnLoad; private diagnostics enabled without developer ownership; W ticks1-32, W+D ticks33-64, Space tick65, D ticks66-96, S ticks97-128, neutral ticks129-180", summaries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM MATCH SCENE PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM MATCH SCENE FAIL " + ex); return 1; }
    }
    private static RunData Capture(Hunter hunter, bool momentum, bool matchRule)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out var keyboard, matchRule: matchRule);
        try
        {
            PlayerEntity player = scene.Players.Main;
            var requested = typeof(Scene).GetField("_developerMomentumRequested", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Require((bool)requested.GetValue(scene)! == (momentum && !matchRule), "only explicit developer fixture installs developer request");
            Require(scene.GameState.MovementModel == (momentum && matchRule ? MovementModel.Momentum : MovementModel.Native), "match configuration carries its explicit model");
            Require(scene.MomentumMovementActive == momentum && scene.MomentumOwnsPlayer(player), "scene resolves actual local rule ownership");
            if (matchRule) Require(!scene.DeveloperMomentumActive, "rule-selected movement does not use developer opt-in");
            var set = typeof(KeyboardState).GetMethod("SetKeyState", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            var ticks = new Tick[180]; int input = 0; long first = scene.DeveloperMomentumSteps; Vector3 origin = player.Position;
            for (int i = 0; i < ticks.Length; i++)
            {
                set(keyboard, Keys.W, i < 64); set(keyboard, Keys.D, i >= 32 && i < 96 && i != 64);
                set(keyboard, Keys.S, i >= 96 && i < 128); set(keyboard, Keys.Space, i == 64);
                long before = scene.DeveloperMomentumSteps; scene.OnSimulationFrame();
                long count = scene.DeveloperMomentumSteps - before;
                Require(count is >= 0 and <= 1, "one solver application at most per actual scene tick");
                Require(!NetSession.Active && !player.IsBot && player.ModIsInPlay, "actual living offline local owner remains in play");
                if (player.Controls.MoveUp.IsDown || player.Controls.MoveRight.IsDown || player.Controls.MoveDown.IsDown || player.Controls.Jump.IsDown) input++;
                ticks[i] = new(player.Position, player.Speed, (uint)player.Flags1, (uint)player.Flags2, count);
            }
            return new(ticks, scene.DeveloperMomentumSteps - first, input, (player.Position - origin).Length);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
