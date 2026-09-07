using System;
using System.Diagnostics;

namespace MphRead.Mods.Network
{
    /// <summary>One scene, one writer, one fixed simulation step per tick.</summary>
    public sealed class AuthoritativeServer
    {
        private volatile bool _running = true;
        private readonly int _port;
        private readonly string _data;
        private readonly string _version;
        private readonly RotationEntry _entry;
        public MapRotation? Rotation { get; init; }
        public int MaxPlayers { get; init; } = 8;
        public bool FriendlyFire { get; init; }
        public string ServerName { get; init; } = "Fruity Prime";
        public MasterReporter? Reporter { get; init; }
        public int BoundPort { get; private set; }

        public AuthoritativeServer(int port, string data, string version, RotationEntry entry)
        {
            _port = port;
            _data = data;
            _version = version;
            _entry = entry;
        }

        public void Stop() => _running = false;

        public void Run()
        {
            try { RunSimulation(); }
            finally
            {
                if (BoundPort != 0) { Reporter?.Farewell((ushort)BoundPort); }
                Reporter?.Dispose();
            }
        }

        private void RunSimulation()
        {
            ServerContent.Open(_data, _version);
            using var transport = new NetTransport(_port);
            BoundPort = transport.LocalPort;
            ServerSimulation simulation = new(_entry);
            try
            {
                GameState.FriendlyFire = FriendlyFire;
                var network = new ServerNetwork(transport, _entry.RoomKey, _entry.Mode, capacity: MaxPlayers)
                {
                    ServerName = ServerName
                };
                network.StatusProvider = () => new MatchStatePacket
                {
                    MatchId = unchecked((ushort)network.MatchId), Mode = (byte)network.Mode,
                    RoomKey = network.Room, NextRoomKey = Rotation?.Next.RoomKey ?? network.Room,
                    PlayerCount = (byte)network.Count, TimeRemaining = Math.Max(0, GameState.MatchTime),
                    PointGoal = (ushort)Math.Clamp(GameState.PointGoal, 0, UInt16.MaxValue),
                    Flags = (byte)((GameState.MatchState == MatchState.InProgress
                        ? MatchStatePacket.FlagInProgress : MatchStatePacket.FlagEnding)
                        | (FriendlyFire ? MatchStatePacket.FlagFriendlyFire : 0))
                };
                var scheduler = new FixedTickScheduler();
                var world = new WorldStateCapture();
                uint tick = 0;
                uint snapshotSequence = 0;
                uint worldRevision = 0;
                uint? endedAt = null;
                int reportedPeers = -1;
                double nextReport = 0;
                long lastReport = Stopwatch.GetTimestamp();
                long previousIn = 0, previousOut = 0;
                long previousAllocated = GC.GetTotalAllocatedBytes(precise: false);
                using var process = Process.GetCurrentProcess();
                TimeSpan previousCpu = process.TotalProcessorTime;
                Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
                Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                Console.WriteLine($"[server] listening on UDP {BoundPort}; {_entry.RoomKey}; data {_version}");
                while (_running)
                {
                    int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                    if (due == 0)
                    {
                        scheduler.Wait();
                        continue;
                    }
                    for (int step = 0; step < due && _running; step++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        network.Poll(tick);
                        if (reportedPeers != network.Count)
                        {
                            reportedPeers = network.Count;
                            Console.WriteLine($"[server] peers={reportedPeers}");
                        }
                        simulation.Step(network, tick);
                        if (tick % 2 == 0)
                        {
                            foreach (ServerPeer? peer in network.Peers)
                            {
                                if (peer?.Connection.State != NetConnectionState.Playing) { continue; }
                                var snapshot = new SnapshotPacket(tick, snapshotSequence, network.MatchId,
                                    peer.Inputs.LastProcessed, peer.Inputs.HasProcessed, Rng.Rng1, Rng.Rng2);
                                int length = snapshot.Write(packet, simulation.States);
                                peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
                            }
                            snapshotSequence++;
                        }
                        // State is recoverable from the next complete update.
                        // A five-Hz world stream leaves budget for combat and movement.
                        if (tick % 12 == 0 && network.Count > 0)
                        {
                            world.Capture(simulation.Scene, network.MatchId, worldRevision++, tick);
                            for (int batch = 0; batch < world.BatchCount; batch++)
                            {
                                int length = world.WriteBatch(packet, batch);
                                foreach (ServerPeer? peer in network.Peers)
                                {
                                    if (peer != null) { network.SendWorld(peer, packet[..length]); }
                                }
                            }
                        }
                        // A stalled peer cannot block the shared event journal.
                        // Admission is per peer; sequence-level dedup is reliable-layer owned.
                        for (int batch = 0; batch < 8; batch++)
                        {
                            int count = simulation.Combat.CopyPending(events);
                            if (count == 0) { break; }
                            int length = CombatEventBatch.Write(packet, events[..count]);
                            foreach (ServerPeer? peer in network.Peers)
                            {
                                if (peer?.Connection.State == NetConnectionState.Playing
                                    && !network.TrySendEvent(peer, ReliableEventType.Combat, packet[..length]))
                                {
                                    Console.Error.WriteLine($"[server] slot {peer.Slot} disconnected: reliable queue exhausted.");
                                    network.Remove(peer.Slot);
                                }
                            }
                            simulation.Combat.Consume(count);
                        }
                        if (GameState.MatchState != MatchState.InProgress)
                        {
                            endedAt ??= tick;
                            if (unchecked(tick - endedAt.Value) >= 300)
                            {
                                RotationEntry next = Rotation?.Advance() ?? _entry;
                                uint match = unchecked(network.MatchId + 1);
                                if (match == 0) { match = 1; }
                                network.ChangeMatch(match, next.RoomKey, next.Mode, tick);
                                // Flush the loading notification before synchronous content IO.
                                network.Poll(tick);
                                simulation.Dispose();
                                simulation = new ServerSimulation(next);
                                GameState.FriendlyFire = FriendlyFire;
                                endedAt = null;
                                Console.WriteLine($"[server] match={match} room={next.RoomKey} tick={tick}");
                            }
                        }
                        tick++;
                        scheduler.DurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                    double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    if (now >= nextReport)
                    {
                        long reportAt = Stopwatch.GetTimestamp();
                        double seconds = Math.Max(Stopwatch.GetElapsedTime(lastReport, reportAt).TotalSeconds, 0.001);
                        long received = transport.Metrics.BytesReceived;
                        long sent = transport.Metrics.BytesSent;
                        long allocated = GC.GetTotalAllocatedBytes(precise: false);
                        TimeSpan cpu = process.TotalProcessorTime;
                        Console.WriteLine(FormattableString.Invariant($"[server] inKBps={(received - previousIn) / seconds / 1000:F1} outKBps={(sent - previousOut) / seconds / 1000:F1} allocatedKBps={(allocated - previousAllocated) / seconds / 1000:F1} cpuCores={(cpu - previousCpu).TotalSeconds / seconds:F3}"));
                        lastReport = reportAt;
                        previousIn = received;
                        previousOut = sent;
                        previousAllocated = allocated;
                        previousCpu = cpu;
                        Console.WriteLine(FormattableString.Invariant($"[server] tick={tick} peers={network.Count} tickMeanMs={scheduler.DurationMs.Mean:F3} tickWorstMs={scheduler.DurationMs.Max:F3} dropped={scheduler.DroppedTicks} catchUp={scheduler.CatchUpTicks} driftMeanMs={scheduler.DriftMs.Mean:F3} rejected={network.Rejected} sentBytes={transport.Metrics.BytesSent} queueDrops={transport.Metrics.QueueDrops} shotsConsidered={simulation.Combat.ShotsConsidered} shotsEligible={simulation.Combat.ShotsEligible} shotsRewound={simulation.Combat.ShotsRewound} shotsClamped={simulation.Combat.ShotsClamped} requestedRewindMeanTicks={simulation.Combat.RequestedRewindTicks.Mean:F2} validatedRewindMeanTicks={simulation.Combat.ValidatedRewindTicks.Mean:F2} validatedRewindMaxTicks={simulation.Combat.ValidatedRewindTicks.Max:F0} historyQueries={simulation.Combat.History.Queries} historyMisses={simulation.Combat.History.Missing} combatDrops={simulation.Combat.Dropped}"));
                        nextReport = now + 30;
                    }
                    Reporter?.Beat(now, ServerName, (ushort)BoundPort, (byte)network.Count,
                        (byte)MaxPlayers, (byte)network.Mode, network.Room, protocol: NetHeader.Version);
                }
            }
            finally
            {
                simulation.Dispose();
            }
        }
    }
}
