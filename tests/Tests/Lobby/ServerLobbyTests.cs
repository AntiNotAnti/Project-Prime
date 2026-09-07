using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class ServerLobbyTests
{
    [Fact]
    public void AuthorityConvergesAfterDuplicateStaleAndReorderedRequests()
    {
        ServerLobby lobby = Create();
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        uint revision = lobby.Runtime.Revision;

        LobbyCommandResult ready = lobby.ReceiveRequest(host,
            Request(lobby, revision, 1, LobbyRequestType.SetReady, value: 1));
        Assert.True(ready.Accepted);
        Assert.True(lobby.Runtime.Players.Single().Ready);
        uint acceptedRevision = lobby.Runtime.Revision;

        LobbyCommandResult duplicate = lobby.ReceiveRequest(host,
            Request(lobby, revision, 1, LobbyRequestType.SetReady, value: 1));
        Assert.Equal(LobbyFeedbackCode.DuplicateRequest, duplicate.Feedback.Code);
        Assert.Equal(acceptedRevision, lobby.Runtime.Revision);

        LobbyCommandResult stale = lobby.ReceiveRequest(host,
            Request(lobby, revision, 3, LobbyRequestType.SelectHunter, value: (int)Hunter.Noxus));
        Assert.Equal(LobbyFeedbackCode.StaleRevision, stale.Feedback.Code);
        Assert.Equal(Hunter.Samus, lobby.Runtime.Players.Single().Hunter);

        LobbyCommandResult reordered = lobby.ReceiveRequest(host,
            Request(lobby, acceptedRevision, 2, LobbyRequestType.SelectHunter, value: (int)Hunter.Noxus));
        Assert.Equal(LobbyFeedbackCode.DuplicateRequest, reordered.Feedback.Code);

        LobbyCommandResult current = lobby.ReceiveRequest(host,
            Request(lobby, acceptedRevision, 4, LobbyRequestType.SelectHunter, value: (int)Hunter.Noxus));
        Assert.True(current.Accepted);
        Assert.Equal(Hunter.Noxus, lobby.Runtime.Players.Single().Hunter);

        uint currentRevision = lobby.Runtime.Revision;
        LobbyCommandResult noOp = lobby.ReceiveRequest(host,
            Request(lobby, currentRevision, 5, LobbyRequestType.SelectHunter, value: (int)Hunter.Noxus));
        Assert.Equal(LobbyFeedbackCode.Conflict, noOp.Feedback.Code);
        Assert.Equal(currentRevision, lobby.Runtime.Revision);
    }

    [Fact]
    public void HostCanExplicitlyForceStartWhenPolicyAllowsIt()
    {
        ServerLobby lobby = Create(new LobbyPolicy(LobbyPolicyKind.PersistentLobby,
            readyRequired: true, minimumPlayers: 1, hostMayForceStart: true));
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);

        LobbyCommandResult started = lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.StartMatch, value: 1));

        Assert.True(started.Accepted);
        Assert.Equal(LobbyCommandAction.StartMatch, started.Action);
        Assert.Equal(LobbyPhase.Locked, lobby.Runtime.Phase);
    }

    [Fact]
    public void FullSnapshotsCoalesceToNewestRevisionPerPeer()
    {
        ServerLobby lobby = Create();
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        Assert.True(lobby.PendingSnapshot(10, out LobbySnapshotPacket? first));

        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.SelectHunter, value: (int)Hunter.Kanden)).Accepted);

        Assert.True(lobby.PendingSnapshot(10, out LobbySnapshotPacket? newest));
        Assert.True(Sequence32.IsNewer(newest!.Revision, first!.Revision));
        Assert.False(newest.Members.Single().Ready);
        Assert.Equal(Hunter.Kanden, newest.Members.Single().Hunter);
        lobby.MarkSnapshotPublished(10, newest.Revision);
        Assert.False(lobby.PendingSnapshot(10, out _));
    }

    [Fact]
    public void SnapshotsCarryEffectivePolicyAndRecipientPermissions()
    {
        var policy = new LobbyPolicy(LobbyPolicyKind.IntermissionLobby,
            readyRequired: false, minimumPlayers: 2, hostMayForceStart: true);
        ServerLobby lobby = Create(policy);
        ServerPeer host = Peer(10, 0, "HOST");
        ServerPeer guest = Peer(20, 1, "GUEST");
        Assert.True(lobby.AddPeer(host, hostAuthorized: true).Accepted);
        Assert.True(lobby.AddPeer(guest, hostAuthorized: false).Accepted);

        Assert.True(lobby.PendingSnapshot(host.Connection.Id, out LobbySnapshotPacket? hostSnapshot));
        Assert.True(lobby.PendingSnapshot(guest.Connection.Id, out LobbySnapshotPacket? guestSnapshot));

        Assert.False(hostSnapshot!.ReadyRequired);
        Assert.Equal(2, hostSnapshot.MinimumPlayers);
        Assert.True(hostSnapshot.HostMayForceStart);
        Assert.Equal(LobbyPermissions.Host, hostSnapshot.Permissions);
        Assert.Equal(LobbyPermissions.Player, guestSnapshot!.Permissions);
        Assert.Equal(hostSnapshot.Revision, guestSnapshot.Revision);
    }

    [Fact]
    public void InvalidMapModePairIsRejectedBeforeRevisionAdvances()
    {
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, Rules(),
            mapAllowed: map => map is "MP1 SANCTORUS" or "UNIT2 ALINOS GATE",
            modeAllowed: _ => true,
            selectionAllowed: (map, mode) => map != "UNIT2 ALINOS GATE" || mode == MatchMode.TeamBattle);
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        uint revision = lobby.Runtime.Revision;

        LobbyCommandResult rejected = lobby.ReceiveRequest(host,
            Request(lobby, revision, 1, LobbyRequestType.SetMap, text: "UNIT2 ALINOS GATE"));

        Assert.Equal(LobbyFeedbackCode.Unsupported, rejected.Feedback.Code);
        Assert.Equal(revision, lobby.Runtime.Revision);
        Assert.Equal("MP1 SANCTORUS", lobby.Runtime.Draft.Current.RoomKey);
        Assert.Equal(MatchMode.Battle, lobby.Runtime.Draft.Current.Mode);
    }

    [Fact]
    public void AcceptedLobbyEditsRefreshDiscoveryRulesMapAndMode()
    {
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        MatchRules rules = Rules();
        var network = new ServerNetwork(transport, rules);
        network.StatusProvider = () => new MatchStatePacket
        {
            MatchId = 1, Mode = (byte)GameMode.Battle, RoomKey = "STALE MAP",
            NextRoomKey = "STALE MAP", Flags = MatchStatePacket.FlagInProgress
        };
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules,
            mapAllowed: map => map is "MP1 SANCTORUS" or "UNIT2 ALINOS GATE",
            selectionAllowed: (_, _) => true);
        var bridge = new ServerLobbyNetwork(lobby, network);
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        Inject(network, host);
        bridge.Open(1);

        Assert.True(network.LobbyRequestReceived!(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetMap, text: "UNIT2 ALINOS GATE")));
        Assert.True(network.LobbyRequestReceived(host,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.SetMode, value: (int)MatchMode.TeamBattle)));
        var ruleRequest = new LobbyRequestPacket(lobby.Runtime.SessionId, lobby.Runtime.Revision, 3,
            LobbyRequestType.SetRule, LobbyRuleField.FriendlyFire, 1, "");
        Assert.True(network.LobbyRequestReceived(host, ruleRequest));

        Assert.Equal("UNIT2 ALINOS GATE", network.Room);
        Assert.Equal(GameMode.BattleTeams, network.Mode);
        Assert.True(network.Rules.FriendlyFire);

        // The injected peer is sufficient to exercise the server callback but has
        // no live transport clock; remove it before Poll services discovery.
        var peers = (ServerPeer?[])typeof(ServerNetwork).GetField("_peers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        var connections = (ServerPeer?[])typeof(ServerNetwork).GetField("_connections",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        peers[host.Slot] = null;
        connections[host.ConnectionIndex] = null;

        socket.SendDatagram(new IPEndPoint(IPAddress.Loopback, transport.LocalPort),
            new byte[] { (byte)PacketType.StatusQuery, ServerStatusPacket.RulesCapability });
        ServerStatusPacket? discovered = null;
        var timer = Stopwatch.StartNew();
        while (discovered == null && timer.Elapsed < TimeSpan.FromSeconds(3))
        {
            network.Poll(2);
            foreach (ReceivedPacket packet in socket.Drain())
                if (packet.Type == PacketType.StatusReply)
                    discovered = ServerStatusPacket.Read(packet.Payload);
            Thread.Sleep(2);
        }
        Assert.NotNull(discovered);
        Assert.Equal("UNIT2 ALINOS GATE", discovered.Value.Match.RoomKey);
        Assert.Equal((byte)GameMode.BattleTeams, discovered.Value.Match.Mode);
        Assert.True(discovered.Value.FriendlyFire);
    }

    [Fact]
    public void ObserversNeverGateReadyAndBotsAreAutomaticallyReady()
    {
        var policy = new LobbyPolicy(LobbyPolicyKind.PersistentLobby, true, 2, false);
        ServerLobby lobby = Create(policy);
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        Assert.True(lobby.Admit(new LobbyAdmissionRequest(20, null, "BOT", Hunter.Trace,
            Bot: true, RequestedTeam: 1)).Accepted);
        Assert.True(lobby.Admit(new LobbyAdmissionRequest(30, null, "WATCH", Hunter.Samus,
            Observer: true)).Accepted);
        Assert.False(lobby.Runtime.AllPlayersReady);

        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.Runtime.AllPlayersReady);
        Assert.All(lobby.Runtime.Observers, observer => Assert.False(observer.Ready));
        Assert.True(lobby.TryStart(host.Connection.Id, force: false, out MatchRules? frozen));
        Assert.Same(lobby.Runtime.Draft.Current, frozen);
        Assert.Equal(LobbyPhase.Locked, lobby.Runtime.Phase);
    }

    [Fact]
    public void OneConnectionReturnsToLobbyAndStartsRematchWithoutTransportReplacement()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        MatchRules rules = Rules();
        Guid capability = Guid.NewGuid();
        var network = new ServerNetwork(serverTransport, rules, observers: null,
            lobbyOwnerCapability: capability, lobbyOwnerAddress: IPAddress.Loopback);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        bridge.Open(1);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "HOST", Hunter.Samus, ownerCapability: capability);
        uint tick = 1;
        PumpUntil(() => client.State == NetConnectionState.Lobby
            && lobby.Runtime.HostConnectionId != 0, network, client, ref tick);
        ServerPeer host = network.Find(lobby.Runtime.HostConnectionId)!;
        Assert.Equal(NetConnectionState.Lobby, host.Connection.State);
        Assert.Equal(77u, host.Connection.SessionId);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.StartMatch)).Accepted);
        Assert.True(bridge.TryStart(2, out _));
        ulong connectionId = host.Connection.Id;
        uint firstMatch = host.Connection.MatchId;
        Assert.Equal(NetConnectionState.Loading, host.Connection.State);
        Assert.True(lobby.CreateSnapshot().Members.Single().Loading);

        Assert.True(lobby.EnterAfterMatch(new MatchSummaryPacket(77, lobby.Runtime.Revision, firstMatch,
            10, MatchMode.Battle, MatchEndReason.Forced, MatchSummaryFlags.None,
            rules.RoomKey, ImmutableArray<MatchSummaryRow>.Empty)));
        bridge.Open(500);
        Assert.Equal(connectionId, host.Connection.Id);
        Assert.Equal(NetConnectionState.Lobby, host.Connection.State);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 3, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 4, LobbyRequestType.Rematch)).Accepted);
        Assert.True(bridge.TryStart(501, out _));
        Assert.Equal(connectionId, host.Connection.Id);
        Assert.True(Sequence32.IsNewer(host.Connection.MatchId, firstMatch));
    }

    [Fact]
    public void RankedLobbyRejectsGuestsBotsAndPrivateConfigurationChanges()
    {
        MatchRules ranked = Rules().With(rankingEligibility: RankingEligibility.VerifiedServerOnly,
            rulesetPreset: RulesetPreset.Competitive, lateJoinPolicy: LateJoinPolicy.Disabled);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, ranked);
        Assert.False(lobby.Admit(new LobbyAdmissionRequest(10, null, "GUEST", Hunter.Samus,
            RequestedTeam: 0)).Accepted);
        Assert.False(lobby.Admit(new LobbyAdmissionRequest(11, null, "BOT", Hunter.Samus,
            Bot: true, RequestedTeam: 0)).Accepted);
        ServerPeer verified = Peer(12, 0, "RANKED");
        verified.PlayerId = new PlayerId(Guid.NewGuid());
        Assert.True(lobby.AddPeer(verified).Accepted);
        LobbyCommandResult changed = lobby.ReceiveRequest(verified,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetMap, text: "MP1 SANCTORUS"));
        Assert.Equal(LobbyFeedbackCode.NotPermitted, changed.Feedback.Code);
    }

    [Fact]
    public void DisconnectedPlayerRemainsVisibleDuringGraceAndReconnectClearsStatus()
    {
        ServerLobby lobby = Create();
        ServerPeer host = Peer(10, 0, "HOST");
        ServerPeer successor = Peer(20, 1, "NEXT");
        Assert.True(lobby.AddPeer(host).Accepted);
        Assert.True(lobby.AddPeer(successor).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetReady, value: 1)).Accepted);

        Assert.True(lobby.Remove(host.Connection.Id, 100, reserveReconnect: true));
        LobbySnapshotMember grace = lobby.CreateSnapshot().Members.Single(member => member.ConnectionId == 10);
        Assert.True(grace.DisconnectedGrace);
        Assert.False(grace.Ready);
        Assert.False(grace.Loading);
        Assert.False(grace.Host);
        Assert.True(lobby.Runtime.Players.Single(member => member.ConnectionId == 20).Host);

        ServerPeer reconnected = Peer(30, 0, "HOST");
        LobbyAdmissionResult result = lobby.AddPeer(reconnected, 101,
            previousConnectionId: 10, reconnectAuthorized: true);
        Assert.True(result.Reconnected);
        LobbySnapshotPacket restored = lobby.CreateSnapshot();
        Assert.DoesNotContain(restored.Members, member => member.ConnectionId == 10);
        LobbySnapshotMember active = restored.Members.Single(member => member.ConnectionId == 30);
        Assert.False(active.DisconnectedGrace);
        Assert.False(active.Loading);
    }

    [Fact]
    public void GraceExpiryRemovesReservedRosterMember()
    {
        ServerLobby lobby = Create();
        ServerPeer host = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(host).Accepted);
        Assert.True(lobby.Remove(host.Connection.Id, 100, reserveReconnect: true));
        uint graceRevision = lobby.Runtime.Revision;
        Assert.Single(lobby.CreateSnapshot().Members);

        lobby.ExpireReservations(100 + ServerLobby.DefaultReconnectGraceTicks);

        Assert.Empty(lobby.CreateSnapshot().Members);
        Assert.True(Sequence32.IsNewer(lobby.Runtime.Revision, graceRevision));
    }

    [Fact]
    public void MatchTransitionMarksPlayersAndObserversLoadingButNotBots()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        using var observerTransport = new NetTransport(0);
        MatchRules rules = Rules();
        Guid capability = Guid.NewGuid();
        var network = new ServerNetwork(serverTransport, rules, observers: new ObserverOptions(1, 0),
            lobbyOwnerCapability: capability, lobbyOwnerAddress: IPAddress.Loopback);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        bridge.Open(1);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "HOST", Hunter.Samus, ownerCapability: capability);
        uint tick = 1;
        PumpUntil(() => client.State == NetConnectionState.Lobby
            && lobby.Runtime.HostConnectionId != 0, network, client, ref tick);
        ServerPeer host = network.Find(lobby.Runtime.HostConnectionId)!;
        using var observer = new NetClient(observerTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "WATCH", Hunter.Samus, observer: true);
        PumpUntil(() => observer.State == NetConnectionState.Lobby
            && observer.Connection != null, network, observer, ref tick);
        Assert.True(lobby.Admit(new LobbyAdmissionRequest(30, null, "BOT", Hunter.Trace,
            Bot: true, RequestedTeam: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.StartMatch)).Accepted);

        Assert.True(bridge.TryStart(2, out _));

        LobbySnapshotPacket snapshot = lobby.CreateSnapshot();
        Assert.True(snapshot.Members.Single(member => member.ConnectionId == host.Connection.Id).Loading);
        Assert.True(snapshot.Members.Single(member => member.ConnectionId == observer.Connection!.Id).Loading);
        Assert.False(snapshot.Members.Single(member => member.ConnectionId == 30).Loading);
    }

    [Fact]
    public void MatchTransitionPreservesAuthoritativeLobbyTeamAssignments()
    {
        using var transport = new NetTransport(0);
        MatchRules rules = Rules().With(mode: MatchMode.TeamBattle);
        var network = new ServerNetwork(transport, rules);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        ServerPeer host = Peer(10, 0, "HOST");
        ServerPeer guest = Peer(20, 1, "GUEST");
        Assert.True(lobby.AddPeer(host, hostAuthorized: true).Accepted);
        Assert.True(lobby.AddPeer(guest, hostAuthorized: false).Accepted);
        Inject(network, host);
        Inject(network, guest);
        bridge.Open(1);

        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.RequestTeam, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(guest,
            Request(lobby, lobby.Runtime.Revision, 1, LobbyRequestType.RequestTeam, value: 0)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(guest,
            Request(lobby, lobby.Runtime.Revision, 2, LobbyRequestType.SetReady, value: 1)).Accepted);
        Assert.True(lobby.ReceiveRequest(host,
            Request(lobby, lobby.Runtime.Revision, 3, LobbyRequestType.StartMatch)).Accepted);

        Assert.True(bridge.TryStart(2, out _));
        Assert.Equal(1, host.TeamIndex);
        Assert.Equal(0, guest.TeamIndex);
    }

    [Fact]
    public void MatchReconnectIdentityReplacesGraceRowWhenLobbyReopens()
    {
        using var transport = new NetTransport(0);
        MatchRules rules = Rules();
        var network = new ServerNetwork(transport, rules);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        ServerPeer oldHost = Peer(10, 0, "HOST");
        ServerPeer successor = Peer(20, 1, "NEXT");
        Assert.True(lobby.AddPeer(oldHost).Accepted);
        Assert.True(lobby.AddPeer(successor).Accepted);
        Assert.True(lobby.Remove(10, 100, reserveReconnect: true));

        ServerPeer reconnected = Peer(30, 0, "HOST");
        reconnected.ReturningFromConnectionId = 10;
        Inject(network, reconnected);
        Inject(network, successor);
        bridge.Open(101);

        LobbySnapshotPacket snapshot = lobby.CreateSnapshot();
        Assert.DoesNotContain(snapshot.Members, member => member.ConnectionId == 10);
        Assert.Contains(snapshot.Members, member => member.ConnectionId == 30 && !member.DisconnectedGrace);
    }

    [Fact]
    public void MatchReconnectReservationSurvivesPostMatchLobbyTransition()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        MatchRules rules = Rules();
        Guid capability = Guid.NewGuid();
        var network = new ServerNetwork(serverTransport, rules, observers: null,
            lobbyOwnerCapability: capability, lobbyOwnerAddress: IPAddress.Loopback);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        bridge.Open(1);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "HOST", Hunter.Samus, ownerCapability: capability);
        uint tick = 1;
        PumpUntil(() => client.State == NetConnectionState.Lobby
            && lobby.Runtime.HostConnectionId != 0, network, client, ref tick);
        ServerPeer host = network.Find(lobby.Runtime.HostConnectionId)!;
        ulong priorConnectionId = host.Connection.Id;
        host.HasParticipated = true;
        network.Phase = MatchPhase.Playing;

        network.Remove(host.Slot, reason: ParticipantExitReason.Timeout);
        Assert.True(network.HasReconnectReservation(host.Slot));
        Assert.True(lobby.Runtime.TryFind(priorConnectionId, out LobbyPlayer reserved));
        Assert.True(reserved.DisconnectedGrace);

        Assert.True(lobby.EnterAfterMatch(new MatchSummaryPacket(77, lobby.Runtime.Revision, 1,
            tick, MatchMode.Battle, MatchEndReason.Forced, MatchSummaryFlags.None,
            rules.RoomKey, ImmutableArray<MatchSummaryRow>.Empty)));
        bridge.Open(++tick);
        Assert.True(network.HasReconnectReservation(host.Slot));

        client.Reconnect();
        PumpUntil(() => client.State == NetConnectionState.Lobby
            && lobby.Runtime.Players.Any(player => player.ConnectionId != priorConnectionId
                && !player.DisconnectedGrace), network, client, ref tick);

        Assert.DoesNotContain(lobby.Runtime.Players, player => player.ConnectionId == priorConnectionId);
        Assert.Contains(lobby.Runtime.Players, player => player.ConnectionId == client.Connection!.Id
            && !player.DisconnectedGrace);
    }

    [Fact]
    public void IdleLobbyTicksDoNotAllocateOrAdvanceRevision()
    {
        using var transport = new NetTransport(0);
        MatchRules rules = Rules();
        var network = new ServerNetwork(transport, rules);
        var lobby = new ServerLobby(77, LobbyPolicy.PrivateHosted, rules);
        var bridge = new ServerLobbyNetwork(lobby, network);
        bridge.Open(1);
        bridge.Tick(2);
        uint revision = lobby.Runtime.Revision;
        long allocated = GC.GetAllocatedBytesForCurrentThread();

        for (uint tick = 3; tick < 1003; tick++) bridge.Tick(tick);

        Assert.Equal(allocated, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(revision, lobby.Runtime.Revision);
    }

    [Fact]
    public void LobbyChatSenderIdentitySurvivesReconnectButNotRemovalOrExpiredGrace()
    {
        ServerLobby lobby = Create();
        ServerPeer original = Peer(10, 0, "HOST");
        Assert.True(lobby.AddPeer(original).Accepted);
        LobbyChatDispatch first = lobby.ReceiveChat(original, new LobbyChatRequestPacket(
            lobby.Runtime.SessionId, lobby.Runtime.Revision, 1, ChatScope.Lobby, "before reconnect"));
        Assert.True(first.Accepted);
        ulong stableIdentity = first.Chat!.Value.SenderIdentity;
        Assert.NotEqual(0ul, stableIdentity);

        Assert.True(lobby.RemovePeer(original, tick: 10, reserveReconnect: true));
        ServerPeer reconnected = Peer(20, 0, "HOST");
        Assert.True(lobby.AddPeer(reconnected, tick: 11, previousConnectionId: original.Connection.Id,
            reconnectAuthorized: true).Reconnected);
        LobbyChatDispatch restored = lobby.ReceiveChat(reconnected, new LobbyChatRequestPacket(
            lobby.Runtime.SessionId, lobby.Runtime.Revision, 1, ChatScope.Lobby, "after reconnect"));
        Assert.True(restored.Accepted);
        Assert.Equal(stableIdentity, restored.Chat!.Value.SenderIdentity);
        Assert.NotEqual(first.Chat.Value.ConnectionId, restored.Chat.Value.ConnectionId);

        Assert.True(lobby.RemovePeer(reconnected, tick: 12, reserveReconnect: false));
        ServerPeer afterExplicitLeave = Peer(30, 0, "HOST");
        Assert.True(lobby.AddPeer(afterExplicitLeave, tick: 13).Accepted);
        LobbyChatDispatch afterLeave = lobby.ReceiveChat(afterExplicitLeave, new LobbyChatRequestPacket(
            lobby.Runtime.SessionId, lobby.Runtime.Revision, 1, ChatScope.Lobby, "after leave"));
        Assert.True(afterLeave.Accepted);
        Assert.NotEqual(stableIdentity, afterLeave.Chat!.Value.SenderIdentity);

        Assert.True(lobby.RemovePeer(afterExplicitLeave, tick: 100, reserveReconnect: true));
        lobby.ExpireReservations(unchecked(100 + lobby.ReconnectGraceTicks));
        ServerPeer afterExpiredGrace = Peer(40, 0, "HOST");
        Assert.True(lobby.AddPeer(afterExpiredGrace, tick: unchecked(101 + lobby.ReconnectGraceTicks)).Accepted);
        LobbyChatDispatch afterExpiry = lobby.ReceiveChat(afterExpiredGrace, new LobbyChatRequestPacket(
            lobby.Runtime.SessionId, lobby.Runtime.Revision, 1, ChatScope.Lobby, "after expiry"));
        Assert.True(afterExpiry.Accepted);
        Assert.NotEqual(afterLeave.Chat.Value.SenderIdentity, afterExpiry.Chat!.Value.SenderIdentity);
    }

    private static ServerLobby Create(LobbyPolicy? policy = null)
        => new(77, policy ?? LobbyPolicy.PrivateHosted, Rules(), map => map is "MP1 SANCTORUS" or "UNIT2 ALINOS GATE");

    private static MatchRules Rules() => new(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 8);

    private static ServerPeer Peer(ulong id, byte slot, string name)
        => new(new NetConnection(id, new IPEndPoint(IPAddress.Loopback, 20000 + slot), 1, 0),
            new JoinPacket(NetHeader.Version, id, Hunter.Samus, name), slot, 0) { TeamIndex = slot };

    private static ServerPeer ObserverPeer(ulong id, string name)
        => new(new NetConnection(id, new IPEndPoint(IPAddress.Loopback, 21000), 1, 0),
            new JoinPacket(NetHeader.Version, id, Hunter.Samus, name, Observer: true), byte.MaxValue, 0)
        { ConnectionIndex = 8, TeamIndex = byte.MaxValue };

    private static LobbyRequestPacket Request(ServerLobby lobby, uint revision, uint id,
        LobbyRequestType type, int value = 0, string text = "")
        => new(lobby.Runtime.SessionId, revision, id, type, LobbyRuleField.None, value, text);

    private static void PumpUntil(Func<bool> condition, ServerNetwork network, NetClient client,
        ref uint tick)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            network.Poll(++tick);
            client.Poll();
            if (condition()) return;
            Thread.Sleep(2);
        }
        Assert.Fail("Network lobby admission did not complete before its deadline.");
    }

    private static void Inject(ServerNetwork network, ServerPeer peer)
    {
        var peers = (ServerPeer?[])typeof(ServerNetwork).GetField("_peers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        var connections = (ServerPeer?[])typeof(ServerNetwork).GetField("_connections",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        peers[peer.Slot] = peer;
        connections[peer.Slot] = peer;
    }

    private static void InjectObserver(ServerNetwork network, ServerPeer peer)
    {
        var observers = (ServerPeer?[])typeof(ServerNetwork).GetField("_observers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        var connections = (ServerPeer?[])typeof(ServerNetwork).GetField("_connections",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!;
        observers[0] = peer;
        connections[8] = peer;
    }
}
