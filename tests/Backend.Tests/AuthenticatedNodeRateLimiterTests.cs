using MphRead.Backend;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class AuthenticatedNodeRateLimiterTests
{
    [Fact]
    public void AuthenticatedIdentityBucketsAreSeparateFromEachOtherAndPerOperation()
    {
        using var limiter = new AuthenticatedNodeRateLimiter();
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        for (int i = 0; i < BackendRoutePolicy.AuthenticatedNodePermitsPerMinute; i++)
            Assert.True(limiter.TryAcquire(first, "heartbeat"));
        Assert.False(limiter.TryAcquire(first, "heartbeat"));
        Assert.True(limiter.TryAcquire(first, "registration"));
        Assert.True(limiter.TryAcquire(second, "heartbeat"));
    }
}
