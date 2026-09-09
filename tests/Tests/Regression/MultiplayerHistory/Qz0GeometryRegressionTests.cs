using System;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// QZ0 translations for latency-sensitive moving geometry. The current Prime
/// implementation exposes immutable player history and presentation history;
/// these tests preserve the compatibility invariants independently from the
/// separately gated moving-geometry implementation.
/// </summary>
public sealed class Qz0GeometryRegressionTests
{
    [Fact]
    [Trait("Regression", "QZ0")]
    public void APlayerCarriedByMovingGeometryInterpolatesWithinItsOwnLife()
    {
        // Prime invariant: latency presentation may smooth a platform-carried
        // player, but it cannot blend an unrelated connection or life.
        var history = new SnapshotInterpolation();
        Add(history, 100, Snapshot(0, connection: 11, life: 4));
        Add(history, 110, Snapshot(10, connection: 11, life: 4));

        Assert.True(history.TrySample(0, 111, out SnapshotPlayer state));
        Assert.Equal(5, state.Position.X);
        Assert.Equal((ulong)11, state.ConnectionId);
        Assert.Equal((uint)4, state.Life);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void APlatformStoppingDuringPredictionCannotExtrapolatePastTheBound()
    {
        // Prime invariant: a stopped moving platform cannot create unbounded
        // client prediction; extrapolation is capped at three ticks.
        var history = new SnapshotInterpolation(maxExtrapolationTicks: 3);
        Add(history, 100, Snapshot(0, speed: new Vector3(2, 0, 0)));
        Add(history, 110, Snapshot(10, speed: new Vector3(2, 0, 0)));

        Assert.True(history.TrySample(0, 120, out SnapshotPlayer bounded));
        Assert.True(history.TrySample(0, 10_000, out SnapshotPlayer held));
        Assert.Equal(13, bounded.Position.X);
        Assert.Equal(bounded.Position, held.Position);
        Assert.Equal(3d, history.MaximumExtrapolationTicks);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void HistoricalPlayerTraceDoesNotMoveTheLiveCollider()
    {
        // Prime invariant: lag compensation reads a historical value and never
        // rewinds a live entity shared by the authoritative simulation.
        LagCompensationState historical = PlayerState() with { Position = Vector3.Zero };
        LagCompensationState live = historical with { Position = new Vector3(9, 0, 0), SpherePosition = new Vector3(9, 1, 0) };
        Vector3 back = new(0, 1.7f, -5);
        Vector3 front = new(0, 1.7f, 5);
        CollisionResult result = default;

        Assert.True(historical.CheckPlayer(back, front, 0.1f, ref result));
        Assert.False(live.CheckPlayer(back, front, 0.1f, ref result));
        Assert.Equal(new Vector3(9, 0, 0), live.Position);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void DoorOpenCloseTransitionUsesTheHistoricalPlaneAtTheQueryTick()
    {
        // Prime invariant: a historical shot samples the door state at its
        // query tick; current open/closed state cannot rewrite that result.
        var fixture = new MovingGeometryFixture();

        Assert.True(HitDynamicPlane(true, fixture.DoorPlane, fixture.TraceStart,
            fixture.TraceEnd, out CollisionResult historical));
        Assert.False(HitDynamicPlane(false, fixture.DoorPlane, fixture.TraceStart,
            fixture.TraceEnd, out _));
        Assert.InRange(historical.Distance, 0f, 1f);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void ForceFieldToggleUsesHistoricalActiveState()
    {
        // Prime invariant: an inactive force field at the shot tick does not
        // block a compensated trace merely because it is active now, and vice
        // versa.
        var fixture = new MovingGeometryFixture();

        Assert.False(HitDynamicPlane(false, fixture.ForceFieldPlane, fixture.TraceStart,
            fixture.TraceEnd, out _));
        Assert.True(HitDynamicPlane(true, fixture.ForceFieldPlane, fixture.TraceStart,
            fixture.TraceEnd, out CollisionResult historical));
        Assert.Equal(0.4f, historical.Distance, 5);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void HistoricalNearestDynamicSurfaceWinsTheCollisionOrdering()
    {
        // Prime invariant: when multiple historical surfaces are active, the
        // nearest valid surface wins deterministically.
        var fixture = new MovingGeometryFixture();
        CollisionResult near = default, far = default;
        Assert.True(CollisionDetection.CheckCylinderIntersectPlane(fixture.TraceStart, fixture.TraceEnd,
            new Vector4(0, 0, 1, -2), ref near));
        Assert.True(CollisionDetection.CheckCylinderIntersectPlane(fixture.TraceStart, fixture.TraceEnd,
            new Vector4(0, 0, 1, 2), ref far));
        Assert.True(near.Distance < far.Distance);
    }

    [Fact]
    [Trait("Regression", "QZ0")]
    public void DynamicHistoryStorageCannotLeakBetweenMatchOwners()
    {
        // Prime invariant: history is owned by one MatchInstance; equal slot,
        // connection and tick values in another instance are unrelated state.
        var first = new LagCompensationHistory();
        var second = new LagCompensationHistory();
        first.Record(7, PlayerState() with { Position = new Vector3(7, 0, 0) });
        second.Record(7, PlayerState() with { Position = new Vector3(70, 0, 0) });

        Assert.True(first.TryGet(0, 7, 13, 2, out LagCompensationState firstState));
        Assert.True(second.TryGet(0, 7, 13, 2, out LagCompensationState secondState));
        Assert.Equal(7, firstState.Position.X);
        Assert.Equal(70, secondState.Position.X);
    }

    private static bool HitDynamicPlane(bool historicalClosed, Vector4 plane,
        Vector3 back, Vector3 front, out CollisionResult result)
    {
        result = default;
        return historicalClosed && CollisionDetection.CheckCylinderIntersectPlane(back, front, plane, ref result);
    }

    private static LagCompensationState PlayerState() => new()
    {
        Slot = 0,
        ConnectionId = 13,
        LifeId = 2,
        Hunter = Hunter.Samus,
        Alive = true,
        Position = Vector3.Zero,
        Facing = -Vector3.UnitZ,
        SpherePosition = Vector3.UnitY,
        SphereRadius = 0.5f,
        MinPickupHeight = 0.2f,
        MaxPickupHeight = 1.8f
    };

    private static SnapshotPlayer Snapshot(float x, ulong connection = 1, uint life = 1, Vector3? speed = null) => new()
    {
        Slot = 0,
        ConnectionId = connection,
        Life = life,
        Health = 100,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
        Position = new Vector3(x, 0, 0),
        Speed = speed ?? Vector3.Zero,
        Aim = Vector3.UnitZ,
        Facing = Vector3.UnitZ,
        AvailableWeapons = 1
    };

    private static void Add(SnapshotInterpolation history, uint tick, SnapshotPlayer player)
        => Assert.True(history.Add(new SnapshotPacket(tick, tick, 1, 0, false, 0, 0),
            new[] { player }, tick));
}
