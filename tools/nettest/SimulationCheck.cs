using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    internal static class SimulationCheck
    {
        public static int Run(string[] args)
        {
            if (args.Length != 3 || !Int32.TryParse(args[1], out int seconds) || seconds < 10 || seconds > 300)
            {
                Console.Error.WriteLine("--simulation SECONDS PORT,PORT,... (2 to 8 clients; ports may be impairment proxies)");
                return 2;
            }
            int[] ports;
            try { ports = args[2].Split(',').Select(Int32.Parse).ToArray(); }
            catch (FormatException) { return 2; }
            catch (OverflowException) { return 2; }
            if (ports.Length < 2 || ports.Length > 8 || ports.Any(p => p < 1 || p > UInt16.MaxValue)) { return 2; }
            var transports = new NetTransport[ports.Length];
            var clients = new NetClient[ports.Length];
            var worlds = new ClientWorldState[ports.Length];
            var eventCounts = new long[ports.Length];
            var combatCounts = new long[ports.Length, 7];
            var histories = new InputCommand[ports.Length, 8];
            var sent = new uint[ports.Length];
            var origins = new Vector3[ports.Length];
            var placed = new bool[ports.Length];
            var maxDistance = new float[ports.Length];
            bool reconnected = false;
            ulong oldIdentity = 0;
            var timer = Stopwatch.StartNew();
            var scheduler = new FixedTickScheduler();
            Span<InputCommand> bundle = stackalloc InputCommand[8];
            Span<CombatEvent> combatEvents = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            try
            {
                for (int i = 0; i < ports.Length; i++)
                {
                    transports[i] = new NetTransport(0);
                    clients[i] = new NetClient(transports[i], new IPEndPoint(IPAddress.Loopback, ports[i]),
                        "SIM" + i, (Hunter)i);
                    var world = new ClientWorldState();
                    worlds[i] = world;
                    clients[i].WorldPacketValidator = WorldPacket.TryValidate;
                    clients[i].WorldPacketReceived = payload => world.Receive(payload);
                }
                while (timer.Elapsed.TotalSeconds < seconds)
                {
                    int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                    if (due == 0) { scheduler.Wait(); continue; }
                    for (int i = 0; i < clients.Length; i++)
                    {
                        NetClient client = clients[i];
                        client.Poll();
                        if (client.Failure != null) { throw new InvalidOperationException(client.Failure); }
                        if (client.State == NetConnectionState.Loading)
                        {
                            worlds[i].Reset(client.Accepted.MatchId);
                            client.Ready(client.Accepted.MatchId);
                        }
                        while (client.TryDequeueEvent(out NetApplicationEvent message))
                        {
                            eventCounts[i]++;
                            if (message.Type != ReliableEventType.Combat) { continue; }
                            if (!CombatEventBatch.TryRead(message.Payload.Span, combatEvents, out int eventCount))
                            {
                                throw new InvalidOperationException("Server delivered a malformed combat event batch.");
                            }
                            foreach (CombatEvent value in combatEvents[..eventCount])
                            {
                                combatCounts[i, (int)value.Kind]++;
                            }
                        }
                        if (!reconnected && i == 0 && timer.Elapsed.TotalSeconds > seconds / 2
                            && client.State == NetConnectionState.Playing)
                        {
                            oldIdentity = client.Connection!.Id;
                            client.Reconnect();
                            sent[i] = 0;
                            reconnected = true;
                            continue;
                        }
                        if (client.State is not (NetConnectionState.Ready or NetConnectionState.Playing)) { continue; }
                        Vector3 aim = -Vector3.UnitZ;
                        Vector3 position = default;
                        foreach (SnapshotPlayer player in client.SnapshotPlayers)
                        {
                            if (player.Slot == client.Accepted.Slot)
                            {
                                position = player.Position;
                                if (!placed[i]) { placed[i] = true; origins[i] = position; }
                                maxDistance[i] = Math.Max(maxDistance[i], (position - origins[i]).Length);
                            }
                            if ((player.AvailableWeapons & (1 << player.Weapon)) == 0)
                            {
                                throw new InvalidOperationException("Server equipped an unavailable weapon.");
                            }
                        }
                        foreach (SnapshotPlayer player in client.SnapshotPlayers)
                        {
                            if (player.Slot != client.Accepted.Slot && player.Health > 0)
                            {
                                Vector3 direction = player.Position - position;
                                if (direction.LengthSquared > 0.01f) { aim = direction.Normalized(); }
                                break;
                            }
                        }
                        uint sequence = sent[i]++;
                        InputButtons movement = ((sequence / 120 + i) % 4) switch
                        {
                            0 => InputButtons.Forward,
                            1 => InputButtons.Left,
                            2 => InputButtons.Back,
                            _ => InputButtons.Right
                        };
                        InputButtons pressed = sequence % 90 == 0 ? InputButtons.Jump : InputButtons.None;
                        if (sequence % 20 == 0) { pressed |= InputButtons.Shoot; }
                        InputButtons buttons = movement;
                        if (sequence % 20 < 6) { buttons |= InputButtons.Shoot; }
                        histories[i, sequence % 8] = new InputCommand(sequence, sequence,
                            client.HasSnapshot ? client.Snapshot.ServerTick : 0, buttons, pressed, aim, InputCommand.NoWeapon);
                        int count = (int)Math.Min(sequence + 1, 8);
                        for (int item = 0; item < count; item++)
                        {
                            bundle[item] = histories[i, (sequence - (uint)(count - 1 - item)) % 8];
                        }
                        client.SendInputs(bundle[..count]);
                    }
                }
                bool success = reconnected && clients[0].Connection!.Id != oldIdentity;
                for (int i = 0; i < clients.Length; i++)
                {
                    NetClient client = clients[i];
                    bool healthy = client.State == NetConnectionState.Playing && client.SnapshotsReceived >= seconds * 10
                        && placed[i] && maxDistance[i] > 1 && client.Snapshot.HasProcessedInput
                        && combatCounts[i, (int)CombatEventKind.Shot] >= seconds;
                    success &= healthy;
                    Console.WriteLine(FormattableString.Invariant($"SIMCHECK client={i} slot={client.Accepted.Slot} state={client.State} snapshots={client.SnapshotsReceived} moved={maxDistance[i]:F2} inputAck={client.Snapshot.LastProcessedInput} rejected={client.Rejected} result={(healthy ? "PASS" : "FAIL")}"));
                    Console.WriteLine($"SIMWORLD client={i} complete={worlds[i].HasState} records={worlds[i].Count} events={eventCounts[i]}");
                    Console.WriteLine($"SIMCOMBAT client={i} shots={combatCounts[i, 1]} damage={combatCounts[i, 2]} "
                        + $"deaths={combatCounts[i, 3]} spawns={combatCounts[i, 4]} afflictions={combatCounts[i, 5]} bombs={combatCounts[i, 6]}");
                }
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                foreach (NetClient? client in clients) { client?.Dispose(); }
                foreach (NetTransport? transport in transports) { transport?.Dispose(); }
            }
        }
    }
}
