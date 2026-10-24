using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Mods.Oddball;
public static class OddballChecks
{
    private static int checks;
    private static void Check(bool pass,string message) { checks++; if(!pass)throw new InvalidOperationException(message); Console.WriteLine("PASS "+message); }
    public static int Run()
    {
        try {
            var rules=new OddballMatch();
            for(int i=0;i<180;i++)rules.Step(-1,-1);
            int clock=rules.RemainingTicks;
            for(int i=0;i<5999;i++)rules.Step(0,0);
            Check(rules.Score(0)==99 && rules.RoundsWon[0]==0,"99.983 s does not prematurely win");
            Check(rules.RemainingTicks==clock,"possession pauses objective clock");
            rules.Step(0,0); Check(rules.RoundsWon[0]==1 && rules.Winner<0,"100 points wins first round only");
            for(int i=0;i<300;i++)rules.Step(-1,-1);
            Check(rules.RoundNumber==2 && rules.Score(0)==0 && rules.HoldTicks[0]==6000,"round points reset; individual hold time retained");
            for(int i=0;i<180;i++)rules.Step(-1,-1);
            for(int i=0;i<6000;i++)rules.Step(0,0);
            Check(rules.Winner==0 && rules.RoundsWon[0]==2,"second round wins match");
            var tie=new OddballMatch();for(int i=0;i<18000;i++)tie.Step(-1,-1);
            Check(tie.Overtime && tie.Winner<0,"tied timeout enters disclosed test sudden death");
            for(int i=0;i<60;i++)tie.Step(1,1);
            Check(tie.RoundsWon[1]==1,"next full possession point resolves test overtime");
            foreach(int count in new[]{1,2,4,8}) {
                Check(MapModeCapabilities.Supports(OddballMatch.RoomKey,GameMode.Oddball,MatchWorldProfile.Resolve(count),out string why,count),"Combat Hall entity layer supports "+count+" players: "+why);
            }
            Check(MapModeCapabilities.Supports("MP1 SANCTORUS",GameMode.Oddball,MatchWorldProfile.Resolve(2),out _),"Data Shrine now supports Oddball");
            Check(PlayerEntity.OddballMeleeIntervalTicks==39,"restored 39-tick / 0.65-second melee cadence");
            Native();
            Console.WriteLine($"ODDBALL CHECKS PASS: {checks}"); return 0;
        } catch(Exception e){Console.Error.WriteLine(e);Console.WriteLine($"ODDBALL CHECKS FAILED after {checks}");return 1;}
    }
    private static void Native()
    {
        int count=2;var mode=GameMode.Oddball;
        var profile=MatchWorldProfile.Resolve(count);
        var match=new MatchDefinition{RoomKey=OddballMatch.RoomKey,Mode=mode,PointGoal=100,TimeLimitSeconds=300};
        var session=new SessionStatePacket{MatchId=1,AuthorityEpoch=1,MaxPlayers=(byte)count,Phase=SessionPhase.InMatch,Match=match,WorldProfile=profile};
        var roster=RosterPacket.Create();roster.Count=(byte)count;roster.MatchId=1;roster.AuthorityEpoch=1;roster.Revision=1;
        for(byte i=0;i<count;i++){roster.Slots[i]=i;roster.Generations[i]=1;roster.Hunters[i]=(byte)Hunter.Samus;roster.Teams[i]=(sbyte)i;roster.Names[i]="ODDBALL TEST";}
        var sim=new ServerSim();
        try{
            Check(sim.Start(OddballMatch.RoomKey,mode,count,_=>{},()=>{},roster,session),"native Combat Hall simulation loads");NetSlotManager.Sync();
            var a=PlayerEntity.Players[0];var b=PlayerEntity.Players[1];var scene=a.OwningScene;var state=scene.GameState;
            OctolithFlagEntity? found=null;foreach(var f in scene.GetOctolithFlagEntities()){found=f;break;} var skull=found ?? throw new InvalidOperationException("No native skull");
            Console.WriteLine("NATIVE SKULL SPAWN "+skull.BasePosition);
            CollisionResult floor=default;
            Check(CollisionDetection.CheckBetweenPoints(skull.BasePosition.AddY(2),skull.BasePosition.AddY(-2),TestFlags.None,scene,ref floor) && floor.Plane.Y>.9f,"neutral midpoint has an actual traversable collision floor");
            foreach(var spawn in scene.GetPlayerSpawnEntities()) {
                Console.WriteLine("NATIVE PLAYER SPAWN "+spawn.Position+" team="+spawn.Data.TeamIndex);
                CollisionResult sf=default;
                Check(CollisionDetection.CheckBetweenPoints(spawn.Position.AddY(2),spawn.Position.AddY(-2),TestFlags.None,scene,ref sf) && sf.Plane.Y>.9f,"spawn has actual floor: "+spawn.Id);
            }
            foreach(var p in new[]{a,b}){p.LoadFlags|=LoadFlags.Active;p.TeamIndex=p.SlotIndex;p.Spawn(skull.BasePosition,Vector3.UnitZ,Vector3.UnitY,p.NodeRef,true);}
            for(int i=0;i<180;i++)state.Oddball!.Tick(scene);
            b.Position=a.Position+Vector3.UnitX*5;
            Check(skull.OddballTryPickup(a,false),"human pickup at native skull with clear line of sight");
            Check(a.ModOddballCarrying && a.ModOddballBlocksCombat,"carrier cannot fire ordinary weapons");
            Check(!skull.OddballTryPickup(b,false),"second carrier rejected");
            // A remote client has not run OddballTryPickup. Reproduce that
            // initial weapon cache while applying the real authority flag row.
            a.ModArmWeapon(BeamType.Missile);
            var captured=skull.CaptureReplayAuthority();skull.OddballDrop();
            typeof(PlayerEntity).GetField("_oddWeapon",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(a,BeamType.PowerBeam);
            skull.ApplyLiveAuthority(captured);
            typeof(PlayerEntity).GetMethod("ModOddballInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(a,null);
            Check(a.ModOddballCarrying,"authoritative pickup initializes a remote non-default weapon without an unintended drop");
            int t=state.Oddball!.RemainingTicks;for(int i=0;i<60;i++)state.Oddball.Tick(scene);
            Check(state.Oddball.Score(0)==1 && state.Oddball.RemainingTicks==t,"native possession ticks exactly one point, pauses clock");
            skull.OnCaptured();Check(state.Oddball.Score(0)==1 && skull.Carrier==a,"native bounty deposit cannot score or consume skull");
            skull.OddballDrop();Check(skull.Carrier==null && !a.ModOddballBlocksCombat,"drop restores weapons");
            Check(!skull.OddballTryPickup(a,false),"dropper cooldown prevents immediate re-pickup");
            b.Position=skull.Position.AddY(-1);Check(skull.OddballTryPickup(b,false),"opponent can immediately recover dropped skull");
            b.TakeDamage(1000,DamageFlags.IgnoreInvuln,null,a);state.Oddball.Tick(scene);
            Check(skull.Carrier==null,"carrier death drops skull");
            skull.OddballReset();Check(skull.Position==skull.BasePosition.AddY(1.25f),"reset restores measured native spawn");
            for(int i=0;i<240;i++)sim.Step();Check(sim.StepFailures==0,"240 complete native world ticks without exception");
            Check(b.Health==0,"dead player stays dead during 10-second respawn delay");
            for(int i=0;i<400;i++)sim.Step();Check(b.Health>0,"dead player respawns after 10-second delay");
            // A second native world must not inherit the previous skull or score.
            sim.Stop();
            Check(sim.Start(OddballMatch.RoomKey,mode,count,_=>{},()=>{},roster,session),"second native match loads on same simulation object");
            var next=PlayerEntity.Players[0].OwningScene;
            Check(next.GameState.Oddball is { RoundNumber:1, Winner:-1 } fresh && fresh.Score(0)==0 && fresh.Score(1)==0,"second match starts with fresh rounds, ownership and scores");
        }finally{sim.Stop();}
    }
}
