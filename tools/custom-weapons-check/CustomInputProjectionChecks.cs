using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Canonical result/state packets are explicit codec-valid fixtures. This checks
// only client input/presentation projection, never source admission or health.
internal static class CustomInputProjectionChecks
{
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomReadinessWire Wire;
        internal readonly WeaponOwnerContext Owner;
        internal readonly WeaponRuntimeState Runtime;
        internal readonly CustomStateMessage Initial;
        internal readonly CustomClientCombat Client;
        internal Fixture()
        {
            var bootstrap=new WorldBootstrapIdentity(new(0x4321,0xfedcba9876543210UL,0x12345678),
                0x87654321,0xabcd,100,0x11223344,0x55667788);
            Wire=new(bootstrap,5);Owner=new(bootstrap.Start.MatchId,bootstrap.Start.AuthorityEpoch,5,bootstrap.SlotGeneration,9);
            Runtime=new(Definition,Owner,100);
            Initial=new(bootstrap,Wire.Manifest.Digest,new(10),100,1,0,default,100,Runtime.Snapshot());
            Client=new(Wire);
            if(!State(Initial))throw new InvalidOperationException("Input projection initial state refused.");
        }
        internal bool State(CustomStateMessage message)
        {
            var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(message,bytes,Wire.Manifest,Definition);
            return Client.Receive(PacketType.CustomWeaponState,bytes,out _);
        }
        internal CustomControlRequest Request(CustomWeaponActionKind kind,byte optic=0)
        {
            if(!Client.TrySubmitControl(kind,optic,0,out var request))throw new InvalidOperationException("Projection fixture intent refused.");
            return request;
        }
        internal CustomWeaponAction Action(CustomControlRequest request)
            =>new(request.Bootstrap,request.Manifest,request.Slot,request.Life,request.Weapon,request.Sequence,
                100+request.Sequence,0,0,request.Kind,request.Optic,Vector3.Zero,Vector3.Zero);
        internal CustomReceiptMessage Accepted(CustomControlRequest request,ulong token)
        {
            var action=Action(request);Runtime.Advance(action.SourceFrame);var before=Runtime.Snapshot();
            var result=request.Kind switch
            {
                CustomWeaponActionKind.Equip=>Runtime.SetEquipped(action.SourceFrame,true),
                CustomWeaponActionKind.Unequip=>Runtime.SetEquipped(action.SourceFrame,false),
                CustomWeaponActionKind.Optic=>Runtime.TrySetOptic(action.SourceFrame,request.Sequence,request.Optic),
                _=>throw new InvalidOperationException("Selection fixture supports only selection/optic acceptance.")
            };
            if(result!=WeaponActionResult.Accepted)throw new InvalidOperationException("Canonical fixture runtime did not accept control.");
            var source=new CustomSourceCertificate(action,action.SourceFrame,action.SourceFrame,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
            return new(token,new(action,action.SourceFrame,CustomJournalDisposition.Accepted,result,
                CustomEmissionDisposition.NotAttempted,source,before,Runtime.Snapshot()));
        }
        internal CustomReceiptMessage Denied(CustomControlRequest request,ulong token)
        {
            var action=Action(request);
            return new(token,new(action,action.SourceFrame,CustomJournalDisposition.SourceDenied,WeaponActionResult.InvalidLife,
                CustomEmissionDisposition.NotAttempted,null,null,null));
        }
        internal bool Receive(CustomReceiptMessage message,out CustomResultAck ack)
        {
            var bytes=new byte[CustomReceiptMessage.Size];CustomCombatResults.Write(message,bytes,Wire.Manifest,Definition);
            return Client.Receive(PacketType.CustomWeaponReceipt,bytes,out ack);
        }
        internal bool Receive(CustomControlDenied message,out CustomResultAck ack)
        {
            var bytes=new byte[CustomControlDenied.Size];message.Write(bytes);
            return Client.Receive(PacketType.CustomWeaponControlDenied,bytes,out ack);
        }
        internal CustomStateMessage Projection(uint revision,uint settled,WeaponStateSnapshot snapshot,bool selected)
            =>Initial with {Revision=revision,AuthorityFrame=snapshot.Tick,SettledSequence=settled,
                Selected=selected?new WeaponRuntimeId(10):default,CommittedSource=snapshot.Tick,State=snapshot};
    }
    private sealed class NoInputSink : ICustomWeaponInputSink
    {
        internal int Calls;
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {Calls++;sequence=0;return false;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var native=Enumerable.Range(0,8).Select(NetDamage.Sequence).ToArray();
        SelectionIsExplicit(check);AcceptedBatch(check);DeniedBatch(check);LifeBoundary(check);Allocation(check);
        check(native.SequenceEqual(Enumerable.Range(0,8).Select(NetDamage.Sequence)),
            "client input result projection never allocates native damage IDs or changes native health accounting");
    }
    private static void SelectionIsExplicit(Action<bool,string> check)
    {
        var f=new Fixture();
        check(f.Client.InputProjection is {Selected:false,Optic:0,SettledSequence:0},"initial input projection is native from explicit Selected field");
        var restricted=f.Projection(2,0,f.Initial.State,true);
        check(f.State(restricted)&&restricted.State.Phase==CustomWeaponPhase.Unequipped
            &&f.Client.InputProjection is {Selected:true,Optic:0},
            "custom ownership remains selected through source restriction even when resource phase is Unequipped");
        var controller=new CustomWeaponInputController();controller.Synchronize(f.Client.InputProjection);
        check(controller.Selected&&controller.SuppressNative,"actual controller uses explicit custom selection rather than inferring native ownership from resource phase");
    }
    private static void AcceptedBatch(Action<bool,string> check)
    {
        var f=new Fixture();var requests=new[] {f.Request(CustomWeaponActionKind.Equip),f.Request(CustomWeaponActionKind.Optic,1),
            f.Request(CustomWeaponActionKind.Unequip),f.Request(CustomWeaponActionKind.Equip),f.Request(CustomWeaponActionKind.Optic,2)};
        var receipts=requests.Select((request,index)=>f.Accepted(request,(ulong)index+1)).ToArray();
        foreach(int index in new[]{4,2,1,3,0})
            check(f.Receive(receipts[index],out var ack)&&ack==receipts[index].Ack,"out-of-order canonical accepted control receipt remains exactly ACKable");
        check(f.Client.State==f.Initial&&f.Client.InputProjection is {Selected:true,Optic:2,SettledSequence:0}
            &&f.Client.LastReceipt==receipts[4],
            "all Equip/Unequip/Equip/Optic results between samples preserve newest facts without advancing private canonical state");
        var controller=new CustomWeaponInputController();var sink=new NoInputSink();controller.Synchronize(f.Client.InputProjection);
        controller.Sample(new(true,false,false,false,false,false,false,false),sink);
        check(controller.Selected&&controller.PresentationOptic==2&&sink.Calls==0,
            "one later simulation sample consumes the complete receipt batch without synthesizing controls or shots");
        var opticOne=receipts[1].Receipt.After!.Value;
        var partial=f.Projection(2,2,opticOne,true);
        check(f.State(partial)&&f.Client.InputProjection is {Selected:true,Optic:2,SettledSequence:2},
            "newer partial canonical state cannot erase later accepted optic fact");
        var native=f.Projection(3,3,receipts[2].Receipt.After!.Value,false);
        check(f.State(native)&&f.Client.InputProjection is {Selected:true,Optic:2,SettledSequence:3},
            "older settled native selection cannot undo already accepted later Equip receipt");
        check(f.State(partial)&&f.Client.State==native&&f.Client.InputProjection is {Selected:true,Optic:2},
            "out-of-order old state revision cannot roll receipt facts or canonical state backward");
        var complete=f.Projection(4,5,receipts[4].Receipt.After!.Value,true);
        check(f.State(complete)&&f.Client.InputProjection is {Selected:true,Optic:2,SettledSequence:5},
            "canonical state catching accepted facts agrees with the receipt-derived input projection");
        var before=f.Client.InputProjection;
        check(f.Receive(receipts[0],out var duplicate)&&duplicate==receipts[0].Ack&&f.Client.InputProjection==before
            &&f.Client.State==complete&&f.Runtime.Snapshot().Magazine==4&&f.Runtime.Snapshot().Reserve==16,
            "late duplicate receipt cannot restore older selection/optic or debit authority fixture resources");
    }
    private static void DeniedBatch(Action<bool,string> check)
    {
        var f=new Fixture();var equip=f.Request(CustomWeaponActionKind.Equip);var unequip=f.Request(CustomWeaponActionKind.Unequip);
        var optic=f.Request(CustomWeaponActionKind.Optic,1);var opticDenied=f.Request(CustomWeaponActionKind.Optic,2);
        var reloadDenied=f.Request(CustomWeaponActionKind.Reload);var final=f.Request(CustomWeaponActionKind.Unequip);
        var acceptedEquip=f.Accepted(equip,1);var acceptedOptic=f.Accepted(optic,3);var deniedSelection=f.Denied(unequip,2);
        var deniedOptic=new CustomControlDenied(opticDenied,4,104,CustomControlDenialReason.SourceUnavailable);
        var deniedReload=new CustomControlDenied(reloadDenied,5,105,CustomControlDenialReason.SourceUnavailable);
        check(f.Receive(deniedSelection,out var ack1)&&ack1==deniedSelection.Ack
            &&f.Receive(deniedReload,out var ack2)&&ack2==deniedReload.Ack
            &&f.Receive(acceptedOptic,out var ack3)&&ack3==acceptedOptic.Ack
            &&f.Receive(acceptedEquip,out var ack4)&&ack4==acceptedEquip.Ack
            &&f.Receive(deniedOptic,out var ack5)&&ack5==deniedOptic.Ack,
            "mixed accepted receipt/source denial/no-source results can all arrive between fixed input samples");
        check(f.Client.InputProjection is {Selected:true,Optic:1,SelectionDenied:2,OpticDenied:5,SettledSequence:0}
            &&f.Client.State==f.Initial,
            "out-of-order denials retain independent high-water marks without undoing accepted selection or optic facts");
        var acceptedFinal=f.Accepted(final,6);
        check(f.Receive(acceptedFinal,out var lastAck)&&lastAck==acceptedFinal.Ack
            &&f.Client.InputProjection is {Selected:false,Optic:0,SelectionDenied:2,OpticDenied:5},
            "later accepted Unequip clears receipt-derived selection/optic even while old denials remain visible");
        var complete=f.Projection(2,6,acceptedFinal.Receipt.After!.Value,false);
        check(f.State(complete)&&f.Receive(deniedSelection,out _)&&f.Receive(deniedOptic,out _)
            &&f.Client.InputProjection is {Selected:false,Optic:0,SelectionDenied:2,OpticDenied:5,SettledSequence:6},
            "settled state plus duplicate older denials cannot resurrect custom selection or change denial frontiers");
    }
    private static void LifeBoundary(Action<bool,string> check)
    {
        var f=new Fixture();var equip=f.Request(CustomWeaponActionKind.Equip);var receipt=f.Accepted(equip,1);f.Receive(receipt,out _);
        var oldDenial=new CustomControlDenied(new(f.Wire.Bootstrap,f.Wire.Manifest.Digest,f.Owner.Slot,f.Owner.Life,new(10),2,CustomWeaponActionKind.Optic,1),
            2,102,CustomControlDenialReason.SourceUnavailable);f.Receive(oldDenial,out _);
        var nextOwner=new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,f.Owner.Slot,f.Owner.Generation,(ushort)(f.Owner.Life+1));
        var nextRuntime=new WeaponRuntimeState(f.Definition,nextOwner,100);
        var next=f.Projection(2,0,nextRuntime.Snapshot(),false);
        check(f.State(next)&&f.Client.InputProjection is {Selected:false,Optic:0,SelectionDenied:0,OpticDenied:0}
            &&f.Client.InputProjection!.Value.Owner==nextOwner&&f.Client.LastReceipt==null&&f.Client.LastControlDenied==null,
            "new full owner life clears old accepted selection/optic and denial facts together");
        var current=f.Request(CustomWeaponActionKind.Equip);
        var lateReceipt=f.Denied(equip with{Sequence=3},3);
        var lateDenial=oldDenial with{Request=oldDenial.Request with{Sequence=4},Token=4};
        check(f.Receive(lateReceipt,out var ack1)&&ack1.Owner==f.Owner
            &&f.Receive(lateDenial,out var ack2)&&ack2.Owner==f.Owner
            &&f.Client.InputProjection is {Selected:false,Optic:0,SelectionDenied:0,OpticDenied:0}
            &&f.Client.State==next&&f.Client.TryNextControl(0,out var pending)&&pending==current,
            "late old-life source/no-source results ACK their original life without consuming current-life nonce1 or restoring facts");
        check(!f.State(f.Initial with{Revision=3})&&f.Client.State==next&&!f.Client.Invalidated
            &&f.Client.InputProjection!.Value.Owner==nextOwner,
            "higher revision old-life state cannot replace the current-life input owner");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var f=new Fixture();var request=f.Request(CustomWeaponActionKind.Equip);f.Receive(f.Accepted(request,1),out _);
        bool selected=true;for(int i=0;i<1000;i++)selected&=f.Client.InputProjection!.Value.Selected;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)selected&=f.Client.InputProjection!.Value.Selected;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(selected&&allocated==0,"100k warmed actual client InputProjection reads allocate zero bytes");
    }
}
