using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class MorphBallBoostInputTests
{
    [Fact]
    public void FlickLegalityRequiresStableAvailableSamusAltAndZeroCooldown()
    {
        Assert.True(CanActivate());
        Assert.False(CanActivate(alive: false));
        Assert.False(CanActivate(frozen: true));
        Assert.False(CanActivate(hunter: Hunter.Kanden));
        Assert.False(CanActivate(isAltForm: false));
        Assert.False(CanActivate(isMorphing: true));
        Assert.False(CanActivate(isUnmorphing: true));
        Assert.False(CanActivate(hasBoostAbility: false));
        Assert.False(CanActivate(cooldown: 1));

        // PlayerFlags1.Boosting is intentionally not an eligibility input:
        // it is velocity-lived and must not block a later legal flick.
        Assert.True(CanActivate());
    }

    [Fact]
    public void FirstFlickWinsAndHeldChargeIsOnlyDerivedWithoutFlick()
    {
        var input = new PlayerEntity.PlayerInput();
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent first));
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitY, out BoostIntent second));

        Assert.True(input.QueueBoostIntent(BoostIntent.Charge));
        Assert.True(input.QueueBoostIntent(first));
        Assert.False(input.QueueBoostIntent(second));
        Assert.Equal(first, input.TakeBoostIntent(boostHeld: true));
        Assert.Equal(first, input.ConsumedBoostIntent);

        Assert.Equal(BoostIntent.Charge, input.TakeBoostIntent(boostHeld: true));
        Assert.Equal(BoostIntent.None, input.TakeBoostIntent(boostHeld: false));
    }

    [Fact]
    public void ClearDropsPendingAndConsumedRequests()
    {
        var input = new PlayerEntity.PlayerInput();
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent flick));
        Assert.True(input.QueueBoostIntent(flick));
        Assert.Equal(flick, input.TakeBoostIntent(boostHeld: false));
        Assert.True(input.QueueBoostIntent(flick));

        input.ClearBoostIntents();

        Assert.Equal(BoostIntent.None, input.ConsumedBoostIntent);
        Assert.Equal(BoostIntent.None, input.TakeBoostIntent(boostHeld: false));
    }

    [Fact]
    public void QuantizedIntentIsIdenticalForLocalCaptureWireAndServerTick()
    {
        Assert.True(BoostIntent.TryCreateFlick(new Vector2(.31337f, -.913f),
            out BoostIntent local));
        var localInput = new PlayerEntity.PlayerInput();
        Assert.True(localInput.QueueBoostIntent(local));
        BoostIntent consumed = localInput.TakeBoostIntent(boostHeld: false);
        var command = new InputCommand(7, 7, 5, InputButtons.None,
            InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon, consumed);
        Span<byte> wire = stackalloc byte[InputCommand.Size];
        command.Write(wire);
        Assert.True(InputCommand.TryRead(wire, out InputCommand decoded));
        var serverInput = new PlayerEntity.PlayerInput();
        Assert.True(serverInput.QueueBoostIntent(decoded.BoostRequest));
        BoostIntent authoritative = serverInput.TakeBoostIntent(boostHeld: false);

        Assert.Equal(local, consumed);
        Assert.Equal(consumed, decoded.BoostRequest);
        Assert.Equal(consumed, authoritative);
        Assert.Equal(consumed.Direction, authoritative.Direction);
    }

    [Theory]
    [InlineData(0, 0, -1, 0, 1)]
    [InlineData(0, 1, 0, 1, 0)]
    [InlineData(0, 0, 1, 0, -1)]
    [InlineData(0, -1, 0, -1, 0)]
    [InlineData(90, 0, -1, 1, 0)]
    [InlineData(90, 1, 0, 0, -1)]
    [InlineData(180, 0, -1, 0, -1)]
    [InlineData(270, 0, -1, -1, 0)]
    public void ScreenDirectionsResolveAgainstHorizontalRollBasis(
        float headingDegrees, float screenX, float screenY,
        float expectedX, float expectedZ)
    {
        float radians = MathHelper.DegreesToRadians(headingDegrees);
        float forwardX = MathF.Sin(radians);
        float forwardZ = MathF.Cos(radians);
        float leftX = -MathF.Cos(radians);
        float leftZ = MathF.Sin(radians);
        var screen = new Vector2(screenX, screenY);
        Assert.True(BoostIntent.TryCreateFlick(screen, out BoostIntent intent));

        Assert.True(PlayerEntity.TryResolveBoostDirection(intent,
            forwardX, forwardZ, leftX, leftZ, out Vector3 direction));
        Assert.InRange(direction.X, expectedX - .0001f, expectedX + .0001f);
        Assert.Equal(0, direction.Y);
        Assert.InRange(direction.Z, expectedZ - .0001f, expectedZ + .0001f);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void DiagonalDirectionsRemainUnitLength(float x, float y)
    {
        Assert.True(BoostIntent.TryCreateFlick(new Vector2(x, y), out BoostIntent intent));
        Assert.True(PlayerEntity.TryResolveBoostDirection(intent,
            forwardX: 0, forwardZ: 1, leftX: -1, leftZ: 0,
            out Vector3 direction));
        Assert.InRange(direction.Length, .99999f, 1.00001f);
        Assert.Equal(MathF.Sign(x), MathF.Sign(direction.X));
        Assert.Equal(-MathF.Sign(y), MathF.Sign(direction.Z));
    }

    [Fact]
    public void AllEightScreenDirectionsResolveAtFourHeadingsWithoutPitch()
    {
        Vector2[] screenDirections =
        {
            new(0, -1), new(1, -1), new(1, 0), new(1, 1),
            new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1)
        };
        foreach (float heading in new[] { 0f, 90f, 180f, 270f })
        {
            float radians = MathHelper.DegreesToRadians(heading);
            float sin = MathF.Sin(radians);
            float cos = MathF.Cos(radians);
            foreach (Vector2 screen in screenDirections)
            {
                Assert.True(BoostIntent.TryCreateFlick(screen, out BoostIntent intent));
                Assert.True(PlayerEntity.TryResolveBoostDirection(intent,
                    forwardX: sin, forwardZ: cos,
                    leftX: -cos, leftZ: sin, out Vector3 actual));
                var expected = new Vector3(
                    cos * screen.X - sin * screen.Y,
                    0,
                    -sin * screen.X - cos * screen.Y).Normalized();
                Assert.InRange((actual - expected).Length, 0, .0001f);
            }
        }
        // Only the horizontal roll basis is accepted by the resolver; no
        // presentation-camera pitch value can alter these results.
    }

    [Fact]
    public void DegenerateOrNonfiniteRollBasisIsRejected()
    {
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent intent));
        Assert.False(PlayerEntity.TryResolveBoostDirection(intent,
            0, 0, 0, 0, out _));
        Assert.False(PlayerEntity.TryResolveBoostDirection(intent,
            0, 1, 0, 2, out _));
        Assert.False(PlayerEntity.TryResolveBoostDirection(intent,
            float.NaN, 1, -1, 0, out _));
    }

    private static bool CanActivate(bool alive = true, bool frozen = false,
        Hunter hunter = Hunter.Samus, bool isAltForm = true,
        bool isMorphing = false, bool isUnmorphing = false,
        bool hasBoostAbility = true, ushort cooldown = 0)
        => PlayerEntity.CanActivateDirectionalBoost(alive, frozen, hunter,
            isAltForm, isMorphing, isUnmorphing, hasBoostAbility, cooldown);
}
