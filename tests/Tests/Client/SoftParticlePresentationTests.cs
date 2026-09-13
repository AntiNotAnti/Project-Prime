using System;
using System.IO;
using System.Linq;
using MphRead;
using Xunit;

public sealed class SoftParticlePresentationTests
{
    [Theory]
    [InlineData("effect/1/element/0", true)]
    [InlineData("effect/2147483647/element/2147483647", true)]
    [InlineData("effect/0/element/0", false)]
    [InlineData("effect/01/element/0", false)]
    [InlineData("effect/1/element/00", false)]
    [InlineData("effect/1/element/-1", false)]
    [InlineData("Effect/1/element/0", false)]
    [InlineData("effect/1/particle/0", false)]
    public void PresentationKeysUseCanonicalEffectAndElementIdentity(string value,
        bool valid)
    {
        bool parsed = SoftParticlePresentationKey.TryParse(value,
            out SoftParticlePresentationKey key);

        Assert.Equal(valid, parsed);
        Assert.Equal(valid, key.IsValid);
        if (valid) Assert.Equal(value, key.Value);
    }

    [Fact]
    public void DepthFadeIsDisabledWithoutAnExplicitProfile()
    {
        Assert.Equal(1, SoftParticleDepthFadePolicy.Evaluate(
            particleLinearDepth: 10, surfaceLinearDepth: 10, profile: null));

        Assert.False(SoftParticleProfileCatalog.Empty.TryResolve(
            SoftParticlePresentationKey.Create(7, 0), out _));
    }

    [Fact]
    public void BackgroundSentinelDoesNotFadeAnOptedInParticle()
    {
        var profile = new SoftParticleProfile(2);

        Assert.Equal(1, SoftParticleDepthFadePolicy.Evaluate(
            particleLinearDepth: 10,
            surfaceLinearDepth: SoftParticleDepthFadePolicy.BackgroundSurfaceDepth,
            profile));
    }

    [Fact]
    public void DepthFadeConvergesAndRejectsParticlesBehindTheSurface()
    {
        var profile = new SoftParticleProfile(2);

        Assert.Equal(1, SoftParticleDepthFadePolicy.Evaluate(8, 10, profile));
        Assert.Equal(0.5f, SoftParticleDepthFadePolicy.Evaluate(9, 10, profile));
        Assert.Equal(0, SoftParticleDepthFadePolicy.Evaluate(10, 10, profile));
        Assert.Equal(0, SoftParticleDepthFadePolicy.Evaluate(11, 10, profile));
    }

    [Fact]
    public void ProfileAndDepthPolicyRejectNonFiniteOrUnboundedInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SoftParticleProfile(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SoftParticleProfile(SoftParticleProfile.MaximumFadeDistance + 1));
        Assert.Throws<ArgumentException>(() => new SoftParticleDescriptor(
            SoftParticlePresentationKey.Create(1, 0), default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SoftParticleDepthFadePolicy.Evaluate(float.PositiveInfinity, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SoftParticleDepthFadePolicy.Evaluate(1, float.NaN, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SoftParticleDepthFadePolicy.Evaluate(0, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SoftParticleDepthFadePolicy.Evaluate(1, -1, null));
    }

    [Fact]
    public void ManifestRequiresExplicitProfilesAndSortsThemDeterministically()
    {
        string root = CreatePack("""
            {
              "format": 1,
              "profiles": [
                { "key": "effect/9/element/2", "fadeDistance": 1.5 },
                { "key": "effect/2/element/4", "fadeDistance": 0.75 }
              ]
            }
            """);
        try
        {
            SoftParticleProfileLoadResult loaded = SoftParticleProfileLoader.Load(root);

            Assert.Empty(loaded.Issues);
            Assert.Equal(2, loaded.Catalog.Count);
            Assert.Equal(new[] { "effect/2/element/4", "effect/9/element/2" },
                loaded.Catalog.Descriptors.Select(descriptor => descriptor.Key.Value));
            Assert.True(loaded.Catalog.TryResolve(
                SoftParticlePresentationKey.Create(9, 2), out SoftParticleProfile profile));
            Assert.Equal(1.5f, profile.FadeDistance);
            Assert.False(loaded.Catalog.TryResolve(
                SoftParticlePresentationKey.Create(9, 3), out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidAndDuplicateProfilesFailSoftWithoutEnablingParticles()
    {
        string root = CreatePack("""
            {
              "format": 1,
              "profiles": [
                { "key": "effect/1/element/0", "fadeDistance": 1 },
                { "key": "effect/2/element/0", "fadeDistance": "bad" },
                { "key": "effect/3/element/0", "fadeDistance": 2 },
                { "key": "effect/3/element/0", "fadeDistance": 3 }
              ]
            }
            """);
        try
        {
            SoftParticleProfileLoadResult loaded = SoftParticleProfileLoader.Load(root);

            Assert.Equal(1, loaded.Catalog.Count);
            Assert.True(loaded.Catalog.TryResolve(
                SoftParticlePresentationKey.Create(1, 0), out _));
            Assert.False(loaded.Catalog.TryResolve(
                SoftParticlePresentationKey.Create(2, 0), out _));
            Assert.False(loaded.Catalog.TryResolve(
                SoftParticlePresentationKey.Create(3, 0), out _));
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == SoftParticleManifestIssueKind.InvalidProfile);
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == SoftParticleManifestIssueKind.DuplicateProfile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingVersionUnknownPropertiesAndMalformedFilesFallBackToEmpty()
    {
        string missing = Path.Combine(Path.GetTempPath(),
            $"prime-soft-particle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(missing);
        string missingVersion = CreatePack("""{ "profiles": [] }""");
        string unknownProperty = CreatePack(
            """{ "format": 1, "profiles": [], "enabled": true }""");
        string unsupported = CreatePack("""{ "format": 2, "profiles": [] }""");
        string malformed = CreatePack("{ not-json }");
        string oversized = CreatePack(new string(' ',
            SoftParticleProfileLoader.MaximumManifestBytes + 1));
        try
        {
            SoftParticleProfileLoadResult noFile = SoftParticleProfileLoader.Load(missing);
            SoftParticleProfileLoadResult noVersion = SoftParticleProfileLoader.Load(missingVersion);
            SoftParticleProfileLoadResult unknown = SoftParticleProfileLoader.Load(unknownProperty);
            SoftParticleProfileLoadResult wrongVersion = SoftParticleProfileLoader.Load(unsupported);
            SoftParticleProfileLoadResult bad = SoftParticleProfileLoader.Load(malformed);
            SoftParticleProfileLoadResult tooLarge = SoftParticleProfileLoader.Load(oversized);

            Assert.Equal(SoftParticleManifestIssueKind.MissingManifest,
                Assert.Single(noFile.Issues).Kind);
            Assert.Equal(SoftParticleManifestIssueKind.MalformedManifest,
                Assert.Single(noVersion.Issues).Kind);
            Assert.Equal(SoftParticleManifestIssueKind.MalformedManifest,
                Assert.Single(unknown.Issues).Kind);
            Assert.Equal(SoftParticleManifestIssueKind.UnsupportedManifestVersion,
                Assert.Single(wrongVersion.Issues).Kind);
            Assert.Equal(SoftParticleManifestIssueKind.MalformedManifest,
                Assert.Single(bad.Issues).Kind);
            Assert.Equal(SoftParticleManifestIssueKind.ManifestTooLarge,
                Assert.Single(tooLarge.Issues).Kind);
            Assert.Equal(0, noFile.Catalog.Count);
            Assert.Equal(0, noVersion.Catalog.Count);
            Assert.Equal(0, unknown.Catalog.Count);
            Assert.Equal(0, wrongVersion.Catalog.Count);
            Assert.Equal(0, bad.Catalog.Count);
            Assert.Equal(0, tooLarge.Catalog.Count);
        }
        finally
        {
            Directory.Delete(missing, recursive: true);
            Directory.Delete(missingVersion, recursive: true);
            Directory.Delete(unknownProperty, recursive: true);
            Directory.Delete(unsupported, recursive: true);
            Directory.Delete(malformed, recursive: true);
            Directory.Delete(oversized, recursive: true);
        }
    }

    [Fact]
    public void ManifestProfileCountIsBoundedAndFailsClosed()
    {
        string entries = String.Join(",", Enumerable.Range(1,
            SoftParticleProfileLoader.MaximumProfiles + 1).Select(index =>
                $$"""{"key":"effect/{{index}}/element/0","fadeDistance":1}"""));
        string root = CreatePack($$"""
            {"format":1,"profiles":[{{entries}}]}
            """);
        try
        {
            SoftParticleProfileLoadResult loaded = SoftParticleProfileLoader.Load(root);

            Assert.Equal(0, loaded.Catalog.Count);
            Assert.Equal(SoftParticleManifestIssueKind.TooManyProfiles,
                Assert.Single(loaded.Issues).Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RuntimeAdmissionUsesOnlyEffectElementIdentityAndClearsPoolState()
    {
        string root = FindRepositoryRoot();
        string renderer = File.ReadAllText(Path.Combine(root,
            "src", "Client.Presentation", "Rendering", "Renderer.cs"));
        string effects = File.ReadAllText(Path.Combine(root,
            "src", "Client.Presentation", "Rendering", "EffectPresentation.cs"));
        Assert.Contains("SetSoftParticleProfile(entry, effect.Id, elementIndex)",
            renderer, StringComparison.Ordinal);
        Assert.Contains("ClearSoftParticleProfile(element)", renderer,
            StringComparison.Ordinal);
        Assert.Contains("softParticleProfile: scene.GetSoftParticleProfile(particle.Owner)",
            effects, StringComparison.Ordinal);
        Assert.DoesNotContain("softParticleProfile:", effects[..effects.IndexOf(
            "public static void AddRenderItem(this EffectParticle", StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    private static string CreatePack(string manifest)
    {
        string root = Path.Combine(Path.GetTempPath(), $"prime-soft-particle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SoftParticleProfileLoader.ManifestFileName), manifest);
        return root;
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
