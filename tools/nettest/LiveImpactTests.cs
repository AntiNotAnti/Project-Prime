using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class LiveImpactTests
{
    private static int _checks;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Exception(label); }
    internal static LiveCombatImpact Fixture => new(ImpactBaselineTests.Fact with { Flags = ReplayShotFactFlags.Direct },
        new(CombatImpactKind.Direct, 1, true, new(.1f, .2f, .3f)));
    internal static int Run(bool matrix = false, string? output = null)
    {
        try
        {
            _checks = 0; Codec(); Publication(); Bounds();
            if (matrix) Matrix(output);
            Console.WriteLine($"PASS: {_checks} live impact assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); NetCombatFactPublisher.LiveEnabled = false; }
    }
    private static void Codec()
    {
        var f = Fixture; byte[] bytes = new byte[LiveCombatImpactPacket.Size];
        LiveCombatImpactPacket.Write(f, bytes);
        Check(LiveCombatImpactPacket.TryRead(bytes, out var copy) && copy.Fact == f.Fact, "embedded v1 fact unchanged");
        Check((copy.Presentation.BodyOffset-f.Presentation.BodyOffset).Length < MathF.Sqrt(3)/8192, "offset precision");
        Check(LiveCombatImpactPacket.Size + NetHeader.Size <= NetConfig.MaxPacketSize, "separate datagram MTU");
        Check(!NetReliableChannel.IsReliable(PacketType.LiveCombatImpact), "cosmetic retransmits never occupy reliable window");
        Check(NetPacketQueue.Priority(PacketType.LiveCombatImpact) == NetPacketPriority.Realtime, "live lane priority");
        foreach (int length in new[] {0, 1, 61, 73, 75, 1472})
            Check(!LiveCombatImpactPacket.TryRead(new byte[length], out _), "exact payload size");
        foreach (int offset in new[] {0, 1, 62, 67})
        { byte old = bytes[offset]; bytes[offset] = 255; Check(!LiveCombatImpactPacket.TryRead(bytes,out _), "version/kind/validity reject"); bytes[offset]=old; }
        LiveCombatImpactPacket.Write(f with { Presentation = f.Presentation with { BodyOffset = new(float.NaN, 0, 0) } },bytes);
        Check(LiveCombatImpactPacket.TryRead(bytes,out copy) && !copy.Presentation.HasBodyOffset, "invalid offsets become explicitly unknown");
        LiveCombatImpactPacket.Write(f with { Fact = f.Fact with { ImpactPoint = new(float.PositiveInfinity,0,0) } },bytes);
        Check(!LiveCombatImpactPacket.TryRead(bytes,out _), "nonfinite world point rejects");
        foreach (var bad in new[] { f.Fact with { ShotId=0 }, f.Fact with { AuthorityEpoch=0 },
            f.Fact with { VictimGeneration=0 }, f.Fact with { VictimLifeId=0 }, f.Fact with { ShooterSlot=8 },
            f.Fact with { Weapon=255 }, f.Fact with { DamageEventId=0 } })
        { LiveCombatImpactPacket.Write(f with { Fact=bad },bytes); Check(!LiveCombatImpactPacket.TryRead(bytes,out _),"identity validation"); }
        Check(f.Matches(1,2,3,4,5,6) && !f.Matches(1,2,3,5,5,6) && !f.Matches(1,2,3,4,5,7), "both lifecycle fences");
        var seen = new ImpactIdentityWindow(); Check(seen.Add(f.Identity) && !seen.Add(f.Identity), "keyed dedup");
        seen.Reset(); Check(seen.Add(f.Identity), "room reset");
    }
    private static void Publication()
    {
        NetSession.StartServerAuthority(_ => {}, () => {});
        NetSession.ApplyMatchState(new MatchStatePacket {MatchId=1,AuthorityEpoch=2},false);
        NetCombatFactPublisher.LiveEnabled=true;
        int calls=0; NetSession.LiveImpactSink = payload => { Check(LiveCombatImpactPacket.TryRead(payload,out _),"published valid wire"); calls++; };
        var f=Fixture;
        NetCombatFactPublisher.Publish(f.Fact,f.Presentation);
        NetCombatFactPublisher.Publish(f.Fact,f.Presentation);
        Check(calls==1,"recorder with no accepted match cannot suppress live publication; dedup once");
        NetSession.Stop(); NetSession.StartPlayback();
        NetSession.LiveImpactSink = _ => calls++;
        NetCombatFactPublisher.Publish(f.Fact,f.Presentation);
        Check(calls==1,"client/playback cannot author facts"); NetSession.Stop();
    }
    private static void Bounds()
    {
        var box = new NetLiveImpactOutbox(); var f=Fixture;
        for (uint i=0;i<1000;i++) box.Add(f with { Fact=f.Fact with {ShotId=i+1}},0);
        Check(box.Count==64 && box.Dropped==936,"bounded saturation");
        int calls=0;
        for (uint frame=0;frame<20;frame++)
        {
            int before=calls;
            box.Pump(frame,1,2,_ => calls++); box.Pump(frame,1,2,_ => calls++);
            Check(calls-before<=4,"per-frame byte/rate budget even when pumped twice");
        }
        Check(box.Count==0 && box.Dropped>936,"deadline drops pending overload");
        box.Add(f,uint.MaxValue-1); box.Pump(1,1,2,_ => calls++);
        box.Pump(20,1,2,_ => calls++); Check(box.Count==0,"frame wrap expiry");
        box.Add(f,30); box.Pump(30,2,2,_ => throw new Exception("cross-match send")); Check(box.Count==0,"stream reset");
        var queue = new NetPacketQueue(512,128); var ep = new IPEndPoint(IPAddress.Loopback,1);
        for(int i=0;i<1000;i++) queue.TryEnqueue(new(ep,new[]{(byte)PacketType.LiveCombatImpact},1));
        Check(queue.Count==64,"cosmetic ingress reserve");
        for(int i=0;i<384;i++) Check(queue.TryEnqueue(new(ep,new[]{(byte)PacketType.Intent},1)),"gameplay realtime admission survives cosmetics");
        for(int i=0;i<128;i++) Check(queue.TryEnqueue(new(ep,new[]{(byte)PacketType.MatchState},1)),"critical admission survives cosmetics");
        Check(queue.Count==576,"cosmetics have a separate 64-entry reserve; full gameplay capacity remains available");
        Check(queue.TryDequeue(NetPacketPriority.Realtime,out var first) && first.Type==PacketType.Intent,"gameplay drains before cosmetics");
    }
    private static void Matrix(string? output)
    {
        var reports=new List<object>();
        foreach(int population in new[]{2,4,8}) foreach(int rtt in new[]{0,50,150,250,350}) foreach(int jitter in new[]{0,40,80})
        {
            var wire=new NetFaultQueue<(int peer, LiveCombatImpact impact)>(109,rtt/2.0,jitter,.02,.03,.01,8192);
            var boxes=new NetLiveImpactOutbox[population]; var seen=new HashSet<CombatImpactIdentity>[population];
            for(int i=0;i<population;i++){boxes[i]=new();seen[i]=new();}
            int unique=0,sent=0,dups=0; double maxDelay=0;
            for(uint frame=0;frame<660;frame++)
            {
                double now=frame*1000.0/60;
                for(int peer=0;peer<population;peer++)
                {
                    if(frame<600 && frame%6==0) boxes[peer].Add(Fixture with { Fact=Fixture.Fact with {ShotId=frame+1,ResolveTick=frame}},frame);
                    int target=peer;
                    boxes[peer].Pump(frame,1,2,bytes=>{ LiveCombatImpactPacket.TryRead(bytes,out var fact);wire.Enqueue(now,(target,fact));sent++;});
                }
                while(wire.TryDequeue(now,out var p))
                    if(seen[p.peer].Add(p.impact.Identity)) {unique++;maxDelay=Math.Max(maxDelay,now-p.impact.Fact.ResolveTick*1000.0/60);}
                    else dups++;
            }
            Check(unique>=population*98,"two unreliable copies deliver >=98% under seeded impairment");
            reports.Add(new{population,rtt,jitter,loss=.02,reorder=.03,duplicate=.01,expected=100*population,unique,sent,dups,maxDelayMs=maxDelay,
                bytes=sent*(LiveCombatImpactPacket.Size+NetHeader.Size),kind="seeded production outbox+codec simulation; no rendering/UDP"});
        }
        if(output!=null) File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
    }
}
