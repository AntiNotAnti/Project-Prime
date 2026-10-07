#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Game entry points launch the paired desktop app; its authenticated instance guard forwards deep links.</summary>
public static class StudioApplicationLauncher
{
    public static bool TryOpen(string? documentPath, bool recover, out string? error)
    {
        error = null;
        string? executable = FindExecutable(AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_PATH"));
        if (executable == null)
        {
            error = "Project Prime Studio is unavailable. Install the complete desktop release or set PROJECT_PRIME_STUDIO_PATH to its executable.";
            return false;
        }
        try
        {
            var launch = CreateStartInfo(executable, documentPath, recover);
            using var process = Process.Start(launch);
            if (process != null) return true;
            error = "Project Prime Studio could not start.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or ArgumentException)
        { error = "Project Prime Studio could not start: " + ex.Message; }
        return false;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string? documentPath, bool recover = false)
    {
        executable = Path.GetFullPath(executable);
        var launch = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        launch.Environment["PROJECT_PRIME_STUDIO_LAUNCH_SECRET"] = ProjectPrime.Studio.Protocol.StudioIpcAuthentication.NewSecret();
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            string? runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            launch.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (runtime == null ? "dotnet" : Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            launch.ArgumentList.Add(executable);
        }
        else launch.FileName = executable;
        if (documentPath != null)
        {
            string path = Path.GetFullPath(documentPath);
            string extension = Path.GetExtension(path);
            launch.ArgumentList.Add(extension.Equals(".ppdemo", StringComparison.OrdinalIgnoreCase) ? "--replay"
                : extension.Equals(".ppclip", StringComparison.OrdinalIgnoreCase) ? "--clip" : "--map");
            launch.ArgumentList.Add(path);
        }
        if (recover) launch.ArgumentList.Add("--recover");
        return launch;
    }

    public static string? FindExecutable(string installationDirectory, string? configured = null)
    {
        if (!String.IsNullOrWhiteSpace(configured))
            return Path.IsPathFullyQualified(configured) && File.Exists(configured) ? configured : null;
        string name = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
        string installation = Path.GetFullPath(installationDirectory);
        foreach (string candidate in new[] { name, "ProjectPrimeStudio.dll" })
        {
            string path = Path.Combine(installation, candidate);
            if (File.Exists(path)) return path;
        }
        var directory = new DirectoryInfo(installation);
        for (int depth = 0; depth < 8 && directory != null; depth++, directory = directory.Parent)
        {
            // Published macOS apps are sibling bundles, each with independent resource seals.
            string bundle = Path.Combine(directory.FullName, "Project Prime Studio.app", "Contents", "MacOS", "ProjectPrimeStudio");
            if (File.Exists(bundle)) return bundle;
            if (!File.Exists(Path.Combine(directory.FullName, "src", "ProjectPrime.Studio", "ProjectPrime.Studio.csproj"))) continue;
            foreach (string configuration in new[] { "Release", "Debug" })
                foreach (string candidate in new[] { name, "ProjectPrimeStudio.dll" })
                {
                    string path = Path.Combine(directory.FullName, "src", "ProjectPrime.Studio", "bin", configuration, "net10.0", candidate);
                    if (File.Exists(path)) return path;
                }
        }
        return null;
    }
}
#endif
