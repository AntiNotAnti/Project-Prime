using System.Diagnostics;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;
using MphRead.Mods.StudioRendering;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;

namespace ProjectPrime.Studio.Replay;

internal sealed record ReplayExportTicket(string Replay, string Hash, string Destination,
    uint Start, uint End, int Width, int Height, int Fps, bool GameHud, ReplayCameraMode Mode,
    int Slot, string Preset, bool Audio, bool Multiview, ReplayCameraMode ComparisonMode, int ComparisonSlot, bool Overlay, bool Director, ReplayVideoSegment[]? Segments, string[] ProtectedInputs, Dictionary<string,string> RuntimePaths, string MphKey, string FhKey, int ParentProcessId, long ParentStartTimeUtcTicks);
internal sealed record ReplayExportWorkerStatus(string State, long Frames, long TotalFrames, int EncodedFrames, string? Error);

internal static class ReplayExportWorker
{
    private static readonly SemaphoreSlim Worker = new(1,1);
    private static int _queued;
    private static ReplayExportTicket? _ticket;
    private static string? _directory;
    internal static async Task ExportAsync(string replay, string hash, string destination, uint start, uint end,
        int width, int height, int fps, bool gameHud, ReplayCameraMode mode, int slot, string preset,
        IReadOnlyList<ReplayCameraKeyframe> camera, bool audio, bool multiview, ReplayCameraMode comparisonMode, int comparisonSlot, bool overlay, bool director, ReplayVideoSegment[]? segments, string[] protectedInputs, CancellationToken cancellation)
    {
        if (Interlocked.Increment(ref _queued) > 4) { Interlocked.Decrement(ref _queued); throw new InvalidOperationException("Replay export queue is full (4 jobs)."); }
        string? directory = null; bool acquired = false;
        try
        {
            // Capture authored state and bytes before awaiting the worker gate.
            destination = Path.GetFullPath(destination);
            StudioReplayOutputGuard.ValidateVideo(destination,protectedInputs);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            directory = Path.Combine(Path.GetDirectoryName(destination)!, ".prime-replay-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string snapshot = Path.Combine(directory, "replay.ppdemo");
            await StudioReplaySnapshot.CopyVerifiedAsync(replay, snapshot, hash, cancellation).ConfigureAwait(false);
            var track = new ReplayCameraTrack();
            foreach (var key in camera) if (!track.Put(key)) throw new InvalidDataException(track.LastError);
            if (!track.Save(snapshot)) throw new IOException(track.LastError);
            var ticket = new ReplayExportTicket(snapshot, hash, destination, start, end, width, height, fps, gameHud, mode, slot, preset, audio, multiview, comparisonMode, comparisonSlot, overlay, director, segments, protectedInputs,
                Paths.AllPaths.ToDictionary(pair=>pair.Key,pair=>pair.Value), Paths.MphKey, Paths.FhKey, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);
            string ticketPath = Path.Combine(directory, "ticket.json");
            await File.WriteAllTextAsync(ticketPath, JsonSerializer.Serialize(ticket), cancellation).ConfigureAwait(false);
            await Worker.WaitAsync(cancellation).ConfigureAwait(false); acquired = true;
            var launch = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Studio executable unavailable."))
            { UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, RedirectStandardOutput=true };
            if (Path.GetFileNameWithoutExtension(launch.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) launch.ArgumentList.Add(typeof(Program).Assembly.Location);
            launch.ArgumentList.Add("--replay-export-worker"); launch.ArgumentList.Add(ticketPath);
            using var process = Process.Start(launch) ?? throw new IOException("Replay export worker did not start.");
            Task<string> stderr = ReadTailAsync(process.StandardError); Task<string> stdout = ReadTailAsync(process.StandardOutput);
            try { await StudioReplayWorkerLifetime.WaitAsync(process,directory,cancellation).ConfigureAwait(false); }
            finally { await stderr.ConfigureAwait(false); await stdout.ConfigureAwait(false); }
            string diagnostic = await stderr.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string statusPath = Path.Combine(directory,"status.json");
            ReplayExportWorkerStatus? status = File.Exists(statusPath) ? JsonSerializer.Deserialize<ReplayExportWorkerStatus>(await File.ReadAllTextAsync(statusPath).ConfigureAwait(false)) : null;
            if (process.ExitCode != 0 || status?.State != nameof(ReplayExportState.Completed))
                throw new IOException(status?.Error ?? "Replay export worker exited without completing: " + diagnostic);
        }
        finally
        {
            if(acquired)Worker.Release(); Interlocked.Decrement(ref _queued);
            if(directory != null && Directory.Exists(directory)) Directory.Delete(directory,true);
        }
    }
    private static async Task<string> ReadTailAsync(StreamReader reader)
    {
        var tail = new System.Text.StringBuilder(); char[] buffer=new char[2048]; int count;
        while((count=await reader.ReadAsync(buffer).ConfigureAwait(false))!=0) { tail.Append(buffer,0,count);if(tail.Length>16384)tail.Remove(0,tail.Length-16384); }
        return tail.ToString();
    }
    internal static int Run(string ticketPath)
    {
        try
        {
            ticketPath=Path.GetFullPath(ticketPath); _directory=Path.GetDirectoryName(ticketPath)!;
            using var input=File.OpenRead(ticketPath);
            if(input.Length>1024*1024)throw new InvalidDataException("Replay export ticket exceeds 1 MiB.");
            _ticket=JsonSerializer.Deserialize<ReplayExportTicket>(input) ?? throw new InvalidDataException("Replay export ticket is empty.");
            if(!Path.GetFileName(_directory).StartsWith(".prime-replay-export-",StringComparison.Ordinal)
                || Path.GetFullPath(_ticket.Replay)!=Path.Combine(_directory,"replay.ppdemo")
                || Path.GetDirectoryName(Path.GetFullPath(_ticket.Destination))!=Path.GetDirectoryName(_directory))
                throw new InvalidDataException("Replay export ticket does not own its scratch directory.");
            StudioReplayOutputGuard.ValidateVideo(_ticket.Destination,_ticket.ProtectedInputs.Append(_ticket.Replay));
            // Runs independently of Avalonia/GL, including a blocked graphics initialization.
            int parentLost=0;
            using var parentWatch = new System.Threading.Timer(_ =>
            {
                if (!StudioReplayWorkerLifetime.ParentAlive(_ticket.ParentProcessId,_ticket.ParentStartTimeUtcTicks)
                    && Interlocked.Exchange(ref parentLost,1)==0)
                {
                    // .NET refuses Kill(entireProcessTree:true) on its own process.
                    // Reap registered encoder/decoder trees first, then exit ourselves.
                    StudioReplayWorkerLifetime.AbandonChildren();
                    Environment.Exit(130);
                }
            },null,TimeSpan.Zero,TimeSpan.FromMilliseconds(250));
            using(var recording=File.OpenRead(_ticket.Replay))
                if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(recording)).Equals(_ticket.Hash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Replay export snapshot hash mismatch.");
            using var installation=MphRead.Mods.Update.DesktopUpdate.HoldInstallationUseLease();
            StudioReplayAudioRuntime.Configure();
            MphRead.AvaloniaShared.StudioGameAssets.InitializeAuthoringProcess();
            foreach(var path in _ticket.RuntimePaths)Paths.SetPath(path.Key,path.Value);
            Paths.MphKey=_ticket.MphKey;Paths.FhKey=_ticket.FhKey;
            MphRead.AvaloniaShared.StudioGameAssets.LoadPresentationSettings();
            StudioGraphicsState.Configure(false);
            // The worker owns its independent compatibility context on this thread.
            // Avalonia's macOS surface is core-only; canonical Game rendering requires GLSL 1.20.
            var settings=MphRead.Mods.Render.DesktopGlContext.Settings(background:true);
            settings.ClientSize=new Vector2i(64,64);
            settings.Title="Project Prime replay export";
            using var window=new NativeWindow(settings);
            using var graphics=new MphRead.Mods.Render.DesktopGraphicsSession(window);
            using var registration=StudioGraphicsState.RegisterSurface("OpenGL",GL.GetString(StringName.Renderer));
            var ticket=_ticket;
            var track=new ReplayCameraTrack();if(!track.Load(ticket.Replay))throw new InvalidDataException(track.LastError);
            var manifest=new ReplayVideoExportManifest(3,ticket.Replay,ticket.Start,ticket.End,ticket.Width,ticket.Height,ticket.Fps,!ticket.GameHud,ticket.Director,true,"","","",Segments:ticket.Segments,GameHud:ticket.GameHud,ReplayOverlay:ticket.Overlay);
            using var export=ReplayVideoExporter.CreateDetached(manifest,_directory!,ticket.Destination,track,ticket.Mode,ticket.Slot,preset:ticket.Preset,audio:ticket.Audio,multiview:ticket.Multiview,comparisonMode:ticket.ComparisonMode,comparisonSlot:ticket.ComparisonSlot,protectedInputs:ticket.ProtectedInputs);
            using var stop=new CancellationTokenSource();
            File.WriteAllText(Path.Combine(_directory,"heartbeat"),"ready");
            var heartbeat=Stopwatch.StartNew();string? lastStatus=null;
            while(!export.Terminal)
            {
                if(File.Exists(Path.Combine(_directory,"cancel")))stop.Cancel();
                export.Advance(stop.Token);
                if(heartbeat.Elapsed>=TimeSpan.FromSeconds(1))
                {File.WriteAllText(Path.Combine(_directory,"heartbeat"),"advance");heartbeat.Restart();}
                var status=new ReplayExportWorkerStatus(export.State.ToString(),export.Frames,export.TotalFrames,export.EncodedFrames,export.Error);
                string encoded=JsonSerializer.Serialize(status);
                if(encoded!=lastStatus||export.Terminal){WriteStatus(status);lastStatus=encoded;}
                if(export.State==ReplayExportState.Encoding)Thread.Sleep(5);
            }
            return export.State==ReplayExportState.Completed?0:export.State==ReplayExportState.Cancelled?130:1;
        }
        catch(Exception ex)
        { Console.Error.WriteLine(ex.Message);WriteStatus(new("Failed",0,0,0,ex.Message));return 1; }
    }
    private static void WriteStatus(ReplayExportWorkerStatus status)
    {
        if(_directory == null)return;
        ReplayAnnotations.WriteAtomic(Path.Combine(_directory,"status.json"),JsonSerializer.Serialize(status));
        Console.Error.WriteLine($"[replay-export] {status.State} {status.Frames}/{status.TotalFrames} encoded={status.EncodedFrames} {status.Error}");
    }
}
