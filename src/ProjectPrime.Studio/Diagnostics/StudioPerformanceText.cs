using System.Globalization;
using System.Text;

namespace ProjectPrime.Studio.Diagnostics;

public static class StudioPerformanceText
{
    public static string Format(StudioPerformanceSnapshot snapshot)
    {
        StringBuilder text = new();
        text.AppendLine($"Process memory  {Bytes(snapshot.ProcessWorkingSetBytes)}");
        text.AppendLine($"Managed memory  {Bytes(snapshot.ManagedBytes)}");
        text.AppendLine($"Jobs running  {snapshot.RunningJobs}  ·  tracked {snapshot.Jobs.Count}");
        foreach (var source in snapshot.Sources)
        {
            text.AppendLine();
            text.AppendLine(source.Name);
            if (source.Render is { } render)
            {
                text.AppendLine($"{(source.Replay == null ? "Renderer CPU submission" : "Replay render CPU")}  {Milliseconds(render.CpuMilliseconds)}");
                text.AppendLine($"GPU frame  {Milliseconds(render.GpuMilliseconds)}");
                text.AppendLine($"Draws / batches  {Count(render.DrawCalls)} / {Count(render.BatchCount)}");
                text.AppendLine($"Submitted primitives  {Count(render.VisiblePrimitives)}");
                text.AppendLine($"Geometry uploaded  {Bytes(render.GeometryUploadBytes)}");
                text.AppendLine($"Geometry resident  {Bytes(render.GeometryResidentBytes)} · meshes {Count(render.ResidentMeshes)}");
                text.AppendLine($"Textures resident  {Count(render.ResidentTextures)} · {Bytes(render.TextureBytes)}");
                text.AppendLine($"Frame readback  {Bytes(render.FrameReadbackBytes)}");
                text.AppendLine($"Pick readback total  {Bytes(render.PickReadbackBytes)}");
                text.AppendLine($"Device generation  {Count(render.DeviceGeneration)}");
            }
            if (source.Replay is { } replay)
            {
                text.AppendLine($"Last seek  {Milliseconds(replay.SeekMilliseconds)}");
                text.AppendLine($"Restore frame / steps  {Count(replay.SeekRestoreFrame)} / {Count(replay.SeekSimulationSteps)}");
                text.AppendLine($"Advance / render CPU  {Milliseconds(replay.AdvanceMilliseconds)} / {Milliseconds(replay.RenderMilliseconds)}");
                text.AppendLine($"Checkpoint capture  {Milliseconds(replay.CheckpointCaptureMilliseconds)}");
                text.AppendLine($"Checkpoints  {replay.CheckpointCount:N0} · {Bytes(replay.CheckpointBytes)}");
                text.AppendLine($"Rejected checkpoints  {Count(replay.RejectedCheckpoints)}");
                text.AppendLine($"Checkpoint source  {replay.CheckpointSource ?? "unavailable"}");
            }
            if (source.Cache is { } cache)
            {
                text.AppendLine($"Compilation cache  {cache.Count:N0} · {Bytes(cache.Bytes)}");
                text.AppendLine($"Builds pending  {cache.PendingJobs:N0} · compilations {cache.CompilationCount:N0}");
                text.AppendLine($"Preparation peak  {cache.PreparationPeak:N0} · total {Milliseconds(cache.PreparationMilliseconds)}");
            }
            if (source.Graphics is { } graphics)
                text.AppendLine($"Graphics resources  device {(graphics.DeviceCreated ? "created" : "idle")} · worlds {graphics.Worlds} · native {graphics.NativeSurfaces} · targets {graphics.ViewportSurfaces}");
            if (source.Detail != null) text.AppendLine(source.Detail);
        }
        return text.ToString().TrimEnd();
    }

    public static string Bytes(long? bytes) => bytes is { } value
        ? value >= 1024 * 1024 ? $"{value / (1024d * 1024):0.##} MiB"
            : value >= 1024 ? $"{value / 1024d:0.##} KiB" : $"{value} B"
        : "unavailable";
    public static string Milliseconds(double? milliseconds) => milliseconds is { } value && double.IsFinite(value)
        ? value.ToString("0.###", CultureInfo.InvariantCulture) + " ms" : "unavailable";
    private static string Count<T>(T? value) where T : struct, IFormattable
        => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "unavailable";
}
