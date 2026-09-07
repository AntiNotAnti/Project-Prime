using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    [Flags]
    public enum InputButtons : uint
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Forward = 1 << 2,
        Back = 1 << 3,
        Shoot = 1 << 4,
        Zoom = 1 << 5,
        Jump = 1 << 6,
        Morph = 1 << 7,
        Boost = 1 << 8,
        AltAttack = 1 << 9,
        NextWeapon = 1 << 10,
        PreviousWeapon = 1 << 11,
        RollLeft = 1 << 12,
        RollRight = 1 << 13,
        RollForward = 1 << 14,
        RollBack = 1 << 15,
        Spectate = 1 << 16,
        All = (1 << 17) - 1
    }

    /// <summary>
    /// One 60 Hz input sample. ViewServerTick is the last presented remote-world
    /// timeline, floored to a whole tick, or the newest usable startup snapshot.
    /// The server validates this timing hint; no client-owned gameplay state.
    /// </summary>
    public readonly record struct InputCommand(uint Sequence, uint ClientTick, uint ViewServerTick,
        InputButtons Buttons, InputButtons Pressed, Vector3 Aim, byte DesiredWeapon)
    {
        public const int Size = 33;
        public const byte NoWeapon = Byte.MaxValue;

        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], ClientTick);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], ViewServerTick);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], (uint)Buttons);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], (uint)Pressed);
            BinaryPrimitives.WriteSingleLittleEndian(destination[20..], Aim.X);
            BinaryPrimitives.WriteSingleLittleEndian(destination[24..], Aim.Y);
            BinaryPrimitives.WriteSingleLittleEndian(destination[28..], Aim.Z);
            destination[32] = DesiredWeapon;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out InputCommand command)
        {
            command = default;
            if (source.Length != Size || (source[32] != NoWeapon && source[32] > 8))
            {
                return false;
            }
            var buttons = (InputButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);
            var pressed = (InputButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[16..]);
            var aim = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(source[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[24..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[28..]));
            // Bound before normalization. A zero, infinite or enormous ray
            // must never introduce NaNs into shared collision state.
            if (((buttons | pressed) & ~InputButtons.All) != 0
                || !Single.IsFinite(aim.X) || !Single.IsFinite(aim.Y) || !Single.IsFinite(aim.Z)
                || aim.LengthSquared < 0.5f || aim.LengthSquared > 1.5f)
            {
                return false;
            }
            command = new InputCommand(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[8..]), buttons, pressed, aim, source[32]);
            return true;
        }

        public InputCommand WithoutEdges() => this with { Pressed = InputButtons.None, DesiredWeapon = NoWeapon };
        public InputCommand Neutral() => WithoutEdges() with { Buttons = Buttons & InputButtons.Spectate };
    }

    public static class InputBundle
    {
        public const int Capacity = 8;
        public const int HeaderSize = 9;
        public const int MaxSize = HeaderSize + Capacity * InputCommand.Size;

        /// <summary>Commands are encoded oldest first, with contiguous sequences.</summary>
        public static int Write(Span<byte> destination, uint matchId, ReadOnlySpan<InputCommand> commands, uint phaseRevision = 0)
        {
            if (commands.Length == 0 || commands.Length > Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(commands));
            }
            BinaryPrimitives.WriteUInt32LittleEndian(destination, matchId);
            destination[4] = (byte)commands.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[5..], phaseRevision);
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i].Write(destination.Slice(HeaderSize + i * InputCommand.Size, InputCommand.Size));
            }
            return HeaderSize + commands.Length * InputCommand.Size;
        }

        /// <summary>Validate the entire bundle before the caller changes input state.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, Span<InputCommand> commands,
            out uint matchId, out int count) => TryRead(source, commands, out matchId, out _, out count);

        public static bool TryRead(ReadOnlySpan<byte> source, Span<InputCommand> commands,
            out uint matchId, out uint phaseRevision, out int count)
        {
            phaseRevision = 0;
            matchId = 0;
            count = 0;
            if (source.Length < HeaderSize || source[4] == 0 || source[4] > Capacity
                || source[4] > commands.Length || source.Length != HeaderSize + source[4] * InputCommand.Size)
            {
                return false;
            }
            int length = source[4];
            for (int i = 0; i < length; i++)
            {
                if (!InputCommand.TryRead(source.Slice(HeaderSize + i * InputCommand.Size, InputCommand.Size), out commands[i])
                    || (i > 0 && (commands[i].Sequence != unchecked(commands[i - 1].Sequence + 1)
                        || commands[i].ClientTick != unchecked(commands[i - 1].ClientTick + 1))))
                {
                    return false;
                }
            }
            matchId = BinaryPrimitives.ReadUInt32LittleEndian(source);
            phaseRevision = BinaryPrimitives.ReadUInt32LittleEndian(source[5..]);
            count = length;
            return true;
        }
    }
}
