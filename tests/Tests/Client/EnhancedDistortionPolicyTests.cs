using System;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedDistortionPolicyTests
{
    private static readonly object _geometry = new();
    private static readonly EnhancedDistortionFormatCapabilities _allSamples = new(
        ColorTarget: true, Sampled: true,
        EnhancedDistortionSampleCounts.One | EnhancedDistortionSampleCounts.Two
            | EnhancedDistortionSampleCounts.Four | EnhancedDistortionSampleCounts.Eight);

    [Fact]
    public void SubmissionValidatesFiniteBoundedPresentationData()
    {
        EnhancedDistortionSubmission field = EnhancedDistortionSubmission.FromForceField(
            7, _geometry, Matrix4.Identity, EnhancedForceFieldProfiles.Default,
            TimeSpan.FromSeconds(12.25));

        Assert.Equal(7UL, field.StableKey);
        Assert.Same(_geometry, field.GeometryIdentity);
        Assert.InRange(field.Strength, float.Epsilon,
            EnhancedDistortionSubmission.MaximumStrength);
        Assert.InRange(field.Falloff, float.Epsilon,
            EnhancedDistortionSubmission.MaximumFalloff);
        Assert.InRange(field.PresentationPhase, 0, .99999999f);
        Assert.Equal(field, EnhancedDistortionSubmission.FromForceField(
            7, _geometry, Matrix4.Identity, EnhancedForceFieldProfiles.Default,
            TimeSpan.FromSeconds(12.25)));

        Assert.Throws<ArgumentNullException>(() => new EnhancedDistortionSubmission(
            1, null!, Matrix4.Identity, .01f, 2, .25f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            New(1, transform: Matrix4.CreateScale(float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => New(1, strength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            New(1, strength: EnhancedDistortionSubmission.MaximumStrength + .001f));
        Assert.Throws<ArgumentOutOfRangeException>(() => New(1, falloff: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => New(1, phase: 1));
    }

    [Fact]
    public void AdmissionAndOrderingAreDeterministicAcrossProducerOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EnhancedDistortionSubmissionBuffer(
                EnhancedDistortionSubmission.MaximumCount + 1));

        ulong[] forward = Enumerable.Range(1, 160).Select(value => (ulong)value).ToArray();
        ulong[] reverse = forward.Reverse().ToArray();

        EnhancedDistortionSubmissionBatch first = Build(forward);
        EnhancedDistortionSubmissionBatch second = Build(reverse);

        Assert.Equal(EnhancedDistortionSubmission.MaximumCount, first.Count);
        Assert.Equal(first.Items.Select(item => item.StableKey),
            second.Items.Select(item => item.StableKey));
        Assert.Equal(Enumerable.Range(1, EnhancedDistortionSubmission.MaximumCount)
            .Select(value => (ulong)value), first.Items.Select(item => item.StableKey));
    }

    [Fact]
    public void DuplicatePolicyRejectsExactAndFailsOnConflictingSource()
    {
        var buffer = new EnhancedDistortionSubmissionBuffer(2);
        EnhancedDistortionSubmission source = New(4);

        Assert.True(buffer.TryAdd(source));
        Assert.False(buffer.TryAdd(source));
        Assert.Throws<InvalidOperationException>(() =>
            buffer.TryAdd(New(4, strength: .02f)));
        Assert.Throws<ArgumentException>(() => buffer.TryAdd(default));
    }

    [Fact]
    public void SealedBatchDoesNotChangeWhenReusableBufferIsCleared()
    {
        var buffer = new EnhancedDistortionSubmissionBuffer(2);
        buffer.TryAdd(New(2));
        buffer.TryAdd(New(1));
        EnhancedDistortionSubmissionBatch batch = buffer.Seal();

        buffer.Clear();

        Assert.Empty(buffer.Seal().Items);
        Assert.Equal(new ulong[] { 1, 2 }, batch.Items.Select(item => item.StableKey));
    }

    [Fact]
    public void NoSourcesUseNeutralBypassWithoutTargetWork()
    {
        EnhancedDistortionTargetPlan target = EnhancedDistortionTargetPlan.Create(
            1920, 1080, 4, requested: true, sourceCount: 0,
            new EnhancedDistortionCapabilities(_allSamples, _allSamples));
        EnhancedDistortionPassPlan passes = EnhancedDistortionPassPlan.Create(target.Enabled);

        Assert.False(target.Enabled);
        Assert.Equal(EnhancedDistortionDisabledReason.NoSources, target.DisabledReason);
        Assert.Null(target.Configuration);
        Assert.Equal(Vector2.Zero, EnhancedDistortionTargetPlan.NeutralOffset);
        Vector2 clamped = EnhancedDistortionTargetPlan.ClampCompositeOffset(
            new Vector2(.3f, .4f));
        Assert.Equal(.03f, clamped.X, precision: 6);
        Assert.Equal(.04f, clamped.Y, precision: 6);
        Assert.False(passes.UsesDistortion);
        Assert.Equal(new[] { EnhancedDistortionPassKind.Bloom,
            EnhancedDistortionPassKind.ToneMap }, passes.Passes);

        EnhancedDistortionTargetPlan notRequested = EnhancedDistortionTargetPlan.Create(
            1920, 1080, 4, requested: false, sourceCount: 1,
            new EnhancedDistortionCapabilities(_allSamples, _allSamples));
        Assert.Equal(EnhancedDistortionDisabledReason.NotRequested,
            notRequested.DisabledReason);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedDistortionTargetPlan.Create(1920, 1080, 4, requested: true,
                sourceCount: EnhancedDistortionSubmission.MaximumCount + 1,
                new EnhancedDistortionCapabilities(_allSamples, _allSamples)));
    }

    [Fact]
    public void TargetPrefersRg16AndMatchesSceneDepthSamplesBeforeResolve()
    {
        EnhancedDistortionTargetPlan plan = EnhancedDistortionTargetPlan.Create(
            1600, 900, 4, requested: true, sourceCount: 3,
            new EnhancedDistortionCapabilities(_allSamples, _allSamples));

        Assert.True(plan.Enabled);
        EnhancedDistortionTargetConfiguration configuration = plan.Configuration!.Value;
        Assert.Equal(EnhancedDistortionTargetFormat.Rg16Float, configuration.Format);
        Assert.Equal(4, configuration.RenderSampleCount);
        Assert.Equal(1, configuration.ResolveSampleCount);
        Assert.True(configuration.RequiresResolve);

        EnhancedDistortionTargetPlan single = EnhancedDistortionTargetPlan.Create(
            1600, 900, 1, requested: true, sourceCount: 1,
            new EnhancedDistortionCapabilities(_allSamples, _allSamples));
        Assert.False(single.Configuration!.Value.RequiresResolve);
        Assert.Equal(1, single.Configuration.Value.RenderSampleCount);
    }

    [Fact]
    public void FormatAndSampleFallbackChooseRgbaThenDisable()
    {
        var oneOnly = new EnhancedDistortionFormatCapabilities(true, true,
            EnhancedDistortionSampleCounts.One);
        EnhancedDistortionTargetPlan rgba = EnhancedDistortionTargetPlan.Create(
            1280, 720, 4, requested: true, sourceCount: 1,
            new EnhancedDistortionCapabilities(oneOnly, _allSamples));
        Assert.Equal(EnhancedDistortionTargetFormat.Rgba16Float,
            rgba.Configuration!.Value.Format);

        EnhancedDistortionTargetPlan off = EnhancedDistortionTargetPlan.Create(
            1280, 720, 4, requested: true, sourceCount: 1,
            new EnhancedDistortionCapabilities(oneOnly, oneOnly));
        Assert.False(off.Enabled);
        Assert.Equal(EnhancedDistortionDisabledReason.UnsupportedFormatOrSampleCount,
            off.DisabledReason);

        var notSampled = new EnhancedDistortionFormatCapabilities(true, false,
            EnhancedDistortionSampleCounts.One | EnhancedDistortionSampleCounts.Four);
        EnhancedDistortionTargetPlan samplerOff = EnhancedDistortionTargetPlan.Create(
            1280, 720, 4, requested: true, sourceCount: 1,
            new EnhancedDistortionCapabilities(notSampled, notSampled));
        Assert.False(samplerOff.Enabled);
    }

    [Fact]
    public void FailureCacheFallsBackAndRetriesOnlyForNewResourceScope()
    {
        var capabilities = new EnhancedDistortionCapabilities(_allSamples, _allSamples);
        var cache = new EnhancedDistortionFailureCache();
        EnhancedDistortionTargetPlan preferred = EnhancedDistortionTargetPlan.Create(
            1920, 1080, 4, true, 1, capabilities, cache);
        EnhancedDistortionTargetConfiguration rg = preferred.Configuration!.Value;

        cache.RecordFailure(rg);
        EnhancedDistortionTargetPlan fallback = EnhancedDistortionTargetPlan.Create(
            1920, 1080, 4, true, 1, capabilities, cache);
        EnhancedDistortionTargetConfiguration rgba = fallback.Configuration!.Value;
        Assert.Equal(EnhancedDistortionTargetFormat.Rgba16Float, rgba.Format);
        Assert.True(cache.Contains(rg));

        cache.RecordFailure(rgba);
        EnhancedDistortionTargetPlan cachedOff = EnhancedDistortionTargetPlan.Create(
            1920, 1080, 4, true, 1, capabilities, cache);
        Assert.False(cachedOff.Enabled);
        Assert.Equal(EnhancedDistortionDisabledReason.CachedAllocationFailure,
            cachedOff.DisabledReason);
        Assert.Equal(2, cache.FailureCount);

        EnhancedDistortionTargetPlan resized = EnhancedDistortionTargetPlan.Create(
            1600, 900, 4, true, 1, capabilities, cache);
        Assert.True(resized.Enabled);
        Assert.Equal(EnhancedDistortionTargetFormat.Rg16Float,
            resized.Configuration!.Value.Format);
        Assert.Equal(0, cache.FailureCount);

        cache.RecordFailure(resized.Configuration.Value);
        EnhancedDistortionTargetPlan sampleChanged = EnhancedDistortionTargetPlan.Create(
            1600, 900, 2, true, 1, capabilities, cache);
        Assert.Equal(EnhancedDistortionTargetFormat.Rg16Float,
            sampleChanged.Configuration!.Value.Format);
        Assert.Equal(0, cache.FailureCount);

        cache.RecordFailure(sampleChanged.Configuration.Value);
        cache.ResetForDeviceLoss();
        Assert.True(EnhancedDistortionTargetPlan.Create(
            1600, 900, 2, true, 1, capabilities, cache).Enabled);
    }

    [Fact]
    public void PassContractRequiresWarpBeforeBloomAndToneMap()
    {
        EnhancedDistortionPassPlan plan = EnhancedDistortionPassPlan.Create(true);
        Assert.Equal(new[]
        {
            EnhancedDistortionPassKind.VectorRaster,
            EnhancedDistortionPassKind.SceneWarp,
            EnhancedDistortionPassKind.Bloom,
            EnhancedDistortionPassKind.ToneMap
        }, plan.Passes);
        Assert.True(plan.UsesDistortion);

        Assert.Throws<InvalidOperationException>(() => EnhancedDistortionPassPlan.Validate(
            new[] { EnhancedDistortionPassKind.VectorRaster,
                EnhancedDistortionPassKind.Bloom,
                EnhancedDistortionPassKind.SceneWarp,
                EnhancedDistortionPassKind.ToneMap }));
        Assert.Throws<InvalidOperationException>(() => EnhancedDistortionPassPlan.Validate(
            new[] { EnhancedDistortionPassKind.SceneWarp,
                EnhancedDistortionPassKind.Bloom,
                EnhancedDistortionPassKind.ToneMap }));
        Assert.Throws<InvalidOperationException>(() => EnhancedDistortionPassPlan.Validate(
            new[] { EnhancedDistortionPassKind.Bloom,
                EnhancedDistortionPassKind.ToneMap,
                EnhancedDistortionPassKind.ToneMap }));
    }

    private static EnhancedDistortionSubmissionBatch Build(ulong[] keys)
    {
        var buffer = new EnhancedDistortionSubmissionBuffer();
        foreach (ulong key in keys) buffer.TryAdd(New(key));
        return buffer.Seal();
    }

    private static EnhancedDistortionSubmission New(ulong key,
        object? geometry = null, Matrix4? transform = null,
        float strength = .01f, float falloff = 2, float phase = .25f)
        => new(key, geometry ?? _geometry, transform ?? Matrix4.Identity,
            strength, falloff, phase);
}
