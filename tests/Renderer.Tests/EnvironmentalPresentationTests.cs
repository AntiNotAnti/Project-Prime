using System;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnvironmentalPresentationTests
{
    [Fact]
    public void EnvironmentalSeedIsStableWithinBucketAndSeparatesInputs()
    {
        ulong first = EnvironmentalParticleSeed.Create("Alinos Gateway", 17,
            TimeSpan.FromMilliseconds(1200));
        ulong sameBucket = EnvironmentalParticleSeed.Create("Alinos Gateway", 17,
            TimeSpan.FromMilliseconds(1999));
        ulong nextBucket = EnvironmentalParticleSeed.Create("Alinos Gateway", 17,
            TimeSpan.FromMilliseconds(2000));

        Assert.Equal(first, sameBucket);
        Assert.NotEqual(first, nextBucket);
        Assert.NotEqual(first, EnvironmentalParticleSeed.Create("Cryochasm", 17,
            TimeSpan.FromMilliseconds(1200)));
        Assert.NotEqual(first, EnvironmentalParticleSeed.Create("Alinos Gateway", 18,
            TimeSpan.FromMilliseconds(1200)));
        Assert.Equal(8355872655782461606UL, first);
    }

    [Fact]
    public void EnvironmentalSeedValidatesPresentationInputs()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentalParticleSeed.Create(" ", 0,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentalParticleSeed.Create("room", 0,
            TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentalParticleSeed.Create("room", 0,
            TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void EnvironmentalLayerDescriptorIsBoundedAndValidated()
    {
        EnvironmentalParticleLayerDescriptor descriptor = Layer();

        Assert.Equal(EnvironmentalParticleKind.Embers, descriptor.Kind);
        Assert.Equal(128, descriptor.MaximumParticles);
        Assert.Equal(0.5f, descriptor.Tint.W);
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(maximumParticles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(maximumParticles:
            EnvironmentalParticleLayerDescriptor.MaximumSupportedParticles + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(boundsMaximum: new Vector3(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(emissionRate: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(minimumLifetime:
            TimeSpan.FromSeconds(3), maximumLifetime: TimeSpan.FromSeconds(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Layer(tint:
            new Vector4(1, 1, 1, 2)));
    }

    [Fact]
    public void DecalPoolEnforcesPerRegionAndGlobalBoundsDeterministically()
    {
        var pool = new ImpactDecalPool(globalCapacity: 3, perRegionCapacity: 2);
        TimeSpan now = TimeSpan.FromSeconds(10);

        Assert.True(pool.TryAdd(Decal(20, 1, spawnedAtSeconds: 1), now));
        Assert.True(pool.TryAdd(Decal(10, 1, spawnedAtSeconds: 1), now));
        Assert.True(pool.TryAdd(Decal(30, 1, spawnedAtSeconds: 2), now));
        Assert.Equal(new ulong[] { 20, 30 }, pool.Items.Select(item => item.StableKey));

        Assert.True(pool.TryAdd(Decal(40, 2, spawnedAtSeconds: 3), now));
        Assert.True(pool.TryAdd(Decal(50, 2, spawnedAtSeconds: 4), now));
        Assert.Equal(3, pool.Items.Count);
        Assert.Equal(1, pool.CountRegion(1));
        Assert.Equal(2, pool.CountRegion(2));
        Assert.DoesNotContain(pool.Items, item => item.StableKey == 20);
        Assert.Equal(new ulong[] { 30, 40, 50 },
            pool.Items.Select(item => item.StableKey).OrderBy(key => key));
    }

    [Fact]
    public void DecalPoolExpiresByPresentationTimeAndRejectsDuplicates()
    {
        var pool = new ImpactDecalPool(globalCapacity: 4, perRegionCapacity: 2);
        Assert.True(pool.TryAdd(Decal(1, 7, spawnedAtSeconds: 1, lifetimeSeconds: 2),
            TimeSpan.FromSeconds(1)));
        Assert.False(pool.TryAdd(Decal(1, 7, spawnedAtSeconds: 1, lifetimeSeconds: 2),
            TimeSpan.FromSeconds(2)));
        Assert.Equal(0, pool.Advance(TimeSpan.FromMilliseconds(2999)));
        Assert.Equal(1, pool.Advance(TimeSpan.FromSeconds(3)));
        Assert.Empty(pool.Items);
        Assert.False(pool.TryAdd(Decal(2, 7, spawnedAtSeconds: 1, lifetimeSeconds: 1),
            TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void DecalPoolClearRetainsClockAndResetStartsNewTimeline()
    {
        var pool = new ImpactDecalPool(globalCapacity: 2, perRegionCapacity: 1);
        Assert.True(pool.TryAdd(Decal(1, 1, spawnedAtSeconds: 5), TimeSpan.FromSeconds(5)));
        pool.Clear();
        Assert.Empty(pool.Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Advance(TimeSpan.FromSeconds(4)));

        pool.Reset();
        Assert.Equal(0, pool.Advance(TimeSpan.FromSeconds(1)));
        Assert.True(pool.TryAdd(Decal(2, 1, spawnedAtSeconds: 1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => pool.TryAdd(default, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void DecalDescriptorsNormalizeNormalsAndValidateVisualData()
    {
        ImpactDecalDescriptor descriptor = Decal(1, 1, spawnedAtSeconds: 0,
            normal: new Vector3(0, 4, 0));
        Assert.Equal(Vector3.UnitY, descriptor.Normal);
        Assert.Throws<ArgumentOutOfRangeException>(() => Decal(1, 1, 0, normal: Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Decal(1, 1, 0, radius: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Decal(1, 1, 0,
            tint: new Vector4(float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Decal(1, 1, -1));
    }

    private static EnvironmentalParticleLayerDescriptor Layer(
        Vector3? boundsMaximum = null, int maximumParticles = 128,
        float emissionRate = 12, TimeSpan? minimumLifetime = null,
        TimeSpan? maximumLifetime = null, Vector4? tint = null)
        => new("embers", 5, EnvironmentalParticleKind.Embers,
            new Vector3(-1), boundsMaximum ?? Vector3.One, maximumParticles,
            emissionRate, minimumLifetime ?? TimeSpan.FromSeconds(1),
            maximumLifetime ?? TimeSpan.FromSeconds(2), minimumSize: 0.02f,
            maximumSize: 0.08f, driftVelocity: new Vector3(0, 0.1f, 0),
            tint: tint ?? new Vector4(1, 0.4f, 0.1f, 0.5f), emissiveStrength: 2);

    private static ImpactDecalDescriptor Decal(ulong stableKey, ulong regionKey,
        double spawnedAtSeconds, double lifetimeSeconds = 20, Vector3? normal = null,
        float radius = 0.25f, Vector4? tint = null)
        => new(stableKey, regionKey, ImpactDecalKind.BeamMark, Vector3.Zero,
            normal ?? Vector3.UnitZ, radius, tint ?? Vector4.One,
            TimeSpan.FromSeconds(spawnedAtSeconds), TimeSpan.FromSeconds(lifetimeSeconds));
}
