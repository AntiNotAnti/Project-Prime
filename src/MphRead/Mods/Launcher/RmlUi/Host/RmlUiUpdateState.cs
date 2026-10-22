using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

[System.Flags]
public enum RmlUiUpdateFlags : uint { None = 0, Dirty = 1, DrawListValid = 2 }

/// <summary>Native owner-thread state after all direct input/document mutations.
/// The delay is the remaining time until RmlUi requires update/render work.
/// A valid draw list belongs to this generation and visual revision.</summary>
public readonly record struct RmlUiUpdateState(ulong Generation, ulong VisualRevision,
    double NextUpdateDelaySeconds, RmlUiUpdateFlags Flags)
{
    public bool Dirty => (Flags & RmlUiUpdateFlags.Dirty) != 0;
    public bool DrawListValid => (Flags & RmlUiUpdateFlags.DrawListValid) != 0;
}
public readonly record struct RmlUiUpdateMetrics(long NativeUpdates, long SkippedUpdates, long StateQueries);

[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiNativeUpdateState
{
    public uint Size, Version;
    public ulong Generation, VisualRevision;
    public double NextUpdateDelaySeconds;
    public RmlUiUpdateFlags Flags;
    public uint Reserved;
    public static RmlUiNativeUpdateState Request() => new() { Size = 40, Version = 1 };
}
