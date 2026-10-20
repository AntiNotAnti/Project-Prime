using System;
using System.Net;
using System.Reflection;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Actual NetSession client/socket/handlers, with explicit synthetic native
    // life and authority result facts. This does not simulate a remote engine.
    public static int RunCustomRuntimeResults()
    {
        _checks=0; NetSession.Stop(); bool enabled=NetSession.DeveloperCustomWeaponReadiness;
        NetSession.DeveloperCustomWeaponReadiness=true;
        try
        {
            using var rig=new Rig(customReadiness:true);
            Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"CustomResultRuntime",Hunter.Samus),
                "actual result runtime client connects through native UDP admission");
            int slot=NetSession.LocalSlot;
            void Until(Func<bool> condition,string label)
                => rig.Wait(()=>{ NetSession.Pump(); return condition(); },label,6000);
            Until(()=>NetSession.LocalIsLobbyOwner && GameState.Nicknames[slot]=="CustomResultRuntime"
                && NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"runtime native lobby identity converges");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true),"runtime sends lobby ready");
            Until(()=>!NetSession.LobbyCommandPending && NetSession.SlotLobbyReady[slot]
                && NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"runtime lobby readiness converges");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.StartMatch),"runtime starts synthetic native bootstrap fixture");
            Until(()=>NetSession.IsStarting && !NetSession.LobbyCommandPending,"runtime receives native scene-load barrier");
            NetSession.MarkMatchLoaded();
            Until(()=>NetSession.WorldAdmissionConfirmed && NetSession.CustomWeaponReadinessConfirmed,
                "actual client readiness handler completes exact custom capability exchange");
            var bootstrap=NetSession.ConfirmedWorldBootstrap!.Value;
            var owner=new WeaponOwnerContext(bootstrap.Start.MatchId,bootstrap.Start.AuthorityEpoch,(byte)slot,bootstrap.SlotGeneration,1);
            var native=new PlayerState { SlotIndex=(byte)slot,SlotGeneration=owner.Generation,LifeId=1,Health=100,
                Flags=PlayerState.FlagActive|PlayerState.FlagSpawned,Facing=Vector3.UnitZ };
            Check(NetPlayerLifecycle.AcceptState(native,bootstrap.AuthorityFrame+1),"explicit synthetic native lifecycle establishes client result recipient");
            var transport=(NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(rig.Server)!;
            var address=new IPEndPoint(IPAddress.Loopback,NetSession.ConnectionPort);
            var definition=RiftlineM7.Create(); var manifest=new CustomWeaponManifest(new[] { definition });
            var snapshot=new WeaponStateSnapshot(owner,definition.Identity,100,CustomWeaponPhase.Unequipped,4,16,0,0,0,0,0);
            var initial=new CustomStateMessage(bootstrap,manifest.Digest,new(10),bootstrap.AuthorityFrame+1,1,0,default,100,snapshot);
            byte[] state=new byte[CustomStateMessage.Size]; CustomCombatResults.Write(initial,state,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponState,state),"synthetic authority state enters actual reliable transport");
            Until(()=>NetSession.CustomWeaponPresentationState==initial,"actual client handler consumes frozen-manifest initial4/16 presentation state");
            ushort damage=NetDamage.Sequence(slot);
            var action=new CustomWeaponAction(bootstrap,manifest.Digest,(byte)slot,1,new(10),1,100,0,0,
                CustomWeaponActionKind.Equip,0,Vector3.Zero,Vector3.Zero);
            Check(!NetSession.CustomWeaponReplicaReady && !NetSession.SubmitCustomWeaponAction(action),
                "private initial state alone cannot publish custom actions before durable public baseline commitment");
            // Explicit synthetic PUBLIC authority table sent by the actual
            // admitted server socket. This proves client consumption, not a
            // server engine capture or source-admission oracle.
            var publicWire=new CustomReadinessWire(bootstrap,(byte)slot);
            var publicSlots=new CustomReplicaSlot[8];var publicFlights=new CustomReplicaFlight[64];
            for(byte i=0;i<8;i++)publicSlots[i]=new(i,false,0,0,false,false,default,0,0,default);
            publicSlots[slot]=new((byte)slot,true,owner.Generation,1,false,true,default,0,100,snapshot);
            var publicPage=new CustomReplicaPage(1,0,bootstrap.AuthorityFrame+1,0,0);
            byte[] publicBaseline=new byte[publicPage.Length];
            Check(CustomReplicaCodec.TryWritePage(publicWire,publicPage,publicSlots,publicFlights,publicBaseline,out _)
                && transport.TrySendSemantic(address,PacketType.CustomWeaponReplicaPage,publicBaseline),
                "explicit synthetic public baseline enters the authenticated actual authority UDP transport");
            Until(()=>NetSession.CustomWeaponReplicaReady,"actual NetSession production consumer durably commits public baseline before publishing");
            var copiedPublic=new CustomReplicaSlot[8];var copiedFlights=new CustomReplicaFlight[64];
            Check(NetSession.TryCopyCustomReplicaBaseline(copiedPublic,copiedFlights,out var copiedCapture)
                &&copiedCapture.World==publicPage.World&&copiedPublic[slot].State==snapshot,
                "runtime exposes original committed public baseline through bounded value copy");
            Check(NetSession.SubmitCustomWeaponAction(action),"actual client publisher retains one contiguous full-identity action");
            Check(!NetSession.SubmitCustomWeaponAction(action with { Sequence=3 }),"actual client publisher rejects a skipped action sequence");
            var pendingClock=System.Diagnostics.Stopwatch.StartNew();
            Until(()=>pendingClock.ElapsedMilliseconds>=300,"actual client action/retry pump runs through native transport ACKs without an application verdict");
            Check(NetSession.LastCustomWeaponReceipt==null && NetSession.CustomWeaponPresentationState==initial,
                "transport ACK alone never becomes an accepted action or new inventory state");
            var after=snapshot with { Phase=CustomWeaponPhase.Ready };
            var source=new CustomSourceCertificate(action,bootstrap.AuthorityFrame+1,bootstrap.AuthorityFrame+1,0,
                Vector3.Zero,Vector3.UnitZ,true,false,false);
            var receipt=new CustomReceiptMessage(11,new(action,bootstrap.AuthorityFrame+2,CustomJournalDisposition.Accepted,
                WeaponActionResult.Accepted,CustomEmissionDisposition.NotAttempted,source,snapshot,after));
            byte[] result=new byte[CustomReceiptMessage.Size]; CustomCombatResults.Write(receipt,result,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReceipt,result),"synthetic terminal result enters genuine host connection");
            Until(()=>NetSession.LastCustomWeaponReceipt==receipt,"actual runtime handler validates and consumes exact retained action result");
            Check(NetSession.CustomWeaponPresentationState==initial && NetDamage.Sequence(slot)==damage,
                "receipt consumption does not mutate canonical native health/history or impersonate a state projection");
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReceipt,result),"authority repeats identical application result");
            var duplicateClock=System.Diagnostics.Stopwatch.StartNew();
            Until(()=>duplicateClock.ElapsedMilliseconds>=100,"duplicate result traverses actual runtime dedupe and application ACK path");
            Check(NetSession.LastCustomWeaponReceipt==receipt && NetDamage.Sequence(slot)==damage,
                "duplicate runtime result is idempotent and cannot apply native damage");
            var projected=initial with { AuthorityFrame=bootstrap.AuthorityFrame+2,Revision=2,SettledSequence=1,Selected=new(10),State=after };
            CustomCombatResults.Write(projected,state,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponState,state),"new authority projection enters host transport");
            Until(()=>NetSession.CustomWeaponPresentationState==projected,"actual runtime cache advances only from newer authoritative projection");
            CustomCombatResults.Write(initial,state,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponState,state),"old projection arrives after current revision");
            var oldClock=System.Diagnostics.Stopwatch.StartNew();
            Until(()=>oldClock.ElapsedMilliseconds>=100,"old state passes actual runtime revision fence");
            Check(NetSession.CustomWeaponPresentationState==projected && NetSession.CustomWeaponReadinessConfirmed,
                "older state cannot roll back current magazine/equip projection");
            var publicAction=new CustomWeaponAction(bootstrap,manifest.Digest,(byte)slot,1,new(10),80,100,
                bootstrap.AuthorityFrame+2,0,CustomWeaponActionKind.Fire,1,new(0,.5f,0),Vector3.UnitZ);
            var publicSource=new CustomSourceCertificate(publicAction,bootstrap.AuthorityFrame+2,bootstrap.AuthorityFrame+2,0,
                Vector3.Zero,Vector3.UnitZ,true,false,false);
            var publicLaunch=new CustomReplicaEvent(CustomReplicaKind.Launch,1,bootstrap.AuthorityFrame+2,
                new(publicSource,bootstrap.AuthorityFrame+2,new(0,.4f,0),Vector3.UnitZ),default);
            var publicOutcome=new CustomContactOutcome(new(new(CustomActionIdentity.For(publicAction),
                new((byte)slot,owner.Generation,1),1,CustomImpactChannel.Body),Vector3.UnitZ,false),
                bootstrap.AuthorityFrame+3,new(100,35,0,0,9));
            var publicContact=new CustomReplicaEvent(CustomReplicaKind.Contact,2,bootstrap.AuthorityFrame+3,default,
                new(bootstrap,2,publicOutcome));
            byte[] launchPacket=new byte[1024],contactPacket=new byte[1024];
            int launchLength=0,contactLength=0;
            Check(CustomReplicaCodec.TryWriteEvent(publicWire,publicLaunch,launchPacket,out launchLength)
                &&CustomReplicaCodec.TryWriteEvent(publicWire,publicContact,contactPacket,out contactLength),
                "synthetic public launch/contact retain exact source and victim identities without a health command");
            // Deliberately invoke the queued-packet adapter boundary directly.
            // This is handler identity fencing, not a claim of UDP authentication.
            var publicConsumer=(CustomReplicaConsumer)typeof(NetSession).GetField("_customReplicaConsumer",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var handleReplica=typeof(NetSession).GetMethod("HandleCustomReplica",BindingFlags.Static|BindingFlags.NonPublic)!;
            byte[] queued=new byte[launchLength+1];queued[0]=(byte)PacketType.CustomWeaponReplicaEvent;
            launchPacket.AsSpan(0,launchLength).CopyTo(queued.AsSpan(1));
            ulong connection=transport.ConnectionStats(address)!.Value.ConnectionId;
            var host=new IPEndPoint(IPAddress.Loopback,rig.Server.BoundPort);
            handleReplica.Invoke(null,new object[] {new ReceivedPacket(host,queued,queued.Length,connectionId:0)});
            handleReplica.Invoke(null,new object[] {new ReceivedPacket(host,queued,queued.Length,connectionId:connection==1?2:connection^1)});
            Check(publicConsumer.DeliveredEvents==0&&publicConsumer.PendingFacts==0&&NetSession.CustomWeaponReplicaReady,
                "queued zero/old connection packets from same endpoint are refused before production consumer mutation (direct adapter invocation)");
            var nativeHealthBefore=NetSession.RemoteStates[slot].Health;
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReplicaEvent,launchPacket.AsSpan(0,launchLength))
                &&transport.TrySendSemantic(address,PacketType.CustomWeaponReplicaEvent,contactPacket.AsSpan(0,contactLength)),
                "synthetic public semantic facts traverse authenticated actual host transport");
            Until(()=>publicConsumer.PendingFacts==2,"actual client durably queues independent public launch/contact before application drain");
            Check(NetSession.RemoteStates[slot].Health==nativeHealthBefore&&NetDamage.Sequence(slot)==damage
                &&NetSession.CustomWeaponPresentationState==projected,
                "received public contact health values cannot mutate native HP/damage counter or private inventory projection");
            var publicCurrent=publicSlots[slot];publicSlots[slot]=publicCurrent with {Selected=new(10),SettledSequence=80,
                State=after with {Magazine=3,LastAcceptedShotId=80}};
            byte[] publicState=new byte[1024];
            Check(CustomReplicaCodec.TryWriteState(publicWire,bootstrap.AuthorityFrame+3,publicSlots,publicState,out int publicLength,1)
                &&transport.TrySendSemantic(address,PacketType.CustomWeaponReplicaEvent,publicState.AsSpan(0,publicLength)),
                "independent canonical public state table enters real client handler");
            Until(()=>NetSession.TryCopyCustomReplicaState(copiedPublic,out _)&&copiedPublic[slot].State.Magazine==3,
                "runtime public table advances independently of private owner state");
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReplicaPage,publicBaseline),
                "original public baseline retries after state and durable contact");
            var publicRetry=System.Diagnostics.Stopwatch.StartNew();Until(()=>publicRetry.ElapsedMilliseconds>=100,
                "duplicate public baseline traverses actual production client adapter");
            Check(NetSession.TryCopyCustomReplicaState(copiedPublic,out uint publicWorld)&&publicWorld==bootstrap.AuthorityFrame+3
                &&copiedPublic[slot].State.Magazine==3&&publicConsumer.PendingFacts==2
                &&NetSession.CustomWeaponPresentationState==projected&&NetSession.RemoteStates[slot].Health==nativeHealthBefore,
                "late baseline cannot restore old public resources, erase queued contact or roll back private/native state");
            Check(NetSession.TryTakeCustomReplicaFact(out var drainedLaunch)&&drainedLaunch==publicLaunch
                &&NetSession.TryTakeCustomReplicaFact(out var drainedContact)&&drainedContact==publicContact
                &&!NetSession.TryTakeCustomReplicaFact(out _),"actual runtime drains durable public launch/contact exactly once in FIFO order");
            native.LifeId=2;
            Check(NetPlayerLifecycle.AcceptState(native,bootstrap.AuthorityFrame+3),"synthetic genuine next native life precedes control publisher reset");
            var nextOwner=new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,2);
            var nextSnapshot=snapshot with { Owner=nextOwner,Tick=101 };
            var nextState=initial with { AuthorityFrame=bootstrap.AuthorityFrame+3,Revision=3,State=nextSnapshot,CommittedSource=101 };
            CustomCombatResults.Write(nextState,state,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponState,state),"new-life projection traverses actual client handler");
            Until(()=>NetSession.CustomWeaponPresentationState==nextState,"canonical next life resets source-action publisher mode");
            var control=new CustomControlRequest(bootstrap,manifest.Digest,(byte)slot,2,new(10),1,CustomWeaponActionKind.Equip,0);
            Check(NetSession.SubmitCustomWeaponControl(control) && !NetSession.SubmitCustomWeaponControl(control with { Sequence=3 }),
                "real client next life publishes reliable primary control and refuses sequence gap");
            // Exercise the exact producer boundary used by SendIntent. These
            // native bytes/lifecycle are synthetic; encoding proves neither
            // server source consumption nor engine pose/control admission.
            var carrierIntent=new IntentPacket { MatchId=nextOwner.MatchId,AuthorityEpoch=nextOwner.AuthorityEpoch,
                SlotGeneration=nextOwner.Generation,LifeId=nextOwner.Life,Frame=400,AckFrame=bootstrap.AuthorityFrame+3,
                Position=new(1,2,3),Aim=Vector3.UnitZ,Buttons=IntentButtons.InPlayState,HasFireEvents=true,
                NativeTouch=new(true,true,true,12,-8,77,false) };
            byte[] carrierNative=new byte[CompactIntentCodec.Size(carrierIntent)];
            CompactIntentCodec.Write(carrierIntent,carrierNative);
            byte[] nativeBefore=new byte[PlayerState.Size],nativeAfter=new byte[PlayerState.Size];
            NetSession.RemoteStates[slot].Write(nativeBefore);
            Check(NetSession.TryEncodeCustomWeaponInput(carrierIntent,carrierNative,out var borrowedCarrier)
                && CustomInputCarrier.TryRead(borrowedCarrier.Span,out var embeddedControl,out var decodedCarrier)
                && embeddedControl==control && decodedCarrier.MatchId==nextOwner.MatchId
                && decodedCarrier.AuthorityEpoch==nextOwner.AuthorityEpoch && decodedCarrier.SlotGeneration==nextOwner.Generation
                && decodedCarrier.LifeId==2 && decodedCarrier.Frame==carrierIntent.Frame
                && decodedCarrier.NativeTouch==carrierIntent.NativeTouch,
                "actual client input encoder automatically embeds oldest pending full-identity next-life control");
            Check(borrowedCarrier.Span[CustomInputCarrier.PrefixSize..].SequenceEqual(carrierNative)
                && borrowedCarrier.Length+NetHeader.Size<=1200 && (byte)PacketType.CustomWeaponInput==93
                && !NetReliableChannel.IsReliable(PacketType.CustomWeaponInput)
                && NetPacketQueue.Priority(PacketType.CustomWeaponInput)==NetPacketPriority.Realtime
                && NetWireTelemetry.Classify(PacketType.CustomWeaponInput)==NetTrafficKind.Intent,
                "actual producer preserves exact compact native bytes inside an unreliable realtime input datagram bounded to1200bytes");
            var fullCarrier=carrierIntent; fullCarrier.FireEventCount=NetFireEvents.LiveCapacity;
            for (int index=0;index<NetFireEvents.LiveCapacity;index++)
                fullCarrier.FireEvents[index]=new((uint)(index+1),400u-(uint)(NetFireEvents.LiveCapacity-1-index),
                    carrierIntent.AckFrame,0,FireEventKind.PressFire,(byte)BeamType.PowerBeam,0,0,
                    FireEvent.FlagPose,Vector3.Zero,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector2.Zero,SourceLife:2);
            byte[] fullNative=new byte[CompactIntentCodec.Size(fullCarrier)];
            CompactIntentCodec.Write(fullCarrier,fullNative); byte[] fullOriginal=fullNative.AsSpan().ToArray();
            Check(CompactIntentCodec.TryRead(fullNative,out var fullDecoded) && fullDecoded.FireEventCount==14
                && fullDecoded.NativeTouch.Active && fullDecoded.NativeTouch.Suppressed
                && fullNative.Length+NetHeader.Size<=1200
                && fullNative.Length+CustomInputCarrier.PrefixSize+NetHeader.Size>1200,
                "valid fourteen-event native frame fits native MTU with existing touch-mode suppression but cannot fit custom envelope");
            Check(!NetSession.TryEncodeCustomWeaponInput(fullCarrier,fullNative,out var noCarrier) && noCarrier.IsEmpty
                && fullNative.AsSpan().SequenceEqual(fullOriginal) && fullCarrier.FireEventCount==14,
                "actual client encoder returns absent wrapper for full native frame without trimming or mutating fallback bytes");
            bool everyNativeEvent=true;
            for (int index=0;index<NetFireEvents.LiveCapacity;index++)
                everyNativeEvent &= fullDecoded.FireEvents[index]==fullCarrier.FireEvents[index];
            Check(everyNativeEvent && CompactIntentCodec.TryRead(fullNative,out var fallback) && fallback.FireEventCount==14,
                "ordinary native fallback retains every exact native event and its source-life identity");
            Check(NetSession.TryEncodeCustomWeaponInput(carrierIntent,carrierNative,out borrowedCarrier)
                && CustomInputCarrier.TryRead(borrowedCarrier.Span,out var stillPending,out _) && stillPending==control,
                "full-frame fallback leaves reliable primary control pending for the next fitting native carrier");
            NetSession.RemoteStates[slot].Write(nativeAfter);
            Check(nativeBefore.AsSpan().SequenceEqual(nativeAfter) && NetPlayerLifecycle.Get(slot)==2
                && NetDamage.Sequence(slot)==damage && NetSession.CustomWeaponPresentationState==nextState
                && NetSession.LastCustomWeaponReceipt==null && NetSession.LastCustomControlDenied==null,
                "client input production and fallback mutate neither native state/health history nor canonical custom projection");
            Check(!NetSession.SubmitCustomWeaponAction(action with { Life=2 }),"real client cannot mix publisher modes inside the new life");
            var controlPump=System.Diagnostics.Stopwatch.StartNew();
            Until(()=>controlPump.ElapsedMilliseconds>=300,"actual primary control pump retries through transport acknowledgements");
            Check(NetSession.LastCustomWeaponReceipt==null && NetSession.LastCustomControlDenied==null,
                "primary control transport ACK supplies neither canonical action nor a no-source verdict");
            var bound=action with { Life=2,SourceFrame=200 };
            var boundBefore=nextSnapshot with { Tick=200 };
            var boundAfter=boundBefore with { Phase=CustomWeaponPhase.Ready };
            var boundSource=source with { Action=bound,ObservedWorldFrame=bootstrap.AuthorityFrame+4,TargetWorldFrame=bootstrap.AuthorityFrame+4 };
            var boundReceipt=new CustomReceiptMessage(12,new(bound,bootstrap.AuthorityFrame+4,CustomJournalDisposition.Accepted,
                WeaponActionResult.Accepted,CustomEmissionDisposition.NotAttempted,boundSource,boundBefore,boundAfter));
            CustomCombatResults.Write(boundReceipt,result,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReceipt,result),"canonical server-selected source response traverses actual host socket");
            Until(()=>NetSession.LastCustomWeaponReceipt==boundReceipt,"request handler accepts canonical Action with authority-selected source frame");
            Check(NetSession.CustomWeaponPresentationState==nextState && NetDamage.Sequence(slot)==damage,
                "bound primary response changes no native damage or projected inventory");
            var unavailable=control with { Sequence=2,Kind=CustomWeaponActionKind.Fire,Optic=1 };
            Check(NetSession.SubmitCustomWeaponControl(unavailable),"next contiguous reliable primary control is retained");
            var noSource=new CustomControlDenied(unavailable,13,bootstrap.AuthorityFrame+5,CustomControlDenialReason.SourceUnavailable);
            byte[] denied=new byte[CustomControlDenied.Size]; noSource.Write(denied);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponControlDenied,denied),"typed no-source response travels through real reliable host channel");
            Until(()=>NetSession.LastCustomControlDenied==noSource,"actual handler consumes exact no-source request and returns application ACK");
            Check(NetSession.CustomWeaponReadinessConfirmed && NetSession.CustomWeaponPresentationState==nextState
                && NetDamage.Sequence(slot)==damage,"no-source terminal retains native health and canonical projection");
            var contradictory=receipt with { Receipt=receipt.Receipt with { Disposition=CustomJournalDisposition.ResourceDenied,
                Result=WeaponActionResult.Unequipped } };
            CustomCombatResults.Write(contradictory,result,manifest,definition);
            Check(transport.TrySendSemantic(address,PacketType.CustomWeaponReceipt,result),"contradictory but structurally valid token reaches actual host receiver");
            Until(()=>!NetSession.CustomWeaponReadinessConfirmed,"same result token with changed payload permanently invalidates custom client cache");
            Check(NetSession.WorldAdmissionConfirmed && NetSession.IsClient && NetDamage.Sequence(slot)==damage
                && !NetSession.SubmitCustomWeaponAction(action with { Sequence=2 }),
                "custom cache refusal preserves native admission/health and blocks new custom requests");
            NetSession.Stop();
            Check(publicConsumer.Closed&&!NetSession.CustomWeaponReplicaReady&&!NetSession.TryTakeCustomReplicaFact(out _),
                "full client stop eagerly closes held public consumer and exposes no queued old-incarnation facts");
            Check(NetSession.LastCustomWeaponReceipt==null && NetSession.LastCustomControlDenied==null && NetSession.CustomWeaponPresentationState==null,
                "full client stop releases custom result and projection owners");
            Console.WriteLine($"PASS {_checks} actual NetSession custom result/publisher UDP checks.");
            Console.WriteLine("SCOPE: actual client transport/readiness/result handlers, action publishing, application-vs-transport acceptance, dedupe/revision/poison and native health isolation. Native lifecycle/bootstrap, public baseline/events and authority result facts are synthetic; queued connection refusal also uses explicit adapter invocation; real combat is exercised separately by --custom-action-udp.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM RUNTIME UDP FAIL "+ex); return 1; }
        finally { NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness=enabled; }
    }
}
