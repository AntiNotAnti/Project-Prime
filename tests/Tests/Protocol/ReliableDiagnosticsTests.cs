using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class ReliableDiagnosticsTests
{
    [Fact]
    public void InvalidAdmissionsAreCountedWithoutChangingPendingState()
    {
        var channel = new ReliableChannel();
        Assert.False(channel.TryEnqueue(ReliableEventType.Combat, new byte[ReliableChannel.MaxPayloadSize + 1], out _));
        Assert.Equal(ReliableAdmissionFailure.OversizedPayload, channel.LastAdmissionFailure);
        Assert.False(channel.TryEnqueue((ReliableEventType)255, default, out _));
        Assert.Equal(ReliableAdmissionFailure.InvalidType, channel.LastAdmissionFailure);
        Assert.Equal(1, channel.OversizedPayloadRejections);
        Assert.Equal(1, channel.InvalidTypeRejections);
        Assert.Equal(0, channel.PendingCount);
        Assert.Equal(0u, channel.OldestPendingSpan);
        Assert.Equal(0, channel.PendingHighWater);
        Assert.True(channel.TryEnqueue(ReliableEventType.Combat, default, out uint id));
        Assert.Equal(0u, id);
        Assert.Equal(ReliableAdmissionFailure.None, channel.LastAdmissionFailure);
    }

    [Fact]
    public void FullQueueReportsCapacityAndHighWaterSurvivesCancellation()
    {
        var channel = new ReliableChannel();
        for (int i = 0; i < ReliableChannel.Capacity; i++)
            Assert.True(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.False(channel.CanEnqueue);
        Assert.Equal(0, channel.CapacityRejections); // A read is not an admission attempt.
        Assert.False(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.Equal(ReliableAdmissionFailure.Capacity, channel.LastAdmissionFailure);
        Assert.Equal(1, channel.CapacityRejections);
        Assert.Equal(0, channel.IdSpanRejections);
        Assert.Equal(32u, channel.OldestPendingSpan);
        channel.CancelPendingExceptWelcome();
        Assert.Equal(0, channel.PendingCount);
        Assert.Equal(0u, channel.OldestPendingSpan);
        Assert.Equal(ReliableChannel.Capacity, channel.PendingHighWater);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(UInt32.MaxValue - 10)]
    public void LostOldEventReportsSpanWithOnlyOnePendingMessage(uint firstId)
    {
        var channel = new ReliableChannel(firstId);
        Assert.True(channel.TryEnqueue(ReliableEventType.Welcome, default, out uint oldest));
        for (uint sequence = 1; sequence < ReliableChannel.EventWindowCapacity; sequence++)
        {
            Assert.True(channel.TryEnqueue(ReliableEventType.Combat, default, out uint id));
            channel.MarkSent(id, sequence, sequence);
            channel.Acknowledge(sequence, 0);
        }
        Assert.Equal(1, channel.PendingCount);
        Assert.Equal(2, channel.PendingHighWater);
        Assert.Equal((uint)ReliableChannel.EventWindowCapacity, channel.OldestPendingSpan);
        Assert.False(channel.CanEnqueue);
        Assert.False(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.Equal(ReliableAdmissionFailure.IdSpan, channel.LastAdmissionFailure);
        Assert.Equal(1, channel.IdSpanRejections);
        Assert.Equal(0, channel.CapacityRejections);
        channel.MarkSent(oldest, 100, 100);
        channel.Acknowledge(100, 0);
        Assert.Equal(0u, channel.OldestPendingSpan);
        Assert.True(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.Equal(ReliableAdmissionFailure.None, channel.LastAdmissionFailure);
    }
}
