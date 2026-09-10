using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class WorkerIpcTests
{
    private static readonly MatchId Match = new(Guid.NewGuid());
    private static readonly WorkerId Worker = new(Guid.NewGuid());
    private static readonly NodeId Node = new(Guid.NewGuid());
    private static readonly WorkerCapacity Capacity = new(4, 32, 1, 2);
    private static MatchSpec Spec() => new(Match, new(Guid.NewGuid()), Node, Guid.NewGuid(),
        new MatchRules(MatchMode.Battle, "test", 2), new("test", "hash", "1", "test", 8),
        MatchTrustClass.Private, null, null,
        ImmutableArray.Create(new RosterSeat(0, new PlayerId(Guid.NewGuid()), null, "Player", Hunter.Samus, 0, SeatRole.Player, false)),
        BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Record, TelemetryPolicy.Record, 1, 2);

    public static IEnumerable<object[]> Messages()
    {
        WorkerMessage[] messages = [new WorkerConfigure(Node, Guid.NewGuid(), Capacity), new CreateMatch(Spec()),
            new CancelMatch(Match, "cancel"), new Drain("drain"), new Shutdown("shutdown"),
            new MatchAdminCommand(Match, AdminAction.KickSeat, 0), new UpdateNodeSigningKey("key", "public"),
            new WorkerHello(Worker, Guid.NewGuid(), Node, "startup-token", "build"), new WorkerReady(Worker, Guid.NewGuid(), Capacity),
            new WorkerHeartbeat(Worker, Guid.NewGuid(), Capacity, new(WorkerStatus.Ready, 100, 2.5, 4096)),
            new MatchReady(new(Match, new WireMatchId(1), Worker, Guid.NewGuid(), "127.0.0.1", 7777)), new MatchStarted(Match), new MatchCompleted(Completion()),
            new MatchFailed(Match, "failed"), new MatchInterrupted(Match, "lost"), new MatchReportReady(Match, Guid.NewGuid(), new WorkerId(Guid.NewGuid()), Guid.NewGuid(), new string('A', 64), 100),
            new WorkerDraining(Worker, Guid.NewGuid()), new WorkerFault(Worker, Guid.NewGuid(), "fault")];
        return messages.Select(m => new object[] { m });
    }

    [Theory, MemberData(nameof(Messages))]
    public void EveryMessageRoundTrips(WorkerMessage message)
    {
        byte[] encoded = WorkerIpcCodec.Encode(message);
        Assert.Equal(encoded.Length - 4, (int)BinaryPrimitives.ReadUInt32LittleEndian(encoded));
        var decoded = WorkerIpcCodec.Decode(encoded);
        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(encoded, WorkerIpcCodec.Encode(decoded));
    }

    [Theory]
    [InlineData("{\"version\":2,\"payload\":{\"reason\":\"test\"}}")]
    [InlineData("{\"version\":1,\"payload\":null}")]
    [InlineData("{\"version\":1,\"payload\":{}}")]
    [InlineData("{\"version\":1,\"payload\":{\"reason\":null}}")]
    [InlineData("{\"version\":1,\"payload\":{\"reason\":\"ok\",\"extra\":true}}")]
    [InlineData("{\"version\":1,\"version\":1,\"payload\":{\"reason\":\"ok\"}}")]
    [InlineData("{\"version\":1,\"payload\":{\"reason\":\"a\",\"reason\":\"b\"}}")]
    [InlineData("{")]
    public void RejectsMalformedPayload(string json) => Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(Frame(4, json)));

    [Fact]
    public void RejectsUnknownTypeLengthsAndTrailingBytes()
    {
        var valid = WorkerIpcCodec.Encode(new Drain("ok"));
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(valid.AsSpan(0, valid.Length - 1)));
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(valid.Concat(new byte[1]).ToArray()));
        valid[4] = 255;
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(valid));
        BinaryPrimitives.WriteUInt32LittleEndian(valid, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(valid));
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(new byte[4]));
    }

    [Fact]
    public async Task StreamReadsSequentialFramesAndDistinguishesCleanFromTruncatedEof()
    {
        using var stream = new MemoryStream();
        await WorkerIpcCodec.WriteAsync(stream, new Drain("one"));
        await WorkerIpcCodec.WriteAsync(stream, new Shutdown("two"));
        stream.Position = 0;
        Assert.IsType<Drain>(await WorkerIpcCodec.ReadAsync(stream));
        Assert.IsType<Shutdown>(await WorkerIpcCodec.ReadAsync(stream));
        Assert.Null(await WorkerIpcCodec.ReadAsync(stream));
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await WorkerIpcCodec.ReadAsync(new MemoryStream(new byte[] { 1 })));
        var prefix = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(prefix, uint.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await WorkerIpcCodec.ReadAsync(new MemoryStream(prefix)));
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 20);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await WorkerIpcCodec.ReadAsync(new MemoryStream(prefix)));
    }

    [Fact]
    public void EnforcesSemanticBoundsOnWriteAndRead()
    {
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new Drain(new string('x', 1025))));
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(Frame(4, "{\"version\":1,\"payload\":{\"reason\":\"" + new string('x', 1025) + "\"}}")));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new MatchStarted(default)));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new WorkerReady(Worker, Guid.NewGuid(), Capacity with { ActiveMatches = 5 })));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new WorkerHeartbeat(Worker, Guid.NewGuid(), Capacity, new(WorkerStatus.Ready, 1, double.NaN, 1))));
    }

    [Fact]
    public void FrozenRosterAndRulesPreserveInputsAndRejectInvalidSeats()
    {
        var spec = Spec(); spec.Validate();
        Assert.Throws<ArgumentException>(() => (spec with { Content = spec.Content with { MapKey = "other" } }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = [spec.Roster[0] with { SeatId = 8 }] }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = [spec.Roster[0] with { Hunter = unchecked((Hunter)(-1)) }] }).Validate());
        var changed = spec.Roster.Add(spec.Roster[0]);
        Assert.Single(spec.Roster);
        Assert.Throws<ArgumentException>(() => (spec with { Roster = changed }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = default }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = [spec.Roster[0] with { Hunter = Hunter.Random }] }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = [spec.Roster[0] with { GuestSessionId = Guid.NewGuid() }] }).Validate());
        Assert.Throws<ArgumentException>(() => (spec with { Roster = [spec.Roster[0] with { Role = SeatRole.Observer }] }).Validate());
    }

    [Fact]
    public async Task FragmentedReadsAndCancellationAreSupported()
    {
        using var stream = new FragmentedStream(WorkerIpcCodec.Encode(new Drain("fragmented")));
        Assert.Equal(new Drain("fragmented"), await WorkerIpcCodec.ReadAsync(stream));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await WorkerIpcCodec.ReadAsync(new MemoryStream(new byte[4]), cancellation.Token));
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    [Fact]
    public void PlacementAdminAndSummaryRejectInvalidIdentitiesAndBounds()
    {
        var placement = new MatchPlacement(Match, new(1), Worker, Guid.NewGuid(), "localhost", 7777);
        placement.Validate();
        Assert.Throws<ArgumentException>(() => (placement with { WireMatchId = default }).Validate());
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new MatchReady(placement with { WorkerIncarnation = Guid.Empty })));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new MatchReady(placement with { WireMatchId = default })));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new MatchAdminCommand(Match, AdminAction.KickSeat, 32)));
        Assert.Equal(typeof(MatchAdminCommand), WorkerIpcCodec.Decode(WorkerIpcCodec.Encode(
            new MatchAdminCommand(Match, AdminAction.LagCompHistory, 0))).GetType());
        Assert.Equal(typeof(MatchAdminCommand), WorkerIpcCodec.Decode(WorkerIpcCodec.Encode(
            new MatchAdminCommand(Match, AdminAction.LagCompClear, 0))).GetType());
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(
            new MatchAdminCommand(Match, AdminAction.LagCompDynamic, null)));
        Assert.Throws<ArgumentException>(() => WorkerIpcCodec.Encode(new MatchAdminCommand(default, AdminAction.Pause, null)));
        var summary = new NodeMatchSummary(Match, new(1), new(Guid.NewGuid()), Worker, Guid.NewGuid(), MatchStatus.Running, "test", 2, 0);
        summary.Validate();
        Assert.Throws<ArgumentException>(() => (summary with { Players = 9 }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Observers = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Observers = 31 }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Status = (MatchStatus)255 }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { MapKey = new string('x', 129) }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { WireMatchId = default }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { WorkerIncarnation = Guid.Empty }).Validate());
    }

    private static MatchCompletionSummary Completion() => new(Match, new(Guid.NewGuid()), MatchEndReason.ScoreGoal,
        [new(Guid.NewGuid(), new PlayerId(Guid.NewGuid()), ParticipantKind.RegisteredHuman, "Player",
            ParticipantOutcome.Finished, 1, 1, -1, 2, 1)], Guid.NewGuid(), null, Guid.NewGuid());

    [Fact]
    public void CompletionBoundsAndIdentitiesAreValidated()
    {
        var summary = Completion(); summary.Validate();
        Assert.Throws<ArgumentException>(() => (summary with { Players = default }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [null!] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = ImmutableArray.CreateRange(Enumerable.Repeat(summary.Players[0], 33)) }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [summary.Players[0], summary.Players[0]] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [summary.Players[0] with { Standing = 9 }] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [summary.Players[0] with { Outcome = (ParticipantOutcome)255 }] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [summary.Players[0] with { Kind = ParticipantKind.Bot }] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { Players = [summary.Players[0] with { DisplayName = new string('x', 65) }] }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { EndReason = (MatchEndReason)255 }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { ReplayId = Guid.Empty }).Validate());
        Assert.Throws<ArgumentException>(() => (summary with { ReportId = Guid.Empty }).Validate());
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(Frame(13, "{\"version\":1,\"payload\":{\"summary\":null}}")));
        var json = Encoding.UTF8.GetString(WorkerIpcCodec.Encode(new MatchCompleted(summary)).AsSpan(5));
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(Frame(13, json.Replace("\"standing\":1", "\"standing\":9"))));
    }

    private static byte[] Frame(byte type, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json); byte[] frame = new byte[body.Length + 5];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length + 1); frame[4] = type; body.CopyTo(frame, 5); return frame;
    }
}
