using System;
using System.IO;

namespace MphRead.Mods.Fidelity;

/// <summary>Read-only diagnostic conversion of an existing canonical counter.
/// The caller supplies observations; this owner cannot access or alter a scene.
/// A rewind, skipped frame or saturated counter invalidates the capture.</summary>
internal sealed class FidelityEventDelta
{
    private int _tick;
    private int _counter;

    internal FidelityEventDelta(int initialCounter)
    {
        ValidateCounter(initialCounter);
        _counter = initialCounter;
    }

    internal long Sample(int tick, int counterBefore, int counterAfter)
    {
        ValidateCounter(counterBefore);
        ValidateCounter(counterAfter);
        if (tick != (long)_tick + 1 || counterBefore != _counter || counterAfter < counterBefore)
            throw new InvalidDataException("Event capture requires contiguous frames and a monotonic canonical counter.");
        long delta = (long)counterAfter - counterBefore;
        _tick = tick;
        _counter = counterAfter;
        return delta;
    }

    private static void ValidateCounter(int counter)
    {
        // The production int counter saturates at MaxValue. Its last increment
        // and subsequent missing increments cannot establish cadence accuracy.
        if (counter < 0 || counter == Int32.MaxValue)
            throw new InvalidDataException("Event capture counter is negative or saturated.");
    }
}
