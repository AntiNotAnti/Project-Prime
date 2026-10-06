using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ServerEngineeringTests
{
    private static void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
    public static int Run()
    {
        try
        {
            ReplayFailureLatch(); ReplayMarkerPrefixes(); SpectatorLobby(); ClientRosterRoles(); QuickPlay(); CompactIntents();
            ProbeBoundAndCancellation().GetAwaiter().GetResult(); LoopDiagnostics();
            Console.WriteLine("PASS: per-match replay failure latch, spectator combat eligibility, Quick Play policy, bounded cancellable probes and loop diagnostics");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { ServerReplayRecorder.Stop(); }
    }

    private static void ReplayFailureLatch()
    {
        Check(ServerReplayRecorder.Enabled, "default server recording is enabled");
        int identities = 0;
        string absent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-map.bin");
        Func<string, ulong> missing = _ =>
        {
            identities++;
            using var file = File.OpenRead(absent); // Genuine I/O failure, before replica construction.
            return 0;
        };
        Check(!ServerReplayRecorder.BeginMatch(62000, 991, "test-room", missing), "missing map fails once");
        string? first = ServerReplayRecorder.LastError;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 600; tick++)
            Check(!ServerReplayRecorder.BeginMatch(62000, 991, "test-room", missing), "same match remains disabled");
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(identities == 1 && allocated < 4096, "failed match does not hash or allocate a new capture each tick");
        Check(ServerReplayRecorder.Diagnostics.State == ServerReplayRecorder.RecordingState.Failed
            && ServerReplayRecorder.Diagnostics.Failures == 1 && !ServerReplayRecorder.IsRecording
            && ServerReplayRecorder.LastError == first, "first error and failure diagnostics persist");
        ServerReplayRecorder.Stop(); // Teardown/reset must not clear this match's latch.
        Check(!ServerReplayRecorder.BeginMatch(62000, 991, "test-room", missing), "reset cannot retry failed match");
        Check(ServerReplayRecorder.BeginMatch(62001, 991, "test-room", _ => { identities++; return 123; }), "new match gets one attempt");
        Check(ServerReplayRecorder.TryGetMapHash(62001, 991, "test-room", out ulong hash) && hash == 123,
            "exact authoritative match shares its prepared replay hash");
        Check(!ServerReplayRecorder.TryGetMapHash(62001, 992, "test-room", out _)
            && !ServerReplayRecorder.TryGetMapHash(62001, 991, "other-room", out _), "hash cannot cross epoch or room");
        ServerReplayRecorder.Stop(matchEnded: true);
        Check(!ServerReplayRecorder.BeginMatch(62001, 991, "test-room", missing), "completed match is not reopened");
        Check(ServerReplayRecorder.BeginMatch(62001, 992, "test-room", _ => { identities++; return 456; }), "new epoch gets one attempt");
        Check(identities == 3, "only distinct match identities run identification");
        ServerReplayRecorder.Stop();
    }

    private static void ReplayMarkerPrefixes()
    {
        var matchProperty = typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!;
        object? original = matchProperty.GetValue(null);
        var accept = typeof(ReplayCapture).GetMethod("AcceptWorld", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(ReplayAuthorityWorld)], null)!;
        var markers = new List<ReplayMarker>();
        void Observe(ReplayTimelineRecord record) { if (record.Marker is { } marker) markers.Add(marker); }
        ReplayCapture.Recorder.Accepted += Observe;
        ReplayNodeState Node(sbyte team) => new(12, team, -1, 0, new(1, 1, 1), 0, 0, 0, 0, 0, false, false);
        ReplayFlagState Flag(byte carrier) => new(11, Vector3.Zero, new(carrier, 1, 1), new(255, 0, 0), false, false, 0, 0);
        ReplayAuthorityWorld World(uint tick) => new() { MatchId = 7, Epoch = 9, Tick = tick, ActiveHardpointId = 12 };
        void Compare(GameMode mode, ReplayAuthorityWorld previous, ReplayAuthorityWorld current)
        {
            ReplayCapture.Reset(); markers.Clear();
            var match = new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = "TEST ARENA",
                NextRoomKey = "", Mode = (byte)mode };
            matchProperty.SetValue(null, match); ReplayCapture.Recorder.AcceptMatch(match, 0);
            accept.Invoke(null, [previous]); accept.Invoke(null, [current]);
        }
        try
        {
            var previous = World(1); var current = World(2);
            previous.Nodes = [Node(0), Node(0)]; current.Nodes = [Node(1), Node(1)];
            previous.NodeCount = current.NodeCount = 1;
            Compare(GameMode.Hardpoint, previous, current);
            Check(markers.Count(m => m.Kind == ReplayMarkerKind.HardpointCaptured) == 1,
                "node markers ignore stale tails in both capture buffers");
            current.NodeCount = 0;
            Compare(GameMode.Hardpoint, previous, current);
            Check(markers.Count == 0, "absent current node cannot emit capture from a stale tail");
            current.NodeCount = 1; previous.NodeCount = 0;
            Compare(GameMode.Hardpoint, previous, current);
            Check(markers.Count == 0, "absent previous node cannot emit capture from a stale tail");
            previous.Nodes = [Node(0)]; current.Nodes = [Node(1)]; previous.NodeCount = current.NodeCount = -1;
            Compare(GameMode.Hardpoint, previous, current);
            Check(markers.Count(m => m.Kind == ReplayMarkerKind.HardpointCaptured) == 1,
                "legacy node counts still use the full decoded array");

            previous = World(1); current = World(2);
            previous.Flags = [Flag(0), Flag(0)]; current.Flags = [Flag(1), Flag(1)];
            previous.FlagCount = current.FlagCount = 1;
            Compare(GameMode.Relic, previous, current);
            Check(markers.Count(m => m.Kind == ReplayMarkerKind.RelicDrop) == 1
                && markers.Count(m => m.Kind == ReplayMarkerKind.RelicPickup) == 1,
                "relic markers ignore stale tails in both capture buffers");
            current.FlagCount = 0;
            Compare(GameMode.Relic, previous, current);
            Check(markers.Count == 0, "absent current relic cannot emit a stale transition");
            current.FlagCount = 1; previous.FlagCount = 0;
            Compare(GameMode.Relic, previous, current);
            Check(markers.Count == 0, "absent previous relic cannot emit a stale transition");
            previous.Flags = [Flag(0)]; current.Flags = [Flag(1)]; previous.FlagCount = current.FlagCount = -1;
            Compare(GameMode.Relic, previous, current);
            Check(markers.Count == 2, "legacy flag counts still use the full decoded array");
        }
        finally
        {
            ReplayCapture.Recorder.Accepted -= Observe;
            ReplayCapture.Reset(); matchProperty.SetValue(null, original);
        }
    }

    private static void SpectatorLobby()
    {
        var match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.BattleTeams,
            Format = MatchFormat.OneVsOne };
        var roster = RosterPacket.Create(); roster.Count = 3;
        for (int i = 0; i < 3; i++) { roster.Slots[i] = (byte)i; roster.Names[i] = "Player " + i; }
        roster.Teams[0] = 0; roster.Teams[1] = 1; roster.Teams[2] = -1;
        roster.LobbyReady[0] = roster.LobbyReady[1] = true; roster.Roles[2] = 1;
        Check(LobbyRules.Validate(match, roster, true, out _) == LobbyResultCode.Ok,
            "spectator needs no combat team/readiness and does not overfill duel");
        byte[] wire = new byte[RosterPacket.Size]; roster.Write(wire);
        Check(RosterPacket.TryRead(wire, out var roundtrip) && roundtrip.IsSpectator(2), "spectator role survives validated roster wire round trip");
        int spectator = RosterPacket.HeaderSize + 2 * RosterPacket.EntrySize;
        int roles = spectator + 12 + RosterPacket.MaxNameBytes;
        wire[roles] = 2;
        Check(!RosterPacket.TryRead(wire, out _), "unknown spectator role rejected");
        wire[roles] = 1; wire[spectator + 9 + RosterPacket.MaxNameBytes] = 1;
        Check(!RosterPacket.TryRead(wire, out _), "bot cannot claim spectator role");
        wire[spectator + 9 + RosterPacket.MaxNameBytes] = 0;
        wire[spectator + 7 + RosterPacket.MaxNameBytes] = 0;
        Check(!RosterPacket.TryRead(wire, out _), "spectator cannot occupy combat team on wire");
        wire[spectator + 7 + RosterPacket.MaxNameBytes] = 255;
        wire[spectator + 8 + RosterPacket.MaxNameBytes] = 1;
        Check(!RosterPacket.TryRead(wire, out _), "spectator cannot claim combat readiness on wire");
        roster.Roles[1] = 1;
        Check(LobbyRules.Validate(match, roster, false, out _) == LobbyResultCode.NotEnoughPlayers,
            "spectator does not satisfy minimum combat population");
        roster.Roles[1] = 0; roster.LobbyReady[1] = false;
        Check(LobbyRules.Validate(match, roster, true, out _) == LobbyResultCode.PlayersNotReady,
            "unready player still blocks ready lobby");
        roster.Flags[1] = 1;
        Check(LobbyRules.Validate(match, roster, true, out _) == LobbyResultCode.Ok,
            "bot remains a combatant without human readiness");
        roster.Roles[2] = 0; roster.Teams[2] = 0;
        Check(LobbyRules.Validate(match, roster, false, out _) == LobbyResultCode.InvalidTeam,
            "real third combatant still exceeds duel team capacity");
    }

    private static void ClientRosterRoles()
    {
        NetSession.Stop();
        try
        {
            // Exercise the live client's actual handler without opening a socket or renderer.
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!.SetValue(null,
                new MatchStatePacket { MatchId = 71, AuthorityEpoch = 91 });
            var roster = RosterPacket.Create(); roster.MatchId = 71; roster.AuthorityEpoch = 91;
            roster.Revision = 1; roster.Count = 1; roster.Slots[0] = 3; roster.Generations[0] = 4;
            roster.Names[0] = "Observer"; roster.Roles[0] = 1;
            NetSession.ApplyRoster(roster);
            var checkpointRoster = NetSession.LobbyRoster();
            Check(NetSession.SlotSpectating[3] && checkpointRoster.Count == 1
                && checkpointRoster.Slots[0] == 3 && checkpointRoster.IsSpectator(0),
                "actual client roster handler preserves observer role in checkpoint roster");
            roster.Revision++; roster.Roles[0] = 0; roster.Teams[0] = 0; roster.LobbyReady[0] = true;
            NetSession.ApplyRoster(roster); checkpointRoster = NetSession.LobbyRoster();
            Check(!NetSession.SlotSpectating[3] && checkpointRoster.Count == 1 && !checkpointRoster.IsSpectator(0),
                "client spectator promotion clears observer role in checkpoint roster");
            roster.Revision++; roster.Roles[0] = 1; roster.Teams[0] = -1; roster.LobbyReady[0] = false;
            NetSession.ApplyRoster(roster);
            roster.Revision++; roster.Count = 0; NetSession.ApplyRoster(roster);
            Check(!NetSession.SlotSpectating[3] && !NetSession.SlotOccupied[3] && NetSession.LobbyRoster().Count == 0,
                "removed observer slot cannot retain spectator state in checkpoint roster");
            roster.Revision++; roster.Count = 1; NetSession.ApplyRoster(roster);
            Check(NetSession.SlotSpectating[3], "observer can reoccupy its cleared slot");
            NetSession.Stop();
            Check(!NetSession.SlotSpectating[3] && NetSession.LobbyRoster().Count == 0,
                "client stop clears observer role before another session");
        }
        finally { NetSession.Stop(); }
    }

    private static void QuickPlay()
    {
        bool Eligible(SessionPhase phase, bool jip, int players = 2, int protocol = NetConfig.ProtocolVersion)
            => ServerBrowserService.CanQuickPlay(new(default, new ServerStatus { Online = true,
                Protocol = protocol, Phase = phase, AllowJoinInProgress = jip, Players = players, MaxPlayers = 8 }));
        Check(Eligible(SessionPhase.Lobby, false), "open lobby is a Quick Play candidate");
        Check(Eligible(SessionPhase.InMatch, true), "active match allowing joins is a candidate");
        Check(!Eligible(SessionPhase.InMatch, false) && !Eligible(SessionPhase.Starting, true)
            && !Eligible(SessionPhase.PostMatch, true), "closed active/loading/postmatch sessions are skipped");
        Check(!Eligible(SessionPhase.Lobby, true, 8) && !Eligible(SessionPhase.Lobby, true, 2, NetConfig.ProtocolVersion + 1),
            "total slot cap and compatibility remain required");
    }

    private static void CompactIntents()
    {
        Check(IntentPacket.LegacyFullSize + 1 == 103 && IntentPacket.FullSize == 1447,
            "independent compact idle/max payload sizes");
        Check(1 + IntentPacket.FullSize == NetConfig.MaxPayloadSize
            && NetHeader.Size + 1 + IntentPacket.FullSize == NetConfig.MaxPacketSize,
            "maximum relayed fire history exactly fits datagram budget");
        byte[] canonical = new byte[IntentPacket.FullSize], compact = new byte[IntentPacket.FullSize + 1];
        for (int count = 0; count <= NetFireEvents.Capacity; count++)
        {
            var intent = IntentPacket.Read(NetArchitectureTests.IntentFixture());
            intent.Frame = 100; intent.FireEventCount = (byte)count; intent.HasFireEvents = true;
            for (int i = 0; i < count; i++) intent.FireEvents[i] = new((uint)(i + 1), (uint)(100 - count + i + 1),
                80, 64, FireEventKind.PressFire, 0, 0, 0, FireEvent.FlagPose,
                new Vector3(i, 2, 3), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, default,
                new Vector3(i, 5, 6), Vector3.UnitY, FireEvent.FlagSourcePose);
            Array.Fill(compact, (byte)0xCD); intent.Write(canonical);
            int size = intent.WriteNetwork(compact);
            Check(size == 103 + count * 84 && size == intent.EncodedSize && compact[size] == 0xCD,
                "compact write removes unused history and preserves outside span");
            Check(compact.AsSpan(0, size).SequenceEqual(canonical.AsSpan(0, size)), "compact prefix equals canonical record");
            Check(IntentPacket.TryReadNetwork(compact.AsSpan(0, size), out var decoded)
                && decoded.FireEventCount == count && decoded.HasFireEvents
                && decoded.Frame == intent.Frame && decoded.Position == intent.Position
                && decoded.AckFrame == intent.AckFrame && NetFireEvents.Validate(decoded), "every fire history count decodes faithfully");
            for (int i = 0; i < count; i++) Check(decoded.FireEvents[i].ShotId == intent.FireEvents[i].ShotId
                && decoded.FireEvents[i].SourcePosition == intent.FireEvents[i].SourcePosition
                && Vector3.Dot(decoded.FireEvents[i].SourceUp, Vector3.UnitY) > .9999f, "source shot pose survives compact history");
            Check(!IntentPacket.TryReadNetwork(compact.AsSpan(0, size - 1), out _)
                && !IntentPacket.TryReadNetwork(compact.AsSpan(0, size + 1), out _), "truncated and trailing compact bytes rejected");
            compact[IntentPacket.LegacyFullSize] = 17;
            Check(!IntentPacket.TryReadNetwork(compact.AsSpan(0, size), out _), "history count beyond capacity rejected");
        }
        Check(!IntentPacket.TryReadNetwork(NetArchitectureTests.IntentFixture(), out _), "legacy padded input is offline-only");
        var invalid = new IntentPacket { FireEventCount = 17 };
        try { invalid.WriteNetwork(compact); throw new InvalidDataException("oversized history encoded"); }
        catch (ArgumentException) { }
        Check(8 * 60 * (103 + NetHeader.Size) == 60960 && 8 * 7 * 60 * (104 + NetHeader.Size) == 430080,
            "eight-player idle ingress/relay sizing includes actual envelope and sender exclusion");
        Console.WriteLine("COMPACT idle owner=127B relay=128B; eight-player 60Hz ingress=60960B/s relay=430080B/s; max relay=1472B");

        NetLag.Configure("0"); NetLag.ConfigureLoss("0"); NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        using var server = new NetTransport(0); using var client = new NetTransport(0);
        Protocol17Tests.Connect(server, client);
        var endpoint = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        var full = new IntentPacket { Frame = 100, FireEventCount = 16 };
        for (int i = 0; i < 16; i++) full.FireEvents[i] = new((uint)(i + 1), 100, 80, 0, FireEventKind.PressFire, 0, 0, 0);
        byte[] relay = new byte[NetConfig.MaxPayloadSize]; relay[0] = 3;
        int length = full.WriteNetwork(relay.AsSpan(1));
        server.Send(endpoint, PacketType.SlotIntent, relay.AsSpan(0, length + 1));
        bool received = false;
        Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in client.Drain())
                if (packet.Type == PacketType.SlotIntent) received = packet.Payload.Length == 1448
                    && packet.Payload[0] == 3 && IntentPacket.TryReadNetwork(packet.Payload[1..], out var decoded)
                    && decoded.FireEventCount == 16;
            return received;
        }, 2000), "maximum compact relay crosses real transport at MTU boundary");
    }

    private static async Task ProbeBoundAndCancellation()
    {
        var listings = Enumerable.Range(0, 64).Select(_ => new MasterListing { Address = "127.0.0.1", Port = 1 }).ToArray();
        int current = 0, maximum = 0, completed = 0;
        await ServerBrowserService.ProbeListingsAsync(listings, async (_, _, token) =>
        {
            int active = Interlocked.Increment(ref current), observed;
            do { observed = Volatile.Read(ref maximum); if (active <= observed) break; }
            while (Interlocked.CompareExchange(ref maximum, active, observed) != observed);
            try { await Task.Delay(5, token); Interlocked.Increment(ref completed); }
            finally { Interlocked.Decrement(ref current); }
        }, default);
        Check(maximum <= ServerBrowserService.MaximumConcurrentProbes && completed == listings.Length,
            "directory scan has bounded workers and probes every row");
        using var cancel = new CancellationTokenSource();
        int started = 0;
        Task scan = ServerBrowserService.ProbeListingsAsync(listings, async (_, _, token) =>
        { Interlocked.Increment(ref started); await Task.Delay(10000, token); }, cancel.Token);
        cancel.Cancel();
        try { await scan; throw new InvalidDataException("cancelled scan succeeded"); }
        catch (OperationCanceledException) { }
        Check(started <= ServerBrowserService.MaximumConcurrentProbes, "cancellation does not start queued rows");

        // A real unresponsive UDP endpoint keeps Receive blocked until the
        // cancellation callback closes the socket; this must not wait 10 seconds.
        using var blackhole = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)blackhole.Client.LocalEndPoint!).Port;
        using var probeCancel = new CancellationTokenSource();
        Task probe = Task.Run(() => NetStatus.Query("127.0.0.1", port, false, 10000, probeCancel.Token));
        IPEndPoint from = new(IPAddress.Any, 0);
        blackhole.Client.ReceiveTimeout = 2000; _ = blackhole.Receive(ref from);
        var clock = Stopwatch.StartNew(); probeCancel.Cancel();
        try { await probe; throw new InvalidDataException("cancelled UDP probe succeeded"); }
        catch (OperationCanceledException) { }
        Check(clock.ElapsedMilliseconds < 1500, "cancellation interrupts actual socket receive promptly");

        using var directoryCancel = new CancellationTokenSource();
        Task directory = Task.Run(() => NetMasterClient.Query("127.0.0.1", port, 10000, directoryCancel.Token));
        _ = blackhole.Receive(ref from); clock.Restart(); directoryCancel.Cancel();
        try { await directory; throw new InvalidDataException("cancelled directory query succeeded"); }
        catch (OperationCanceledException) { }
        Check(clock.ElapsedMilliseconds < 1500, "directory cancellation interrupts actual socket receive promptly");

        using var legacyCancel = new CancellationTokenSource();
        Task legacy = Task.Run(() => NetStatus.Query("127.0.0.1", port, true, 100, legacyCancel.Token));
        byte[] query = blackhole.Receive(ref from);
        Check(query[0] == (byte)PacketType.StatusQuery, "legacy fallback first tries cheap status");
        query = blackhole.Receive(ref from);
        Check(query[0] == (byte)PacketType.Hello, "legacy fallback borrows a seat only after status deadline");
        legacyCancel.Cancel();
        try { await legacy; throw new InvalidDataException("cancelled legacy probe succeeded"); }
        catch (OperationCanceledException) { }
        query = blackhole.Receive(ref from);
        Check(query[0] == (byte)PacketType.Bye, "cancelled legacy probe returns borrowed seat");
    }

    private static void LoopDiagnostics()
    {
        var metrics = new ServerLoopDiagnostics();
        using (metrics.Begin(false)) metrics.MarkPhase(ServerLoopPhase.Simulation);
        Check(metrics.Capture().Work.Count == 0, "disabled loop diagnostics produce no samples");
        for (int i = 0; i < 20; i++)
        {
            using var sample = metrics.Begin(true);
            metrics.MarkPhase(ServerLoopPhase.MapAndControl);
            metrics.MarkPhase(ServerLoopPhase.Simulation);
            metrics.MarkPhase(ServerLoopPhase.PostIngress);
            metrics.MarkPhase(ServerLoopPhase.Maintenance);
            sample.Dispose(); // The outer scope must be idempotent.
        }
        var snapshot = metrics.Capture();
        Check(snapshot.Work.Count == 20 && snapshot.Phases.All(p => p.Count == 20), "whole loop and each phase sampled once");
        Check(snapshot.Work.Histogram.Sum() == snapshot.Work.Count
            && snapshot.Work.P99Milliseconds <= snapshot.Work.WorstMilliseconds + .01, "bounded histogram accounts for samples");
    }
}
