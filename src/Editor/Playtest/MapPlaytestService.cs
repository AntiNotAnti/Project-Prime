using System.Diagnostics;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.Playtest;

public sealed class MapPlaytestService
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public Process Launch(string projectPath, bool bots, string? clientPath = null,
        bool deleteProjectOnExit = false)
    {
        if (IsRunning) throw new InvalidOperationException("A map playtest is already running.");
        string? configured = clientPath ?? Environment.GetEnvironmentVariable("PRIME_CLIENT_PATH")
            ?? FindPackagedClient();
        ProcessStartInfo start;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            start = new ProcessStartInfo(Path.GetFullPath(configured));
        }
        else
        {
            string repository = FindRepository(projectPath);
            start = new ProcessStartInfo("dotnet");
            start.ArgumentList.Add("run");
            start.ArgumentList.Add("--project");
            start.ArgumentList.Add(Path.Combine(repository, "src", "Client", "Client.csproj"));
            start.ArgumentList.Add("--no-build");
            start.ArgumentList.Add("--");
        }
        start.ArgumentList.Add("-editorplaytest");
        start.ArgumentList.Add(Path.GetFullPath(projectPath));
        if (bots) start.ArgumentList.Add("-bots");
        start.ArgumentList.Add("-seconds");
        start.ArgumentList.Add("86400");
        start.UseShellExecute = false;
        start.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        _process = Process.Start(start)
            ?? throw new InvalidOperationException("Project Prime client could not be started.");
        if (deleteProjectOnExit)
        {
            string temporaryProject = Path.GetFullPath(projectPath);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                try { File.Delete(temporaryProject); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
        }
        return _process;
    }

    private static string? FindPackagedClient()
    {
        string name = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "..", name),
            Path.Combine(AppContext.BaseDirectory, name)
        ];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static string FindRepository(string start)
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(Path.GetFullPath(start))!);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Game.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
