using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    private sealed record ActionActorSnapshot(WeaponOwnerContext Owner,Vector3 Position,float HeadHeight,
        int Health,ushort DamageId,bool NativeHistory,bool HasState,WeaponStateSnapshot State,
        uint InputFrame,bool HasSource,CustomEngineObservation Source,int Kills,int Deaths);
    private sealed record ActionWorldSnapshot(uint World,ActionActorSnapshot First,ActionActorSnapshot Second,
        ulong Contacts,ulong Payments,long StepFailures,int Setup,int Mutation);
    private sealed record ActionFixtureMutation(int Serial,System.Action<Scene> Apply);

    // This probe is deliberately an application, not another authority. It
    // decodes packets and withholds explicit result ACKs; it never changes HP.
    private sealed class ActionResultProbe
    {
        private readonly Client _client;
        private readonly WeaponDefinition _definition=RiftlineM7.Create();
        private readonly CustomWeaponManifest _manifest;
        internal readonly Dictionary<uint,CustomReceiptMessage> Receipts=new();
        internal readonly Dictionary<uint,CustomContactMessage> Contacts=new();
        private readonly Dictionary<uint,byte[]> _receiptBytes=new(),_contactBytes=new();
        internal CustomStateMessage? State;
        internal uint SnapshotWorld;
        internal int ReceiptPackets,ContactPackets,StatePackets,DiscardReceipts;
        internal ActionResultProbe(Client client)
        {
            _client=client; _manifest=new(new[] { _definition });
            var readiness=client.PacketReceived;
            client.PacketReceived=packet => { readiness?.Invoke(packet); Receive(packet); };
        }
        private void Receive(ReceivedPacket packet)
        {
            if (packet.Type is PacketType.Snapshot or PacketType.SnapshotFast && packet.Payload.Length>=SnapshotHeader.Size)
            {
                var native=SnapshotHeader.Read(packet.Payload);
                if (unchecked((int)(native.Frame-SnapshotWorld))>0) SnapshotWorld=native.Frame;
            }
            if (packet.Type==PacketType.CustomWeaponReceipt)
            {
                ReceiptPackets++;
                Check(CustomCombatResults.TryReadReceipt(packet.Payload,_manifest,_definition,out var value),
                    "real UDP receipt decodes full owner/action, source and bounded resource snapshots");
                uint sequence=value.Receipt.Action.Sequence;
                if (_receiptBytes.TryGetValue(sequence,out var first))
                    Check(packet.Payload.SequenceEqual(first),"application retry retains byte-identical receipt and token");
                else _receiptBytes.Add(sequence,packet.Payload.ToArray());
                if (DiscardReceipts>0) { DiscardReceipts--; return; }
                if (Receipts.TryGetValue(sequence,out var previous))
                    Check(previous==value,"duplicate receipt is immutable at the application boundary");
                else Receipts.Add(sequence,value);
            }
            else if (packet.Type==PacketType.CustomWeaponContact)
            {
                ContactPackets++;
                Check(CustomCombatResults.TryReadContact(packet.Payload,_manifest,_definition,out var value),
                    "real UDP contact decodes full action/victim life and canonical outcome");
                uint sequence=value.Outcome.Contact.Key.Action.Sequence;
                if (_contactBytes.TryGetValue(sequence,out var first))
                    Check(packet.Payload.SequenceEqual(first),"application retry retains byte-identical terminal contact and token");
                else _contactBytes.Add(sequence,packet.Payload.ToArray());
                if (Contacts.TryGetValue(sequence,out var previous))
                    Check(previous==value,"duplicate terminal outcome is immutable at the application boundary");
                else Contacts.Add(sequence,value);
            }
            else if (packet.Type==PacketType.CustomWeaponState)
            {
                StatePackets++;
                Check(CustomCombatResults.TryReadState(packet.Payload,_manifest,_definition,out var value),
                    "real UDP presentation state decodes bounded resource values and full owner");
                // A projection has a revision frontier per bootstrap. Older packets
                // cannot roll it back; HP is exclusively in native snapshots.
                if (State is not { } current || current.Bootstrap!=value.Bootstrap
                    || unchecked((int)(value.Revision-current.Revision))>0) State=value;
            }
        }
        internal void Ack(CustomResultAck ack)
        {
            byte[] bytes=new byte[CustomResultAck.Size]; ack.Write(bytes);
            _client.Send(PacketType.CustomWeaponResultAck,bytes);
        }
        internal void AckReceipt(uint sequence) => Ack(Receipts[sequence].Ack);
        internal void AckContact(uint sequence) => Ack(Contacts[sequence].Ack);
    }

    // Real dedicated engine and endpoint-owned UDP. Native remote scene-loading
    // acknowledgements are manual. Explicit placement/health/protection seeds
    // are fixture setup, executed on the simulation thread before source input.
    private sealed class ActionTransportFixture : IDisposable
    {
        internal readonly Rig Rig;
        internal readonly Client First,Second;
        internal readonly CustomReadinessProbe FirstReady,SecondReady;
        internal readonly ActionResultProbe A,B;
        private readonly ServerSim _sim;
        private readonly Scene _scene;
        private readonly FieldInfo _sinkField;
        private readonly SnapshotSink _original;
        private ActionWorldSnapshot? _latest;
        private int _setupRequest,_setupDone,_retiring,_mutationSerial,_mutationDone;
        private ActionFixtureMutation? _mutation;
        private bool _protect;
        private readonly bool _impaired,_publicAvailability;
        private uint _firstSource,_secondSource;
        private Vector3 _publicLaneFirst,_publicLaneSecond;
        private readonly System.Collections.Generic.Dictionary<CustomWeaponAction,CustomResourceKnowledge> _actionKnowledge=new();
        internal ActionWorldSnapshot Latest => Volatile.Read(ref _latest) ?? throw new InvalidOperationException("No engine snapshot.");
        internal ActionTransportFixture(bool protect=false,bool completeRoom=true,bool impaired=false,int maxPlayers=2,Guid ownerToken=default,bool tokenOwnerSecond=false,bool publicAvailability=false)
        {
            ResetActionLag();
            if(tokenOwnerSecond&&ownerToken==Guid.Empty)throw new ArgumentException("Second-slot token owner fixture requires an actual owner token.");
            Rig=new(token:ownerToken,simulate:true,room:"MP3 PROVING GROUND",maxPlayers:maxPlayers,customReadiness:!publicAvailability,customCombat:!publicAvailability,publicAvailability:publicAvailability);
            try
            {
                _impaired=impaired; _publicAvailability=publicAvailability;
                // Server faults are OFF. Each actual client holds outgoing and
                // incoming datagrams 80 ms, adding 160 ms RTT only once.
                if (impaired)
                {
                    NetLag.Configure("160"); NetLag.ConfigureLoss("10%"); NetLag.ConfigureSeed("431");
                    Console.WriteLine("CUSTOM ACTION UDP FAULTS: "+NetLag.Describe()+"; client transports only");
                }
                First=Rig.Add(9140,tokenOwnerSecond ? default:ownerToken); Second=Rig.Add(9141,tokenOwnerSecond ? ownerToken:default);
                FirstReady=new(First); SecondReady=new(Second); A=new(First); B=new(Second);
                var startOwner=tokenOwnerSecond ? Second:First;
                Rig.ReadyAll(); Rig.Expect(startOwner,startOwner.Command(LobbyCommandType.StartMatch),LobbyResultCode.Ok);
                First.Loaded(); Second.Loaded();
                Rig.Wait(()=>First.State?.Phase==SessionPhase.InMatch && FirstReady.Ready.HasValue && SecondReady.Ready.HasValue,
                    "actual dedicated match supplies both endpoint-bound custom manifests",15000);
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                _sim=(ServerSim)typeof(DedicatedServer).GetField("_sim",hidden)!.GetValue(Rig.Server)!;
                _scene=_sim.EvidenceScene!;
                Check(_scene.CustomCombat is { IsEligible:true },"explicit custom combat is installed in actual dedicated scene");
                if(publicAvailability) Check(!Rig.Server.DeveloperCustomWeaponReadiness&&!Rig.Server.DeveloperCustomWeaponCombat
                    &&!NetSession.DeveloperCustomWeaponReadiness&&First.State!.Value.Match.RiftlineM7Available,
                    "public frozen match activates real engine combat with every developer capability flag off");
                _sinkField=typeof(NetSession).GetField("_snapshotSink",BindingFlags.Static|BindingFlags.NonPublic)!;
                _original=(SnapshotSink)_sinkField.GetValue(null)!;
                _protect=protect; Volatile.Write(ref _setupRequest,1);
                SnapshotSink observe=payload =>
                {
                    _original(payload);
                    if (Volatile.Read(ref _retiring)!=0 || _scene.CustomCombat is not { IsEligible:true } root) return;
                    if (NetPlayerLifecycle.Generation(First.Slot)==0 || NetPlayerLifecycle.Get(First.Slot)==0
                        || NetPlayerLifecycle.Generation(Second.Slot)==0 || NetPlayerLifecycle.Get(Second.Slot)==0) return;
                    int request=Volatile.Read(ref _setupRequest);
                    if (request!=_setupDone) { SetupActors(); _setupDone=request; }
                    if (Interlocked.Exchange(ref _mutation,null) is { } mutation)
                    { mutation.Apply(_scene); _mutationDone=mutation.Serial; }
                    ActionActorSnapshot Capture(Client client)
                    {
                        var player=_scene.Players.Items[client.Slot];
                        var owner=new WeaponOwnerContext(NetSession.CurrentMatchId,NetSession.AuthorityEpoch,(byte)client.Slot,
                            NetPlayerLifecycle.Generation(client.Slot),NetPlayerLifecycle.Get(client.Slot));
                        bool state=root.TryState(player,out var resource);
                        uint input=NetSession.RemoteIntents[client.Slot].Frame;
                        bool source=root.TryGetSourceObservation(owner,input,out var observation);
                        PlayerState native=default; NetDamage.Write(client.Slot,ref native);
                        bool history=false;
                        for (int i=0;i<PlayerState.DamageHistory;i++) history|=native.EventAt(i).EventId!=0;
                        return new(owner,player.Position,Fixed.ToFloat(player.Values.MaxPickupHeight)-.15f,player.Health,
                            NetDamage.Sequence(client.Slot),history,state,resource,input,source,observation,
                            _scene.GameState.Kills[client.Slot],_scene.GameState.Deaths[client.Slot]);
                    }
                    Volatile.Write(ref _latest,new(NetSession.NetFrame,Capture(First),Capture(Second),
                        root.TerminalContacts,root.PositivePayments,_sim.StepFailures,_setupDone,_mutationDone));
                };
                _sinkField.SetValue(null,observe);
                Rig.Wait(()=>Volatile.Read(ref _latest) is { Setup:1 },"simulation thread seeds measured clear body/head fixture lane");
                Check(!Latest.First.HasState && !Latest.Second.HasState,"native bootstrap and manifest publication grant no custom inventory");
                FirstReady.Send(); if (completeRoom) SecondReady.Send();
                Rig.Wait(()=>FirstReady.Confirmations>0 && (!completeRoom || SecondReady.Confirmations>0),
                    "requested actual participating endpoints acknowledge the same custom manifest");
                _firstSource=Latest.World+1; _secondSource=_firstSource;
                Observe(First,_firstSource,false);
                if (completeRoom) { Observe(Second,_secondSource,false); WaitInitialStates(); }
            }
            catch { Rig.Dispose(); ResetActionLag(); throw; }
        }
        private void SetupActors()
        {
            var first=_scene.Players.Items[First.Slot]; var second=_scene.Players.Items[Second.Slot];
            _scene.GameState.MatchTime=-1; _scene.GameState.PointGoal=10000; _scene.GameState.DamageLevel=1;
            _scene.GameState.EnhancedHunters=_scene.GameState.BalancedMode=false; _scene.GameState.PrimeHunter=-1;
            foreach (var item in _scene.GetItemInstanceEntities()) item.DespawnTimer=0;
            foreach (var player in new[] { first,second })
            {
                player.ModForceForm(false); player.Controls.ClearAll(); player.Health=200;
                Field(player,"_spawnInvulnTimer",(ushort)0); Field(player,"_damageInvulnTimer",(ushort)0);
                Field(player,"_frozenTimer",(ushort)0); player.ModReleaseSpawnProtectionFromNetwork();
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(player,GunAnimation.Idle);
                NetDamage.ForgetSlot(player.SlotIndex);
            }
            bool clear=false; Vector3 origin=first.Position;
            if(_publicAvailability)
            {
                // Declared native spawn4 floor placement. Subsequent acquisition
                // advances actual UDP owner positions through authored contact.
                origin=new(2.291504f,.5f,-22.847168f);first.ModPlaceAt(origin);
            }
            foreach (int height in new[] { 0,2,5 })
            {
                foreach (var direction in new[] { Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitX,-Vector3.UnitZ })
                {
                    Vector3 start=origin+Vector3.UnitY*height,end=start+direction*3,muzzle=start+Vector3.UnitY*.4f;
                    if (BeamProjectileEntity.TryBeamObstacle(muzzle,end+Vector3.UnitY*.2f,_scene,out _,out _)
                        || BeamProjectileEntity.TryBeamObstacle(muzzle,end+Vector3.UnitY*(Fixed.ToFloat(second.Values.MaxPickupHeight)-.15f),_scene,out _,out _)) continue;
                    first.ModPlaceAt(start); second.ModPlaceAt(end); clear=true; break;
                }
                if (clear) break;
            }
            if (!clear) throw new InvalidOperationException("Actual room has no clear fixture body/head lane.");
            if(_publicAvailability){_publicLaneFirst=first.Position;_publicLaneSecond=second.Position;}
            if (_protect) Field(second,"_spawnInvulnTimer",(ushort)2000);
        }
        private static void Field(PlayerEntity player,string name,object value)
            => typeof(PlayerEntity).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(player,value);
        private void Mutate(System.Action<Scene> apply,string label)
        {
            int serial=++_mutationSerial;
            if (Interlocked.CompareExchange(ref _mutation,new(serial,apply),null)!=null)
                throw new InvalidOperationException("Fixture mutations must be serialized.");
            Rig.Wait(()=>Latest.Mutation==serial,label);
        }
        internal void SeedHealth(int first,int second)
            => Mutate(scene =>
            {
                scene.Players.Items[First.Slot].Health=first; scene.Players.Items[Second.Slot].Health=second;
            },"explicit fixture health seed is applied on the simulation owner thread");
        internal void RespawnFirst()
            => Mutate(scene =>
            {
                var player=scene.Players.Items[First.Slot]; var position=player.Position; var node=player.NodeRef;
                // Actual native canonical environment death and Spawn. No
                // lifecycle table, journal, source or receipt is injected.
                player.TakeDamage(0,DamageFlags.Death|DamageFlags.IgnoreInvuln,null,player);
                if (player.Health!=0) throw new InvalidOperationException("Fixture native death failed.");
                player.Spawn(position,Vector3.UnitZ,Vector3.UnitY,node,respawn:true);
                player.Health=200; Field(player,"_spawnInvulnTimer",(ushort)0); Field(player,"_damageInvulnTimer",(ushort)0);
                Field(player,"_frozenTimer",(ushort)0); player.ModReleaseSpawnProtectionFromNetwork();
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(player,GunAnimation.Idle);
            },"native death/Spawn on the simulation thread advances the firing player's life");
        private void WaitInitialStates()
        {
            if(_publicAvailability)
            {
                Rig.Wait(()=>A.State is {HasWeapon:false}&&B.State is {HasWeapon:false}
                    &&FirstReady.Pickups?.IsReady==true&&SecondReady.Pickups?.IsReady==true,
                    "both public lives publish empty inventory and durably consume actual authored pickup baseline");
                AcquirePublic(First);return;
            }
            Rig.Wait(()=>A.State is { State.Magazine:4,State.Reserve:16,State.Phase:CustomWeaponPhase.Unequipped }
                && B.State is { State.Magazine:4,State.Reserve:16,State.Phase:CustomWeaponPhase.Unequipped }
                && Rig.Server.TryGetCustomReplicaReady(First.Slot,out bool firstReady,out _) && firstReady
                && Rig.Server.TryGetCustomReplicaReady(Second.Slot,out bool secondReady,out _) && secondReady,
                "fresh actual input automatically publishes independent initial four/sixteen unequipped state and recipient baselines are acknowledged");
            Check(A.State!.Value.State.Owner==Latest.First.Owner && B.State!.Value.State.Owner==Latest.Second.Owner,
                "initial wire resources name exact full current engine lives");
        }
        internal void CompleteRoom()
        {
            SecondReady.Send(); Rig.Wait(()=>SecondReady.Confirmations>0,"last actual participant completes custom readiness");
            if(_publicAvailability) Observe(First,++_firstSource,false);
            Observe(Second,_secondSource,false);
            if(_publicAvailability){WaitInitialStates();return;}
            Rig.Wait(()=>A.State.HasValue && B.State is { State.Magazine:4,State.Reserve:16,State.Phase:CustomWeaponPhase.Unequipped }
                && Rig.Server.TryGetCustomReplicaReady(First.Slot,out bool firstReady,out _) && firstReady
                && Rig.Server.TryGetCustomReplicaReady(Second.Slot,out bool secondReady,out _) && secondReady,
                "last participant's fresh actual source publishes its independent initial resources after both recipient baselines");
        }
        internal void Observe(Client client,uint source,bool shoot,bool head=false,uint? ackOverride=null,Vector3? position=null)
        {
            var world=Latest; bool first=client==First;
            var actor=first ? world.First:world.Second; var target=first ? world.Second:world.First;
            var aim=(target.Position+Vector3.UnitY*(head ? target.HeadHeight:.2f)
                -(actor.Position+Vector3.UnitY*.4f)).Normalized();
            var owner=actor.Owner;
            uint ack=ackOverride ?? (_impaired ? (client==First ? A:B).SnapshotWorld:world.World);
            Check(ack!=0,"source names an actually available native snapshot world");
            var input=new IntentPacket { MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,SlotGeneration=owner.Generation,
                LifeId=owner.Life,Frame=source,Position=position??actor.Position,Aim=aim,AckFrame=ack,
                Buttons=IntentButtons.InPlayState|(shoot ? IntentButtons.Shoot:0),WeaponSelect=(byte)BeamType.PowerBeam,
                HasState=true,HasFireEvents=true,HasAnalogMove=true,HasContinuousFireTick=true };
            // Empty explicit native fire-event list prevents native fallback
            // emission while preserving actual Shoot physical input evidence.
            byte[] bytes=new byte[CompactIntentCodec.Size(input)]; CompactIntentCodec.Write(input,bytes);
            PacketType packet=PacketType.Intent;
            if(_publicAvailability&&Knowledge(client).IsPresent)
            {
                var ready=(first?FirstReady:SecondReady).Ready!.Value;
                var wrapped=new byte[CustomInputCarrier.EmptyPrefixSize+bytes.Length];
                if(!CustomInputCarrier.TryWrite(ready.Bootstrap,ready.Manifest,Knowledge(client),null,bytes,wrapped,out int written)
                    ||written!=wrapped.Length)throw new InvalidOperationException("Public fixture source stamp cannot encode.");
                bytes=wrapped;packet=PacketType.CustomWeaponInput;
            }
            client.Send(packet,bytes);
            var retry=Stopwatch.StartNew(); long nextRetry=50;
            Rig.Wait(()=>{ var actorNow=client==First ? Latest.First:Latest.Second;
                if (_impaired && retry.ElapsedMilliseconds>=nextRetry)
                { client.Send(packet,bytes); nextRetry=retry.ElapsedMilliseconds+50; }
                return actorNow.HasSource && actorNow.Source.Input.Frame==source; },
                "actual UDP source reaches placement/control and first immutable engine observation",_impaired ? 8000:4000);
            var sample=client==First ? Latest.First:Latest.Second;
            Check(sample.Source.Input.AckFrame==ack && sample.Source.Input.Owner==owner
                && sample.Source.Input.InputBits==(uint)input.Buttons,"source retains exact accepted ACK, full life and physical buttons");
            Check(sample.Source.CanUseWeapon,"actual sampled native body/control state permits precision fire");
        }
        internal CustomWeaponAction Action(Client client,uint sequence,CustomWeaponActionKind kind,byte optic=0)
        {
            var actor=client==First ? Latest.First:Latest.Second;
            var ready=(client==First ? FirstReady:SecondReady).Ready!.Value;
            var source=actor.Source;
            var action=new CustomWeaponAction(ready.Bootstrap,ready.Manifest,(byte)client.Slot,actor.Owner.Life,new(10),sequence,
                source.Input.Frame,kind==CustomWeaponActionKind.Fire ? source.Input.AckFrame:0,
                kind==CustomWeaponActionKind.Fire ? source.Input.AckSubFrame:(byte)0,kind,optic,
                kind==CustomWeaponActionKind.Fire ? source.Body+Vector3.UnitY*.4f:Vector3.Zero,
                kind==CustomWeaponActionKind.Fire ? source.Aim:Vector3.Zero);
            _actionKnowledge.TryAdd(action,Knowledge(client));return action;
        }
        internal static byte[] Bytes(CustomWeaponAction action)
        { byte[] bytes=new byte[CustomPrivateAction.Size]; new CustomPrivateAction(action,default).Write(bytes); return bytes; }
        internal void Send(Client client,CustomWeaponAction action)
        {
            var bytes=new byte[CustomPrivateAction.Size];
            new CustomPrivateAction(action,_actionKnowledge.TryGetValue(action,out var known)?known:Knowledge(client)).Write(bytes);
            client.Send(PacketType.CustomWeaponAction,bytes);
        }
        internal CustomResourceKnowledge Knowledge(Client client)
        {
            var state=(client==First?A:B).State;var owner=(client==First?Latest.First:Latest.Second).Owner;
            return state is {HasWeapon:true} value&&value.State.Owner==owner?value.Knowledge:default;
        }
        internal void AcquirePublic(Client client)
        {
            if(!_publicAvailability)return;
            var probe=client==First?A:B;var receiver=client==First?FirstReady:SecondReady;
            var owner=(client==First?Latest.First:Latest.Second).Owner;
            if(probe.State is {HasWeapon:true} existing&&existing.State.Owner==owner)return;
            Refresh(client);
            Rig.Wait(()=>probe.State is {HasWeapon:false} empty&&empty.State.Owner==owner&&receiver.Pickups?.IsReady==true,
                "public acquiring life starts empty with actual recipient pickup baseline");
            var wait=Stopwatch.StartNew();long heartbeat=0;
            Rig.Wait(()=>
            {
                if(wait.ElapsedMilliseconds>=heartbeat)
                {Refresh(First);Refresh(Second);heartbeat=wait.ElapsedMilliseconds+250;}
                return receiver.Pickups!.TryState(0,out var item)&&item.Available;
            },"actual authored pickup becomes available; repeat owners wait genuine1800 simulation ticks",45000);
            receiver.Pickups!.TryState(0,out var available);
            Mutate(scene=>
            {
                var player=scene.Players.Items[client.Slot];player.ModPlaceAt(new(2.291504f,.5f,-22.847168f));
                player.Speed=Vector3.Zero;
            },"explicit native spawn4 floor placement precedes actual UDP pickup approach");
            var start=new Vector3(2.291504f,.5f,-22.847168f);
            var center=receiver.Pickups.Layout.Placements[0].Position;center.Y=start.Y;
            int steps=0;
            for(;steps<40&&!(probe.State is {HasWeapon:true} funded&&funded.State.Owner==owner);steps++)
            {
                uint source=client==First?(_firstSource=Math.Max(_firstSource+1,Latest.World+1))
                    :(_secondSource=Math.Max(_secondSource+1,Latest.World+1));
                Observe(client,source,false,position:Vector3.Lerp(start,center,Math.Min(1,(steps+1)/30f)));
            }
            Rig.Wait(()=>probe.State is {HasWeapon:true} funded&&funded.State.Owner==owner&&funded.Knowledge.IsPresent
                &&receiver.Pickups.TryState(0,out var consumed)&&consumed.Key.Incarnation==available.Key.Incarnation+1&&!consumed.Available,
                "real authored contact funds only approached current life and publishes next incarnation/private knowledge");
            Check(steps>0&&probe.State!.Value.State is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Unequipped}
                &&!probe.State.Value.Selected.IsValid,"public first ownership is actual contact-funded4/16 with native selection preserved");
            Mutate(scene=>
            {
                scene.Players.Items[First.Slot].ModPlaceAt(_publicLaneFirst);
                scene.Players.Items[Second.Slot].ModPlaceAt(_publicLaneSecond);
                scene.Players.Items[First.Slot].Speed=scene.Players.Items[Second.Slot].Speed=Vector3.Zero;
            },"public contact fixture restores declared clear combat lane without changing HP or resources");
            Refresh(First);Refresh(Second);
            Console.WriteLine($"PUBLIC PICKUP acquired slot{client.Slot} life{owner.Life}, incarnation{available.Key.Incarnation}, actual UDP approach steps{steps}; no direct funding.");
        }
        internal CustomReceiptMessage Settle(Client client,CustomWeaponAction action)
        {
            var probe=client==First ? A:B; Send(client,action);
            Rig.Wait(()=>probe.Receipts.ContainsKey(action.Sequence),"actual UDP action receives retained application receipt");
            return probe.Receipts[action.Sequence];
        }
        internal void Refresh(Client client)
        {
            if (client==First) { _firstSource=Math.Max(_firstSource+1,Latest.World+1); Observe(client,_firstSource,false); }
            else { _secondSource=Math.Max(_secondSource+1,Latest.World+1); Observe(client,_secondSource,false); }
        }
        internal void Equip(Client client,bool freshSources=false)
        {
            AcquirePublic(client);
            var probe=client==First ? A:B;
            if (freshSources) Refresh(client);
            var equip=Settle(client,Action(client,1,CustomWeaponActionKind.Equip));
            if (freshSources) Refresh(client);
            var optic=Settle(client,Action(client,2,CustomWeaponActionKind.Optic,1));
            Check(equip.Receipt.Result==WeaponActionResult.Accepted && optic.Receipt.Result==WeaponActionResult.Accepted,
                "UDP equip and optic actions are source-admitted in exact sequence");
            probe.AckReceipt(1); probe.AckReceipt(2);
            Rig.Wait(()=>Rig.Server.TryGetCustomActionPending(client.Slot,out int pending) && pending==0,
                "full-identity application receipt acknowledgements retire settled nonfire reservations");
        }
        internal CustomWeaponAction Press(Client client,uint sequence,bool head=false,uint? ack=null)
        {
            if (client==First) Observe(client,++_firstSource,true,head,ack);
            else Observe(client,++_secondSource,true,head,ack);
            return Action(client,sequence,CustomWeaponActionKind.Fire,1);
        }
        internal long DroppedDatagrams()
        {
            long total=0;
            foreach (var client in new[] { First,Second })
            {
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                object gate=typeof(NetTransport).GetField("_heldLock",hidden)!.GetValue(client.Transport)!;
                lock (gate)
                    foreach (string name in new[] { "_heldIn","_heldOut" })
                    {
                        object queue=typeof(NetTransport).GetField(name,hidden)!.GetValue(client.Transport)!;
                        total+=(long)queue.GetType().GetProperty("Dropped")!.GetValue(queue)!;
                    }
            }
            return total;
        }
        internal void WaitWorld(uint world,string label) => Rig.Wait(()=>Latest.World>=world,label);
        public void Dispose()
        {
            Volatile.Write(ref _retiring,1);
            // The original native sink remains valid while ServerSim retires.
            if (_sinkField!=null) _sinkField.SetValue(null,_original);
            try { FirstReady?.Dispose(); SecondReady?.Dispose(); Rig.Dispose(); }
            finally { ResetActionLag(); }
        }
    }

    private static void ResetActionLag()
    {
        NetLag.Configure("0"); NetLag.ConfigureLoss("0"); NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        NetLag.ConfigureSeed("1");
    }

    public static int RunCustomActionTransport(string data,bool publicAvailability=false)
    {
        string directory=Directory.GetCurrentDirectory(); _checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
            CustomActionUdpDamage(false,false,publicAvailability); CustomActionUdpDamage(true,false,publicAvailability); CustomActionUdpDamage(false,true,publicAvailability);
            CustomActionUdpCapacity(publicAvailability); CustomActionUdpOwnership(publicAvailability); CustomActionUdpRoomGate(publicAvailability);
            CustomActionUdpTrade(false,publicAvailability); CustomActionUdpTrade(true,publicAvailability); CustomActionUdpRespawnRetention(publicAvailability); CustomActionUdpImpaired(publicAvailability);
            Console.WriteLine($"PASS {_checks} real UDP/engine custom action transport checks.");
            Console.WriteLine("SCOPE: actual dedicated engine, endpoint UDP readiness/input/action handlers, source-admitted equip/optic/fire, retained receipt/contact application ACKs, bounded backpressure, canonical health/trade, native respawn and one client-transport 160ms RTT/10% seeded datagram-loss run. Remote native scene-load acknowledgements and clear-lane/health/protection/death/respawn setup are explicit fixtures. No rendered client, bot/local parity or latency fairness is claimed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM ACTION UDP FAIL "+ex); return 1; }
        finally { NetSession.Stop(); Directory.SetCurrentDirectory(directory); }
    }

    private static void CustomActionUdpDamage(bool head,bool protect,bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(protect,publicAvailability:publicAvailability); f.Equip(f.First);
        if(publicAvailability) f.Rig.Server.RiftlineM7Available=false;
        var action=f.Press(f.First,3,head);
        f.A.DiscardReceipts=1;
        f.Send(f.First,action);
        f.Rig.Wait(()=>f.A.ReceiptPackets>0,"first application receipt arrives and is deliberately discarded");
        f.Send(f.First,action);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(3) && f.A.Contacts.ContainsKey(3),
            "duplicate exact UDP action recovers immutable receipt and terminal contact under withheld application ACK",6000);
        var receipt=f.A.Receipts[3]; var contact=f.A.Contacts[3]; var health=contact.Outcome.Health;
        int damage=protect ? 0:head ? 150:65;
        Check(receipt.Receipt.Action==action && receipt.Receipt.Result==WeaponActionResult.Accepted
            && receipt.Receipt.Emission==CustomEmissionDisposition.Spawned
            && receipt.Receipt.Before is { Magazine:4 } && receipt.Receipt.After is { Magazine:3 },
            "one UDP Fire pays one round and emits once despite receipt loss and duplicate action");
        Check(receipt.Receipt.Source is { } source && source.ObservedWorldFrame<receipt.Receipt.AuthorityFrame
            && source.TargetWorldFrame==action.AckFrame,"processor waits past actual source observation world and preserves client target ACK");
        Check(contact.Outcome.Contact.Key.Action==CustomActionIdentity.For(action)
            && contact.Outcome.Contact.Key.Victim==new CustomVictimIdentity(f.Latest.Second.Owner.Slot,f.Latest.Second.Owner.Generation,f.Latest.Second.Owner.Life)
            && contact.Outcome.Contact.Key.Component==1 && contact.Outcome.Contact.Key.Channel==CustomImpactChannel.Body,
            "contact carries full independent action and exact victim generation/life/component/channel");
        Check(contact.Outcome.Contact.Headshot==head && health.BodyBefore==200 && health.BodyAfter==200-damage
            && health.BodyRemoved==damage && (health.DamageEventId!=0)==!protect,
            "actual authority collision produces exact body65/head150/protected-zero canonical damage");
        f.Rig.Wait(()=>f.Latest.Contacts==1 && f.Latest.Second.Health==200-damage,
            "actual engine commits one terminal nonpiercing contact and expected victim health");
        Check(f.Latest.Payments==(protect ? 0UL:1UL) && !f.Latest.Second.NativeHistory,
            "custom canonical health changes never insert native damage-event history");
        Check(f.Latest.Second.DamageId==health.DamageEventId,"wire contact names the same canonical victim damage counter");
        int receipts=f.A.ReceiptPackets,contacts=f.A.ContactPackets;
        ObserveCustomReadiness(f.Rig,350,"withheld result application ACKs trigger bounded semantic resend");
        Check(f.A.ReceiptPackets>receipts && f.A.ContactPackets>contacts && f.Latest.Contacts==1
            && f.Latest.Second.Health==200-damage,"resending receipt/contact causes neither second damage nor second canonical contact");
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending) && pending==1,
            "one action retains receipt and contact until both exact application acknowledgements");
        f.A.Ack(receipt.Ack with { Token=receipt.Token+1 });
        f.A.Ack(contact.Ack with { Kind=CustomResultKind.Receipt });
        byte[] stolen=new byte[CustomResultAck.Size]; contact.Ack.Write(stolen);
        f.Second.Send(PacketType.CustomWeaponResultAck,stolen);
        ObserveCustomReadiness(f.Rig,80,"wrong-token and wrong-kind result acknowledgements traverse actual dispatch");
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out pending) && pending==1,
            "wrong token/kind or another admitted endpoint cannot free live result reservation");
        f.A.AckReceipt(3);
        ObserveCustomReadiness(f.Rig,80,"receipt acknowledgement arrives independently of contact acknowledgement");
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out pending) && pending==1,
            "receipt ACK alone retains the terminal contact reservation");
        f.A.AckContact(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int count) && count==0,
            "both full result acknowledgements release exactly one bounded action reservation");
        Check(f.Latest.StepFailures==0,"real UDP action/contact processing has no simulation exceptions");
    }

    private static void CustomActionUdpCapacity(bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(publicAvailability:publicAvailability);
        for (uint sequence=1;sequence<=16;sequence++)
        {
            var kind=sequence==1 ? CustomWeaponActionKind.Equip:CustomWeaponActionKind.Optic;
            var receipt=f.Settle(f.First,f.Action(f.First,sequence,kind,sequence==1 ? (byte)0:(byte)1));
            Check(receipt.Receipt.Result==WeaponActionResult.Accepted,"all sixteen unacknowledged actions settle without eviction");
        }
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending) && pending==16,
            "per-peer live application-result capacity is exactly sixteen");
        var fire=f.Press(f.First,17); f.Send(f.First,fire);
        uint before=f.Latest.World;
        f.WaitWorld(before+8,"full result outbox observes a genuine ready-to-fire UDP action over multiple engine frames");
        Check(!f.A.Receipts.ContainsKey(17) && !f.A.Contacts.ContainsKey(17) && f.Latest.First.State.Magazine==4
            && f.Latest.Second.Health==200 && f.Latest.Contacts==0,
            "outbox capacity blocks action mutation before ammo, emission and HP");
        f.A.AckReceipt(1);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int count) && count==15,
            "one exact application ACK makes one result reservation available");
        var receipt17=f.Settle(f.First,fire);
        f.Rig.Wait(()=>f.A.Contacts.ContainsKey(17),"same previously blocked action can settle after explicit capacity release");
        Check(receipt17.Receipt.Result==WeaponActionResult.Accepted && receipt17.Receipt.After is { Magazine:3 }
            && f.A.Contacts[17].Outcome.Health.BodyRemoved==65 && f.Latest.Contacts==1,
            "capacity refusal neither burns action identity nor partly charges the eventual successful shot");
        Check(f.Latest.StepFailures==0,"outbox backpressure and recovery leave actual simulation healthy");
    }

    private static void CustomActionUdpOwnership(bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(publicAvailability:publicAvailability);
        var equip=f.Action(f.First,1,CustomWeaponActionKind.Equip);
        f.Send(f.Second,equip);
        ObserveCustomReadiness(f.Rig,100,"different admitted UDP slot carries copied custom action");
        Check(f.A.Receipts.Count==0 && f.B.Receipts.Count==0 && f.Latest.First.State.Phase==CustomWeaponPhase.Unequipped,
            "an admitted peer cannot act for another full custom owner");
        var transport=(NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Rig.Server)!;
        long invalid=transport.Telemetry.Capture().Invalid;
        using (var stranger=new UdpClient(0))
        {
            byte[] payload=ActionTransportFixture.Bytes(equip),wire=new byte[NetHeader.Size+4+payload.Length];
            ulong connection=f.First.Transport.ConnectionStats(f.First.Server)!.Value.ConnectionId;
            new NetHeader(PacketType.CustomWeaponAction,NetHeaderFlags.Reliable,connection,0x71000000,0,0).Write(wire);
            BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(NetHeader.Size),1); payload.CopyTo(wire,NetHeader.Size+4);
            stranger.Send(wire,wire.Length,f.First.Server);
            f.Rig.Wait(()=>transport.Telemetry.Capture().Invalid>invalid,"foreign UDP endpoint with copied live connection ID is rejected by transport");
        }
        Check(f.A.Receipts.Count==0 && f.Latest.First.State.Magazine==4,
            "foreign endpoint cannot manufacture a custom action receipt or debit resources");
        var settled=f.Settle(f.First,equip);
        Check(settled.Receipt.Result==WeaponActionResult.Accepted,"foreign ownership attempts do not poison the correct endpoint action");
        f.Send(f.First,equip with { Kind=CustomWeaponActionKind.Unequip });
        ObserveCustomReadiness(f.Rig,150,"contradictory payload under the same full action identity reaches production ingress");
        Check(f.A.Receipts[1]==settled && f.Latest.Contacts==0 && f.Latest.First.State.Magazine==4,
            "conflicting duplicate cannot revise settled receipt or emit/debit a second action");
        Check(f.Latest.StepFailures==0,"ownership and duplicate conflicts do not throw on simulation thread");
    }

    private static void CustomActionUdpRoomGate(bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(completeRoom:false,publicAvailability:publicAvailability);
        if(publicAvailability)
        {
            Check(f.Latest.First is { HasState:false,HasSource:true }
                &&!f.Latest.Second.HasState&&!f.A.State.HasValue&&!f.B.State.HasValue,
                "ready public actor binds a genuine source without manufacturing inventory before room readiness");
            var ready=f.FirstReady.Ready!.Value;
            var premature=new CustomControlRequest(ready.Bootstrap,ready.Manifest,(byte)f.First.Slot,
                f.Latest.First.Owner.Life,new(10),1,CustomWeaponActionKind.Equip,0);
            var bytes=new byte[CustomControlRequest.Size]; premature.Write(bytes);
            f.First.Send(PacketType.CustomWeaponControl,bytes);
            uint waiting=f.Latest.World;
            f.WaitWorld(waiting+6,"public incomplete capability room continues native simulation without custom activation");
            Check(!f.Latest.First.HasState&&!f.Latest.Second.HasState
                &&!f.A.State.HasValue&&!f.B.State.HasValue&&f.A.Receipts.Count==0&&f.Latest.Contacts==0&&f.Latest.Payments==0
                &&(!f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)||pending==0),
                "public partial readiness publishes no private projection and reserves no premature control deadline or action effects");
            f.CompleteRoom();
            Check(f.A.State is { HasWeapon:true,State.Phase:CustomWeaponPhase.Unequipped }
                &&f.B.State is { HasWeapon:false }&&f.Latest.StepFailures==0,
                "public ready room funds only actual approaching owner while native victim remains empty");
            f.Equip(f.First,freshSources:true);
            return;
        }
        Check(f.Latest.First.HasState && !f.Latest.Second.HasState,
            "one ready source owner cannot manufacture the other participant's inventory");
        var equip=f.Action(f.First,1,CustomWeaponActionKind.Equip); f.Send(f.First,equip);
        uint before=f.Latest.World;
        f.WaitWorld(before+6,"actual unsupported participant remains in the room through several action-processing phases");
        Check(f.A.Receipts.Count==0 && f.Latest.First.State.Phase==CustomWeaponPhase.Unequipped
            && f.Latest.First.State.Magazine==4 && f.Latest.Second.Health==200,
            "one unsupported current participant blocks all custom action effects before partial room activation");
        f.CompleteRoom(); var receipt=f.Settle(f.First,equip);
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && f.Latest.Second.HasState,
            "exact action retry can progress after every current participant becomes ready");
        Check(f.Latest.StepFailures==0,"room capability gate leaves ordinary native simulation running");
    }

    private static void CustomActionUdpTrade(bool earlierKill,bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(publicAvailability:publicAvailability); f.Equip(f.First); f.Equip(f.Second);
        f.SeedHealth(65,65);
        uint sharedAck=f.Latest.World;
        var first=f.Press(f.First,3,ack:sharedAck);
        if (earlierKill) f.WaitWorld(sharedAck+2,"second living source can acknowledge a strictly later sampled world");
        var second=f.Press(f.Second,3,ack:earlierKill ? f.Latest.World:sharedAck);
        Check(f.Latest.First.Health==65 && f.Latest.Second.Health==65 && first.AckFrame==sharedAck
            && (earlierKill ? second.AckFrame>first.AckFrame:second.AckFrame==first.AckFrame),
            "both lethal source inputs were actually captured while alive with declared equal/later ACK worlds");
        var firstReceipt=f.Settle(f.First,first);
        f.Rig.Wait(()=>f.A.Contacts.ContainsKey(3) && f.Latest.Second.Health==0,
            "first actual UDP shot canonically kills the opposing full life");
        Check(firstReceipt.Receipt.Result==WeaponActionResult.Accepted && f.A.Contacts[3].Outcome.Lethal
            && f.A.Contacts[3].Outcome.Health.BodyRemoved==65,"first killing result is an actual canonical sixty-five damage contact");
        // Deliver the second already-captured action AFTER the first canonical
        // kill. This exercises the launch-world trade fence, not simultaneous
        // processing order or an injected death certificate.
        var secondReceipt=f.Settle(f.Second,second);
        if (earlierKill)
        {
            Check(secondReceipt.Receipt.Disposition==CustomJournalDisposition.SourceDenied
                && secondReceipt.Receipt.Result==WeaponActionResult.InvalidLife
                && secondReceipt.Receipt.Emission==CustomEmissionDisposition.NotAttempted
                && !secondReceipt.Receipt.Before.HasValue && !secondReceipt.Receipt.After.HasValue,
                "earlier canonical killing ACK rejects the later-world predeath source before emission/resources");
            uint before=f.Latest.World; f.WaitWorld(before+6,"denied later-world source traverses subsequent projectile phases");
            Check(f.Latest.First.Health==65 && f.Latest.Second.Health==0 && f.Latest.Second.State.Magazine==4
                && !f.B.Contacts.ContainsKey(3) && f.Latest.Contacts==1 && f.Latest.Payments==1,
                "earlier-kill denial creates no round debit, contact or return damage");
            Check(f.Latest.First.Kills==1 && f.Latest.First.Deaths==0 && f.Latest.Second.Kills==0 && f.Latest.Second.Deaths==1,
                "earlier-kill denial preserves exactly one canonical score/death transition");
        }
        else
        {
            f.Rig.Wait(()=>f.B.Contacts.ContainsKey(3) && f.Latest.First.Health==0 && f.Latest.Contacts==2,
                "equal-ACK action delivered after death still pays the admitted return kill exactly once");
            Check(secondReceipt.Receipt.Result==WeaponActionResult.Accepted
                && secondReceipt.Receipt.Emission==CustomEmissionDisposition.Spawned
                && secondReceipt.Receipt.Source?.TargetWorldFrame==sharedAck
                && secondReceipt.Receipt.After is { Magazine:3 }
                && f.B.Contacts[3].Outcome.Lethal && f.B.Contacts[3].Outcome.Health.BodyRemoved==65,
                "equal-world trade shares actual canonical death fence and spends one return round");
            Check(f.Latest.First.Kills==1 && f.Latest.First.Deaths==1 && f.Latest.Second.Kills==1 && f.Latest.Second.Deaths==1
                && f.Latest.Payments==2 && !f.Latest.First.NativeHistory && !f.Latest.Second.NativeHistory,
                "UDP equal-world trade produces exactly two canonical scores/deaths without native event history");
            f.Send(f.Second,second);
            uint before=f.Latest.World; f.WaitWorld(before+6,"duplicate equal-world dead-owner action reenters actual UDP ingress");
            Check(f.Latest.Contacts==2 && f.Latest.First.Deaths==1 && f.Latest.Second.Deaths==1
                && f.Latest.First.State.Magazine==3 && f.Latest.Second.State.Magazine==3,
                "postdeath UDP duplicate cannot pay, kill, score or spend a trade again");
        }
        Check(f.Latest.StepFailures==0,"actual UDP trade and earlier-kill denial complete without engine exceptions");
    }

    private static void CustomActionUdpRespawnRetention(bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(publicAvailability:publicAvailability); f.Equip(f.First);
        var fire=f.Press(f.First,3); var receipt=f.Settle(f.First,fire);
        f.Rig.Wait(()=>f.A.Contacts.ContainsKey(3) && f.Latest.Second.Health==135,
            "old firing life owns a genuine emitted UDP receipt and canonical contact before respawn");
        var contact=f.A.Contacts[3]; var oldOwner=fire.Owner;
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending) && pending==1,
            "old firing life retains its unacknowledged receipt/contact obligation");
        f.RespawnFirst(); var next=f.Latest.First.Owner;
        Check(next.Generation==oldOwner.Generation && next.Life!=oldOwner.Life && next.MatchId==oldOwner.MatchId
            && next.AuthorityEpoch==oldOwner.AuthorityEpoch,"actual native Spawn advances life within the same admitted occupant");
        f.Refresh(f.First);
        f.Rig.Wait(()=>f.A.State is { } state && state.State.Owner==next
            && (publicAvailability ? !state.HasWeapon&&state.State.Magazine==0&&state.State.Reserve==0
                : state.State.Phase==CustomWeaponPhase.Unequipped&&state.State.Magazine==4&&state.State.Reserve==16),
            "fresh post-respawn source publishes explicit public absence or developer fixture inventory");
        int receiptPackets=f.A.ReceiptPackets,contactPackets=f.A.ContactPackets;
        ObserveCustomReadiness(f.Rig,350,"old-life application obligations retry after a genuine native respawn");
        Check(f.A.ReceiptPackets>receiptPackets && f.A.ContactPackets>contactPackets && f.A.Receipts[3]==receipt
            && f.A.Contacts[3]==contact && f.A.Contacts[3].Outcome.Contact.Key.Action.Owner==oldOwner,
            "respawn retains exact old full-life receipt/contact tokens and immutable payloads");
        Check(f.A.State!.Value.State.Owner==next && f.A.State.Value.State.Magazine==(publicAvailability?0:4)
            && f.A.State.Value.State.LastAcceptedShotId==0 && f.Latest.Second.Health==135 && f.Latest.Contacts==1,
            "retrying old-life results neither rolls back fresh-life resources nor pays victim twice");
        f.A.Ack(receipt.Ack with { Life=next.Life }); f.A.Ack(contact.Ack with { Life=next.Life });
        ObserveCustomReadiness(f.Rig,80,"new-life rewritten acknowledgements traverse old-life result retention");
        Check(f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out pending) && pending==1,
            "new current life cannot acknowledge another life merely by reusing sequence/token");
        f.A.Ack(receipt.Ack); f.A.Ack(contact.Ack);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int count) && count==0,
            "same bootstrap/connection may acknowledge exact retained old-life results after native respawn");
        if(publicAvailability)f.AcquirePublic(f.First);
        Check(f.Latest.First.Owner==next && f.Latest.First.State.Magazine==4 && f.Latest.First.State.Reserve==16
            && f.Latest.StepFailures==0,"old-life ACK retirement and genuine public reacquisition leave fresh inventory intact");
    }

    private static void CustomActionUdpImpaired(bool publicAvailability=false)
    {
        using var f=new ActionTransportFixture(impaired:true,publicAvailability:publicAvailability);
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var serverTransport=(NetTransport)typeof(DedicatedServer).GetField("_transport",hidden)!.GetValue(f.Rig.Server)!;
        Check(typeof(NetTransport).GetField("_lagWorker",hidden)!.GetValue(serverTransport)==null
            && typeof(NetTransport).GetField("_lagWorker",hidden)!.GetValue(f.First.Transport)!=null
            && typeof(NetTransport).GetField("_lagWorker",hidden)!.GetValue(f.Second.Transport)!=null,
            "actual client datagrams traverse fault workers while dedicated transport adds no duplicate RTT");
        Check(NetLag.RoundTripMs==160 && NetLag.LossPercent==10 && NetLag.JitterMs==0
            && NetLag.ReorderRate==0 && NetLag.DuplicateRate==0 && NetLag.Seed==431,
            "impaired run declares exact 160ms RTT and deterministic ten-percent datagram loss");
        long droppedBefore=f.DroppedDatagrams();
        // Each action gets a freshly consumed actual source; sequence order
        // does not ask the strict 64-world window to cover a whole handshake.
        f.Equip(f.First,freshSources:true);
        f.Refresh(f.First); var fire=f.Press(f.First,3);
        Check(fire.AckFrame<=f.A.SnapshotWorld && fire.AckFrame<f.Latest.World,
            "impaired Fire echoes a native snapshot actually delivered over the delayed client transport");
        f.A.DiscardReceipts=1; f.Send(f.First,fire);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(3) && f.A.Contacts.ContainsKey(3) && f.Latest.Second.Health==135,
            "actual source/action/result UDP survives delayed lossy datagrams and an intentionally discarded application receipt",8000);
        var receipt=f.A.Receipts[3]; var contact=f.A.Contacts[3];
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted && receipt.Receipt.After is { Magazine:3 }
            && receipt.Receipt.Source?.TargetWorldFrame==fire.AckFrame && contact.Outcome.Health.BodyRemoved==65
            && f.Latest.Contacts==1 && f.Latest.Payments==1 && !f.Latest.Second.NativeHistory,
            "impaired real UDP shot consumes one round and canonically removes sixty-five HP once");
        long droppedDuring=f.DroppedDatagrams()-droppedBefore;
        Check(droppedDuring>0,"fault queues actually dropped datagrams during fresh source/action/result traffic");
        Console.WriteLine($"CUSTOM ACTION UDP FAULT EVIDENCE: droppedDuringActions={droppedDuring}, receiptPackets={f.A.ReceiptPackets}, contactPackets={f.A.ContactPackets}");
        f.A.AckReceipt(3); f.A.AckContact(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending) && pending==0,
            "exact application acknowledgements eventually retire impaired-path receipt/contact reservation",8000);
        Check(f.Latest.Second.Health==135 && f.Latest.Contacts==1 && f.Latest.StepFailures==0,
            "delayed transport/application retries leave canonical HP and simulation stable");
    }
}
