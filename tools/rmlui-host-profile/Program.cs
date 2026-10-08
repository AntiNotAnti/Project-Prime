using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

if(args.Length!=1)throw new ArgumentException("Usage: rmlui-host-profile <native-bridge>");
var module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
using var host=new RmlUiHost();
if(!host.Initialize(1280,720,1,Path.Combine(AppContext.BaseDirectory,"rmlui"),RmlUiRenderBackend.DrawList))throw new InvalidOperationException("Real host initialization failed");
using var pages=new RmlUiLauncherPages(host);
pages.ShowBaseline();
var accessibility=new RmlUiAccessibilityService();
var phaseBytes = new long[5];
void Frame(double[][]? times,int index) {
    long bytes=GC.GetAllocatedBytesForCurrentThread();
    long start=Stopwatch.GetTimestamp();pages.Flush();if(times!=null){times[0][index]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;phaseBytes[0]+=GC.GetAllocatedBytesForCurrentThread()-bytes;}
    bytes=GC.GetAllocatedBytesForCurrentThread();
    start=Stopwatch.GetTimestamp();host.Update();if(times!=null){times[1][index]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;phaseBytes[1]+=GC.GetAllocatedBytesForCurrentThread()-bytes;}
    bytes=GC.GetAllocatedBytesForCurrentThread();
    start=Stopwatch.GetTimestamp();pages.AfterUpdate();if(times!=null){times[2][index]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;phaseBytes[2]+=GC.GetAllocatedBytesForCurrentThread()-bytes;}
    bytes=GC.GetAllocatedBytesForCurrentThread();
    start=Stopwatch.GetTimestamp();accessibility.Capture(host);if(times!=null){times[3][index]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;phaseBytes[3]+=GC.GetAllocatedBytesForCurrentThread()-bytes;}
    bytes=GC.GetAllocatedBytesForCurrentThread();
    start=Stopwatch.GetTimestamp();host.Render(1280,720);if(times!=null){times[4][index]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;phaseBytes[4]+=GC.GetAllocatedBytesForCurrentThread()-bytes;}
}
for(int i=0;i<50;i++){Frame(null,0);Thread.Sleep(10);} // Let real entry animations finish.
const int Samples=1000;
var samples=Enumerable.Range(0,5).Select(_=>new double[Samples]).ToArray();
var before=host.UpdateMetrics;var axBefore=accessibility.CaptureMetrics;
long allocated=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<Samples;i++)Frame(samples,i);
long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
var after=host.UpdateMetrics;var axAfter=accessibility.CaptureMetrics;
object Stats(double[] values){Array.Sort(values);return new{meanMs=values.Average(),p95Ms=values[949],maxMs=values[^1]};}
Console.WriteLine(JsonSerializer.Serialize(new {
    fixture="Actual composed Home + retained legacy home, native draw-list backend. Tight-loop owner CPU subphase profile; no GPU/engine scene/process comparison.",
    samples=Samples,phases=new{bindings=Stats(samples[0]),contextUpdate=Stats(samples[1]),focus=Stats(samples[2]),semantics=Stats(samples[3]),contextRender=Stats(samples[4])},
    managedBytes=bytes,nativeUpdates=after.NativeUpdates-before.NativeUpdates,skippedUpdates=after.SkippedUpdates-before.SkippedUpdates,
    allocatedBytesByPhase=new{bindings=phaseBytes[0],contextUpdate=phaseBytes[1],focus=phaseBytes[2],semantics=phaseBytes[3],contextRender=phaseBytes[4]},
    nativeSemanticReads=axAfter.NativeReads-axBefore.NativeReads,decodedSemanticSnapshots=axAfter.DecodedSnapshots-axBefore.DecodedSnapshots,
    reusedSemanticSnapshots=axAfter.ReusedSnapshots-axBefore.ReusedSnapshots,
    idleSemanticSkips=axAfter.IdleSkips-axBefore.IdleSkips,
    optionalUpdateState=host.TryGetUpdateState(out var state)?state:(RmlUiUpdateState?)null
},new JsonSerializerOptions{WriteIndented=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals}));
