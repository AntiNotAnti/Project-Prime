using System;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
public sealed class ReliableEventWindowTests
{
    [Theory]
    [InlineData(0u, false)]
    [InlineData(0u, true)]
    [InlineData(uint.MaxValue - 100, false)]
    [InlineData(uint.MaxValue - 100, true)]
    public void LostEventOrLostAckSurvivesSteadyNewCompletions(uint first, bool receivedBeforeAckLoss)
    {
        var sender = new ReliableChannel(first); var receiver = new ReliableChannel();
        Assert.True(sender.TryEnqueue(ReliableEventType.WorldEvent, new byte[] { 1 }, out uint old));
        sender.MarkSent(old, 0, 1);
        int oldDeliveries = receivedBeforeAckLoss && receiver.Receive(old) ? 1 : 0;
        // Only actually received newer datagrams are acknowledged. The oldest
        // event/ACK is withheld through 512 completions, well beyond packet ACK32.
        for (uint packet = 1; packet <= 512; packet++)
        {
            Assert.True(sender.TryEnqueue(ReliableEventType.Combat, default, out uint id));
            sender.MarkSent(id, packet, packet / 60d + 1);
            Assert.True(receiver.Receive(id)); Assert.False(receiver.Receive(id));
            sender.Acknowledge(packet, 0);
            Assert.Equal(1, sender.PendingCount);
        }
        Assert.True(sender.CanEnqueue); Assert.Equal(513u, sender.OldestPendingSpan);
        var diagnostic = sender.OldestPending(20)!.Value;
        Assert.Equal(old, diagnostic.Id); Assert.Equal(ReliableEventType.WorldEvent, diagnostic.Type);
        Assert.Equal(1u, diagnostic.SentAttempts); Assert.Equal(19, diagnostic.AgeSeconds);
        sender.MarkSent(old, 1000, 20);
        if (receiver.Receive(old)) oldDeliveries++;
        Assert.False(receiver.Receive(old)); sender.Acknowledge(1000, 0);
        Assert.Equal(1, oldDeliveries); Assert.Equal(0, sender.PendingCount);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue - 10)]
    public void WindowEdgesAndHalfRangeAreExact(uint first)
    {
        var receiver = new ReliableChannel();
        Assert.True(receiver.Receive(first));
        uint newest = unchecked(first + ReliableChannel.EventWindowCapacity - 1);
        Assert.True(receiver.Receive(newest)); Assert.False(receiver.Receive(first));
        uint unseenOld = unchecked(first + 1);
        Assert.True(receiver.Receive(unseenOld)); Assert.False(receiver.Receive(unseenOld));
        Assert.True(receiver.Receive(unchecked(newest + 1)));
        Assert.False(receiver.Receive(first));
        Assert.False(receiver.Receive(unchecked(newest + 1 + 0x80000000u)));
        // A large forward jump clears stale physical ring bits.
        uint jumped = unchecked(newest + 1 + ReliableChannel.EventWindowCapacity * 2);
        Assert.True(receiver.Receive(jumped));
        Assert.True(receiver.Receive(unchecked(jumped - 1))); Assert.False(receiver.Receive(unchecked(jumped - 1)));
    }
}
