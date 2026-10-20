using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure authored-layout/contact arbitration and trusted callback fixtures only.
// No Scene activation, map spawn discovery, network delivery or actor movement proof.
internal static class PickupWorldChecks
{
    private static readonly WeaponOwnerContext Owner=new(1,1,0,1,1), Other=new(1,1,1,1,1);
    private static readonly WeaponRuntimeId Weapon=new(10);
    private static readonly MapContentIdentity BuiltIn=MapContentIdentity.BuiltIn("MP1 SANCTORUS");
    private static CustomPickupPlacement Placement(ushort id=1,uint delay=0,uint respawn=1)
        =>new(id,Weapon,0,0,0,delay,respawn,16);
    private static CustomPickupContact Contact(uint frame=100,WeaponOwnerContext? owner=null,bool alt=false)
        =>new(owner??Owner,frame,frame,Vector3.Zero,Vector3.Zero,.55f,0,2,alt,true,true);
    private sealed class Fixture : IDisposable
    {
        internal readonly WeaponDefinition[] Definitions;
        internal readonly WeaponRegistry Registry;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly CustomPickupLayout Layout;
        internal readonly CustomPickupWorld World;
        internal readonly CustomPickupLedger Ledger;
        internal Fixture(CustomPickupPlacement[]? placements=null,uint birth=100,MapContentIdentity? map=null)
        {
            Definitions=new[] { WeaponDefinition.Create(Guid.Parse("00000001-0000-0000-0000-000000000001"),"Pickup A",MechanicsChecks.Rules()),
                WeaponDefinition.Create(Guid.Parse("00000002-0000-0000-0000-000000000001"),"Pickup B",MechanicsChecks.Rules()) };
            Registry=new(true,Definitions);Manifest=new(Definitions);
            Layout=new(map??BuiltIn,Manifest,Registry,placements??new[]{Placement()});
            World=new(new(1,1,1),Manifest.Digest,Layout.Digest);Ledger=new(Layout,World,birth);
        }
        internal CustomPickupItemState State(int index=0)
        {if(!Ledger.TryState(index,out var state))throw new Exception("Missing fixture pickup.");return state;}
        public void Dispose()=>Registry.Dispose();
    }
    private sealed class Funding(Fixture fixture) : ICustomPickupFunding
    {
        internal int Calls;internal ulong Revision;internal bool Accept=true,Throws;
        internal Func<CustomPickupFundingResult,CustomPickupFundingResult>? Alter;
        internal Action? During;
        public bool TryFund(CustomPickupItemKey item,in CustomPickupContact contact,WeaponRuntimeId weapon,
            int reserveRefill,out CustomPickupFundingResult funded)
        {
            Calls++;During?.Invoke();funded=default;
            if(Throws)throw new InvalidOperationException("Explicit uncertain trusted funding failure.");
            if(!Accept)return false;
            fixture.Registry.TryResolve(weapon,out var entry);
            funded=new(new(contact.Owner,entry!.Custom!.Identity,unchecked(contact.SourceFrame+1),CustomWeaponPhase.Unequipped,
                4,16,0,0,0,0,0),++Revision);
            if(Alter!=null)funded=Alter(funded);
            return true;
        }
    }
    private sealed class JournalFunding : ICustomPickupFunding
    {
        internal readonly Dictionary<WeaponOwnerContext,CustomSourceActionJournal> Journals=new();
        internal int Calls;
        internal JournalFunding(Fixture f)
        {
            foreach(var owner in new[]{Owner,Other})Journals.Add(owner,new(owner,new(owner,100,100),f.Manifest,
                f.Registry,Array.Empty<WeaponRuntimeId>(),100,100,true,f.World));
        }
        public bool TryFund(CustomPickupItemKey item,in CustomPickupContact contact,WeaponRuntimeId weapon,
            int reserveRefill,out CustomPickupFundingResult funded)
        {
            Calls++;funded=default;
            if(!Journals.TryGetValue(contact.Owner,out var journal)
                ||!journal.TryFundPickup(item,contact.Owner,weapon,contact.SourceFrame,contact.AuthorityFrame,reserveRefill,out var state))return false;
            funded=new(state,journal.PickupRevision);return true;
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;void Verify(bool pass,string label){check(pass,"pickup world: "+label);count++;}
        LayoutChecks(Verify);GeometryChecks(Verify);ArbitrationAndJournal(Verify);TimelineChecks(Verify);
        PublicationAndFaults(Verify);Allocation(Verify);
        Console.WriteLine($"Pickup layout/ledger foundation passed: {count} assertions; pure contacts/trusted callbacks, no scene activation claim.");
    }
    private static void LayoutChecks(Action<bool,string> check)
    {
        using var f=new Fixture();var first=Placement();var second=Placement(2) with{Weapon=new(11),X4096=4096};
        CustomPickupLayout Make(IEnumerable<CustomPickupPlacement> placements,MapContentIdentity? map=null)
            =>new(map??BuiltIn,f.Manifest,f.Registry,placements);
        var array=new[]{second,first};var ordered=Make(array);var reverse=Make(array.Reverse());
        check(ordered.Digest==reverse.Digest&&ordered.Placements[0]==first&&ordered.Placements[1]==second,
            "layout hash and canonical spawn ordering ignore enumeration order");
        array[0]=second with{Z4096=4096};
        check(ordered.Placements[1]==second&&ordered.Digest==reverse.Digest,"layout detaches mutable caller placement array");
        foreach(var changed in new[]{first with{SpawnId=3},first with{Weapon=new(11)},first with{X4096=1},
            first with{Y4096=-1},first with{Z4096=1},first with{InitialDelayTicks=1},first with{RespawnTicks=2},first with{ReserveRefill=1}})
            check(Make(new[]{changed}).Digest!=f.Layout.Digest,"every authored placement field changes hash: "+changed);
        check(Make(new[]{first},MapContentIdentity.BuiltIn("mp1 sanctorus")).Digest==f.Layout.Digest
            &&Make(new[]{first},MapContentIdentity.BuiltIn("MP2 OTHER")).Digest!=f.Layout.Digest,
            "room names normalize case while different built-in room changes full layout identity");
        var custom=new MapContentIdentity(Guid.Parse("11111111-2222-3333-4444-555555555555"),"CUSTOM ROOM",new(1,2,3,4),new(5,6,7,8),true);
        var customLayout=Make(new[]{first},custom);
        foreach(var map in new[]{custom with{MapId=Guid.Parse("21111111-2222-3333-4444-555555555555")},custom with{RoomKey="CUSTOM OTHER"},
            custom with{ContentHash=new(2,2,3,4)},custom with{ContentHash=new(1,3,3,4)},custom with{ContentHash=new(1,2,4,4)},custom with{ContentHash=new(1,2,3,5)},
            custom with{PackageHash=new(6,6,7,8)},custom with{PackageHash=new(5,7,7,8)},custom with{PackageHash=new(5,6,8,8)},custom with{PackageHash=new(5,6,7,9)}})
            check(Make(new[]{first},map).Digest!=customLayout.Digest,"full custom map GUID/name/content/package identity changes layout hash");
        var changedDefinitions=new[] { WeaponDefinition.Create(f.Definitions[0].Identity.ContentId,"Changed pickup",MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65","\"body\":66"))),f.Definitions[1] };
        using(var changedRegistry=new WeaponRegistry(true,changedDefinitions))
        {
            var changedManifest=new CustomWeaponManifest(changedDefinitions);
            var changedLayout=new CustomPickupLayout(BuiltIn,changedManifest,changedRegistry,new[]{first});
            check(changedManifest.Digest!=f.Manifest.Digest&&changedLayout.Digest!=f.Layout.Digest,
                "frozen mechanics manifest changes layout identity");
            bool refused=false;try{_ = new CustomPickupLayout(BuiltIn,changedManifest,f.Registry,new[]{first});}catch(ArgumentException){refused=true;}
            check(refused,"same-count registry with different mechanics identity refuses");
        }
        using(var disposedRegistry=new WeaponRegistry(true,f.Definitions))
        {
            disposedRegistry.Dispose();bool refused=false;
            try{_ = new CustomPickupLayout(BuiltIn,f.Manifest,disposedRegistry,new[]{first});}catch(ArgumentException){refused=true;}
            check(refused,"disposed registry cannot establish layout identity");
        }
        var edge=Make(new[]{first with{X4096=-8192*4096,Y4096=8192*4096,Z4096=1,InitialDelayTicks=36000,RespawnTicks=36000,ReserveRefill=64}});
        check(edge.Placements[0].Position==new Vector3(-8192,8192,1f/4096),"fixed4096 coordinate and delay/refill maxima remain exact");
        void Reject(IEnumerable<CustomPickupPlacement> placements,string label,MapContentIdentity? map=null)
        {bool refused=false;try{Make(placements,map);}catch(ArgumentException){refused=true;}check(refused,label);}
        foreach(var invalid in new[]{first with{SpawnId=0},first with{Weapon=new(1)},first with{Weapon=new(12)},
            first with{X4096=8192*4096+1},first with{Y4096=-8192*4096-1},first with{Z4096=8192*4096+1},
            first with{InitialDelayTicks=36001},first with{RespawnTicks=0},first with{RespawnTicks=36001},
            first with{ReserveRefill=0},first with{ReserveRefill=65}})Reject(new[]{invalid},"invalid placement refuses before publication");
        Reject(Array.Empty<CustomPickupPlacement>(),"empty layout refuses");Reject(new[]{first,first},"duplicate spawn ID refuses");
        var maximum=Enumerable.Range(1,64).Select(i=>Placement((ushort)i)).ToArray();
        check(Make(maximum).Count==64,"64 authored pickups fit exact layout bound");
        Reject(maximum.Append(Placement(65)),"65th authored pickup refuses");
        int yielded=0;IEnumerable<CustomPickupPlacement> Infinite(){while(true){yielded++;yield return Placement((ushort)yielded);}}
        Reject(Infinite(),"infinite placement enumeration terminates at bounded input budget");
        check(yielded==65,"infinite enumerable consumes only65 records");
        foreach(var map in new[]{default(MapContentIdentity),BuiltIn with{RoomKey="A\nB"},BuiltIn with{RoomKey=new string('x',81)},
            BuiltIn with{MapId=custom.MapId},BuiltIn with{ContentHash=new(1,0,0,0)},custom with{MapId=Guid.Empty},
            custom with{ContentHash=default},custom with{PackageHash=default},BuiltIn with{RoomKey="A\uD800"},BuiltIn with{RoomKey="A\uD801"}})
            Reject(new[]{first},"malformed/noncanonical full map identity refuses",map);
        bool wrongWorld=false;try{_ = new CustomPickupLedger(f.Layout,f.World with{Layout=new(9,8,7,6)},100);}catch(ArgumentException){wrongWorld=true;}
        check(wrongWorld,"ledger construction rejects a different frozen layout world");
    }
    private static void GeometryChecks(Action<bool,string> check)
    {
        void Case(CustomPickupContact contact,bool collect,string label,CustomPickupPlacement? placement=null)
        {
            using var f=new Fixture(new[]{placement??Placement()});var callback=new Funding(f);var before=f.State();
            var result=f.Ledger.TryCollect(before.Key,contact,callback,out _);
            check(result==(collect?CustomPickupCollectResult.Collected:CustomPickupCollectResult.Invalid)
                &&callback.Calls==(collect?1:0),label);
        }
        var pose=Contact();float step=1f/4096;
        Case(pose,true,"biped origin contact accepts");
        Case(pose with{Position=new(1-step,0,0)},true,"biped radial interior accepts");
        Case(pose with{Position=new(1,0,0)},false,"biped exact radial boundary is excluded");
        Case(pose with{Position=new(0,0,1)},false,"biped z radial boundary is excluded");
        Case(pose with{Position=new(.75f,0,.75f)},false,"biped radial test is circular rather than axis-box");
        Case(pose with{Position=new(0,-2,0)},true,"biped exact maximum height is included");
        Case(pose with{Position=new(0,-2-step,0)},false,"biped above maximum height refuses");
        Case(pose with{Position=new(0,step,0)},false,"biped below minimum height refuses");
        Case(pose with{Alt=true,Position=new(100,100,100),SpherePosition=Vector3.Zero},true,"alt contact uses sphere center independent of biped position");
        Case(pose with{Alt=true,SpherePosition=new(1-step,0,0)},true,"alt sphere radial interior accepts");
        Case(pose with{Alt=true,SpherePosition=new(0,1,0)},false,"alt exact three-dimensional sphere boundary is excluded");
        foreach(var invalid in new[]{pose with{Owner=default},pose with{Owner=new(2,1,0,1,1)},pose with{Owner=new(1,2,0,1,1)},
            pose with{Alive=false},pose with{CanPickup=false},pose with{AuthorityFrame=99},pose with{AuthorityFrame=101},
            pose with{Position=new(float.NaN,0,0)},pose with{SpherePosition=new(float.PositiveInfinity,0,0)},pose with{Position=new(8193,0,0)},
            pose with{Radius=0},pose with{Radius=8.1f},pose with{Radius=float.NaN},pose with{MinHeight=3,MaxHeight=2},
            pose with{MinHeight=-129},pose with{MaxHeight=129}})Case(invalid,false,"invalid/alive/gate/current authority checks refuse before funding");
        var random=new Random(12391);bool parity=true;
        for(int i=0;i<2000;i++)
        {
            Vector3 position=new(random.Next(-8,9)/4f,random.Next(-8,9)/4f,random.Next(-8,9)/4f);
            Vector3 sphere=position+new Vector3(.25f,.5f,-.25f);Vector3 item=new(random.Next(-12,13)/4f,random.Next(-12,13)/4f,random.Next(-12,13)/4f);
            var contact=pose with{Position=position,SpherePosition=sphere,Alt=(i&1)!=0,Radius=.25f+(i%8)/4f};
            var native=new PickupContactPose(default,contact.SourceFrame,contact.AuthorityFrame,position,sphere,
                contact.Radius,contact.MinHeight,contact.MaxHeight,contact.Alt,true,true);
            parity&=contact.IsValid==native.IsValid&&contact.Reaches(item)==native.Reaches(item,position);
        }
        check(parity,"2000 valid-geometry fixtures match native biped/sphere contact formula without joining native identity");
    }
    private static void ArbitrationAndJournal(Action<bool,string> check)
    {
        using(var invalid=new Fixture())
        {
            var real=new JournalFunding(invalid);var invalidBefore=invalid.State();
            check(invalid.Ledger.TryCollect(invalidBefore.Key,Contact() with{SourceFrame=101},real,out _)==CustomPickupCollectResult.FundingRefused
                &&invalid.State()==invalidBefore&&invalid.Ledger.Revision==0&&invalid.Ledger.PendingOutcomes==0
                &&real.Journals[Owner].PickupRevision==0&&!real.Journals[Owner].TryPreview(Weapon,out _),
                "future unobserved source reaches trusted adapter but cannot fund or consume world item");
        }
        using var f=new Fixture();var funding=new JournalFunding(f);var journal=funding.Journals[Owner];var other=funding.Journals[Other];
        check(journal.SealNativeSelection()&&journal.TryReleaseNativeSelection(Owner,100),"empty actual journal releases contact-source native permission");
        var key=f.State().Key;var before=f.State();
        check(f.Ledger.TryCollect(key,Contact(),funding,out var outcome)==CustomPickupCollectResult.Collected
            &&outcome.Owner==Owner&&outcome.Consumed==key&&outcome.Funding.Revision==1
            &&outcome.Funding.State.Tick==101&&outcome.Funding.State.Magazine==4&&outcome.Funding.State.Reserve==16,
            "world collection funds exact real empty journal at next source");
        check(f.Ledger.TryCollect(key,Contact(owner:Other),funding,out _)==CustomPickupCollectResult.Unavailable
            &&funding.Calls==1&&!other.TryPreview(Weapon,out _)&&f.Ledger.PendingOutcomes==1,
            "two contenders for same incarnation fund exactly one owner");
        check(!journal.TryPreview(Weapon,out _)&&!journal.Selected.IsValid&&journal.TryReleaseNativeSelection(Owner,100),
            "world pickup neither exposes premature ownership nor switches native selection");
        journal.ObserveSource(Owner,101,101,true);f.Ledger.Advance(101);
        check(journal.TryPreview(Weapon,out var first)&&first.Magazine==4&&first.Reserve==16&&first.Phase==CustomWeaponPhase.Unequipped,
            "real journal exposes next-source inventory while native remains selected");
        check(f.Ledger.TryCollect(f.State().Key,Contact(101),funding,out var repeated)==CustomPickupCollectResult.Collected
            &&repeated.Funding.Revision==2&&repeated.Funding.State.Reserve==32,
            "next incarnation adds real pending reserve with monotonic owner resource revision");
        journal.ObserveSource(Owner,102,102,true);
        check(journal.TryPreview(Weapon,out var projected)&&projected.Reserve==32
            &&journal.TryCommitted(Weapon,out var committed)&&committed.Reserve==16&&!journal.Selected.IsValid,
            "ledger repeat retains source-fenced pure preview without committing inventory or selection");
        var state=f.State();var calls=funding.Calls;
        var foreign=state.Key with{World=state.Key.World with{Start=new(1,1,2)}};
        check(f.Ledger.TryCollect(foreign,Contact(101),funding,out _)==CustomPickupCollectResult.Invalid
            &&funding.Calls==calls&&f.State()==state,"stale frozen world refuses before trusted funding callback");
        check(before.Key.Incarnation==1&&outcome.Next.Key.Incarnation==2&&outcome.Next.ReadyAt==101,
            "frozen first outcome retains original and next incarnation lifecycle");
    }
    private static void TimelineChecks(Action<bool,string> check)
    {
        using(var f=new Fixture(new[]{Placement(delay:2,respawn:3)}))
        {
            var funding=new Funding(f);var key=f.State().Key;
            check(!f.State().Available&&f.Ledger.TryCollect(key,Contact(),funding,out _)==CustomPickupCollectResult.Unavailable
                &&funding.Calls==0,"initial delay is unavailable before deadline without callback");
            f.Ledger.Advance(101);check(!f.State().Available,"initial delay remains unavailable one tick before");
            f.Ledger.Advance(102);check(f.State().Available&&f.Ledger.TryCollect(key,Contact(102),funding,out var first)==CustomPickupCollectResult.Collected
                &&first.Next.ReadyAt==105&&!f.State().Available,"initial exact deadline collects and schedules exact respawn");
            f.Ledger.Advance(104);check(!f.State().Available,"respawn remains unavailable one tick before");
            f.Ledger.Advance(105);var secondKey=f.State().Key;
            check(f.State().Available&&f.Ledger.TryCollect(key,Contact(105),funding,out _)==CustomPickupCollectResult.Unavailable
                &&f.Ledger.TryCollect(secondKey,Contact(105),funding,out _) ==CustomPickupCollectResult.Collected&&funding.Calls==2,
                "respawn exact deadline accepts only current incarnation");
            var revision=f.Ledger.Revision;var count=f.Ledger.PendingOutcomes;
            check(!f.Ledger.Advance(104)&&f.Ledger.AuthorityFrame==105&&f.Ledger.Advance(106)
                &&f.Ledger.Revision==revision&&f.Ledger.PendingOutcomes==count,
                "stale time refuses and quiet advancement does not manufacture outcomes or revisions");
            check(!f.Ledger.TryState(-1,out _)&&!f.Ledger.TryState(f.Layout.Count,out _),"state peek bounds refuse safely");
        }
        using(var f=new Fixture(new[]{Placement(delay:2,respawn:3)},uint.MaxValue-1))
        {
            var funding=new Funding(f);check(!f.State().Available&&f.State().ReadyAt==0,"initial delay wraps to sourcezero");
            f.Ledger.Advance(uint.MaxValue);check(!f.State().Available,"wrapped initial delay preserves prior tick");
            f.Ledger.Advance(0);check(f.State().Available&&f.Ledger.TryCollect(f.State().Key,Contact(0),funding,out var outcome)==CustomPickupCollectResult.Collected
                &&outcome.Next.ReadyAt==3,"wrapped exact deadline collects and respawns at3");
            f.Ledger.Advance(2);check(!f.State().Available&&f.Ledger.Advance(3)&&f.State().Available,"wrapped respawn boundary remains exact");
        }
    }
    private static void PublicationAndFaults(Action<bool,string> check)
    {
        using(var f=new Fixture())
        {
            var funding=new Funding(f){Accept=false};var state=f.State();
            check(f.Ledger.TryCollect(state.Key,Contact(),funding,out _)==CustomPickupCollectResult.FundingRefused
                &&f.State()==state&&f.Ledger.Revision==0&&f.Ledger.PendingOutcomes==0,"false trusted callback leaves incarnation and publication unchanged");
            funding.Accept=true;CustomPickupCollectResult inner=default;bool advance=true,ack=true,read=true;
            funding.During=()=>{inner=f.Ledger.TryCollect(state.Key,Contact(),funding,out _);advance=f.Ledger.Advance(101);ack=f.Ledger.AcknowledgePublication(1);read=f.Ledger.TryState(0,out _);};
            check(f.Ledger.TryCollect(state.Key,Contact(),funding,out _)==CustomPickupCollectResult.Collected
                &&inner==CustomPickupCollectResult.Busy&&!advance&&!ack&&!read&&funding.Calls==2,
                "reentrant collection is Busy and cannot read mixed baseline, advance world or acknowledge reserved outcome");
        }
        using(var f=new Fixture())
        {
            var funding=new Funding(f);var state=f.State();
            foreach(var wrong in new[]{state.Key with{SpawnId=0},state.Key with{SpawnId=9},state.Key with{Incarnation=0},
                state.Key with{World=f.World with{Layout=new(9,8,7,6)}},state.Key with{World=f.World with{Manifest=new(9,8,7,6)}},
                state.Key with{World=f.World with{Start=new(1,2,1)}}})
                check(f.Ledger.TryCollect(wrong,Contact(),funding,out _)==CustomPickupCollectResult.Invalid
                    &&funding.Calls==0&&f.State()==state&&f.Ledger.Revision==0,
                    "invalid spawn/incarnation/frozen world key refuses atomically before funding");
        }
        using(var f=new Fixture())
        {
            var funding=new Funding(f);bool full=true;
            for(uint i=0;i<128;i++){uint frame=100+i;full&=f.Ledger.Advance(frame)
                &&f.Ledger.TryCollect(f.State().Key,Contact(frame),funding,out _)==CustomPickupCollectResult.Collected;}
            f.Ledger.Advance(228);var pending=f.State();
            check(full&&funding.Calls==128&&f.Ledger.PendingOutcomes==128&&f.Ledger.Revision==128
                &&f.Ledger.TryCollect(pending.Key,Contact(228),funding,out _)==CustomPickupCollectResult.Backpressure
                &&funding.Calls==128&&f.State()==pending,"128 unacknowledged outcomes exert backpressure before resource callback");
            check(f.Ledger.TryPeekOutcome(out var head)&&head.Revision==1&&!f.Ledger.AcknowledgePublication(2)
                &&!f.Ledger.AcknowledgePublication(0)&&f.Ledger.PendingOutcomes==128,"only exact head publication acknowledgment can free a slot");
            check(f.Ledger.AcknowledgePublication(head.Revision)&&!f.Ledger.AcknowledgePublication(head.Revision)
                &&f.Ledger.TryCollect(pending.Key,Contact(228),funding,out var retried)==CustomPickupCollectResult.Collected
                &&retried.Revision==129&&funding.Calls==129&&f.Ledger.PendingOutcomes==128
                &&f.Ledger.TryPeekOutcome(out var next)&&next.Revision==2,"exact head ACK enables unchanged-incarnation retry and retains FIFO outcomes");
        }
        foreach(string broken in new[]{"throw","zero-revision","owner","weapon","tick","phase-invalid","phase-255","mag-negative","mag-large","reserve-negative","reserve-large","optic"})
        {
            using var f=new Fixture(new[]{Placement(),Placement(2)});var funding=new Funding(f){Throws=broken=="throw"};
            funding.Alter=value=>broken switch {
                "zero-revision"=>value with{Revision=0},"owner"=>value with{State=value.State with{Owner=Other}},
                "weapon"=>value with{State=value.State with{Weapon=f.Definitions[1].Identity}},
                "tick"=>value with{State=value.State with{Tick=100}},"phase-invalid"=>value with{State=value.State with{Phase=CustomWeaponPhase.Invalid}},
                "phase-255"=>value with{State=value.State with{Phase=(CustomWeaponPhase)255}},
                "mag-negative"=>value with{State=value.State with{Magazine=-1}},"mag-large"=>value with{State=value.State with{Magazine=5}},
                "reserve-negative"=>value with{State=value.State with{Reserve=-1}},"reserve-large"=>value with{State=value.State with{Reserve=65}},
                "optic"=>value with{State=value.State with{Optic=3}},_=>value };
            check(f.Ledger.TryCollect(f.State().Key,Contact(),funding,out _)==CustomPickupCollectResult.Fault&&f.Ledger.IsRetired
                &&!f.State().Available&&!f.State(1).Available&&f.Ledger.PendingOutcomes==0&&f.Ledger.Revision==0,
                "uncertain/invalid trusted funding quarantines whole world ledger: "+broken);
            check(f.Ledger.TryCollect(f.State(1).Key,Contact(),funding,out _)==CustomPickupCollectResult.Retired
                &&!f.Ledger.Advance(101)&&funding.Calls==1,"quarantined world cannot fund another spawn: "+broken);
        }
    }
    private static void Allocation(Action<bool,string> check)
    {
        using var f=new Fixture();var funding=new Funding(f);bool valid=true;
        for(uint i=0;i<20000;i++)
        {
            uint frame=100+i;valid&=f.Ledger.Advance(frame)&&f.Ledger.TryState(0,out var state)
                &&f.Ledger.TryCollect(state.Key,Contact(frame),funding,out var result)==CustomPickupCollectResult.Collected
                &&f.Ledger.AcknowledgePublication(result.Revision);
        }
        ulong before=f.Ledger.Revision;long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(uint i=20000;i<120000;i++)
        {
            uint frame=100+i;valid&=f.Ledger.Advance(frame)&&f.Ledger.TryState(0,out var state)
                &&f.Ledger.TryCollect(state.Key,Contact(frame),funding,out var result)==CustomPickupCollectResult.Collected
                &&f.Ledger.TryPeekOutcome(out var retained)&&retained==result&&f.Ledger.AcknowledgePublication(result.Revision);
        }
        bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(valid&&bytes==0&&f.Ledger.Revision==before+100000&&f.Ledger.PendingOutcomes==0,
            "100000 warmed advance/peek/collect/publication cycles allocate0B with immutable outcome equality");
        var original=f.State();before=f.Ledger.Revision;bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)valid&=f.Ledger.TryState(0,out var state)&&state==original&&f.Ledger.Advance(f.Ledger.AuthorityFrame);
        bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(valid&&bytes==0&&f.State()==original&&f.Ledger.Revision==before&&f.Ledger.PendingOutcomes==0,
            "100000 readonly state peeks and quiet same-frame advances allocate0B without mutation");
    }
}
