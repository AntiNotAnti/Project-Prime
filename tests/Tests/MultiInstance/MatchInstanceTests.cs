using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using FruityPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Admin;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class MatchInstanceTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void TwoRealMatchesFinishOnceWithoutReplacingTheirWorldOrOwningTransport()
    {
        using var content = OpenContent();
        var transport = new SilentTransport();
        var firstSpec = Spec() with { TournamentId = Guid.NewGuid(), RoundId = Guid.NewGuid() };
        using var first = new MatchInstance(new(firstSpec, 10) { ReportingServerId = Guid.NewGuid() }, transport);
        using var second = new MatchInstance(new(Spec(), 11), new SilentTransport());
        var world = first.Simulation;
        int completed = 0;
        first.Completed += completion => { completed++; Assert.Equal(firstSpec.MatchId.Value, completion.MatchId); };
        first.Start(); second.Start();
        for (int tick = 0; tick < 1000; tick++) { first.Tick(); second.Tick(); }
        Assert.Equal(MatchInstanceState.Completed, first.State);
        Assert.Equal(MatchInstanceState.Completed, second.State);
        Assert.Equal(1, completed);
        Assert.Same(world, first.Simulation);
        Assert.Equal(0, first.Simulation.Bots.Participants[0]!.TeamIndex);
        Assert.Equal(1, first.Simulation.Bots.Participants[1]!.TeamIndex);
        Assert.NotSame(first.Simulation.Scene, second.Simulation.Scene);
        Assert.NotNull(first.Completion!.Result);
        Assert.NotNull(first.Completion.Telemetry);
        Assert.Equal(firstSpec.MatchId.Value, first.Completion.Report!.MatchId);
        Assert.Equal(firstSpec.TournamentId.Value.ToString("D"), first.Completion.Report.TournamentId);
        Assert.Equal(firstSpec.RoundId.Value.ToString("D"), first.Completion.Report.RoundId);
        uint final = first.NextTick;
        first.Tick(); first.RequestStop(MatchStopReason.Requested);
        Assert.Equal(final, first.NextTick);
        Assert.Equal(1, completed);
        first.Dispose();
        Assert.False(transport.Disposed);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FrozenBotSeatsAndPoliciesApplyBeforeFirstTickAndStopIsIdempotent()
    {
        using var content = OpenContent();
        using var match = new MatchInstance(new(Spec() with { Rules = new MatchRules(MatchMode.TeamBattle, "MP1 SANCTORUS", maxPlayers: 2) }, 12), new SilentTransport());
        Assert.True(match.Network.AdmissionClosed);
        Assert.Equal(2, match.Simulation.Bots.Count);
        Assert.Equal(Hunter.Spire, match.Simulation.Bots.Participants[0]!.Hunter);
        Assert.Equal("Frozen Spire", match.Simulation.Scene.Roster.Nicknames[0]);
        Assert.Equal(1, match.Simulation.Scene.Players[0].TeamIndex);
        int completed = 0; match.Completed += _ => completed++;
        match.Start(); match.RequestStop(MatchStopReason.Requested); match.RequestStop(MatchStopReason.Requested);
        match.Tick();
        Assert.Equal(1, completed);
        Assert.Equal(MatchInstanceState.Stopped, match.State);
        Assert.Equal(0u, match.NextTick);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void WorkerAdminEndsThroughNormalResultAndRejectsNodeOwnedPause()
    {
        using var content = OpenContent(); var spec = Spec();
        using var match = new MatchInstance(new(spec, 14), new SilentTransport());
        match.Start();
        Assert.Equal("node_owned", WorkerMatchAdmin.Apply(match, new(spec.MatchId, AdminAction.Pause, null)).Code);
        for (int tick = 0; tick < 190; tick++) match.Tick();
        Assert.Equal(MatchPhase.Playing, match.Simulation.Scene.Match.Phase);
        Assert.True(WorkerMatchAdmin.Apply(match, new(spec.MatchId, AdminAction.EndMatch, null)).Applied);
        match.Tick();
        Assert.NotNull(match.Simulation.Scene.Match.Result);
        Assert.Equal(MatchPhase.Ending, match.Simulation.Scene.Match.Phase);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FailedTickEmitsOnceAndDoesNotRetryTheWorld()
    {
        using var content = OpenContent();
        using var match = new MatchInstance(new(Spec(), 13), new SilentTransport { FailDrain = true });
        int failures = 0; match.Failed += _ => failures++;
        match.Start();
        Assert.Throws<InvalidOperationException>(() => match.Tick());
        Assert.Equal(MatchInstanceState.Failed, match.State);
        match.Tick(); match.RequestStop(MatchStopReason.Requested);
        Assert.Equal(1, failures);
        Assert.Equal(0u, match.NextTick);
    }

    [Fact]
    public void ConflictingLaunchPoliciesAreRejectedBeforeWorldConstruction()
    {
        var spec = Spec() with { ReplayPolicy = ReplayPolicy.Disabled };
        Assert.Throws<ArgumentException>(() => new MatchInstance(new(spec, 1) { RequireReplay = true }, new SilentTransport()));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void MatchInstanceDrainsItsSemanticSinkIntoTelemetry()
    {
        using var content = OpenContent();
        using var match = new MatchInstance(new(Spec(), 21), new SilentTransport());
        match.Start();
        match.Tick(); // Binds the authoritative wire match identity.
        MatchEvent normalized = match.Simulation.Scene.Match.SemanticEvents.Dispatch(new(0, 1, 21,
            match.Simulation.Scene.Match.PhaseRevision, MatchEventKind.OvertimeStarted,
            CombatActor.None, CombatActor.None));

        match.Tick();

        Assert.True(match.SemanticQueueHighWater > 0);
        Assert.Equal(0L, match.DroppedSemanticEvents);
        Assert.Contains(match.Telemetry!.Complete(match.NextTick, completed: false).Events,
            value => value.Kind == MphRead.Telemetry.TelemetryKind.MatchSemantic
                && value.SemanticId == normalized.Id
                && value.Value == (int)MatchEventKind.OvertimeStarted);
    }

    private static MatchSpec Spec() => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
        new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2, timeLimit: TimeSpan.FromSeconds(1)),
        new("MP1 SANCTORUS", "test-content", "AMHE1", "test-build", NetHeader.Version), MatchTrustClass.Community,
        null, null, ImmutableArray.Create(
            new RosterSeat(0, null, null, "Frozen Spire", Hunter.Spire, 1, SeatRole.Bot, false),
            new RosterSeat(1, null, null, "Frozen Samus", Hunter.Samus, 0, SeatRole.Bot, false)),
        FruityPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled,
        TelemetryPolicy.Record, 123, 456);

    private static IDisposable OpenContent()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        var context = ServerContent.PreserveContext("AMHE1");
        try { ServerContent.Open(data, "AMHE1"); return context; }
        catch { context.Dispose(); throw; }
    }

    private sealed class SilentTransport : INetTransport
    {
        public bool Disposed { get; private set; }
        public bool FailDrain { get; init; }
        public int LocalPort => 0;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();
        public void Dispose() => Disposed = true;
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public IEnumerable<ReceivedPacket> Drain() => FailDrain ? throw new InvalidOperationException("Test transport failure") : Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
    }
}
