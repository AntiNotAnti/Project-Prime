using System.Numerics;
using MphRead;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;

int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Near(float a, float b, string message) => Check(MathF.Abs(a-b)<.001f, message);
void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is FormatException or ArgumentException or System.Text.Json.JsonException) { assertions++; return; } throw new Exception(message); }
foreach (var (w,h) in new[] { (1280,720), (1920,1080), (2560,1440), (3440,1440), (3840,2160), (1080,1920), (1920,1200), (1440,1080) })
foreach (HudAnchor anchor in Enum.GetValues<HudAnchor>())
{
    var t=new HudTransform(w,h,.02f); var p=t.Resolve(anchor,Vector2.Zero);
    Near(p.X,w*(.02f+.96f*((int)anchor%3)/2),"horizontal anchor");
    Near(p.Y,h*(.02f+.96f*((int)anchor/3)/2),"vertical anchor");
    Vector2 offset=new(32,-48); var restored=t.OffsetFor(anchor,t.Resolve(anchor,offset));
    Near(restored.X,offset.X,"inverse x"); Near(restored.Y,offset.Y,"inverse y");
}
foreach (CrosshairDotShape shape in Enum.GetValues<CrosshairDotShape>())
{
    var dot = new CrosshairRuntime(new CrosshairProfile { Inner=false,Dot=true,DotShape=shape,DotSize=10,Outline=2,OutlineColor="#00FFFF",OutlineOpacity=.4f });
    Check(dot.Dot.Length>0 || dot.Bars.Length>0,"dot geometry exists");
    Near(dot.OutlineColor.Y,1,"outline color"); Near(dot.OutlineOpacity,.4f,"outline opacity");
    if(shape is CrosshairDotShape.Circle or CrosshairDotShape.Diamond)
    { Near(dot.Dot[1].X,5,"dot radius"); Check(dot.OutlineDot[1].X>dot.Dot[1].X,"outline surrounds polygon"); }
}
Span<HudRectPrimitive> meterPrimitives=stackalloc HudRectPrimitive[3];
Check(HudPrimitives.Meter(meterPrimitives,2,16,40,3,.5f,new(0,1,0,1),false)==3,"meter primitive count");
Near(meterPrimitives[2].Width,20,"half horizontal meter");
HudPrimitives.Meter(meterPrimitives,2,16,40,3,.25f,new(0,1,0,1),true);
Near(meterPrimitives[2].Height,10,"quarter vertical meter"); Near(meterPrimitives[2].Y,6,"vertical meter grows upward");
Check(HudPrimitives.Meter(meterPrimitives,2,16,40,3,0,new(0,1,0,1),false)==2,"empty meter omits fill");
var accessibilityLimits=HudProfileStore.Parse("""{"textScale":999,"iconScale":-1,"notifications":{"maxVisible":99,"spacing":999},"elements":{"core.health":{"contexts":255}}}""");
Check(accessibilityLimits.TextScale==3 && accessibilityLimits.IconScale==.5f,"accessibility scale bounds");
Check(accessibilityLimits.Notifications.MaxVisible==5 && accessibilityLimits.Notifications.Spacing==32,"notification bounds");
Check(accessibilityLimits.Elements["core.health"].Contexts==HudContext.All,"context flags bounded");
Check(!HudProfileDefaults.Create("Project Prime").Elements["combat.opponent"].Enabled,"custom opponent information opt-in");
var inheritedCrosshair=new CrosshairProfile { Color="#00FFFF",HealthColor=false,Dot=true,DotSize=4 };
var sparseCrosshair=new CrosshairProfile { DotSize=2,OverrideProperties=new[]{nameof(CrosshairProfile.DotSize)} };
var sparseResolved=CrosshairProperties.Resolve(inheritedCrosshair,sparseCrosshair);
Check(sparseResolved.DotSize==2 && sparseResolved.Color=="#00FFFF","sparse override inherits untouched color");
inheritedCrosshair.Color="#FF00FF";
Check(CrosshairProperties.Resolve(inheritedCrosshair,sparseCrosshair).Color=="#FF00FF","default edits flow into sparse overrides");
var dotDescriptor=CrosshairProperties.All.Single(d=>d.Id==nameof(CrosshairProfile.DotSize));
dotDescriptor.Reset(sparseCrosshair,inheritedCrosshair);
Check(sparseCrosshair.OverrideProperties!.Length==0,"reset property resumes inheritance");
dotDescriptor.Set(sparseCrosshair,7f);
Check(CrosshairProperties.Resolve(inheritedCrosshair,sparseCrosshair).DotSize==7,"descriptor edits mark overridden property");
var multiPart=new CrosshairRuntime(new CrosshairProfile { Dot=true,Inner=true,DotStyle=new() { CustomColor=true,Color="#FF0000",Opacity=.4f,Outline=3 } });
Check(multiPart.Parts.Length==2,"independent parts compiled");
Near(multiPart.Parts[0].Color.X,1,"dot part red"); Near(multiPart.Parts[0].Color.Y,0,"dot part not default");
Near(multiPart.Parts[0].Opacity,.4f,"part opacity"); Near(multiPart.Parts[0].Outline,3,"part outline");
Check(multiPart.Parts[1].HealthColor,"other part retains health color");
var detachable=HudProfileDefaults.Create("Project Prime");
detachable.Elements["core.health"].OffsetX=100; detachable.Elements["core.health"].Scale=2;
detachable.DetachGauges();
Near(detachable.Elements["core.healthGauge"].OffsetX,122.5f,"detached gauge preserves horizontal position");
Near(detachable.Elements["core.healthGauge"].OffsetY,56.25f,"detached gauge preserves vertical position");
Check(detachable.IndependentGauges,"gauges independently enabled");
Span<HudShapePrimitive> radarPrimitives=stackalloc HudShapePrimitive[8];
var radarStyle=new HudRadarRuntime(new HudRadarProfile());
Check(HudRadarGeometry.Build(radarPrimitives,radarStyle,100,true,true,new(0,0,0,.5f),Vector4.One,Vector4.One,1)==5,"basic radar frame primitive count");
Near(radarPrimitives[2].Radius,55,"radar inner ring");
Check(HudRadarGeometry.Build(radarPrimitives,new(new HudRadarProfile { Style=HudRadarStyle.Minimal }),100,true,true,Vector4.One,Vector4.One,Vector4.One,1)==0,"minimal radar suppresses frame");
Check(HudRadarGeometry.Build(radarPrimitives,new(new HudRadarProfile { Style=HudRadarStyle.Square }),100,true,true,Vector4.One,Vector4.One,Vector4.One,1)==5,"square radar backing and edges");
var nativeProfile=HudProfileDefaults.Create("Project Prime");nativeProfile.Mode=HudMode.Custom;
nativeProfile.Crosshair.Native=true;nativeProfile.Health.Native=true;nativeProfile.Ammo.Native=true;nativeProfile.Inventory.Native=true;
nativeProfile.GlobalScale=1.25f;nativeProfile.Crosshair.Scale=1.5f;
foreach(string id in new[]{"core.health","core.ammo","core.weapons","core.crosshair"}) nativeProfile.Elements[id].Scale=2;
var nativeRuntime=new HudRuntimeProfile(HudProfileStore.Parse(HudProfileStore.Serialize(nativeProfile)));
Check(nativeRuntime.Crosshair.Native && nativeRuntime.Health.Native && nativeRuntime.Ammo.Native && nativeRuntime.Inventory.Native,"native selections persist and resolve");
Near(nativeRuntime.Crosshair.NativeScale,1.5f,"native crosshair size persists");
foreach(int index in new[]{0,1,2,3}) Near(nativeRuntime[index].Scale,2.5f,"native element scale includes global scale");
var accessible=HudProfileDefaults.Create("Accessibility");
var healthLayout=accessible.Elements["core.health"];
var accessibleTransform=new HudTransform(1440,600);
float accessibleY=accessibleTransform.Resolve(healthLayout.Anchor,new(healthLayout.OffsetX,healthLayout.OffsetY)).Y;
Check(accessibleY + 112.5f*accessible.GlobalScale*accessibleTransform.UnitScale <= 600,"accessibility health stays on screen");
foreach (CrosshairStyle style in Enum.GetValues<CrosshairStyle>())
foreach (CrosshairSize size in Enum.GetValues<CrosshairSize>())
{
    var runtime = new CrosshairRuntime(CrosshairProfile.FromLegacy(style,size));
    var legacy = Crosshair.BarsOf(style,Crosshair.ScaleOf(size));
    Check(runtime.Bars.Length==legacy.Count,"legacy geometry count");
    for (int i=0;i<legacy.Count;i++)
    {
        // A set comparison permits the equivalent arm ordering to change.
        bool found=false;
        foreach (var bar in runtime.Bars)
            if (MathF.Abs(bar.X-legacy[i].X)<.0001f && MathF.Abs(bar.Y-legacy[i].Y)<.0001f && MathF.Abs(bar.Width-legacy[i].Width)<.0001f && MathF.Abs(bar.Height-legacy[i].Height)<.0001f) found=true;
        Check(found,"legacy geometry match");
    }
    var (radius,thickness)=Crosshair.RingOf(style,Crosshair.ScaleOf(size));
    Check((runtime.Ring.Length>0)==(thickness>0),"legacy ring presence");
    if (thickness>0) { Near(runtime.Ring[0].X,radius+thickness/2,"ring outside"); Near(runtime.Ring[1].X,radius-thickness/2,"ring inside"); }
}
for (int i=0;i<CrosshairPresets.Names.Length;i++)
{
    var preset=CrosshairPresets.Create(i); var imported=CrosshairPresets.Import(CrosshairPresets.Share(preset));
    Check(CrosshairPresets.Share(preset)==CrosshairPresets.Share(imported),"crosshair sharing roundtrip");
}
Reject(()=>CrosshairPresets.Import("PPCH9:abc"),"unknown share version");
var profile = HudProfileStore.Parse("""{"unknown":"ignored","elements":{"core.radar":{"opacity":0.4}},"crosshair":{"dot":true}}""");
Near(HudProfileStore.Parse("{\"basePreset\":\"Accessibility\"}").GlobalScale,1.5f,"base preset inheritance");
Check(profile.Elements["core.radar"].Anchor==HudAnchor.TopRight,"missing anchor inherits per-element default");
Near(profile.Elements["core.radar"].Opacity,.4f,"explicit property wins");
Check(profile.Crosshair.Dot && profile.Crosshair.Inner,"partial crosshair");
Check(HudProfileStore.Serialize(profile)==HudProfileStore.Serialize(profile.DeepClone()),"roundtrip");
var clone = profile.DeepClone(); clone.Elements["core.health"].OffsetX=123; Check(profile.Elements["core.health"].OffsetX!=123,"draft detached");
profile.GlobalScale=float.NaN; profile.GlobalOpacity=9; profile.SafeArea=-10; profile.Crosshair.Segments=10000; profile.Validate();
Near(profile.GlobalScale,1,"nonfinite clamps"); Near(profile.GlobalOpacity,1,"opacity clamps"); Near(profile.SafeArea,0,"safearea clamps"); Check(profile.Crosshair.Segments==128,"segments bounded");
Reject(()=>HudProfileStore.Parse("{"),"bad JSON accepted"); Reject(()=>HudProfileStore.Parse("{\"schemaVersion\":99}"),"future schema accepted");
Reject(()=>HudProfileStore.Parse(new string(' ',HudProfileStore.MaximumBytes+1)),"oversized JSON accepted");
profile=HudProfileStore.Parse("""{"elements":null,"crosshair":null,"weaponCrosshairs":null,"name":null} """); Check(profile.Elements.Count==HudProfileDefaults.ElementIds.Length,"null recovery");
Features.ProHud=false; Features.ProHudFixedWeapon=false; Features.ReticleOpacity=.3f; Radar.Enabled=false; Radar.ShowBackground=true; Radar.ShowOutlines=false; Features.KillFeedEnabled=false;
Crosshair.Style=CrosshairStyle.Brackets; Crosshair.Size=CrosshairSize.Big;
profile=HudProfileMigration.FromLegacy(); Check(profile.Mode==HudMode.Classic && !profile.FixedWeapon && !profile.Elements["core.radar"].Enabled && profile.RadarBackground && !profile.RadarOutlines && !profile.Elements["combat.killFeed"].Enabled,"migration"); Near(profile.NativeReticleOpacity,.3f,"reticle migration"); Check(profile.Crosshair.Brackets,"crosshair migration");
profile.Crosshair.Scale=8; profile.ResetElement("core.crosshair"); Near(profile.Crosshair.Scale,1,"reset restores crosshair appearance");
var history=new HudStudioHistory(profile); string before=history.Capture();
history.Draft.Elements["core.radar"].OffsetX=100; history.Draft.Elements["core.radar"].OffsetX=200; history.Commit(before);
Check(history.CanUndo,"drag undo"); history.Undo(); Check(history.Capture()==before && !history.CanUndo,"drag coalesced"); history.Redo(); Near(history.Draft.Elements["core.radar"].OffsetX,200,"redo"); history.Undo(); history.Edit(p=>p.SafeArea=.1f); Check(!history.CanRedo,"branch clears redo");
for(int i=0;i<180;i++) history.Edit(p=>p.Elements["core.radar"].OffsetX=i);
int undos=0; while(history.CanUndo){ history.Undo(); undos++; } Check(undos==150,"history bounded");
profile=new(); profile.WeaponCrosshairs[4]=new(){Dot=true,Inner=false}; profile.ZoomCrosshair=new(){Ring=true,Inner=false};
var resolved=new HudRuntimeProfile(profile); Check(resolved.CrosshairFor(4,false).Bars.Length==1,"weapon override"); Check(resolved.CrosshairFor(0,false).Bars.Length==4,"default inheritance"); Check(resolved.CrosshairFor(4,true).Ring.Length>0,"zoom override");
profile.Crosshair.Enabled=false; Check(resolved.Crosshair.Enabled,"runtime detached");
var tstyle=new CrosshairRuntime(new(){TStyle=true}); Check(tstyle.Bars.Length==3,"T arms");
string directory=Path.Combine(Path.GetTempPath(),"prime-hud-check-"+Guid.NewGuid().ToString("N"));
try
{
    var store=new HudProfileStore(directory); profile=new(){Name="first"}; store.Save("active",profile); profile.Name="second"; store.Save("active",profile);
    Check(store.Load("active").Name=="second","save load"); File.WriteAllText(Path.Combine(directory,"active.json"),"broken"); Check(store.Load("active").Name=="first","backup fallback");
    profile.Name="recovered"; store.Save("active",profile); Check(HudProfileStore.Parse(File.ReadAllText(Path.Combine(directory,"active.json.bak"))).Name=="first","preserve valid backup on repair");
    foreach(var name in new[]{"../outside","a/b","a\\b","C:thing","","CON","LPT1","name ","name\n"}) Reject(()=>store.Save(name,profile),"path escape");
    HudProfiles.Load(directory); var current=HudProfiles.CopyCurrent(); var edit=new HudStudioHistory(current); edit.Edit(p=>p.Crosshair.Scale=8); Check(HudProfiles.CopyCurrent().Crosshair.Scale==current.Crosshair.Scale,"cancel isolation");
    Reject(()=>HudProfiles.SaveNamed("active",profile),"reserved active library name");
    int generation=HudProfiles.Generation; HudProfiles.Save(edit.Draft); Check(HudProfiles.Generation==generation+1,"publication generation");
    // Warm up, then exercise the exact span traversal consumed by the renderer.
    float sum=0;
    for(int i=0;i<1000;i++) foreach(var bar in resolved.Crosshair.Bars) sum+=bar.Width;
    long allocated=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<100000;i++) { var c=resolved.CrosshairFor(4,false); foreach(var part in c.Parts) { foreach(var bar in part.Bars) sum+=bar.Width; foreach(var vertex in part.Dot) sum+=vertex.X; foreach(var vertex in part.Ring) sum+=vertex.Y; } var point=new HudTransform(3440,1440).Resolve(resolved[1].Anchor,resolved[1].Offset); sum+=point.X; }
    long delta=GC.GetAllocatedBytesForCurrentThread()-allocated;
    Check(delta==0,"runtime allocates: "+delta); GC.KeepAlive(sum);
}
finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
assertions += RadarChecks.Run();
Console.WriteLine($"HUD checks passed: {assertions} assertions; runtime traversal allocated 0 bytes.");
