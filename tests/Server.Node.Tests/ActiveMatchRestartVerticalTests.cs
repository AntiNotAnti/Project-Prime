using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Trait("LifecycleVertical", "true")]
public sealed class ActiveMatchRestartVerticalTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RealWorkerRestartWithFixedBotsPublishesFreshHandoffTwice()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? throw new InvalidOperationException("GAME_DATA_DIRECTORY is required.");
        using var artifacts = new ArtifactDirectory();
        var launch = new WorkerLaunchOptions
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments =
            [
                typeof(WorkerOptions).Assembly.Location,
                "--content-dir", Path.GetFullPath(data),
                "--content-version", "AMHE1",
                "--lanes", "1",
                "--max-matches", "2",
                "--max-matches-per-lane", "2",
                "--replay-dir", Path.Combine(artifacts.Path, "replays")
            ],
            ArtifactDirectory = artifacts.Path,
            Capacity = new(2, 16, 0, 0),
            StartupTimeout = TimeSpan.FromSeconds(30),
            ShutdownTimeout = TimeSpan.FromSeconds(3)
        };

        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers, TimeSpan.FromSeconds(20));
        using var issuer = new WorkerAdmissionIssuer("restart-vertical-test");
        var workerEvents = new ConcurrentQueue<string>();
        scheduler.Observed += (_, message) =>
        {
            if (message is not WorkerHeartbeat)
                workerEvents.Enqueue(message.ToString() ?? message.GetType().Name);
        };
        ManagedWorker worker = await scheduler.StartWorkerAsync(launch);
        await scheduler.InitializeSigningKeyAsync(worker,
            new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey()));
        WorkerContentIdentity workerContent = worker.Content
            ?? throw new InvalidOperationException("Worker did not publish content identity.");
        var content = new ContentIdentity("MP1 SANCTORUS", workerContent.ContentHash,
            workerContent.ContentVersion, workerContent.BuildVersion,
            workerContent.ProtocolVersion);
        var clock = new MutableClock();
        var lobbies = new LobbyManager(clock: clock);
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers,
            issuer, new NodeContentCatalog([content]),
            NullLogger<NodeMatchCoordinator>.Instance,
            new NodeMatchCoordinatorOptions { Clock = clock });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var notifications = new ConcurrentQueue<NodeMatchNotification>();
        Task reader = Task.Run(async () =>
        {
            try
            {
                await foreach (NodeMatchNotification notification in
                    coordinator.ReadNotifications(stop.Token))
                    notifications.Enqueue(notification);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        Task continuation = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Restart", LobbyVisibility.Public, 4));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, content.MapKey, MatchMode.Battle,
                    BotCount: 2, BotDifficulty: BotDifficulty.Hard));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));

            NodeMatchHandoff current = Assert.IsType<NodeMatchHandoff>(
                coordinator.ForSession(owner.SessionId));
            Guid sessionId = owner.SessionId;
            Guid lobbyId = lobby.LobbyId;
            for (int restart = 0; restart < 2; restart++)
            {
                WorkerMatchAssignment priorAssignment = Assignment(current.MatchId);
                Assert.Equal(2, priorAssignment.Spec.Roster.Count(seat =>
                    seat.Role == SeatRole.Bot));
                Assert.Equal(BotDifficulty.Hard, priorAssignment.Spec.BotDifficulty);

                int deliveredBeforeRestart = notifications.Count;
                NodeMatchTransitionVoteSnapshot approved = Assert.IsType<NodeMatchTransitionVoteSnapshot>(
                    await coordinator.ExecuteAsync(owner, new LobbyMatchTransitionPropose(
                        lobbies.ForSession(sessionId)!.Revision, current.MatchId,
                        MatchTransitionChoice.Restart)));
                Assert.Equal(MatchTransitionVoteState.Approved, approved.State);

                Guid previousMatchId = current.MatchId;
                string previousTicket = current.Ticket;
                ulong previousNonce = current.Nonce;
                try
                {
                    await WaitUntilAsync(() => notifications.Skip(deliveredBeforeRestart)
                        .Any(notification => notification.SessionId == sessionId
                            && notification.Payload is NodeMatchHandoff next
                            && next.MatchId != previousMatchId), stop.Token);
                }
                catch (OperationCanceledException error)
                {
                    throw new InvalidOperationException(
                        $"restart={restart}; lobby={lobbies.ForSession(sessionId)}; "
                        + $"current={coordinator.ForSession(sessionId)}; "
                        + $"workers={string.Join(" | ", workers.Snapshot())}; "
                        + $"events={string.Join(" | ", workerEvents)}; "
                        + $"notifications={string.Join(" | ", notifications.Select(value => value.Payload))}",
                        error);
                }
                current = notifications.Skip(deliveredBeforeRestart)
                    .Where(notification => notification.SessionId == sessionId)
                    .Select(notification => notification.Payload)
                    .OfType<NodeMatchHandoff>()
                    .Last(handoff => handoff.MatchId != previousMatchId);

                Assert.Equal(sessionId, owner.SessionId);
                Assert.Equal(lobbyId, lobbies.ForSession(sessionId)!.LobbyId);
                Assert.Equal(LobbyPhase.InMatch, lobbies.ForSession(sessionId)!.Phase);
                Assert.NotEqual(previousMatchId, current.MatchId);
                Assert.NotEqual(previousTicket, current.Ticket);
                Assert.NotEqual(previousNonce, current.Nonce);
                NodeMatchNotification[] delivered = notifications
                    .Where(notification => notification.SessionId == sessionId).ToArray();
                int started = Array.FindLastIndex(delivered, notification =>
                    notification.Payload is NodeMatchTransitionStarted value
                    && value.PreviousMatchId == previousMatchId);
                int terminal = Array.FindLastIndex(delivered, notification =>
                    notification.Payload is NodeMatchEnded value
                    && value.MatchId == previousMatchId && value.Interrupted);
                int handoff = Array.FindLastIndex(delivered, notification =>
                    notification.Payload is NodeMatchHandoff value
                    && value.MatchId == current.MatchId);
                Assert.True(started >= 0 && terminal > started && handoff > terminal,
                    string.Join(" | ", delivered.Select(value => value.Payload.GetType().Name)));
                Assert.False(scheduler.TryGetAssignment(new(previousMatchId), out _));

                WorkerMatchAssignment replacement = Assignment(current.MatchId);
                Assert.Equal(2, replacement.Spec.Roster.Count(seat =>
                    seat.Role == SeatRole.Bot));
                Assert.Equal(priorAssignment.Spec.Roster
                        .Where(seat => seat.Role == SeatRole.Bot)
                        .Select(seat => (seat.SeatId, seat.DisplayName, seat.Team, seat.Role)),
                    replacement.Spec.Roster
                        .Where(seat => seat.Role == SeatRole.Bot)
                        .Select(seat => (seat.SeatId, seat.DisplayName, seat.Team, seat.Role)));

                if (restart == 0)
                {
                    LobbyCommandException cooldown = await Assert.ThrowsAsync<LobbyCommandException>(
                        () => coordinator.ExecuteAsync(owner,
                            new LobbyMatchTransitionPropose(
                                lobbies.ForSession(sessionId)!.Revision, current.MatchId,
                                MatchTransitionChoice.Restart)));
                    Assert.Equal("cooldown", cooldown.Code);
                    // Exact expiry is sufficient and keeps the synthetic Node
                    // clock within the Worker's allowed admission-key skew.
                    clock.Advance(LobbyManager.MatchTransitionProposerCooldown);
                    lobbies.PruneWaitlists();
                }
            }

            scheduler.CancelMatch(new(current.MatchId), "restart vertical cleanup");
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(continuation, reader);
        }

        WorkerMatchAssignment Assignment(Guid matchId)
        {
            Assert.True(scheduler.TryGetAssignment(new(matchId), out var assignment));
            return assignment!;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(10, cancellationToken);
    }

    private sealed class ArtifactDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "restart-vertical-" + Guid.NewGuid().ToString("N"));

        public ArtifactDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
