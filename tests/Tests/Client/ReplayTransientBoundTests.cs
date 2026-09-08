using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class ReplayTransientBoundTests
{
    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSingleAndLinkedLockjawBombsCannotOutliveReplayWarmup(bool linked)
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"));
        Scene scene = simulation.Scene;
        scene.Match.Phase = MatchPhase.Playing;
        scene.StepHeadlessFrame(advanceMatch: false);
        PlayerEntity owner = scene.Players[0]; owner.ServerActivate(100, Hunter.Sylux, 0);
        owner.Position = new Vector3(0, 20000, 0);
        var first = Assert.IsType<BombEntity>(BombEntity.Spawn(owner, Matrix4.CreateTranslation(0, 10000, 0), scene));
        if (!first.Initialized) first.Initialize();
        owner.SyluxBombs[0] = first; first.BombIndex = 0; owner.SyluxBombCount = 1;
        BombEntity? second = null;
        if (linked)
        {
            second = Assert.IsType<BombEntity>(BombEntity.Spawn(owner, Matrix4.CreateTranslation(1, 10000, 0), scene));
            if (!second.Initialized) second.Initialize();
            owner.SyluxBombs[1] = second; second.BombIndex = 1; owner.SyluxBombCount = 2;
        }
        Assert.Equal(1800, first.Countdown);
        if (second != null) Assert.Equal(1800, second.Countdown);
        bool firstAlive = true, secondAlive = second != null;
        int steps = 0;
        while (firstAlive || secondAlive)
        {
            Assert.True(++steps <= DemoPlayback.TransientWarmupTicks);
            if (firstAlive) firstAlive = first.Process();
            if (secondAlive) secondAlive = second!.Process();
        }
        Assert.Equal(1801, steps);
    }
}
