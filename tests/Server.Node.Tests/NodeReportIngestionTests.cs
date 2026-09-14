using System.Collections.Immutable;
using System.Net;
using ProjectPrime.Server.Node.Reporting;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker.Reporting;
using MphRead;
using MphRead.Entities;
using MphRead.Identity;
using MphRead.Mods.Network;
using MphRead.Reporting;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeReportIngestionTests
{
    [Fact]
    public async Task ActualLedgerArtifactSurvivesBackendOutageAndNodeOutboxRestart()
    {
        string root = Temporary();
        try
        {
            var (spec, report) = Capture(); var worker = new WorkerId(Guid.NewGuid()); Guid incarnation = Guid.NewGuid();
            var artifact = WorkerReportArtifactWriter.Write(root, worker, incarnation, spec, 1, report);
            var blocked = new Transport { Block = true };
            await using (var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), blocked))
            await using (var ingest = new NodeReportIngestor(outbox, Path.Combine(root, "receipts")))
            {
                await Until(() => ingest.CanAcceptOfficial); Assert.True(ingest.TryReserve(spec.MatchId));
                Assert.True(ingest.TryQueue(spec, worker, incarnation, 1, root, artifact));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await ingest.WaitForDurabilityAsync(timeout.Token);
                Assert.Equal(1, outbox.Status.DurablePending); Assert.Equal(1, ingest.Ingested);
                Assert.True(ingest.TryQueue(spec, worker, incarnation, 1, root, artifact)); await Until(() => ingest.Ingested == 2);
                Assert.Equal(1, outbox.Status.DurablePending);
            }
            var accepted = new Transport();
            await using var recovered = new MatchReportOutbox(new(Path.Combine(root, "outbox")), accepted);
            await using var restarted = new NodeReportIngestor(recovered, Path.Combine(root, "receipts"));
            await Until(() => accepted.Calls == 1 && recovered.Status.DurablePending == 0 && restarted.CanAcceptOfficial);
            Assert.True(restarted.TryQueue(spec, worker, incarnation, 1, root, artifact)); await Until(() => restarted.Ingested == 1);
            Assert.Equal(1, accepted.Calls); Assert.Equal(spec.MatchId.Value, accepted.Id);
            Assert.Equal(7, accepted.Report!.Participants[0].Metrics.Kills);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WrongWorkerAndConflictingImmutableArtifactsFailClosed()
    {
        string root = Temporary();
        try
        {
            var (spec, report) = Capture(); var worker = new WorkerId(Guid.NewGuid()); Guid incarnation = Guid.NewGuid();
            var artifact = WorkerReportArtifactWriter.Write(root, worker, incarnation, spec, 1, report);
            Assert.Throws<IOException>(() => WorkerReportArtifactWriter.Write(root, worker, incarnation, spec, 1, report with { PlayedTicks = 2 }));
            await using var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), new Transport());
            await using var ingest = new NodeReportIngestor(outbox, Path.Combine(root, "receipts"));
            await Until(() => ingest.CanAcceptOfficial);
            Assert.True(ingest.TryQueue(spec, worker, Guid.NewGuid(), 1, root, artifact)); await Until(() => ingest.LastError != null);
            Assert.False(ingest.CanAcceptOfficial); Assert.Equal(0, ingest.Ingested); Assert.Equal(0, outbox.Status.DurablePending);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ValidatedPendingArtifactRecoversWhenOutboxStopsBeforeDurableEnqueue()
    {
        string root = Temporary();
        try
        {
            var (spec, report) = Capture(); var worker = new WorkerId(Guid.NewGuid()); Guid incarnation = Guid.NewGuid();
            var artifact = WorkerReportArtifactWriter.Write(root, worker, incarnation, spec, 1, report);
            var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), new Transport());
            await using (var ingest = new NodeReportIngestor(outbox, Path.Combine(root, "receipts")))
            {
                await Until(() => ingest.CanAcceptOfficial); await outbox.DisposeAsync();
                Assert.True(ingest.TryQueue(spec, worker, incarnation, 1, root, artifact)); await Until(() => ingest.LastError != null);
                Assert.False(ingest.CanAcceptOfficial);
            }
            var accepted = new Transport();
            await using var recovered = new MatchReportOutbox(new(Path.Combine(root, "outbox")), accepted);
            await using var restarted = new NodeReportIngestor(recovered, Path.Combine(root, "receipts"));
            await Until(() => restarted.CanAcceptOfficial && accepted.Calls == 1);
            var conflict = artifact with { PayloadHash = new string('B', 64) };
            Assert.True(restarted.TryQueue(spec, worker, incarnation, 1, root, conflict)); await Until(() => restarted.LastError != null);
            Assert.Equal(1, accepted.Calls); Assert.False(restarted.CanAcceptOfficial);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnavailableReceiptOwnsReservationUntilDurabilityAndIsIdempotent()
    {
        string root = Temporary();
        try
        {
            var (spec, _) = Capture();
            var worker = new WorkerId(Guid.NewGuid());
            Guid incarnation = Guid.NewGuid();
            var unavailable = new MatchReportUnavailable(spec.MatchId, spec.MatchId.Value,
                worker, incarnation, ArtifactFailureCode.WorkerLost);
            await using var outbox = new MatchReportOutbox(new(
                Path.Combine(root, "outbox"), MaximumReports: 1), new Transport { Block = true });
            await using var ingest = new NodeReportIngestor(outbox,
                Path.Combine(root, "receipts"), queueCapacity: 1);

            await Until(() => ingest.CanAcceptOfficial);
            Assert.True(ingest.TryReserve(spec.MatchId));
            Assert.Equal(1, outbox.Status.ReservedReports);
            Assert.True(ingest.TryQueueUnavailable(spec, unavailable,
                out Task durability));

            await durability.WaitAsync(TimeSpan.FromSeconds(5));
            // This is the same durability edge consumed by worker-soak for an
            // observed MatchReportUnavailable event.  It must not complete
            // until the scheduler-owned reservation release follows the
            // durable receipt.
            using var waitCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task observedDurability = ingest.WaitForDurabilityAsync(waitCancellation.Token);
            Assert.False(observedDurability.IsCompleted);
            // The ingestor only establishes the durable failure receipt. The
            // scheduler owns the subsequent CancelReservation edge.
            Assert.Equal(1, outbox.Status.ReservedReports);
            ingest.CancelReservation(spec.MatchId);
            await observedDurability;
            Assert.Equal(0, outbox.Status.ReservedReports);

            Assert.True(ingest.TryQueueUnavailable(spec, unavailable,
                out Task duplicateDurability));
            await duplicateDurability.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "receipts", "unavailable"), "*.json"));

            MatchReportUnavailable conflict = unavailable with
            {
                FailureCode = ArtifactFailureCode.QueueExhausted
            };
            Assert.True(ingest.TryQueueUnavailable(spec, conflict,
                out Task conflictDurability));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                conflictDurability.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "receipts", "unavailable"), "*.json"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("trust")]
    [InlineData("report-id")]
    [InlineData("size")]
    [InlineData("directory-link")]
    [InlineData("parent-link")]
    [InlineData("file-link")]
    public async Task ArtifactAttacksCannotReachOutbox(string attack)
    {
        string root = Temporary();
        try
        {
            string artifactRoot = Path.Combine(root, "worker"); Directory.CreateDirectory(artifactRoot);
            var (spec, report) = Capture(); var worker = new WorkerId(Guid.NewGuid()); Guid incarnation = Guid.NewGuid();
            var ready = WorkerReportArtifactWriter.Write(artifactRoot, worker, incarnation, spec, 1, report);
            string path = Path.Combine(artifactRoot, "reports", ready.ReportId.ToString("N") + ".json");
            if (attack == "trust")
            {
                var original = System.Text.Json.JsonSerializer.Deserialize<MatchReportV1>(File.ReadAllBytes(path))!;
                byte[] altered = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original with { TrustClass = MatchTrustClass.Ranked });
                File.WriteAllBytes(path, altered);
                ready = ready with { PayloadBytes = altered.Length, PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(altered)) };
            }
            if (attack == "report-id") ready = ready with { ReportId = Guid.NewGuid() };
            if (attack == "size") ready = ready with { PayloadBytes = MatchReportReady.MaximumPayloadBytes + 1 };
            if (attack == "directory-link")
            { string actual = Path.Combine(artifactRoot, "real-reports"); Directory.Move(Path.GetDirectoryName(path)!, actual); Directory.CreateSymbolicLink(Path.GetDirectoryName(path)!, actual); }
            if (attack == "parent-link")
            { string actual = Path.Combine(root, "actual-worker"); Directory.Move(artifactRoot, actual); Directory.CreateSymbolicLink(artifactRoot, actual); }
            if (attack == "file-link")
            { string actual = path + ".actual"; File.Move(path, actual); File.CreateSymbolicLink(path, actual); }
            var transport = new Transport();
            await using var outbox = new MatchReportOutbox(new(Path.Combine(root, "outbox")), transport);
            await using var ingest = new NodeReportIngestor(outbox, Path.Combine(root, "receipts"));
            await Until(() => ingest.CanAcceptOfficial);
            Assert.True(ingest.TryQueue(spec, worker, incarnation, 1, artifactRoot, ready)); await Until(() => ingest.LastError != null);
            Assert.Equal(0, transport.Calls); Assert.False(ingest.CanAcceptOfficial); Assert.Equal(0, outbox.Status.DurablePending);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MixedBotGuestObserverLedgerUsesEffectiveFreeForAllTeamsAtCompletion()
    {
        string root = Temporary();
        try
        {
            var (baseline, _) = Capture();
            Guid guest = Guid.NewGuid();
            var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 4);
            var spec = baseline with { Rules = rules, Roster = ImmutableArray.Create(
                new RosterSeat(0, null, null, "BotZero", Hunter.Spire, 0, SeatRole.Bot, false),
                new RosterSeat(3, null, null, "BotThree", Hunter.Samus, 0, SeatRole.Bot, false),
                new RosterSeat(2, null, guest, "GuestTwo", Hunter.Samus, 0, SeatRole.Player, false),
                new RosterSeat(8, null, Guid.NewGuid(), "Observer", Hunter.Samus, 0, SeatRole.Observer, false)),
                ObserverPolicy = ObserverPolicy.Allowed };
            var scene = new Scene(headless: true); scene.Match.ApplyRules(rules); scene.Match.MatchId = 1;
            scene.Players.ActiveCount = 3;
            foreach (int slot in new[] { 0, 2, 3 }) { scene.Players[slot].Health = 100; scene.Players[slot].LoadFlags = LoadFlags.Active; scene.Players[slot].TeamIndex = slot; }
            var network = new ServerNetwork(new SilentTransport(), rules);
            var connection = new NetConnection(99, new IPEndPoint(IPAddress.Loopback, 12345), 1, 0);
            connection.Ready(1); connection.StartPlaying();
            var join = new JoinPacket(NetHeader.Version, 99, Hunter.Samus, "GuestTwo");
            var peer = (ServerPeer)typeof(ServerPeer).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single()
                .Invoke(new object[] { connection, join, (byte)2, 0d });
            typeof(ServerPeer).GetProperty(nameof(ServerPeer.TeamIndex))!.SetValue(peer, (byte)2);
            typeof(ServerPeer).GetProperty(nameof(ServerPeer.GuestSessionId))!.SetValue(peer, guest);
            ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(network)!)[2] = peer;
            var ledger = new MatchParticipantLedger(spec.NodeId.Value, spec.NodeIncarnation, spec.Content.BuildVersion,
                () => DateTimeOffset.UnixEpoch, spec.MatchId.Value);
            ledger.BeginPlaying(scene, network, 0);
            ledger.ActivateBot(scene, new BotParticipant(0, 100, Hunter.Spire, 0, "BotZero"), 0);
            ledger.ActivateBot(scene, new BotParticipant(3, 101, Hunter.Samus, 3, "BotThree"), 0);
            ledger.RecordPlayedStep(0); scene.Match.Players[2].Kills = 7;
            typeof(MatchRuntime).GetMethod("CaptureResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(scene.Match, new object[] { 1u });
            MatchReportV1 snapshot = ledger.Complete(scene, 1)!;
            var ready = WorkerReportArtifactWriter.Write(root, new WorkerId(Guid.NewGuid()), Guid.NewGuid(), spec, 1, snapshot);
            var report = System.Text.Json.JsonSerializer.Deserialize<MatchReportV1>(File.ReadAllBytes(Path.Combine(root, "reports", ready.ReportId.ToString("N") + ".json")))!;
            Assert.Equal(3, report.Participants.Length);
            Assert.DoesNotContain(report.Participants, participant => participant.DisplayName == "Observer");
            Assert.Contains(report.Participants, participant => participant.Kind == ParticipantKind.Guest && participant.Spans[0].Slot == 2 && participant.Spans[0].TeamIndex == 2);
            Assert.Contains(report.Participants, participant => participant.Kind == ParticipantKind.Bot && participant.Spans[0].Slot == 3 && participant.Spans[0].TeamIndex == 3);
            MatchReportBinding.Validate(spec, 1, report);
            foreach (int slot in new[] { 2, 3 })
            {
                var corrupted = report with { Participants = report.Participants.Select(participant => participant.Spans[0].Slot != slot ? participant
                    : participant with { Spans = ImmutableArray.Create(participant.Spans[0] with { TeamIndex = 0 }) }).ToImmutableArray() };
                Assert.Throws<ArgumentException>(() => MatchReportBinding.Validate(spec, 1, corrupted));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LeavingAfterTheCurrentTickWasPlayedKeepsItsExclusiveEndEvidence()
    {
        var (spec, report) = Capture(departAfterPlayedTick: true);
        Assert.True(report.IsValid);
        var participant = Assert.Single(report.Participants);
        var span = Assert.Single(participant.Spans);
        Assert.Equal(1u, span.PlayedTicks);
        Assert.Equal(1u, span.LeftTick);
        Assert.Equal(ParticipantExitReason.ExplicitLeave, span.ExitReason);
        Assert.Equal(ParticipantOutcome.Departed, participant.Outcome);
        var oldEnd = report with { Participants = ImmutableArray.Create(participant with
            { Spans = ImmutableArray.Create(span with { LeftTick = 0 }) }) };
        Assert.False(oldEnd.IsValid);
        Assert.Contains("played ticks exceed exclusive duration",
            Assert.Throws<ArgumentException>(() => MatchReportBinding.Validate(spec, 1, oldEnd)).Message);
    }

    internal static (MatchSpec, MatchReportV1) Capture(bool departAfterPlayedTick = false)
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        var spec = new MatchSpec(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(), rules,
            new(rules.RoomKey, "content-hash", "AMHE1", "test-build", NetHeader.Version), MatchTrustClass.Community, null, null,
            ImmutableArray.Create(new RosterSeat(0, null, null, "Bot", Hunter.Samus, 0, SeatRole.Bot, false)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled, 1, 2);
        var scene = new Scene(headless: true); scene.Match.ApplyRules(rules); scene.Match.MatchId = 1;
        scene.Players.ActiveCount = 1; scene.Players[0].Health = 100; scene.Players[0].LoadFlags = LoadFlags.Active;
        var network = new ServerNetwork(new SilentTransport(), rules);
        var ledger = new MatchParticipantLedger(spec.NodeId.Value, spec.NodeIncarnation, spec.Content.BuildVersion,
            () => DateTimeOffset.UnixEpoch, spec.MatchId.Value);
        ledger.BeginPlaying(scene, network, 0); ledger.ActivateBot(scene, new BotParticipant(0, 1, Hunter.Samus, 0, "Bot"), 0);
        ledger.RecordPlayedStep(0); scene.Match.Players[0].Kills = 7;
        if (departAfterPlayedTick) ledger.LeaveSlot(scene, 0, 0, ParticipantExitReason.ExplicitLeave); typeof(MatchRuntime).GetMethod("CaptureResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(scene.Match, new object[] { 1u });
        return (spec, ledger.Complete(scene, 1)!);
    }
    private static string Temporary() { string path = Path.Combine(Path.GetTempPath(), "node-report-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!predicate()) await Task.Delay(10, timeout.Token); }
    private sealed class Transport : IMatchReportTransport
    {
        public bool Block; public int Calls; public Guid Id; public MatchReportV1? Report;
        public async Task<ReportDelivery> SubmitAsync(Guid id, string hash, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            Id = id; Report = System.Text.Json.JsonSerializer.Deserialize<MatchReportV1>(payload.Span); Interlocked.Increment(ref Calls);
            return new(ReportDeliveryKind.Accepted);
        }
    }
    private sealed class SilentTransport : INetTransport
    {
        public int LocalPort => 0; public long PacketsDropped => 0; public int QueuedPackets => 0; public int HeldIncomingPackets => 0; public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new(); public void Dispose() { } public IEnumerable<ReceivedPacket> Drain() => Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> data) { } public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> data, long hold) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> data, long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> data = default) { } public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { } public void AnswerPingsImmediately() { }
    }
}
