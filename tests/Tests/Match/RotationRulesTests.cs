using System;
using System.IO;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class RotationRulesTests
{
    [Fact]
    public void RotationEntryUsesModeDefaultsWithoutCouplingObjectiveAndPointGoals()
    {
        MatchRules defender = new RotationEntry
        {
            RoomKey = "MP5 ARCTERRA",
            Mode = GameMode.Defender,
            TimeLimit = 900,
            PointGoal = 17
        }.ToMatchRules();

        Assert.Equal(MatchMode.Defender, defender.Mode);
        Assert.Equal(TimeSpan.FromSeconds(900), defender.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(90), defender.ObjectiveTimeGoal);
        Assert.Equal(17, defender.ScoreGoal);
        Assert.Equal(0, defender.StartingLives);

        MatchRules survival = new RotationEntry
        {
            RoomKey = "MP2 ACCELERATOR",
            Mode = GameMode.Survival,
            TimeLimit = 900,
            PointGoal = 3
        }.ToMatchRules();

        Assert.Equal(TimeSpan.FromSeconds(900), survival.TimeLimit);
        Assert.Equal(0, survival.ScoreGoal);
        Assert.Equal(3, survival.StartingLives);
        Assert.Null(survival.ObjectiveTimeGoal);
    }

    [Fact]
    public void RotationEntryHonorsExplicitZeroObjectiveAndHostingOptions()
    {
        MatchRules rules = new RotationEntry
        {
            RoomKey = "MP5 ARCTERRA",
            Mode = GameMode.Defender,
            TimeLimit = 0,
            PointGoal = 11,
            ObjectiveTimeGoal = 0
        }.ToMatchRules(maxPlayers: 4, friendlyFire: true);

        Assert.Null(rules.TimeLimit);
        Assert.Equal(TimeSpan.Zero, rules.ObjectiveTimeGoal);
        Assert.Equal(11, rules.ScoreGoal);
        Assert.Equal(4, rules.MaxPlayers);
        Assert.True(rules.FriendlyFire);
    }

    [Fact]
    public void RotationFileLoadsBothLegacyFourAndObjectiveFiveColumnEntries()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path,
                "# comments and blank lines are allowed\n\n"
                + "MP1 SANCTORUS | Battle | 7 | 7\n"
                + "MP5 ARCTERRA | Defender | 15 | 17 | 45\n");

            MapRotation rotation = MapRotation.Load(path);

            Assert.Equal(2, rotation.Entries.Count);
            Assert.Equal("MP1 SANCTORUS", rotation.Entries[0].RoomKey);
            Assert.Null(rotation.Entries[0].ObjectiveTimeGoal);
            Assert.Equal(GameMode.Defender, rotation.Entries[1].Mode);
            Assert.Equal(45, rotation.Entries[1].ObjectiveTimeGoal);
            Assert.Equal(TimeSpan.FromSeconds(45), rotation.Entries[1].ToMatchRules().ObjectiveTimeGoal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RotationEntryRejectsInvalidDurationsModesAndGoals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            TimeLimit = -1
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            TimeLimit = float.NaN
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            TimeLimit = float.PositiveInfinity
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            TimeLimit = float.MaxValue
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            ObjectiveTimeGoal = -1
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            ObjectiveTimeGoal = float.NaN
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            PointGoal = -1
        }.ToMatchRules());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotationEntry
        {
            Mode = GameMode.SinglePlayer
        }.ToMatchRules());
    }

    [Theory]
    [InlineData("MP1 SANCTORUS | SinglePlayer | 7 | 7")]
    [InlineData("MP1 SANCTORUS | Battle | NaN | 7")]
    [InlineData("MP1 SANCTORUS | Battle | Infinity | 7")]
    [InlineData("MP1 SANCTORUS | Battle | -1 | 7")]
    [InlineData("MP1 SANCTORUS | Battle | 200000000 | 7")]
    [InlineData("MP1 SANCTORUS | Battle | 7 | -1")]
    [InlineData("MP1 SANCTORUS | Battle | 7 | 2147483648")]
    [InlineData("MP1 SANCTORUS | Defender | 15 | 7 | -1")]
    [InlineData("MP1 SANCTORUS | Defender | 15 | 7 | Infinity")]
    public void RotationFileRejectsInvalidRulesWithTheSourceLine(string invalidEntry)
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "# header\nMP3 PROVING GROUND | Battle | 7 | 7\n" + invalidEntry + "\n");

            ProgramException error = Assert.Throws<ProgramException>(() => MapRotation.Load(path));

            Assert.Contains("line 3", error.Message);
            Assert.Contains(path, error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(),
        "project-prime-rotation-rules-" + Guid.NewGuid().ToString("N") + ".txt");
}
