using System;
using System.Diagnostics;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    // Test-only server loadouts and infinite ammunition. Every attack still enters
    // through a real UDP peer and the normal authoritative input/weapon pipeline.
    internal static class MixedCombatSoak
    {
        internal static BeamType Weapon(int slot) => (slot / 2) switch
        { 0 => BeamType.PowerBeam, 1 => BeamType.Imperialist, 2 => BeamType.Missile, _ => BeamType.ShockCoil };

        public static int RunServer(string[] args)
        {
            if (args.Length != 7 || !int.TryParse(args[2], out int port) || port is < 1 or > 65535
                || !int.TryParse(args[3], out int seconds) || seconds is < 10 or > 300 || args[5] is not ("on" or "trace-only" or "off") || !uint.TryParse(args[6], out uint seed))
            { Console.Error.WriteLine("--mixed-soak-server DATA PORT SECONDS REPORT_JSON on|trace-only|off SEED"); return 2; }
            try { return Server(args[1], port, seconds, args[4], args[5], seed); }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static int Server(string data, int port, int seconds, string report, string mode, uint seed)
        {
            const string room = "MP1 SANCTORUS";
            ServerContent.Open(data, "AMHE1");
            Rng.SetRng1(seed); Rng.SetRng2(unchecked(seed + 1));
            using var simulation = new ServerSimulation(new RotationEntry { RoomKey = room, Mode = GameMode.Battle, PointGoal = 0 }, lagCompEnabled: mode != "off", projectileCatchUpEnabled: mode == "on");
            using var transport = new NetTransport(port);
            using var process = Process.GetCurrentProcess();
            var network = new ServerNetwork(transport, room, GameMode.Battle);
            var scheduler = new FixedTickScheduler();
            var world = new WorldStateCapture();
            var lives = new uint[8];
            var spots = new Vector3[8];
            var shots = new long[9, 4]; // charged and affinity bits; server journal only
            var rootMechanics = new long[5];
            var compensationModes = new long[4];
            long unresolvedMechanics = 0, homingRootTargets = 0;
            var activeMechanics = new long[4]; // traveling, trace, actual homing target, continuous
            var durations = new double[40000];
            int durationCount = 0, maxDue = 0, minimumPeers = 8;
            long damage = 0, deaths = 0, reliableOverflow = 0;
            long caughtAt = 0, stepsAt = 0, collisionsAt = 0;
            uint tick = 0, snapshot = 0, revision = 0;
            long started = Stopwatch.GetTimestamp(), readyAt = 0, measureAt = 0;
            long allocatedAt = 0, queueAt = 0, droppedAt = 0, catchUpAt = 0, cpuAt = 0, rxAt = 0, txAt = 0;
            int gc0 = 0, gc1 = 0, gc2 = 0;
            bool arranged = false, finished = false, success = false;
            Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            Console.WriteLine($"MIXEDSOAK listening on UDP {port}; fixed loadouts, infinite ammo, normal deaths/respawns; no rendering");
            while (!finished || Stopwatch.GetElapsedTime(measureAt).TotalSeconds < seconds + 10)
            {
                int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                if (due == 0) { scheduler.Wait(); continue; }
                for (int step = 0; step < due; step++)
                {
                    long start = Stopwatch.GetTimestamp();
                    bool measuring = measureAt != 0 && !finished;
                    network.Poll(tick);
                    int playing = 0;
                    foreach (ServerPeer? peer in network.Peers)
                        if (peer?.Connection.State == NetConnectionState.Playing) playing++;
                    if (playing == 8 && readyAt == 0) readyAt = start;
                    if (readyAt == 0 && Stopwatch.GetElapsedTime(started).TotalSeconds > 30)
                        throw new InvalidOperationException("Eight peers did not join.");
                    if (!arranged && readyAt != 0 && Stopwatch.GetElapsedTime(readyAt).TotalSeconds > 2)
                    { Arrange(simulation.Scene, spots); arranged = true; }
                    if (arranged)
                    {
                        foreach (PlayerEntity player in simulation.Scene.GetPlayerEntities())
                        {
                            CombatActor actor = player.ServerCombatIdentity;
                            if (!actor.IsValid || player.Health == 0 || lives[actor.Slot] == actor.Life) continue;
                            player.AvailableWeapons[Weapon(actor.Slot)] = true;
                            player.EquipInfo.SetAmmo?.Invoke(10000); // Existing ammo setter also satisfies normal weapon-selection validation.
                            player.EquipInfo.InfiniteAmmo = true;
                            player.IgnoreItemPickups = true;
                            lives[actor.Slot] = actor.Life;
                            Vector3 position = spots[actor.Slot];
                            player.Teleport(position, (spots[actor.Slot ^ 1] - position).Normalized(), simulation.Scene.GetNodeRefByPosition(position));
                        }
                    }
                    simulation.Step(network, tick);
                    if (tick % 2 == 0)
                    {
                        foreach (ServerPeer? peer in network.Peers)
                        {
                            if (peer?.Connection.State != NetConnectionState.Playing) continue;
                            var state = new SnapshotPacket(tick, snapshot, network.MatchId, peer.Inputs.LastProcessed,
                                peer.Inputs.HasProcessed, Rng.Rng1, Rng.Rng2);
                            int length = state.Write(packet, simulation.States);
                            peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
                        }
                        snapshot++;
                    }
                    if (tick % 12 == 0 && network.Count > 0)
                    {
                        world.Capture(simulation.Scene, network.MatchId, revision++, tick);
                        for (int batch = 0; batch < world.BatchCount; batch++)
                        {
                            int length = world.WriteBatch(packet, batch);
                            foreach (ServerPeer? peer in network.Peers) if (peer != null) network.SendWorld(peer, packet[..length]);
                        }
                    }
                    for (int batch = 0; batch < 8; batch++)
                    {
                        int count = simulation.Combat.CopyPending(events);
                        if (count == 0) break;
                        if (measuring) foreach (CombatEvent value in events[..count])
                        {
                            if (value.Kind == CombatEventKind.Shot && value.Weapon < 9)
                            {
                                int variant = (value.Flags.TestFlag(CombatEventFlags.Charged) ? 1 : 0)
                                    | (value.Flags.TestFlag(CombatEventFlags.Affinity) ? 2 : 0);
                                shots[value.Weapon, variant]++;
                                bool found = false;
                                foreach (BeamProjectileEntity beam in PlayerEntity.Players[value.Actor.Slot].EquipInfo.Beams)
                                {
                                    CombatShot shot = beam.CombatShot;
                                    if (shot.Actor != value.Actor || shot.CommandSequence != value.CommandSequence
                                        || shot.ProcessedServerTick != value.Tick || (byte)beam.Mechanics.Beam != value.Weapon) continue;
                                    BeamMechanics mechanics = beam.Mechanics;
                                    int kind = mechanics.Continuous ? 3 : mechanics.InstantArea ? 4 : mechanics.Homing > 0 ? 2
                                        : LagCompensationPolicy.GetMode(mechanics) == LagCompensationMode.HistoricalTrace ? 1 : 0;
                                    rootMechanics[kind]++;
                                    compensationModes[(int)shot.Mode]++;
                                    if (mechanics.Homing > 0 && !mechanics.Continuous && beam.Target != null) homingRootTargets++;
                                    found = true;
                                    break;
                                }
                                if (!found) unresolvedMechanics++;
                            }
                            if (value.Kind == CombatEventKind.Damage) damage++;
                            if (value.Kind == CombatEventKind.Death) deaths++;
                        }
                        int length = CombatEventBatch.Write(packet, events[..count]);
                        reliableOverflow += SendCombatBatch(network, packet[..length]);
                        simulation.Combat.Consume(count);
                    }
                    if (measuring)
                    {
                        foreach (BeamProjectileEntity beam in simulation.Scene.GetBeamProjectileEntities())
                        {
                            if (beam.Lifespan <= 0 || beam.Flags.TestFlag(BeamFlags.Collided)) continue;
                            if (beam.Flags.TestFlag(BeamFlags.Continuous)) activeMechanics[3]++;
                            else if (beam.Flags.TestFlag(BeamFlags.Homing) && beam.Target != null) activeMechanics[2]++;
                            else if (beam.Beam == BeamType.Imperialist) activeMechanics[1]++;
                            else activeMechanics[0]++;
                        }
                        maxDue = Math.Max(maxDue, due);
                        minimumPeers = Math.Min(minimumPeers, playing);
                        if (durationCount == durations.Length) throw new InvalidOperationException("Tick sample capacity exceeded.");
                        durations[durationCount++] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    }
                    tick++;
                    if (measureAt == 0 && arranged && Stopwatch.GetElapsedTime(readyAt).TotalSeconds >= 5)
                    {
                        measureAt = Stopwatch.GetTimestamp(); allocatedAt = GC.GetTotalAllocatedBytes(false);
                        cpuAt = process.TotalProcessorTime.Ticks; queueAt = transport.Metrics.QueueDrops;
                        droppedAt = scheduler.DroppedTicks; catchUpAt = scheduler.CatchUpTicks;
                        rxAt = transport.Metrics.BytesReceived; txAt = transport.Metrics.BytesSent;
                        gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
                        caughtAt = simulation.Combat.CatchUp.ProjectilesCaughtUp;
                        stepsAt = simulation.Combat.CatchUp.Steps;
                        collisionsAt = simulation.Combat.CatchUp.Collisions;
                        Console.WriteLine("MIXEDSOAK measurement started");
                    }
                    if (!finished && measureAt != 0 && Stopwatch.GetElapsedTime(measureAt).TotalSeconds >= seconds)
                    {
                        double wall = Stopwatch.GetElapsedTime(measureAt).TotalSeconds;
                        long allocated = GC.GetTotalAllocatedBytes(false) - allocatedAt;
                        double cpu = (process.TotalProcessorTime.Ticks - cpuAt) / (double)TimeSpan.TicksPerSecond;
                        int gen0 = GC.CollectionCount(0) - gc0, gen1 = GC.CollectionCount(1) - gc1, gen2 = GC.CollectionCount(2) - gc2;
                        long[] variants = new long[36];
                        for (int w = 0; w < 9; w++) for (int v = 0; v < 4; v++) variants[w * 4 + v] = shots[w, v];
                        Array.Sort(durations, 0, durationCount);
                        long[] modes = [rootMechanics[0], rootMechanics[1], rootMechanics[2], rootMechanics[3]];
                        bool pass = minimumPeers == 8 && scheduler.DroppedTicks == droppedAt
                            && transport.Metrics.QueueDrops == queueAt && reliableOverflow == 0 && simulation.Combat.Dropped == 0
                            && Array.TrueForAll(modes, count => count >= Math.Max(1, seconds / 10))
                            && unresolvedMechanics == 0 && simulation.Combat.CatchUp.QueueDrops == 0 && simulation.Combat.CatchUp.Pending == 0
                            && simulation.Combat.CatchUp.MaxSteps <= LagCompensationPolicy.MaxProjectileFastForwardTicks
                            && (activeMechanics[2] > 0 || homingRootTargets > 0) && activeMechanics[3] > 0 && maxDue <= FixedTickScheduler.MaxCatchUp;
                        var result = new { passed = pass, mode, seed, lagCompensation = simulation.Combat.LagCompEnabled, projectileCatchUp = simulation.Combat.ProjectileCatchUpEnabled, seconds = wall, ticks = durationCount, minimumPeers,
                            conditions = "fixed server loadouts; infinite ammo; normal inputs, damage, death and respawn; no rendering",
                            modeNames = new[] { "travelingProjectile", "historicalTrace", "homingProjectile", "continuousBeam" },
                            rootShots = modes, unresolvedMechanics, homingRootTargets,
                            compensationModeNames = Enum.GetNames<LagCompensationMode>(), compensationModeRootShots = compensationModes,
                            weaponVariantLayout = "index = 4 * BeamType + variant; variant bit 0 charged, bit 1 affinity",
                            weaponVariantRootShots = variants, activeBeamFrameObservations = activeMechanics,
                            damage, deaths, allocatedBytes = allocated, allocatedBytesPerTick = allocated / (double)durationCount,
                            cpuSeconds = cpu, cpuCores = cpu / wall, gen0, gen1, gen2,
                            tickP50Ms = durations[(durationCount - 1) / 2], tickP95Ms = durations[(durationCount - 1) * 95 / 100],
                            tickP99Ms = durations[(durationCount - 1) * 99 / 100], tickMaxMs = durations[durationCount - 1],
                            droppedTicks = scheduler.DroppedTicks - droppedAt, catchUpTicks = scheduler.CatchUpTicks - catchUpAt, maxDue,
                            queueDrops = transport.Metrics.QueueDrops - queueAt, reliableOverflow, combatDrops = simulation.Combat.Dropped,
                            projectilesCaughtUp = simulation.Combat.CatchUp.ProjectilesCaughtUp - caughtAt,
                            projectileCatchUpSteps = simulation.Combat.CatchUp.Steps - stepsAt, projectileCatchUpMaxSteps = simulation.Combat.CatchUp.MaxSteps,
                            projectileCatchUpCollisions = simulation.Combat.CatchUp.Collisions - collisionsAt, projectileCatchUpQueueDrops = simulation.Combat.CatchUp.QueueDrops,
                            projectileCatchUpPending = simulation.Combat.CatchUp.Pending,
                            receivedBytes = transport.Metrics.BytesReceived - rxAt, sentBytes = transport.Metrics.BytesSent - txAt };
                        System.IO.File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                        Console.WriteLine("MIXEDSOAK result=" + (pass ? "PASS" : "FAIL"));
                        success = pass;
                        finished = true;
                    }
                }
            }
            return success ? 0 : 1;
        }

        // Match AuthoritativeServer's bounded per-peer admission policy. A refusal
        // disconnects that peer; retrying the shared batch would duplicate healthy peers' events.
        internal static int SendCombatBatch(ServerNetwork network, ReadOnlySpan<byte> payload)
        {
            int refused = 0;
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.Combat, payload))
                {
                    refused++;
                    Console.Error.WriteLine($"MIXEDSOAK slot {peer.Slot} disconnected: reliable queue exhausted.");
                    network.Remove(peer.Slot);
                }
            }
            return refused;
        }

        private static void Arrange(Scene scene, Vector3[] positions)
        {
            foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities())
            {
                bool valid = true;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * MathF.PI / 4;
                    Vector3 spot = spawn.Position + new Vector3(MathF.Sin(angle) * 3, 0, MathF.Cos(angle) * 3);
                    CollisionResult floor = default;
                    if (!CollisionDetection.CheckBetweenPoints(spot.AddY(3), spot.AddY(-3), TestFlags.Players, scene, ref floor)
                        || floor.Plane.Y < 0.5f) { valid = false; break; }
                    spot.Y = floor.Position.Y - Fixed.ToFloat(PlayerEntity.Players[i].Values.MinPickupHeight) + 0.01f;
                    CollisionResult wall = default;
                    if (CollisionDetection.CheckBetweenPoints(spawn.Position.AddY(0.5f), spot.AddY(0.5f), TestFlags.Beams, scene, ref wall))
                    { valid = false; break; }
                    positions[i] = spot;
                }
                if (valid) return;
            }
            throw new InvalidOperationException("No grounded clear eight-player arena.");
        }
    }
}
