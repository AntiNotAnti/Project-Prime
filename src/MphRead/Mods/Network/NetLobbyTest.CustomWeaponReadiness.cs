using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Real UDP and production handlers. Control-plane cases supply explicit
    // synthetic native bootstraps; engine cases acknowledge actual server native
    // bootstraps manually. These probes do not construct a remote engine scene.
    private sealed class CustomReadinessProbe : IDisposable
    {
        private readonly Client _client;
        private readonly WeaponDefinition _definition;
        private readonly WeaponRegistry _registry;
        private CustomManifestAssembler? _assembler;
        internal WorldBootstrapIdentity? NativeBootstrap;
        internal CustomWeaponReady? Ready;
        internal int Pages, Confirmations, DiscardedConfirmations;
        internal bool DiscardConfirmation;
        internal CustomReplicaConsumer? Replica;
        internal CustomReadinessWire? ReplicaWire;
        internal int ReplicaPages,ReplicaEvents,ReplicaAcks,WithheldReplicaAcks;
        internal bool DiscardReplicaBaselineAcks,DiscardReplicaEventAcks;
        internal byte[]? LastReplicaBaselineAck,LastReplicaEventAck;
        internal CustomPickupReplicaReceiver? Pickups;
        private WorldBootstrapIdentity? _pickupBootstrap;
        internal int PickupPackets,PickupAcks,WithheldPickupAcks;
        internal bool DiscardPickupAcks;
        internal byte[]? LastPickupAck;
        internal CustomReadinessProbe(Client client,WeaponDefinition? definition=null)
        {
            _client = client; _definition=definition??RiftlineM7.Create(); _registry = new(true,new[] { _definition });
            var previous=client.PacketReceived;client.PacketReceived=packet=>{previous?.Invoke(packet);Receive(packet);};
        }
        private void Receive(ReceivedPacket packet)
        {
            if (packet.Type == PacketType.WorldBootstrap && WorldBootstrapIdentity.TryRead(packet.Payload,out var native))
                NativeBootstrap = native;
            if (packet.Type == PacketType.CustomWeaponManifest)
            {
                Check(CustomManifestPage.TryRead(packet.Payload,out var page) && page != null,
                    "UDP manifest carries bounded valid custom page codec");
                var manifest = new CustomWeaponManifest(new[] { _definition });
                Check(page!.Bootstrap == NativeBootstrap && page.Manifest == manifest.Digest
                    && page.TotalRows == 1 && page.Rows.Count == 1 && page.Rows[0] == manifest.Rows[0],
                    "UDP manifest binds exact known native bootstrap and independently built M7 mechanics");
                _assembler ??= new(page.Bootstrap,page.Manifest);
                var result = _assembler.Receive(page);
                Check(result is CustomManifestReceipt.Complete or CustomManifestReceipt.Duplicate
                    && _assembler.CanResolve(_registry),"UDP manifest resolves complete exact detached client registry");
                Ready = new(page.Bootstrap,page.Manifest,(byte)_client.Slot,WeaponMechanics.SchemaVersion,
                    WeaponMechanics.BehaviorVersion,CustomWeaponReady.PrecisionCapability);
                Pages++;
            }
            if (packet.Type is PacketType.CustomWeaponReplicaPage or PacketType.CustomWeaponReplicaEvent)
            {
                Check(Ready is { } && NativeBootstrap.HasValue
                    && packet.Sender.Equals(_client.Server)
                    && packet.ConnectionId==_client.Transport.ConnectionStats(_client.Server)!.Value.ConnectionId,
                    "public custom facts arrive from exact admitted authority endpoint/connection after manifest resolution");
                ReplicaWire ??= new(Ready!.Value.Bootstrap,(byte)_client.Slot,_definition);
                Replica ??= new(ReplicaWire,NetSession.Clock);
                Check(Replica.Receive(packet.Type,packet.Payload,NetSession.Clock,out var ack),
                    "actual UDP public baseline/event is durably consumed by production bounded presentation consumer");
                if (packet.Type==PacketType.CustomWeaponReplicaPage) ReplicaPages++; else ReplicaEvents++;
                if (ack.Id!=0)
                {
                    byte[] bytes=new byte[CustomReplicaAck.Size];
                    Check(CustomReplicaCodec.TryWriteAck(ReplicaWire,ack,bytes),"durable public fact produces exact recipient digest acknowledgement");
                    if(ack.Baseline) LastReplicaBaselineAck=bytes; else LastReplicaEventAck=bytes;
                    if (ack.Baseline ? DiscardReplicaBaselineAcks:DiscardReplicaEventAcks) WithheldReplicaAcks++;
                    else { _client.Send(PacketType.CustomWeaponReplicaAck,bytes);ReplicaAcks++; }
                }
            }
            if(packet.Type==PacketType.CustomWeaponPickupState)
            {
                Check(Ready.HasValue&&_client.State is {Match.RiftlineM7Available:true}
                    &&packet.Sender.Equals(_client.Server)
                    &&packet.ConnectionId==_client.Transport.ConnectionStats(_client.Server)!.Value.ConnectionId,
                    "public pickup fact comes from exact admitted authority and frozen public match");
                var ready=Ready!.Value;var match=_client.State!.Value.Match;
                if(Pickups==null||_pickupBootstrap!=ready.Bootstrap)
                {
                    var manifest=new CustomWeaponManifest(new[]{_definition});
                    var layout=new CustomPickupLayout(match.MapIdentity.Content(match.RoomKey),manifest,_registry,
                        StockRiftlinePickups.ForRoom(match.RoomKey));
                    Pickups=new(ready.Bootstrap,layout);_pickupBootstrap=ready.Bootstrap;
                }
                PickupPackets++;
                if(!Pickups.Receive(packet.Payload,out var ack))
                {Check(!Pickups.Closed,"stale pickup fact cannot contradict retained production receiver state");return;}
                var bytes=new byte[CustomPickupReplicaCodec.AckSize];CustomPickupReplicaCodec.Write(ack,bytes);
                Check(CustomPickupReplicaCodec.TryReadAck(bytes,out var exact)&&exact==ack,
                    "durably received pickup fact produces exact recipient/world/item/serial/digest acknowledgement");
                LastPickupAck=bytes;
                if(DiscardPickupAcks)WithheldPickupAcks++;
                else{_client.Send(PacketType.CustomWeaponPickupAck,bytes);PickupAcks++;}
            }
            if (packet.Type == PacketType.CustomWeaponReadyConfirmed)
            {
                Check(CustomWeaponReady.TryRead(packet.Payload,out var ready) && Ready == ready,
                    "UDP authority confirmation echoes exact locally resolved readiness");
                if (DiscardConfirmation) DiscardedConfirmations++; else Confirmations++;
            }
        }
        internal byte[] Bytes(CustomWeaponReady? replacement = null)
        {
            byte[] bytes = new byte[CustomWeaponReady.Size];
            (replacement ?? Ready ?? throw new InvalidOperationException("Manifest has not completed.")).Write(bytes);
            return bytes;
        }
        internal void Send(CustomWeaponReady? replacement = null)
            => _client.Send(PacketType.CustomWeaponReady,Bytes(replacement));
        public void Dispose() { _client.PacketReceived = null; _registry.Dispose(); }
    }
    private static void StartCustomReadinessMatch(Rig rig,params Client[] clients)
    {
        rig.ReadyAll();
        rig.Expect(clients[0],clients[0].Command(LobbyCommandType.StartMatch),LobbyResultCode.Ok);
        foreach (var client in clients) client.Loaded();
        rig.Wait(()=>clients.All(c=>c.State?.Phase == SessionPhase.InMatch),
            "synthetic native world acknowledgements release real UDP control-plane match");
    }
    private static void ObserveCustomReadiness(Rig rig,int milliseconds,string label)
    {
        var clock = Stopwatch.StartNew();
        rig.Wait(()=>clock.ElapsedMilliseconds >= milliseconds,label,Math.Max(2000,milliseconds+1000));
    }
    private static void CustomReadinessOptOut()
    {
        using var rig = new Rig(); var client = rig.Add(9100);
        using var probe = new CustomReadinessProbe(client);
        StartCustomReadinessMatch(rig,client);
        ObserveCustomReadiness(rig,300,"default-off custom readiness remains quiet after native barrier");
        Check(probe.Pages == 0 && !rig.Server.TryGetCustomWeaponReady(client.Slot,out _),
            "ordinary UDP server neither publishes custom manifest nor owns custom readiness");
    }
    private static void CustomReadinessUdpOwnership()
    {
        using var rig = new Rig(maxPlayers:2,customReadiness:true);
        var first = rig.Add(9110); var second = rig.Add(9111);
        using var a = new CustomReadinessProbe(first); using var b = new CustomReadinessProbe(second);
        StartCustomReadinessMatch(rig,first,second);
        rig.Wait(()=>a.Ready.HasValue && b.Ready.HasValue,"two real UDP peers receive independent complete custom manifests");
        Check(!rig.Server.TryGetCustomWeaponReady(first.Slot,out _) && !rig.Server.TryGetCustomWeaponReady(second.Slot,out _),
            "publishing manifests alone grants no custom readiness");
        // The sender is A's actual admitted connection; the payload names B's
        // slot/bootstrap. It must not ready B or poison A's independent offer.
        first.Send(PacketType.CustomWeaponReady,b.Bytes());
        ObserveCustomReadiness(rig,100,"cross-slot readiness traverses real server dispatch");
        Check(!rig.Server.TryGetCustomWeaponReady(first.Slot,out _) && !rig.Server.TryGetCustomWeaponReady(second.Slot,out _),
            "authenticated peer cannot ready another slot or bootstrap");
        var transport = (NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(rig.Server)!;
        ulong connection = second.Transport.ConnectionStats(second.Server)!.Value.ConnectionId;
        long invalid = transport.Telemetry.Capture().Invalid;
        using (var stranger = new UdpClient(0))
        {
            byte[] payload = b.Bytes(); byte[] wire = new byte[NetHeader.Size+4+payload.Length];
            new NetHeader(PacketType.CustomWeaponReady,NetHeaderFlags.Reliable,connection,0x70000000,0,0).Write(wire);
            BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(NetHeader.Size),1);
            payload.CopyTo(wire,NetHeader.Size+4);
            for (int i=0; i<16; i++) stranger.Send(wire,second.Server);
            rig.Wait(()=>transport.Telemetry.Capture().Invalid > invalid,
                "foreign endpoint carrying copied connection identity is rejected by actual UDP receive worker");
        }
        Check(!rig.Server.TryGetCustomWeaponReady(second.Slot,out _)
            && Enumerable.Range(2,6).All(slot=>!rig.Server.TryGetCustomWeaponReady(slot,out _)),
            "foreign readiness flood cannot allocate additional slot readiness or authorize known peer");
        a.DiscardConfirmation = true; a.Send();
        rig.Wait(()=>rig.Server.TryGetCustomWeaponReady(first.Slot,out _) && a.DiscardedConfirmations > 0,
            "server opens exact readiness while fixture deliberately discards first application confirmation");
        Check(rig.Server.TryGetCustomWeaponReady(first.Slot,out var original),"first peer publishes immutable readiness evidence");
        Check(a.Confirmations == 0 && original.Bootstrap == a.Ready!.Value.Bootstrap && original.Manifest == a.Ready.Value.Manifest
            && original.Slot == first.Slot && original.ConnectionId == first.Transport.ConnectionStats(first.Server)!.Value.ConnectionId,
            "server evidence owns native bootstrap manifest slot and actual endpoint incarnation");
        a.DiscardConfirmation = false; a.Send(); a.Send();
        rig.Wait(()=>a.Confirmations > 0,"duplicate UDP ready recovers withheld authority confirmation");
        Check(rig.Server.TryGetCustomWeaponReady(first.Slot,out var repeated) && repeated.Equals(original),
            "duplicate readiness preserves original opening world and immutable evidence");
        // Explicit native lifecycle packet fixture, with no engine or death
        // simulation claim. Capability belongs to occupant/bootstrap, not life.
        NetPlayerLifecycle.SetOccupant(first.Slot,original.Bootstrap.SlotGeneration);
        var life = new PlayerState { SlotIndex = (byte)first.Slot,SlotGeneration = original.Bootstrap.SlotGeneration,
            LifeId = 1,Health = 100,Flags = PlayerState.FlagActive|PlayerState.FlagSpawned,Facing = Vector3.UnitZ };
        Check(NetPlayerLifecycle.AcceptState(life,10),"synthetic native life-one state passes real lifecycle validator");
        life.Health = 0;
        Check(NetPlayerLifecycle.AcceptState(life,11),"synthetic native death state passes real lifecycle validator");
        life.LifeId = 2; life.Health = 100;
        Check(NetPlayerLifecycle.AcceptState(life,12) && NetPlayerLifecycle.Get(first.Slot) == 2
            && rig.Server.TryGetCustomWeaponReady(first.Slot,out var afterLife) && afterLife.Equals(original),
            "native lifecycle transition preserves occupant readiness without issuing any custom action");
        b.Send(); rig.Wait(()=>b.Confirmations > 0 && rig.Server.TryGetCustomWeaponReady(second.Slot,out _),
            "second independently admitted UDP peer confirms exact readiness");
        Check(rig.Server.TryGetCustomWeaponReady(second.Slot,out var departed),"second peer evidence exists before disconnect");
        int oldSlot = second.Slot; var oldReady = b.Ready!.Value;
        second.Send(PacketType.Bye,Array.Empty<byte>());
        rig.Wait(()=>!rig.Server.TryGetCustomWeaponReady(oldSlot,out _),"actual UDP disconnect retires custom readiness");
        rig.Clients.Remove(second); second.Dispose();
        var replacement = rig.Add(9112); using var next = new CustomReadinessProbe(replacement);
        Check(replacement.Slot == oldSlot,"replacement UDP occupant reuses departed physical slot");
        replacement.Loaded();
        rig.Wait(()=>next.Ready.HasValue,"replacement occupant receives fresh exact manifest bootstrap");
        Check(next.Ready!.Value.Bootstrap.SlotGeneration != oldReady.Bootstrap.SlotGeneration,
            "replacement native bootstrap carries advanced occupant generation");
        next.Send(oldReady);
        ObserveCustomReadiness(rig,100,"stale readiness crosses fresh admitted replacement connection");
        Check(!rig.Server.TryGetCustomWeaponReady(oldSlot,out _),"old bootstrap cannot authorize replacement occupant");
        next.Send(); rig.Wait(()=>next.Confirmations > 0 && rig.Server.TryGetCustomWeaponReady(oldSlot,out _),
            "replacement confirms only its own exact current readiness");
        Check(rig.Server.TryGetCustomWeaponReady(oldSlot,out var fresh) && fresh.ConnectionId != departed.ConnectionId,
            "replacement readiness binds fresh transport incarnation");
        // A contradictory valid-shape digest on the SAME bootstrap poisons this
        // admission. A later repeat cannot reopen it or borrow another slot.
        byte[] contradiction = a.Bytes(); contradiction[33] ^= 1;
        first.Send(PacketType.CustomWeaponReady,contradiction);
        rig.Wait(()=>!rig.Server.TryGetCustomWeaponReady(first.Slot,out _),"same-bootstrap contradictory readiness permanently closes admission");
        a.Send(); ObserveCustomReadiness(rig,100,"original readiness retries after contradictory same-bootstrap acknowledgement");
        Check(!rig.Server.TryGetCustomWeaponReady(first.Slot,out _) && rig.Server.TryGetCustomWeaponReady(oldSlot,out _),
            "poisoned ready cannot reopen while independent replacement remains ready");
    }
    private static void CustomReadinessRuntimeClient()
    {
        NetSession.Stop();
        bool previous = NetSession.DeveloperCustomWeaponReadiness;
        NetSession.DeveloperCustomWeaponReadiness = true;
        try
        {
            using var rig = new Rig(customReadiness:true);
            Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"CustomReadinessRuntime",Hunter.Samus),
                "actual NetSession client connects to custom readiness UDP server");
            int slot = NetSession.LocalSlot;
            void PumpUntil(Func<bool> condition,string label)
                => rig.Wait(()=> { NetSession.Pump(); return condition(); },label,6000);
            PumpUntil(()=>NetSession.LocalIsLobbyOwner && GameState.Nicknames[slot] == "CustomReadinessRuntime"
                && NetSession.LobbyRoster().SessionRevision == NetSession.SessionRevision,
                "actual runtime client converges admitted lobby identity");
            Check(!NetSession.CustomWeaponReadinessConfirmed && !rig.Server.TryGetCustomWeaponReady(slot,out _),
                "actual runtime client cannot confirm capability in lobby before native bootstrap");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true),"actual runtime client sends native lobby ready");
            PumpUntil(()=>!NetSession.LobbyCommandPending && NetSession.SlotLobbyReady[slot]
                && NetSession.LobbyRoster().SessionRevision == NetSession.SessionRevision,
                "actual runtime lobby ready is acknowledged before start");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.StartMatch),"actual runtime client starts synthetic control-plane match");
            PumpUntil(()=>NetSession.IsStarting && !NetSession.LobbyCommandPending,
                "actual runtime client receives native load barrier");
            Check(!NetSession.WorldAdmissionConfirmed && !NetSession.CustomWeaponReadinessConfirmed,
                "custom runtime confirmation remains closed before synthetic native scene acknowledgement");
            // Rig.SeedAuthorityFixture explicitly supplies the empty baseline and
            // stands in for successful scene application. The sockets, native
            // WorldReady confirmation and custom runtime handlers remain real.
            NetSession.MarkMatchLoaded();
            PumpUntil(()=>NetSession.WorldAdmissionConfirmed && NetSession.CustomWeaponReadinessConfirmed
                && rig.Server.TryGetCustomWeaponReady(slot,out _),
                "actual NetSession manifest/ready/confirmation pumps complete over real UDP");
            Check(NetSession.ConfirmedWorldBootstrap is { } bootstrap
                && rig.Server.TryGetCustomWeaponReady(slot,out var evidence)
                && evidence.Bootstrap == bootstrap && evidence.Slot == slot,
                "runtime capability matches exact authority native bootstrap and local slot");
            var baseline = NetSession.ConfirmedWorldBootstrap!.Value;
            var clientTransport = (NetTransport)typeof(NetSession).GetField("_transport",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var host = new IPEndPoint(IPAddress.Loopback,rig.Server.BoundPort);
            Check(rig.Server.TryGetCustomWeaponReady(slot,out var original)
                && original.ConnectionId == clientTransport.ConnectionStats(host)!.Value.ConnectionId,
                "runtime capability binds actual host transport incarnation");
            // Give a FOREIGN endpoint its own valid transport incarnation using
            // the normal Hello/Welcome machinery. This tests the application
            // host filter after transport authentication, not only unknown-UDP
            // rejection. It does not change NetSession's selected host.
            using var foreign = new NetTransport(0);
            var foreignAddress = new IPEndPoint(IPAddress.Loopback,foreign.LocalPort);
            var clientAddress = new IPEndPoint(IPAddress.Loopback,NetSession.ConnectionPort);
            byte[] hello = new byte[6]; hello[0] = (byte)NetConfig.ProtocolVersion;
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2),NetSession.ClientId);
            clientTransport.Send(foreignAddress,PacketType.Hello,hello);
            byte[] welcome = new byte[17]; welcome[0] = (byte)slot;
            BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1),NetSession.ClientId);
            foreign.Send(clientAddress,PacketType.Welcome,welcome);
            PumpUntil(()=>clientTransport.ConnectionStats(foreignAddress).HasValue,
                "foreign manual authority establishes separate genuine transport connection to runtime client");
            byte[] foreignManifest = new CustomReadinessWire(baseline,(byte)slot).ManifestBytes.ToArray();
            foreignManifest[^1] ^= 1; // Would invalidate an accepted current-host manifest.
            foreign.Send(clientAddress,PacketType.CustomWeaponManifest,foreignManifest);
            PumpUntil(()=>foreign.ReliableStats(clientAddress) is { Pending:0 },
                "foreign admitted endpoint custom packet reaches and is ACKed by actual runtime transport");
            Check(NetSession.CustomWeaponReadinessConfirmed && NetSession.ConfirmedWorldBootstrap == baseline
                && NetSession.LocalSlot == slot && rig.Server.TryGetCustomWeaponReady(slot,out var unchanged)
                && unchanged.Equals(original),
                "runtime host-endpoint filter ignores authenticated foreign manifest without invalidating readiness");
            NetSession.Stop();
            Check(!NetSession.CustomWeaponReadinessConfirmed,"stopping actual runtime client clears confirmed custom capability");
        }
        finally { NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness = previous; }
    }
    public static int RunCustomWeaponReadiness()
    {
        _checks = 0;
        try
        {
            CustomReadinessOptOut(); CustomReadinessUdpOwnership(); CustomReadinessRuntimeClient();
            Console.WriteLine($"PASS {_checks} custom readiness real UDP checks.");
            Console.WriteLine("SCOPE: actual DedicatedServer/NetTransport and NetSession client readiness pumps, endpoint-incarnation/host filtering, manifest/ready/confirmation, retries, disconnect/replacement and explicit native lifecycle packet fixtures. Native scene/bootstrap acknowledgements are synthetic; no engine scene, authenticated custom actions, source admission, resource refill, emission or combat is exercised.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM READINESS UDP FAIL " + ex); return 1; }
        finally { NetSession.Stop(); NetPlayerLifecycle.Reset(); }
    }
}
