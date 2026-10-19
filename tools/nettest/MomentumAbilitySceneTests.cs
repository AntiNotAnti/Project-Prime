using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Actual offline native forms and keyboard ability input. Bomb activation has
// separate acceptance in MomentumSpecialSceneTests; Kanden/Sylux here cover roll.
internal static class MomentumAbilitySceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, PlayerFlags1 Flags1,
        PlayerFlags2 Flags2, bool Alt, bool Morphing, bool Unmorphing, int AttackTime,
        int Cooldown, int Charge, int Acceleration, int CrestTimer, int TurretHealth, Vector3 TurretPosition);
    private sealed record Trace(List<Tick> Ticks, bool Activated, bool Released, bool Split,
        bool Anchored, bool Recombined, bool EligibleReturned, long AltSolverSteps);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static int Number(PlayerEntity player, string field)
        => Convert.ToInt32(typeof(PlayerEntity).GetField(field, Hidden)!.GetValue(player));

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay ability fixture");
            Check(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            var results = new List<object>();
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
                Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                Trace native = Capture(hunter, false), momentum = Capture(hunter, true);
                Check(native.Ticks.Count == momentum.Ticks.Count, hunter + " identical native transition/ability duration");
                for (int i = 0; i < native.Ticks.Count; i++)
                    Check(native.Ticks[i] == momentum.Ticks[i], $"{hunter} exact native form/ability trajectory and timing at {i}");
                Check(native.Activated && momentum.Activated, hunter + " genuine ability or roll activation");
                Check(native.Released && momentum.Released, hunter + " release stops native ability state");
                Check(momentum.AltSolverSteps == 0, hunter + " no solver in native forms/transitions");
                Check(momentum.EligibleReturned, hunter + " biped solver eligibility returns");
                if (hunter == Hunter.Weavel)
                    Check(native.Split && native.Anchored && native.Recombined
                        && momentum.Split && momentum.Anchored && momentum.Recombined, "actual Halfturret split/anchor/recombine");
                results.Add(new { hunter = hunter.ToString(), ticks = momentum.Ticks.Count,
                    momentum.Activated, momentum.Released, momentum.Split, momentum.Anchored,
                    momentum.Recombined, momentum.EligibleReturned, momentum.AltSolverSteps });
                Console.WriteLine("MOMENTUM ABILITY PASS " + hunter);
            }
            Trace wallNative = Capture(Hunter.Spire, false, wall: true);
            Trace wallMomentum = Capture(Hunter.Spire, true, wall: true);
            for (int i = 0; i < wallNative.Ticks.Count; i += 20)
            {
                Tick t = wallNative.Ticks[i];
                Console.WriteLine($"SPIRE WALL tick={i} position={t.Position} speed={t.Speed} flags={t.Flags1}/{t.Flags2} crest={t.CrestTimer}");
            }
            Check(wallNative.Activated && wallMomentum.Activated, "Spire actual authored wall climb activated");
            Check(wallNative.Ticks.Exists(t => t.CrestTimer > 0) && wallMomentum.Ticks.Exists(t => t.CrestTimer > 0),
                "Spire actual wall lip loss activates native ledge crest timer");
            Check(wallNative.Ticks.Count == wallMomentum.Ticks.Count, "Spire wall trace length parity");
            for (int i = 0; i < wallNative.Ticks.Count; i++)
                Check(wallNative.Ticks[i] == wallMomentum.Ticks[i], "Spire native wall climb trajectory parity " + i);
            Check(wallMomentum.AltSolverSteps == 0 && wallMomentum.EligibleReturned,
                "Spire wall climb remains native and returns biped eligibility");
            results.Add(new { hunter = "Spire", scenario = "authored-wall-climb", ticks = wallMomentum.Ticks.Count,
                wallMomentum.Activated, crestObserved = wallMomentum.Ticks.Exists(t => t.CrestTimer > 0) });
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "asset-backed offline CPU paired native form and keyboard ability traces plus authored Spire wall climb; Kanden/Sylux roll only, bombs covered separately; no GL/network/replay acceptance", results }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM ABILITY PASS {_checks} assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM ABILITY FAIL " + error); return 1; }
    }

    private static Trace Capture(Hunter hunter, bool momentum, bool wall = false)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out var keyboard,
            wall ? "TEST MOMENTUM COLLISION" : "MP3 PROVING GROUND");
        try
        {
            PlayerEntity player = scene.Players.Main;
            player.Controls.AltAttack.Key = Keys.F; player.Controls.Boost.Key = Keys.B;
            player.Controls.RollUp.Key = Keys.W;
            if (wall)
            {
                player.Reposition(new Vector3(-60, 2, -3), Vector3.UnitZ, NodeRef.None);
                player.PrevPosition = player.Position; player.Speed = Vector3.Zero;
                for (int i = 0; i < 30; i++) scene.OnSimulationFrame();
            }
            var ticks = new List<Tick>();
            bool activated = false, released = false, split = false, anchored = false;
            Vector3? anchor = null;
            long steps = scene.DeveloperMomentumSteps;
            void Step()
            {
                scene.OnSimulationFrame();
                Check(player.Health > 0, hunter + " remains alive throughout native ability fixture");
                ticks.Add(new(player.Position, player.Speed, player.Flags1, player.Flags2,
                    player.IsAltForm, player.IsMorphing, player.IsUnmorphing,
                    Number(player, "_altAttackTime"), Number(player, "_altAttackCooldown"),
                    Number(player, "_boostCharge"), Number(player, "_accelerationTimer"),
                    Number(player, "_spireLedgeCrestTimer"),
                    hunter == Hunter.Weavel ? player.Halfturret.Health : 0,
                    hunter == Hunter.Weavel ? player.Halfturret.Position : Vector3.Zero));
                if (hunter == Hunter.Weavel && player.Flags2.TestFlag(PlayerFlags2.Halfturret)) split = true;
            }
            player.ModStartFormSwitch();
            Check(player.IsMorphing, hunter + " native morph entry");
            for (int i = 0; i < 180 && player.IsMorphing; i++) Step();
            Check(player.IsAltForm && !player.IsMorphing, hunter + " real morph animation completes");
            for (int i = 0; i < 120; i++) Step();
            if (hunter == Hunter.Weavel) anchor = player.Halfturret.Position;
            Vector3 start = player.Position;
            bool roll = wall || hunter is Hunter.Samus or Hunter.Kanden or Hunter.Sylux;
            Key(keyboard, Keys.W, roll);
            Key(keyboard, hunter == Hunter.Samus ? Keys.B : Keys.F, !roll || hunter == Hunter.Samus);
            for (int i = 0; i < (wall ? 400 : 160); i++)
            {
                Step();
                activated |= wall ? player.Flags2.TestFlag(PlayerFlags2.SpireClimbing)
                    : hunter == Hunter.Samus ? Number(player, "_boostCharge") > 0
                    : roll ? (player.Position - start).LengthSquared > .01f
                    : player.Flags2.TestFlag(PlayerFlags2.AltAttack);
            }
            Key(keyboard, Keys.B, false); Key(keyboard, Keys.F, false); Key(keyboard, Keys.W, false);
            bool boostReleased = false;
            for (int i = 0; i < 180; i++)
            {
                Step();
                boostReleased |= player.Flags1.TestFlag(PlayerFlags1.Boosting);
                if (anchor.HasValue) anchored |= player.Halfturret.Position == anchor.Value
                    && (player.Position - anchor.Value).LengthSquared > .01f;
            }
            if (hunter == Hunter.Samus) Check(boostReleased, "Samus real charge release sets Boosting");
            if (hunter is Hunter.Trace or Hunter.Weavel)
                Check(ticks.Exists(t => t.Flags2.TestFlag(PlayerFlags2.AltAttack) && t.Speed.LengthSquared > .01f), hunter + " actual lunge velocity");
            released = !player.Flags2.TestFlag(PlayerFlags2.AltAttack) && Number(player, "_boostCharge") == 0;
            player.ModStartFormSwitch();
            Check(!player.IsAltForm || player.IsUnmorphing, hunter + " native unmorph requested");
            for (int i = 0; i < 180 && (player.IsAltForm || player.IsMorphing || player.IsUnmorphing); i++) Step();
            bool recombined = hunter != Hunter.Weavel || !player.Flags2.TestFlag(PlayerFlags2.Halfturret);
            // The first biped simulation may legitimately run the solver; end
            // parity at the native form boundary, before any further biped ticks.
            long altSteps = scene.DeveloperMomentumSteps - steps;
            bool eligible = player.ModMomentumEligible;
            return new(ticks, activated, released, split, anchored, recombined, eligible, altSteps);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
