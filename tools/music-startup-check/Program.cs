using System;
using System.IO;
using MphRead.Sound;

int failures = 0, checks = 0;
void Check(bool condition, string name)
{
    checks++;
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures++;
}

var healthy = new Engine();
var device = new object();
object acquired = MusicStartup.InitializeDevice(healthy, () => device);
Check(ReferenceEquals(acquired, device) && healthy.DisposeCalls == 0,
    "successful playback initialization retains the exact engine and device");
healthy.Dispose();
Check(healthy.DisposeCalls == 1, "successful engine remains owned by its ordinary shutdown");

int retained = 0, originalFailures = 0;
for (int cycle = 0; cycle < 120; cycle++)
{
    var engine = new Engine();
    var failure = new IOException("injected playback-device initialization failure");
    try { MusicStartup.InitializeDevice<object>(engine, () => throw failure); }
    catch (IOException ex) { if (ReferenceEquals(ex, failure)) originalFailures++; }
    if (engine.DisposeCalls != 1) retained++;
}
Check(retained == 0 && originalFailures == 120,
    "120 failed device initializations dispose each acquired engine exactly once and preserve their original failure");

var failedCleanup = new Engine(throwOnDispose: true);
var original = new InvalidOperationException("injected device failure");
Exception? observed = null;
try { MusicStartup.InitializeDevice<object>(failedCleanup, () => throw original); }
catch (Exception ex) { observed = ex; }
Check(failedCleanup.DisposeCalls == 1 && ReferenceEquals(original, observed),
    "engine disposal failure is attempted once without masking the startup failure");

Console.WriteLine($"RESULT {checks - failures} PASS / {failures} FAIL; failed startup engines retained: {retained}/120");
return failures == 0 ? 0 : 1;

sealed class Engine(bool throwOnDispose = false) : IDisposable
{
    internal int DisposeCalls;
    public void Dispose()
    {
        DisposeCalls++;
        if (throwOnDispose) throw new IOException("injected engine cleanup failure");
    }
}
