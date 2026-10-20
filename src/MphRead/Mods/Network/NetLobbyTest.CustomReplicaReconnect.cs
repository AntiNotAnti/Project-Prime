using System;
using System.IO;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    public static int RunCustomReplicaReconnect(string data)
    {
        string directory=Directory.GetCurrentDirectory();_checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            CustomReplicaUdpReconnect();
            Console.WriteLine($"PASS {_checks} real UDP/engine custom spectator reincarnation checks.");
            Console.WriteLine("SCOPE: actual dedicated engine, same-client-id new socket/spectator generation/bootstrap, old exact consumption ACKs replayed over the new authenticated connection, immutable outstanding owner payment, current baseline and later canonical fire. Native load ACKs, actor placement and health are explicit fixtures. No old physical flight or rendered client is fabricated.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("CUSTOM REPLICA RECONNECT FAIL "+ex);return 1;}
        finally{NetSession.Stop();Directory.SetCurrentDirectory(directory);}
    }
    private static void CustomReplicaUdpReconnect()
    {
        using var f=new ActionTransportFixture(maxPlayers:3);
        var oldViewer=f.Rig.Add(9180,initialSpectator:true);
        using var oldProbe=new CustomReadinessProbe(oldViewer){DiscardReplicaEventAcks=true};
        oldViewer.Loaded();
        f.Rig.Wait(()=>oldProbe.Ready.HasValue,"original spectator receives exact native/custom bootstrap",10000);
        oldProbe.Send();
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomReplicaReady(oldViewer.Slot,out bool ready,out _)&&ready,
            "original spectator opens only after complete baseline application ACK",6000);
        var oldWire=oldProbe.ReplicaWire!;
        byte[] oldBaseline=oldProbe.LastReplicaBaselineAck!;
        ulong oldConnection=oldViewer.Transport.ConnectionStats(oldViewer.Server)!.Value.ConnectionId;
        ushort oldGeneration=oldWire.Bootstrap.SlotGeneration;
        f.Refresh(f.Second);f.Equip(f.First,freshSources:true);
        var first=f.Press(f.First,3);f.Send(f.First,first);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(3)&&f.A.Contacts.ContainsKey(3)&&oldProbe.Replica?.DeliveredEvents==1,
            "first actual custom shot is paid while old observer withholds its launch ACK");
        var receipt=f.A.Receipts[3];var contact=f.A.Contacts[3];
        byte[] oldLaunch=oldProbe.LastReplicaEventAck!;
        int oldReceiptPackets=f.A.ReceiptPackets,oldContactPackets=f.A.ContactPackets;
        Check(CustomReplicaCodec.TryReadAck(oldWire,oldLaunch,out var oldAck)&&!oldAck.Baseline&&oldAck.Id==1
            &&receipt.Receipt.After is {Magazine:3}&&contact.Outcome.Health.BodyRemoved==65,
            "captured original event ACK is actual token1 for one genuine canonical round/contact");
        int slot=oldViewer.Slot;
        oldViewer.Send(PacketType.Bye,Array.Empty<byte>());f.Rig.Clients.Remove(oldViewer);oldViewer.Transport.Dispose();
        f.Rig.Wait(()=>!f.Rig.Server.TryGetCustomReplicaReady(slot,out _,out _),"original observer mailbox retires before slot reuse");
        var viewer=f.Rig.Add(9180,initialSpectator:true);
        using var probe=new CustomReadinessProbe(viewer){DiscardReplicaBaselineAcks=true,DiscardReplicaEventAcks=true};
        viewer.Loaded();
        f.Rig.Wait(()=>probe.Ready.HasValue,"same client id on a new socket receives its replacement bootstrap",10000);
        probe.Send();
        f.Rig.Wait(()=>probe.Replica is {IsReady:true},"replacement consumer durably assembles current authority baseline",6000);
        var wire=probe.ReplicaWire!;
        ulong connection=viewer.Transport.ConnectionStats(viewer.Server)!.Value.ConnectionId;
        Check(viewer.Slot==slot&&connection!=oldConnection&&wire.Bootstrap.SlotGeneration!=oldGeneration
            &&wire.Bootstrap!=oldWire.Bootstrap&&wire.Bootstrap.Start==oldWire.Bootstrap.Start,
            "real reconnect changes transport, occupant generation and recipient bootstrap within the same match start");
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        Check(probe.Replica!.TryCopyBaseline(slots,flights,out var baseline)&&baseline.FlightCount==0
            &&slots[slot] is {Occupied:true,Spectating:true,Life:0,HasWeapon:false}
            &&slots[f.First.Slot].State.Magazine==3&&probe.Replica.DeliveredEvents==0,
            "new Life0 viewer gets current resources without resurrecting retired observer facts or expired physical flight");
        viewer.Send(PacketType.CustomWeaponReplicaAck,oldBaseline);
        MaintainActionInputs(f,100,"old baseline ACK traverses actual new authenticated connection");
        Check(f.Rig.Server.TryGetCustomReplicaReady(slot,out bool readyAfterOld,out _)&&!readyAfterOld,
            "old exact baseline digest cannot open the replacement recipient even from its own new connection");
        probe.DiscardReplicaBaselineAcks=false;
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomReplicaReady(slot,out bool ready,out _)&&ready,
            "replacement exact baseline ACK opens its own mailbox",6000);
        Check(!oldBaseline.AsSpan().SequenceEqual(probe.LastReplicaBaselineAck)
            &&probe.Replica.DeliveredEvents==0&&f.A.Receipts[3]==receipt&&f.A.Contacts[3]==contact
            &&f.A.ReceiptPackets>oldReceiptPackets&&f.A.ContactPackets>oldContactPackets
            &&f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int originalPending)&&originalPending==1
            &&f.Latest.Payments==1&&f.Latest.Contacts==1,
            "new baseline is independent while original owner retains exact prior payment/result");
        f.A.AckReceipt(3);f.A.AckContact(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int count)&&count==0,
            "owner independently acknowledges old mandatory result after spectator reconnect");
        var waiting=System.Diagnostics.Stopwatch.StartNew();
        while(f.Latest.First.State.Phase==CustomWeaponPhase.BoltCycling)
        {
            if(waiting.ElapsedMilliseconds>5000)throw new InvalidOperationException("Actual source-driven bolt cooldown did not complete.");
            MaintainActionInputs(f,100,"fresh actual native inputs advance the original bounded source clock");
        }
        f.Refresh(f.First);var second=f.Press(f.First,4);f.Send(f.First,second);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(4)&&f.A.Contacts.ContainsKey(4)&&probe.Replica.DeliveredEvents==1,
            "later real Fire reaches only the current observer incarnation");
        byte[] newLaunch=probe.LastReplicaEventAck!;
        Check(CustomReplicaCodec.TryReadAck(wire,newLaunch,out var newAck)&&newAck.Id==oldAck.Id
            &&!oldLaunch.AsSpan().SequenceEqual(newLaunch),
            "new recipient may reuse token1 but its exact source/recipient digest differs");
        viewer.Send(PacketType.CustomWeaponReplicaAck,oldLaunch);
        MaintainActionInputs(f,100,"old launch ACK is replayed through actual current authenticated transport");
        Check(probe.Replica.DeliveredEvents==1&&f.Rig.Server.TryGetCustomReplicaReady(slot,out bool currentReady,out int pending)
            &&currentReady&&pending==1,"old same-number event ACK cannot release current observer terminal");
        probe.DiscardReplicaEventAcks=false;
        f.Rig.Wait(()=>probe.Replica.DeliveredEvents==2,"exact current launch ACK releases only current terminal",6000);
        Check(probe.Replica.TryTakeFact(out var launch)&&launch.Launch.Identity==CustomActionIdentity.For(second)
            &&probe.Replica.TryTakeFact(out var terminal)&&terminal.Contact.Outcome==f.A.Contacts[4].Outcome
            &&terminal.Contact.Bootstrap==second.Bootstrap&&f.A.Receipts[4].Receipt.After is {Magazine:2}
            &&f.Latest.Second.Health==70&&f.Latest.Payments==2&&f.Latest.Contacts==2&&!f.Latest.Second.NativeHistory,
            "replacement delivers exact later firing bootstrap and canonical65 damage once without old fact replay or native history alias");
        f.A.AckReceipt(4);f.A.AckContact(4);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int count)&&count==0,
            "new observer ACK ownership does not replace mandatory second private result ACK");
        Check(f.Latest.StepFailures==0,"actual reconnect and old ACK replay cause no simulation exceptions");
    }
}
