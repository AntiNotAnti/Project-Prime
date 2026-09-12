using System;
using System.IO;
using System.Linq;
using MphRead.Mods;
using SDL;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class EnhancedSkyRuntimeTests
{
    [Fact]
    public void RoomSelectionUsesOnlyTheCanonicalDefaultKey()
    {
        Assert.True(EnhancedSkyRuntimePolicy.TryCreateRoomDefaultKey("MP1",
            out EnhancedSkyAssetKey key));
        Assert.Equal("room/mp1/sky/default", key.Value);

        Assert.False(EnhancedSkyRuntimePolicy.TryCreateRoomDefaultKey("mp1/sky/night",
            out _));
        Assert.False(EnhancedSkyRuntimePolicy.TryCreateRoomDefaultKey("../mp1", out _));
        Assert.False(EnhancedSkyRuntimePolicy.TryCreateRoomDefaultKey(null, out _));
    }

    [Fact]
    public void CubemapWinsAndFrameCapturesEveryUsedTextureInCompositionOrder()
    {
        EnhancedSkyTextureAsset background = Texture("background.png", 1);
        EnhancedSkyTextureAsset[] faces = Enumerable.Range(0, 6)
            .Select(index => Texture($"face-{index}.png", (byte)(10 + index)))
            .ToArray();
        EnhancedSkyTextureAsset nebula = Texture("nebula.png", 30);
        EnhancedSkyTextureAsset stars = Texture("stars.png", 40);
        EnhancedSkyTextureAsset far = Texture("far.png", 50);
        EnhancedSkyTextureAsset near = Texture("near.png", 60);
        var replacement = new EnhancedSkyReplacement(
            EnhancedSkyAssetKey.ForRoom("mp1"), background,
            new EnhancedSkyCubemap(faces[0], faces[1], faces[2], faces[3],
                faces[4], faces[5]),
            new[]
            {
                new EnhancedSkyAnimatedLayer(far, -2, .01f, 0, 0, .5f, 1),
                new EnhancedSkyAnimatedLayer(near, 3, 0, .02f, .03f, .75f, 2)
            },
            new EnhancedSkyStars(stars, 1.5f, 2, .04f),
            new EnhancedSkyNebula(nebula, .6f, 1.25f, .01f, -.01f, .02f));

        RenderSkyState state = Assert.IsType<RenderSkyState>(
            RenderSkyState.FromReplacement(replacement, TimeSpan.FromSeconds(12.5)));
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        frame.CaptureSky(state);

        Assert.Equal(EnhancedSkyBaseKind.Cubemap, state.BaseKind);
        Assert.Null(state.Background);
        Assert.Equal(12.5f, state.TimeSeconds);
        Assert.False(state.SelectiveBloom);
        Assert.Equal(new[]
        {
            EnhancedSkyCompositionKind.Nebula,
            EnhancedSkyCompositionKind.Stars,
            EnhancedSkyCompositionKind.AuthoredLayer,
            EnhancedSkyCompositionKind.AuthoredLayer
        }, state.Composition.Select(layer => layer.Kind));
        Assert.All(state.Composition, layer =>
        {
            Assert.Equal(EnhancedSkyOverlayBlend.StraightAlpha, layer.Blend);
            Assert.False(layer.SelectiveBloom);
        });
        Assert.Equal(10, state.TextureIdentities.Count);
        Assert.DoesNotContain(background.TextureIdentity, state.TextureIdentities);
        Assert.Equal(state.TextureIdentities.Count, frame.TextureResources.Count);
        Assert.All(state.TextureIdentities,
            identity => Assert.True(frame.TextureResources.ContainsKey(identity)));

        frame.Reset();
        Assert.Null(frame.Sky);
        Assert.Empty(frame.TextureResources);
    }

    [Fact]
    public void InvalidTimeAndMissingBaseFailSoft()
    {
        EnhancedSkyTextureAsset background = Texture("background.png", 1);
        var valid = new EnhancedSkyReplacement(EnhancedSkyAssetKey.ForRoom("mp1"),
            background, null, Array.Empty<EnhancedSkyAnimatedLayer>(), null, null);
        var missing = new EnhancedSkyReplacement(EnhancedSkyAssetKey.ForRoom("mp2"),
            null, null, Array.Empty<EnhancedSkyAnimatedLayer>(), null, null);

        Assert.Null(RenderSkyState.FromReplacement(null, TimeSpan.Zero));
        Assert.Null(RenderSkyState.FromReplacement(valid, TimeSpan.FromTicks(-1)));
        Assert.Null(RenderSkyState.FromReplacement(missing, TimeSpan.Zero));
    }

    [Fact]
    public void BackendPolicyIsEnhancedOnlyAndOpaqueClearTracksSuccessfulEncoding()
    {
        EnhancedSkyTextureAsset background = Texture("background.png", 1);
        var replacement = new EnhancedSkyReplacement(
            EnhancedSkyAssetKey.ForRoom("mp1"), background, null,
            Array.Empty<EnhancedSkyAnimatedLayer>(), null, null);
        RenderSkyState state = Assert.IsType<RenderSkyState>(
            RenderSkyState.FromReplacement(replacement, TimeSpan.Zero));

        Assert.True(SdlGpuSkyPolicy.IsEligible(state, GraphicsPreset.Enhanced));
        Assert.False(SdlGpuSkyPolicy.IsEligible(state, GraphicsPreset.Original));
        Assert.False(SdlGpuSkyPolicy.IsEligible(state, GraphicsPreset.Performance));
        Assert.False(SdlGpuSkyPolicy.IsEligible(null, GraphicsPreset.Enhanced));
        Assert.False(SdlGpuSkyPolicy.OpaqueClearsColor(skyEncoded: true));
        Assert.True(SdlGpuSkyPolicy.OpaqueClearsColor(skyEncoded: false));
    }

    [Fact]
    public void PreparationFailureIsCachedOnlyForStableSkyTargetConfiguration()
    {
        var cache = new SdlGpuConfigurationFailureCache<SdlGpuSkyConfiguration>();
        EnhancedSkyAssetKey key = EnhancedSkyAssetKey.ForRoom("mp1");
        var failed = new SdlGpuSkyConfiguration(key,
            SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT, 1);

        Assert.True(cache.ShouldAttempt(failed));
        cache.RecordFailure(failed);
        Assert.False(cache.ShouldAttempt(failed));
        Assert.True(cache.ShouldAttempt(failed with { SampleCount = 2 }));
        Assert.True(cache.ShouldAttempt(failed with
        {
            Key = EnhancedSkyAssetKey.ForRoom("mp2")
        }));
        cache.RecordSuccess();
        Assert.True(cache.ShouldAttempt(failed));
    }

    [Fact]
    public void CanonicalShaderRemovesCameraTranslationAndHasNoBloomOutput()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "sky.hlsl"));

        Assert.Contains("inverseViewRotation", source, StringComparison.Ordinal);
        Assert.Contains("float4(viewDirection, 0.0f)", source,
            StringComparison.Ordinal);
        Assert.Contains("SampleCubeFaces", source, StringComparison.Ordinal);
        Assert.Contains("saturate(sampled.a * skyOptions.z)", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SV_Target1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bloomTexture", source, StringComparison.Ordinal);
    }

    private static EnhancedSkyTextureAsset Texture(string path, byte value)
        => new(path, 1, 1, ReadOnlySpan<byte>.Empty,
            new byte[] { value, value, value, 255 });
}
