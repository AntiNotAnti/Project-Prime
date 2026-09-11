using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class PresentedCollisionFrameTests
{
    private static readonly CombatActor Remote = new(1, 20, 3);
    private static readonly CombatActor Local = new(0, 10, 2);

    [Fact]
    public void PreparedStateIsInvisibleUntilSuccessfulCommit()
    {
        var frame = new PresentedCollisionFrame();
        SnapshotPresentation presentation = new(42.75, 7, 9, SnapshotPresentationMode.Interpolated);
        frame.Begin(presentation);
        frame.Stage(State(Remote, new Vector3(10, 2, 3)));
        Assert.False(frame.TryGet(Remote, out _));

        Assert.True(frame.Commit(presentation));
        Assert.True(frame.TryGet(Remote, out PresentedPlayerCollisionState committed));
        Assert.Equal(new Vector3(10, 2, 3), committed.Position);
        Assert.Equal(42u, committed.PresentedTick);
    }

    [Fact]
    public void AbandonedPrepareCannotReplaceCommittedFrame()
    {
        var frame = new PresentedCollisionFrame();
        SnapshotPresentation first = new(10.1, 1, 2, SnapshotPresentationMode.Interpolated);
        frame.Begin(first);
        frame.Stage(State(Remote, Vector3.Zero));
        Assert.True(frame.Commit(first));

        SnapshotPresentation abandoned = new(11.9, 1, 2, SnapshotPresentationMode.Interpolated);
        frame.Begin(abandoned);
        frame.Stage(State(Remote, new Vector3(100, 0, 0)));
        frame.AbortPending();

        Assert.True(frame.TryGet(Remote, out PresentedPlayerCollisionState state));
        Assert.Equal(Vector3.Zero, state.Position);
        Assert.Equal(10u, state.PresentedTick);
    }

    [Fact]
    public void ShotMeasurementFencesLifecycleAndRecordsMeaningfulTickAndBuckets()
    {
        var frame = new PresentedCollisionFrame();
        SnapshotPresentation presentation = new(100.5, 8, 4, SnapshotPresentationMode.Interpolated);
        frame.Begin(presentation);
        frame.Stage(State(Remote, Vector3.Zero));
        Assert.True(frame.Commit(presentation));

        CombatShot shot = new(Local, 7, 101, 100, 0, 0);
        Assert.True(frame.BeginLocalShot(shot));
        frame.RecordShotCandidate(shot, Remote, new Vector3(0.05f, 0, 0), Hunter.Samus,
            teamIndex: 0, active: true, spawned: true, alive: true, altForm: false,
            spectating: false, simulationTick: 101);
        frame.RecordShotCandidate(shot, Remote with { Life = 4 }, new Vector3(1, 0, 0),
            Hunter.Samus, 0, true, true, true, false, false, 101);

        PresentedCollisionMetrics metrics = frame.Metrics;
        Assert.Equal(1, metrics.PresentedPoseSamples);
        Assert.Equal(1, metrics.Minor);
        Assert.Equal(0, metrics.Material);
        Assert.Equal(1, metrics.PresentedPoseUnavailable);
        Assert.Equal(1, metrics.PresentedTickVsShotTickMismatch.Count);
        Assert.Equal(1, metrics.PresentedTickVsSimulationTickMismatch.Count);
    }

    [Fact]
    public void SpeculativeHitDistanceUsesSeparatePopulationAndDeduplicatesShotIdentity()
    {
        var frame = new PresentedCollisionFrame();
        SnapshotPresentation presentation = new(20.25, 2, 1, SnapshotPresentationMode.Interpolated);
        frame.Begin(presentation);
        frame.Stage(State(Remote, new Vector3(0, 0, 0)));
        Assert.True(frame.Commit(presentation));
        CombatShot shot = new(Local, 9, 20, 20, 0, 0);
        Assert.True(frame.BeginLocalShot(shot));
        Assert.False(frame.BeginLocalShot(shot));

        frame.RecordSpeculativeHit(shot, Remote, new Vector3(0, 0.4f, 0), Hunter.Samus,
            0, true, true, true, false, false, 20);
        PresentedCollisionMetrics metrics = frame.Metrics;
        Assert.Equal(1, metrics.SpeculativeHitPresentedPoseDistance.Count);
        Assert.Equal(0.4, metrics.SpeculativeHitPresentedPoseDistance.Mean, precision: 3);
        Assert.Equal(0, metrics.PresentedPoseSamples);
    }

    [Fact]
    public void ReportSnapshotSurvivesLiveEpochReset()
    {
        var frame = new PresentedCollisionFrame();
        SnapshotPresentation presentation = new(20.25, 2, 1,
            SnapshotPresentationMode.Interpolated);
        frame.Begin(presentation);
        frame.Stage(State(Remote, Vector3.Zero));
        Assert.True(frame.Commit(presentation));
        CombatShot shot = new(Local, 9, 20, 20, 0, 0);
        Assert.True(frame.BeginLocalShot(shot));
        frame.RecordShotCandidate(shot, Remote, new Vector3(0.5f, 0, 0),
            Hunter.Samus, 0, true, true, true, false, false, 20);

        PresentedCollisionMetricsSnapshot snapshot
            = PresentedCollisionMetricsSnapshot.Capture(frame.Metrics);
        frame.Clear();

        Assert.Equal(1, snapshot.PresentedPoseSamples);
        Assert.Equal(1, snapshot.PresentedPoseErrorPercentiles.Count);
        Assert.Equal(0.5, snapshot.PresentedPoseErrorPercentiles.P95, precision: 3);
        Assert.Equal(0, frame.Metrics.PresentedPoseSamples);
    }

    private static SnapshotPlayer State(CombatActor actor, Vector3 position)
        => new()
        {
            Slot = actor.Slot,
            ConnectionId = actor.ConnectionId,
            Life = actor.Life,
            Hunter = Hunter.Samus,
            TeamIndex = 0,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
            Health = 100,
            Position = position,
            Aim = Vector3.UnitZ,
            Facing = Vector3.UnitZ
        };
}
