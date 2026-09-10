using System;

namespace MphRead.Mods.Input
{
    public enum PointerToolKind
    {
        Finger,
        Stylus,
        Eraser,
        Mouse,
        Unknown
    }

    [Flags]
    public enum StylusButtons
    {
        None = 0,
        Primary = 1 << 0,
        Secondary = 1 << 1
    }

    /// <summary>Platform-neutral pointer data; Android types stop at its view boundary.</summary>
    public readonly record struct PointerSample(int Id, PointerToolKind Tool,
        float X, float Y, float Pressure, StylusButtons Buttons, long Timestamp);
}
