using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Codec-valid authority facts are explicit fixtures. This tests cosmetic result
// delivery only; it does not qualify source admission, remote authority or HP.
internal static class CustomPresentationResultChecks
{
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomReadinessWire Wire;
        internal readonly CustomClientCombat Client;
        internal readonly CustomStateMessage Initial;
        internal readonly WeaponRuntimeState Runtime;
        internal Fixture()
        {
            var bootstrap=new WorldBootstrapIdentity(new(7,0xfedcba9876543210UL,3),4,9,100,5,6);
            Wire=new(bootstrap,2);Client=new(Wire);
            var owner=new WeaponOwnerContext(7,bootstrap.Start.AuthorityEpoch,2,9,12);
            Runtime=new(Definition,owner,100);
            Initial=new(bootstrap,Wire.Manifest.Digest,new(10),100,1,0,default,100,Runtime.Snapshot());
            if(!State(Initial))throw new InvalidOperationException("Presentation fixture state refused.");
        }
        internal bool State(CustomStateMessage state)
        {
            var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(state,bytes,Wire.Manifest,Definition);
            return Client.Receive(PacketType.CustomWeaponState,bytes,out _);
        }
        internal CustomControlRequest Request(uint sequence,CustomWeaponActionKind kind=CustomWeaponActionKind.Reload,ushort life=12)
            =>new(Wire.Bootstrap,Wire.Manifest.Digest,Wire.Slot,life,new(10),sequence,kind,kind==CustomWeaponActionKind.Optic?(byte)1:(byte)0);
        internal CustomReceiptMessage Accepted(uint sequence,CustomWeaponActionKind kind,ulong token)
        {
            var request=Request(sequence,kind);
            var action=new CustomWeaponAction(request.Bootstrap,request.Manifest,request.Slot,request.Life,request.Weapon,
                sequence,100+sequence,0,0,kind,request.Optic,Vector3.Zero,Vector3.Zero);
            Runtime.Advance(action.SourceFrame);var before=Runtime.Snapshot();
            var result=kind switch
            {
                CustomWeaponActionKind.Equip=>Runtime.SetEquipped(action.SourceFrame,true),
                CustomWeaponActionKind.Unequip=>Runtime.SetEquipped(action.SourceFrame,false),
                CustomWeaponActionKind.Optic=>Runtime.TrySetOptic(action.SourceFrame,sequence,request.Optic),
                _=>throw new InvalidOperationException("Fixture kind unsupported.")
            };
            if(result!=WeaponActionResult.Accepted)throw new InvalidOperationException("Fixture control not accepted.");
            var source=new CustomSourceCertificate(action,action.SourceFrame,action.SourceFrame,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
            return new(token,new(action,action.SourceFrame,CustomJournalDisposition.Accepted,result,
                CustomEmissionDisposition.NotAttempted,source,before,Runtime.Snapshot()));
        }
        internal bool Receipt(CustomReceiptMessage message,out CustomResultAck ack)
        {
            var bytes=new byte[CustomReceiptMessage.Size];CustomCombatResults.Write(message,bytes,Wire.Manifest,Definition);
            return Client.Receive(PacketType.CustomWeaponReceipt,bytes,out ack);
        }
        internal bool Denial(uint sequence,ulong token,out CustomResultAck ack,ushort life=12)
        {
            var denied=new CustomControlDenied(Request(sequence,life:life),token,100,CustomControlDenialReason.SourceUnavailable);
            var bytes=new byte[CustomControlDenied.Size];denied.Write(bytes);
            return Client.Receive(PacketType.CustomWeaponControlDenied,bytes,out ack);
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        AcceptedAndDenied(check);EmptyResult(check);Overflow(check);LifeBoundary(check);Allocation(check);
    }
    private static void AcceptedAndDenied(Action<bool,string> check)
    {
        var f=new Fixture();var equip=f.Accepted(1,CustomWeaponActionKind.Equip,1);
        var optic=f.Accepted(2,CustomWeaponActionKind.Optic,2);var unequip=f.Accepted(3,CustomWeaponActionKind.Unequip,3);
        check(f.Receipt(unequip,out var third)&&third==unequip.Ack&&f.Receipt(equip,out _)&&f.Receipt(optic,out _),
            "codec-valid out-of-order current-owner receipts receive exact application ACKs");
        check(f.Receipt(optic,out _)&&f.Receipt(optic with{Token=4},out _),
            "identical control result with repeated or fresh token remains acknowledged");
        foreach(var message in new[]{unequip,equip,optic})
        {
            var action=message.Receipt.Action;
            check(f.Client.TryTakePresentationResult(out var result)
                &&result==new CustomPresentationResult(action.Bootstrap,action.Manifest,action.Owner,action.Sequence,
                    action.Kind,WeaponActionResult.Accepted,true),
                "FIFO preserves receive order and complete trusted accepted result identity: "+action.Kind);
        }
        check(!f.Client.TryTakePresentationResult(out var empty)&&empty==default,
            "deduplicated results cannot cue again after draining and empty output is default");
        check(f.Denial(4,5,out var deniedAck)&&deniedAck.Sequence==4
            &&f.Client.TryTakePresentationResult(out var denied)&&!denied.Accepted
            &&denied.Kind==CustomWeaponActionKind.Reload&&denied.Result==WeaponActionResult.InvalidLife,
            "typed source-unavailable denial produces one explicit refusal without resource-phase inference");
        var foreign=equip with{Token=6,Receipt=equip.Receipt with{Action=equip.Receipt.Action with{Slot=3}}};
        // A no-resource denial avoids inventing a foreign canonical source/state.
        foreign=foreign with{Receipt=foreign.Receipt with{Disposition=CustomJournalDisposition.SourceDenied,
            Result=WeaponActionResult.InvalidLife,Source=null,Before=null,After=null}};
        check(!f.Receipt(foreign,out _)&&!f.Client.TryTakePresentationResult(out _)&&!f.Client.Invalidated,
            "foreign recipient result never becomes a presentation fact");
    }
    private static void EmptyResult(Action<bool,string> check)
    {
        var f=new Fixture();var runtime=new WeaponRuntimeState(f.Definition,f.Initial.State.Owner,100);
        runtime.SetEquipped(100,true);
        for(uint i=1;i<=4;i++)
        {
            runtime.Advance(i*100);
            if(runtime.TryFire(i*100,i,true)!=WeaponActionResult.Accepted)throw new InvalidOperationException("Empty fixture did not expend actual magazine.");
        }
        runtime.Advance(500);var before=runtime.Snapshot();var result=runtime.TryFire(500,5,true);
        var request=f.Request(5,CustomWeaponActionKind.Fire);
        var action=new CustomWeaponAction(request.Bootstrap,request.Manifest,request.Slot,request.Life,request.Weapon,
            5,500,500,0,CustomWeaponActionKind.Fire,0,Vector3.Zero,Vector3.UnitZ);
        var source=new CustomSourceCertificate(action,500,500,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
        var message=new CustomReceiptMessage(1,new(action,500,CustomJournalDisposition.ResourceDenied,result,
            CustomEmissionDisposition.NotAttempted,source,before,runtime.Snapshot()));
        check(result==WeaponActionResult.Empty&&f.Receipt(message,out _)
            &&f.Client.TryTakePresentationResult(out var fact)&&!fact.Accepted&&fact.Result==WeaponActionResult.Empty
            &&fact.Kind==CustomWeaponActionKind.Fire&&f.Client.State==f.Initial,
            "actual runtime Empty receipt preserves explicit refusal result without inferring resource phase or mutating canonical client state");
    }
    private static void Overflow(Action<bool,string> check)
    {
        var f=new Fixture();var state=f.Client.State;bool received=true;
        for(uint i=1;i<=65;i++)received&=f.Denial(i,i,out _);
        check(received&&f.Client.PresentationResultsOverflowed&&!f.Client.Invalidated&&f.Client.State==state,
            "65 current-owner facts overflow only cosmetic FIFO without invalidating gameplay or changing canonical resources");
        check(f.Client.TakePresentationOverflowed()&&!f.Client.TakePresentationOverflowed()
            &&!f.Client.PresentationResultsOverflowed,"overflow signal is consumed once without clearing queued facts");
        bool ordered=true;for(uint i=2;i<=65;i++)ordered&=f.Client.TryTakePresentationResult(out var result)&&result.Sequence==i;
        check(ordered&&!f.Client.TryTakePresentationResult(out _),"64-bound FIFO drops exactly oldest and preserves remaining results in order");
        check(f.Denial(1,1000,out _)&&f.Denial(65,1001,out _)&&!f.Client.TryTakePresentationResult(out _),
            "ancient sequence and retained identity under fresh tokens cannot cue after FIFO eviction or drain");
        check(f.Denial(100,1002,out _)&&f.Denial(99,1003,out _)
            &&f.Client.TryTakePresentationResult(out var newest)&&newest.Sequence==100
            &&f.Client.TryTakePresentationResult(out var late)&&late.Sequence==99,
            "unseen out-of-order identities inside bounded sequence window each queue once");
        check(f.Client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var request)&&request.Sequence==1,
            "presentation overflow and unsolicited cosmetic results never consume gameplay request sequence");
    }
    private static void LifeBoundary(Action<bool,string> check)
    {
        var f=new Fixture();for(uint i=1;i<=65;i++)f.Denial(i,i,out _);
        var owner=f.Initial.State.Owner;var next=new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,13);
        var state=f.Initial with{Revision=2,State=f.Initial.State with{Owner=next}};
        check(f.State(state)&&!f.Client.PresentationResultsOverflowed&&!f.Client.TryTakePresentationResult(out _),
            "new canonical full owner clears old presentation queue, dedup window and overflow");
        check(f.Denial(66,66,out var oldAck)&&oldAck.Sequence==66&&!f.Client.TryTakePresentationResult(out _),
            "late old-life trusted result is ACKed without cueing new life");
        check(f.Denial(1,67,out _,life:13)&&f.Client.TryTakePresentationResult(out var result)
            &&result.Owner==next&&result.Sequence==1&&!result.Accepted,
            "new-life sequence one has independent full-owner presentation identity");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var f=new Fixture();for(int i=0;i<1000;i++){f.Client.TryTakePresentationResult(out _);f.Client.TakePresentationOverflowed();}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){f.Client.TryTakePresentationResult(out _);f.Client.TakePresentationOverflowed();}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0,"100000 warmed empty result/overflow consumption probes allocate zero bytes");
    }
}
