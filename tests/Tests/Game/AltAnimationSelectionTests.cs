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
}
