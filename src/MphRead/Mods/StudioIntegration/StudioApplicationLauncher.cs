#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using MphRead.Mods.Update;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Launches the verified paired app; local builds resolve only this checkout's Studio output.</summary>
public static class StudioApplicationLauncher
{
    public static bool TryOpen(string? documentPath, bool recover, out string? error, string? workspace = null)
    {
        error = null;
        string? executable;
        if (BuildVersion.IsRelease)
        {
            if (!DesktopUpdate.TryGetPairedStudioExecutable(out string paired, out string detail))
            { error = "Install the complete matching desktop release to use Project Prime Studio. " + detail; return false; }
            executable = paired;
        }
        else executable = FindExecutable(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_PATH"));
        if (executable == null)
        { error = "Project Prime Studio is unavailable. Build Studio from this checkout or install the complete paired desktop release. The embedded editor remains available."; return false; }
        try
        {
            using var process = Process.Start(CreateStartInfo(executable, documentPath, recover, workspace));
            if (process != null) return true;
            error = "Project Prime Studio could not start.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or ArgumentException)
        { error = "Project Prime Studio could not start: " + ex.Message; }
        return false;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string? documentPath, bool recover = false, string? workspace = null)
    {
        executable = Path.GetFullPath(executable);
        var launch = new ProcessStartInfo { FileName = executable, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            string host = Environment.ProcessPath ?? "";
            if (!Path.IsPathFullyQualified(host) || !Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Build Studio's apphost before launching it from the game.");
            launch.FileName = host;
            launch.ArgumentList.Add(executable);
        }
        if (documentPath != null)
        {
            string path = Path.GetFullPath(documentPath);
            string extension = Path.GetExtension(path);
            string kind = extension.Equals(".ppdemo", StringComparison.OrdinalIgnoreCase) ? "--replay"
                : extension.Equals(".ppclip", StringComparison.OrdinalIgnoreCase) ? "--clip"
                : extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ppmap", StringComparison.OrdinalIgnoreCase) ? "--map"
                : throw new ArgumentException("Choose a map project, replay, or replay clip.");
            launch.ArgumentList.Add(kind); launch.ArgumentList.Add(path);
        }
        else launch.ArgumentList.Add(workspace switch { "maps" => "--maps", "replays" => "--replays", _ => "--home" });
        if (recover) launch.ArgumentList.Add("--recover");
        return launch;
    }

    public static string? FindExecutable(string installationDirectory, string? configured = null)
    {
        string name = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
        string installation = Path.GetFullPath(installationDirectory);
        bool Accept(string candidate) => File.Exists(candidate)
            && (configured == null || Path.GetFullPath(configured).Equals(candidate, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        // Local output must belong to this source checkout; an arbitrary path in
        // an environment variable never becomes executable authority.
        var directory = new DirectoryInfo(installation);
        for (int depth = 0; depth < 8 && directory != null; depth++, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "src", "ProjectPrime.Studio", "ProjectPrime.Studio.csproj"))
                || !File.Exists(Path.Combine(directory.FullName, "src", "MphRead", "MphRead.csproj"))) continue;
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string candidate = Path.Combine(directory.FullName, "src", "ProjectPrime.Studio", "bin", configuration, "net10.0", name);
                if (Accept(candidate)) return candidate;
            }
        }
        return null;
    }
}
#endif
