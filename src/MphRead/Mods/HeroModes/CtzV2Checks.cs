using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.HeroModes;

/// <summary>Opt-in checks of the shipped native callbacks, not an alternate simulation.</summary>
public static class CtzV2Checks
{
    public static int Run(string room)
    {
        var passed=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);passed.Add(name);Console.WriteLine("CTZ2 PASS "+name);}
        var sim=new ServerSim();
        try
        {
            Check(HeroRules.Definition(Hunter.Trace).MaxHealth==90,"Trace90 maximum health");
            Check(HeroRules.Definition(Hunter.Kanden).MaxHealth==160,"Kanden160 maximum health");
            var expected=new Dictionary<Hunter,int>{{Hunter.Trace,1200},{Hunter.Samus,600},{Hunter.Kanden,600},
                {Hunter.Noxus,300},{Hunter.Sylux,300},{Hunter.Spire,300},{Hunter.Weavel,300}};
            foreach(var pair in expected) Check(CtzRules.RespawnTicks(pair.Key)==pair.Value,"Configured death delay "+pair.Key);
            var profile=new HudProfile{Mode=HudMode.Custom};
            profile.Elements["match.objective"].OffsetX=117;profile.Elements["match.timer"].OffsetY=214;
            profile.Elements.Remove("match.rounds");profile.Elements.Remove("match.respawn");profile.Elements.Remove("combat.abilityCooldown");
            var migrated=HudProfileStore.Parse(HudProfileStore.Serialize(profile));migrated.Validate();
            Check(migrated.Elements["match.objective"].OffsetX==117 && migrated.Elements["match.timer"].OffsetY==214,"Existing objective/timer saved offsets retained");
            foreach(string id in new[]{"match.score","match.objective","match.timer","match.rounds","match.respawn","combat.abilityCooldown"})
                Check(migrated.Elements.ContainsKey(id) && HudTypography.Ids.Contains(id),"Movable and typographic HUD registration "+id);
            var support=new CtzSupportState(0,1,1,1800,300,5,1,1,1,120);
            using(var stream=new MemoryStream())
            {
                support.Write(new BinaryWriter(stream));stream.Position=0;
                Check(CtzSupportState.Read(new BinaryReader(stream))==support,"Bounded support state roundtrip");
            }
            Check(!(support with{FreezeCooldown=1801}).Valid && !(support with{Slot=8}).Valid,"Invalid support facts refused");
            Check(ServerSim.Available(out var reason),"Private native assets available: "+reason);
            Check(sim.Start(room,GameMode.CaptureTheZone,8,_=>{},()=>{}),"Actual CTZ world starts");
            var scene=sim.ActiveScene!;
            NetSession.ApplyMatchState(new MatchStatePacket{MatchId=1,AuthorityEpoch=1,RoomKey=room,Mode=(byte)GameMode.CaptureTheZone},false);
            var roster=RosterPacket.Create();roster.MatchId=1;roster.AuthorityEpoch=1;roster.Revision=1;roster.Count=8;
            Hunter[] hunters=[Hunter.Noxus,Hunter.Samus,Hunter.Weavel,Hunter.Spire,Hunter.Trace,Hunter.Kanden,Hunter.Sylux,Hunter.Noxus];
            for(byte i=0;i<8;i++){roster.Slots[i]=i;roster.Generations[i]=1;roster.Hunters[i]=(byte)hunters[i];roster.Teams[i]=(sbyte)(i/4);roster.Names[i]="V2"+i;}
            NetSession.ApplyRoster(roster);NetSlotManager.Sync();
            for(int i=0;i<200;i++)sim.Step();
            Check(sim.StepFailures==0,"Native initial200 steps no exception");
            var players=scene.Players.Items;
            var invuln=typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var sceneFrame=typeof(Scene).GetField("_frameCount",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var resolve=typeof(BeamProjectileEntity).GetMethod("ResolveCollision",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var splash=typeof(BeamProjectileEntity).GetMethod("CheckSplashDamage",BindingFlags.Instance|BindingFlags.NonPublic)!;
            void ClearProtection(PlayerEntity p){invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();p.ModSetSpectating(false);}
            for(int i=0;i<8;i++)
            {
                var p=players[i];ClearProtection(p);p.ModPlaceAt(new Vector3(i<4?-20:20,1,18+(i%4)*3));p.Speed=Vector3.Zero;
                Check(p.Hunter==hunters[i] && p.Health==HeroRules.Definition(p.Hunter).MaxHealth,"Native spawned hero HP slot"+i);
                Check(p.EquipInfo.InfiniteAmmo,"Native infinite ammo slot"+i);
            }
            scene.CaptureZone.Apply(new(CtzPhase.Active,1,-1,-1,0,0,0,0,0,0,0,false));
            var rooms=CtzRuntime.Definition(scene)?.SpawnRooms;
            Check(rooms?.Count==2 && rooms.All(r=>r.Valid),"Native map contains two valid team rooms");
            var red=players[1];var blue=players[4];
            red.ModPlaceAt(new Vector3(-43,1,14));blue.ModPlaceAt(new Vector3(-30,1,14));
            int hp=red.Health;red.TakeDamage(100,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,blue);
            Check(red.Health==hp,"Enemy damage cannot penetrate own spawn room");
            Check(CtzSpawnRooms.BlocksMovement(blue,blue.Position,red.Position),"Enemy movement denied at room entrance");
            Check(!CtzSpawnRooms.BlocksMovement(red,red.Position,blue.Position),"Friendly movement exits own room");
            Check(CtzSpawnRooms.BlocksSegment(scene,1,blue.Position,red.Position,out var distance,out _) && distance is >0 and <1,"Incoming enemy projectile meets room shield");
            Check(!CtzSpawnRooms.BlocksSegment(scene,0,red.Position,blue.Position,out _,out _),"Outgoing friendly projectile crosses own door");
            scene.CaptureZone.Apply(scene.CaptureZone.State with{Round=2});
            Check(!CtzSpawnRooms.BlocksMovement(blue,blue.Position,red.Position) && CtzSpawnRooms.BlocksMovement(red,blue.Position,red.Position),"Shield ownership swaps with round side");
            scene.CaptureZone.Apply(scene.CaptureZone.State with{Round=1});
            red.ModPlaceAt(new Vector3(0,1,0));blue.ModPlaceAt(new Vector3(22,1,20));
            var noxus=players[0];var weavel=players[2];var spire=players[3];
            noxus.ModPlaceAt(new Vector3(0,1,-5));weavel.ModPlaceAt(new Vector3(-12,1,-12));spire.ModPlaceAt(new Vector3(12,1,-12));
            BeamProjectileEntity Fire(PlayerEntity p,bool charge=false)
            {
                foreach(var beam in p.EquipInfo.Beams)beam.Lifespan=0;
                p.EquipInfo.ChargeLevel=(ushort)(charge?p.EquipInfo.Weapon.FullCharge*2:0);
                NetFireEvents.Begin(p);NetUnlagged.BeginShot(p);
                try{BeamProjectileEntity.Spawn(p,p.EquipInfo,p.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,p.NodeRef,scene);}
                finally{NetUnlagged.EndShot(p);}
                return p.EquipInfo.Beams.First(b=>b.Owner==p && b.Lifespan>0 && b.Age>=0);
            }
            void Hit(BeamProjectileEntity b,PlayerEntity target,bool head)
            {
                float h=head?Fixed.ToFloat(target.Values.MaxPickupHeight)-.1f:.5f;
                var point=target.Position+Vector3.UnitY*h;
                b.SpawnPosition=point-Vector3.UnitZ*5;b.Position=point;
                var res=new CollisionResult{Position=point,Plane=new Vector4(0,0,-1,-point.Z),Distance=.5f};
                resolve.Invoke(b,new object?[]{res,target,false,.5f,false});
            }
            red.Health=100;Hit(Fire(noxus),red,false);
            Check(red.Health==105,"Native ordinary Judicator body hit heals5");
            Hit(Fire(noxus),red,true);Check(red.Health==113,"Native ordinary Judicator head hit heals8");
            Check(noxus.ModCaptureCtzSupport().HealTarget==red.SlotIndex && noxus.ModCaptureCtzSupport().TargetTicks==120,"Native Noxus hit selects existing target information");
            red.Health=red.HealthMax-2;Hit(Fire(noxus),red,false);Check(red.Health==red.HealthMax,"Healing capped at native maximum HP");
            red.Health=100;var hammer=Fire(weavel);Check(hammer.Damage==10.5f && hammer.HeadshotDamage==10.5f && hammer.SplashDamage==3,"Actual Battlehammer projectiles scaled by exactly75percent");
            Hit(hammer,red,false);Check(red.Health==101,"Native Battlehammer direct heal1");
            splash.Invoke(hammer,new object?[]{null,red.Position});Check(red.Health==101,"Same projectile direct plus splash cannot heal twice");
            var hammer2=Fire(weavel);splash.Invoke(hammer2,new object?[]{null,red.Position});Check(red.Health==102,"Separate projectile splash heals1");
            var hammer3=Fire(weavel);float remainderTest=players[5].ModCtzWholeDamage(hammer3,10.5f)+players[5].ModCtzWholeDamage(hammer3,10.5f);
            Check(remainderTest==21,"Fractional nerf retains10.5 average, not permanent rounding penalty");
            red.Health=100;var magma=Fire(spire,true);Hit(magma,red,false);
            Check(red.Health==100 && red.ModCtzAuraRemaining==300,"Charged ally Magmaul starts five-second aura without immediate heal");
            Check((ushort)typeof(PlayerEntity).GetField("_burnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(red)! == 0,"Charged Magmaul support does not burn the allied player");
            var firstAura=red.ModCaptureCtzSupport();
            red.ModStartCtzHealingAura();Check(red.ModCaptureCtzSupport()==firstAura,"Repeated aura does not stack or extend duration");
            for(int i=0;i<300;i++)
            {sceneFrame.SetValue(scene,scene.FrameCount+1);red.ModTickCtzSupport();if(i==58)Check(red.Health==100,"Aura waits one second for first pulse");if(i==59)Check(red.Health==101,"Aura first pulse exactly1HP");}
            Check(red.Health==105 && red.ModCtzAuraRemaining==0,"Aura grants5HP TOTAL over native burn duration");
            int healthBefore=blue.Health;Check(!CtzHealing.TryAllyImpact(Fire(noxus),blue,false,false) && blue.Health==healthBefore,"Support callback cannot heal enemies");
            noxus.EquipInfo.ChargeLevel=(ushort)(noxus.EquipInfo.Weapon.FullCharge*2);
            foreach(var b in noxus.EquipInfo.Beams)b.Lifespan=0;
            BeamProjectileEntity.Spawn(noxus,noxus.EquipInfo,noxus.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,noxus.NodeRef,scene);
            Check(noxus.ModCtzFreezeCooldown==1800,"Released charged Judicator starts30second cooldown");
            var blocked=BeamProjectileEntity.Spawn(noxus,noxus.EquipInfo,noxus.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,noxus.NodeRef,scene);
            Check(blocked==BeamResultFlags.NoSpawn && noxus.ModCtzChargeBlocked,"Second charged release and charging blocked");
            Check(Fire(noxus).Lifespan>0 && noxus.ModCtzFreezeCooldown==1800,"Ordinary Judicator remains available during freeze cooldown");
            var scratch=new ReplayAuthorityCaptureScratch();var world=scratch.Capture(scene,1,1,123,_=>1);
            var decoded=ReplayAuthorityWorld.Decode(world.Encode());
            Check(decoded.Support.Length==8 && decoded.Support[0].FreezeCooldown==1800,"Production snapshot scratch carries actual cooldown for all eight slots");
            Check(decoded.Zone==scene.CaptureZone.State,"Objective snapshot and support extension coexist");
            int savedCooldown=noxus.ModCtzFreezeCooldown;
            noxus.ModResetCtzLifeSupport();Check(noxus.ModCtzFreezeCooldown==savedCooldown,"Respawn loadout reset does not bypass freeze cooldown");
            for(int i=0;i<1800;i++){sceneFrame.SetValue(scene,scene.FrameCount+1);noxus.ModTickCtzSupport();}
            Check(noxus.ModCtzFreezeCooldown==0 && !noxus.ModCtzChargeBlocked,"Freeze cooldown expires after1800 native ticks");
            red.Health=0;int noHeal=red.ModGainCtzHealth(5);Check(noHeal==0 && red.Health==0,"Healing cannot resurrect dead teammate");
            red.Health=100;
            foreach(var p in players)
            {
                ClearProtection(p);p.ModPlaceAt(new Vector3(0,1,25));p.Health=HeroRules.Definition(p.Hunter).MaxHealth;
                p.TakeDamage(100000,DamageFlags.Death|DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,null);
                Check(p.Health==0 && p.RespawnTimer==expected[p.Hunter],"Actual native death timer "+p.Hunter+" slot"+p.SlotIndex);
            }
            noxus.ModStartCtzFreezeCooldown();
            Check(noxus.ModCtzFreezeCooldown==1800,"Departing occupant has a real active cooldown");
            NetPlayerLifecycle.SetOccupant(noxus.SlotIndex,2);
            sceneFrame.SetValue(scene,scene.FrameCount+1);noxus.ModTickCtzSupport();
            Check(noxus.ModCtzFreezeCooldown==0,"New slot occupant cannot inherit previous player's cooldown");
            Check(sim.StepFailures==0,"No swallowed native simulation exception");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=passed.Count,checks=passed,
                scope="Real headless native scene and weapon impact callbacks; exact support values, rooms, HUD persistence and serialization. Not Windows or full internet/bot-match verification."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=passed.Count,checks=passed,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{sim.Stop();NetSession.Stop();}
    }
}
