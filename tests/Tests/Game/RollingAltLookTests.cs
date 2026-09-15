using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class RollingAltLookTests
{
    [Theory]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(-1, 0, 0, -1)]
    public void AbsoluteAimProvidesUnitOrthogonalHorizontalBasis(
        float aimX, float aimY, float aimZ, float expectedForwardX)
    {
        (Vector3 forward, Vector3 left) =
            PlayerEntity.ResolveRollingAltControlBasis(
                new Vector3(aimX, aimY, aimZ), Vector3.UnitZ);

        Assert.Equal(expectedForwardX, forward.X, 5);
        Assert.Equal(1, forward.Length, 5);
        Assert.Equal(1, left.Length, 5);
        Assert.Equal(0, forward.Y, 5);
        Assert.Equal(0, left.Y, 5);
        Assert.Equal(0, Vector3.Dot(forward, left), 5);
    }

    [Fact]
    public void PitchOnlyAimPreservesRetainedHeading()
    {
        (Vector3 forward, Vector3 left) =
            PlayerEntity.ResolveRollingAltControlBasis(
                new Vector3(0, 1, 0), new Vector3(1, 0, 1));

        Assert.Equal(new Vector3(1, 0, 1).Normalized(), forward);
        Assert.Equal(new Vector3(forward.Z, 0, -forward.X), left);
    }

    [Fact]
    public void InvalidAimFallsBackToFiniteRetainedHeading()
    {
        (Vector3 forward, Vector3 left) =
            PlayerEntity.ResolveRollingAltControlBasis(
                new Vector3(float.NaN, float.PositiveInfinity, 0),
                new Vector3(-2, 0, 0));

        Assert.Equal(-Vector3.UnitX, forward);
        Assert.Equal(new Vector3(0, 0, 1), left);
        Assert.True(VectorMath.IsFinite(forward));
        Assert.True(VectorMath.IsFinite(left));
    }

    [Fact]
    public void MissingRollingHeadingUsesCanonicalForward()
    {
        (Vector3 forward, Vector3 left) =
            PlayerEntity.ResolveRollingAltControlBasis(Vector3.Zero,
                Vector3.Zero);

        Assert.Equal(-Vector3.UnitZ, forward);
        Assert.Equal(-Vector3.UnitX, left);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, -1)]
    [InlineData(-1, 0, 0, 0, 1)]
    [InlineData(0, -1, -1, 0, 0)]
    [InlineData(0, 1, 1, 0, 0)]
    public void WasdUsesStableScreenRelativeDirections(float forwardInput,
        float lateralInput, float expectedX, float expectedY, float expectedZ)
    {
        Vector3 movement = PlayerEntity.ResolveRollingAltMovement(
            -Vector3.UnitZ, -Vector3.UnitX, forwardInput, lateralInput, 1);

        Assert.Equal(new Vector3(expectedX, expectedY, expectedZ), movement);
    }

    [Fact]
    public void ExplicitYawRotatesControlBasisWhilePitchAndVelocityCannot()
    {
        (Vector3 forward, Vector3 left) =
            PlayerEntity.RotateRollingAltControlBasis(-Vector3.UnitZ, 90);
        (Vector3 unchanged, _) =
            PlayerEntity.RotateRollingAltControlBasis(forward, 0);

        Assert.Equal(-1, forward.X, 5);
        Assert.Equal(0, forward.Z, 5);
        Assert.Equal(0, left.X, 5);
        Assert.Equal(1, left.Z, 5);
        Assert.Equal(forward.X, unchanged.X, 5);
        Assert.Equal(forward.Z, unchanged.Z, 5);

        Vector3 transmitted = PlayerEntity.ResolveNetworkInputAim(
            isAltForm: true, isMorphing: false,
            usesStrafeAltMovement: false,
            gunAim: Vector3.UnitX, rollingForward: forward);
        Assert.Equal(forward.X, transmitted.X, 5);
        Assert.Equal(forward.Z, transmitted.Z, 5);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void InputAimUsesRollingHeadingOnlyDuringRollingControl(
        bool isAltForm, bool isMorphing, bool usesStrafeAltMovement,
        bool expectedRolling)
    {
        Vector3 gun = Vector3.UnitX;
        Vector3 rolling = -Vector3.UnitZ;

        Vector3 result = PlayerEntity.ResolveNetworkInputAim(isAltForm,
            isMorphing, usesStrafeAltMovement, gun, rolling);

        Assert.Equal(expectedRolling ? rolling : gun, result);
    }

    [Fact]
    public void WireAndPacketLossFallbackRetainRollingHeading()
    {
        var command = new InputCommand(4, 4, 3, InputButtons.RollForward,
            InputButtons.RollForward, -Vector3.UnitZ,
            InputCommand.NoWeapon);
        Span<byte> wire = stackalloc byte[InputCommand.Size];

        command.Write(wire);

        Assert.True(InputCommand.TryRead(wire, out InputCommand decoded));
        Assert.Equal(command.Aim, decoded.Aim);
        Assert.Equal(command.Aim, decoded.WithoutEdges().Aim);
    }

    [Fact]
    public void LocalAndAuthorityResolveTheSameRollingAcceleration()
    {
        (Vector3 localForward, Vector3 localLeft) =
            PlayerEntity.RotateRollingAltControlBasis(-Vector3.UnitZ, 37);
        Vector3 sentHeading = PlayerEntity.ResolveNetworkInputAim(
            isAltForm: true, isMorphing: false,
            usesStrafeAltMovement: false,
            gunAim: Vector3.UnitX, rollingForward: localForward);
        (Vector3 authorityForward, Vector3 authorityLeft) =
            PlayerEntity.ResolveRollingAltControlBasis(sentHeading,
                Vector3.UnitX);

        Vector3 local = PlayerEntity.ResolveRollingAltMovement(localForward,
            localLeft, forwardInput: 1, lateralInput: -1, traction: .125f);
        Vector3 authority = PlayerEntity.ResolveRollingAltMovement(
            authorityForward, authorityLeft, forwardInput: 1,
            lateralInput: -1, traction: .125f);

        Assert.Equal(local.X, authority.X, 6);
        Assert.Equal(local.Z, authority.Z, 6);
    }

    [Theory]
    [InlineData(CameraType.First, false, true)]
    [InlineData(CameraType.First, true, false)]
    [InlineData(CameraType.Third1, false, false)]
    [InlineData(CameraType.Third2, false, false)]
    public void NetworkAimRecentersOnlyAFirstPersonBipedCamera(
        CameraType cameraType, bool isAltForm, bool expected)
    {
        Assert.Equal(expected, PlayerEntity.ShouldRecenterNetworkAimCamera(
            cameraType, isAltForm));
    }

    [Fact]
    public void HorizontalLookRotatesTheOrbitAndMovementBasisTogether()
    {
        (Vector3 position, Vector3 forward, Vector3 right) =
            PlayerEntity.RotateRollingAltCamera(new Vector3(0, 1, -3),
                Vector3.Zero, new Vector2(90, 0));

        Assert.Equal(-3, position.X, 5);
        Assert.Equal(1, position.Y, 5);
        Assert.Equal(0, position.Z, 5);
        Assert.Equal(1, forward.X, 5);
        Assert.Equal(0, forward.Y, 5);
        Assert.Equal(0, forward.Z, 5);
        Assert.Equal(0, right.X, 5);
        Assert.Equal(0, right.Y, 5);
        Assert.Equal(-1, right.Z, 5);
    }

    [Fact]
    public void VerticalLookPreservesRadiusAndReturnsFiniteHorizontalAxes()
    {
        Vector3 original = new(0, 1, -3);

        (Vector3 position, Vector3 forward, Vector3 right) =
            PlayerEntity.RotateRollingAltCamera(original, Vector3.Zero,
                new Vector2(15, 20));

        Assert.Equal(original.Length, position.Length, 5);
        Assert.True(VectorMath.IsFinite(position));
        Assert.True(VectorMath.IsFinite(forward));
        Assert.True(VectorMath.IsFinite(right));
        Assert.Equal(1, forward.Length, 5);
        Assert.Equal(1, right.Length, 5);
        Assert.Equal(0, forward.Y, 6);
        Assert.Equal(0, right.Y, 6);
        Assert.Equal(0, Vector3.Dot(forward, right), 5);
    }

    [Fact]
    public void RollingCameraTranslationPreservesHorizontalOrbitAndHeight()
    {
        Vector3 oldTarget = new(2, 4, -3);
        Vector3 nextTarget = new(-11, 19, 17);
        Vector3 camera = new(5, 12, 9);

        Vector3 translated = PlayerEntity.TranslateRollingCameraOrbit(
            camera, oldTarget, nextTarget, rollingForm: true,
            cameraSwitchComplete: true, morphCameraActive: false);

        Assert.Equal(camera.Y, translated.Y, 6);
        Assert.Equal(camera.X - oldTarget.X,
            translated.X - nextTarget.X, 6);
        Assert.Equal(camera.Z - oldTarget.Z,
            translated.Z - nextTarget.Z, 6);
    }

    [Fact]
    public void RollingCameraTranslationKeepsAYawedOrbitYawed()
    {
        Vector3 oldTarget = new(3, 1, -2);
        Vector3 nextTarget = new(17, -5, 11);
        Vector3 camera = oldTarget + new Vector3(-4, 8, 7);

        Vector3 translated = PlayerEntity.TranslateRollingCameraOrbit(
            camera, oldTarget, nextTarget, rollingForm: true,
            cameraSwitchComplete: true, morphCameraActive: false);

        Vector3 oldHorizontal = (camera - oldTarget).WithY(0);
        Vector3 newHorizontal = (translated - nextTarget).WithY(0);
        Assert.Equal(oldHorizontal.X, newHorizontal.X, 6);
        Assert.Equal(oldHorizontal.Z, newHorizontal.Z, 6);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RollingCameraTranslationSkipsDisabledTransitionAndMorphGates(
        bool rollingForm, bool cameraSwitchComplete, bool morphCameraActive)
    {
        Vector3 camera = new(5, 12, 9);
        Vector3 translated = PlayerEntity.TranslateRollingCameraOrbit(
            camera, new(2, 4, -3), new(-11, 19, 17), rollingForm,
            cameraSwitchComplete, morphCameraActive);

        Assert.Equal(camera, translated);
    }

    [Fact]
    public void RollingCameraTranslationHasNoZeroDeltaOrRepeatedTranslationDrift()
    {
        Vector3 camera = new(5, 12, 9);
        Vector3 oldTarget = new(2, 4, -3);
        Vector3 nextTarget = new(-11, 19, 17);
        Vector3 unchanged = PlayerEntity.TranslateRollingCameraOrbit(
            camera, oldTarget, oldTarget, rollingForm: true,
            cameraSwitchComplete: true, morphCameraActive: false);
        Vector3 translated = PlayerEntity.TranslateRollingCameraOrbit(
            camera, oldTarget, nextTarget, rollingForm: true,
            cameraSwitchComplete: true, morphCameraActive: false);
        Vector3 repeated = PlayerEntity.TranslateRollingCameraOrbit(
            translated, nextTarget, nextTarget, rollingForm: true,
            cameraSwitchComplete: true, morphCameraActive: false);

        Assert.Equal(camera, unchanged);
        Assert.Equal(translated, repeated);
    }
}
