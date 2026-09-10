using System.Collections.Immutable;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
using MphRead;
using MphRead.Identity;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class RealWorkerProcessTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task TwoRealWorkersHostIndependentMatchesAndCrashOnlyInterruptsItsOwner()
    {
        string? data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("Set GAME_DATA_DIRECTORY to extracted AMHE1 for real Worker integration.");
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager, TimeSpan.FromSeconds(20));
        using var issuer = new WorkerAdmissionIssuer("process-test");
        WorkerLaunchOptions launch = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [typeof(WorkerOptions).Assembly.Location, "--content-dir", Path.GetFullPath(data), "--content-version", "AMHE1",
                "--lanes", "1", "--max-matches", "2", "--max-matches-per-lane", "2"],
            Capacity = new(2, 16, 0, 0), StartupTimeout = TimeSpan.FromSeconds(30), HeartbeatTimeout = TimeSpan.FromSeconds(10),
            ShutdownTimeout = TimeSpan.FromSeconds(3)
        };
        ManagedWorker a = await scheduler.StartWorkerAsync(launch);
        ManagedWorker b = await scheduler.StartWorkerAsync(launch);
        Assert.True(a.TrySend(new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey())));
        Assert.True(b.TrySend(new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey())));
        var content = Assert.IsType<WorkerContentIdentity>(a.Content);
        Assert.Equal(content, b.Content);
        MatchSpec Spec() => new(new(Guid.NewGuid()), new(Guid.NewGuid()), manager.NodeId, manager.NodeIncarnation,
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", 2), new("MP1 SANCTORUS", content.ContentHash, content.ContentVersion, content.BuildVersion, content.ProtocolVersion),
            MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(0, null, null, "BotA", Hunter.Spire, 0, SeatRole.Bot, false),
                new RosterSeat(1, null, null, "BotB", Hunter.Samus, 1, SeatRole.Bot, false)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        MatchSpec firstSpec = Spec(), secondSpec = Spec(), thirdSpec = Spec(), fourthSpec = Spec();
        MatchPlacement first = await scheduler.PlaceAsync(firstSpec);
        MatchPlacement second = await scheduler.PlaceAsync(secondSpec);
        MatchPlacement third = await scheduler.PlaceAsync(thirdSpec);
        MatchPlacement fourth = await scheduler.PlaceAsync(fourthSpec);
        Assert.NotEqual(first.WorkerId, second.WorkerId);
        Assert.NotEqual(first.MatchId, third.MatchId);
        Assert.Equal(2, a.Snapshot().Matches.Count);
        Assert.Equal(2, b.Snapshot().Matches.Count);
        await Assert.ThrowsAsync<WorkerPlacementException>(() => scheduler.PlaceAsync(Spec()));
        using (var progress = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            while (a.Snapshot().Health?.Diagnostics is not { } metrics || metrics.Lanes.All(l => l.Ticks < 60))
                await Task.Delay(25, progress.Token);
        Assert.All(a.Snapshot().Matches.Values, status => Assert.Equal(MatchStatus.Running, status));
        var interrupted = new System.Collections.Concurrent.ConcurrentDictionary<MatchId, bool>();
        scheduler.Ended += (id, failed) => interrupted[id] = failed;
        using (var child = System.Diagnostics.Process.GetProcessById(a.Snapshot().ProcessId!.Value)) child.Kill();
        await a.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (interrupted.Count < 2) await Task.Delay(10, deadline.Token);
        Assert.All(a.Snapshot().Matches, match => Assert.Equal(MatchStatus.Interrupted, match.Value));
        Assert.Equal(WorkerStatus.Ready, b.Snapshot().Status);
        Assert.All(b.Snapshot().Matches, match => Assert.True(match.Value is MatchStatus.Ready or MatchStatus.Running));
        Assert.All(interrupted.Values, Assert.True);
        foreach (MatchId id in b.Snapshot().Matches.Keys) Assert.True(scheduler.CancelMatch(id, "integration cleanup"));
        await scheduler.WaitForDrainAsync(deadline.Token);
        await scheduler.ShutdownAsync("integration complete");
    }

    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData("Starting")]
    [InlineData("Playing")]
    [InlineData("Ending")]
    public async Task CrashAtObservedLifecyclePhaseInterruptsExactlyOnce(string phase)
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? throw new InvalidOperationException("Set GAME_DATA_DIRECTORY to extracted AMHE1.");
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(manager, TimeSpan.FromSeconds(20));
        using var issuer = new WorkerAdmissionIssuer("phase-test");
        var worker = await scheduler.StartWorkerAsync(new WorkerLaunchOptions
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [typeof(WorkerOptions).Assembly.Location, "--content-dir", Path.GetFullPath(data), "--content-version", "AMHE1"],
            Capacity = new(1, 8, 0, 0), StartupTimeout = TimeSpan.FromSeconds(30), HeartbeatTimeout = TimeSpan.FromSeconds(10)
        });
        Assert.True(worker.TrySend(new UpdateNodeSigningKey(issuer.KeyId, issuer.ExportPublicKey())));
        var content = Assert.IsType<WorkerContentIdentity>(worker.Content);
        var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), manager.NodeId, manager.NodeIncarnation,
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", 2), new("MP1 SANCTORUS", content.ContentHash, content.ContentVersion, content.BuildVersion, content.ProtocolVersion),
            MatchTrustClass.Private, null, null,
            ImmutableArray.Create(new RosterSeat(0, null, null, "BotA", Hunter.Spire, 0, SeatRole.Bot, false),
                new RosterSeat(1, null, null, "BotB", Hunter.Samus, 1, SeatRole.Bot, false)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        scheduler.Ended += (id, interrupted) => { Assert.Equal(spec.MatchId, id); Interlocked.Increment(ref count); ended.TrySetResult(interrupted); };
        Task<MatchPlacement> placement = scheduler.PlaceAsync(spec);
        if (phase == "Starting")
        {
            // This is Node's accepted creation reservation, before waiting for Worker Ready.
            Assert.Equal(MatchStatus.Starting, worker.Snapshot().Matches[spec.MatchId]);
        }
        else
        {
            await placement;
            await WaitForPhase("Playing");
            if (phase == "Ending")
            {
                Assert.True(worker.TrySend(new MatchAdminCommand(spec.MatchId, AdminAction.EndMatch, null)));
                await WaitForPhase("Ending");
            }
        }
        using (var child = System.Diagnostics.Process.GetProcessById(worker.Snapshot().ProcessId!.Value)) child.Kill();
        try { await placement; } catch (WorkerPlacementException) { }
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.Equal(MatchStatus.Interrupted, worker.Snapshot().Matches[spec.MatchId]);

        async Task WaitForPhase(string expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (worker.Snapshot().Health?.Diagnostics is not { } diagnostics
                || diagnostics.Matches.IsDefault || !diagnostics.Matches.Any(m => m.MatchId == spec.MatchId && m.Phase == expected))
                await Task.Delay(20, deadline.Token);
        }
    }
}
