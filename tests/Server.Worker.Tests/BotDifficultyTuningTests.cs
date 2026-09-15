using System;
using System.Linq;
using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class BotDifficultyTuningTests
{
    [Fact]
    public void RetailDifficultyBandsRemainBaseline()
    {
        var expected = new[]
        {
            new PlayerEntity.PlayerAiData.DifficultyTuning(
                AwarenessChecksPerFrame: 1,
                TurnDegreesPerTick: 5,
                ExactTurnDotThreshold: 255 / 256f,
                AimPredictionFrames: 15,
                AimMotionErrorScale: 5,
                AimMinimumError: 0.25f,
                AimDistanceErrorDivisor: 2,
                ShotDelayFrames: 60,
                JudicatorChargeDistanceSquared: 9,
                DodgeChanceOneIn: 10),
            new PlayerEntity.PlayerAiData.DifficultyTuning(
                AwarenessChecksPerFrame: 1,
                TurnDegreesPerTick: 10,
                ExactTurnDotThreshold: 4025 / 4096f,
                AimPredictionFrames: 10,
                AimMotionErrorScale: 3.5f,
                AimMinimumError: 0.18f,
                AimDistanceErrorDivisor: 4,
                ShotDelayFrames: 30,
                JudicatorChargeDistanceSquared: 10,
                DodgeChanceOneIn: 10),
            new PlayerEntity.PlayerAiData.DifficultyTuning(
                AwarenessChecksPerFrame: 1,
                TurnDegreesPerTick: 15,
                ExactTurnDotThreshold: 3956 / 4096f,
                AimPredictionFrames: 7,
                AimMotionErrorScale: 2,
                AimMinimumError: 0.1f,
                AimDistanceErrorDivisor: 9,
                ShotDelayFrames: 15,
                JudicatorChargeDistanceSquared: 11,
                DodgeChanceOneIn: 10),
            new PlayerEntity.PlayerAiData.DifficultyTuning(
                AwarenessChecksPerFrame: 1,
                TurnDegreesPerTick: 20,
                ExactTurnDotThreshold: 3849 / 4096f,
                AimPredictionFrames: 3,
                AimMotionErrorScale: 0.2f,
                AimMinimumError: 0.01f,
                AimDistanceErrorDivisor: 50,
                ShotDelayFrames: 5,
                JudicatorChargeDistanceSquared: 13,
                DodgeChanceOneIn: 10),
            new PlayerEntity.PlayerAiData.DifficultyTuning(
                AwarenessChecksPerFrame: 1,
                TurnDegreesPerTick: 24,
                ExactTurnDotThreshold: 3780 / 4096f,
                AimPredictionFrames: 2,
                AimMotionErrorScale: 0.08f,
                AimMinimumError: 0.005f,
                AimDistanceErrorDivisor: 100,
                ShotDelayFrames: 2,
                JudicatorChargeDistanceSquared: 16,
                DodgeChanceOneIn: 10)
        };

        var actual = Enum.GetValues<BotDifficulty>()
            .Select(PlayerEntity.PlayerAiData.GetDifficultyTuning)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MixedDifficultyDoesNotChangeSharedAwarenessCadence()
    {
        var cadence = Enum.GetValues<BotDifficulty>()
            .Select(difficulty => PlayerEntity.PlayerAiData
                .GetDifficultyTuning(difficulty).AwarenessChecksPerFrame)
            .ToArray();

        Assert.Equal(5, cadence.Length);
        Assert.All(cadence, value => Assert.Equal(1, value));
    }

    [Theory]
    [InlineData(1, 0.0f, 0.0f, true)]
    [InlineData(1, 0.999f, 0.999f, true)]
    [InlineData(0, 0.5f, 0.5f, false)]
    [InlineData(1, -0.001f, 0.5f, false)]
    [InlineData(1, 1.0f, 0.5f, false)]
    [InlineData(1, 0.5f, -0.001f, false)]
    [InlineData(1, 0.5f, 1.0f, false)]
    public void AwarenessRequiresAForwardOnScreenProjection(float w, float x,
        float y, bool expected)
        => Assert.Equal(expected, PlayerEntity.PlayerAiData.IsProjectedOpponentOnScreen(
            w, new Vector2(x, y)));

    [Fact]
    public void TargetChoiceUsesPriorityThenActualCandidateDistance()
    {
        Assert.True(PlayerEntity.PlayerAiData.IsBetterTargetCandidate(
            priority: 2, distanceSquared: 100, bestPriority: 1,
            bestDistanceSquared: 1));
        Assert.True(PlayerEntity.PlayerAiData.IsBetterTargetCandidate(
            priority: 2, distanceSquared: 4, bestPriority: 2,
            bestDistanceSquared: 9));
        Assert.False(PlayerEntity.PlayerAiData.IsBetterTargetCandidate(
            priority: 1, distanceSquared: 1, bestPriority: 2,
            bestDistanceSquared: 100));
        Assert.False(PlayerEntity.PlayerAiData.IsBetterTargetCandidate(
            priority: 2, distanceSquared: 10, bestPriority: 2,
            bestDistanceSquared: 9));
    }
}
