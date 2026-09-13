using System;
using System.Buffers.Binary;
using System.Text;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

/// <summary>
/// QZ0 translations for Node/control-plane flooding and malformed input. All
/// clocks and identities are synthetic so the suite is deterministic and does
/// not require a running Node, database, or external content.
/// </summary>
public sealed class Qz0ControlRegressionTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    [Trait("Regression", "QZ0")]
    public void StatusFloodStopsAtTheDeterministicTokenBucketCapacity()
    {
        // Prime invariant: discovery/status abuse is bounded without blocking
        // the authoritative worker or allocating an unbounded queue.
        var fixture = new FloodFixture { Now = 0 };
        var limit = new NetRateLimit(perSecond: 8, capacity: 16, now: fixture.Now);
        Assert.Equal(16, fixture.Consume(ref limit, 17));
        fixture.Now = 1;
        Assert.Equal(8, fixture.Consume(ref limit, 9));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void JoinFloodCannotExceedTheBoundedAdmissionBurst()
    {
        // Prime invariant: repeated join attempts consume a bounded admission
        // budget and never become an unbounded pending-work source.
        var fixture = new FloodFixture();
        var limit = new NetRateLimit(perSecond: 16, capacity: 32, now: fixture.Now);
        Assert.Equal(32, fixture.Consume(ref limit, 100));
        Assert.Equal(0, fixture.Consume(ref limit, 1));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ChatFloodHonorsMonotonicTimeAndRefillsOnlyAtTheConfiguredRate()
    {
        // Prime invariant: chat flood protection rejects clock rollback and
        // replenishes one half token per second, independently of join/status.
        var fixture = new FloodFixture { Now = 5 };
        var limit = new NetRateLimit(perSecond: 0.5, capacity: 3, now: fixture.Now);
        Assert.Equal(3, fixture.Consume(ref limit, 4));
        fixture.Now = 4;
        Assert.Equal(0, fixture.Consume(ref limit, 1));
        fixture.Now = 7;
        Assert.Equal(1, fixture.Consume(ref limit, 1));
        Assert.False(limit.Take(double.NaN));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public async Task RejoinFloodUsesExplicitGenerationAndRemainsBoundedByWorkerAdmission()
    {
        // Prime invariant: each recovery mint is explicit and monotonically
        // fenced. Transport-level request/session limits remain the flood
        // boundary; the retired five-second coordinator rejection is not a
        // correctness mechanism.
        var fixture = new FloodFixture();
        await using var manager = new WorkerManager(new(fixture.NextGuid()), fixture.NextGuid());
        await using var scheduler = new WorkerScheduler(manager);
        using var issuer = new WorkerAdmissionIssuer("qz0-rejoin");
        // WorkerProcessHarness intentionally advertises its fixed protocol-8
        // fixture identity. Rejoin throttling is transport-version agnostic.
        const byte harnessProtocol = 8;
        var content = new ContentIdentity("qz0", "hash", "1", "test", harnessProtocol);
        var workerContent = new WorkerContentIdentity(content.ContentVersion,
            content.ContentHash, content.BuildVersion, content.ProtocolVersion);
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("normal") with { Content = workerContent });
        var lobbies = new LobbyManager();
        LobbyIdentity owner = fixture.Identity("Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, manager,
            issuer, new NodeContentCatalog([content]));
        var lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyCreate("Arena", LobbyVisibility.Public));
        lobby = (LobbySnapshot)lobbies.Execute(owner,
            new LobbyConfigure(lobby.Revision, content.MapKey, MatchMode.Battle));
        lobby = (LobbySnapshot)lobbies.Execute(owner, new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
        Guid matchId = lobby.CurrentMatchId!.Value;

        NodeMatchHandoff first = Assert.IsType<NodeMatchHandoff>(
            await coordinator.ExecuteAsync(owner, new NodeMatchRejoin(matchId)));
        HandoffGeneration priorGeneration = first.HandoffGeneration;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            NodeMatchHandoff next = Assert.IsType<NodeMatchHandoff>(
                await coordinator.ExecuteAsync(owner, new NodeMatchRejoin(matchId)));
            Assert.True(next.HandoffGeneration.Value > priorGeneration.Value);
            priorGeneration = next.HandoffGeneration;
        }

        Assert.Equal(matchId, first.MatchId);
        Assert.True(scheduler.TryGetAssignment(new(matchId), out WorkerMatchAssignment? assignment));
        Assert.NotNull(assignment);
        Assert.Equal(matchId, lobbies.ForSession(owner.SessionId)!.CurrentMatchId);
        scheduler.CancelMatch(new(matchId), "qz0 cleanup");
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void DuplicateControlEnvelopeFieldsFailClosedBeforeCommandMutation()
    {
        // Prime invariant: ambiguous JSON is rejected before any Node command
        // reaches lobby state or a worker handoff.
        byte[] frame = Encoding.UTF8.GetBytes(
            $"{{\"version\":{NodeControlCodec.Version},\"version\":{NodeControlCodec.Version},\"type\":\"node.ping\",\"requestId\":\"11111111-1111-1111-1111-111111111111\",\"payload\":{{}}}}");
        Assert.Throws<System.Text.Json.JsonException>(() => NodeControlCodec.Read(frame));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void DuplicateNestedControlFieldsFailClosed()
    {
        // Prime invariant: duplicate nested fields cannot smuggle two values
        // past strict Node DTO decoding.
        byte[] frame = Frame("lobby.create",
            "{\"name\":\"Arena\",\"name\":\"Other\",\"visibility\":\"Public\"}");
        Assert.Throws<System.Text.Json.JsonException>(() => NodeControlCodec.Read(frame));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void InvalidControlEnumFailsClosed()
    {
        // Prime invariant: a client cannot invent a lobby visibility/mode enum
        // that changes admission policy.
        byte[] frame = Frame("lobby.create", "{\"name\":\"Arena\",\"visibility\":\"ForeignMode\"}");
        Assert.Throws<System.Text.Json.JsonException>(() => NodeControlCodec.Read(frame));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void OversizeControlFrameIsRejectedBeforeParsing()
    {
        // Prime invariant: control frames have a hard bound and cannot create a
        // parser or queue amplification path.
        Assert.Throws<System.Text.Json.JsonException>(() =>
            NodeControlCodec.Read(new byte[NodeControlCodec.MaximumFrameBytes + 1]));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void UnknownControlFieldsAreRejectedByTheStrictSchema()
    {
        // Prime invariant: protocol evolution is explicit; unknown fields never
        // silently alter a Node command.
        byte[] frame = Frame("lobby.create",
            "{\"name\":\"Arena\",\"visibility\":\"Public\",\"unboundedQueue\":true}");
        Assert.Throws<System.Text.Json.JsonException>(() => NodeControlCodec.Read(frame));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ReplayingAStaleLobbyRevisionCannotPartiallyMutateTheLobby()
    {
        // Prime invariant: command flood and stale revisions are atomic; only
        // the first command at a revision changes lobby state.
        var manager = new LobbyManager();
        var owner = new LobbyIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222"), null, "Owner",
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var created = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Arena", LobbyVisibility.Public));
        var accepted = (LobbySnapshot)manager.Execute(owner, new LobbyChat("first", created.Revision));

        for (int i = 0; i < 32; i++)
            Assert.Throws<LobbyCommandException>(() => manager.Execute(owner, new LobbyChat("stale", created.Revision)));

        LobbySnapshot current = manager.ForSession(owner.SessionId)!;
        Assert.Equal(accepted.Revision, current.Revision);
        Assert.Single(current.Chat);
        Assert.Equal("first", current.Chat[0].Text);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorkerIpcDuplicateFieldsAreRejectedWithoutResynchronizingTheStream()
    {
        // Prime invariant: malformed local control traffic is terminal for the
        // frame; a duplicate field never advances into a second message.
        byte[] body = Encoding.UTF8.GetBytes(
            "{\"version\":1,\"payload\":{\"reason\":\"one\",\"reason\":\"two\"}}");
        byte[] frame = new byte[body.Length + 5];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length + 1);
        frame[4] = 4; // WorkerIpcCodec's Drain message type.
        body.CopyTo(frame, 5);
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(frame));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorkerIpcOversizeLengthIsRejectedBeforePayloadAllocation()
    {
        // Prime invariant: local Node/Worker framing is bounded independently
        // from the reliable gameplay channel.
        byte[] frame = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)WorkerIpcCodec.MaxFrameLength + 1);
        frame[4] = 4;
        Assert.Throws<InvalidDataException>(() => WorkerIpcCodec.Decode(frame));
    }

    private static byte[] Frame(string type, string payload)
        => Encoding.UTF8.GetBytes($$"""{"version":{{NodeControlCodec.Version}},"type":"{{type}}","requestId":"{{RequestId:D}}","payload":{{payload}}}""");
}
