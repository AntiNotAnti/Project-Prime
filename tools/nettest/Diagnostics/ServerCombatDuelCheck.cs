using System;
using System.Diagnostics;
using System.Net;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Two real UDP clients; server fixture arranges an arena, then only normal input and collision decide combat.</summary>
    public static class ServerCombatDuelCheck
    {
        public static int Run(string data, string version, string room, int seconds = 20, BeamType weapon = BeamType.Imperialist)
        {
            if (seconds is < 10 or > 120 || weapon is not (BeamType.Imperialist or BeamType.Judicator
                or BeamType.Magmaul or BeamType.VoltDriver))
            {
                Console.Error.WriteLine("Combat duel requires 10 to 120 seconds and Imperialist, Judicator, Magmaul or VoltDriver.");
                return 2;
            }
            ServerContent.Open(data, version);
            using var simulation = new ServerSimulation(new RotationEntry { RoomKey = room, Mode = GameMode.Battle, PointGoal = 0 });
            using var serverTransport = new NetTransport(0);
            var network = new ServerNetwork(serverTransport, simulation.Scene.Match.Rules);
            using var shooterTransport = new NetTransport(0);
            using var targetTransport = new NetTransport(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort);
            Hunter hunter = weapon switch
            {
                BeamType.Judicator => Hunter.Noxus, BeamType.Magmaul => Hunter.Spire,
                BeamType.VoltDriver => Hunter.Kanden, _ => Hunter.Trace
            };
            using var shooterClient = new NetClient(shooterTransport, endpoint, "DUEL-SHOOTER", hunter);
            using var targetClient = new NetClient(targetTransport, endpoint, "DUEL-TARGET", Hunter.Kanden);
            NetClient[] clients = [shooterClient, targetClient];
            var histories = new InputCommand[2, InputBundle.Capacity];
            uint[] sequences = new uint[2];
            long[,] delivered = new long[2, 7];
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            Span<InputCommand> bundle = stackalloc InputCommand[InputBundle.Capacity];
            Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
            uint tick = 0, snapshot = 0;
            uint? arrangedAt = null;
            uint? readyAt = null;
            int resolvedDamage = 0, resolvedDeaths = 0, missDamage = 0, occludedShots = 0, afflictions = 0;
            Vector3 clearSpot = default;
            bool movedToClear = false;
            var timer = Stopwatch.StartNew();
            var scheduler = new FixedTickScheduler();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                if (scheduler.TakeDue(Stopwatch.GetTimestamp()) == 0) { scheduler.Wait(); continue; }
                network.Poll(tick);
                simulation.Step(network, tick);
                if (shooterClient.State == NetConnectionState.Playing && targetClient.State == NetConnectionState.Playing)
                {
                    readyAt ??= tick;
                    if (!arrangedAt.HasValue && unchecked(tick - readyAt.Value) > 180)
                    {
                        PlayerEntity shooter = PlayerEntity.Players[shooterClient.Accepted.Slot];
                        PlayerEntity target = PlayerEntity.Players[targetClient.Accepted.Slot];
                        Vector3 forward = default, spot = default;
                        bool found = false;
                        for (int bearing = 0; bearing < 32; bearing++)
                        {
                            float angle = bearing * MathF.PI / 16;
                            forward = new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle));
                            spot = target.Position + forward * 2.5f;
                            CollisionResult floor = default;
                            if (!CollisionDetection.CheckBetweenPoints(spot.AddY(1), spot.AddY(-2),
                                TestFlags.Players, simulation.Scene, ref floor) || floor.Plane.Y < 0.5f) continue;
                            spot.Y = floor.Position.Y - Fixed.ToFloat(shooter.Values.MinPickupHeight) + 0.01f;
                            CollisionResult collision = default;
                            if (CollisionDetection.CheckBetweenPoints(target.Position.AddY(0.5f), spot.AddY(0.5f),
                                TestFlags.Beams, simulation.Scene, ref collision)) continue;
                            found = true; break;
                        }
                        if (!found) throw new ProgramException("No clear grounded duel position near the existing spawn.");
                        clearSpot = spot;
                        bool foundOcclusion = false;
                        foreach (PlayerSpawnEntity spawn in simulation.Scene.GetPlayerSpawnEntities())
                        {
                            Vector3 candidate = spawn.Position;
                            float distance = (candidate - target.Position).Length;
                            if (distance < 8 || distance > 100) continue;
                            CollisionResult floor = default;
                            if (!CollisionDetection.CheckBetweenPoints(candidate.AddY(2), candidate.AddY(-2),
                                TestFlags.Players, simulation.Scene, ref floor) || floor.Plane.Y < 0.5f) continue;
                            candidate.Y = floor.Position.Y - Fixed.ToFloat(shooter.Values.MinPickupHeight) + 0.01f;
                            CollisionResult wall = default;
                            if (!CollisionDetection.CheckBetweenPoints(candidate.AddY(0.5f), target.Position.AddY(0.5f),
                                TestFlags.Beams, simulation.Scene, ref wall)) continue;
                            spot = candidate; foundOcclusion = true; break;
                        }
                        if (!foundOcclusion) throw new ProgramException("No wall-occluded grounded spawn pair for duel check.");
                        shooter.Teleport(spot, (target.Position - spot).Normalized(), simulation.Scene.GetNodeRefByPosition(spot));
                        shooter.ModArmWeapon(weapon);
                        Console.WriteLine($"[combatduel] weapon={weapon} hunter={shooter.Hunter} minCharge={shooter.EquipInfo.Weapon.MinCharge * 2} fullCharge={shooter.EquipInfo.Weapon.FullCharge * 2} affinityFlags={shooter.EquipInfo.Weapon.Afflictions[1]}");
                        arrangedAt = tick;
                        Console.WriteLine($"[combatduel] arena={room} occluded={spot} clear={clearSpot} target={target.Position} geometry=PASS");
                    }
                    if (arrangedAt.HasValue && !movedToClear && unchecked(tick - arrangedAt.Value) >= 180)
                    {
                        PlayerEntity shooter = PlayerEntity.Players[shooterClient.Accepted.Slot];
                        PlayerEntity target = PlayerEntity.Players[targetClient.Accepted.Slot];
                        shooter.Teleport(clearSpot, (target.Position - clearSpot).Normalized(),
                            simulation.Scene.GetNodeRefByPosition(clearSpot));
                        movedToClear = true;
                    }
                }
                while (simulation.Combat.Count > 0)
                {
                    int count = simulation.Combat.CopyPending(events);
                    foreach (CombatEvent value in events[..count])
                    {
                        if (value.Kind == CombatEventKind.Shot && arrangedAt.HasValue
                            && unchecked(tick - arrangedAt.Value) < 180) occludedShots++;
                        if (value.Kind == CombatEventKind.Damage && value.Target.Slot == targetClient.Accepted.Slot)
                        {
                            resolvedDamage++;
                            if (!arrangedAt.HasValue || unchecked(tick - arrangedAt.Value) < 180) missDamage++;
                        }
                        if (value.Kind == CombatEventKind.Death && value.Target.Slot == targetClient.Accepted.Slot) resolvedDeaths++;
                        if (value.Kind == CombatEventKind.Affliction && value.Target.Slot == targetClient.Accepted.Slot) afflictions++;
                    }
                    int length = CombatEventBatch.Write(packet, events[..count]);
                    foreach (ServerPeer? peer in network.Peers)
                        if (peer?.Connection.State == NetConnectionState.Playing
                            && !network.TrySendEvent(peer, ReliableEventType.Combat, packet[..length]))
                            throw new ProgramException("Duel reliable event queue exhausted.");
                    simulation.Combat.Consume(count);
                }
                if (tick % 2 == 0)
                {
                    foreach (ServerPeer? peer in network.Peers)
                    {
                        if (peer?.Connection.State != NetConnectionState.Playing) continue;
                        var state = new SnapshotPacket(tick, snapshot++, network.MatchId, peer.Inputs.LastProcessed,
                            peer.Inputs.HasProcessed, Rng.Rng1, Rng.Rng2);
                        int length = state.Write(packet, simulation.States);
                        peer.Connection.Send(serverTransport, NetMessageType.Snapshot, packet[..length]);
                    }
                }
                for (int clientIndex = 0; clientIndex < clients.Length; clientIndex++)
                {
                    NetClient client = clients[clientIndex];
                    client.Poll();
                    if (client.Failure != null) throw new ProgramException(client.Failure);
                    if (client.State == NetConnectionState.Loading) client.Ready(client.Accepted.MatchId);
                    while (client.TryDequeueEvent(out NetApplicationEvent message))
                    {
                        if (message.Type != ReliableEventType.Combat || !CombatEventBatch.TryRead(message.Payload.Span, events, out int count)) continue;
                        foreach (CombatEvent value in events[..count]) delivered[clientIndex, (int)value.Kind]++;
                    }
                    if (client.State != NetConnectionState.Playing || network.Phase != MatchPhase.Playing) continue;
                    Vector3 origin = default, targetPosition = default;
                    foreach (SnapshotPlayer state in client.SnapshotPlayers)
                    {
                        if (state.Slot == shooterClient.Accepted.Slot) origin = state.Position;
                        if (state.Slot == targetClient.Accepted.Slot) targetPosition = state.Position;
                    }
                    Vector3 delta = targetPosition - origin;
                    Vector3 aim = delta.LengthSquared > 0.01f ? delta.Normalized() : -Vector3.UnitZ;
                    bool shoot = clientIndex == 0 && arrangedAt.HasValue;
                    uint sequence = sequences[clientIndex]++;
                    uint fullChargeTicks = (uint)Weapons.Current[(int)weapon + 9].FullCharge * 2;
                    uint firePeriod = weapon == BeamType.Imperialist ? 60u : fullChargeTicks + 60;
                    uint holdTicks = weapon == BeamType.Imperialist ? 6u : fullChargeTicks + 30;
                    uint fireTick = arrangedAt.HasValue ? unchecked(tick - arrangedAt.Value) : 0;
                    bool held = shoot && fireTick % firePeriod < holdTicks;
                    InputButtons buttons = held ? InputButtons.Shoot : 0;
                    InputButtons pressed = shoot && fireTick % firePeriod == 0 ? InputButtons.Shoot : 0;
                    histories[clientIndex, sequence % InputBundle.Capacity] = new(sequence, sequence,
                        client.Snapshot.ServerTick, buttons, pressed, aim, InputCommand.NoWeapon);
                    int countCommands = (int)Math.Min(sequence + 1, InputBundle.Capacity);
                    for (int i = 0; i < countCommands; i++)
                        bundle[i] = histories[clientIndex, (sequence - (uint)(countCommands - 1 - i)) % InputBundle.Capacity];
                    // This combat fixture shares the server owner; phase replication
                    // itself is exercised separately by the match-phase UDP check.
                    client.SendInputs(bundle[..countCommands], network.PhaseRevision);
                }
                tick++;
            }
            bool affinityPass = weapon == BeamType.Imperialist || afflictions > 0
                && delivered[0, (int)CombatEventKind.Affliction] > 0 && delivered[1, (int)CombatEventKind.Affliction] > 0;
            bool deathPass = weapon != BeamType.Imperialist || resolvedDeaths > 0
                && delivered[0, (int)CombatEventKind.Death] > 0 && delivered[1, (int)CombatEventKind.Death] > 0;
            bool pass = affinityPass && deathPass && arrangedAt.HasValue && resolvedDamage > 0 && missDamage == 0 && occludedShots > 0
                && delivered[0, (int)CombatEventKind.Damage] > 0 && delivered[1, (int)CombatEventKind.Damage] > 0;
            Console.WriteLine($"[combatduel] weapon={weapon} resolvedDamage={resolvedDamage} resolvedDeaths={resolvedDeaths} occludedShots={occludedShots} missDamage={missDamage} afflictions={afflictions} "
                + $"clientDamage={delivered[0,2]}/{delivered[1,2]} clientDeaths={delivered[0,3]}/{delivered[1,3]} "
                + $"rttMs={shooterClient.Clock.Metrics.SmoothedRttMs:F1} shotsRewound={simulation.Combat.ShotsRewound} historyQueries={simulation.Combat.History.Queries} "
                + $"result={(pass ? "PASS" : "FAIL")}");
            return pass ? 0 : 1;
        }
    }
}
