using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class AimAssistSliceBTests
{
    [Fact]
    public void EscapeIntentCoversAxesTangentAndInversion()
    {
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(1, 0)), 5);
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(0, 1), new Vector2(1, 0)), 5);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(-1, 0), new Vector2(1, 0)), .99f, 1f);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(1, 0), invertX: true), .99f, 1f);
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(-1, 0), invertX: true), 5);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(0, -1), new Vector2(0, -1), invertY: true), .99f, 1f);
    }

    [Fact]
    public void EscapeIntentSmoothlyRemovesPullAndFriction()
    {
        Vector2 toward = PlayerAimAssist.ApplyAssistance(
            new Vector2(1, 0), new Vector2(1, 0), 4, 1, 1, 1f / 60f);
        Vector2 away = PlayerAimAssist.ApplyAssistance(
            new Vector2(-1, 0), new Vector2(1, 0), 1, 1, 1, 1f / 60f);

        Assert.True(toward.X > 0 && toward.X < 1);
        Assert.Equal(-1, away.X, 5);
        Assert.Equal(1, PlayerAimAssist.FrictionMultiplier(1, 1, 1), 5);
        Assert.InRange(PlayerAimAssist.FrictionMultiplier(1, 1, .5f),
            PlayerAimAssist.FrictionMultiplier(1, 1), 1);
    }

    [Fact]
    public void AssistTargetUsesCurrentActiveCollisionCenter()
    {
        using var scene = new Scene();
        PlayerEntity player = scene.Players[0];
        player.Position = new Vector3(4, 5, 6);
        player._volume = new CollisionVolume(new Vector3(4, 7, 6), .5f);

        Assert.Equal(new Vector3(4, 7, 6), player.ModAssistAimTarget);
        Assert.NotEqual(player.ModAimTarget, player.ModAssistAimTarget);
    }
}
