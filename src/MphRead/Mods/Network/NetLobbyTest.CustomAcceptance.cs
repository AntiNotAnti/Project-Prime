using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Real ordinary client carriers keep fixture actors under the same native
    // stale-input budget as a human continuously sending gameplay input.
    private static void WaitForActionInputs(ActionTransportFixture fixture,Func<bool> condition,string label,int timeout=8000)
    {
        var clock=Stopwatch.StartNew();long nextInput=0;
        fixture.Rig.Wait(()=>
        {
            if(condition())return true;
            if(clock.ElapsedMilliseconds>=nextInput)
            {
                fixture.Refresh(fixture.First);fixture.Refresh(fixture.Second);
                nextInput=clock.ElapsedMilliseconds+100;
            }
            return condition();
        },label,timeout);
    }
    private static void MaintainActionInputs(ActionTransportFixture fixture,int milliseconds,string label)
    {
        var clock=Stopwatch.StartNew();
        WaitForActionInputs(fixture,()=>clock.ElapsedMilliseconds>=milliseconds,label,Math.Max(4000,milliseconds+3000));
    }
    public static int RunCustomAuthorityAcceptance(string data)
    {
        string directory=Directory.GetCurrentDirectory();_checks=0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            CustomAcceptanceRejectedRay(false);CustomAcceptanceRejectedRay(true);CustomAcceptanceCooldownReload();
            CustomAcceptanceOwnerSymmetry(false);CustomAcceptanceOwnerSymmetry(true);
            Console.WriteLine($"PASS {_checks} real UDP/engine custom authority acceptance checks.");
            Console.WriteLine("SCOPE: actual dedicated authority, ordinary endpoint-owned clients, genuine input source capture, terminal aim/muzzle denial, cooldown and source-driven reload, token-owning/non-owning slot symmetry, and client-only160ms RTT/10% seeded datagram loss. Native loading acknowledgements, actor placement and initial health are explicit fixtures. No forged source certificates, legacy-host reassignment, bot/local custom admission, rendered client or quantitative latency fairness is claimed.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("CUSTOM AUTHORITY ACCEPTANCE UDP FAIL "+ex);return 1;}
        finally{NetSession.Stop();Directory.SetCurrentDirectory(directory);}
    }
    private static void CustomAcceptanceRejectedRay(bool muzzle)
    {
        using var f=new ActionTransportFixture();f.Refresh(f.Second);f.Equip(f.First,freshSources:true);
        f.Refresh(f.First);var valid=f.Press(f.First,3);
        var rejected=muzzle ? valid with {Origin=valid.Origin+Vector3.UnitX*.25f}
            : valid with {Direction=Vector3.Cross(valid.Direction,Vector3.UnitY).Normalized()};
        Check(rejected.IsValid,"ray disagreement fixture retains a structurally valid full-identity action");
        f.Send(f.First,rejected);
        WaitForActionInputs(f,()=>f.A.Receipts.ContainsKey(3),"actual source-referenced ray disagreement receives terminal application denial");
        var denied=f.A.Receipts[3];
        Check(denied.Receipt.Action==rejected&&denied.Receipt.Disposition==CustomJournalDisposition.SourceDenied
            &&denied.Receipt.Emission==CustomEmissionDisposition.NotAttempted&&denied.Receipt.Source==null
            &&denied.Receipt.Before==null&&denied.Receipt.After==null,
            "untrusted aim/muzzle disagreement cannot mint a canonical source or resource transition");
        f.Send(f.First,valid);f.Send(f.First,rejected);
        MaintainActionInputs(f,350,"corrected same-nonce ray and exact denied retry traverse real ingress with regular native input");
        Check(f.A.Receipts[3]==denied&&!f.A.Contacts.ContainsKey(3)&&f.Latest.First.State.Magazine==4
            &&f.Latest.Second.Health==200&&f.Latest.Payments==0&&f.Latest.Contacts==0
            &&f.FirstReady.Replica?.DeliveredEvents==0&&f.SecondReady.Replica?.DeliveredEvents==0,
            "terminal denial cannot be promoted by corrected same identity, charged, launched publicly or paid later");
        f.A.AckReceipt(3);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,
            "terminal rejected ray retires only after exact private application acknowledgement");
        f.Refresh(f.First);var next=f.Press(f.First,4);f.Send(f.First,next);
        WaitForActionInputs(f,()=>f.A.Receipts.ContainsKey(4)&&f.A.Contacts.ContainsKey(4)&&f.Latest.Payments==1&&f.Latest.Second.Health==135,"next fresh contiguous valid action remains admissible after source denial");
        Check(f.A.Receipts[4].Receipt.Result==WeaponActionResult.Accepted&&f.A.Receipts[4].Receipt.After is {Magazine:3}
            &&f.A.Contacts[4].Outcome.Health.BodyRemoved==65&&f.Latest.Second.Health==135&&f.Latest.Payments==1&&f.Latest.Contacts==1
            &&!f.Latest.Second.NativeHistory,"fresh nonce pays exactly one custom round and canonical65HP after bad ray cannot be revised");
        f.A.AckReceipt(4);f.A.AckContact(4);
        f.Rig.Wait(()=>f.Rig.Server.TryGetCustomActionPending(f.First.Slot,out int pending)&&pending==0,"valid recovery action releases full private result obligations");
        Check(f.Latest.StepFailures==0,"ray admission denial/recovery leaves real simulation healthy");
        Console.WriteLine("CUSTOM ACCEPTANCE RAY: "+(muzzle?"muzzle":"aim")+" terminal denied identity; valid next sequence recovered");
    }
    private static void CustomAcceptanceCooldownReload()
    {
        using var f=new ControlTransportFixture();f.Equip();var engine=f.Engine;
        CustomReceiptMessage Settle(uint sequence,CustomWeaponActionKind kind,byte optic=0,bool contact=false)
        {
            var request=f.Request(sequence,kind,optic);ulong beforePayments=engine.Latest.Payments;f.Reserve(request);
            WaitForActionInputs(engine,()=>f.A.HasReceipt(request)&&(!contact||f.A.Contacts.ContainsKey(ControlUdpProbe.Key(request))&&engine.Latest.Payments==beforePayments+1),
                "primary control binds a genuinely consumed fresh native sample and receives canonical verdict");
            var receipt=f.Settle(request,acknowledge:false);f.A.Ack(receipt.Ack);
            if(contact)f.A.Ack(f.A.Contacts[ControlUdpProbe.Key(request)].Ack);
            f.WaitPending(0,"exact receipt/contact application acknowledgements release complete control reservation");
            return receipt;
        }
        var fired=Settle(3,CustomWeaponActionKind.Fire,1,contact:true);
        Check(fired.Receipt.Result==WeaponActionResult.Accepted&&fired.Receipt.After is {Magazine:3}
            &&engine.Latest.Second.Health==135&&engine.Latest.Payments==1,"actual first control shot establishes canonical cadence and one round debit");
        uint ready=fired.Receipt.After!.Value.FireReadyTick;
        var early=Settle(4,CustomWeaponActionKind.Fire,1);
        Check(early.Receipt.Action.SourceFrame<ready&&early.Receipt.Result==WeaponActionResult.Cooldown
            &&early.Receipt.Emission==CustomEmissionDisposition.NotAttempted&&early.Receipt.Before?.Magazine==3&&early.Receipt.After?.Magazine==3
            &&engine.Latest.Second.Health==135&&engine.Latest.Payments==1&&engine.Latest.Contacts==1,
            "real fresh input during bolt deadline returns terminal cooldown without refund, emission or damage");
        var reload=Settle(5,CustomWeaponActionKind.Reload);
        Check(reload.Receipt.Action.SourceFrame<ready&&reload.Receipt.Result==WeaponActionResult.Accepted
            &&reload.Receipt.After is {Phase:CustomWeaponPhase.Reloading,Optic:0,Magazine:3,Reserve:16},
            "canonical reload is allowed during bolt cycle, clears optic and preserves prior ammo until completion");
        uint deadline=reload.Receipt.After!.Value.ReloadCompleteTick;
        var during=Settle(6,CustomWeaponActionKind.Fire,0);
        Check(during.Receipt.Action.SourceFrame<deadline&&during.Receipt.Result==WeaponActionResult.Reloading
            &&during.Receipt.Emission==CustomEmissionDisposition.NotAttempted&&during.Receipt.After is {Magazine:3,Reserve:16}
            &&engine.Latest.Second.Health==135&&engine.Latest.Payments==1,
            "fresh source before reload completion cannot charge or emit a Fire");
        WaitForActionInputs(engine,()=>engine.Latest.First.State.Tick>=deadline&&engine.Latest.First.State.Phase==CustomWeaponPhase.Ready,
            "regular genuine native input advances source time through canonical reload completion",6000);
        Check(engine.Latest.First.State is {Magazine:4,Reserve:15,Optic:0}&&engine.Latest.Second.Health==135,
            "source-driven reload transfers one reserve round exactly once without health effects");
        var optic=Settle(7,CustomWeaponActionKind.Optic,1);
        Check(optic.Receipt.Result==WeaponActionResult.Accepted&&optic.Receipt.After is {Magazine:4,Reserve:15,Optic:1},
            "fresh explicit optic control re-enters precision only after canonical reload completion");
        var next=Settle(8,CustomWeaponActionKind.Fire,1,contact:true);
        Check(next.Receipt.Result==WeaponActionResult.Accepted&&next.Receipt.After is {Magazine:3,Reserve:15}
            &&engine.Latest.Second.Health==70&&engine.Latest.Payments==2&&engine.Latest.Contacts==2&&!engine.Latest.Second.NativeHistory,
            "post-reload Fire pays one newly loaded round and second canonical65HP without native damage history");
        Check(engine.Latest.StepFailures==0,"actual cooldown/reload control lifecycle has no simulation exceptions");
        Console.WriteLine($"CUSTOM ACCEPTANCE RELOAD: fireSource={fired.Receipt.Action.SourceFrame} ready={ready} reloadSource={reload.Receipt.Action.SourceFrame} deadline={deadline} nextSource={next.Receipt.Action.SourceFrame}");
    }
    private static void CustomAcceptanceOwnerSymmetry(bool impaired)
    {
        using var f=new ActionTransportFixture(impaired:impaired,ownerToken:Guid.NewGuid(),tokenOwnerSecond:true);
        Check(f.First.State?.OwnerSlot==f.Second.Slot&&f.Second.State?.OwnerSlot==f.Second.Slot&&f.First.Slot!=f.Second.Slot,
            "actual owner token assigns lobby control to second ordinary UDP slot rather than first arrival");
        Check(NetSession.Role==NetRole.Server&&NetSession.IsAuthority&&!NetSession.IsHost&&!NetSession.IsClient&&NetSession.LocalSlot==-1,
            "playing lobby owner and non-owner are both remote clients of a dedicated authority with no local host body");
        if(impaired)
        {
            var transport=(NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Rig.Server)!;
            Check(typeof(NetTransport).GetField("_lagWorker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(transport)==null
                &&typeof(NetTransport).GetField("_lagWorker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.First.Transport)!=null
                &&typeof(NetTransport).GetField("_lagWorker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Second.Transport)!=null
                &&NetLag.RoundTripMs==160&&NetLag.LossPercent==10&&NetLag.Seed==431,
                "symmetric impaired clients both traverse160ms RTT/10% loss while dedicated transport adds no duplicate fault delay");
        }
        long before=f.DroppedDatagrams();
        void Shoot(Client client,ActionResultProbe probe,bool targetFirst,ulong expectedPayments)
        {
            MaintainActionInputs(f,100,"both ordinary clients keep real input current before owner/non-owner action sequence");
            foreach(uint sequence in new uint[] {1,2})
            {
                f.Refresh(f.First);f.Refresh(f.Second);
                var setup=f.Action(client,sequence,sequence==1?CustomWeaponActionKind.Equip:CustomWeaponActionKind.Optic,sequence==1?(byte)0:(byte)1);
                f.Send(client,setup);
                WaitForActionInputs(f,()=>probe.Receipts.ContainsKey(sequence),"owner/non-owner equip and optic use identical source-admitted action ingress",10000);
                Check(probe.Receipts[sequence].Receipt.Result==WeaponActionResult.Accepted,"owner/non-owner source-admitted equip/optic succeeds");
                probe.AckReceipt(sequence);
                WaitForActionInputs(f,()=>f.Rig.Server.TryGetCustomActionPending(client.Slot,out int pending)&&pending==0,
                    "owner/non-owner setup receipts retire while both real input streams remain current",10000);
            }
            f.Refresh(client);var action=f.Press(client,3,ack:probe.SnapshotWorld);f.Send(client,action);
            WaitForActionInputs(f,()=>probe.Receipts.ContainsKey(3)&&probe.Contacts.ContainsKey(3)&&f.Latest.Payments==expectedPayments
                &&(targetFirst?f.Latest.First:f.Latest.Second).Health==135,
                "same endpoint-owned action handler settles owner/non-owner actual shot",10000);
            var result=probe.Receipts[3];var outcome=probe.Contacts[3].Outcome;var target=targetFirst?f.Latest.First:f.Latest.Second;
            Check(result.Receipt.Result==WeaponActionResult.Accepted&&result.Receipt.Action.Owner==action.Owner
                &&result.Receipt.Source?.TargetWorldFrame==action.AckFrame&&result.Receipt.After is {Magazine:3}
                &&outcome.Health.BodyRemoved==65&&target.Health==135&&f.Latest.Payments==expectedPayments
                &&f.Latest.Contacts==expectedPayments&&!target.NativeHistory,
                "same source/ACK/life/payment rules give each ordinary slot exactly one65HP result and one custom round debit");
            probe.AckReceipt(3);probe.AckContact(3);
            WaitForActionInputs(f,()=>f.Rig.Server.TryGetCustomActionPending(client.Slot,out int pending)&&pending==0,
                "same full application acknowledgements retire owner/non-owner private result",10000);
        }
        Shoot(f.Second,f.B,true,1);Shoot(f.First,f.A,false,2);
        Check(f.Latest.First.State.Magazine==3&&f.Latest.Second.State.Magazine==3&&f.Latest.First.Health==135&&f.Latest.Second.Health==135
            &&f.Latest.StepFailures==0,"token ownership never changes canonical damage, custom ammo or simulation validity");
        long dropped=f.DroppedDatagrams()-before;
        if(impaired)Check(dropped>0,"symmetric impaired source/action/result traffic actually loses datagrams");
        Console.WriteLine($"CUSTOM ACCEPTANCE OWNER SYMMETRY: impaired={impaired} ownerSlot={f.Second.Slot} otherSlot={f.First.Slot} payments={f.Latest.Payments} contacts={f.Latest.Contacts} dropped={dropped}");
    }
}
