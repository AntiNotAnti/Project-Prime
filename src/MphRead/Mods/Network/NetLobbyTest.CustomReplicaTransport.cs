using System;
using System.IO;
using System.Linq;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    public static int RunCustomReplicaTransport(string data)
    {
        string directory=Directory.GetCurrentDirectory();_checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            CustomReplicaUdpFanout(false);CustomReplicaUdpFanout(true);
            Console.WriteLine($"PASS {_checks} real UDP/engine custom replica transport checks.");
            Console.WriteLine("SCOPE: actual dedicated engine and three admitted UDP peers including genuine initial Life0 spectator; automatic recipient baselines, production durable consumer, digest application ACK retries, source-admitted M7 body/head canonical damage, spectator retirement while private results remain outstanding. Native loading acknowledgements, clear-lane placement and health setup are explicit fixtures. Public facts never apply HP; no rendered client or physical in-flight disconnect timing is claimed.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("CUSTOM REPLICA UDP FAIL "+ex);return 1;}
        finally{NetSession.Stop();Directory.SetCurrentDirectory(directory);}
    }
    private static void CustomReplicaUdpFanout(bool head)
    {
        using var f=new ActionTransportFixture(maxPlayers:3);
        var viewer=f.Rig.Add(head?9171u:9170u,initialSpectator:true);
        using var observed=new CustomReadinessProbe(viewer){DiscardReplicaBaselineAcks=true,DiscardReplicaEventAcks=true};
        viewer.Loaded();
        f.Rig.Wait(()=>observed.Ready.HasValue,"genuine spectator endpoint receives exact custom manifest without creating a combat life",10000);
        observed.Send();
        f.Rig.Wait(()=>observed.Confirmations>0&&observed.Replica is {IsReady:true},"Life0 spectator consumes complete real authority baseline before sending its withheld digest ACK",10000);
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        Check(observed.Replica!.TryCopyBaseline(slots,flights,out var baseline)
            &&slots[viewer.Slot] is {Occupied:true,Spectating:true,Life:0,HasWeapon:false}
            &&slots[viewer.Slot].State==default,"public baseline represents genuine spectator incarnation without invented owner, resources or initial-life grant");
        Check(f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out bool ready,out _)&&!ready,
            "durable local baseline alone does not open server observer stream until exact application ACK");
        int pages=observed.ReplicaPages;
        ObserveCustomReadiness(f.Rig,350,"withheld public baseline ACK drives actual bounded authority page retry");
        Check(observed.ReplicaPages>pages&&observed.WithheldReplicaAcks>1,
            "baseline retry remains durable and withheld at application boundary");
        observed.DiscardReplicaBaselineAcks=false;
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out bool value,out _)&&value,
            "next exact baseline retry ACK opens only this spectator observer mailbox",6000);
        viewer.Rebind();
        ObserveCustomReadiness(f.Rig,80,"genuine initial spectator role persists across Hello rebind");
        f.Equip(f.First,freshSources:true);var action=f.Press(f.First,3,head);f.Send(f.First,action);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(3)&&f.A.Contacts.ContainsKey(3)
            &&observed.Replica!.DeliveredEvents==1,"actual custom Fire produces independent private result and durably queued spectator launch",6000);
        ulong firstDeliveries=observed.Replica!.DeliveredEvents;int firstPackets=observed.ReplicaEvents;
        ObserveCustomReadiness(f.Rig,350,"withheld spectator launch ACK triggers immutable application retry before terminal delivery");
        Check(observed.ReplicaEvents>firstPackets&&observed.Replica.DeliveredEvents==firstDeliveries
            &&observed.Replica.PendingFacts==1,"public launch retry reACKs durable fact without duplicate queue or premature contact delivery");
        observed.DiscardReplicaEventAcks=false;
        f.Rig.Wait(()=>observed.Replica!.DeliveredEvents==2,"exact launch ACK releases retained terminal contact to the same spectator",6000);
        var receipt=f.A.Receipts[3];var contact=f.A.Contacts[3];int damage=head?150:65;
        Check(receipt.Receipt.Emission==CustomEmissionDisposition.Spawned&&receipt.Receipt.After?.Magazine==3
            &&contact.Outcome.Health.BodyRemoved==damage&&contact.Outcome.Contact.Headshot==head,
            "private mandatory payment remains exactly one round and canonical body65/head150");
        Check(observed.Replica.TryCopyState(slots,out _)&&slots[viewer.Slot] is {Life:0,Spectating:true,HasWeapon:false},
            "later public state preserves unspawned spectator role after Hello rebind");
        Check(observed.Replica.TryTakeFact(out var launch)&&launch.Kind==CustomReplicaKind.Launch
            &&launch.Launch.Identity==CustomActionIdentity.For(action)&&launch.Launch.Source==receipt.Receipt.Source,
            "spectator launch retains original firing bootstrap and immutable admitted source");
        Check(observed.Replica.TryTakeFact(out var terminal)&&terminal.Kind==CustomReplicaKind.Contact
            &&terminal.Contact.Outcome==contact.Outcome&&terminal.Contact.Bootstrap==action.Bootstrap,
            "spectator terminal carries same full victim/action and canonical health outcome as private owner result");
        Check(f.Latest.Second.Health==200-damage&&f.Latest.Payments==1&&f.Latest.Contacts==1&&!f.Latest.Second.NativeHistory,
            "public consumer facts leave actual native HP/counter ownership exclusively at canonical authority seam");
        Check(observed.Replica.PendingFacts==0&&f.Latest.Second.Health==200-damage&&f.Latest.Payments==1,
            "draining public presentation facts causes no second canonical health payment");
        // Result ownership remains mandatory while the optional observer retires.
        viewer.Send(PacketType.Bye,Array.Empty<byte>());f.Rig.Clients.Remove(viewer);viewer.Transport.Dispose();
        f.Rig.Wait(()=>!f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out _,out _),
            "real spectator Bye retires exact observer mailbox before private results are acknowledged");
        f.Send(f.First,action);
        ObserveCustomReadiness(f.Rig,350,"old owner action retries after spectator retirement through real server ingress");
        Check(f.A.Receipts[3]==receipt&&f.A.Contacts[3]==contact&&f.Latest.Second.Health==200-damage
            &&f.Latest.Payments==1&&f.Latest.Contacts==1,
            "observer disconnect neither discards nor repays outstanding mandatory private receipt/contact");
        f.A.AckReceipt(3);f.A.AckContact(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,
            "remaining owner independently retires original private payment obligations");
        Check(f.Latest.StepFailures==0,"replica baseline/fanout/retry/retirement produces no simulation exceptions");
    }
}
