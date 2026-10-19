#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Replay;

/// <summary>A private process owns playback, camera, graphics and output independently of an editor document.</summary>
internal static class ReplayDetachedExportWorker
{
    internal static int Run(string path)
    {
        ReplayDetachedExportTicket? ticket = null;
        TextWriter originalOut = Console.Out, originalError = Console.Error;
        StreamWriter? log = null;
        try
        {
            using var installation = OperatingSystem.IsWindows() ? Update.DesktopInstallationUse.Acquire(AppContext.BaseDirectory) : null;
            if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("The export ticket path must be absolute.");
            ticket = ReplayDetachedExportTicket.Read(path);
            log = new StreamWriter(new FileStream(ticket.LogPath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            Console.SetOut(TextWriter.Synchronized(log)); Console.SetError(Console.Out);
            Directory.SetCurrentDirectory(ticket.WorkDirectory);
            GameFiles.Root = ticket.WorkDirectory; LauncherPrefs.Directory = ticket.WorkDirectory;
            Paths.UpdatePaths(); Paths.MphKey = ticket.MphKey; Paths.FhKey = ticket.FhKey;
            CustomRooms.MapDirectory = ticket.MapDirectory; CustomRooms.UserMapDirectory = ticket.UserMapDirectory;
            GraphicsBackendPolicy.Configure("opengl");
            return Render(ticket);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine(error);
            if (ticket != null)
                try { Publish(ticket, ReplayExportState.Failed, error.Message, error.Message); }
                catch (Exception statusError) when (statusError is IOException or UnauthorizedAccessException)
                { Console.Error.WriteLine("Export status: " + statusError.Message); }
            return 1;
        }
        finally
        {
            Console.SetOut(originalOut); Console.SetError(originalError); log?.Dispose();
        }
    }

    private static int Render(ReplayDetachedExportTicket ticket)
    {
        var settings = DesktopGlContext.Settings(background: true);
        settings.ClientSize = new Vector2i(640, 480);
        using var window = new NativeWindow(settings);
        using var graphics = new DesktopGraphicsSession(window);
        var shell = new Scene(settings.ClientSize, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, initializeRuntime: false);
        var clock = Stopwatch.StartNew();
        TimeSpan lastStatus = TimeSpan.FromSeconds(-1);
        try
        {
            Publish(ticket, ReplayExportState.Queued, "Preparing private replay");
            if (File.Exists(ticket.CancelPath)) return Cancel(ticket);
            if (!DemoPlayback.Join(ticket.Manifest.Replay)) throw new InvalidDataException(DemoPlayback.LastError ?? "Replay could not be opened.");
            while (DemoPlayback.PresentationScene == null)
            {
                if (File.Exists(ticket.CancelPath)) return Cancel(ticket);
                if (clock.Elapsed > TimeSpan.FromMinutes(2)) throw new TimeoutException("Private replay preparation timed out.");
                shell.OnSimulationFrame();
                if (!DemoPlayback.IsActive || ReplayController.State == ReplayState.Error)
                    throw new InvalidDataException(DemoPlayback.LastError ?? "Private replay preparation failed.");
                Thread.Sleep(1);
            }
            if (DemoPlayback.Metadata is not { } metadata || ReplayMapIdentity.Compute(metadata.RoomKey) != ticket.GeometryHash)
                throw new InvalidDataException("The worker's actual map geometry differs from the reviewed export.");
            SpectatorMode.Watch(ticket.Subject);
            // Bind the private replay before installing the accepted camera snapshot.
            // Otherwise the first draw loads its absent sidecar and clears those keys.
            ReplayCamera.EnsureTrack();
            ticket.Presentation.Apply();
            if (!ReplayVideoExporter.Start(ticket.Manifest)) throw new InvalidDataException(ReplayVideoExporter.Status);
            while (ReplayVideoExporter.Active)
            {
                if (File.Exists(ticket.CancelPath)) ReplayVideoExporter.Cancel();
                if (clock.Elapsed > TimeSpan.FromHours(6)) throw new TimeoutException("Export exceeded its six-hour deadline.");
                if (clock.Elapsed - lastStatus >= TimeSpan.FromMilliseconds(250))
                {
                    Publish(ticket, ReplayVideoExporter.State, ReplayVideoExporter.Status);
                    lastStatus = clock.Elapsed;
                }
                if (!ReplayVideoExporter.Active) break;
                if (!ReplayVideoExporter.Rendering) { Thread.Sleep(5); continue; }
                shell.OnSimulationFrame();
                if (ReplayController.IsSeeking) { Thread.Sleep(1); continue; }
                if (DemoPlayback.PresentationScene is not { } scene)
                    throw new InvalidDataException(DemoPlayback.LastError ?? "Private playback lost its presentation.");
                if(scene.Players.MainPlayerIndex!=ticket.Subject)SpectatorMode.Watch(ticket.Subject);
                shell.OnDrawFrame(); shell.OnRenderFrame();
                ReplayVideoExporter.AfterSceneDraw(scene);
            }
            var state = ReplayVideoExporter.State;
            if (state is not (ReplayExportState.Completed or ReplayExportState.Cancelled or ReplayExportState.Failed))
                throw new InvalidDataException("Export stopped without a terminal result.");
            Publish(ticket, state, ReplayVideoExporter.Status, ReplayVideoExporter.LastError);
            return state == ReplayExportState.Failed ? 1 : 0;
        }
        finally
        {
            try { ReplayVideoExporter.Cancel(); }
            finally
            {
                try { DemoPlayback.Stop(); }
                finally { try { shell.DoCleanup(); } finally { shell.UnloadGl(); } }
            }
        }
    }

    private static int Cancel(ReplayDetachedExportTicket ticket)
    {
        ReplayVideoExporter.Cancel();
        Publish(ticket, ReplayExportState.Cancelled, "Export cancelled");
        return 0;
    }

    private static void Publish(ReplayDetachedExportTicket ticket, ReplayExportState state, string message, string? error = null)
        => ReplayDetachedExportFiles.WriteStatus(ticket.StatusPath, new ReplayDetachedExportStatus(ticket.Id,
            Environment.ProcessId, state, message, ReplayVideoExporter.Progress, ReplayVideoExporter.FramesWritten,
            ReplayVideoExporter.LastOutput, error, DateTime.UtcNow));
}
#endif
