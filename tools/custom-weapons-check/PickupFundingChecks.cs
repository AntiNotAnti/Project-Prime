using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using Fixture = SourceJournalChecks.Fixture;

// Trusted-contact funding is exercised directly against the source journal.
// The supplied source oracle/emitter are deterministic stubs, not world contacts,
// live spawn activation, collision, client delivery or historical engine proof.
internal static class PickupFundingChecks
{
    private static readonly WeaponRuntimeId Weapon = new(10);
    private static CustomPickupItemKey Item(Fixture f, ushort spawn = 1, ulong incarnation = 1)
        => new(f.PickupWorld!.Value,spawn,incarnation);
    private static bool Fund(Fixture f, ushort spawn, ulong incarnation, int amount, out WeaponStateSnapshot view)
        => f.Journal.TryFundPickup(Item(f,spawn,incarnation),f.Journal.Owner,Weapon,
            f.Journal.ObservedSourceFrame,f.Now,amount,out view);
    private static (ulong Revision,int Funds,uint Observed,uint Committed,uint Settled,int Pending,WeaponRuntimeId Selected,bool Owned,WeaponStateSnapshot State) Capture(Fixture f)
    {
        bool owned=f.Journal.TryCommitted(Weapon,out var state);
        return (f.Journal.PickupRevision,f.Journal.PendingPickupFundingCount,f.Journal.ObservedSourceFrame,
            f.Journal.CommittedSourceFrame,f.Journal.SettledSequence,f.Journal.PendingCount,f.Journal.Selected,owned,state);
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;
        void Verify(bool pass,string label) { check(pass,"pickup funding: "+label);count++; }
        FirstOwnership(Verify); SourceOrder(Verify); AdmissionAndInterruption(Verify);
        InvalidAndCapacity(Verify); WrappedSource(Verify); ProjectParity(Verify); Allocation(Verify);
        Console.WriteLine($"Pickup funding foundation passed: {count} assertions; trusted-contact/source-oracle fixtures, no live world activation claim.");
    }
    private static void FirstOwnership(Action<bool,string> check)
    {
        using var f=new Fixture(loadout:Array.Empty<WeaponRuntimeId>(),pickupFunding:true);
        check(!f.Journal.Selected.IsValid&&!f.Journal.TryCommitted(Weapon,out _)
            &&!f.Journal.TryPreview(Weapon,out _)&&f.Journal.SealNativeSelection()
            &&f.Journal.TryReleaseNativeSelection(f.Journal.Owner,100),
            "empty life seals and releases native permission without custom inventory");
        f.Offer(f.Action(1,100),100);
        f.Offer(f.Action(2,100,CustomWeaponActionKind.Equip),100);
        check(Fund(f,1,1,16,out var initial)&&initial.Tick==101&&initial.Magazine==4&&initial.Reserve==16
            &&initial.Phase==CustomWeaponPhase.Unequipped&&f.Journal.PickupRevision==1
            &&f.Journal.PendingPickupFundingCount==0&&!f.Journal.Selected.IsValid,
            "first pickup funds next-source 4/16 unequipped without action sequence or selection");
        check(!f.Journal.TryPreview(Weapon,out _)&&!f.Journal.TryCommitted(Weapon,out _)
            &&f.Journal.CommittedSourceFrame==100&&f.Journal.SettledSequence==0,
            "first funding stays hidden until effective source and cannot advance committed source");
        check(f.Observe(101,101),"next source observes pickup ownership boundary");
        check(f.Take(101,check,"pre-acquisition Fire settles").Disposition==CustomJournalDisposition.NotOwned
            &&f.Take(101,check,"pre-acquisition Equip settles").Disposition==CustomJournalDisposition.NotOwned
            &&f.Engine.Calls==0&&f.Oracle.Calls==0&&!f.Journal.Selected.IsValid,
            "already queued old actions cannot borrow later ownership or emit");
        check(f.Journal.TryReleaseNativeSelection(f.Journal.Owner,100)
            &&f.Journal.ReadNativeSelection(f.Journal.Owner,100)==CustomNativeSelection.Native,
            "pickup preserves native fire permission already released on contact source");
        check(f.Journal.TryPreview(Weapon,out var visible)&&visible.Magazine==4&&visible.Reserve==16
            &&visible.Phase==CustomWeaponPhase.Unequipped&&visible.Tick==101,
            "observed next source exposes owned resources without auto-equipping");
        f.Offer(f.Action(3,101,CustomWeaponActionKind.Equip),101);
        check(f.Take(101,check,"first source-qualified Equip settles").Result==WeaponActionResult.Accepted
            &&f.Journal.Selected==Weapon&&f.Journal.SealNativeSelection()
            &&f.Journal.ReadNativeSelection(f.Journal.Owner,101)==CustomNativeSelection.Custom,
            "explicit Equip at effective source selects newly acquired weapon");
        f.Offer(f.Action(4,101),101);var fired=f.Take(101,check,"funded Fire settles");
        check(fired.Emission==CustomEmissionDisposition.Spawned&&fired.Before?.Magazine==4
            &&fired.After?.Magazine==3&&fired.After?.Reserve==16&&f.Engine.Calls==1
            &&f.Journal.PickupRevision==1&&f.Journal.SettledSequence==4,
            "funded Fire pays once while pickup revision stays independent of settled sequence");
        var before=Capture(f);
        check(!Fund(f,1,1,16,out _)&&Capture(f)==before,
            "duplicate incarnation cannot add resources or mutate journal counters");
    }
    private static void SourceOrder(Action<bool,string> check)
    {
        using var f=new Fixture(pickupFunding:true);f.Reloading(check);
        var committed=f.State();
        check(committed.Magazine==3&&committed.Reserve==16&&committed.ReloadCompleteTick==239,
            "reload fixture supplies exact source-time baseline");
        f.Observe(150,150);check(Fund(f,1,1,7,out var first)&&first.Tick==151&&first.Reserve==23,
            "first repeat projects reserve at its next-source boundary");
        f.Observe(200,200);check(Fund(f,2,1,11,out var second)&&second.Tick==201&&second.Reserve==34,
            "second repeat projects both pending grants in source order");
        f.Observe(240,240);
        check(f.Journal.TryPreview(Weapon,out var preview)&&preview.Magazine==4&&preview.Reserve==33
            &&preview.Phase==CustomWeaponPhase.Ready&&f.State()==committed
            &&f.Journal.PendingPickupFundingCount==2,
            "current preview completes reload over value snapshots without consuming pending grants");
        f.Offer(f.Action(4,200),240);var oldFire=f.Take(240,check,"late pre-second-grant Fire settles");
        check(oldFire.Result==WeaponActionResult.Reloading&&oldFire.Before?.Magazine==3
            &&oldFire.Before?.Reserve==23&&oldFire.After?.Reserve==23
            &&f.Journal.PendingPickupFundingCount==1&&f.Engine.Calls==1,
            "delayed Fire sees only source-due first grant, never newer reserve from preview");
        f.Offer(f.Action(5,240),240);var fresh=f.Take(240,check,"post-reload funded Fire settles");
        check(fresh.Emission==CustomEmissionDisposition.Spawned&&fresh.Before?.Magazine==4
            &&fresh.Before?.Reserve==33&&fresh.After?.Magazine==3&&fresh.After?.Reserve==33
            &&f.Journal.PendingPickupFundingCount==0&&f.Journal.PickupRevision==2&&f.Engine.Calls==2,
            "newer Fire commits remaining grant and reload transfer once before payment");
        check(f.Journal.TryGetReceipt(4,out var retained)&&retained==oldFire,
            "later funding cannot rewrite retained older resource receipt");
    }
    private static void AdmissionAndInterruption(Action<bool,string> check)
    {
        using(var f=new Fixture(pickupFunding:true))
        {
            f.Equip(check);check(Fund(f,1,1,8,out _),"pending-admission fixture receives repeat funding");
            var committed=f.State();f.Observe(101,101);f.Oracle.Decision=CustomSourceDecision.Pending;
            f.Offer(f.Action(2,101),101);
            check(!f.Process(101,out _)&&f.State()==committed&&f.Journal.PendingPickupFundingCount==1
                &&f.Journal.SettledSequence==1&&f.Engine.Calls==0,
                "unresolved admission retains funding and cannot mutate resources or sequence");
            f.Oracle.Decision=CustomSourceDecision.Rejected;
            check(f.Take(102,check,"rejected admission settles").Disposition==CustomJournalDisposition.SourceDenied
                &&f.State()==committed&&f.Journal.PendingPickupFundingCount==1,
                "rejected source proof cannot commit trusted future funding via a denied action");
            f.Oracle.Decision=CustomSourceDecision.Verified;
            f.Offer(f.Action(3,101,CustomWeaponActionKind.Optic),102);
            check(f.Take(102,check,"next admitted action commits retained funding").Result==WeaponActionResult.Accepted
                &&f.State().Reserve==24&&f.Journal.PendingPickupFundingCount==0&&f.Engine.Calls==0,
                "later admitted source commits retained funding exactly once");
        }
        using(var f=new Fixture(pickupFunding:true))
        {
            f.Equip(check);f.Observe(101,101);f.Offer(f.Action(2,101,CustomWeaponActionKind.Optic),101);
            var before=Capture(f);bool denied=false,unchanged=false;
            f.Oracle.DuringInspect=()=>{denied=!Fund(f,1,1,8,out _);unchanged=Capture(f)==before;};
            check(f.Take(101,check,"action with reentrant funding attempt settles").Result==WeaponActionResult.Accepted
                &&denied&&unchanged&&f.Journal.PickupRevision==0&&f.Journal.PendingPickupFundingCount==0,
                "reentrant funding during source inspection refuses atomically without disturbing admitted action");
        }
        using(var f=new Fixture(pickupFunding:true))
        {
            f.Equip(check);check(Fund(f,1,1,8,out _),"capacity-denied fixture receives legitimate due funding");
            f.Observe(101,101);f.Engine.Capacity=false;f.Offer(f.Action(2,101),101);
            var receipt=f.Take(101,check,"capacity-denied funded Fire settles");
            check(receipt.Result==WeaponActionResult.CapacityUnavailable&&receipt.Before?.Magazine==4
                &&receipt.After?.Magazine==4&&receipt.Before?.Reserve==24&&receipt.After?.Reserve==24
                &&f.State().Magazine==4&&f.State().Reserve==24&&f.Journal.PendingPickupFundingCount==0
                &&f.Journal.PickupRevision==1&&f.Engine.CapacityCalls==1&&f.Engine.Calls==0,
                "legitimate due funding commits before capacity refusal without shot payment or emission");
        }
        using(var f=new Fixture(pickupFunding:true))
        {
            f.Reloading(check);var committed=f.State();f.Observe(150,150,false);
            check(Fund(f,1,1,5,out _),"interruption fixture receives trusted reserve contact");
            f.Observe(250,250);
            check(f.Journal.TryPreview(Weapon,out var preview)&&preview.Magazine==3&&preview.Reserve==21
                &&preview.ReloadCompleteTick==0&&preview.Phase==CustomWeaponPhase.Ready&&f.State()==committed,
                "preview cancels reload before later funding rather than completing it while advancing grant");
            f.Offer(f.Action(4,250),250);var receipt=f.Take(250,check,"resumed interrupted funded Fire settles");
            check(receipt.Emission==CustomEmissionDisposition.Spawned&&receipt.Before?.Magazine==3
                &&receipt.Before?.Reserve==21&&receipt.Before?.ReloadCompleteTick==0
                &&receipt.After?.Magazine==2&&receipt.After?.Reserve==21,
                "committed interruption/funding ordering matches pure preview without phantom reload transfer");
        }
    }
    private static void InvalidAndCapacity(Action<bool,string> check)
    {
        using(var f=new Fixture(loadout:Array.Empty<WeaponRuntimeId>(),pickupFunding:true))
        {
            var owner=f.Journal.Owner;var item=Item(f);var before=Capture(f);
            foreach(var wrong in new[]{new WeaponOwnerContext(2,1,0,1,1),new(1,2,0,1,1),new(1,1,1,1,1),new(1,1,0,2,1),new(1,1,0,1,2),default})
                check(!f.Journal.TryFundPickup(item,wrong,Weapon,100,100,16,out _)&&Capture(f)==before,
                    "foreign/default full owner refuses atomically: "+wrong);
            foreach(var wrong in new[]{default(CustomPickupItemKey),item with{SpawnId=0},item with{Incarnation=0},
                item with{World=item.World with{Start=new(2,1,1)}},item with{World=item.World with{Start=new(1,2,1)}},
                item with{World=item.World with{Start=new(1,1,2)}},item with{World=item.World with{Manifest=new(9,8,7,6)}},
                item with{World=item.World with{Layout=new(9,8,7,6)}}})
                check(!f.Journal.TryFundPickup(wrong,owner,Weapon,100,100,16,out _)&&Capture(f)==before,
                    "foreign/invalid frozen world or incarnation refuses atomically");
            foreach(var weapon in new[]{default(WeaponRuntimeId),new(1),new(12),new(74)})
                check(!f.Journal.TryFundPickup(item,owner,weapon,100,100,16,out _)&&Capture(f)==before,
                    "native/unregistered/out-of-range weapon refuses atomically: "+weapon);
            foreach(int amount in new[]{-1,0,65})
                check(!f.Journal.TryFundPickup(item,owner,Weapon,100,100,amount,out _)&&Capture(f)==before,
                    "invalid refill amount refuses atomically: "+amount);
            foreach(uint source in new uint[]{99,101,133})
                check(!f.Journal.TryFundPickup(item,owner,Weapon,source,100,16,out _)&&Capture(f)==before,
                    "non-frontier contact source refuses without clock advance: "+source);
            foreach(uint world in new uint[]{99,101})
                check(!f.Journal.TryFundPickup(item,owner,Weapon,100,world,16,out _)&&Capture(f)==before,
                    "wrong authority frame refuses without authority-budget mutation: "+world);
            check(Fund(f,1,1,16,out _)&&f.Journal.PickupRevision==1&&f.Clock.Observe(owner,132,100),
                "all refusals preserve valid same-frame funding and original clock budget");
        }
        using(var f=new Fixture(loadout:Array.Empty<WeaponRuntimeId>()))
            check(!f.Journal.TryFundPickup(new(new(new(1,1,1),f.Manifest.Digest,new(1,2,3,4)),1,1),
                f.Journal.Owner,Weapon,100,100,16,out _)&&f.Journal.PickupRevision==0,
                "legacy journal without frozen pickup world cannot receive funding");
        using(var f=new Fixture(pickupFunding:true))
        {
            Fund(f,1,1,16,out _);f.Journal.Retire(WeaponCancellation.Death);var before=Capture(f);
            check(f.Journal.PendingPickupFundingCount==0&&!Fund(f,1,2,16,out _)&&Capture(f)==before,
                "retired life clears pending funding and cannot revive from contact");
        }
        using(var f=new Fixture(loadout:Array.Empty<WeaponRuntimeId>(),pickupFunding:true))
        {
            bool accepted=true;for(ushort spawn=1;spawn<=CustomSourceActionJournal.PickupSpawnCapacity;spawn++)
                accepted&=Fund(f,spawn,1,1,out _);
            var before=Capture(f);
            check(accepted&&!Fund(f,65,1,1,out _)&&Capture(f)==before&&f.Journal.PickupRevision==64,
                "64 spawn high-water entries admit exactly and65th rejects without mutation");
            check(Fund(f,64,2,1,out _)&&!Fund(f,64,1,1,out _),
                "known spawn may advance incarnation at spawn capacity while older incarnation remains refused");
        }
        using(var f=new Fixture(pickupFunding:true))
        {
            f.Equip(check);bool accepted=true;
            for(ulong incarnation=1;incarnation<=CustomSourceActionJournal.PickupFundingCapacity;incarnation++)
                accepted&=Fund(f,1,incarnation,1,out _);
            var before=Capture(f);
            check(accepted&&f.Journal.PendingPickupFundingCount==128&&!Fund(f,1,129,1,out _)&&Capture(f)==before,
                "128 pending repeat fundings admit exactly;129th refuses without consuming incarnation");
            f.Observe(101,101);f.Offer(f.Action(2,101,CustomWeaponActionKind.Optic),101);
            check(f.Take(101,check,"capacity funding drain settles").Result==WeaponActionResult.Accepted
                &&f.State().Reserve==64&&f.State().Magazine==4&&f.Journal.PendingPickupFundingCount==0,
                "due repeats cap reserve and never reset magazine or require full-reserve refusal");
            var committed=f.State();
            check(Fund(f,1,129,16,out var full)&&full.Reserve==64&&f.State()==committed
                &&f.Journal.PickupRevision==129&&f.Journal.PendingPickupFundingCount==1,
                "capacity retry accepts same incarnation after drain; full reserve still accepts capped funding");
        }
    }
    private static void WrappedSource(Action<bool,string> check)
    {
        using var f=new Fixture(uint.MaxValue,uint.MaxValue,loadout:Array.Empty<WeaponRuntimeId>(),pickupFunding:true);
        check(f.Journal.SealNativeSelection()&&f.Journal.TryReleaseNativeSelection(f.Journal.Owner,uint.MaxValue)
            &&Fund(f,1,1,16,out var funded)&&funded.Tick==0&&!f.Journal.TryPreview(Weapon,out _),
            "next-source ownership wraps maxuint tozero without rewriting native contact-source permission");
        check(f.Observe(0,0)&&f.Journal.TryPreview(Weapon,out var owned)&&owned.Tick==0&&owned.Magazine==4,
            "wrapped zero source exposes legitimate next-tick ownership");
        f.Offer(f.Action(1,0,CustomWeaponActionKind.Equip),0);
        check(f.Take(0,check,"wrapped-source Equip settles").Result==WeaponActionResult.Accepted
            &&f.Journal.Selected==Weapon&&f.Journal.SealNativeSelection()
            &&f.Journal.ReadNativeSelection(f.Journal.Owner,uint.MaxValue)==CustomNativeSelection.Native,
            "wrapped Equip preserves old native permission and explicit current custom selection");
    }
    private static void ProjectParity(Action<bool,string> check)
    {
        var definition=RiftlineM7.Create();var owner=new WeaponOwnerContext(1,1,0,1,1);
        WeaponRuntimeState State(uint tick=100)=>new(definition,owner,tick);
        void Compare(WeaponRuntimeState state,string name,params uint[] ticks)
        {
            var original=state.Snapshot();
            foreach(uint tick in ticks)
            {
                state.Preview(tick,out var baseline);
                var projected=WeaponRuntimeState.Project(original,definition.Mechanics,tick);
                check(projected==baseline&&state.Snapshot()==original,
                    "value projection equals unchanged baseline Preview: "+name+" at "+tick);
            }
        }
        var unequipped=State();Compare(unequipped,"unequipped",99,100,101,500);
        var ready=State();ready.SetEquipped(100,true);Compare(ready,"ready",100,101,500);
        var bolt=State();bolt.SetEquipped(100,true);bolt.TryFire(100,1,true);
        Compare(bolt,"bolt deadline",100,174,175,176);
        check(WeaponRuntimeState.Project(bolt.Snapshot(),definition.Mechanics,174).Phase==CustomWeaponPhase.BoltCycling
            &&WeaponRuntimeState.Project(bolt.Snapshot(),definition.Mechanics,175).Phase==CustomWeaponPhase.Ready,
            "value projection changes bolt phase at exact cooldown boundary");
        var reload=State();reload.SetEquipped(100,true);reload.TryFire(100,1,true);reload.TryReload(101,1);
        Compare(reload,"reload deadline",100,101,238,239,240);
        var before=WeaponRuntimeState.Project(reload.Snapshot(),definition.Mechanics,238);
        var at=WeaponRuntimeState.Project(reload.Snapshot(),definition.Mechanics,239);
        check(before.Phase==CustomWeaponPhase.Reloading&&before.Magazine==3&&before.Reserve==16
            &&at.Phase==CustomWeaponPhase.Ready&&at.Magazine==4&&at.Reserve==15,
            "value projection transfers exactly one missing round only at reload deadline");
        var empty=State();empty.SetEquipped(100,true);
        for(uint shot=1;shot<=4;shot++)empty.TryFire(100+(shot-1)*75,shot,true);
        empty.Advance(400);Compare(empty,"empty",399,400,401,500);
        check(WeaponRuntimeState.Project(empty.Snapshot(),definition.Mechanics,500).Phase==CustomWeaponPhase.Empty
            &&empty.Snapshot().Reserve==16,"value projection does not refill empty magazine from reserve without reload");
        reload.SetEquipped(150,false);Compare(reload,"canceled reload",150,238,239,500);
        check(WeaponRuntimeState.Project(reload.Snapshot(),definition.Mechanics,500).Magazine==3
            &&WeaponRuntimeState.Project(reload.Snapshot(),definition.Mechanics,500).Reserve==16,
            "unequipped canceled reload cannot transfer resources in value projection");
        uint birth=uint.MaxValue-50;var wrapped=State(birth);wrapped.SetEquipped(birth,true);
        wrapped.TryFire(birth,1,true);wrapped.TryReload(unchecked(birth+1),1);
        uint deadline=unchecked(birth+1+138);Compare(wrapped,"wrapped reload deadline",
            unchecked(birth+1),unchecked(deadline-1),deadline,unchecked(deadline+1));
        var wrappedReady=WeaponRuntimeState.Project(wrapped.Snapshot(),definition.Mechanics,deadline);
        check(deadline==88&&wrappedReady.Phase==CustomWeaponPhase.Ready
            &&wrappedReady.Magazine==4&&wrappedReady.Reserve==15,
            "wrapped reload deadline retains exact one-round transfer at source88");
        ready.Invalidate(WeaponCancellation.Death);Compare(ready,"invalid life",100,200);
    }
    private static void Allocation(Action<bool,string> check)
    {
        using var f=new Fixture(pickupFunding:true);f.Reloading(check);
        for(ulong incarnation=1;incarnation<=32;incarnation++)
            if(!Fund(f,1,incarnation,1,out _))throw new Exception("Allocation fixture repeat funding refused.");
        f.Observe(250,250);var committed=f.State();var before=Capture(f);
        bool stable=f.Journal.TryPreview(Weapon,out var expected)&&expected.Magazine==4&&expected.Reserve==47;
        for(int i=0;i<20000;i++)stable&=f.Journal.TryPreview(Weapon,out var view)&&view==expected;
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)stable&=f.Journal.TryPreview(Weapon,out var view)&&view==expected;
        bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(stable&&bytes==0&&Capture(f)==before&&f.State()==committed&&f.Journal.PendingPickupFundingCount==32,
            "100000 pending-funding previews allocate0B and cannot commit reload/resources/funding/sequence");
    }
}
