using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class TransportLifecycleTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        _checks++;
        NetArchitectureTests.Check(condition, name);
    }

    public static int Run()
    {
        try
        {
            TerminalChannelDrain();
            foreach (PacketType type in new[] { PacketType.Bye, PacketType.Refused }) TerminalRetry(type);
            IncomingCloseAck();
            ConnectionCapacity();
            EndpointIdentityBinding();
            RepeatedLobbyClose();
            RoleTransitions();
            FailedClientStart();
            MatchedLoadTimeout();
            OptionalWireCounters();
            DirectoryFarewellScope();
            ResumePreservesAdmission();
            foreach (PacketType type in new[] { PacketType.Bye, PacketType.Refused }) ResumePreservesTerminal(type);
            WorldBootstrapFragments();
            Console.WriteLine($"PASS: {_checks} transport lifecycle assertions; terminal retries/ACKs, lobby retirement, roles, startup cleanup, fenced load timeout, wire counters, directory farewell scope");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); }
    }

    private static void TerminalChannelDrain()
    {
        var channel = new NetReliableChannel();
        channel.TryQueue(PacketType.Roster, new byte[] { 1 }, 0, out _);
        channel.TryQueue(PacketType.WorldBootstrap, new byte[] { 2 }, 0, out _);
        channel.TryQueue(PacketType.Bye, ReadOnlySpan<byte>.Empty, 0, out uint close);
        channel.BeginTerminalDrain();
        Check(channel.Capture(0).Pending == 1 && channel.TrySend(0, out var sent)
            && sent.EventId == close && sent.Type == PacketType.Bye,
            "terminal drain discards obsolete retries and preserves final event identity");
        channel.Acknowledge(close);
        Check(channel.Capture(1).Pending == 0, "terminal event completes with its original ACK identity");
    }

    private static void WorldBootstrapFragments()
    {
        // A valid map's door facts cross the first full-fragment boundary.
        // Exercise the real reliable queue and UDP envelope, then assemble the
        // received fragments in reverse order as the unordered channel permits.
        var world = new ReplayAuthorityWorld { MatchId = 7, Epoch = 11, Tick = 100, Doors = new ReplayDoorState[96] };
        for (int i = 0; i < world.Doors.Length; i++) world.Doors[i] = new(i, 0, false, true, false);
        byte[] encoded = world.Encode();
        Check(ReplayAuthorityWorld.Decode(encoded).Doors.Length == world.Doors.Length, "multi-fragment objective fixture is a valid ordinary world");
        var identity = new WorldBootstrapIdentity(new(7, 11, 3), 5, 2, 100, 9, 12);
        byte[][] fragments = WorldBootstrapObjectives.Packets(identity, world);
        Check(fragments.Length > 1 && fragments[0].Length == NetReliableChannel.MaximumPayloadSize,
            "objective baseline crosses a full reliable-fragment boundary");
        using var transport = new NetTransport(0);
        transport.EnablePacketKindDiagnostics();
        using var peer = new WirePeer(transport);
        foreach (byte[] fragment in fragments) transport.Send(peer.Endpoint, PacketType.WorldBootstrap, fragment);
        var received = new List<byte[]>(); var events = new HashSet<uint>();
        IPEndPoint sender = new(IPAddress.Any, 0);
        var clock = Stopwatch.StartNew();
        while (received.Count < fragments.Length && clock.ElapsedMilliseconds < 3000)
        {
            if (peer.Wire.Available == 0) { Thread.Sleep(2); continue; }
            byte[] wire = peer.Wire.Receive(ref sender);
            if (!NetHeader.TryRead(wire, out var header) || header.Type != PacketType.WorldBootstrap) continue;
            Check((header.Flags & NetHeaderFlags.Reliable) != 0 && wire.Length <= NetConfig.MaxPacketSize,
                "objective fragment travels in a reliable datagram within the unchanged wire ceiling");
            peer.Connection.Receive(header, 0); peer.Ack();
            uint eventId = BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(NetHeader.Size));
            if (!events.Add(eventId)) continue;
            byte[] payload = wire.AsSpan(NetHeader.Size + NetReliableChannel.EventIdSize).ToArray();
            Check(WorldBootstrapIdentity.TryRead(payload, out var stamp) && stamp == identity
                && payload[WorldBootstrapIdentity.Size] == 3, "received objective fragment retains the current full bootstrap identity");
            received.Add(payload);
        }
        Check(received.Count == fragments.Length, "all objective fragments pass the production reliable queue and arrive over UDP");
        Check(SpinWait.SpinUntil(() => transport.ReliableStats(peer.Endpoint)?.Pending == 0, 2000),
            "every objective fragment completes through its actual datagram ACK");
        var assembly = new WorldBootstrapObjectives();
        for (int i = received.Count - 1; i >= 0; i--)
        {
            ReadOnlySpan<byte> part = received[i].AsSpan(WorldBootstrapIdentity.Size + 1);
            Check(assembly.Accept(part, identity), "valid objective fragment assembles under ordinary unordered delivery");
            Check(assembly.Accept(part, identity), "duplicate objective fragment remains idempotent");
            if (i > 0) Check(assembly.World == null, "partial objective world cannot complete bootstrap readiness");
        }
        Check(assembly.World != null && assembly.World.MatchId == identity.Start.MatchId
            && assembly.World.Epoch == identity.Start.AuthorityEpoch && assembly.World.Tick == identity.AuthorityFrame
            && assembly.World.Encode().AsSpan().SequenceEqual(encoded), "complete objective world matches its current identity and every original fact");
        Check(transport.PacketKindDiagnostics!.Capture(PacketType.WorldBootstrap).PeakBytesSent == NetConfig.MaxPacketSize,
            "full objective fragment uses exactly the live datagram ceiling including event and envelope overhead");
    }

    private sealed class WirePeer : IDisposable
    {
        public readonly UdpClient Wire = new(0);
        public readonly IPEndPoint Endpoint, Server;
        public readonly NetConnection Connection;
        private IPEndPoint _from = new(IPAddress.Any, 0);
        public WirePeer(NetTransport transport)
        {
            Wire.Client.ReceiveTimeout = 2000;
            Endpoint = new(IPAddress.Loopback, ((IPEndPoint)Wire.Client.LocalEndPoint!).Port);
            Server = new(IPAddress.Loopback, transport.LocalPort);
            transport.Send(Endpoint, PacketType.Welcome, new byte[17]);
            byte[] welcome = Wire.Receive(ref _from);
            Check(NetHeader.TryRead(welcome, out var header), "loopback Welcome has a connection envelope");
            Connection = new(Server, header.ConnectionId);
            Connection.Receive(header, 0);
            Ack();
            Check(SpinWait.SpinUntil(() => transport.ReliableStats(Endpoint)?.Pending == 0, 2000),
                "loopback Welcome acknowledged before close");
        }
        public void Ack()
        {
            byte[] ack = new byte[NetHeader.Size];
            Connection.Send(0, 0, NetHeaderFlags.AckOnly).Write(ack);
            Wire.Send(ack, Server);
        }
        public NetHeader Next(PacketType type, int timeout = 2000)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeout)
            {
                if (Wire.Available == 0) { Thread.Sleep(2); continue; }
                if (NetHeader.TryRead(Wire.Receive(ref _from), out var header) && header.Type == type) return header;
            }
            throw new TimeoutException($"No {type} datagram within {timeout}ms");
        }
        public NetHeader Send(PacketType type, uint eventId = 0, ReadOnlySpan<byte> payload = default)
        {
            bool reliable = NetReliableChannel.IsReliable(type);
            int offset = NetHeader.Size + (reliable ? 4 : 0);
            byte[] packet = new byte[offset + payload.Length];
            NetHeader header = Connection.Send(type, 0, reliable ? NetHeaderFlags.Reliable : NetHeaderFlags.None, reliable ? eventId : null);
            header.Write(packet);
            if (reliable) BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(NetHeader.Size), eventId);
            payload.CopyTo(packet.AsSpan(offset));
            Wire.Send(packet, Server);
            return header;
        }
        public void Dispose() => Wire.Dispose();
    }

    private static void TerminalRetry(PacketType type)
    {
        using var transport = new NetTransport(0);
        transport.EnablePacketKindDiagnostics();
        using var peer = new WirePeer(transport);
        transport.Send(peer.Endpoint, PacketType.Roster, new byte[] { 1 });
        _ = peer.Next(PacketType.Roster); // Deliberately left unacknowledged: it is obsolete at close.
        transport.Send(peer.Endpoint, type, type == PacketType.Bye
            ? ReadOnlySpan<byte>.Empty : new byte[] { RefusedPacket.ReasonKicked, 1, 4 });
        transport.RetireConnection(peer.Endpoint);
        NetHeader first = peer.Next(type); // One lost terminal application/ACK.
        NetHeader retry = peer.Next(type);
        Check(first.Sequence != retry.Sequence, $"retired {type} retries with a fresh datagram sequence");
        peer.Connection.Receive(retry, 0);
        peer.Ack();
        Check(SpinWait.SpinUntil(() => transport.ReliableStats(peer.Endpoint)?.Pending == 0, 2000),
            $"retired {type} finishes on retry ACK");
        var sent = transport.PacketKindDiagnostics!.Capture(type);
        Check(sent.PacketsSent >= 2 && sent.PeakBytesSent == NetHeader.Size + 4 + (type == PacketType.Bye ? 0 : 3),
            $"wire {type} counters include successful retry datagrams and envelope bytes");
        long packets = transport.Telemetry.Capture().PacketsSent;
        transport.Send(peer.Endpoint, PacketType.Snapshot, new byte[NetConfig.MaxPacketSize + 1]);
        Check(transport.Telemetry.Capture().PacketsSent == packets,
            "retired connection discards obsolete outgoing gameplay before payload validation");
        bool delivered = false;
        long received = transport.Telemetry.Capture().PacketsReceived;
        peer.Send(PacketType.Intent);
        Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().PacketsReceived > received, 2000),
            "retired gameplay datagram reaches the transport");
        foreach (var packet in transport.Drain()) delivered |= packet.Type == PacketType.Intent;
        Check(!delivered, "retired connection discards incoming gameplay");
        Check(SpinWait.SpinUntil(() => transport.ConnectionStats(peer.Endpoint) == null, 2500),
            "terminal tombstone expires within its fixed bounded lifetime");
    }

    private static void IncomingCloseAck()
    {
        using var transport = new NetTransport(0);
        using var peer = new WirePeer(transport);
        peer.Send(PacketType.Bye, 1);
        bool closed = false;
        Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in transport.Drain()) closed |= packet.Type == PacketType.Bye;
            return closed;
        }, 2000), "incoming reliable Bye reaches normal close handler");
        transport.RetireConnection(peer.Endpoint);
        _ = peer.Next(0); // Ignore the first ACK; sender's retry must still receive an ACK.
        peer.Send(PacketType.Bye, 1);
        NetHeader ack = peer.Next(0);
        Check((ack.Flags & NetHeaderFlags.AckOnly) != 0 && (ack.Flags & NetHeaderFlags.AckValid) != 0,
            "retirement keeps enough connection state to ACK a duplicate final event");
        bool repeated = false;
        foreach (var packet in transport.Drain()) repeated |= packet.Type == PacketType.Bye;
        Check(!repeated, "retired duplicate Bye is ACKed without a second application");
    }

    private static void RepeatedLobbyClose()
    {
        var server = new DedicatedServer(0, 4) { SessionPolicy = ServerSessionPolicy.Lobby };
        typeof(DedicatedServer).GetField("_controlPlaneOnlyForTests", Hidden)!.SetValue(server, true);
        using var transport = new NetTransport(0);
        typeof(DedicatedServer).GetField("_transport", Hidden)!.SetValue(server, transport);
        typeof(DedicatedServer).GetField("_phase", Hidden)!.SetValue(server, SessionPhase.Lobby);
        object definition = typeof(DedicatedServer).GetMethod("DefinitionFor", Hidden)!.Invoke(server, new object[] { new RotationEntry() })!;
        typeof(DedicatedServer).GetField("_lobbyMatch", Hidden)!.SetValue(server, definition);
        var peers = (IList)typeof(DedicatedServer).GetField("_peers", Hidden)!.GetValue(server)!;
        var hello = typeof(DedicatedServer).GetMethod("HandleHello", Hidden)!;
        var close = typeof(DedicatedServer).GetMethod("CloseLobbySession", Hidden)!;
        var sockets = new List<UdpClient>();
        var endpoints = new List<IPEndPoint>();
        try
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int player = 0; player < 4; player++)
                {
                    var wire = new UdpClient(0); sockets.Add(wire);
                    var endpoint = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)wire.Client.LocalEndPoint!).Port);
                    endpoints.Add(endpoint);
                    byte[] packet = new byte[24]; packet[0] = (byte)PacketType.Hello;
                    packet[1] = NetConfig.ProtocolVersion; packet[2] = 255;
                    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), (uint)(cycle * 4 + player + 1));
                    hello.Invoke(server, new object[] { new ReceivedPacket(endpoint, packet, packet.Length), (double)cycle, -1 });
                }
                Check(peers.Count == 4, "fresh endpoint cycle admits four normal lobby members");
                close.Invoke(server, new object[] { peers[0]! });
                Check(peers.Count == 0, "owner close clears lobby membership");
                foreach (var endpoint in endpoints)
                    Check(transport.ReliableStats(endpoint) is { Pending: 1 },
                        "each closed lobby member retains only its final goodbye retry");
            }
            Check(SpinWait.SpinUntil(() => endpoints.TrueForAll(endpoint => transport.ConnectionStats(endpoint) == null), 3000),
                "repeated owner closure expires every old transport connection");
            byte[] rejoin = new byte[24]; rejoin[0] = (byte)PacketType.Hello;
            rejoin[1] = NetConfig.ProtocolVersion; rejoin[2] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(rejoin.AsSpan(3), 1);
            hello.Invoke(server, new object[] { new ReceivedPacket(endpoints[0], rejoin, rejoin.Length), 4.0, -1 });
            Check(peers.Count == 1 && transport.ConnectionStats(endpoints[0]) != null,
                "normal admission succeeds after repeated closure releases prior connections");
        }
        finally { foreach (var wire in sockets) wire.Dispose(); }
    }

    private static void ConnectionCapacity()
    {
        // Component boundary state only: no datagrams, flood, or third-party
        // endpoint. The production admission path must safely refuse a full table.
        using var transport = new NetTransport(0, playbackOnly: true);
        for (int i = 0; i < NetTransport.MaximumConnections; i++)
            transport.Send(new IPEndPoint(IPAddress.Loopback, 40000 + i), PacketType.Welcome, new byte[17]);
        var newEndpoint = new IPEndPoint(IPAddress.Loopback, 45000);
        Check(!transport.CanAdmitConnection(newEndpoint), "bounded full connection table exposes admission backpressure");
        transport.Send(newEndpoint, PacketType.Welcome, new byte[17]);
        Check(transport.ConnectionStats(newEndpoint) == null,
            "Welcome at full capacity is safely refused without a process-ending exception");
        var existing = new IPEndPoint(IPAddress.Loopback, 40000);
        ulong oldId = transport.ConnectionStats(existing)!.Value.ConnectionId;
        byte[] replacement = new byte[17]; BinaryPrimitives.WriteUInt32LittleEndian(replacement.AsSpan(1), 17);
        transport.Send(existing, PacketType.Welcome, replacement);
        Check(transport.CanAdmitConnection(existing) && transport.ConnectionStats(existing)!.Value.ConnectionId != oldId,
            "same-endpoint replacement reuses its bounded table position at capacity");
        var server = new DedicatedServer(0, 4) { SessionPolicy = ServerSessionPolicy.Lobby };
        typeof(DedicatedServer).GetField("_transport", Hidden)!.SetValue(server, transport);
        byte[] hello = new byte[24]; hello[0] = (byte)PacketType.Hello;
        hello[1] = NetConfig.ProtocolVersion; hello[2] = 255;
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(3), 9);
        typeof(DedicatedServer).GetMethod("HandleHello", Hidden)!.Invoke(server,
            new object[] { new ReceivedPacket(newEndpoint, hello, hello.Length), 0.0, -1 });
        Check(server.PeerCount == 0, "server capacity preflight leaves membership unchanged when admission cannot be bound");
    }

    private static void EndpointIdentityBinding()
    {
        // Ordinary handler lifecycle: two sequential clients reuse one local
        // endpoint, with a normal leave/silence boundary between admissions.
        var server = new DedicatedServer(0, 4) { SessionPolicy = ServerSessionPolicy.Lobby };
        typeof(DedicatedServer).GetField("_controlPlaneOnlyForTests", Hidden)!.SetValue(server, true);
        using var transport = new NetTransport(0, playbackOnly: true);
        typeof(DedicatedServer).GetField("_transport", Hidden)!.SetValue(server, transport);
        typeof(DedicatedServer).GetField("_phase", Hidden)!.SetValue(server, SessionPhase.Lobby);
        object definition = typeof(DedicatedServer).GetMethod("DefinitionFor", Hidden)!.Invoke(server, new object[] { new RotationEntry() })!;
        typeof(DedicatedServer).GetField("_lobbyMatch", Hidden)!.SetValue(server, definition);
        var peers = (IList)typeof(DedicatedServer).GetField("_peers", Hidden)!.GetValue(server)!;
        var endpoint = new IPEndPoint(IPAddress.Loopback, 47000);
        var hello = typeof(DedicatedServer).GetMethod("HandleHello", Hidden)!;
        void Join(uint clientId, double now)
        {
            byte[] packet = new byte[24]; packet[0] = (byte)PacketType.Hello;
            packet[1] = NetConfig.ProtocolVersion; packet[2] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), clientId);
            hello.Invoke(server, new object[] { new ReceivedPacket(endpoint, packet, packet.Length), now, -1 });
        }
        Join(10, 1);
        object original = peers[0]!;
        Type type = original.GetType();
        ulong firstConnection = transport.ConnectionStats(endpoint)!.Value.ConnectionId;
        Join(20, 2);
        Check(peers.Count == 1 && ReferenceEquals(peers[0], original)
            && (uint)type.GetField("ClientId")!.GetValue(original)! == 10
            && transport.ConnectionStats(endpoint)!.Value.ConnectionId == firstConnection,
            "changed-ID Hello preserves the live occupant and connection identity");
        Check((double)type.GetField("LastSeen")!.GetValue(original)! == 1,
            "rejected identity change does not refresh the current occupant's liveness");
        typeof(DedicatedServer).GetMethod("Handle", Hidden)!.Invoke(server,
            new object[] { new ReceivedPacket(endpoint, new byte[] { (byte)PacketType.Bye }, 1,
                connectionId: firstConnection), 2.5 });
        Check(peers.Count == 0, "normal connection-bound leave releases endpoint identity");
        Join(20, 3);
        object replacement = peers[0]!;
        Check(!ReferenceEquals(replacement, original) && (uint)type.GetField("ClientId")!.GetValue(replacement)! == 20
            && transport.ConnectionStats(endpoint)!.Value.ConnectionId != firstConnection,
            "next ordinary client may reuse endpoint after normal leave with a fresh connection");
        Join(20, 4);
        Check(ReferenceEquals(peers[0], replacement) && (double)type.GetField("LastSeen")!.GetValue(replacement)! == 4,
            "same client identity may reannounce and refresh its existing admission");
        Join(30, 4 + NetConfig.TimeoutSeconds);
        Check(ReferenceEquals(peers[0], replacement), "identity replacement respects normal strict timeout boundary");
        Join(30, 4 + NetConfig.TimeoutSeconds + .01);
        Check(!ReferenceEquals(peers[0], replacement) && (uint)type.GetField("ClientId")!.GetValue(peers[0])! == 30,
            "expired normal silence allows a fresh client identity at the reused endpoint");
    }

    private static void FailedClientStart()
    {
        foreach (int invalidPort in new[] { -1, 65536 })
        {
            NetSession.StartClient("127.0.0.1", invalidPort);
            Check(NetSession.Role == NetRole.Offline && NetSession.LastError != null
                && typeof(NetSession).GetField("_transport", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) == null,
                "failed client endpoint validation leaves no socket or worker owner");
        }
        using var server = new UdpClient(0);
        NetSession.StartClient("127.0.0.1", ((IPEndPoint)server.Client.LocalEndPoint!).Port);
        Check(NetSession.Role == NetRole.Client && NetSession.LastError == null,
            "valid client start succeeds after failed configuration");
        NetSession.Stop();
    }

    private static void RoleTransitions()
    {
        var server = new DedicatedServer(0, 4) { SessionPolicy = ServerSessionPolicy.Lobby };
        typeof(DedicatedServer).GetField("_controlPlaneOnlyForTests", Hidden)!.SetValue(server, true);
        using var transport = new NetTransport(0, playbackOnly: true);
        typeof(DedicatedServer).GetField("_transport", Hidden)!.SetValue(server, transport);
        typeof(DedicatedServer).GetField("_phase", Hidden)!.SetValue(server, SessionPhase.Lobby);
        var definition = (MatchDefinition)typeof(DedicatedServer).GetMethod("DefinitionFor", Hidden)!.Invoke(server, new object[] { new RotationEntry() })!;
        definition = definition with { Mode = GameMode.BattleTeams, Format = MatchFormat.OneVsOne };
        typeof(DedicatedServer).GetField("_lobbyMatch", Hidden)!.SetValue(server, definition);
        var peers = (IList)typeof(DedicatedServer).GetField("_peers", Hidden)!.GetValue(server)!;
        var hello = typeof(DedicatedServer).GetMethod("HandleHello", Hidden)!;
        void Join(int index, bool spectating)
        {
            byte[] packet = new byte[24]; packet[0] = (byte)PacketType.Hello;
            packet[1] = NetConfig.ProtocolVersion; packet[2] = 255; packet[23] = spectating ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), (uint)(index + 10));
            hello.Invoke(server, new object[] { new ReceivedPacket(new IPEndPoint(IPAddress.Loopback, 49000 + index), packet, packet.Length), (double)index, -1 });
        }
        Join(0, false); Join(1, false); Join(2, true);
        Check(peers.Count == 3, "spectator admission does not consume a full duel team seat");
        object spectator = peers[2]!;
        Type peerType = spectator.GetType();
        bool Spectating(object peer) => (bool)peerType.GetField("Spectating")!.GetValue(peer)!;
        sbyte Team(object peer) => (sbyte)peerType.GetField("TeamIndex")!.GetValue(peer)!;
        bool Ready(object peer) => (bool)peerType.GetField("LobbyReady")!.GetValue(peer)!;
        Check(Spectating(spectator) && Team(spectator) == -1 && !Ready(spectator),
            "spectator has no combat team or ready vote");
        Join(2, false);
        Check(Spectating(spectator) && Team(spectator) == -1,
            "full team layout rejects becoming a combatant while retaining valid spectator admission");
        object formerPlayer = peers[0]!;
        peerType.GetField("LobbyReady")!.SetValue(formerPlayer, true);
        Join(0, true);
        Check(Spectating(formerPlayer) && Team(formerPlayer) == -1 && !Ready(formerPlayer),
            "changing to spectator relinquishes team and ready state");
        Join(2, false);
        Check(!Spectating(spectator) && Team(spectator) >= 0 && !Ready(spectator),
            "becoming a player claims the now-free valid combat team");
    }

    private static void MatchedLoadTimeout()
    {
        using var server = new NetTransport(0);
        NetSession.StartClient("127.0.0.1", server.LocalPort);
        var client = (NetTransport)typeof(NetSession).GetField("_transport", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var endpoint = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        var state = new SessionStatePacket
        {
            MatchId = 42, AuthorityEpoch = 9, StartGeneration = 17, Revision = 1,
            Phase = SessionPhase.Starting, StartStage = StartStage.Loading, Policy = ServerSessionPolicy.Lobby,
            MaxPlayers = 4, OwnerSlot = 1, ExpectedParticipants = 2,
            WorldProfile = MatchWorldProfile.Resolve(4),
            Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, Format = MatchFormat.FreeForAll }
        };
        NetSession.ApplySessionState(state);
        byte[] welcome = new byte[17]; welcome[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), NetSession.ClientId);
        BinaryPrimitives.WriteUInt16LittleEndian(welcome.AsSpan(5), state.MatchId);
        BinaryPrimitives.WriteUInt64LittleEndian(welcome.AsSpan(7), state.AuthorityEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(welcome.AsSpan(15), 7);
        server.Send(endpoint, PacketType.Welcome, welcome);
        Check(SpinWait.SpinUntil(() => { NetSession.Update(0); return NetSession.LocalSlot == 1; }, 2000),
            "load-timeout fixture uses real UDP Welcome to assign a client occupant");
        Check(NetPlayerLifecycle.Generation(1) == 7, "Welcome installs load-timeout occupant fence");
        void DeliverRefusal(ReadOnlySpan<byte> payload)
        {
            long before = client.Telemetry.Capture().PacketsReceived;
            server.Send(endpoint, PacketType.Refused, payload);
            Check(SpinWait.SpinUntil(() =>
            {
                NetSession.Update(0);
                return client.Telemetry.Capture().PacketsReceived > before
                    && server.ReliableStats(endpoint)?.Pending == 0 && client.Telemetry.Capture().QueueCurrent == 0;
            }, 2000), "normal server refusal reached and drained through the client transport");
        }
        DeliverRefusal(new byte[] { RefusedPacket.ReasonFull, 4, 4 });
        Check(!NetSession.Refused, "ordinary delayed pre-admission refusal cannot remove an admitted client");
        var current = new LoadFailurePacket(42, 9, 17, 7, RefusedPacket.ReasonLoadTimeout);
        foreach (var stale in new[] { current with { MatchId = 41 }, current with { AuthorityEpoch = 8 },
            current with { StartGeneration = 16 }, current with { RecipientSlotGeneration = 6 } })
        {
            byte[] payload = new byte[LoadFailurePacket.Size]; stale.Write(payload); DeliverRefusal(payload);
            Check(!NetSession.Refused, "stale load timeout cannot terminate current start/occupant");
        }
        byte[] matched = new byte[LoadFailurePacket.Size]; current.Write(matched); DeliverRefusal(matched);
        Check(NetSession.Refused && NetSession.RefusedReason.Reason == RefusedPacket.ReasonLoadTimeout,
            "matched post-Welcome hard load timeout becomes a user-visible refusal");
        double maintenance = NetSession.Clock - 10;
        typeof(NetSession).GetField("_lastClientMaintenance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, maintenance);
        typeof(NetSession).GetField("_lastServerPacket", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, maintenance);
        long reannouncements = NetSession.ReAnnouncements;
        NetSession.Update(0);
        Check(NetSession.Refused && NetSession.ReAnnouncements == reannouncements,
            "terminal load refusal stops automatic readmission while failure UI handles cleanup");
        NetSession.Stop();
    }

    private static void OptionalWireCounters()
    {
        bool debug = NetDiagnostics.Enabled; NetDiagnostics.Enabled = false;
        try
        {
            using var transport = new NetTransport(0);
            if (!MphRead.Mods.Network.Telemetry.ProductionTelemetry.Enabled)
                Check(transport.PacketKindDiagnostics == null, "packet-kind tables are absent when diagnostics are disabled");
            transport.EnablePacketKindDiagnostics();
            using var wire = new UdpClient(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)wire.Client.LocalEndPoint!).Port);
            transport.Send(endpoint, PacketType.StatusQuery, new byte[] { 3, 4 });
            Check(transport.PacketKindDiagnostics!.Capture(PacketType.StatusQuery) is { PacketsSent: 1, BytesSent: 3, PeakBytesSent: 3 },
                "unsequenced wire counters measure actual successful UDP bytes");
            wire.Send(new byte[] { (byte)PacketType.StatusQuery, 7 }, new IPEndPoint(IPAddress.Loopback, transport.LocalPort));
            Check(SpinWait.SpinUntil(() => transport.PacketKindDiagnostics.Capture(PacketType.StatusQuery).PacketsReceived == 1, 2000)
                && transport.PacketKindDiagnostics.Capture(PacketType.StatusQuery) is { BytesReceived: 2, PeakBytesReceived: 2 },
                "received wire counters measure the original datagram");
            using var playback = new NetTransport(0, playbackOnly: true);
            playback.EnablePacketKindDiagnostics();
            playback.Send(endpoint, PacketType.StatusQuery, new byte[] { 8 });
            Check(playback.PacketKindDiagnostics!.Capture(PacketType.StatusQuery).PacketsSent == 0,
                "playback without a socket never records an actual wire send");
        }
        finally { NetDiagnostics.Enabled = debug; }
    }

    private static void DirectoryFarewellScope()
    {
        using var gameplay = new NetTransport(0);
        using var directory = new NetTransport(0);
        directory.EnableDirectoryFarewells();
        using var reporter = new UdpClient(0);
        byte[] farewell = { (byte)PacketType.Bye, 0x34, 0x12 };
        reporter.Send(farewell, new IPEndPoint(IPAddress.Loopback, gameplay.LocalPort));
        Check(SpinWait.SpinUntil(() => gameplay.Telemetry.Capture().Invalid == 1, 2000),
            "ordinary peer transport still rejects a bare directory farewell");
        bool peerBye = false;
        foreach (var packet in gameplay.Drain()) peerBye |= packet.Type == PacketType.Bye;
        Check(!peerBye, "bare directory farewell cannot remove an ordinary gameplay peer");
        reporter.Send(farewell, new IPEndPoint(IPAddress.Loopback, directory.LocalPort));
        Check(SpinWait.SpinUntil(() => directory.Telemetry.Capture().QueueCurrent == 1, 2000),
            "directory opt-in accepts its established three-byte reporter farewell");
        bool directoryBye = false;
        foreach (var packet in directory.Drain())
            directoryBye |= packet.Type == PacketType.Bye && packet.Payload.Length == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload) == 0x1234;
        Check(directoryBye, "directory farewell preserves reporter port for endpoint authentication");
        reporter.Send(new byte[] { (byte)PacketType.Bye, 0x34 },
            new IPEndPoint(IPAddress.Loopback, directory.LocalPort));
        Check(SpinWait.SpinUntil(() => directory.Telemetry.Capture().Invalid == 1, 2000),
            "directory opt-in requires the exact existing farewell datagram size");
    }

    private static void ResumePreservesAdmission()
    {
        using var transport = new NetTransport(0);
        transport.EnableRealtimeStateCoalescing();
        using var peer = new WirePeer(transport);
        int port = transport.LocalPort;
        transport.Send(peer.Endpoint, PacketType.Roster, new byte[] { 1 });
        _ = peer.Next(PacketType.Roster);
        transport.Send(peer.Endpoint, PacketType.Intent, ReadOnlySpan<byte>.Empty);
        NetHeader before = peer.Next(PacketType.Intent);
        peer.Wire.Send(new byte[] { (byte)PacketType.StatusQuery, 7 }, peer.Server);
        byte[] stale = new byte[NetHeader.Size];
        peer.Connection.Send(PacketType.Snapshot, 0).Write(stale);
        peer.Wire.Send(stale, peer.Server);
        var state = typeof(NetTransport).GetField("_latestSnapshot", Hidden)!;
        Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().QueueCurrent == 1
            && state.GetValue(transport) != null, 2000),
            "resume fixture caches one ordinary arrival and one coalesced snapshot");
        var held = (NetFaultQueue<ReceivedPacket>)typeof(NetTransport).GetField("_heldIn", Hidden)!.GetValue(transport)!;
        held.Enqueue(0, new ReceivedPacket(peer.Endpoint, new byte[] { (byte)PacketType.StatusQuery }, 1),
            extraDelayMs: double.MaxValue, lossOverride: 0);
        transport.DiscardIncoming();
        bool cached = false;
        foreach (var packet in transport.Drain()) cached = true;
        Check(!cached && held.Count == 0 && state.GetValue(transport) == null,
            "resume discards queued, held and coalesced stale arrivals");
        held.Enqueue(0, new ReceivedPacket(peer.Endpoint, new byte[] { (byte)PacketType.StatusQuery }, 1), lossOverride: 0);
        Check(held.TryDequeue(0, out _), "resume removes cancelled simulated arrival timing from the next fresh packet");
        Check(transport.LocalPort == port && transport.ConnectionStats(peer.Endpoint)?.ConnectionId == before.ConnectionId
            && transport.ReliableStats(peer.Endpoint)?.Pending == 1,
            "resume preserves the existing socket, admission and outgoing reliable event");
        long duplicates = transport.ConnectionStats(peer.Endpoint)!.Value.Duplicates;
        peer.Wire.Send(stale, peer.Server);
        Check(SpinWait.SpinUntil(() => transport.ConnectionStats(peer.Endpoint)?.Duplicates > duplicates, 2000)
            && state.GetValue(transport) == null,
            "resume preserves the receive window and rejects a previously observed snapshot");
        transport.Send(peer.Endpoint, PacketType.Intent, ReadOnlySpan<byte>.Empty);
        NetHeader after = peer.Next(PacketType.Intent);
        Check(after.ConnectionId == before.ConnectionId && SequenceMath.Newer(after.Sequence, before.Sequence),
            "resume continues the established outgoing datagram sequence");
        using var playback = new NetTransport(0, playbackOnly: true);
        playback.EnqueueForPlayback(new byte[] { (byte)PacketType.StatusQuery, 3 }, 2);
        playback.DiscardIncoming();
        bool replayed = false;
        foreach (var packet in playback.Drain()) replayed = true;
        Check(!replayed, "incoming discard also releases queued playback arrivals");
    }

    private static void ResumePreservesTerminal(PacketType type)
    {
        using var transport = new NetTransport(0);
        using var peer = new WirePeer(transport);
        byte[] payload = type == PacketType.Refused
            ? new byte[] { RefusedPacket.ReasonKicked, 1, 4 } : Array.Empty<byte>();
        peer.Send(type, eventId: 51, payload: payload);
        Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().QueueCurrent == 1, 2000),
            $"resume fixture accepts reliable {type} before cache discard");
        transport.DiscardIncoming();
        int delivered = 0;
        foreach (var packet in transport.Drain()) if (packet.Type == type) delivered++;
        Check(delivered == 1, $"resume preserves the already acknowledged {type} terminal notice");
        NetHeader retry = peer.Send(type, eventId: 51, payload: payload);
        // An ACK-only response proves the retry reached receive/ACK processing.
        NetHeader ack;
        do { ack = peer.Next(0); }
        while (!NetReceiveWindow.Acknowledges(retry.Sequence, ack.Ack, ack.AckBits));
        delivered = 0;
        foreach (var packet in transport.Drain()) if (packet.Type == type) delivered++;
        Check(delivered == 0, $"resume still delivers reliable {type} exactly once after a duplicate attempt");
    }
}
