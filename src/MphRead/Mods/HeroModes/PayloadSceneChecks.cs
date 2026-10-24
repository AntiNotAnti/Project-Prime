using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Mods.HeroModes;
internal static class PayloadSceneChecks
{
    internal static int Run(string room)
    {
        var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("NATIVE PAYLOAD PASS "+label);}
        using var fixture=new Fixture();var sim=fixture.Sim;
        try
        {
            Check(ServerSim.Available(out var reason),"Private native assets available: "+reason);
            Check(sim.Start(room,GameMode.PushThePayload,8,_=>{},()=>{}),"Native payload scene starts");
            var scene=sim.ActiveScene!;
            NetSession.ApplyMatchState(new MatchStatePacket{MatchId=1,AuthorityEpoch=1,RoomKey=room,Mode=(byte)GameMode.PushThePayload},false);
            var roster=RosterPacket.Create();roster.MatchId=1;roster.AuthorityEpoch=1;roster.Revision=1;roster.Count=8;
            Hunter[] hunters=[Hunter.Samus,Hunter.Kanden,Hunter.Weavel,Hunter.Spire,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Samus];
            for(byte i=0;i<8;i++){roster.Slots[i]=i;roster.Generations[i]=1;roster.Hunters[i]=(byte)hunters[i];roster.Teams[i]=(sbyte)(i/4);roster.Names[i]="PAYLOAD"+i;}
            NetSession.ApplyRoster(roster);NetSlotManager.Sync();
            for(int i=0;i<200;i++)sim.Step();
            Check(sim.StepFailures==0,"200 native simulation steps without exception");
            scene.Payload.Ensure(scene);
            Check(scene.Payload.Map!=null && scene.Payload.Frame is {} f && f.Valid,"Loaded immutable route and bounded native state");
            var carts=new List<PayloadEntity>();foreach(var entity in scene.Entities)if(entity is PayloadEntity cartEntity)carts.Add(cartEntity);
            Check(carts.Count==1,"Exactly one real native payload entity");
            Check(carts[0].GetModels().Count()>0,"Payload has compiled native model");
            var invuln=typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            foreach(var p in scene.Players.Items)
            {
                p.IsBot=false;
                Check(p.Hunter==hunters[p.SlotIndex] && p.ModIsInPlay,$"Native active hunter slot{p.SlotIndex}");
                Check(p.HealthMax==HeroRules.Definition(p.Hunter).MaxHealth && p.EquipInfo.InfiniteAmmo && p.CurrentWeapon==HeroRules.Primary(p.Hunter),$"Latest hero health and legal primary slot{p.SlotIndex}");
                Check(scene.Payload.ValidSpawn(scene,p.TeamIndex,p.Position),$"Correct initial protected room slot{p.SlotIndex}");
                p.ModSetSpectating(false);invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();
            }
            var attack=scene.Players.Items[0];var defense=scene.Players.Items[4];
            Check(PayloadRuntime.Locked(attack)&&!PayloadRuntime.Locked(defense),"Preparation locks attackers but permits defender setup");
            scene.GameState.MatchState=MatchState.InProgress;
            for(int i=0;i<PayloadRuntime.Rules.PreparationTicks+1;i++)PayloadRuntime.Tick(scene);
            Check(scene.Payload.Frame!.Value.Phase==1 && !PayloadRuntime.Locked(attack),"Actual native adapter releases attackers after preparation");
            var cart=scene.Payload.Position;
            for(int i=0;i<8;i++){var p=scene.Players.Items[i];p.Position=cart+new Vector3(20,0,20+i*2);p.Speed=Vector3.Zero;}
            attack.Position=cart+new Vector3(0,.1f,-2);attack.Health=100;
            for(int i=0;i<60;i++)PayloadRuntime.Tick(scene);
            var pushed=scene.Payload.Frame!.Value;
            Check(pushed.Progress>0 && pushed.Escorts==1,"Living unprotected attacker advances physical-cart state");
            Check(attack.Health>100 && attack.Health<=attack.HealthMax,"Authoritative payload healing reaches native health");
            cart=scene.Payload.Position;attack.Position=cart+new Vector3(-2,.1f,0);defense.Position=cart+new Vector3(2,.1f,0);
            long progress=scene.Payload.Frame.Value.Progress;
            for(int i=1;i<4;i++)scene.Players.Items[i].Position=cart+new Vector3(-2,.1f,i*.3f);
            for(int i=0;i<60;i++)PayloadRuntime.Tick(scene);
            Check(scene.Payload.Frame.Value.Contested && scene.Payload.Frame.Value.Progress==progress,"One native defender stops four attackers");
            defense.Health=0;PayloadRuntime.Tick(scene);
            Check(!scene.Payload.Frame.Value.Contested && scene.Payload.Frame.Value.Progress>progress,"Dead defender cannot contest");
            var wire=scene.Payload.Frame.Value;
            using(var stream=new MemoryStream())
            {using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))wire.Write(writer);stream.Position=0;Check(PayloadFrame.Read(new BinaryReader(stream))==wire,"Native payload snapshot round trips exactly");}
            for(int i=0;i<8;i++)scene.Players.Items[i].Position=cart+new Vector3(20,0,20+i*2);
            progress=scene.Payload.Frame.Value.Progress;long best=scene.Payload.Frame.Value.Best;
            for(int i=0;i<PayloadRuntime.Rules.RollbackDelayTicks+90;i++)PayloadRuntime.Tick(scene);
            Check(scene.Payload.Frame.Value.Progress<progress && scene.Payload.Frame.Value.Best==best,"Native unattended rollback preserves best achieved distance");
            Check(sim.StepFailures==0,"No swallowed native simulation failures");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,scope="Actual headless native map and eight-player lifecycle with deterministic controlled occupancy. Not an autonomous bot match, rendered view, Windows execution, or internet gameplay."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
    private sealed class Fixture:IDisposable{internal readonly ServerSim Sim=new();public void Dispose(){Sim.Stop();NetSession.Stop();}}
}
