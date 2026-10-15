using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Only reports observed state. Missing GPU measurements remain unavailable.</summary>
public static class StudioPerformanceSources
{
    public static StudioPerformanceSourceSnapshot Graphics()
        => new("Graphics lifetime", Graphics: new(StudioGraphicsHost.HasDevice,
            StudioGraphicsHost.ResidentWorldCount, StudioGraphicsHost.NativeSurfaceCount,
            StudioGraphicsHost.ViewportSurfaceCount));

    public static StudioPerformanceSourceSnapshot? Document(IStudioDocument? document)
    {
        string detail = document is ReplayStudioDocument replay
            ? $"Replay frame {replay.CurrentFrame} / {replay.DurationFrames}."
            : document is null ? "Open a map or replay to inspect document state."
            : $"{document.Kind} document is {document.State}; unsaved changes: {document.Dirty}.";
        return new(document?.Title ?? "No active document", Detail: detail);
    }
}
