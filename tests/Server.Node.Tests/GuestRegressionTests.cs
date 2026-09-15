using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text.Json;
using ProjectPrime.Server.Node.Reporting;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker.Reporting;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using MphRead.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Collection(WorkerProcessCollection.Name)]
public sealed class GuestRegressionTests
{
    [Fact]
    public async Task GuestSessionConsumesCapacityThroughResumeGraceThenReleasesAfterPruning()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var host = new NodeHostFixture(clock, builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Node:MaximumSessions"] = "1"
            }));
        await host.App.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Uri uri = ControlUri(host);
        var manager = host.App.Services.GetRequiredService<NodeSessionManager>();
        using var first = await ConnectAsync(uri, host.Ticket(Guid.NewGuid(), kind: "guest"), timeout.Token);
        Assert.Equal(1, manager.Count);

        await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "grace", timeout.Token);
        await UntilAsync(() => manager.CanResume(first.ResumeToken), timeout.Token);
        Assert.Equal(0, manager.Count);

        await AssertRejectedAsync(uri, host.Ticket(Guid.NewGuid(), kind: "guest"), timeout.Token);

        clock.Now += NodeSessionManager.DisconnectGrace + TimeSpan.FromSeconds(1);
        manager.PruneExpired();
        Assert.False(manager.CanResume(first.ResumeToken));

        using var second = await ConnectAsync(uri, host.Ticket(Guid.NewGuid(), kind: "guest"), timeout.Token);
        Assert.Equal(1, manager.Count);
        await host.App.StopAsync(timeout.Token);
    }

    [Fact]
    public void GuestObserverInFrozenRosterForcesPracticeAndSurvivesLaterMembershipChanges()
    {
        var lobbies = new ProjectPrime.Server.Node.Lobbies.LobbyManager();
        var owner = new ProjectPrime.Server.Node.Lobbies.LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        Guid guestId = Guid.NewGuid();
        var observer = new ProjectPrime.Server.Node.Lobbies.LobbyIdentity(Guid.NewGuid(), null, guestId, "GuestObserver");
        var snapshot = (ProjectPrime.Server.Shared.LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Guest observer", LobbyVisibility.Public, 1, 1));
        snapshot = (ProjectPrime.Server.Shared.LobbySnapshot)lobbies.Execute(observer,
            new LobbyJoin(snapshot.LobbyId, snapshot.Revision, Observer: true));
        snapshot = (ProjectPrime.Server.Shared.LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(snapshot.Revision, "unit", MatchMode.Battle));
        snapshot = (ProjectPrime.Server.Shared.LobbySnapshot)lobbies.Execute(owner,
            new LobbySetReady(true, snapshot.Revision));

        MatchSpec spec = lobbies.PrepareMatch(owner.SessionId, snapshot.Revision,
            new("unit", "hash", "test", "client", NetHeader.Version), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(MatchTrustClass.Practice, spec.TrustClass);
        RosterSeat guestSeat = Assert.Single(spec.Roster.Where(seat => seat.Role == SeatRole.Observer));
        Assert.Equal(guestId, guestSeat.GuestSessionId);
        Assert.Null(guestSeat.PlayerId);

        lobbies.Disconnect(observer.SessionId);
        lobbies.Disconnect(owner.SessionId);
        Assert.Equal(0, lobbies.Count);
        Assert.Equal(MatchTrustClass.Practice, spec.TrustClass);
        Assert.Equal(guestId, Assert.Single(spec.Roster.Where(seat => seat.Role == SeatRole.Observer)).GuestSessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestMatchReportReadyIsValidatedDiscardedAndNeverEnqueued(bool configureIngestor)
    {
        string root = Temporary();
        MatchReportOutbox? outbox = null;
        NodeReportIngestor? ingestor = null;
        try
        {
            var artifact = GuestArtifact(root);
            var transport = new CountingTransport();
            if (configureIngestor)
            {
                outbox = new(new(Path.Combine(root, "outbox")), transport);
                ingestor = new(outbox, Path.Combine(root, "receipts"));
                using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await UntilAsync(() => ingestor.CanAcceptOfficial, readyTimeout.Token);
            }

            bool Discard(MatchReportReady ready)
                => ingestor?.TryDiscard(artifact.Spec, artifact.Worker, artifact.Incarnation, 1, root, ready)
                    ?? NodeReportIngestor.TryDiscardArtifact(artifact.Spec, artifact.Worker, artifact.Incarnation, 1, root, ready);

            Assert.False(Discard(artifact.Ready with { PayloadHash = new string('B', 64) }));
            Assert.True(File.Exists(artifact.Path));
            Assert.True(Discard(artifact.Ready));
            Assert.False(File.Exists(artifact.Path));
            if (outbox is not null)
            {
                Assert.Equal(0, transport.Calls);
                Assert.Equal(0, outbox.Status.DurablePending);
                Assert.Equal(0, outbox.Status.QueuedPending);
            }
        }
        finally
        {
            if (ingestor is not null) await ingestor.DisposeAsync();
            if (outbox is not null) await outbox.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestArtifactCleanupPrecedesForgetAndDrainWithoutBackendSubmission(bool invalidArtifact)
    {
        string root = Temporary();
        try
        {
            var artifact = GuestArtifact(root, harnessContent: true);
            if (invalidArtifact) File.WriteAllText(artifact.Path, "{}");
            var manager = new WorkerManager(artifact.Spec.NodeId, artifact.Spec.NodeIncarnation);
            var transport = new CountingTransport();
            await using var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), transport);
            await using var ingestor = new NodeReportIngestor(outbox, Path.Combine(root, "receipts"));
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await UntilAsync(() => ingestor.CanAcceptOfficial, readyTimeout.Token);
            await using var scheduler = new WorkerScheduler(manager, reports: ingestor);
            await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("guest-report") with
            {
                Content = new("1", "hash", "test", 8), ArtifactDirectory = root
            });
            var completed = new TaskCompletionSource<(bool Forgotten, bool Drained)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (id, _) =>
            {
                // Both operations execute on the event consumer before it can
                // process the following report-ready, with no timing race.
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                bool retained = scheduler.TryGetAssignment(id, out WorkerMatchAssignment? _);
                completed.TrySetResult((retained, scheduler.WaitForDrainAsync(cancelled.Token).IsCompletedSuccessfully));
            };
            scheduler.ReportReady += (_, _) => handled.TrySetResult(!scheduler.TryGetAssignment(artifact.Spec.MatchId, out _));
            await scheduler.PlaceAsync(artifact.Spec).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((true, false), await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(!invalidArtifact,
                await handled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (invalidArtifact)
                await Assert.ThrowsAsync<IOException>(() =>
                    scheduler.WaitForDrainAsync(drainTimeout.Token));
            else
                await scheduler.WaitForDrainAsync(drainTimeout.Token);
            Assert.Equal(invalidArtifact, File.Exists(artifact.Path));
            Assert.Equal(0, transport.Calls);
            Assert.Equal(0, outbox.Status.DurablePending);
            Assert.Equal(0, outbox.Status.QueuedPending);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task GuestCompletionDrainAndForgetMatchDoNotWaitForReportSubmission()
    {
        string root = Temporary();
        var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        var transport = new BlockingTransport();
        try
        {
            await using var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), transport);
            await using var ingestor = new NodeReportIngestor(outbox, Path.Combine(root, "receipts"));
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await UntilAsync(() => ingestor.CanAcceptOfficial, readyTimeout.Token);
            await using var scheduler = new WorkerScheduler(manager, reports: ingestor);
            await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("completed") with
            {
                Content = new("1", "hash", "test", 8),
                // The harness does not produce a report artifact; placement only
                // needs to exercise the guest completion/report expectation gate.
            });
            MatchSpec spec = GuestSpec(manager);
            var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Ended += (matchId, interrupted) =>
            {
                if (matchId == spec.MatchId) ended.TrySetResult(interrupted);
            };

            await scheduler.PlaceAsync(spec).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            scheduler.Drain("guest report suppression");
            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await scheduler.WaitForDrainAsync(drainTimeout.Token);
            Assert.False(scheduler.TryGetAssignment(spec.MatchId, out _));
            Assert.Equal(0, transport.Calls);
            Assert.Equal(0, outbox.Status.DurablePending);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MatchSpec GuestSpec(WorkerManager manager)
    {
        MatchSpec original = WorkerManagerTests.Spec(manager);
        return original with
        {
            TrustClass = MatchTrustClass.Practice,
            Roster = ImmutableArray.Create(original.Roster[0] with
            {
                PlayerId = null,
                GuestSessionId = Guid.NewGuid(),
                DisplayName = "Guest"
            })
        };
    }

    private static (MatchSpec Spec, WorkerId Worker, Guid Incarnation, MatchReportReady Ready, string Path) GuestArtifact(string root, bool harnessContent = false)
    {
        (MatchSpec original, MatchReportV1 report) = NodeReportIngestionTests.Capture();
        MatchSpec spec = original with
        {
            TrustClass = MatchTrustClass.Practice,
            Roster = ImmutableArray.Create(original.Roster[0] with
            {
                PlayerId = null,
                GuestSessionId = Guid.NewGuid(),
                DisplayName = "Guest",
                Role = SeatRole.Player
            })
        };
        if (harnessContent) spec = spec with { Content = new(spec.Content.MapKey, "hash", "1", "test", 8) };
        MatchReportV1 guestReport = report with
        {
            ContentHash = spec.Content.ContentHash,
            BuildVersion = spec.Content.BuildVersion,
            ProtocolVersion = spec.Content.ProtocolVersion,
            TrustClass = spec.TrustClass,
            Participants = report.Participants.Select(participant => participant with
            {
                PlayerId = null,
                Kind = ParticipantKind.Guest,
                DisplayName = "Guest"
            }).ToImmutableArray()
        };
        WorkerId worker = new(Guid.NewGuid());
        Guid incarnation = Guid.NewGuid();
        MatchReportReady ready = WorkerReportArtifactWriter.Write(root, worker, incarnation, spec, 1, guestReport);
        string path = Path.Combine(root, "reports", ready.ReportId.ToString("N") + ".json");
        return (spec, worker, incarnation, ready, path);
    }

    private static Uri ControlUri(NodeHostFixture host)
        => new(host.App.Urls.Single().Replace("https:", "wss:", StringComparison.Ordinal) + "/v1/control");

    private static async Task<TrackedClientWebSocket> ConnectAsync(Uri uri, string authorization, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.SetRequestHeader("Authorization", "Bearer " + authorization);
        try
        {
            await socket.ConnectAsync(uri, cancellationToken);
            using JsonDocument welcome = await ReadAsync(socket, cancellationToken);
            Assert.Equal("node.session", welcome.RootElement.GetProperty("type").GetString());
            string token = welcome.RootElement.GetProperty("payload").GetProperty("resumeToken").GetString()!;
            return new TrackedClientWebSocket(socket, token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task AssertRejectedAsync(Uri uri, string authorization, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.SetRequestHeader("Authorization", "Bearer " + authorization);
        try
        {
            await socket.ConnectAsync(uri, cancellationToken);
            await ReadAsync(socket, cancellationToken);
            Assert.Fail("The second guest session was admitted while the first was in resume grace.");
        }
        catch (WebSocketException) { }
        catch (JsonException) { }
    }

    private static async Task<JsonDocument> ReadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[32768];
        int count = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), cancellationToken);
            count += result.Count;
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.AsMemory(0, count));
    }

    private static async Task UntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate()) await Task.Delay(10, cancellationToken);
    }

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "guest-node-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TrackedClientWebSocket(ClientWebSocket socket, string resumeToken) : IDisposable
    {
        public string ResumeToken { get; } = resumeToken;
        public Task CloseOutputAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
            => socket.CloseOutputAsync(status, description, cancellationToken);
        public void Dispose() => socket.Dispose();
    }

    private sealed class CountingTransport : IMatchReportTransport
    {
        public int Calls;
        public Task<ReportDelivery> SubmitAsync(Guid id, string hash, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new ReportDelivery(ReportDeliveryKind.Accepted));
        }
    }

    private sealed class BlockingTransport : IMatchReportTransport
    {
        public int Calls;
        public async Task<ReportDelivery> SubmitAsync(Guid id, string hash, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ReportDelivery(ReportDeliveryKind.Retry);
        }
    }
}
