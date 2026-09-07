using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>One deterministic real-content scenario per process; see docs/NETWORK_LAGCOMP_COMPARISON.md.</summary>
    internal static class LagCompScriptCheck
    {
        public record Config(string Scenario = "trace", bool Enabled = true, uint Seed = 0xA1758,
            int Ticks = 1200, int DelayTicks = 6, int JitterTicks = 2, int LossPerThousand = 30,
            string Room = "MP1 SANCTORUS", string Version = "AMHE1", bool ProjectileCatchUpEnabled = true);
        private record Delivery(int Slot, uint SampleTick, uint DeliveryTick, bool Dropped, byte[] Body);
        private record Shot(uint Tick, uint Command, byte Weapon, ushort Flags, ushort Charge,
            uint Seed, ulong Connection, uint Life, float[] Position, float[] Direction);
        private record Hit(uint Tick, uint Command, byte Weapon, ushort Flags, ushort Amount,
            ulong TargetConnection, uint TargetLife);
        private record Accepted(uint Tick, int Slot, bool Processed, uint LastProcessed, string Command);
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        public static int Run(string[] args)
        {
            if (args.Length != 4 || args[0] != "--lagcomp-script")
            {
                Console.Error.WriteLine("--lagcomp-script DATA CONFIG_JSON OUTPUT_JSON");
                return 2;
            }
            try
            {
                Config config = JsonSerializer.Deserialize<Config>(File.ReadAllText(args[2]), Json)
                    ?? throw new ArgumentException("Configuration must be a JSON object.");
                Run(args[1], config, args[3]);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Run(string data, Config config, string output)
        {
            if (config.Ticks is < 600 or > 7200 || config.DelayTicks is < 0 or > 30
                || config.JitterTicks < 0 || config.JitterTicks > config.DelayTicks
                || config.LossPerThousand is < 0 or > 300) throw new ArgumentException("Unbounded script configuration.");
            (BeamType weapon, Hunter hunter, bool charged, string expectedPolicy) = config.Scenario switch
            {
                "trace" => (BeamType.Imperialist, Hunter.Trace, false, "HistoricalTrace"),
                "travel" => (BeamType.PowerBeam, Hunter.Samus, false, "ProjectileCatchUp"),
                "homing" => (BeamType.Missile, Hunter.Samus, true, "None"),
                "continuous" => (BeamType.ShockCoil, Hunter.Sylux, false, "None"),
                "area" => (BeamType.Judicator, Hunter.Noxus, true, "None"),
                _ => throw new ArgumentException("Unknown scenario.")
            };
            ServerContent.Open(data, config.Version);
            Rng.SetRng1(config.Seed); Rng.SetRng2(config.Seed ^ 0x98AC72u);
            using var simulation = new ServerSimulation(new RotationEntry { RoomKey = config.Room, Mode = GameMode.Battle },
                lagCompEnabled: config.Enabled, projectileCatchUpEnabled: config.ProjectileCatchUpEnabled);
            ServerCombat combat = simulation.Combat;
            PlayerEntity shooter = PlayerEntity.Players[0], target = PlayerEntity.Players[1];
            PlayerEntity[] players = [shooter, target];
            using (combat.Enter(0))
            {
                shooter.ServerActivate(100, hunter, 0);
                target.ServerActivate(200, Hunter.Kanden, 1);
            }
            PlayerEntity.PlayerCount = 2;
            shooter.ModArmWeapon(weapon);
            shooter.EquipInfo.InfiniteAmmo = true;
            (Vector3 origin, Vector3 center, Vector3 right) = FindLane(simulation.Scene, shooter, target);
            int chargeTicks = charged ? shooter.EquipInfo.Weapon.FullCharge * 2 + 15 : 6;
            int period = charged ? chargeTicks + 80 : 60;
            List<Delivery> deliveries = CreateScript(config, origin, center, right, chargeTicks, period);
            string scriptHash = Hash(deliveries.SelectMany(d => BitConverter.GetBytes(d.Slot)
                .Concat(BitConverter.GetBytes(d.SampleTick)).Concat(BitConverter.GetBytes(d.DeliveryTick))
                .Concat(new[] { d.Dropped ? (byte)1 : (byte)0 }).Concat(d.Body)).ToArray());
            Delivery[] arrivals = deliveries.Where(d => !d.Dropped).OrderBy(d => d.DeliveryTick)
                .ThenBy(d => d.SampleTick).ThenBy(d => d.Slot).ToArray();
            var streams = new[] { new ServerInputStream(), new ServerInputStream() };
            var shots = new List<Shot>();
            var hits = new List<Hit>();
            var accepted = new List<Accepted>();
            var trajectory = new List<float[]>();
            var beamVariants = new HashSet<string>();
            Span<InputCommand> decoded = stackalloc InputCommand[InputBundle.Capacity];
            Span<byte> commandBytes = stackalloc byte[InputCommand.Size];
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            int nextArrival = 0;
            combat.Consume(combat.Count);
            // No RNG resets after this point: any divergent accepted shot seed is
            // a failed pair, even if the aggregate hit counts appear favorable.
            uint initialRng1 = Rng.Rng1, initialRng2 = Rng.Rng2;
            for (uint tick = 0; tick < config.Ticks; tick++)
            {
                using var scope = combat.Enter(tick);
                while (nextArrival < arrivals.Length && arrivals[nextArrival].DeliveryTick <= tick)
                {
                    Delivery arrival = arrivals[nextArrival++];
                    if (!InputBundle.TryRead(arrival.Body, decoded, out uint match, out int count) || match != 1)
                        throw new InvalidOperationException("Generated input bundle failed the production codec.");
                    streams[arrival.Slot].Receive(decoded[..count], tick);
                }
                // A prescribed path and health restoration prevent knockback,
                // death, dropped pickups or respawn choices changing future inputs.
                // This is collision-policy simulation evidence, not a duel outcome.
                Vector3 targetPosition = TargetPosition(tick, center, right);
                shooter.Reposition(origin, (center - origin).Normalized(), simulation.Scene.GetNodeRefByPosition(origin));
                target.Reposition(targetPosition, (origin - center).Normalized(), simulation.Scene.GetNodeRefByPosition(targetPosition));
                shooter.Speed = target.Speed = Vector3.Zero;
                shooter.Health = target.Health = 10000;
                for (int slot = 0; slot < 2; slot++)
                {
                    PlayerEntity player = slot == 0 ? shooter : target;
                    InputCommand command = streams[slot].Take(tick);
                    combat.SetCommand(slot, command, config.DelayTicks * (2000d / 60));
                    command.Write(commandBytes);
                    accepted.Add(new(tick, slot, streams[slot].HasProcessed, streams[slot].LastProcessed,
                        Convert.ToHexString(commandBytes)));
                    player.ApplyNetworkInput(command);
                }
                simulation.Scene.StepHeadlessFrame(advanceMatch: false);
                combat.CatchUp.Drain();
                foreach (PlayerEntity player in players)
                {
                    player.ModRepairVectors();
                    SnapshotPlayer snapshot = player.CaptureServerState();
                    combat.History.Record(tick, player, snapshot.ConnectionId, snapshot.Life);
                    if (snapshot.Health == 0 || snapshot.Life != 1) throw new InvalidOperationException("Controlled actors died or respawned.");
                }
                trajectory.Add(Vec(target.Position));
                foreach (BeamProjectileEntity beam in simulation.Scene.GetBeamProjectileEntities())
                    if (beam.Owner == shooter) beamVariants.Add($"{beam.Beam}:{beam.Flags}:homing={beam.Homing}");
                while (combat.Count > 0)
                {
                    int count = combat.CopyPending(events);
                    foreach (CombatEvent value in events[..count])
                    {
                        if (value.Kind == CombatEventKind.Shot)
                            shots.Add(new(value.Tick, value.CommandSequence, value.Weapon, (ushort)value.Flags,
                                value.ChargeLevel, value.SpreadSeed, value.Actor.ConnectionId, value.Actor.Life,
                                Vec(value.Position), Vec(value.Direction)));
                        if (value.Kind == CombatEventKind.Damage)
                            hits.Add(new(value.Tick, value.CommandSequence, value.Weapon, (ushort)value.Flags,
                                value.Amount, value.Target.ConnectionId, value.Target.Life));
                        if (value.Kind == CombatEventKind.Death) throw new InvalidOperationException("Unexpected combat death.");
                    }
                    combat.Consume(count);
                }
            }
            if (shots.Count == 0) throw new InvalidOperationException("Script produced no accepted root shots.");
            if (charged && !shots.Any(s => (s.Flags & (ushort)CombatEventFlags.Charged) != 0))
                throw new InvalidOperationException("Charged scenario produced no charged root shots.");
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Schema = 1, Evidence = "controlled real-content simulation; no UDP/WAN impairment",
                Config = config, ExpectedPolicy = expectedPolicy, ScriptHash = scriptHash,
                InitialRng1 = initialRng1, InitialRng2 = initialRng2,
                Origin = Vec(origin), TargetCenter = Vec(center), TargetAxis = Vec(right),
                HealthRestoredEachTick = 10000, MovementPrescribedEachTick = true,
                ChargeHoldTicks = chargeTicks, FirePeriod = period,
                Deliveries = deliveries.Select(d => new { d.Slot, d.SampleTick, d.DeliveryTick, d.Dropped, PayloadHash = Hash(d.Body) }),
                Accepted = accepted, Shots = shots, Hits = hits, TargetTrajectory = trajectory,
                BeamVariants = beamVariants.OrderBy(s => s).ToArray(),
                HistoryQueries = combat.History.Queries, HistoryMissing = combat.History.Missing,
                DamageEvents = hits.Count, TotalDamage = hits.Sum(h => (int)h.Amount),
                RootShots = shots.Count, CombatDropped = combat.Dropped,
                ActualLagCompEnabled = combat.LagCompEnabled,
                ActualProjectileCatchUpEnabled = combat.ProjectileCatchUpEnabled,
                combat.ShotsConsidered, combat.ShotsEligible, combat.ShotsRewound,
                combat.CatchUp.ProjectilesCaughtUp, combat.CatchUp.Steps, combat.CatchUp.QueueDrops
            }, Json));
            Console.WriteLine($"SCRIPT {config.Scenario} enabled={config.Enabled} shots={shots.Count} damage={hits.Count}/{hits.Sum(h => (int)h.Amount)} hash={scriptHash}");
        }

        private static List<Delivery> CreateScript(Config config, Vector3 origin, Vector3 center,
            Vector3 right, int hold, int period)
        {
            var result = new List<Delivery>();
            var history = new InputCommand[2, config.Ticks];
            uint random = config.Seed;
            Span<InputCommand> bundle = stackalloc InputCommand[InputBundle.Capacity];
            byte[] bytes = new byte[InputBundle.MaxSize];
            for (uint tick = 0; tick < config.Ticks; tick++)
            {
                int firingTick = (int)tick - 240;
                bool firing = firingTick >= 0 && firingTick % period < hold;
                bool pressed = firingTick >= 0 && firingTick % period == 0;
                uint viewed = tick > 6 ? tick - 6 : 0;
                Vector3 aim = (TargetPosition(viewed, center, right) - origin).Normalized();
                for (int slot = 0; slot < 2; slot++)
                {
                    history[slot, tick] = new(tick, tick, viewed, slot == 0 && firing ? InputButtons.Shoot : 0,
                        slot == 0 && pressed ? InputButtons.Shoot : 0, slot == 0 ? aim : Vector3.UnitZ,
                        InputCommand.NoWeapon);
                    int count = (int)Math.Min(tick + 1, InputBundle.Capacity);
                    for (int i = 0; i < count; i++) bundle[i] = history[slot, tick - (uint)(count - 1 - i)];
                    int length = InputBundle.Write(bytes, 1, bundle[..count]);
                    random = Next(random);
                    bool dropped = random % 1000 < config.LossPerThousand;
                    random = Next(random);
                    int jitter = (int)(random % (2 * config.JitterTicks + 1)) - config.JitterTicks;
                    result.Add(new(slot, tick, tick + (uint)(config.DelayTicks + jitter), dropped, bytes.AsSpan(0, length).ToArray()));
                }
            }
            return result;
        }

        private static Vector3 TargetPosition(uint tick, Vector3 center, Vector3 right)
        {
            float offset = ((tick % 120) <= 60 ? tick % 120 : 120 - tick % 120) / 60f * 2.4f - 1.2f;
            return center + right * offset;
        }

        private static (Vector3, Vector3, Vector3) FindLane(Scene scene, PlayerEntity shooter, PlayerEntity target)
        {
            foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities())
            {
                Vector3 center = spawn.Position;
                if (!Floor(scene, ref center, target)) continue;
                for (int bearing = 0; bearing < 64; bearing++)
                {
                    float angle = bearing * MathF.PI / 32;
                    Vector3 forward = new(MathF.Sin(angle), 0, MathF.Cos(angle));
                    Vector3 right = new(forward.Z, 0, -forward.X);
                    Vector3 origin = center + forward * 8;
                    if (!Floor(scene, ref origin, shooter)) continue;
                    bool clear = true;
                    for (int i = -4; i <= 4; i++)
                    {
                        Vector3 sample = center + right * (i * 0.3f), floor = sample;
                        CollisionResult wall = default;
                        if (!Floor(scene, ref floor, target) || MathF.Abs(floor.Y - center.Y) > 0.1f
                            || CollisionDetection.CheckBetweenPoints(origin.AddY(0.5f), sample.AddY(0.5f), TestFlags.Beams, scene, ref wall))
                        { clear = false; break; }
                    }
                    if (clear) return (origin, center, right);
                }
            }
            throw new InvalidOperationException("No grounded clear 8-unit lane with 2.4-unit lateral target path.");
        }

        private static bool Floor(Scene scene, ref Vector3 point, PlayerEntity player)
        {
            CollisionResult floor = default;
            if (!CollisionDetection.CheckBetweenPoints(point.AddY(2), point.AddY(-2), TestFlags.Players, scene, ref floor)
                || floor.Plane.Y < 0.5f) return false;
            point.Y = floor.Position.Y - Fixed.ToFloat(player.Values.MinPickupHeight) + 0.01f;
            return true;
        }
        private static uint Next(uint value) => unchecked(value * 1664525u + 1013904223u);
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static float[] Vec(Vector3 value) => new[] { value.X, value.Y, value.Z };
    }
}
