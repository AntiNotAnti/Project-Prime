using System;
using System.Diagnostics;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// QZ0 translations for long-running multiplayer clocks. These tests use only
/// deterministic synthetic ticks; they never wait for wall-clock time or load
/// external game data.
/// </summary>
public sealed class Qz0ClockRegressionTests
{
    [Fact]
    [Trait("Regression", "QZ0")]
    public void JoinDuringTickWrapProducesOneContinuousClockAndSnapshotTimeline()
    {
        // Prime invariant: admission near uint rollover neither disconnects a
        // valid join nor starts the joined player in a false future epoch.
        var fixture = new WraparoundClockFixture();
        ulong nonce = fixture.NextNonce();
        var accepted = new JoinAcceptedPacket(nonce, 0, 77, fixture.BeforeWrap, 60,
            MatchRules.CreateDefault(MatchMode.Battle, "qz0-wrap"));
        byte[] bytes = new byte[JoinAcceptedPacket.Size];
        accepted.Write(bytes);
        Assert.True(JoinAcceptedPacket.TryRead(bytes, out JoinAcceptedPacket decoded));
        Assert.Equal(fixture.BeforeWrap, decoded.ServerTick);
        Assert.Equal(nonce, decoded.ClientNonce);

        var clock = new NetClock();
        Assert.True(clock.Observe(fixture.SentAt, fixture.SentAt + fixture.RoundTrip,
            decoded.ServerTick));
        var snapshots = new SnapshotInterpolation(delayTicks: 0);
        SnapshotPlayer before = fixture.Player(0);
        SnapshotPlayer after = fixture.Player(6);
        Assert.True(snapshots.Add(new SnapshotPacket(fixture.BeforeWrap,
            fixture.BeforeWrap, decoded.MatchId, 0, false, 0, 0), new[] { before }, fixture.SentAt));

        long wrappedSent = fixture.SentAt + System.Diagnostics.Stopwatch.Frequency / 10;
        Assert.True(clock.Observe(wrappedSent, wrappedSent + fixture.RoundTrip, fixture.AfterWrap));
        Assert.True(snapshots.Add(new SnapshotPacket(fixture.AfterWrap,
            fixture.AfterWrap, decoded.MatchId, 0, false, 0, 0), new[] { after }, wrappedSent));
        Assert.True(snapshots.TrySample(0, 4294967299d, out SnapshotPlayer sampled));
        Assert.Equal(6, sampled.Position.X);
        Assert.True(clock.Synchronized);
        Assert.True(clock.EstimateServerTick(wrappedSent) > fixture.BeforeWrap);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void NetClockAcceptsTickWrapWithoutMovingTheEstimatedTimelineBackward()
    {
        // Prime invariant: a uint server-tick wrap is one continuous timeline,
        // not a disconnect or a future-time sample.
        long frequency = Stopwatch.Frequency;
        long firstSent = frequency * 10;
        var clock = new NetClock();

        Assert.True(clock.Observe(firstSent, firstSent + frequency / 60, UInt32.MaxValue - 1));
        double before = clock.EstimateServerTick(firstSent + frequency);
        Assert.True(clock.Observe(firstSent + frequency, firstSent + frequency + frequency / 60, 2));
        double after = clock.EstimateServerTick(firstSent + frequency * 2);

        Assert.True(after > before);
        Assert.True(clock.Synchronized);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void NetClockRejectsAStaleReplyAfterTheEpochHasAdvanced()
    {
        // Prime invariant: delayed ping replies cannot move the client clock
        // back into an already-completed tick epoch.
        long frequency = Stopwatch.Frequency;
        long sent = frequency * 20;
        var clock = new NetClock();

        Assert.True(clock.Observe(sent, sent + frequency / 60, UInt32.MaxValue - 4));
        Assert.True(clock.Observe(sent + frequency, sent + frequency + frequency / 60, 3));
        Assert.False(clock.Observe(sent + frequency * 2, sent + frequency * 2 + frequency / 60, UInt32.MaxValue - 20));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void SnapshotInterpolationKeepsWrappedSnapshotsInOneTimeline()
    {
        // Prime invariant: snapshots around uint wrap interpolate in order and
        // preserve the displayed view tick modulo the wire representation.
        var history = new SnapshotInterpolation();
        Add(history, UInt32.MaxValue - 4, Player(0));
        Add(history, 5, Player(10));

        Assert.True(history.TrySample(0, 6, out SnapshotPlayer wrapped));
        Assert.True(history.TrySample(0, 4294967302d, out SnapshotPlayer continuous));
        Assert.Equal(wrapped.Position, continuous.Position);
        Assert.Equal(5, wrapped.Position.X);
        Assert.True(history.TryPreparePresentation(6.75, out SnapshotPresentation presentation));
        Assert.True(history.MarkPresented(presentation));
        Assert.True(history.TryCaptureViewTick(out uint view));
        Assert.Equal(0u, view);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void SnapshotInterpolationRejectsAnOldPacketAfterTickWrap()
    {
        // Prime invariant: an old snapshot cannot re-open a prior history slot
        // after the stream has crossed the uint boundary.
        var history = new SnapshotInterpolation();
        Add(history, UInt32.MaxValue - 4, Player(0));
        Add(history, 5, Player(10));

        Assert.False(history.Add(new SnapshotPacket(UInt32.MaxValue, UInt32.MaxValue, 1, 0, false, 0, 0),
            new[] { Player(100) }, 7));
        Assert.Equal(2, history.Count);
        Assert.True(history.TrySample(0, 6, out SnapshotPlayer state));
        Assert.Equal(5, state.Position.X);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ReceiveWindowAcknowledgementsRemainCorrectAcrossWrap()
    {
        // Prime invariant: reliable ACK state describes the previous 32 packets
        // even when the newest sequence is zero.
        var window = new ReceiveWindow();
        Assert.Equal(ReceiveResult.Newest, window.Record(UInt32.MaxValue - 1));
        Assert.Equal(ReceiveResult.Newest, window.Record(0));
        Assert.Equal(ReceiveResult.OutOfOrder, window.Record(UInt32.MaxValue));
        Assert.Equal(ReceiveResult.Duplicate, window.Record(UInt32.MaxValue));
        Assert.True(ReceiveWindow.IsAcknowledged(UInt32.MaxValue, window.Ack, window.AckBits));
        Assert.True(ReceiveWindow.IsAcknowledged(UInt32.MaxValue - 1, window.Ack, window.AckBits));
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ReliableEventIdsCrossWrapAndDeliverExactlyOnce()
    {
        // Prime invariant: reliable event identity is serial arithmetic; a
        // retransmitted wrapped event is a duplicate, never a second cue.
        var sender = new ReliableChannel(UInt32.MaxValue - 1);
        var receiver = new ReliableChannel();
        uint[] ids = new uint[4];
        for (int i = 0; i < ids.Length; i++)
        {
            Assert.True(sender.TryEnqueue(ReliableEventType.WorldEvent, new byte[] { (byte)i }, out ids[i]));
            Assert.True(receiver.Receive(ids[i]));
            Assert.False(receiver.Receive(ids[i]));
        }

        Assert.Equal(new[] { UInt32.MaxValue - 1, UInt32.MaxValue, 0u, 1u }, ids);
        Assert.Equal(4, sender.PendingCount);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void FixedTickSchedulerBoundsSyntheticMultiDayCatchup()
    {
        // Prime invariant: a long-paused authoritative worker catches up by a
        // bounded number of fixed ticks and records dropped work explicitly.
        const long frequency = 1_000_000;
        const long start = frequency * 123;
        var scheduler = new FixedTickScheduler(start, frequency);
        long sevenDays = frequency * 7 * 24 * 60 * 60;

        Assert.Equal(FixedTickScheduler.MaxCatchUp, scheduler.TakeDue(start + sevenDays));
        Assert.True(scheduler.DroppedTicks > 0);
        Assert.Equal(1, scheduler.Overloads);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void LagCompensationWrapKeepsRewindWithinTheServerBudget()
    {
        // Prime invariant: a wrapped client view hint cannot request an
        // unbounded historical rewind or classify a future tick as current.
        LagCompensationTime result = LagCompensationPolicy.ResolveTick(3, UInt32.MaxValue - 2, 250);
        Assert.Equal(UInt32.MaxValue - 2, result.Tick);
        Assert.Equal(6u, result.RewindTicks);
        Assert.InRange(result.RewindTicks, 0u, LagCompensationPolicy.MaxRewindTicks);
        Assert.Equal(result.RewindTicks, unchecked(3u - result.Tick));
    }

    private static SnapshotPlayer Player(float x, byte slot = 0) => new()
    {
        Slot = slot,
        ConnectionId = 1,
        Life = 1,
        Health = 100,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
        Position = new Vector3(x, 0, 0),
        Speed = new Vector3(2, 0, 0),
        Aim = Vector3.UnitZ,
        Facing = Vector3.UnitZ,
        AvailableWeapons = 1
    };

    private static void Add(SnapshotInterpolation history, uint tick, SnapshotPlayer player)
        => Assert.True(history.Add(new SnapshotPacket(tick, tick, 1, 0, false, 0, 0),
            new[] { player }, tick));
}
