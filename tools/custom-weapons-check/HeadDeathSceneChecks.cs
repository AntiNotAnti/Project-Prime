using System;
using System.Reflection;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

// CPU adapter integration only: real Scene methods with explicitly supplied accepted
// native facts, actor HP/form, native lifecycle and two clocks. No damage, network,
// GL/body rig or passive-replay simulation is performed by this fixture.
internal static class HeadDeathSceneChecks
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static readonly MethodInfo Step=typeof(Scene).GetMethod("StepHeadDeathPresentation",All)!;
    private static readonly MethodInfo Accept=typeof(Scene).GetMethod("AcceptNativeHeadDeath",All)!;
    private static readonly MethodInfo Release=typeof(Scene).GetMethod("ReleaseHeadDeathPresentation",All)!;
    private static readonly FieldInfo Slots=typeof(Scene).GetField("_headDeaths",All)!;
    private static readonly FieldInfo SceneFrame=typeof(Scene).GetField("_frameCount",All)!;
    private static readonly FieldInfo World=typeof(NetSession).GetField("_lastSnapshotFrame",All)!;
    private static object? Read(object value,string name)=>value.GetType().GetProperty(name,All)?.GetValue(value)
        ??value.GetType().GetField(name,All)?.GetValue(value);
    private static object? Slot(Scene scene)=>((Array?)Slots.GetValue(scene))?.GetValue(0);
    private static HeadDeathTrack? Track(Scene scene)=>Slot(scene) is {} slot?(HeadDeathTrack?)Read(slot,"Track"):null;
    private static bool Queued(Scene scene)=>Slot(scene) is {} slot&&((HeadHitFact)Read(slot,"Pending")!).Event.IsValid;
    private static bool Active(Scene scene,PlayerEntity player)=>scene.ModHeadDeathPose(player,CosmeticCatalog.DeathPresentations[0]).Active;
    private static ReplayShotFact Fact(uint shot=1,uint world=105,ushort life=7,bool head=true)=>new(1,1,world,world-1,shot,1,
        1,8,3,0,5,life,(byte)BeamType.Imperialist,ReplayShotFactFlags.Lethal|ReplayShotFactFlags.Direct|(head?ReplayShotFactFlags.Headshot:0),150,0,0,0,Vector3.One);
    internal static void Run(Action<bool,string> check)
    {
        check(!NetSession.Active,"CPU head-death scene begins without live network ownership");if(NetSession.Active)return;
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
            var actor=scene.Players.Main;var player=actor;
            role.SetValue(null,NetRole.Host);match.SetValue(null,null);
            for(int i=0;i<lifecycle.Length;i++)lifecycle[i]=new();
            void Life7(){lifecycle[0]=new();lifecycle[0].SetOccupant(5);for(int n=0;n<7;n++)lifecycle[0].BeginLife();}
            void Clock(uint world,ulong step){World.SetValue(null,world);SceneFrame.SetValue(scene,step);}
            void Tick(uint world,ulong step){Clock(world,step);Step.Invoke(scene,null);}
            void Offer(ReplayShotFact fact)=>Accept.Invoke(scene,new object?[]{player,fact,null});
            void Eligible()
            {player.Health=100;player.LoadFlags=LoadFlags.Active|LoadFlags.Spawned;flags1.SetValue(player,(PlayerFlags1)0);flags2.SetValue(player,(PlayerFlags2)0);frozen.SetValue(player,(ushort)0);RenderOptions.ShowCustomCosmetics=true;runtime.SetValue(null,new HudRuntimeProfile(new HudProfile{ReduceMotion=false}));}
            void Reset(bool alive=true)
            {Release.Invoke(scene,null);player=actor;scene.Players.Values[0]=actor;Life7();Eligible();player.Position=Vector3.Zero;if(!alive)player.Health=0;Clock(100,1000);Step.Invoke(scene,null);}
            Reset();Offer(Fact());
            check(Queued(scene)&&Track(scene) is {HasCause:false}&&!Active(scene,player),"future lethal fact queues exact cause without early corpse reaction");
            Offer(Fact(2));check(Queued(scene)&&Track(scene) is {HasCause:false},"one pending cause per victim life refuses alternate later fact");
            Tick(104,1001);check(Queued(scene)&&!Active(scene,player),"future cause waits for authority world rather than presentation clock");
            Tick(105,1002);check(!Queued(scene)&&Track(scene) is {HasCause:true,BeganDead:false}&&!Active(scene,player)&&player.Health==100,"authority catchup consumes pending cause while still-living actor stays untouched");
            player.Health=0;Tick(105,1003);var original=scene.ModHeadDeathPose(player,CosmeticCatalog.DeathPresentations[0]);
            check(original.Active&&scene.ModHeadDeathAnchor(player,out _,out _),"first dead snapshot exposes and anchors eligible accepted cause");
            Offer(Fact(3));check(scene.ModHeadDeathPose(player,CosmeticCatalog.DeathPresentations[0])==original,"later same-life cause cannot restart or change variant");
            Reset();Offer(Fact());Tick(100,1018);
            check(Queued(scene)&&Track(scene) is {HasCause:false},"pending authority catchup deadline includes18 presentation steps");
            Tick(100,1019);check(!Queued(scene)&&Track(scene) is {HasCause:true},"pending timeout consumes first cause identity without a corpse pose");
            player.Health=0;Tick(105,1020);Offer(Fact(2,105));
            check(!Active(scene,player)&&Track(scene)!.Event.Native.ShotId==1,"delayed retry or different old cause cannot reopen timed-out corpse");

            Reset();Offer(Fact());player.Health=0;frozen.SetValue(player,(ushort)1);Tick(105,1001);
            check(!Queued(scene)&&Track(scene) is {HasCause:true}&&!Active(scene,player),"freeze arriving with lethal HP snapshot suppresses queued reaction despite previously unfrozen alive sample");
            frozen.SetValue(player,(ushort)0);Tick(105,1002);Offer(Fact(2,105));
            check(!Active(scene,player),"thaw cannot resurrect consumed frozen death cause");
            Reset();player.Health=0;frozen.SetValue(player,(ushort)1);Clock(105,1001);Offer(Fact());
            check(Track(scene) is {HasCause:true}&&!Active(scene,player),"current frozen corpse blocks immediate accepted head cause");
            Reset();player.Health=0;player.Position=new(5,0,0);Tick(105,1001);Offer(Fact());
            check(Slot(scene) is {} ds&&(bool)Read(ds,"DeadBlocked")!&&!Active(scene,player)&&Track(scene) is {HasCause:true},"dead-body teleport before first fact irreversibly blocks same-life overlay");
            Reset();Offer(Fact());player.Health=0;player.Position=new(5,0,0);Tick(105,1001);player.Position=Vector3.Zero;Tick(105,1002);Offer(Fact(2,105));
            check(!Active(scene,player),"teleport cancellation preserves suppression across return and delayed cause");
            foreach(var form in new[]{PlayerFlags1.Morphing,PlayerFlags1.AltForm,PlayerFlags1.Unmorphing})
            {
                Reset();flags1.SetValue(player,form);Offer(Fact());flags1.SetValue(player,(PlayerFlags1)0);player.Health=0;Tick(105,1001);Offer(Fact(2,105));
                check(!Active(scene,player)&&Track(scene) is {HasCause:true},"ineligible-at-arrival form cannot become head-death overlay after recovery: "+form);
            }
            foreach(string reason in new[]{"reduce-motion","cosmetics-off","spectator"})
            {
                Reset();Offer(Fact());
                if(reason=="reduce-motion")runtime.SetValue(null,new HudRuntimeProfile(new HudProfile{ReduceMotion=true}));
                if(reason=="cosmetics-off")RenderOptions.ShowCustomCosmetics=false;
                if(reason=="spectator")flags2.SetValue(player,PlayerFlags2.Spectating);
                Tick(100,1001);Eligible();player.Health=0;Tick(105,1002);Offer(Fact(2,105));
                check(!Active(scene,player)&&Track(scene) is {HasCause:true},"ineligibility while pending is sticky through death and recovery: "+reason);
            }
            Reset(false);Clock(105,1001);Offer(Fact());
            check(Track(scene) is {HasCause:true}&&!Active(scene,player),"live latejoin first seen already dead consumes cause without replaying corpse reaction");
            Reset();player.Health=0;Clock(105,1001);Offer(Fact(head:false));Offer(Fact(2));
            check(Track(scene) is {HasCause:true}&&!Active(scene,player)&&Track(scene)!.Event.Native.ShotId==1,"valid body cause seals life before later head fact");
            foreach(var bad in new[]{Fact() with{Weapon=255},Fact() with{ImpactPoint=new(float.NaN,0,0)},Fact() with{Damage=0},Fact() with{HealthAfter=1},Fact() with{VictimLifeId=8},Fact() with{VictimGeneration=6},Fact() with{MatchId=2},Fact() with{AuthorityEpoch=2}})
            {Reset();Clock(105,1001);Offer(bad);check(Track(scene) is null or {HasCause:false}&&!Queued(scene),"malformed/foreign lethal cannot consume valid body-fallback cause");}
            Reset();Offer(Fact());lifecycle[0].BeginLife();player.Health=0;Tick(105,1001);
            check(!Queued(scene)&&Track(scene) is {HasCause:false}&&!Active(scene,player),"native life transition discards old pending cause");
            player.Health=100;Tick(105,1002);player.Health=0;Offer(Fact(2,105,8));
            check(Active(scene,player),"fresh actual life can accept its own eligible first death cause");
            Reset();Offer(Fact());player=new PlayerEntity(0,scene){Health=0,LoadFlags=LoadFlags.Active|LoadFlags.Spawned};scene.Players.Values[0]=player;Tick(105,1001);Offer(Fact(2,105));
            check(!Queued(scene)&&!Active(scene,player),"same slot/gen/life replacement actor does not inherit alive-observed eligibility or queued cause");
            Reset();Clock(105,1001);player.Health=0;Offer(Fact());var first=Track(scene)!.Event;
            for(int n=0;n<100;n++)Offer(Fact((uint)n+2));
            check(Track(scene)!.Event==first&&Active(scene,player),"full-life cause latch survives100 alternate accepted identities without eviction");
            // Explicitly injected CPU facts exercise only visual admission.
            Reset();Clock(105,1001);player.Health=0;Offer(Fact());
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.Low;
            actor.CameraInfo.Position=new(24,0,0);Tick(105,1002);
            check(Active(scene,player),"Low corpse distance includes exactly24 world units");
            actor.CameraInfo.Position=new(24.01f,0,0);Tick(105,1003);
            check(!Active(scene,player)&&Track(scene) is {HasCause:true},"far-distance retirement retains the consumed accepted cause");
            actor.CameraInfo.Position=Vector3.Zero;RenderOptions.CosmeticQuality=CosmeticEffectQuality.High;Tick(105,1004);
            check(!Active(scene,player),"returning camera or raising quality cannot resurrect retired same-life corpse");
            Reset();Clock(105,1001);player.Health=0;Offer(Fact());
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.Off;Tick(105,1002);
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.High;Tick(105,1003);
            check(!Active(scene,player)&&Track(scene) is {HasCause:true},"quality Off cancels presentation without reopening death authority");
            Reset();actor.CameraInfo.Position=Vector3.Zero;
            for(int i=1;i<8;i++)
            {
                lifecycle[i]=new();lifecycle[i].SetOccupant(5);for(int n=0;n<7;n++)lifecycle[i].BeginLife();
                var other=new PlayerEntity(i,scene){Health=100,LoadFlags=LoadFlags.Active|LoadFlags.Spawned,Position=Vector3.Zero};
                flags1.SetValue(other,(PlayerFlags1)0);flags2.SetValue(other,(PlayerFlags2)0);frozen.SetValue(other,(ushort)0);
                scene.Players.Values[i]=other;
            }
            Tick(100,1000);Clock(105,1001);
            for(int i=0;i<8;i++)
            {
                var victim=scene.Players.Values[i]!;victim.Health=0;
                Accept.Invoke(scene,new object?[]{victim,Fact((uint)i+1) with{VictimSlot=(byte)i},null});
            }
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.Low;Tick(105,1002);
            for(int i=0;i<8;i++)
            {
                var victim=scene.Players.Values[i]!;
                var track=(HeadDeathTrack)Read(((Array)Slots.GetValue(scene)!).GetValue(i)!,"Track")!;
                check(track.HasCause&&scene.ModHeadDeathPose(victim,CosmeticCatalog.DeathPresentations[0]).Active==(i<2),
                    "Low corpse budget admits two with stable slot tie-break while retaining every cause: "+i);
                check(victim.Health==0,"visual budget cannot alter canonical death HP: "+i);
            }
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.High;Tick(105,1003);
            check(Enumerable.Range(0,8).Count(i=>scene.ModHeadDeathPose(scene.Players.Values[i]!,CosmeticCatalog.DeathPresentations[0]).Active)==2,
                "quality upgrade does not restart six retired accepted causes");
            Release.Invoke(scene,null);headless.SetValue(null,true);Tick(105,1004);
            check(Slots.GetValue(scene)==null,"headless fixed-step dispatch creates no corpse tracks/budget state");
            headless.SetValue(null,false);
            Release.Invoke(scene,null);check(Slots.GetValue(scene)==null,"scene teardown releases all death tracks and pending causes");
        }
        finally
        {
            if(scene!=null)Release.Invoke(scene,null);
            for(int i=0;i<lifecycle.Length;i++)lifecycle[i]=oldSlots[i];
            role.SetValue(null,oldRole);match.SetValue(null,oldMatch);World.SetValue(null,oldWorld);
            netFrame.SetValue(null,oldNetFrame);arrived.SetValue(null,oldArrived);hasSnapshot.SetValue(null,oldHasSnapshot);
            runtime.SetValue(null,oldHud);headless.SetValue(null,oldHeadless);RenderOptions.ShowCustomCosmetics=oldVisible;RenderOptions.CosmeticQuality=oldQuality;
            GameState.Current=oldGame;PlayerEntity.LegacyRegistry=oldPlayers;Rng.Current=oldRandom;
        }
        check(ReferenceEquals(GameState.Current,oldGame)&&ReferenceEquals(PlayerEntity.LegacyRegistry,oldPlayers)
            &&ReferenceEquals(Rng.Current,oldRandom)&&Headless.Active==oldHeadless&&Equals(role.GetValue(null),oldRole)
            &&Equals(match.GetValue(null),oldMatch)&&Equals(World.GetValue(null),oldWorld),"CPU death adapter test restores foreground/global roles and world facades");
    }
}
