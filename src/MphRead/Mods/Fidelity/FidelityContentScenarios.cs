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
        foreach (string id in new[] { "movement.walk", "movement.jump", "movement.air-control", "movement.knockback", "weapon.powerbeam", "weapon.missile",
            "weapon.imperialist", "weapon.judicator", "weapon.magmaul", "weapon.battlehammer", "weapon.voltdriver", "weapon.shockcoil" })
            scenarios.Add(new(id, 1, 180, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
                "Real offline engine, Samus slot 1, 120 warmup ticks, fixed native room. Movement/firing/ammo/projectile-state regression; not targeted headshot/splash acceptance."));
        foreach (string beam in new[] { "powerbeam", "missile", "imperialist", "judicator", "magmaul", "battlehammer", "voltdriver", "shockcoil" })
            scenarios.Add(new("weapon." + beam + ".hit", 1, 360, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
                "Actual engine aimed projectiles and victim damage, stationary close-range target; not exhaustive headshot/splash geometry coverage."));
        scenarios.Add(new("weapon.imperialist.headshot", 1, 360, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
            "Actual aimed Imperialist projectile must produce a classified headshot kill."));
        scenarios.Add(new("weapon.missile.splash", 1, 360, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
            "Actual missiles aimed one unit beside the victim at floor height; captures area damage."));
        scenarios.Add(new("simulation.respawn", 1, 420, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
            "Actual lethal damage at tick 10, death timer and scripted respawn request; offline Battle."));
        scenarios.Add(new("simulation.match-transition", 1, 180, 123456, "extracted:AMHE1:MP3 PROVING GROUND", 1, FidelityTier.F2,
            "Actual Battle timer expiration at tick 10; captures engine match-state transition."));
        scenarios.AddRange(FidelityMatchScenarios.All);
        // Deliberately last: the core simulation, weapon and match/life probes run first.
        foreach (var hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux, Hunter.Noxus, Hunter.Spire, Hunter.Weavel })
            scenarios.Add(new("alt." + hunter.ToString().ToLowerInvariant(), 1, 840, 123456,
                "extracted:AMHE1:MP3 PROVING GROUND", 2, FidelityTier.F2,
                "Final hunter matrix: morph, movement/attack/boost, unmorph, remorph, lethal damage and respawn. Fixture health 999, alternating movement and recentering between phases; captures every two ticks. Not rendered pose/hit-volume acceptance."));
        return scenarios.ToArray();
    }
    internal static List<FidelityCheckpoint> Capture(FidelityScenario scenario, int presentationHz)
    {
        if (scenario.Id.StartsWith("match.", StringComparison.Ordinal)) return FidelityMatchScenarios.Capture(scenario, presentationHz);
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
            bool alternate = scenario.Id.StartsWith("alt.", StringComparison.Ordinal);
            Hunter hunter = alternate ? Enum.Parse<Hunter>(scenario.Id[4..], ignoreCase: true) : Hunter.Samus;
            scene.AddPlayer(Hunter.Samus); scene.AddPlayer(hunter);
            scene.Players.PlayerCount = 2; scene.Players.MainPlayerIndex = 0;
            foreach (var player in scene.Players.Items) player.IsBot = false;
            scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 2);
            scene.OnLoad(); scene.GameState.MatchTime = -1;
            scene.Random.SetRng1(scenario.Seed); scene.Random.SetRng2(scenario.Seed ^ 0xa5a5a5a5);
            var actor = scene.Players.Items[1]; // slot 0 is the headless host's suppressed local input lane
            for (int warmup = 0; warmup < 120; warmup++) { NetTestScript.Rest(actor, wantBiped: true); scene.OnSimulationFrame(); }
            if (!actor.LoadFlags.TestFlag(LoadFlags.Spawned) || actor.Health <= 0) throw new InvalidOperationException("F2 actor failed to spawn.");
            Vector3 altOrigin = actor.Position;
            if (alternate) actor.Health = 999;
            bool weapon = scenario.Id.StartsWith("weapon.", StringComparison.Ordinal);
            if (weapon)
            {
                BeamType beam = scenario.Id[7..].Split('.')[0] switch
                {
                    "powerbeam" => BeamType.PowerBeam, "missile" => BeamType.Missile, "imperialist" => BeamType.Imperialist,
                    "judicator" => BeamType.Judicator, "magmaul" => BeamType.Magmaul, "battlehammer" => BeamType.Battlehammer,
                    "voltdriver" => BeamType.VoltDriver, "shockcoil" => BeamType.ShockCoil,
                    _ => throw new InvalidOperationException("Unknown weapon scenario.")
                };
                actor.ModArmWeapon(beam);
            }
            bool headshot = scenario.Id.EndsWith(".headshot", StringComparison.Ordinal);
            bool splash = scenario.Id.EndsWith(".splash", StringComparison.Ordinal);
            bool hit = headshot || splash || scenario.Id.EndsWith(".hit", StringComparison.Ordinal);
            var victim = scene.Players.Items[0];
            if (hit)
            {
                victim.ModPlaceAt(actor.Position + new Vector3(0, 0, -3));
                victim.Health = headshot ? 99 : 999;
            }
            if (presentationHz < 30 || presentationHz > 1000) throw new ArgumentOutOfRangeException(nameof(presentationHz));
            var output = new List<FidelityCheckpoint>();
            Render.FrameTiming.Reset(); Render.FrameTiming.ResetDiagnostics();
            int tick = 0, frames = 0;
            while (tick < scenario.Ticks && frames++ < presentationHz * (scenario.Ticks / 60 + 2))
            {
                int steps = Render.FrameTiming.Advance(1.0 / presentationHz);
                for (int step = 0; step < steps && tick < scenario.Ticks; step++)
                {
                    tick++;
                    if (alternate) DriveAlt(actor, scene.Players.Items[0], altOrigin, tick);
                    else if (scenario.Id.StartsWith("simulation.", StringComparison.Ordinal))
                    {
                        NetTestScript.Rest(actor, wantBiped: true);
                        if (tick == 10 && scenario.Id == "simulation.respawn")
                            actor.TakeDamage(500, DamageFlags.IgnoreInvuln, null, scene.Players.Items[0]);
                        if (tick == 10 && scenario.Id == "simulation.match-transition") scene.GameState.MatchTime = 0.05f;
                    }
                    else if (weapon)
                    {
                        if (hit)
                        {
                            var aim = actor.ModAimDeltaTowards(headshot ? victim.Position.AddY(Fixed.ToFloat(victim.Values.MaxPickupHeight) - 0.15f)
                                : splash ? victim.Position + new Vector3(1, -0.45f, 0) : victim.ModAimTarget);
                            actor.ModApplyScriptAim(aim.X, aim.Y);
                        }
                        NetTestScript.HoldFire(actor, tick % 30 < 12);
                    }
                    else
                    {
                        if (scenario.Id is "movement.jump" or "movement.knockback") NetTestScript.Rest(actor, wantBiped: true);
                        else NetTestScript.WalkForward(actor);
                        if (scenario.Id == "movement.knockback" && tick == 10)
                            actor.TakeDamage(10, DamageFlags.IgnoreInvuln, new Vector3(0.3f, 0.2f, 0.1f), scene.Players.Items[0]);
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
                    if (scenario.Id.StartsWith("simulation.", StringComparison.Ordinal))
                    {
                        point.Values.Add("matchState", (int)scene.GameState.MatchState);
                        point.Values.Add("deaths", scene.GameState.Deaths[1]);
                        point.Values.Add("kills", scene.GameState.Kills[0]);
                    }
                    if (hit)
                    {
                        point.Values.Add("victim.health", victim.Health);
                        point.Values.Add("shooter.headshotKills", scene.GameState.HeadshotKills[1]);
                        point.Values.Add("victim.deaths", scene.GameState.Deaths[0]);
                        Vector("victim.position4096", victim.Position);
                    }
                    if (alternate)
                    {
                        point.Values.Add("alt", actor.IsAltForm ? 1 : 0);
                        point.Values.Add("morphing", actor.IsMorphing ? 1 : 0);
                        point.Values.Add("unmorphing", actor.IsUnmorphing ? 1 : 0);
                        point.Values.Add("flags1", (uint)actor.Flags1); point.Values.Add("flags2", (uint)actor.Flags2);
                        point.Values.Add("boostDamage", actor.ModBoostDamage);
                        point.Values.Add("deaths", scene.GameState.Deaths[1]);
                        int bombs = 0;
                        foreach (var entity in scene.Entities)
                            if (entity is BombEntity bomb && bomb.Owner == actor)
                            {
                                if (bombs >= 6) throw new InvalidOperationException("F2 bomb capture budget exceeded.");
                                string prefix = "bomb." + bombs++;
                                point.Values.Add(prefix + ".entity", bomb.Id); point.Values.Add(prefix + ".type", (int)bomb.BombType);
                                point.Values.Add(prefix + ".countdown", bomb.Countdown); point.Values.Add(prefix + ".damage", bomb.Damage);
                                Vector(prefix + ".position4096", bomb.Position);
                            }
                        point.Values.Add("bombs", bombs);
                        if (hunter == Hunter.Weavel)
                        {
                            point.Values.Add("turretHealth", actor.Halfturret.Health);
                            Vector("turret.position4096", actor.Halfturret.Position);
                        }
                    }
                    if (tick % scenario.CaptureInterval == 0) output.Add(point);
                }
            }
            if (tick != scenario.Ticks || Render.FrameTiming.Stalls != 0 || Render.FrameTiming.DroppedSteps != 0)
                throw new InvalidOperationException("F2 clock failed to deliver the scenario ticks.");
            if (scenario.Id.StartsWith("movement.", StringComparison.Ordinal) && output.TrueForAll(p => p.Values["position4096.x"] == output[0].Values["position4096.x"]
                && p.Values["position4096.y"] == output[0].Values["position4096.y"] && p.Values["position4096.z"] == output[0].Values["position4096.z"]))
                throw new InvalidOperationException("F2 movement probe did not move its actor.");
            if (weapon && !output.Exists(p => p.Values["projectiles"] > 0))
                throw new InvalidOperationException("F2 firing probe did not create a projectile.");
            if (hit && !output.Exists(p => p.Values["victim.health"] < (headshot ? 99 : 999)))
                throw new InvalidOperationException("F2 aimed projectile probe did not damage its victim.");
            if (splash)
            {
                var firstHit = output.Find(p => p.Values["victim.health"] < 999)!;
                if (!firstHit.Values.TryGetValue("projectile.0.splash4096", out long splashDamage)
                    || (999 - firstHit.Values["victim.health"]) * 4096 != splashDamage
                    || splashDamage == firstHit.Values["projectile.0.damage4096"])
                    throw new InvalidOperationException("F2 floor impact did not apply the distinct splash damage value.");
            }
            if (headshot && !output.Exists(p => p.Values["shooter.headshotKills"] > 0))
                throw new InvalidOperationException("F2 aimed headshot did not produce a classified headshot kill.");
            if (scenario.Id == "simulation.respawn")
            {
                int death = output.FindIndex(p => p.Values["health"] == 0);
                if (death < 0 || !output.GetRange(death + 1, output.Count - death - 1).Exists(p => p.Values["health"] > 0)
                    || output[^1].Values["deaths"] != 1)
                    throw new InvalidOperationException("F2 respawn probe did not observe one death and a subsequent live player.");
            }
            if (scenario.Id == "simulation.match-transition" && output.TrueForAll(p => p.Values["matchState"] == output[0].Values["matchState"]))
                throw new InvalidOperationException("F2 match timer did not cause a state transition.");
            if (alternate)
            {
                if (!output.Exists(p => p.Tick < 301 && p.Values["alt"] == 1)
                    || !output.Exists(p => p.Tick is >= 301 and <= 420 && p.Values["alt"] == 0)
                    || !output.Exists(p => p.Tick is >= 421 and <= 540 && p.Values["alt"] == 1)
                    || !output.Exists(p => p.Tick > 540 && p.Values["health"] == 0)
                    || output[^1].Values["health"] == 0 || output[^1].Values["alt"] != 0)
                    throw new InvalidOperationException($"F2 alt lifecycle incomplete: {hunter}; "
                        + $"firstAlt={output.Exists(p => p.Tick < 301 && p.Values["alt"] == 1)}, "
                        + $"biped={output.Exists(p => p.Tick is >= 301 and <= 420 && p.Values["alt"] == 0)}, "
                        + $"secondAlt={output.Exists(p => p.Tick is >= 421 and <= 540 && p.Values["alt"] == 1)}, "
                        + $"dead={output.Exists(p => p.Tick > 540 && p.Values["health"] == 0)}, "
                        + $"health420={output.Find(p => p.Tick == 420)!.Values["health"]}, finalHealth={output[^1].Values["health"]}, finalAlt={output[^1].Values["alt"]}.");
                bool attack = output.Exists(p => p.Tick is >= 61 and <= 300 && (p.Values["bombs"] > 0
                    || (p.Values["flags2"] & (uint)PlayerFlags2.AltAttack) != 0));
                if (!attack) throw new InvalidOperationException("F2 alt probe did not observe its attack state or bomb.");
                var firstMotion = output.Find(p => p.Tick >= 62)!;
                if (!output.Exists(p => p.Tick is >= 61 and <= 300 && (p.Values["position4096.x"] != firstMotion.Values["position4096.x"]
                    || p.Values["position4096.z"] != firstMotion.Values["position4096.z"])))
                    throw new InvalidOperationException("F2 alternate form did not move.");
                if (hunter == Hunter.Samus && !output.Exists(p => p.Values["boostDamage"] > 0))
                    throw new InvalidOperationException("F2 Samus did not release a charged boost.");
                if (hunter == Hunter.Weavel && !output.Exists(p => p.Values["turretHealth"] > 0))
                    throw new InvalidOperationException("F2 Weavel did not create a live turret.");
            }
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
    private static void DriveAlt(PlayerEntity actor, PlayerEntity killer, Vector3 origin, int tick)
    {
        bool attackWasDown = actor.Controls.AltAttack.IsDown;
        bool boostWasDown = actor.Controls.Boost.IsDown;
        if (tick is 301 or 421) actor.ModPlaceAt(origin);
        if (tick is >= 61 and <= 300)
        {
            NetTestScript.WalkForward(actor);
            bool forward = tick % 60 < 30;
            actor.Controls.MoveUp.IsDown = forward; actor.Controls.MoveDown.IsDown = !forward;
            actor.Controls.RollUp.IsDown = forward; actor.Controls.RollDown.IsDown = !forward;
        }
        else NetTestScript.Rest(actor, wantBiped: tick is >= 301 and <= 420 || tick > 540);
        if (tick <= 60 || tick is >= 421 and <= 540)
        {
            bool morph = !actor.IsAltForm && !actor.IsMorphing && !actor.IsUnmorphing && tick % 20 == 1;
            actor.Controls.Morph.IsDown = morph; actor.Controls.Morph.IsPressed = morph;
        }
        bool attack = tick is >= 61 and <= 300 && (actor.Hunter == Hunter.Noxus ? tick % 120 < 90 : tick % 45 < 6);
        actor.Controls.AltAttack.IsDown = attack; actor.Controls.AltAttack.IsPressed = attack && !attackWasDown;
        actor.Controls.AltAttack.IsReleased = !attack && attackWasDown;
        bool boost = tick is >= 61 and <= 300 && tick % 60 < 25;
        actor.Controls.Boost.IsDown = boost; actor.Controls.Boost.IsPressed = boost && !boostWasDown;
        actor.Controls.Boost.IsReleased = !boost && boostWasDown;
        if (actor.Controls.Morph.IsDown || attack || boost) actor.ModNoteInput();
        if (tick == 541) actor.TakeDamage(2000, DamageFlags.IgnoreInvuln, null, killer);
    }

}
