using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Real authoritative sockets and codecs; synthetic states are not gameplay evidence.</summary>
    internal static class ConnectionBaseline
    {
        public static int RunServer(string[] args)
        {
            if (args.Length != 2 || !Int32.TryParse(args[1], out int port) || port < 0 || port > UInt16.MaxValue)
                return 2;
            using var transport = new NetTransport(port);
            using var stopped = new CancellationTokenSource();
            using var signals = new ShutdownSignals();
            signals.OnShutdown(stopped.Cancel);
            var network = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle)
            {
                ServerName = "Authoritative connection fixture"
            };
            var scheduler = new FixedTickScheduler();
            var players = new SnapshotPlayer[8];
            Span<byte> packet = stackalloc byte[SnapshotPacket.MaxSize];
            uint tick = 0, sequence = 0;
            Console.WriteLine($"[connection-fixture] listening on UDP {transport.LocalPort}; synthetic states, no gameplay");
            while (!stopped.IsCancellationRequested)
            {
                int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                if (due == 0) { scheduler.Wait(); continue; }
                for (int step = 0; step < due; step++, tick++)
                {
                    network.Poll(tick);
                    int count = 0;
                    foreach (ServerPeer? peer in network.Peers)
                    {
                        if (peer?.Connection.State == NetConnectionState.Ready) peer.Connection.StartPlaying();
                        if (peer?.Connection.State != NetConnectionState.Playing) continue;
                        peer.Inputs.Take(tick);
                        players[count++] = new SnapshotPlayer
                        {
                            Slot = peer.Slot, ConnectionId = peer.Connection.Id, Hunter = peer.Hunter,
                            TeamIndex = peer.Slot, Life = 1, Health = 99, AvailableWeapons = 1,
                            Position = new Vector3(peer.Slot, 0, tick / 60f),
                            Aim = -Vector3.UnitZ, Facing = -Vector3.UnitZ,
                            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                        };
                    }
                    if (tick % 2 != 0) continue;
                    foreach (ServerPeer? peer in network.Peers)
                    {
                        if (peer?.Connection.State != NetConnectionState.Playing) continue;
                        var state = new SnapshotPacket(tick, sequence, network.MatchId,
                            peer.Inputs.LastProcessed, peer.Inputs.HasProcessed, 0, 0);
                        int length = state.Write(packet, players.AsSpan(0, count));
                        peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
                    }
                    sequence++;
                }
            }
            Console.WriteLine($"[connection-fixture] stopped tick={tick} rejected={network.Rejected} queueDrops={transport.Metrics.QueueDrops}");
            return 0;
        }

        public static int Run(string[] args, IPAddress? address = null)
        {
            if (args.Length != 3 || !Int32.TryParse(args[1], out int seconds) || seconds < 5 || seconds > 300)
            {
                Console.Error.WriteLine("Usage: nettest --baseline SECONDS PORT,PORT,... (2 to 8 authoritative clients)");
                return 2;
            }
            string[] ports = args[2].Split(',');
            if (ports.Length < 2 || ports.Length > 8) return 2;
            var transports = new NetTransport[ports.Length];
            var clients = new NetClient[ports.Length];
            var worlds = new ClientWorldState[ports.Length];
            var commands = new InputCommand[ports.Length, InputBundle.Capacity];
            var sequences = new uint[ports.Length];
            var initialIn = new long[ports.Length];
            var initialOut = new long[ports.Length];
            var initialSnapshots = new long[ports.Length];
            var initialRejected = new long[ports.Length];
            var intervals = new NetSample[ports.Length];
            var previous = new long[ports.Length];
            var observed = new long[ports.Length];
            Span<InputCommand> bundle = stackalloc InputCommand[InputBundle.Capacity];
            try
            {
                for (int i = 0; i < ports.Length; i++)
                {
                    if (!Int32.TryParse(ports[i], out int port) || port < 1 || port > UInt16.MaxValue) return 2;
                    transports[i] = new NetTransport(0);
                    clients[i] = new NetClient(transports[i], new IPEndPoint(address ?? IPAddress.Loopback, port),
                        "WIRE" + i, (Hunter)i);
                    var world = new ClientWorldState();
                    worlds[i] = world;
                    clients[i].WorldPacketValidator = WorldPacket.TryValidate;
                    clients[i].WorldPacketReceived = body => world.Receive(body);
                }
                var clock = Stopwatch.StartNew();
                bool ready = false;
                while (clock.Elapsed.TotalSeconds < 15)
                {
                    Poll(clients, worlds);
                    ready = Array.TrueForAll(clients, c => c.State == NetConnectionState.Playing
                        && c.HasRoster && c.Roster.Length == clients.Length && c.Clock.Synchronized);
                    if (ready) break;
                    Thread.Sleep(2);
                }
                if (!ready) throw new InvalidOperationException("Authoritative peers did not become ready with a roster and synchronized clock.");
                uint occupied = 0;
                for (int i = 0; i < clients.Length; i++)
                {
                    uint bit = 1u << clients[i].Accepted.Slot;
                    if ((occupied & bit) != 0) throw new InvalidOperationException("Two peers were assigned the same slot.");
                    occupied |= bit;
                    initialIn[i] = transports[i].Metrics.BytesReceived;
                    initialOut[i] = transports[i].Metrics.BytesSent;
                    initialSnapshots[i] = observed[i] = clients[i].SnapshotsReceived;
                    initialRejected[i] = clients[i].Rejected;
                }
                var scheduler = new FixedTickScheduler();
                clock.Restart();
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    if (scheduler.TakeDue(Stopwatch.GetTimestamp()) == 0) { scheduler.Wait(); continue; }
                    Poll(clients, worlds);
                    long now = Stopwatch.GetTimestamp();
                    for (int i = 0; i < clients.Length; i++)
                    {
                        NetClient client = clients[i];
                        if (client.SnapshotsReceived != observed[i])
                        {
                            if (previous[i] != 0) intervals[i].Record((now - previous[i]) * (1000d / Stopwatch.Frequency));
                            previous[i] = now; observed[i] = client.SnapshotsReceived;
                        }
                        uint sequence = sequences[i]++;
                        commands[i, sequence % InputBundle.Capacity] = new InputCommand(sequence, sequence,
                            client.Snapshot.ServerTick, InputButtons.None, InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon);
                        int count = (int)Math.Min(sequence + 1, InputBundle.Capacity);
                        for (int item = 0; item < count; item++)
                            bundle[item] = commands[i, (sequence - (uint)(count - 1 - item)) % InputBundle.Capacity];
                        client.SendInputs(bundle[..count]);
                    }
                }
                bool passed = true;
                for (int i = 0; i < clients.Length; i++)
                {
                    NetClient client = clients[i];
                    long snapshots = client.SnapshotsReceived - initialSnapshots[i];
                    bool healthy = client.State == NetConnectionState.Playing && client.Snapshot.HasProcessedInput
                        && snapshots >= seconds * 5 && client.SnapshotPlayers.Length == clients.Length
                        && client.Rejected == initialRejected[i] && transports[i].Metrics.QueueDrops == 0;
                    passed &= healthy;
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        kind = "authoritative-connection-baseline", players = clients.Length, client = i,
                        slot = client.Accepted.Slot, seconds, healthy,
                        snapshotsPerSecond = snapshots / (double)seconds,
                        snapshotIntervalMs = intervals[i].Mean,
                        processedInput = client.Snapshot.LastProcessedInput,
                        reportedRttMs = client.Clock.Metrics.SmoothedRttMs,
                        bytesIn = transports[i].Metrics.BytesReceived - initialIn[i],
                        bytesOut = transports[i].Metrics.BytesSent - initialOut[i],
                        queueDrops = transports[i].Metrics.QueueDrops,
                        startupRejected = initialRejected[i], rejected = client.Rejected - initialRejected[i]
                    }));
                }
                return passed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex); return 1;
            }
            finally
            {
                foreach (NetClient? client in clients) client?.Dispose();
                foreach (NetTransport? transport in transports) transport?.Dispose();
            }
        }

        private static void Poll(NetClient[] clients, ClientWorldState[] worlds)
        {
            for (int i = 0; i < clients.Length; i++)
            {
                NetClient client = clients[i];
                client.Poll();
                if (client.Failure != null) throw new InvalidOperationException(client.Failure);
                if (client.State == NetConnectionState.Loading)
                {
                    worlds[i].Reset(client.Accepted.MatchId);
                    client.Ready(client.Accepted.MatchId);
                }
                while (client.TryDequeueEvent(out _)) { }
            }
        }
    }
}
