using System.Collections.Immutable;
using System.Net;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using SharedBotFillPolicy = ProjectPrime.Server.Shared.BotFillPolicy;
using Xunit;

namespace ProjectPrime.Server.Worker.Tests;

[Collection("Match baseline globals")]
[Trait("LifecycleFast", "true")]
public sealed class WorkerArtifactPipelineTests
{
    [Fact]
    public async Task BlockedReplayForADoesNotBlockIndependentArtifactJobB()
    {
        MatchId a = new(Guid.NewGuid());
        MatchId b = new(Guid.NewGuid());
        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipeline = new WorkerArtifactPipeline(2, 4, async (facts, _) =>
        {
            if (facts.Spec.MatchId == a)
            {
                aStarted.TrySetResult();
                await releaseA.Task;
            }
            else bFinished.TrySetResult();
            return new(null, null, 0);
        });

        try
        {
            Assert.True(pipeline.TryEnqueue(Facts(a)));
            await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(pipeline.TryEnqueue(Facts(b)));
            await bFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(pipeline.Snapshot.Active >= 1);
        }
        finally
        {
            releaseA.TrySetResult();
        }

        await WaitForBaseline(pipeline);
    }

    [Fact]
    public async Task QueueExhaustionIsBoundedAndReturnsToBaseline()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipeline = new WorkerArtifactPipeline(1, 1, async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return new(null, null, 0);
        });

        try
        {
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            Assert.False(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            Assert.InRange(pipeline.Snapshot.Active, 1, 2);
        }
        finally
        {
            release.TrySetResult();
        }

        await WaitForBaseline(pipeline);
        Assert.Equal(PersistenceHealth.Unavailable, pipeline.Snapshot.Health);
        Assert.Equal(1, pipeline.Snapshot.Failures);
    }

    [Fact]
    public async Task ReservedBurstFitsQueueBeforeConsumersMakeSpace()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipeline = new WorkerArtifactPipeline(1, 3, async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return new(null, null, 0);
        });

        try
        {
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            // The worker is held by a deterministic gate and cannot make
            // space. The reservation cap equals the channel capacity, so the
            // remaining reserved jobs are accepted without relying on a
            // consumer dequeue race.
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            Assert.Equal(3, pipeline.Snapshot.Active);
        }
        finally
        {
            release.TrySetResult();
        }

        await WaitForBaseline(pipeline);
    }

    [Fact]
    public void ArtifactReservationCapacityCannotExceedQueueCapacity()
    {
        WorkerOptions defaults = new();
        defaults.Validate();
        Assert.Equal(defaults.ArtifactQueueCapacity, defaults.ArtifactReservationCapacity);

        new WorkerOptions
        {
            ArtifactConcurrency = 2,
            ArtifactQueueCapacity = 2,
            ArtifactReservationCapacity = 2
        }.Validate();

        Assert.Throws<ArgumentException>(() => new WorkerOptions
        {
            ArtifactConcurrency = 2,
            ArtifactQueueCapacity = 2,
            ArtifactReservationCapacity = 3
        }.Validate());
    }

    [Fact]
    public async Task ArtifactOperationFailureIsContainedAndNeverBecomesGameplayFailure()
    {
        var result = new TaskCompletionSource<WorkerArtifactResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipeline = new WorkerArtifactPipeline(1, 2,
            (_, _) => throw new IOException("test persistence failure"),
            (_, value) => result.TrySetResult(value));

        Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()), reportExpected: true)));
        WorkerArtifactResult outcome = await result.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ArtifactFailureCode.PersistenceFailed, outcome.ReportFailure);
        Assert.Null(outcome.ReportReady);
        Assert.Equal(PersistenceHealth.Unavailable, pipeline.Snapshot.Health);
    }

    [Fact]
    public async Task ReportFailurePublishesExactlyOneUnavailableDisposition()
    {
        var published = new TaskCompletionSource<(MatchReportReady? Ready, ArtifactFailureCode? Failure)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int publications = 0;
        await using var pipeline = new WorkerArtifactPipeline(1, 1,
            (_, _) => throw new InvalidDataException("malformed artifact"),
            reportDisposition: (_, ready, failure) =>
            {
                Interlocked.Increment(ref publications);
                published.TrySetResult((ready, failure));
            });
        WorkerArtifactFacts facts = Facts(new(Guid.NewGuid()), reportExpected: true) with
        {
            ReportDisposition = new WorkerArtifactDisposition()
        };

        Assert.True(pipeline.TryEnqueue(facts));
        (MatchReportReady? ready, ArtifactFailureCode? failure) =
            await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(ready);
        Assert.Equal(ArtifactFailureCode.PersistenceFailed, failure);
        await WaitForBaseline(pipeline);
        Assert.Equal(1, Volatile.Read(ref publications));
    }

    [Fact]
    public async Task ReadyDispositionIsNotRewrittenByLaterArtifactFailure()
    {
        var published = new TaskCompletionSource<(MatchReportReady? Ready, ArtifactFailureCode? Failure)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int publications = 0;
        MatchId matchId = new(Guid.NewGuid());
        await using var pipeline = new WorkerArtifactPipeline(1, 1,
            (_, _) => Task.FromResult(new WorkerArtifactResult(
                new MatchReportReady(matchId, matchId.Value, new WorkerId(Guid.NewGuid()),
                    Guid.NewGuid(), new string('A', 64), 100), null, 1)),
            reportDisposition: (_, ready, failure) =>
            {
                Interlocked.Increment(ref publications);
                published.TrySetResult((ready, failure));
            });
        WorkerArtifactFacts facts = Facts(matchId, reportExpected: true) with
        {
            ReportDisposition = new WorkerArtifactDisposition()
        };

        Assert.True(pipeline.TryEnqueue(facts));
        (MatchReportReady? ready, ArtifactFailureCode? failure) =
            await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(ready);
        Assert.Null(failure);
        await WaitForBaseline(pipeline);
        Assert.Equal(1, Volatile.Read(ref publications));
    }

    [Fact]
    public async Task ShutdownWaitsForOwnedArtifactOperationInsteadOfAbandoningIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new WorkerArtifactPipeline(1, 1, async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return new WorkerArtifactResult(null, null, 0);
        });
        try
        {
            Assert.True(pipeline.TryEnqueue(Facts(new(Guid.NewGuid()))));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task shutdown = pipeline.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            release.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult();
            await pipeline.DisposeAsync();
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RuntimeUnexpectedArtifactFailurePublishesOneUnavailableEvent()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        string root = Path.Combine(Path.GetTempPath(), "worker-artifact-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new WorkerOptions
            {
                ArtifactDirectory = root,
                ArtifactOperations = new(WriteReport: (_, _) =>
                    throw new InvalidOperationException("injected report callback failure"))
            };
            var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
                new RoutedMatchDatagramRouter(), options.MaxMatches);
            await using var runtime = new WorkerRuntime(options, content.Content, hub);
            MatchId matchId = new(Guid.NewGuid());
            int unavailableEvents = 0;
            var unavailable = new TaskCompletionSource<MatchReportUnavailable>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Event += value =>
            {
                if (value is MatchReportUnavailable notice && notice.MatchId == matchId)
                {
                    Interlocked.Increment(ref unavailableEvents);
                    unavailable.TrySetResult(notice);
                }
            };
            WorkerArtifactFacts facts = Facts(matchId, reportExpected: true) with
            {
                Completion = new MatchCompletion(matchId.Value, 1, 0, null,
                    new MatchReportV1(MatchReportV1.CurrentSchema, matchId.Value, 1,
                        Guid.NewGuid(), Guid.NewGuid(), "test-build", NetHeader.Version,
                        null, MatchTrustClass.Community, "test", null,
                        new MatchRules(MatchMode.Battle, "test", 1),
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0,
                        MatchEndReason.TimeLimit, ImmutableArray<MatchReportParticipant>.Empty),
                    null, null),
                ReportDisposition = new WorkerArtifactDisposition()
            };

            Assert.True(runtime.TryEnqueueArtifactForTesting(facts));
            MatchReportUnavailable result = await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(ArtifactFailureCode.PersistenceFailed, result.FailureCode);
            await runtime.DisposeAsync();
            Assert.Equal(1, Volatile.Read(ref unavailableEvents));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RuntimeReservedBurstFitsQueueBeforeConsumersMakeSpace()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        string root = Path.Combine(Path.GetTempPath(), "worker-artifact-burst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkerRuntime? runtime = null;
        try
        {
            WorkerId workerId = new(Guid.NewGuid());
            Guid incarnation = Guid.NewGuid();
            var options = new WorkerOptions
            {
                WorkerId = workerId,
                Incarnation = incarnation,
                ArtifactDirectory = root,
                ArtifactConcurrency = 1,
                ArtifactQueueCapacity = 3,
                ArtifactReservationCapacity = 3,
                ArtifactOperations = new(WriteReport: async (facts, _) =>
                {
                    writerStarted.TrySetResult();
                    await releaseWriter.Task;
                    return new MatchReportReady(facts.Spec.MatchId, facts.ReportId,
                        workerId, incarnation, new string('C', 64), 128);
                })
            };
            var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
                new RoutedMatchDatagramRouter(), options.MaxMatches);
            runtime = new WorkerRuntime(options, content.Content, hub);

            Assert.True(runtime.TryEnqueueArtifactForTesting(
                ReportFacts(new(Guid.NewGuid())), reserve: true));
            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(runtime.TryEnqueueArtifactForTesting(
                ReportFacts(new(Guid.NewGuid())), reserve: true));
            Assert.True(runtime.TryEnqueueArtifactForTesting(
                ReportFacts(new(Guid.NewGuid())), reserve: true));
            // No consumer has returned a job while the writer gate is held.
            // The reservation cap equals the queue capacity, so all three
            // reserved jobs are admitted and the next reservation is fenced
            // before it can create an unenqueueable job.
            Assert.Equal(3, runtime.ArtifactReservationsForTesting);
            Assert.Equal(3, runtime.ArtifactDiagnostics.Active);
            Assert.False(runtime.TryEnqueueArtifactForTesting(
                ReportFacts(new(Guid.NewGuid())), reserve: true));

            releaseWriter.TrySetResult();
            await WaitForRuntimeBaseline(runtime);
            Assert.Equal(0, runtime.ArtifactReservationsForTesting);
            Assert.Equal(0, runtime.ArtifactDiagnostics.Active);
        }
        finally
        {
            releaseWriter.TrySetResult();
            if (runtime is not null)
            {
                try { await runtime.DisposeAsync(); } catch { }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RuntimeReportDeadlinePublishesImmediatelyAndRetainsOwnershipUntilWriterReturns()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        string root = Path.Combine(Path.GetTempPath(), "worker-artifact-deadline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource<MatchReportReady?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new System.Collections.Concurrent.ConcurrentQueue<string>();
        WorkerRuntime? runtime = null;
        try
        {
            var options = new WorkerOptions
            {
                ArtifactDirectory = root,
                ArtifactReportTimeout = TimeSpan.FromMilliseconds(25),
                ArtifactShutdownTimeout = TimeSpan.FromMilliseconds(100),
                ArtifactOperations = new(
                    WriteReport: (_, _) =>
                    {
                        writerStarted.TrySetResult();
                        return releaseWriter.Task;
                    },
                    Lifecycle: lifecycle.Enqueue)
            };
            var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
                new RoutedMatchDatagramRouter(), options.MaxMatches);
            runtime = new WorkerRuntime(options, content.Content, hub);
            MatchId matchId = new(Guid.NewGuid());
            int unavailableEvents = 0;
            var unavailable = new TaskCompletionSource<MatchReportUnavailable>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Event += value =>
            {
                if (value is MatchReportUnavailable notice && notice.MatchId == matchId)
                {
                    Interlocked.Increment(ref unavailableEvents);
                    unavailable.TrySetResult(notice);
                }
            };
            WorkerArtifactFacts facts = ReportFacts(matchId);

            Assert.True(runtime.TryEnqueueArtifactForTesting(facts, reserve: true));
            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            MatchReportUnavailable notice = await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(ArtifactFailureCode.DeadlineExceeded, notice.FailureCode);
            Assert.Equal(1, Volatile.Read(ref unavailableEvents));
            Assert.Equal(PersistenceHealth.Unavailable, runtime.ArtifactDiagnostics.Health);
            Assert.Equal(1, runtime.ArtifactDiagnostics.Active);
            Assert.Equal(1, runtime.ArtifactReservationsForTesting);
            Assert.Equal(WorkerStatus.Ready, runtime.StatusForTesting);

            ValueTask firstDispose = runtime.DisposeAsync();
            await Assert.ThrowsAsync<TimeoutException>(() => firstDispose.AsTask());
            Assert.Equal(WorkerStatus.Draining, runtime.StatusForTesting);
            Assert.Equal(1, runtime.ArtifactDiagnostics.Active);
            Assert.Equal(1, runtime.ArtifactReservationsForTesting);
            Assert.Equal(1, Volatile.Read(ref unavailableEvents));

            // The report writer is deliberately late. Its successful result
            // must not replace the already-published Unavailable disposition.
            releaseWriter.TrySetResult(new MatchReportReady(matchId, matchId.Value,
                options.WorkerId, options.Incarnation, new string('B', 64), 128));
            await runtime.DisposeAsync();
            Assert.Equal(WorkerStatus.Stopped, runtime.StatusForTesting);
            Assert.Equal(0, runtime.ArtifactDiagnostics.Active);
            Assert.Equal(0, runtime.ArtifactReservationsForTesting);
            Assert.Equal(1, Volatile.Read(ref unavailableEvents));
            Assert.Equal(new[] { "simulation-stopped", "artifacts-drained" }, lifecycle.ToArray());
        }
        finally
        {
            releaseWriter.TrySetResult(null);
            if (runtime is not null)
            {
                try { await runtime.DisposeAsync(); }
                catch (TimeoutException)
                {
                    releaseWriter.TrySetResult(null);
                    try { await runtime.DisposeAsync(); } catch { }
                }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RuntimeDisposalStopsSimulationBeforeArtifactPipeline()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        var lifecycle = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var options = new WorkerOptions
        {
            ArtifactOperations = new(Lifecycle: lifecycle.Enqueue)
        };
        var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
            new RoutedMatchDatagramRouter(), options.MaxMatches);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);

        await runtime.DisposeAsync();

        // This is the narrow stop-order seam. A real active-match shutdown
        // requires the content-backed admission harness and remains covered
        // by the separate Worker process tests.
        Assert.Equal(new[] { "simulation-stopped", "artifacts-drained" }, lifecycle.ToArray());
        Assert.Equal(WorkerStatus.Stopped, runtime.StatusForTesting);
    }

    private static async Task WaitForBaseline(WorkerArtifactPipeline pipeline)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (pipeline.Snapshot.Active != 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static async Task WaitForRuntimeBaseline(WorkerRuntime runtime)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (runtime.ArtifactDiagnostics.Active != 0
            || runtime.ArtifactReservationsForTesting != 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static WorkerArtifactFacts Facts(MatchId matchId, bool reportExpected = false)
    {
        var node = new NodeId(Guid.NewGuid());
        MatchRules rules = new(MatchMode.Battle, "test", 1);
        var spec = new MatchSpec(matchId, new(Guid.NewGuid()), node, Guid.NewGuid(), rules,
            new("test", "hash", "build", "build", NetHeader.Version), MatchTrustClass.Community,
            null, null, ImmutableArray.Create(new RosterSeat(0, null, null, "Bot",
                Hunter.Samus, 0, SeatRole.Bot, false)), SharedBotFillPolicy.Disabled,
            ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        return new(spec, 1, null, null, null, reportExpected, matchId.Value);
    }

    private static WorkerArtifactFacts ReportFacts(MatchId matchId)
        => Facts(matchId, reportExpected: true) with
        {
            Completion = new MatchCompletion(matchId.Value, 1, 0, null,
                new MatchReportV1(MatchReportV1.CurrentSchema, matchId.Value, 1,
                    Guid.NewGuid(), Guid.NewGuid(), "test-build", NetHeader.Version,
                    null, MatchTrustClass.Community, "test", null,
                    new MatchRules(MatchMode.Battle, "test", 1),
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0,
                    MatchEndReason.TimeLimit, ImmutableArray<MatchReportParticipant>.Empty),
                null, null),
            ReportDisposition = new WorkerArtifactDisposition()
        };

    private sealed class SilentTransport : INetTransport
    {
        public int LocalPort => 27666;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();
        public void Dispose() { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public IEnumerable<ReceivedPacket> Drain() => Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
    }
}
