using Avalonia;
using Avalonia.Threading;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead.AvaloniaShared;

namespace ProjectPrime.Studio;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--radiant-mesh-worker"]) return Map.RadiantMeshWorker.Run();
        if (args is ["--replay-export-worker", var ticket]) return Replay.ReplayExportWorker.Run(ticket);
        if(args is ["--self-test"])return StudioAcceptanceChecks.Run();
        if(args is ["--native-map-check",var mapSource])return StudioNativeMapChecks.Run(mapSource);
        if(args is ["--native-replay-check",var replaySource,var pathsFile])return StudioNativeMapChecks.Run(replaySource,pathsFile);
        StudioStartupMetrics.Begin();
        if (args is ["--version"])
        {
            static string Version(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine(JsonSerializer.Serialize(new { studioVersion=Version(typeof(Program).Assembly), gameVersion=Version(typeof(AvaloniaMapStudioHost).Assembly), studioIpcVersion=StudioProtocol.StudioIpcVersion }));
            return 0;
        }
        if (args.Any(arg => arg is "--help" or "-h")) { Console.WriteLine(StudioLaunchRequest.Usage); return 0; }

        StudioOpenRequest request = new(Guid.NewGuid(), StudioOpenKind.Home);
        if (!StudioLaunchRequest.TryParse(args, out request, out string? error))
        { return ShowStartupError(error + Environment.NewLine + StudioLaunchRequest.Usage, 2); }
        TaskCompletionSource<StudioWindow> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource lifetime = new();
        StudioInstanceGuard? guard = null;
        IDisposable? installation = null;
        try
        {
            StudioPaths paths = StudioPaths.CreateDefault();
            installation = MphRead.Mods.Update.DesktopUpdate.HoldInstallationUseLease();

            // Studio is a separate process from the game, but replay presentation is
            // expected to use the same local visual choices. Without applying the
            // game's settings here, HD character/viewmodel replacements, cosmetics
            // and other render options stay at their static defaults in Studio.
            StudioGameAssets.LoadPresentationSettings();

            guard = StudioInstanceGuard.TryAcquireAsync(paths.InstallationDirectory, paths.UserDataDirectory, request,
                async (forwarded, token) =>
                {
                    StudioWindow window = await ready.Task.WaitAsync(token).ConfigureAwait(false);
                    return await Dispatcher.UIThread.InvokeAsync(() => window.EnqueueLaunchRequest(forwarded, token));
                }, lifetime.Token).GetAwaiter().GetResult();
            if (!guard.IsPrimary)
            {
                return guard.ForwardResult.Accepted ? 0 : ShowStartupError(guard.ForwardResult.Error ?? "Studio could not receive the open request.", 1);
            }
            App.Configure(paths, request, window => ready.TrySetResult(window));
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([], Avalonia.Controls.ShutdownMode.OnMainWindowClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ArgumentException)
        { return ShowStartupError("Studio could not start: " + ex.Message, 1); }
        finally
        {
            lifetime.Cancel();
            ready.TrySetCanceled(lifetime.Token);
            try { if (guard is not null) guard.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            finally
            {
                MphRead.Sound.AudioLifetime.Shutdown();
                installation?.Dispose();
            }
        }
    }
    private static int ShowStartupError(string error, int exitCode)
    {
        Console.Error.WriteLine(error);
        // A failed native layout may leave Avalonia initialized. Starting a
        // second platform instance would hide the original diagnostic.
        if(Application.Current is not null)return exitCode;
        App.ConfigureStartupError(error);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime([], Avalonia.Controls.ShutdownMode.OnMainWindowClose); }
        catch (Exception ex) { Console.Error.WriteLine("Unable to display the startup error window: " + ex.Message); }
        return exitCode;
    }
    public static AppBuilder BuildAvaloniaApp() => ConfigureDesktop(AppBuilder.Configure<App>());
    internal static AppBuilder ConfigureDesktop(AppBuilder builder) => builder.UsePlatformDetect().WithInterFont()
        .With(new Win32PlatformOptions
        {
            RenderingMode = [Win32RenderingMode.Wgl, Win32RenderingMode.Software],
            WglProfiles = [new(Avalonia.OpenGL.GlProfileType.OpenGL, 2, 1)]
        })
        .With(new X11PlatformOptions
        {
            RenderingMode = [X11RenderingMode.Glx, X11RenderingMode.Software],
            GlProfiles = [new(Avalonia.OpenGL.GlProfileType.OpenGL, 2, 1)]
        })
        .With(new AvaloniaNativePlatformOptions
        {
            RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
        });
}
