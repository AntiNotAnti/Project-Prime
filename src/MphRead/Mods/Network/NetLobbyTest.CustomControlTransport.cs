using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    private sealed class ControlUdpProbe
    {
        private readonly Client _client;
        private readonly WeaponDefinition _definition=RiftlineM7.Create();
        private readonly CustomWeaponManifest _manifest;
        private readonly Dictionary<ulong,(PacketType Type,byte[] Bytes)> _tokens=new();
        internal readonly Dictionary<CustomActionIdentity,CustomReceiptMessage> Receipts=new();
        internal readonly Dictionary<CustomActionIdentity,CustomContactMessage> Contacts=new();
        internal readonly Dictionary<CustomActionIdentity,CustomControlDenied> Denials=new();
        internal CustomStateMessage? State;
        internal uint NativeWorld;
        internal int ReceiptPackets,ContactPackets,DenialPackets;
        internal ControlUdpProbe(Client client,ActionResultProbe previous)
        {
            _client=client; _manifest=new(new[] { _definition }); State=previous.State; NativeWorld=previous.SnapshotWorld;
            var original=client.PacketReceived;
            client.PacketReceived=packet =>
            {
                // Keep the existing capability probe but replace the old K
                // single-life result probe. This fixture keys every result by
                // full independent action identity across actual respawns.
                if (packet.Type is PacketType.WorldBootstrap or PacketType.CustomWeaponManifest or PacketType.CustomWeaponReadyConfirmed
                    or PacketType.CustomWeaponReplicaPage or PacketType.CustomWeaponReplicaEvent)
                    original?.Invoke(packet);
                Receive(packet);
            };
        }
        internal static CustomActionIdentity Key(CustomControlRequest request)
            => new(request.Owner,request.Manifest,request.Weapon,request.Sequence);
        private void Token(ulong token,ReceivedPacket packet)
        {
            if (_tokens.TryGetValue(token,out var previous))
                Check(previous.Type==packet.Type && packet.Payload.SequenceEqual(previous.Bytes),
                    "control application result token retains exact packet kind and payload under retries");
            else _tokens.Add(token,(packet.Type,packet.Payload.ToArray()));
        }
        private void Receive(ReceivedPacket packet)
        {
            if (packet.Type is PacketType.Snapshot or PacketType.SnapshotFast && packet.Payload.Length>=SnapshotHeader.Size)
            {
                uint world=SnapshotHeader.Read(packet.Payload).Frame;
                if (unchecked((int)(world-NativeWorld))>0) NativeWorld=world;
            }
            else if (packet.Type==PacketType.CustomWeaponReceipt)
            {
                Check(CustomCombatResults.TryReadReceipt(packet.Payload,_manifest,_definition,out var result),
                    "actual control UDP receipt validates canonical action/source/resource codec");
                ReceiptPackets++; Token(result.Token,packet);
                Receipts[CustomActionIdentity.For(result.Receipt.Action)]=result;
            }
            else if (packet.Type==PacketType.CustomWeaponContact)
            {
                Check(CustomCombatResults.TryReadContact(packet.Payload,_manifest,_definition,out var result),
                    "actual control UDP terminal contact validates full action/victim canonical health codec");
                ContactPackets++; Token(result.Token,packet); Contacts[result.Outcome.Contact.Key.Action]=result;
            }
            else if (packet.Type==PacketType.CustomWeaponControlDenied)
            {
                Check(CustomControlDenied.TryRead(packet.Payload,out var result),
                    "actual typed no-source UDP denial carries original request without canonical ray/action");
                DenialPackets++; Token(result.Token,packet); Denials[Key(result.Request)]=result;
            }
            else if (packet.Type==PacketType.CustomWeaponState)
            {
                Check(CustomCombatResults.TryReadState(packet.Payload,_manifest,_definition,out var state),
                    "actual control UDP projection validates complete source-independent resource state");
                if (State is not { } previous || previous.Bootstrap!=state.Bootstrap || state.Revision>previous.Revision) State=state;
            }
        }
        internal bool HasReceipt(CustomControlRequest request) => Receipts.ContainsKey(Key(request));
        internal bool HasContact(CustomControlRequest request) => Contacts.ContainsKey(Key(request));
        internal bool HasDenial(CustomControlRequest request) => Denials.ContainsKey(Key(request));
        internal CustomReceiptMessage Receipt(CustomControlRequest request) => Receipts[Key(request)];
        internal CustomContactMessage Contact(CustomControlRequest request) => Contacts[Key(request)];
        internal CustomControlDenied Denial(CustomControlRequest request) => Denials[Key(request)];
        internal void Ack(CustomResultAck ack)
        { byte[] bytes=new byte[CustomResultAck.Size]; ack.Write(bytes); _client.Send(PacketType.CustomWeaponResultAck,bytes); }
    }
    private sealed record ControlSourceProbe(uint Missing,uint Expected);
    private sealed record ControlSourceSnapshot(uint World,bool MissingObserved,bool ExpectedObserved,CustomEngineObservation Expected);
    private sealed class ControlSourcePause : IDisposable
    {
        internal readonly ManualResetEventSlim Entered=new(),Release=new(),Exited=new();
        public void Dispose()
        {
            Release.Set();
            if (Exited.Wait(3000)) { Entered.Dispose(); Release.Dispose(); Exited.Dispose(); }
        }
    }
    private sealed class ControlTransportFixture : IDisposable
    {
        internal readonly ActionTransportFixture Engine;
        internal readonly ControlUdpProbe A,B;
        private readonly FieldInfo _sink;
        private readonly SnapshotSink _previous;
        private readonly Scene _scene;
        private ControlSourcePause? _pause;
        private ControlSourceProbe? _probe;
        private ControlSourceSnapshot? _source;
        private uint _sourceFrame;
        internal ControlSourceSnapshot? Source => Volatile.Read(ref _source);
        internal ControlTransportFixture(bool impaired=false)
        {
            Engine=new(impaired:impaired); A=new(Engine.First,Engine.A); B=new(Engine.Second,Engine.B);
            _sourceFrame=Engine.Latest.First.InputFrame;
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var sim=(ServerSim)typeof(DedicatedServer).GetField("_sim",hidden)!.GetValue(Engine.Rig.Server)!;
            _scene=sim.EvidenceScene!;
            _sink=typeof(NetSession).GetField("_snapshotSink",BindingFlags.Static|BindingFlags.NonPublic)!;
            _previous=(SnapshotSink)_sink.GetValue(null)!;
            SnapshotSink observe=payload =>
            {
                _previous(payload);
                if (_scene.CustomCombat is { IsEligible:true } root && Volatile.Read(ref _probe) is { } probe)
                {
                    var owner=Engine.Latest.First.Owner;
                    bool missing=root.TryGetSourceObservation(owner,probe.Missing,out _);
                    bool expected=root.TryGetSourceObservation(owner,probe.Expected,out var sample);
                    Volatile.Write(ref _source,new(NetSession.NetFrame,missing,expected,sample));
                }
                if (Interlocked.Exchange(ref _pause,null) is { } pause)
                {
                    pause.Entered.Set();
                    try
                    { if (!pause.Release.Wait(3000)) throw new InvalidOperationException("Bounded control source test pause timed out."); }
                    finally { pause.Exited.Set(); }
                }
            };
            _sink.SetValue(null,observe);
        }
        internal CustomControlRequest Request(uint sequence,CustomWeaponActionKind kind,byte optic=0)
        {
            var ready=Engine.FirstReady.Ready!.Value; var owner=Engine.Latest.First.Owner;
            return new(ready.Bootstrap,ready.Manifest,owner.Slot,owner.Life,new(10),sequence,kind,optic);
        }
        internal static byte[] Bytes(CustomControlRequest request)
        { var bytes=new byte[CustomControlRequest.Size]; request.Write(bytes); return bytes; }
        internal void Send(CustomControlRequest request) => Engine.First.Send(PacketType.CustomWeaponControl,Bytes(request));
        internal void WaitPending(int count,string label)
            => Engine.Rig.Wait(()=>Engine.Rig.Server.TryGetCustomActionPending(Engine.First.Slot,out int pending) && pending==count,label);
        internal void Reserve(CustomControlRequest request,int count=1)
        { Send(request); WaitPending(count,"actual authenticated control request reserves bounded results before a fresh source exists"); }
        private uint NextSource()
        { _sourceFrame=Math.Max(_sourceFrame+1,Math.Max(Engine.Latest.World+1,Engine.Latest.First.InputFrame+1)); return _sourceFrame; }
        private byte[] Input(uint frame,bool shoot,Vector3? position=null)
        {
            var world=Engine.Latest; var actor=world.First; var target=world.Second; var owner=actor.Owner;
            var aim=(target.Position+Vector3.UnitY*.2f-(actor.Position+Vector3.UnitY*.4f)).Normalized();
            var input=new IntentPacket { MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,SlotGeneration=owner.Generation,
                LifeId=owner.Life,Frame=frame,AckFrame=A.NativeWorld,Position=position ?? actor.Position,Aim=aim,
                Buttons=IntentButtons.InPlayState|(shoot ? IntentButtons.Shoot:0),WeaponSelect=(byte)BeamType.PowerBeam,
                HasState=true,HasFireEvents=true,HasAnalogMove=true,HasContinuousFireTick=true };
            var bytes=new byte[CompactIntentCodec.Size(input)]; CompactIntentCodec.Write(input,bytes); return bytes;
        }
        internal uint Fresh(Vector3? position=null)
        {
            uint frame=NextSource(); var bytes=Input(frame,false,position); Engine.First.Send(PacketType.Intent,bytes);
            var time=Stopwatch.StartNew(); long retry=50;
            Engine.Rig.Wait(()=>
            {
                if (time.ElapsedMilliseconds>=retry) { Engine.First.Send(PacketType.Intent,bytes); retry=time.ElapsedMilliseconds+50; }
                return Engine.Latest.First.InputFrame==frame;
            },"new released native input actually reaches current movement/control consumption",8000);
            return frame;
        }
        internal CustomReceiptMessage Settle(CustomControlRequest request,bool acknowledge=true)
        {
            Engine.Rig.Wait(()=>A.HasReceipt(request),"reliable control receives canonical returned Action from real fresh engine source",8000);
            var result=A.Receipt(request);
            Check(request.Matches(result.Receipt.Action),"returned canonical Action preserves exact request identity/kind/optic without client pose/time claims");
            if (acknowledge) A.Ack(result.Ack);
            return result;
        }
        internal void Equip()
        {
            var equip=Request(1,CustomWeaponActionKind.Equip); Reserve(equip); Fresh();
            Check(Settle(equip).Receipt.Result==WeaponActionResult.Accepted,"reliable Equip binds actual fresh source and settles"); WaitPending(0,"Equip application acknowledgement releases request obligation");
            var optic=Request(2,CustomWeaponActionKind.Optic,1); Reserve(optic); Fresh();
            Check(Settle(optic).Receipt.Result==WeaponActionResult.Accepted,"reliable Optic binds another fresh source and settles"); WaitPending(0,"Optic application acknowledgement releases request obligation");
        }
        internal (uint Down,uint Up) CoalescedTap()
        {
            using var pause=new ControlSourcePause();
            if (Interlocked.CompareExchange(ref _pause,pause,null)!=null) throw new InvalidOperationException("Source pauses must be serialized.");
            Engine.Rig.Wait(()=>pause.Entered.IsSet,"simulation is paused at a bounded actual source/snapshot boundary");
            uint down=NextSource(),up=++_sourceFrame;
            Volatile.Write(ref _probe,new(down,up));
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var transport=(NetTransport)typeof(DedicatedServer).GetField("_transport",hidden)!.GetValue(Engine.Rig.Server)!;
            long before=transport.Wire.Capture().Categories.Single(c=>c.Kind==NetTrafficKind.Intent).RxPackets;
            Engine.First.Send(PacketType.Intent,Input(down,true)); Engine.First.Send(PacketType.Intent,Input(up,false));
            Engine.Rig.Wait(()=>transport.Wire.Capture().Categories.Single(c=>c.Kind==NetTrafficKind.Intent).RxPackets>=before+2,
                "actual UDP worker receives BOTH native tap carriers before another engine consumption pass");
            pause.Release.Set();
            Engine.Rig.Wait(()=>pause.Exited.IsSet && Engine.Latest.First.InputFrame==up && Source is { ExpectedObserved:true } source
                && source.Expected.Input.Frame==up,"next actual engine pass consumes latest released carrier after queued down/up pair");
            Check(Source is { MissingObserved:false,ExpectedObserved:true } observed
                && (observed.Expected.Input.InputBits&(uint)IntentButtons.Shoot)==0,
                "native down carrier was truly coalesced without source geometry while released post-fence carrier was actually observed");
            return (down,up);
        }
        public void Dispose()
        {
            Interlocked.Exchange(ref _pause,null)?.Release.Set();
            _sink.SetValue(null,_previous); Engine.Dispose();
        }
    }
    public static int RunCustomControlTransport(string data)
    {
        string directory=Directory.GetCurrentDirectory(); _checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
            CustomControlUdpLostTap(); CustomControlUdpOrdering(); CustomControlUdpDenied();
            CustomControlUdpRespawn(); CustomControlUdpCapacity(); CustomControlUdpExpiry(); CustomControlUdpLoss();
            Console.WriteLine($"PASS {_checks} reliable custom control UDP/engine checks.");
            Console.WriteLine("SCOPE: actual dedicated UDP control/source/result handlers, fully coalesced native tap, released-input canonical Fire, ordered one-head-per-source processing, typed no-source denial, real native respawn, bounded capacity and160ms RTT/10% client datagram faults. Native scene-load acknowledgements and room/health/respawn setup are explicit fixtures inherited from K. No source certificates, movement replay or rendered input UI are injected or claimed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM CONTROL UDP FAIL "+ex); return 1; }
        finally { NetSession.Stop(); Directory.SetCurrentDirectory(directory); }
    }
    private static void CustomControlUdpLostTap()
    {
        using var f=new ControlTransportFixture(); f.Equip();
        var fire=f.Request(3,CustomWeaponActionKind.Fire,1); f.Reserve(fire);
        var frames=f.CoalescedTap(); var result=f.Settle(fire,false);
        f.Engine.Rig.Wait(()=>f.A.HasContact(fire) && f.Engine.Latest.Second.Health==135,"reliable Fire survives wholly unconsumed native press and removes65 canonical HP");
        Check(result.Receipt.Result==WeaponActionResult.Accepted && result.Receipt.Emission==CustomEmissionDisposition.Spawned
            && result.Receipt.Action.SourceFrame==frames.Up && result.Receipt.After is { Magazine:3 }
            && result.Receipt.Source?.ObservedWorldFrame<result.Receipt.AuthorityFrame,
            "reliable request materializes exact released post-fence source and waits until sampled world is available");
        Check(f.A.Contact(fire).Outcome.Health.BodyRemoved==65 && f.Engine.Latest.Contacts==1 && f.Engine.Latest.Payments==1
            && !f.Engine.Latest.Second.NativeHistory,"lost native tap uses one private canonical payment without native history contamination");
        int receipts=f.A.ReceiptPackets; f.Send(fire);
        ObserveCustomReadiness(f.Engine.Rig,350,"withheld control application ACK and exact request retry traverse real delivery");
        Check(f.A.ReceiptPackets>receipts && f.A.Receipt(fire)==result && f.Engine.Latest.Contacts==1
            && f.Engine.Latest.First.State.Magazine==3,"control retry cannot rebind, refill or emit another shot");
        f.A.Ack(result.Ack); f.A.Ack(f.A.Contact(fire).Ack); f.WaitPending(0,"exact receipt/contact application ACKs release lost-tap request");
        Check(f.Engine.Latest.StepFailures==0,"lost-tap source barrier and canonical effects have no simulation exceptions");
    }
    private static void CustomControlUdpOrdering()
    {
        using var f=new ControlTransportFixture();
        var second=f.Request(2,CustomWeaponActionKind.Optic,1); f.Reserve(second); f.Fresh();
        uint before=f.Engine.Latest.World; f.Engine.WaitWorld(before+3,"out-of-order request is held across real source and combat phases");
        Check(!f.A.HasReceipt(second) && f.Engine.Latest.First.State.Phase==CustomWeaponPhase.Unequipped,
            "requestseq2 cannot select an early source or mutate before missingseq1");
        var first=f.Request(1,CustomWeaponActionKind.Equip); f.Reserve(first,2); uint firstSource=f.Fresh();
        var r1=f.Settle(first); before=f.Engine.Latest.World; f.Engine.WaitWorld(before+3,"held same input cannot activate second control head");
        Check(!f.A.HasReceipt(second) && r1.Receipt.Action.SourceFrame==firstSource,
            "one new source binds exactly one contiguous request head");
        uint secondSource=f.Fresh(); var r2=f.Settle(second);
        Check(r2.Receipt.Result==WeaponActionResult.Accepted && r2.Receipt.Action.SourceFrame==secondSource
            && secondSource>firstSource && r2.Receipt.Source?.ObservedWorldFrame>r1.Receipt.Source?.ObservedWorldFrame,
            "later request waits for genuinely later consumed source and preserves source-time ordering");
        f.WaitPending(0,"both ordered control application acknowledgements retire without gaps");
        Check(f.Engine.Latest.First.State.Magazine==4 && f.Engine.Latest.StepFailures==0,"ordered control setup has no ammo or simulation side effects");
    }
    private static void CustomControlUdpDenied()
    {
        using var f=new ControlTransportFixture(); f.Equip();
        var fire=f.Request(3,CustomWeaponActionKind.Fire,1); f.Reserve(fire); uint deniedSource=f.Fresh(Vector3.Zero);
        f.Engine.Rig.Wait(()=>f.A.HasDenial(fire),"actual rejected first post-fence placement yields typed no-source terminal");
        var denied=f.A.Denial(fire);
        Check(denied.Reason==CustomControlDenialReason.SourceUnavailable && denied.Request==fire
            && !f.A.HasReceipt(fire) && !f.A.HasContact(fire) && f.Engine.Latest.First.InputFrame==deniedSource
            && f.Engine.Latest.First.State.Magazine==4 && f.Engine.Latest.Second.Health==200 && f.Engine.Latest.Contacts==0,
            "first negative source cannot slide to a later pose, invent a ray or debit ammo/HP");
        f.A.Ack(denied.Ack); f.WaitPending(0,"typed no-source application ACK releases its unused contact reservation");
        var equip=f.Request(4,CustomWeaponActionKind.Equip); f.Reserve(equip); f.Fresh(); var result=f.Settle(equip);
        Check(result.Receipt.Result==WeaponActionResult.Accepted && result.Receipt.Action.Sequence==4
            && f.Engine.Latest.First.Owner==fire.Owner,"same-life next request progresses after typed no-source result instead of sequence deadlock");
        f.WaitPending(0,"same-life recovery acknowledgement retires normally");
        Check(f.Engine.Latest.StepFailures==0,"actual negative source and ordered recovery have no engine exception");
    }
    private static void CustomControlUdpRespawn()
    {
        using var f=new ControlTransportFixture(); f.Equip();
        var old=f.Request(3,CustomWeaponActionKind.Fire,1); f.Reserve(old); f.Engine.RespawnFirst();
        f.Engine.Rig.Wait(()=>f.A.HasDenial(old),"real native respawn retires old-life unbound request without a canonical shot");
        var denied=f.A.Denial(old); var next=f.Engine.Latest.First.Owner;
        Check(denied.Reason==CustomControlDenialReason.LifeRetired && denied.Request.Owner==old.Owner
            && next.Generation==old.Owner.Generation && next.Life!=old.Life && f.Engine.Latest.Contacts==0,
            "pending control cannot borrow a new life or wait through respawn to fire");
        f.Fresh(); f.Engine.Rig.Wait(()=>f.A.State?.State.Owner==next,"actual fresh new-life source automatically publishes canonical inventory");
        var equip=f.Request(1,CustomWeaponActionKind.Equip); f.Reserve(equip,2); f.Fresh(); var receipt=f.Settle(equip);
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && receipt.Receipt.Action.Owner==next
            && receipt.Receipt.After is { Magazine:4,Reserve:16 },"new life gets a separate request sequence/mode and fresh inventory without old Fire");
        f.A.Ack(denied.Ack); f.WaitPending(0,"same admitted bootstrap/connection acknowledges exact old-life no-source terminal alongside new-life result");
        Check(f.Engine.Latest.Second.Health==200 && f.Engine.Latest.Contacts==0 && f.Engine.Latest.StepFailures==0,
            "old-life result retirement never causes a delayed shot or engine failure");
    }
    private static void CustomControlUdpCapacity()
    {
        using var f=new ControlTransportFixture(); var first=f.Request(1,CustomWeaponActionKind.Equip);
        for (uint sequence=1;sequence<=16;sequence++)
        {
            var request=sequence==1 ? first:first with { Sequence=sequence,Kind=CustomWeaponActionKind.Optic,Optic=1 };
            f.Reserve(request,(int)sequence); f.Fresh();
            Check(f.Settle(request,false).Receipt.Result==WeaponActionResult.Accepted,"each retained control requires a real fresh source before settlement");
        }
        f.WaitPending(16,"sixteen settled but unacknowledged control results still occupy full bounded capacity");
        var fire=f.Request(17,CustomWeaponActionKind.Fire,1); f.Send(fire); f.Fresh();
        Check(!f.A.HasReceipt(fire) && !f.A.HasContact(fire) && !f.A.HasDenial(fire)
            && f.Engine.Latest.First.State.Magazine==4,"seventeenth control is blocked before binding/debit even when an actual source arrives");
        f.A.Ack(f.A.Receipt(first).Ack); f.WaitPending(15,"one exact application ACK frees one control admission slot");
        f.Reserve(fire,16); f.Fresh(); var receipt=f.Settle(fire,false);
        f.Engine.Rig.Wait(()=>f.A.HasContact(fire) && f.Engine.Latest.Second.Health==135,
            "previously backpressured Fire pays exactly once after capacity and fresh source become available");
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && receipt.Receipt.After is { Magazine:3 },
            "same previously backpressured request can bind only after capacity release and another fresh source");
        Check(f.Engine.Latest.StepFailures==0,"control capacity/fresh-source backpressure has no simulation exception");
    }
    private static void CustomControlUdpExpiry()
    {
        using var f=new ControlTransportFixture(); var request=f.Request(1,CustomWeaponActionKind.Equip); f.Reserve(request);
        f.Engine.Rig.Wait(()=>f.A.HasDenial(request),"no new consumed source produces bounded typed timeout instead of an indefinitely latched control",5000);
        var denied=f.A.Denial(request);
        Check(denied.Reason==CustomControlDenialReason.SourceUnavailable && !f.A.HasReceipt(request)
            && f.Engine.Latest.First.State.Phase==CustomWeaponPhase.Unequipped,"source wait timeout has no guessed action or resource mutation");
        f.Fresh(); uint before=f.Engine.Latest.World; f.Engine.WaitWorld(before+3,"late input arrives after fixed source-wait terminal");
        Check(!f.A.HasReceipt(request) && f.A.Denial(request)==denied && f.Engine.Latest.First.State.Phase==CustomWeaponPhase.Unequipped,
            "late fresh input cannot revive a timed-out control");
        f.A.Ack(denied.Ack); f.WaitPending(0,"typed source-wait timeout still needs explicit application ACK");
        Check(f.Engine.Latest.StepFailures==0,"bounded source wait and late input preserve actual simulation");
    }
    private static void CustomControlUdpLoss()
    {
        using var f=new ControlTransportFixture(impaired:true); long before=f.Engine.DroppedDatagrams(); f.Equip();
        var fire=f.Request(3,CustomWeaponActionKind.Fire,1); f.Reserve(fire); uint source=f.Fresh(); var receipt=f.Settle(fire,false);
        f.Engine.Rig.Wait(()=>f.A.HasContact(fire) && f.Engine.Latest.Second.Health==135,"reliable custom control survives actual160ms RTT/10% datagram loss while all native input stays released",8000);
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && receipt.Receipt.Action.SourceFrame==source
            && receipt.Receipt.After is { Magazine:3 } && f.A.Contact(fire).Outcome.Health.BodyRemoved==65
            && f.Engine.Latest.Contacts==1 && f.Engine.DroppedDatagrams()>before,
            "loss path uses fresh actually consumed released input and one canonical sixty-five HP payment");
        f.A.Ack(receipt.Ack); f.A.Ack(f.A.Contact(fire).Ack); f.WaitPending(0,"lossy control receipt/contact application ACKs eventually release capacity");
        Check(f.Engine.Latest.StepFailures==0,"impaired control source/result transport has no simulation exception");
    }
}
