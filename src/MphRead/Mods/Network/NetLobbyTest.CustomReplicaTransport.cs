using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
            Console.WriteLine("SCOPE: actual dedicated engine and three admitted UDP peers including genuine initial spectator; automatic recipient baselines, production durable consumer, digest application ACK retries, source-admitted M7 body/head canonical damage, spectator retirement while private results remain outstanding. Native loading acknowledgements, clear-lane placement and health setup are explicit fixtures. Public facts never apply HP; actual native spectator life is printed and compared with the captured row; no rendered client or physical in-flight disconnect timing is claimed.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("CUSTOM REPLICA UDP FAIL "+ex);return 1;}
        finally{NetSession.Stop();Directory.SetCurrentDirectory(directory);}
    }
    private static void CustomReplicaUdpFanout(bool head)
    {
        using var f=new ActionTransportFixture(maxPlayers:3);
        void MaintainInputs(int milliseconds,string label)
        {
            var elapsed=Stopwatch.StartNew();long nextInput=0;
            f.Rig.Wait(()=>
            {
                // Conditions run after the outer client drain has completed.
                // Refresh may wait for actual engine input consumption without
                // reentering a packet callback or enumerating the same drain.
                if(elapsed.ElapsedMilliseconds>=nextInput)
                {
                    f.Refresh(f.First);f.Refresh(f.Second);
                    nextInput=elapsed.ElapsedMilliseconds+100;
                }
                return elapsed.ElapsedMilliseconds>=milliseconds;
            },label,Math.Max(4000,milliseconds+3000));
        }
        var viewer=f.Rig.Add(head?9171u:9170u,initialSpectator:true);
        using var observed=new CustomReadinessProbe(viewer){DiscardReplicaBaselineAcks=true,DiscardReplicaEventAcks=true};
        viewer.Loaded();
        f.Rig.Wait(()=>observed.Ready.HasValue,"genuine initial spectator endpoint receives exact custom manifest without creating custom resources",10000);
        observed.Send();
        f.Rig.Wait(()=>observed.Confirmations>0&&observed.Replica is {IsReady:true},"initial spectator consumes complete real authority baseline before sending its withheld digest ACK",10000);
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        ushort nativeLife=0;bool nativeSpectator=false,nativeHasState=false;
        // Reuse the existing simulation-owner mutation queue for read-only
        // evidence. The test coordination thread never reads live actors.
        var roleRead=(System.Action<Scene>)(scene=>
        {
            nativeLife=NetPlayerLifecycle.Get(viewer.Slot);
            nativeSpectator=NetSession.SlotSpectating[viewer.Slot];
            nativeHasState=scene.CustomCombat!.TryState(scene.Players.Items[viewer.Slot],out _);
        });
        typeof(ActionTransportFixture).GetMethod("Mutate",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(f,new object[] {roleRead,"owner-thread read observes actual native initial spectator role and existing life"});
        bool copied=observed.Replica!.TryCopyBaseline(slots,flights,out var baseline);
        Console.WriteLine($"CUSTOM REPLICA INITIAL SPECTATOR: slot={viewer.Slot} nativeLife={nativeLife} nativeSpectating={nativeSpectator} nativeCustomState={nativeHasState}; copied={copied}; rowOccupied={slots[viewer.Slot].Occupied} rowLife={slots[viewer.Slot].Life} rowSpectating={slots[viewer.Slot].Spectating} rowHasWeapon={slots[viewer.Slot].HasWeapon} rowStateDefault={slots[viewer.Slot].State==default}");
        Check(copied&&nativeSpectator&&!nativeHasState
            &&slots[viewer.Slot] is {Occupied:true,Spectating:true,HasWeapon:false}
            &&slots[viewer.Slot].Life==nativeLife&&slots[viewer.Slot].State==default,
            "public baseline faithfully represents actual native initial spectator life without inventing custom owner/resources");
        Check(f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out bool ready,out _)&&!ready,
            "durable local baseline alone does not open server observer stream until exact application ACK");
        int pages=observed.ReplicaPages;
        MaintainInputs(350,"withheld public baseline ACK drives actual bounded authority page retry with regular actual native input");
        Check(observed.ReplicaPages>pages&&observed.WithheldReplicaAcks>1,
            "baseline retry remains durable and withheld at application boundary");
        observed.DiscardReplicaBaselineAcks=false;
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out bool value,out _)&&value,
            "next exact baseline retry ACK opens only this spectator observer mailbox",6000);
        viewer.Rebind();
        MaintainInputs(80,"genuine initial spectator role persists across Hello rebind");
        f.Refresh(f.Second);
        f.Equip(f.First,freshSources:true);var action=f.Press(f.First,3,head);f.Send(f.First,action);
        f.Rig.Wait(()=>f.A.Receipts.ContainsKey(3)&&f.A.Contacts.ContainsKey(3)
            &&observed.Replica!.DeliveredEvents==1,"actual custom Fire produces independent private result and durably queued spectator launch",6000);
        ulong firstDeliveries=observed.Replica!.DeliveredEvents;int firstPackets=observed.ReplicaEvents,firstWithheld=observed.WithheldReplicaAcks;
        MaintainInputs(350,"withheld spectator launch ACK triggers immutable application retry before terminal delivery");
        Check(observed.ReplicaEvents>firstPackets&&observed.WithheldReplicaAcks>firstWithheld&&observed.Replica.DeliveredEvents==firstDeliveries
            &&observed.Replica.PendingFacts==1,"public launch retry reACKs durable fact without duplicate queue or premature contact delivery");
        observed.DiscardReplicaEventAcks=false;
        f.Rig.Wait(()=>observed.Replica!.DeliveredEvents==2,"exact launch ACK releases retained terminal contact to the same spectator",6000);
        var receipt=f.A.Receipts[3];var contact=f.A.Contacts[3];int damage=head?150:65;
        Check(receipt.Receipt.Emission==CustomEmissionDisposition.Spawned&&receipt.Receipt.After?.Magazine==3
            &&contact.Outcome.Health.BodyRemoved==damage&&contact.Outcome.Contact.Headshot==head,
            "private mandatory payment remains exactly one round and canonical body65/head150");
        Check(observed.Replica.TryCopyState(slots,out _)&&slots[viewer.Slot] is {Spectating:true,HasWeapon:false}&&slots[viewer.Slot].Life==nativeLife,
            "later public state preserves actual native spectator life/role after Hello rebind");
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
        // Keep this 350ms delivery probe inside the native30-world stale-input
        // budget with a real neutral UDP sample; otherwise unrelated native
        // fall damage can resume while the target has no input traffic.
        f.Refresh(f.Second);
        var beforeRetirement=f.Latest;
        static void DumpRetirement(string phase,ActionWorldSnapshot value)
        {
            Console.WriteLine($"CUSTOM REPLICA RETIREMENT {phase}: world={value.World} payments={value.Payments} contacts={value.Contacts} stepFailures={value.StepFailures}; firstHealth={value.First.Health} firstDamage={value.First.DamageId} firstOwner={value.First.Owner} firstPosition={value.First.Position} firstInput={value.First.InputFrame}; secondHealth={value.Second.Health} secondDamage={value.Second.DamageId} secondOwner={value.Second.Owner} secondPosition={value.Second.Position} secondInput={value.Second.InputFrame}");
        }
        DumpRetirement("before",beforeRetirement);
        viewer.Send(PacketType.Bye,Array.Empty<byte>());f.Rig.Clients.Remove(viewer);viewer.Transport.Dispose();
        f.Rig.Wait(()=>!f.Rig.Server.TryGetCustomReplicaReady(viewer.Slot,out _,out _),
            "real spectator Bye retires exact observer mailbox before private results are acknowledged");
        f.Send(f.First,action);
        MaintainInputs(350,"old owner action retries after spectator retirement through real server ingress");
        var afterRetirement=f.Latest;
        DumpRetirement("after",afterRetirement);
        Console.WriteLine($"CUSTOM REPLICA RETIREMENT PREDICATES: receiptEqual={f.A.Receipts[3]==receipt} contactEqual={f.A.Contacts[3]==contact} healthExpected={200-damage} healthEqual={afterRetirement.Second.Health==200-damage} paymentsEqual={afterRetirement.Payments==1} contactsEqual={afterRetirement.Contacts==1}; receiptBeforeToken={receipt.Token} receiptAfterToken={f.A.Receipts[3].Token} receiptBeforeWorld={receipt.Receipt.AuthorityFrame} receiptAfterWorld={f.A.Receipts[3].Receipt.AuthorityFrame}; contactBeforeToken={contact.Token} contactAfterToken={f.A.Contacts[3].Token} contactBeforeWorld={contact.Outcome.AuthorityFrame} contactAfterWorld={f.A.Contacts[3].Outcome.AuthorityFrame}");
        Check(f.A.Receipts[3]==receipt&&f.A.Contacts[3]==contact&&f.Latest.Second.Health==200-damage
            &&f.Latest.Payments==1&&f.Latest.Contacts==1,
            "observer disconnect neither discards nor repays outstanding mandatory private receipt/contact");
        f.A.AckReceipt(3);f.A.AckContact(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,
            "remaining owner independently retires original private payment obligations");
        Check(f.Latest.StepFailures==0,"replica baseline/fanout/retry/retirement produces no simulation exceptions");
    }
}
