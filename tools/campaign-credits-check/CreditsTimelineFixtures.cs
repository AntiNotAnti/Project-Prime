using System.Buffers.Binary;
using System.Collections;
using MphRead.Mods.Campaign;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using Timeline=MphRead.Mods.Campaign.CampaignCreditsTimeline;
using Frames=MphRead.Mods.Campaign.CampaignCreditsFrameTable;

// Independent invented page graph. No source assets, callbacks, wall clock,
// UI, models, audio, Scene, or campaign/save authority exists in these fixtures.
internal static class CreditsTimelineFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;FrameBinding();DelayAndSourceOrder();VisualAndSelfHide();TimersAndCallbacks();RetirementAndLimits();TerminalCallbacks();
        Console.WriteLine($"CREDITS TIMELINE PASS {checks} generated checks; explicit30Hz steps only, no presentation/save authority.");
    }
    private static void FrameBinding()
    {
        var script=new Script {AnimatedWidget=true};script.Pages[6].Items[0].States=new(){new(17,Widget:0)};
        var fixture=script.Build();var duration=Duration.Read(AnimationDurationFixtures.Build(first:new[]{2,3,4,5}).Bytes);
        var supplied=new Dictionary<string,Duration.Snapshot>{{AnimationPath,duration},{"unused.bin",duration}};
        var table=Frames.Bind(fixture.Document,supplied);
        Check(table.Frames.Count==1&&table.Frames[0]==5&&table.Animations.Count==1&&table.Animations[AnimationPath].Sha256==duration.Sha256,
            "frame binding retains exact required widget and normalized path, ignoring extra inventory");
        Check(table.MarmSha256==fixture.Document.MarmSha256&&table.TextSha256==fixture.Document.TextSha256,"frame binding pins both source identities");
        supplied.Clear();Check(table.Frames[0]==5&&table.Animations.Count==1,"caller inventory mutation cannot retarget duration capture");
        Expect<NotSupportedException>(()=>((IDictionary<int,int>)table.Frames).Clear(),"frame dictionary immutable");
        Expect<NotSupportedException>(()=>((IDictionary<string,Duration.Snapshot>)table.Animations).Clear(),"animation dictionary immutable");
        Expect<InvalidDataException>(()=>Frames.Bind(fixture.Document,new Dictionary<string,Duration.Snapshot>()),"required animation cannot default to still1");
        Expect<InvalidDataException>(()=>Frames.Bind(fixture.Document,new Dictionary<string,Duration.Snapshot>{{AnimationPath,null!}}),"null duration inventory rejected");
        var insensitive=new Dictionary<string,Duration.Snapshot>(StringComparer.OrdinalIgnoreCase){{AnimationPath.ToUpperInvariant(),duration}};
        Expect<InvalidDataException>(()=>Frames.Bind(fixture.Document,insensitive),"caller comparer cannot change exact ordinal source path identity");
        Expect<InvalidDataException>(()=>Frames.Bind(fixture.Document,new Dictionary<string,Duration.Snapshot>{{AnimationPath.Replace('/','\\'),duration}}),"unadmitted backslash dictionary alias cannot replace normalized source key");
        var many=Enumerable.Range(0,128).ToDictionary(i=>"unused"+i,d=>duration);many.Add(AnimationPath,duration);
        Expect<InvalidDataException>(()=>Frames.Bind(fixture.Document,many),"all inventory entries count toward bounded128 admission");
        many.Remove("unused0");Check(Frames.Bind(fixture.Document,many).Animations.Count==1,"exact128 inventory admitted while only referenced duration retained");
        var differentMarm=(byte[])fixture.Marm.Clone();differentMarm[16]^=1;
        Expect<InvalidDataException>(()=>new Timeline(Source.Read(differentMarm,fixture.Text),table,true),"timeline rejects wrong MARM identity despite equivalent widget graph");
        var differentText=(byte[])fixture.Text.Clone();differentText[^1]^=1;
        Expect<InvalidDataException>(()=>new Timeline(Source.Read(fixture.Marm,differentText),table,true),"timeline rejects wrong text identity despite equivalent MARM");
        using(var same=new Timeline(Source.Read(fixture.Marm,fixture.Text),table,true))
            Check(same.Snapshot().Page==6&&!same.Snapshot().Closed,"identical detached document bytes can use same immutable table");
        var still=new Script();still.Pages[6].Items[0].States=new(){new(17,Widget:0)};
        var stillDoc=still.Build().Document;Check(Frames.Bind(stillDoc,new Dictionary<string,Duration.Snapshot>()).Frames[0]==1,"genuinely missing animation is explicit still1");
    }
    private static void DelayAndSourceOrder()
    {
        var script=new Script();var page=script.Pages[6];page.Items[0]=new(){Delay=2048,States=new(){new(17,2048),new(2),new(10,4096)}};
        page.Timers.Add(new(2048,new(TargetPage:7)));
        using var timeline=Create(script);var first=timeline.Snapshot();
        Check(first.Page==6&&first.PageFrame==0&&first.SettledFrame==-1&&first.Items[0] is {Started:false,Code:1,Frame:0,DelayFx:2048},"half-tick item delay is not rounded early at Enter");
        Check(timeline.Tick(),"first bounded timeline tick active");var one=timeline.Snapshot();
        Check(one.PageFrame==1&&one.SettledFrame==-1&&one.Items[0] is {Started:true,Code:17,Frame:0},"newly due item starts after ordered item loop and remains transition frame0");
        timeline.Tick();var two=timeline.Snapshot();
        Check(two.Items[0] is {State:2,Code:2,Frame:0}&&two.SettledFrame==0&&two.PendingPage==null,"fractional transition arrives on next tick and establishes settlement zero");
        timeline.Tick();var three=timeline.Snapshot();
        Check(three.Page==6&&three.PendingPage==7&&three.SettledFrame==1&&three.Items[0] is {Code:10,Frame:0},"fractional timer fires settlement plus1, retaining old page during exit");
        timeline.Tick();Check(timeline.Snapshot().Page==7&&timeline.Snapshot().PageFrame==0&&timeline.Snapshot().SettledFrame==-1,"pending destination enters only after old exit and resets clocks");
        Check(first.Items[0].Started==false&&first.PageFrame==0,"prior snapshot detached from later timeline steps");
        Check(first.Items is IList {IsReadOnly:true},"snapshot item list immutable");
        Expect<NotSupportedException>(()=>((IList)first.Items).Clear(),"snapshot item clearing refused");
        foreach(bool earlier in new[]{false,true})
        {
            var ordered=new Script();var source=new Item{States=new(){new(17,4096),new(2)},Actions=new(){new(Arrival:2,TargetItem:earlier?0:1,TargetCode:2)}};
            var target=new Item{Delay=100*4096,States=new(){new(17,8192),new(2)}};
            ordered.Pages[6].Items=earlier?new(){target,source}:new(){source,target};
            using var run=Create(ordered);run.Tick();var item=run.Snapshot().Items[earlier?0:1];
            Check(item.Started&&item.Code==17&&item.Frame==(earlier?0:1)&&item.DelayFx==0,"synchronous arrival action starts target with exact source-order same-tick advancement");
        }
        var negative=new Script();negative.Pages[6].Items[0].Delay=0;negative.Pages[6].Items[0].States=new(){new(2)};
        using var immediate=Create(negative);Check(immediate.Snapshot().Items[0] is {Started:true,State:2,Code:2,Frame:0},"zero delay and missing entrance arrive synchronously at Enter");
    }
    private static void VisualAndSelfHide()
    {
        var self=new Script();self.Pages[6].Items[0]=new(){States=new(){new(17,4096),new(2,10*4096),new(10,8192)},Actions=new(){new(Arrival:2,TargetItem:0,TargetCode:10)}};
        using(var run=Create(self))
        {
            run.Tick();Check(run.Snapshot().Items[0] is {State:1,Code:10,Frame:0},"arrival-triggered self-hide transition survives return from outer Arrive");
            run.Tick();Check(run.Snapshot().Items[0] is {Code:10,Frame:1},"idle text duration does not invent a hold before source self-hide");
            run.Tick();Check(run.Snapshot().Items[0] is {State:1,Code:1,Frame:0,Visual:null}&&run.Snapshot().SettledFrame==0,"self-hide finishes once without replaying Idle arrival action");
        }
        var widget=new Script();widget.Pages[6].Items[0].States=new(){new(17,Widget:0)};
        using(var run=Create(widget))
        {
            Check(run.Snapshot().Items[0] is {Code:17,Frame:0,Visual:not null},"still widget entrance lasts minimum1 even without animation");
            run.Tick();Check(run.Snapshot().Items[0] is {Code:2,Visual:null},"missing steady visual disappears rather than freezing prior entrance");
        }
        var zero=new Script();zero.Pages[6].Items[0].States=new(){new(17,0)};
        using(var run=Create(zero))Check(run.Snapshot().Items[0] is {Code:2,Visual:null},"zero-duration text transition arrives immediately and does not borrow a steady visual");
        var none=new Script();none.Pages[6].Items[0].States=new(){new(1)};
        using(var run=Create(none)){Check(run.Snapshot().Items[0].Visual==null,"fully absent visual remains absent");run.Tick();Check(run.Snapshot().SettledFrame==0,"absent visuals still permit page settlement");}
    }
    private static void TimersAndCallbacks()
    {
        var ordered=new Script();ordered.Pages[6].Timers=new(){new(0,new(TargetPage:7)),new(0,new(TargetPage:8))};
        using(var run=Create(ordered))
        {Check(run.Snapshot().PendingPage==null,"timer0 does not fire inside Enter");run.Tick();Check(run.Snapshot().PendingPage==8&&run.Snapshot().Page==6,"equal timer actions continue in source order after first pending page");run.Tick();Check(run.Snapshot().Page==8,"last ordered pending destination owns eventual entrance");}
        var clock=new Script();clock.Pages[6].Items[0].States=new(){new(2),new(10,3*4096)};
        clock.Pages[6].Timers=new(){new(4096,new(TargetItem:0,TargetCode:1)),new(8192,new(TargetPage:7))};
        using(var run=Create(clock)){run.Tick();run.Tick();Check(run.Snapshot().SettledFrame==1&&run.Snapshot().Items[0].Code==10,"later transition starts after settled clock1");run.Tick();Check(run.Snapshot().SettledFrame==2&&run.Snapshot().PendingPage==7,"already settled timer clock continues through later transition");}
        foreach(bool good in new[]{true,false})
        {
            var branch=BranchScript();using var run=Create(branch,good);
            Check(!run.InvokeItemAction(6,0,0)&&!run.TryTakeReturn(),"credits item activation cannot manufacture results return");
            run.Tick();Check(run.Snapshot().Page==6&&run.Snapshot().PendingPage==12,"source page navigation remains pending even with absent exits");
            Check(!run.InvokeItemAction(6,0,0),"pending exit ignores explicit item input");run.Tick();Check(run.Snapshot().Page==12,"first navigation enters page12");
            run.Tick();Check(run.Snapshot().PendingPage==(good?49:50)&&run.Snapshot().Page==12,"callback12 chooses immutable ending branch through normal pending exit");
            run.Tick();int result=good?49:50;Check(run.Snapshot().Page==result,"chosen results page eventually enters");
            Check(!run.InvokeItemAction(12,0,1)&&!run.InvokeItemAction(result,-1,1)&&!run.InvokeItemAction(result,0,99)&&!run.InvokeItemAction(result,0,0),"stale page, invalid item/action and non-kind1 activation refused");
            Check(run.InvokeItemAction(result,0,1),"explicit authored results kind1 action accepted");
            Check(run.TryTakeReturn()&&!run.TryTakeReturn(),"callback206 produces exactly one consumable return intent");
            Check(!run.Tick()&&!run.InvokeItemAction(result,0,1),"terminal return prevents additional timeline mutation");
        }
        var noOp=new Script();noOp.Pages[6].Timers.Add(new(0,new(Callback:11)));
        using(var run=Create(noOp)){for(int i=0;i<8;i++)run.Tick();Check(run.Snapshot().Page==6&&run.Snapshot().PendingPage==null&&!run.TryTakeReturn(),"ending callback11 is no navigation/reset/return");}
        var ordering=new Script();ordering.Pages[6].Timers.Add(new(0,new(Callback:12,TargetPage:9)));
        using(var run=Create(ordering)){run.Tick();Check(run.Snapshot().PendingPage==9,"explicit action page change follows callback12 destination request");}
    }
    private static void RetirementAndLimits()
    {
        using(var run=Create(new Script()))
        {
            var errors=new List<Exception?>();
            System.Action[] operations={()=>run.Snapshot(),()=>run.Tick(),()=>run.InvokeItemAction(49,0,0),()=>run.TryTakeReturn(),()=>run.Retire(),()=>run.Dispose()};
            var worker=new Thread(()=>{foreach(var operation in operations){try{operation();errors.Add(null);}catch(Exception error){errors.Add(error);}}}){IsBackground=true};
            worker.Start();Check(worker.Join(TimeSpan.FromSeconds(5)),"wrong-thread guard checks complete without dispatching owner work");
            Check(errors.Count==operations.Length&&errors.All(error=>error is InvalidOperationException),"every public timeline access rejects a different thread");
            Check(!run.Snapshot().Closed&&run.Snapshot().PageFrame==0&&run.Tick(),"wrong-thread calls leave owner timeline unchanged and usable");
        }
        using(var run=Create(new Script())){var old=run.Snapshot();run.Retire();Check(run.Snapshot().Closed&&!run.Tick()&&!run.TryTakeReturn()&&!run.InvokeItemAction(6,0,0),"retired owner accepts no steps/actions/return");Check(!old.Closed,"pre-retirement snapshot remains detached");run.Retire();run.Dispose();Check(run.Snapshot().Closed,"retirement/disposal idempotent");}
        var cycle=new Script();cycle.Pages[6].Items[0]=new(){States=new(){new(2),new(3)},Actions=new(){new(Arrival:2,TargetItem:0,TargetCode:3),new(Arrival:3,TargetItem:0,TargetCode:2)}};
        using(var run=Create(cycle))Check(run.Snapshot().Closed&&!string.IsNullOrEmpty(run.Snapshot().Failure)&&!run.Tick()&&!run.TryTakeReturn(),"synchronous constructor cycle closes with bounded failure rather than stack overflow or silent omission");
        var retiredReturn=Create(BranchScript());using(retiredReturn)
        {for(int i=0;i<4;i++)retiredReturn.Tick();Check(retiredReturn.InvokeItemAction(49,0,1),"retirement fixture first produces authored return intent");retiredReturn.Retire();Check(!retiredReturn.TryTakeReturn()&&!retiredReturn.Tick(),"retirement clears even an already pending return intent");}
        var timeout=new Script();timeout.Pages[6].Items[0].States=new(){new(2),new(10,122*4096)};timeout.Pages[6].Timers.Add(new(0,new(TargetPage:7)));
        using(var run=Create(timeout)){for(int i=0;i<130&&!run.Snapshot().Closed;i++)run.Tick();Check(run.Snapshot().Closed&&run.Snapshot().Page==6&&!string.IsNullOrEmpty(run.Snapshot().Failure),"exit timeout is explicit failure, not claimed successful animation completion");}
        foreach(int which in new[]{0,1,2})
        {
            var huge=new Script();if(which==0)huge.Pages[6].Items[0].Delay=18000*4096+1;
            if(which==1)huge.Pages[6].Items[0].States=new(){new(17,18000*4096+1)};
            if(which==2)huge.Pages[6].Timers.Add(new(18000*4096+1,new()));
            Expect<InvalidDataException>(()=>Create(huge),"timeline preflight caps delay/text/timer extents to18000ticks");
        }
        var outside=new Script();outside.Pages[6].Timers.Add(new(0,new(TargetPage:1)));
        Expect<InvalidDataException>(()=>Create(outside),"timeline refuses valid global page outside selected profile before execution");
        var longRun=BranchScript();using(var run=Create(longRun))
        {
            for(int i=0;i<4;i++)run.Tick();Check(run.Snapshot().Page==49,"long-lived synthetic results reached");
            for(int i=0;i<20001;i++)run.Tick();
            long frame=run.Snapshot().Items[0].Frame;Check(frame>18000&&!run.Snapshot().Closed,"steady item age is long-lived rather than clamped to animation bound");
        }
        foreach(int samples in new[]{2,4,5})
        {
            using var control=Create(new Script());using var observed=Create(new Script());
            for(int tick=0;tick<120;tick++){control.Tick();for(int sample=0;sample<samples;sample++)observed.Snapshot();observed.Tick();}
            Check(control.Snapshot().PageFrame==observed.Snapshot().PageFrame&&control.Snapshot().Items[0].Frame==observed.Snapshot().Items[0].Frame,
                "host sampling frequency never advances explicit timeline clock");
        }
        AtomicEnterFailure();
    }

    private static void AtomicEnterFailure()
    {
        var script=new Script();script.Pages[6].Items=Enumerable.Range(0,64).Select(index=>
        {
            // A real one-tick entrance prevents the source's justPlayed=1
            // fallback from firing Hidden-arrival actions during Enter itself.
            var item=new Item{States=new(){new(17,Widget:0),new(2),new(10,Widget:0)}};
            for(int action=0;action<4;action++)item.Actions.Add(new(Arrival:1,Callbacks:Enumerable.Repeat(11,13).ToArray()));
            if(index<22)item.Actions.Add(new(Arrival:1,Callback:11));
            return item;
        }).ToList();
        script.Pages[6].Timers.Add(new(0,new(TargetPage:7)));
        script.Pages[7].Items=Enumerable.Range(0,64).Select(_=>new Item()).ToList();
        using var run=Create(script);run.Tick();
        Check(run.Snapshot().PendingPage==7&&run.Snapshot().Items.Count==64,"bounded exit-work fixture has complete old page before destination initialization");
        Check(!run.Tick(),"near-limit exit callbacks terminate destination Enter within deterministic operation budget");
        var final=run.Snapshot();
        Check(final.Closed&&!string.IsNullOrEmpty(final.Failure)&&final.Page==6&&final.Items.Count==64&&final.Items.All(i=>i!=null),
            "failed Enter preserves complete old graph and readable closed snapshot rather than publishing null destination slots");
        Check(!run.Tick()&&!run.TryTakeReturn(),"failed publication remains terminal without return intent");
    }

    private static Script BranchScript()
    {
        var script=new Script();script.Pages[6].Timers.Add(new(0,new(TargetPage:12)));script.Pages[12].Timers.Add(new(0,new(Callback:12)));
        foreach(int page in new[]{49,50})script.Pages[page].Items[0].Actions=new(){new(),new(Kind:1,Callback:206)};
        return script;
    }
    private static void TerminalCallbacks()
    {
        var script=BranchScript();script.Pages[49].Items[0]=new(){States=new(){new(17,4096),new(2)},
            Actions=new(){new(Arrival:1,Callback:206),new(Kind:1,Callbacks:new[]{12,12})}};
        using var run=Create(script);for(int i=0;i<5;i++)run.Tick();
        Check(run.Snapshot().Page==49&&run.Snapshot().Items[0] is {State:2,Code:2}&&!run.Snapshot().Closed,
            "terminal callback fixture reaches visible results without triggering hidden arrival at entrance");
        Check(run.InvokeItemAction(49,0,1),"explicit results action enters callback sequence");
        Check(run.Snapshot().Closed&&run.Snapshot().PendingPage==null&&run.Snapshot().Failure==null,
            "nested hidden-arrival return terminates remaining callbacks without restoring a pending page");
        Check(run.TryTakeReturn()&&!run.TryTakeReturn()&&!run.Tick(),"nested return remains one consumable terminal intent");
    }
    private static Timeline Create(Script script,bool good=true)
    {var f=script.Build();var inventory=new Dictionary<string,Duration.Snapshot>();if(script.AnimatedWidget)inventory.Add(AnimationPath,Duration.Read(AnimationDurationFixtures.Build().Bytes));return new(f.Document,Frames.Bind(f.Document,inventory),good);}
    private const string AnimationPath="gameoptions/invented_Anim.bin";
    private sealed record State(int Code,int Duration=0,int? Widget=null);
    private sealed record Action(int Kind=0,int Arrival=0,int TargetItem=65535,int TargetCode=0,int TargetPage=255,int Callback=0,int[]? Callbacks=null);
    private sealed record Timer(int Delay,Action Action);
    private sealed class Item
    {internal int Delay;internal int Initial=2;internal List<State> States=new(){new(2)};internal List<Action> Actions=new();}
    private sealed class Page
    {internal List<Item> Items=new(){new()};internal List<Action> Actions=new();internal List<Timer> Timers=new();}
    private sealed record Fixture(byte[] Marm,byte[] Text,Source.Document Document);
    private sealed class Script
    {
        internal bool AnimatedWidget;
        internal Dictionary<int,Page> Pages=new[]{6,7,8,9,10,11,12,49,50}.ToDictionary(i=>i,_=>new Page());
        internal Fixture Build()
        {
            var m=new Bytes();var t=new Bytes();m.Reserve(20);m.Copy(0,"MARM"u8.ToArray());m.Byte(6,12);m.Byte(7,51);
            int pages=m.Reserve(52*4),widgets=m.Reserve(8),unusedPage=m.Reserve(32);m.Word(8,(uint)pages);m.Word(12,(uint)widgets);
            for(int i=0;i<51;i++)m.Word(pages+i*4,(uint)unusedPage);
            t.Reserve(8);int entry=t.Reserve(12),text=t.Reserve(1);t.Byte(text,(byte)'T');t.Word(0,(uint)entry);t.Word(entry,(uint)text);t.Word(entry+4,(uint)text);t.Half(entry+8,1);t.Half(entry+10,1);
            int Call(Action action)
            {int[] calls=action.Callbacks??(action.Callback==0?Array.Empty<int>():new[]{action.Callback});return m.List(calls.Select(code=>{int record=m.Reserve(8);m.Word(record+4,(uint)code);return record;}).ToArray());}
            int EmitAction(Action action)
            {int at=m.Reserve(24);m.Half(at,(ushort)action.Kind);m.Byte(at+2,(byte)action.Arrival);m.Word(at+16,(uint)Call(action));m.Half(at+20,(ushort)action.TargetItem);m.Byte(at+22,(byte)action.TargetPage);m.Byte(at+23,(byte)action.TargetCode);return at;}
            bool usesWidget=false;
            foreach(var pair in Pages.OrderBy(p=>p.Key))
            {
                int page=m.Reserve(32);m.Word(pages+4*pair.Key,(uint)page);m.Byte(page+28,(byte)pair.Key);m.Half(page+20,65535);m.Half(page+22,65535);m.Half(page+24,65535);m.Half(page+26,(ushort)pair.Value.Items.Count);
                var items=new List<int>();
                for(int ordinal=0;ordinal<pair.Value.Items.Count;ordinal++)
                {
                    var spec=pair.Value.Items[ordinal];int item=m.Reserve(60);items.Add(item);m.Word(item+20,(uint)spec.Delay);m.Byte(item+52,(byte)spec.Initial);m.Half(item+54,(ushort)ordinal);
                    var states=new List<int>();
                    foreach(var s in spec.States)
                    {
                        int state=m.Reserve(8);states.Add(state);m.Byte(state+1,(byte)s.Code);m.Half(state+2,0xbbbb);
                        if(s.Widget.HasValue){usesWidget=true;m.Word(state+4,(uint)s.Widget.Value);}
                        else{m.Byte(state,1);int binding=m.Reserve(8),style=m.Reserve(24);m.Word(binding+4,(uint)style);m.Word(style+12,(uint)s.Duration);m.Word(state+4,(uint)binding);}
                    }
                    m.Word(item+12,(uint)m.List(states.ToArray()));m.Word(item+4,(uint)m.List(spec.Actions.Select(EmitAction).ToArray()));
                }
                m.Word(page+8,(uint)m.List(items.ToArray()));m.Word(page+12,(uint)m.List(pair.Value.Actions.Select(EmitAction).ToArray()));
                var timers=new List<int>();foreach(var timer in pair.Value.Timers){int at=m.Reserve(32);timers.Add(at);m.Word(at,(uint)timer.Delay);int action=EmitAction(timer.Action);m.Copy(at+4,m.Data.Skip(action).Take(24).ToArray());}
                m.Word(page+16,(uint)m.List(timers.ToArray()));
            }
            if(usesWidget)
            {
                int File(string path,int kind){int name=m.Add(System.Text.Encoding.ASCII.GetBytes(path+'\0'));int file=m.Reserve(12);m.Byte(file,(byte)kind);m.Byte(file+1,(byte)(path.Length+1));m.Word(file+8,(uint)name);return file;}
                int model=File("gameoptions/invented_Model.bin",7);int[] files=AnimatedWidget?new[]{model,File(AnimationPath,8)}:new[]{model};
                int widget=m.Reserve(12);m.Word(widget+8,(uint)m.List(files));m.Word(widgets,(uint)widget);
            }
            byte[] marm=m.Data.ToArray(),localized=t.Data.ToArray();return new(marm,localized,Source.Read(marm,localized));
        }
    }
    private sealed class Bytes
    {
        internal List<byte> Data=new();internal int Reserve(int count){int at=Data.Count;Data.AddRange(new byte[count]);return at;}
        internal int Add(byte[] bytes){int at=Data.Count;Data.AddRange(bytes);return at;}
        internal void Byte(int at,byte value)=>Data[at]=value;
        internal void Word(int at,uint value){for(int i=0;i<4;i++)Data[at+i]=(byte)(value>>(8*i));}
        internal void Half(int at,ushort value){Data[at]=(byte)value;Data[at+1]=(byte)(value>>8);}
        internal void Copy(int at,byte[] bytes){for(int i=0;i<bytes.Length;i++)Data[at+i]=bytes[i];}
        internal int List(params int[] values){if(values.Length==0)return 0;int at=Reserve((values.Length+1)*4);for(int i=0;i<values.Length;i++)Word(at+4*i,(uint)values[i]);return at;}
    }
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Timeline fixture: "+label);}
    private static void Expect<T>(System.Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Timeline expected "+typeof(T).Name+": "+label);}
}
