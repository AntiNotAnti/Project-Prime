using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class InputTests
    {
        private static InputCommand Command(uint sequence, InputButtons pressed = InputButtons.None)
            => new(sequence, sequence, 42, InputButtons.Forward | InputButtons.Shoot,
                pressed, -Vector3.UnitZ, InputCommand.NoWeapon);

        [Fact]
        public void BundleRoundTripsAcrossWrapAndRejectsPartialOrInvalidCommands()
        {
            var commands = new InputCommand[8];
            for (uint i = 0; i < 8; i++) { commands[i] = Command(unchecked(UInt32.MaxValue - 3 + i)); }
            byte[] bytes = new byte[InputBundle.MaxSize];
            Assert.Equal(bytes.Length, InputBundle.Write(bytes, 7, commands, phaseRevision: 19));
            var decoded = new InputCommand[8];
            Assert.True(InputBundle.TryRead(bytes, decoded, out uint match, out uint phaseRevision, out int count));
            Assert.Equal(7u, match);
            Assert.Equal(19u, phaseRevision);
            Assert.Equal(8, count);
            Assert.Equal(commands, decoded);
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.False(InputBundle.TryRead(bytes.AsSpan(0, length), decoded, out _, out _));
            }
            Assert.False(InputBundle.TryRead(new byte[bytes.Length + 1], decoded, out _, out _));
            bytes[^1] = 254;
            Assert.False(InputBundle.TryRead(bytes, decoded, out _, out _));
            InputBundle.Write(bytes, 7, commands);
            bytes[InputBundle.HeaderSize + InputCommand.Size] ^= 2;
            Assert.False(InputBundle.TryRead(bytes, decoded, out _, out _));
        }

        [Fact]
        public void InputRejectsNonfiniteNonunitAimAndUnknownButtons()
        {
            byte[] bytes = new byte[InputCommand.Size];
            foreach (float invalid in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity, Single.MaxValue })
            {
                (Command(0) with { Aim = new Vector3(invalid, 0, -1) }).Write(bytes);
                Assert.False(InputCommand.TryRead(bytes, out _));
            }
            (Command(0) with { Aim = Vector3.Zero }).Write(bytes);
            Assert.False(InputCommand.TryRead(bytes, out _));
            (Command(0) with { Buttons = (InputButtons)(1u << 31) }).Write(bytes);
            Assert.False(InputCommand.TryRead(bytes, out _));
        }

        [Fact]
        public void ViewTicksPreserveHeldFramesClockCorrectionsAndUintWrapOnTheWire()
        {
            InputCommand[] commands =
            {
                Command(0) with { ViewServerTick = UInt32.MaxValue },
                Command(1) with { ViewServerTick = 0 },
                Command(2) with { ViewServerTick = 0 },
                Command(3) with { ViewServerTick = UInt32.MaxValue }
            };
            byte[] bytes = new byte[InputBundle.HeaderSize + commands.Length * InputCommand.Size];
            InputBundle.Write(bytes, 1, commands, phaseRevision: 19);
            var decoded = new InputCommand[commands.Length];
            Assert.True(InputBundle.TryRead(bytes, decoded, out _, out uint phaseRevision, out _));
            Assert.Equal(19u, phaseRevision);
            Assert.Equal(commands, decoded);
            Assert.Equal(UInt32.MaxValue,
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(InputBundle.HeaderSize + 8)));
        }

        [Fact]
        public void RedundancyDoesNotRepeatEdgesOrAccelerateSimulation()
        {
            var stream = new ServerInputStream();
            var commands = new InputCommand[8];
            for (uint i = 0; i < 8; i++) { commands[i] = Command(i, InputButtons.Jump); }
            for (int duplicate = 0; duplicate < 100; duplicate++) { stream.Receive(commands, 0); }
            Assert.False(stream.HasProcessed);
            int jumps = 0;
            for (uint tick = 0; tick < 16; tick++)
            {
                InputCommand input = stream.Take(tick);
                if ((input.Pressed & InputButtons.Jump) != 0) { jumps++; }
            }
            Assert.Equal(8, jumps);
            Assert.Equal(7u, stream.LastProcessed);
            Assert.Equal(InputButtons.None, stream.Take(20).Buttons);
            Assert.True(stream.StarvedTicks > 0);
        }

        [Fact]
        public void LateCommandsCannotRewindAndLongLossDoesNotWedgeTheStream()
        {
            var stream = new ServerInputStream();
            stream.Receive(new[] { Command(UInt32.MaxValue), Command(0) }, 0);
            for (uint tick = 0; tick < 4; tick++) { stream.Take(tick); }
            Assert.Equal(0u, stream.LastProcessed);
            stream.Receive(new[] { Command(UInt32.MaxValue) }, 5);
            Assert.Equal(0u, stream.LastProcessed);
            Assert.Equal(1, stream.LateCommands);
            stream.Receive(new[] { Command(600, InputButtons.Morph) }, 600);
            InputCommand recovered = default;
            for (uint tick = 600; tick < 604; tick++)
            {
                InputCommand value = stream.Take(tick);
                if (value.Pressed != InputButtons.None) { recovered = value; }
            }
            Assert.Equal(600u, recovered.Sequence);
            Assert.Equal(InputButtons.Morph, recovered.Pressed);
            Assert.True(stream.SkippedCommands >= 599);
        }
    }
}
