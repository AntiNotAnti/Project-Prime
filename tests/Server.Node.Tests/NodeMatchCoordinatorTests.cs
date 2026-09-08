using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class NodeMatchCoordinatorTests
{
    [Fact]
    public async Task FrozenLobbyPlacesThroughAuthenticatedChildAndIssuesNewResumeTicket()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("normal") with { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var content = new ContentIdentity("unit", "hash", "1", "test", 8);
        var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer, new NodeContentCatalog([content]));
        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));
        var placed = (LobbySnapshot)await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
        Assert.Equal(LobbyPhase.InMatch, placed.Phase);
        var handoff = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
        var refreshed = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
        Assert.Equal(placed.CurrentMatchId, handoff.MatchId);
        Assert.NotEqual(handoff.Ticket, refreshed.Ticket);
        Assert.NotEqual(handoff.Nonce, refreshed.Nonce);
        Assert.DoesNotContain(handoff.Ticket, handoff.ToString());
        var retry = Assert.IsType<NodeMatchHandoff>(await coordinator.ExecuteAsync(owner, new NodeMatchRejoin(handoff.MatchId)));
        Assert.NotEqual(refreshed.Ticket, retry.Ticket);
        await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(owner, new NodeMatchRejoin(handoff.MatchId)));
        await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Intruder"), new NodeMatchRejoin(handoff.MatchId)));
        Assert.Throws<LobbyCommandException>(() => lobbies.Execute(owner, new LobbySelectHunter(Hunter.Kanden, placed.Revision)));
        Assert.True(lobbies.MatchEnded(new(handoff.MatchId), false));
        Assert.False(lobbies.MatchEnded(new(handoff.MatchId), true));
        var ended = lobbies.ForSession(owner.SessionId)!;
        var reopened = lobbies.ReturnToLobby(owner.SessionId, ended.Revision);
        Assert.Equal(LobbyPhase.Open, reopened.Phase);
        Assert.False(reopened.Members[0].Ready);
        Assert.Null(reopened.CurrentMatchId);
        // A late terminal event must not repopulate credentials for an expired
        // session after its membership and cached state have been removed.
        lobbies.Disconnect(owner.SessionId);
        coordinator.ForgetSession(owner.SessionId);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (_, _) => stopped.TrySetResult();
        scheduler.CancelMatch(new(handoff.MatchId), "test cleanup");
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(coordinator.ForSession(owner.SessionId));
    }
    [Fact]
    public async Task NoCapacityReturnsFrozenLobbyToPostMatchWithoutLosingMembership()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager(); var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer, new NodeContentCatalog([new("unit", "hash", "1", "test", 8)]));
        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));
        await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision)));
        Assert.Equal(LobbyPhase.PostMatch, lobbies.ForSession(owner.SessionId)!.Phase);
        Assert.Single(lobbies.ForSession(owner.SessionId)!.Members);
        Assert.True(Assert.IsType<NodeMatchEnded>(coordinator.ForSession(owner.SessionId)).Interrupted);
    }
}
