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
    public void OrdinaryTrafficCannotConsumeTheCriticalReserve()
    {
        var channel = new ReliableChannel();
        for (int i = 0; i < ReliableChannel.OrdinaryCapacity; i++)
            Assert.True(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.False(channel.CanEnqueue);
        Assert.False(channel.CanEnqueueType(ReliableEventType.Combat));
        Assert.True(channel.CanEnqueueType(ReliableEventType.Kill));
        Assert.Equal(0, channel.CapacityRejections); // A read is not an admission attempt.
        Assert.False(channel.TryEnqueue(ReliableEventType.Combat, default, out _));
        Assert.Equal(ReliableAdmissionFailure.ReservedCapacity, channel.LastAdmissionFailure);
        Assert.Equal(1, channel.CapacityRejections);
        Assert.Equal(1, channel.OrdinaryAdmissionRejections);
        for (int i = 0; i < ReliableChannel.CriticalReserve; i++)
            Assert.True(channel.TryEnqueue(ReliableEventType.Kill, default, out _));
        Assert.Equal(ReliableChannel.CriticalReserve, channel.CriticalReserveUses);
        Assert.False(channel.CanEnqueueType(ReliableEventType.Kill));
        Assert.False(channel.TryEnqueue(ReliableEventType.Kill, default, out _));
        Assert.Equal(ReliableAdmissionFailure.Capacity, channel.LastAdmissionFailure);
        Assert.Equal(1, channel.CriticalAdmissionRejections);
        Assert.Equal(2, channel.CapacityRejections);
        Assert.Equal(0, channel.IdSpanRejections);
        Assert.Equal(32u, channel.OldestPendingSpan);
        channel.CancelPendingExceptWelcome();
        Assert.Equal(0, channel.PendingCount);
        Assert.Equal(0u, channel.OldestPendingSpan);
        Assert.Equal(ReliableChannel.Capacity, channel.PendingHighWater);
    }

    [Fact]
    public void AdaptiveRetryIsBoundedAndBacksOffAfterRepeatedLoss()
    {
        var channel = new ReliableChannel();
        Assert.Equal(ReliableChannel.InitialRetrySeconds, channel.RetrySeconds);
        Assert.False(channel.ConfigureRetry(Double.NaN, 0));
        Assert.Equal(ReliableChannel.InitialRetrySeconds, channel.RetrySeconds);
        Assert.True(channel.ConfigureRetry(10, 0));
        Assert.Equal(ReliableChannel.MinimumRetrySeconds, channel.RetrySeconds);
        Assert.True(channel.TryEnqueue(ReliableEventType.Kill, default, out uint id));
        Assert.True(channel.TryGetDue(0, out _, out _, out _));
        channel.MarkSent(id, 1, 0);
        Assert.False(channel.TryGetDue(0.049, out _, out _, out _));
        Assert.True(channel.TryGetDue(0.05, out _, out _, out _));
        channel.MarkSent(id, 2, 0.05);
        Assert.False(channel.TryGetDue(0.149, out _, out _, out _));
        Assert.True(channel.TryGetDue(0.151, out _, out _, out _));
        Assert.True(channel.ConfigureRetry(400, 50));
        Assert.Equal(ReliableChannel.MaximumRetrySeconds, channel.RetrySeconds);
    }

    [Fact]
    public void AdaptiveRetryCanBeDisabledWithoutChangingFixedRto()
    {
        var channel = new ReliableChannel(adaptiveRetryEnabled: false);
        Assert.False(channel.ConfigureRetry(10, 0));
        Assert.Equal(ReliableChannel.InitialRetrySeconds, channel.RetrySeconds);
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
