using System;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class TransientVisualLightPoolTests
{
    [Fact]
    public void LifetimeUsesHalfOpenPresentationTimeBoundaries()
    {
        var pool = new TransientVisualLightPool();
        pool.Advance(10, TimeSpan.FromSeconds(1));
        Assert.True(pool.TrySpawn(1, new Vector3(1, 2, 3),
            Profile(priority: 3, lifetime: 0.5f)));

        pool.Advance(11, TimeSpan.FromMilliseconds(1499));
        VisualLightCandidate active = Assert.Single(pool.Snapshot());
        Assert.Equal(new Vector3(1, 2, 3), active.Position);
        Assert.Equal(2f, active.Profile.Falloff);

        pool.Advance(12, TimeSpan.FromMilliseconds(1500));
        Assert.Empty(pool.Snapshot());
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void OverflowKeepsHigherPrioritiesAndUsesStableKeyTies()
    {
        var pool = new TransientVisualLightPool();
        pool.Advance(1, TimeSpan.Zero);
        for (ulong key = 1; key <= TransientVisualLightPool.MaximumLights; key++)
            Assert.True(pool.TrySpawn(key, Vector3.Zero, Profile(priority: 1)));

        Assert.True(pool.TrySpawn(1000, Vector3.Zero, Profile(priority: 2)));
        Assert.False(pool.TrySpawn(1001, Vector3.Zero, Profile(priority: 0)));

        ulong[] keys = pool.Snapshot().Select(light => light.SourceKey).ToArray();
        Assert.Equal(TransientVisualLightPool.MaximumLights, keys.Length);
        Assert.Equal(1000ul, keys[0]);
        Assert.DoesNotContain(128ul, keys);
        Assert.DoesNotContain(1001ul, keys);
    }

    [Fact]
    public void EqualPriorityOverflowIsIndependentOfArrivalOrder()
    {
        ulong[] keys = Enumerable.Range(1,
            TransientVisualLightPool.MaximumLights + 8)
            .Select(value => (ulong)value).ToArray();
        var forward = new TransientVisualLightPool();
        var reverse = new TransientVisualLightPool();
        forward.Advance(1, TimeSpan.Zero);
        reverse.Advance(1, TimeSpan.Zero);

        foreach (ulong key in keys)
            forward.TrySpawn(key, Vector3.Zero, Profile(priority: 1));
        foreach (ulong key in keys.Reverse())
            reverse.TrySpawn(key, Vector3.Zero, Profile(priority: 1));

        ulong[] expected = Enumerable.Range(1,
            TransientVisualLightPool.MaximumLights)
            .Select(value => (ulong)value).ToArray();
        Assert.Equal(expected,
            forward.Snapshot().Select(light => light.SourceKey));
        Assert.Equal(expected,
            reverse.Snapshot().Select(light => light.SourceKey));
    }

    [Fact]
    public void DuplicateSourceIsIdempotentAndDoesNotExtendLifetime()
    {
        var pool = new TransientVisualLightPool();
        pool.Advance(4, TimeSpan.FromSeconds(2));
        Assert.True(pool.TrySpawn(7, Vector3.Zero,
            Profile(priority: 1, lifetime: 0.25f)));
        Assert.False(pool.TrySpawn(7, Vector3.One,
            Profile(priority: 9, lifetime: 10)));
        Assert.Equal(TransientVisualLightAdvance.SameTick,
            pool.Advance(4, TimeSpan.FromSeconds(2)));
        Assert.False(pool.TrySpawn(7, Vector3.One,
            Profile(priority: 9, lifetime: 10)));

        VisualLightCandidate original = Assert.Single(pool.Snapshot());
        Assert.Equal(Vector3.Zero, original.Position);
        Assert.Equal(1, original.Profile.Priority);

        pool.Advance(5, TimeSpan.FromMilliseconds(2250));
        Assert.Empty(pool.Snapshot());
    }

    [Fact]
    public void RewindClearsAbandonedTimelineAndAllowsKeyReuse()
    {
        var pool = new TransientVisualLightPool();
        pool.Advance(10, TimeSpan.FromSeconds(10));
        pool.TrySpawn(4, Vector3.Zero, Profile(priority: 1));

        Assert.Equal(TransientVisualLightAdvance.Rewound,
            pool.Advance(9, TimeSpan.FromSeconds(9)));
        Assert.Empty(pool.Snapshot());
        Assert.True(pool.TrySpawn(4, Vector3.One, Profile(priority: 2)));
        Assert.Equal(Vector3.One, Assert.Single(pool.Snapshot()).Position);

        Assert.Equal(TransientVisualLightAdvance.Rewound,
            pool.Advance(11, TimeSpan.FromSeconds(8)));
        Assert.Empty(pool.Snapshot());
    }

    [Fact]
    public void ExplicitResetClearsLightsAndTimeline()
    {
        var pool = new TransientVisualLightPool();
        pool.Advance(2, TimeSpan.FromSeconds(1));
        pool.TrySpawn(1, Vector3.Zero, Profile(priority: 1));

        pool.Reset();

        Assert.Equal(0, pool.Count);
        Assert.False(pool.HasTimeline);
        Assert.Equal(0ul, pool.PresentationTick);
        Assert.Empty(pool.Snapshot());
        Assert.Throws<InvalidOperationException>(() =>
            pool.TrySpawn(2, Vector3.Zero, Profile(priority: 1)));
        Assert.Equal(TransientVisualLightAdvance.Advanced,
            pool.Advance(0, TimeSpan.Zero));
    }

    [Fact]
    public void PoolRejectsUnstableKeysInvalidTimelineAndNonQuadraticProfiles()
    {
        var pool = new TransientVisualLightPool();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pool.Advance(0, TimeSpan.FromTicks(-1)));
        pool.Advance(0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() =>
            pool.Advance(0, TimeSpan.FromTicks(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pool.TrySpawn(0, Vector3.Zero, Profile(priority: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VisualLightProfile(Vector3.One, radius: 1, intensity: 1,
                priority: 1, lifetime: 1, falloff: 1));
    }

    private static VisualLightProfile Profile(int priority, float lifetime = 10)
        => new(Vector3.One, radius: 4, intensity: 1, priority,
            lifetime, falloff: VisualLightProfile.SupportedFalloff);
}
