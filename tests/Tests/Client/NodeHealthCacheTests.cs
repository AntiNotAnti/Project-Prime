using System;
using System.Linq;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class NodeHealthCacheTests
{
    [Fact]
    public void TransientFailurePenalizesOnlyTheNodeForSixtySeconds()
    {
        var cache = new NodeHealthCache();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        NodeListing failed = Node(1, players: 1);
        NodeListing healthy = Node(2, players: 1);

        cache.RecordTransientFailure(failed.NodeId, now);

        Assert.True(cache.IsUnhealthy(failed.NodeId, now));
        Assert.Equal(healthy,
            PlayController.OrderAutomaticNodes([failed, healthy], "Automatic",
                healthCache: cache, now: now).First());
        Assert.Equal(failed,
            PlayController.OrderAutomaticNodes([failed, healthy], "Automatic",
                healthCache: cache, now: now).Last());
        Assert.False(cache.IsUnhealthy(failed.NodeId, now.AddSeconds(60)));
    }

    [Fact]
    public void SuccessfulConnectionClearsFailureAndCacheEvictsOldestWhenBounded()
    {
        var cache = new NodeHealthCache(maximumEntries: 2);
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        NodeListing first = Node(1, players: 1);
        NodeListing second = Node(2, players: 1);
        NodeListing third = Node(3, players: 1);

        cache.RecordTransientFailure(first.NodeId, now);
        cache.RecordTransientFailure(second.NodeId, now.AddSeconds(1));
        cache.RecordTransientFailure(third.NodeId, now.AddSeconds(2));

        Assert.Equal(2, cache.Count);
        Assert.False(cache.IsUnhealthy(first.NodeId, now.AddSeconds(2)));
        Assert.True(cache.IsUnhealthy(second.NodeId, now.AddSeconds(2)));
        Assert.True(cache.IsUnhealthy(third.NodeId, now.AddSeconds(2)));

        for (int attempt = 0; attempt < 20; attempt++)
            cache.RecordTransientFailure(third.NodeId, now.AddSeconds(3 + attempt));
        Assert.True(cache.TryGetObservation(third.NodeId, now.AddSeconds(22),
            out NodeHealthObservation observation));
        Assert.Equal(NodeHealthCache.MaximumFailureCount, observation.FailureCount);
        Assert.Equal(now.AddSeconds(22), observation.LastFailedConnection);

        DateTimeOffset later = now.AddSeconds(22);
        cache.RecordSuccess(second.NodeId, later);
        Assert.False(cache.IsUnhealthy(second.NodeId, later));
        Assert.Equal(second,
            PlayController.OrderAutomaticNodes([second, third], "Automatic",
                healthCache: cache, now: later).First());
    }

    [Fact]
    public void CachedSuccessfulRttFillsProbeGapsAndAllFailedNodesRemainFallbacks()
    {
        var cache = new NodeHealthCache();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        NodeListing slow = Node(1, players: 1);
        NodeListing fast = Node(2, players: 1);
        cache.RecordSuccess(slow.NodeId, now, TimeSpan.FromMilliseconds(80));
        cache.RecordSuccess(fast.NodeId, now, TimeSpan.FromMilliseconds(20));
        cache.RecordTransientFailure(slow.NodeId, now.AddSeconds(1));
        cache.RecordTransientFailure(fast.NodeId, now.AddSeconds(1));

        NodeListing[] ordered = PlayController.OrderAutomaticNodes([slow, fast], "Automatic",
            healthCache: cache, now: now.AddSeconds(1)).ToArray();
        Assert.Equal([fast, slow], ordered);
        Assert.True(cache.TryGetRecentRtt(fast.NodeId, now.AddSeconds(1), out TimeSpan rtt));
        Assert.Equal(TimeSpan.FromMilliseconds(20), rtt);
    }

    private static NodeListing Node(int id, int players)
        => new(new Guid(id, 0, 0, new byte[8]), "Server", "Automatic",
            "wss://localhost/v1/control", 1, "build", "content", 8, players,
            1, 0, "community", DateTimeOffset.UnixEpoch);
}
