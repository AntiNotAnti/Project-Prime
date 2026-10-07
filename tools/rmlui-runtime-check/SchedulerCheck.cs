using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

static class SchedulerCheck
{
    public static void Run()
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
        Check(Marshal.SizeOf<RmlUiNativeUpdateState>()==40,"Update status ABI layout changed");
        var bridge=new FakeBridge{ScheduleSupported=true};using var host=new RmlUiHost(bridge);
        Check(host.Initialize(640,360,1,"."),"Schedule initialization");
        host.Update();Check(bridge.UpdateCount==1,"Initial dirty document must update");
        for(int i=0;i<1000;i++)host.Update();
        Check(bridge.UpdateCount==1&&host.UpdateMetrics.SkippedUpdates==1000,"Infinite genuine idle delay skips only native update");
        host.Render(640,360);Check(bridge.RenderCount==1,"An idle host still submits its overlay for scene composition");
        host.SetText(host.HomeDocument,"label","first");host.Update();Check(bridge.UpdateCount==2,"Binding mutation updates immediately");
        host.SetText(host.HomeDocument,"label","first");host.Update();Check(bridge.UpdateCount==2,"Unchanged cached binding does not dirty the context");
        host.EnqueueSnapshot(new(host.HomeDocument,1,new Dictionary<string,RmlUiBindingValue>{{"label",RmlUiBindingValue.FromText("worker")}}));
        host.Update();Check(bridge.UpdateCount==3&&bridge.Texts[(host.HomeDocument.DocumentId,"label")]=="worker","Queued worker mutation runs before scheduling decision");
        host.Input.PointerMoved(4,8);host.Update();Check(bridge.UpdateCount==4,"Native input mutation cannot be hidden by managed schedule cache");
        bridge.NextUpdateDelay=0;
        for(int i=0;i<10;i++)host.Update();
        Check(bridge.UpdateCount==14,"Rml animation zero delay retains every update");
        bridge.NextUpdateDelay=.1;host.Update();Check(bridge.UpdateCount==14,"Positive remaining caret/scroll deadline skips early work");
        bridge.NextUpdateDelay=0;host.Update();Check(bridge.UpdateCount==15,"Expired caret/scroll deadline updates immediately");
        bridge.NextUpdateDelay=double.PositiveInfinity;host.Resize(1280,720,2);host.Update();Check(bridge.UpdateCount==16,"Viewport change invalidates true idle");
        Check(Task.Run(()=>{try{host.TryGetUpdateState(out _);return false;}catch(InvalidOperationException){return true;}}).Result,"Status query remains owner-thread-only");
        var old=host.HomeDocument;Check(host.Reinitialize()&&host.HomeDocument!=old,"Reinitialization restores new schedule lifetime");
        host.Update();Check(bridge.UpdateCount==17&&host.TryGetUpdateState(out var state)&&state.Generation==host.HomeDocument.Generation,"Reinit first dirty update and state use live generation");
        var invalid=new Func<RmlUiNativeUpdateState,RmlUiNativeUpdateState>[] {
            s=>{s.Size=0;return s;},s=>{s.Version=0;return s;},s=>{s.Generation++;return s;},s=>{s.VisualRevision=0;return s;},
            s=>{s.Reserved=1;return s;},s=>{s.Flags=(RmlUiUpdateFlags)8;return s;},s=>{s.NextUpdateDelaySeconds=double.NaN;return s;},
            s=>{s.NextUpdateDelaySeconds=-1;return s;},s=>{s.NextUpdateDelaySeconds=double.NegativeInfinity;return s;}
        };
        foreach(var corrupt in invalid) {
            var native=new FakeBridge{ScheduleSupported=true,FilterUpdatePacket=corrupt};using var instance=new RmlUiHost(native);
            instance.Initialize(640,360,1,".");for(int i=0;i<3;i++)instance.Update();
            Check(native.UpdateCount==3&&native.StateQueryCount==1,"Invalid optional state falls back to eager native updates");
        }
        foreach(bool missing in new[]{false,true}) {
            var native=new FakeBridge{ScheduleMissing=missing};using var instance=new RmlUiHost(native);
            instance.Initialize(640,360,1,".");for(int i=0;i<3;i++)instance.Update();
            Check(native.UpdateCount==3&&native.StateQueryCount==1,"Old optional ABI is probed once and preserves updates");
        }
        var request=new RmlUiNativeUpdateState{Size=40,Version=0};
        Check(bridge.UpdateState(ref request)==0,"Optional state native request handshake rejects wrong version");
        Console.WriteLine($"PASS {checks} scheduled-update correctness checks");
    }
}
