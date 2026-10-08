using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Input;

int checks=0;
void Check(bool value,string message){if(!value)throw new InvalidOperationException("HUD contract: "+message);checks++;}
var original=HudProfileDefaults.Create("Project Prime");
int generation=HudProfiles.Generation;
var backend=new HudBoundary();
using(var controller=new HudEditorController(original,backend))
{
    var first=controller.Snapshot();
    Check(ReferenceEquals(first,controller.Snapshot()),"unchanged profile snapshot retains revision");
    controller.SelectElement(1);controller.Dispatch(HudEditorAction.NudgeRight);
    Check(original.Elements["core.health"].OffsetX==11.25f&&HudProfiles.Generation==generation,"detached edits never mutate original or publish active HUD");
    Check(first.Elements[1].Bounds==new HudEditorController(original,backend).Snapshot().Elements[1].Bounds,"captured geometry stays immutable");
    controller.Dispatch(HudEditorAction.Undo);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==11.25f,"undo restores real profile data");
    controller.Dispatch(HudEditorAction.Redo);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==12.25f,"redo preserves authored element movement");
    controller.Dispatch(HudEditorAction.ToggleLock);controller.Nudge(50,50);controller.Resize(1);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==12.25f&&controller.CopyDraft().Elements["core.health"].Scale==1,"locked elements reject gesture movement and resize");
    controller.Dispatch(HudEditorAction.ToggleLock);
    string before=controller.Snapshot().ProfileJson;
    Check(!controller.Dispatch(HudEditorAction.ApplyLayout,x:"NaN",y:"0",scale:"1",opacity:"1",color:"#FFFFFF")&&controller.Snapshot().ProfileJson==before,
        "invalid finite layout values reject atomically");
    Check(!controller.Dispatch(HudEditorAction.ApplyLayout,x:"0",y:"0",scale:"1",opacity:"1",color:"<rml>")&&controller.Snapshot().ProfileJson==before,
        "invalid color cannot change the profile");
    Check(controller.Dispatch(HudEditorAction.ApplyLayout,x:"123",y:"-180",scale:"1.25",opacity:".8",color:"#00FFAA")
        &&controller.CopyDraft().Elements["core.health"].OffsetX==123,"all element layout fields apply through HUD validation");
    controller.SetCanvasSize(640,360);var rect=controller.Bounds(1);float x=rect.Center.X,y=rect.Center.Y;
    float offset=controller.CopyDraft().Elements["core.health"].OffsetX;
    Check(controller.PointerDown(1,x,y)&&controller.Snapshot().Selected==1,"pointer hit uses shared HUD bounds");
    controller.HandleControllerAxes(new(null, default, 0), 100);
    Check(controller.PointerMove(1,x+12,y,shift:true),"disconnected gamepad poll preserves mouse drag");
    controller.HandleControllerAxes(new("pad", new GamepadState { Connected=true }, 1), 116);
    Check(controller.PointerMove(1,x+24,y,shift:true),"gamepad connection change preserves mouse drag");
    controller.PointerUp(1);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==offset+72,"physical drag maps to logical HUD units");
    controller.Dispatch(HudEditorAction.Undo);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==offset,"multi-frame drag commits one undo step");
    controller.SelectElement(1);controller.SelectElement(2,group:true);
    float previousX=controller.CopyDraft().Elements["core.health"].OffsetX;
    rect=controller.Bounds(2);controller.PointerDown(1,rect.Center.X,rect.Center.Y);controller.PointerMove(1,rect.Center.X+9,rect.Center.Y,shift:true);controller.PointerUp(1);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==previousX+27,"group drag preserves shared logical delta");
    controller.Dispatch(HudEditorAction.AlignLeft);
    Check(Math.Abs(controller.Bounds(1).X-controller.Bounds(2).X)<.01,"group alignment uses actual anchor geometry");
    controller.SelectElement(0);var crosshair=controller.Bounds(0);float centered=crosshair.Center.X;
    controller.PointerDown(1,centered,crosshair.Center.Y);controller.PointerMove(1,centered+100,crosshair.Center.Y);controller.PointerUp(1);
    Check(controller.Bounds(0).Center.X==centered,"aim center stays fixed even under pointer drag");
    controller.Dispatch(HudEditorAction.NextCrosshairTarget);controller.Dispatch(HudEditorAction.ToggleCrosshairOverride);
    Check(controller.CopyDraft().WeaponCrosshairs[0]?.OverrideProperties?.Length==0,"new weapon override initially inherits unchanged default properties");
    bool found=false;
    for(int pages=0;pages<40&&!found;pages++)
    {
        var snapshot=controller.Snapshot();
        for(int i=0;i<snapshot.Properties.Length;i++)if(snapshot.Properties[i].Path=="/crosshair/gap")
        {controller.SelectProperty(i);found=true;break;}
        if(!found)controller.Dispatch(HudEditorAction.PropertyNext);
    }
    Check(found&&controller.Dispatch(HudEditorAction.PropertyApply,"7"),"full crosshair property registry remains reachable");
    Check(controller.CopyDraft().WeaponCrosshairs[0]!.Gap==7&&controller.CopyDraft().WeaponCrosshairs[0]!.OverrideProperties!.Contains("Gap")
        &&controller.CopyDraft().Crosshair.Gap!=7,"weapon edits mark exact override and leave default crosshair unchanged");
    controller.Dispatch(HudEditorAction.PropertyReset);
    Check(!controller.CopyDraft().WeaponCrosshairs[0]!.OverrideProperties!.Contains("Gap"),"reset override restores future inheritance");
    controller.Dispatch(HudEditorAction.ShareCrosshair);string code=controller.Snapshot().JsonText;
    Check(code.StartsWith("PPCH1:")&&controller.Dispatch(HudEditorAction.ImportCrosshair,code),"actual crosshair share/import codec round trips");
    Check(!controller.Dispatch(HudEditorAction.ImportJson,"{\"schemaVersion\":99}")&&controller.Snapshot().Error.Contains("Unsupported HUD profile version"),"unsupported schema keeps detached draft and explains failure");
    controller.Dispatch(HudEditorAction.ExportJson);var exported=HudProfileStore.Parse(controller.Snapshot().JsonText);
    Check(exported.Elements.Count==14&&exported.WeaponCrosshairs[0]!=null,"profile export preserves all elements and override data");
    controller.Dispatch(HudEditorAction.SaveNamed,name:"λ Prime");
    Check(backend.Saved?.Name=="λ Prime"&&HudProfiles.Generation==generation,"named save persists detached library profile without activation");
    controller.Dispatch(HudEditorAction.Use,name:"Project Prime");
    Check(controller.TryTakeAccepted(out var result)&&result.Name=="Project Prime (Custom)"&&!controller.TryTakeAccepted(out _),"settings receives validated profile handoff once with reserved preset label distinguished");
    Check(HudProfiles.Generation==generation&&original.WeaponCrosshairs[0]==null,"settings handoff still waits for Settings Apply");
}
using(var controller=new HudEditorController(original,backend))
{
    controller.SelectElement(1);controller.Nudge(10,10);controller.Dispatch(HudEditorAction.Cancel);
    Check(controller.TryTakeCancelled()&&!controller.TryTakeCancelled()&&!controller.TryTakeAccepted(out _),"cancel hands off no profile and is consumed once");
    Check(Task.Run(()=>{try{controller.Nudge(1,1);}catch(InvalidOperationException){return true;}return false;}).GetAwaiter().GetResult(),"worker cannot mutate engine-thread HUD draft");
}
using(var controller=new HudEditorController(original,backend))
{
    controller.SelectElement(1);float before=controller.CopyDraft().Elements["core.health"].OffsetX;
    var held=new GamepadState{Connected=true,LeftX=1,Name="Contract pad"};
    controller.HandleControllerAxes(new("pad",held,1),0);controller.HandleControllerAxes(new("pad",held,1),16);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==before,"held gamepad input is quarantined when editor ownership begins");
    controller.HandleControllerAxes(new("pad",new GamepadState{Connected=true,Name="Contract pad"},1),32);
    controller.HandleControllerAxes(new("pad",held,1),82);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==before+9,"gamepad motion uses existing deadzone, bounded time and authoring speed");
    controller.HandleControllerAxes(new("pad",new GamepadState{Connected=true,Name="Contract pad"},1),98);
    controller.Dispatch(HudEditorAction.Undo);
    Check(controller.CopyDraft().Elements["core.health"].OffsetX==before,"continuous gamepad gesture commits one history step");
    controller.HandleController(UiAction.Accept,canvasFocused:true);controller.HandleController(UiAction.Back,canvasFocused:true);
    Check(!controller.Snapshot().Closed&&!controller.TryTakeCancelled(),"gamepad Back leaves move mode before cancelling the document");
}
using(var controller=new HudEditorController(original,backend))
{
    controller.SelectElement(1);controller.Dispatch(HudEditorAction.ApplyLayout,x:"200",y:"-100",scale:"2",opacity:".6",color:"#FFFFFF");
    void Property(string path)
    {
        for(int page=0;page<50;page++)
        {
            var snapshot=controller.Snapshot();
            for(int row=0;row<snapshot.Properties.Length;row++)if(snapshot.Properties[row].Path==path){controller.SelectProperty(row);return;}
            controller.Dispatch(HudEditorAction.PropertyNext);
        }
        throw new InvalidOperationException("Property unavailable "+path);
    }
    Property("/independentGauges");
    Check(controller.Dispatch(HudEditorAction.PropertyApply,"true")&&controller.CopyDraft().Elements["core.healthGauge"].OffsetX==222.5f
        &&controller.CopyDraft().Elements["core.healthGauge"].Scale==2&&controller.CopyDraft().Elements["core.healthGauge"].Opacity==.6f,
        "independent gauge editing uses existing detach authority and preserves parent appearance");
    Property("/elements/core.health/contexts");
    Check(controller.Dispatch(HudEditorAction.PropertyApply,"Playing,Replay")&&controller.CopyDraft().Elements["core.health"].Contexts==(HudContext.Playing|HudContext.Replay),
        "context flags preserve arbitrary valid combinations independent of spacing");
}
Console.WriteLine($"HUD CONTRACT PASS {checks} checks.");
#if MPHREAD_RMLUI_POC
if(args.Length==3&&args[0]=="--native")NativeHudCheck.Run(args[1],args[2]);
#endif
internal sealed class HudBoundary:IHudEditorBackend
{
    public string? Warning=>null;
    public HudProfile? Saved;
    public void SaveNamed(string name,HudProfile profile){Saved=profile.DeepClone();Saved.Name=name;}
    public HudProfile LoadNamed(string name)=>Saved?.DeepClone()??throw new FileNotFoundException("Named HUD profile missing.");
}
