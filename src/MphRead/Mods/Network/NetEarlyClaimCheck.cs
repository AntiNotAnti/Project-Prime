using System;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Real admitted native paths against detached historical bodies, then
/// ordinary claim arbitration. This is not a rendered/network-latency campaign.</summary>
public static class NetEarlyClaimCheck
{
    public static int Run(string room)
    {
        Headless.Enter();
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        var priorMode = NetClaimEarlySettlement.Mode;
        int priorCeiling = NetUnlagged.MaxRewindFrames;
        try
        {
            foreach (var mode in new[] { EarlyClaimMode.Off, EarlyClaimMode.Shadow, EarlyClaimMode.Enabled })
            foreach (bool older in new[] { false, true })
            foreach (ushort damage in new ushort[] { 1, 2 })
            {
                var sim = new ServerSim();
                if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
                try
                {
                    NetSession.ApplyMatchState(new MatchStatePacket { MatchId=1, AuthorityEpoch=1,
                        RoomKey=room, Mode=(byte)GameMode.Battle, Flags=MatchStatePacket.FlagInProgress }, false);
                    var roster=RosterPacket.Create(); roster.MatchId=1; roster.AuthorityEpoch=1; roster.Revision=1; roster.Count=2;
                    for(byte slot=0;slot<2;slot++) { roster.Slots[slot]=slot; roster.Generations[slot]=1; roster.Hunters[slot]=(byte)Hunter.Samus; roster.Names[slot]="Early proof"; }
                    NetSession.ApplyRoster(roster); NetSlotManager.Sync();
                    for(int i=0;i<120;i++) sim.Step();
                    NetHitClaims.Reset(); NetFireEvents.Reset(); NetUnlagged.MaxRewindFrames=45;
                    NetClaimEarlySettlement.Mode=mode;
                    var shooter=PlayerEntity.Players[0]; var victim=PlayerEntity.Players[1];
                    shooter.ModArmWeapon(BeamType.Imperialist); shooter.Health=99;
                    int initialHealth=damage==2?2:99; victim.Health=initialHealth;
                    var frameProperty=typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!;
                    void Frame(uint frame) => frameProperty.SetValue(null,frame);
                    var aim=typeof(PlayerEntity).GetMethod("UpdateAimVecs",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    shooter.ModSetAim(Vector3.UnitZ); aim.Invoke(shooter,null);
                    Vector3 muzzle=shooter.ModMuzzlePos;
                    victim.ModPlaceAt(muzzle+Vector3.UnitZ-Vector3.UnitY*(Fixed.ToFloat(victim.Values.MinPickupHeight)+.05f));
                    IntentPacket Carrier(uint now,uint source,uint id)
                    {
                        shooter.ModCaptureFireSource(false,out var body,out var up,out byte flags);
                        var history=new FireEventHistory();
                        history[0]=new(id,source,source,0,FireEventKind.PressFire,(byte)BeamType.Imperialist,0,FireEvent.ScopedStateBit,
                            FireEvent.FlagPose,muzzle,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ,default,body,up,flags);
                        return new() { MatchId=1, AuthorityEpoch=1, SlotGeneration=NetPlayerLifecycle.Generation(0),LifeId=NetPlayerLifecycle.Get(0),
                            Frame=now,AckFrame=source,Position=shooter.Position,Aim=Vector3.UnitZ,WeaponSelect=(byte)BeamType.Imperialist,
                            HasFireEvents=true,FireEventCount=1,FireEvents=history };
                    }
                    void EmitDetached()
                    {
                        int health=victim.Health; victim.Health=0;
                        try { NetAcceptedAttacks.EmitPending(); foreach(var beam in shooter.EquipInfo.Beams)
                            if(beam.Lifespan>0 && beam.Age==0 && !beam.Flags.TestFlag(BeamFlags.Collided)) beam.Process(); }
                        finally { victim.Health=health; }
                    }
                    if(older)
                    {
                        Frame(200); NetUnlagged.Record(200); NetSession.AcceptSlotIntent(0,Carrier(200,200,1)); EmitDetached();
                        Check(NetAcceptedAttacks.Authorized(0,1),"earlier attack retained after native emission");
                    }
                    const uint launch=400, arrival=430;
                    Frame(launch); NetUnlagged.Record(launch);
                    Frame(arrival); NetUnlagged.Record(arrival);
                    Check(NetSession.AcceptSlotIntent(0,Carrier(arrival,launch,2)) && NetAcceptedAttacks.Authorized(0,2),"independent resource-backed delayed Imperialist admitted");
                    EmitDetached();
                    BeamProjectileEntity? native=null;
                    foreach(var beam in shooter.EquipInfo.Beams) if(beam.ModShotId==2) native=beam;
                    Check(native!=null && native.ModWitnessComponent!=0,"native spawn owns exact witness component");
                    var claim=new HitClaimPacket { MatchId=1,AuthorityEpoch=1,ClaimId=1,Beam=(byte)BeamType.Imperialist,
                        ShooterGeneration=NetPlayerLifecycle.Generation(0),ShooterLifeId=NetPlayerLifecycle.Get(0),
                        VictimSlot=1,VictimGeneration=NetPlayerLifecycle.Generation(1),VictimLifeId=NetPlayerLifecycle.Get(1),
                        ShotId=2,Frame=launch,AckFrame=launch,LaunchFrame=launch,HitPoint=victim.Position,Damage=damage,Flags=HitClaimPacket.FlagDirect };
                    Check(NetAcceptedAttacks.ValidateClaim(0,claim),"independent native path proves historical contact");
                    byte[] wire=new byte[1+HitClaimPacket.Size]; wire[0]=1; claim.Write(wire.AsSpan(1));
                    NetSession.SlotPing[0]=250; int grace=NetHitClaims.GraceFor(0);
                    NetHitClaims.Receive(0,wire); NetHitClaims.Tick();
                    Check(victim.Health==initialHealth && NetHitClaims.ClaimsPendingCurrent==1,"open admission horizon retains claim");
                    Frame(446); NetHitClaims.Tick();
                    Check(victim.Health==initialHealth,"fractional boundary remains closed to early application");
                    Frame(447); NetHitClaims.Tick();
                    bool expected=mode==EarlyClaimMode.Enabled && !older;
                    Check(victim.Health==(expected?initialHealth-damage:initialHealth),$"{mode}, older={older}: only complete proof settles early");
                    Check(NetClaimEarlySettlement.AppliedEarly==(expected?1:0),"only applied early damage counted");
                    if(expected)
                    {
                        Check(NetClaimEarlySettlement.SavedWaitFrames==18,"250ms fixture saves 18 grace frames (300ms)");
                        NetUnlagged.MaxRewindFrames=NetUnlagged.MaxRewindCeiling;
                        NetSession.AcceptSlotIntent(0,Carrier(448,399,3));
                        Check(!NetAcceptedAttacks.Authorized(0,3),"sealed admission cannot reopen after ceiling change");
                        NetUnlagged.MaxRewindFrames=45;
                    }
                    Frame(arrival+(uint)grace); NetHitClaims.Tick();
                    Check(victim.Health==initialHealth-damage && NetHitClaims.AppliedHere==1,"all modes converge on one ordinary damage application");
                    NetHitClaims.Receive(0,wire); NetHitClaims.Tick();
                    Check(victim.Health==initialHealth-damage,"lost-verdict retransmission is idempotent");
                    for(int retry=0;retry<3;retry++)
                        Check(NetHitClaims.AlreadyRescued(0,1,2,native!.ModLaunchKey,
                            NetHitClaims.ComponentKey(native.ModWitnessComponent,true,false)),"exact native callback stays suppressed");
                    native!.EnhancedDirectHit=true;
                    try
                    {
                        for(int retry=0;retry<3;retry++) victim.TakeDamage(damage,DamageFlags.NoDmgInvuln,null,native);
                        Check(victim.Health==initialHealth-damage,"late native TakeDamage cannot apply the rescued component again");
                    }
                    finally { native.EnhancedDirectHit=false; }
                    Console.WriteLine($"early-proof mode={mode} older={older} damage={damage} savedFrames={NetClaimEarlySettlement.SavedWaitFrames} health={victim.Health}");
                    Check(!NetHitClaims.AlreadyRescued(0,1,2,native!.ModLaunchKey,
                        NetHitClaims.ComponentKey(native.ModWitnessComponent,false,false)),"direct rescue does not spend a separate splash component");
                }
                finally { sim.Stop(); }
            }
            Console.WriteLine($"PASS: {checks} native early-settlement assertions"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetClaimEarlySettlement.Reset(); NetClaimEarlySettlement.Mode=priorMode; NetUnlagged.MaxRewindFrames=priorCeiling; }
    }
}
