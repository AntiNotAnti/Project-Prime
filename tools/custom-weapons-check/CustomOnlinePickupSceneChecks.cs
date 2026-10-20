using System.Diagnostics;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Actual two-endpoint UDP/DedicatedServer/ServerSim contact and funding. Native
// loading ACKs, floor approach placement and authored native input positions are
// declared fixtures; no custom grant, certificate or source proof is injected.
internal static class CustomOnlinePickupSceneChecks
{
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed record Observation(uint World,WeaponOwnerContext Owner,Vector3 Body,int Health,int OtherHealth,
        ushort Damage,ushort OtherDamage,uint Source,bool HasSource,bool HasWeapon,WeaponStateSnapshot State,
        CustomNativeSelection OldSelection,ulong Revision,CustomPickupItemState Item,long NativeAccepted,long NativeFired,
        ulong Emissions,ulong Payments,ulong Contacts,long Failures,bool RoomReady,bool OldRetained,CustomResourceKnowledge OldKnowledge,
        string Diagnostic,bool DisablePowerups,bool TimedCanPickup,bool WeaponCanPickup,bool Reaches);
    private static object Get(object value,string name)=>value.GetType().GetField(name,All)?.GetValue(value)
        ??value.GetType().GetProperty(name,All)?.GetValue(value)??throw new InvalidOperationException("Missing fixture member "+name);
    private static object? Call(object target,string name,params object?[] args)
    {
        var method=target.GetType().GetMethods(All).Single(m=>m.Name==name);var parameters=method.GetParameters();
        var supplied=parameters.Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray();
        try{return method.Invoke(target,supplied);}catch(TargetInvocationException e)when(e.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static CustomStateMessage? State(object probe)=>GetNullable(probe,"State") is CustomStateMessage value?value:null;
    private static object? GetNullable(object target,string name)=>target.GetType().GetField(name,All)!.GetValue(target);

    internal static int Run(string data)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,developer=NetSession.DeveloperCustomWeaponReadiness;
        object? fixture=null;FieldInfo? sinkField=null;SnapshotSink? previous=null;
        Observation? observed=null;int retiring=0,checks=0;uint oldSource=0;
        try
        {
            void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;Console.WriteLine("ONLINE PICKUP PASS "+label);}
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            MapAudit.ForceEveryone=true;NetSession.DeveloperCustomWeaponReadiness=false;
            var type=typeof(NetLobbyTest).GetNestedType("ActionTransportFixture",BindingFlags.NonPublic)!;
            var ctor=type.GetConstructors(All).Single();var parameters=ctor.GetParameters();
            var arguments=parameters.Select(p=>p.Name=="completeRoom"?(object)false:p.Name=="publicAvailability"?true:p.DefaultValue).ToArray();
            fixture=ctor.Invoke(arguments);var rig=Get(fixture,"Rig");var server=(DedicatedServer)Get(rig,"Server");
            var first=Get(fixture,"First");var second=Get(fixture,"Second");var a=Get(fixture,"A");var b=Get(fixture,"B");
            var firstReady=Get(fixture,"FirstReady");var secondReady=Get(fixture,"SecondReady");
            int slot=(int)Get(first,"Slot"),otherSlot=(int)Get(second,"Slot");
            var scene=(Scene)Get(fixture,"_scene");var sim=(ServerSim)Get(fixture,"_sim");
            void Wait(Func<bool> condition,string label,int ms=6000)=>Call(rig,"Wait",condition,label,ms);
            void Mutate(Action<Scene> mutation,string label)=>Call(fixture,"Mutate",mutation,label);
            void Send(object client,PacketType kind,byte[] bytes)=>Call(client,"Send",kind,bytes);
            object Actor(object client)=>Get(Get(fixture,"Latest"),ReferenceEquals(client,first)?"First":"Second");
            uint Next(object client)=>Math.Max((uint)Get(Get(fixture,"Latest"),"World")+1,(uint)Get(Actor(client),"InputFrame")+1);
            void Fresh(object client)=>Call(fixture,"Observe",client,Next(client),false);
            sinkField=typeof(NetSession).GetField("_snapshotSink",All)!;previous=(SnapshotSink)sinkField.GetValue(null)!;
            SnapshotSink observer=payload=>
            {
                previous(payload);
                if(Volatile.Read(ref retiring)!=0||scene.CustomCombat is not {IsEligible:true} root
                    ||NetPlayerLifecycle.Generation(slot)==0||NetPlayerLifecycle.Get(slot)==0)return;
                var player=scene.Players.Items[slot];var other=scene.Players.Items[otherSlot];
                var owner=new WeaponOwnerContext(NetSession.CurrentMatchId,NetSession.AuthorityEpoch,(byte)slot,
                    NetPlayerLifecycle.Generation(slot),NetPlayerLifecycle.Get(slot));
                uint source=NetSession.RemoteIntents[slot].Frame;
                bool hasSource=root.TryGetSourceObservation(owner,source,out var sample);
                root.TryPrivateState(player,out var state,out bool owns);
                CustomPickupItemState item=default;var ledger=root.Pickups;ledger?.TryState(0,out item);
                var native=NetFireEvents.CaptureLedger(slot);
                uint priorSource=Volatile.Read(ref oldSource);
                bool retained=root.TryGetSourceObservation(owner,priorSource,out var historical);
                var weaponPose=player.ModWeaponPickupContactPose(source,NetSession.NetFrame);
                var timedPose=player.ModPickupContactPose(source,NetSession.NetFrame);
                string diagnostic;bool reaches=false;
                try
                {
                    var pose=weaponPose;
                    var centre=ledger is {Layout.Count:>0}?ledger.Layout.Placements[0].Position:Vector3.Zero;
                    var contact=new CustomPickupContact(owner,source,NetSession.NetFrame,pose.Position,pose.SpherePosition,
                        pose.Radius,pose.MinHeight,pose.MaxHeight,pose.Alt,pose.Alive,pose.CanPickup);
                    reaches=contact.Reaches(centre);
                    var life=Call(root,"Find",owner);var journal=life==null?null:GetNullable(life,"Journal");
                    var knowledge=life==null?null:GetNullable(life,"Knowledge");
                    var delivery=GetNullable(root,"_delivery");
                    diagnostic=$"world={NetSession.NetFrame} source={source} sourceAvailable={hasSource} ingress={sample.Input.IngressWorld} "
                        +$"body={player.Position} centre={centre} lateral={(centre-pose.Position).WithY(0).Length:F4} dy={centre.Y-pose.Position.Y:F4} "
                        +$"radius={pose.Radius:F4} min={pose.MinHeight:F4} max={pose.MaxHeight:F4} alt={pose.Alt} "
                        +$"alive={pose.Alive} canPickup={pose.CanPickup} timedCanPickup={timedPose.CanPickup} disablePowerups={scene.Services.DisablePowerups} validContact={contact.IsValid} reaches={reaches} "
                        +$"health={player.Health} active={player.ModIsInPlay} rootEligible={root.IsEligible} intentAge={NetSession.RemoteIntentAge(slot)} "
                        +$"itemAvailable={item.Available} itemRevision={item.Revision} ledgerRetired={ledger?.IsRetired} "
                        +$"life={life!=null} journal={journal!=null} journalRetired={(journal==null?"n/a":Get(journal,"IsRetired"))} "
                        +$"journalFrontier={(journal==null?"n/a":Get(journal,"ObservedSourceFrame"))} journalAuthority={(journal==null?"n/a":Get(journal,"_authority"))} "
                        +$"knowledgeFence={knowledge!=null} knowledgeFrontier={(knowledge==null?"n/a":Get(knowledge,"SourceFrontier"))} "
                        +$"collectDelivery={(delivery==null?"n/a":Call(delivery,"CanCollectPickup",root,0))}";
                }
                catch(Exception diagnosticError){diagnostic="diagnostic-error="+diagnosticError;}
                Volatile.Write(ref observed,new(NetSession.NetFrame,owner,player.Position,player.Health,other.Health,
                    NetDamage.Sequence(slot),NetDamage.Sequence(otherSlot),source,hasSource,owns,state,
                    priorSource==0?CustomNativeSelection.Pending:root.ReadNativeSourceSelection(owner,priorSource),
                    ledger?.Revision??0,item,native.Accepted,native.Fired,root.AcceptedEmissions,root.PositivePayments,
                    root.TerminalContacts,sim.StepFailures,
                    (bool)server.GetType().GetMethod("CustomRoomReady",All)!.Invoke(server,new object[]{scene})!,
                    retained,historical.Input.Knowledge,diagnostic,scene.Services.DisablePowerups,timedPose.CanPickup,weaponPose.CanPickup,reaches));
            };
            sinkField.SetValue(null,observer);
            var publicItems=new Dictionary<int,CustomPickupReplicaState>();
            int ItemPackets()=>(int)Get(firstReady,"PickupPackets")+(int)Get(secondReady,"PickupPackets");
            int ItemAcks()=>(int)Get(firstReady,"PickupAcks")+(int)Get(secondReady,"PickupAcks");
            foreach(var client in new[]{first,second})
            {
                var peer=client;int peerSlot=(int)Get(peer,"Slot");
                var probe=ReferenceEquals(peer,first)?firstReady:secondReady;
                // The shared endpoint may have already consumed and ACKed its
                // baseline during fixture construction. Copy only its actual
                // production receiver's retained fact; never synthesize a packet
                // or another application ACK to make this test's view complete.
                if(GetNullable(probe,"Pickups") is CustomPickupReplicaReceiver receiver)
                    foreach(var retainedItem in (CustomPickupReplicaState?[])Get(receiver,"_states"))
                        if(retainedItem is {} retainedState)publicItems[peerSlot]=retainedState;
                var callback=peer.GetType().GetField("PacketReceived",All)!;
                var original=(Action<ReceivedPacket>?)callback.GetValue(peer);
                callback.SetValue(peer,(Action<ReceivedPacket>)(packet=>
                {
                    original?.Invoke(packet);if(packet.Type!=PacketType.CustomWeaponPickupState)return;
                    if(!CustomPickupReplicaCodec.TryReadState(packet.Payload,out var value))
                        throw new InvalidOperationException("Actual public item packet failed codec.");
                    var capability=(CustomWeaponReady)Get(ReferenceEquals(peer,first)?firstReady:secondReady,"Ready");
                    if(value.Bootstrap!=capability.Bootstrap||value.World.Manifest!=capability.Manifest
                        ||value.Count!=1||value.Index!=0||value.Item.Key.SpawnId!=1)
                        throw new InvalidOperationException("Public item recipient or authored station identity mismatch.");
                    if(publicItems.TryGetValue(peerSlot,out var previousItem)&&value.Serial==previousItem.Serial&&value!=previousItem)
                        throw new InvalidOperationException("Public item retry changed immutable serial payload.");
                    if(!publicItems.TryGetValue(peerSlot,out previousItem)||value.Serial>=previousItem.Serial)publicItems[peerSlot]=value;
                    // original invokes the common production receiver and sends
                    // its exact authenticated/digest-bound ACK once, above.
                }));
            }
            Call(secondReady,"Send");Wait(()=>(int)Get(secondReady,"Confirmations")>0,"second peer capability confirms");
            Fresh(first);Fresh(second);
            Wait(()=>State(a) is {HasWeapon:false}&&State(b) is {HasWeapon:false}
                &&server.TryGetCustomReplicaReady(slot,out bool ar,out _)&&ar
                &&server.TryGetCustomReplicaReady(otherSlot,out bool br,out _)&&br,"both actual UDP peers commit empty private lives and public baselines");
            Wait(()=>Volatile.Read(ref observed) is {RoomReady:true}&&publicItems.Count==2,
                "both genuine endpoints application-ACK their actual authored public item baselines");
            Check(ItemPackets()>=2&&ItemAcks()==ItemPackets(),
                "common production receivers account for actual baseline packets and ACKs including preinstalled facts");
            Check(!server.DeveloperCustomWeaponReadiness&&!server.DeveloperCustomWeaponCombat&&!NetSession.DeveloperCustomWeaponReadiness
                &&server.RiftlineM7Available,"actual public activation has no developer loadout or capability override");
            var initial=Volatile.Read(ref observed)!;var empty=State(a)!.Value;
            Check(!empty.HasWeapon&&empty.Knowledge.IsEmpty&&empty.State.Magazine==0&&empty.State.Reserve==0
                &&State(b) is {HasWeapon:false},"both native-ready public lives begin without custom ownership");
            var ready=(CustomWeaponReady)Get(firstReady,"Ready");var wire=new CustomReadinessWire(ready.Bootstrap,ready.Slot);
            var expectedCentre=new Vector3(5044,2662,-82087)/4096f;
            Vector3 approach=default;var walk=new Vector3(-.3533436f,0,.9354937f);
            Mutate(ownerScene=>
            {
                var player=ownerScene.Players.Items[slot];PlayerSpawnEntity? spawn=null;
                foreach(var candidate in ownerScene.GetPlayerSpawnEntities())if(candidate.Id==4)spawn=candidate;
                if(spawn==null)throw new InvalidOperationException("Authored native approach spawn4 is missing.");
                var floor=default(CollisionResult);var start=spawn.Position;
                if(!CollisionDetection.CheckBetweenPoints(start+Vector3.UnitY*2,start-Vector3.UnitY*6,TestFlags.Players,ownerScene,ref floor))
                    throw new InvalidOperationException("Authored native approach lacks floor collision.");
                start.Y=floor.Position.Y;approach=start.AddY(.5f);
                player.Reposition(approach,walk,ownerScene.Room!.GetNodeRefByPosition(start));player.ModSetAim(walk);player.Speed=Vector3.Zero;
                if(ownerScene.CustomCombat?.Pickups is not {} ledger||ledger.Layout.Count!=1
                    ||ledger.Layout.Placements[0].Position!=expectedCentre)throw new InvalidOperationException("Production stock placement differs from independent fixture centre.");
            },"declared native spawn4/floor approach placement occurs on simulation owner thread");
            Check((approach-expectedCentre).WithY(0).Length>2,"native approach setup remains outside item contact");

            uint SendInput(object client,Vector3 position,Vector3 aim,CustomResourceKnowledge knowledge=default,
                CustomControlRequest? control=null,bool shoot=false,uint? ackOverride=null)
            {
                var actor=Actor(client);var owner=(WeaponOwnerContext)Get(actor,"Owner");uint frame=Next(client);
                uint ack=ackOverride??(uint)Get(ReferenceEquals(client,first)?a:b,"SnapshotWorld");
                var intent=new IntentPacket{MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,SlotGeneration=owner.Generation,
                    LifeId=owner.Life,Frame=frame,AckFrame=ack,Position=position,Aim=aim,Buttons=IntentButtons.InPlayState|(shoot?IntentButtons.Shoot:0),
                    WeaponSelect=(byte)BeamType.PowerBeam,HasState=true,HasFireEvents=true,HasAnalogMove=true,HasContinuousFireTick=true};
                var native=new byte[CompactIntentCodec.Size(intent)];CompactIntentCodec.Write(intent,native);
                if(knowledge.IsPresent||control.HasValue)
                {
                    var bytes=new byte[CustomInputCarrier.MaxPayload];
                    if(!CustomInputCarrier.TryWrite(ready.Bootstrap,ready.Manifest,knowledge,control,native,bytes,out int length))
                        throw new InvalidOperationException("Actual received knowledge native carrier failed codec.");
                    Send(client,PacketType.CustomWeaponInput,bytes[..length]);
                }
                else Send(client,PacketType.Intent,native);
                return frame;
            }
            void AwaitSource(uint source,string label)=>Wait(()=>Volatile.Read(ref observed) is {HasSource:true} value&&value.Source==source,label);
            void MaintainUntil(Func<bool> condition,string label,int ms=6000)
            {
                var clock=Stopwatch.StartNew();long nextInput=0;
                Wait(()=>
                {
                    if(clock.ElapsedMilliseconds>=nextInput)
                    {
                        // Actual neutral UDP inputs keep ordinary native actors
                        // inside the30-world stale-input budget during ACK loss.
                        SendInput(first,(Vector3)Get(Actor(first),"Position"),walk,State(a)?.Knowledge??default);
                        SendInput(second,(Vector3)Get(Actor(second),"Position"),Vector3.UnitZ);
                        nextInput=clock.ElapsedMilliseconds+100;
                    }
                    return condition();
                },label,ms);
            }
            uint atApproach=SendInput(first,approach,walk);Fresh(second);AwaitSource(atApproach,"actual UDP approach enters native placement and immutable source history");
            Console.WriteLine("ONLINE PICKUP DIAG approach "+Volatile.Read(ref observed)!.Diagnostic);
            Check(Volatile.Read(ref observed) is {Revision:0,HasWeapon:false},"native approach input has not funded the item");
            Check(Volatile.Read(ref observed) is {DisablePowerups:true,TimedCanPickup:false,WeaponCanPickup:true},
                "frozen timed-powerup restriction blocks timed pose while actual ordinary-weapon contact remains allowed");
            oldSource=atApproach;var before=Volatile.Read(ref observed)!;
            var discardField=secondReady.GetType().GetField("DiscardPickupAcks",All)!;
            int withheldBefore=(int)Get(secondReady,"WithheldPickupAcks");discardField.SetValue(secondReady,true);
            // Explicit client-authored positions along the already qualified
            // native approach. This is a UDP contact test, not a physical-key or
            // client-side movement simulation test.
            uint contactSource=0;
            for(int step=1;step<=60&&Volatile.Read(ref observed)!.Revision==0;step++)
            {
                Volatile.Write(ref oldSource,Volatile.Read(ref observed)!.Source);
                var position=approach+walk*(step*.12f);
                uint source=SendInput(first,position,walk);Fresh(second);AwaitSource(source,"actual advancing UDP position reaches item-phase source "+step);
                var diagnostic=Volatile.Read(ref observed)!;
                if(step%10==0||diagnostic.Reaches)
                    Console.WriteLine($"ONLINE PICKUP DIAG step={step} requested={position} roomReady={diagnostic.RoomReady} "+diagnostic.Diagnostic);
                if(Volatile.Read(ref observed)!.Revision>0){contactSource=source;break;}
            }
            Check(contactSource!=0,"actual item phase consumed the authored world station");
            uint visibleSource=SendInput(first,Volatile.Read(ref observed)!.Body,walk);
            Fresh(second);AwaitSource(visibleSource,"next real native source crosses acquisition source+1 without inventing knowledge");
            Wait(()=>Volatile.Read(ref observed) is {Revision:1,HasWeapon:true}
                &&State(a) is {HasWeapon:true,Knowledge.IsPresent:true,State.Magazine:4,State.Reserve:16},"real item contact publishes canonical grant through actual UDP");
            var granted=State(a)!.Value;var acquired=Volatile.Read(ref observed)!;
            Check(contactSource!=0&&acquired.Item.Key.Incarnation==2&&!acquired.Item.Available&&acquired.Item.Revision==1
                &&granted.State.Owner==before.Owner&&granted.EffectiveSource==unchecked(contactSource+1)
                &&granted.GrantWorld==unchecked(acquired.Item.ReadyAt-StockRiftlinePickups.RespawnTicks),
                "single authoritative incarnation consumption names exact owner, source+1 and actual grant world");
            Check(State(b) is {HasWeapon:false,Knowledge.IsEmpty:true}&&acquired.OldSelection==CustomNativeSelection.Native
                &&acquired.OldRetained&&acquired.OldKnowledge.IsEmpty,
                "other UDP life stays empty and retained pre-acquisition source remains native with its original empty knowledge");
            Check(acquired.NativeAccepted==before.NativeAccepted&&acquired.NativeFired==before.NativeFired
                &&acquired.Damage==before.Damage&&acquired.OtherDamage==before.OtherDamage
                &&acquired.Health==before.Health&&acquired.OtherHealth==before.OtherHealth
                &&acquired.Emissions==0&&acquired.Payments==0&&acquired.Contacts==0,
                "world acquisition does not allocate native shots, remove HP or enter custom damage/payment");
            MaintainUntil(()=>publicItems.TryGetValue(slot,out var firstItem)&&firstItem.Item.Revision==1
                &&publicItems.TryGetValue(otherSlot,out var secondItem)&&secondItem.Item.Revision==1
                &&Volatile.Read(ref observed) is {RoomReady:false},"second endpoint durably retains consumed item while withheld application ACK blocks room execution");
            MaintainUntil(()=>(int)Get(secondReady,"WithheldPickupAcks")>=withheldBefore+3,
                "original consumed fact plus at least two identical application retries reach the durable common receiver");
            var retainedAck=(byte[])Get(secondReady,"LastPickupAck");
            Check(CustomPickupReplicaCodec.TryReadAck(retainedAck,out var actualAck)
                &&actualAck.Serial==publicItems[otherSlot].Serial,"withheld ACK is the actual retained second-recipient fact digest and serial");
            var wrongAck=actualAck with{Digest=actualAck.Digest with{A=actualAck.Digest.A^1}};
            var wrongBytes=new byte[CustomPickupReplicaCodec.AckSize];CustomPickupReplicaCodec.Write(wrongAck,wrongBytes);
            Send(second,PacketType.CustomWeaponPickupAck,wrongBytes);
            Send(second,PacketType.CustomWeaponPickupAck,(byte[])Get(firstReady,"LastPickupAck"));
            var rejectClock=Stopwatch.StartNew();
            MaintainUntil(()=>rejectClock.ElapsedMilliseconds>=150,"wrong digest and copied other-recipient ACK traverse actual dispatch");
            Check(Volatile.Read(ref observed) is {RoomReady:false,Revision:1,Payments:0,Contacts:0,Emissions:0} blocked
                &&blocked.Health==before.Health&&blocked.OtherHealth==before.OtherHealth&&blocked.Damage==before.Damage
                &&blocked.OtherDamage==before.OtherDamage&&State(a) is {State.Magazine:4,State.Reserve:16} held
                &&held.Knowledge==granted.Knowledge,"bad ACKs cannot reopen room, duplicate funding or alter canonical ammo/HP");
            discardField.SetValue(secondReady,false);
            // Explicit replay of the exact durably generated ACK; the common
            // probe remains the sole automatic ACK sender for all received facts.
            Send(second,PacketType.CustomWeaponPickupAck,retainedAck);
            MaintainUntil(()=>Volatile.Read(ref observed) is {RoomReady:true},
                "exact retained recipient ACK restores room execution without another collection");
            int withheld=(int)Get(firstReady,"WithheldPickupAcks")+(int)Get(secondReady,"WithheldPickupAcks");
            Check(publicItems.Values.All(value=>value.World==acquired.Item.Key.World&&value.Item.Key.Incarnation==2
                &&!value.Item.Available)&&ItemPackets()>=6&&ItemAcks()+withheld==ItemPackets(),
                "common probe packet counts include every automatic or deliberately withheld ACK without duplicate test consumers");

            // Echo only the stamp actually delivered in the private grant, then
            // author later controls against fresh consumed sources.
            var current=Volatile.Read(ref observed)!;uint echoed=SendInput(first,current.Body,walk,granted.Knowledge);
            Fresh(second);AwaitSource(echoed,"actual UDP input echoes received private funding knowledge");
            var beforeWrongStamp=Volatile.Read(ref observed)!;
            var wrongStampRequest=new CustomControlRequest(ready.Bootstrap,ready.Manifest,ready.Slot,granted.State.Owner.Life,
                new(10),1,CustomWeaponActionKind.Equip,0,default);
            uint wrongStampSource=SendInput(first,beforeWrongStamp.Body,walk,granted.Knowledge,wrongStampRequest);
            Fresh(second);AwaitSource(wrongStampSource,"fresh actual input retains A while accompanying Equip1 deliberately retains empty request knowledge");
            MaintainUntil(()=>((System.Collections.IDictionary)Get(a,"Receipts")).Contains(1u),
                "actual authority returns terminal receipt for mismatched private request knowledge");
            var deniedStamp=(CustomReceiptMessage)((System.Collections.IDictionary)Get(a,"Receipts"))[1u]!;
            MaintainUntil(()=>State(a) is {SettledSequence:1,State.Magazine:4,State.Reserve:16},
                "canonical private projection settles denied nonce1 without an ammo mutation");
            var afterWrongStamp=Volatile.Read(ref observed)!;
            Check(deniedStamp.Receipt.Disposition==CustomJournalDisposition.SourceDenied
                &&deniedStamp.Receipt.Result!=WeaponActionResult.Accepted
                &&deniedStamp.Receipt.Emission==CustomEmissionDisposition.NotAttempted&&deniedStamp.Knowledge.IsEmpty
                &&State(a) is {Selected.IsValid:false,State.Phase:CustomWeaponPhase.Unequipped}
                &&afterWrongStamp.Health==beforeWrongStamp.Health&&afterWrongStamp.OtherHealth==beforeWrongStamp.OtherHealth
                &&afterWrongStamp.Damage==beforeWrongStamp.Damage&&afterWrongStamp.OtherDamage==beforeWrongStamp.OtherDamage
                &&afterWrongStamp.Emissions==0&&afterWrongStamp.Payments==0&&afterWrongStamp.Contacts==0,
                "input knowledge A cannot upgrade an empty-stamped request into Equip or spend ammo/HP");
            int retryBefore=(int)Get(a,"ReceiptPackets");
            var exactRequestBytes=new byte[CustomControlRequest.Size];wrongStampRequest.Write(exactRequestBytes);
            Send(first,PacketType.CustomWeaponControl,exactRequestBytes);
            MaintainUntil(()=>(int)Get(a,"ReceiptPackets")>retryBefore,
                "exact original empty-stamped reliable request traverses UDP and recovers its immutable terminal receipt");
            Check((CustomReceiptMessage)((System.Collections.IDictionary)Get(a,"Receipts"))[1u]! == deniedStamp
                &&Volatile.Read(ref observed) is {Emissions:0,Payments:0,Contacts:0}
                &&State(a) is {SettledSequence:1,State.Magazine:4,State.Reserve:16},
                "denied request retry retains full stamp/token/verdict and cannot equip or repay");
            Call(a,"AckReceipt",1u);
            CustomReceiptMessage Control(uint sequence,CustomWeaponActionKind kind,byte optic=0,bool shoot=false,uint? ackOverride=null)
            {
                var request=new CustomControlRequest(ready.Bootstrap,ready.Manifest,ready.Slot,granted.State.Owner.Life,new(10),sequence,kind,optic,granted.Knowledge);
                var position=(Vector3)Get(Actor(first),"Position");var target=(Vector3)Get(Actor(second),"Position");
                var aim=(target+Vector3.UnitY*.2f-position-Vector3.UnitY*.4f).Normalized();
                uint source=SendInput(first,position,aim,granted.Knowledge,request,shoot,ackOverride);Fresh(second);AwaitSource(source,"fresh knowledge-stamped control source "+sequence);
                Wait(()=>((System.Collections.IDictionary)Get(a,"Receipts")).Contains(sequence),"actual UDP canonical control receipt "+sequence);
                var receipt=(CustomReceiptMessage)((System.Collections.IDictionary)Get(a,"Receipts"))[sequence]!;
                Call(a,"AckReceipt",sequence);return receipt;
            }
            var equip=Control(2,CustomWeaponActionKind.Equip);
            Check(equip.Receipt.Result==WeaponActionResult.Accepted&&equip.Knowledge==granted.Knowledge,
                "actual received acquisition stamp admits a fresh Equip through the ordinary control/source path");
            Mutate(ownerScene=>
            {
                var player=ownerScene.Players.Items[slot];var target=ownerScene.Players.Items[otherSlot];bool clear=false;
                foreach(var direction in new[]{Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitX,-Vector3.UnitZ})
                {
                    var end=player.Position+direction*3;
                    if(BeamProjectileEntity.TryBeamObstacle(player.Position+Vector3.UnitY*.4f,end+Vector3.UnitY*.2f,ownerScene,out _,out _))continue;
                    target.ModPlaceAt(end);target.Speed=Vector3.Zero;clear=true;break;
                }
                if(!clear)throw new InvalidOperationException("Authored pickup approach has no actual clear three-unit shot lane.");
            },"explicit native target-lane setup after real acquisition runs on simulation owner");
            Fresh(second);
            var optic=Control(3,CustomWeaponActionKind.Optic,1);
            Check(optic.Receipt.Result==WeaponActionResult.Accepted&&optic.Knowledge==granted.Knowledge,
                "actual knowledge-stamped source admits optic without additional ownership funding");
            var fire=Control(4,CustomWeaponActionKind.Fire,1,shoot:true);
            Wait(()=>((System.Collections.IDictionary)Get(a,"Contacts")).Contains(4u)
                &&Volatile.Read(ref observed) is {Payments:1,Contacts:1},"fresh received-knowledge fire pays through canonical authority contact");
            var contact=(CustomContactMessage)((System.Collections.IDictionary)Get(a,"Contacts"))[4u]!;
            void FinalDiagnostic(string phase)
            {
                var value=Volatile.Read(ref observed)!;var projected=State(a);
                Console.WriteLine($"ONLINE PICKUP FINAL {phase} result={fire.Receipt.Result} emission={fire.Receipt.Emission} "
                    +$"knowledgeMatches={fire.Knowledge==granted.Knowledge} removed={contact.Outcome.Health.BodyRemoved} "
                    +$"victimHP={value.OtherHealth} victimBefore={before.OtherHealth} privateMag={projected?.State.Magazine} "
                    +$"privateReserve={projected?.State.Reserve} rootMag={value.State.Magazine} rootReserve={value.State.Reserve} "
                    +$"emissions={value.Emissions} contacts={value.Contacts} payments={value.Payments} failures={value.Failures}");
            }
            FinalDiagnostic("contact-arrived");
            MaintainUntil(()=>State(a) is {State.Magazine:3,State.Reserve:16},
                "authoritative spent private projection arrives independently after receipt/contact");
            FinalDiagnostic("private-projection-arrived");
            var final=Volatile.Read(ref observed)!;
            Check(fire.Receipt.Result==WeaponActionResult.Accepted&&fire.Receipt.Emission==CustomEmissionDisposition.Spawned
                &&fire.Knowledge==granted.Knowledge&&contact.Outcome.Health.BodyRemoved==65&&final.OtherHealth==before.OtherHealth-65
                &&State(a) is {State.Magazine:3,State.Reserve:16}&&final.Emissions==1&&final.Failures==0,
                "real pickup-funded M7 fires once, removes canonical65 HP and spends one round without a fabricated certificate");
            Check(final.Revision==1&&final.NativeAccepted==before.NativeAccepted&&final.NativeFired==before.NativeFired,
                "later custom fire neither recollects the station nor allocates a native fire-event identity");
            Call(a,"AckContact",4u);
            Check(granted.GrantWorld>ready.Bootstrap.AuthorityFrame,
                "actual grant world has a preceding native-bootstrap-valid ACK for the backdating rejection fixture");
            var beforeBackdate=Volatile.Read(ref observed)!;
            var backdated=Control(5,CustomWeaponActionKind.Fire,1,shoot:true,ackOverride:granted.GrantWorld-1);
            MaintainUntil(()=>State(a) is {SettledSequence:5,State.Magazine:3,State.Reserve:16},
                "actual private state settles Fire5 refusal without a second debit");
            var afterBackdate=Volatile.Read(ref observed)!;
            Check(backdated.Receipt.Disposition==CustomJournalDisposition.SourceDenied
                &&backdated.Receipt.Result!=WeaponActionResult.Accepted
                &&backdated.Receipt.Emission==CustomEmissionDisposition.NotAttempted&&backdated.Knowledge==granted.Knowledge
                &&backdated.Receipt.Action.AckFrame==granted.GrantWorld-1
                &&afterBackdate.Health==beforeBackdate.Health&&afterBackdate.OtherHealth==beforeBackdate.OtherHealth
                &&afterBackdate.Damage==beforeBackdate.Damage&&afterBackdate.OtherDamage==beforeBackdate.OtherDamage
                &&afterBackdate.Emissions==1&&afterBackdate.Payments==1&&afterBackdate.Contacts==1
                &&!((System.Collections.IDictionary)Get(a,"Contacts")).Contains(5u),
                "valid current knowledge cannot authorize a pre-grant target world or cause another shot/payment/HP removal");
            Console.WriteLine($"PASS {checks} actual online pickup checks.");
            Console.WriteLine("SCOPE: real two-peer UDP, DedicatedServer and ServerSim public empty lives, authored stock item contact, private grant, item-ACK withholding/retries/recovery and knowledge-stamped control. Reflection, manual native loading ACKs, initial native floor/actor setup, authored positional inputs, wrong ACK injection and exact retained ACK replay are explicit fixtures. No custom Grant, funding callback, certificate, private state or source observation is injected.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine("ONLINE PICKUP SCENE FAIL "+e);return 1;}
        finally
        {
            Volatile.Write(ref retiring,1);if(sinkField!=null&&previous!=null)sinkField.SetValue(null,previous);
            (fixture as IDisposable)?.Dispose();NetSession.Stop();MapAudit.ForceEveryone=force;
            NetSession.DeveloperCustomWeaponReadiness=developer;Directory.SetCurrentDirectory(cwd);
        }
    }
}
