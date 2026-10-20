using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure owner-thread selection gate. FakePolicy intentionally supplies only a
// selection answer: these checks claim no pose, native proof, emission or health.
internal static class NativeFireGateChecks
{
    private static readonly WeaponOwnerContext Owner=new(7,0xfedcba9876543210UL,3,9,12);
    private const ulong Connection=0x123456789abcdef0UL;
    internal static void Run(Action<bool,string> check)
    {
        Basic(check);Admission(check);Immutable(check);OwnedCarrier(check);Validation(check);Turret(check);
        Identity(check);Retention(check);Bounds(check);SustainedAdmission(check);HistoryIntegration(check);Wrap(check);Allocation(check);
    }
    private static FireEvent Fire(uint id=1,uint source=1000,ushort life=12)
        =>new(id,source,990,128,FireEventKind.PressFire,(byte)BeamType.Imperialist,0,FireEvent.ScopedStateBit,
            FireEvent.FlagPose|FireEvent.FlagReticle,new(1,2,3),Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,new(.25f,.75f),life);
    private static IntentPacket Carrier(FireEvent fire)
    {
        var carrier=new IntentPacket {MatchId=Owner.MatchId,AuthorityEpoch=Owner.AuthorityEpoch,
            SlotGeneration=Owner.Generation,LifeId=Owner.Life,Frame=fire.SourceFrame,
            Buttons=IntentButtons.Shoot|IntentButtons.Zoom|IntentButtons.Jump,Position=new(4,5,6),Aim=Vector3.UnitZ,
            WeaponSelect=255,AmmoUa=87,AmmoMissiles=23,AckFrame=990,AckSubFrame=128,
            HasState=true,ChargeLevel=17,BoostDamage=11,ShotFlags=IntentPacket.FlagDoubleDamage,
            HasAnalogMove=true,MoveX=-78,MoveY=92,HasContinuousFireTick=true,ContinuousFireTick=998,
            HasFireEvents=true,FireEventCount=1,ShotInputCount=3};
        carrier.FireEvents[0]=fire;
        carrier.ShotInputs[0]=ShotInputHistory.Sample((byte)BeamType.Imperialist,true,true,true);
        carrier.ShotInputs[1]=ShotInputHistory.Sample((byte)BeamType.Imperialist,false,true,true);
        carrier.ShotInputs[2]=ShotInputHistory.Sample((byte)BeamType.Missile,true,false,false);
        return carrier;
    }
    private static ShotKey Key(FireEvent fire,IntentPacket? carrier=null)
    {var c=carrier??Carrier(fire);return new(c.AuthorityEpoch,c.MatchId,Owner.Slot,c.SlotGeneration,
        fire.SourceLife==0?c.LifeId:fire.SourceLife,fire.ShotId);}
    private sealed class FakePolicy : ICustomNativeSelectionPolicy
    {
        internal CustomNativeSelection Selection=CustomNativeSelection.Pending;
        internal bool AllowRelease=true;
        internal int Reads,Releases;
        internal WeaponOwnerContext LastOwner;
        internal uint LastSource;
        public CustomNativeSelection ReadNativeSelection(WeaponOwnerContext owner,uint source)
        {Reads++;LastOwner=owner;LastSource=source;return Selection;}
        public bool TryReleaseNativeSelection(WeaponOwnerContext owner,uint source)
        {Releases++;LastOwner=owner;LastSource=source;return AllowRelease;}
    }
    private sealed class HistoryPolicy(CustomNativeSelectionHistory history) : ICustomNativeSelectionPolicy
    {
        public CustomNativeSelection ReadNativeSelection(WeaponOwnerContext owner,uint source)=>history.Read(owner,source);
        public bool TryReleaseNativeSelection(WeaponOwnerContext owner,uint source)=>history.TryReleaseNative(owner,source);
    }
    private static void Basic(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();var carrier=Carrier(fire);var key=Key(fire);
        check(gate.Read(key,out var absent)==CustomNativeFireDecision.Pending&&absent==default,
            "missing gate identity reports no fabricated fire payload");
        check(gate.Offer(carrier,fire,Connection,100)==CustomNativeFireDecision.Pending&&gate.PendingCount==1&&policy.Releases==0,
            "unknown selection stages exactly one native fire without permission release");
        check(!gate.TryTake(100,out var missing,out var connection,out var missingKey)&&connection==0&&missingKey==default
            &&missing.Frame==0,"pending selection has default unavailable take outputs");
        policy.Selection=CustomNativeSelection.Native;
        check(gate.TryTake(101,out var ready,out connection,out var taken)&&connection==Connection&&taken==key
            &&ready.FireEvents[0]==fire&&gate.PendingCount==1&&policy.Releases==0,
            "taking newly native source is a preview without releasing or consuming work");
        check(gate.TryTake(101,out _,out _,out var again)&&again==key&&policy.Releases==0,
            "repeated take preserves exact pending identity until explicit release");
        check(!gate.Release(key,Connection+1,101)&&gate.PendingCount==1&&policy.Releases==0,
            "foreign connection cannot release waiting native permission");
        check(gate.Release(key,Connection,101)&&gate.PendingCount==0&&policy.Releases==1
            &&gate.Read(key,out var saved)==CustomNativeFireDecision.Released&&saved==fire,
            "explicit release freezes source permission and preserves immutable terminal fire");
        check(!gate.Release(key,Connection,101)&&gate.Offer(carrier,fire,Connection,102)==CustomNativeFireDecision.Released
            &&policy.Releases==1&&!gate.TryTake(102,out _,out _,out _),
            "released retry neither stages nor releases the native shot again");
        var direct=Fire(2);var dc=Carrier(direct);
        check(gate.Offer(dc,direct,Connection,102)==CustomNativeFireDecision.Released&&policy.Releases==2,
            "already sealed native source releases synchronously once");
        policy.AllowRelease=false;var refused=Fire(3);
        check(gate.Offer(Carrier(refused),refused,Connection,102)==CustomNativeFireDecision.Rejected,
            "native read cannot bypass a failed atomic permission lock");
    }
    private static void Admission(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);
        var fire=Fire();var carrier=Carrier(fire);var key=Key(fire);
        check(!gate.Confirm(key,fire)&&gate.ReadAdmitted(key,out var absent)==CustomNativeFireDecision.Pending&&absent==default,
            "missing native identity cannot be confirmed or synthesize an admitted fire");
        gate.Offer(carrier,fire,Connection,100);
        check(!gate.Confirm(key,fire)&&gate.ReadAdmitted(key,out var pending)==CustomNativeFireDecision.Pending&&pending==fire,
            "waiting selection cannot be confirmed before native permission release");
        policy.Selection=CustomNativeSelection.Native;
        bool taken=gate.TryTake(100,out _,out _,out var takenKey);
        check(taken&&gate.Release(takenKey,Connection,100)&&gate.Read(key,out var released)==CustomNativeFireDecision.Released
            &&released==fire&&gate.ReadAdmitted(key,out var unconfirmed)==CustomNativeFireDecision.Pending&&unconfirmed==fire,
            "released permission alone is not native admission and cannot authorize a shadow claim");
        foreach(var foreign in new[]{default(ShotKey),key with{AuthorityEpoch=key.AuthorityEpoch^0x8000000000000000UL},
            key with{MatchId=8},key with{ShooterSlot=4},key with{Generation=10},key with{LifeId=13},key with{ShotId=2}})
            check(!gate.Confirm(foreign,fire)&&gate.ReadAdmitted(key,out var original)==CustomNativeFireDecision.Pending&&original==fire,
                "native admission confirmation requires every original full shot-key field");
        foreach(var changed in new[]{default(FireEvent),fire with{AckFrame=989},fire with{AckSubFrame=0},
            fire with{SourceFrame=999},fire with{SourceLife=11},fire with{Weapon=(byte)BeamType.Missile},
            fire with{Origin=Vector3.Zero},fire with{ContinuousPhase=0}})
            check(!gate.Confirm(key,changed)&&gate.ReadAdmitted(key,out var original)==CustomNativeFireDecision.Pending&&original==fire,
                "native admission confirmation requires the immutable exact accepted fire payload");
        check(gate.Confirm(key,fire)&&gate.Confirm(key,fire)
            &&gate.ReadAdmitted(key,out var admitted)==CustomNativeFireDecision.Released&&admitted==fire
            &&gate.Read(key,out var stillOriginal)==CustomNativeFireDecision.Released&&stillOriginal==fire
            &&policy.Releases==1&&gate.PendingCount==0,
            "exact native acceptance confirmation is idempotent without reacquiring permission or replacing payload");
        var rejectedFire=Fire(2);policy.Selection=CustomNativeSelection.Pending;
        gate.Offer(Carrier(rejectedFire),rejectedFire,Connection,100);gate.Reject(Key(rejectedFire));
        check(!gate.Confirm(Key(rejectedFire),rejectedFire)
            &&gate.ReadAdmitted(Key(rejectedFire),out var rejected)==CustomNativeFireDecision.Rejected&&rejected==rejectedFire,
            "rejected native work cannot become admitted by later confirmation");
        gate.Advance(610);
        check(gate.ReadAdmitted(key,out var inFlight)==CustomNativeFireDecision.Released&&inFlight==fire,
            "confirmed native fact survives full510-tick missile flight beyond unadmitted retention");
        gate.Advance(754);
        check(gate.Confirm(key,fire)&&gate.ReadAdmitted(key,out var grace)==CustomNativeFireDecision.Released&&grace==fire,
            "confirmed native fact survives510-tick flight plus144-tick claim grace");
        gate.Advance(100+CustomNativeFireGate.AdmittedRetention);
        check(gate.Confirm(key,fire)&&gate.ReadAdmitted(key,out var atBoundary)==CustomNativeFireDecision.Released&&atBoundary==fire,
            "native admission survives exact1024-world identity retention boundary");
        gate.Advance(101+CustomNativeFireGate.AdmittedRetention);
        check(!gate.Confirm(key,fire)&&gate.ReadAdmitted(key,out var expired)==CustomNativeFireDecision.Pending&&expired==default,
            "duplicate admission confirmation never refreshes first arrival or revives an expired key");
        var immediate=new CustomNativeFireGate(Owner.Slot,new FakePolicy{Selection=CustomNativeSelection.Native});
        check(immediate.Offer(carrier,fire,Connection,100)==CustomNativeFireDecision.Released
            &&immediate.ReadAdmitted(key,out _)==CustomNativeFireDecision.Pending&&immediate.Confirm(key,fire)
            &&immediate.ReadAdmitted(key,out _)==CustomNativeFireDecision.Released,
            "synchronous native selection release also requires separate actual native acceptance confirmation");
    }
    private static void Immutable(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();var carrier=Carrier(fire);var key=Key(fire);
        gate.Offer(carrier,fire,Connection,100);
        foreach(var changed in new[]{fire with{AckFrame=989},fire with{AckSubFrame=129},fire with{SourceFrame=999},
            fire with{Kind=FireEventKind.ReleaseFire},fire with{Weapon=(byte)BeamType.Missile},fire with{Charge=1},
            fire with{ContinuousPhase=0},fire with{Origin=new(2,2,3)},fire with{Direction=Vector3.UnitX},
            fire with{Aim=Vector3.UnitX},fire with{View=Vector3.UnitX},fire with{Reticle=new(.5f,.75f)}})
        {
            var alternate=Carrier(changed);alternate.Frame=1000;
            check(gate.Offer(alternate,changed,Connection,101)==CustomNativeFireDecision.Conflict
                &&gate.Read(key,out var original)==CustomNativeFireDecision.Pending&&original==fire&&gate.PendingCount==1,
                "same native identity with contradictory shot payload cannot replace first waiting fact");
        }
        gate.Reject(key);policy.Selection=CustomNativeSelection.Native;
        check(gate.Offer(carrier,fire,Connection,102)==CustomNativeFireDecision.Rejected&&policy.Releases==0
            &&!gate.TryTake(102,out _,out _,out _),"explicit rejection cannot be promoted by later native selection");
        var rewritten=fire with{AckFrame=991};
        check(gate.Offer(Carrier(rewritten),rewritten,Connection,102)==CustomNativeFireDecision.Conflict
            &&gate.Read(key,out var rejectedOriginal)==CustomNativeFireDecision.Rejected&&rejectedOriginal==fire,
            "contradictory retry cannot replace a rejected terminal payload");
        var other=Fire(2);policy.Selection=CustomNativeSelection.Pending;gate.Offer(Carrier(other),other,Connection,102);
        check(gate.Offer(Carrier(other),other,Connection+1,103)==CustomNativeFireDecision.Rejected&&gate.PendingCount==0,
            "new authenticated connection cannot adopt work waiting on old connection");
        policy.Selection=CustomNativeSelection.Custom;var custom=Fire(3);
        check(gate.Offer(Carrier(custom),custom,Connection,103)==CustomNativeFireDecision.Rejected&&policy.Releases==0,
            "known custom source selection refuses native fire before any permission lock");
        var pending=Fire(4);policy.Selection=CustomNativeSelection.Pending;gate.Offer(Carrier(pending),pending,Connection,103);
        policy.Selection=CustomNativeSelection.Custom;
        check(!gate.TryTake(104,out _,out _,out _)&&gate.Read(Key(pending),out _)==CustomNativeFireDecision.Rejected,
            "waiting event becomes terminal when resolved source belongs to custom weapons");
    }
    private static void OwnedCarrier(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();var carrier=Carrier(fire);
        carrier.FireEventCount=2;carrier.FireEvents[1]=Fire(2);
        var expected=carrier;expected.FireEventCount=1;expected.FireEvents=default;expected.FireEvents[0]=fire;
        byte[] bytes=new byte[CompactIntentCodec.Size(expected)];CompactIntentCodec.Write(expected,bytes);
        gate.Offer(carrier,fire,Connection,100);
        carrier.Frame=1001;carrier.Buttons=IntentButtons.None;carrier.AmmoUa=0;carrier.ShotInputs[0]=0;
        carrier.FireEvents[0]=fire with{Origin=Vector3.Zero};carrier.FireEvents[1]=default;
        var retry=carrier;retry.FireEventCount=1;retry.FireEvents=default;retry.FireEvents[0]=fire;
        check(gate.Offer(retry,fire,Connection,100)==CustomNativeFireDecision.Pending&&gate.PendingCount==1,
            "retry carrier may advance physical inputs but cannot replace the first owned source carrier");
        policy.Selection=CustomNativeSelection.Native;bool taken=gate.TryTake(100,out var owned,out _,out _);
        byte[] actual=new byte[CompactIntentCodec.Size(owned)];CompactIntentCodec.Write(owned,actual);
        check(taken&&bytes.AsSpan().SequenceEqual(actual)&&owned.FireEventCount==1&&owned.ShotInputCount==3,
            "waiting gate owns exact native carrier bytes, physical Shoot/Zoom history and only selected fire");
        owned.ShotInputs[0]=0;owned.FireEvents[0]=default;
        check(gate.TryTake(100,out var twice,out _,out _)&&twice.FireEvents[0]==fire&&twice.ShotInputs[0]==expected.ShotInputs[0],
            "mutating a returned carrier cannot alias pending inline histories");
    }
    private static void Validation(Action<bool,string> check)
    {
        var policy=new FakePolicy {Selection=CustomNativeSelection.Native};var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();
        foreach(var bad in new[]{default(IntentPacket),Carrier(fire) with{MatchId=0},Carrier(fire) with{AuthorityEpoch=0},
            Carrier(fire) with{SlotGeneration=0},Carrier(fire) with{LifeId=0},Carrier(fire) with{Frame=999},Carrier(fire) with{Frame=1033}})
            check(gate.Offer(bad,fire,Connection,100)==CustomNativeFireDecision.Rejected,
                "invalid carrier lifecycle or fire outside source retention refuses native authorization");
        check(gate.Offer(Carrier(fire),fire,0,100)==CustomNativeFireDecision.Rejected&&policy.Releases==0,
            "zero connection is not an owner of native waiting work");
        foreach(var bad in new[]{fire with{ShotId=0},fire with{Kind=(FireEventKind)255},fire with{Weapon=255},
            fire with{Origin=new(float.NaN,0,0)},fire with{Direction=Vector3.Zero}})
            check(gate.Offer(Carrier(bad),bad,Connection,100)==CustomNativeFireDecision.Rejected,
                "structurally invalid individual native fire fails closed");
        var mismatch=fire with{ShotId=2};
        check(gate.Offer(Carrier(fire),mismatch,Connection,100)==CustomNativeFireDecision.Rejected&&policy.Releases==0,
            "separate fire argument must belong exactly to validated carrier history");
    }
    private static void Turret(Action<bool,string> check)
    {
        var policy=new FakePolicy {Selection=CustomNativeSelection.Custom};var gate=new CustomNativeFireGate(Owner.Slot,policy);
        var turret=Fire() with{Kind=FireEventKind.TurretFire,Weapon=(byte)BeamType.Battlehammer,ContinuousPhase=0};
        check(gate.Offer(Carrier(turret),turret,Connection,100)==CustomNativeFireDecision.Released&&policy.Reads==0&&policy.Releases==0,
            "valid Battlehammer turret source is independent of handheld custom selection and release lock");
        foreach(var bad in new[]{turret with{ShotId=2,Weapon=(byte)BeamType.Imperialist},turret with{ShotId=3,Charge=1},
            turret with{ShotId=4,PoseFlags=0},turret with{ShotId=5,SourceLife=0}})
            check(gate.Offer(Carrier(bad),bad,Connection,100)==CustomNativeFireDecision.Rejected&&policy.Reads==0&&policy.Releases==0,
                "turret exemption requires Battlehammer, zero charge, pose and explicit source life");
    }
    private static void Identity(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();var carrier=Carrier(fire);
        gate.Offer(carrier,fire,Connection,100);
        foreach(var c in new[]{carrier with{MatchId=8},carrier with{AuthorityEpoch=Owner.AuthorityEpoch^0x8000000000000000UL},
            carrier with{SlotGeneration=10}})
        {
            check(gate.Offer(c,fire,Connection,100)==CustomNativeFireDecision.Pending&&Key(fire,c)!=Key(fire),
                "match, full-width epoch and occupant generation partition same-number native shot identities");
        }
        var old=fire with{SourceLife=11};var newer=carrier with{LifeId=13};newer.FireEvents[0]=old;
        check(gate.Offer(newer,old,Connection,100)==CustomNativeFireDecision.Pending&&policy.LastOwner.Life==11
            &&policy.LastOwner.MatchId==Owner.MatchId&&policy.LastOwner.AuthorityEpoch==Owner.AuthorityEpoch,
            "recovered native source uses original explicit firing life rather than newer carrier life");
        var nextLife=fire with{SourceLife=13};var nc=Carrier(nextLife);nc.LifeId=13;
        check(gate.Offer(nc,nextLife,Connection,100)==CustomNativeFireDecision.Pending&&Key(nextLife,nc)!=Key(fire),
            "same-number native shots in another life retain a distinct full key");
        var slotPolicy=new FakePolicy();var otherSlot=new CustomNativeFireGate(4,slotPolicy);
        check(otherSlot.Offer(carrier,fire,Connection,100)==CustomNativeFireDecision.Pending&&slotPolicy.LastOwner.Slot==4,
            "trusted gate slot joins full source owner instead of an untrusted carrier slot");
        var futureLife=Fire(8) with{SourceLife=13};var futureCarrier=Carrier(futureLife);
        check(gate.Offer(futureCarrier,futureLife,Connection,100)==CustomNativeFireDecision.Rejected,
            "future source life cannot borrow an earlier carrier occupant life");
        var wrappedOld=Fire(9) with{SourceLife=ushort.MaxValue};var wrappedCarrier=Carrier(wrappedOld);wrappedCarrier.LifeId=1;
        check(gate.Offer(wrappedCarrier,wrappedOld,Connection,100)==CustomNativeFireDecision.Pending,
            "source life immediately before ushort wrap remains historical in a newer carrier");
        var wrappedFuture=Fire(10) with{SourceLife=1};var oldCarrier=Carrier(wrappedFuture);oldCarrier.LifeId=ushort.MaxValue;
        check(gate.Offer(oldCarrier,wrappedFuture,Connection,100)==CustomNativeFireDecision.Rejected,
            "source life after ushort wrap remains future to an older carrier life");
        var fallback=Fire(2) with{SourceLife=0};var fc=Carrier(fallback);
        check(gate.Offer(fc,fallback,Connection,100)==CustomNativeFireDecision.Pending&&policy.LastOwner==Owner
            &&policy.LastSource==fallback.SourceFrame,"legacy source-life fallback retains full carrier owner identity");
    }
    private static void Retention(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var fire=Fire();var c=Carrier(fire);var key=Key(fire);
        gate.Offer(c,fire,Connection,100);
        check(gate.Offer(c,fire,Connection,164)==CustomNativeFireDecision.Pending&&gate.PendingCount==1,
            "pending source retention includes exact64-world boundary without refreshing arrival");
        check(gate.Advance(165)&&gate.PendingCount==0&&gate.Read(key,out var old)==CustomNativeFireDecision.Rejected&&old==fire
            &&gate.Expired==1,"pending native source expires at65 and preserves terminal first payload");
        policy.Selection=CustomNativeSelection.Native;
        check(gate.Offer(c,fire,Connection,200)==CustomNativeFireDecision.Rejected&&policy.Releases==0,
            "retry after pending expiry cannot revive native authorization");
        check(gate.Advance(356)&&gate.Read(key,out old)==CustomNativeFireDecision.Rejected&&old==fire,
            "terminal first identity remains retained through exact256 worlds");
        check(gate.Advance(357)&&gate.Read(key,out old)==CustomNativeFireDecision.Pending&&old==default,
            "terminal entry retires only after256-world remembered decision window");
        check(!gate.Advance(356)&&!gate.TryTake(356,out _,out _,out _)&&!gate.Release(key,Connection,356),
            "backdated authority world cannot advance, take or release gate work");
        var permissionOnly=new CustomNativeFireGate(Owner.Slot,new FakePolicy{Selection=CustomNativeSelection.Native});
        permissionOnly.Offer(c,fire,Connection,100);permissionOnly.Advance(356);
        check(permissionOnly.Read(key,out _)==CustomNativeFireDecision.Released
            &&permissionOnly.ReadAdmitted(key,out _)==CustomNativeFireDecision.Pending,
            "unconfirmed permission retains only ordinary256-world identity window and never becomes admission");
        permissionOnly.Advance(357);
        check(permissionOnly.Read(key,out var cleared)==CustomNativeFireDecision.Pending&&cleared==default
            &&!permissionOnly.Confirm(key,fire),"unconfirmed release expires at257 instead of borrowing confirmed1024 retention");
    }
    private static void Bounds(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);
        for(uint id=1;id<=CustomNativeFireGate.Capacity;id++)
        {var f=Fire(id);check(gate.Offer(Carrier(f),f,Connection,100)==CustomNativeFireDecision.Pending,"all64 waiting native carrier slots are usable");}
        var excess=Fire(65);
        check(gate.Offer(Carrier(excess),excess,Connection,100)==CustomNativeFireDecision.Overflow&&gate.PendingCount==64,
            "65th pending carrier is refused without evicting earlier native work");
        gate.Reject(Key(Fire()));policy.Selection=CustomNativeSelection.Native;
        check(gate.Offer(Carrier(excess),excess,Connection,101)==CustomNativeFireDecision.Rejected,
            "capacity-refused identity remains denied when a carrier slot becomes free");
        var full=new CustomNativeFireGate(Owner.Slot,new FakePolicy {Selection=CustomNativeSelection.Custom});
        for(uint id=1;id<=CustomNativeFireGate.SeenCapacity;id++)
        {var f=Fire(id);check(full.Offer(Carrier(f),f,Connection,100)==CustomNativeFireDecision.Rejected,"all fixed-capacity retained terminal identities fit without eviction");}
        var next=Fire((uint)CustomNativeFireGate.SeenCapacity+1);
        check(full.Offer(Carrier(next),next,Connection,100)==CustomNativeFireDecision.Overflow
            &&full.Read(Key(Fire()),out var first)==CustomNativeFireDecision.Rejected&&first==Fire(),
            "full seen table refuses new identity without replacing retained tombstone");
    }
    private static void SustainedAdmission(Action<bool,string> check)
    {
        var policy=new FakePolicy {Selection=CustomNativeSelection.Native};var gate=new CustomNativeFireGate(Owner.Slot,policy);
        for(uint i=0;i<=CustomNativeFireGate.AdmittedRetention;i++)
        {
            var fire=Fire(i+1,1000+i);var input=Carrier(fire);
            check(gate.Offer(input,fire,Connection,100+i)==CustomNativeFireDecision.Released&&gate.Confirm(Key(fire),fire),
                "one confirmed native shot per world fits the complete inclusive1024-world retention horizon");
        }
        var first=Fire();
        check(gate.ReadAdmitted(Key(first),out var retained)==CustomNativeFireDecision.Released&&retained==first,
            "sustained60Hz admission cannot evict the oldest still-retained native fact");
        for(uint i=0;i<128;i++)
        {
            var turret=Fire(2000+i,2024) with{Kind=FireEventKind.TurretFire,Weapon=(byte)BeamType.Battlehammer,ContinuousPhase=0};
            check(gate.Offer(Carrier(turret),turret,Connection,1124)==CustomNativeFireDecision.Released
                &&gate.Confirm(Key(turret),turret),"conservative same-world turret burst fits beside retained handheld flight facts");
        }
        check(gate.Overflow==0&&gate.PendingCount==0&&gate.ReadAdmitted(Key(first),out _)==CustomNativeFireDecision.Released,
            "1153 retained confirmed handheld/turret facts fit without overflow or premature eviction");
    }
    private static void HistoryIntegration(Action<bool,string> check)
    {
        var history=new CustomNativeSelectionHistory(Owner,1000);var gate=new CustomNativeFireGate(Owner.Slot,new HistoryPolicy(history));
        var f=Fire();gate.Offer(Carrier(f),f,Connection,100);history.Observe(Owner,1000);history.Seal(Owner,1000);
        check(gate.TryTake(100,out _,out _,out var key)&&history.CanEquip(Owner,1000),
            "real native-history take leaves same-source custom equip permission provisional");
        history.Commit(Owner,1000,new(10));
        check(!gate.Release(key,Connection,100)&&!gate.TryTake(100,out _,out _,out _)
            &&gate.Read(key,out _)==CustomNativeFireDecision.Rejected,
            "real same-source custom equip between take and release denies native shot instead of dual emission");
        var native=new CustomNativeSelectionHistory(Owner,1000);var second=new CustomNativeFireGate(Owner.Slot,new HistoryPolicy(native));
        second.Offer(Carrier(f),f,Connection,100);native.Observe(Owner,1000);native.Seal(Owner,1000);
        bool taken=second.TryTake(100,out _,out _,out key);
        check(taken&&second.Release(key,Connection,100)&&!native.CanEquip(Owner,1000)&&!native.Commit(Owner,1000,new(10)),
            "real released native shot locks same-source selection against later raw custom equip");
        native.Observe(Owner,1010);native.Commit(Owner,1010,new(10));native.Seal(Owner,1010);
        var late=Fire(2,1009);var lc=Carrier(late);lc.Frame=1010;
        check(second.Offer(lc,late,Connection,101)==CustomNativeFireDecision.Released
            &&native.Read(Owner,1010)==CustomNativeSelection.Custom,
            "late pre-equip native fire resolves real historical ownership rather than current custom state");
        native.Observe(Owner,1138);native.Seal(Owner,1138);var expired=Fire(3,1009);
        check(second.Offer(Carrier(expired),expired,Connection,102)==CustomNativeFireDecision.Rejected,
            "expired real selection history refuses recovered native fire without current fallback");
    }
    private static void Wrap(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var f=Fire(1,uint.MaxValue-1);var c=Carrier(f);c.Frame=1;
        uint start=uint.MaxValue-20;
        check(gate.Offer(c,f,Connection,start)==CustomNativeFireDecision.Pending&&gate.Advance(43)&&gate.PendingCount==1,
            "native carrier source retention and exact64-world pending lifetime survive uint wrap");
        check(gate.Advance(44)&&gate.PendingCount==0&&gate.Read(Key(f,c),out _)==CustomNativeFireDecision.Rejected,
            "wrapped pending age65 expires without reopening source permission");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var policy=new FakePolicy();var gate=new CustomNativeFireGate(Owner.Slot,policy);var f=Fire();var c=Carrier(f);var key=Key(f);
        gate.Offer(c,f,Connection,100);bool valid=true;
        for(int i=0;i<1000;i++) valid&=gate.Offer(c,f,Connection,100)==CustomNativeFireDecision.Pending&&!gate.TryTake(100,out _,out _,out _);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++) valid&=gate.Offer(c,f,Connection,100)==CustomNativeFireDecision.Pending
            &&gate.Read(key,out var stored)==CustomNativeFireDecision.Pending&&stored==f&&!gate.TryTake(100,out _,out _,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid&&allocated==0,"100000 warmed duplicate/read/pending-take native gate probes allocate zero bytes");
        Console.WriteLine($"Native fire gate duplicate/read/take allocation: {allocated} bytes / 100000 warmed probes.");
        var rollingPolicy=new FakePolicy();var rolling=new CustomNativeFireGate(Owner.Slot,rollingPolicy);
        bool Cycle(uint id)
        {
            var shot=f with{ShotId=id};var input=c;input.FireEvents[0]=shot;
            uint world=id*(CustomNativeFireGate.AdmittedRetention+1);rollingPolicy.Selection=CustomNativeSelection.Pending;
            if(rolling.Offer(input,shot,Connection,world)!=CustomNativeFireDecision.Pending)return false;
            rollingPolicy.Selection=CustomNativeSelection.Native;
            return rolling.TryTake(world,out _,out var conn,out var next)&&conn==Connection
                &&rolling.Release(next,conn,world)&&rolling.PendingCount==0
                &&rolling.ReadAdmitted(next,out _)==CustomNativeFireDecision.Pending
                &&rolling.Confirm(next,shot)&&rolling.ReadAdmitted(next,out var accepted)==CustomNativeFireDecision.Released
                &&accepted==shot;
        }
        for(uint id=1;id<=1000;id++)valid&=Cycle(id);
        before=GC.GetAllocatedBytesForCurrentThread();
        for(uint id=1001;id<=101000;id++)valid&=Cycle(id);
        allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid&&allocated==0,
            "100000 warmed novel stage/take/release/confirm cycles reclaim fixed storage with zero allocation");
        Console.WriteLine($"Native fire gate rolling stage/take/release/confirm allocation: {allocated} bytes / 100000 warmed cycles.");
    }
}
