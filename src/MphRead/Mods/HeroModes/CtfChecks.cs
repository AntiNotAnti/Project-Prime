using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;
using ProjectPrime.CaptureZone;

namespace MphRead.Mods.HeroModes;

/// <summary>Tests production CTF definitions and transition code. No world/graphics are faked as gameplay.</summary>
public static class CtfChecks
{
    private static readonly List<string> Checks = new();
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); Checks.Add(name); }
    public static int Run()
    {
        Checks.Clear();
        try
        {
            Flags(); Roster(); Definitions(); CtfBotStartupChecks.Run(Check);
            Console.WriteLine(JsonSerializer.Serialize(new { passed=true, count=Checks.Count, checks=Checks,
                scope="Production CTF state machine, roster, weapons and asset-free real bot objective initialization/input; full native personality/world execution remains maptest acceptance",
                native_map_gameplay_verified=false, graphics_verified=false, online_world_verified=false },
                new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new {passed=false,count=Checks.Count,checks=Checks,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));
            return 1;
        }
    }
    private static void Flags()
    {
        var f = new CtfFlagState(1, new Vector3(0,1,0));
        Check(f.Phase==CtfFlagPhase.Home && f.CarrierSlot==-1, "Flag starts home and unowned");
        Check(!f.Pickup(0,1,true,false,f.Position,true),"Cannot steal own flag");
        Check(!f.Pickup(0,0,false,false,f.Position,true),"Dead or alternate-form player cannot pick up");
        Check(!f.Pickup(0,0,true,true,f.Position,true),"Cannot carry both flags");
        Check(!f.Pickup(0,0,true,false,f.Position,false),"Wall-blocked pickup denied");
        Check(!f.Pickup(0,0,true,false,new Vector3(100,1,0),true),"Out-of-range pickup denied");
        Check(!f.Pickup(8,0,true,false,f.Position,true),"Invalid slot denied");
        Check(!f.Pickup(0,0,true,false,new Vector3(float.NaN,1,0),true),"NaN pickup denied");
        Check(f.Pickup(0,0,true,false,f.Position,true),"Enemy flag pickup succeeds");
        uint revision=f.Revision;
        Check(!f.Pickup(1,0,true,false,f.Position,true) && f.CarrierSlot==0 && f.Revision==revision,"Second pickup cannot duplicate ownership");
        f.Follow(new Vector3(2,2,0));
        Check(f.Position==new Vector3(2,2,0),"Carried flag follows validated position");
        Check(!f.Capture(1,true,true),"Other player cannot capture held flag");
        Check(!f.Capture(0,false,true),"Own flag must be home to score");
        Check(!f.Capture(0,true,false),"Capture requires own base");
        Check(f.Drop(Vector3.UnitZ,true),"Throw releases ownership");
        Check(f.Phase==CtfFlagPhase.Dropped && f.CarrierSlot==-1 && f.Velocity.Z==CtfRules.ThrowSpeed,"Throw has real forward velocity");
        Check(!f.Drop(Vector3.UnitZ,true),"Repeated throw does nothing");
        Check(!f.Pickup(0,0,true,false,f.Position,true),"Thrower cannot immediately repick");
        Check(f.CanPickup(1,0,true,false,f.Position,true),"Teammate can receive thrown flag");
        Vector3 prior=f.Position, proposed=f.ProposeMotion(1f/60);
        Check(proposed.Z>prior.Z && f.Velocity.Y<CtfRules.ThrowLift,"Thrown flag moves and gravity integrates");
        f.ResolveMotion(proposed,Vector3.UnitY,true);
        Check(f.Grounded && f.Velocity==Vector3.Zero,"Swept floor contact grounds flag");
        for(int i=0;i<31;i++)f.Advance(1f/60,false,false);
        Check(f.Pickup(0,0,true,false,f.Position,true),"Thrower lock expires without permanent lock");
        Check(f.Capture(0,true,true),"Legal capture atomically resets home");
        Check(!f.Capture(0,true,true),"Duplicate capture cannot score twice");
        for(int round=0;round<12;round++)
        {
            Check(f.Pickup(0,0,true,false,f.Position,true),$"Cycle {round}: fresh ownership");
            f.Drop(Vector3.Zero,false);
            for(int i=0;i<180;i++)f.Advance(1f/60,true,false);
            Check(f.Phase==CtfFlagPhase.Home && f.CarrierSlot==-1 && f.Position==f.Home,$"Cycle {round}: defended return clears ownership/timers");
        }
        f.Pickup(0,0,true,false,f.Position,true);f.Drop(Vector3.Zero,false);
        for(int i=0;i<60;i++)f.Advance(1f/60,true,false);
        float saved=f.ReturnProgress;
        for(int i=0;i<60;i++)f.Advance(1f/60,true,true);
        Check(Math.Abs(f.ReturnProgress-saved)<1e-5,"Contested return pauses");
        f.Advance(1f/60,false,false);
        Check(f.ReturnProgress==0,"Abandoned return loses progress");
        for(int i=0;i<910 && f.Phase==CtfFlagPhase.Dropped;i++)f.Advance(1f/60,false,false);
        Check(f.Phase==CtfFlagPhase.Home,"Unattended flag resets");
        f.Pickup(0,0,true,false,f.Position,true);f.Drop(new Vector3(float.NaN),true);
        Check(CtfRules.Finite(f.Velocity),"Invalid throw direction cannot inject NaNs");
        f.ResolveMotion(new Vector3(float.NaN),Vector3.Zero,false);
        Check(f.Phase==CtfFlagPhase.Home && CtfRules.Finite(f.Position),"Invalid physics position safely resets");
        bool rejected=false;try{f.Advance(float.NaN,false,false);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected,"Invalid fixed-step duration rejected");
    }
    private static void Roster()
    {
        for(int size=1;size<=4;size++)
        {
            var core=new CaptureZoneRoster(size);
            for(int t=0;t<2;t++)for(int p=0;p<size;p++)
            {
                Guid id=HeroRosterBridge.SlotId(t*4+p);
                Check(core.Join(id,$"T{t}P{p}",t).Accepted,$"{size}v{size}: join {t}/{p}");
                Check(core.Select(id,(ZoneHunter)p).Accepted,$"{size}v{size}: opposing mirrored hunter allowed {t}/{p}");
                core.Ready(id,true);
            }
            Check(core.CanStart().Accepted,$"{size}v{size}: can start without bots");
            core.SetPhase(ZoneLobbyPhase.Combat);
            Guid local=HeroRosterBridge.SlotId(0);
            Check(!core.Select(local,ZoneHunter.Sylux).Accepted,$"{size}v{size}: living/combat selection denied");
            core.SetRespawnSelection(local,true);
            Check(core.Select(local,ZoneHunter.Sylux).Accepted,$"{size}v{size}: trusted respawn selection allowed");
            core.SetRespawnSelection(local,false);
            Check(!core.Select(local,ZoneHunter.Trace).Accepted,$"{size}v{size}: respawn permission revoked");
        }
        var race=new CaptureZoneRoster(4);var a=Guid.NewGuid();var b=Guid.NewGuid();
        race.Join(a,"A",0);race.Join(b,"B",0);race.Select(a,ZoneHunter.Samus);race.Select(b,ZoneHunter.Kanden);
        RosterResult? ra=null,rb=null;
        Parallel.Invoke(()=>ra=race.Select(a,ZoneHunter.Trace),()=>rb=race.Select(b,ZoneHunter.Trace));
        Check(ra!.Accepted!=rb!.Accepted,"Simultaneous teammate reservation has exactly one winner");
        Check(race.Snapshot().Slots.Count(s=>s.Hunter==ZoneHunter.Trace)==1,"Race leaves one Trace");
        Check(race.Snapshot().Slots.All(s=>s.Hunter.HasValue),"Failed swap retains prior reservation");
        Guid winner=ra.Accepted?a:b,loser=ra.Accepted?b:a;
        race.Disconnect(winner);Check(race.Select(loser,ZoneHunter.Trace).Accepted,"Disconnect releases hunter");
        race.Spectate(loser);Check(!race.Select(loser,ZoneHunter.Samus).Accepted,"Spectator cannot reserve hunter");
        race.Reset();Check(race.Snapshot().Slots.Count==0,"Lobby reset releases all reservations");
    }
    private static void Definitions()
    {
        for(int i=0;i<7;i++)
        {
            var h=(Hunter)i;
            Check(HeroRules.ToGame(HeroRules.ToZone(h))==h,$"Hunter enum mapping {h}");
            Check(CtfRules.Definition(h).MaxHealth==2*Metadata.PlayerValues[i].EnergyTank-1,$"Native health capacity {h}");
            Check(CtfRules.Definition(h).DamageReduction==0,$"No hero damage reduction {h}");
            Check(CtfRules.ReduceDamage(h,100)==100,$"Native damage unchanged {h}");
            Check(CtfRules.ReduceDamage(h,0)==0,$"Zero damage remains zero {h}");
        }
        Check(Object.ReferenceEquals(CtfRules.WeaponTable,Weapons.WeaponsMP),"CTF references the exact native multiplayer weapon table");
        Check(CtfRules.WeaponTable[0].Name!="Aether Pulse","Native Power Beam is not renamed/reworked");
        Check(CtfRules.WeaponTable[(int)BeamType.Battlehammer].UnchargedGravity==Weapons.WeaponsMP[(int)BeamType.Battlehammer].UnchargedGravity,"Native Battlehammer gravity retained");
        Check(CtfRules.WeaponTable[(int)BeamType.Battlehammer].ShotCooldown==Weapons.WeaponsMP[(int)BeamType.Battlehammer].ShotCooldown,"Native Battlehammer cadence retained");
        Check(CtfRules.RulesetVersion==2 && CtfRules.Capability==0x33465443,"Native combat unchanged; CTF audio event compatibility is explicit");
        foreach(ItemType item in Enum.GetValues<ItemType>())
            Check(CtfRules.AllowedPickup(item),$"CTF does not filter native pickup {item}");
        var nativeRules = new MatchDefinition {Mode=GameMode.CaptureTheFlag,AffinityWeapons=true,DisablePowerups=true,BalancedMode=true}.NormalizeLegacy();
        Check(!nativeRules.AffinityWeapons&&!nativeRules.DisablePowerups&&!nativeRules.BalancedMode,"Native inventory, pickups and combat survive inherited modifiers");
        Check(ProjectPrime.CaptureZone.HeroCatalog.For(ZoneHunter.Trace).MaxHealth==90,"Capture the Zone character catalog is untouched");
        var state=new SceneGameState(new ScenePlayerRegistry());state.ConfigureMatchMode(GameMode.CaptureTheFlag,true);
        Check(state.Teams && state.IsOctolithMode,"CTF joins native team/objective lifecycle");
        Check(state.PointGoal==CtfRules.CaptureLimit && state.MatchTime==CtfRules.MatchSeconds,"CTF mode default goals");
        Check(MatchGoalRules.DefaultValue(GameMode.CaptureTheFlag)==CtfRules.CaptureLimit,"Lobby default score matches gameplay");
        state.ConfigureMatchMode(GameMode.Capture,true);
        Check(state.PointGoal==5 && !HeroRules.Active(state.Mode),"Ordinary Capture defaults restored");
        Check(SceneSetup.GetMultiplayerEntityLayer(GameMode.CaptureTheFlag,4,ResourceSpawnProfile.Vanilla)
            ==SceneSetup.GetMultiplayerEntityLayer(GameMode.Capture,4,ResourceSpawnProfile.Vanilla),"Native Capture entity layer reused");
        var packet=RosterPacket.Create();packet.Count=1;packet.Slots[0]=0;packet.Teams[0]=0;packet.Hunters[0]=0;
        packet.Flags[0]=HeroRosterBridge.SpectatorFlag;packet.Names[0]="Spectator";
        var bytes=new byte[RosterPacket.Size];packet.Write(bytes);
        Check(RosterPacket.TryRead(bytes,out var decoded)&&decoded.Flags[0]==2,"CTF spectator bit survives actual roster wire codec");
        Check(HeroRosterBridge.Snapshot(decoded,4).Slots[0].Team==-1,"Spectator excluded from team reservations");
        Check(CtfRules.ZoomDescription.Contains("pick it up") && CtfRules.ZoomDescription.Contains("throw it"),"Controls description includes pickup and throw");
    }
}
