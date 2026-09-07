using MphRead.Combat;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class WorldFeedbackAnnouncementTests
{
    private static readonly CombatActor Local = new(0, 100, 1);

    [Fact]
    public void PickupRespawnAnnouncementIsOptInAndMajorOnly()
    {
        WorldEvent major = Respawn(1, ItemType.DoubleDamage);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);

        Assert.True(feedback.Process(major, Local, 10));
        Assert.Empty(feedback.Message);
        Assert.Equal(0u, feedback.Sequence);

        Assert.True(feedback.Process(major with { Id = 2 }, Local, 20, pickupRespawnAnnouncements: true));
        Assert.Equal("MAJOR PICKUP AVAILABLE", feedback.Message);
        Assert.Equal(WorldSignalKind.PickupRespawned, feedback.LastKind);
        Assert.Equal(20u, feedback.Tick);
    }

    [Theory]
    [InlineData((uint)0)]
    [InlineData((uint)1)]
    [InlineData((uint)2)]
    [InlineData((uint)4)]
    [InlineData((uint)5)]
    public void OrdinaryPickupRespawnDoesNotAnnounce(uint item)
    {
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        Assert.True(feedback.Process(Respawn(1, (ItemType)item), Local, 10, pickupRespawnAnnouncements: true));
        Assert.Empty(feedback.Message);
        Assert.Equal(0u, feedback.Sequence);
    }

    [Fact]
    public void OvertimeAndMatchPointEventsProduceMessages()
    {
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent overtime = new(1, 50, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.OvertimeStarted, 255, 0, CombatActor.None, Vector3.Zero, A: 1);
        WorldEvent matchPoint = new(2, 51, 1, 1, WorldSubjectKind.Match,
            WorldSignalKind.MatchPoint, 0, 0, CombatActor.None, Vector3.Zero);

        Assert.True(feedback.Process(overtime, Local, 60));
        Assert.Equal("OVERTIME", feedback.Message);
        Assert.True(feedback.Process(matchPoint, Local, 61));
        Assert.Equal("MATCH POINT", feedback.Message);
        Assert.Equal(2u, feedback.Sequence);
    }

    private static WorldEvent Respawn(uint id, ItemType item) => new(id, 10, 1, 1,
        WorldSubjectKind.Spawner, WorldSignalKind.PickupRespawned, 255, 7, CombatActor.None,
        new Vector3(4, 5, 6), A: (uint)item);
}
