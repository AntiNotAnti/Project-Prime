using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Paired native emission/immutable-pose microbenchmarks. This excludes
/// transport and rendering; full server frame tails are measured separately.</summary>
public static class NetServerScratchCheck
{
    public static int Run(string room, string output)
    {
        Headless.Enter();
        int checks=0;
        void Check(bool value,string label) { checks++; if(!value) throw new Exception(label); }
        var rows=new List<object>();
        var fixedRoots=new Dictionary<int,Vector3[]>();
        bool oldEquip=NetAcceptedAttacks.EmissionScratchEnabled,oldPose=NetUnlagged.ClaimPoseCacheEnabled,oldProfile=NetCombatProfile.Enabled;
        try
        {
            for(int repeat=0;repeat<3;repeat++)
            foreach(int players in new[]{2,4,8})
            foreach(bool enabled in repeat%2==0?new[]{false,true}:new[]{true,false})
            {
                var sim=new ServerSim();
                if(!sim.Start(room,GameMode.Battle,players,_=>{},()=>{})) return 1;
                try
                {
                    NetSession.ApplyMatchState(new MatchStatePacket { MatchId=1,AuthorityEpoch=1,RoomKey=room,
                        Mode=(byte)GameMode.Battle,Flags=MatchStatePacket.FlagInProgress },false);
                    var roster=RosterPacket.Create();roster.MatchId=1;roster.AuthorityEpoch=1;roster.Revision=1;roster.Count=(byte)players;
                    for(byte slot=0;slot<players;slot++) { roster.Slots[slot]=slot;roster.Generations[slot]=1;roster.Hunters[slot]=(byte)Hunter.Samus;roster.Names[slot]="Scratch"; }
                    NetSession.ApplyRoster(roster);NetSlotManager.Sync();
                    for(int i=0;i<120;i++)sim.Step();
                    NetAcceptedAttacks.EmissionScratchEnabled=NetUnlagged.ClaimPoseCacheEnabled=enabled;
                    NetFireEvents.Reset(); NetClaimEarlySettlement.Reset();
                    var frameProperty=typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!;
                    var aimMethod=typeof(PlayerEntity).GetMethod("UpdateAimVecs",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    void Frame(uint frame)=>frameProperty.SetValue(null,frame);
                    if(!fixedRoots.TryGetValue(players,out var roots))
                    {
                        roots=new Vector3[players];
                        for(int slot=0;slot<players;slot++) roots[slot]=PlayerEntity.Players[slot].Position;
                        fixedRoots.Add(players,roots);
                    }
                    // Compare fractional reads, misses, same-frame replacement,
                    // ring overwrite and slot invalidation against the uncached path.
                    for(uint f=200;f<216;f++)
                    {
                        Frame(f);
                        for(int slot=0;slot<players;slot++) PlayerEntity.Players[slot].ModPlaceAt(roots[slot]+Vector3.UnitX*((f-200)*.01f));
                        NetUnlagged.Record(f);
                    }
                    for(int slot=0;slot<players;slot++)
                    foreach(double target in new[]{199,200,200.25,207.5,214.75,216,double.NaN})
                    {
                        var player=PlayerEntity.Players[slot]; NetUnlagged.ClaimPoseCacheEnabled=false;
                        bool reference=NetUnlagged.TryHistoricalPose(player,target,out var before);
                        NetUnlagged.ClaimPoseCacheEnabled=true;
                        for(int query=0;query<3;query++)
                            Check(NetUnlagged.TryHistoricalPose(player,target,out var after)==reference && before==after,"cached fractional pose/absence parity");
                    }
                    var victim=PlayerEntity.Players[1];
                    NetUnlagged.ClaimPoseCacheEnabled=true;NetUnlagged.TryHistoricalPose(victim,215,out var first);
                    victim.ModPlaceAt(victim.Position+Vector3.UnitX);NetUnlagged.Record(215);
                    Check(NetUnlagged.TryHistoricalPose(victim,215,out var changed) && changed.Position!=first.Position,"same-frame replacement invalidates read cache");
                    NetUnlagged.Record(215+NetUnlagged.HistoryFrames);
                    Check(!NetUnlagged.TryHistoricalPose(victim,215,out _),"overwritten history cannot remain cached");
                    NetUnlagged.TryHistoricalPose(victim,215+NetUnlagged.HistoryFrames,out _);NetUnlagged.ResetSlot(1);
                    Check(!NetUnlagged.TryHistoricalPose(victim,215+NetUnlagged.HistoryFrames,out _),"slot reset invalidates cached history");
                    NetUnlagged.Record(216+NetUnlagged.HistoryFrames);
                    NetUnlagged.ClaimPoseCacheEnabled=enabled;
                    NetCombatProfile.Enabled=true;
                    long beforeAccepted=NetAcceptedAttacks.Accepted;
                    const int warmup=64,rounds=256;
                    var emissionTimes=new double[rounds];var poseTimes=new double[rounds];
                    long emissionBytes=0,poseBytes=0;
                    long poseHits=NetUnlagged.ClaimPoseCacheHits;
                    for(int round=0;round<warmup+rounds;round++)
                    {
                        uint launch=1000+(uint)round*100;
                        Frame(launch);
                        for(int slot=0;slot<players;slot++)
                        {
                            var player=PlayerEntity.Players[slot];player.Health=9999;player.ModPlaceAt(roots[slot]);
                            foreach(var beam in player.EquipInfo.Beams) { beam.Lifespan=0;beam.Flags|=BeamFlags.Collided; }
                            player.ModArmWeapon(BeamType.PowerBeam);player.ModSetAim(Vector3.UnitZ);aimMethod.Invoke(player,null);
                        }
                        for(uint f=launch;f<=launch+8;f++) { Frame(f);NetUnlagged.Record(f); }
                        for(int slot=0;slot<players;slot++)
                        {
                            var player=PlayerEntity.Players[slot];
                            player.ModCaptureFireSource(false,out var body,out var up,out byte sourceFlags);
                            var history=new FireEventHistory();history[0]=new((uint)round+1,launch,launch,128,FireEventKind.PressFire,
                                (byte)BeamType.PowerBeam,0,0,FireEvent.FlagPose,player.ModMuzzlePos,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ,default,body,up,sourceFlags);
                            NetSession.AcceptSlotIntent(slot,new IntentPacket { MatchId=1,AuthorityEpoch=1,SlotGeneration=NetPlayerLifecycle.Generation(slot),
                                LifeId=NetPlayerLifecycle.Get(slot),Frame=launch+8,AckFrame=launch,Position=player.Position,Aim=Vector3.UnitZ,
                                WeaponSelect=(byte)BeamType.PowerBeam,HasFireEvents=true,FireEventCount=1,FireEvents=history });
                        }
                        if(round==warmup)NetCombatProfile.Reset();
                        long bytes=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
                        NetAcceptedAttacks.EmitPending();
                        double elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        if(round>=warmup) { emissionTimes[round-warmup]=elapsed;emissionBytes+=GC.GetAllocatedBytesForCurrentThread()-bytes; }
                        // Repeated claim proof reads in the same published world.
                        bytes=GC.GetAllocatedBytesForCurrentThread();started=Stopwatch.GetTimestamp();
                        for(int query=0;query<128;query++)
                            for(int slot=0;slot<players;slot++)
                                if(!NetUnlagged.TryHistoricalPose(PlayerEntity.Players[slot],launch+.5,out _)) throw new Exception("benchmark pose unavailable");
                        elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        if(round>=warmup) { poseTimes[round-warmup]=elapsed;poseBytes+=GC.GetAllocatedBytesForCurrentThread()-bytes; }
                    }
                    Check(NetAcceptedAttacks.Accepted-beforeAccepted==(warmup+rounds)*players,"all benchmark shots independently admitted");
                    foreach(var player in PlayerEntity.Players.Take(players)) Check(player.ModAmmo.Ua>=0,"native resource balance valid");
                    Array.Sort(emissionTimes);Array.Sort(poseTimes);
                    rows.Add(new { repeat,players,enabled,rounds,shots=rounds*players,
                        emissionMedianMs=emissionTimes[rounds/2],emissionP99Ms=emissionTimes[(int)(rounds*.99)],emissionBytes,
                        poseMedianMs=poseTimes[rounds/2],poseP99Ms=poseTimes[(int)(rounds*.99)],poseBytes,
                        poseQueries=rounds*128*players,cacheHits=NetUnlagged.ClaimPoseCacheHits-poseHits,profile=NetCombatProfile.Capture() });
                    Console.WriteLine($"scratch paired players={players} enabled={enabled} emissionMedian={emissionTimes[rounds/2]:F6}ms bytes={emissionBytes} poseMedian={poseTimes[rounds/2]:F6}ms");
                }
                finally { sim.Stop(); }
            }
            // Existing native admission, charge/scope, damage and child mechanics
            // must agree with the optimization enabled, not just a timing loop.
            NetAcceptedAttacks.EmissionScratchEnabled=NetUnlagged.ClaimPoseCacheEnabled=true;
            Check(NetworkAuthorityPolicyCheck.RunCombat(room)==0,"optimized authority proof suite");
            Check(NetAcceptedFireContextCheck.Run(room)==0,"optimized original native firing context suite");
            Check(NetCombatCheck.Run(room)==0,"optimized native combat/arbitration suite");
            Check(NetAltHitCheck.Run(room)==0,"optimized native alternate-form historical body suite");
            Check(NetEarlyClaimCheck.Run(room)==0,"optimized early-settlement parity suite");
            File.WriteAllText(output,JsonSerializer.Serialize(new { checks,rows,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                assemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(NetConfig).Assembly.Location))),scope="native emission and repeated immutable-pose reads; no UDP or rendering" },new JsonSerializerOptions { WriteIndented=true }));
            Console.WriteLine($"PASS: {checks} server scratch assertions");return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
        finally { NetAcceptedAttacks.EmissionScratchEnabled=oldEquip;NetUnlagged.ClaimPoseCacheEnabled=oldPose;NetCombatProfile.Enabled=oldProfile; }
    }
}
