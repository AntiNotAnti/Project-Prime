using System.Net.WebSockets;
using System.Text;
using ProjectPrime.Server.Node.Lobbies.Queue;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class LobbyWaitlistTests
{
    [Fact]
    public void FIFOOffersAndAcceptsQueuedOnlyIdentityWhenASeatOpens()
    {
        var manager = new LobbyManager(maximumWaitlistPerLobby: 4);
        var owner = Person("Owner"); var player = Person("Player");
        var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        Assert.Equal(2, lobby.Waitlist!.Count);
        Assert.Equal("First", lobby.Waitlist.Entries[0].DisplayName);

        manager.Disconnect(owner.SessionId);
        lobby = manager.ForSession(player.SessionId)!;
        Assert.Equal(2, lobby.Waitlist!.Count);
        Assert.True(manager.ForSession(first.SessionId)!.Waitlist!.SelfOffer is not null);
        Guid offer = manager.ForSession(first.SessionId)!.Waitlist!.SelfOffer!.OfferId;
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer));
        Assert.Equal(2, lobby.Members.Count(m => !m.Observer));
        Assert.Equal(first.IdentityKey, lobby.Members.Single(m => m.SessionId == first.SessionId).IdentityKey);
        Assert.True(manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer is null);
    }

    [Fact]
    public void ObserverCanRemainQueuedAndAcceptConvertsToPlayer()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var observer = Person("Observer");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 1));
        lobby = (LobbySnapshot)manager.Execute(observer, new LobbyJoin(lobby.LobbyId, lobby.Revision, true));
        lobby = (LobbySnapshot)manager.Execute(observer, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision, LobbyQueueRequestedRole.Player, 1));
        Assert.True(lobby.Waitlist!.IsSelfQueued);
        manager.Disconnect(owner.SessionId);
        lobby = manager.ForSession(observer.SessionId)!;
        Guid offer = lobby.Waitlist!.SelfOffer!.OfferId;
        lobby = (LobbySnapshot)manager.Execute(observer, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer));
        LobbyMember member = lobby.Members.Single(m => m.SessionId == observer.SessionId);
        Assert.False(member.Observer); Assert.Equal((byte)0, member.Team); // non-team allocator remains authoritative
    }

    [Fact]
    public void ExpiredOfferAdvancesNextHeadAndMetricsRemainBounded()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var manager = new LobbyManager(maximumWaitlistPerLobby: 4) { RoundClock = clock };
        var owner = Person("Owner"); var player = Person("Player");
        var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        manager.Disconnect(owner.SessionId);
        clock.Advance(TimeSpan.FromSeconds(16)); manager.PruneWaitlists();
        var next = manager.ForSession(second.SessionId)!;
        Assert.True(next.Waitlist!.SelfOffer is not null);
        Assert.Equal(1, manager.WaitlistMetrics(next.LobbyId)!.OffersExpired);
        Assert.Equal(1, manager.WaitlistMetrics(next.LobbyId)!.Size);
    }

    [Fact]
    public void CancellingOfferedHeadAdvancesTheNextQueuedIdentity()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var player = Person("Player");
        var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));

        manager.Disconnect(owner.SessionId);
        LobbySnapshot offered = manager.ForSession(first.SessionId)!;
        Assert.NotNull(offered.Waitlist!.SelfOffer);
        manager.Execute(first, new LobbyQueueLeave(offered.LobbyId, offered.Revision));

        Assert.Null(manager.ForSession(first.SessionId));
        Assert.NotNull(manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer);
    }

    [Fact]
    public void DecliningOfferedHeadAdvancesFifoAndRecordsTheDecision()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        LobbyQueueOffer offer = manager.ForSession(first.SessionId)!.Waitlist!.SelfOffer!;

        lobby = manager.ForSession(first.SessionId)!;
        manager.Execute(first, new LobbyQueueDecline(lobby.LobbyId, lobby.Revision, offer.OfferId));

        Assert.Null(manager.ForSession(first.SessionId));
        Assert.NotNull(manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer);
        Assert.Equal(1, manager.WaitlistMetrics(lobby.LobbyId)!.OffersDeclined);
    }

    [Fact]
    public void MultipleOpeningsReserveDistinctSeatsForConsecutiveFifoHeads()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var player2 = Person("Player2"); var player3 = Person("Player3");
        var first = Person("First"); var second = Person("Second"); var third = Person("Third");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 3, 0));
        lobby = (LobbySnapshot)manager.Execute(player2, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(player3, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(third, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));

        manager.Disconnect(player2.SessionId);
        manager.Disconnect(player3.SessionId);
        LobbyQueueOffer firstOffer = manager.ForSession(first.SessionId)!.Waitlist!.SelfOffer!;
        LobbyQueueOffer secondOffer = manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer!;
        Assert.NotEqual(firstOffer.OfferId, secondOffer.OfferId);
        Assert.Null(manager.ForSession(third.SessionId)!.Waitlist!.SelfOffer);

        lobby = manager.ForSession(first.SessionId)!;
        manager.Execute(first, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, firstOffer.OfferId));
        lobby = manager.ForSession(second.SessionId)!;
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, secondOffer.OfferId));
        Assert.Equal(3, lobby.Members.Count(member => !member.Observer));
        Assert.Null(manager.ForSession(third.SessionId)!.Waitlist!.SelfOffer);
    }

    [Fact]
    public void FinalQueueDisconnectCancelsItsReservationAndAdvancesFifo()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        Assert.NotNull(manager.ForSession(first.SessionId)!.Waitlist!.SelfOffer);
        Assert.Null(manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer);

        manager.Disconnect(first.SessionId);

        Assert.Null(manager.ForSession(first.SessionId));
        Assert.NotNull(manager.ForSession(second.SessionId)!.Waitlist!.SelfOffer);
    }

    [Fact]
    public void DuplicateIdentityAndCrossLobbyQueueHopAreRejected()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var sameIdentity = new LobbyIdentity(Guid.NewGuid(), owner.PlayerId, "Other");
        var other = Person("Other");
        LobbySnapshot first = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("One", LobbyVisibility.Public, 1, 0));
        LobbySnapshot second = (LobbySnapshot)manager.Execute(other, new LobbyCreate("Two", LobbyVisibility.Public, 1, 0));
        Assert.Equal("duplicate_identity", Assert.Throws<LobbyCommandException>(() => manager.Execute(sameIdentity,
            new LobbyQueueJoin(first.LobbyId, first.Revision))).Code);
        var queuedIdentity = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Queued");
        LobbySnapshot queued = (LobbySnapshot)manager.Execute(queuedIdentity,
            new LobbyQueueJoin(first.LobbyId, first.Revision));
        string queuedDisplayName = queued.Waitlist!.Entries.Single().DisplayName;
        Assert.Equal("Queued", queuedDisplayName);
        Assert.Equal("queue_hop", Assert.Throws<LobbyCommandException>(() => manager.Execute(queuedIdentity,
            new LobbyQueueJoin(second.LobbyId, second.Revision))).Code);
        var queuedSession = manager.ForSession(first.Members.Single().SessionId); // member view remains available
        Assert.NotNull(queuedSession);
        _ = second; // keep the second lobby alive for the identity-scope assertion above
    }

    [Fact]
    public void SessionAndIdentityBothBindAQueueEntryAndSeatReservation()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        Guid queuedPlayerId = Guid.NewGuid();
        var queued = new LobbyIdentity(Guid.NewGuid(), queuedPlayerId, "Queued");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(queued, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        LobbyQueueOffer offer = lobby.Waitlist!.SelfOffer!;

        var changedIdentity = new LobbyIdentity(queued.SessionId, Guid.NewGuid(), "Changed");
        Assert.Equal("duplicate_identity", Assert.Throws<LobbyCommandException>(() => manager.Execute(changedIdentity,
            new LobbyQueueJoin(lobby.LobbyId, lobby.Revision))).Code);

        var duplicateIdentity = new LobbyIdentity(Guid.NewGuid(), queuedPlayerId, "Duplicate");
        Assert.Equal("stale_offer", Assert.Throws<LobbyCommandException>(() => manager.Execute(duplicateIdentity,
            new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId))).Code);
        Assert.Equal("not_queued", Assert.Throws<LobbyCommandException>(() => manager.Execute(duplicateIdentity,
            new LobbyQueueLeave(lobby.LobbyId, lobby.Revision))).Code);

        lobby = (LobbySnapshot)manager.Execute(queued, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId));
        Assert.Equal(2, lobby.Members.Count(member => !member.Observer));
        Assert.Equal("stale_offer", Assert.Throws<LobbyCommandException>(() => manager.Execute(queued,
            new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId))).Code);
    }

    [Fact]
    public void OfferedReservationCannotBeStolenByAnOrdinaryJoin()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var queued = Person("Queued"); var outsider = Person("Outsider");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(queued, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        LobbyQueueOffer offer = lobby.Waitlist!.SelfOffer!;

        Assert.Equal("capacity", Assert.Throws<LobbyCommandException>(() => manager.Execute(outsider,
            new LobbyJoin(lobby.LobbyId, lobby.Revision))).Code);
        lobby = (LobbySnapshot)manager.Execute(queued, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId));
        Assert.Contains(lobby.Members, member => member.SessionId == queued.SessionId && !member.Observer);
    }

    [Fact]
    public void StaleRevisionAndOfferFailClosed()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var player = Person("Player"); var queued = Person("Queued");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        manager.Execute(queued, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        manager.Disconnect(owner.SessionId);
        lobby = manager.ForSession(player.SessionId)!;
        Guid offer = manager.ForSession(queued.SessionId)!.Waitlist!.SelfOffer!.OfferId;
        Assert.Equal("stale_revision", Assert.Throws<LobbyCommandException>(() => manager.Execute(queued,
            new LobbyQueueAccept(lobby.LobbyId, lobby.Revision - 1, offer))).Code);
        lobby = manager.ForSession(queued.SessionId)!;
        Assert.Equal("stale_offer", Assert.Throws<LobbyCommandException>(() => manager.Execute(queued,
            new LobbyQueueDecline(lobby.LobbyId, lobby.Revision, Guid.NewGuid()))).Code);
    }

    [Fact]
    public void AllQueueCommandsDecodeThroughTheJsonControlBoundary()
    {
        Guid lobbyId = Guid.NewGuid();
        Guid offerId = Guid.NewGuid();

        Assert.Equal(new LobbyQueueJoin(lobbyId, 7, LobbyQueueRequestedRole.Player, 1),
            NodeControlCodec.Read(ControlFrame("lobby.queue.join",
                $$"""{"lobbyId":"{{lobbyId:D}}","expectedRevision":7,"requestedRole":"Player","requestedTeam":1}""")).Command);
        Assert.Equal(new LobbyQueueLeave(lobbyId, 8),
            NodeControlCodec.Read(ControlFrame("lobby.queue.leave",
                $$"""{"lobbyId":"{{lobbyId:D}}","expectedRevision":8}""")).Command);
        Assert.Equal(new LobbyQueueAccept(lobbyId, 9, offerId),
            NodeControlCodec.Read(ControlFrame("lobby.queue.accept",
                $$"""{"lobbyId":"{{lobbyId:D}}","expectedRevision":9,"offerId":"{{offerId:D}}"}""")).Command);
        Assert.Equal(new LobbyQueueDecline(lobbyId, 10, offerId),
            NodeControlCodec.Read(ControlFrame("lobby.queue.decline",
                $$"""{"lobbyId":"{{lobbyId:D}}","expectedRevision":10,"offerId":"{{offerId:D}}"}""")).Command);
    }

    [Fact]
    public void SurvivalQueueWaitsForPostMatchBoundary()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var player = Person("Player"); var queued = Person("Queued");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Survival", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbyConfigure(lobby.Revision, "MP1 SANCTORUS", MatchMode.Survival));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbySetReady(true, lobby.Revision));
        manager.Execute(queued, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        MatchSpec spec = manager.PrepareMatch(owner.SessionId, manager.ForSession(owner.SessionId)!.Revision,
            new("MP1 SANCTORUS", "hash", "AMHE1", "build", 1), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Null(manager.ForSession(queued.SessionId)!.Waitlist!.SelfOffer);
        Assert.True(manager.MatchReady(new(spec.MatchId, new(1), new(Guid.NewGuid()), Guid.NewGuid(), "localhost", 1)));
        Assert.Null(manager.ForSession(queued.SessionId)!.Waitlist!.SelfOffer);
        manager.Disconnect(player.SessionId); // the next-match seat is now genuinely open
        Assert.True(manager.MatchEnded(spec.MatchId, false));
        Assert.NotNull(manager.ForSession(queued.SessionId)!.Waitlist!.SelfOffer);
    }

    [Fact]
    public void RequestedTeamIsAdvisoryAndAllocatorBalancesThePromotedPlayer()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var player2 = Person("Player2"); var player3 = Person("Player3"); var queued = Person("Queued");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Teams", LobbyVisibility.Public, 3, 0));
        lobby = (LobbySnapshot)manager.Execute(player2, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(player3, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.TeamBattle));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbyRequestTeam(1, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(player2, new LobbyRequestTeam(1, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(queued,
            new LobbyQueueJoin(lobby.LobbyId, lobby.Revision, LobbyQueueRequestedRole.Player, RequestedTeam: 1));

        manager.Disconnect(player3.SessionId);
        lobby = manager.ForSession(queued.SessionId)!;
        LobbyQueueOffer offer = lobby.Waitlist!.SelfOffer!;
        lobby = (LobbySnapshot)manager.Execute(queued, new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId));

        Assert.Equal((byte)0, lobby.Members.Single(member => member.SessionId == queued.SessionId).Team);
    }

    [Fact]
    public void ExplicitDuelFifoKeepsServerSequenceOrder()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner"); var first = Person("First"); var second = Person("Second");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Duel", LobbyVisibility.Public, 1, 2, DuelQueuePolicy: DuelQueuePolicy.Fifo));
        lobby = (LobbySnapshot)manager.Execute(first, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(second, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));

        Assert.Equal(DuelQueuePolicy.Fifo, lobby.DuelQueuePolicy);
        Assert.Collection(lobby.Waitlist!.Entries,
            entry => Assert.Equal("First", entry.DisplayName),
            entry => Assert.Equal("Second", entry.DisplayName));
        Assert.True(lobby.Waitlist.Entries[0].QueueSequence < lobby.Waitlist.Entries[1].QueueSequence);
    }

    [Theory]
    [InlineData(DuelQueuePolicy.WinnerStays)]
    [InlineData(DuelQueuePolicy.LoserStays)]
    [InlineData(DuelQueuePolicy.Manual)]
    public void UnimplementedDuelPoliciesFailClosed(DuelQueuePolicy policy)
    {
        var manager = new LobbyManager();
        LobbyCommandException error = Assert.Throws<LobbyCommandException>(() => manager.Execute(Person("Owner"),
            new LobbyCreate("Duel", LobbyVisibility.Public, 2, 0, DuelQueuePolicy: policy)));
        Assert.Equal("unsupported", error.Code);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void DuplicateFloodIsIdempotentAndCapacityAndHistoryStayBounded()
    {
        var manager = new LobbyManager(maximumWaitlistPerLobby: 4);
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public, 1, 0));
        LobbyIdentity[] queued = Enumerable.Range(0, 4).Select(index => Person("Q" + index)).ToArray();
        foreach (LobbyIdentity identity in queued)
            lobby = (LobbySnapshot)manager.Execute(identity, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
        long stableRevision = lobby.Revision;

        for (int attempt = 0; attempt < 100; attempt++)
        {
            lobby = (LobbySnapshot)manager.Execute(queued[0], new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));
            Assert.Equal(stableRevision, lobby.Revision);
        }
        Assert.Equal(4, lobby.Waitlist!.Count);
        Assert.Equal("capacity", Assert.Throws<LobbyCommandException>(() => manager.Execute(Person("Overflow"),
            new LobbyQueueJoin(lobby.LobbyId, lobby.Revision))).Code);

        var domain = new LobbyWaitlist(4);
        for (int cycle = 0; cycle < 100; cycle++)
        {
            LobbyIdentity identity = Person("D" + cycle);
            Entry entry = domain.Enqueue(identity.SessionId, identity.IdentityKey, identity.DisplayName,
                LobbyQueueRequestedRole.Player, null, DateTimeOffset.UnixEpoch);
            domain.RecordCancelled(entry, DateTimeOffset.UnixEpoch);
            domain.EvictTerminalEntries();
            Assert.True(domain.Entries.Count <= domain.MaximumEntries * 2);
        }
        Assert.Equal(0, domain.Count);
    }

    [Fact]
    public async Task ActiveSeatAndQueuedEntrySurviveReconnectGraceThenReleaseOnFinalPrune()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = new NodeHostFixture(clock);
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string endpoint = host.App.Urls.Single().Replace("https://", "wss://") + "/v1/control";
        NodeControlClient Control()
        {
            var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _)
                => certificate?.GetCertHashString() == host.CertificateThumbprint;
            return new NodeControlClient(socket);
        }

        await using var owner = Control();
        var active = Control();
        await using var queued = Control();
        await owner.ConnectAsync(Ticket(host, endpoint));
        await active.ConnectAsync(Ticket(host, endpoint));
        await queued.ConnectAsync(Ticket(host, endpoint));
        await owner.SendAsync("lobby.create", new LobbyCreate("Grace", LobbyVisibility.Public, 2, 0));
        await UntilAsync(() => owner.Lobby is not null, timeout.Token);
        await active.SendAsync("lobby.join", new LobbyJoin(owner.Lobby!.LobbyId, owner.Lobby.Revision));
        await UntilAsync(() => owner.Lobby!.Members.Length == 2 && active.Lobby?.Members.Length == 2, timeout.Token);
        await queued.SendAsync("lobby.queue.join", new LobbyQueueJoin(owner.Lobby!.LobbyId, owner.Lobby.Revision));
        await UntilAsync(() => queued.Error is null
            ? queued.Lobby?.Waitlist?.IsSelfQueued == true
            : throw new InvalidOperationException(queued.Error), timeout.Token);

        Guid activeSession = active.Session!.SessionId;
        string firstResumeToken = active.Session.ResumeToken;
        await active.DisposeAsync();
        NodeSessionManager sessions = host.App.Services.GetRequiredService<NodeSessionManager>();
        LobbyManager lobbies = host.App.Services.GetRequiredService<LobbyManager>();
        await UntilAsync(() => sessions.CanResume(firstResumeToken), timeout.Token);
        Assert.Null(lobbies.ForSession(queued.Session!.SessionId)!.Waitlist!.SelfOffer);
        clock.Advance(NodeSessionManager.DisconnectGrace - TimeSpan.FromSeconds(1));
        sessions.PruneExpired();
        Assert.NotNull(lobbies.ForSession(activeSession));
        Assert.Null(lobbies.ForSession(queued.Session.SessionId)!.Waitlist!.SelfOffer);

        var resumed = Control();
        await resumed.ResumeAsync(active, timeout.Token);
        await UntilAsync(() => resumed.Lobby?.Members.Length == 2, timeout.Token);
        string secondResumeToken = resumed.Session!.ResumeToken;
        await resumed.DisposeAsync();
        await UntilAsync(() => sessions.CanResume(secondResumeToken), timeout.Token);
        clock.Advance(NodeSessionManager.DisconnectGrace + TimeSpan.FromSeconds(1));
        sessions.PruneExpired();

        Assert.Null(lobbies.ForSession(activeSession));
        Assert.NotNull(lobbies.ForSession(queued.Session.SessionId)!.Waitlist!.SelfOffer);
        await owner.DisposeAsync();
        await queued.DisposeAsync();
        await host.App.StopAsync(timeout.Token);
    }

    private static LobbyIdentity Person(string name) => new(Guid.NewGuid(), Guid.NewGuid(), name);

    private static NodeAdmissionTicket Ticket(NodeHostFixture host, string endpoint)
        => new(host.Ticket(Guid.NewGuid()), DateTimeOffset.UtcNow.AddMinutes(2), host.NodeId, endpoint);

    private static byte[] ControlFrame(string type, string payload)
        => Encoding.UTF8.GetBytes($$"""{"version":{{NodeControlCodec.Version}},"type":"{{type}}","requestId":"{{Guid.NewGuid():D}}","payload":{{payload}}}""");

    private static async Task UntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset? now = null) => _now = now ?? DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
