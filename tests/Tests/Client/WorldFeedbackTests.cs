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

    [Fact]
    public void NoticesPreserveAcceptedEventOrderAndReceiptTick()
    {
        var local = new CombatActor(0, 100, 1);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);

        WorldEvent health = Pickup(1, ItemType.HealthSmall, local);
        WorldEvent ammo = Pickup(2, ItemType.UASmall, local);
        WorldEvent capture = new(3, 12, 1, 1, WorldSubjectKind.Flag,
            WorldSignalKind.FlagCaptured, 0, 9, local, Vector3.Zero);

        Assert.True(feedback.Process(health, local, 20));
        Assert.True(feedback.Process(ammo, local, 21));
        Assert.True(feedback.Process(capture, local, 22));
        Assert.Equal(3, feedback.PendingNoticeCount);

        AssertNotice(feedback, health, 20);
        AssertNotice(feedback, ammo, 21);
        AssertNotice(feedback, capture, 22);
        Assert.False(feedback.TryDequeueNotice(out _));
    }

    [Fact]
    public void DuplicateAndRemotePickupEventsDoNotCreateLocalNotices()
    {
        var local = new CombatActor(0, 100, 1);
        var remote = new CombatActor(1, 200, 1);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        WorldEvent pickup = Pickup(1, ItemType.HealthSmall, local);

        Assert.True(feedback.Process(pickup, local, 20));
        Assert.False(feedback.Process(pickup, local, 21));
        Assert.True(feedback.Process(pickup with { Id = 2, Actor = remote }, local, 22));
        Assert.Equal(1, feedback.PendingNoticeCount);
        Assert.Equal(1u, feedback.Sequence);
        AssertNotice(feedback, pickup, 20);
        Assert.False(feedback.TryDequeueNotice(out _));
    }

    [Fact]
    public void NoticeOverflowDropsOldestAndKeepsNewestInOrder()
    {
        var local = new CombatActor(0, 100, 1);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);

        for (uint id = 1; id <= WorldFeedback.NoticeCapacity + 8; id++)
        {
            WorldEvent capture = new(id, id, 1, 1, WorldSubjectKind.Flag,
                WorldSignalKind.FlagCaptured, 0, id, local, Vector3.Zero);
            Assert.True(feedback.Process(capture, local, id + 100));
        }

        Assert.Equal(WorldFeedback.NoticeCapacity, feedback.PendingNoticeCount);
        Assert.Equal(8u, feedback.DroppedNotices);
        for (uint id = 9; id <= WorldFeedback.NoticeCapacity + 8; id++)
        {
            Assert.True(feedback.TryDequeueNotice(out WorldFeedbackNotice notice));
            Assert.Equal(id, notice.Event.Id);
            Assert.Equal(id + 100, notice.ReceiptTick);
        }
        Assert.False(feedback.TryDequeueNotice(out _));
    }

    [Fact]
    public void BindingNewPhaseClearsPendingNotices()
    {
        var local = new CombatActor(0, 100, 1);
        var feedback = new WorldFeedback();
        feedback.Bind(1, 1);
        Assert.True(feedback.Process(Pickup(1, ItemType.HealthSmall, local), local, 20));
        Assert.Equal(1, feedback.PendingNoticeCount);

        feedback.Bind(1, 2);

        Assert.Equal(0, feedback.PendingNoticeCount);
        Assert.False(feedback.TryDequeueNotice(out _));
    }

    private static void AssertNotice(WorldFeedback feedback, WorldEvent expected, uint receiptTick)
    {
        Assert.True(feedback.TryDequeueNotice(out WorldFeedbackNotice notice));
        Assert.Equal(expected, notice.Event);
        Assert.Equal(receiptTick, notice.ReceiptTick);
    }

    private static WorldEvent Pickup(uint id, ItemType item, CombatActor actor) => new(id, id, 1, 1,
        WorldSubjectKind.Item, WorldSignalKind.PickupConsumed, 0, id, actor, Vector3.Zero, A: (uint)item);
}
