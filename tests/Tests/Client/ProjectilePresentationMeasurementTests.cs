using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class ProjectilePresentationMeasurementTests
{
    private static readonly CombatActor Actor = new(2, 0x1234UL, 7);

    [Fact]
    public void MatchesAuthoritativeIdentityAndMeasuresPositionAndAngle()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 41, weapon: 2,
            Vector3.Zero, Vector3.UnitZ, visualCount: 2));

        CombatEvent authoritative = Shot(id: 9, command: 41, weapon: 2,
            position: Vector3.UnitX, direction: Vector3.UnitX);
        Assert.True(measurement.RecordAuthoritativeShot(authoritative));
        Assert.False(measurement.RecordAuthoritativeShot(authoritative));

        var metrics = measurement.Metrics;
        Assert.Equal(1, metrics.PredictedShotCreated);
        Assert.Equal(1, metrics.AuthoritativeShotObserved);
        Assert.Equal(1, metrics.AuthoritativeMissileShotObserved);
        Assert.Equal(0, metrics.AuthoritativeJudicatorShotObserved);
        Assert.Equal(1, metrics.AuthoritativeShotMatched);
        Assert.Equal(0, metrics.AdoptionCandidateMiss);
        Assert.Equal(1d, metrics.VisualCorrectionDistance, precision: 5);
        Assert.Equal(90d, metrics.VisualCorrectionAngle, precision: 5);
        Assert.Equal(1, metrics.CorrectionSamples);
        Assert.Equal(1, metrics.CorrectionAngleSamples);
        Assert.Equal(1d, metrics.MatchRate, precision: 5);
    }

    [Fact]
    public void SuppressedLocalEchoDoesNotCountAsVisualDuplicate()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 11, weapon: 1,
            Vector3.Zero, Vector3.UnitZ));
        CombatEvent authoritative = Shot(id: 1, command: 11, weapon: 1,
            position: Vector3.Zero, direction: Vector3.UnitZ);
        Assert.True(measurement.RecordAuthoritativeShot(authoritative));

        Assert.False(measurement.ObserveAuthoritativeVisual(authoritative, visualWillSpawn: false));
        Assert.Equal(0, measurement.Metrics.VisualDuplicate);
    }

    [Fact]
    public void SpawnedAuthoritativeEchoCountsOneDuplicateOnly()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 12, weapon: 1,
            Vector3.Zero, Vector3.UnitZ));
        CombatEvent authoritative = Shot(id: 2, command: 12, weapon: 1,
            position: Vector3.Zero, direction: Vector3.UnitZ);
        Assert.True(measurement.RecordAuthoritativeShot(authoritative));

        Assert.True(measurement.ObserveAuthoritativeVisual(authoritative, visualWillSpawn: true));
        Assert.False(measurement.ObserveAuthoritativeVisual(authoritative, visualWillSpawn: true));
        Assert.Equal(1, measurement.Metrics.VisualDuplicate);
    }

    [Fact]
    public void RejectedAndUnmatchedCandidatesAreCountedOnceAtBoundedExpiry()
    {
        var measurement = new ProjectilePresentationMeasurement(capacity: 4, windowFrames: 3);
        measurement.SetContext(9, Actor);
        Assert.True(measurement.RecordPredictedShot(Actor, 20, weapon: 1,
            Vector3.Zero, Vector3.UnitZ));
        measurement.Advance(); measurement.Advance(); measurement.Advance();
        Assert.Equal(1, measurement.Metrics.AdoptionCandidateMiss);
        Assert.Equal(0, measurement.Metrics.PendingCandidates);

        CombatEvent missingPrediction = Shot(id: 3, command: 21, weapon: 1,
            position: Vector3.Zero, direction: Vector3.UnitZ);
        Assert.False(measurement.RecordAuthoritativeShot(missingPrediction));
        Assert.False(measurement.RecordAuthoritativeShot(missingPrediction));
        Assert.Equal(2, measurement.Metrics.AdoptionCandidateMiss);
    }

    [Fact]
    public void ContextChangeDropsOldLifeAndMatchCandidatesWithoutAttribution()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 30, weapon: 1,
            Vector3.Zero, Vector3.UnitZ));

        CombatActor replacement = new(2, Actor.ConnectionId + 1, 1);
        measurement.SetContext(10, replacement);
        Assert.False(measurement.RecordAuthoritativeShot(Shot(id: 4, command: 30, weapon: 1,
            position: Vector3.Zero, direction: Vector3.UnitZ, actor: Actor)));
        Assert.Equal(0, measurement.Metrics.PredictedShotCreated);
        Assert.Equal(0, measurement.Metrics.AuthoritativeShotMatched);
        Assert.Equal(0, measurement.Metrics.AdoptionCandidateMiss);
        Assert.Equal(replacement, measurement.LocalActor);
        Assert.Equal((uint)10, measurement.MatchId);
    }

    [Fact]
    public void MultishotIsOneCommandIdentityWithBoundedVisualCount()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 51, weapon: 3,
            Vector3.Zero, Vector3.UnitZ, visualCount: 40));
        Assert.False(measurement.RecordPredictedShot(Actor, 51, weapon: 3,
            Vector3.Zero, Vector3.UnitZ, visualCount: 1));
        Assert.True(measurement.RecordAuthoritativeShot(Shot(id: 5, command: 51, weapon: 3,
            position: Vector3.Zero, direction: Vector3.UnitZ)));
        Assert.Equal(1, measurement.Metrics.PredictedShotCreated);
        Assert.Equal(1, measurement.Metrics.AuthoritativeShotMatched);
    }

    [Fact]
    public void WeaponSwitchDoesNotMatchAVisualFromAnotherWeapon()
    {
        var measurement = NewMeasurement();
        Assert.True(measurement.RecordPredictedShot(Actor, 61, weapon: 1,
            Vector3.Zero, Vector3.UnitZ));
        Assert.False(measurement.RecordAuthoritativeShot(Shot(id: 6, command: 61, weapon: 2,
            position: Vector3.Zero, direction: Vector3.UnitZ)));
        Assert.Equal(0, measurement.Metrics.AuthoritativeShotMatched);
        Assert.Equal(1, measurement.Metrics.AdoptionCandidateMiss);
    }

    [Fact]
    public void CountsUniqueAuthoritativeJudicatorWeaponFactsAndClearsWithContext()
    {
        var measurement = NewMeasurement();
        CombatEvent judicator = Shot(id: 7, command: 71, weapon: (byte)BeamType.Judicator,
            position: Vector3.Zero, direction: Vector3.UnitZ);

        Assert.False(measurement.RecordAuthoritativeShot(judicator));
        Assert.False(measurement.RecordAuthoritativeShot(judicator));
        Assert.Equal(1, measurement.Metrics.AuthoritativeShotObserved);
        Assert.Equal(0, measurement.Metrics.AuthoritativeMissileShotObserved);
        Assert.Equal(1, measurement.Metrics.AuthoritativeJudicatorShotObserved);

        measurement.SetContext(10, Actor);
        Assert.Equal(0, measurement.Metrics.AuthoritativeShotObserved);
        Assert.Equal(0, measurement.Metrics.AuthoritativeMissileShotObserved);
        Assert.Equal(0, measurement.Metrics.AuthoritativeJudicatorShotObserved);
    }

    private static ProjectilePresentationMeasurement NewMeasurement()
    {
        var measurement = new ProjectilePresentationMeasurement();
        measurement.SetContext(9, Actor);
        return measurement;
    }

    private static CombatEvent Shot(uint id, uint command, byte weapon,
        Vector3 position, Vector3 direction, CombatActor? actor = null)
        => new(id, 100, command, CombatEventKind.Shot, weapon,
            CombatEventFlags.None, actor ?? Actor, CombatActor.None, 0, 0,
            position, direction, 0, 0, 0);

}
