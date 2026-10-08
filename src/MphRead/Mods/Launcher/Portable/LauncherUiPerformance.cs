using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher;

/// <summary>Explicit diagnostic only. Timings include the real production UI path, including IME and accessibility polling.</summary>
internal static class LauncherUiPerformance
{
    private static readonly string? Output=Environment.GetEnvironmentVariable("PROJECT_PRIME_UI_PERF");
    private static readonly bool Exit=Environment.GetEnvironmentVariable("PROJECT_PRIME_UI_PERF_EXIT")=="1";
    private static readonly string? Screenshot=Environment.GetEnvironmentVariable("PROJECT_PRIME_UI_PERF_SCREENSHOT");
    private static readonly double Duration=double.TryParse(Environment.GetEnvironmentVariable("PROJECT_PRIME_UI_PERF_SECONDS"),out double seconds)?Math.Clamp(seconds,1,120):20;
    private static readonly Process BenchmarkProcess=Process.GetCurrentProcess();
    private static readonly List<double> Updates=new(4096),Draws=new(4096),Uploads=new(4096),FrameCosts=new(4096);
    private static readonly Dictionary<string,List<double>> Phases=new(StringComparer.Ordinal);
    private static long _firstPresented,_sampleStarted,_sampleCpuTicks,_allocationStart,_uploadBytes;
    private static long _frames,_redraws,_bitmapUploads;
    private static long _axRequests,_axReads,_axDecoded,_axReused,_axIdleSkips,_initialAxRequests,_initialAxReads,_initialAxDecoded,_initialAxReused,_initialAxIdleSkips;
    private static long _drawRequests,_drawCaptured,_drawReused,_initialDrawRequests,_initialDrawCaptured,_initialDrawReused;
    private static double _coldMs;
    private static double _pendingFrameCost;
    private static int _width,_height;
    private static bool _native,_written,_screenshotAttempted;
    internal static bool Enabled=>!string.IsNullOrWhiteSpace(Output);
    internal static bool ExitRequested=>Enabled&&Exit&&_written;
    internal static long Start()=>Enabled&&!_written?Stopwatch.GetTimestamp():0;
    private static bool Sampling=>Enabled&&!_written&&_sampleStarted!=0;
    // Capture the matched final composite during warmup, before sampled CPU and allocation counters begin.
    internal static string? TakeWarmupScreenshotPath()
    {
        if(!Enabled||_written||_screenshotAttempted||_firstPresented==0||_sampleStarted!=0
            ||string.IsNullOrWhiteSpace(Screenshot)||Stopwatch.GetElapsedTime(_firstPresented).TotalSeconds<3)return null;
        _screenshotAttempted=true;
        return Path.GetFullPath(Screenshot);
    }
    internal static void RecordAccessibilityCapture(long requests,long nativeReads,long decoded,long reused,long idleSkips=0)
    {
        if(!Enabled||_written)return;
        _axRequests=requests;_axReads=nativeReads;_axDecoded=decoded;_axReused=reused;_axIdleSkips=idleSkips;
    }
    internal static void RecordDrawListCapture(long requests,long captured,long reused)
    {
        if(!Enabled||_written)return;
        _drawRequests=requests;_drawCaptured=captured;_drawReused=reused;
    }
    internal static void RecordPhase(string name,long started)
    {
        if(started==0||!Sampling)return;
        if(!Phases.TryGetValue(name,out var samples))Phases.Add(name,samples=new(4096));
        samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    internal static void RecordNativeUpdate(long started){if(started!=0&&Sampling){double ms=Stopwatch.GetElapsedTime(started).TotalMilliseconds;Updates.Add(ms);_pendingFrameCost+=ms;}}
    internal static void RecordNativeRender(long started){if(started!=0&&Sampling){double ms=Stopwatch.GetElapsedTime(started).TotalMilliseconds;Draws.Add(ms);_pendingFrameCost+=ms;_redraws++;}}
    internal static void RecordAvaloniaUpdate(long started){if(started!=0&&Sampling){double ms=Stopwatch.GetElapsedTime(started).TotalMilliseconds;Updates.Add(ms);_pendingFrameCost+=ms;}}
    internal static void RecordAvaloniaComposite(long started){if(started!=0&&Sampling)_pendingFrameCost+=Stopwatch.GetElapsedTime(started).TotalMilliseconds;}
    internal static void RecordAvaloniaRedraw(double milliseconds,double uploadMilliseconds,long uploadBytes)
    {
        if(!Sampling)return;Draws.Add(milliseconds);_redraws++;
        if(uploadBytes>0){Uploads.Add(uploadMilliseconds);_uploadBytes+=uploadBytes;_bitmapUploads++;}
    }
    /// <summary>Call once after a successful, verified Home surface presentation.
    /// A startup/title frame must not start the workload's warmup or counters.</summary>
    internal static void Presented(int width,int height,bool native,bool homeReady)
    {
        if(!Enabled||_written)return;
        if(!homeReady)
        {
            if(_firstPresented!=0)
            {
                string failedPath=Path.GetFullPath(Output!);
                Directory.CreateDirectory(Path.GetDirectoryName(failedPath)!);
                string failedPending=failedPath+".pending";
                File.WriteAllText(failedPending,JsonSerializer.Serialize(new
                {
                    format=2,workload="Home",homePresentationVerified=false,workloadChanged=true,
                    reason="The Home workload changed after its first verified presentation. This sample is invalid."
                }));
                File.Move(failedPending,failedPath,overwrite:true);
                _written=true;
                Console.WriteLine("[ui-perf] Home workload changed; diagnostic sample rejected");
            }
            return;
        }
        long now=Stopwatch.GetTimestamp();
        if(_firstPresented==0)
        {
            _firstPresented=now;_width=width;_height=height;_native=native;
            _coldMs=(DateTime.UtcNow-BenchmarkProcess.StartTime.ToUniversalTime()).TotalMilliseconds;
        }
        if(_sampleStarted==0)
        {
            if(Stopwatch.GetElapsedTime(_firstPresented,now).TotalSeconds<5)return;
            _sampleStarted=now;_sampleCpuTicks=BenchmarkProcess.TotalProcessorTime.Ticks;
            _allocationStart=GC.GetTotalAllocatedBytes(precise:false);
            _initialAxRequests=_axRequests;_initialAxReads=_axReads;_initialAxDecoded=_axDecoded;_initialAxReused=_axReused;_initialAxIdleSkips=_axIdleSkips;
            _initialDrawRequests=_drawRequests;_initialDrawCaptured=_drawCaptured;_initialDrawReused=_drawReused;return;
        }
        _frames++;
        FrameCosts.Add(_pendingFrameCost);_pendingFrameCost=0;
        double elapsed=Stopwatch.GetElapsedTime(_sampleStarted,now).TotalSeconds;
        if(elapsed<Duration)return;
        BenchmarkProcess.Refresh();
        double cpuSeconds=TimeSpan.FromTicks(BenchmarkProcess.TotalProcessorTime.Ticks-_sampleCpuTicks).TotalSeconds;
        var report=new
        {
            format=2,workload="Home",homePresentationVerified=true,mode=_native?"RmlUi":"Avalonia",platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),logicalProcessors=Environment.ProcessorCount,
            width=_width,height=_height,firstPresentationFromProcessStartMs=_coldMs,warmupSeconds=5,
            sampleSeconds=elapsed,frames=_frames,framesPerSecond=_frames/elapsed,
            processCpuSeconds=cpuSeconds,processCpuPercentOfOneCore=100*cpuSeconds/elapsed,
            processCpuPercentOfMachine=100*cpuSeconds/elapsed/Environment.ProcessorCount,
            totalUiFrame=Stats(FrameCosts),update=Stats(Updates),render=Stats(Draws),bitmapUpload=Stats(Uploads),phases=Phases.ToDictionary(p=>p.Key,p=>Stats(p.Value)),redraws=_redraws,
            redrawsPerSecond=_redraws/elapsed,bitmapUploads=_bitmapUploads,bitmapUploadBytes=_uploadBytes,
            managedAllocatedBytes=GC.GetTotalAllocatedBytes(precise:false)-_allocationStart,
            accessibilityCapture=new{requests=_axRequests-_initialAxRequests,nativeReads=_axReads-_initialAxReads,decoded=_axDecoded-_initialAxDecoded,reused=_axReused-_initialAxReused,idleSkips=_axIdleSkips-_initialAxIdleSkips},
            drawListCapture=new{requests=_drawRequests-_initialDrawRequests,captured=_drawCaptured-_initialDrawCaptured,reused=_drawReused-_initialDrawReused},
            processWorkingSetBytes=BenchmarkProcess.WorkingSet64,processPrivateBytes=BenchmarkProcess.PrivateMemorySize64>0?(long?)BenchmarkProcess.PrivateMemorySize64:null,
#if !MPHREAD_SERVER
            backend=GraphicsBackendPolicy.Resolved.ToString(),frameRateCap=FrameTiming.FrameRateCap,
            gpuResources=ModernGraphicsCompat.Active?(object)ModernGraphicsCompat.LiveResources:null,
#if MPHREAD_RMLUI_POC
            rmlGpuResources=ModernGraphicsCompat.Active?(object)ModernGraphicsCompat.RmlUiResources:null,
#endif
#endif
            scope="Verified Home UI update (including IME/accessibility) and native draw or Avalonia bitmap redraw/upload; full-process idle CPU includes the live scene and presentation. Explicit diagnostics disable account/presence start, authentication/HTTP, automatic updater requests, background startup maintenance and preview generation. IME and accessibility stay enabled.",
            storageNote="Tracked native resource capacity is not driver residency or total VRAM. GPU completion time is not inferred from CPU submission time."
        };
        string path=Path.GetFullPath(Output!);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending=path+".pending";
        File.WriteAllText(pending,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(pending,path,overwrite:true);
        _written=true;
    }
    private static object Stats(List<double> samples)
    {
        if(samples.Count==0)return new{count=0,meanMs=(double?)null,p50Ms=(double?)null,p95Ms=(double?)null,maxMs=(double?)null};
        double[] sorted=samples.Order().ToArray();
        return new{count=sorted.Length,meanMs=(double?)sorted.Average(),p50Ms=(double?)sorted[(int)Math.Ceiling(.5*sorted.Length)-1],p95Ms=(double?)sorted[(int)Math.Ceiling(.95*sorted.Length)-1],maxMs=(double?)sorted[^1]};
    }
}
