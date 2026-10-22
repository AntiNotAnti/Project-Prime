using System;
using System.Linq;
using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

// Supplied pure accepted-cause fixtures only. No canonical death/HP, corpse rig,
// engine form simulation, replay transport/checkpoint or rendered visibility proof.
internal static class HeadDeathTrackChecks
{
    private static readonly DeathPresentationDefinition Classic=CosmeticCatalog.DeathPresentations[0];
    private static HeadHitVictim Victim()=>new(1,new object(),4,7,9,6,11,0);
    private static WorldBootstrapIdentity Bootstrap()=>new(new(7,9,3),2,5,90,1,1);
    private static ReplayShotFact Native()=>new(7,9,100,99,1,1,2,5,7,4,6,11,0,
        ReplayShotFactFlags.Headshot|ReplayShotFactFlags.Lethal|ReplayShotFactFlags.Direct,150,0,0,0,Vector3.One);
    private static CustomContactOutcome Custom()=>new(new(new(new(new(7,9,2,5,7),new(1,2,3,4),new(10),1),new(4,6,11),1,CustomImpactChannel.Body),Vector3.One,true),100,new(150,0,0,0,1));
    private static HeadHitFact Local(HeadHitVictim victim,ulong id=1,uint world=100,Vector3? direction=null)
        =>new(victim,new(HeadHitDomain.Local,default,default,default,id,0),world,direction??Vector3.UnitZ);
    private static HeadDeathTrack Track(HeadHitVictim victim){var t=new HeadDeathTrack();t.Bind(victim);return t;}
    private static bool Finite(HeadDeathPose pose)=>float.IsFinite(pose.Head)&&float.IsFinite(pose.Collapse)
        &&float.IsFinite(pose.Alpha)&&pose.Head is >=0 and <=1&&pose.Collapse is >=0 and <=1&&pose.Alpha is >=0 and <=1
        &&float.IsFinite(pose.Direction.X)&&float.IsFinite(pose.Direction.Y)&&float.IsFinite(pose.Direction.Z);
    internal static void Run(Action<bool,string> check)
    {
        var oldRandom=Rng.Current;var random=new MatchRandom();random.SetRng1(0x12345678);random.SetRng2(0x87654321);Rng.Current=random;
        try
        {
            var rngBefore=(random.Rng1,random.Rng2);var victim=Victim();var native=Native();var custom=Custom();var bootstrap=Bootstrap();
            check(HeadHitFact.TryNativeDeath(victim,native,out var nf)&&nf.Event.Domain==HeadHitDomain.Native&&nf.ResolveWorld==100,"actual lethal native head classification preserves canonical resolve and tagged source");
            check(HeadHitFact.TryCustomDeath(victim,bootstrap,custom,Vector3.UnitX,out var cf)&&cf.Event.Domain==HeadHitDomain.Custom&&cf.ResolveWorld==100,"actual lethal custom body removal classifies full typed victim/payment source");
            check(!HeadHitFact.TryNative(victim,native,out _)&&!HeadHitFact.TryCustom(victim,bootstrap,custom,Vector3.UnitX,out _),"lethal facts never enter surviving head-hit classification");
            foreach(var bad in new[]{native with{HealthAfter=1},native with{Damage=0},native with{Flags=ReplayShotFactFlags.Lethal},native with{Flags=native.Flags&~ReplayShotFactFlags.Lethal},native with{Flags=native.Flags|ReplayShotFactFlags.HalfturretTarget},native with{VictimLifeId=12},native with{VictimGeneration=7},native with{DamageEventId=0},native with{Weapon=255},native with{ImpactPoint=new(float.NaN,0,0)}})
                check(!HeadHitFact.TryNativeDeath(victim,bad,out var none)&&none==default,"surviving/body/zero/turret/malformed native cause cannot classify head death");
            foreach(var bad in new[]{custom with{Health=new(150,1,0,0,1)},custom with{Health=new(0,0,20,0,1)},custom with{Disposition=CustomContactDisposition.TradeDenied},custom with{Contact=custom.Contact with{Headshot=false}},custom with{Contact=custom.Contact with{Key=custom.Contact.Key with{Channel=CustomImpactChannel.Halfturret}}},custom with{Contact=custom.Contact with{Key=custom.Contact.Key with{Victim=new(4,6,12)}}},custom with{Contact=custom.Contact with{Point=new(0,float.PositiveInfinity,0)}},custom with{Health=new(150,0,0,0,0)}})
                check(!HeadHitFact.TryCustomDeath(victim,bootstrap,bad,Vector3.UnitX,out var none)&&none==default,"protected/trade/body/turret/malformed custom cause cannot classify head death");
            var oldSource=custom with{Contact=custom.Contact with{Key=custom.Contact.Key with{Action=custom.Contact.Key.Action with{Owner=new(7,9,2,5,6)}}}};
            check(HeadHitFact.TryCustomDeath(victim,bootstrap,oldSource,Vector3.UnitX,out _),"old admitted shooter life does not invalidate exact current victim death");
            foreach(var wrong in new[]{victim with{Scene=2},victim with{Actor=new object()},victim with{Slot=3},victim with{Match=8},victim with{Epoch=10},victim with{Generation=7},victim with{Life=12},victim with{Timeline=1}})
            {var t=Track(victim);check(!t.Accept(Local(wrong),100,1000,true,true)&&!t.HasCause,"every wrong scene/actor/slot/stream/life/timeline refuses without consuming current life");}
            var missing=Track(default);check(!missing.Accept(Local(victim),100,1000,true,true)&&!missing.HasCause,"unbound track cannot acquire a death cause");
            foreach(var invalid in new[]{default(HeadHitEvent),new HeadHitEvent((HeadHitDomain)255,default,default,default,1,0),nf.Event with{Bootstrap=bootstrap},cf.Event with{Native=nf.Event.Native}})
            {var t=Track(victim);check(!t.Accept(Local(victim) with{Event=invalid},100,1000,true,true)&&!t.HasCause,"invalid or cross-domain cause refuses atomically");}

            foreach(bool head in new[]{false,true})foreach(bool eligible in new[]{false,true})
            {
                var t=Track(victim);var fact=Local(victim);
                check(t.Accept(fact,100,1000,head,eligible)==(head&&eligible)&&t.HasCause&&t.Event==fact.Event&&!t.BeganDead&&t.Sample(0,Classic)==default,"first valid body/denied/head cause latches once and never renders a living actor");
                check(!t.Accept(Local(victim,2),100,1000,true,true)&&t.Event==fact.Event,"a later contradictory headshot cannot upgrade first cause");
                t.Step(1001,false,true);check(t.Sample(.5f,Classic)==default&&!t.BeganDead,"accepted cause before health snapshot remains pictureless while alive");
                t.Step(1002,true,true);check(t.BeganDead&&t.Sample(0,Classic).Active==(head&&eligible),"only canonical dead observation exposes eligible first head cause");
                t.Bind(victim);check(t.HasCause&&!t.Accept(Local(victim,3),100,1002,true,true),"same-life bind cannot reset or replace cause");
            }
            var once=Track(victim);once.Accept(nf,100,1000,true,true);once.Step(1004,true,true);var before=once.Sample(0,Classic);
            foreach(var candidate in new[]{nf,cf,Local(victim,2)})check(!once.Accept(candidate,100,1004,true,true)&&once.Sample(0,Classic)==before,"native/custom/local retries or alternate cause cannot restart same victim life");
            once.Cancel();once.Step(1005,true,true);check(once.HasCause&&once.Sample(0,Classic)==default&&!once.Accept(Local(victim,3),100,1005,true,true),"explicit cancellation retains cause and cannot revive on later eligible samples");
            var next=victim with{Life=12};once.Bind(next);check(!once.HasCause&&once.Event==default&&!once.Accept(Local(victim,4),100,1006,true,true)&&once.Accept(Local(next,1),100,1006,true,true),"new actual victim life clears cause while old life remains refused");

            var suppressed=Track(victim);var suppressedFact=Local(victim);
            suppressed.Suppress(suppressedFact);suppressed.Step(1000,true,true);
            check(suppressed.HasCause&&suppressed.Event==suppressedFact.Event&&suppressed.Sample(0,Classic)==default,"expired pending cause is consumed without ever exposing a corpse reaction");
            check(!suppressed.Accept(Local(victim,2),100,1000,true,true),"delayed alternate head cause cannot reopen timed-out same-life corpse");
            suppressed.Bind(victim);check(suppressed.HasCause,"same-life bind retains pending timeout suppression");
            suppressed.Bind(next);check(!suppressed.HasCause&&suppressed.Accept(Local(next),100,1000,true,true),"new actual life clears timeout suppression");
            foreach(var invalid in new[]{Local(default),Local(victim) with{Event=default},Local(victim with{Life=12})})
            {var t=Track(victim);t.Suppress(invalid);check(!t.HasCause&&t.Event==default,"invalid or foreign pending suppression cannot consume current valid life");}
            var unboundSuppressed=Track(default);unboundSuppressed.Suppress(Local(default));
            check(!unboundSuppressed.HasCause&&unboundSuppressed.Event==default,"unbound/default victim cannot acquire even a valid local suppression event");

            foreach(uint age in new uint[]{0,1,18}){var t=Track(victim);check(t.Accept(Local(victim),100+age,1000,true,true)&&t.HasCause,"live cause within inclusive18 authoritative ticks accepted");}
            foreach(uint age in new uint[]{19,42,43}){var t=Track(victim);check(!t.Accept(Local(victim),100+age,1000,true,true)&&!t.HasCause,"late live cause cannot be relabeled as fresh death");}
            check(!Track(victim).Accept(Local(victim,world:101),100,1000,true,true),"future cause waits in adapter instead of changing track clock");
            var pending=Track(victim);pending.Accept(Local(victim),100,1000,true,true);pending.Step(1018,false,true);pending.Step(1018,true,true);
            check(pending.Sample(0,Classic).Active&&pending.BeganDead,"HP snapshot at inclusive18 presentation ticks still exposes bounded pose");
            pending=Track(victim);pending.Accept(Local(victim),100,1000,true,true);pending.Step(1019,false,true);pending.Step(1020,true,true);
            check(pending.HasCause&&pending.Sample(0,Classic)==default,"late HP snapshot after pending lifetime cannot revive timed-out head cause");
            var wrapped=Track(victim);wrapped.Accept(Local(victim,world:uint.MaxValue-2),1,uint.MaxValue-4,true,true);wrapped.Step(0,true,true);
            var plain=Track(victim);plain.Accept(Local(victim,world:100),104,1000,true,true);plain.Step(1005,true,true);
            check(wrapped.Sample(.5f,Classic)==plain.Sample(.5f,Classic),"canonical and presentation clocks wrap independently without age reversal");
            var rewind=Track(victim);rewind.Accept(Local(victim),100,1000,true,true);rewind.Step(999,true,true);rewind.Step(1001,true,true);
            check(rewind.HasCause&&rewind.Sample(0,Classic)==default,"backward presentation clock cancels and cannot restart consumed cause");
            var revive=Track(victim);revive.Accept(Local(victim),100,1000,true,true);revive.Step(1002,true,true);revive.Step(1003,false,true);revive.Step(1004,true,true);
            check(revive.BeganDead&&revive.Sample(0,Classic)==default,"same-life positive-health resurrection permanently cancels corpse pose");
            foreach(string reason in new[]{"reduced motion","morph","frozen","quality off","spectator"})
            {var t=Track(victim);t.Accept(Local(victim),100,1000,true,true);t.Step(1004,true,true);t.Step(1005,true,false);t.Step(1006,true,true);check(t.HasCause&&t.Sample(0,Classic)==default,"caller ineligibility permanently cancels without cause replay: "+reason);}

            var replay=Track(victim);check(replay.Accept(nf,130,1000,true,true,historical:true),"native recorded cause reconstruction may cover complete short42-tick reaction");replay.Step(1000,true,true);
            var linear=Track(victim);linear.Accept(nf,100,1000,true,true);linear.Step(1030,true,true);
            check(replay.Sample(.4f,Classic)==linear.Sample(.4f,Classic),"late native recording reconstruction equals linear pose at same cause age");
            replay=Track(victim);check(replay.Accept(nf,143,1000,true,true,historical:true)&&replay.HasCause,"historical cause beyond Classic42 remains latched for longer nonclassic body presentation");replay.Step(1000,true,true);
            check(replay.Sample(0,Classic)==default,"historical Classic remains finished after42 even though source retention is longer");
            replay=Track(victim);check(!replay.Accept(nf,245,1000,true,true,historical:true)&&!replay.HasCause,"recorded cause beyond bounded144 reconstruction ticks is refused");
            check(HeadDeathTrack.Duration==42&&HeadDeathTrack.MaximumDuration==144,"Classic42 and maximum144 lifetimes remain explicit bounded contracts");
            foreach(var definition in CosmeticCatalog.DeathPresentations.Where(x=>x.WireId!=0))
            {
                var reconstructed=Track(victim);check(reconstructed.Accept(nf,180,2000,true,true,historical:true),"native historical age80 cause retains exact source for "+definition.Key);reconstructed.Step(2000,true,true);
                var forward=Track(victim);forward.Accept(nf,100,1000,true,true);forward.Step(1080,true,true);
                var pose80=reconstructed.Sample(.5f,definition);
                check(pose80==forward.Sample(.5f,definition)&&pose80.Active&&pose80.Alpha==1&&pose80.Head==0,"historical age80 equals linear held nonclassic corpse pose: "+definition.Key);
                var seekVictim=victim with{Scene=98,Actor=new object(),Timeline=77};var sought=Track(seekVictim);
                sought.Accept(nf with{Victim=seekVictim,ResolveWorld=500},580,3000,true,true,historical:true);sought.Step(3000,true,true);
                check(sought.Sample(.5f,definition)==pose80&&sought.Event==nf.Event,"historical native source/variant remains stable across seek-only context and clock substitutions: "+definition.Key);
            }
            foreach(var fact in new[]{nf,cf,Local(victim)})
            {
                var t=Track(victim);t.Accept(fact,100,1000,true,true);t.Step(1004,true,true);int side=t.Sample(0,Classic).Side;
                int expected=fact.Event.Domain==HeadHitDomain.Native?1:-1;
                check(side==expected,"stable variant matches independent fixed-framing golden for "+fact.Event.Domain);
                var copy=victim with{Scene=99,Actor=new object(),Timeline=88};var r=Track(copy);r.Accept(fact with{Victim=copy,ResolveWorld=5000},5000,8000,true,true);r.Step(8004,true,true);
                check(r.Sample(0,Classic).Side==side,"scene/reference/seek-clock substitutions preserve recorded variant without process hash or RNG");
            }
            var sides=new bool[2];for(ulong id=1;id<=64;id++){var t=Track(victim);t.Accept(Local(victim,id),100,1000,true,true);t.Step(1004,true,true);sides[t.Sample(0,Classic).Side==1?1:0]=true;}
            check(sides.All(x=>x),"deterministic source identities exercise both bounded collapse sides");
            var normalized=Track(victim);normalized.Accept(Local(victim,direction:new(3,0,4)),100,1000,true,true);normalized.Step(1004,true,true);
            check((normalized.Sample(0,Classic).Direction-new Vector3(.6f,0,.8f)).Length<1e-6,"finite shot direction normalizes before cosmetic pose");
            foreach(var direction in new[]{Vector3.Zero,new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),new Vector3(float.MaxValue,0,0),new Vector3(1e-6f,0,0)})
            {var t=Track(victim);t.Accept(Local(victim,direction:direction),100,1000,true,true);t.Step(1004,true,true);check(t.Sample(0,Classic).Direction==Vector3.Zero,"missing/nonfinite/overflow/tiny cause direction is neutral");}

            check(CosmeticCatalog.DeathPresentations.Count==8,"all eight existing death choices including Classic covered");
            check(CosmeticCatalog.DeathPresentations.Where(d=>d.WireId!=0).All(d=>float.IsFinite(d.Duration)&&d.Duration>0&&d.Duration*60<HeadDeathTrack.MaximumDuration),
                "bounded144 track exceeds all current nonclassic body durations; it does not own or extend their visibility");
            foreach(var definition in CosmeticCatalog.DeathPresentations)
            {
                var sampled=Track(victim);var untouched=Track(victim);sampled.Accept(Local(victim),100,1000,true,true);untouched.Accept(Local(victim),100,1000,true,true);
                for(uint age=0;age<=145;age++)
                {
                    sampled.Step(1000+age,true,true);untouched.Step(1000+age,true,true);var expected=untouched.Sample(.4f,definition);
                    foreach(float alpha in new[]{float.NaN,float.NegativeInfinity,-1,0,.5f,1,2,float.PositiveInfinity})check(Finite(sampled.Sample(alpha,definition)),"all catalog curves remain finite bounded for draw alpha: "+definition.Key);
                    for(int draw=0;draw<240;draw++)sampled.Sample(draw/240f,definition);
                    check(sampled.Sample(.4f,definition)==expected,"extra60/120/144/240-style draws never advance death lifetime: "+definition.Key);
                    if(age>=12&&age<144&&definition.WireId!=0)
                    {
                        float held=definition.PoseStyle==DeathPoseStyle.ShatterFreeze ? 0 : definition.PoseStyle==DeathPoseStyle.Float ? .15f : definition.PoseStyle==DeathPoseStyle.KneelCollapse ? .25f : 1;
                        check(expected.Active&&expected.Alpha==1&&expected.Head==0&&MathF.Abs(expected.Collapse-held)<1e-6f,
                            "nonclassic collapse stays held after kick settles; caller owns existing body visibility: "+definition.Key);
                    }
                    if(age>=42&&definition.WireId==0)check(!expected.Active,"Classic overlay remains finished after42 ticks");
                    if(age>=144)check(!expected.Active,"all overlays end at bounded maximum144 without extending caller visibility");
                }
            }
            var longest=Track(victim);longest.Accept(nf,244,3000,true,true,historical:true);longest.Step(3000,true,true);
            check(longest.HasCause&&CosmeticCatalog.DeathPresentations.All(d=>longest.Sample(0,d)==default),"inclusive historical144 may retain dedupe cause but exposes no expired pose");
            var boundary=Track(victim);boundary.Accept(nf,100,1000,true,true);boundary.Step(1143,true,true);
            foreach(var definition in CosmeticCatalog.DeathPresentations.Where(x=>x.WireId!=0))
                check(boundary.Sample(.999f,definition).Active&&!boundary.Sample(1,definition).Active,"nonclassic fractional drawing stops at144 without advancing track: "+definition.Key);
            var curve=Track(victim);curve.Accept(Local(victim),100,1000,true,true);curve.Step(1004,true,true);check(curve.Sample(0,Classic).Head==1,"classic head kick reaches bounded peak at tick4");
            curve.Step(1012,true,true);check(curve.Sample(0,Classic).Head==0&&curve.Sample(0,Classic).Collapse==1,"classic kick settles while collapse completes by tick12");
            curve.Step(1036,true,true);check(MathF.Abs(curve.Sample(0,Classic).Alpha-.5f)<1e-6,"classic retained visual body fades tohalf at tick36");
            var shatter=CosmeticCatalog.DeathPresentations.Single(x=>x.PoseStyle==DeathPoseStyle.ShatterFreeze);var st=Track(victim);st.Accept(Local(victim),100,1000,true,true);st.Step(1004,true,true);
            check(st.Sample(0,shatter).Head==0&&st.Sample(0,shatter).Collapse==0&&st.Sample(0,shatter).Alpha==1,"shatter policy retains its existing freeze pose without additive skeletal collapse");
            var hot=Track(victim);hot.Accept(Local(victim),100,1000,true,true);hot.Step(1005,true,true);for(int i=0;i<1000;i++)hot.Sample(.5f,Classic);
            long allocated=GC.GetAllocatedBytesForCurrentThread();float sum=0;for(int i=0;i<100_000;i++)sum+=hot.Sample((i%100)/100f,Classic).Collapse;
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;check(sum>0&&allocated==0,"100k warmed death draw samples allocate0B with nonvacuous collapse");
            var duplicate=Local(victim);for(int i=0;i<1000;i++)hot.Accept(duplicate,100,1000,true,true);
            allocated=GC.GetAllocatedBytesForCurrentThread();int repeated=0;for(int i=0;i<100_000;i++)if(hot.Accept(duplicate,100,1000,true,true))repeated++;
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;check(repeated==0&&allocated==0,"100k repeated same-life death facts allocate0B and never replay");
            check(ReferenceEquals(Rng.Current,random)&&(random.Rng1,random.Rng2)==rngBefore,"classification/variant/sample/cancel paths leave both gameplay RNG streams untouched");
        }
        finally{Rng.Current=oldRandom;}
    }
}
