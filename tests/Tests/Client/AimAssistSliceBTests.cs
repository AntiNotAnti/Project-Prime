using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class AimAssistSliceBTests
{
    [Fact]
    public void DefaultProfileDoublesRotationAgainWithoutMoreStickiness()
    {
        AimAssistProfile profile = PlayerAimAssist.DefaultProfile;

        Assert.Equal(120f, profile.MaxYawRate);
        Assert.Equal(88f, profile.MaxPitchRate);
        Assert.Equal(8f, profile.AcquireConeDegrees);
        Assert.Equal(11f, profile.RetainConeDegrees);
        Assert.Equal(.40f, profile.MaxSlowdown);

        Vector2 assisted = PlayerAimAssist.ApplyAssistance(Vector2.Zero,
            new Vector2(20, -20), angularDistance: 0, stickMagnitude: 1,
            strength: 1, deltaSeconds: 1f / 60f);
        Assert.Equal(2f, assisted.X, 5);
        Assert.Equal(-88f / 60f, assisted.Y, 5);
    }

    [Fact]
    public void EscapeIntentCoversAxesTangentAndInversion()
    {
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(1, 0)), 5);
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(0, 1), new Vector2(1, 0)), 5);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(-1, 0), new Vector2(1, 0)), .99f, 1f);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(1, 0), invertX: true), .99f, 1f);
        Assert.Equal(0, PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(1, 0), new Vector2(-1, 0), invertX: true), 5);
        Assert.InRange(PlayerAimAssist.ComputeEscapeIntent(
            new Vector2(0, -1), new Vector2(0, -1), invertY: true), .99f, 1f);
    }

    [Fact]
    public void EscapeIntentSmoothlyRemovesPullAndFriction()
    {
        Vector2 toward = PlayerAimAssist.ApplyAssistance(
            new Vector2(1, 0), new Vector2(1, 0), 4, 1, 1, 1f / 60f);
        Vector2 away = PlayerAimAssist.ApplyAssistance(
            new Vector2(-1, 0), new Vector2(1, 0), 1, 1, 1, 1f / 60f);

        Assert.True(toward.X > PlayerAimAssist.FrictionMultiplier(4, 1),
            "Toward input should retain rotational pull after friction.");
        Assert.Equal(-1, away.X, 5);
        Assert.Equal(1, PlayerAimAssist.FrictionMultiplier(1, 1, 1), 5);
        Assert.InRange(PlayerAimAssist.FrictionMultiplier(1, 1, .5f),
            PlayerAimAssist.FrictionMultiplier(1, 1), 1);
    }

    [Fact]
    public void AssistTargetUsesCurrentActiveCollisionCenter()
    {
        using var scene = new Scene();
        PlayerEntity player = scene.Players[0];
        player.Position = new Vector3(4, 5, 6);
        player._volume = new CollisionVolume(new Vector3(4, 7, 6), .5f);

        Assert.Equal(new Vector3(4, 7, 6), player.ModAssistAimTarget);
        Assert.NotEqual(player.ModAimTarget, player.ModAssistAimTarget);
    }

    [Fact]
    public void HeadPreferenceUsesAuthoritativeRegionAndOnlyHeadshotCapableShots()
    {
        using var scene = new Scene();
        PlayerEntity player = scene.Players[0];
        player.Position = new Vector3(4, 5, 6);

        float top = Fixed.ToFloat(player.Values.MaxPickupHeight);
        Assert.Equal(top - .15f,
            player.ModAssistHeadTarget.Y - player.Position.Y, 5);
        Assert.True(PlayerAimAssist.ShouldPreferHead(altForm: false,
            weaponCanHeadshot: true, bodyErrorDegrees: 2.5f,
            headErrorDegrees: 3f));
        Assert.False(PlayerAimAssist.ShouldPreferHead(altForm: true,
            weaponCanHeadshot: true, bodyErrorDegrees: 2.5f,
            headErrorDegrees: 3f));
        Assert.False(PlayerAimAssist.ShouldPreferHead(altForm: false,
            weaponCanHeadshot: false, bodyErrorDegrees: 2.5f,
            headErrorDegrees: 3f));

        Assert.True(PlayerAimAssist.WeaponCanHeadshot(
            BeamType.Imperialist, 80));
        Assert.True(PlayerAimAssist.WeaponCanHeadshot(
            BeamType.PowerBeam, 15));
        Assert.False(PlayerAimAssist.WeaponCanHeadshot(
            BeamType.PowerBeam, 15.01f));
        Assert.False(PlayerAimAssist.WeaponCanHeadshot(
            BeamType.ShockCoil, 1));
    }

    [Fact]
    public void ProjectedTargetSizeMakesCloseAcquisitionGeometryMoreForgiving()
    {
        float close = PlayerAimAssist.ProjectedConeBonus(.5f, 5);
        float far = PlayerAimAssist.ProjectedConeBonus(.5f, 50);

        Assert.True(close > far);
        Assert.InRange(close, 2.8f, 2.9f);
        Assert.InRange(far, .28f, .29f);
        Assert.Equal(0, PlayerAimAssist.ProjectedConeBonus(float.NaN, 5));
    }

    [Fact]
    public void MotionTrackingAddsBoundedTargetAngularVelocity()
    {
        Vector2 ordinary = PlayerAimAssist.MotionTrackingCorrection(
            new Vector2(2, 1), new Vector2(1.6f, .8f));
        Assert.Equal(.3f, ordinary.X, 5);
        Assert.Equal(.15f, ordinary.Y, 5);

        Vector2 bounded = PlayerAimAssist.MotionTrackingCorrection(
            new Vector2(10, 0), Vector2.Zero);
        Assert.Equal(.65f, bounded.Length, 5);
        Assert.Equal(Vector2.Zero, PlayerAimAssist.MotionTrackingCorrection(
            new Vector2(float.NaN, 0), Vector2.Zero));
    }
}
