using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Rendering;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ProjectPrime.Studio.Replay;

/// <summary>One persisted export owns a separate process, native device and canonical replay player.</summary>
public static class ReplayExportWorkerHost
{
    internal static StudioReplayExportTicket? Ticket;
    internal static int ExitCode;
    public static async Task LaunchAsync(string ticketPath)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("Studio executable is unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ReplayExportWorkerHost).Assembly.Location);
        start.ArgumentList.Add("--export-worker"); start.ArgumentList.Add(Path.GetFullPath(ticketPath));
        using var process = Process.Start(start) ?? throw new IOException("Export worker did not start.");
        // The task does not bind child lifetime to the editing document. Explicit cancellation
        // is a persisted signal which remains meaningful after a document/window is closed.
        Task stdout = Drain(process.StandardOutput);
        Task<string> stderr = Tail(process.StandardError);
        await process.WaitForExitAsync().ConfigureAwait(false);
        await stdout.ConfigureAwait(false); string errors = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            try
            {
                using var file = File.OpenRead(ticketPath);
                if (file.Length <= 1024 * 1024)
                {
                    var ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(file, new JsonSerializerOptions { IncludeFields = true });
                    string? log = ticket == null ? null : Path.Combine(ticket.CacheRoot, "worker.log");
                    if (log != null && File.Exists(log) && new FileInfo(log).Length <= 65536)
                        errors = File.ReadAllText(log) + errors;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            if (errors.Length > 16384) errors = errors[^16384..];
            throw new IOException("Export worker failed: " + errors);
        }
    }
    private static async Task Drain(StreamReader reader) { while (await reader.ReadLineAsync().ConfigureAwait(false) != null) { } }
    private static async Task<string> Tail(StreamReader reader)
    {
        var text = new System.Text.StringBuilder(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        { text.Append(buffer, 0, count); if (text.Length > 16384) text.Remove(0, text.Length - 16384); }
        return text.ToString();
    }
    public static int Run(string ticketPath)
    {
        MphRead.Mods.Update.InstallationLifetime? installation = null;
        TextWriter output = Console.Out, error = Console.Error;
        ReplayExportWorkerLog? diagnostics = null;
        try
        {
            using (var file = File.OpenRead(ticketPath))
            {
                if (file.Length > 1024 * 1024) throw new InvalidDataException("Export ticket exceeds the size limit.");
                Ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(file, new JsonSerializerOptions { IncludeFields = true })
                    ?? throw new InvalidDataException("Export ticket is empty.");
            }
            diagnostics = new(Path.Combine(Ticket.CacheRoot, "worker.log"));
            Console.SetOut(diagnostics); Console.SetError(diagnostics);
            installation = MphRead.Mods.Update.InstallationLifetime.AcquireApplication(AppContext.BaseDirectory);
            foreach (var path in Ticket.RuntimePaths) MphRead.Paths.SetPath(path.Key, path.Value);
            MphRead.Paths.MphKey = Ticket.MphKey; MphRead.Paths.FhKey = Ticket.FhKey;
            Directory.CreateDirectory(Ticket.CacheRoot);
            string snapshot = Path.Combine(Ticket.CacheRoot, "source.ppdemo");
            using (var source = new FileStream(Ticket.ReplayPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.None))
            { source.CopyTo(output); output.Flush(flushToDisk: true); }
            Ticket = Ticket with { ReplayPath = snapshot };
            ExitCode = 0;
            StudioGraphicsHost.Initialize(safeMode: false);
            AppBuilder.Configure<ReplayExportWorkerApplication>().UsePlatformDetect().WithInterFont()
                .StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
            return ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            if (Ticket != null) WriteStatus(new(Ticket.Id, "Failed", 0, 0, ex.Message, Ticket.Request.Directory));
            return 1;
        }
        finally
        {
            try { StudioGraphicsHost.Shutdown(); }
            finally
            {
                installation?.Dispose();
                Console.SetOut(output); Console.SetError(error); diagnostics?.Dispose();
            }
        }
    }
    internal static void WriteStatus(StudioReplayExportStatus status)
    {
        if (Ticket == null) return;
        string staging = Ticket.StatusFile + ".staging";
        Directory.CreateDirectory(Path.GetDirectoryName(Ticket.StatusFile)!);
        File.WriteAllText(staging, JsonSerializer.Serialize(status with { Id = Ticket.Id }));
        File.Move(staging, Ticket.StatusFile, overwrite: true);
    }
}

public sealed class ReplayExportWorkerApplication : Application
{
    public override void Initialize() { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ReplayExportWorkerWindow(ReplayExportWorkerHost.Ticket!);
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class ReplayExportWorkerWindow : Window
{
    private readonly StudioReplayExportTicket _ticket;
    private readonly StudioReplayPlayer _player;
    private readonly ReplayViewportHost _viewport;
    private readonly TextBlock _status = new() { Margin = new Thickness(12) };
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastWrite;
    private Guid? _job;
    private bool _terminal;
    internal ReplayExportWorkerWindow(StudioReplayExportTicket ticket)
    {
        _ticket = ticket; Title = "Project Prime Studio · Export worker"; Width = 800; Height = 520;
        _player = new(ticket.ReplayPath, ticket.CacheRoot, ticket.PackageDirectories);
        _viewport = new(new ExportPresentation(_player));
        var content = new DockPanel(); DockPanel.SetDock(_status, Dock.Top); content.Children.Add(_status);
        var cancel = new Button { Content = "Cancel export", Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Cancel(); DockPanel.SetDock(cancel, Dock.Bottom); content.Children.Add(cancel); content.Children.Add(_viewport); Content = content;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        Opened += (_, _) => _timer.Start();
        Closing += (_, args) => { if (!_terminal) { Cancel(); args.Cancel = true; } };
        Closed += (_, _) => { _timer.Stop(); _viewport.Dispose(); _player.Dispose(); };
    }
    private void Cancel() { if (_job is { } job) _player.CancelExport(job); else Finish(new(_ticket.Id, "Cancelled", 0, 0, null, _ticket.Request.Directory)); }
    private void Tick()
    {
        if (_terminal) return;
        try
        {
            if (_viewport.PresentationError is { } error) { Finish(new(_ticket.Id, "Failed", 0, 0, error, _ticket.Request.Directory)); return; }
            if (_player.Status.Error is { } failure) { Finish(new(_ticket.Id, "Failed", 0, 0, failure, _ticket.Request.Directory)); return; }
            if (File.Exists(_ticket.CancelFile)) { Cancel(); if (_terminal) return; }
            if (_job == null && _player.Status.Ready)
            {
                foreach (var key in _ticket.CameraKeys) _player.PutCameraKey(key);
                _job = _player.QueueExport(_ticket.Request);
            }
            var state = _job is { } id ? _player.Exports.First(j => j.Id == id)
                : new StudioReplayExportStatus(_ticket.Id, "Preparing", 0, 0, null, _ticket.Request.Directory);
            _status.Text = $"{state.State} · {state.Frames}/{state.TotalFrames} frames";
            if (_clock.Elapsed - _lastWrite >= TimeSpan.FromSeconds(.25)) { ReplayExportWorkerHost.WriteStatus(state); _lastWrite = _clock.Elapsed; }
            if (state.State is "Complete" or "Failed" or "Cancelled") Finish(state);
        }
        catch (Exception ex) { Finish(new(_ticket.Id, "Failed", 0, 0, ex.Message, _ticket.Request.Directory)); }
    }
    private void Finish(StudioReplayExportStatus status)
    {
        _terminal = true; _timer.Stop(); ReplayExportWorkerHost.WriteStatus(status);
        ReplayExportWorkerHost.ExitCode = status.State == "Failed" ? 1 : 0; Dispatcher.UIThread.Post(Close);
    }
    private sealed class ExportPresentation(StudioReplayPlayer player) : IStudioReplayGraphicsSession
    {
        public void OnGraphicsInitialize(int width, int height) => player.OnGraphicsInitialize(width, height);
        public void OnGraphicsFrame(TimeSpan elapsed, StudioReplayView view) => player.Advance(elapsed);
        public void OnGraphicsDeinitialize(bool nativeReleaseEligible) => player.OnGraphicsDeinitialize(nativeReleaseEligible);
    }
}
