using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    internal static class MixedCombatClients
    {
        internal const double MaximumSnapshotAgeSeconds = 1;

        internal static bool SnapshotIsFresh(bool hasSnapshot, long receivedAt, long now)
            => hasSnapshot && receivedAt > 0 && now >= receivedAt
                && Stopwatch.GetElapsedTime(receivedAt, now).TotalSeconds <= MaximumSnapshotAgeSeconds;

        public static int Run(string[] args)
        {
            if (args.Length != 5 || !Int32.TryParse(args[1], out int seconds) || seconds < 10 || seconds > 330)
            {
                Console.Error.WriteLine("--mixed-soak-clients SECONDS PORT,PORT,... REPORT_JSON SERVER_COMPLETION_JSON (exactly eight clients)");
                return 2;
            }
            int[] ports;
            try { ports = args[2].Split(',').Select(Int32.Parse).ToArray(); }
            catch (FormatException) { return 2; }
            catch (OverflowException) { return 2; }
            if (ports.Length != 8 || ports.Any(p => p < 1 || p > UInt16.MaxValue)) { return 2; }
            var transports = new NetTransport[ports.Length];
            var clients = new NetClient[ports.Length];
            var worlds = new ClientWorldState[ports.Length];
            var eventCounts = new long[ports.Length];
            var combatCounts = new long[ports.Length, 7];
            var histories = new InputCommand[ports.Length, 8];
            var sent = new uint[ports.Length];
            var inputEpochs = new uint[ports.Length];
            var origins = new Vector3[ports.Length];
            var placed = new bool[ports.Length];
            var maxDistance = new float[ports.Length];
            long[] ownShots = new long[8];
            uint[] lastSnapshot = new uint[8];
            var interpolation = new SnapshotInterpolation[8];
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
                        "SOAK" + i, Hunter.Samus);
                    interpolation[i] = new SnapshotInterpolation();
                    var world = new ClientWorldState();
                    worlds[i] = world;
                    clients[i].WorldPacketValidator = WorldPacket.TryValidate;
                    clients[i].WorldPacketReceived = payload => world.Receive(payload);
                }
                while (timer.Elapsed.TotalSeconds < seconds + 40 && !System.IO.File.Exists(args[4]))
                {
                    int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                    if (due == 0) { scheduler.Wait(); continue; }
                    for (int step = 0; step < due; step++)
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
                                if (value.Kind == CombatEventKind.Shot && value.Actor.Slot == client.Accepted.Slot) ownShots[i]++;
                            }
                        }
                        if (client.State != NetConnectionState.Playing
                            || !worlds[i].HasState || worlds[i].Phase != MatchPhase.Playing) { continue; }
                        Vector3 aim = -Vector3.UnitZ;
                        Vector3 position = default;
                        byte currentWeapon = InputCommand.NoWeapon;
                        uint inputEpoch = 1;
                        foreach (SnapshotPlayer player in client.SnapshotPlayers)
                        {
                            if (player.Slot == client.Accepted.Slot)
                            {
                                position = player.Position;
                                currentWeapon = player.Weapon;
                                inputEpoch = player.Life;
                                if (!placed[i]) { placed[i] = true; origins[i] = position; }
                                maxDistance[i] = Math.Max(maxDistance[i], (position - origins[i]).Length);
                            }
                            if ((player.AvailableWeapons & (1 << player.Weapon)) == 0)
                            {
                                throw new InvalidOperationException("Server equipped an unavailable weapon.");
                            }
                        }
                        if (inputEpochs[i] != inputEpoch)
                        {
                            inputEpochs[i] = inputEpoch;
                            sent[i] = 0;
                        }
                        uint sequence = sent[i]++;
                        // Small alternating strafe keeps targets moving while retaining the short-range arena.
                        InputButtons movement = sequence % 120 < 6 ? InputButtons.Left
                            : sequence % 120 is >= 60 and < 66 ? InputButtons.Right : 0;
                        BeamType weapon = MixedCombatSoak.Weapon(client.Accepted.Slot);
                        uint period = weapon == BeamType.Missile ? 150u : weapon == BeamType.ShockCoil ? 120u : 30u;
                        uint hold = weapon == BeamType.Missile ? 110u : weapon == BeamType.ShockCoil ? 100u : 5u;
                        uint phase = sequence % period;
                        InputButtons buttons = movement | (phase < hold ? InputButtons.Shoot : 0);
                        InputButtons pressed = phase == 0 ? InputButtons.Shoot : 0;
                        if (client.HasSnapshot && (interpolation[i].Count == 0 || lastSnapshot[i] != client.Snapshot.Sequence))
                        {
                            interpolation[i].Add(client.Snapshot, client.SnapshotPlayers, Stopwatch.GetTimestamp());
                            lastSnapshot[i] = client.Snapshot.Sequence;
                        }
                        // Socket fixture samples the same delayed timeline, without claiming rendered evidence.
                        if (interpolation[i].TryPreparePresentation(client.Clock.EstimateServerTick(Stopwatch.GetTimestamp()), out SnapshotPresentation frame))
                        {
                            if (interpolation[i].TrySample(client.Accepted.Slot ^ 1, frame, out SnapshotPlayer target) && target.Health > 0)
                            {
                                Vector3 direction = target.Position - position;
                                if (direction.LengthSquared > 0.01f) aim = direction.Normalized();
                            }
                            interpolation[i].MarkPresented(frame);
                        }
                        interpolation[i].TryCaptureViewTick(out uint viewTick);
                        histories[i, sequence % 8] = new InputCommand(sequence, sequence,
                            viewTick, buttons, pressed, aim,
                            currentWeapon == (byte)weapon ? InputCommand.NoWeapon : (byte)weapon,
                            inputEpoch);
                        int count = (int)Math.Min(sequence + 1, 8);
                        for (int item = 0; item < count; item++)
                        {
                            bundle[item] = histories[i, (sequence - (uint)(count - 1 - item)) % 8];
                        }
                        client.SendInputs(bundle[..count], worlds[i].PhaseRevision);
                    }
                }
                bool success = scheduler.DroppedTicks == 0 && System.IO.File.Exists(args[4]);
                var reports = new object[8];
                for (int i = 0; i < clients.Length; i++)
                {
                    NetClient client = clients[i];
                    double snapshotAgeSeconds = client.HasSnapshot
                        ? Stopwatch.GetElapsedTime(client.SnapshotReceivedAt).TotalSeconds : double.PositiveInfinity;
                    bool snapshotFresh = SnapshotIsFresh(client.HasSnapshot, client.SnapshotReceivedAt, Stopwatch.GetTimestamp());
                    bool healthy = snapshotFresh && client.State == NetConnectionState.Playing && client.SnapshotsReceived >= seconds * 10
                        && placed[i] && client.Snapshot.HasProcessedInput
                        && ownShots[i] >= seconds / 8 && transports[i].Metrics.QueueDrops == 0;
                    success &= healthy;
                    reports[i] = new { client = i, slot = client.Accepted.Slot, healthy,
                        snapshotFresh, snapshotAgeSeconds = double.IsFinite(snapshotAgeSeconds) ? (double?)snapshotAgeSeconds : null,
                        snapshots = client.SnapshotsReceived, ownRootShots = ownShots[i], inputsSent = sent[i],
                        rttMs = client.Clock.Metrics.SmoothedRttMs, queueDrops = transports[i].Metrics.QueueDrops };
                    Console.WriteLine(FormattableString.Invariant($"MIXEDCLIENT client={i} slot={client.Accepted.Slot} state={client.State} snapshots={client.SnapshotsReceived} moved={maxDistance[i]:F2} inputAck={client.Snapshot.LastProcessedInput} rejected={client.Rejected} snapshotAgeSeconds={snapshotAgeSeconds:F3} snapshotFresh={snapshotFresh} result={(healthy ? "PASS" : "FAIL")}"));
                    Console.WriteLine($"MIXEDWORLD client={i} complete={worlds[i].HasState} records={worlds[i].Count} events={eventCounts[i]}");
                    Console.WriteLine($"MIXEDCOMBAT client={i} shots={combatCounts[i, 1]} damage={combatCounts[i, 2]} "
                        + $"deaths={combatCounts[i, 3]} spawns={combatCounts[i, 4]} afflictions={combatCounts[i, 5]} bombs={combatCounts[i, 6]}");
                }
                System.IO.File.WriteAllText(args[3], System.Text.Json.JsonSerializer.Serialize(new
                { passed = success, clients = reports, droppedTicks = scheduler.DroppedTicks, catchUpTicks = scheduler.CatchUpTicks },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
