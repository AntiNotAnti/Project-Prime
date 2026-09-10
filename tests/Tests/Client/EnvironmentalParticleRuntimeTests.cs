using System;
using System.Collections.Generic;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnvironmentalParticleRuntimeTests
{
    [Fact]
    public void HighRefreshAndRepeatedTicksDoNotChangeSpawnAccumulation()
    {
        EnvironmentalParticleRuntime highRefresh = Runtime(64, "room", Layer(
            "embers", 1, rate: 30, maximumParticles: 64));
        EnvironmentalParticleSnapshot current = highRefresh.Advance(TimeSpan.Zero);
        for (int frame = 1; frame <= 240; frame++)
        {
            TimeSpan time = TimeSpan.FromTicks(
                frame * TimeSpan.TicksPerSecond / 240);
            current = highRefresh.Advance(time);
            EnvironmentalParticleSnapshot repeated = highRefresh.Advance(time);
            Assert.Equal(current.Items, repeated.Items);
        }

        EnvironmentalParticleRuntime sparse = Runtime(64, "room", Layer(
            "embers", 1, rate: 30, maximumParticles: 64));
        EnvironmentalParticleSnapshot once = sparse.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(30, current.Items.Count);
        Assert.Equal(once.Items, current.Items);
    }

    [Fact]
    public void SameSchedulingTickSamplesSmoothMotionWithoutNewSpawns()
    {
        EnvironmentalParticleRuntime runtime = Runtime(8, "room", Layer(
            "embers", 1, rate: 1, maximumParticles: 8,
            drift: new Vector3(0, 6, 0)));
        TimeSpan schedulingTime = TimeSpan.FromSeconds(1);
        EnvironmentalParticleSnapshot start = runtime.Advance(
            schedulingTime, schedulingTime);
        EnvironmentalParticleSnapshot half = runtime.Advance(schedulingTime,
            schedulingTime + TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120));

        Assert.Equal(start.Items.Count, half.Items.Count);
        Assert.Equal(start.Items.Select(item => item.Particle.StableKey),
            half.Items.Select(item => item.Particle.StableKey));
        Assert.Equal(start.SchedulingTick, half.SchedulingTick);
        EnvironmentalParticleSample first = Assert.Single(start.Items);
        EnvironmentalParticleSample moved = Assert.Single(half.Items);
        Assert.Equal(first.Position.Y + .05f, moved.Position.Y, precision: 5);
        Assert.True(half.SampleTick > start.SampleTick);
    }

    [Fact]
    public void ReplayIsDeterministicAcrossAdvanceCadenceAndLayerInputOrder()
    {
        EnvironmentalParticleLayerDescriptor embers = Layer("embers", 1, rate: 7);
        EnvironmentalParticleLayerDescriptor dust = Layer("dust", 2, rate: 11,
            kind: EnvironmentalParticleKind.Dust);
        EnvironmentalParticleRuntime first = Runtime(128, "Alinos", embers, dust);
        EnvironmentalParticleRuntime second = Runtime(128, "Alinos", dust, embers);

        first.Advance(TimeSpan.FromMilliseconds(170));
        first.Advance(TimeSpan.FromMilliseconds(630));
        EnvironmentalParticleSnapshot expected = first.Advance(TimeSpan.FromSeconds(2));
        EnvironmentalParticleSnapshot replay = second.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(expected.Items, replay.Items);
        Assert.Equal(expected.Items.Select(item => item.Particle.StableKey),
            expected.Items.Select(item => item.Particle.StableKey).Distinct());
    }

    [Fact]
    public void PerLayerAndAggregateCapsEvictOldestThenStableKey()
    {
        EnvironmentalParticleRuntime perLayer = Runtime(8, "room",
            Layer("embers", 1, rate: 10, maximumParticles: 2));
        EnvironmentalParticleSnapshot layerSnapshot = perLayer.Advance(
            TimeSpan.FromMilliseconds(300));
        Assert.Equal(2, layerSnapshot.Items.Count);
        Assert.Equal(new[] { 2_000_000L, 3_000_000L },
            layerSnapshot.Items.Select(item => item.Particle.SpawnedAtTick));

        EnvironmentalParticleLayerDescriptor first = Layer("a", 1, rate: 10);
        EnvironmentalParticleLayerDescriptor second = Layer("b", 2, rate: 10);
        EnvironmentalParticleRuntime candidates = Runtime(4, "room", first, second);
        EnvironmentalParticleSnapshot all = candidates.Advance(
            TimeSpan.FromMilliseconds(200));
        EnvironmentalParticleRecord[] expected = all.Items
            .Select(item => item.Particle)
            .OrderByDescending(item => item.SpawnedAtTick)
            .ThenByDescending(item => item.StableKey)
            .Take(2)
            .OrderBy(item => item.SpawnedAtTick)
            .ThenBy(item => item.StableKey)
            .ToArray();

        EnvironmentalParticleRuntime capped = Runtime(2, "room", second, first);
        EnvironmentalParticleSnapshot actual = capped.Advance(
            TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, actual.Items.Count);
        Assert.Equal(expected, actual.Items.Select(item => item.Particle));
    }

    [Fact]
    public void MotionIsFiniteAndParticlesExpireByPresentationLifetime()
    {
        EnvironmentalParticleRuntime runtime = Runtime(4, "room", Layer(
            "motes", 4, rate: 1, minimumLifetimeSeconds: 1,
            maximumLifetimeSeconds: 1, drift: new Vector3(2, -1, .5f)));

        EnvironmentalParticleSnapshot spawned = runtime.Advance(TimeSpan.FromSeconds(1));
        EnvironmentalParticleRecord particle = Assert.Single(spawned.Items).Particle;
        EnvironmentalParticleSnapshot moved = runtime.Advance(
            TimeSpan.FromMilliseconds(1500));
        EnvironmentalParticleSample sample = Assert.Single(moved.Items);
        Assert.Equal(particle.InitialPosition.X + 1, sample.Position.X, precision: 5);
        Assert.Equal(particle.InitialPosition.Y - .5f, sample.Position.Y, precision: 5);
        Assert.True(float.IsFinite(sample.Position.X));
        Assert.True(float.IsFinite(sample.Position.Y));
        Assert.True(float.IsFinite(sample.Position.Z));

        EnvironmentalParticleSnapshot expired = runtime.Advance(TimeSpan.FromSeconds(2));
        EnvironmentalParticleRecord replacement = Assert.Single(expired.Items).Particle;
        Assert.Equal(2 * TimeSpan.TicksPerSecond, replacement.SpawnedAtTick);
        Assert.NotEqual(particle.StableKey, replacement.StableKey);
    }

    [Fact]
    public void RewindRequiresResetWhileResetAndRoomChangeStartNewTimelines()
    {
        EnvironmentalParticleLayerDescriptor layer = Layer("dust", 9, rate: 4);
        EnvironmentalParticleRuntime runtime = Runtime(16, "first", layer);
        EnvironmentalParticleSnapshot original = runtime.Advance(TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            runtime.Advance(TimeSpan.FromMilliseconds(999)));
        runtime.Reset(TimeSpan.Zero);
        Assert.Equal(original.Items,
            runtime.Advance(TimeSpan.FromSeconds(1)).Items);

        runtime.EnterRoom("second", new[] { layer }, TimeSpan.Zero);
        Assert.Equal(0, runtime.Count);
        EnvironmentalParticleSnapshot changed = runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("second", changed.RoomKey);
        Assert.NotEqual(original.Items.Select(item => item.Particle.StableKey),
            changed.Items.Select(item => item.Particle.StableKey));
    }

    [Fact]
    public void RuntimeHasFixedBoundsAndNoExternalRandomDependency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EnvironmentalParticleRuntime(0));
        Assert.Throws<InvalidOperationException>(() =>
            new EnvironmentalParticleRuntime(1).Advance(TimeSpan.Zero));

        EnvironmentalParticleRuntime first = Runtime(32, "room",
            Layer("frost", 5, rate: 8));
        EnvironmentalParticleRuntime second = Runtime(32, "room",
            Layer("frost", 5, rate: 8));
        var unrelatedRandom = new Random(12345);
        for (int i = 0; i < 10_000; i++) unrelatedRandom.Next();

        EnvironmentalParticleSnapshot firstSnapshot = first.Advance(
            TimeSpan.FromSeconds(2));
        for (int i = 0; i < 10_000; i++) unrelatedRandom.Next();
        EnvironmentalParticleSnapshot secondSnapshot = second.Advance(
            TimeSpan.FromSeconds(2));

        Assert.Equal(firstSnapshot.Items, secondSnapshot.Items);
        Assert.DoesNotContain(typeof(EnvironmentalParticleRuntime)
            .GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic),
            field => typeof(Random).IsAssignableFrom(field.FieldType));
    }

    private static EnvironmentalParticleRuntime Runtime(int capacity,
        string roomKey, params EnvironmentalParticleLayerDescriptor[] layers)
    {
        var runtime = new EnvironmentalParticleRuntime(capacity);
        runtime.EnterRoom(roomKey, layers, TimeSpan.Zero);
        return runtime;
    }

    private static EnvironmentalParticleLayerDescriptor Layer(string key,
        ulong region, float rate, int maximumParticles = 64,
        EnvironmentalParticleKind kind = EnvironmentalParticleKind.Embers,
        double minimumLifetimeSeconds = 10, double maximumLifetimeSeconds = 10,
        Vector3? drift = null)
        => new(key, region, kind, new Vector3(-1), Vector3.One,
            maximumParticles, rate,
            TimeSpan.FromSeconds(minimumLifetimeSeconds),
            TimeSpan.FromSeconds(maximumLifetimeSeconds), .02f, .08f,
            drift ?? new Vector3(0, .1f, 0), new Vector4(1, .5f, .25f, .5f), 2);
}
