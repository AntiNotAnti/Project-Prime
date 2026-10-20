using System;

namespace MphRead.Mods.Input;

public enum TouchStylusResponse { Direct, Classic }

/// <summary>
/// Timing response for the existing accumulated biped touch delta. Direct
/// preserves Project's next-step sampling, including a flick released between
/// steps. Classic uses the DS ten-step hold/eight-step window at fixed 60 Hz,
/// with the same eventual degrees per pixel and existing sensitivity units.
/// Timing reference: SecondHunt 1578a87 (MIT, repository LICENSE retained).
/// This owns no pointer, aim basis, movement gesture or simulation clock.
/// </summary>
public sealed class StylusAimResponse
{
    private const int Window = 8, Hold = 10;
    private readonly float[] _x = new float[Window], _y = new float[Window];
    private int _next, _held;

    public (float X, float Y) Sample(TouchStylusResponse response, bool down, float x, float y)
    {
        if (!Single.IsFinite(x) || !Single.IsFinite(y)) { Reset(); return (0, 0); }
        (x, y) = BoundDelta(x, y);
        if (response != TouchStylusResponse.Classic) { Reset(); return (x, y); }
        if (!down) { Reset(); return (0, 0); }
        _held = Math.Min(_held + 1, Hold + 1);
        if (_held == 1) x = y = 0;
        _x[_next] = x;
        _y[_next] = y;
        _next = (_next + 1) % Window;
        if (_held <= Hold) return (0, 0);
        float sumX = 0, sumY = 0;
        for (int i = 0; i < Window; i++) { sumX += _x[i]; sumY += _y[i]; }
        return (sumX / Window, sumY / Window);
    }

    public static (float X, float Y) BoundDelta(float x, float y) =>
        (!Single.IsFinite(x) || !Single.IsFinite(y)) ? (0, 0)
        : (Math.Clamp(x, -1_000_000, 1_000_000), Math.Clamp(y, -1_000_000, 1_000_000));

    public void Reset()
    {
        Array.Clear(_x); Array.Clear(_y);
        _next = _held = 0;
    }
}
