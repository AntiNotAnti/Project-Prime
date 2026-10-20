using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    public static int RunCustomPublicAvailability(string data)
    {
        string directory=Directory.GetCurrentDirectory();
        string dataRoot=Path.GetFullPath(data);
        bool developer=NetSession.DeveloperCustomWeaponReadiness;
        _checks=0; NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness=false;
        try
        {
            Directory.SetCurrentDirectory(dataRoot); Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
            CustomPublicLobby(); CustomPublicBots(); CustomPublicNativeScene();
            CustomPublicRuntimeClient(false); CustomPublicRuntimeClient(true); CustomPublicReadinessTimeout();
            CustomPublicLateJoinControls(); CustomPublicLateJoinActions();
            // Preserve the existing independent explicit developer override check.
            CustomReadinessRuntimeClient();
            int configurationChecks=_checks;
            Check(RunCustomActionTransport(dataRoot,publicAvailability:true)==0,
                "complete source/action UDP engine acceptance passes with public availability and no developer flags");
            int actionChecks=_checks;
            Check(RunCustomControlTransport(dataRoot,publicAvailability:true)==0,
                "complete reliable control UDP engine acceptance passes with public availability and no developer flags");
            Console.WriteLine($"PASS public availability: {configurationChecks} configuration/client, {actionChecks} action, {_checks} control checks.");
            Console.WriteLine("SCOPE: real server API/lobby-owner UDP configuration, native-only engine absence, actual NetSession automatic public capability handshake and explicit developer override, declared readiness-clock aging, real stalled late-peer admission with prompt strict/control denials consumed by production client producer and recovery after readiness, and full dedicated action/control damage/respawn/loss suites using the frozen public option. Producer deadline checks advance only its detached clock. Remote native scene-load acknowledgements are manual; control-plane client baselines are synthetic. Engine lane/health/protection/respawn setup remains explicit. No rendered launcher, remote engine client or forced already-bound race is claimed.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine("CUSTOM PUBLIC AVAILABILITY UDP FAIL "+ex); return 1; }
        finally { NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness=developer; Directory.SetCurrentDirectory(directory); }
    }

    private static void CustomPublicLobby()
    {
        using var rig=new Rig(publicAvailability:true);
        var owner=rig.Add(9280); var guest=rig.Add(9281);
        Check(owner.State!.Value.Match.RiftlineM7Available&&guest.State!.Value.Match.RiftlineM7Available
            &&!rig.Server.DeveloperCustomWeaponReadiness&&!rig.Server.DeveloperCustomWeaponCombat
            &&!NetSession.DeveloperCustomWeaponReadiness,
            "public server API seeds synchronized Battle availability while all developer flags remain false");
        var disabled=owner.State.Value; disabled.Match=disabled.Match with { RiftlineM7Available=false };
        rig.Expect(guest,guest.Command(LobbyCommandType.UpdateMatch,config:disabled),LobbyResultCode.NotOwner);
        rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:disabled),LobbyResultCode.Ok);
        Check(!owner.State!.Value.Match.RiftlineM7Available&&!guest.State!.Value.Match.RiftlineM7Available,
            "lobby owner can disable public availability before the frozen round");
        var enabled=owner.State.Value; enabled.Match=enabled.Match with { RiftlineM7Available=true };
        rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:enabled),LobbyResultCode.Ok);
        foreach(var match in new[] {
            enabled.Match with { Mode=GameMode.BattleTeams },
            enabled.Match with { InstaGib=true }, enabled.Match with { NoImperialist=true },
            enabled.Match with { BalancedMode=true }, enabled.Match with { Mode=GameMode.OneInTheChamber } })
        {
            var invalid=owner.State!.Value; invalid.Match=match;
            rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:invalid),LobbyResultCode.InvalidConfiguration);
        }
        using var a=new CustomReadinessProbe(owner); using var b=new CustomReadinessProbe(guest);
        StartCustomReadinessMatch(rig,owner,guest);
        rig.Wait(()=>a.Ready.HasValue&&b.Ready.HasValue,"public frozen match publishes both endpoint-owned custom manifests");
        rig.Server.RiftlineM7Available=false;
        a.Send(); b.Send();
        rig.Wait(()=>a.Confirmations>0&&b.Confirmations>0&&rig.Server.TryGetCustomWeaponReady(owner.Slot,out _)
            &&rig.Server.TryGetCustomWeaponReady(guest.Slot,out _),
            "changing mutable server default cannot disable the already frozen public round");
        Check(owner.State!.Value.Match.RiftlineM7Available&&guest.State!.Value.Match.RiftlineM7Available,
            "current round continues advertising the frozen public option");
        rig.Expect(owner,owner.Command(LobbyCommandType.AddBot),LobbyResultCode.InvalidConfiguration);
    }

    private static void CustomPublicBots()
    {
        using var rig=new Rig(); var owner=rig.Add(9282);
        rig.Expect(owner,owner.Command(LobbyCommandType.AddBot),LobbyResultCode.Ok);
        byte bot=owner.Roster.Slots[Enumerable.Range(0,owner.Roster.Count).Single(owner.Roster.IsBot)];
        var enabled=owner.State!.Value; enabled.Match=enabled.Match with { RiftlineM7Available=true };
        rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:enabled),LobbyResultCode.InvalidConfiguration);
        Check(!owner.State!.Value.Match.RiftlineM7Available&&owner.Roster.IsBot(Array.IndexOf(owner.Roster.Slots,bot)),
            "refused public enable preserves native bot roster and prior native rules");
        rig.Expect(owner,owner.Command(LobbyCommandType.RemoveBot,target:bot),LobbyResultCode.Ok);
        enabled=owner.State!.Value; enabled.Match=enabled.Match with { RiftlineM7Available=true };
        rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:enabled),LobbyResultCode.Ok);
        rig.Expect(owner,owner.Command(LobbyCommandType.AddBot),LobbyResultCode.InvalidConfiguration);
        Check(owner.Roster.Count==1&&owner.State!.Value.Match.RiftlineM7Available,
            "public availability refuses new online bots without changing human-only rules");
    }

    private static void CustomPublicNativeScene()
    {
        using var rig=new Rig(simulate:true,room:"MP3 PROVING GROUND",maxPlayers:1);
        var client=rig.Add(9283); using var probe=new CustomReadinessProbe(client);
        StartCustomReadinessMatch(rig,client);
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var sim=(ServerSim)typeof(DedicatedServer).GetField("_sim",hidden)!.GetValue(rig.Server)!;
        rig.Wait(()=>sim.EvidenceScene!=null,"native-only dedicated engine scene exists");
        ObserveCustomReadiness(rig,350,"native-only actual engine traverses readiness and simulation phases");
        Check(!rig.Server.RiftlineM7Available&&!client.State!.Value.Match.RiftlineM7Available
            &&!rig.Server.DeveloperCustomWeaponReadiness&&!rig.Server.DeveloperCustomWeaponCombat
            &&probe.Pages==0&&!rig.Server.TryGetCustomWeaponReady(client.Slot,out _)
            &&sim.EvidenceScene!.CustomCombat==null&&sim.EvidenceScene.CustomWeapons==null&&sim.EvidenceScene.OfflineWeapons==null,
            "native-only default allocates no custom engine runtime, registry, manifest or capability");
        Check(sim.StepFailures==0,"native-only quiet custom path leaves actual engine healthy");
    }

    private static void CustomPublicRuntimeClient(bool publicAvailability)
    {
        NetSession.Stop(); NetSession.DeveloperCustomWeaponReadiness=false;
        try
        {
            using var rig=new Rig(publicAvailability:publicAvailability);
            Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"PublicM7Runtime",Hunter.Samus),
                "actual runtime client connects without developer readiness override");
            int slot=NetSession.LocalSlot;
            void Until(Func<bool> condition,string label)=>rig.Wait(()=>{NetSession.Pump();return condition();},label,6000);
            Until(()=>NetSession.LocalIsLobbyOwner&&GameState.Nicknames[slot]=="PublicM7Runtime"
                &&NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"public runtime lobby identity converges");
            Check(!NetSession.CustomWeaponReadinessConfirmed,"public runtime does not activate capability in lobby");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true),"public runtime sends native ready");
            Until(()=>!NetSession.LobbyCommandPending&&NetSession.SlotLobbyReady[slot]
                &&NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"public runtime native ready settles");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.StartMatch),"public runtime requests match start");
            Until(()=>NetSession.IsStarting&&!NetSession.LobbyCommandPending,"public runtime receives native load barrier");
            NetSession.MarkMatchLoaded();
            Until(()=>NetSession.WorldAdmissionConfirmed,"manual native application releases actual runtime WorldReady handshake");
            if(publicAvailability)
            {
                Until(()=>NetSession.CustomWeaponReadinessConfirmed&&rig.Server.TryGetCustomWeaponReady(slot,out _),
                    "frozen public option automatically activates actual runtime manifest/ready/confirmation with developer flag false");
                Check(!NetSession.DeveloperCustomWeaponReadiness&&!rig.Server.DeveloperCustomWeaponReadiness
                    &&!rig.Server.DeveloperCustomWeaponCombat&&NetSession.ConfirmedWorldBootstrap is { } bootstrap
                    &&rig.Server.TryGetCustomWeaponReady(slot,out var evidence)&&evidence.Bootstrap==bootstrap,
                    "automatic public runtime capability belongs to the exact native bootstrap with every developer flag off");
                var transport=(NetTransport)typeof(NetSession).GetField("_transport",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var contradictory=new CustomReadinessWire(NetSession.ConfirmedWorldBootstrap!.Value,(byte)slot).ReadyBytes.ToArray();
                contradictory[33]^=1;
                transport.Send(new IPEndPoint(IPAddress.Loopback,rig.Server.BoundPort),PacketType.CustomWeaponReady,contradictory);
                Until(()=>NetSession.Refused&&NetSession.RefusedReason.Reason==RefusedPacket.ReasonCustomWeaponReadiness
                    &&rig.Server.PeerCount==0&&!rig.Server.TryGetCustomWeaponReady(slot,out _),
                    "authenticated contradictory readiness removes admitted endpoint and reaches actual runtime reason-seven refusal handler");
                Check(NetSession.RefusedReason.Describe("Server")=="Riftline M7 readiness failed or timed out. Rejoin with a compatible build.",
                    "actual admitted runtime exposes the precise public readiness failure for its existing launcher teardown owner");
            }
            else
            {
                var elapsed=Stopwatch.StartNew(); Until(()=>elapsed.ElapsedMilliseconds>=350,"native runtime remains quiet across manifest retry interval");
                Check(!NetSession.CustomWeaponReadinessConfirmed&&!rig.Server.TryGetCustomWeaponReady(slot,out _),
                    "native-only actual runtime does not opt in or acquire custom capability");
            }
        }
        finally { NetSession.Stop(); }
        Check(NetSession.Role==NetRole.Offline&&!NetSession.CustomWeaponReadinessConfirmed,
            "existing owner stop disconnects actual runtime and retires public capability");
    }

    private static void CustomPublicReadinessTimeout()
    {
        using var rig=new Rig(publicAvailability:true); var client=rig.Add(9284);
        using var probe=new CustomReadinessProbe(client);
        StartCustomReadinessMatch(rig,client);
        rig.Wait(()=>probe.Ready.HasValue,"public incompatible-client fixture receives manifest but deliberately sends no readiness");
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(Array)typeof(DedicatedServer).GetField("_customReadyEntries",hidden)!.GetValue(rig.Server)!;
        object entry=entries.GetValue(client.Slot)!;
        var state=(CustomReadinessPeer)entry.GetType().GetProperty("State")!.GetValue(entry)!;
        // Declared clock fixture only: retain the genuine endpoint/bootstrap,
        // offer and absence of readiness; age the deadline instead of sleeping 30s.
        double serverNow=(double)typeof(DedicatedServer).GetField("_now",hidden)!.GetValue(rig.Server)!;
        typeof(CustomReadinessPeer).GetField("_created",hidden)!.SetValue(state,serverNow-CustomReadinessPeer.DeadlineSeconds-1);
        rig.Wait(()=>client.Refusal is { Reason:RefusedPacket.ReasonCustomWeaponReadiness },
            "public manifest deadline sends typed custom readiness refusal to actual incompatible UDP peer");
        rig.Wait(()=>rig.Server.PeerCount==0&&!rig.Server.TryGetCustomWeaponReady(client.Slot,out _),
            "timed-out public endpoint is removed and retains no custom capability");
    }

    // A detached production client producer consumes actual result/state bytes.
    // Transport ACK ownership stays with the existing probes so reservations
    // cannot disappear before their test assertions inspect them.
    private sealed class CustomPublicProducerProbe : IDisposable
    {
        private readonly Client _client;
        private readonly Action<ReceivedPacket>? _previous;
        internal readonly CustomClientCombat Combat;
        internal CustomPublicProducerProbe(Client client,CustomWeaponReady ready)
        {
            _client=client; _previous=client.PacketReceived;
            Combat=new(new CustomReadinessWire(ready.Bootstrap,ready.Slot));
            client.PacketReceived=packet=>
            {
                _previous?.Invoke(packet);
                if(packet.Type is PacketType.CustomWeaponState or PacketType.CustomWeaponReceipt
                    or PacketType.CustomWeaponControlDenied or PacketType.CustomWeaponContact)
                    Check(Combat.Receive(packet.Type,packet.Payload,out _),
                        "production detached client producer consumes exact actual public state/result bytes");
            };
        }
        public void Dispose()=>_client.PacketReceived=_previous;
    }

    private static void CompleteCustomPublicLateJoin(Rig rig,Client late,CustomReadinessProbe probe)
    {
        late.Loaded();
        rig.Wait(()=>probe.Ready.HasValue,"late actual endpoint manually acknowledges native scene and receives its custom manifest",15000);
        probe.Send();
        rig.Wait(()=>probe.Confirmations>0&&rig.Server.TryGetCustomWeaponReady(late.Slot,out _)
            &&rig.Server.TryGetCustomReplicaReady(late.Slot,out bool ready,out _)&&ready
            &&probe.Pickups?.IsReady==true&&probe.PickupAcks>0,
            "late endpoint resolves custom readiness and durably acknowledges its production replica baseline",10000);
    }

    private static void CustomPublicLateJoinControls()
    {
        using var f=new ControlTransportFixture(publicAvailability:true,maxPlayers:3); f.Equip();
        using var producer=new CustomPublicProducerProbe(f.Engine.First,f.Engine.FirstReady.Ready!.Value);
        f.Engine.Rig.Wait(()=>producer.Combat.State is { SettledSequence:2 },
            "real public initial state reaches detached production control producer after equip and optic");
        var late=f.Engine.Rig.Add(9285); using var ready=new CustomReadinessProbe(late);
        Check(!ready.Ready.HasValue,"late UDP peer deliberately stalls before native scene and custom readiness");
        foreach(var kind in new[] {CustomWeaponActionKind.Fire,CustomWeaponActionKind.Equip})
        {
            Check(producer.Combat.TrySubmitControl(kind,kind==CustomWeaponActionKind.Fire?(byte)1:(byte)0,0,out var request),
                "production producer assigns next public control sequence while late peer is stalled");
            uint expected=kind==CustomWeaponActionKind.Fire?3u:4u;
            Check(request.Sequence==expected,"terminal late-join denial advances producer sequence without a life reset");
            f.Reserve(request); uint source=f.Fresh();
            f.Engine.Rig.Wait(()=>f.A.HasDenial(request),"stalled late-peer control receives prompt typed terminal instead of waiting for room readiness",3000);
            var denied=f.A.Denial(request);
            Check(denied.Reason==CustomControlDenialReason.SourceUnavailable&&!f.A.HasReceipt(request)
                &&!f.A.HasContact(request)&&f.Engine.Latest.First.Source.Input.Frame==source
                &&f.Engine.Latest.First.State is {Magazine:4,Reserve:16}&&f.Engine.Latest.Second.Health==200
                &&f.Engine.Latest.Contacts==0&&f.Engine.Latest.Payments==0,
                "fresh actual source during stalled JIP settles no-source control without ammo, health, launch or payment");
            Check(producer.Combat.LastControlDenied?.Request==request&&!producer.Combat.Invalidated,
                "production producer consumes matching actual terminal and preserves readiness for the next action");
            f.A.Ack(denied.Ack); f.WaitPending(0,"late-join denial exact ACK releases its bounded result reservation");
        }
        double afterDeadline=CustomActionOutbox.DeadlineSeconds+1;
        Check(!producer.Combat.TryNextControl(afterDeadline,out _)&&!producer.Combat.Invalidated,
            "prompt terminal controls leave no request that can invalidate production producer beyond its fifteen-second deadline");
        CompleteCustomPublicLateJoin(f.Engine.Rig,late,ready);
        Check(producer.Combat.TrySubmitControl(CustomWeaponActionKind.Fire,1,afterDeadline,out var accepted)&&accepted.Sequence==5,
            "same production control producer submits next sequence after late peer becomes ready");
        f.Reserve(accepted); f.Fresh(); var receipt=f.Settle(accepted);
        f.Engine.Rig.Wait(()=>f.A.HasContact(accepted)&&f.Engine.Latest.Second.Health==135,
            "same unretired public journal fires normally after late peer completes readiness",6000);
        Check(receipt.Receipt.Result==WeaponActionResult.Accepted&&receipt.Receipt.After is {Magazine:3,Reserve:16}
            &&f.Engine.Latest.Payments==1&&f.Engine.Latest.StepFailures==0&&!producer.Combat.Invalidated,
            "post-JIP recovery pays one round and one canonical sixty-five HP effect without producer or journal retirement");
        f.A.Ack(f.A.Contact(accepted).Ack); f.WaitPending(0,"post-JIP control contact ACK releases final result reservation");
    }

    private static void CustomPublicLateJoinActions()
    {
        using var f=new ActionTransportFixture(maxPlayers:3,publicAvailability:true); f.Equip(f.First);
        using var producer=new CustomPublicProducerProbe(f.First,f.FirstReady.Ready!.Value);
        f.Rig.Wait(()=>producer.Combat.State is { SettledSequence:2 },
            "real public initial state reaches detached strict producer after equip and optic");
        var late=f.Rig.Add(9286); using var ready=new CustomReadinessProbe(late);
        Check(!ready.Ready.HasValue,"strict-action late peer is genuinely admitted but deliberately unready");
        for(uint sequence=3;sequence<=4;sequence++)
        {
            CustomWeaponAction action;
            if(sequence==3) action=f.Press(f.First,sequence);
            else { f.Refresh(f.First); action=f.Action(f.First,sequence,CustomWeaponActionKind.Equip); }
            Check(producer.Combat.Submit(action,0),"production strict producer accepts the next real observed-source action during stalled JIP");
            f.Send(f.First,action);
            f.Rig.Wait(()=>f.A.Receipts.ContainsKey(sequence),"stalled late-peer strict action gets prompt terminal instead of hanging an admitted journal head",3000);
            var receipt=f.A.Receipts[sequence];
            Check(receipt.Receipt.Disposition==CustomJournalDisposition.SourceDenied
                &&receipt.Receipt.Emission==CustomEmissionDisposition.NotAttempted
                &&receipt.Receipt.Before==null&&receipt.Receipt.After==null&&!f.A.Contacts.ContainsKey(sequence)
                &&f.Latest.First.State is {Magazine:4,Reserve:16}&&f.Latest.Second.Health==200
                &&f.Latest.Contacts==0&&f.Latest.Payments==0,
                "strict JIP denial precedes resource payment, launch or victim mutation");
            Check(producer.Combat.LastReceipt?.Receipt.Action==action&&!producer.Combat.Invalidated,
                "production strict producer consumes actual denial without a pending sequence gap");
            f.A.AckReceipt(sequence);
            f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,
                "strict JIP denial exact ACK releases result capacity");
        }
        double afterDeadline=CustomActionOutbox.DeadlineSeconds+1;
        Check(!producer.Combat.TryNext(afterDeadline,out _)&&!producer.Combat.Invalidated,
            "strict producer remains valid past request deadline after both actual terminal denials");
        CompleteCustomPublicLateJoin(f.Rig,late,ready);
        f.Refresh(f.First); var fire=f.Press(f.First,5);
        Check(producer.Combat.Submit(fire,afterDeadline),"same strict producer advances to sequence five after JIP readiness");
        var accepted=f.Settle(f.First,fire);
        f.Rig.Wait(()=>f.A.Contacts.ContainsKey(5)&&f.Latest.Second.Health==135,
            "same strict journal recovers a real canonical hit after stalled endpoint completes readiness",6000);
        Check(accepted.Receipt.Result==WeaponActionResult.Accepted&&accepted.Receipt.After is {Magazine:3,Reserve:16}
            &&f.Latest.Payments==1&&f.Latest.StepFailures==0&&!producer.Combat.Invalidated,
            "strict post-JIP recovery has one round, one payment and no source journal retirement");
        f.A.AckReceipt(5); f.A.AckContact(5);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,
            "strict recovery receipt/contact ACKs release final obligations");
    }
}
