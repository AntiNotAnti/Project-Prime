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
    /// Canonical wire conversion for the optional radial movement sample.
    /// Axes deliberately use -127..127; -128 is reserved so malformed values
    /// cannot be mistaken for a valid direction after sign extension.
    /// </summary>
    public static class AnalogMovementCodec
    {
        public const int MaxAxis = 127;
        private const float RadialTolerance = 2 / (float)MaxAxis;

        public static bool TryQuantize(Vector2 movement, out sbyte moveX, out sbyte moveY)
        {
            moveX = moveY = 0;
            if (!IsFinite(movement)) return false;
            float radial = movement.Length;
            if (!float.IsFinite(radial) || radial > 1 + 0.0001f) return false;
            moveX = (sbyte)Math.Clamp((int)MathF.Round(
                Math.Clamp(movement.X, -1, 1) * MaxAxis, MidpointRounding.AwayFromZero),
                -MaxAxis, MaxAxis);
            moveY = (sbyte)Math.Clamp((int)MathF.Round(
                Math.Clamp(movement.Y, -1, 1) * MaxAxis, MidpointRounding.AwayFromZero),
                -MaxAxis, MaxAxis);
            return true;
        }

        public static bool TryDecode(sbyte moveX, sbyte moveY, bool present,
            out Vector2 movement)
        {
            movement = Vector2.Zero;
            if (moveX == sbyte.MinValue || moveY == sbyte.MinValue)
            {
                return false;
            }
            if (!present)
            {
                return moveX == 0 && moveY == 0;
            }
            movement = new Vector2(moveX / (float)MaxAxis, moveY / (float)MaxAxis);
            if (!IsFinite(movement)) return false;
            float radial = movement.Length;
            if (!float.IsFinite(radial) || radial > 1 + RadialTolerance) return false;
            // Encoder-rounded diagonals such as (90,90) are just over unit
            // length. Normalize that bounded tolerance, but reject genuinely
            // malformed radial values instead of silently changing intent.
            if (radial > 1)
            {
                movement /= radial;
            }
            return IsFinite(movement);
        }

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    /// <summary>
    /// One 60 Hz input sample. ViewServerTick is the last presented remote-world
    /// timeline, floored to a whole tick, or the newest usable startup snapshot.
    /// InputEpoch is the client's observed local CombatActor life. The server
    /// validates both hints; neither is client-owned gameplay state.
    /// </summary>
    public readonly record struct InputCommand(uint Sequence, uint ClientTick, uint ViewServerTick,
        InputButtons Buttons, InputButtons Pressed, Vector3 Aim, byte DesiredWeapon,
        BoostActivation BoostActivation, sbyte BoostDirectionX, sbyte BoostDirectionY,
        uint InputEpoch, sbyte MoveX, sbyte MoveY, bool AnalogMovementPresent)
    {
        public const int Size = 43;
        public const byte NoWeapon = Byte.MaxValue;

        // The source-compatible constructors intentionally target the first
        // server life. Live capture supplies the authoritative snapshot life
        // explicitly through the overload below.
        private const uint DefaultInputEpoch = 1;

        /// <summary>
        /// Source-compatible constructor for existing callers. The held Boost
        /// bit remains charge authority; an edge alone is not a held charge.
        /// </summary>
        public InputCommand(uint sequence, uint clientTick, uint viewServerTick,
            InputButtons buttons, InputButtons pressed, Vector3 aim, byte desiredWeapon)
            : this(sequence, clientTick, viewServerTick, buttons, pressed, aim,
                desiredWeapon,
                (buttons & InputButtons.Boost) != 0
                    ? BoostActivation.Charge : BoostActivation.None,
                0, 0, DefaultInputEpoch, 0, 0, false)
        {
        }

        public InputCommand(uint sequence, uint clientTick, uint viewServerTick,
            InputButtons buttons, InputButtons pressed, Vector3 aim, byte desiredWeapon,
            in BoostIntent boostIntent)
            : this(sequence, clientTick, viewServerTick, buttons, pressed, aim,
                desiredWeapon, boostIntent.Activation, boostIntent.X, boostIntent.Y,
                DefaultInputEpoch, 0, 0, false)
        {
        }

        public InputCommand(uint sequence, uint clientTick, uint viewServerTick,
            InputButtons buttons, InputButtons pressed, Vector3 aim, byte desiredWeapon,
            uint inputEpoch)
            : this(sequence, clientTick, viewServerTick, buttons, pressed, aim,
                desiredWeapon,
                (buttons & InputButtons.Boost) != 0
                    ? BoostActivation.Charge : BoostActivation.None,
                0, 0, inputEpoch, 0, 0, false)
        {
        }

        public InputCommand(uint sequence, uint clientTick, uint viewServerTick,
            InputButtons buttons, InputButtons pressed, Vector3 aim, byte desiredWeapon,
            in BoostIntent boostIntent, uint inputEpoch)
            : this(sequence, clientTick, viewServerTick, buttons, pressed, aim,
                desiredWeapon, boostIntent.Activation, boostIntent.X, boostIntent.Y,
                inputEpoch, 0, 0, false)
        {
        }

        public InputCommand(uint sequence, uint clientTick, uint viewServerTick,
            InputButtons buttons, InputButtons pressed, Vector3 aim, byte desiredWeapon,
            in BoostIntent boostIntent, uint inputEpoch, sbyte moveX, sbyte moveY,
            bool analogMovementPresent)
            : this(sequence, clientTick, viewServerTick, buttons, pressed, aim,
                desiredWeapon, boostIntent.Activation, boostIntent.X, boostIntent.Y,
                inputEpoch, moveX, moveY, analogMovementPresent)
        {
            if (!AnalogMovementCodec.TryDecode(moveX, moveY, analogMovementPresent,
                out _))
            {
                throw new ArgumentOutOfRangeException(nameof(moveX),
                    "Analog movement must be finite, bounded, and use -127..127.");
            }
        }

        public Vector2 AnalogMovement
            => AnalogMovementCodec.TryDecode(MoveX, MoveY, AnalogMovementPresent,
                out Vector2 movement) ? movement : Vector2.Zero;

        public BoostIntent BoostRequest
            => BoostIntent.TryDecode(BoostActivation, BoostDirectionX,
                BoostDirectionY, out BoostIntent intent) ? intent : default;

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
            BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], InputEpoch);
            destination[36] = DesiredWeapon;
            destination[37] = (byte)BoostActivation;
            destination[38] = unchecked((byte)BoostDirectionX);
            destination[39] = unchecked((byte)BoostDirectionY);
            destination[40] = unchecked((byte)MoveX);
            destination[41] = unchecked((byte)MoveY);
            destination[42] = AnalogMovementPresent ? (byte)1 : (byte)0;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out InputCommand command)
        {
            command = default;
            if (source.Length != Size)
            {
                return false;
            }
            uint inputEpoch = BinaryPrimitives.ReadUInt32LittleEndian(source[32..]);
            if (inputEpoch == 0 || (source[36] != NoWeapon && source[36] > 8))
            {
                return false;
            }
            var buttons = (InputButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);
            var pressed = (InputButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[16..]);
            var aim = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(source[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[24..]),
                BinaryPrimitives.ReadSingleLittleEndian(source[28..]));
            var boostActivation = (BoostActivation)source[37];
            sbyte boostX = unchecked((sbyte)source[38]);
            sbyte boostY = unchecked((sbyte)source[39]);
            sbyte moveX = unchecked((sbyte)source[40]);
            sbyte moveY = unchecked((sbyte)source[41]);
            byte movementPresent = source[42];
            bool boostHeld = (buttons & InputButtons.Boost) != 0;
            // Bound before normalization. A zero, infinite or enormous ray
            // must never introduce NaNs into shared collision state.
            if (((buttons | pressed) & ~InputButtons.All) != 0
                || !Single.IsFinite(aim.X) || !Single.IsFinite(aim.Y) || !Single.IsFinite(aim.Z)
                || aim.LengthSquared < 0.5f || aim.LengthSquared > 1.5f
                || !BoostIntent.TryDecode(boostActivation, boostX, boostY,
                    out BoostIntent boostIntent)
                || boostIntent.Activation == BoostActivation.Charge && !boostHeld
                || boostIntent.Activation == BoostActivation.None && boostHeld)
            {
                return false;
            }
            if (movementPresent > 1
                || !AnalogMovementCodec.TryDecode(moveX, moveY, movementPresent != 0,
                    out _))
            {
                return false;
            }
            command = new InputCommand(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[8..]), buttons, pressed,
                aim, source[36], boostIntent, inputEpoch, moveX, moveY,
                movementPresent != 0);
            return true;
        }

        public InputCommand WithoutEdges()
        {
            bool boostHeld = (Buttons & InputButtons.Boost) != 0;
            return this with
            {
                Pressed = InputButtons.None,
                DesiredWeapon = NoWeapon,
                BoostActivation = boostHeld ? BoostActivation.Charge : BoostActivation.None,
                BoostDirectionX = 0,
                BoostDirectionY = 0
            };
        }

        public InputCommand Neutral() => WithoutEdges() with
        {
            Buttons = Buttons & InputButtons.Spectate,
            BoostActivation = BoostActivation.None,
            MoveX = 0,
            MoveY = 0,
            AnalogMovementPresent = false
        };
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
            uint inputEpoch = commands[0].InputEpoch;
            if (inputEpoch == 0)
            {
                throw new ArgumentException("Input epoch is required.", nameof(commands));
            }
            for (int i = 1; i < commands.Length; i++)
            {
                if (commands[i].InputEpoch != inputEpoch)
                {
                    throw new ArgumentException("An input bundle cannot mix life epochs.", nameof(commands));
                }
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
            Span<InputCommand> decoded = stackalloc InputCommand[Capacity];
            for (int i = 0; i < length; i++)
            {
                if (!InputCommand.TryRead(source.Slice(HeaderSize + i * InputCommand.Size,
                        InputCommand.Size), out decoded[i])
                    || (i > 0 && (decoded[i].Sequence != unchecked(decoded[i - 1].Sequence + 1)
                        || decoded[i].ClientTick != unchecked(decoded[i - 1].ClientTick + 1)
                        || decoded[i].InputEpoch != decoded[i - 1].InputEpoch)))
                {
                    return false;
                }
            }
            decoded[..length].CopyTo(commands);
            matchId = BinaryPrimitives.ReadUInt32LittleEndian(source);
            phaseRevision = BinaryPrimitives.ReadUInt32LittleEndian(source[5..]);
            count = length;
            return true;
        }
    }
}
