using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class PickupNetworkTests
{
    private static int _checks;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException(label); _checks++; }
    private static object Get(string name) => typeof(NetPickupNetwork).GetField(name,Static)!.GetValue(null)!;
    private static void Set(object target,string name,object value) => target.GetType().GetField(name,Instance)!.SetValue(target,value);
    private static void Session(string name,object? value) => typeof(NetSession).GetProperty(name)!.SetValue(null,value);
    private static PickupClaim Claim(uint id=1,int slot=0) => new(new(11,7,slot,1,1,id),new(11,7,3,10,1),TimedPowerUp.DoubleDamage,100,Vector3.Zero);
    private static PickupItemFact Item(uint generation=1,bool available=true,uint room=3,uint frame=10)
        => new(new(11,7,room,10,generation),TimedPowerUp.DoubleDamage,frame,available,new Vector3(.5f,1,0));
    private static object Owner(PickupClaim claim,double? created=null)
    {
        Type pending=typeof(NetPickupNetwork).GetNestedType("Pending",BindingFlags.NonPublic)!;
        object value=Activator.CreateInstance(pending,true)!;
        Set(value,"Claim",claim); Set(value,"Created",created ?? NetPickupNetwork.Now);
        ((IDictionary)Get("Owners")).Add(claim.Key,value); return value;
    }
    private static void Client() { NetPickupNetwork.Reset(); Session(nameof(NetSession.Role),NetRole.Client); Session(nameof(NetSession.IsAuthority),false); Session(nameof(NetSession.LocalSlot),0); }
    private static void Authority() { NetPickupNetwork.Reset(); Session(nameof(NetSession.Role),NetRole.Server); Session(nameof(NetSession.IsAuthority),true); Session(nameof(NetSession.LocalSlot),-1); }
    private static void OwnerDelivery()
    {
        Client(); int attempts=0,terminals=0,failures=0;
        NetPickupNetwork.SendClaim=_=>{ attempts++; return false; };
        NetPickupNetwork.Terminal=_=>terminals++;
        NetPickupNetwork.ClientFailure=_=>{ failures++; NetPickupNetwork.Reset(); };
        var request=Claim(); var owner=Owner(request);
        NetPickupNetwork.PumpOwner(); NetPickupNetwork.PumpOwner();
        Check(attempts==2,"failed queue admission remains immediately retryable");
        NetPickupNetwork.SendClaim=_=>{ attempts++; return true; };
        NetPickupNetwork.PumpOwner(); NetPickupNetwork.PumpOwner();
        Check(attempts==3,"successful admission starts retry interval");
        Set(owner,"LastSent",NetPickupNetwork.Now-1); NetPickupNetwork.PumpOwner();
        Check(attempts==4,"retry resumes after last actual send interval");
        var verdict=new PickupVerdict(request,PickupResult.Grant,11,10,1,11,1811);
        Check(!NetPickupNetwork.ReceiveVerdict(verdict with { Claim=request with { Position=Vector3.UnitX } }),"divergent terminal cannot settle owner's original claim");
        Check(NetPickupNetwork.ReceiveVerdict(verdict) && !NetPickupNetwork.ReceiveVerdict(verdict)
            && terminals==1 && NetPickupNetwork.PendingCount==0,"matching terminal settles once and retires owner");
        var trackers=(NetLifecycleTracker[])typeof(NetPlayerLifecycle).GetField("_slots",Static)!.GetValue(null)!;
        var before=trackers[0].Capture();
        var previousLife=Claim(4); Owner(previousLife);
        trackers[0].Restore(new(1,2,NetworkPlayerState.Alive,false));
        int previousAttempts=attempts; NetPickupNetwork.PumpOwner();
        Check(NetPickupNetwork.PendingCount==1 && attempts==previousAttempts+1,
            "respawn preserves previous-life terminal delivery obligation");
        Check(NetPickupNetwork.ReceiveVerdict(new(previousLife,PickupResult.Invalid,20,0,0,0,0))
            && NetPickupNetwork.PendingCount==0 && terminals==2,
            "previous-life terminal is received once after respawn without inventing a receipt exception");
        trackers[0].Restore(before);
        Owner(Claim(2),NetPickupNetwork.Now-16); NetPickupNetwork.PumpOwner();
        Check(failures==1 && NetPickupNetwork.PendingCount==0,"deadline invokes failure/reset without dictionary invalidation");
        Owner(Claim(3)); NetPickupNetwork.SendClaim=_=>{ NetPickupNetwork.Reset(); return true; };
        NetPickupNetwork.PumpOwner(); Check(NetPickupNetwork.PendingCount==0,"send callback can reset owner collection safely");
    }
    private static void Discovery()
    {
        Client(); int discoveryFailures=0; NetPickupNetwork.ClientFailure=_=>discoveryFailures++;
        Check(NetPickupNetwork.ReceiveItem(Item()),"birth discovery accepted");
        Check(!NetPickupNetwork.ReceiveItem(Item()),"identical discovery is a no-op");
        Check(NetPickupNetwork.ReceiveItem(Item(available:false)),"same-frame consumption wins");
        Check(!NetPickupNetwork.ReceiveItem(Item(available:false) with { Position=Vector3.UnitX })
            && discoveryFailures==0,"same-frame moved native removal cannot contradict an existing consumption tombstone");
        Check(!NetPickupNetwork.ReceiveItem(Item(frame:11)) && discoveryFailures==1
            && !((PickupItemFact)((IDictionary)Get("KnownItems"))[Item().Spawn]!).Available, "consumed incarnation cannot resurrect and newer contradiction explicitly fails");
        Check(NetPickupNetwork.ReceiveItem(Item(2,room:4)),"new room discovery advances fence");
        Check(!NetPickupNetwork.ReceiveItem(Item(3,room:3,frame:20)),"late prior-room fact refused");
        Check(!NetPickupNetwork.ReceiveItem(Item(3,room:4) with { Spawn=new(12,7,4,10,3) }),"wrong epoch discovery refused");
        Authority(); var peer=NetPickupNetwork.Join(); NetPickupNetwork.PublishItem(Item());
        Check(NetPickupNetwork.PeerPending(peer)==1 && NetPickupNetwork.Pump(peer,0,(_,_)=>false),"blocked discovery remains queued initially");
        peer.PendingSince=NetPickupNetwork.Now-16;
        Check(!NetPickupNetwork.Pump(peer,0,(_,_)=>false),"delta discovery deadline expires without scene or more births");
        var baseline=NetPickupNetwork.Join(); baseline.Baseline.Enqueue(new byte[PickupItemFact.Size]); baseline.Started=NetPickupNetwork.Now-16;
        Check(!NetPickupNetwork.Pump(baseline,0,(_,_)=>false),"bootstrap deadline bounded independently");
        peer=NetPickupNetwork.Join(); NetPickupNetwork.PublishItem(Item(2)); int deliveries=0;
        Check(NetPickupNetwork.Pump(peer,0,(type,bytes)=>{ deliveries++; return type==PacketType.PickupItem && bytes.Length==PickupItemFact.Size; })
            && deliveries==1 && NetPickupNetwork.PeerPending(peer)==0,"admitted delta advances cursor exactly once");
        NetPickupNetwork.Reset(); Check(!NetPickupNetwork.Current(peer) && !NetPickupNetwork.Pump(peer,0,(_,_)=>true),"reset invalidates old discovery cursor");
    }
    private static void LongItemHistory()
    {
        Client(); int failures=0; NetPickupNetwork.ClientFailure=_=>failures++;
        var live=Item(1,frame:1);
        Check(NetPickupNetwork.ReceiveItem(live), "old live incarnation discovered");
        PickupItemFact pruned=default;
        for(uint generation=2;generation<=3000;generation++)
        {
            uint frame=generation*2;
            var birth=Item(generation,frame:frame);
            Check(NetPickupNetwork.ReceiveItem(birth), "long-room native incarnation birth accepted");
            var taken=birth with { Available=false, Position=new Vector3(1,1,0) };
            Check(NetPickupNetwork.ReceiveItem(taken), "same-frame moving native consumption accepted");
            if(generation==2) pruned=birth;
        }
        var known=(IDictionary)Get("KnownItems");
        Check(known.Count<PickupItemHistory.Capacity && failures==0, "over 2048 lifetime incarnations remain bounded without capacity failure");
        Check(known.Contains(live.Spawn) && ((PickupItemFact)known[live.Spawn]!).Available,
            "very old active item is never pruned");
        Check(!known.Contains(pruned.Spawn) && !NetPickupNetwork.ReceiveItem(pruned)
            && !known.Contains(pruned.Spawn), "old birth cannot resurrect a pruned consumption tombstone");
        var recent=Item(3000,frame:6000);
        Check(!NetPickupNetwork.ReceiveItem(recent), "recent same-frame consumed identity ignores delayed birth");
        var refreshed=live with { AuthorityFrame=6001,Position=new Vector3(2,1,0) };
        Check(NetPickupNetwork.ReceiveItem(refreshed) && (PickupItemFact)known[live.Spawn]! == refreshed,
            "late-join refreshed authority frame admits current old live item");
        Check(NetPickupNetwork.ReceiveItem(refreshed with { Available=false,Position=new Vector3(3,1,0) }),
            "same-frame moved availability transition preserves final consumed position");
    }

    private static void AuthorityDelivery(NetLifecycleTracker[] trackers)
    {
        Authority();
        Check(NetPickupNetwork.Receive(1,Claim(),250,40) && NetPickupNetwork.PendingCount==0,"spoofed sender cannot enter another slot queue");
        Check(NetPickupNetwork.Receive(0,Claim(),250,40) && NetPickupNetwork.Receive(0,Claim() with { Position=Vector3.UnitX },250,40)
            && NetPickupNetwork.PendingCount==1,"pending duplicate key preserves one request");
        for(uint id=2;id<=16;id++) Check(NetPickupNetwork.Receive(0,Claim(id),250,40),"bounded incoming claim admitted");
        Check(!NetPickupNetwork.Receive(0,Claim(17),250,40) && NetPickupNetwork.FailedSlot(0)
            && NetPickupNetwork.PendingCount==16,"seventeenth pending claim explicitly fails slot without growth");
        trackers[0].Restore(new(2,1,NetworkPlayerState.Alive,false));
        Check(!NetPickupNetwork.FailedSlot(0) && NetPickupNetwork.PendingCount==0,"replacement generation discards old failed delivery queue");
        trackers[0].Restore(new(1,1,NetworkPlayerState.Alive,false));
        var peer=NetPickupNetwork.Join(); Check(NetPickupNetwork.Receive(0,Claim(20),250,40),"replacement can admit fresh request");
        var incoming=(Array)Get("Incoming"); var dictionary=(IDictionary)incoming.GetValue(0)!;
        Set(dictionary[Claim(20).Key]!,"Created",NetPickupNetwork.Now-16);
        Check(!NetPickupNetwork.Pump(peer,0,(_,_)=>true),"incoming deadline enforced by network pump without a scene");
        NetPickupNetwork.ForgetSlot(0);
        Check(NetPickupNetwork.PendingCount==0 && !NetPickupNetwork.FailedSlot(0),"disconnect cleanup closes delivery ownership");
    }
    private readonly record struct Wire(bool ToServer,NetHeader Header,uint Event,byte[] Payload);
    private static void FaultDelivery(double loss)
    {
        var endpoint=new IPEndPoint(IPAddress.Loopback,42);
        var client=new NetConnection(endpoint,12); var server=new NetConnection(endpoint,12);
        client.Reliable.UpdateRto(250,40); server.Reliable.UpdateRto(250,40);
        // 105..145ms each direction gives 210..290ms round trip (250 +/-40).
        var wire=new NetFaultQueue<Wire>(loss==.05?8128:8130,105,40,loss,.05,.02);
        int offeredDatagrams=0;
        void Schedule(double time,Wire packet) { offeredDatagrams++; wire.Enqueue(time,packet); }
        var grants=new NetTimedGrants(11,7,3); grants.BeginLife(new(11,7,0,1,1,0),0);
        var claims=new NetPickupClaims(11,7,3,grants); var items=new List<PickupItemFact>(); var requests=new List<PickupClaim>();
        for(uint id=1;id<=16;id++)
        {
            Check(grants.RegisterSpawn((int)id,TimedPowerUp.DoubleDamage,out var spawn),"fault fixture registers independent native item");
            var claim=Claim(id) with { Spawn=spawn }; requests.Add(claim);
            items.Add(new(spawn,TimedPowerUp.DoubleDamage,10,true,new Vector3(.5f,1,0)));
            byte[] bytes=new byte[PickupClaim.Size]; claim.Write(bytes);
            Check(client.Reliable.TryQueue(PacketType.PickupClaim,bytes,0,out _),"real reliable pickup payload admitted");
        }
        Check(claims.Observe(new(new(11,7,0,1,1,0),100,10,Vector3.Zero,new Vector3(0,.5f,0),.5f,0,2,false,true,true),items.ToArray()),"historical contact snapshot supports fault workload");
        int applications=0; var settled=new Dictionary<PickupClaimKey,PickupVerdict>();
        TimedGrantResult Commit(in PickupClaim claim,uint now,out TimedGrantRecord? grant)
        { applications++; return grants.TryGrant(claim.Spawn,claim.Key.LifeKey,now,100,out grant); }
        double nextKeepalive=0;
        for(double now=0;now<=14000;now+=1000.0/60)
        {
            // Continuous connection traffic makes the declared rare faults a
            // measured workload, including after the small claim burst drains.
            if(now>=nextKeepalive)
            {
                Schedule(now,new(true,client.Send(0,now,NetHeaderFlags.AckOnly),0,Array.Empty<byte>()));
                Schedule(now,new(false,server.Send(0,now,NetHeaderFlags.AckOnly),0,Array.Empty<byte>()));
                nextKeepalive=now+100;
            }
            while(client.Reliable.TrySend(now,out var tx)) Schedule(now,new(true,client.Send(tx.Type,now,NetHeaderFlags.Reliable,tx.EventId),tx.EventId,tx.Payload.ToArray()));
            while(server.Reliable.TrySend(now,out var serverTx)) Schedule(now,new(false,server.Send(serverTx.Type,now,NetHeaderFlags.Reliable,serverTx.EventId),serverTx.EventId,serverTx.Payload.ToArray()));
            while(wire.TryDequeue(now,out var packet))
            {
                var receiver=packet.ToServer?server:client; var result=receiver.Receive(packet.Header,now);
                if((packet.Header.Flags & NetHeaderFlags.Reliable)==0) continue;
                if(result is SequenceResult.New or SequenceResult.Reordered && receiver.Reliable.Receive(packet.Event))
                {
                    if(packet.ToServer)
                    {
                        Check(PickupClaim.TryRead(packet.Payload,out var claim),"claim survives real reliable fault delivery codec");
                        var verdict=claims.Judge(claim,0,claim.Key.LifeKey,true,10+(uint)(now* .06),250,40,Commit);
                        byte[] bytes=new byte[PickupVerdict.Size]; verdict.Write(bytes);
                        Check(server.Reliable.TryQueue(PacketType.PickupVerdict,bytes,now,out _),"terminal response reliably queued");
                    }
                    else
                    {
                        Check(PickupVerdict.TryRead(packet.Payload,out var verdict) && !settled.ContainsKey(verdict.Claim.Key),"terminal payload settles exactly once");
                        settled.Add(verdict.Claim.Key,verdict);
                    }
                }
                Schedule(now,new(!packet.ToServer,receiver.Send(0,now,NetHeaderFlags.AckOnly),0,Array.Empty<byte>()));
            }
        }
        Check(settled.Count==16 && requests.All(r=>settled.ContainsKey(r.Key)),"all offered claim IDs reach finite terminal under loss/reorder/duplication");
        Check(settled.Values.All(v=>v.Result is PickupResult.Grant or PickupResult.TooOld) && applications>0
            && applications==settled.Values.Count(v=>v.Result==PickupResult.Grant) && grants.HistoryCount==applications,
            "honest timely contacts grant once; late arrivals explicitly refuse without consumption");
        Check(client.Reliable.Capture(14000).Pending==0 && server.Reliable.Capture(14000).Pending==0
            && !client.Reliable.Failed && !server.Reliable.Failed,"both real reliable channels drain within finite lifetime");
        Check(wire.Dropped>0 && wire.Duplicated>0 && wire.Reordered>0,$"fault workload actually exercises loss duplication and reordering: loss={loss}, dropped={wire.Dropped}, duplicated={wire.Duplicated}, reordered={wire.Reordered}");
        Console.WriteLine($"PICKUP FAULT loss={loss} offeredDatagrams={offeredDatagrams} terminals={settled.Count} grants={applications} dropped={wire.Dropped} duplicated={wire.Duplicated} reordered={wire.Reordered}");
    }
    public static int Run()
    {
        var role=NetSession.Role; var authority=NetSession.IsAuthority; var slot=NetSession.LocalSlot; var match=NetSession.ServerMatch;
        var send=NetPickupNetwork.SendClaim; var terminal=NetPickupNetwork.Terminal; var failure=NetPickupNetwork.ClientFailure;
        var trackers=(NetLifecycleTracker[])typeof(NetPlayerLifecycle).GetField("_slots",Static)!.GetValue(null)!;
        var lives=trackers.Select(t=>t.Capture()).ToArray();
        try
        {
            _checks=0; Session(nameof(NetSession.ServerMatch),new MatchStatePacket { MatchId=7,AuthorityEpoch=11 });
            for(int i=0;i<8;i++) trackers[i].Restore(new(1,1,NetworkPlayerState.Alive,false));
            OwnerDelivery(); Discovery(); LongItemHistory(); AuthorityDelivery(trackers); FaultDelivery(.05); FaultDelivery(.10);
            Console.WriteLine($"PASS: {_checks} pickup network deadline/retry/discovery and real reliable fault assertions"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetPickupNetwork.Reset(); NetPickupNetwork.SendClaim=send; NetPickupNetwork.Terminal=terminal; NetPickupNetwork.ClientFailure=failure;
            for(int i=0;i<8;i++) trackers[i].Restore(lives[i]);
            Session(nameof(NetSession.Role),role); Session(nameof(NetSession.IsAuthority),authority); Session(nameof(NetSession.LocalSlot),slot); Session(nameof(NetSession.ServerMatch),match);
        }
    }
}
