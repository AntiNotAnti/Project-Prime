using System;
using System.Net;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// QZ0 translations for slot reuse and match-boundary identity. Connection and
/// life identity are deliberately exercised independently from display names.
/// </summary>
public sealed class Qz0IdentityBoundaryRegressionTests
{
    [Fact]
    [Trait("Regression", "QZ0")]
    public void OldAssistContributionCannotTransferToAReplacementLife()
    {
        // Prime invariant: a contribution belongs to one full combat actor;
        // reusing its slot with a new life removes the old assist credit.
        var fixture = new SlotReuseFixture();
        uint tick = fixture.NextTick(100);
        var ledger = new DamageContributionLedger();
        ledger.Add(fixture.Victim, fixture.Original, tick, 30, hostile: true);
        Span<CombatActor> assists = stackalloc CombatActor[8];
        Assert.Equal(1, ledger.Collect(fixture.Killer, tick + 1, 20, 300, assists));
        Assert.Equal(fixture.Original, assists[0]);

        ledger.Add(fixture.Victim, fixture.ReplacementLife, tick + 2, 1, hostile: true);
        assists.Clear();
        Assert.Equal(0, ledger.Collect(fixture.Killer, tick + 3, 20, 300, assists));
        Assert.DoesNotContain(fixture.ReplacementLife, assists.ToArray());
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void LagCompensationHistoryRejectsAReusedSlotWithAnOldConnection()
    {
        // Prime invariant: a reused slot with a new connection cannot read the
        // previous owner's historical collider.
        var history = new LagCompensationHistory();
        history.Record(12, PlayerState(connection: 10, life: 1));
        Assert.False(history.TryGet(0, 12, 11, 1, out _));
        Assert.True(history.TryGet(0, 12, 10, 1, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void LagCompensationHistoryRejectsAReusedLifeWithTheSameConnection()
    {
        // Prime invariant: a new life on the same connection cannot inherit an
        // old projectile attribution or hitbox history.
        var history = new LagCompensationHistory();
        history.Record(12, PlayerState(connection: 10, life: 1));
        history.Record(13, PlayerState(connection: 10, life: 2));
        Assert.False(history.TryGet(0, 13, 10, 1, out _));
        Assert.True(history.TryGet(0, 13, 10, 2, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void SnapshotInterpolationDoesNotBlendAReusedSlotAcrossLives()
    {
        // Prime invariant: a slot replacement is a discrete transition; the
        // old occupant's pose is never used to interpolate the new occupant.
        var history = new SnapshotInterpolation();
        Add(history, 100, Snapshot(100, connection: 7, life: 1));
        Add(history, 110, Snapshot(500, connection: 8, life: 1));

        Assert.True(history.TrySample(0, 105, out SnapshotPlayer state));
        Assert.Equal(500, state.Position.X);
        Assert.Equal((ulong)8, state.ConnectionId);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void CombatFeedbackDoesNotApplyDamageFromAFormerLocalLife()
    {
        // Prime invariant: late damage for an old life is retained as an
        // unowned presentation fact, never applied to the replacement life.
        CombatActor oldLife = new(0, 77, 1);
        CombatActor newLife = new(0, 77, 2);
        var feedback = new CombatFeedback();
        feedback.Bind(1, newLife, Array.Empty<NetRosterEntry>());
        var damage = new CombatEvent(1, 30, 9, CombatEventKind.Damage, 0,
            CombatEventFlags.None, new CombatActor(1, 88, 1), oldLife, 90, 10,
            Vector3.Zero, Vector3.UnitZ, 0, 0, 0);

        Assert.True(feedback.Process(damage));
        Assert.Equal(0, feedback.History.Count);
        Assert.False(feedback.State.Dead);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorldFeedbackRejectsAnOldMatchEventAfterMatchBindingChanges()
    {
        // Prime invariant: a reliable world event from Match A cannot mutate
        // presentation state after the client binds Match B.
        var feedback = new WorldFeedback();
        feedback.Bind(2, 1);
        WorldEvent oldMatch = FlagEvent(1, match: 1, phase: 1, WorldSignalKind.FlagCaptured);

        Assert.False(feedback.Process(oldMatch, CombatActor.None, 20));
        Assert.Equal(0u, feedback.Sequence);
        Assert.Equal("", feedback.Message);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void WorldFeedbackRejectsAnOldPhaseEventAfterThePhaseBoundary()
    {
        // Prime invariant: an intermission/reconnect phase boundary invalidates
        // old presentation events even when the persistent match ID is reused.
        var feedback = new WorldFeedback();
        feedback.Bind(5, 2);
        WorldEvent oldPhase = FlagEvent(1, match: 5, phase: 1, WorldSignalKind.FlagDropped);

        Assert.False(feedback.Process(oldPhase, CombatActor.None, 20));
        Assert.Equal(0u, feedback.Sequence);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void PreparedSnapshotPresentationIsInvalidAfterAReusableMatchReset()
    {
        // Prime invariant: a reset invalidates prepared pictures by generation,
        // so Match A's frame cannot be presented in Match B.
        var history = new SnapshotInterpolation();
        Add(history, 100, Snapshot(1));
        Add(history, 110, Snapshot(2));
        Assert.True(history.TryPreparePresentation(111, out SnapshotPresentation prepared));
        history.Reset();
        Add(history, 1, Snapshot(50));

        Assert.False(history.MarkPresented(prepared));
        Assert.False(history.TrySample(0, prepared, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void NetConnectionRejectsAnOldReadyAfterMatchTransition()
    {
        // Prime invariant: readiness is scoped to the current match epoch and
        // cannot re-admit a stale reconnect packet.
        var connection = new NetConnection(41, new IPEndPoint(IPAddress.Loopback, 40001), 7, 0);
        Assert.True(connection.Ready(7));
        Assert.True(connection.StartPlaying());
        connection.BeginLoading(8);

        Assert.False(connection.Ready(7));
        Assert.True(connection.Ready(8));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void MatchTransitionCancelsQueuedApplicationEventsButKeepsWelcome()
    {
        // Prime invariant: changing match state cannot deliver old combat/world
        // events, while the admission welcome remains deliverable.
        var channel = new ReliableChannel();
        Assert.True(channel.TryEnqueue(ReliableEventType.Welcome, ReadOnlySpan<byte>.Empty, out _));
        Assert.True(channel.TryEnqueue(ReliableEventType.WorldEvent, ReadOnlySpan<byte>.Empty, out _));
        channel.CancelPendingExceptWelcome();

        Assert.Equal(1, channel.PendingCount);
        Assert.True(channel.TryGetDue(0, out uint id, out ReliableEventType type, out _));
        Assert.Equal(ReliableEventType.Welcome, type);
        channel.MarkSent(id, 1, 0);
        Assert.False(channel.TryGetDue(0, out _, out _, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void RoutedJoinRequiresAnExplicitWireMatchIdentity()
    {
        // Prime invariant: a multi-match worker never routes a join with a
        // missing/zero WireMatchId to an arbitrary match.
        var router = new RoutedMatchDatagramRouter();
        byte[] routed = JoinDatagram(new JoinPacket(NetHeader.Version, 91, Hunter.Samus, "route", WireMatchId: 70001));
        Assert.True(router.TryRoute(routed, out WorkerDatagramRoute route));
        Assert.Equal(70001u, route.WireMatchId);
        Assert.True(route.IsJoin);

        byte[] legacy = JoinDatagram(new JoinPacket(NetHeader.Version, 92, Hunter.Samus, "legacy"));
        Assert.False(router.TryRoute(legacy, out _));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void OldReplayEventFromMatchACannotEnterMatchB()
    {
        // Prime invariant: replay rotation clears queued Match A facts and the
        // decoder rejects any late Match A event after Match B is current.
        var fixture = new MatchBoundaryFixture();
        var state = new ModernReplayState();
        state.Reset(NetHeader.Version);
        Assert.True(state.Receive(fixture.MatchRecord(fixture.MatchA, UInt32.MaxValue - 2)));
        Assert.True(state.Receive(fixture.WorldEventRecord(fixture.MatchA,
            fixture.PhaseA, WorldSignalKind.FlagDropped)));

        Assert.True(state.Receive(fixture.MatchRecord(fixture.MatchB, 3)));
        Assert.Equal(fixture.MatchB, state.Match.MatchId);
        Assert.False(state.Receive(fixture.WorldEventRecord(fixture.MatchA,
            fixture.PhaseA, WorldSignalKind.FlagCaptured)));
        Assert.Equal(fixture.MatchB, state.Match.MatchId);
    }

    private static byte[] JoinDatagram(JoinPacket join)
    {
        byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
        join.Write(bytes.AsSpan(NetHeader.Size));
        return bytes;
    }

    private static WorldEvent FlagEvent(uint id, uint match, uint phase, WorldSignalKind kind)
        => new(id, 40, match, phase, WorldSubjectKind.Flag, kind, 255, 1,
            new CombatActor(0, 10, 1), Vector3.Zero);

    private static LagCompensationState PlayerState(ulong connection, uint life) => new()
    {
        Slot = 0,
        ConnectionId = connection,
        LifeId = life,
        Hunter = Hunter.Samus,
        Alive = true,
        Position = Vector3.Zero,
        Facing = -Vector3.UnitZ,
        SpherePosition = Vector3.UnitY,
        SphereRadius = 0.5f,
        MinPickupHeight = 0.2f,
        MaxPickupHeight = 1.8f
    };

    private static SnapshotPlayer Snapshot(float x, ulong connection = 1, uint life = 1) => new()
    {
        Slot = 0,
        ConnectionId = connection,
        Life = life,
        Health = 100,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
        Position = new Vector3(x, 0, 0),
        Aim = Vector3.UnitZ,
        Facing = Vector3.UnitZ,
        AvailableWeapons = 1
    };

    private static void Add(SnapshotInterpolation history, uint tick, SnapshotPlayer player)
        => Assert.True(history.Add(new SnapshotPacket(tick, tick, 1, 0, false, 0, 0),
            new[] { player }, tick));
}
