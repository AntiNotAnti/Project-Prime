using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class GuardianAimGeometryTests
{
    [Fact]
    public void GuardianAltOriginUsesCollisionSphereAndNormalizedAimBasis()
    {
        Vector3 origin = PlayerEntity.ResolveActiveShotOrigin(
            Hunter.Guardian, isAltForm: true,
            bipedMuzzle: new Vector3(90, 90, 90),
            collisionSphere: new Vector3(10, 2, 3),
            aimBasis: new Vector3(0, 0, -2), muzzleOffset: 1.5f);

        Assert.Equal(new Vector3(10, 2, 1.5f), origin);
        Assert.True(VectorMath.IsFinite(origin));
    }

    [Fact]
    public void BipedOriginRemainsExactlyTheAuthoredMuzzle()
    {
        Vector3 bipedMuzzle = new(7, 8, 9);

        Assert.Equal(bipedMuzzle, PlayerEntity.ResolveActiveShotOrigin(
            Hunter.Guardian, isAltForm: false, bipedMuzzle,
            collisionSphere: new Vector3(1, 2, 3), aimBasis: Vector3.UnitX,
            muzzleOffset: 2));
        Assert.Equal(bipedMuzzle, PlayerEntity.ResolveActiveShotOrigin(
            Hunter.Samus, isAltForm: true, bipedMuzzle,
            collisionSphere: new Vector3(1, 2, 3), aimBasis: Vector3.UnitX,
            muzzleOffset: 2));
    }

    [Fact]
    public void GuardianShotDirectionIsFiniteAndNormalized()
    {
        Vector3 direction = PlayerEntity.ResolveActiveShotDirection(
            target: new Vector3(4, 5, -2),
            shotOrigin: new Vector3(1, 1, 2),
            aimBasis: Vector3.UnitX);

        Assert.True(VectorMath.IsFinite(direction));
        Assert.Equal(1, direction.Length, precision: 5);
        Assert.Equal(Vector3.Normalize(new Vector3(3, 4, -4)), direction);
    }

    [Fact]
    public void MalformedGuardianAltGeometryUsesFiniteDeterministicFallbacks()
    {
        Vector3 origin = PlayerEntity.ResolveActiveShotOrigin(
            Hunter.Guardian, isAltForm: true,
            bipedMuzzle: new Vector3(float.NaN, 0, 0),
            collisionSphere: new Vector3(float.PositiveInfinity, 0, 0),
            aimBasis: Vector3.Zero, muzzleOffset: float.NaN);

        Assert.Equal(Vector3.Zero, origin);
        Assert.True(VectorMath.IsFinite(origin));
        Vector3 direction = PlayerEntity.ResolveActiveShotDirection(
            new Vector3(float.NaN, 0, 0), origin, Vector3.UnitY);
        Assert.Equal(Vector3.UnitY, direction);
    }

    [Theory]
    [InlineData(Hunter.Guardian, true, -85, 85)]
    [InlineData(Hunter.Samus, true, -25, 5)]
    [InlineData(Hunter.Weavel, true, -25, 5)]
    [InlineData(Hunter.Guardian, false, -85, 85)]
    public void GuardianAltUsesTheNormalBipedAimPitchEnvelope(
        Hunter hunter, bool isAltForm, float expectedMin, float expectedMax)
    {
        (float min, float max) = PlayerEntity.ResolveAimPitchLimits(
            hunter, isAltForm);

        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }
}
