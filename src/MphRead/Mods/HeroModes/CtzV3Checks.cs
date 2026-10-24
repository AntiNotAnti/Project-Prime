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
public static class CtzV3Checks
{
    public static int Run(string room)
    {
        var passed=new List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);passed.Add(name);Console.WriteLine("CTZ5 PASS "+name);}
        var sim=new ServerSim();
        try
        {
            Check(CtzRules.Capability==0x355a5443 && CtzRules.RulesetVersion==5,"CTZ5 admission requires synchronized scoreboard totals");
            Check(CtzRules.NoxusBodyHeal==9 && CtzRules.NoxusHeadHeal==15 && CtzRules.NoxusFreezeHeal==30,"Noxus9/15/30 configured");
            Check(CtzRules.WeavelDirectHeal==4 && CtzRules.WeavelSplashHeal==3,"Battlehammer4/3 configured");
            Check(CtzRules.SpireNormalHeal==3 && CtzRules.SpireTotalHeal==20 && CtzRules.SpireAuraPulses==5 && CtzRules.SpireHealPerPulse==4,"Spire3 direct and20 over five1s pulses configured");
            Check(HeroRules.WeaponDetails(Hunter.Noxus).Contains("+30 HP") && HeroRules.WeaponDetails(Hunter.Weavel).Contains("+4 HP direct") && HeroRules.WeaponDetails(Hunter.Spire).Contains("+20 HP"),"Lobby reads the same healing rules as gameplay");
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
            var clock=new CtzState();
            for(int i=0;i<CtzRules.CountdownTicks;i++)clock.Tick(0,0);
            Check(CtzPresentation.ShowRoundStartBanner(clock.State),"Mode announcement appears at active round start");
            for(int i=0;i<179;i++)clock.Tick(0,0);
            Check(CtzPresentation.ShowRoundStartBanner(clock.State),"Announcement remains during first179ticks");
            clock.Tick(0,0);
            Check(!CtzPresentation.ShowRoundStartBanner(clock.State),"Announcement ends at exactly180ticks / three seconds");
            for(int i=0;i<420;i++)clock.Tick(0,0);
            Check(clock.State.RoundElapsedTicks==600 && CtzPresentation.RoundTime(clock.State)=="00:10", "Clock survives zone unlock rather than disappearing");
            for(int i=0;i<3120;i++)clock.Tick(1,1);
            Check(clock.State.RoundElapsedTicks==3720 && CtzPresentation.RoundTime(clock.State)=="01:02", "Contested rounds still advance the visible clock");
            Check(CtzPresentation.ObjectiveStatus(clock.State)=="CONTESTED","Movable objective status is not a permanent mode-name label");
            clock.Apply(clock.State with{Phase=CtzPhase.Intermission,Remaining=300,RedWins=1});
            clock.Tick(0,0);
            Check(clock.State.RoundElapsedTicks==3720,"Intermission freezes finished round time");
            for(int i=0;i<299;i++)clock.Tick(0,0);
            Check(clock.State.Round==2 && clock.State.RoundElapsedTicks==0,"New round resets clock without changing score");
            for(int i=0;i<180;i++)clock.Tick(0,0);
            Check(CtzPresentation.ShowRoundStartBanner(clock.State),"Second round gets one new three-second announcement");
            Check(!(clock.State with{RoundElapsedTicks=-1}).Valid,"Negative network clock refused");
            var recorded=clock.State with{RoundElapsedTicks=3720,Red=720,Blue=1500};
            using(var bs=new MemoryStream()) {recorded.Write(new BinaryWriter(bs));bs.Position=0;
                Check(CtzSnapshot.Read(new BinaryReader(bs))==recorded,"Round elapsed clock roundtrips with objective snapshot");}
            Check(CtzPresentation.TeamScore(recorded,0).Contains("12%") && CtzPresentation.TeamScore(recorded,1).Contains("25%"),"Scoreboard shows authority team capture percentages");
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
            scene.GameState.CtzStats.Reset();
            red.Health=100;Hit(Fire(noxus),red,false);
            Check(red.Health==109,"Native ordinary Judicator body hit heals9");
            Check(scene.GameState.CtzStats.Get(0).Done==9 && scene.GameState.CtzStats.Get(1).Received==9,"Actual Noxus body impact records9 done and9 received");
            Hit(Fire(noxus),red,true);Check(red.Health==124,"Native ordinary Judicator head hit heals15");
            Check(scene.GameState.CtzStats.Get(0).Done==24 && scene.GameState.CtzStats.Get(1).Received==24,"Actual Noxus head impact adds15 to support totals");
            Check(noxus.ModCaptureCtzSupport().HealTarget==red.SlotIndex && noxus.ModCaptureCtzSupport().TargetTicks==120,"Native Noxus hit selects existing target information");
            red.Health=red.HealthMax-2;Hit(Fire(noxus),red,false);Check(red.Health==red.HealthMax,"Healing capped at native maximum HP");
            Check(scene.GameState.CtzStats.Get(0).Done==26 && scene.GameState.CtzStats.Get(1).Received==26,"Overheal awards only the2HP actually restored");
            red.Health=100;var hammer=Fire(weavel);Check(hammer.Damage==10.5f && hammer.HeadshotDamage==10.5f && hammer.SplashDamage==3,"Actual Battlehammer projectiles scaled by exactly75percent");
            Hit(hammer,red,false);Check(red.Health==104,"Native Battlehammer direct heal4");
            splash.Invoke(hammer,new object?[]{null,red.Position});Check(red.Health==104,"Same projectile direct plus splash cannot heal twice");
            var hammer2=Fire(weavel);splash.Invoke(hammer2,new object?[]{null,red.Position});Check(red.Health==107,"Separate projectile splash heals3");
            var hammer3=Fire(weavel);float remainderTest=players[5].ModCtzWholeDamage(hammer3,10.5f)+players[5].ModCtzWholeDamage(hammer3,10.5f);
            Check(remainderTest==21,"Fractional nerf retains10.5 average, not permanent rounding penalty");
            // One real native Spawn reserves three independent delayed projectiles.
            red.Health=100;
            scene.Players.MainPlayerIndex=weavel.SlotIndex;
            Fire(weavel);
            var burst=weavel.EquipInfo.Beams.Where(b=>b.Owner==weavel && b.Lifespan>0).OrderByDescending(b=>b.Age).ToArray();
            Check(burst.Length==3 && burst.Distinct().Count()==3,"One Battlehammer trigger creates three distinct projectile instances");
            Check(burst[0].Age==0 && burst[1].Age<0 && burst[2].Age<burst[1].Age,"Burst projectiles retain their native scheduled release offsets");
            for(int shot=0;shot<3;shot++)
            {
                // Resolve at each scheduled impact; these tests exercise real collision/splash callbacks, not ballistics.
                burst[shot].Age=0;
                if(shot==1) { splash.Invoke(burst[shot],new object?[]{null,red.Position}); Hit(burst[shot],red,false); }
                else { Hit(burst[shot],red,false); splash.Invoke(burst[shot],new object?[]{null,red.Position}); }
                Check(red.Health==100+4*(shot+1),"Single burst projectile"+(shot+1)+" heals exactly once despite direct+splash overlap");
                Check(weavel.ModCaptureCtzSupport().HealTarget==red.SlotIndex && weavel.ModCaptureCtzSupport().TargetTicks==120,"Weavel projectile"+(shot+1)+" publishes teammate panel identity and lifetime");
            }
            Check(red.Health==112,"Full three-direct-hit burst heals12HP, not4 or21");
            Fire(weavel);var next=weavel.EquipInfo.Beams.Where(b=>b.Owner==weavel && b.Lifespan>0).ToArray();
            foreach(var b in next){b.Age=0;splash.Invoke(b,new object?[]{null,red.Position});}
            Check(red.Health==121,"Reused projectile pool resets dedup for the next three splash heals (9HP)");
            Check(scene.GameState.CtzStats.Get(2).Done==28 && scene.GameState.CtzStats.Get(1).Received==54,"All real Weavel directs/splash/bursts count once per applied heal");
            Check(weavel.ModCaptureCtzSupport().HealTarget==red.SlotIndex,"Weavel target presentation is captured for network publication");
            scene.Players.MainPlayerIndex=spire.SlotIndex;
            red.Health=100;var magma=Fire(spire,true);Hit(magma,red,false);
            Check(spire.ModCaptureCtzSupport().HealTarget==red.SlotIndex && spire.ModCaptureCtzSupport().TargetTicks==420,"Spire exposes ally health for aura duration plus two seconds");
            Check(red.Health==100 && red.ModCtzAuraRemaining==300,"Charged ally Magmaul starts five-second aura without immediate heal");
            Check(scene.GameState.CtzStats.Get(3).Done==0,"Starting an aura does not pre-credit healing that has not occurred");
            Check((ushort)typeof(PlayerEntity).GetField("_burnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(red)! == 0,"Charged Magmaul support does not burn the allied player");
            var firstAura=red.ModCaptureCtzSupport();
            red.ModStartCtzHealingAura();Check(red.ModCaptureCtzSupport()==firstAura,"Repeated aura does not stack or extend duration");
            for(int i=0;i<300;i++)
            {sceneFrame.SetValue(scene,scene.FrameCount+1);red.ModTickCtzSupport();spire.ModTickCtzSupport();if(i==58)Check(red.Health==100,"Aura waits one second for first pulse");if(i==59)Check(red.Health==104,"Aura first pulse exactly4HP");}
            Check(red.Health==120 && red.ModCtzAuraRemaining==0,"Aura grants20HP TOTAL over five seconds");
            Check(scene.GameState.CtzStats.Get(3).Done==20 && scene.GameState.CtzStats.Get(1).Received==74,"Five native aura pulses attribute20HP to the original Spire");
            Check(spire.ModCaptureCtzSupport().HealTarget==red.SlotIndex && spire.ModCaptureCtzSupport().TargetTicks==120,"Spire teammate panel survives all five healing pulses");
            // v4: normal Magmaul heals immediately without replacing a charged aura.
            red.Health=100;var normalMagma=Fire(spire);Hit(normalMagma,red,false);
            Check(red.Health==103 && red.ModCtzAuraRemaining==0,"Normal Magmaul direct heals3 without starting aura");
            splash.Invoke(normalMagma,new object?[]{null,red.Position});
            Check(red.Health==103,"Normal Magmaul direct plus own blast does not double-heal");
            Hit(Fire(spire,true),red,false);
            var auraAtStart=red.ModCaptureCtzSupport();
            Hit(Fire(spire),red,false);
            Check(red.Health==106 && red.ModCaptureCtzSupport().AuraRemaining==auraAtStart.AuraRemaining,
                "Normal3HP stacks additively during charged aura without resetting it");
            for(int i=0;i<300;i++)
            {
                sceneFrame.SetValue(scene,scene.FrameCount+1);red.ModTickCtzSupport();spire.ModTickCtzSupport();
                if((i+1)%60==0)Check(red.Health==106+4*((i+1)/60),"Aura native second"+((i+1)/60)+" adds4HP");
            }
            Check(red.Health==126 && red.ModCtzAuraRemaining==0,"Two normal shots plus full aura total26HP");
            red.Health=red.HealthMax-2;Hit(Fire(spire),red,false);
            Check(red.Health==red.HealthMax,"Normal Magmaul cannot overheal");
            red.Health=red.HealthMax-2;Hit(Fire(spire,true),red,false);
            for(int i=0;i<300;i++){sceneFrame.SetValue(scene,scene.FrameCount+1);red.ModTickCtzSupport();}
            Check(red.Health==red.HealthMax && red.ModCtzAuraRemaining==0,"Charged aura cannot overheal and expires normally");
            Check(scene.GameState.CtzStats.Get(3).Done==50,"Normal shots plus aura stack in totals, with actual-only partial heals");
            red.Health=100;scene.Players.MainPlayerIndex=weavel.SlotIndex;
            var mixed=Fire(weavel);splash.Invoke(mixed,new object?[]{null,red.Position});
            Check(red.Health==103,"First splash-only callback heals3");
            Hit(mixed,red,false);Check(red.Health==104,"Later direct callback upgrades same projectile to4 total");
            Hit(mixed,red,false);splash.Invoke(mixed,new object?[]{null,red.Position});
            Check(red.Health==104,"Repeated impact callbacks cannot farm extra healing");
            scene.Players.MainPlayerIndex=noxus.SlotIndex;
            int healthBefore=blue.Health;Check(!CtzHealing.TryAllyImpact(Fire(noxus),blue,false,false) && blue.Health==healthBefore,"Support callback cannot heal enemies");
            red.Health=100;red.ModPlaceAt(new Vector3(0,1,0));
            // Original affinity cone reaches 15360/4096 = 3.75 world units, not five.
            noxus.ModPlaceAt(new Vector3(0,1,-2));
            Check(noxus.EquipInfo.Weapon.ChargedDistance==15360,"Freeze native3.75-unit reach is unchanged");
            scene.GameState.FriendlyFire=true;
            noxus.EquipInfo.ChargeLevel=(ushort)(noxus.EquipInfo.Weapon.FullCharge*2);
            foreach(var b in noxus.EquipInfo.Beams)b.Lifespan=0;
            BeamProjectileEntity.Spawn(noxus,noxus.EquipInfo,noxus.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,noxus.NodeRef,scene);
            Check(red.Health==130,"Actual native charged ice-wave release heals ally30HP");
            Check(scene.GameState.CtzStats.Get(noxus.SlotIndex).Done==56,"Native charged freeze adds exactly30 to Noxus healing done");
            Check(!red.ModFrozen,"Allied freeze heal does not freeze the recipient with friendly fire enabled");
            Check(noxus.ModCaptureCtzSupport().HealTarget==red.SlotIndex,"Charged freeze heal also reveals allied health");
            Check(noxus.ModCtzFreezeCooldown==1800,"Released charged Judicator starts30second cooldown");
            var blocked=BeamProjectileEntity.Spawn(noxus,noxus.EquipInfo,noxus.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,noxus.NodeRef,scene);
            Check(blocked==BeamResultFlags.NoSpawn && noxus.ModCtzChargeBlocked,"Second charged release and charging blocked");
            Check(Fire(noxus).Lifespan>0 && noxus.ModCtzFreezeCooldown==1800,"Ordinary Judicator remains available during freeze cooldown");
            var scratch=new ReplayAuthorityCaptureScratch();var world=scratch.Capture(scene,1,1,123,_=>1);
            var decoded=ReplayAuthorityWorld.Decode(world.Encode());
            Check(decoded.Support.Length==8 && decoded.Support[0].FreezeCooldown==1800,"Production snapshot scratch carries actual cooldown for all eight slots");
            Check(decoded.Zone==scene.CaptureZone.State,"Objective snapshot and support extension coexist");
            Check(world.Encode()[0]==10,"CTZ5 objective/statistics extension is explicitly versioned");
            Check(decoded.Support[weavel.SlotIndex].HealTarget==red.SlotIndex && decoded.Support[spire.SlotIndex].HealTarget==red.SlotIndex,"Authority snapshots carry both Weavel and Spire ally target identities");
            var legacy=new ReplayAuthorityWorld{Zone=recorded,MatchId=1,Epoch=1,Phase=MatchState.InProgress,MatchTime=-1};var current=legacy.Encode();
            // Build an actual legacy v6 tail: omit unified flags/ball length,
            // the later clock, and the combat rows. Do not relabel a v10 body.
            var legacyPrefix=current[..^(4+21+1+1)];
            using var legacyStream=new MemoryStream();legacyStream.Write(legacyPrefix);legacyStream.Position=0;legacyStream.WriteByte(6);legacyStream.Position=legacyStream.Length;
            using(var writer=new BinaryWriter(legacyStream,System.Text.Encoding.UTF8,true)) { recorded.Write(writer); }
            var withClock=legacyStream.ToArray();var oldBytes=withClock[..^4].Concat(new byte[]{0}).ToArray();
            var historical=ReplayAuthorityWorld.Decode(oldBytes);
            Check(historical.Zone?.RoundElapsedTicks==0 && historical.Zone?.Red==recorded.Red,"Old CTZ2 objective snapshots remain readable without shifting support fields");
            Check(new ReplayAuthorityWorld().Encode()[0]==10,"Unified authority format explicitly versioned");
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
            CtzV5Checks.Run(scene, Check);
            Check(sim.StepFailures==0,"No swallowed native simulation exception");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=passed.Count,checks=passed,
                scope="Real headless native scene and weapon impact callbacks; exact support values, rooms, HUD persistence and serialization. Not Windows or full internet/bot-match verification."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=passed.Count,checks=passed,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{sim.Stop();NetSession.Stop();}
    }
}
