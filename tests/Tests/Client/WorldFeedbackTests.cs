using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;
namespace MphRead.Tests.Client;
public class WorldFeedbackTests
{
    [Fact]
    public void EpochAndIdentityFencesPreventStaleOrForeignPickupMessages()
    {
        var local = new CombatActor(0, 100, 1);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        var value = new WorldEvent(1, 10, 1, 1, WorldSubjectKind.Item, WorldSignalKind.PickupConsumed, 255, 1, local, Vector3.Zero);
        Assert.True(feedback.Process(value, local, 20));
        Assert.Equal("PICKUP ACQUIRED", feedback.Message);
        Assert.Equal(20u, feedback.Tick);
        Assert.False(feedback.Process(value, local, 21));
        feedback.Bind(1, 2);
        Assert.False(feedback.Process(value, local, 22));
        Assert.True(feedback.Process(value with { PhaseRevision = 2, Actor = new CombatActor(0, 200, 1) }, local, 23));
        Assert.Empty(feedback.Message);
        Assert.True(feedback.Process(value with { Id = 2, PhaseRevision = 2 }, local, 24));
        Assert.Equal(2u, feedback.Sequence);
        feedback.Bind(2, 1);
        Assert.False(feedback.Process(value, local, 25));
    }
}
