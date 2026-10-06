using MphRead.Mods.StudioIntegration;

string root = Path.Combine(Path.GetTempPath(), "studio launch check " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int count = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
    count++;
    Console.WriteLine("PASS " + message);
}
try
{
    string name = OperatingSystem.IsWindows() ? "ProjectPrimeStudio.exe" : "ProjectPrimeStudio";
    string executable = Path.Combine(root, name);
    File.WriteAllText(executable, "fixture");
    Check(StudioApplicationLauncher.FindExecutable(root) == executable, "paired executable resolved beside game");
    foreach (var item in new[] { ("--replay", "replay with spaces.ppdemo"), ("--clip", "take with spaces.ppclip"), ("--map", "map with spaces.json"), ("--map", "runtime package.ppmap") })
    {
        string document = Path.Combine(root, item.Item2);
        var start = StudioApplicationLauncher.CreateStartInfo(executable, document);
        Check(start.FileName == executable && !start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] { item.Item1, document }),
            "deep link preserves exact " + item.Item1 + " path as one argument");
    }
    Check(StudioApplicationLauncher.CreateStartInfo(executable, null, true).ArgumentList.SequenceEqual(new[] { "--recover" }),
        "legacy recovery launch carries only explicit recovery option");
    var firstLaunch = StudioApplicationLauncher.CreateStartInfo(executable, null);
    var secondLaunch = StudioApplicationLauncher.CreateStartInfo(executable, null);
    string firstSecret = firstLaunch.Environment["PROJECT_PRIME_STUDIO_LAUNCH_SECRET"]!;
    Check(Convert.FromBase64String(firstSecret).Length == 32
        && firstSecret != secondLaunch.Environment["PROJECT_PRIME_STUDIO_LAUNCH_SECRET"]
        && !firstLaunch.ArgumentList.Contains(firstSecret),
        "parent launches receive independent 256-bit capabilities through the environment");
    Check(StudioApplicationLauncher.FindExecutable(root, "relative/path") == null,
        "explicit executable override must be absolute");
    Check(StudioApplicationLauncher.FindExecutable(root, executable + ".missing") == null,
        "missing explicit override is diagnosed instead of silently choosing another install");
    File.Delete(executable);
    string game = Path.Combine(root, "Project Prime.app", "Contents", "MacOS");
    string studio = Path.Combine(root, "Project Prime Studio.app", "Contents", "MacOS", "ProjectPrimeStudio");
    Directory.CreateDirectory(game); Directory.CreateDirectory(Path.GetDirectoryName(studio)!); File.WriteAllText(studio, "fixture");
    Check(StudioApplicationLauncher.FindExecutable(game) == studio, "macOS game resolves sibling Studio bundle");
    string dll = Path.Combine(root, "ProjectPrimeStudio.dll"); File.WriteAllText(dll, "fixture");
    var development = StudioApplicationLauncher.CreateStartInfo(dll, Path.Combine(root, "source.ppdemo"));
    Check(development.ArgumentList[0] == dll && development.ArgumentList[1] == "--replay", "framework-dependent development launch selects dotnet host");
    Console.WriteLine($"Studio launch checks passed ({count}).");
    return 0;
}
finally { Directory.Delete(root, true); }
