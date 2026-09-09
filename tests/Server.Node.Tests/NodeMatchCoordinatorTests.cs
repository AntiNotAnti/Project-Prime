using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class NodeMatchCoordinatorTests
{
    [Fact]
    public async Task AllowedModeCanConfigureLobby()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            NodeContentCatalog.FromConfiguration([
                new NodeMapConfiguration("unit", "hash", "1", "test", 8, [(int)MatchMode.TeamBattle]) ]));

        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        var configured = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.TeamBattle));

        Assert.Equal("unit", configured.MapKey);
        Assert.Equal(MatchMode.TeamBattle, configured.Mode);
        Assert.False(configured.Members.Single().Ready);
    }

    [Fact]
    public async Task UnsupportedModeLeavesLobbyStateUnchangedBeforeConfigure()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            NodeContentCatalog.FromConfiguration([
                new NodeMapConfiguration("unit", "hash", "1", "test", 8, [(int)MatchMode.Battle]) ]));

        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));

        var error = await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.TeamBattle)));

        Assert.Equal("mode_unavailable", error.Code);
        var unchanged = lobbies.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.Equal(lobby.MapKey, unchanged.MapKey);
        Assert.Equal(lobby.Mode, unchanged.Mode);
        Assert.Equal(lobby.Members.Single().Ready, unchanged.Members.Single().Ready);
    }

    [Fact]
    public async Task StartRechecksCurrentLobbyModeBeforeFreezingState()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            NodeContentCatalog.FromConfiguration([
                new NodeMapConfiguration("unit", "hash", "1", "test", 8, [(int)MatchMode.Battle]) ]));

        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        // Configure through LobbyManager to exercise the defense at the Node
        // start boundary even when a lower layer has accepted the mode.
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.TeamBattle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));

        var error = await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(owner,
            new LobbyStart(lobby.Revision)));

        Assert.Equal("mode_unavailable", error.Code);
        var unchanged = lobbies.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.Equal(LobbyPhase.Open, unchanged.Phase);
        Assert.Equal(MatchMode.TeamBattle, unchanged.Mode);
        Assert.True(unchanged.Members.Single().Ready);
    }

    [Fact]
    public void MapCatalogRejectsEmptyDuplicateAndInvalidModeConfiguration()
    {
        static NodeMapConfiguration Map(string key, int[] modes)
            => new(key, "hash", "1", "test", 8, modes);

        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration([Map("unit", [])]));
        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration([
            Map("unit", [(int)MatchMode.Battle]), Map("unit", [(int)MatchMode.TeamBattle]) ]));
        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration([Map("unit", [int.MaxValue])]));
        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration([
            Map("unit", [(int)MatchMode.Battle, (int)MatchMode.Battle]) ]));
        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration(
            Enumerable.Range(0, 65).Select(index => Map("unit" + index, Enum.GetValues<MatchMode>().Select(mode => (int)mode).ToArray()))));
        Assert.Throws<ArgumentException>(() => NodeContentCatalog.FromConfiguration(
            Enumerable.Range(0, 257).Select(index => Map("unit" + index, [(int)MatchMode.Battle]))));
    }

    [Fact]
    public async Task UnsupportedConfigureLeavesLobbyStateUnchangedAndValidMapStillWorks()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([
                new("unit", "hash", "1", "test", 8),
                new("unit2", "hash", "1", "test", 8)]));
        var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));

        var error = await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobby.Revision, "missing", MatchMode.Battle)));
        Assert.Equal("map_unavailable", error.Code);
        var unchanged = lobbies.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.Equal("unit", unchanged.MapKey);
        Assert.True(unchanged.Members.Single().Ready);

        var applied = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyConfigure(unchanged.Revision, "unit2", MatchMode.Battle));
        Assert.Equal("unit2", applied.MapKey);
        Assert.False(applied.Members.Single().Ready);
        Assert.True(applied.Revision > unchanged.Revision);
    }

    [Fact]
    public async Task GuestHandoffAndRejoinRemainBoundToTaggedIdentity()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("normal") with { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        Guid guestId = Guid.NewGuid();
        var guest = new LobbyIdentity(Guid.NewGuid(), null, guestId, "Guest");
        var content = new ContentIdentity("unit", "hash", "1", "test", 8);
        var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer, new NodeContentCatalog([content]));
        var lobby = (LobbySnapshot)lobbies.Execute(guest, new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(guest, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(guest, new LobbySetReady(true, lobby.Revision));
        var placed = (LobbySnapshot)await coordinator.ExecuteAsync(guest, new LobbyStart(lobby.Revision));
        var handoff = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(guest.SessionId));
        Assert.True(scheduler.TryGetAssignment(new(placed.CurrentMatchId!.Value), out var assignment));
        using var verifier = new WorkerAdmissionVerifier(assignment!.Spec, assignment.Placement!);
        verifier.UpdatePublicKey(issuer.KeyId, issuer.ExportPublicKey());
        var join = new JoinPacket(NetHeader.Version, handoff.Nonce, Hunter.Samus, "Guest",
            WireMatchId: handoff.WireMatchId, Ticket: handoff.Ticket);
        Assert.True(verifier.TryConsume(handoff.Ticket, join, DateTimeOffset.UtcNow, out var claims));
        Assert.Null(claims!.PlayerId);
        Assert.Equal(guestId, claims.GuestSessionId);

        await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(
            new LobbyIdentity(guest.SessionId, null, Guid.NewGuid(), "Guest"), new NodeMatchRejoin(handoff.MatchId)));
        Assert.IsType<NodeMatchHandoff>(await coordinator.ExecuteAsync(guest, new NodeMatchRejoin(handoff.MatchId)));
        scheduler.CancelMatch(new(handoff.MatchId), "test cleanup");
    }

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
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompHistory, 0));
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompDynamic, 0));
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompClear, 0));
        Assert.False(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.EndMatch, 0));
        Assert.False(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompHistory, 1));
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
