using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    /// <summary>Native authority and eight ordinary UDP peers. Every peer validates
    /// all baseline lanes before acknowledging WorldReady; no rendered-client claim.</summary>
    public static int RunEightPeerBootstrap(string assetDirectory)
    {
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(assetDirectory));
            Paths.UpdatePaths(); Paths.ChooseMphPath();
            NetLag.Configure("0"); NetLag.ConfigureLoss("0");
            NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
            using var rig = new Rig(simulate: true, room: "MP1 SANCTORUS");
            // Construct every socket before sending the first Hello. No Add/Loaded
            // sequence can let a smaller early baseline hide full-capacity staging.
            Client[] clients = Enumerable.Range(0, PlayerEntity.SlotCapacity)
                .Select(i => new Client(rig.Server.BoundPort, (uint)(1900 + i), dropInitialHello: true)).ToArray();
            rig.Clients.AddRange(clients);
            foreach (Client client in clients) client.Hello();
            rig.Wait(() => clients.All(c => c.Slot >= 0 && c.State != null), "eight peers admitted together");
            foreach (Client client in clients) client.Identify();
            rig.Stable();
            Check(clients.Select(c => c.Slot).Distinct().Count() == PlayerEntity.SlotCapacity
                && clients.All(c => c.Roster.Count == PlayerEntity.SlotCapacity), "eight unique occupied roster slots");
            rig.ReadyAll();
            Client owner = clients.Single(c => c.State!.Value.OwnerSlot == c.Slot);
            rig.Expect(owner, owner.Command(LobbyCommandType.StartMatch), LobbyResultCode.Ok);
            Check(rig.Server.Simulating && clients.All(c => c.State!.Value.Phase == SessionPhase.Starting
                && c.State.Value.ExpectedParticipants == byte.MaxValue), "native eight-peer load barrier started");

            var baselines = clients.Select(c => new EightPeerBaseline(c)).ToArray();
            long[] snapshots = new long[clients.Length];
            long[] frozenSnapshots = new long[clients.Length];
            uint[] frames = new uint[clients.Length], acks = new uint[clients.Length];
            FieldInfo errorField = typeof(Rig).GetField("_error", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (Client client in clients) client.Loaded();
            void Pump()
            {
                if (errorField.GetValue(rig) is Exception error)
                    throw new InvalidOperationException("Eight-peer server owner thread failed.", error);
                for (int i = 0; i < clients.Length; i++)
                    foreach (ReceivedPacket packet in clients[i].Transport.Drain())
                    {
                        Client client = clients[i];
                        if (packet.Type is PacketType.Bye or PacketType.Refused)
                            throw new InvalidOperationException($"Peer {client.Slot} received unexpected {packet.Type} during bootstrap.");
                        if (packet.Type == PacketType.WorldBootstrap) baselines[i].Accept(packet.Payload);
                        if (packet.Type == PacketType.SessionState && SessionStatePacket.TryRead(packet.Payload, out var state)
                            && (state.Revision == client.State!.Value.Revision
                                || SessionStatePacket.IsNewer(state.Revision, client.State.Value.Revision))) client.State = state;
                        if (packet.Type == PacketType.Roster && RosterPacket.TryRead(packet.Payload, out var roster)
                            && (roster.Revision == client.Roster.Revision || NetLifecycleTracker.Newer(roster.Revision, client.Roster.Revision)))
                            client.Roster = roster;
                        if (packet.Type == PacketType.SnapshotFast)
                        {
                            // The authority publishes fast lanes during Starting.
                            // Match the live client's FreezeGameplay gate: they
                            // cannot replace its frozen baseline or count as play.
                            if (!baselines[i].ReadySent || client.State!.Value.Phase != SessionPhase.InMatch)
                            { frozenSnapshots[i]++; continue; }
                            var start = baselines[i].Identity!.Value.Start;
                            Check(NetReplicationReceiver.ValidateFast(packet.Payload, start.MatchId, start.AuthorityEpoch, out var header)
                                && header.PlayerCount == PlayerEntity.SlotCapacity, "continuing native snapshot contains all eight peers");
                            snapshots[i]++; acks[i] = header.Frame;
                        }
                    }
            }
            void Wait(Func<bool> condition, string label, int milliseconds)
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < milliseconds)
                {
                    Pump();
                    if (condition()) { Check(true, label); return; }
                    Thread.Sleep(2);
                }
                throw new InvalidOperationException($"Timed out: {label}; masks={string.Join(',', baselines.Select(b => b.Mask))}");
            }
            Wait(() => baselines.All(b => b.ReadySent), "all eight complete four-lane baselines acknowledged", 15000);
            Wait(() => clients.All(c => c.State!.Value.Phase == SessionPhase.InMatch), "all eight world-ready peers released into match", 15000);
            var sim = (ServerSim)typeof(DedicatedServer).GetField("_sim", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Server)!;
            long initialFrames = sim.Frames;
            var traffic = Stopwatch.StartNew(); double nextIntent = 0;
            while (traffic.Elapsed.TotalSeconds < 2)
            {
                if (traffic.Elapsed.TotalSeconds >= nextIntent)
                {
                    nextIntent += 1 / 60.0;
                    for (int i = 0; i < clients.Length; i++)
                    {
                        int slot = clients[i].Slot; var player = PlayerEntity.Players[slot];
                        var intent = new IntentPacket
                        {
                            Frame = ++frames[i], MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
                            SlotGeneration = NetPlayerLifecycle.Generation(slot), LifeId = NetPlayerLifecycle.Get(slot),
                            Position = player.Position, Aim = Vector3.UnitZ, WeaponSelect = 255, AmmoUa = 400, AmmoMissiles = 50,
                            AckFrame = acks[i], Buttons = player.Health > 0 ? IntentButtons.InPlayState : 0
                        };
                        byte[] bytes = new byte[intent.EncodedSize]; intent.WriteNetwork(bytes); clients[i].Send(PacketType.Intent, bytes);
                    }
                }
                Pump(); Thread.Sleep(1);
            }
            Check(sim.StepFailures == 0 && sim.Frames - initialFrames > 60, "native authority continues healthy after full admission");
            Check(snapshots.All(n => n > 20), "every peer continues receiving eight-player snapshots");
            Check(clients.All(c => NetSession.RemoteIntentValid[c.Slot]), "all eight ordinary inputs accepted by native policy");
            Console.WriteLine($"EIGHT PEER BOOTSTRAP PASS: {_checks} checks; fast={SnapshotFast.MaximumPayloadSize}, "
                + $"reliableBootstrapWire={WorldBootstrapIdentity.Size + 1 + SnapshotFast.MaximumPayloadSize + NetHeader.Size + sizeof(uint)}, "
                + $"snapshots=[{string.Join(',', snapshots)}], discardedFrozenSnapshots=[{string.Join(',', frozenSnapshots)}], "
                + $"nativeSteps={sim.Frames - initialFrames}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); Directory.SetCurrentDirectory(cwd); }
    }

    private sealed class EightPeerBaseline
    {
        private readonly Client _client;
        private readonly MatchStartIdentity _start;
        private readonly ushort _generation;
        private readonly NetReplicationReceiver _receiver = new();
        private readonly WorldBootstrapObjectives _objectives = new();
        private readonly byte[][] _lanes =
        {
            new byte[SnapshotFast.MaximumPayloadSize], new byte[NetReplicationLanes.MaximumSlowPayloadSize],
            new byte[NetReplicationLanes.MaximumWorldPayloadSize]
        };
        private readonly int[] _lengths = new int[3];
        public WorldBootstrapIdentity? Identity { get; private set; }
        public byte Mask { get; private set; }
        public bool ReadySent { get; private set; }
        public EightPeerBaseline(Client client)
        {
            _client = client; var state = client.State!.Value;
            _start = new(state.MatchId, state.AuthorityEpoch, state.StartGeneration);
            int at = Array.IndexOf(client.Roster.Slots, (byte)client.Slot, 0, client.Roster.Count);
            _generation = client.Roster.Generations[at];
            Check(_lanes.All(lane => lane.Length + WorldBootstrapIdentity.Size + 1 + sizeof(uint) <= NetConfig.MaxPayloadSize),
                "maximum reliable baseline lanes fit the unchanged datagram ceiling");
        }
        public void Accept(ReadOnlySpan<byte> payload)
        {
            Check(payload.Length > WorldBootstrapIdentity.Size && WorldBootstrapIdentity.TryRead(payload, out _), "baseline identity decodes");
            WorldBootstrapIdentity.TryRead(payload, out var identity);
            Check(identity.Start == _start && identity.SlotGeneration == _generation, "baseline matches current start and recipient incarnation");
            Check(Identity == null || Identity == identity, "frozen baseline identity remains stable until acknowledgement");
            if (Identity == null) { Identity = identity; _receiver.Reset(_start.MatchId, _start.AuthorityEpoch); }
            int lane = payload[WorldBootstrapIdentity.Size]; var data = payload[(WorldBootstrapIdentity.Size + 1)..];
            Check(lane is >= 0 and <= 3, "baseline lane is known");
            if (lane == 3)
            {
                Check(_objectives.Accept(data, identity), "objective baseline fragment validates");
                if (_objectives.World == null) return;
            }
            else if ((Mask & (1 << lane)) != 0)
                Check(data.SequenceEqual(_lanes[lane].AsSpan(0, _lengths[lane])), "duplicate baseline lane is identical");
            else
            {
                Check(data.Length <= _lanes[lane].Length, "lane fits its derived layout capacity");
                if (lane == 0)
                {
                    Check(NetReplicationReceiver.ValidateFast(data, _start.MatchId, _start.AuthorityEpoch, out var header)
                        && header.Frame == identity.AuthorityFrame && header.PlayerCount == PlayerEntity.SlotCapacity
                        && data.Length == SnapshotFast.MaximumPayloadSize, "first baseline carries all eight player states");
                }
                else Check(_receiver.Receive(lane == 1 ? PacketType.PlayerSlowState : PacketType.WorldState,
                    data, _start.MatchId, _start.AuthorityEpoch), "full slow/world baseline validates");
                data.CopyTo(_lanes[lane]); _lengths[lane] = data.Length;
            }
            Mask |= (byte)(1 << lane);
            if (Mask != 15) return;
            Check(_receiver.SlowRevision == identity.SlowRevision && _receiver.WorldRevision == identity.WorldRevision,
                "all lanes belong to the frozen baseline revisions");
            byte[] canonical = new byte[NetConfig.MaxSnapshotSize];
            int length = _receiver.Assemble(_lanes[0].AsSpan(0, _lengths[0]), canonical, _start.MatchId, _start.AuthorityEpoch);
            Check(length > 0 && SnapshotHeader.Read(canonical).PlayerCount == PlayerEntity.SlotCapacity,
                "complete native baseline assembles all eight occupants");
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                var state = PlayerState.Read(canonical.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
                int at = Array.IndexOf(_client.Roster.Slots, state.SlotIndex, 0, _client.Roster.Count);
                Check(at >= 0 && _client.Roster.Generations[at] == state.SlotGeneration, "baseline occupant matches the current roster generation");
            }
            byte[] ready = new byte[WorldBootstrapIdentity.Size]; identity.Write(ready);
            _client.Send(PacketType.WorldReady, ready); ReadySent = true;
        }
    }
}
