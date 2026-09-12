using System;
using System.IO;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedEnvironmentTests
{
    [Fact]
    public void LegacyDefaultsPreserveColorAndFarPlaneFogOpacity()
    {
        var legacy = new LegacyRoomEnvironment(true, new Vector3(0.25f, 0.5f, 0.75f),
            fogSlope: 5, fogOffset: 32384, farClip: 400);

        EnhancedEnvironment environment = EnhancedEnvironment.FromLegacy(legacy);
        float fogMinimum = legacy.FogOffset / (float)0x7FFF;
        float fogMaximum = (legacy.FogOffset + 32 * (0x400 >> legacy.FogSlope))
            / (float)0x7FFF;
        float legacyFarOpacity = Math.Clamp((1 - fogMinimum)
            / (fogMaximum - fogMinimum), 0, 0.95f);
        float enhancedFarOpacity = 1 - MathF.Exp(-legacy.FarClip * environment.FogDensity);

        Assert.Equal(legacy.FogColor, environment.FogColor);
        Assert.Equal(legacyFarOpacity, enhancedFarOpacity, precision: 5);
        Assert.Equal(0, environment.FogHeight);
        Assert.Equal(0, environment.FogFalloff);
        Assert.Equal(1, environment.Exposure);
        Assert.Null(environment.LutKey);
        Assert.Null(environment.ReflectionProbeKey);
        Assert.Equal(0, EnhancedEnvironment.FromLegacy(new LegacyRoomEnvironment(false,
            Vector3.One, 5, 32384, 400)).FogDensity);
    }

    [Fact]
    public void RoomMetadataDefaultsUseExistingCatalogValues()
    {
        RoomMetadata room = Assert.IsType<RoomMetadata>(Metadata.GetRoomById(102, noThrow: true));
        EnhancedEnvironment environment = EnhancedEnvironment.FromRoomMetadata(room);

        Assert.Equal(new Vector3(room.FogColor.Red / 31f, room.FogColor.Green / 31f,
            room.FogColor.Blue / 31f), environment.FogColor);
        Assert.Equal(room.FogEnabled, environment.FogDensity > 0);
        Assert.True(environment.IsInitialized);
    }

    [Fact]
    public void EnvironmentAndLegacyInputRejectUnboundedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LegacyRoomEnvironment(true,
            new Vector3(float.NaN), 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LegacyRoomEnvironment(true,
            Vector3.One, 31, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnhancedEnvironment(
            Vector3.One, float.PositiveInfinity, 0, 0, 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnhancedEnvironment(
            Vector3.One, 0, 0, 0, 100, null, null));
        Assert.Throws<ArgumentException>(() => EnhancedEnvironment.FromLegacy(default));
        Assert.Throws<ArgumentException>(() => new EnhancedEnvironment(
            Vector3.One, 0, 0, 0, 1, default(EnhancedEnvironmentAssetKey), null));
    }

    [Theory]
    [InlineData("luts/alinos.png", true)]
    [InlineData("probes/archive_01.ktx", true)]
    [InlineData("../outside.png", false)]
    [InlineData("luts/../../outside.png", false)]
    [InlineData("/absolute/lut.png", false)]
    [InlineData("C:\\absolute\\lut.png", false)]
    [InlineData("luts//bad.png", false)]
    public void EnvironmentAssetKeysAreCanonicalAndPathSafe(string value, bool valid)
    {
        bool parsed = EnhancedEnvironmentAssetKey.TryParse(value,
            out EnhancedEnvironmentAssetKey key);
        Assert.Equal(valid, parsed);
        Assert.Equal(valid, key.IsValid);
        if (valid)
        {
            Assert.Equal(value, key.Value);
        }
    }

    [Fact]
    public void LoaderAppliesValidRoomOverrideAndFallsBackForUnknownRoom()
    {
        string root = CreatePack("""
            {
              "format": 1,
              "rooms": [
                {
                  "room": "MP9 CRYOCHASM",
                  "fogColor": [0.1, 0.4, 0.8],
                  "fogDensity": 0.025,
                  "fogHeight": -2,
                  "fogFalloff": 0.4,
                  "exposure": 1.1,
                  "lutKey": "luts/cryochasm.png",
                  "reflectionProbeKey": "probes/cryochasm.ktx"
                  ,"primaryShadowLightIndex": 1
                }
              ]
            }
            """);
        try
        {
            EnhancedEnvironmentLoadResult loaded = EnhancedEnvironmentOverrideLoader.Load(root);
            EnhancedEnvironment fallback = EnhancedEnvironment.Neutral;
            EnhancedEnvironment resolved = loaded.Overrides.Resolve("MP9 CRYOCHASM", fallback);

            Assert.Empty(loaded.Issues);
            Assert.Equal(1, loaded.Overrides.Count);
            Assert.Equal(new Vector3(0.1f, 0.4f, 0.8f), resolved.FogColor);
            Assert.Equal(0.025f, resolved.FogDensity);
            Assert.Equal(-2, resolved.FogHeight);
            Assert.Equal(0.4f, resolved.FogFalloff);
            Assert.Equal(1.1f, resolved.Exposure);
            Assert.Equal("luts/cryochasm.png", resolved.LutKey!.Value.Value);
            Assert.Equal("probes/cryochasm.ktx", resolved.ReflectionProbeKey!.Value.Value);
            Assert.Equal(1, resolved.PrimaryShadowLightIndex);
            Assert.Equal(fallback, loaded.Overrides.Resolve("unknown", fallback));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidPrimaryShadowOverrideFailsSoft()
    {
        string root = CreatePack("""
            { "format": 1, "rooms": [
              { "room": "invalid", "primaryShadowLightIndex": 2 }
            ] }
            """);
        try
        {
            EnhancedEnvironmentLoadResult loaded = EnhancedEnvironmentOverrideLoader.Load(root);
            Assert.Equal(0, loaded.Overrides.Count);
            Assert.Equal(EnhancedEnvironmentIssueKind.InvalidRoom,
                Assert.Single(loaded.Issues).Kind);
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnhancedEnvironment(
                Vector3.Zero, 0, 0, 0, 1, null, null, 2));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InvalidAndDuplicateRoomsAreIgnoredWithoutLosingValidEntries()
    {
        string root = CreatePack("""
            {
              "rooms": [
                { "room": "valid", "exposure": 1.25 },
                { "room": "unsafe", "lutKey": "../escape.png" },
                { "room": "duplicate", "fogDensity": 0.1 },
                { "room": "duplicate", "fogDensity": 0.2 }
              ]
            }
            """);
        try
        {
            EnhancedEnvironmentLoadResult loaded = EnhancedEnvironmentOverrideLoader.Load(root);

            Assert.Equal(1, loaded.Overrides.Count);
            Assert.Equal(1.25f,
                loaded.Overrides.Resolve("valid", EnhancedEnvironment.Neutral).Exposure);
            Assert.Equal(EnhancedEnvironment.Neutral,
                loaded.Overrides.Resolve("unsafe", EnhancedEnvironment.Neutral));
            Assert.Equal(EnhancedEnvironment.Neutral,
                loaded.Overrides.Resolve("duplicate", EnhancedEnvironment.Neutral));
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == EnhancedEnvironmentIssueKind.InvalidRoom
                    && issue.RoomKey == "unsafe");
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == EnhancedEnvironmentIssueKind.DuplicateRoom
                    && issue.RoomKey == "duplicate");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoaderFailsSoftForMissingMalformedAndOversizedManifest()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), $"prime-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(missingRoot);
        string malformedRoot = CreatePack("{ not-json }");
        string oversizedRoot = CreatePack(new string(' ',
            EnhancedEnvironmentOverrideLoader.MaximumManifestBytes + 1));
        try
        {
            EnhancedEnvironmentLoadResult missing
                = EnhancedEnvironmentOverrideLoader.Load(missingRoot);
            EnhancedEnvironmentLoadResult malformed
                = EnhancedEnvironmentOverrideLoader.Load(malformedRoot);
            EnhancedEnvironmentLoadResult oversized
                = EnhancedEnvironmentOverrideLoader.Load(oversizedRoot);

            Assert.Equal(EnhancedEnvironmentIssueKind.MissingManifest,
                Assert.Single(missing.Issues).Kind);
            Assert.Equal(EnhancedEnvironmentIssueKind.MalformedManifest,
                Assert.Single(malformed.Issues).Kind);
            Assert.Equal(EnhancedEnvironmentIssueKind.ManifestTooLarge,
                Assert.Single(oversized.Issues).Kind);
            Assert.Equal(0, missing.Overrides.Count);
            Assert.Equal(0, malformed.Overrides.Count);
            Assert.Equal(0, oversized.Overrides.Count);
        }
        finally
        {
            Directory.Delete(missingRoot, recursive: true);
            Directory.Delete(malformedRoot, recursive: true);
            Directory.Delete(oversizedRoot, recursive: true);
        }
    }

    private static string CreatePack(string manifest)
    {
        string root = Path.Combine(Path.GetTempPath(), $"prime-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, EnhancedEnvironmentOverrideLoader.ManifestFileName),
            manifest);
        return root;
    }
}
