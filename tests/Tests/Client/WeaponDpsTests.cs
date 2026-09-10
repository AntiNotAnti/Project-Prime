using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class WeaponDpsTests
{
    [Fact]
    public void LethalFrameIsCountedAfterSimulationKilledVictim()
    {
        var measurement = new WeaponDpsMeasurement(99, 40, 600);
        measurement.Observe(0, 40, completedFiringFrames: 17);

        WeaponDpsMeasurementResult result = measurement.Result(999);
        Assert.Equal(99, result.Damage);
        Assert.Equal(1, result.Hits);
        Assert.Equal(17, result.KillFrame);
        Assert.Equal(17, result.FiringFrames);
    }

    [Fact]
    public void OneShotCountsFullDamageAndFreezesDuration()
    {
        var measurement = new WeaponDpsMeasurement(100, 40, 600);
        measurement.Observe(0, 40, completedFiringFrames: 1);
        measurement.Observe(100, 50, completedFiringFrames: 500);
        measurement.Observe(0, 55, completedFiringFrames: 501);

        WeaponDpsMeasurementResult result = measurement.Result(600);
        Assert.Equal(100, result.Damage);
        Assert.Equal(1, result.Hits);
        Assert.Equal(1, result.FiringFrames);
        Assert.Equal(0, result.Healing);
    }

    [Fact]
    public void SurvivorUsesFullRequestedDuration()
    {
        var measurement = new WeaponDpsMeasurement(100, 50, requestedFrames: 120);
        measurement.Observe(80, 50, 30);
        measurement.Observe(75, 50, 120);

        WeaponDpsMeasurementResult result = measurement.Result(120);
        Assert.False(result.Killed);
        Assert.Equal(120, result.FiringFrames);
        Assert.Equal(2, result.Hits);
        Assert.Equal(25, result.Damage);
    }

    [Fact]
    public void HealthIncreasesAreNotDamageAndShooterHealingIsReported()
    {
        var measurement = new WeaponDpsMeasurement(80, 20, requestedFrames: 60);
        measurement.Observe(90, 35, 1);
        measurement.Observe(70, 40, 2);

        WeaponDpsMeasurementResult result = measurement.Result(60);
        Assert.Equal(20, result.Damage);
        Assert.Equal(1, result.Hits);
        Assert.Equal(20, result.Healing);
    }

    [Fact]
    public void RepeatedMeasurementsAreDeterministic()
    {
        static WeaponDpsMeasurementResult Run()
        {
            var measurement = new WeaponDpsMeasurement(100, 30, 90);
            measurement.Observe(75, 35, 10);
            measurement.Observe(25, 35, 20);
            measurement.Observe(0, 40, 30);
            return measurement.Result(90);
        }

        Assert.Equal(Run(), Run());
    }

    [Theory]
    [InlineData("PowerBeam", (int)WeaponDpsMeasurementKind.PowerBeam, BeamType.PowerBeam, null)]
    [InlineData("shock-coil", (int)WeaponDpsMeasurementKind.ShockCoil, BeamType.ShockCoil, null)]
    [InlineData("Lockjaw", (int)WeaponDpsMeasurementKind.Lockjaw, null, BombType.Lockjaw)]
    [InlineData("morph_ball_bomb", (int)WeaponDpsMeasurementKind.MorphBallBomb, null, BombType.MorphBall)]
    [InlineData("Stinglarva", (int)WeaponDpsMeasurementKind.Stinglarva, null, BombType.Stinglarva)]
    public void ParsesOrdinaryContinuousAndBombModes(string text, int kindValue,
        BeamType? beam, BombType? bomb)
    {
        WeaponDpsMeasurementKind kind = (WeaponDpsMeasurementKind)kindValue;
        Assert.True(WeaponDpsSelection.TryParse(text, out WeaponDpsSelection selection));
        Assert.Equal(kind, selection.Kind);
        Assert.Equal(beam, selection.Beam);
        Assert.Equal(bomb, selection.Bomb);
        Assert.Equal(kind == WeaponDpsMeasurementKind.ShockCoil, selection.Continuous);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Enemy")]
    [InlineData("Platform")]
    [InlineData("not-a-weapon")]
    public void InvalidModesFailClosed(string text)
    {
        Assert.False(WeaponDpsSelection.TryParse(text, out _));
    }
}
