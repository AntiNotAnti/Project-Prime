using System;
using System.Collections.Immutable;
using System.IO;
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

    [Fact]
    public void ReplayCheckpointDoesNotSerializePendingWorldNotices()
    {
        var combat = new CombatFeedback();
        var world = new WorldFeedback();
        var local = new CombatActor(0, 100, 1);
        world.Bind(1, 1);
        WorldEvent capture = new(1, 10, 1, 1, WorldSubjectKind.Flag,
            WorldSignalKind.FlagCaptured, 0, 9, local, Vector3.Zero);
        Assert.True(world.Process(capture, local, 20));
        byte[] checkpoint = ReplayFeedbackState.Capture(combat, world);

        Assert.True(world.TryDequeueNotice(out _));
        Assert.Equal(checkpoint, ReplayFeedbackState.Capture(combat, world));

        var restoredCombat = new CombatFeedback();
        var restoredWorld = new WorldFeedback();
        restoredWorld.Bind(1, 1);
        Assert.True(restoredWorld.Process(capture, local, 30));
        Assert.True(ReplayFeedbackState.Restore(checkpoint, restoredCombat, restoredWorld));
        Assert.False(restoredWorld.TryDequeueNotice(out _));
        Assert.Equal(checkpoint, ReplayFeedbackState.Capture(restoredCombat, restoredWorld));
    }

    [Fact]
    public void FeedbackCheckpointV3RoundTripsMarkerDetailsAndOlderVersionsRemainReadable()
    {
        var source = new CombatFeedback();
        var world = new WorldFeedback();
        var roster = new[]
        {
            new NetRosterEntry(0, 100, Hunter.Samus, 0, "Local"),
            new NetRosterEntry(1, 200, Hunter.Trace, 1, "Original")
        };
        var local = new CombatActor(0, 100, 1);
        var enemy = new CombatActor(1, 200, 1);
        source.Bind(1, local, roster, presentationTick: 100, phaseRevision: 1);
        Assert.True(source.Process(new CombatEvent(1, 100, 1, CombatEventKind.Damage, 0,
            CombatEventFlags.Headshot, local, enemy, 80, 20, Vector3.Zero,
            Vector3.UnitZ, 0, 0, 0)));
        Assert.True(source.Process(new KillEvent(2, 100, 1, 1, local, enemy, 0,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty)));
        world.Bind(1, 1);

        byte[] v3 = ReplayFeedbackState.Capture(source, world);
        Assert.Equal(3, v3[0]);
        var restoredV3 = new CombatFeedback();
        var restoredWorldV3 = new WorldFeedback();
        Assert.True(ReplayFeedbackState.Restore(v3, restoredV3, restoredWorldV3));
        Assert.Equal("HEADSHOT!", restoredV3.State.HeadshotNotice.Text);
        Assert.Equal("YOUR HEADSHOT KILLED Original!", restoredV3.State.KillNotice.Text);
        Assert.Equal(source.State.MarkerPulseSequence, restoredV3.State.MarkerPulseSequence);
        Assert.Equal(source.State.MarkerAudioSequence, restoredV3.State.MarkerAudioSequence);
        Assert.Equal(source.State.MarkerDamage, restoredV3.State.MarkerDamage);
        Assert.Equal(source.State.MarkerFlags, restoredV3.State.MarkerFlags);
        Assert.Equal(v3, ReplayFeedbackState.Capture(restoredV3, restoredWorldV3));

        byte[] v2;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)2);
            source.WriteReplay(writer, includeNotices: true);
            world.WriteReplay(writer);
            v2 = stream.ToArray();
        }
        var restoredV2 = new CombatFeedback();
        Assert.True(ReplayFeedbackState.Restore(v2, restoredV2, new WorldFeedback()));
        Assert.Equal("HEADSHOT!", restoredV2.State.HeadshotNotice.Text);
        Assert.Equal(source.State.Marker, restoredV2.State.MarkerAudioKind);

        // Build the old outer-v1 layout through the compatibility writer. It
        // deliberately omits the new notice fields while retaining every old
        // combat/world field in its original order.
        byte[] v1;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)1);
            source.WriteReplay(writer);
            world.WriteReplay(writer);
            v1 = stream.ToArray();
        }
        var restoredV1 = new CombatFeedback();
        var restoredWorldV1 = new WorldFeedback();
        Assert.True(ReplayFeedbackState.Restore(v1, restoredV1, restoredWorldV1));
        Assert.False(restoredV1.State.HeadshotNotice.IsValid);
        Assert.False(restoredV1.State.KillNotice.IsValid);
    }
}
