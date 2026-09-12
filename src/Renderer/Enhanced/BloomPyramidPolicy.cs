using System;
using System.Collections.Generic;

namespace MphRead;

internal enum BloomPyramidStorageFormat : byte
{
    Rgba8Unorm,
    Rgba16Float
}

internal readonly struct BloomPyramidWeights : IEquatable<BloomPyramidWeights>
{
    public const float MaximumTotal = 1;

    private BloomPyramidWeights(float quarter, float eighth, float sixteenth)
    {
        Quarter = quarter;
        Eighth = eighth;
        Sixteenth = sixteenth;
    }

    public float Quarter { get; }
    public float Eighth { get; }
    public float Sixteenth { get; }
    public float Total => Quarter + Eighth + Sixteenth;

    public static BloomPyramidWeights Default { get; } = Create(.5f, .3f, .2f);

    public static BloomPyramidWeights Create(float quarter, float eighth, float sixteenth)
    {
        Validate(quarter, nameof(quarter));
        Validate(eighth, nameof(eighth));
        Validate(sixteenth, nameof(sixteenth));
        float total = quarter + eighth + sixteenth;
        if (!float.IsFinite(total) || total <= 0 || total > MaximumTotal)
            throw new ArgumentOutOfRangeException(nameof(quarter),
                $"Bloom pyramid weights must have a positive total no greater than {MaximumTotal}.");
        return new BloomPyramidWeights(quarter, eighth, sixteenth);
    }

    private static void Validate(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name,
                "Bloom pyramid weights must be finite values between zero and one.");
    }

    public bool Equals(BloomPyramidWeights other)
        => Quarter.Equals(other.Quarter) && Eighth.Equals(other.Eighth)
            && Sixteenth.Equals(other.Sixteenth);
    public override bool Equals(object? obj) => obj is BloomPyramidWeights other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Quarter, Eighth, Sixteenth);
    public static bool operator ==(BloomPyramidWeights left, BloomPyramidWeights right)
        => left.Equals(right);
    public static bool operator !=(BloomPyramidWeights left, BloomPyramidWeights right)
        => !left.Equals(right);
}

internal enum BloomPyramidLevel : byte
{
    Quarter,
    Eighth,
    Sixteenth
}

internal enum BloomPyramidResource : byte
{
    EmissionSource,
    QuarterA,
    QuarterB,
    EighthA,
    EighthB,
    SixteenthA,
    SixteenthB,
    SceneInput,
    SceneOutput
}

internal enum BloomPyramidPassKind : byte
{
    Downsample,
    BlurHorizontal,
    BlurVertical,
    UpsampleCombine,
    SceneComposite
}

internal readonly record struct BloomPyramidLevelDescriptor(
    BloomPyramidLevel Level,
    uint ScaleDenominator,
    uint Width,
    uint Height,
    BloomPyramidResource First,
    BloomPyramidResource Second,
    float Weight,
    long EstimatedBytes);

internal readonly record struct BloomPyramidPassDescriptor(
    int Index,
    BloomPyramidPassKind Kind,
    BloomPyramidResource InputA,
    BloomPyramidResource? InputB,
    BloomPyramidResource Output,
    uint OutputWidth,
    uint OutputHeight,
    float InputAWeight,
    float InputBWeight)
{
    public bool HasReadWriteAlias
        => InputA == Output || InputB == Output;
}

internal readonly record struct BloomPyramidConfiguration(
    uint SceneWidth,
    uint SceneHeight,
    BloomPyramidStorageFormat Format,
    BloomPyramidWeights Weights);

/// <summary>
/// CPU reference for the derivative-based footprint used by bloom.hlsl.
/// Four bilinear taps at this positive/negative offset cover a 4x4 source
/// footprint for the first pass and a 2x2 footprint for later passes.
/// </summary>
internal static class BloomDownsampleFootprint
{
    public static float SourceTexelOffset(uint sourceDimension,
        uint targetDimension)
    {
        if (sourceDimension == 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDimension));
        if (targetDimension == 0)
            throw new ArgumentOutOfRangeException(nameof(targetDimension));
        return sourceDimension / (float)targetDimension * .25f;
    }
}

/// <summary>
/// Fixed VE12 resource and pass plan. It is deliberately a small policy, not
/// a render graph: the backend allocates two textures for each listed scale
/// and encodes these passes in their listed order.
/// </summary>
internal sealed class BloomPyramidPlan
{
    public const int LevelCount = 3;
    public const int PassCount = 12;
    public const long MaximumEstimatedBytes = 256L * 1024 * 1024;

    private readonly IReadOnlyList<BloomPyramidLevelDescriptor> _levels;
    private readonly IReadOnlyList<BloomPyramidPassDescriptor> _passes;

    private BloomPyramidPlan(BloomPyramidConfiguration configuration,
        BloomPyramidLevelDescriptor[] levels, BloomPyramidPassDescriptor[] passes,
        long estimatedBytes)
    {
        Configuration = configuration;
        _levels = Array.AsReadOnly(levels);
        _passes = Array.AsReadOnly(passes);
        EstimatedBytes = estimatedBytes;
    }

    public BloomPyramidConfiguration Configuration { get; }
    public IReadOnlyList<BloomPyramidLevelDescriptor> Levels => _levels;
    public IReadOnlyList<BloomPyramidPassDescriptor> Passes => _passes;
    public long EstimatedBytes { get; }

    public static BloomPyramidPlan Create(uint sceneWidth, uint sceneHeight,
        BloomPyramidStorageFormat format,
        BloomPyramidWeights? weights = null)
    {
        if (sceneWidth == 0) throw new ArgumentOutOfRangeException(nameof(sceneWidth));
        if (sceneHeight == 0) throw new ArgumentOutOfRangeException(nameof(sceneHeight));
        int bytesPerPixel = BytesPerPixel(format);
        BloomPyramidWeights resolvedWeights = weights ?? BloomPyramidWeights.Default;
        // Reject default(BloomPyramidWeights), which bypasses Create and has
        // no useful energy contribution.
        if (resolvedWeights.Total <= 0 || resolvedWeights.Total > BloomPyramidWeights.MaximumTotal)
            throw new ArgumentOutOfRangeException(nameof(weights));

        BloomPyramidLevelDescriptor quarter;
        BloomPyramidLevelDescriptor eighth;
        BloomPyramidLevelDescriptor sixteenth;
        try
        {
            quarter = Level(BloomPyramidLevel.Quarter, 4,
                BloomPyramidResource.QuarterA, BloomPyramidResource.QuarterB,
                resolvedWeights.Quarter, sceneWidth, sceneHeight, bytesPerPixel);
            eighth = Level(BloomPyramidLevel.Eighth, 8,
                BloomPyramidResource.EighthA, BloomPyramidResource.EighthB,
                resolvedWeights.Eighth, sceneWidth, sceneHeight, bytesPerPixel);
            sixteenth = Level(BloomPyramidLevel.Sixteenth, 16,
                BloomPyramidResource.SixteenthA, BloomPyramidResource.SixteenthB,
                resolvedWeights.Sixteenth, sceneWidth, sceneHeight, bytesPerPixel);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneWidth),
                "Bloom pyramid dimensions exceed the addressable resource budget.");
        }
        BloomPyramidLevelDescriptor[] levels = [quarter, eighth, sixteenth];
        long estimatedBytes = checked(quarter.EstimatedBytes + eighth.EstimatedBytes
            + sixteenth.EstimatedBytes);
        if (estimatedBytes > MaximumEstimatedBytes)
            throw new ArgumentOutOfRangeException(nameof(sceneWidth),
                $"Bloom pyramid requires {estimatedBytes} bytes, exceeding its {MaximumEstimatedBytes}-byte budget.");

        BloomPyramidPassDescriptor[] passes =
        [
            Pass(0, BloomPyramidPassKind.Downsample, BloomPyramidResource.EmissionSource,
                null, quarter.First, quarter.Width, quarter.Height),
            Pass(1, BloomPyramidPassKind.BlurHorizontal, quarter.First,
                null, quarter.Second, quarter.Width, quarter.Height),
            Pass(2, BloomPyramidPassKind.BlurVertical, quarter.Second,
                null, quarter.First, quarter.Width, quarter.Height),
            Pass(3, BloomPyramidPassKind.Downsample, quarter.First,
                null, eighth.First, eighth.Width, eighth.Height),
            Pass(4, BloomPyramidPassKind.BlurHorizontal, eighth.First,
                null, eighth.Second, eighth.Width, eighth.Height),
            Pass(5, BloomPyramidPassKind.BlurVertical, eighth.Second,
                null, eighth.First, eighth.Width, eighth.Height),
            Pass(6, BloomPyramidPassKind.Downsample, eighth.First,
                null, sixteenth.First, sixteenth.Width, sixteenth.Height),
            Pass(7, BloomPyramidPassKind.BlurHorizontal, sixteenth.First,
                null, sixteenth.Second, sixteenth.Width, sixteenth.Height),
            Pass(8, BloomPyramidPassKind.BlurVertical, sixteenth.Second,
                null, sixteenth.First, sixteenth.Width, sixteenth.Height),
            Pass(9, BloomPyramidPassKind.UpsampleCombine, eighth.First,
                sixteenth.First, eighth.Second, eighth.Width, eighth.Height,
                resolvedWeights.Eighth, resolvedWeights.Sixteenth),
            Pass(10, BloomPyramidPassKind.UpsampleCombine, quarter.First,
                eighth.Second, quarter.Second, quarter.Width, quarter.Height,
                resolvedWeights.Quarter, 1),
            Pass(11, BloomPyramidPassKind.SceneComposite, BloomPyramidResource.SceneInput,
                quarter.Second, BloomPyramidResource.SceneOutput, sceneWidth, sceneHeight, 1, 1)
        ];
        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i].Index != i || passes[i].HasReadWriteAlias)
                throw new InvalidOperationException("Bloom pyramid generated an invalid pass sequence.");
        }

        var configuration = new BloomPyramidConfiguration(sceneWidth, sceneHeight,
            format, resolvedWeights);
        return new BloomPyramidPlan(configuration, levels, passes, estimatedBytes);
    }

    public static uint ScaledDimension(uint dimension, uint denominator)
    {
        if (dimension == 0) throw new ArgumentOutOfRangeException(nameof(dimension));
        if (denominator == 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        return Math.Max(1u, dimension / denominator + (dimension % denominator == 0 ? 0u : 1u));
    }

    private static BloomPyramidLevelDescriptor Level(BloomPyramidLevel level,
        uint denominator, BloomPyramidResource first, BloomPyramidResource second,
        float weight, uint sceneWidth, uint sceneHeight, int bytesPerPixel)
    {
        uint width = ScaledDimension(sceneWidth, denominator);
        uint height = ScaledDimension(sceneHeight, denominator);
        long bytes = checked((long)width * height * bytesPerPixel * 2);
        return new BloomPyramidLevelDescriptor(level, denominator, width, height,
            first, second, weight, bytes);
    }

    private static BloomPyramidPassDescriptor Pass(int index, BloomPyramidPassKind kind,
        BloomPyramidResource inputA, BloomPyramidResource? inputB,
        BloomPyramidResource output, uint width, uint height,
        float inputAWeight = 1, float inputBWeight = 0)
        => new(index, kind, inputA, inputB, output, width, height,
            inputAWeight, inputBWeight);

    private static int BytesPerPixel(BloomPyramidStorageFormat format) => format switch
    {
        BloomPyramidStorageFormat.Rgba8Unorm => 4,
        BloomPyramidStorageFormat.Rgba16Float => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}

/// <summary>
/// Device-owned, feature-local allocation failure cache. Per-frame bloom
/// eligibility is intentionally not part of the configuration: an identical
/// failed allocation is not retried just because an emissive object appears.
/// HDR scene allocation and use remain outside this policy.
/// </summary>
internal sealed class BloomPyramidFailureCache
{
    public BloomPyramidConfiguration? FailedConfiguration { get; private set; }

    public bool ShouldAttempt(BloomPyramidConfiguration configuration,
        bool qualityRequested, bool frameHasEligibleEmission)
        => qualityRequested && frameHasEligibleEmission
            && (FailedConfiguration is null || FailedConfiguration.Value != configuration);

    public void RecordFailure(BloomPyramidConfiguration configuration)
        => FailedConfiguration = configuration;

    public void RecordSuccess()
        => FailedConfiguration = null;

    public void ResetForDeviceLoss()
        => FailedConfiguration = null;
}
