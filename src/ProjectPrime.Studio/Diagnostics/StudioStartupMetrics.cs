using System.Diagnostics;

namespace ProjectPrime.Studio.Diagnostics;

public static class StudioStartupMetrics
{
    private static readonly Stopwatch Clock = new();
    public static void Begin() => Clock.Restart();
    public static string RecordShellOpened()
    {
        long workingSet;
        using (Process process = Process.GetCurrentProcess()) workingSet = process.WorkingSet64;
        return $"Measured shell-open startup: {(Clock.IsRunning ? Clock.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "unavailable")} ms; process working set: {workingSet} bytes. Document inspection has not been prepared yet.";
    }
}
