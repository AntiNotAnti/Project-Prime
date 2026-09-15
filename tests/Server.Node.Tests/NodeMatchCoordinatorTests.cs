using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Collection(WorkerProcessCollection.Name)]
[Trait("LifecycleFast", "true")]
public sealed class NodeMatchCoordinatorTests
{
    [Fact]
    public async Task LegacyLobbyRematchIsUnsupportedAndCannotReturnLobby()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var content = new ContentIdentity("unit", "hash", "1", "test", 8);
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers,
            issuer, new NodeContentCatalog([content]));

        LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, content.MapKey, MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        MatchSpec spec = lobbies.PrepareMatch(owner.SessionId, lobby.Revision,
            content, workers.NodeId, workers.NodeIncarnation);
        Assert.True(lobbies.MatchEnded(spec.MatchId, interrupted: false));
        LobbySnapshot before = lobbies.ForSession(owner.SessionId)!;

        LobbyCommandException error = await Assert.ThrowsAsync<LobbyCommandException>(() =>
            coordinator.ExecuteAsync(owner, new LobbyRematch(before.Revision)));

        Assert.Equal("unsupported", error.Code);
        LobbySnapshot after = lobbies.ForSession(owner.SessionId)!;
        Assert.Equal(LobbyPhase.PostMatch, after.Phase);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.CurrentMatchId, after.CurrentMatchId);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(4, false)] [InlineData(1, true)]
    public async Task AutomaticContinuationUsesFreshMatchTicketAndNonce(byte option, bool failPlacement)
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog(new[] { "unit", "unit2", "unit3" }.Select(key => new ContentIdentity(key, "hash", "1", "test", 8))));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
            var first = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (_, _) => ended.TrySetResult();
            Assert.True(scheduler.TrySendMatchAdmin(new(new(first.MatchId), AdminAction.EndMatch, null)));
            await ended.Task.WaitAsync(stop.Token);
            var round = lobbies.RoundForSession(owner.SessionId)!;
            Assert.NotEmpty(round.Options);
            if (failPlacement) scheduler.Drain("continuation failure test");
            await coordinator.ExecuteAsync(owner, new LobbyVoteCast(round.Lobby.Revision, round.BallotRevision, option));
            if (failPlacement)
            {
                await foreach (var notification in coordinator.ReadNotifications(stop.Token))
                {
                    if (notification.Payload is NodeMatchEnded { Interrupted: true }) break;
                }
                Assert.Equal(LobbyPhase.Open, lobbies.ForSession(owner.SessionId)!.Phase);
                Assert.Null(lobbies.ForSession(owner.SessionId)!.CurrentMatchId);
                Assert.Empty(lobbies.RoundForSession(owner.SessionId)!.Options);
                return;
            }
            // Deliberately do not consume notifications until the next match
            // is already placed, so terminal and handoff are pending together.
            await foreach (var snapshot in lobbies.ReadNotifications(stop.Token))
                if (snapshot.Phase == LobbyPhase.InMatch && snapshot.CurrentMatchId != first.MatchId) break;
            NodeMatchHandoff? next = null;
            var delivered = new List<object>();
            NodeMatchNotification? firstHandoffNotification = null;
            await foreach (var notification in coordinator.ReadNotifications(stop.Token))
            {
                delivered.Add(notification.Payload);
                if (notification.Payload is NodeMatchHandoff firstHandoff
                    && firstHandoff.MatchId == first.MatchId)
                    firstHandoffNotification = notification;
                if (notification.Payload is NodeMatchHandoff handoff && handoff.MatchId != first.MatchId)
                { next = handoff; break; }
            }
            Assert.NotNull(next);
            Assert.NotNull(firstHandoffNotification?.HandoffSnapshot);
            Assert.Equal(first.MatchId,
                firstHandoffNotification!.HandoffSnapshot!.CurrentMatchId);
            Assert.Equal(LobbyPhase.InMatch,
                firstHandoffNotification.HandoffSnapshot.Phase);
            int terminalIndex = delivered.FindIndex(payload => payload is NodeMatchEnded endedMatch && endedMatch.MatchId == first.MatchId);
            int completionIndex = delivered.FindIndex(payload => payload is NodeMatchCompletion completed
                && completed.Summary.MatchId.Value == first.MatchId);
            int handoffIndex = delivered.FindIndex(payload => payload is NodeMatchHandoff nextMatch && nextMatch.MatchId == next.MatchId);
            Assert.True(completionIndex >= 0 && completionIndex < terminalIndex);
            Assert.True(terminalIndex >= 0 && terminalIndex < handoffIndex);
            Assert.NotEqual(first.Ticket, next.Ticket); Assert.NotEqual(first.Nonce, next.Nonce);
            Assert.Equal(lobby.LobbyId, lobbies.ForSession(owner.SessionId)!.LobbyId);
            Assert.False(lobbies.ForSession(owner.SessionId)!.Members[0].Ready);
            Assert.True(scheduler.TryGetAssignment(new(next.MatchId), out var assignment));
            Assert.Equal(option == 1 ? "unit" : option == 2 ? "unit2" : "unit3", assignment!.Spec.Content.MapKey);
        }
        finally { stop.Cancel(); await continuation; }
    }

    [Fact]
    public async Task UnexpectedAutomaticContinuationFailureRecoversWithoutFaultingLoop()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var injected = new TaskCompletionSource<Guid>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int failNextContinuation = 0;
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers,
            issuer, new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]),
            options: new NodeMatchCoordinatorOptions
            {
                BeforeMatchCommit = matchId =>
                {
                    if (Interlocked.Exchange(ref failNextContinuation, 0) != 1)
                        return;
                    injected.TrySetResult(matchId.Value);
                    throw new Exception("injected continuation fault");
                }
            });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Automatic recovery", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
            NodeMatchHandoff first = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(owner.SessionId));

            var ended = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, _) =>
            {
                if (matchId == new MatchId(first.MatchId)) ended.TrySetResult();
            };
            Assert.True(scheduler.TrySendMatchAdmin(new(new(first.MatchId),
                AdminAction.EndMatch, null)));
            await ended.Task.WaitAsync(stop.Token);

            NodeRoundSnapshot round = lobbies.RoundForSession(owner.SessionId)!;
            LobbyVoteEntry rematch = round.Options.Single(option =>
                option.Choice == LobbyVoteChoice.Rematch);
            Interlocked.Exchange(ref failNextContinuation, 1);
            await coordinator.ExecuteAsync(owner, new LobbyVoteCast(round.Lobby.Revision,
                round.BallotRevision, rematch.Id));

            Guid failedMatchId = await injected.Task.WaitAsync(stop.Token);
            Assert.NotEqual(first.MatchId, failedMatchId);
            // ContinueAsync must contain the injected exception; the shared
            // continuation loop remains available until this test cancels it.
            Assert.False(continuation.IsCompleted);

            using var cleanupTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            cleanupTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (lobbies.ForSession(owner.SessionId) is not
                { Phase: LobbyPhase.Open, CurrentMatchId: null })
                await Task.Delay(10, cleanupTimeout.Token);
            LobbySnapshot recovered = lobbies.ForSession(owner.SessionId)!;
            Assert.Equal(LobbyPhase.Open, recovered.Phase);
            Assert.Null(recovered.CurrentMatchId);

            while (scheduler.RetentionSnapshot is not
                {
                    ActivePlacements: 0,
                    TerminalPlacements: 0,
                    PendingAdmissionInstalls: 0,
                    PendingAdmissionRetirements: 0
                })
                await Task.Delay(10, cleanupTimeout.Token);
            Assert.False(continuation.IsCompleted);
        }
        finally
        {
            stop.Cancel();
            await continuation;
        }
    }

    [Fact]
    public async Task AcceptedContinuationCancellationStaysFencedUntilActualTerminalAndDoesNotBlockOtherContent()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        var contentA = new ContentIdentity("map-a", "hash-a", "1", "test", 8);
        var contentB = new ContentIdentity("map-b", "hash-b", "1", "test", 8);
        ManagedWorker workerA = await scheduler.StartWorkerAsync(Launch(
            "cancel-hang-after-end", contentA));
        var lobbies = new LobbyManager();
        var ownerA = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner A");
        var ownerB = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner B");
        var failed = new TaskCompletionSource<Guid>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int failNextContinuation = 0;
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog([contentA, contentB]),
            options: new NodeMatchCoordinatorOptions
            {
                CancelAcknowledgementTimeout = TimeSpan.FromSeconds(10),
                TransitionHardTimeout = TimeSpan.FromSeconds(30),
                BeforeMatchCommit = matchId =>
                {
                    if (Interlocked.Exchange(ref failNextContinuation, 0) != 1) return;
                    failed.TrySetResult(matchId.Value);
                    throw new Exception("injected continuation fault");
                }
            });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobbyA = (LobbySnapshot)lobbies.Execute(ownerA,
                new LobbyCreate("Lobby A", LobbyVisibility.Public));
            lobbyA = (LobbySnapshot)await coordinator.ExecuteAsync(ownerA,
                new LobbyConfigure(lobbyA.Revision, contentA.MapKey, MatchMode.Battle));
            lobbyA = (LobbySnapshot)lobbies.Execute(ownerA,
                new LobbySetReady(true, lobbyA.Revision));
            await coordinator.ExecuteAsync(ownerA, new LobbyStart(lobbyA.Revision));
            NodeMatchHandoff initialA = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(ownerA.SessionId));

            var endedA = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, _) =>
            {
                if (matchId == new MatchId(initialA.MatchId)) endedA.TrySetResult();
            };
            Assert.True(scheduler.TrySendMatchAdmin(new(new(initialA.MatchId),
                AdminAction.EndMatch, null)));
            await endedA.Task.WaitAsync(stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerA.SessionId)
                is { Phase: LobbyPhase.PostMatch }, stop.Token);

            NodeRoundSnapshot roundA = lobbies.RoundForSession(ownerA.SessionId)!;
            LobbyVoteEntry rematchA = roundA.Options.Single(option =>
                option.Choice == LobbyVoteChoice.Rematch);
            Interlocked.Exchange(ref failNextContinuation, 1);
            await coordinator.ExecuteAsync(ownerA, new LobbyVoteCast(
                roundA.Lobby.Revision, roundA.BallotRevision, rematchA.Id));
            Guid failedReplacementA = await failed.Task.WaitAsync(stop.Token);

            await WaitUntilAsync(() => scheduler.TryGetCancellationSnapshot(
                new(failedReplacementA), out WorkerCancellationSnapshot snapshot)
                && snapshot.Acknowledged && !snapshot.TerminalObserved, stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerA.SessionId) is
                { Phase: LobbyPhase.StartingMatch, CurrentMatchId: var id }
                && id == failedReplacementA, stop.Token);
            Assert.True(coordinator.TryGetLifecycleState(new(failedReplacementA),
                out MatchLifecycleState state));
            Assert.Equal(MatchLifecycleState.Retiring, state);
            Assert.True(scheduler.TryGetAssignment(new(failedReplacementA),
                out WorkerMatchAssignment? assignmentA));
            Assert.Equal(workerA.Id, assignmentA!.WorkerId);
            Assert.False(continuation.IsCompleted);
            Assert.DoesNotContain(coordinator.ForSessionEvents(ownerA.SessionId),
                payload => payload is NodeMatchHandoff handoff
                    && handoff.MatchId == failedReplacementA);

            // An acknowledgement is progress, not terminal evidence. A
            // watchdog pass at the unchanged clock must leave A fenced.
            coordinator.CheckTransitionWatchdogs();
            Assert.True(scheduler.TryGetCancellationSnapshot(new(failedReplacementA),
                out WorkerCancellationSnapshot unchanged));
            Assert.True(unchanged.Acknowledged);
            Assert.False(unchanged.TerminalObserved);
            Assert.Equal(LobbyPhase.StartingMatch,
                lobbies.ForSession(ownerA.SessionId)!.Phase);
            Assert.True(scheduler.QuarantineWorker(workerA.Id,
                "test continuation fence"));
            Assert.True(scheduler.IsWorkerQuarantined(workerA.Id));

            ManagedWorker workerB = await scheduler.StartWorkerAsync(Launch(
                "controlled-completion", contentB));
            LobbySnapshot lobbyB = (LobbySnapshot)lobbies.Execute(ownerB,
                new LobbyCreate("Lobby B", LobbyVisibility.Public));
            lobbyB = (LobbySnapshot)await coordinator.ExecuteAsync(ownerB,
                new LobbyConfigure(lobbyB.Revision, contentB.MapKey, MatchMode.Battle));
            lobbyB = (LobbySnapshot)lobbies.Execute(ownerB,
                new LobbySetReady(true, lobbyB.Revision));
            await coordinator.ExecuteAsync(ownerB, new LobbyStart(lobbyB.Revision));
            NodeMatchHandoff initialB = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(ownerB.SessionId));
            Assert.True(scheduler.TryGetAssignment(new(initialB.MatchId),
                out WorkerMatchAssignment? assignmentB));
            Assert.Equal(workerB.Id, assignmentB!.WorkerId);
            Assert.NotEqual(workerA.Id, assignmentB.WorkerId);
            Assert.Equal(LobbyPhase.StartingMatch,
                lobbies.ForSession(ownerA.SessionId)!.Phase);
            Assert.Equal(failedReplacementA,
                lobbies.ForSession(ownerA.SessionId)!.CurrentMatchId);

            var endedB = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, _) =>
            {
                if (matchId == new MatchId(initialB.MatchId)) endedB.TrySetResult();
            };
            Assert.True(scheduler.TrySendMatchAdmin(new(new(initialB.MatchId),
                AdminAction.EndMatch, null)));
            await endedB.Task.WaitAsync(stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerB.SessionId)
                is { Phase: LobbyPhase.PostMatch }, stop.Token);
            NodeRoundSnapshot roundB = lobbies.RoundForSession(ownerB.SessionId)!;
            LobbyVoteEntry rematchB = roundB.Options.Single(option =>
                option.Choice == LobbyVoteChoice.Rematch);
            await coordinator.ExecuteAsync(ownerB, new LobbyVoteCast(
                roundB.Lobby.Revision, roundB.BallotRevision, rematchB.Id));
            await WaitUntilAsync(() => coordinator.ForSession(ownerB.SessionId)
                is NodeMatchHandoff handoff && handoff.MatchId != initialB.MatchId,
                stop.Token);
            NodeMatchHandoff replacementB = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(ownerB.SessionId));
            Assert.True(scheduler.TryGetAssignment(new(replacementB.MatchId),
                out WorkerMatchAssignment? replacementAssignmentB));
            Assert.Equal(workerB.Id, replacementAssignmentB!.WorkerId);
            Assert.Equal(LobbyPhase.StartingMatch,
                lobbies.ForSession(ownerA.SessionId)!.Phase);

            // Only the Worker terminal for the failed replacement may cross
            // the retained ownership boundary and move A out of StartingMatch.
            var failedEnded = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, _) =>
            {
                if (matchId == new MatchId(failedReplacementA)) failedEnded.TrySetResult();
            };
            Assert.True(scheduler.TrySendMatchAdmin(new(new(failedReplacementA),
                AdminAction.EndMatch, null)));
            await failedEnded.Task.WaitAsync(stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerA.SessionId) is
                { Phase: LobbyPhase.Open, CurrentMatchId: null }, stop.Token);
            Assert.Contains(coordinator.ForSessionEvents(ownerA.SessionId),
                payload => payload is NodeMatchEnded ended
                    && ended.MatchId == failedReplacementA);
            Assert.DoesNotContain(coordinator.ForSessionEvents(ownerA.SessionId),
                payload => payload is NodeMatchEnded ended
                    && ended.MatchId == initialA.MatchId);

            scheduler.CancelMatch(new(replacementB.MatchId), "test cleanup");
        }
        finally
        {
            stop.Cancel();
            await continuation;
        }
    }

    [Trait("LifecycleStress", "true")]
    [Fact]
    public async Task ContentFreeMixedContinuationsRemainFreshAcrossOneHundredRounds()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("lifecycle-stress");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        {
            Content = new("1", "hash", "test", 8),
            HeartbeatTimeout = TimeSpan.FromSeconds(5)
        });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog(new[] { "unit", "unit2", "unit3" }.Select(key =>
                new ContentIdentity(key, "hash", "1", "test", 8))));
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);

        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Mixed lifecycle", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));

            Guid previousMatch = Guid.Empty;
            ulong previousEpoch = 0;
            string? previousTicket = null;
            ulong previousNonce = 0;
            for (int round = 0; round < 100; round++)
            {
                NodeMatchHandoff handoff = await WaitForFreshHandoffAsync(coordinator,
                    owner.SessionId, previousMatch, stop.Token);
                Assert.NotEqual(previousMatch, handoff.MatchId);
                Assert.True(handoff.LifecycleEpoch.Value > previousEpoch);
                Assert.NotEqual(0ul, handoff.HandoffGeneration.Value);
                if (handoff.UdpAuthenticationEnabled)
                {
                    Assert.NotEqual(Guid.Empty, handoff.AdmissionId);
                    Assert.NotEqual(0u, handoff.WireMatchId);
                }
                if (previousTicket != null)
                {
                    Assert.NotEqual(previousTicket, handoff.Ticket);
                    Assert.NotEqual(previousNonce, handoff.Nonce);
                }
                Assert.True(scheduler.TryGetAssignment(new(handoff.MatchId), out _));

                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnEnded(MatchId matchId, bool _) {
                    if (matchId.Value == handoff.MatchId) ended.TrySetResult();
                }
                scheduler.Ended += OnEnded;
                try
                {
                    Assert.True(scheduler.TrySendMatchAdmin(new(new(handoff.MatchId),
                        AdminAction.EndMatch, null)));
                    await ended.Task.WaitAsync(stop.Token);
                }
                finally { scheduler.Ended -= OnEnded; }

                NodeRoundSnapshot ballot = await WaitForRoundAsync(lobbies,
                    owner.SessionId, stop.Token);
                LobbyVoteChoice choice = (round % 4) switch
                {
                    0 => LobbyVoteChoice.Rematch,
                    1 => LobbyVoteChoice.NextMap,
                    2 => LobbyVoteChoice.Map,
                    _ => LobbyVoteChoice.ReturnToLobby
                };
                LobbyVoteEntry selected = ballot.Options.First(option =>
                    option.Choice == choice);
                await coordinator.ExecuteAsync(owner, new LobbyVoteCast(
                    ballot.Lobby.Revision, ballot.BallotRevision, selected.Id));

                previousMatch = handoff.MatchId;
                previousEpoch = handoff.LifecycleEpoch.Value;
                previousTicket = handoff.Ticket;
                previousNonce = handoff.Nonce;
                Assert.False(scheduler.TrySendMatchAdmin(new(new(previousMatch),
                    AdminAction.EndMatch, null)));

                if (choice == LobbyVoteChoice.ReturnToLobby)
                {
                    await WaitUntilAsync(() => lobbies.ForSession(owner.SessionId) is
                        { Phase: LobbyPhase.Open, CurrentMatchId: null }, stop.Token);
                    if (round != 99)
                    {
                        lobby = lobbies.ForSession(owner.SessionId)!;
                        lobby = (LobbySnapshot)lobbies.Execute(owner,
                            new LobbySetReady(true, lobby.Revision));
                        await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
                    }
                }
            }

            // The final cycle returns to Open before the next manual start; the
            // coordinator and scheduler must not retain any lifecycle work.
            await WaitUntilAsync(() => lobbies.ForSession(owner.SessionId) is
                { Phase: LobbyPhase.Open, CurrentMatchId: null }, stop.Token);
            Assert.Empty(coordinator.LifecycleDiagnosticsSnapshot());
            Assert.Equal(0, scheduler.RetentionSnapshot.ActivePlacements);
            Assert.Equal(0, scheduler.RetentionSnapshot.PendingAdmissionInstalls);
            Assert.Equal(0, scheduler.RetentionSnapshot.PendingAdmissionRetirements);
        }
        finally
        {
            stop.Cancel();
            await continuation;
        }
    }

    [Fact]
    public async Task ContinuationRecoveryHardTimeoutForceRetiresWorkerAndReopensOnlyThatLobby()
    {
        var clock = new MutableClock();
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        var contentA = new ContentIdentity("timeout-a", "timeout-hash-a", "1", "test", 8);
        var contentB = new ContentIdentity("timeout-b", "timeout-hash-b", "1", "test", 8);
        ManagedWorker workerA = await scheduler.StartWorkerAsync(Launch(
            "cancel-hang-after-end", contentA));
        var lobbies = new LobbyManager(clock: clock);
        var ownerA = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Timeout A");
        var ownerB = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Timeout B");
        var failed = new TaskCompletionSource<Guid>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int failNextContinuation = 0;
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog([contentA, contentB]),
            options: new NodeMatchCoordinatorOptions
            {
                Clock = clock,
                CancelAcknowledgementTimeout = TimeSpan.FromSeconds(1),
                TransitionHardTimeout = TimeSpan.FromSeconds(2),
                BeforeMatchCommit = matchId =>
                {
                    if (Interlocked.Exchange(ref failNextContinuation, 0) != 1) return;
                    failed.TrySetResult(matchId.Value);
                    throw new Exception("injected continuation fault");
                }
            });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobbyA = (LobbySnapshot)lobbies.Execute(ownerA,
                new LobbyCreate("Timeout A", LobbyVisibility.Public));
            lobbyA = (LobbySnapshot)await coordinator.ExecuteAsync(ownerA,
                new LobbyConfigure(lobbyA.Revision, contentA.MapKey, MatchMode.Battle));
            lobbyA = (LobbySnapshot)lobbies.Execute(ownerA,
                new LobbySetReady(true, lobbyA.Revision));
            await coordinator.ExecuteAsync(ownerA, new LobbyStart(lobbyA.Revision));
            NodeMatchHandoff initialA = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(ownerA.SessionId));
            Interlocked.Exchange(ref failNextContinuation, 1);
            var endedA = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, _) =>
            {
                if (matchId == new MatchId(initialA.MatchId)) endedA.TrySetResult();
            };
            Assert.True(scheduler.TrySendMatchAdmin(new(new(initialA.MatchId),
                AdminAction.EndMatch, null)));
            await endedA.Task.WaitAsync(stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerA.SessionId)
                is { Phase: LobbyPhase.PostMatch }, stop.Token);
            NodeRoundSnapshot roundA = lobbies.RoundForSession(ownerA.SessionId)!;
            LobbyVoteEntry rematchA = roundA.Options.Single(option =>
                option.Choice == LobbyVoteChoice.Rematch);
            await coordinator.ExecuteAsync(ownerA, new LobbyVoteCast(
                roundA.Lobby.Revision, roundA.BallotRevision, rematchA.Id));
            Guid failedReplacementA = await failed.Task.WaitAsync(stop.Token);
            await WaitUntilAsync(() => scheduler.TryGetCancellationSnapshot(
                new(failedReplacementA), out WorkerCancellationSnapshot snapshot)
                && snapshot.Acknowledged && !snapshot.TerminalObserved, stop.Token);
            Assert.True(scheduler.QuarantineWorker(workerA.Id,
                "test continuation hard-timeout fence"));

            ManagedWorker workerB = await scheduler.StartWorkerAsync(Launch(
                "controlled-completion", contentB));
            LobbySnapshot lobbyB = (LobbySnapshot)lobbies.Execute(ownerB,
                new LobbyCreate("Timeout B", LobbyVisibility.Public));
            lobbyB = (LobbySnapshot)await coordinator.ExecuteAsync(ownerB,
                new LobbyConfigure(lobbyB.Revision, contentB.MapKey, MatchMode.Battle));
            lobbyB = (LobbySnapshot)lobbies.Execute(ownerB,
                new LobbySetReady(true, lobbyB.Revision));
            await coordinator.ExecuteAsync(ownerB, new LobbyStart(lobbyB.Revision));
            NodeMatchHandoff handoffB = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(ownerB.SessionId));
            Assert.True(scheduler.TryGetAssignment(new(handoffB.MatchId),
                out WorkerMatchAssignment? assignmentB));
            Assert.Equal(workerB.Id, assignmentB!.WorkerId);
            Assert.Equal(LobbyPhase.StartingMatch,
                lobbies.ForSession(ownerA.SessionId)!.Phase);

            clock.Advance(TimeSpan.FromSeconds(3));
            coordinator.CheckTransitionWatchdogs();
            await WaitUntilAsync(() => workers.Snapshot().All(snapshot =>
                snapshot.WorkerId != workerA.Id), stop.Token);
            await WaitUntilAsync(() => lobbies.ForSession(ownerA.SessionId) is
                { Phase: LobbyPhase.Open, CurrentMatchId: null }, stop.Token);
            Assert.Equal(LobbyPhase.InMatch,
                lobbies.ForSession(ownerB.SessionId)!.Phase);
            Assert.Contains(workers.Snapshot(), snapshot =>
                snapshot.WorkerId == workerB.Id && snapshot.Status == WorkerStatus.Ready);
            Assert.True(scheduler.TryGetAssignment(new(handoffB.MatchId),
                out WorkerMatchAssignment? liveB));
            Assert.Equal(workerB.Id, liveB!.WorkerId);

            scheduler.CancelMatch(new(handoffB.MatchId), "test cleanup");
        }
        finally
        {
            stop.Cancel();
            await continuation;
        }
    }

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
    public async Task CanceledReconnectWaiterDoesNotCancelNodeOwnedAdmissionInstall()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager, admissionInstallTimeout: TimeSpan.FromSeconds(5));
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("admission-key-delay") with
        { Content = new("1", "hash", "test", 8), HeartbeatTimeout = TimeSpan.FromSeconds(5) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]));
        try
        {
            var lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
            var initial = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));

            // Passive reconnect reconstruction must not mint a credential. An
            // explicit rejoin owns the fresh install; cancel only the waiter,
            // leaving the Node-owned producer running.
            Assert.DoesNotContain(coordinator.ForSessionEvents(owner.SessionId),
                payload => payload is NodeMatchHandoff);
            using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                coordinator.ExecuteAsync(owner, new NodeMatchRejoin(initial.MatchId), canceled.Token));

            NodeMatchHandoff refreshed = await WaitForHandoffAsync(coordinator, owner.SessionId,
                initial.AdmissionId, TimeSpan.FromSeconds(10));
            Assert.Equal(refreshed, Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId)));
            scheduler.CancelMatch(new(initial.MatchId), "test cleanup");
        }
        finally { coordinator.Dispose(); }
    }

    private static async Task<NodeMatchHandoff> WaitForHandoffAsync(NodeMatchCoordinator coordinator,
        Guid sessionId, Guid previousAdmissionId, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            if (coordinator.ForSession(sessionId) is NodeMatchHandoff handoff
                && handoff.AdmissionId != previousAdmissionId) return handoff;
            await Task.Delay(10, cancellation.Token);
        }
    }

    private static async Task<NodeMatchHandoff> WaitForFreshHandoffAsync(
        NodeMatchCoordinator coordinator, Guid sessionId, Guid previousMatch,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (coordinator.ForSession(sessionId) is NodeMatchHandoff handoff
                && handoff.MatchId != previousMatch)
                return handoff;
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task<NodeRoundSnapshot> WaitForRoundAsync(
        LobbyManager lobbies, Guid sessionId, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (lobbies.RoundForSession(sessionId) is { Options.IsEmpty: false } round)
                return round;
            await Task.Delay(10, cancellationToken);
        }
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
        Assert.NotEqual(Guid.Empty, handoff.AdmissionId);
        Assert.NotEmpty(handoff.AdmissionKey);
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompHistory, 0));
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompDynamic, 0));
        Assert.True(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompClear, 0));
        Assert.False(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.EndMatch, 0));
        Assert.False(coordinator.TrySendHistoricalDebug(new(handoff.MatchId), AdminAction.LagCompHistory, 1));
        Assert.DoesNotContain(await coordinator.ForSessionEventsAsync(owner.SessionId),
            payload => payload is NodeMatchHandoff);
        Assert.Equal(placed.CurrentMatchId, handoff.MatchId);
        Assert.DoesNotContain(handoff.Ticket, handoff.ToString());
        Assert.DoesNotContain(handoff.AdmissionKey, handoff.ToString());
        var refreshed = Assert.IsType<NodeMatchHandoff>(await coordinator.ExecuteAsync(owner, new NodeMatchRejoin(handoff.MatchId)));
        Assert.NotEqual(handoff.Ticket, refreshed.Ticket);
        Assert.NotEqual(handoff.Nonce, refreshed.Nonce);
        var retry = Assert.IsType<NodeMatchHandoff>(await coordinator.ExecuteAsync(owner, new NodeMatchRejoin(handoff.MatchId)));
        Assert.NotEqual(refreshed.Ticket, retry.Ticket);
        Assert.NotEqual(refreshed.HandoffGeneration, retry.HandoffGeneration);
        await Assert.ThrowsAsync<LobbyCommandException>(() => coordinator.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Intruder"), new NodeMatchRejoin(handoff.MatchId)));
        var selected = Assert.IsType<LobbySnapshot>(lobbies.Execute(owner,
            new LobbySelectHunter(Hunter.Kanden, placed.Revision)));
        Assert.Equal(Hunter.Kanden, selected.Members.Single().Hunter);
        Assert.Equal(Hunter.Samus, handoff.Hunter);
        Assert.True(lobbies.MatchEnded(new(handoff.MatchId), false));
        Assert.False(lobbies.MatchEnded(new(handoff.MatchId), true));
        var ended = lobbies.ForSession(owner.SessionId)!;
        var ballot = lobbies.RoundForSession(owner.SessionId)!;
        var reopened = ((NodeRoundSnapshot)lobbies.Execute(owner, new LobbyVoteCast(ended.Revision, ballot.BallotRevision, 3))).Lobby;
        Assert.Equal(LobbyPhase.Open, reopened.Phase);
        Assert.False(reopened.Members[0].Ready);
        Assert.Null(reopened.CurrentMatchId);
        // A late terminal event must not repopulate credentials for an expired
        // session after its membership and cached state have been removed.
        lobbies.Disconnect(owner.SessionId);
        coordinator.ClearSessionMatchState(owner.SessionId);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (_, _) => stopped.TrySetResult();
        scheduler.CancelMatch(new(handoff.MatchId), "test cleanup");
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(coordinator.ForSession(owner.SessionId));
    }
    [Fact]
    public async Task NoCapacityReturnsFrozenLobbyToOpenWithoutLosingMembership()
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
        Assert.Equal(LobbyPhase.Open, lobbies.ForSession(owner.SessionId)!.Phase);
        Assert.Single(lobbies.ForSession(owner.SessionId)!.Members);
        Assert.True(Assert.IsType<NodeMatchEnded>(coordinator.ForSession(owner.SessionId)).Interrupted);
    }

    [Fact]
    public async Task SameSessionLeavingLobbyFencesLateMatchEventsAfterJoiningAnotherLobby()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]));

        var lobbyA = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Lobby A", LobbyVisibility.Public));
        lobbyA = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobbyA.Revision, "unit", MatchMode.Battle));
        lobbyA = (LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, lobbyA.Revision));
        var startedA = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyStart(lobbyA.Revision));
        var handoffA = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
        MembershipGeneration generationA = lobbies.MembershipForSession(owner.SessionId)!.Value.Generation;
        Assert.Equal(generationA, handoffA.MembershipGeneration);

        var left = Assert.IsType<LobbyLeft>(await coordinator.ExecuteAsync(owner,
            new LobbyLeave(startedA.Revision)));
        Assert.Equal(lobbyA.LobbyId, left.LobbyId);
        Assert.Null(lobbies.MembershipForSession(owner.SessionId));
        var lobbyB = Assert.IsType<LobbySnapshot>(await coordinator.ExecuteAsync(owner,
            new LobbyCreate("Lobby B", LobbyVisibility.Public)));
        MembershipGeneration generationB = lobbies.MembershipForSession(owner.SessionId)!.Value.Generation;
        Assert.True(generationB.Value > generationA.Value);
        Assert.Equal(lobbyB.LobbyId,
            lobbies.MembershipForSession(owner.SessionId)!.Value.LobbyId);

        // The same Node session is a normal member of the new lobby now. Its
        // new membership boundary must be able to start and receive a fresh
        // handoff while the old Worker is still allowed to drain.
        lobbyB = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyConfigure(lobbyB.Revision, "unit", MatchMode.Battle));
        lobbyB = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbySetReady(true, lobbyB.Revision));
        var startedB = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyStart(lobbyB.Revision));
        var handoffB = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
        Assert.NotEqual(handoffA.MatchId, handoffB.MatchId);
        Assert.Equal(generationB, handoffB.MembershipGeneration);
        Assert.Equal(startedB.CurrentMatchId, handoffB.MatchId);

        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (matchId, _) =>
        {
            if (matchId == new MatchId(handoffA.MatchId)) ended.TrySetResult();
        };
        Assert.True(scheduler.TrySendMatchAdmin(new(new(handoffA.MatchId), AdminAction.EndMatch, null)));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The old completion/ended callbacks are still allowed to arrive from
        // the Worker, but neither may repopulate state for the new membership
        // boundary. The new lobby remains the only current session owner.
        Assert.Equal(handoffB,
            Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId)));
        Assert.DoesNotContain(coordinator.ForSessionEvents(owner.SessionId), payload =>
            payload switch
            {
                NodeMatchHandoff handoff => handoff.MatchId == handoffA.MatchId,
                NodeMatchCompletion completion => completion.Summary.MatchId.Value == handoffA.MatchId,
                NodeMatchEnded endedEvent => endedEvent.MatchId == handoffA.MatchId,
                _ => false
            });
        Assert.Equal(lobbyB.LobbyId, lobbies.ForSession(owner.SessionId)!.LobbyId);
        Assert.Equal(handoffB.MatchId, lobbies.ForSession(owner.SessionId)!.CurrentMatchId);
        Assert.Equal(handoffB, coordinator.ForSession(owner.SessionId));

        // Finish the second match so this test does not leave an active
        // controlled Worker placement for fixture teardown.
        var secondEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (matchId, _) =>
        {
            if (matchId == new MatchId(handoffB.MatchId)) secondEnded.TrySetResult();
        };
        Assert.True(scheduler.TrySendMatchAdmin(new(new(handoffB.MatchId), AdminAction.EndMatch, null)));
        await secondEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IReadOnlyList<object> secondMatchEvents = coordinator.ForSessionEvents(owner.SessionId);
        Assert.Contains(secondMatchEvents, payload => payload is NodeMatchCompletion completion
            && completion.Summary.MatchId.Value == handoffB.MatchId);
        Assert.Contains(secondMatchEvents, payload => payload is NodeMatchEnded terminal
            && terminal.MatchId == handoffB.MatchId);
    }

    [Fact]
    public async Task RemainingMemberReceivesTerminalAfterPeerLeaves()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var peer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Peer");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]));

        LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Shared", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)lobbies.Execute(peer,
            new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)lobbies.Execute(peer,
            new LobbySetReady(true, lobby.Revision));
        LobbySnapshot started = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyStart(lobby.Revision));
        NodeMatchHandoff ownerHandoff = Assert.IsType<NodeMatchHandoff>(
            coordinator.ForSession(owner.SessionId));
        NodeMatchHandoff peerHandoff = Assert.IsType<NodeMatchHandoff>(
            coordinator.ForSession(peer.SessionId));
        Assert.Equal(ownerHandoff.MatchId, peerHandoff.MatchId);
        Assert.Equal(started.CurrentMatchId, ownerHandoff.MatchId);

        // The peer's membership boundary is removed while the Worker still
        // owns the frozen match. The owner remains a valid recipient of the
        // terminal and completion payloads for that same frozen match.
        LobbySnapshot peerLobby = lobbies.ForSession(peer.SessionId)!;
        Assert.IsType<LobbyLeft>(await coordinator.ExecuteAsync(peer,
            new LobbyLeave(peerLobby.Revision)));
        Assert.Null(lobbies.MembershipForSession(peer.SessionId));
        Assert.NotNull(lobbies.MembershipForSession(owner.SessionId));

        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Ended += (matchId, _) =>
        {
            if (matchId == new MatchId(ownerHandoff.MatchId)) ended.TrySetResult();
        };
        Assert.True(scheduler.TrySendMatchAdmin(new(new(ownerHandoff.MatchId), AdminAction.EndMatch, null)));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        IReadOnlyList<object> ownerEvents = coordinator.ForSessionEvents(owner.SessionId);
        Assert.Contains(ownerEvents, payload => payload is NodeMatchCompletion completion
            && completion.Summary.MatchId.Value == ownerHandoff.MatchId);
        Assert.Contains(ownerEvents, payload => payload is NodeMatchEnded terminal
            && terminal.MatchId == ownerHandoff.MatchId);
        Assert.DoesNotContain(coordinator.ForSessionEvents(peer.SessionId), payload =>
            (payload is NodeMatchCompletion completion
                && completion.Summary.MatchId.Value == ownerHandoff.MatchId)
            || (payload is NodeMatchEnded terminal && terminal.MatchId == ownerHandoff.MatchId));
        Assert.Equal(LobbyPhase.PostMatch, lobbies.ForSession(owner.SessionId)!.Phase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task PresentationOnlyChatDuringStartDoesNotInvalidateFrozenMatch(int chatCount)
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]),
            options: new NodeMatchCoordinatorOptions
            {
                BeforeMatchCommit = _ =>
                {
                    for (int index = 0; index < chatCount; index++)
                    {
                        LobbySnapshot current = lobbies.ForSession(owner.SessionId)!;
                        lobbies.Execute(owner, new LobbyChat("start-chat", current.Revision));
                    }
                }
            });

        LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Chat", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        LobbySnapshot started = (LobbySnapshot)await coordinator.ExecuteAsync(owner,
            new LobbyStart(lobby.Revision));

        Assert.Equal(LobbyPhase.InMatch, started.Phase);
        Assert.NotNull(Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId)));
        Assert.Equal(Math.Min(chatCount, 16), started.Chat.Length);
    }

    [Fact]
    public async Task MemberLeaveDuringStartCancelsWithoutPublishingPartialHandoff()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager,
            admissionInstallTimeout: TimeSpan.FromMilliseconds(100));
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var peer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Peer");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]),
            options: new NodeMatchCoordinatorOptions
            {
                BeforeMatchCommit = _ =>
                {
                    LobbySnapshot current = lobbies.ForSession(peer.SessionId)!;
                    lobbies.Execute(peer, new LobbyLeave(current.Revision));
                }
            });

        LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Leave", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)lobbies.Execute(peer,
            new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)lobbies.Execute(peer,
            new LobbySetReady(true, lobby.Revision));

        LobbyCommandException error = await Assert.ThrowsAsync<LobbyCommandException>(() =>
            coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision)));
        Assert.Equal("placement_failed", error.Code);
        Assert.Equal(LobbyPhase.Open, lobbies.ForSession(owner.SessionId)!.Phase);
        Assert.Null(lobbies.ForSession(owner.SessionId)!.CurrentMatchId);
        Assert.Null(lobbies.MembershipForSession(peer.SessionId));
        Assert.DoesNotContain(coordinator.ForSessionEvents(owner.SessionId),
            payload => payload is NodeMatchHandoff);
        Assert.Equal(0, scheduler.RetentionSnapshot.PendingAdmissionInstalls);
        Assert.Equal(0, scheduler.RetentionSnapshot.PendingAdmissionRetirements);
    }

    [Fact]
    public void SameLobbyReentryGetsFreshMembershipBoundary()
    {
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var peer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Peer");
        LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Reentry", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)lobbies.Execute(peer,
            new LobbyJoin(lobby.LobbyId, lobby.Revision));
        MembershipGeneration before = lobbies.MembershipForSession(owner.SessionId)!.Value.Generation;

        LobbyLeft left = Assert.IsType<LobbyLeft>(lobbies.Execute(owner,
            new LobbyLeave(lobby.Revision)));
        Assert.Equal(lobby.LobbyId, left.LobbyId);
        Assert.False(lobbies.IsCurrentMembership(owner.SessionId, lobby.LobbyId, before));

        LobbySnapshot rejoined = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyJoin(lobby.LobbyId, lobbies.ForSession(peer.SessionId)!.Revision));
        MembershipGeneration after = lobbies.MembershipForSession(owner.SessionId)!.Value.Generation;
        Assert.True(after.Value > before.Value);
        Assert.True(lobbies.IsCurrentMembership(owner.SessionId, rejoined.LobbyId, after));
        Assert.False(lobbies.IsCurrentMembership(owner.SessionId, rejoined.LobbyId, before));
    }

    [Fact]
    public async Task ExpiredSessionStateCanBeClearedWithoutPoisoningLaterMembership()
    {
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager);
        var lobbies = new LobbyManager();
        using var issuer = new WorkerAdmissionIssuer("test");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]));
        var identity = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");

        var first = (LobbySnapshot)lobbies.Execute(identity,
            new LobbyCreate("First", LobbyVisibility.Public));
        MembershipGeneration firstGeneration = lobbies.MembershipForSession(identity.SessionId)!.Value.Generation;
        coordinator.ExpireSession(identity.SessionId);
        lobbies.Disconnect(identity.SessionId);
        Assert.Null(lobbies.MembershipForSession(identity.SessionId));

        var second = (LobbySnapshot)lobbies.Execute(identity,
            new LobbyCreate("Second", LobbyVisibility.Public));
        MembershipGeneration secondGeneration = lobbies.MembershipForSession(identity.SessionId)!.Value.Generation;
        Assert.True(secondGeneration.Value > firstGeneration.Value);
        Assert.Equal(second.LobbyId,
            lobbies.MembershipForSession(identity.SessionId)!.Value.LobbyId);
        Assert.Null(coordinator.ForSession(identity.SessionId));
    }

    private static WorkerLaunchOptions Launch(string mode,
        WorkerContentIdentity content)
    {
        WorkerLaunchOptions launch = WorkerManagerTests.Launch(mode);
        return launch with
        {
            Content = content,
            Arguments = [.. launch.Arguments,
                "--test-content-version", content.ContentVersion,
                "--test-content-hash", content.ContentHash,
                "--test-content-build", content.BuildVersion,
                "--test-content-protocol", content.ProtocolVersion.ToString()]
        };
    }

    private static WorkerLaunchOptions Launch(string mode, ContentIdentity content)
        => Launch(mode, new WorkerContentIdentity(content.ContentVersion,
            content.ContentHash, content.BuildVersion, content.ProtocolVersion));

    private static async Task WaitUntilAsync(Func<bool> predicate,
        CancellationToken cancellationToken)
    {
        while (!predicate())
            await Task.Delay(10, cancellationToken);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
