using System;
using System.Diagnostics;

internal enum NativeInputKind { KeyDown, KeyUp, Text, MouseDown, MouseUp, MouseWheel, MouseMove }
internal readonly record struct NativeInputObservation(long Timestamp, NativeInputKind Kind, int Code, double X, double Y);

// Value-only bounded provenance. Events are ignored before they can enter the
// real RenderWindow handlers; recording does no formatting or I/O in callbacks.
internal sealed class NativeInputIsolation
{
    private readonly NativeInputObservation[] _events = new NativeInputObservation[128];
    private readonly long[] _counts = new long[7];
    private int _count;
    private long _dropped;
    private NativeInputObservation? _lastKeyDown;
    internal void Observe(NativeInputKind kind, int code = 0, double x = 0, double y = 0)
    {
        _counts[(int)kind]++;
        // Pointer motion can flood callbacks. Its aggregate never consumes
        // discrete key/button provenance, and every motion is still ignored.
        if (kind == NativeInputKind.MouseMove) return;
        var observation = new NativeInputObservation(Stopwatch.GetTimestamp(), kind, code, x, y);
        if (kind == NativeInputKind.KeyDown) _lastKeyDown = observation;
        if (_count == _events.Length) { _dropped++; return; }
        _events[_count++] = observation;
    }
    internal object Receipt() => new
    {
        policy = "Native OS keyboard/text/mouse callbacks ignored; Scene snapshots remain frozen; window close/focus callbacks remain active",
        timestampFrequency = Stopwatch.Frequency,
        kindCodes = new { keyDown = 0, keyUp = 1, text = 2, mouseDown = 3, mouseUp = 4, mouseWheel = 5, mouseMove = 6 },
        keyDown = _counts[0], keyUp = _counts[1], text = _counts[2], mouseDown = _counts[3], mouseUp = _counts[4], mouseWheel = _counts[5], mouseMove = _counts[6],
        recordedDiscreteEvents = _count, droppedDiscreteObservations = _dropped,
        lastKeyDown = _lastKeyDown, events = _events.AsSpan(0, _count).ToArray(),
        physicalGamepad = "Production polling unchanged; physical controller isolation is not claimed"
    };
}
