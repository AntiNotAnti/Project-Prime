using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class MorphBallBoostProtocolTests
{
    [Theory]
    [InlineData(1, 0, 127, 0)]
    [InlineData(-1, 0, -127, 0)]
    [InlineData(0, 1, 0, 127)]
    [InlineData(0, -1, 0, -127)]
    [InlineData(1, 1, 90, 90)]
    [InlineData(-1, -1, -90, -90)]
    public void FlickCreationQuantizesOnceAndDecodesNormalized(
        float x, float y, int encodedX, int encodedY)
    {
        Assert.True(BoostIntent.TryCreateFlick(new Vector2(x, y), out BoostIntent intent));
        Assert.Equal(BoostActivation.Flick, intent.Activation);
        Assert.Equal((sbyte)encodedX, intent.X);
        Assert.Equal((sbyte)encodedY, intent.Y);
        Assert.InRange(intent.Direction.Length, .99999f, 1.00001f);
        Assert.Equal(MathF.Sign(x), MathF.Sign(intent.Direction.X));
        Assert.Equal(MathF.Sign(y), MathF.Sign(intent.Direction.Y));
    }

    [Fact]
    public void FlickCreationRejectsZeroAndNonfiniteDirections()
    {
        Assert.False(BoostIntent.TryCreateFlick(Vector2.Zero, out _));
        Assert.False(BoostIntent.TryCreateFlick(new Vector2(float.NaN, 1), out _));
        Assert.False(BoostIntent.TryCreateFlick(
            new Vector2(float.PositiveInfinity, 1), out _));
    }

    [Fact]
    public void CommandsRoundTripAllModesAtTheExactProtocolSize()
    {
        Assert.Equal(43, InputCommand.Size);
        Assert.Equal(353, InputBundle.MaxSize);
        Assert.Equal(16, NetHeader.Version);
        Assert.True(BoostIntent.TryCreateFlick(new Vector2(.25f, -1),
            out BoostIntent flick));
        InputCommand[] commands =
        {
            Command(1, InputButtons.None, BoostIntent.None),
            Command(2, InputButtons.Boost, BoostIntent.Charge),
            Command(3, InputButtons.Boost, flick),
            Command(4, InputButtons.None, flick)
        };

        foreach (InputCommand expected in commands)
        {
            byte[] wire = new byte[InputCommand.Size];
            expected.Write(wire);
            Assert.True(InputCommand.TryRead(wire, out InputCommand actual));
            Assert.Equal(expected, actual);
            Assert.Equal(expected.BoostRequest, actual.BoostRequest);
        }
    }

    [Fact]
    public void SevenArgumentConstructorDerivesOnlyHeldCharge()
    {
        InputCommand held = new(1, 1, 1, InputButtons.Boost,
            InputButtons.Boost, -Vector3.UnitZ, InputCommand.NoWeapon);
        InputCommand edgeOnly = new(2, 2, 2, InputButtons.None,
            InputButtons.Boost, -Vector3.UnitZ, InputCommand.NoWeapon);

        Assert.Equal(BoostActivation.Charge, held.BoostActivation);
        Assert.Equal(BoostActivation.None, edgeOnly.BoostActivation);
    }

    [Fact]
    public void MalformedModesDirectionsAndHeldChargeContractsAreRejected()
    {
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent flick));
        AssertRejected(Command(1, InputButtons.None, flick), 37, 3); // unknown mode
        AssertRejected(Command(1, InputButtons.None, flick), 38, 0x80); // forbidden -128
        AssertRejected(Command(1, InputButtons.None, flick), 39, 0x80); // forbidden -128
        AssertRejectedZeroFlick(Command(1, InputButtons.None, flick));

        Assert.True(BoostIntent.TryDecode(BoostActivation.Flick, 127, -127,
            out BoostIntent extreme));
        Span<byte> extremeWire = stackalloc byte[InputCommand.Size];
        Command(1, InputButtons.None, extreme).Write(extremeWire);
        Assert.True(InputCommand.TryRead(extremeWire, out InputCommand decodedExtreme));
        Assert.Equal(extreme, decodedExtreme.BoostRequest);

        InputCommand none = Command(1, InputButtons.None, BoostIntent.None);
        AssertRejected(none, 38, 1); // reserved direction on None
        InputCommand charge = Command(1, InputButtons.Boost, BoostIntent.Charge);
        AssertRejected(charge, 39, 1); // reserved direction on Charge
        AssertRejected(charge, 13, 0); // Charge without held Boost
        InputCommand heldNone = none with { Buttons = InputButtons.Boost };
        AssertRejected(heldNone, 37, (byte)BoostActivation.None);
    }

    [Fact]
    public void BundleFailureIsAtomicForCallerOutput()
    {
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitY, out BoostIntent flick));
        InputCommand[] source =
        {
            Command(10, InputButtons.None, BoostIntent.None),
            Command(11, InputButtons.None, flick)
        };
        byte[] wire = new byte[InputBundle.HeaderSize + source.Length * InputCommand.Size];
        int length = InputBundle.Write(wire, 7, source);
        wire[InputBundle.HeaderSize + InputCommand.Size + 37] = 0xFF;
        InputCommand sentinel = Command(99, InputButtons.Boost, BoostIntent.Charge);
        var destination = new[] { sentinel, sentinel };

        Assert.False(InputBundle.TryRead(wire.AsSpan(0, length), destination,
            out _, out _, out _));
        Assert.Equal(new[] { sentinel, sentinel }, destination);
    }

    [Fact]
    public void EdgeAndStarvationFallbackNeverRepeatFlick()
    {
        Assert.True(BoostIntent.TryCreateFlick(new Vector2(-1, 1),
            out BoostIntent flick));
        var stream = new ServerInputStream();
        InputCommand command = Command(10, InputButtons.Boost, flick);
        for (int duplicate = 0; duplicate < 4; duplicate++)
            stream.Receive(new[] { command }, 100);

        int flicks = 0;
        int charges = 0;
        for (uint tick = 100; tick < 115; tick++)
        {
            InputCommand taken = stream.Take(tick);
            if (taken.BoostActivation == BoostActivation.Flick) flicks++;
            if (taken.BoostActivation == BoostActivation.Charge) charges++;
        }

        Assert.Equal(1, flicks);
        Assert.True(charges > 0); // held Boost may survive the short gap
        Assert.Equal(BoostActivation.None, stream.Take(120).BoostActivation);
    }

    [Fact]
    public void WithoutEdgesErasesFlickAndNeutralAlwaysClearsBoost()
    {
        Assert.True(BoostIntent.TryCreateFlick(Vector2.UnitX, out BoostIntent flick));
        InputCommand heldFlick = Command(1, InputButtons.Boost, flick) with
        {
            Pressed = InputButtons.Boost,
            DesiredWeapon = 2
        };
        InputCommand held = heldFlick.WithoutEdges();
        Assert.Equal(InputButtons.None, held.Pressed);
        Assert.Equal(InputCommand.NoWeapon, held.DesiredWeapon);
        Assert.Equal(BoostActivation.Charge, held.BoostActivation);
        Assert.Equal(0, held.BoostDirectionX);
        Assert.Equal(0, held.BoostDirectionY);

        InputCommand unheld = Command(2, InputButtons.None, flick).WithoutEdges();
        Assert.Equal(BoostActivation.None, unheld.BoostActivation);
        InputCommand neutral = heldFlick.Neutral();
        Assert.Equal(InputButtons.None, neutral.Buttons);
        Assert.Equal(BoostActivation.None, neutral.BoostActivation);
    }

    private static InputCommand Command(uint sequence, InputButtons buttons,
        in BoostIntent intent) => new(sequence, sequence, 42, buttons,
            InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon, intent);

    private static void AssertRejected(InputCommand command, int offset, byte value)
    {
        Span<byte> wire = stackalloc byte[InputCommand.Size];
        command.Write(wire);
        wire[offset] = value;
        Assert.False(InputCommand.TryRead(wire, out _));
    }

    private static void AssertRejectedZeroFlick(InputCommand command)
    {
        Span<byte> wire = stackalloc byte[InputCommand.Size];
        command.Write(wire);
        wire[38] = 0;
        wire[39] = 0;
        Assert.False(InputCommand.TryRead(wire, out _));
    }
}
