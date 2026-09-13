using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class GameplayFeelRegressionTests
{
    [Fact]
    public void NormalizeGuardsReturnFiniteDeterministicDirections()
    {
        Assert.Equal(Vector3.UnitX, VectorMath.NormalizeOr(Vector3.Zero, Vector3.UnitX));
        Assert.Equal(Vector3.UnitX,
            VectorMath.NormalizeHorizontalOr(new Vector3(0, 1, 0), Vector3.UnitX));

        Vector3 perpendicular = VectorMath.Perpendicular(Vector3.UnitY, Vector3.UnitY);
        Assert.True(VectorMath.IsFinite(perpendicular));
        Assert.Equal(0, Vector3.Dot(Vector3.UnitY, perpendicular), 5);
        Assert.Equal(1, perpendicular.Length, 5);
    }

    [Fact]
    public void AimFacingKeepsTheProjectedTangentOrientation()
    {
        Vector3 gun = Vector3.UnitZ;
        Vector3 facing = new Vector3(0.6f, 0, 0.8f);
        Vector3 tangent = VectorMath.NormalizeOr(facing - gun * Vector3.Dot(gun, facing), Vector3.UnitX);
        Vector3 expected = VectorMath.NormalizeOr(
            (Fixed.ToFloat(3956) * gun + Fixed.ToFloat(1060) * tangent) * 0.9f + gun * 0.1f,
            gun);

        Assert.Equal(expected, PlayerEntity.ResolveAimFacing(gun, facing));
        Assert.True(PlayerEntity.ResolveAimFacing(gun, facing).X > 0);
    }

    [Fact]
    public void RepeatedNeutralAimSamplesDoNotAdvanceCameraFacing()
    {
        Vector3 gun = Vector3.UnitZ;
        Vector3 facing = VectorMath.NormalizeOr(new Vector3(0.2f, 0, 1), gun);

        Vector3 neutral = facing;
        for (int i = 0; i < 120; i++)
        {
            neutral = PlayerEntity.ResolveAimFacingAfterInput(
                gun, neutral, appliedAngle: 0);
        }
        Vector3 active = PlayerEntity.ResolveAimFacingAfterInput(
            gun, facing, appliedAngle: 0.01f);

        Assert.Equal(facing.X, neutral.X, 6);
        Assert.Equal(facing.Y, neutral.Y, 6);
        Assert.Equal(facing.Z, neutral.Z, 6);
        Assert.True(Vector3.Dot(active, gun) > Vector3.Dot(neutral, gun));
    }

    [Fact]
    public void DynamicCrosshairTravelDelaysCameraFollowUntilItsThreshold()
    {
        Vector3 gun = Vector3.UnitZ;
        Vector3 insideTravel = FacingAtDegrees(10);
        Vector3 outsideTravel = FacingAtDegrees(20);

        Vector3 insideResult = PlayerEntity.ResolveAimFacingAfterInput(
            gun, insideTravel, appliedAngle: 0.01f, travelDegrees: 15,
            turnSpeed: 1);
        Vector3 outsideResult = PlayerEntity.ResolveAimFacingAfterInput(
            gun, outsideTravel, appliedAngle: 0.01f, travelDegrees: 15,
            turnSpeed: 1);

        Assert.Equal(insideTravel.X, insideResult.X, 6);
        Assert.Equal(insideTravel.Z, insideResult.Z, 6);
        Assert.True(Vector3.Dot(outsideResult, gun) > Vector3.Dot(outsideTravel, gun));
    }

    [Fact]
    public void DynamicCrosshairTurnSpeedControlsCameraResponse()
    {
        Vector3 gun = Vector3.UnitZ;
        Vector3 facing = FacingAtDegrees(20);

        Vector3 slow = PlayerEntity.ResolveAimFacingAfterInput(
            gun, facing, appliedAngle: 0.01f, travelDegrees: 5,
            turnSpeed: 0.5f);
        Vector3 fast = PlayerEntity.ResolveAimFacingAfterInput(
            gun, facing, appliedAngle: 0.01f, travelDegrees: 5,
            turnSpeed: 3);

        Assert.True(Vector3.Dot(fast, gun) > Vector3.Dot(slow, gun));
        Assert.Equal(PlayerEntity.ResolveAimFacing(gun, facing),
            PlayerEntity.ResolveAimFacingAfterInput(
                gun, facing, appliedAngle: 0.01f, travelDegrees: 0,
                turnSpeed: 1));
    }

    [Fact]
    public void DynamicCrosshairFollowDoesNotAlternateBetweenStoppingAndJumping()
    {
        Vector3 facing = Vector3.UnitZ;
        float previousFacingDegrees = 0;
        for (int gunDegrees = 1; gunDegrees <= 24; gunDegrees++)
        {
            facing = PlayerEntity.ResolveAimFacingAfterInput(
                FacingAtDegrees(gunDegrees), facing,
                appliedAngle: MathHelper.DegreesToRadians(1),
                travelDegrees: 15, turnSpeed: 1);
            float facingDegrees = MathHelper.RadiansToDegrees(
                MathF.Atan2(facing.X, facing.Z));
            if (gunDegrees <= 15)
            {
                Assert.Equal(0, facingDegrees, 4);
            }
            else
            {
                Assert.Equal(1, facingDegrees - previousFacingDegrees, 4);
            }
            previousFacingDegrees = facingDegrees;
        }
    }

    [Fact]
    public void DynamicCrosshairTravelIsLeftRightSymmetric()
    {
        Vector3 rightFacing = Vector3.UnitZ;
        Vector3 leftFacing = Vector3.UnitZ;
        for (int gunDegrees = 1; gunDegrees <= 30; gunDegrees++)
        {
            float step = MathHelper.DegreesToRadians(1);
            rightFacing = PlayerEntity.ResolveAimFacingAfterInput(
                FacingAtDegrees(gunDegrees), rightFacing, step,
                travelDegrees: 15, turnSpeed: .65f);
            leftFacing = PlayerEntity.ResolveAimFacingAfterInput(
                FacingAtDegrees(-gunDegrees), leftFacing, -step,
                travelDegrees: 15, turnSpeed: .65f);

            Assert.Equal(rightFacing.X, -leftFacing.X, 5);
            Assert.Equal(rightFacing.Y, leftFacing.Y, 5);
            Assert.Equal(rightFacing.Z, leftFacing.Z, 5);
        }
    }

    [Fact]
    public void NoxusOverlapUsesAttackerFacingAndKeepsOneEighthMagnitude()
    {
        Vector3 direction = PlayerEntity.ResolveHorizontalKnockbackDirection(
            Vector3.Zero, new Vector3(0, 0, -1), 1 / 8f);

        Assert.Equal(new Vector3(0, 0, -0.125f), direction);
        Assert.Equal(0.125f, direction.Length, 5);

        direction = PlayerEntity.ResolveHorizontalKnockbackDirection(
            Vector3.Zero, Vector3.Zero, 1 / 8f);
        Assert.Equal(new Vector3(0.125f, 0, 0), direction);
    }

    [Fact]
    public void ShockCoilAnimationEntersOnlyForHeldLiveWeaponWithAmmo()
    {
        Assert.True(PlayerEntity.ShouldPlayShockCoilAnimation(
            BeamType.ShockCoil, shooting: true, ammo: 4, ammoCost: 2,
            health: 1, altForm: false, morphing: false, unmorphing: false));
        Assert.False(PlayerEntity.ShouldPlayShockCoilAnimation(
            BeamType.ShockCoil, shooting: true, ammo: 1, ammoCost: 2,
            health: 1, altForm: false, morphing: false, unmorphing: false));
        Assert.False(PlayerEntity.ShouldPlayShockCoilAnimation(
            BeamType.ShockCoil, shooting: false, ammo: 4, ammoCost: 2,
            health: 1, altForm: false, morphing: false, unmorphing: false));
        Assert.False(PlayerEntity.ShouldPlayShockCoilAnimation(
            BeamType.ShockCoil, shooting: true, ammo: -1, ammoCost: 2,
            health: 1, altForm: false, morphing: true, unmorphing: false));
        Assert.False(PlayerEntity.ShouldPlayShockCoilAnimation(
            BeamType.PowerBeam, shooting: true, ammo: -1, ammoCost: 0,
            health: 1, altForm: false, morphing: false, unmorphing: false));
    }

    [Fact]
    public void CameraInfoVerticalAndZeroFacingStayFinite()
    {
        using Scene scene = Scene.CreateHeadless();
        var camera = new CameraInfo
        {
            Position = Vector3.Zero,
            Target = Vector3.Zero,
            UpVector = Vector3.UnitY
        };

        camera.Update(scene);

        AssertFinite(camera.Facing);
        AssertFinite(camera.TrueUp);
        AssertFinite(camera.ViewMatrix);
        Assert.Equal(1, new Vector2(camera.Field48, camera.Field4C).Length, 5);

        camera.Target = Vector3.UnitY;
        camera.Update(scene);
        AssertFinite(camera.Facing);
        AssertFinite(camera.TrueUp);
        Assert.Equal(1, new Vector2(camera.Field48, camera.Field4C).Length, 5);
    }

    [Fact]
    public void SphereEdgeUsesClosestSegmentDistanceAndContinuesPolygonOrder()
    {
        CollisionCandidate candidate = MakeSquareCandidate();
        var results = new CollisionResult[4];

        int faceCount = CollisionDetection.CheckSphereBetweenPoints(
            new[] { candidate }, new Vector3(5, 5, 1), new Vector3(5, 5, -1),
            radius: 0.1f, limit: 4, includeOffset: true, TestFlags.Players, null!, results);
        Assert.Equal(1, faceCount);
        Assert.Equal(0, results[0].Field0);

        int edgeCount = CollisionDetection.CheckSphereBetweenPoints(
            new[] { candidate }, new Vector3(5, -0.5f, 1), new Vector3(5, -0.5f, -1),
            radius: 0.75f, limit: 4, includeOffset: true, TestFlags.Players, null!, results);
        Assert.Equal(1, edgeCount);
        Assert.Equal(1, results[0].Field0);
        Assert.Equal(new Vector3(0, 0, 0), results[0].EdgePoint1);
        Assert.Equal(new Vector3(10, 0, 0), results[0].EdgePoint2);

        // The first edge's infinite-line distance is only 0.5, but its finite
        // segment is ten units away at this x. A later edge is also out of
        // range, so the old first-edge shortcut must not report a hit.
        int missCount = CollisionDetection.CheckSphereBetweenPoints(
            new[] { candidate }, new Vector3(20, -0.5f, 1), new Vector3(20, -0.5f, -1),
            radius: 0.75f, limit: 4, includeOffset: true, TestFlags.Players, null!, results);
        Assert.Equal(0, missCount);
    }

    [Fact]
    public void SphereEdgePreservesTranslationAndZeroLengthEdgesAreSafe()
    {
        CollisionCandidate candidate = MakeSquareCandidate(translation: new Vector3(2, 3, 4),
            duplicateFirstEdge: true);
        var results = new CollisionResult[2];

        int count = CollisionDetection.CheckSphereBetweenPoints(
            new[] { candidate }, new Vector3(7, 2.5f, 5), new Vector3(7, 2.5f, 3),
            radius: 0.75f, limit: 2, includeOffset: true, TestFlags.Players, null!, results);

        Assert.Equal(1, count);
        Assert.Equal(1, results[0].Field0);
        Assert.Equal(new Vector3(2, 3, 4), results[0].EdgePoint1);
        Assert.Equal(new Vector3(12, 3, 4), results[0].EdgePoint2);
        Assert.Equal(new Vector3(7, 2.5f, 4), results[0].Position);
        AssertFinite(results[0].Plane);
    }

    private static CollisionCandidate MakeSquareCandidate(Vector3? translation = null,
        bool duplicateFirstEdge = false)
    {
        Vector3[] points =
        {
            new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)
        };
        var pointIndices = duplicateFirstEdge
            ? new ushort[] { 0, 0, 1, 2, 3 }
            : new ushort[] { 0, 1, 2, 3, 0 };
        var data = Struct<CollisionData>(
            (nameof(CollisionData.PlaneIndex), (ushort)0),
            (nameof(CollisionData.PointIndexCount), (ushort)4),
            (nameof(CollisionData.PointStartIndex), (ushort)0));
        var info = new MphCollisionInfo(default,
            Array.ConvertAll(points, point => new Vector3Fx(Fixed.ToInt(point.X),
                Fixed.ToInt(point.Y), Fixed.ToInt(point.Z))),
            new[] { Struct<Vector4Fx>((nameof(Vector4Fx.Z), new Fixed(4096))) },
            pointIndices, new[] { data }, new ushort[] { 0 },
            new[] { new CollisionEntry(1, 0) }, Array.Empty<Portal>());
        var instance = new CollisionInstance("feel-square", info, false)
        {
            Translation = translation ?? Vector3.Zero
        };
        return new CollisionCandidate(instance, new CollisionEntry(1, 0));
    }

    private static T Struct<T>(params (string Name, object Value)[] fields) where T : struct
    {
        object value = default(T);
        foreach ((string name, object fieldValue) in fields)
            typeof(T).GetField(name)!.SetValue(value, fieldValue);
        return (T)value;
    }

    private static void AssertFinite(Vector3 value)
        => Assert.True(VectorMath.IsFinite(value));

    private static void AssertFinite(Vector4 value)
        => Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W));

    private static void AssertFinite(Matrix4 value)
    {
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            Assert.True(float.IsFinite(value[row, column]));
    }

    private static Vector3 FacingAtDegrees(float degrees)
    {
        float radians = MathHelper.DegreesToRadians(degrees);
        return new Vector3(MathF.Sin(radians), 0, MathF.Cos(radians));
    }
}
