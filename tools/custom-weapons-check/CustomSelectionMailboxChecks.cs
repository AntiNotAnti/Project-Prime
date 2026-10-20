using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

internal static class CustomSelectionMailboxChecks
{
    private sealed class Sink : ICustomWeaponInputSink
    {
        internal bool Accept;internal int Attempts;internal uint Sequence;
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {Attempts++;sequence=0;if(!Accept)return false;sequence=++Sequence;return true;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var definition=RiftlineM7.Create();var manifest=new CustomWeaponManifest(new[]{definition}).Digest;
        var bootstrap=new WorldBootstrapIdentity(new(7,9,4),5,2,100,6,8);
        var context=new CustomWeaponSelectionContext(11,new(7,9,0,2,3),bootstrap,manifest,definition.Identity);
        var initial=new CustomWeaponSelectionState(context,true,true,false,false,false);
        var box=new CustomWeaponSelectionMailbox();
        check(box.Snapshot==null&&!box.TryRequest(null!,true,0)&&!box.TryTake(true,false,false,0,out _),"unpublished selection mailbox cannot emit desired input");
        box.Publish(initial);var first=box.Snapshot!;
        check(first.Context==context&&first.CanEquip&&!first.CanReturnNative&&!first.NativePending&&!first.Selected,
            "immutable selection snapshot exposes explicit owner context and native-selected affordance");
        box.Publish(initial);check(ReferenceEquals(first,box.Snapshot),"identical fixed-step publication preserves snapshot reference identity");
        check(!box.TryRequest(new(initial),true,0),"lookalike value snapshot cannot replace exact published reference");
        check(!box.TryRequest(first,false,0),"already native selection is a no-op without queued desired change");
        for(int i=0;i<1000;i++)if(!box.TryRequest(first,true,0))throw new Exception("repeat selection admission");
        check(box.TryRequest(first,false,0)&&box.TryTake(true,false,false,0,out bool native)&&!native
            &&!box.TryTake(true,false,false,0,out _),"capacity-one desired mailbox coalesces repeated clicks to final native target and drains once");
        check(box.TryRequest(first,true,0)&&box.TryTake(true,false,false,2000,out bool custom)&&custom,
            "selection deadline is inclusive at exact2000ms boundary");
        check(box.TryRequest(first,true,0)&&!box.TryTake(true,false,false,2001,out _)
            &&!box.TryTake(true,false,false,2001,out _),"expired selection is consumed without delayed resubmission");
        check(box.TryRequest(first,true,0)&&!box.TryTake(false,true,false,1500,out _)
            &&box.TryTake(true,false,false,1999,out custom)&&custom,"only asynchronous menu resume may hold eligible request inside bounded deadline");
        check(box.TryRequest(first,true,0)&&!box.TryTake(false,true,false,2001,out _)
            &&!box.TryTake(true,false,false,2001,out _),"menu transition cannot extend fixed desired-selection lifetime");
        check(box.TryRequest(first,true,0)&&!box.TryTake(false,false,false,1,out _)
            &&!box.TryTake(true,false,false,2,out _),"non-menu input block consumes queued choice instead of retrying after thaw");
        check(box.TryRequest(first,true,0)&&!box.TryTake(true,false,true,1,out _)
            &&!box.TryTake(true,false,false,2,out _),"actual native selection intent wins and consumes simultaneous UI choice");
        foreach(var restricted in new[]{initial with{Available=false},initial with{ActorReady=false}})
        {
            box.Publish(initial);var view=box.Snapshot!;box.TryRequest(view,true,0);box.Publish(restricted);
            check(!box.TryRequest(box.Snapshot!,true,0)&&!box.TryTake(true,true,false,1,out _),
                "readiness or actor restriction clears existing and new desired input");
            box.Publish(initial);check(!box.TryTake(true,false,false,1,out _),"same-context recovery cannot rearm retired selection");
        }
        var selected=initial with{Selected=true,NativeSuppressed=true};box.Publish(selected);var selectedView=box.Snapshot!;
        check(!selectedView.CanEquip&&selectedView.CanReturnNative&&!box.TryRequest(selectedView,true,0)
            &&box.TryRequest(selectedView,false,0),"custom-selected snapshot permits one native return and refuses redundant Equip");
        box.Publish(selected with{Selected=false,NativePending=true});
        check(box.Snapshot is {NativePending:true,CanReturnNative:false}
            &&!box.TryRequest(selectedView,false,0)&&box.TryTake(true,false,false,1,out native)&&!native,
            "pending native state exposes pending fence, rejects stale reference and preserves already queued desired value");
        var changes=new[]{context with{SceneGeneration=12},context with{Owner=new(8,9,0,2,3)},context with{Owner=new(7,10,0,2,3)},
            context with{Owner=new(7,9,1,2,3)},context with{Owner=new(7,9,0,3,3)},context with{Owner=new(7,9,0,2,4)},
            context with{Bootstrap=bootstrap with{Start=new(8,9,4)}},context with{Bootstrap=bootstrap with{Start=new(7,10,4)}},
            context with{Bootstrap=bootstrap with{Start=new(7,9,5)}},context with{Bootstrap=bootstrap with{Revision=6}},
            context with{Bootstrap=bootstrap with{SlotGeneration=3}},context with{Bootstrap=bootstrap with{AuthorityFrame=101}},
            context with{Bootstrap=bootstrap with{SlowRevision=7}},context with{Bootstrap=bootstrap with{WorldRevision=9}},
            context with{Manifest=new(1,2,3,4)},context with{Weapon=WeaponIdentity.Custom(Guid.Parse("11111111-1111-1111-1111-111111111111"),new string('a',64))}};
        foreach(var replacement in changes)
        {
            box.Publish(initial);var old=box.Snapshot!;box.TryRequest(old,true,0);box.Publish(initial with{Context=replacement});
            check(!box.TryRequest(old,true,0)&&!box.TryTake(true,false,false,1,out _),
                "every full context component independently retires old UI request: "+replacement);
        }
        foreach(var invalid in new[]{default(CustomWeaponSelectionContext),context with{SceneGeneration=0},context with{Owner=default},
            context with{Manifest=default},context with{Weapon=default},context with{Weapon=WeaponIdentity.Native(BeamType.PowerBeam)}})
        {
            box.Publish(initial);var old=box.Snapshot!;box.TryRequest(old,true,0);box.Publish(initial with{Context=invalid});
            check(box.Snapshot==null&&!box.TryRequest(old,true,0)&&!box.TryTake(true,false,false,1,out _),
                "invalid selection context clears publication and pending choice");
        }
        box.Publish(initial);first=box.Snapshot!;
        bool stable=true;for(int i=0;i<10000;i++){box.Publish(initial);stable&=ReferenceEquals(box.Snapshot,first);}
        long allocation=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){box.Publish(initial);stable&=ReferenceEquals(box.Snapshot,first);}
        allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
        check(stable&&allocation==0,"100000 unchanged fixed-step publications and snapshot reads allocate zero bytes");
        var controller=new CustomWeaponInputController();var sink=new Sink();
        controller.Synchronize(new(context.Owner,false,0,0,0,0,context.Bootstrap,context.Manifest));
        box.TryRequest(first,true,0);
        bool consumed=box.TryTake(true,false,false,0,out bool wanted);
        controller.Sample(new(true,consumed&&wanted,false,false,false,false,false,false),sink);
        check(consumed&&sink.Attempts==1&&!controller.Selected&&!controller.SuppressNative,
            "queued desired selection can be consumed by full sink without predicting accepted Equip or native permission change");
        for(int i=0;i<100;i++)controller.Sample(new(true,false,false,false,false,false,false,false),sink);
        check(sink.Attempts==1&&!box.TryTake(true,false,false,1,out _),
            "full sink refusal never retries automatically on later100 input samples");
        sink.Accept=true;box.TryRequest(first,true,2);consumed=box.TryTake(true,false,false,2,out wanted);
        controller.Sample(new(true,consumed&&wanted,false,false,false,false,false,false),sink);
        check(sink.Attempts==2&&sink.Sequence==1&&controller.Selected&&controller.SuppressNative,
            "fresh explicit desired retry alone may issue first accepted control through sink");
        box.TryRequest(first,true,0);box.Clear();
        check(box.Snapshot==null&&!box.TryRequest(first,true,0)&&!box.TryTake(true,false,false,1,out _),
            "Clear retires immutable view and queued desired state together");
    }
}
