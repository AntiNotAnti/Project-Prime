using System;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReplayStateTests
{
    [Theory]
    [InlineData(.25, 25)] [InlineData(.5, 50)] [InlineData(1, 100)] [InlineData(2, 200)] [InlineData(4, 400)]
    public void RatesConserveFixedTicksAndPauseDoesNotAccumulateCatchup(double rate, int expected)
    {
        var transport = new ReplayTransport { Rate = rate };
        int ticks = 0;
        for (int i = 0; i < 100; i++) ticks += transport.TakeSteps();
        Assert.Equal(expected, ticks);
        transport.Paused = true;
        for (int i = 0; i < 1000; i++) Assert.Equal(0, transport.TakeSteps());
        transport.Step(); Assert.Equal(1, transport.TakeSteps()); Assert.Equal(0, transport.TakeSteps());
        transport.Paused = false;
        for (int i = 0; i < 100; i++) ticks += transport.TakeSteps();
        Assert.Equal(expected * 2, ticks);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.Rate = double.NaN);
    }

    [Fact]
    public void FeedbackCheckpointRestoresEveryByteAndDeduplicatesHistoricalDamageAndKills()
    {
        var source = new CombatFeedback(); var world = new WorldFeedback();
        var roster = new[] { new NetRosterEntry(0, 100, Hunter.Samus, 0, "Local"), new NetRosterEntry(1, 200, Hunter.Trace, 1, "Original") };
        var enemy = new CombatActor(1, 200, 1);
        CombatEvent damage = default; KillEvent kill = default;
        for (uint life = 1; life <= 20; life++)
        {
            var local = new CombatActor(0, 100, life);
            source.Bind(1, local, roster, life * 60, 1);
            damage = new CombatEvent(life * 2, life * 60, 1, CombatEventKind.Damage, 0, 0,
                enemy, local, 0, 20, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
            kill = new KillEvent(life * 2 + 1, life * 60, 1, 1, enemy, local, 255, 0,
                ImmutableArray<CombatActor>.Empty, KillSourceKind.Bomb);
            source.Process(damage); source.Process(kill);
        }
        world.Bind(1, 1);
        byte[] checkpoint = ReplayFeedbackState.Capture(source, world);
        var restored = new CombatFeedback(); var restoredWorld = new WorldFeedback();
        Assert.True(ReplayFeedbackState.Restore(checkpoint, restored, restoredWorld));
        Assert.Equal(checkpoint, ReplayFeedbackState.Capture(restored, restoredWorld));
        Assert.Equal(16, restored.Recaps.Count);
        Assert.Equal("Bomb  20 damage", restored.Recaps[15].Final);
        Assert.False(restored.Process(damage)); Assert.False(restored.Process(kill));
        Assert.Equal(checkpoint, ReplayFeedbackState.Capture(restored, restoredWorld));
        // A malformed checkpoint cannot partially clear the target's archive, names or ID windows.
        for (int length = 0; length < checkpoint.Length; length += 101)
        {
            Assert.False(ReplayFeedbackState.Restore(checkpoint.AsSpan(0, length), restored, restoredWorld));
            Assert.Equal(checkpoint, ReplayFeedbackState.Capture(restored, restoredWorld));
        }
        byte[] trailing = new byte[checkpoint.Length + 1]; checkpoint.CopyTo(trailing, 0);
        Assert.False(ReplayFeedbackState.Restore(trailing, restored, restoredWorld));
        Assert.Equal(checkpoint, ReplayFeedbackState.Capture(restored, restoredWorld));
    }
}
