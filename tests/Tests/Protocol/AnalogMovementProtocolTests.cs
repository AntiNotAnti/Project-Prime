using System;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AnalogMovementProtocolTests
{
    [Fact]
    public void MovementSampleKeepsRadialVectorAlongsideDigitalHysteresis()
    {
        var processor = new GamepadMovementProcessor();

        GamepadMovementSample cardinal = processor.Process(new Vector2(.5f, 0));
        Assert.True(cardinal.Active);
        Assert.Equal(GamepadMovementDirection.Right, cardinal.Direction);
        Assert.InRange(cardinal.Magnitude, .4117f, .4119f);
        Assert.Equal(cardinal.Magnitude, cardinal.Vector.X, 5);
        Assert.Equal(0, cardinal.Vector.Y, 5);

        GamepadMovementSample diagonal = processor.Process(new Vector2(.75f, .75f));
        Assert.Equal(GamepadMovementDirection.Up | GamepadMovementDirection.Right,
            diagonal.Direction);
        Assert.InRange(diagonal.Vector.Length, .9999f, 1.0001f);

        GamepadMovementSample held = processor.Process(new Vector2(.19f, 0));
        Assert.True(held.Active);
        Assert.Equal(GamepadMovementDirection.Right, held.Direction);
        Assert.InRange(held.Magnitude, .047f, .048f);

        GamepadMovementSample released = processor.Process(Vector2.Zero);
        Assert.False(released.Active);
        Assert.Equal(Vector2.Zero, released.Vector);
        Assert.Equal(0, released.Magnitude);
    }

    [Fact]
    public void MovementCodecQuantizesCardinalsAndRoundedDiagonals()
    {
        Assert.True(AnalogMovementCodec.TryQuantize(new Vector2(.5f, 0),
            out sbyte x, out sbyte y));
        Assert.Equal((sbyte)64, x);
        Assert.Equal((sbyte)0, y);
        Assert.True(AnalogMovementCodec.TryDecode(x, y, present: true,
            out Vector2 cardinal));
        Assert.InRange(cardinal.X, .5038f, .5040f);

        Assert.True(AnalogMovementCodec.TryDecode(90, 90, present: true,
            out Vector2 diagonal));
        Assert.InRange(diagonal.Length, .99999f, 1.00001f);
        Assert.True(diagonal.X > .70f && diagonal.Y > .70f);

        Assert.False(AnalogMovementCodec.TryDecode(sbyte.MinValue, 0, true, out _));
        Assert.False(AnalogMovementCodec.TryDecode(127, 127, true, out _));
        Assert.False(AnalogMovementCodec.TryDecode(1, 0, false, out _));
        Assert.True(AnalogMovementCodec.TryDecode(0, 0, true, out Vector2 explicitNeutral));
        Assert.Equal(Vector2.Zero, explicitNeutral);
    }

    [Fact]
    public void InputCommandRoundTripsAnalogPresenceAndNeutralFallback()
    {
        var command = new InputCommand(5, 5, 4, InputButtons.Right,
            InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon,
            BoostIntent.None, 2, 64, 90, analogMovementPresent: true);
        Span<byte> wire = stackalloc byte[InputCommand.Size];
        command.Write(wire);
        Assert.True(InputCommand.TryRead(wire, out InputCommand decoded));
        Assert.Equal(command, decoded);
        Assert.True(decoded.AnalogMovementPresent);
        Assert.Equal(64 / 127f, decoded.AnalogMovement.X, 5);
        Assert.Equal(90 / 127f, decoded.AnalogMovement.Y, 5);
        Assert.InRange(decoded.AnalogMovement.Length, .8695f, .8697f);

        InputCommand withoutEdges = decoded.WithoutEdges();
        Assert.True(withoutEdges.AnalogMovementPresent);
        Assert.Equal(decoded.MoveX, withoutEdges.MoveX);
        Assert.Equal(decoded.MoveY, withoutEdges.MoveY);

        InputCommand neutral = decoded.Neutral();
        Assert.False(neutral.AnalogMovementPresent);
        Assert.Equal((sbyte)0, neutral.MoveX);
        Assert.Equal((sbyte)0, neutral.MoveY);
    }

    [Theory]
    [InlineData(0, 0, .5f)]
    [InlineData(1, 1f, .75f)]
    [InlineData(-1, -1f, -.25f)]
    public void MovementAxisPreservesDigitalIntentAndScalesControllerOnlyInput(
        float expectedDigital, float digital, float analog)
    {
        float result = PlayerEntity.ResolveAnalogMovementAxis(digital, analog,
            analogPresent: true);
        if (expectedDigital == 0)
        {
            Assert.Equal(analog, result, 5);
        }
        else
        {
            Assert.True(MathF.Abs(result) >= MathF.Abs(expectedDigital));
        }
    }
}
