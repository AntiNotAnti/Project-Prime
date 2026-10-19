using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>Installation-wide coordination, independent of account and user-data directory.</summary>
internal static class DesktopInstallationUse
{
    internal static string MutexName(string installation, bool studio)
    {
        string root = Path.GetFullPath(installation)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        return (studio ? "ProjectPrime.StudioClient." : "ProjectPrime.InteractiveClient.") + suffix;
    }

    internal static IDisposable Acquire(string installation)
        => new FileStream(ReleaseInstallation.OwnedPath(installation, ".project-prime-update.lock", internalPath: true),
            FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);

    internal static bool IsStudioProcess =>
        String.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "ProjectPrimeStudio", StringComparison.OrdinalIgnoreCase)
        || Environment.GetCommandLineArgs().Contains("--studio", StringComparer.Ordinal);

    internal static bool OtherApplicationRunning(string installation, bool studio)
        // A probe must never recursively acquire a mutex owned by the UI thread.
        => Task.Run(() =>
        {
            try
            {
                using var mutex = Mutex.OpenExisting(MutexName(installation, !studio));
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                if (owned) mutex.ReleaseMutex();
                return !owned;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }).GetAwaiter().GetResult();

    internal static string? UpdateBlockedReason(string installation, bool studio)
        => OperatingSystem.IsWindows() && OtherApplicationRunning(installation, studio)
            ? "Close " + (studio ? "Project Prime" : "Project Prime Studio") + " before installing this version. Both applications belong to the same installation."
            : null;
}
