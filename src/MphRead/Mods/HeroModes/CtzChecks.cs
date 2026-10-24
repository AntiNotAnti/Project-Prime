using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using OpenTK.Mathematics;
using ProjectPrime.CaptureZone;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.HeroModes;

public static class CtzChecks
{
    public static int Run()
    {
        var checks = new List<string>();
        void Check(bool ok,string name) { if(!ok) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            Check(!HeroRules.Active(GameMode.Battle) && !HeroRules.Active(GameMode.Capture) && HeroRules.Active(GameMode.CaptureTheZone),"Mode isolation");
            var before=Weapons.WeaponsMP.Select(w=>(w.UnchargedDamage,w.ShotCooldown,w.UnchargedGravity,w.UnchargedSpeed)).ToArray();
            foreach(Hunter h in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
            {
                Check(HeroRules.ToGame(HeroRules.ToZone(h))==h,$"Native enum mapping {h}");
                var hero=HeroRules.Definition(h);
                Check(hero.MaxHealth is >=90 and <=300 && HeroRules.ReduceDamage(h,100)==(uint)Math.Ceiling(100*(1-hero.DamageReduction)),$"HP and actual DR {h}");
                Check(!string.IsNullOrEmpty(HeroRules.WeaponDetails(h)),$"Shared gameplay-derived details {h}");
            }
            Check(HeroRules.Definition(Hunter.Trace).MaxHealth==90,"Trace 90 HP");
            Check(HeroRules.ReduceDamage(Hunter.Weavel,100)==85,"Weavel exact 15 percent reduction");
            Check(HeroRules.Primary(Hunter.Weavel)==BeamType.Battlehammer,"Battlehammer affinity");
            var burst=HeroRules.WeaponTable[(int)BeamType.Battlehammer+9];
            Check(burst.UnchargedGravity==0 && burst.UnchargedSpeed>0,"Straight projectile row");
            Check(burst.FullCharge>burst.MinCharge && (burst.Flags & WeaponFlags.CanCharge)==0,"Noncharging burst has a safe native animation range");
            Check(CtzRules.BurstRounds==3 && CtzRules.BurstPeriodTicks==24,"Three rounds and discrete 60Hz cadence");
            var pulse=HeroRules.WeaponTable[(int)BeamType.PowerBeam];
            Check(pulse.UnchargedDamage!=Weapons.WeaponsMP[0].UnchargedDamage || pulse.ShotCooldown!=Weapons.WeaponsMP[0].ShotCooldown,
                "Aether Pulse changes mechanics, not only name");
            Check(before.SequenceEqual(Weapons.WeaponsMP.Select(w=>(w.UnchargedDamage,w.ShotCooldown,w.UnchargedGravity,w.UnchargedSpeed))),
                "Ordinary shared weapon table unchanged");
            foreach(ItemType item in Enum.GetValues<ItemType>())
                Check(HeroRules.AllowedPickup(item)==(item is ItemType.HealthSmall or ItemType.HealthMedium or ItemType.HealthBig),$"Health-only pickup policy {item}");
            var volume=new CaptureZoneDefinition();
            Check(volume.Valid && volume.Contains(Vector3.Zero) && !volume.Contains(new Vector3(9,0,0)) && !volume.Contains(new Vector3(float.NaN,0,0)),
                "Explicit finite volume and exact boundary");
            var zone=new CtzState();
            for(int i=0;i<CtzRules.CountdownTicks;i++)zone.Tick(1,0);
            Check(zone.State.Phase==CtzPhase.Active && zone.State.Unlock==600,"Round countdown");
            for(int i=0;i<600;i++)zone.Tick(1,0);
            Check(zone.State.Owner==-1 && zone.State.Capture==0,"Ten-second unlock prevents early capture");
            for(int i=0;i<150;i++)zone.Tick(1,0);
            var held=zone.State;
            for(int i=0;i<120;i++)zone.Tick(1,1);
            Check(zone.State.Capture==held.Capture && zone.State.Red==0 && zone.State.Contested,"Contest pauses capture");
            zone.Tick(0,0);
            Check(zone.State.Capture==0 && zone.State.Capturing==-1,"Abandoned capture resets");
            for(int i=0;i<300;i++)zone.Tick(4,0);
            Check(zone.State.Owner==0 && zone.State.Red==0,"Four players still require five seconds");
            for(int i=0;i<60;i++)zone.Tick(1,0);
            Check(zone.State.Red==60,"One percent per second with occupation");
            for(int i=0;i<60;i++)zone.Tick(0,0);
            Check(zone.State.Red==60 && zone.State.Owner==0,"Empty ownership never scores");
            for(int i=0;i<300;i++)zone.Tick(0,1);
            Check(zone.State.Owner==1 && zone.State.Blue==0,"Enemy takes ownership after full capture");
            zone.Apply(zone.State with{Owner=0,Red=5999,Blue=0,Capturing=-1,Capture=0});
            zone.Tick(1,1);
            Check(zone.State.Red==5999 && zone.State.Phase==CtzPhase.Active,"Contest evaluated before victory");
            zone.Tick(1,0);
            Check(zone.State.RedWins==1 && zone.State.Phase==CtzPhase.Intermission,"Round won exactly at 100 percent");
            for(int i=0;i<300;i++)zone.Tick(0,0);
            Check(zone.State.Round==2 && zone.State.RedWins==1 && zone.State.Owner==-1 && zone.State.Red==0,"Round reset preserves match win");
            zone.Apply(zone.State with{Phase=CtzPhase.Active,Remaining=0,Unlock=0,Owner=0,Red=5999});
            zone.Tick(1,0);
            Check(zone.State.RedWins==2 && zone.State.Phase==CtzPhase.Finished,"Best-of-three finish");
            var complete=zone.State;zone.Tick(4,0);Check(zone.State==complete,"Finished state cannot award again");
            using(var ms=new MemoryStream())
            {
                using(var w=new BinaryWriter(ms,System.Text.Encoding.UTF8,true))complete.Write(w);
                ms.Position=0;
                using var r=new BinaryReader(ms);Check(CtzSnapshot.Read(r)==complete,"Bounded objective snapshot roundtrip");
            }
            var world=new ReplayAuthorityWorld{MatchId=1,Epoch=1,Tick=1};
            byte[] ordinary=world.Encode();
            Check(ordinary[0]==10 && ReplayAuthorityWorld.Decode(ordinary).Zone==null,"Unified ordinary authority format is version ten");
            world.Zone=complete;byte[] custom=world.Encode();
            Check(custom[0]==10 && ReplayAuthorityWorld.Decode(custom).Zone==complete,"Unified CTZ authority payload version ten");
            for(int length=custom.Length-1;length>=custom.Length-4;length--)
            {
                bool refused=false;try{ReplayAuthorityWorld.Decode(custom.AsSpan(0,length));}catch(InvalidDataException){refused=true;}
                Check(refused,"Truncated CTZ snapshot rejected "+length);
            }
            world.Zone=null;Check(ordinary.SequenceEqual(world.Encode()),"Ordinary wire bytes unchanged after CTZ snapshot reset");
            for(int capacity=1;capacity<=4;capacity++)
            {
                var roster=new CaptureZoneRoster(capacity);
                for(int team=0;team<2;team++)for(int i=0;i<capacity;i++)
                {
                    Guid id=HeroRosterBridge.SlotId(team*4+i);
                    Check(roster.Join(id,$"T{team}P{i}",team).Accepted,"Join "+capacity+"/"+team+"/"+i);
                    Check(roster.Select(id,(ZoneHunter)i).Accepted,"Unique same-team selection "+capacity+"/"+team+"/"+i);
                    roster.Ready(id,true);
                }
                Check(roster.CanStart().Accepted,$"Equal ready teams {capacity}v{capacity}");
                var first=HeroRosterBridge.SlotId(0);
                if(capacity>=2)
                {
                    var old=roster.Snapshot().Slots.Single(s=>s.PlayerId==first).Hunter;
                    Check(!roster.Select(first,ZoneHunter.Kanden).Accepted
                        && roster.Snapshot().Slots.Single(s=>s.PlayerId==first).Hunter==old,"Failed atomic switch keeps prior "+capacity);
                }
                roster.SetPhase(ZoneLobbyPhase.Combat);
                Check(!roster.Select(first,ZoneHunter.Weavel).Accepted,"Live swap rejected "+capacity);
                roster.SetRespawnSelection(first,true);
                Check(roster.Select(first,ZoneHunter.Weavel).Accepted,"Trusted death enables legal swap "+capacity);
                roster.Disconnect(first);
                Check(!roster.Snapshot().Slots.Any(s=>s.PlayerId==first),"Disconnect releases reservation "+capacity);
            }
            var race=new CaptureZoneRoster(2);var a=Guid.NewGuid();var b=Guid.NewGuid();
            race.Join(a,"A",0);race.Join(b,"B",0);
            RosterResult? ar=null,br=null;
            Parallel.Invoke(()=>ar=race.Select(a,ZoneHunter.Trace),()=>br=race.Select(b,ZoneHunter.Trace));
            Check((ar!.Accepted?1:0)+(br!.Accepted?1:0)==1,"Concurrent Trace selection has exactly one winner");
            var packet=RosterPacket.Create();packet.Count=2;packet.Slots[0]=0;packet.Slots[1]=1;
            packet.Teams[0]=0;packet.Teams[1]=1;packet.Hunters[0]=packet.Hunters[1]=(byte)Hunter.Trace;
            packet.Names[0]="A";packet.Names[1]="B";packet.LobbyReady[0]=packet.LobbyReady[1]=true;
            var bridge=HeroRosterBridge.Create(packet,4);
            Check(bridge.CanStart().Accepted,"Authoritative roster bridge mirrors across teams");
            packet.Flags[1]=HeroRosterBridge.SpectatorFlag;
            Check(!HeroRosterBridge.Create(packet,4).CanStart(false).Accepted,"Spectator excluded from start roster");
            packet.Flags[1]=0;
            Check(HeroRosterBridge.Create(packet,4).CanStart().Accepted,"Spectator readmission restores valid roster");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                test_scope="Production domain, roster, weapon definitions and authority serializer; no gameplay or Windows claim"},
                new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));
            return 1;
        }
    }
}
