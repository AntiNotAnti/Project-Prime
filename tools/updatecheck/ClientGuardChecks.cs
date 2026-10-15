using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Launcher;

internal static class ClientGuardChecks
{
    internal static int? RunChild(string[] args)
    {
        if (args.Length != 2 || args[0] != "--exclusive-update-lease") return null;
        using var lease = new FileStream(Path.Combine(AppContext.BaseDirectory, ".project-prime-in-use.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        File.WriteAllText(args[1], "ready");
        Thread.Sleep(20000);
        return 0;
    }

    internal static void Run(Action<bool, string> check)
    {
        string signal = Path.Combine(Path.GetTempPath(), "prime-updater-start-" + Guid.NewGuid().ToString("N"));
        string executable = Environment.ProcessPath!;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--exclusive-update-lease"); info.ArgumentList.Add(signal);
        using var child = Process.Start(info)!;
        try
        {
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(signal) && timeout.Elapsed < TimeSpan.FromSeconds(10) && !child.HasExited) Thread.Sleep(20);
            check(File.Exists(signal), "separate updater process holds the exclusive installation lease");
            check(!ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromMilliseconds(50)),
                "Game startup fails closed while updater owns installation binaries");
        }
        finally
        {
            if (!child.HasExited) child.Kill(true);
            child.WaitForExit();
            File.Delete(signal);
        }
        check(ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromMilliseconds(50)),
            "failed updater contention releases interactive mutex for a later clean start");
        ClientInstanceGuard.ReleaseProcess();
        check(ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromMilliseconds(50)),
            "normal teardown releases both Game ownership leases");
        ClientInstanceGuard.ReleaseProcess();
    }
}

// The source under test only needs logging; no game engine is loaded here.
namespace MphRead.Mods
{
    internal static class DebugLog
    {
        internal static void Line(string category, string message) => Console.WriteLine("[" + category + "] " + message);
    }
}
