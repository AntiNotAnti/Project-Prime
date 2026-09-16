using MphRead;
using SDL;
using Xunit;

namespace ProjectPrime.Renderer.Tests;

public sealed unsafe class SdlGpuPassBindingCacheTests
{
    [Fact]
    public void ResetMakesTheFirstPipelineAndSamplerBindingsNecessaryAgain()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[1];
        bindings[0] = Binding(0x100, 0x200);

        Assert.True(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.True(cache.ShouldBindFragmentSamplers(0, 1, bindings));
        Assert.False(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.False(cache.ShouldBindFragmentSamplers(0, 1, bindings));

        cache.Reset();

        Assert.True(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.True(cache.ShouldBindFragmentSamplers(0, 1, bindings));
    }

    [Fact]
    public void DescriptorBearingMarkerIsConsumedAndResetForEachPass()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[1];
        bindings[0] = Binding(0x100, 0x200);

        Assert.True(cache.ConsumeDescriptorBearing());
        Assert.False(cache.ConsumeDescriptorBearing());
        Assert.True(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.True(cache.ConsumeDescriptorBearing());
        Assert.False(cache.ConsumeDescriptorBearing());
        Assert.True(cache.ShouldBindFragmentSamplers(0, 1, bindings));
        Assert.True(cache.ConsumeDescriptorBearing());
        Assert.False(cache.ConsumeDescriptorBearing());

        Assert.False(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.False(cache.ShouldBindFragmentSamplers(0, 1, bindings));
        Assert.False(cache.ConsumeDescriptorBearing());

        Assert.True(cache.ShouldBindPipeline(Pipeline(0x20)));
        Assert.True(cache.ConsumeDescriptorBearing());
        Assert.False(cache.ConsumeDescriptorBearing());

        cache.Reset();
        Assert.True(cache.ConsumeDescriptorBearing());
        Assert.False(cache.ConsumeDescriptorBearing());
    }

    [Fact]
    public void PipelineBindingSkipsOnlyAnIdenticalPipelineAndPreservesSamplers()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[1];
        bindings[0] = Binding(0x100, 0x200);

        Assert.True(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.False(cache.ShouldBindPipeline(Pipeline(0x10)));
        Assert.True(cache.ShouldBindPipeline(Pipeline(0x20)));
        Assert.False(cache.ShouldBindPipeline(Pipeline(0x20)));

        Assert.True(cache.ShouldBindFragmentSamplers(0, 1, bindings));
        Assert.True(cache.ShouldBindPipeline(Pipeline(0x30)));
        Assert.False(cache.ShouldBindFragmentSamplers(0, 1, bindings));
    }

    [Fact]
    public void SamplerBindingIdentityIncludesFirstSlotAndCount()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        const int count = SdlGpuPassBindingCache.MaximumSamplerBindingCount;
        SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[count];
        Fill(bindings, count, 0x100, 0x200);

        Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
        Assert.False(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
        Assert.True(cache.ShouldBindFragmentSamplers(1, CountAsUInt(count), bindings));
        Assert.False(cache.ShouldBindFragmentSamplers(1, CountAsUInt(count), bindings));
        Assert.True(cache.ShouldBindFragmentSamplers(1, CountAsUInt(count - 1), bindings));
        Assert.False(cache.ShouldBindFragmentSamplers(1, CountAsUInt(count - 1), bindings));
    }

    [Fact]
    public void SamplerBindingIdentityComparesEveryTextureAndSamplerIncludingPaddedSlot()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        const int count = SdlGpuPassBindingCache.MaximumSamplerBindingCount;
        SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[count];
        Fill(bindings, count, 0x100, 0x200);

        Assert.Equal(8, count);
        Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
        Assert.False(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));

        for (int index = 0; index < count; index++)
        {
            SDL_GPUTextureSamplerBinding saved = bindings[index];
            bindings[index] = Binding(0x1000 + index, 0x2000 + index);
            Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
            Assert.False(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
            bindings[index] = saved;
            Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), bindings));
        }
    }

    [Fact]
    public void SamplerHandlesAreCopiedRatherThanRetainingTheCallerStorage()
    {
        SdlGpuPassBindingCache cache = default;
        cache.Reset();
        const int count = SdlGpuPassBindingCache.MaximumSamplerBindingCount;
        SDL_GPUTextureSamplerBinding* first = stackalloc SDL_GPUTextureSamplerBinding[count];
        SDL_GPUTextureSamplerBinding* second = stackalloc SDL_GPUTextureSamplerBinding[count];
        Fill(first, count, 0x100, 0x200);
        Fill(second, count, 0x100, 0x200);

        Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), first));
        Assert.False(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), second));

        first[0] = Binding(0x300, 0x400);
        Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), first));
        Assert.True(cache.ShouldBindFragmentSamplers(0, CountAsUInt(count), second));
    }

    private static uint CountAsUInt(int count) => checked((uint)count);

    private static void Fill(SDL_GPUTextureSamplerBinding* bindings, int count,
        nint textureBase, nint samplerBase)
    {
        for (int index = 0; index < count; index++)
            bindings[index] = Binding(textureBase + index, samplerBase + index);
    }

    private static SDL_GPUTextureSamplerBinding Binding(nint texture, nint sampler)
        => new()
        {
            texture = (SDL_GPUTexture*)texture,
            sampler = (SDL_GPUSampler*)sampler
        };

    private static SDL_GPUGraphicsPipeline* Pipeline(nint handle)
        => (SDL_GPUGraphicsPipeline*)handle;
}
