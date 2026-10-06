using System.Diagnostics;
using System.Text.Json;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0] == "--instance-probe") return await RunInstanceProbeAsync(args);
        if (args.Length > 0 && args[0] == "--native-probe") return RunNativeProbe(args);
        if (args.Length > 0 && args[0] == "--replay-probe") return RunReplayProbe(args);
        if (args.Length > 0 && args[0] == "--game-probe") return RunNativeGameProbe(args);
        if (args.Length == 2 && args[0] == "--export-worker") return ProjectPrime.Studio.Replay.ReplayExportWorkerHost.Run(args[1]);
        string directory = Path.Combine(Path.GetTempPath(), "prime-studio-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckLaunchParsing(directory);
            CheckPersistence(directory);
            await CheckDocumentLifecycleAsync(directory);
            await CheckJobsAsync();
            await CheckProcessIsolationAsync(directory);
            if (Array.IndexOf(args,"--replay") is int replayIndex && replayIndex >= 0)
                await CheckReplayProcessesAsync(directory, args.Length > replayIndex + 1 ? args[replayIndex + 1]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime", "paths.txt"));
            if (args.Contains("--native")) await CheckNativeProcessLifecycleAsync(directory);
            if(Array.IndexOf(args,"--native-startup") is int startupIndex&&startupIndex>=0)
                await CheckNativeStartupAsync(directory,args[startupIndex+1]);
            if(Array.IndexOf(args,"--native-dense") is int denseIndex&&denseIndex>=0)
                await CheckNativeDenseMapAsync(directory,args[denseIndex+1]);
            if(Array.IndexOf(args,"--native-map-modes") is int modeIndex&&modeIndex>=0)
                await CheckNativeMapModesAsync(directory,args[modeIndex+1]);
            if (Array.IndexOf(args,"--native-game") is int gameIndex && gameIndex >= 0)
                await CheckNativeGameLifecycleAsync(directory, args.Length > gameIndex + 1 ? args[gameIndex + 1]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime", "paths.txt"));
            if(Array.IndexOf(args,"--native-replay") is int nativeReplayIndex&&nativeReplayIndex>=0)
                await CheckNativeReplayLifecycleAsync(directory,args[nativeReplayIndex+1],args[nativeReplayIndex+2],args[nativeReplayIndex+3],args.Contains("--native-replay-no-hud"),args.Contains("--native-replay-workers-only"),args.Contains("--native-replay-stale-view"));
            Console.WriteLine($"Studio lifecycle checks passed: {_checks}. Base gates cover CLI, persistence, document host, jobs and authenticated process isolation; " +
                "optional native/replay/game/performance gates run only when their explicit arguments are supplied.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }

    private static void CheckLaunchParsing(string directory)
    {
        Check(StudioLaunchRequest.TryParse([], out StudioOpenRequest home, out _) && home.Kind == StudioOpenKind.Home,
            "no arguments opens Home");
        foreach ((string option, string extension, StudioOpenKind kind) in new[]
        {
            ("--map", ".json", StudioOpenKind.Map),
            ("--replay", ".ppdemo", StudioOpenKind.Replay),
            ("--clip", ".ppclip", StudioOpenKind.Clip)
        })
        {
            string path = Path.Combine(directory, "spaces and unicode ü" + extension);
            Check(StudioLaunchRequest.TryParse(["--safe-mode", option, path, "--recover"], out StudioOpenRequest request, out _)
                && request.Kind == kind && request.Path == Path.GetFullPath(path) && request.Recover && request.SafeMode,
                option + " preserves one exact path and flags in any order");
        }
        Check(StudioLaunchRequest.TryParse(["--map", "relative.json"], out StudioOpenRequest relative, out _)
            && relative.Path == Path.GetFullPath("relative.json"), "relative document path is normalized");
        Check(StudioLaunchRequest.TryParse(["--recover", "--safe-mode"], out StudioOpenRequest recovery, out _)
            && recovery.Recover && recovery.SafeMode, "recovery and safe mode can open Home");
        foreach (string[] invalid in new[]
        {
            new[] { "--map" }, new[] { "--replay", "--safe-mode" }, new[] { "--clip" },
            new[] { "--map", "one.json", "--replay", "two.ppdemo" },
            new[] { "--mapstudio" }, new[] { "--map=one.json" }, new[] { "one.ppdemo" },
            new[] { "--safe-mode-extra" }, new[] { "--MAP", "one.json" }, new[] { "--unknown" },
            new[] { "--map", "\0invalid.json" }
        })
        {
            Check(!StudioLaunchRequest.TryParse(invalid, out _, out string? error) && !string.IsNullOrWhiteSpace(error),
                "invalid CLI fails with actionable error: " + string.Join(" ", invalid).Replace('\0', '?'));
        }
    }

    private static void CheckPersistence(string directory)
    {
        var paths = new StudioPaths(directory, Path.Combine(directory, "settings"));
        var store = new StudioSettingsStore(paths);
        Check(store.LoadSettings().RecentDocuments.Count == 0 && store.LoadSession() is null,
            "missing settings/session return defaults");
        string mapPath = Path.Combine(directory, "map.json");
        var settings = new StudioSettings { WindowWidth = 1920, WindowHeight = 1080 };
        settings.RecentDocuments.Add(new(StudioDocumentKind.Map, mapPath, DateTimeOffset.UtcNow));
        Check(store.SaveSettings(settings), "settings save");
        var loaded = store.LoadSettings();
        Check(loaded.WindowWidth == 1920 && loaded.WindowHeight == 1080
            && loaded.RecentDocuments.Single().Path == mapPath, "settings and recents survive restart");
        Guid id = Guid.NewGuid();
        var session = new StudioSession { CleanExit = false, SelectedDocument = id };
        session.Documents.Add(new(id, StudioDocumentKind.Replay, Path.Combine(directory, "missing.ppdemo")));
        Check(store.SaveSession(session), "unclean session is saved");
        Check(store.LoadSession() is { CleanExit: false } restored && restored.SelectedDocument == id
            && restored.Documents.Single().Id == id, "unclean session preserves selection and missing-file metadata");
        session.CleanExit = true;
        Check(store.SaveSession(session) && store.LoadSession() is { CleanExit: true }, "normal shutdown marks clean session");
        Check(!Directory.EnumerateFiles(paths.UserDataDirectory, "*.tmp").Any(), "successful atomic replacement leaves no temporary files");
        using (JsonDocument.Parse(File.ReadAllText(paths.SettingsFile))) { }
        using (JsonDocument.Parse(File.ReadAllText(paths.SessionFile))) { }
        for (int index = 0; index < 20; index++)
        {
            settings.WindowWidth = 1000 + index;
            Check(store.SaveSettings(settings) && store.LoadSettings().WindowWidth == 1000 + index,
                "atomic settings replacement remains parseable " + index);
        }
        File.WriteAllText(paths.SettingsFile, "{malformed");
        File.WriteAllText(paths.SessionFile, "{malformed");
        Check(store.LoadSettings().WindowWidth == 1280 && store.LastError is not null,
            "malformed settings fall back with diagnostics");
        Check(store.LoadSession() is null && store.LastError is not null,
            "malformed session is safely ignored with diagnostics");
        File.WriteAllText(paths.SettingsFile, "{\"Version\":999}");
        File.WriteAllText(paths.SessionFile, "{\"Version\":999,\"Documents\":[]}");
        Check(store.LoadSettings().WindowWidth == 1280 && store.LoadSession() is null, "future schemas do not restore incompatible state");
        File.WriteAllText(paths.SettingsFile, "{\"Version\":1,\"WindowWidth\":2,\"WindowHeight\":9000,\"RecentDocuments\":[{\"Kind\":\"Map\",\"Path\":\"relative.json\"}]}");
        var sanitized = store.LoadSettings();
        Check(sanitized.WindowWidth >= 900 && sanitized.WindowHeight <= 3000 && sanitized.RecentDocuments.Count == 0,
            "corrupt dimensions and relative recent paths are sanitized");
        File.WriteAllText(paths.SessionFile, new string('x', 1024 * 1024 + 1));
        Check(store.LoadSession() is null, "oversized session is rejected");
        string blockedRoot = Path.Combine(directory, "blocked-settings");
        File.WriteAllText(blockedRoot, "preserve");
        var blocked = new StudioSettingsStore(new StudioPaths(directory, blockedRoot));
        Check(!blocked.SaveSettings(new()) && blocked.LastError is not null && File.ReadAllText(blockedRoot) == "preserve",
            "persistence failure preserves prior bytes and reports error");
    }
}
