using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnvironmentalParticleIntegrationTests
{
    [Fact]
    public void MissingOrUnlistedManifestContentAddsNoRoomProfile()
    {
        using var directory = new TemporaryDirectory();
        EnvironmentalParticleCatalogLoadResult missing
            = EnvironmentalParticleManifestLoader.Load(directory.Path);

        Assert.Equal(0, missing.Catalog.RoomCount);
        Assert.Contains(missing.Issues, issue => issue.Kind
            == EnvironmentalParticleManifestIssueKind.MissingManifest);
        Assert.False(missing.Catalog.TryResolve("mp1", out var layers));
        Assert.Empty(layers);
    }

    [Fact]
    public void ManifestStrictlyResolvesOnlyAuthoredArchiveKeys()
    {
        using var directory = ManifestDirectory();
        EnvironmentalParticleCatalogLoadResult loaded
            = EnvironmentalParticleManifestLoader.Load(directory.Path);

        Assert.Empty(loaded.Issues);
        Assert.True(loaded.Catalog.TryResolve("mp1", out var layers));
        EnvironmentalParticleLayerDescriptor layer = Assert.Single(layers);
        Assert.Equal("embers", layer.StableLayerKey);
        Assert.Equal(EnvironmentalParticleKind.Embers, layer.Kind);
        Assert.Equal(2f, layer.EmissionRatePerSecond);
        Assert.False(loaded.Catalog.TryResolve("mp2", out _));

        File.WriteAllText(Path.Combine(directory.Path,
            EnvironmentalParticleManifestLoader.ManifestFileName),
            """{"version":1,"rooms":[],"unexpected":true}""");
        EnvironmentalParticleCatalogLoadResult strict
            = EnvironmentalParticleManifestLoader.Load(directory.Path);
        Assert.Equal(0, strict.Catalog.RoomCount);
        Assert.Contains(strict.Issues, issue => issue.Kind
            == EnvironmentalParticleManifestIssueKind.MalformedManifest);
    }

    [Fact]
    public void PresentationStateIsEnhancedAndPackOptInAndReplayDeterministic()
    {
        using var directory = ManifestDirectory();
        EnvironmentalParticleCatalog catalog
            = EnvironmentalParticleManifestLoader.Load(directory.Path).Catalog;
        var state = new EnvironmentalParticlePresentationState(catalog, capacity: 8);
        state.ConfigureRoom("mp1");

        Assert.Null(state.Prepare(GraphicsPreset.Original, presentationTick: 60));
        EnvironmentalParticleSnapshot first = Assert.IsType<EnvironmentalParticleSnapshot>(
            state.Prepare(GraphicsPreset.Enhanced, presentationTick: 60));
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(first.Items, state.Prepare(GraphicsPreset.Enhanced, 60)!.Items);
        EnvironmentalParticleSnapshot interpolated
            = Assert.IsType<EnvironmentalParticleSnapshot>(
                state.Prepare(GraphicsPreset.Enhanced, 60, renderFraction: .5f));
        Assert.Equal(first.Items.Select(item => item.Particle.StableKey),
            interpolated.Items.Select(item => item.Particle.StableKey));
        Assert.Equal(first.Items.Count, interpolated.Items.Count);
        Assert.All(first.Items.Zip(interpolated.Items), pair =>
            Assert.True(pair.Second.Position.Y > pair.First.Position.Y));

        var unrelatedRandom = new Random(81);
        for (int i = 0; i < 1000; i++) unrelatedRandom.Next();
        state.Prepare(GraphicsPreset.Enhanced, 120);
        EnvironmentalParticleSnapshot rewound = Assert.IsType<EnvironmentalParticleSnapshot>(
            state.Prepare(GraphicsPreset.Enhanced, 60, renderFraction: .5f));

        var replay = new EnvironmentalParticlePresentationState(catalog, capacity: 8);
        replay.ConfigureRoom("mp1");
        Assert.Equal(replay.Prepare(GraphicsPreset.Enhanced, 60, .5f)!.Items,
            rewound.Items);

        state.ConfigureRoom("unlisted");
        Assert.Null(state.Prepare(GraphicsPreset.Enhanced, 60));
    }

    [Fact]
    public void SimulationTickClockAndBillboardGeometryAreFiniteAndStable()
    {
        Assert.Equal(TimeSpan.FromSeconds(1),
            EnvironmentalParticlePresentationClock.FromSimulationTick(60));
        Assert.Equal(TimeSpan.FromTicks(166_666),
            EnvironmentalParticlePresentationClock.FromSimulationTick(1));
        Assert.Equal(TimeSpan.MaxValue,
            EnvironmentalParticlePresentationClock.FromSimulationTick(ulong.MaxValue));
        EnvironmentalParticlePresentationClock.FrameTime half
            = EnvironmentalParticlePresentationClock.Capture(60, .5f);
        Assert.Equal(TimeSpan.FromSeconds(1), half.SchedulingTime);
        Assert.Equal(TimeSpan.FromTicks(10_083_333), half.SamplingTime);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnvironmentalParticlePresentationClock.Capture(60, float.NaN));

        var particle = new EnvironmentalParticleRecord(stableKey: 7,
            spawnOrdinal: 0, stableLayerKey: "embers", spawnRegionKey: 1,
            kind: EnvironmentalParticleKind.Embers,
            initialPosition: new Vector3(1, 2, 3),
            velocity: Vector3.Zero, size: 2, tint: Vector4.One,
            emissiveStrength: 0, spawnedAtTick: 0,
            lifetimeTicks: TimeSpan.TicksPerSecond);
        var sample = new EnvironmentalParticleSample(particle,
            new Vector3(1, 2, 3), TimeSpan.Zero);
        var vertices = new Vector3[4];

        ScenePresentation.BuildEnvironmentalParticleQuad(sample,
            Vector3.UnitX, Vector3.UnitY, vertices);

        Assert.Equal(new Vector3(0, 3, 3), vertices[0]);
        Assert.Equal(new Vector3(2, 3, 3), vertices[1]);
        Assert.Equal(new Vector3(2, 1, 3), vertices[2]);
        Assert.Equal(new Vector3(0, 1, 3), vertices[3]);
        Assert.All(vertices, vertex =>
            Assert.True(float.IsFinite(vertex.X) && float.IsFinite(vertex.Y)
                && float.IsFinite(vertex.Z)));
    }

    private static TemporaryDirectory ManifestDirectory()
    {
        var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path,
            EnvironmentalParticleManifestLoader.ManifestFileName), """
            {
              "version": 1,
              "rooms": [
                {
                  "room": "mp1",
                  "layers": [
                    {
                      "key": "embers",
                      "spawnRegionKey": 9,
                      "kind": "Embers",
                      "boundsMinimum": [-2, -1, -3],
                      "boundsMaximum": [2, 3, 4],
                      "maximumParticles": 8,
                      "emissionRatePerSecond": 2,
                      "minimumLifetimeSeconds": 5,
                      "maximumLifetimeSeconds": 5,
                      "minimumSize": 0.05,
                      "maximumSize": 0.1,
                      "driftVelocity": [0, 0.2, 0],
                      "tint": [1, 0.4, 0.1, 0.5],
                      "emissiveStrength": 2
                    }
                  ]
                }
              ]
            }
            """);
        return directory;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "prime-environment-particles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
