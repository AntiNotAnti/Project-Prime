using System;
using System.Linq;
using MphRead;
using Xunit;

public sealed class BloomPyramidPolicyTests
{
    [Fact]
    public void PlanUsesFixedQuarterEighthAndSixteenthLevels()
    {
        BloomPyramidPlan plan = BloomPyramidPlan.Create(1920, 1080,
            BloomPyramidStorageFormat.Rgba16Float);

        Assert.Equal(BloomPyramidPlan.LevelCount, plan.Levels.Count);
        Assert.Collection(plan.Levels,
            level => AssertLevel(level, BloomPyramidLevel.Quarter, 4, 480, 270, .5f),
            level => AssertLevel(level, BloomPyramidLevel.Eighth, 8, 240, 135, .3f),
            level => AssertLevel(level, BloomPyramidLevel.Sixteenth, 16, 120, 68, .2f));
        Assert.Equal(plan.Levels.Sum(level => level.EstimatedBytes), plan.EstimatedBytes);
        Assert.Equal(2_722_560, plan.EstimatedBytes);
    }

    [Theory]
    [InlineData(1u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(3u, 5u, 1u, 2u, 1u, 1u)]
    [InlineData(1919u, 1079u, 480u, 270u, 240u, 135u)]
    public void OddAndMinimumDimensionsUseCeilingAndNeverReachZero(
        uint width, uint height, uint quarterWidth, uint quarterHeight,
        uint eighthWidth, uint eighthHeight)
    {
        BloomPyramidPlan plan = BloomPyramidPlan.Create(width, height,
            BloomPyramidStorageFormat.Rgba16Float);

        Assert.Equal((quarterWidth, quarterHeight),
            (plan.Levels[0].Width, plan.Levels[0].Height));
        Assert.Equal((eighthWidth, eighthHeight),
            (plan.Levels[1].Width, plan.Levels[1].Height));
        Assert.True(plan.Levels.All(level => level.Width >= 1 && level.Height >= 1));
    }

    [Fact]
    public void PassSequenceIsExactAndNeverReadsItsOutput()
    {
        BloomPyramidPlan plan = BloomPyramidPlan.Create(1919, 1079,
            BloomPyramidStorageFormat.Rgba16Float);

        Assert.Equal(BloomPyramidPlan.PassCount, plan.Passes.Count);
        Assert.Equal(new[]
        {
            BloomPyramidPassKind.Downsample,
            BloomPyramidPassKind.BlurHorizontal,
            BloomPyramidPassKind.BlurVertical,
            BloomPyramidPassKind.Downsample,
            BloomPyramidPassKind.BlurHorizontal,
            BloomPyramidPassKind.BlurVertical,
            BloomPyramidPassKind.Downsample,
            BloomPyramidPassKind.BlurHorizontal,
            BloomPyramidPassKind.BlurVertical,
            BloomPyramidPassKind.UpsampleCombine,
            BloomPyramidPassKind.UpsampleCombine,
            BloomPyramidPassKind.SceneComposite
        }, plan.Passes.Select(pass => pass.Kind));
        Assert.Equal(Enumerable.Range(0, BloomPyramidPlan.PassCount),
            plan.Passes.Select(pass => pass.Index));
        Assert.All(plan.Passes, pass => Assert.False(pass.HasReadWriteAlias));

        BloomPyramidPassDescriptor broad = plan.Passes[9];
        Assert.Equal(BloomPyramidResource.EighthA, broad.InputA);
        Assert.Equal(BloomPyramidResource.SixteenthA, broad.InputB);
        Assert.Equal(BloomPyramidResource.EighthB, broad.Output);
        Assert.Equal(.3f, broad.InputAWeight);
        Assert.Equal(.2f, broad.InputBWeight);
        BloomPyramidPassDescriptor sharp = plan.Passes[10];
        Assert.Equal(BloomPyramidResource.QuarterA, sharp.InputA);
        Assert.Equal(BloomPyramidResource.EighthB, sharp.InputB);
        Assert.Equal(BloomPyramidResource.QuarterB, sharp.Output);
        Assert.Equal(.5f, sharp.InputAWeight);
        Assert.Equal(1, sharp.InputBWeight);
    }

    [Fact]
    public void DownsampleFootprintIsFourToOneThenTwoToOne()
    {
        Assert.Equal(1, BloomDownsampleFootprint.SourceTexelOffset(1920, 480));
        Assert.Equal(.5f, BloomDownsampleFootprint.SourceTexelOffset(480, 240));
        Assert.Equal(.5f, BloomDownsampleFootprint.SourceTexelOffset(240, 120));
        Assert.Equal(.75f, BloomDownsampleFootprint.SourceTexelOffset(3, 1));
        Assert.InRange(BloomDownsampleFootprint.SourceTexelOffset(1919, 480),
            .999f, 1f);
    }

    [Fact]
    public void FourByFourFootprintPreservesTranslatedSinglePixelEmission()
    {
        const float expected = 1f / 16f;
        float offset = BloomDownsampleFootprint.SourceTexelOffset(4, 1);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var source = new float[4, 4];
                source[y, x] = 1;
                Assert.InRange(Downsample4Tap(source, offset),
                    expected - .00001f, expected + .00001f);
            }
        }

        var corner = new float[4, 4];
        corner[0, 0] = 1;
        Assert.Equal(0, Downsample4Tap(corner, .5f));
    }

    [Fact]
    public void FourByFourFootprintPreservesTranslatedThinLines()
    {
        const float expected = .25f;
        float offset = BloomDownsampleFootprint.SourceTexelOffset(4, 1);
        for (int translation = 0; translation < 4; translation++)
        {
            var vertical = new float[4, 4];
            var horizontal = new float[4, 4];
            for (int coordinate = 0; coordinate < 4; coordinate++)
            {
                vertical[coordinate, translation] = 1;
                horizontal[translation, coordinate] = 1;
            }
            Assert.InRange(Downsample4Tap(vertical, offset),
                expected - .00001f, expected + .00001f);
            Assert.InRange(Downsample4Tap(horizontal, offset),
                expected - .00001f, expected + .00001f);
        }

        var edgeLine = new float[4, 4];
        for (int y = 0; y < 4; y++) edgeLine[y, 0] = 1;
        Assert.Equal(0, Downsample4Tap(edgeLine, .5f));
    }

    [Fact]
    public void WeightsAndEstimatedAllocationAreBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BloomPyramidWeights.Create(float.NaN, .3f, .2f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BloomPyramidWeights.Create(.8f, .3f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BloomPyramidWeights.Create(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomPyramidPlan.Create(
            UInt32.MaxValue, UInt32.MaxValue, BloomPyramidStorageFormat.Rgba16Float));

        BloomPyramidPlan minimum = BloomPyramidPlan.Create(1, 1,
            BloomPyramidStorageFormat.Rgba16Float);
        Assert.Equal(48, minimum.EstimatedBytes);
        Assert.InRange(minimum.EstimatedBytes, 1, BloomPyramidPlan.MaximumEstimatedBytes);
    }

    [Fact]
    public void FailureCacheRetriesOnlyAfterResourceConfigurationChanges()
    {
        BloomPyramidPlan plan = BloomPyramidPlan.Create(1920, 1080,
            BloomPyramidStorageFormat.Rgba16Float);
        var cache = new BloomPyramidFailureCache();

        Assert.True(cache.ShouldAttempt(plan.Configuration,
            qualityRequested: true, frameHasEligibleEmission: true));
        cache.RecordFailure(plan.Configuration);
        Assert.False(cache.ShouldAttempt(plan.Configuration,
            qualityRequested: true, frameHasEligibleEmission: true));
        Assert.False(cache.ShouldAttempt(plan.Configuration,
            qualityRequested: true, frameHasEligibleEmission: false));
        Assert.False(cache.ShouldAttempt(plan.Configuration,
            qualityRequested: false, frameHasEligibleEmission: true));

        BloomPyramidConfiguration resized = BloomPyramidPlan.Create(1600, 900,
            BloomPyramidStorageFormat.Rgba16Float).Configuration;
        Assert.True(cache.ShouldAttempt(resized,
            qualityRequested: true, frameHasEligibleEmission: true));
        BloomPyramidConfiguration differentFormat = BloomPyramidPlan.Create(1920, 1080,
            BloomPyramidStorageFormat.Rgba8Unorm).Configuration;
        Assert.True(cache.ShouldAttempt(differentFormat,
            qualityRequested: true, frameHasEligibleEmission: true));
        BloomPyramidConfiguration differentWeights = BloomPyramidPlan.Create(1920, 1080,
            BloomPyramidStorageFormat.Rgba16Float,
            BloomPyramidWeights.Create(.6f, .25f, .15f)).Configuration;
        Assert.True(cache.ShouldAttempt(differentWeights,
            qualityRequested: true, frameHasEligibleEmission: true));
        cache.RecordSuccess();
        Assert.True(cache.ShouldAttempt(plan.Configuration,
            qualityRequested: true, frameHasEligibleEmission: true));
    }

    [Fact]
    public void EligibilityChangesDoNotBecomeAllocationConfigurations()
    {
        BloomPyramidConfiguration configuration = BloomPyramidPlan.Create(1280, 720,
            BloomPyramidStorageFormat.Rgba16Float).Configuration;
        var cache = new BloomPyramidFailureCache();
        cache.RecordFailure(configuration);

        foreach (bool eligible in new[] { true, false, true, false })
        {
            Assert.Equal(configuration, BloomPyramidPlan.Create(1280, 720,
                BloomPyramidStorageFormat.Rgba16Float).Configuration);
            Assert.False(cache.ShouldAttempt(configuration,
                qualityRequested: true, frameHasEligibleEmission: eligible));
        }

        // This cache has no HDR state or fallback output. Integration must
        // continue the already-selected HDR scene when bloom is suppressed.
        Assert.Equal(configuration, cache.FailedConfiguration);
    }

    private static void AssertLevel(BloomPyramidLevelDescriptor actual,
        BloomPyramidLevel level, uint denominator, uint width, uint height, float weight)
    {
        Assert.Equal(level, actual.Level);
        Assert.Equal(denominator, actual.ScaleDenominator);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
        Assert.Equal(weight, actual.Weight);
        Assert.NotEqual(actual.First, actual.Second);
        Assert.True(actual.EstimatedBytes > 0);
    }

    private static float Downsample4Tap(float[,] source,
        float sourceTexelOffset)
    {
        int height = source.GetLength(0);
        int width = source.GetLength(1);
        float offsetX = sourceTexelOffset / width;
        float offsetY = sourceTexelOffset / height;
        return (Bilinear(source, .5f - offsetX, .5f - offsetY)
            + Bilinear(source, .5f + offsetX, .5f - offsetY)
            + Bilinear(source, .5f - offsetX, .5f + offsetY)
            + Bilinear(source, .5f + offsetX, .5f + offsetY)) * .25f;
    }

    private static float Bilinear(float[,] source, float u, float v)
    {
        int height = source.GetLength(0);
        int width = source.GetLength(1);
        float x = u * width - .5f;
        float y = v * height - .5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float tx = x - x0;
        float ty = y - y0;
        int left = Math.Clamp(x0, 0, width - 1);
        int right = Math.Clamp(x0 + 1, 0, width - 1);
        int top = Math.Clamp(y0, 0, height - 1);
        int bottom = Math.Clamp(y0 + 1, 0, height - 1);
        float upper = source[top, left] * (1 - tx) + source[top, right] * tx;
        float lower = source[bottom, left] * (1 - tx) + source[bottom, right] * tx;
        return upper * (1 - ty) + lower * ty;
    }
}
