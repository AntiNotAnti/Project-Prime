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
            Check(NetSession.LastCustomWeaponReceipt==null && NetSession.LastCustomControlDenied==null && NetSession.CustomWeaponPresentationState==null,
                "full client stop releases custom result and projection owners");
            Console.WriteLine($"PASS {_checks} actual NetSession custom result/publisher UDP checks.");
            Console.WriteLine("SCOPE: actual client transport/readiness/result handlers, action publishing, application-vs-transport acceptance, dedupe/revision/poison and native health isolation. Native lifecycle/bootstrap and authority result facts are synthetic; real combat is exercised separately by --custom-action-udp.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM RUNTIME UDP FAIL "+ex); return 1; }
        finally { NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness=enabled; }
    }
}
