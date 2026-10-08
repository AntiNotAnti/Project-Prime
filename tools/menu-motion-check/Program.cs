using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using G = MphRead.Mods.Render.GraphicsApi;

// Isolated real engine + native RmlUi capture, without joining or publishing a session.
if (args.Length < 4) throw new ArgumentException("Usage: menu-motion-check <output> <native bridge> <RmlUi assets> <paths.txt> [--benchmark-only]");
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
string fixture=Directory.CreateTempSubdirectory("prime-motion-").FullName;
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",fixture);
LauncherPrefs.Directory=GameFiles.Root=fixture;
Paths.UpdatePaths(args[3]); Paths.ChooseMphPath();
LauncherPrefs.DebugLogs=false;
nint module=NativeLibrary.Load(Path.GetFullPath(args[1]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
if(OperatingSystem.IsMacOS())NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly,(name,_,_)=>name=="libSkiaSharp"?NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"runtimes/osx/native/libSkiaSharp.dylib")):0);
OpenTK.Windowing.Desktop.GLFWProvider.EnsureInitialized();
GraphicsTimingPolicy.Enable();
GraphicsBackendPolicy.Configure("metal");
using var window=RenderWindow.Create(shell:true);
window.IsVisible=false;window.ClientSize=new(1280,720);DesktopGraphicsSession.Resize(window);ModernGraphicsCompat.SetVSync(false);
int width=window.FramebufferSize.X,height=window.FramebufferSize.Y;
using var host=new RmlUiHost();
if(!host.Initialize(width,height,width/1280f,Path.GetFullPath(args[2]),RmlUiRenderBackend.DrawList))throw new Exception("host");
using var pages=new RmlUiLauncherPages(host);
pages.SetText("player_name","JARRETT");pages.SetText("build_version","CINEMATIC REVIEW");pages.SetBool("reduce_motion",true);
var reader=new RmlUiDrawListReader();
// Reflection allows this exact harness to measure the pre-change assembly too.
Type? presentation=typeof(LauncherBackdropComposer).Assembly.GetType("MphRead.Mods.Launcher.LauncherPresentation");
object? motion=presentation?.GetProperty("Motion")?.GetValue(null);
MethodInfo? advance=motion?.GetType().GetMethod("Advance");
double time=0;
void Advance(Hunter hunter,double seconds,LauncherActivityAmbience activity)
{
 LauncherBackdrop.Set(activity.Name switch { "offline-battle"=>LauncherBackdropScene.Offline, "aim-lab"=>LauncherBackdropScene.Training, "adventure"=>LauncherBackdropScene.Adventure, "community-forge"=>LauncherBackdropScene.MapEditor, "studio-technical"=>LauncherBackdropScene.ReplayStudio, "live-lobby"=>LauncherBackdropScene.Lobby, _=>LauncherBackdropScene.Play });
 presentation?.GetProperty("Active")?.SetValue(null,true);
 for(int i=0;i<Math.Max(1,(int)(seconds*120));i++) { time+=1d/120;advance?.Invoke(motion,new object[]{time,true,LauncherPrefs.ReduceMotion,activity,hunter}); }
}
void Chamber(){G.Viewport(0,0,width,height);G.Disable(EnableCap.ScissorTest);G.ClearColor(0,0,0,1);G.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);LauncherBackdropComposer.Enabled=true;LauncherStageFx.Enabled=true;LauncherBackdropComposer.Draw(width,height);LauncherStageFx.DrawUnderHunter(width,height);}
void DrawHunter(int slot,Hunter hunter,int suit)
{
 var pad=LauncherLobbyFormation.At(Math.Max(0,slot));
 typeof(LauncherHunter).GetProperty("Formation")?.SetValue(null,slot>=0);
 LauncherHunter.PreviewSlot=slot;LauncherHunter.Hunter=hunter;LauncherHunter.Suit=suit;LauncherHunter.Wanted=true;LauncherHunter.TransparentBackground=true;LauncherHunter.CinematicLighting=true;
 LauncherHunter.Left=pad.HunterLeft;LauncherHunter.Right=pad.HunterRight;LauncherHunter.Top=pad.HunterTop;LauncherHunter.Bottom=pad.HunterBottom;LauncherHunter.DistanceScale=pad.HunterDistance;
 LauncherHunter.Draw(window,width,height);
 if(!LauncherHunter.Drawn)throw new Exception("Hunter preview failed: "+hunter);
}
void Save(string name)
{
 pages.Flush();host.Update();pages.AfterUpdate();host.Render(width,height);
 ModernGraphicsCompat.DrawRmlUi(reader.Capture(),width,height);
 byte[] rgb=FinalCompositeCapture.Read(width,height);
 using var bitmap=new SKBitmap(width,height,SKColorType.Bgra8888,SKAlphaType.Opaque);unsafe{byte* dst=(byte*)bitmap.GetPixels();for(int y=0;y<height;y++)for(int x=0;x<width;x++){int src=((height-1-y)*width+x)*3,d=(y*width+x)*4;dst[d]=rgb[src+2];dst[d+1]=rgb[src+1];dst[d+2]=rgb[src];dst[d+3]=255;}}
 using var file=File.Create(Path.Combine(output,name+".png"));bitmap.Encode(file,SKEncodedImageFormat.Png,100);
 ModernGraphicsCompat.Present();
}
pages.ShowBaseline();LauncherPrefs.ReduceMotion=false;LauncherHunter.Wanted=true;
// Identical isolated chamber+FX throughput workload before/after, excludes models/readback.
LauncherHunter.Hunter=Hunter.Samus;Advance(Hunter.Samus,1,LauncherActivityAmbience.ServerBrowser);
for(int i=0;i<20;i++){ModernGraphicsCompat.BeginGpuFrameTiming(i);Chamber();ModernGraphicsCompat.Present();}
while(ModernGraphicsCompat.TryTakeGpuFrameSample(out _)){}
var gpu=new List<double>(128);var durations=new List<double>(120);var presents=new List<double>(120);long bytes=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<120;i++){long started=Stopwatch.GetTimestamp();ModernGraphicsCompat.BeginGpuFrameTiming(i+20);Chamber();G.Finish();durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);started=Stopwatch.GetTimestamp();ModernGraphicsCompat.Present();presents.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);while(ModernGraphicsCompat.TryTakeGpuFrameSample(out var sample))if(sample.FrameId>=20)gpu.Add(sample.Milliseconds);}
long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;durations.Sort();gpu.Sort();presents.Sort();
File.WriteAllText(Path.Combine(output,"performance.txt"),$"{width}x{height} Metal {ModernGraphicsCompat.ActivePresentMode} chamber + FX serialized CPU + GPU completion (Finish); present separately\nmedian={durations[60]:F3}ms p95={durations[114]:F3}ms allocation={allocated/120}B/frame\nPresent median={presents[60]:F3}ms p95={presents[114]:F3}ms\nGPU timestamp samples={gpu.Count} median={(gpu.Count>0?gpu[gpu.Count/2]:double.NaN):F3}ms p95={(gpu.Count>0?gpu[(int)(gpu.Count*.95)]:double.NaN):F3}ms\n");
if(args.Contains("--benchmark-only")){Directory.Delete(fixture,true);Console.WriteLine("MENU MOTION BENCHMARK PASS "+output);return;}
foreach(Hunter hunter in Enum.GetValues<Hunter>().Where(h=>(int)h<7))
{
 LauncherHunter.Hunter=hunter;pages.SetText("hunter_name",hunter.ToString().ToUpperInvariant());
 Advance(hunter,1.5,LauncherActivityAmbience.ServerBrowser);Chamber();DrawHunter(-1,hunter,0);Save("home-"+hunter);
}
foreach(var ambience in new[]{LauncherActivityAmbience.ServerBrowser,LauncherActivityAmbience.OfflineBattle,LauncherActivityAmbience.AimLab,LauncherActivityAmbience.Adventure,LauncherActivityAmbience.Community,LauncherActivityAmbience.Studio})
{
 Advance(Hunter.Samus,1,ambience);Chamber();DrawHunter(-1,Hunter.Samus,0);Save("activity-"+ambience.Name);
}
LauncherLobbyVisuals.Active=true;pages.ShowBaseline(RmlUiMenuPage.Lobby);pages.SetText("lobby_name","FORMATION REVIEW");
pages.SetText("lobby_mode_name","BATTLE");pages.SetText("lobby_map","TRANSFER LOCK");pages.SetText("lobby_time","7:00");pages.SetText("lobby_score","20");
pages.SetText("lobby_format","AUTO");pages.SetText("lobby_local_hunter","SAMUS");pages.SetText("lobby_status","CONNECTED // LOCAL VISUAL FIXTURE");
pages.SetText("lobby_brief_title","READY TO DEPLOY");pages.SetText("lobby_brief_count","8 / 8 HUNTERS");pages.SetText("lobby_brief_detail","8 COMBATANTS // READY CHECK OFF");pages.SetBool("lobby_owner",true);pages.SetBool("lobby_can_start",true);
foreach(int count in new[]{1,4,8})
{
 LauncherLobbyVisuals.OccupiedMask=(byte)((1<<count)-1);pages.SetText("lobby_player_count",$"{count} / 8");pages.SetText("lobby_ready_count",$"{count-1} READY");pages.SetText("lobby_brief_count",$"{count} / 8 HUNTERS");
 for(int i=0;i<8;i++){var slot=LauncherLobbyFormation.At(i);host.SetLobbyAnchor(i,slot.LabelX,slot.LabelY);LauncherLobbyVisuals.SetHunter(i,(Hunter)(i%7));pages.SetBool($"slot{i}_occupied",i<count);pages.SetBool($"slot{i}_can_select",true);pages.SetText($"slot{i}_state",i==0?"WAITING":"READY");pages.SetBool($"slot{i}_ready",i>0&&i<count);pages.SetText($"slot{i}_name",i==0?"JARRETT":"BOT "+(i+1));pages.SetText($"slot{i}_hunter",((Hunter)(i%7)).ToString().ToUpperInvariant());}
 Advance(Hunter.Samus,1.5,LauncherActivityAmbience.Lobby);
 for(int frame=0;frame<3;frame++)
 {
  Advance(Hunter.Samus,.25,LauncherActivityAmbience.Lobby);Chamber();
  foreach(int i in new[]{7,5,6,3,4,1,2,0})if(i<count)DrawHunter(i,(Hunter)(i%7),i%4);
  Save($"lobby-{count}-t{frame}");
 }
}
// Roster/ready/countdown facts are fixtures; the engine render path is real.
void State(string property, object value) => typeof(LauncherLobbyVisuals).GetProperty(property)?.SetValue(null,value);
void FormationFrame(string name,double delta)
{
 Advance(Hunter.Samus,delta,LauncherActivityAmbience.Lobby);Chamber();
 foreach(int i in new[]{7,5,6,3,4,1,2,0}) DrawHunter(i,(Hunter)(i%7),i%4);
 Save(name);
}
for(int i=0;i<8;i++)pages.SetBool($"slot{i}_ready",false);
State("ReadyMask",(byte)0);FormationFrame("ready-before",.25);
for(int i=0;i<8;i++)pages.SetBool($"slot{i}_ready",true);
State("ReadyMask",(byte)255);FormationFrame("ready-pulse",.025);FormationFrame("ready-settled",.5);
pages.SetBool("lobby_starting",true);pages.SetText("lobby_countdown_number","3");pages.SetText("lobby_countdown_detail","MATCH STARTING");State("Starting",true);State("CountdownSeconds",3d);FormationFrame("launch-3",.2);
pages.SetText("lobby_countdown_number","1");State("CountdownSeconds",1d);FormationFrame("launch-1",.2);
pages.SetBool("lobby_starting",false);State("Starting",false);FormationFrame("launch-cancelled",.5);
LauncherLobbyVisuals.OccupiedMask=127;pages.SetBool("slot7_occupied",false);pages.SetText("slot7_name","OPEN SLOT");pages.SetText("slot7_hunter","+ ADD BOT");FormationFrame("departure-t0",.025);FormationFrame("departure-t1",.2);
LauncherLobbyVisuals.OccupiedMask=255;pages.SetBool("slot7_occupied",true);pages.SetText("slot7_name","BOT 8");pages.SetText("slot7_hunter","SAMUS");FormationFrame("arrival-t0",.025);FormationFrame("arrival-t1",.2);
foreach (var rearHunter in new[]{Hunter.Trace,Hunter.Noxus,Hunter.Spire})
{
 LauncherLobbyVisuals.SetHunter(7,rearHunter);pages.SetText("slot7_hunter",rearHunter.ToString().ToUpperInvariant());
 Advance(Hunter.Samus,.25,LauncherActivityAmbience.Lobby);Chamber();
 foreach(int i in new[]{7,5,6,3,4,1,2,0}) DrawHunter(i,i==7?rearHunter:(Hunter)(i%7),i%4);
 Save("rear-"+rearHunter);
}
LauncherPrefs.ReduceMotion=true;Advance(Hunter.Samus,1,LauncherActivityAmbience.Lobby);FormationFrame("reduced-motion",.1);
LauncherLobbyVisuals.Reset();LauncherHunter.Reset();
Directory.Delete(fixture,true);
Console.WriteLine("MENU MOTION VISUAL PASS "+output);
