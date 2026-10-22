using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real offline native events, CPU/headless Roam input seam. No network role or movement replay is installed.
internal static class MomentumSpecialSceneTests
{
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private sealed record Result(string Hunter, string Scenario, int Ticks, int SuspendedTicks, int ResumedTicks, float[] VerticalSpeeds,
        float[] Heights, float Displacement, int ContactOverflows, int RecoveryFallbacks, int BombsObserved, float[]? ImmediateVelocity, int IncomingOverCapSamples, int NeutralAirRetentionSamples, int TimedAccelerationEvents);

    private sealed class World : IDisposable
    {
        internal Scene Scene { get; }
        internal PlayerEntity Player => Scene.Players.Main;
        private readonly KeyboardState _keyboard;
        private readonly Action<KeyboardState, Keys, bool> _set;
        private readonly bool _momentum;
        internal readonly List<float> Vertical = new(), Heights = new();
        internal int Suspended, Resumed, OverCapSamples, AirRetentionSamples;
        internal World(Hunter hunter, bool momentum)
        {
            _momentum = momentum;
            Scene = MomentumSceneTests.Load(hunter, momentum, out _keyboard);
            _set = typeof(KeyboardState).GetMethod("SetKeyState", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            Player.Controls.Morph.Type = ButtonType.Key; Player.Controls.Morph.Key = Keys.M;
            Player.Controls.AltAttack.Type = ButtonType.Key; Player.Controls.AltAttack.Key = Keys.B;
        }
        internal void Key(Keys key, bool down) => _set(_keyboard, key, down);
        internal void Step(int count = 1, bool requireSuspension = false)
        {
            for (int i = 0; i < count; i++)
            {
                long before = Scene.DeveloperMomentumSteps;
                Scene.OnSimulationFrame();
                long steps = Scene.DeveloperMomentumSteps - before;
                Require(!NetSession.Active && !DemoPlayback.IsActive, "offline event remains outside network/replay ownership");
                Require(steps is >= 0 and <= 1, "one solver at most per native event tick");
                if (_momentum)
                {
                    if (requireSuspension) Require(steps == 0, "native special owner suspends horizontal solver");
                    if (steps == 0) Suspended++; else
                    {
                        Resumed++;
                        var sample = Scene.LastMomentumStep!.Value;
                        if (sample.IncomingOverCap) OverCapSamples++;
                        if (sample.IncomingOverCap && !sample.Input.Grounded && sample.Input.MoveAxes == Vector2.Zero)
                        {
                            Require(MathF.Abs(sample.NativeVelocityBefore.X - sample.NativeVelocityAfter.X) <= .00001f
                                && MathF.Abs(sample.NativeVelocityBefore.Z - sample.NativeVelocityAfter.Z) <= .00001f,
                                "neutral air steering retains actual incoming external overspeed");
                            AirRetentionSamples++;
                        }
                    }
                }
                Vector3 p = Player.Position, v = Player.Speed;
                Require(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
                    && float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "native event preserves finite collision body");
                Vertical.Add(v.Y); Heights.Add(p.Y);
            }
        }
        internal void WaitEligible(int maximum = 600)
        {
            for (int i = 0; i < maximum && (!Player.ModIsInPlay || Player.Health == 0 || Player.IsAltForm
                || Player.IsMorphing || Player.IsUnmorphing || _momentum && !Player.ModMomentumEligible); i++) Step();
            Require(Player.ModIsInPlay && Player.Health > 0 && !Player.IsAltForm && !Player.IsMorphing && !Player.IsUnmorphing,
                "native special event returns to a living settled biped");
            if (_momentum)
            {
                Require(Player.ModMomentumEligible, "ordinary local biped eligibility resumes");
                long before = Scene.DeveloperMomentumSteps; Step();
                Require(Scene.DeveloperMomentumSteps == before + 1, "ordinary horizontal solver actually resumes");
            }
            else Step();
        }
        public void Dispose() => MomentumSceneTests.Release(Scene);
    }

    private static IEnumerable<BombEntity> Bombs(Scene scene)
    { foreach (BombEntity bomb in scene.GetBombEntities()) yield return bomb; }

    internal static int Run(string? outputPath)
    {
        bool previousWatching = WorldEvents.Watching;
        try
        {
            _checks = 0; WorldEvents.Watching = true; WorldEvents.Reset();
            Require(!NetSession.Active && !DemoPlayback.IsActive, "fresh offline process");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            var results = new List<object>();
            foreach (Hunter hunter in Hunters)
            foreach (string scenario in new[] { "teleport", "pad", "directed-damage", "large-directed-endpoint-stress", "form-cycle", "prime-native", "death-respawn", "q3-push", "q3-teleport" })
            {
                Result native = Capture(hunter, false, scenario), momentum = Capture(hunter, true, scenario);
                Require(native.Ticks == momentum.Ticks, scenario + " paired native event tick count");
                if (native.ImmediateVelocity != null) Require(native.ImmediateVelocity.SequenceEqual(momentum.ImmediateVelocity!), "instantaneous native XYZ impulse parity before any subsequent steering");
                if (scenario == "large-directed-endpoint-stress")
                    Require(momentum.IncomingOverCapSamples > 0 && momentum.NeutralAirRetentionSamples > 0, "real directed endpoint installs retained over-budget native impulse");
                for (int i = 0; i < native.Ticks; i++)
                {
                    Require(MathF.Abs(native.VerticalSpeeds[i] - momentum.VerticalSpeeds[i]) <= .00001f, $"{hunter}/{scenario} native vertical speed parity tick{i}");
                    Require(MathF.Abs(native.Heights[i] - momentum.Heights[i]) <= .00001f, $"{hunter}/{scenario} native height parity tick{i}");
                }
                results.Add(new { native, momentum });
                Console.WriteLine($"MOMENTUM SPECIAL SCENE PASS {hunter}/{scenario}");
            }
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Sylux })
            {
                Result native = Capture(hunter, false, "native-bomb"), momentum = Capture(hunter, true, "native-bomb");
                Require(native.BombsObserved > 0 && momentum.BombsObserved > 0, "native supported Hunter bomb placed through actual controls");
                Require(native.Ticks == momentum.Ticks, "paired native bomb event tick count");
                for (int i = 0; i < native.Ticks; i++)
                    Require(MathF.Abs(native.VerticalSpeeds[i] - momentum.VerticalSpeeds[i]) <= .00001f,
                        $"{hunter} native bomb vertical impulse parity tick{i}");
                results.Add(new { native, momentum });
                Console.WriteLine($"MOMENTUM SPECIAL SCENE PASS {hunter}/native-bomb");
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "asset-backed offline CPU scene simulation/input/native event endpoints; headless Roam; no GL, physical devices, network, replay or projectile-hit acceptance",
                    instantaneousImpulsePolicy = "directed TakeDamage adds native Speed immediately without an acceleration timer; immediate XYZ must match Classic; subsequent ordinary horizontal steering/friction may differ; large .5,.1,0 endpoint is labeled synthetic stress, not projectile acceptance",
                    conditions = "MP3 PROVING GROUND; seeded helper native spawn/intro skip; 60Hz; entity geometry and initial placement are fixture setup; no per-tick position/speed overrides; Prime earned by native lethal damage on initialized support victim",
                    remaining = "slippery surfaces/custom geometry tested separately; full custom-map Q3 loading, physical projectiles, actual camera/HUD/device/bot acceptance remain outside this suite", results }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM SPECIAL SCENE PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM SPECIAL SCENE FAIL " + ex); return 1; }
        finally { WorldEvents.Watching = previousWatching; }
    }

    private static Result Capture(Hunter hunter, bool momentum, string scenario)
    {
        using var world = new World(hunter, momentum);
        var scene = world.Scene; var player = world.Player;
        Vector3 origin = player.Position; int bombsObserved = 0, timedAccelerationEvents = 0; float[]? immediateVelocity = null;
        switch (scenario)
        {
            case "teleport":
            {
                Vector3 target = origin + Vector3.UnitX * 8;
                var header = new EntityDataHeader((ushort)EntityType.Teleporter, 30000, origin, Vector3.UnitY, Vector3.UnitZ);
                var data = new TeleporterEntityData(header, 0, 0, 8, 1, 1, null, target.ToVector3Fx(), null);
                var entity = new TeleporterEntity(data, "", scene, forceMultiplayer: true); scene.AddEntity(entity);
                player.Reposition(origin - Vector3.UnitX * 8, Vector3.UnitZ, player.NodeRef); world.Step(2);
                int before = WorldEvents.TeleportsFor(player.SlotIndex);
                player.Reposition(origin, Vector3.UnitZ, player.NodeRef); world.Step(2);
                Require(WorldEvents.TeleportsFor(player.SlotIndex) > before && (player.Position - origin).Length > 4,
                    $"actual native teleporter volume/process moves player and records event: watching={WorldEvents.Watching}, count={before}->{WorldEvents.TeleportsFor(player.SlotIndex)}, position={player.Position}, source={origin}, volume={player.Volume.SpherePosition}, entityActive={entity.Active}, initialized={entity.Initialized}");
                world.Step(90); world.WaitEligible(); break;
            }
            case "pad":
            {
                var volume = new RawCollisionVolume(Vector3.UnitY.ToVector3Fx(), new Vector3Fx(0, 0, 0), new Fixed(Fixed.ToInt(2)), new Fixed(Fixed.ToInt(3)));
                var header = new EntityDataHeader((ushort)EntityType.JumpPad, 30001, origin, Vector3.UnitY, Vector3.UnitZ);
                var data = new JumpPadEntityData(header, -1, volume, Vector3.UnitY.ToVector3Fx(), new Fixed(Fixed.ToInt(.3f)), 0, 20, 1, 0, 0, TriggerFlags.PlayerBiped);
                var pad = new JumpPadEntity(data, "", scene); scene.AddEntity(pad);
                ushort before = player.ModJumpPadAudioEventId;
                world.Step(); pad.SetActive(false);
                Require(player.Speed.Y > 0 && player.ModJumpPadAudioEventId != before, "native pad process installs launch and event sequence");
                world.Step(10, requireSuspension: true); world.Step(150); world.WaitEligible(); break;
            }
            case "q3-push":
            case "q3-teleport":
            {
                bool push = scenario == "q3-push";
                Vector3 target = origin + new Vector3(8, push ? 12 : 0, 0);
                var trigger = new MapQ3Trigger { Kind = push ? "push" : "teleport", SourceEntity = 30002,
                    Min = new[] { origin.X - .1f, origin.Y - .1f, origin.Z - .1f },
                    Max = new[] { origin.X + .1f, origin.Y + .1f, origin.Z + .1f },
                    Destination = new[] { target.X, target.Y, target.Z }, Gravity = 90, Yaw = 90, CooldownFrames = 120 };
                Q3Traversal.Bind(scene, scene.Room!.Meta.Name, new[] { trigger });
                // Fixture metadata is registered after helper warmup. Invalidate the
                // player's previously cached empty traversal list through its native
                // lifecycle reset; the next real scene tick must discover/activate it.
                player.ModResetMovementValidation();
                world.Step(requireSuspension: true);
                if (push)
                {
                    Require(player.Speed.Y > .5f, "actual Q3 push trigger installs authored ballistic launch");
                    world.Step(10, requireSuspension: true);
                }
                else Require((player.Position - origin).Length > 4, "actual Q3 teleport trigger moves collision body");
                world.Step(180); world.WaitEligible(); break;
            }
            case "directed-damage":
            case "large-directed-endpoint-stress":
            {
                int health = player.Health; Vector3 beforeImpulse = player.Speed;
                player.TakeDamage(1, DamageFlags.IgnoreInvuln, new Vector3(scenario == "directed-damage" ? .2f : .5f, .1f, 0), player);
                Require(player.Health < health && player.Speed != beforeImpulse && player.Speed.X > beforeImpulse.X, "native directed damage changes health and installs actual velocity impulse");
                immediateVelocity = new[] { player.Speed.X, player.Speed.Y, player.Speed.Z };
                world.Step(162); world.WaitEligible(); break;
            }
            case "form-cycle":
            {
                player.ModStartFormSwitch(); Require(player.IsMorphing, "native morph transition begins");
                for (int i = 0; i < 100 && player.IsMorphing; i++) world.Step(requireSuspension: true);
                Require(player.IsAltForm, "native model animation completes alt entry"); world.Step(10, requireSuspension: true);
                if (hunter == Hunter.Trace)
                {
                    world.Key(Keys.B, true); world.Step(requireSuspension: true); world.Key(Keys.B, false);
                    Require(player.Acceleration != Vector3.Zero, "actual native Trace AltAttack installs timed lunge acceleration");
                    timedAccelerationEvents++;
                    world.Step(60, requireSuspension: true);
                }
                player.ExitAltForm(); Require(player.IsUnmorphing, "native unmorph transition begins");
                for (int i = 0; i < 100 && player.IsUnmorphing; i++) world.Step(requireSuspension: true);
                world.WaitEligible(); break;
            }
            case "prime-native":
            {
                scene.GameState.Mode = GameMode.PrimeHunter;
                var victim = scene.Players.Create(Hunter.Samus, 0)
                    ?? throw new InvalidOperationException("support victim registry allocation failed");
                Require(victim.SlotIndex != player.SlotIndex, "support victim allocated through scene registry");
                victim.LoadFlags |= LoadFlags.Active | LoadFlags.Initial; scene.Players.PlayerCount++;
                scene.AddEntity(victim); victim.Spawn(origin + Vector3.UnitX * 8, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
                victim.TakeDamage(10000, DamageFlags.IgnoreInvuln, null, player);
                Require(victim.Health == 0 && player.IsPrimeHunter, "native lethal damage awards Prime Hunter ownership");
                world.Key(Keys.Space, true); world.Step(requireSuspension: true); world.Key(Keys.Space, false);
                Require(player.Speed.Y > .3f, "native Prime Hunter jump impulse remains canonical");
                world.Step(20, requireSuspension: true);
                // A real death removes Prime ownership and exercises its retirement path.
                player.TakeDamage(10000, DamageFlags.IgnoreInvuln, null, null);
                Require(player.Health == 0 && !player.IsPrimeHunter, "native death retires Prime ownership");
                world.Key(Keys.Enter, true); world.Step(240); world.Key(Keys.Enter, false); world.WaitEligible(); break;
            }
            case "death-respawn":
            {
                player.TakeDamage(10000, DamageFlags.IgnoreInvuln, null, null);
                Require(player.Health == 0, "native lethal damage enters death"); world.Step(10, requireSuspension: true);
                world.Key(Keys.Enter, true); world.Step(240); world.Key(Keys.Enter, false); world.WaitEligible(); break;
            }
            case "native-bomb":
            {
                player.ModStartFormSwitch();
                for (int i = 0; i < 100 && player.IsMorphing; i++) world.Step(requireSuspension: true);
                Require(player.IsAltForm, "supported native bomb owner reaches alt form");
                world.Key(Keys.B, true); world.Step(requireSuspension: true); world.Key(Keys.B, false);
                var placedBombs = Bombs(scene).Where(bomb => ReferenceEquals(bomb.Owner, player)).ToArray();
                bombsObserved = placedBombs.Length; int bombStart = world.Vertical.Count;
                Require(bombsObserved > 0, "actual native AltAttack control places supported Hunter bomb");
                if (hunter == Hunter.Sylux)
                {
                    // Native Lockjaw detonation uses the fourth placement press; cooldowns advance normally.
                    for (int i = 0; i < 3; i++) { world.Step(60, requireSuspension: true); world.Key(Keys.B, true); world.Step(requireSuspension: true); world.Key(Keys.B, false); }
                }
                world.Step(120, requireSuspension: true);
                Require(placedBombs.Any(bomb => bomb.Flags.TestFlag(BombFlags.Exploded)), "actual placed native bomb reaches explosion terminal");
                if (hunter == Hunter.Samus) Require(world.Vertical.Skip(bombStart).Any(y => y > .01f), "actual native Samus bomb contact produces upward bomb jump");
                player.ExitAltForm();
                for (int i = 0; i < 100 && player.IsUnmorphing; i++) world.Step(requireSuspension: true);
                world.WaitEligible(); break;
            }
        }
        Require(player.ModCollisionContactOverflows == 0 && player.ModCollisionRecoveryFallbacks == 0,
            "native special-event fixture does not exhaust collision contacts or require recovery fallback");
        return new(hunter.ToString(), scenario, world.Vertical.Count, world.Suspended, world.Resumed, world.Vertical.ToArray(), world.Heights.ToArray(),
            (player.Position - origin).Length, player.ModCollisionContactOverflows, player.ModCollisionRecoveryFallbacks, bombsObserved, immediateVelocity, world.OverCapSamples, world.AirRetentionSamples, timedAccelerationEvents);
    }
}
