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

    /// <summary>
    /// Coordinate semantics supplied by a native pen adapter. Unknown keeps
    /// the legacy density-only behavior when a platform cannot describe its
    /// mapped coordinate space.
    /// </summary>
    public enum PointerCoordinateKind
    {
        Unknown,
        Direct,
        Indirect
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
        float X, float Y, float Pressure, StylusButtons Buttons, long Timestamp,
        PointerCoordinateKind CoordinateKind = PointerCoordinateKind.Unknown,
        float LogicalDisplayScale = 1,
        float MappedExtentX = 0,
        float MappedExtentY = 0);
}
