using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Mods.HeroModes;
public static class CtzSceneChecks
{
    public static int Run(string room)
    {
        var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("NATIVE CTZ PASS "+label);}
        var sim=new ServerSim();
        try
        {
            Check(ServerSim.Available(out var reason),"Private native assets available: "+reason);
            Check(sim.Start(room,GameMode.CaptureTheZone,8,_=>{},()=>{}),"Actual authoritative CTZ scene loads");
            var scene=sim.ActiveScene!;
            NetSession.ApplyMatchState(new MatchStatePacket{MatchId=1,AuthorityEpoch=1,RoomKey=room,Mode=(byte)GameMode.CaptureTheZone},false);
            var roster=RosterPacket.Create();roster.MatchId=1;roster.AuthorityEpoch=1;roster.Revision=1;roster.Count=8;
            Hunter[] hunters=[Hunter.Samus,Hunter.Kanden,Hunter.Weavel,Hunter.Spire,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Samus];
            for(byte i=0;i<8;i++){roster.Slots[i]=i;roster.Generations[i]=1;roster.Hunters[i]=(byte)hunters[i];roster.Teams[i]=(sbyte)(i/4);roster.Names[i]="CTZ"+i;}
            NetSession.ApplyRoster(roster);NetSlotManager.Sync();
            for(int i=0;i<200;i++)sim.Step();
            Check(sim.StepFailures==0,"200 native simulation frames without exception");
            Check(CtzRuntime.Definition(scene)?.Valid==true,"Loaded room retains explicit capture volume");
            var equip=typeof(PlayerEntity).GetMethod("TryEquipWeapon",BindingFlags.Instance|BindingFlags.NonPublic)!;
            for(int i=0;i<8;i++)
            {
                var p=scene.Players.Items[i];
                Check(p.Hunter==hunters[i] && p.ModIsInPlay,$"Native slot {i} active as {hunters[i]}");
                Check(p.Health==HeroRules.Definition(p.Hunter).MaxHealth && p.HealthMax==p.Health,$"Native full HP slot {i}: {p.Health}/{p.HealthMax}");
                Check(p.CurrentWeapon==HeroRules.Primary(p.Hunter) && p.EquipInfo.InfiniteAmmo,$"Native affinity and infinite ammo slot {i}");
                Check(!(bool)equip.Invoke(p,new object[]{BeamType.Missile,true,false})!,$"Missile equip blocked slot {i}");
                if(p.Hunter!=Hunter.Samus)Check(!(bool)equip.Invoke(p,new object[]{BeamType.PowerBeam,true,false})!,$"Ordinary primary blocked slot {i}");
                p.ModSetSpectating(false);
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,(ushort)0);
                p.ModReleaseSpawnProtection();
                p.Position=new Vector3((i<4?-20:20),1,12+(i%4)*3);p.Speed=Vector3.Zero;
            }
            var victim=scene.Players.Items[2];var attacker=scene.Players.Items[4];
            int hp=victim.Health;
            victim.TakeDamage(100,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,attacker);
            Check(victim.Health==hp-85,$"Native TakeDamage honors exact Weavel DR: {hp}->{victim.Health}");
            victim.Health=hp;
            scene.CaptureZone.Apply(new(CtzPhase.Active,1,-1,-1,0,0,0,0,0,0,0,false));
            var red=scene.Players.Items[0];var blue=scene.Players.Items[4];
            red.Position=new Vector3(0,1,0);
            for(int i=0;i<300;i++)CtzRuntime.Tick(scene);
            Check(scene.CaptureZone.State.Owner==0,"Actual living player captures authored zone");
            for(int i=0;i<60;i++)CtzRuntime.Tick(scene);
            Check(scene.CaptureZone.State.Red==60,"Actual occupied zone scores one percent");
            blue.Position=new Vector3(2,1,0);
            for(int i=0;i<60;i++)CtzRuntime.Tick(scene);
            Check(scene.CaptureZone.State.Contested && scene.CaptureZone.State.Red==60,"Actual opposing player contests and pauses scoring");
            blue.Health=0;CtzRuntime.Tick(scene);
            Check(!scene.CaptureZone.State.Contested && scene.CaptureZone.State.Red==61,"Dead player cannot contest");
            blue.Health=90;blue.Position=new Vector3(40,1,12);
            var shooter=scene.Players.Items[2];shooter.Position=new Vector3(0,2,-12);
            shooter.EquipInfo.ChargeLevel=0;
            foreach(var beam in shooter.EquipInfo.Beams)beam.Lifespan=0;
            NetFireEvents.Begin(shooter);NetUnlagged.BeginShot(shooter);
            var result=BeamProjectileEntity.Spawn(shooter,shooter.EquipInfo,shooter.Position+Vector3.UnitY,
                Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,shooter.NodeRef,scene);
            NetUnlagged.EndShot(shooter);
            var live=shooter.EquipInfo.Beams.Where(b=>b.Owner==shooter && b.Lifespan>0).ToArray();
            Check(result!=BeamResultFlags.NoSpawn && live.Length==3,"Native Battlehammer creates three real projectiles");
            Check(live.Count(b=>b.Age<0)==2 && live.All(b=>Math.Abs(b.Velocity.Y)<.0001f && b.Acceleration==Vector3.Zero),"Delayed rounds and straight native launch vectors");
            shooter.ModCancelHeroFire();
            Check(live.Count(b=>b.Lifespan>0)==1,"Cancellation discards queued rounds, not flying projectile");
            var before=scene.CaptureZone.State;
            scene.CaptureZone.Apply(before with{Owner=0,Red=5999,Blue=0,Contested=false});
            CtzRuntime.Tick(scene);
            Check(scene.CaptureZone.State.RedWins==1 && scene.GameState.TeamPoints[0]==1,"Native world reports objective round winner");
            Check(sim.StepFailures==0,"Native no swallowed step exceptions");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                scope="Real headless native engine, eight slots, actual loadouts/damage/projectiles and occupancy; not rendering, Windows or internet verification"},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{sim.Stop();NetSession.Stop();}
    }
}
