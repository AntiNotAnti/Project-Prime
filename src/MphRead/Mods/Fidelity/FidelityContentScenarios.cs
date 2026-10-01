using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Fidelity;

/// <summary>Optional real engine probes. No content bytes or proprietary fixtures are serialized.</summary>
internal static class FidelityContentScenarios
{
    internal static readonly FidelityScenario[] All = Create();
    private static FidelityScenario[] Create()
    {
        var scenarios = new List<FidelityScenario>();
        foreach (string id in new[] { "movement.walk", "movement.jump", "movement.air-control", "weapon.powerbeam", "weapon.missile",
            "weapon.imperialist", "weapon.judicator", "weapon.magmaul", "weapon.battlehammer", "weapon.voltdriver", "weapon.shockcoil" })
            scenarios.Add(new(id, 1, 180, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
                "Real offline engine, Samus slot 1, 120 warmup ticks, fixed native room. Movement/firing/ammo/projectile-state regression; not targeted headshot/splash acceptance."));
        return scenarios.ToArray();
    }
    internal static List<FidelityCheckpoint> Capture(FidelityScenario scenario, int presentationHz)
    {
        if (NetSession.Active) throw new InvalidOperationException("Content oracle requires a fresh offline process.");
        Paths.MphKey = Ver.AMHE1;
        if (!ServerSim.Available(out string reason)) throw new InvalidOperationException("F2 content unavailable: " + reason);
        Headless.Enter();
        Rng.SetRng1(scenario.Seed); Rng.SetRng2(scenario.Seed ^ 0xa5a5a5a5);
        bool force = MapAudit.ForceEveryone;
        MapAudit.ForceEveryone = true;
        Scene? scene = null;
        try
        {
            scene = new Scene(new(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            scene.AddPlayer(Hunter.Samus); scene.AddPlayer(Hunter.Samus);
            scene.Players.PlayerCount = 2; scene.Players.MainPlayerIndex = 0;
            foreach (var player in scene.Players.Items) player.IsBot = false;
            scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 2);
            scene.OnLoad(); scene.GameState.MatchTime = -1;
            scene.Random.SetRng1(scenario.Seed); scene.Random.SetRng2(scenario.Seed ^ 0xa5a5a5a5);
            var actor = scene.Players.Items[1]; // slot 0 is the headless host's suppressed local input lane
            for (int warmup = 0; warmup < 120; warmup++) { NetTestScript.Rest(actor, wantBiped: true); scene.OnSimulationFrame(); }
            if (!actor.LoadFlags.TestFlag(LoadFlags.Spawned) || actor.Health <= 0) throw new InvalidOperationException("F2 actor failed to spawn.");
            bool weapon = scenario.Id.StartsWith("weapon.", StringComparison.Ordinal);
            if (weapon)
            {
                BeamType beam = scenario.Id[7..] switch
                {
                    "powerbeam" => BeamType.PowerBeam, "missile" => BeamType.Missile, "imperialist" => BeamType.Imperialist,
                    "judicator" => BeamType.Judicator, "magmaul" => BeamType.Magmaul, "battlehammer" => BeamType.Battlehammer,
                    "voltdriver" => BeamType.VoltDriver, "shockcoil" => BeamType.ShockCoil,
                    _ => throw new InvalidOperationException("Unknown weapon scenario.")
                };
                actor.ModArmWeapon(beam);
            }
            if (presentationHz < 30 || presentationHz > 1000) throw new ArgumentOutOfRangeException(nameof(presentationHz));
            var output = new List<FidelityCheckpoint>();
            Render.FrameTiming.Reset(); Render.FrameTiming.ResetDiagnostics();
            int tick = 0, frames = 0;
            while (tick < scenario.Ticks && frames++ < presentationHz * 5)
            {
                int steps = Render.FrameTiming.Advance(1.0 / presentationHz);
                for (int step = 0; step < steps && tick < scenario.Ticks; step++)
                {
                    tick++;
                    if (weapon) NetTestScript.HoldFire(actor, tick % 30 < 12);
                    else
                    {
                        if (scenario.Id == "movement.jump") NetTestScript.Rest(actor, wantBiped: true);
                        else NetTestScript.WalkForward(actor);
                        if (scenario.Id is "movement.jump" or "movement.air-control")
                        {
                            bool jump = tick % 60 == 1;
                            actor.Controls.Jump.IsDown = jump; actor.Controls.Jump.IsPressed = jump;
                            actor.Controls.Jump.IsReleased = tick % 60 == 2;
                        }
                    }
                    scene.OnSimulationFrame();
                    var point = FidelityOracle.Point(tick, ("rng1", scene.Random.Rng1), ("rng2", scene.Random.Rng2),
                        ("health", actor.Health), ("charge", actor.ModChargeLevel), ("weapon", (int)actor.CurrentWeapon), ("ammoUa", actor.ModAmmo.Ua), ("ammoMissile", actor.ModAmmo.Missiles),
                        ("animation", actor.BipedModel2.AnimInfo.Index[0]), ("gunAnimation", (int)actor.GunAnimation));
                    void Vector(string key, Vector3 vector)
                    {
                        point.Values.Add(key + ".x", FidelityOracle.Normalize(vector.X));
                        point.Values.Add(key + ".y", FidelityOracle.Normalize(vector.Y));
                        point.Values.Add(key + ".z", FidelityOracle.Normalize(vector.Z));
                    }
                    Vector("position4096", actor.Position); Vector("velocity4096", actor.Speed);
                    int count = 0;
                    foreach (var entity in scene.Entities)
                        if (entity is BeamProjectileEntity beam)
                        {
                            if (count >= 16) throw new InvalidOperationException("F2 projectile capture budget exceeded; do not silently truncate state.");
                            string prefix = "projectile." + count++;
                            point.Values.Add(prefix + ".entity", beam.Id); point.Values.Add(prefix + ".weapon", (int)beam.Beam);
                            point.Values.Add(prefix + ".damage4096", FidelityOracle.Normalize(beam.Damage));
                            point.Values.Add(prefix + ".headshot4096", FidelityOracle.Normalize(beam.HeadshotDamage));
                            point.Values.Add(prefix + ".splash4096", FidelityOracle.Normalize(beam.SplashDamage));
                            Vector(prefix + ".position4096", beam.Position); Vector(prefix + ".velocity4096", beam.Velocity);
                        }
                    point.Values.Add("projectiles", count);
                    output.Add(point);
                }
            }
            if (tick != scenario.Ticks || Render.FrameTiming.Stalls != 0 || Render.FrameTiming.DroppedSteps != 0)
                throw new InvalidOperationException("F2 clock failed to deliver the scenario ticks.");
            if (!weapon && output.TrueForAll(p => p.Values["position4096.x"] == output[0].Values["position4096.x"]
                && p.Values["position4096.y"] == output[0].Values["position4096.y"] && p.Values["position4096.z"] == output[0].Values["position4096.z"]))
                throw new InvalidOperationException("F2 movement probe did not move its actor.");
            if (weapon && !output.Exists(p => p.Values["projectiles"] > 0))
                throw new InvalidOperationException("F2 firing probe did not create a projectile.");
            return output;
        }
        finally
        {
            Render.FrameTiming.Reset(); Render.FrameTiming.ResetDiagnostics();
            MapAudit.ForceEveryone = force;
            scene?.DoCleanup();
            Read.ClearCache();
        }
    }
}
