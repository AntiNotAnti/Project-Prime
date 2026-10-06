#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using SkiaSharp;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    private readonly List<ExportJob> _exports = new();
    /// <summary>The desktop host may detach native export ownership into its own worker process.</summary>
    public Func<string, Task>? ExportWorkerLauncher { get; set; }
    public Guid QueueExport(StudioReplayExportRequest request)
    {
        RequireOwner();
        if (!new[] { 24, 30, 48, 60, 90, 120, 144 }.Contains(request.Fps)) throw new ArgumentOutOfRangeException(nameof(request.Fps));
        if (request.Width <= 0 || request.Height <= 0 || request.Width > 7680 || request.Height > 4320) throw new ArgumentOutOfRangeException(nameof(request.Width));
        if (request.StartFrame > request.EndFrame || request.EndFrame > Status.DurationFrames) throw new ArgumentOutOfRangeException(nameof(request.EndFrame));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_exports.Count(j => !j.Terminal) >= 32) throw new InvalidOperationException("Replay export queue is full.");
        var job = new ExportJob(request with { Directory = Path.GetFullPath(request.Directory) });
        if (ExportWorkerLauncher is { } launch)
        {
            if (_playbackPath == null) throw new InvalidOperationException("Prepare the replay before starting its detached export.");
            string directory = Path.Combine(_cacheRoot, "exports", job.Id.ToString("N"));
            Directory.CreateDirectory(directory);
            job.Detached = true; job.StatusFile = Path.Combine(directory, "status.json"); job.CancelFile = Path.Combine(directory, "cancel");
            var ticket = new StudioReplayExportTicket(job.Id, _playbackPath, Path.Combine(directory, "cache"),
                job.Request with { Directory = Path.Combine(job.Request.Directory, job.Id.ToString("N")) },
                job.StatusFile, job.CancelFile, Paths.AllPaths.ToDictionary(p => p.Key, p => p.Value),
                CameraKeys, new[] { Path.Combine(_cacheRoot, "packages") }, Paths.MphKey, Paths.FhKey,
                _player?.Current.Session.Metadata?.CustomMapIdentity?.PackageHash.ToString());
            job.PublishedDirectory = ticket.Request.Directory;
            string path = Path.Combine(directory, "ticket.json");
            File.WriteAllText(path, JsonSerializer.Serialize(ticket, new JsonSerializerOptions { IncludeFields = true }));
            job.DetachedTask = launch(path); job.State = "Worker starting";
        }
        _exports.Add(job); Changed?.Invoke(); return job.Id;
    }
    public void CancelExport(Guid id) { _exports.FirstOrDefault(j => j.Id == id)?.CancelExplicit(); Changed?.Invoke(); }
    private void AdvanceExports()
    {
        ExportJob? job = _exports.FirstOrDefault(j => !j.Terminal);
        if (job == null || _playbackPath == null) return;
        if (job.Detached) { job.RefreshDetached(); return; }
        try
        {
            if (job.Cancelled)
            {
                job.ReleasePlayer(); job.Encoder?.Cancel(); job.State = "Cancelling";
                if (job.Write?.IsCompleted == false || job.AudioWrite?.IsCompleted == false || job.Encoder?.Completion.IsCompleted == false) return;
                job.State = "Cancelled"; return;
            }
            if (job.Preparation == null && job.Player == null && job.Encoder == null
                && (job.Frames < job.Sampler.Count || job.Request.Audio is { Enabled: true } && job.AudioWrite == null))
            { Directory.CreateDirectory(job.Request.Directory); job.Preparation = ReplayPreparationJob.File(_playbackPath); job.State = "Preparing"; }
            if (job.Preparation is { Completed: true } preparation)
            {
                using var source = preparation.TakeCompleted();
                job.Player = new(source, new(job.Request.Width, job.Request.Height), new(EnableAsyncPreparation: true));
                job.Player.Transport.Pause(); job.Player.Seek(job.Sampler.At(Math.Min(job.Frames, job.Sampler.Count - 1)).SimulationFrame);
                preparation.Dispose(); job.Preparation = null; job.State = "Rendering";
            }
            if (job.Player is { } player && job.Frames < job.Sampler.Count)
            {
                var sample = job.Sampler.At(job.Frames);
                if (player.Transport.CurrentFrame != sample.SimulationFrame && !player.Transport.IsSeeking) player.Seek(sample.SimulationFrame);
                if (!player.Ready || player.Transport.CurrentFrame != sample.SimulationFrame) { player.Update(120, 3); return; }
                if (job.Write is { IsCompleted: false }) return;
                job.Write?.GetAwaiter().GetResult();
                var view = job.Request.View ?? new(job.Request.Width, job.Request.Height, job.Request.Camera);
                RenderPlayer(player, view with { Width = job.Request.Width, Height = job.Request.Height,
                    GameHud = job.Request.GameHud, ReplayOverlay = job.Request.ReplayOverlay,
                    PresentationFrame = sample.Frame, PresentationAlpha = sample.Alpha }, 0);
                var capture = CaptureScene(player.Current.Scene);
                string file = Path.Combine(job.Request.Directory, $"frame_{job.Frames:D08}.png");
                job.Write = Task.Run(() => SavePng(file, capture)); job.Frames++;
            }
            if (job.Frames == job.Sampler.Count && job.Write?.IsCompleted != false && job.Encoder == null && job.State != "Complete")
            {
                job.Write?.GetAwaiter().GetResult();
                if (job.Request.Audio is { Enabled: true })
                {
                    if (job.AudioWrite == null)
                    {
                        var events = job.Player!.Current.Session.Events.ToArray();
                        var players = job.Player.Current.Session.Metadata?.Players.ToArray() ?? Array.Empty<ReplayPlayerInfo>();
                        job.AudioWrite = Task.Run(() => StudioReplayAudioTimeline.Write(Path.Combine(job.Request.Directory, "offline.wav"),
                            job.Request, job.Sampler.Count, events, players, job.Stop.Token));
                        job.State = "Mixing audio";
                    }
                    if (!job.AudioWrite.IsCompleted) return;
                    job.AudioWrite.GetAwaiter().GetResult();
                }
                job.ReleasePlayer();
                if (string.IsNullOrWhiteSpace(job.Request.Encoder)) { job.State = "Complete"; return; }
                string output = Path.GetFileName(job.Request.OutputName);
                string audio = job.Request.Audio is { Enabled: true } ? "-i offline.wav -c:a aac -shortest " : "";
                string arguments = $"-y -framerate {job.Request.Fps} -i frame_%08d.png {audio}-c:v libx264 -pix_fmt yuv420p -progress pipe:1 {Quote(output)}";
                job.Encoder = new(job.Request.Encoder!, arguments, job.Request.Directory); job.State = "Encoding";
            }
            if (job.Encoder?.Completion is { IsCompleted: true } completion)
            { var result = completion.GetAwaiter().GetResult(); job.Error = result.Error; job.State = result.Cancelled ? "Cancelled" : result.ExitCode == 0 ? "Complete" : "Failed"; job.Encoder.Dispose(); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { job.Error = ex is OperationCanceledException ? null : ex.Message; job.State = job.Cancelled ? "Cancelled" : "Failed"; job.ReleasePlayer(); job.Encoder?.Dispose(); }
    }
    public static void SavePng(string path, StudioReplayCapture capture)
    {
        using var bitmap = new SKBitmap(capture.Width, capture.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        System.Runtime.InteropServices.Marshal.Copy(capture.Rgba, 0, bitmap.GetPixels(), capture.Rgba.Length);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path); png.SaveTo(stream);
    }
    private sealed class ExportJob(StudioReplayExportRequest request)
    {
        internal Guid Id { get; } = Guid.NewGuid(); internal StudioReplayExportRequest Request { get; } = request;
        internal ReplayExportSampler Sampler { get; } = new(request.StartFrame, request.EndFrame, request.Fps);
        internal string State = "Queued"; internal string? Error; internal long Frames; internal bool Cancelled;
        internal PassiveReplayPlayer? Player; internal ReplayPreparationJob? Preparation; internal ReplayEncoderJob? Encoder; internal Task? Write;
        internal Task? AudioWrite; internal CancellationTokenSource Stop { get; } = new();
        internal bool Detached; internal string? StatusFile, CancelFile, PublishedDirectory; internal Task? DetachedTask;
        internal bool Terminal => State is "Complete" or "Failed" or "Cancelled";
        internal StudioReplayExportStatus Status { get { RefreshDetached(); return new(Id, State, Frames, Sampler.Count, Error, PublishedDirectory ?? Request.Directory); } }
        internal void RefreshDetached()
        {
            if (!Detached) return;
            try
            {
                if (StatusFile != null && File.Exists(StatusFile) && new FileInfo(StatusFile).Length <= 65536)
                {
                    var status = JsonSerializer.Deserialize<StudioReplayExportStatus>(File.ReadAllText(StatusFile));
                    if (status != null && status.Id == Id) { State = status.State; Frames = status.Frames; Error = status.Error; PublishedDirectory = status.Directory; }
                }
                if (DetachedTask?.IsFaulted == true) { State = "Failed"; Error = DetachedTask.Exception?.GetBaseException().Message; }
                else if (DetachedTask?.IsCompleted == true && !Terminal) { State = "Failed"; Error = "Export worker exited without a terminal result."; }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        internal void CancelExplicit()
        {
            if (Detached) { if (!Terminal && CancelFile != null) File.WriteAllText(CancelFile, "cancel"); return; }
            Cancel();
        }
        internal void Cancel() { if (Detached) return; Cancelled = true; Stop.Cancel(); Preparation?.Dispose(); Encoder?.Cancel(); }
        internal void ReleasePlayer() { Player?.Dispose(); Player = null; Preparation?.Dispose(); Preparation = null; }
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

#endif
