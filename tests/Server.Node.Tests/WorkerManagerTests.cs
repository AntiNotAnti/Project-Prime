using System.Collections.Immutable;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class WorkerManagerTests
{
    internal static WorkerLaunchOptions Launch(string mode) => new()
    {
        FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
        Arguments = [Harness(), "--mode", mode],
        StartupTimeout = TimeSpan.FromSeconds(5), HeartbeatTimeout = TimeSpan.FromMilliseconds(600),
        ShutdownTimeout = TimeSpan.FromSeconds(2)
    };
    private static string Harness()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "tests", "WorkerProcessHarness"))) root = root.Parent;
        Assert.NotNull(root);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(root.FullName, "tests", "WorkerProcessHarness", "bin", configuration, "net10.0", "WorkerProcessHarness.dll");
    }
    private static WorkerManager Manager() => new(new(Guid.NewGuid()), Guid.NewGuid());

    [Fact]
    public async Task RealProcessAuthenticatesHeartbeatsDrainsAndShutsDown()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("normal"));
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (await worker.Events.ReadAsync(deadline.Token) is not WorkerHeartbeat) { }
        Assert.NotNull(worker.Snapshot().Health);
        Assert.True(worker.Drain("update"));
        while (await worker.Events.ReadAsync(deadline.Token) is not WorkerDraining) { }
        Assert.Equal(WorkerStatus.Draining, worker.Snapshot().Status);
        Assert.False(worker.TrySend(new CreateMatch(Spec(manager))));
        await worker.ShutdownAsync("update");
        Assert.Equal(WorkerStatus.Stopped, worker.Snapshot().Status);
    }

    [Fact]
    public async Task WorkerChildReceivesDataDirectoryButNeverNodeDirectoryCredential()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "project-prime-map-data-forwarding");
        string? previousSecret = Environment.GetEnvironmentVariable("PRIME_NODE_DIRECTORY_SECRET");
        string? previousData = Environment.GetEnvironmentVariable("PRIME_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("PRIME_NODE_DIRECTORY_SECRET", "test-only-marker");
            Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", dataDirectory);
            await using var manager = Manager();
            WorkerLaunchOptions launch = Launch("environment");
            launch = launch with
            {
                Arguments = [.. launch.Arguments, "--expected-data-directory", dataDirectory]
            };
            ManagedWorker worker = await manager.StartAsync(launch);
            Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
            await worker.ShutdownAsync("test completed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRIME_NODE_DIRECTORY_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", previousData);
        }
    }

    [Fact]
    public async Task WorkerStartupSecretNeverAppearsInChildProcessArguments()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("secret-canary"));
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        await worker.ShutdownAsync("secret canary completed");
    }

    [Fact]
    public async Task NodeLaunchPassesConfiguredSnapshotRateToWorkerProcess()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("snapshot-rate") with { SnapshotRateHz = 60 });
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        await worker.ShutdownAsync("snapshot cadence completed");
    }

    [Fact]
    public async Task NodeLaunchPassesAdaptiveTimingFlagsToWorkerProcess()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("adaptive-timing") with
        {
            AdaptiveTimingEnabled = true,
            AdaptiveTimingV2Enabled = true,
            AdaptiveInputPlayoutEnabled = true
        });
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        await worker.ShutdownAsync("adaptive timing flags completed");
    }

    [Fact]
    public async Task NodeLaunchPassesTransportAndReliableRollbackFlags()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("network-flags") with
        {
            TransportQueueV2Enabled = true,
            TransportCriticalReserveEnabled = false,
            CriticalTransportReserve = 16,
            WorkerGlobalNetworkBudgetEnabled = false,
            MaximumDatagramsPerPump = 256,
            ReliableAdaptiveRtoEnabled = true
        });
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        await worker.ShutdownAsync("network flags completed");
    }

    [Fact]
    public async Task NodeLaunchPassesOptInAckCoalescingFlagToWorkerProcess()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("ack-coalescing") with
        {
            AckCoalescingEnabled = true
        });
        Assert.Equal(WorkerStatus.Ready, worker.Snapshot().Status);
        await worker.ShutdownAsync("ack coalescing flag completed");
    }

    [Fact]
    public async Task WorkerSecretCanaryRejectsCommandLineSubstringMatches()
    {
        const string dotnet = "dotnet";
        string secret = new('S', 64);
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? dotnet)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(Harness());
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add("secret-canary");
        start.ArgumentList.Add("--node-id");
        start.ArgumentList.Add(Guid.NewGuid().ToString("D"));
        start.ArgumentList.Add("--worker-id");
        start.ArgumentList.Add(Guid.NewGuid().ToString("D"));
        start.ArgumentList.Add("--worker-incarnation");
        start.ArgumentList.Add(Guid.NewGuid().ToString("D"));
        start.ArgumentList.Add("prefix-" + secret + "-suffix");
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.StandardInput.WriteLineAsync(secret);
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(29, process.ExitCode);
    }

    [Fact]
    public async Task ChildOutputAndLifecycleFailureReasonAreSanitized()
    {
        await using var manager = Manager();
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => manager.StartAsync(Launch("secret-output")));
        Assert.DoesNotContain("A24-lifecycle-output-canary", failure.ToString(), StringComparison.Ordinal);
        Assert.Empty(manager.Snapshot());
    }

    [Theory]
    [InlineData("token")]
    [InlineData("identity")]
    [InlineData("exit")]
    [InlineData("hang")]
    [InlineData("profile")]
    [InlineData("stale-ready")]
    [InlineData("bad-capacity")]
    public async Task InvalidStartupFailsAndReapsChild(string mode)
    {
        await using var manager = Manager();
        await Assert.ThrowsAnyAsync<Exception>(() => manager.StartAsync(Launch(mode) with { StartupTimeout = TimeSpan.FromMilliseconds(800), Content = new("1", "hash", "test", 8) }));
        Assert.Empty(manager.Snapshot());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("silent")]
    [InlineData("heartbeat-stall")]
    [InlineData("heartbeat-stop-after-ready")]
    [InlineData("disconnect")]
    [InlineData("duplicate-ready")]
    public async Task ConsumedHelloMalformedFrameAndHeartbeatLossAreTerminal(string mode)
    {
        await using var manager = Manager();
        ManagedWorker? worker = null;
        try { worker = await manager.StartAsync(Launch(mode)); }
        catch (IOException) { } // The protocol violation can beat the ready continuation.
        if (worker != null) await worker.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(WorkerStatus.Faulted, Assert.Single(manager.Snapshot()).Status);
    }

    [Fact]
    public async Task ProcessCrashInterruptsOwnedMatchAndDoesNotRestartIt()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("crash-match"));
        MatchSpec spec = Spec(manager);
        Assert.True(worker.TrySend(new CreateMatch(spec)));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        WorkerSnapshot snapshot = Assert.Single(manager.Snapshot());
        Assert.Equal(WorkerStatus.Faulted, snapshot.Status);
        Assert.Equal(MatchStatus.Interrupted, snapshot.Matches[spec.MatchId]);
        Assert.False(worker.TrySend(new CreateMatch(Spec(manager))));
    }

    [Fact]
    public async Task DrainAcknowledgementPermitsCleanExitWithoutShutdownCommand()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("drain-exit"));
        Assert.True(worker.Drain("update"));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(WorkerStatus.Stopped, worker.Snapshot().Status);
    }

    [Fact]
    public async Task ForcedShutdownDeadlineReapsUnresponsiveChild()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("ignore-shutdown") with { ShutdownTimeout = TimeSpan.FromMilliseconds(200) });
        await Assert.ThrowsAsync<TimeoutException>(() => worker.ShutdownAsync("deadline"));
        int pid = worker.Snapshot().ProcessId!.Value;
        Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
        Assert.Equal(WorkerStatus.Faulted, worker.Snapshot().Status);
    }

    [Fact]
    public async Task StartupCancellationReapsChild()
    {
        await using var manager = Manager();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.StartAsync(Launch("hang"), cancel.Token));
        Assert.Empty(manager.Snapshot());
    }

    [Fact]
    public async Task SlowEventConsumerFaultsInsteadOfGrowingMemory()
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch("normal") with { EventCapacity = 1 });
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(WorkerStatus.Faulted, worker.Snapshot().Status);
    }

    [Theory]
    [InlineData("stale-match")]
    [InlineData("wrong-host")]
    [InlineData("duplicate-terminal")]
    [InlineData("premature-report")]
    [InlineData("interrupted-report")]
    [InlineData("wrong-report-id")]
    [InlineData("conflicting-report")]
    public async Task InvalidMatchOwnershipEndpointAndTerminalTransitionsFaultWorker(string mode)
    {
        await using var manager = Manager();
        ManagedWorker worker = await manager.StartAsync(Launch(mode));
        Assert.True(worker.TrySend(new CreateMatch(Spec(manager))));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(WorkerStatus.Faulted, worker.Snapshot().Status);
    }

    [Fact]
    public async Task NodeDisposalReapsEveryOwnedChild()
    {
        var manager = Manager();
        var first = await manager.StartAsync(Launch("normal"));
        var second = await manager.StartAsync(Launch("normal"));
        int[] ids = [first.Snapshot().ProcessId!.Value, second.Snapshot().ProcessId!.Value];
        await manager.DisposeAsync();
        await manager.DisposeAsync();
        foreach (int id in ids) Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(id));
        Assert.True(first.Completion.IsCompletedSuccessfully);
        Assert.True(second.Completion.IsCompletedSuccessfully);
    }

    internal static MatchSpec Spec(WorkerManager manager) => new(new(Guid.NewGuid()), new(Guid.NewGuid()), manager.NodeId, manager.NodeIncarnation,
        new MatchRules(MatchMode.Battle, "test", 2), new("test", "hash", "1", "test", 8), MatchTrustClass.Private, null, null,
        ImmutableArray.Create(new RosterSeat(0, new PlayerId(Guid.NewGuid()), null, "Player", Hunter.Samus, 0, SeatRole.Player, false)),
        BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Record, TelemetryPolicy.Record, 1, 2);
}
