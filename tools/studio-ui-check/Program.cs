using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static int _checks;
    private static readonly List<object> Captures = [];

    private static async Task<int> Main(string[] args)
    {
        string output = args.Length > 0 ? Path.GetFullPath(args[0])
            : Path.Combine(Path.GetTempPath(), "prime-studio-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        string data = Path.Combine(Path.GetTempPath(), "prime-studio-ui-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Dispatch may complete its Task inline on the session worker; asynchronous disposal
            // lets that worker return before awaiting the dispatcher lifetime.
            await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(StudioUiApplication));
            await session.Dispatch(async () =>
            {
                await CheckShellAsync(output, data); await CheckMapEditorAsync(output, Path.Combine(data, "map-editor"),args.Contains("--assets"));
                int replay=Array.IndexOf(args,"--replay");
                if(replay>=0)await CheckReplayEditorAsync(output,Path.Combine(data,"replay-editor"),Path.GetFullPath(args[replay+1]));
                return true;
            }, CancellationToken.None);
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                scope = "Desktop shell and canonical Map Studio CPU viewport; optional actual Replay editor controls without headless GPU pixels. Actual GPU presentation uses separate native acceptance.",
                checks = _checks,
                captures = Captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Studio shell UI checks passed: {_checks}. Screenshots: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { if (Directory.Exists(data)) Directory.Delete(data, recursive: true); } catch (IOException) { } }
    }

    private static async Task CheckShellAsync(string output, string data)
    {
        Directory.CreateDirectory(data);
        var paths = new StudioPaths(AppContext.BaseDirectory, data);
        var settings = new StudioSettings();
        var window = new StudioWindow(paths, settings, new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home));
        ProjectPrime.Studio.Diagnostics.StudioPerformanceHud? disposingHud=null;
        window.Show();
        await window.InitializeAsync();
        try
        {
            Check(window.Documents.Documents.Count == 0, "asset-free Home starts without documents");
            await CaptureVariantsAsync(window, output, "home", null);
            CheckHotkeys(settings, paths);
            CheckPerformanceHud(window);
            CheckAboutWindow(window,paths,output);
            window.OpenEmptyWorkspace(StudioDocumentKind.Map);
            PumpLayout(window);
            Check(window.GetVisualDescendants().OfType<MapStudioWorkspace>().Any(view => view.IsEffectivelyVisible), "empty Map workspace is hosted in desktop window");
            await CaptureVariantsAsync(window, output, "map-empty", "Map Studio");
            window.OpenEmptyWorkspace(StudioDocumentKind.Replay);
            PumpLayout(window);
            Check(window.GetVisualDescendants().OfType<ReplayStudioWorkspace>().Any(view => view.IsEffectivelyVisible), "empty Replay workspace is hosted in desktop window");
            await CaptureVariantsAsync(window, output, "replay-empty", "Replay Studio");
            Check(window.Documents.Documents.Count == 2, "Map and Replay workspaces share one multiple-document host");
            Check(!window.Commands[StudioCommand.Save].CanExecute(null) && !window.Commands[StudioCommand.ReplayPlayPause].CanExecute(null),
                "unavailable editing and playback commands are disabled in inspection shell");
            CheckDocking(window);
            await CheckOpenFailuresAsync(window, data);
            window.TogglePerformanceHud();
            disposingHud=window.PerformanceHud!;disposingHud.RefreshSnapshot();
        }
        finally
        {
            Check(await window.TryCloseAsync(), "clean shell shutdown closes owned workspaces");
            window.Close();
            await window.DisposeResourcesAsync();
            Dispatcher.UIThread.RunJobs();
            if(disposingHud is not null)Check(!disposingHud.IsSampling&&disposingHud.Snapshot is null,
                "application disposal stops active HUD timer and releases measured sources");
        }
        Check(new StudioSettingsStore(paths).LoadSession() is { CleanExit: true, Documents.Count: 2 },
            "normal shell exit persists open tabs for later restoration");
        Check(!window.EnqueueLaunchRequest(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home)).Accepted,
            "closed desktop rejects new forwarded work");
        await CheckRecoveryAndSafeModeAsync(data);
    }

    private static async Task CaptureVariantsAsync(StudioWindow window, string output, string route, string? heading)
    {
        foreach ((int width, int height, double scale) in new[] { (1280, 800, 1d), (1920, 1080, 1d), (1280, 800, 2d) })
        {
            window.Width = width;
            window.Height = height;
            window.SetRenderScaling(scale);
            PumpLayout(window);
            Check(Math.Abs(window.RenderScaling - scale) < .01, route + " actual headless DPI scaling");
            Check(window.ClientSize.Width >= width - 1 && window.ClientSize.Height >= height - 1,
                route + " usable logical client area");
            if (heading is not null)
            {
                TextBlock title = window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == heading && block.FontSize >= 24 && block.IsEffectivelyVisible);
                CheckControlBounds(window, title, 100, 20, route + " workspace heading is visible and unclipped");
            }
            else
            {
                TextBlock title = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "StudioHomeHeading");
                CheckControlBounds(window, title, 100, 20, route + " Home heading is visible and unclipped");
            }
            TextBlock status = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "StudioStatus");
            CheckControlBounds(window, status, 100, 10, route + " status bar is visible and unclipped");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Count(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text)) >= 10,
                route + " renders meaningful shell text");
            foreach (Button button in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && button.Bounds.Width > 0 && button.Bounds.Height > 0))
                CheckControlBounds(window, button, 1, 1, route + " interactive control is inside window");
            using Bitmap bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Native Skia frame did not render.");
            Check(bitmap.PixelSize.Width == (int)Math.Round(window.ClientSize.Width * scale)
                && bitmap.PixelSize.Height == (int)Math.Round(window.ClientSize.Height * scale), route + " screenshot physical resolution matches DPI");
            CheckImageContent(bitmap, route);
            string name = $"{route}-{width}x{height}" + (scale == 1 ? "" : "-2x") + ".png";
            bitmap.Save(Path.Combine(output, name), new PngBitmapEncoderOptions());
            Captures.Add(new { route, logicalWidth = width, logicalHeight = height, scale, file = name,
                pixelWidth = bitmap.PixelSize.Width, pixelHeight = bitmap.PixelSize.Height });
            await Task.Yield();
        }
    }

    private static void CheckDocking(StudioWindow window)
    {
        StudioDockHost dock = window.GetVisualDescendants().OfType<StudioDockHost>().Single();
        dock.Hide(StudioDockRegion.Left);
        PumpLayout(window);
        Check(!dock.IsRegionVisible(StudioDockRegion.Left) && !dock.CaptureLayout().LeftVisible, "hidden dock panel updates persistent layout");
        dock.Show(StudioDockRegion.Left);
        dock.Detach(StudioDockRegion.Right);
        PumpLayout(window);
        Check(dock.CaptureLayout().Detached.Contains(StudioDockRegion.Right), "dock panel detaches into native window and persists region");
        dock.RestoreDefaults();
        PumpLayout(window);
        Check(dock.IsRegionVisible(StudioDockRegion.Left) && dock.IsRegionVisible(StudioDockRegion.Right) && dock.IsRegionVisible(StudioDockRegion.Bottom)
            && dock.CaptureLayout().Detached.Count == 0, "default layout closes floating panel and restores all dock regions");
    }

    private static async Task CheckRecoveryAndSafeModeAsync(string root)
    {
        var paths = new StudioPaths(AppContext.BaseDirectory, Path.Combine(root, "restore"));
        var store = new StudioSettingsStore(paths);
        Guid mapId = Guid.NewGuid(), replayId = Guid.NewGuid();
        var previous = new StudioSession { CleanExit = false, SelectedDocument = replayId };
        previous.Documents.Add(new(mapId, StudioDocumentKind.Map, null));
        previous.Documents.Add(new(Guid.NewGuid(), StudioDocumentKind.Replay, Path.Combine(root, "missing-recovery.ppdemo")));
        previous.Documents.Add(new(replayId, StudioDocumentKind.Replay, null));
        previous.Documents.Add(new(mapId, StudioDocumentKind.Map, null));
        previous.Documents.Add(null!);
        Check(store.SaveSession(previous), "unclean recovery fixture is durable");
        var restored = new StudioWindow(paths, new StudioSettings(), new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, Recover: true), store.LoadSession());
        await restored.InitializeAsync(promptForRecovery: false);
        restored.Show();
        PumpLayout(restored);
        Check(restored.Documents.Documents.Count == 2 && restored.Documents.ActiveDocument?.Id.Value == replayId,
            "explicit recovery restores workspaces and selection while skipping missing/null/duplicate source entries");
        await restored.TryCloseAsync();
        restored.Close();
        await restored.DisposeResourcesAsync();

        var settings = new StudioSettings
        {
            Layout = new StudioDockLayout { LeftVisible = false, RightVisible = false, BottomVisible = false, Detached = [StudioDockRegion.Right] }
        };
        Check(store.SaveSettings(settings) && store.SaveSession(previous), "safe-mode fixture stores custom layout and unclean session");
        string settingsBefore = File.ReadAllText(paths.SettingsFile), sessionBefore = File.ReadAllText(paths.SessionFile);
        var safe = new StudioWindow(paths, store.LoadSettings(), new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, Recover: true, SafeMode: true), store.LoadSession());
        await safe.InitializeAsync(promptForRecovery: false);
        safe.Show();
        PumpLayout(safe);
        Check(safe.Documents.Documents.Count == 0 && safe.DockHost.IsRegionVisible(StudioDockRegion.Left)
            && safe.DockHost.IsRegionVisible(StudioDockRegion.Right) && safe.DockHost.IsRegionVisible(StudioDockRegion.Bottom)
            && safe.DockHost.CaptureLayout().Detached.Count == 0, "safe mode starts with default layout and suppresses session recovery");
        await safe.TryCloseAsync();
        safe.Close();
        await safe.DisposeResourcesAsync();
        Check(File.ReadAllText(paths.SettingsFile) == settingsBefore && File.ReadAllText(paths.SessionFile) == sessionBefore,
            "safe mode leaves saved layout/session bytes intact");
    }

    private static async Task CheckOpenFailuresAsync(StudioWindow window, string directory)
    {
        int existing = window.Documents.Documents.Count;
        StudioOpenRequest missing = new(Guid.NewGuid(), StudioOpenKind.Replay, Path.Combine(directory, "missing.ppdemo"));
        StudioRequestResult failed = await window.HandleLaunchRequestAsync(missing, CancellationToken.None);
        PumpLayout(window);
        Check(!failed.Accepted && window.Documents.Documents.Count == existing && window.Documents.ActiveDocument?.Kind == StudioDocumentKind.Replay,
            "missing forwarded file leaves existing workspace alive");
        Check(!window.EnqueueLaunchRequest(missing).Accepted, "missing source is rejected before IPC acceptance");
        Check(!window.EnqueueLaunchRequest(new StudioOpenRequest(Guid.Empty, StudioOpenKind.Home)).Accepted,
            "invalid forwarded request is rejected before queueing");
        Check(!window.EnqueueLaunchRequest(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, SafeMode: true)).Accepted,
            "safe-mode launch cannot silently change an existing normal workspace");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(!window.EnqueueLaunchRequest(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home), cancelled.Token).Accepted,
            "cancelled forwarded request is not accepted");
        Check(!(await window.HandleLaunchRequestAsync(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home), cancelled.Token)).Accepted,
            "cancelled direct open returns rejection without an unhandled gate exception");

        string source = Path.Combine(directory, "queued.json");
        MapProjectSerializer.Save(MapTemplates.Create("QUEUED MAP", false), source);
        string sourceBefore = File.ReadAllText(source);
        Check(!window.EnqueueLaunchRequest(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Replay, source)).Accepted,
            "wrong workspace extension is rejected before queueing");
        var request = new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Map, source);
        Check(window.EnqueueLaunchRequest(request).Accepted, "valid forwarded source is accepted into observable job queue");
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (window.Documents.Find(StudioDocumentKind.Map, source) is null && timeout.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        IStudioDocument? document = window.Documents.Find(StudioDocumentKind.Map, source);
        if (document is null || document.Dirty || window.Documents.Documents.Count != existing + 1)
            Console.Error.WriteLine("Queued open diagnostics: " + string.Join("; ", window.Jobs.Jobs.Select(job => job.Title + "=" + job.State + ":" + job.Error))
                + " · documents=" + string.Join(", ", window.Documents.Documents.Select(item => item.Title + ":" + item.Path + ":" + item.Dirty))
                + " · status=" + window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "StudioStatus").Text);
        Check(document is not null && !document.Dirty && window.Documents.Documents.Count == existing + 1 && File.ReadAllText(source) == sourceBefore,
            "queued canonical source open publishes one clean tab without changing source bytes");
        Check(new StudioSettingsStore(new StudioPaths(AppContext.BaseDirectory, directory)).LoadSettings().RecentDocuments.Any(item => item.Path == source),
            "queued successful open persists recent source");
        Check(window.EnqueueLaunchRequest(request with { RequestId = Guid.NewGuid() }).Accepted, "repeated open request is accepted");
        PumpLayout(window);
        Check(window.Documents.Documents.Count == existing + 1, "reopening source selects existing tab without duplication");
        await window.Documents.RequestCloseAsync(document!, _ => Task.FromResult(StudioCloseDecision.Discard), _ => Task.FromResult<string?>(null));
        string malformed = Path.Combine(directory, "malformed-map.json");
        await File.WriteAllTextAsync(malformed, "{malformed");
        Check(!(await window.HandleLaunchRequestAsync(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Map, malformed))).Accepted
            && window.Documents.Documents.Count == existing && File.ReadAllText(malformed) == "{malformed",
            "malformed map returns rejection while retaining previous workspaces and source bytes");
    }

    private static void CheckControlBounds(StudioWindow window, Control control, double minimumWidth, double minimumHeight, string message)
    {
        Point? origin = control.TranslatePoint(default, window);
        Check(origin is { } point && control.Bounds.Width >= minimumWidth && control.Bounds.Height >= minimumHeight
            && point.X >= -.5 && point.Y >= -.5 && point.X + control.Bounds.Width <= window.ClientSize.Width + .5
            && point.Y + control.Bounds.Height <= window.ClientSize.Height + .5,
            message+ $"; control={control.GetType().Name}, origin={origin}, bounds={control.Bounds}, client={window.ClientSize}");
    }

    private static void CheckImageContent(Bitmap bitmap, string route)
    {
        using WriteableBitmap readable = new(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer framebuffer = readable.Lock();
        bitmap.CopyPixels(framebuffer);
        byte[] pixels = new byte[framebuffer.RowBytes * framebuffer.Size.Height];
        Marshal.Copy(framebuffer.Address, pixels, 0, pixels.Length);
        HashSet<int> colors = [];
        int opaque = 0, bright = 0;
        int samples = 0;
        for (int y = 0; y < framebuffer.Size.Height; y += 4)
            for (int x = 0; x < framebuffer.Size.Width; x += 4)
            {
                int offset = y * framebuffer.RowBytes + x * 4;
                samples++;
                if (pixels[offset + 3] > 240) opaque++;
                if (pixels[offset] + pixels[offset + 1] + pixels[offset + 2] > 420) bright++;
                colors.Add((pixels[offset] >> 3) | ((pixels[offset + 1] >> 3) << 5) | ((pixels[offset + 2] >> 3) << 10));
            }
        Check(opaque > samples * .95 && bright > samples * .001 && colors.Count >= 30,
            route + " actual image has opaque surface, contrast and text instead of a blank frame");
    }

    private static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }
}

public sealed class StudioUiApplication
{
    public static AppBuilder BuildAvaloniaApp() => ProjectPrime.Studio.Program.BuildAvaloniaApp()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true });
}
