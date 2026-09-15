using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltAnimationSelectionTests
{
    [Theory]
    [InlineData(Hunter.Trace, 1f, 0f, 4)]
    [InlineData(Hunter.Trace, -1f, 0f, 2)]
    [InlineData(Hunter.Trace, 0f, 1f, 3)]
    [InlineData(Hunter.Trace, 0f, -1f, 5)]
    [InlineData(Hunter.Weavel, 1f, 0f, 4)]
    [InlineData(Hunter.Weavel, -1f, 0f, 2)]
    [InlineData(Hunter.Weavel, 0f, 1f, 3)]
    [InlineData(Hunter.Weavel, 0f, -1f, 6)]
    public void StrafeAltMovementSelectsHunterSpecificAnimation(
        Hunter hunter, float lateralSign, float forwardSign, int expected)
    {
        Assert.Equal(expected, PlayerEntity.SelectAltMovementAnimation(
            hunter, lateralSign, forwardSign));
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    public void SyluxMovementDoesNotSelectDirectionalAnimation(
        float lateralSign, float forwardSign)
    {
        Assert.Equal(-1, PlayerEntity.SelectAltMovementAnimation(
            Hunter.Sylux, lateralSign, forwardSign));
    }

    [Fact]
    public void AltAnimationEnumIndicesMatchAuthoredDirectionalSlots()
    {
        Assert.Equal(2, (int)TraceAltAnim.MoveLeft);
        Assert.Equal(3, (int)TraceAltAnim.MoveForward);
        Assert.Equal(4, (int)TraceAltAnim.MoveRight);
        Assert.Equal(5, (int)TraceAltAnim.MoveBackward);

        Assert.Equal(2, (int)WeavelAltAnim.MoveLeft);
        Assert.Equal(3, (int)WeavelAltAnim.MoveForward);
        Assert.Equal(4, (int)WeavelAltAnim.MoveRight);
        Assert.Equal(5, (int)WeavelAltAnim.Turn);
        Assert.Equal(6, (int)WeavelAltAnim.MoveBackward);

        Assert.Equal(0, (int)PsychoBitAltAnim.Stable);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void MorphAnimationRequiresARealOrExistingTransition(
        bool switchSucceeded, bool isMorphing, bool expected)
    {
        Assert.Equal(expected, PlayerEntity.ShouldApplyMorphAnimation(
            switchSucceeded, isMorphing));
    }

    [Fact]
    public void InvalidRollingCameraInputUsesCanonicalForwardAndLeftBasis()
    {
        (_, Vector3 forward, Vector3 left) =
            PlayerEntity.RotateRollingAltCamera(
                new Vector3(float.NaN, 0, 0), Vector3.Zero, Vector2.Zero);

        Assert.Equal(-Vector3.UnitZ, forward);
        Assert.Equal(-Vector3.UnitX, left);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void JumpPadStandingIsNotPhysicalAltGroundContact(bool standing,
        bool usedJumpPad, bool expected)
    {
        PlayerFlags1 flags = PlayerFlags1.None;
        if (standing) flags |= PlayerFlags1.Standing;
        if (usedJumpPad) flags |= PlayerFlags1.UsedJumpPad;

        Assert.Equal(expected,
            PlayerEntity.HasPhysicalAltGroundContact(flags));
    }

    [Theory]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Weavel)]
    public void AltLocomotionRequiresPhysicalGroundContact(Hunter hunter)
    {
        int requested = PlayerEntity.SelectAltMovementAnimation(
            hunter, lateralSign: 0, forwardSign: 1);
        Vector3 displacement = -Vector3.UnitZ;

        var airborne = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.None, altAttackActive: false,
            currentAnimation: 0, currentFlags: AnimFlags.None,
            requestedAnimation: requested, requestedFlags: AnimFlags.None,
            displacement: displacement);
        var jumpPad = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing | PlayerFlags1.UsedJumpPad,
            altAttackActive: false, currentAnimation: 0,
            currentFlags: AnimFlags.None, requestedAnimation: requested,
            requestedFlags: AnimFlags.None, displacement: displacement);

        Assert.Equal(0, airborne.Animation);
        Assert.Equal(0, jumpPad.Animation);
    }

    [Theory]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Weavel)]
    public void WallBlockedAltRequestFallsBackToIdle(Hunter hunter)
    {
        int requested = PlayerEntity.SelectAltMovementAnimation(
            hunter, lateralSign: 0, forwardSign: 1);

        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: 0, currentFlags: AnimFlags.None,
            requestedAnimation: requested, requestedFlags: AnimFlags.None,
            displacement: Vector3.Zero);

        Assert.Equal(0, result.Animation);
    }

    [Theory]
    [InlineData(Hunter.Trace, 1)]
    [InlineData(Hunter.Weavel, 1)]
    public void ActiveEndedAttackRetainsItsEndPose(Hunter hunter,
        int expectedAttack)
    {
        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: true,
            currentAnimation: expectedAttack,
            currentFlags: AnimFlags.NoLoop | AnimFlags.Ended,
            requestedAnimation: PlayerEntity.SelectAltMovementAnimation(
                hunter, lateralSign: 0, forwardSign: 1),
            requestedFlags: AnimFlags.None, displacement: -Vector3.UnitZ);

        Assert.Equal(expectedAttack, result.Animation);
        Assert.True(result.Flags.TestFlag(AnimFlags.Ended));
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    public void GuardianMovementLeavesPsychoBitOnStablePose(float lateralSign,
        float forwardSign)
    {
        Assert.Equal(-1, PlayerEntity.SelectAltMovementAnimation(
            Hunter.Guardian, lateralSign, forwardSign));

        var result = PlayerEntity.ResolveAltAnimation(
            Hunter.Guardian, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: (int)PsychoBitAltAnim.Stable,
            currentFlags: AnimFlags.Paused,
            requestedAnimation: -1, requestedFlags: AnimFlags.None,
            displacement: new Vector3(lateralSign, 0, -forwardSign));

        Assert.Equal((int)PsychoBitAltAnim.Stable, result.Animation);
        Assert.Equal(AnimFlags.Paused, result.Flags);
    }

    [Theory]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Weavel)]
    public void InactiveNonEndedAttackIsAllowedToFinish(Hunter hunter)
    {
        int requested = PlayerEntity.SelectAltMovementAnimation(
            hunter, lateralSign: 0, forwardSign: 1);

        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: 1, currentFlags: AnimFlags.NoLoop,
            requestedAnimation: requested, requestedFlags: AnimFlags.None,
            displacement: -Vector3.UnitZ);

        Assert.Equal(1, result.Animation);
    }

    [Theory]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Weavel)]
    public void SameTickEndedAttackStillPlaysItsAcceptedClip(
        Hunter hunter)
    {
        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: 0, currentFlags: AnimFlags.None,
            requestedAnimation: 1, requestedFlags: AnimFlags.NoLoop,
            displacement: Vector3.UnitX);

        Assert.Equal(1, result.Animation);
        Assert.Equal(AnimFlags.NoLoop, result.Flags);
    }

    [Theory]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Weavel)]
    public void GroundedTurnDoesNotRequireHorizontalDisplacement(Hunter hunter)
    {
        int turnAnimation = (int)WeavelAltAnim.Turn;
        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: 0, currentFlags: AnimFlags.None,
            requestedAnimation: turnAnimation,
            requestedFlags: AnimFlags.Reverse,
            displacement: Vector3.Zero,
            requestedAnimationRequiresMovement: false);

        Assert.Equal(turnAnimation, result.Animation);
        Assert.Equal(AnimFlags.Reverse, result.Flags);
    }

    [Theory]
    [InlineData(Hunter.Trace, 2)]
    [InlineData(Hunter.Weavel, 2)]
    public void RealAltMovementSelectsTheRequestedClip(Hunter hunter,
        int expectedAnimation)
    {
        var result = PlayerEntity.ResolveAltAnimation(
            hunter, PlayerFlags1.Standing, altAttackActive: false,
            currentAnimation: 0, currentFlags: AnimFlags.None,
            requestedAnimation: expectedAnimation,
            requestedFlags: AnimFlags.Reverse, displacement: Vector3.UnitX);

        Assert.Equal(expectedAnimation, result.Animation);
        Assert.Equal(AnimFlags.Reverse, result.Flags);
    }

    [Fact]
    public void SamusBoostStopsFromFinalHorizontalSpeedOnly()
    {
        Assert.True(PlayerEntity.ShouldStopSamusAltBoost(
            Hunter.Samus, isAltForm: true, boosting: true,
            speed: new Vector3(0.2f, 20, 0), altMinHSpeed: 0.2f));
        Assert.False(PlayerEntity.ShouldStopSamusAltBoost(
            Hunter.Samus, isAltForm: true, boosting: true,
            speed: new Vector3(0.2f, 0, 0.01f), altMinHSpeed: 0.2f));
        Assert.False(PlayerEntity.ShouldStopSamusAltBoost(
            Hunter.Trace, isAltForm: true, boosting: true,
            speed: Vector3.Zero, altMinHSpeed: 0.2f));
        Assert.False(PlayerEntity.ShouldStopSamusAltBoost(
            Hunter.Samus, isAltForm: false, boosting: true,
            speed: Vector3.Zero, altMinHSpeed: 0.2f));
    }
}
