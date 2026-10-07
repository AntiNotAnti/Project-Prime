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
        StudioStartupMetrics.Begin();
        if (args is ["--version"])
        {
            static string Version(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine(JsonSerializer.Serialize(new { studioVersion=Version(typeof(Program).Assembly), gameVersion=Version(typeof(AvaloniaMapStudioHost).Assembly), studioIpcVersion=StudioProtocol.StudioIpcVersion }));
            return 0;
        }
        if (args.Any(arg => arg is "--help" or "-h")) { Console.WriteLine(StudioLaunchRequest.Usage); return 0; }
        bool exportWorker = args is ["--export-worker", _];
        StudioOpenRequest request = new(Guid.NewGuid(), StudioOpenKind.Home);
        if (!exportWorker && !StudioLaunchRequest.TryParse(args, out request, out string? error))
        { return ShowStartupError(error + Environment.NewLine + StudioLaunchRequest.Usage, 2); }
        TaskCompletionSource<StudioWindow> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource lifetime = new();
        StudioInstanceGuard? guard = null;
        MphRead.Mods.Update.InstallationLifetime? installation = null;
        try
        {
            StudioPaths paths = StudioPaths.CreateDefault();
            installation = MphRead.Mods.Update.InstallationLifetime.AcquireApplication(paths.InstallationDirectory);
            if (exportWorker) return ProjectPrime.Studio.Replay.ReplayExportWorkerHost.Run(args[1]);
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
            finally { installation?.Dispose(); }
        }
    }
    private static int ShowStartupError(string error, int exitCode)
    {
        Console.Error.WriteLine(error);
        App.ConfigureStartupError(error);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime([], Avalonia.Controls.ShutdownMode.OnMainWindowClose); }
        catch (Exception ex) { Console.Error.WriteLine("Unable to display the startup error window: " + ex.Message); }
        return exitCode;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
