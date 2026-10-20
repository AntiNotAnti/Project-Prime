using System;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

// Pure supplied cosmetic facts, never accepted damage, engine rig/render or network admission proof.
internal static class HeadHitTrackChecks
{
    private sealed record ActorValue(int Value);
    private static HeadHitVictim Victim()=>new(1,new ActorValue(1),4,7,9,6,11,0);
    private static WorldBootstrapIdentity Bootstrap()=>new(new(7,9,3),2,5,90,1,1);
    private static CustomActionIdentity Action(uint sequence=1,ushort life=7)=>new(new(7,9,2,5,life),new(1,2,3,4),new(10),sequence);
    private static ReplayShotFact Native()=>new(7,9,100,99,1,1,2,5,7,4,6,11,0,
        ReplayShotFactFlags.Headshot|ReplayShotFactFlags.Direct,25,175,0,0,Vector3.One);
    private static CustomContactOutcome Custom()=>new(new(new(Action(),new(4,6,11),1,CustomImpactChannel.Body),Vector3.One,true),100,new(200,50,0,0,1));
    private static HeadHitFact Local(HeadHitVictim v,ulong id,uint world=100,Vector3? direction=null)
        =>new(v,new(HeadHitDomain.Local,default,default,default,id,0),world,direction??Vector3.UnitZ);
    private static HeadHitTrack Track(HeadHitVictim v){var t=new HeadHitTrack();t.Bind(v);return t;}
    internal static void Run(Action<bool,string> check)
    {
        var v=Victim();var native=Native();var hit=Custom();var boot=Bootstrap();
        check(v.IsValid&&new HeadHitVictim(1,new object(),0,0,0,0,uint.MaxValue,0).IsValid,"online full-life and offline spawn-ordinal victims are distinct supported shapes");
        foreach(var invalid in new[]{v with{Scene=0},v with{Actor=null!},v with{Slot=8},v with{Life=0},v with{Epoch=0},v with{Generation=0},v with{Life=65536},v with{Match=0}})
            check(!invalid.IsValid,"incomplete victim identity refuses");
        check(!v.Matches(v with{Actor=new ActorValue(1)}),"actor matching uses reference identity even when object values equal");
        check(HeadHitFact.TryNative(v,native,out var nf)&&nf.Event.Domain==HeadHitDomain.Native&&nf.Direction==Vector3.Zero,"surviving native headshot yields tagged neutral-direction fact");
        check(HeadHitFact.TryCustom(v,boot,hit,Vector3.UnitX,out var cf)&&cf.Event.Domain==HeadHitDomain.Custom&&cf.ResolveWorld==100,"canonical surviving custom headshot preserves payment and resolve identity");
        check(nf.Event!=cf.Event&&nf.Event.Native.ShotId==cf.Event.Custom.Action.Sequence,"native/custom sequence1 never aliases");
        foreach(var bad in new[]{native with{Flags=ReplayShotFactFlags.Direct},native with{Flags=native.Flags|ReplayShotFactFlags.Lethal},native with{Flags=native.Flags|ReplayShotFactFlags.HalfturretTarget},native with{Damage=0},native with{HealthAfter=0},native with{ShotId=0},native with{DamageEventId=0},native with{ShooterSlot=8},native with{VictimLifeId=12},native with{VictimGeneration=7},native with{Weapon=255},native with{ImpactPoint=new(float.NaN,0,0)},native with{ImpactPoint=new(0,float.PositiveInfinity,0)}})
            check(!HeadHitFact.TryNative(v,bad,out var none)&&none==default,"native body/lethal/protected/turret/incomplete identities refuse without partial fact");
        foreach(var bad in new[]{hit with{Contact=hit.Contact with{Headshot=false}},hit with{Disposition=CustomContactDisposition.TradeDenied},hit with{Health=new(200,200,0,0,0)},hit with{Health=new(150,0,0,0,1)},hit with{Contact=hit.Contact with{Key=hit.Contact.Key with{Channel=CustomImpactChannel.Halfturret}}},hit with{Contact=hit.Contact with{Key=hit.Contact.Key with{Victim=new(4,6,12)}}},hit with{Contact=hit.Contact with{Point=new(float.NaN,0,0)}},hit with{Health=new(200,50,0,0,0)}})
            check(!HeadHitFact.TryCustom(v,boot,bad,Vector3.UnitX,out var none)&&none==default,"custom body/lethal/protected/trade/turret/old victim facts refuse without partial output");
        foreach(var bad in new[]{boot with{SlotGeneration=6},boot with{Start=new(8,9,3)},boot with{Start=new(7,10,3)},default})
            check(!HeadHitFact.TryCustom(v,bad,hit,Vector3.UnitX,out _),"source bootstrap epoch/match/generation fence is exact");
        check(HeadHitFact.TryNative(v,native with{ResolveTick=20,LaunchFrame=19},out var staleNative)&&staleNative.ResolveWorld==20&&!Track(v).Offer(staleNative,100,true),"native canonical resolve tick cannot be retagged to arrival time");
        var oldSource=hit with{Contact=hit.Contact with{Key=hit.Contact.Key with{Action=Action(life:6)}}};
        check(HeadHitFact.TryCustom(v,boot,oldSource,Vector3.UnitX,out _),"admitted old shooter life is not confused with current victim life");
        check(!(nf.Event with{Bootstrap=boot}).IsValid&&!(nf.Event with{Custom=cf.Event.Custom}).IsValid&&!(cf.Event with{Native=nf.Event.Native}).IsValid,"mixed-domain payloads cannot create extra dedupe identities");

        foreach(var changed in new[]{v with{Scene=2},v with{Actor=new object()},v with{Slot=3},v with{Match=8},v with{Epoch=10},v with{Generation=7},v with{Life=12},v with{Timeline=1}})
        {
            var t=Track(v);check(!t.Offer(Local(changed,1),100,true),"every victim context substitution refuses old fact");
            check(t.Offer(Local(v,1),100,true),"wrong victim does not consume current victim dedupe slot");
        }
        var keys=new[]{nf.Event,cf.Event,new HeadHitEvent(HeadHitDomain.Local,default,default,default,1,0)};
        var tagged=Track(v);foreach(var key in keys)check(tagged.Offer(new(v,key,100,Vector3.UnitX),100,true),"tagged domains remain independent at same numeric nonce");
        check(!tagged.Offer(new(v,cf.Event,100,Vector3.UnitY),100,true),"exact payment duplicate cannot replay or replace direction");
        var rebound=Track(v);check(rebound.Offer(Local(v,1),100,true),"baseline local event starts");rebound.Bind(v);
        check(!rebound.Offer(Local(v,1),100,true),"same context bind preserves dedupe");
        var next=v with{Life=12};rebound.Bind(next);check(!rebound.Offer(Local(v,2),100,true)&&rebound.Offer(Local(next,1),100,true),"new victim life resets local ledger and refuses old life");
        var suppressed=Track(v);check(!suppressed.Offer(Local(v,1),100,false)&&!suppressed.Offer(Local(v,1),100,true),"suppressed hit is consumed and cannot replay after quality change");
        check(suppressed.Offer(Local(v,2),100,true),"new actual hit remains usable after suppression");suppressed.Step(101,false);
        check(suppressed.Sample(0)==default&&!suppressed.Offer(Local(v,2),101,true),"ineligibility cancels pose without clearing dedupe");

        foreach(uint age in new uint[]{0,1,17})check(Track(v).Offer(Local(v,1,100),100+age,true),"fresh hit within active age window starts");
        foreach(uint age in new uint[]{18,19,121})check(!Track(v).Offer(Local(v,1,100),100+age,true),"boundary/expired hit produces no reaction");
        check(!Track(v).Offer(Local(v,1,101),100,true),"future resolve world refuses");
        check(Track(v).Offer(Local(v,1,uint.MaxValue-5),3,true),"resolve age accepts uint wrap without signed stale reversal");
        var wrapped=Track(v);wrapped.Offer(Local(v,1,uint.MaxValue),uint.MaxValue,true);wrapped.Step(0,true);
        var plain=Track(v);plain.Offer(Local(v,1,100),100,true);plain.Step(101,true);
        check(wrapped.Sample(.5f)==plain.Sample(.5f),"fixed-step uint wrap advances exactly one tick");
        var wrappedCapacity=Track(v);for(ulong i=1;i<=HeadHitTrack.Capacity;i++)wrappedCapacity.Offer(Local(v,i,uint.MaxValue-50),uint.MaxValue-50,false);
        check(!wrappedCapacity.Offer(Local(v,33,69),69,true)&&wrappedCapacity.Offer(Local(v,34,70),70,true),"bounded retention120/121 is wrap-safe");
        var coalesce=Track(v);coalesce.Offer(Local(v,1,100,Vector3.UnitX),100,true);
        check(!coalesce.Offer(Local(v,2,99,Vector3.UnitY),100,true)&&coalesce.Sample(0).Direction==Vector3.UnitX,"out-of-order older resolve cannot replace active newer direction");
        check(coalesce.Offer(Local(v,3,101,Vector3.UnitY),101,true)&&coalesce.Sample(0).Direction==Vector3.UnitY,"newer canonical resolve coalesces to newest hit");
        check(!coalesce.Offer(Local(v,2,101,Vector3.UnitZ),101,true),"older suppressed identity was consumed before coalescing decision");

        var cap=Track(v);for(ulong i=1;i<=HeadHitTrack.Capacity;i++)cap.Offer(Local(v,i),100,false);
        check(!cap.Offer(Local(v,33),100,true),"full fixed32 ledger refuses novel hit without eviction");
        check(!cap.Offer(Local(v,34,220),220,true),"retention boundary120 remains occupied");
        check(cap.Offer(Local(v,35,221),221,true),"retention121 frees capacity for fresh identity");
        var ageTrack=Track(v);ageTrack.Step(100,true);ageTrack.Offer(Local(v,1,101),101,true);ageTrack.Step(101,true);
        var before=ageTrack.Sample(.25f);ageTrack.Step(101,true);check(before==ageTrack.Sample(.25f),"repeated same-world step never advances twice");
        ageTrack.Step(200,true);check(ageTrack.Sample(0)==default,"large forward gap expires old reaction immediately");
        ageTrack.Offer(Local(v,2,201),201,true);ageTrack.Step(190,true);check(ageTrack.Sample(0)==default,"backward world step cancels reaction");
        var previous=Track(v);previous.Step(100,true);previous.Offer(Local(v,1,110),110,true);previous.Step(110,true);
        var fresh=Track(v);fresh.Offer(Local(v,1,110),110,true);fresh.Step(110,true);
        check(previous.Sample(0)==fresh.Sample(0),"fresh hit after skipped steps is not charged time before its resolve");

        var separated=Track(v);separated.Step(1000,true);separated.Offer(Local(v,1,100),105,true,1005);separated.Step(1005,true);
        var authorityOnly=Track(v);authorityOnly.Offer(Local(v,1,100),105,true);
        check(separated.Sample(0)==authorityOnly.Sample(0),"independent presentation clock preserves canonical authority age without cross-clock subtraction");

        foreach(var direction in new[]{Vector3.Zero,new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),new Vector3(float.MaxValue,0,0),new Vector3(1e-6f,0,0)})
        {var t=Track(v);t.Offer(Local(v,1,direction:direction),100,true);check(t.Sample(0).Direction==Vector3.Zero,"missing/nonfinite/overflow/tiny direction safely becomes neutral");}
        var norm=Track(v);norm.Offer(Local(v,1,direction:new(3,0,4)),100,true);check((norm.Sample(0).Direction-new Vector3(.6f,0,.8f)).Length<1e-6,"finite direction normalizes independently of magnitude");
        var a=Track(v);var b=Track(v);a.Offer(Local(v,1),100,true);b.Offer(Local(v,1),100,true);
        for(uint step=100;step<125;step++)
        {
            a.Step(step,true);b.Step(step,true);var expected=b.Sample(.4f);
            foreach(var alpha in new[]{float.NaN,float.NegativeInfinity,-1,0,.1f,.5f,.9f,1,2,float.PositiveInfinity})
            {var pose=a.Sample(alpha);check(float.IsFinite(pose.Amount)&&pose.Amount is >=0 and <=1,"interpolated curve stays finite bounded for all alpha inputs");}
            for(int draw=0;draw<240;draw++)a.Sample(draw/240f);
            check(a.Sample(.4f)==expected,"60/120/144/240-style repeated draws do not change fixed-step state");
        }
        check(a.Sample(0)==default,"reaction settles to zero within bounded duration");
        var invalidDomain=Local(v,1) with{Event=new((HeadHitDomain)255,default,default,default,1,0)};
        check(!Track(v).Offer(invalidDomain,100,true),"unknown event domain refuses before consuming bounded storage");
        var hot=Track(v);hot.Offer(Local(v,1),100,true);for(int i=0;i<1000;i++)hot.Sample(.5f);
        long allocated=GC.GetAllocatedBytesForCurrentThread();float sum=0;for(int i=0;i<100_000;i++)sum+=hot.Sample((i%100)/100f).Amount;
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;check(sum>0&&allocated==0,"100k warmed draw samples allocate0B and retain nonvacuous pose");
        var duplicate=Local(v,1);for(int i=0;i<1000;i++)hot.Offer(duplicate,100,true);
        allocated=GC.GetAllocatedBytesForCurrentThread();int replayed=0;for(int i=0;i<100_000;i++)if(hot.Offer(duplicate,100,true))replayed++;
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;check(replayed==0&&allocated==0,"100k duplicate fact offers allocate0B and never replay");
    }
}
