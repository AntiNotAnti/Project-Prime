using System;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedRenderingMathTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.003f)]
    [InlineData(0.04045f)]
    [InlineData(0.18f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void SrgbLinearConversionRoundTrips(float encoded)
    {
        float linear = EnhancedColorMath.SrgbToLinear(encoded);
        float roundTrip = EnhancedColorMath.LinearToSrgb(linear);

        Assert.InRange(linear, 0, 1);
        Assert.Equal(encoded, roundTrip, precision: 5);
    }

    [Fact]
    public void ColorConversionRejectsNonFiniteAndOutOfRangeInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorMath.SrgbToLinear(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorMath.SrgbToLinear(-0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorMath.LinearToSrgb(1.01f));
    }

    [Fact]
    public void AcesToneMapIsBoundedAndMonotonic()
    {
        float[] inputs = { 0, 0.05f, 0.18f, 1, 2, 5, 10, float.MaxValue };
        float previous = -1;
        foreach (float input in inputs)
        {
            Vector3 mapped = EnhancedColorMath.ToneMapAces(new Vector3(input));
            Assert.InRange(mapped.X, 0, 1);
            Assert.True(mapped.X >= previous);
            Assert.Equal(mapped.X, mapped.Y);
            Assert.Equal(mapped.X, mapped.Z);
            previous = mapped.X;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorMath.ToneMapAces(new Vector3(-1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorMath.ToneMapAces(Vector3.One, 0));
    }

    [Theory]
    [MemberData(nameof(NormalCases))]
    public void OctNormalEncodingRoundTrips(Vector3 normal)
    {
        Vector2 encoded = EnhancedSurfaceMath.EncodeOctNormal(normal);
        Vector3 decoded = EnhancedSurfaceMath.DecodeOctNormal(encoded);

        Assert.InRange(encoded.X, 0, 1);
        Assert.InRange(encoded.Y, 0, 1);
        Assert.True(Vector3.Dot(normal.Normalized(), decoded) > 0.9999f);
        Assert.Equal(1, decoded.Length, precision: 5);
    }

    public static TheoryData<Vector3> NormalCases => new()
    {
        Vector3.UnitX,
        -Vector3.UnitX,
        Vector3.UnitY,
        -Vector3.UnitY,
        Vector3.UnitZ,
        -Vector3.UnitZ,
        new Vector3(1, 2, 3),
        new Vector3(-3, 2, -1)
    };

    [Fact]
    public void SurfacePackingPreservesDepthMaterialAndNormal()
    {
        var normal = new Vector3(-2, 3, 4);
        Vector4 packed = EnhancedSurfaceMath.Pack(normal, 123.5f, 0.75f);
        EnhancedSurfaceData unpacked = EnhancedSurfaceMath.Unpack(packed);

        Assert.True(Vector3.Dot(normal.Normalized(), unpacked.WorldNormal) > 0.9999f);
        Assert.Equal(123.5f, unpacked.LinearDepth);
        Assert.Equal(0.75f, unpacked.MaterialValue);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedSurfaceMath.Pack(Vector3.Zero, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedSurfaceMath.Pack(Vector3.UnitZ, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedSurfaceMath.Unpack(new Vector4(2, 0, 1, 0)));

        Vector2 extreme = EnhancedSurfaceMath.EncodeOctNormal(
            new Vector3(float.MaxValue, -float.MaxValue, float.MaxValue));
        Assert.True(float.IsFinite(extreme.X));
        Assert.True(float.IsFinite(extreme.Y));
    }

    [Fact]
    public void SsaoKernelIsDeterministicBoundedAndHemispherical()
    {
        var first = EnhancedSsaoKernel.Create(EnhancedSsaoKernel.MaximumSampleCount, 42);
        var second = EnhancedSsaoKernel.Create(EnhancedSsaoKernel.MaximumSampleCount, 42);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
        Assert.All(first, sample =>
        {
            Assert.True(sample.Z >= 0);
            Assert.InRange(sample.Length, float.Epsilon, 1);
            Assert.True(float.IsFinite(sample.X));
            Assert.True(float.IsFinite(sample.Y));
            Assert.True(float.IsFinite(sample.Z));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => EnhancedSsaoKernel.Create(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnhancedSsaoKernel.Create(13));
    }

    [Fact]
    public void LutAddressingUsesPixelCentersAndAdjacentBlueSlices()
    {
        EnhancedLutSample black = EnhancedLutAddressing.Address(Vector3.Zero);
        Assert.Equal(0.5f / EnhancedLutAddressing.TextureWidth, black.LowerUv.X);
        Assert.Equal(0.5f / EnhancedLutAddressing.TextureHeight, black.LowerUv.Y);
        Assert.Equal(0, black.SliceBlend);

        EnhancedLutSample middle = EnhancedLutAddressing.Address(new Vector3(1, 1, 0.5f));
        Assert.Equal(0.5f, middle.SliceBlend, precision: 5);
        Assert.True(middle.UpperUv.X > middle.LowerUv.X);
        Assert.All(new[] { middle.LowerUv.X, middle.LowerUv.Y,
            middle.UpperUv.X, middle.UpperUv.Y }, value => Assert.InRange(value, 0, 1));

        EnhancedLutSample white = EnhancedLutAddressing.Address(Vector3.One);
        Assert.Equal(white.LowerUv, white.UpperUv);
        Assert.Equal(0, white.SliceBlend);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedLutAddressing.Address(new Vector3(0, 0, float.PositiveInfinity)));
    }

    [Fact]
    public void FogIsBoundedMonotonicAndHeightAware()
    {
        Assert.Equal(0, EnhancedFogMath.DistanceFactor(0, 1));
        Assert.Equal(0, EnhancedFogMath.DistanceFactor(100, 0));
        float near = EnhancedFogMath.DistanceFactor(5, 0.05f);
        float far = EnhancedFogMath.DistanceFactor(20, 0.05f);
        Assert.InRange(near, 0, 1);
        Assert.InRange(far, near, 1);

        float inLayer = EnhancedFogMath.DistanceHeightFactor(20, 0.05f,
            cameraHeight: 0, worldHeight: 0, fogHeight: 1, fogFalloff: 0.5f);
        float aboveLayer = EnhancedFogMath.DistanceHeightFactor(20, 0.05f,
            cameraHeight: 10, worldHeight: 12, fogHeight: 1, fogFalloff: 0.5f);
        Assert.Equal(far, inLayer, precision: 5);
        Assert.InRange(aboveLayer, 0, inLayer);
        Assert.Equal(far, EnhancedFogMath.DistanceHeightFactor(20, 0.05f,
            cameraHeight: 100, worldHeight: -100, fogHeight: 1,
            fogFalloff: 0), precision: 6);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedFogMath.DistanceFactor(-1, 0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedFogMath.DistanceHeightFactor(1, 0.1f, 0, 0, 0, -1));
    }

    [Fact]
    public void DistortionClampPreservesDirectionAndMaximum()
    {
        Vector2 inside = EnhancedDistortionMath.ClampOffset(new Vector2(0.01f, -0.02f), 0.1f);
        Assert.Equal(new Vector2(0.01f, -0.02f), inside);

        Vector2 clamped = EnhancedDistortionMath.ClampOffset(new Vector2(3, 4), 0.25f);
        Assert.Equal(0.25f, clamped.Length, precision: 5);
        Assert.True(Vector2.Dot(clamped.Normalized(), new Vector2(3, 4).Normalized()) > 0.9999f);
        Assert.Equal(Vector2.Zero, EnhancedDistortionMath.ClampOffset(new Vector2(3, 4), 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedDistortionMath.ClampOffset(new Vector2(float.NaN, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedDistortionMath.ClampOffset(Vector2.Zero, -1));
    }
}
