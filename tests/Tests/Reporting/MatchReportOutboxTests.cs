using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Reporting;
using Xunit;
namespace MphRead.Tests.Reporting;

public sealed class MatchReportOutboxTests
{
    internal static MatchReportV1 Report(Guid? id = null)
    {
        var metrics = new MatchReportMetrics(0, 0, 1, 2, 3, 4, 100, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 0, 0, 0, 0, 0, ImmutableArray.Create(0,0,0,0,0,0,0,0,0));
        var participant = new MatchReportParticipant(Guid.NewGuid(), null, ParticipantKind.Guest, "Guest", true,
            ParticipantOutcome.Finished, 60, ImmutableArray.Create(new MatchParticipationSpan(1, 61, ParticipantExitReason.Completed, 0, Hunter.Samus, 0, 60)), metrics);
        return new(1, id ?? Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "test-build", 8, null,
            MatchTrustClass.Community, "Custom", null, new(MatchMode.Battle, "MP1 SANCTORUS"),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 60, MatchEndReason.TimeLimit,
            ImmutableArray.Create(participant));
    }
    private sealed class Transport : IMatchReportTransport
    {
        public Func<Guid,string,ReadOnlyMemory<byte>,CancellationToken,Task<ReportDelivery>> Send = (_,_,_,_) => Task.FromResult(new ReportDelivery(ReportDeliveryKind.Accepted));
        public Task<ReportDelivery> SubmitAsync(Guid id, string hash, ReadOnlyMemory<byte> body, CancellationToken token) => Send(id,hash,body,token);
    }
    private static string DirectoryPath() { string path = Path.Combine(Path.GetTempPath(), "prime-outbox-test-" + Guid.NewGuid()); Directory.CreateDirectory(path); return path; }
    internal static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!predicate()) await Task.Delay(10, timeout.Token); }
    private static MatchReportOutbox NewOutbox(MatchReportOutboxOptions options, IMatchReportTransport transport,
        Action<string, byte[]> persist)
    {
        var constructor = typeof(MatchReportOutbox).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, new[] { typeof(MatchReportOutboxOptions), typeof(IMatchReportTransport), typeof(Action<string, byte[]>) },
            modifiers: null);
        Assert.NotNull(constructor);
        return (MatchReportOutbox)constructor!.Invoke(new object[] { options, transport, persist });
    }

    [Fact]
    public async Task DurableWriterDoesNotWaitForHttpAndQueueAndBytesAreBounded()
    {
        string path = DirectoryPath();
        try
        {
            var transport = new Transport { Send = async (_,_,_,token) => { await Task.Delay(Timeout.Infinite, token); return default; } };
            await using var outbox = new MatchReportOutbox(new(path, MaximumReports: 2, MaximumBytes: 32768, MaximumReportBytes: 8192), transport);
            await Until(() => outbox.Status.Ready);
            Assert.True(outbox.TryEnqueue(Report(), out var first));
            await Until(() => first!.State == ReportSubmissionState.DurablyStored);
            Assert.True(outbox.TryEnqueue(Report(), out var second));
            await Until(() => second!.State == ReportSubmissionState.DurablyStored);
            Assert.False(outbox.TryEnqueue(Report(), out _));
            Assert.Equal(2, Directory.GetFiles(path, "*.json").Length);
            Assert.InRange(outbox.Status.ReservedBytes, 1, 32768);
            Assert.Equal(2, outbox.Status.DurablePending);
        }
        finally { Directory.Delete(path, true); }
    }
    [Fact]
    public async Task RestartRetriesExactDurableBodyAndAcknowledgementDeletesOnlyAcceptedReport()
    {
        string path = DirectoryPath(); byte[]? expected = null, actual = null;
        var firstTransport = new Transport { Send = async (_,_,body,token) => { expected = body.ToArray(); await Task.Delay(Timeout.Infinite, token); return default; } };
        try
        {
            await using (var first = new MatchReportOutbox(new(path), firstTransport))
            {
                await Until(() => first.Status.Ready); Assert.True(first.TryEnqueue(Report(), out var receipt));
                await Until(() => receipt!.State == ReportSubmissionState.DurablyStored && expected != null);
            }
            var secondTransport = new Transport { Send = (_,_,body,_) => { actual = body.ToArray(); return Task.FromResult(new ReportDelivery(ReportDeliveryKind.Accepted)); } };
            await using var recovered = new MatchReportOutbox(new(path), secondTransport);
            await Until(() => recovered.Status.Ready && actual != null && recovered.Status.ReservedReports == 0);
            Assert.Equal(expected, actual); Assert.Empty(Directory.GetFiles(path, "*.json"));
        }
        finally { Directory.Delete(path, true); }
    }
    [Fact]
    public async Task DuplicateSharesReceiptAndConflictNeverOverwritesOriginal()
    {
        string path = DirectoryPath();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport { Send = async (_,_,_,token) => { await release.Task.WaitAsync(token); return new(ReportDeliveryKind.Accepted); } };
        try
        {
            await using var outbox = new MatchReportOutbox(new(path), transport);
            await Until(() => outbox.Status.Ready); var report = Report();
            outbox.TryEnqueue(report, out var original); await Until(() => original!.State == ReportSubmissionState.DurablyStored);
            outbox.TryEnqueue(report, out var duplicate); await Until(() => duplicate!.State == ReportSubmissionState.DurablyStored);
            outbox.TryEnqueue(report with { BuildVersion = "different" }, out var conflict); await Until(() => conflict!.State == ReportSubmissionState.Failed);
            Assert.Single(Directory.GetFiles(path, "*.json"));
            release.SetResult(); await Until(() => original!.State == ReportSubmissionState.BackendAccepted);
            Assert.Equal(ReportSubmissionState.BackendAccepted, duplicate!.State);
        }
        finally { Directory.Delete(path, true); }
    }
    [Fact]
    public async Task RecoveryQuarantinesIncompleteAndCorruptBodiesAndStopsNewMatches()
    {
        string path = DirectoryPath();
        try
        {
            File.WriteAllText(Path.Combine(path, "interrupted.tmp"), "half");
            File.WriteAllText(Path.Combine(path, "corrupt.json"), "{}");
            await using var outbox = new MatchReportOutbox(new(path), new Transport());
            await Until(() => outbox.Status.Ready);
            Assert.Equal(2, outbox.Status.Quarantined); Assert.False(outbox.CanAccept);
            Assert.Equal(2, Directory.GetFiles(path, "*.quarantine").Length);
            await outbox.DisposeAsync();
            await using var recovered = new MatchReportOutbox(new(path), new Transport());
            await Until(() => recovered.Status.Ready);
            Assert.Equal(2, recovered.Status.Quarantined); Assert.False(recovered.CanAccept);
        }
        finally { Directory.Delete(path, true); }
    }
    [Fact]
    public async Task TimedOutShutdownRetainsExclusiveOwnershipUntilUncooperativeWorkerExits()
    {
        string path = DirectoryPath();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        var transport = new Transport { Send = async (_,_,_,_) => { Interlocked.Exchange(ref entered, 1); await release.Task; return new(ReportDeliveryKind.Retry); } };
        try
        {
            var outbox = new MatchReportOutbox(new(path), transport);
            await Until(() => outbox.Status.Ready); outbox.TryEnqueue(Report(), out _); await Until(() => Volatile.Read(ref entered) == 1);
            await outbox.DisposeAsync();
            Assert.Throws<IOException>(() => new MatchReportOutbox(new(path), new Transport()));
            release.SetResult();
            MatchReportOutbox? recovered = null;
            await Until(() =>
            {
                try { recovered = new MatchReportOutbox(new(path), new Transport()); return true; }
                catch (IOException) { return false; }
            });
            await recovered!.DisposeAsync();
        }
        finally { release.TrySetResult(); Directory.Delete(path, true); }
    }

    [Fact]
    public async Task DiskFailureKeepsRamOwnershipAndRecoversWithoutBlockingEnqueue()
    {
        string path = DirectoryPath(); int writes = 0;
        var transport = new Transport { Send = async (_,_,_,token) => { await Task.Delay(Timeout.Infinite, token); return default; } };
        try
        {
            await using var outbox = NewOutbox(new(path), transport, (file, bytes) =>
            {
                if (Interlocked.Increment(ref writes) == 1) throw new IOException("injected disk full");
                File.WriteAllBytes(file, bytes);
            });
            await Until(() => outbox.Status.Ready); Assert.True(outbox.TryEnqueue(Report(), out var receipt));
            await Until(() => outbox.Status.LastError != null);
            Assert.Equal(ReportSubmissionState.Queued, receipt!.State); Assert.False(outbox.Healthy);
            await Until(() => receipt.State == ReportSubmissionState.DurablyStored);
            Assert.True(outbox.Healthy); Assert.Single(Directory.GetFiles(path, "*.json"));
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task SuccessfulLaterWriteCannotClearPermanentValidationFault()
    {
        string path = DirectoryPath(); var release = new ManualResetEventSlim(); int entered = 0;
        var transport = new Transport { Send = async (_,_,_,token) => { await Task.Delay(Timeout.Infinite, token); return default; } };
        try
        {
            await using var outbox = NewOutbox(new(path), transport, (file, bytes) =>
            { Interlocked.Exchange(ref entered, 1); release.Wait(); File.WriteAllBytes(file, bytes); });
            await Until(() => outbox.Status.Ready); outbox.TryEnqueue(Report(), out _); await Until(() => Volatile.Read(ref entered) == 1);
            Assert.True(outbox.TryEnqueue(Report() with { SchemaVersion = 99 }, out var bad));
            Assert.True(outbox.TryEnqueue(Report(), out var good)); release.Set();
            await Until(() => good!.State == ReportSubmissionState.DurablyStored);
            Assert.Equal(ReportSubmissionState.Failed, bad!.State); Assert.False(outbox.Healthy);
        }
        finally { release.Set(); release.Dispose(); Directory.Delete(path, true); }
    }

    [Fact]
    public async Task ReservationProtectsInFlightMatchCapacityUntilCompletionTransfer()
    {
        string path = DirectoryPath();
        try
        {
            await using var outbox = new MatchReportOutbox(new(path, MaximumReports: 1), new Transport());
            await Until(() => outbox.Status.Ready); Assert.True(outbox.TryReserve(out var reservation));
            Assert.False(outbox.CanAccept); Assert.False(outbox.TryEnqueue(Report(), out _));
            Assert.True(outbox.TryEnqueue(Report(), reservation!, out var receipt)); reservation!.Dispose();
            await Until(() => receipt!.State == ReportSubmissionState.BackendAccepted);
            Assert.True(outbox.CanAccept);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task RetryKeepsPerServerFifoAndDoesNotBlockDurabilityOfLaterReport()
    {
        string path = DirectoryPath(); var ids = new System.Collections.Concurrent.ConcurrentQueue<Guid>();
        int sends = 0; var first = Report(); var second = Report();
        var transport = new Transport { Send = (id,_,_,_) =>
        { ids.Enqueue(id); return Task.FromResult(new ReportDelivery(Interlocked.Increment(ref sends) == 1 ? ReportDeliveryKind.Retry : ReportDeliveryKind.Accepted)); } };
        try
        {
            await using var outbox = new MatchReportOutbox(new(path), transport);
            await Until(() => outbox.Status.Ready); outbox.TryEnqueue(first, out var firstReceipt);
            outbox.TryEnqueue(second, out var secondReceipt);
            await Until(() => secondReceipt!.State == ReportSubmissionState.DurablyStored);
            Assert.Equal(ReportSubmissionState.DurablyStored, firstReceipt!.State);
            await Until(() => secondReceipt!.State == ReportSubmissionState.BackendAccepted);
            Assert.Equal(new[] { first.MatchId, first.MatchId, second.MatchId }, ids.ToArray());
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void ContractRoundTripRejectsInventedIdentitiesAndMissingImmutableMetrics()
    {
        var report = Report();
        var restored = JsonSerializer.Deserialize<MatchReportV1>(JsonSerializer.SerializeToUtf8Bytes(report))!;
        Assert.True(restored.IsValid); Assert.Equal(report.Participants[0].Metrics.DamageDealt, restored.Participants[0].Metrics.DamageDealt);
        Assert.False((report with { Participants = ImmutableArray.Create(report.Participants[0] with { Kind = ParticipantKind.RegisteredHuman }) }).IsValid);
        Assert.False((report with { Participants = ImmutableArray<MatchReportParticipant>.Empty }).IsValid);
        Assert.Null(restored.Participants[0].Metrics.Shots);
        Assert.Throws<ArgumentException>(() => new HttpMatchReportTransport(Guid.NewGuid(), new Uri("http://localhost/report"), "test"));
    }
}
