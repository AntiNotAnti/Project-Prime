using System;
using System.Diagnostics;
namespace MphRead.Mods.Render.Hud;

/// <summary>Opt-in measurement of the 2D HUD-object pass, including legacy code. HUD models are a separate pass.</summary>
public static class HudDrawMetrics
{
    public static bool Enabled { get; private set; }
    private static long _frames,_bytes,_peakBytes,_ticks;
    public static void Enable()
    {
        if(Enabled) return;
        Enabled=true;
        AppDomain.CurrentDomain.ProcessExit += (_,_) => Console.WriteLine(Report());
    }
    public static long Begin(out long started)
    { started=Stopwatch.GetTimestamp(); return GC.GetAllocatedBytesForCurrentThread(); }
    public static void End(long before,long started)
    {
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        _frames++; _bytes+=bytes; _peakBytes=Math.Max(_peakBytes,bytes); _ticks+=Stopwatch.GetTimestamp()-started;
    }
    public static string Report() => $"HUDMETRICS frames={_frames} bytes={_bytes} peakBytes={_peakBytes} meanBytes={(_frames==0 ? 0 : (double)_bytes/_frames):F2} meanMs={(_frames==0 ? 0 : _ticks*1000.0/Stopwatch.Frequency/_frames):F4}";
}
