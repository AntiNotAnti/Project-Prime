using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Mods.Network;
namespace MphRead.Mods.HeroModes;
internal static class HeroMergeChecks
{
    internal static int Run()
    {
        var checks=new List<string>();
        void Check(bool okay,string label){if(!okay)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("MERGE PASS "+label);}
        try
        {
            Check(new[]{"Battle","Survival","Bounty","Defender","Nodes","Capture","Hardpoint","Gun Game","One in the Chamber","Kill Confirmed","Headhunter","Relic","Prime Hunter","Hero Shooter","Oddball","Capture the Flag"}.SequenceEqual(MatchTypeCatalog.GameTypes.Select(x=>x.Label)),"All existing and delivered modes remain reachable");
            Check(Enum.GetNames<PacketType>().Where(x=>x!=nameof(PacketType.HitVerdict)).Select(x=>(byte)Enum.Parse<PacketType>(x)).Distinct().Count()==Enum.GetNames<PacketType>().Length-1 && PacketType.HitVerdict==PacketType.CombatAck,"Network packet IDs are unique except the documented CombatAck/HitVerdict alias");
            Check((byte)PacketType.PartyReservationRequest == 80 && (byte)PacketType.CustomWeaponManifest == 83
                && (byte)PacketType.WeaponPackageConfirmed == 101, "Updated party and custom-weapon wire IDs survive integration");
            Check(RefusedPacket.ReasonCustomWeaponReadiness == 7 && RefusedPacket.ReasonCtfRuleset == 8,
                "Distinct custom-weapon and custom-mode refusal reasons");
            var reservations = RosterPacket.Create();
            reservations.Count = 3;
            for (int i = 0; i < 3; i++) { reservations.Slots[i] = (byte)i; reservations.Hunters[i] = (byte)i; }
            reservations.Teams[0] = reservations.Teams[1] = 0; reservations.Teams[2] = 1;
            var allowed = HeroSelectionPolicy.Available(reservations, 0);
            Check(allowed.Contains(Hunter.Samus) && !allowed.Contains(Hunter.Kanden) && allowed.Contains(Hunter.Trace),
                "Native hero picker reserves teammate hunters and keeps enemy choices available");
            reservations.Flags[0] = HeroRosterBridge.SpectatorFlag;
            Check(HeroSelectionPolicy.Available(reservations, 0).Length == 0, "Spectators cannot reserve a hunter");
#if !MPHREAD_SERVER
            Check(Launcher.RmlUi.Core.RmlUiModeCatalog.GameTypes.Any(type => type.Resolve(MatchFormat.FourVsFour) == GameMode.PushThePayload),
                "Native mode carousel reaches the physical payload objective");
#endif
            var custom=new[]{GameMode.CaptureTheZone,GameMode.PushThePayload,GameMode.Oddball,GameMode.CaptureTheFlag};
            Check(custom.Select(x=>(int)x).Distinct().Count()==4,"Every custom game/wire mode ID is unique");
            Check(new[]{CtzRules.Capability,CtfRules.Capability,UnifiedModeRules.Capability}.Distinct().Count()==3,"Ruleset capability tags are distinct");
            var family=MatchTypeCatalog.GameTypes.Single(x=>x.Label=="Hero Shooter");
            Check(MatchTypeCatalog.GameTypes.Count(x=>HeroModeMenu.IsFamily(x))==1,"Exactly one parent Hero Shooter mode");
            Check(!MatchTypeCatalog.GameTypes.Any(x=>x.Label=="Capture the Zone"||x.Label=="Push the Payload"),"Objectives are not duplicated as top-level modes");
            Check(HeroModeMenu.Labels.SequenceEqual(new[]{"Capture the Zone","Push the Payload"}),"Exact user-requested objective names and ordering");
            foreach(var format in new[]{MatchFormat.OneVsOne,MatchFormat.TwoVsTwo,MatchFormat.ThreeVsThree,MatchFormat.FourVsFour})
            {
                Check(HeroModeMenu.Resolve(family,format,0)==GameMode.CaptureTheZone,"Zone resolves for "+format);
                Check(HeroModeMenu.Resolve(family,format,1)==GameMode.PushThePayload,"Payload resolves for "+format);
            }
            Check(MatchTypeCatalog.BaseModeIndex(GameMode.CaptureTheZone)==MatchTypeCatalog.BaseModeIndex(GameMode.PushThePayload),"Both durable objective IDs restore the same parent");
            Check((int)GameMode.CaptureTheZone==25 && (int)GameMode.PushThePayload==26,"Legacy zone numeric ID preserved; distinct payload ID");
            foreach(var h in new[]{(Hunter.Trace,90,20),(Hunter.Samus,180,10),(Hunter.Sylux,180,5),(Hunter.Kanden,160,10),(Hunter.Noxus,220,5),(Hunter.Weavel,260,5),(Hunter.Spire,300,5)})
            {Check(HeroRules.Definition(h.Item1).MaxHealth==h.Item2 && CtzRules.RespawnTicks(h.Item1)==h.Item3*60,"Latest shared HP and respawn "+h.Item1);}
            Check(HeroRules.Active(GameMode.CaptureTheZone)&&HeroRules.Active(GameMode.PushThePayload)&&!HeroRules.Active(GameMode.Battle)&&!HeroRules.Active(GameMode.Capture),"Shared combat remains isolated from ordinary modes");
            Check(!HeroRules.IsZone(GameMode.PushThePayload)&&!HeroRules.IsPayload(GameMode.CaptureTheZone),"Objective predicates cannot run one another");
            foreach(var mode in new[]{GameMode.CaptureTheZone,GameMode.PushThePayload})
            {
                var m=new MatchDefinition{Mode=mode,Fiesta=true,InstaGib=true,EnhancedHunters=true,TimeLimitSeconds=420}.NormalizeLegacy();
                Check(!m.Fiesta&&!m.InstaGib&&!m.EnhancedHunters&&m.AffinityWeapons&&!m.FriendlyFire&&m.TimeLimitSeconds==0,"Shared legal hero modifiers "+mode);
                Check(m.PointGoal==(mode==GameMode.CaptureTheZone?2:3),"Mode-specific objective goal "+mode);
            }
            var payload=new PayloadFrame(0,0,0,1,0,12000,1000,100,200,6000000,0,0,false,0,false,1,-1,200,0,0,0,0,0,-1,false);
            Check(payload.Valid,"Bounded payload fixture valid");
            byte[] bytes;
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))payload.Write(writer);bytes=stream.ToArray();}
            using(var reader=new BinaryReader(new MemoryStream(bytes)))Check(PayloadFrame.Read(reader)==payload,"Payload state exact binary round trip");
            Check(!(payload with{Progress=201}).Valid&&!(payload with{Attacker=2}).Valid&&!(payload with{Winner=0}).Valid,"Reject invalid progress, team, and premature winner");
            foreach(var world in new[]{new ReplayAuthorityWorld{MatchId=1,Epoch=1,Tick=1},new ReplayAuthorityWorld{MatchId=1,Epoch=1,Tick=1,Zone=new CtzState().State},new ReplayAuthorityWorld{MatchId=1,Epoch=1,Tick=1,Payload=payload}})
            {var encoded=world.Encode();var decoded=ReplayAuthorityWorld.Decode(encoded);Check(decoded.Payload==world.Payload&&decoded.Zone==world.Zone,"World snapshot coexistence version "+encoded[0]);}
            bool rejected=false;try{_ = new ReplayAuthorityWorld{MatchId=1,Epoch=1,Tick=1,Payload=payload,Zone=new CtzState().State}.Encode();}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Cannot serialize both objectives at once");
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,scope="Integrated selector, shared hero definitions, mode isolation and objective snapshot regression"},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
}
