using System;
using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class JudicatorIceWaveTests
{
    private const float Cosine = 0.5f;

    [Theory]
    [InlineData(0, 0, 5, true)]
    [InlineData(8.65, 0, 5.01, true)]
    [InlineData(8.67, 0, 4.99, false)]
    [InlineData(0, 4, 5, true)]
    [InlineData(0, -4, 5, true)]
    [InlineData(0, 100, 1, false)]
    [InlineData(0, 0, -1, false)]
    public void UsesTrueThreeDimensionalCone(double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, Inside(new Vector3((float)x, (float)y, (float)z), max: 200));
    }

    [Fact]
    public void ConeEdgeIsStrict()
    {
        float x = MathF.Sqrt(3);
        Assert.False(Inside(new Vector3(x, 0, 1), max: 10));
        Assert.True(Inside(new Vector3(x - 0.001f, 0, 1), max: 10));
        Assert.False(Inside(new Vector3(x + 0.001f, 0, 1), max: 10));
    }

    [Fact]
    public void DistanceBoundsAreStrict()
    {
        Assert.False(Inside(Vector3.Zero, max: 10));
        Assert.False(Inside(new Vector3(0, 0, 0.0000001f), max: 10));
        Assert.True(Inside(new Vector3(0, 0, 9.999f), max: 10));
        Assert.False(Inside(new Vector3(0, 0, 10), max: 10));
        Assert.False(Inside(new Vector3(0, 0, 10.001f), max: 10));
    }

    [Fact]
    public void RejectsNonFiniteAndInvalidParameters()
    {
        Assert.False(Inside(new Vector3(float.NaN, 0, 1), max: 10));
        Assert.False(Inside(new Vector3(0, float.PositiveInfinity, 1), max: 10));
        Assert.False(BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero,
            new Vector3(float.NegativeInfinity, 0, 1), Vector3.UnitZ, 10, Cosine));
        Assert.False(BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero,
            Vector3.Zero, Vector3.UnitZ, 10, Cosine));
        Assert.False(BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero,
            Vector3.UnitZ, Vector3.UnitZ, float.NaN, Cosine));
        Assert.False(BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero,
            Vector3.UnitZ, Vector3.UnitZ, 10, 2));
    }

    [Fact]
    public void NormalizesNonUnitDirection()
    {
        Assert.True(BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero,
            new Vector3(0, 0, 50), new Vector3(0, 0, 5), 10, Cosine));
    }

    private static bool Inside(Vector3 position, float max)
        => BeamProjectileEntity.IsInsideIceWaveCone(Vector3.Zero, Vector3.UnitZ,
            position, max, Cosine);
}
