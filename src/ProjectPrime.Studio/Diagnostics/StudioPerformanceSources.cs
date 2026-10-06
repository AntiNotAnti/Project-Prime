using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Adapters read published counters. No scene traversal, disk scan, or device creation occurs here.</summary>
public static class StudioPerformanceSources
{
    public static StudioPerformanceSourceSnapshot Graphics()
        => new("Graphics lifetime", Graphics: new(StudioGraphicsHost.HasDevice,
            StudioGraphicsHost.ResidentWorldCount, StudioGraphicsHost.NativeSurfaceCount,
            StudioGraphicsHost.ViewportSurfaceCount));

    public static StudioPerformanceSourceSnapshot? Document(IStudioDocument? document)
    {
        if (document is MapStudioDocument map)
        {
            var metrics = StudioGraphicsHost.LastMetrics;
            var scheduler = map.BuildScheduler;
            var render = metrics == null ? UnknownRender() : new StudioRenderPerformance(
                metrics.CpuMilliseconds, metrics.GpuMilliseconds, metrics.DrawCalls, metrics.BatchCount,
                metrics.VisiblePrimitives, metrics.GeometryUploadBytes, metrics.GeometryBytes,
                metrics.ResidentMeshes, metrics.ResidentTextures, metrics.TextureBytes,
                metrics.ReadbackBytes, metrics.PickReadbackBytes, metrics.DeviceGeneration);
            return new("Map · " + map.Title, Render: render,
                Cache: new(scheduler.CompiledCacheCount, scheduler.CompiledCacheBytes,
                    scheduler.PendingCount, scheduler.CompilationCount,
                    scheduler.PreparationPeak, scheduler.PreparationMilliseconds),
                Detail: "Frame counters describe the latest map viewport. GPU timestamp timing is unavailable.");
        }
        if (document is ReplayStudioDocument { Session: { } session } replay)
        {
            var status = session.Player.Status;
            var performance = session.Player.Performance;
            return new("Replay · " + replay.Title,
                Render: UnknownRender() with { CpuMilliseconds = StudioGraphicsHost.LastReplayCpuMilliseconds },
                Replay: new(performance.SeekMilliseconds, performance.AdvanceMilliseconds,
                    performance.RenderMilliseconds, performance.CheckpointCaptureMilliseconds,
                    performance.SeekMilliseconds.HasValue ? performance.SeekRestoreFrame : null,
                    performance.SeekMilliseconds.HasValue ? performance.SeekSimulationSteps : null,
                    status.CheckpointCount, status.CheckpointBytes,
                    status.Ready ? performance.RejectedCheckpoints : null, performance.CheckpointSource),
                Detail: "Replay GPU counters and checkpoint capture timing are unavailable until measured.");
        }
        return new("Viewport", Render: UnknownRender(), Detail: "Open a map or replay to collect viewport measurements.");
    }

    private static StudioRenderPerformance UnknownRender()
        => new(null, null, null, null, null, null, null, null, null, null, null, null, null);
}
