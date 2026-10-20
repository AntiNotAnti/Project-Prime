using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

// Declared CPU integration seam: real Scene dispatch, declared actor health/form,
// lifecycle and clocks. No actual damage, authority transport, asset/GL or rig claim.
internal static class HeadHitSceneChecks
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static readonly MethodInfo Owner=typeof(Scene).GetMethod("HeadHitOwner",All)!;
    private static readonly MethodInfo Accept=typeof(Scene).GetMethod("ModAcceptNativeHeadHit",All)!;
    private static readonly MethodInfo Step=typeof(Scene).GetMethod("StepHeadHitPresentation",All)!;
    private static readonly MethodInfo Release=typeof(Scene).GetMethod("ReleaseHeadHitPresentation",All)!;
    private static readonly FieldInfo Pending=typeof(Scene).GetField("_pendingHeadHits",All)!;
    private static readonly FieldInfo Slots=typeof(Scene).GetField("_headHits",All)!;
    private static readonly FieldInfo SceneFrame=typeof(Scene).GetField("_frameCount",All)!;
    private static readonly FieldInfo World=typeof(NetSession).GetField("_lastSnapshotFrame",All)!;
    private static object? Read(object value,string name)=>value.GetType().GetProperty(name,All)?.GetValue(value)
        ??value.GetType().GetField(name,All)?.GetValue(value);
    private static int Queued(Scene scene)=>Pending.GetValue(scene) is Array values
        ?values.Cast<object>().Count(x=>Read(Read(x,"Fact")!,"Event") is {} e&&Convert.ToByte(Read(e,"Domain"))!=0):0;
    private static object? Slot(Scene scene)=>((Array?)Slots.GetValue(scene))?.GetValue(0);
    private static bool Active(Scene scene)=>Slot(scene) is {} slot&&Read(slot,"Track") is {} t&&(bool)Read(t,"_active")!;
    private static bool Terminated(Scene scene)=>Slot(scene) is {} slot&&(bool)Read(slot,"Terminated")!;
    private static ReplayShotFact Fact(uint shot=1,uint world=105,ushort life=7)=>new(1,1,world,world-1,shot,1,
        1,8,3,0,5,life,0,ReplayShotFactFlags.Headshot|ReplayShotFactFlags.Direct,25,75,0,0,Vector3.One);
    internal static void Run(Action<bool,string> check)
    {
        check(!NetSession.Active,"CPU head-hit scene begins without a live network owner");
        if(NetSession.Active)return;
        var role=typeof(NetSession).GetProperty(nameof(NetSession.Role),All)!;
        var match=typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch),All)!;
        var netFrame=typeof(NetSession).GetProperty(nameof(NetSession.NetFrame),All)!;
        var arrived=typeof(NetSession).GetProperty(nameof(NetSession.SnapshotArrived),All)!;
        var hasSnapshot=typeof(NetSession).GetField("_hasSnapshot",All)!;
        var headless=typeof(Headless).GetProperty(nameof(Headless.Active),All)!;
        var runtime=typeof(HudProfiles).GetField("_runtime",All)!;
        var lifecycle=(NetLifecycleTracker[])typeof(NetPlayerLifecycle).GetField("_slots",All)!.GetValue(null)!;
        var oldSlots=(NetLifecycleTracker[])lifecycle.Clone();
        var flags1=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1),All)!;
        var flags2=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2),All)!;
        var frozen=typeof(PlayerEntity).GetField("_frozenTimer",All)!;
        var oldRole=role.GetValue(null);var oldMatch=match.GetValue(null);var oldWorld=World.GetValue(null);
        var oldNetFrame=netFrame.GetValue(null);var oldArrived=arrived.GetValue(null);var oldHasSnapshot=hasSnapshot.GetValue(null);
        bool oldHeadless=Headless.Active,oldVisible=RenderOptions.ShowCustomCosmetics;
        var oldQuality=RenderOptions.CosmeticQuality;var oldHud=runtime.GetValue(null);
        var oldGame=GameState.Current;var oldPlayers=PlayerEntity.LegacyRegistry;var oldRandom=Rng.Current;
        Scene? scene=null;
        try
        {
            headless.SetValue(null,false);RenderOptions.ShowCustomCosmetics=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.High;
            runtime.SetValue(null,new HudRuntimeProfile(new HudProfile{ReduceMotion=false}));
            scene=new(new Vector2i(256,192),SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{},initializeRuntime:false);
            var player=scene.Players.Main;var actor=player;
            role.SetValue(null,NetRole.Host);match.SetValue(null,null);
            for(int i=0;i<lifecycle.Length;i++)lifecycle[i]=new();
            lifecycle[0].SetOccupant(5);for(int i=0;i<7;i++)lifecycle[0].BeginLife();
            void Clock(uint world,ulong step){World.SetValue(null,world);SceneFrame.SetValue(scene,step);}
            void Tick(uint world,ulong step){Clock(world,step);Step.Invoke(scene,null);}
            void Offer(ReplayShotFact fact)=>Accept.Invoke(scene,new object?[]{fact,null});
            void Eligible()
            {player.Health=100;player.LoadFlags=LoadFlags.Active|LoadFlags.Spawned;flags1.SetValue(player,(PlayerFlags1)0);flags2.SetValue(player,(PlayerFlags2)0);frozen.SetValue(player,(ushort)0);RenderOptions.ShowCustomCosmetics=true;runtime.SetValue(null,new HudRuntimeProfile(new HudProfile{ReduceMotion=false}));}
            void Reset()
            {Release.Invoke(scene,null);player=actor;scene.Players.Values[0]=actor;Eligible();player.Position=Vector3.Zero;Clock(100,1000);Step.Invoke(scene,null);}
            Reset();Offer(Fact());
            check(Queued(scene)==1&&!Active(scene),"future accepted head hit queues normalized identity without prematurely starting pose");
            Offer(Fact());check(Queued(scene)==1,"duplicate future fact occupies one bounded queue cell");
            Tick(104,1001);check(Queued(scene)==1&&!Active(scene),"pending fact waits for authoritative world rather than retagging arrival");
            Tick(105,1002);check(Queued(scene)==0&&Active(scene)&&player.Health==100,"clock catchup consumes pending fact and starts cosmetic without changing health");
            Tick(105,1003);Offer(Fact());check(Queued(scene)==0&&Active(scene),"post-catchup exact retry cannot create a future queue entry");
            Reset();Offer(Fact());Tick(100,1019);Tick(105,1020);
            check(Queued(scene)==0&&!Active(scene),"future queue expires after18 presentation steps while authority world remains stalled");
            Reset();for(uint i=1;i<=65;i++)Offer(Fact(i));
            check(Queued(scene)==64,"future queue capacity64 refuses65th novel identity without eviction");
            Tick(105,1001);check(Queued(scene)==0&&Active(scene),"bounded catchup drains queued cells even beyond fixed reaction ledger capacity");

            foreach(var form in new[]{PlayerFlags1.Morphing,PlayerFlags1.AltForm,PlayerFlags1.Unmorphing})
            {
                Reset();flags1.SetValue(player,form);Offer(Fact());flags1.SetValue(player,(PlayerFlags1)0);Tick(105,1001);Offer(Fact());
                check(!Active(scene)&&Queued(scene)==0,"ineligible-at-arrival form cannot reactivate after recovery or duplicate "+form);
                Reset();Offer(Fact());flags1.SetValue(player,form);Tick(100,1001);flags1.SetValue(player,(PlayerFlags1)0);Tick(105,1002);Offer(Fact());
                check(!Active(scene)&&Queued(scene)==0,"form cancellation fences already pending future fact through recovery "+form);
            }
            foreach(string reason in new[]{"freeze","reduce-motion","cosmetics-off","spectator","death"})
            {
                Reset();Offer(Fact());
                if(reason=="freeze")frozen.SetValue(player,(ushort)1);
                if(reason=="reduce-motion")runtime.SetValue(null,new HudRuntimeProfile(new HudProfile{ReduceMotion=true}));
                if(reason=="cosmetics-off")RenderOptions.ShowCustomCosmetics=false;
                if(reason=="spectator")flags2.SetValue(player,PlayerFlags2.Spectating);
                if(reason=="death")player.Health=0;
                Tick(100,1001);Eligible();Tick(105,1002);Offer(Fact());
                check(!Active(scene)&&Queued(scene)==0,"pending cancellation does not replay after eligibility recovers: "+reason);
            }
            Reset();Offer(Fact());player.Position=new(5,0,0);Tick(100,1001);Tick(105,1002);Offer(Fact());
            check(!Active(scene)&&Queued(scene)==0,"teleport cancellation fences pending fact captured before body discontinuity");
            Reset();Offer(Fact());lifecycle[0].BeginLife();Tick(105,1001);
            check(!Active(scene)&&Queued(scene)==0,"new native life discards previous life pending fact");
            Offer(Fact(2,105,8));check(Active(scene),"current new-life head hit remains eligible after old-life pending discard");
            lifecycle[0]=new();lifecycle[0].SetOccupant(5);for(int i=0;i<7;i++)lifecycle[0].BeginLife();
            Reset();Offer(Fact());var replacement=new PlayerEntity(0,scene){Health=100,LoadFlags=LoadFlags.Active|LoadFlags.Spawned};
            scene.Players.Values[0]=replacement;player=replacement;Tick(105,1001);
            check(!Active(scene)&&Queued(scene)==0,"same slot/generation/life replacement actor reference cannot inherit queued pose");

            Reset();Offer(Fact());Offer(Fact(2,106) with{Flags=ReplayShotFactFlags.Headshot|ReplayShotFactFlags.Lethal,Damage=100,HealthAfter=0});
            Tick(106,1001);Offer(Fact(3,106));
            check(Terminated(scene)&&!Active(scene)&&Queued(scene)==0&&player.Health==100,"accepted lethal fact fences pending and later same-life nonlethal facts before health snapshot arrives");
            lifecycle[0].BeginLife();Tick(107,1002);Offer(Fact(4,107,8));
            check(!Terminated(scene)&&Active(scene),"real context life transition clears lethal fence for new-life fact");
            lifecycle[0]=new();lifecycle[0].SetOccupant(5);for(int i=0;i<7;i++)lifecycle[0].BeginLife();
            Reset();check(!DemoPlayback.IsActive,"native batch guard fixture has no demo suppression shortcut");byte[] batch=new byte[ReplayShotFactPacket.Size*2];ReplayShotFactPacket.Write(Fact(),batch);ReplayShotFactPacket.Write(Fact(2) with{Weapon=255},batch.AsSpan(ReplayShotFactPacket.Size));
            ReplayCapture.AcceptedShotFact(batch,100);
            check(Queued(scene)==0&&!Active(scene),"native ingress validates complete batch before any first-row visual mutation");
            ReplayCapture.AcceptedShotFact(batch.AsSpan(0,batch.Length-1),100);
            check(Queued(scene)==0&&!Active(scene),"truncated native fact batch cannot queue a visual prefix");
            Offer(Fact() with{MatchId=2});Offer(Fact() with{AuthorityEpoch=2});Offer(Fact() with{VictimGeneration=6});Offer(Fact() with{VictimLifeId=8});
            check(Queued(scene)==0&&!Active(scene),"exact scene stream/victim lifecycle guards reject foreign supplied facts");
            Reset();role.SetValue(null,NetRole.Client);match.SetValue(null,new MatchStatePacket{MatchId=1,AuthorityEpoch=1});
            netFrame.SetValue(null,200u);arrived.SetValue(null,200u);hasSnapshot.SetValue(null,false);
            Tick(100,1000);Offer(Fact(1,100));
            check(!Active(scene)&&Queued(scene)==0,"client with no authority snapshot cannot start a reaction from supplied accepted fact");
            hasSnapshot.SetValue(null,true);Offer(Fact(2,100));
            check(Active(scene),"client with current authority snapshot accepts fresh normalized fact");
            netFrame.SetValue(null,219u);Tick(100,1001);
            check(!Active(scene),"snapshot arrival age19 cancels active reaction even while authority resolve clock is frozen");
            Offer(Fact(3));check(Queued(scene)==1&&!Active(scene),"stale snapshot future fact can occupy bounded dedupe cell but is ineligible at arrival");
            arrived.SetValue(null,219u);Tick(105,1002);Offer(Fact(3));
            check(Queued(scene)==0&&!Active(scene),"fresh snapshot cannot revive fact suppressed during stale-world arrival");
            netFrame.SetValue(null,237u);Tick(106,1003);Offer(Fact(4,106));
            check(NetSession.SnapshotAge==18&&Active(scene),"inclusive snapshot age18 still permits a new canonical reaction");
            role.SetValue(null,NetRole.Host);match.SetValue(null,null);Reset();
            Offer(Fact());Release.Invoke(scene,null);
            check(Slots.GetValue(scene)==null&&Pending.GetValue(scene)==null,"scene presentation release clears all active and pending owned state");
            check(player.Health==100&&scene.FrameCount==1000,"CPU adapter never changes actor health or declared presentation clock");
        }
        finally
        {
            if(scene!=null)Release.Invoke(scene,null);
            for(int i=0;i<lifecycle.Length;i++)lifecycle[i]=oldSlots[i];
            role.SetValue(null,oldRole);match.SetValue(null,oldMatch);World.SetValue(null,oldWorld);
            netFrame.SetValue(null,oldNetFrame);arrived.SetValue(null,oldArrived);hasSnapshot.SetValue(null,oldHasSnapshot);
            runtime.SetValue(null,oldHud);headless.SetValue(null,oldHeadless);
            RenderOptions.ShowCustomCosmetics=oldVisible;RenderOptions.CosmeticQuality=oldQuality;
            GameState.Current=oldGame;PlayerEntity.LegacyRegistry=oldPlayers;Rng.Current=oldRandom;
        }
        check(ReferenceEquals(GameState.Current,oldGame)&&ReferenceEquals(PlayerEntity.LegacyRegistry,oldPlayers)
            &&ReferenceEquals(Rng.Current,oldRandom)&&Headless.Active==oldHeadless&&Equals(role.GetValue(null),oldRole)
            &&Equals(match.GetValue(null),oldMatch)&&Equals(World.GetValue(null),oldWorld)
            &&Equals(netFrame.GetValue(null),oldNetFrame)&&Equals(arrived.GetValue(null),oldArrived)&&Equals(hasSnapshot.GetValue(null),oldHasSnapshot),"CPU scene test restores foreground/global role/world/lifecycle facades");
    }
}
