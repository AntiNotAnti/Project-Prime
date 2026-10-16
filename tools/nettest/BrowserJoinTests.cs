using System;
using System.IO;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

/// <summary>
/// Tests the real portable join rejection path using a pre-cancelled token so
/// no external directory, server or network timing is needed.
/// </summary>
internal static class BrowserJoinTests
{
    internal static int Run()
    {
        string originalDir = LauncherPrefs.Directory;
        string originalHost = LauncherPrefs.ServerAddress;
        int originalPort = LauncherPrefs.ServerPort;
        int originalKind = LauncherPrefs.LastKind;
        string originalName = LauncherPrefs.PlayerName;
        Hunter originalHunter = LauncherPrefs.LastHunter;
        int originalColor = LauncherPrefs.LastColor;
        string dir = Path.Combine(Path.GetTempPath(), "prime-browser-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool good, string reason)
        {
            checks++;
            if (!good) throw new InvalidOperationException("BROWSER JOIN " + reason);
        }
        try
        {
            Directory.CreateDirectory(dir);
            LauncherPrefs.Directory = dir;
            LauncherPrefs.ServerAddress = "known-good.example";
            LauncherPrefs.ServerPort = 27888;
            LauncherPrefs.LastKind = 2;
            LauncherPrefs.PlayerName = "Existing";
            LauncherPrefs.LastHunter = Hunter.Samus;
            LauncherPrefs.LastColor = 1;
            LauncherPrefs.Save();
            string initialFile = File.ReadAllText(Path.Combine(dir, "launcher.txt"));

            var invalid = ServerBrowserService.JoinAsync("  ", 27888, "Different", Hunter.Trace, 3)
                .GetAwaiter().GetResult();
            Check(!invalid.Joined, "blank endpoint rejected");
            var badPort = ServerBrowserService.JoinAsync("bad.invalid", 0, "Different", Hunter.Trace, 3)
                .GetAwaiter().GetResult();
            Check(!badPort.Joined, "port zero rejected");
            var highPort = ServerBrowserService.JoinAsync("bad.invalid", 65536, "Different", Hunter.Trace, 3)
                .GetAwaiter().GetResult();
            Check(!highPort.Joined, "invalid high port rejected");

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var failed = ServerBrowserService.JoinAsync("bad.invalid", 27889,
                    "Different", Hunter.Trace, 3, cancelled.Token).GetAwaiter().GetResult();
                Check(!failed.Joined && failed.Error == "Join cancelled.", "cancelled join refused");
            }
            Check(LauncherPrefs.ServerAddress == "known-good.example"
                && LauncherPrefs.ServerPort == 27888 && LauncherPrefs.LastKind == 2,
                "failed joins preserve in-memory last-known-good endpoint");
            Check(File.ReadAllText(Path.Combine(dir, "launcher.txt")) == initialFile,
                "failed joins do not persist a dead endpoint");

            Check(!ServerBrowserService.TryParseEndpoint("a:0", "old", 27888, out _, out _),
                "port zero parse rejected");
            Check(!ServerBrowserService.TryParseEndpoint("a:65536", "old", 27888, out _, out _),
                "out-of-range port rejected");
            Check(ServerBrowserService.TryParseEndpoint("a", "old", 27888,
                out var host, out int port) && host == "a" && port == 27888,
                "host-only entry preserves default port");
            Check(ServerBrowserService.TryParseEndpoint("a:28000", "old", 27888,
                out host, out port) && host == "a" && port == 28000,
                "explicit port correctly parsed");

            Console.WriteLine("BROWSER JOIN PASS " + checks + " checks");
            return 0;
        }
        finally
        {
            LauncherPrefs.Directory = originalDir;
            LauncherPrefs.ServerAddress = originalHost;
            LauncherPrefs.ServerPort = originalPort;
            LauncherPrefs.LastKind = originalKind;
            LauncherPrefs.PlayerName = originalName;
            LauncherPrefs.LastHunter = originalHunter;
            LauncherPrefs.LastColor = originalColor;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
