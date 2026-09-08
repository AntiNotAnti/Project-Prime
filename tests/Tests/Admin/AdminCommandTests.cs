using System;
using MphRead.Admin;
using Xunit;

namespace MphRead.Tests.Admin;

public sealed class AdminCommandTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void CommandsAreOwnerExecutedBoundedIdempotentAndExpireWithoutMutation()
    {
        var clock = new Clock(); var queue = new AdminCommandQueue(clock); int applied = 0;
        var command = new AdminCommand(Guid.NewGuid(), AdminCommandKind.LockRoster, clock.Now);
        Assert.Equal("queued", queue.Submit(command).State); Assert.Equal(0, applied);
        Assert.Equal("conflict", queue.Submit(command with { Kind = AdminCommandKind.UnlockRoster }).State);
        queue.Drain(7, c => { applied++; return new(c.RequestId, "applied", "locked"); });
        Assert.Equal(1, applied); Assert.Equal((uint)7, queue.Submit(command).AppliedTick);
        queue.Drain(8, _ => throw new InvalidOperationException()); Assert.Equal(1, applied);
        AdminCommand? last = null;
        for (int i = 0; i < 32; i++) Assert.Equal("queued", queue.Submit(last = command with { RequestId = Guid.NewGuid() }).State);
        Assert.Equal("unavailable", queue.Submit(command with { RequestId = Guid.NewGuid() }).State);
        clock.Now += TimeSpan.FromSeconds(31);
        for (int i = 0; i < 8; i++) queue.Drain(9, _ => throw new Exception("Expired command executed"));
        Assert.Equal("expired", queue.Find(last!.RequestId)!.State);
        Assert.Equal("expired", queue.Submit(command with { RequestId = Guid.NewGuid() }).State);
        queue.Close();
    }

}
