using System.Diagnostics;
using MphRead.Mods.Launcher;
using ProjectPrime.DesktopShared;

if (args.Length > 0 && args[0] is "--hold" or "--probe")
{
    int hostBaseIndex = Array.IndexOf(args, "--host-base");
    if (hostBaseIndex >= 0)
    {
        // Exercise the production guard against an uncanonicalized host base.
        // AppContext is the BCL host configuration seam; the guard API is unchanged.
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", args[hostBaseIndex + 1] + Path.DirectorySeparatorChar);
    }
    Console.WriteLine("BASE: " + AppContext.BaseDirectory);
    if (!ClientInstanceGuard.TryAcquireForProcess(TimeSpan.FromMilliseconds(400)))
    { Console.WriteLine("BLOCKED"); return 20; }
    try { Console.WriteLine("HELD"); if (args[0] == "--hold") Console.ReadLine(); return 0; }
    finally { ClientInstanceGuard.ReleaseProcess(); }
}

string root = Path.Combine(Path.GetTempPath(), "prime-client-guard-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
Process? owner = null, other = null;
try
{
    string install = Path.Combine(root, "InstallCase"), secondInstall = Path.Combine(root, "OtherInstall");
    Copy(install); Copy(secondInstall);
    owner = Start(install, "--hold"); string realBase = await Ready(owner);
    other = Start(secondInstall, "--hold"); await Ready(other);
    Check(!owner.HasExited && !other.HasExited, "different installation processes keep independent client guards");
    var same = await Probe(install);
    Check(same.Exit == 20 && same.Output.Contains("BLOCKED"), "a second actual process from the same installation is blocked");
    var obsoleteRole = await Probe(install, "-mapstudio");
    Check(obsoleteRole.Exit == 20 && obsoleteRole.Output.Contains("BLOCKED"),
        "an obsolete mapstudio command-line flag cannot split the game client guard role");
    string alias = Path.Combine(root, "InstallAlias");
    bool aliases = true;
    try { Directory.CreateSymbolicLink(alias, install); }
    catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { aliases = false; }
    catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314) { aliases = false; }
    if (aliases)
    {
        var launchedAlias = await Probe(alias);
        Check(launchedAlias.Exit == 20 && launchedAlias.Output.Contains("BLOCKED"),
            "a child launched through a real installation directory alias cannot acquire a second client guard");
        var linked = await Probe(alias, "--host-base", alias);
        string aliasBase = linked.Output.Split('\n').Single(line => line.StartsWith("BASE: "))[6..].Trim();
        Check(realBase != aliasBase && DesktopInstallationIdentity.CanonicalInstallation(realBase)
            == DesktopInstallationIdentity.CanonicalInstallation(aliasBase),
            "independent child host contexts expose distinct real and alias paths to the same physical installation");
        Check(linked.Exit == 20 && linked.Output.Contains("BLOCKED"),
            "the production guard blocks a real process with an uncanonicalized symbolic-link host base");
        Check(!owner.HasExited && !other.HasExited, "blocked alias leaves both existing installation owners alive");
        await owner.StandardInput.WriteLineAsync("release"); await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(owner.ExitCode == 0 && (await Probe(alias, "--host-base", alias)).Exit == 0,
            "normal owner release permits the same installation alias to acquire the guard");
        owner.Dispose(); owner = Start(install, "--hold"); await Ready(owner);
        owner.Kill(entireProcessTree: true); await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check((await Probe(alias, "--host-base", alias)).Exit == 0, "owner process death releases the native mutex for alias recovery");
        Check(!other.HasExited, "owner death leaves the unrelated installation guard alive");
    }
    else Console.WriteLine("SKIP: real alias process checks require Windows symbolic-link privilege.");
    await other.StandardInput.WriteLineAsync("release"); await other.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(other.ExitCode == 0, "independent installation releases its own process lease normally");
    Console.WriteLine($"PASS: {checks} real client-guard process assertions."); return 0;
}
finally
{
    foreach (var process in new[] { owner, other })
    { if (process == null) continue; if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } process.Dispose(); }
    Directory.Delete(root, true);
}

void Copy(string destination)
{
    Directory.CreateDirectory(destination);
    foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
}
Process Start(string directory, params string[] arguments)
{
    string? runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? (runtime == null ? "dotnet" : Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
    var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardInput = true,
        RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(directory, "client-instance-check.dll"));
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    return Process.Start(start)!;
}
async Task<string> Ready(Process process)
{
    string? first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    string? status = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (first?.StartsWith("BASE: ") != true || status != "HELD") throw new InvalidOperationException("Owner could not hold its client guard: " + first + " " + status);
    return first[6..];
}
async Task<(int Exit, string Output)> Probe(string directory, params string[] extra)
{
    using var child = Start(directory, new[] { "--probe" }.Concat(extra).ToArray());
    try
    {
        Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return (child.ExitCode, await output + await error);
    }
    finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
}
void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); }
