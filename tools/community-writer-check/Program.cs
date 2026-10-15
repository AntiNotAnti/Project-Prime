using System.Diagnostics;
using System.Reflection;
using MphRead.Mods.MapGen;

if (args is ["--probe", var probeRoot, var probeVersion])
{
    try { using var lease = new CommunityTransferCoordination(probeRoot).AcquireWriterVersion(probeVersion); return 0; }
    catch (IOException) { return 3; }
}
if (args is ["--owner", var ownerRoot, var ownerVersion, var ready])
{
    using var lease = new CommunityTransferCoordination(ownerRoot).AcquireWriterVersion(ownerVersion);
    File.WriteAllText(ready, "ready");
    await Task.Delay(Timeout.Infinite);
    return 0;
}
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++; Console.WriteLine("PASS " + name);
}
Process Start(params string[] values)
{
    string host = Environment.ProcessPath!;
    var info = new ProcessStartInfo(host) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(host) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (var value in values) info.ArgumentList.Add(value);
    return Process.Start(info)!;
}
async Task<int> Probe(string root, string version)
{
    using var child = Start("--probe", root, version);
    try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
    catch { if (!child.HasExited) child.Kill(true); throw; }
    return child.ExitCode;
}
string directory = Path.Combine(Path.GetTempPath(), "prime-writers-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var coordination = new CommunityTransferCoordination(directory);
    using (var first = coordination.AcquireWriterVersion("1.2.3"))
    {
        using var second = coordination.AcquireWriterVersion("1.2.3");
        Check(await Probe(directory, "1.2.3") == 0, "matching Game/Studio writer versions coexist across OS processes");
        Check(await Probe(directory, "1.2.4") == 3, "different active version cannot join the authoritative transfer queue");
        Check(!File.Exists(Path.Combine(directory, "jobs-v1.json")), "version rejection does not create or rewrite job receipts");
    }
    Check(await Probe(directory, "2.0.0") == 0, "new version replaces stale metadata after all old writers exit");
    string signal = Path.Combine(directory, "ready");
    using (var crashed = Start("--owner", directory, "3.0.0", signal))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!File.Exists(signal)) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(25, timeout.Token); }
        Check(await Probe(directory, "4.0.0") == 3, "live writer remains protected until its OS lifetime lock is released");
        crashed.Kill(true); await crashed.WaitForExitAsync();
    }
    Check(await Probe(directory, "4.0.0") == 0, "crash releases writer ownership and permits safe version takeover");
    bool malformed = false;
    try { using var invalid = coordination.AcquireWriterVersion("bad\nversion"); }
    catch (InvalidDataException) { malformed = true; }
    Check(malformed, "invalid version metadata is rejected before admission");
    Console.WriteLine($"Community writer checks: {checks} PASS.");
    return 0;
}
finally { Directory.Delete(directory, true); }
