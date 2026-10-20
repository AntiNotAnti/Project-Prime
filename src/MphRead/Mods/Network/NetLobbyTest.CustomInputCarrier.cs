using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Actual dedicated UDP/engine fixture. Existing K/L actor placement, native
    // load ACKs and health seeds are explicit setup, never source certificates.
    private sealed class InputCarrierFixture : IDisposable
    {
        internal readonly ControlTransportFixture F=new();
        private readonly FieldInfo _sink;
        private readonly SnapshotSink _previous;
        private readonly Scene _scene;
        private uint _frame,_killFrame;
        private int _killed;
        private ControlSourcePause? _pause;
        private ControlSourceProbe? _probe;
        private ControlSourceSnapshot? _source;
        private byte[]? _relayed;
        internal bool Killed => Volatile.Read(ref _killed)!=0;
        internal uint KillWorld;
        internal InputCarrierFixture()
        {
            var sim=(ServerSim)typeof(DedicatedServer).GetField("_sim",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(F.Engine.Rig.Server)!;
            _scene=sim.EvidenceScene!; _frame=F.Engine.Latest.First.InputFrame;
            _sink=typeof(NetSession).GetField("_snapshotSink",BindingFlags.Static|BindingFlags.NonPublic)!;
            _previous=(SnapshotSink)_sink.GetValue(null)!;
            SnapshotSink observer=payload =>
            {
                _previous(payload);
                if (_scene.CustomCombat is { IsEligible:true } root)
                {
                    var owner=F.Engine.Latest.First.Owner;
                    if (Volatile.Read(ref _probe) is { } probe)
                    {
                        bool missing=root.TryGetSourceObservation(owner,probe.Missing,out _);
                        bool expected=root.TryGetSourceObservation(owner,probe.Expected,out var source);
                        Volatile.Write(ref _source,new(NetSession.NetFrame,missing,expected,source));
                    }
                    if (_killFrame!=0 && F.Engine.Latest.First.InputFrame==_killFrame && !Killed
                        && root.TryGetSourceObservation(owner,_killFrame,out var sample) && sample.CanUseWeapon)
                    {
                        Check(F.Engine.Latest.First.State.Magazine==4 && F.Engine.Latest.Contacts==0,
                            "pre-death atomic source is captured before deferred canonical custom emission/payment");
                        KillWorld=NetSession.NetFrame;
                        var player=_scene.Players.Items[owner.Slot];
                        player.TakeDamage(0,DamageFlags.Death|DamageFlags.IgnoreInvuln,null,player);
                        Check(player.Health==0,"actual canonical environment death occurs on owner thread in captured source world");
                        Volatile.Write(ref _killed,1);
                    }
                }
                if (Interlocked.Exchange(ref _pause,null) is { } pause)
                {
                    pause.Entered.Set();
                    try { if (!pause.Release.Wait(3000)) throw new InvalidOperationException("Atomic carrier bounded source pause timed out."); }
                    finally { pause.Exited.Set(); }
                }
            };
            _sink.SetValue(null,observer);
            var receive=F.Engine.Second.PacketReceived;
            F.Engine.Second.PacketReceived=packet =>
            {
                receive?.Invoke(packet);
                if (packet.Type==PacketType.SlotIntent && packet.Payload.Length>1 && packet.Payload[0]==F.Engine.First.Slot)
                    Volatile.Write(ref _relayed,packet.Payload[1..].ToArray());
            };
        }
        internal uint Next() => _frame=Math.Max(_frame+1,Math.Max(F.Engine.Latest.World+1,F.Engine.Latest.First.InputFrame+1));
        internal byte[] Native(uint frame,Vector3? position=null)
        {
            var world=F.Engine.Latest; var owner=world.First.Owner;
            var aim=(world.Second.Position+Vector3.UnitY*.2f-(world.First.Position+Vector3.UnitY*.4f)).Normalized();
            var input=new IntentPacket { MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,SlotGeneration=owner.Generation,LifeId=owner.Life,
                Frame=frame,AckFrame=F.A.NativeWorld,Position=position??world.First.Position,Aim=aim,
                Buttons=IntentButtons.InPlayState,WeaponSelect=(byte)BeamType.PowerBeam,HasState=true,HasFireEvents=true,
                HasAnalogMove=true,HasContinuousFireTick=true };
            var bytes=new byte[CompactIntentCodec.Size(input)]; CompactIntentCodec.Write(input,bytes); return bytes;
        }
        internal static byte[] Wrap(CustomControlRequest request,byte[] native)
        {
            var bytes=new byte[CustomInputCarrier.PrefixSize+native.Length];
            if (!CustomInputCarrier.TryWrite(request,native,bytes,out int written) || written!=bytes.Length)
                throw new InvalidOperationException("Valid atomic UDP test wrapper failed to encode.");
            return bytes;
        }
        internal void Send(CustomControlRequest request,byte[] native)
            => F.Engine.First.Send(PacketType.CustomWeaponInput,Wrap(request,native));
        internal uint Atomic(CustomControlRequest request,Vector3? position=null,bool killBeforeProcessing=false)
        {
            uint frame=Next(); var native=Native(frame,position);
            if (killBeforeProcessing) _killFrame=frame;
            var bytes=Wrap(request,native); F.Engine.First.Send(PacketType.CustomWeaponInput,bytes);
            var timer=Stopwatch.StartNew();long retry=50;
            F.Engine.Rig.Wait(()=>
            {
                if (timer.ElapsedMilliseconds>=retry) { F.Engine.First.Send(PacketType.CustomWeaponInput,bytes);retry=timer.ElapsedMilliseconds+50; }
                return F.Engine.Latest.First.InputFrame==frame;
            },"atomic UDP carrier itself becomes the actual current native input",8000);
            return frame;
        }
        internal CustomReceiptMessage Settle(CustomControlRequest request,bool acknowledge=true)
        { var result=F.Settle(request,acknowledge);if (acknowledge) F.WaitPending(0,"atomic control application ACK retires its result");return result; }
        internal void Equip()
        {
            var equip=F.Request(1,CustomWeaponActionKind.Equip); uint source=Atomic(equip);var r1=Settle(equip);
            Check(r1.Receipt.Result==WeaponActionResult.Accepted && r1.Receipt.Action.SourceFrame==source,
                "atomic Equip requires no extra future carrier after same-carrier native acceptance");
            var optic=F.Request(2,CustomWeaponActionKind.Optic,1);source=Atomic(optic);var r2=Settle(optic);
            Check(r2.Receipt.Result==WeaponActionResult.Accepted && r2.Receipt.Action.SourceFrame==source,
                "atomic Optic preserves same actually-consumed source boundary");
        }
        internal void WaitRelay(byte[] expected)
            => F.Engine.Rig.Wait(()=>Volatile.Read(ref _relayed) is { } bytes && bytes.SequenceEqual(expected),
                "other actual UDP peer receives byte-identical native SlotIntent payload");
        internal uint Coalesced(CustomControlRequest request)
        {
            using var pause=new ControlSourcePause();
            Interlocked.Exchange(ref _pause,pause); F.Engine.Rig.Wait(()=>pause.Entered.IsSet,"atomic coalescing pauses at real owner-thread source boundary");
            uint first=Next(),last=Next();Volatile.Write(ref _probe,new(first,last));
            var transport=(NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(F.Engine.Rig.Server)!;
            long before=transport.Wire.Capture().Categories.Single(x=>x.Kind==NetTrafficKind.Intent).RxPackets;
            Send(request,Native(first));Send(request,Native(last));
            F.Engine.Rig.Wait(()=>transport.Wire.Capture().Categories.Single(x=>x.Kind==NetTrafficKind.Intent).RxPackets>=before+2,
                "both93wrappers reach actual server ingress before next input consumption");
            pause.Release.Set();
            F.Engine.Rig.Wait(()=>pause.Exited.IsSet && F.Engine.Latest.First.InputFrame==last
                && Volatile.Read(ref _source) is { ExpectedObserved:true },"coalesced wrapper retains only latest actually-consumed source");
            Check(Volatile.Read(ref _source) is { MissingObserved:false,ExpectedObserved:true },
                "skipped first carrier has no accepted source geometry and cannot claim its original tap time");
            return last;
        }
        public void Dispose()
        { Interlocked.Exchange(ref _pause,null)?.Release.Set();_sink.SetValue(null,_previous);F.Dispose(); }
    }
    public static int RunCustomInputCarrier(string data)
    {
        string directory=Directory.GetCurrentDirectory();_checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            CustomInputCarrierSameSource();CustomInputCarrierNegative();CustomInputCarrierStale();
            CustomInputCarrierCoalesced();CustomInputCarrierDeath();CustomInputCarrierMalformed();
            Console.WriteLine($"PASS {_checks} atomic custom input carrier UDP/engine checks.");
            Console.WriteLine("SCOPE: actual93UDP ingress/native consumption/custom source/result/canonical effects, exact native relay bytes, stale nonce refusal, first-negative immutability, coalesced latest-source capture and pre-processing canonical environment death. Native load ACKs, placement and HP seeds are explicit fixtures. No historical source/certificate, native fire event or UI input is invented.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM INPUT CARRIER FAIL "+ex);return 1; }
        finally { NetSession.Stop();Directory.SetCurrentDirectory(directory); }
    }
    private static void CustomInputCarrierSameSource()
    {
        using var f=new InputCarrierFixture();f.Equip();var fire=f.F.Request(3,CustomWeaponActionKind.Fire,1);
        uint source=f.Atomic(fire);var receipt=f.Settle(fire,false);
        f.F.Engine.Rig.Wait(()=>f.F.A.HasContact(fire) && f.F.Engine.Latest.Second.Health==135,"same-carrier released native input produces one canonical65HP custom contact");
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && receipt.Receipt.Action.SourceFrame==source
            && receipt.Receipt.After is { Magazine:3 } && receipt.Receipt.Source?.ObservedWorldFrame<receipt.Receipt.AuthorityFrame
            && f.F.Engine.Latest.Contacts==1 && f.F.Engine.Latest.Payments==1 && !f.F.Engine.Latest.Second.NativeHistory,
            "atomic Fire shares exact consumed carrier while remaining independent of native fire events/history/payment");
        var raw=f.Native(f.Next());f.Send(fire,raw);f.WaitRelay(raw);
        Check(CompactIntentCodec.TryRead(raw,out var decoded) && decoded.FireEventCount==0
            && (decoded.Buttons&IntentButtons.Shoot)==0,"custom wrapper adds no native Shoot flag or FireEvent alias to relayed compact bytes");
        f.F.A.Ack(receipt.Ack);f.F.A.Ack(f.F.A.Contact(fire).Ack);f.F.WaitPending(0,"same-source receipt/contact ACKs release reservation");
        Check(f.F.Engine.Latest.StepFailures==0,"atomic same-source transport has no simulation exception");
    }
    private static void CustomInputCarrierNegative()
    {
        using var f=new InputCarrierFixture();f.Equip();var fire=f.F.Request(3,CustomWeaponActionKind.Fire,1);f.Atomic(fire,Vector3.Zero);
        f.F.Engine.Rig.Wait(()=>f.F.A.HasDenial(fire),"first atomic negative placement receives typed no-source terminal");var denial=f.F.A.Denial(fire);
        f.Atomic(fire);f.F.Send(fire);uint before=f.F.Engine.Latest.World;f.F.Engine.WaitWorld(before+4,"same nonce traverses later positive input and reliable91retry");
        Check(f.F.A.Denial(fire)==denial && !f.F.A.HasReceipt(fire) && !f.F.A.HasContact(fire)
            && f.F.Engine.Latest.First.State.Magazine==4 && f.F.Engine.Latest.Second.Health==200 && f.F.Engine.Latest.Contacts==0,
            "first negative actual source cannot promote to a later positive source or debit/emit on repeated nonce");
        f.F.A.Ack(denial.Ack);f.F.WaitPending(0,"negative atomic result releases unused Fire contact obligation only after ACK");
        Check(f.F.Engine.Latest.StepFailures==0,"negative atomic source/repeat has no simulation exception");
    }
    private static void CustomInputCarrierStale()
    {
        using var f=new InputCarrierFixture();uint frame=f.Next();var raw=f.Native(frame);f.F.Engine.First.Send(PacketType.Intent,raw);
        f.F.Engine.Rig.Wait(()=>f.F.Engine.Latest.First.InputFrame==frame,"plain native input is actually consumed before attaching a new control nonce");
        var equip=f.F.Request(1,CustomWeaponActionKind.Equip);f.Send(equip,raw);uint world=f.F.Engine.Latest.World;f.F.Engine.WaitWorld(world+4,"retroactive wrapper reaches actual ingress after source freeze");
        Check(!f.F.A.HasReceipt(equip) && !f.F.A.HasDenial(equip)
            && f.F.Engine.Rig.Server.TryGetCustomActionPending(f.F.Engine.First.Slot,out int count) && count==0
            && f.F.Engine.Latest.First.State.Phase==CustomWeaponPhase.Unequipped,
            "new nonce on already-consumed carrier is refused before reservation or history/source mutation");
        uint fresh=f.Atomic(equip);Check(f.Settle(equip).Receipt.Action.SourceFrame==fresh,"same refused nonce succeeds only on genuinely fresh atomic carrier");
        Check(f.F.Engine.Latest.StepFailures==0,"stale carrier rejection preserves native simulation");
    }
    private static void CustomInputCarrierCoalesced()
    {
        using var f=new InputCarrierFixture();f.Equip();var fire=f.F.Request(3,CustomWeaponActionKind.Fire,1);uint actual=f.Coalesced(fire);
        var receipt=f.Settle(fire,false);f.F.Engine.Rig.Wait(()=>f.F.A.HasContact(fire),"coalesced atomic Fire receives terminal canonical contact");
        Check(receipt.Receipt.Action.SourceFrame==actual && receipt.Receipt.Result==WeaponActionResult.Accepted
            && f.F.Engine.Latest.Second.Health==135 && f.F.Engine.Latest.Contacts==1 && f.F.Engine.Latest.First.State.Magazine==3,
            "repeated nonce freezes first actually-consumed latest carrier without replaying skipped movement/tap pose");
        Check(f.F.Engine.Latest.StepFailures==0,"coalesced atomic controls have no source-phase exception");
    }
    private static void CustomInputCarrierDeath()
    {
        using var f=new InputCarrierFixture();f.Equip();f.F.Engine.SeedHealth(200,200);
        var fire=f.F.Request(3,CustomWeaponActionKind.Fire,1);uint source=f.Atomic(fire,killBeforeProcessing:true);
        var receipt=f.Settle(fire,false);f.F.Engine.Rig.Wait(()=>f.Killed && f.F.A.HasContact(fire) && f.F.Engine.Latest.Second.Health==135,
            "pre-death actual atomic source can settle once after canonical environment death in same captured world");
        var contact=f.F.A.Contact(fire);Check(f.F.Engine.Latest.First.Health==0 && receipt.Receipt.Result==WeaponActionResult.Accepted
            && receipt.Receipt.Action.SourceFrame==source && receipt.Receipt.Source?.ObservedWorldFrame==f.KillWorld
            && receipt.Receipt.Action.AckFrame<=f.KillWorld && receipt.Receipt.After is { Magazine:3 }
            && contact.Outcome.Health.BodyRemoved==65 && f.F.Engine.Latest.Contacts==1 && f.F.Engine.Latest.Payments==1,
            "immutable before-death source/ACK shares launch-world trade rules and canonical payment after owner death");
        f.F.Send(fire);ObserveCustomReadiness(f.F.Engine.Rig,350,"late reliable91request retries frozen atomic result after canonical owner death");
        Check(f.F.A.Receipt(fire)==receipt && f.F.A.Contact(fire)==contact && f.F.Engine.Latest.Second.Health==135
            && f.F.Engine.Latest.Contacts==1 && f.F.Engine.Latest.Payments==1,"late reliable delivery cannot select another source or repay immutable dead-owner action");
        f.F.A.Ack(receipt.Ack);f.F.A.Ack(contact.Ack);f.F.WaitPending(0,"dead-owner frozen results retain exact application acknowledgement obligations");
        Check(f.F.Engine.Latest.StepFailures==0,"canonical death between atomic capture and payment has no simulation exception");
    }
    private static void CustomInputCarrierMalformed()
    {
        using var f=new InputCarrierFixture();var equip=f.F.Request(1,CustomWeaponActionKind.Equip);uint initial=f.F.Engine.Latest.First.InputFrame;
        foreach (int field in new[] { 0,2+33,2+65,2+68 })
        {
            uint frame=f.Next();var bytes=InputCarrierFixture.Wrap(equip,f.Native(frame));bytes[field]^=1;
            f.F.Engine.First.Send(PacketType.CustomWeaponInput,bytes);
        }
        ObserveCustomReadiness(f.F.Engine.Rig,120,"contradictory length/manifest/slot/runtime wrappers traverse actual UDP ingress");
        Check(f.F.Engine.Latest.First.InputFrame==initial && f.F.Engine.Latest.First.State.Phase==CustomWeaponPhase.Unequipped
            && f.F.Engine.Rig.Server.TryGetCustomActionPending(f.F.Engine.First.Slot,out int count) && count==0,
            "known malformed custom envelope rejects whole wrapper before any native current-input mutation");
        Check(f.F.Engine.Latest.StepFailures==0,"whole-wrapper validation refusal has no simulation exception");
    }
}
