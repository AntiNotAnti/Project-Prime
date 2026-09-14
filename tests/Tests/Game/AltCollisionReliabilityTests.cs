using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltCollisionReliabilityTests
{
    [Theory]
    [InlineData(Hunter.Spire, 0.5f)]
    [InlineData(Hunter.Sylux, 0.5f)]
    [InlineData(Hunter.Noxus, 0.35f)]
    public void AltSweepPaddingMatchesTheNarrowPhase(Hunter hunter,
        float expected)
    {
        Assert.Equal(expected, PlayerEntity.ResolveAltSweepPadding(hunter));
    }

    [Fact]
    public void CollisionBoundsContainAFullSphereAtEveryKandenSegment()
    {
        Vector3 min = new(-1, -2, -3);
        Vector3 max = new(1, 2, 3);

        PlayerEntity.ExpandCollisionBoundsForSphere(ref min, ref max,
            new Vector3(4, 5, 6), 0.75f);
        PlayerEntity.ExpandCollisionBoundsForSphere(ref min, ref max,
            new Vector3(-5, -6, -7), 0.5f);

        Assert.Equal(new Vector3(-5.5f, -6.5f, -7.5f), min);
        Assert.Equal(new Vector3(4.75f, 5.75f, 6.75f), max);
    }

    [Fact]
    public void GroundedSyluxClearanceUsesTheResolvedBipedOrigin()
    {
        CollisionVolume alt = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 2];
        CollisionVolume bipedBottom = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 0];
        CollisionVolume bipedTop = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 1];
        Vector3 origin = new(0, 4, 0);

        Vector3 resolved = PlayerEntity.ResolveFormOrigin(origin, alt,
            bipedBottom, grounded: true);
        float actualTop = (resolved + bipedTop.SpherePosition).Y
            + bipedTop.SphereRadius;
        float incorrectlyUnshiftedTop = (origin + bipedTop.SpherePosition).Y
            + bipedTop.SphereRadius;

        Assert.True(actualTop < incorrectlyUnshiftedTop);
        Assert.Equal((origin + alt.SpherePosition).Y - alt.SphereRadius,
            (resolved + bipedBottom.SpherePosition).Y
                - bipedBottom.SphereRadius,
            precision: 5);
    }

    [Fact]
    public void AltEdgeContactUsesClosestPointNormalAndLinearPenetration()
    {
        bool hit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(0.6f, 0.6f, 0), new Vector3(-2, 0, 0),
            new Vector3(2, 0, 0), 1, out Vector3 normal,
            out float penetration);

        Assert.True(hit);
        Assert.Equal(0, normal.X, 5);
        Assert.Equal(1, normal.Y, 5);
        Assert.Equal(0, normal.Z, 5);
        Assert.Equal(0.4f, penetration, 5);
        Assert.True(VectorMath.IsFinite(normal));
        Assert.True(float.IsFinite(penetration));
    }

    [Fact]
    public void AltEdgeContactHandlesCornersAndZeroLengthSegments()
    {
        bool cornerHit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(0.3f, 0.4f, 0), Vector3.Zero, Vector3.Zero,
            0.75f, out Vector3 cornerNormal, out float cornerPenetration);

        Assert.True(cornerHit);
        Assert.Equal(new Vector3(0.6f, 0.8f, 0), cornerNormal);
        Assert.Equal(0.25f, cornerPenetration, 5);

        bool degenerateHit = PlayerEntity.TryResolveAltEdgeContact(
            Vector3.Zero, Vector3.Zero, Vector3.Zero, 0.75f,
            out Vector3 degenerateNormal, out float degeneratePenetration);

        Assert.False(degenerateHit);
        Assert.Equal(Vector3.Zero, degenerateNormal);
        Assert.Equal(0, degeneratePenetration);
        Assert.True(VectorMath.IsFinite(degenerateNormal));
        Assert.True(float.IsFinite(degeneratePenetration));
    }

    [Fact]
    public void AltEdgeContactRejectsNonFiniteGeometry()
    {
        bool hit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(float.NaN, 0, 0), Vector3.Zero, Vector3.UnitX,
            1, out Vector3 normal, out float penetration);

        Assert.False(hit);
        Assert.True(VectorMath.IsFinite(normal));
        Assert.True(float.IsFinite(penetration));
    }

    [Fact]
    public void SyluxHoverUsesTheDeepestContactOnly()
    {
        float depth = float.NaN;
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.4f);
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.7f);
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.7f);

        Assert.Equal(0.7f, depth, 5);
    }

    [Fact]
    public void SyluxHoverResponseIsAppliedOnceAtTheSixtyHertzRate()
    {
        Vector3 speed = new(0, 0, 0);
        float gravity = Fixed.ToFloat(-245);

        Vector3 response = PlayerEntity.ApplySyluxHoverResponse(speed,
            hoverDepth: 0.4f, altAirGravity: gravity);

        float expectedY = gravity / 2 * Fixed.ToFloat(4034)
            + 0.4f * 0.2f / 2;
        Assert.Equal(expectedY, response.Y, 6);

        float duplicateDepth = PlayerEntity.AggregateSyluxHoverDepth(
            PlayerEntity.AggregateSyluxHoverDepth(float.NaN, 0.4f), 0.4f);
        Vector3 duplicateResponse = PlayerEntity.ApplySyluxHoverResponse(
            speed, hoverDepth: duplicateDepth, altAirGravity: gravity);
        Assert.Equal(response.Y, duplicateResponse.Y, 6);
    }
}
