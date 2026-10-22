using System;
using System.Buffers.Binary;

namespace MphRead.Mods.EnhancedHunters;

/// <summary>Twenty-six bytes: also fences slot generation, which may reuse a life ID.</summary>
public struct EnhancedHunterNetState
{
    public const int Size = 10 + 2 * EnhancedMovementEvent.Size;
    public EnhancedMovementEvent Impulse0, Impulse1;
    public byte Flags, TargetSlot, ValueA, ValueB, TimerA, TimerB;
    public ushort TargetLifeId, TargetGeneration;
    public void Write(Span<byte> destination)
    {
        Impulse0.Write(destination[10..]); Impulse1.Write(destination[18..]);
        destination[0] = Flags; destination[1] = TargetSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], TargetLifeId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], TargetGeneration);
        destination[6] = ValueA; destination[7] = ValueB;
        destination[8] = TimerA; destination[9] = TimerB;
    }
    public static EnhancedHunterNetState Read(ReadOnlySpan<byte> source) => new()
    {
        Impulse0 = EnhancedMovementEvent.Read(source[10..]), Impulse1 = EnhancedMovementEvent.Read(source[18..]),
        Flags = source[0], TargetSlot = source[1],
        TargetLifeId = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
        TargetGeneration = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
        ValueA = source[6], ValueB = source[7], TimerA = source[8], TimerB = source[9]
    };
    internal static EnhancedHunterNetState Capture(EnhancedHunterState state) => new()
    {
        Impulse0 = state.Impulse0, Impulse1 = state.Impulse1,
        Flags = state.Flags, TargetSlot = state.TargetSlot,
        TargetLifeId = state.TargetLifeId, TargetGeneration = state.TargetGeneration,
        ValueA = state.ValueA, ValueB = state.ValueB,
        TimerA = (byte)Math.Clamp((state.TimerA + 3) / 4, 0, 255),
        TimerB = (byte)Math.Clamp((Math.Max(state.TimerB, state.GhostFrames) + 3) / 4, 0, 255)
    };
    internal void Apply(EnhancedHunterState state)
    {
        state.Impulse0 = Impulse0; state.Impulse1 = Impulse1;
        state.Flags = Flags; state.TargetSlot = TargetSlot; state.TargetLifeId = TargetLifeId;
        state.TargetGeneration = TargetGeneration; state.ValueA = ValueA; state.ValueB = ValueB;
        state.TimerA = TimerA * 4; state.TimerB = TimerB * 4;
        if (state.Hunter == Hunter.Trace) state.GhostFrames = TimerB * 4;
    }
}
