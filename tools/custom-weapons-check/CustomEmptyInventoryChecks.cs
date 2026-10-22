using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure private-state/recipient/input-prediction contracts. These fixtures do not
// prove live server empty binding, pickup activation, device input or damage.
internal static class CustomEmptyInventoryChecks
{
    private sealed class Fixture
    {
        internal readonly WorldBootstrapIdentity Bootstrap=new(new(1,1,1),1,1,100,1,1);
        internal readonly CustomReadinessWire Wire;
        internal WeaponDefinition Definition=>Wire.Definition;
        internal CustomWeaponManifest Manifest=>Wire.Manifest;
        internal Fixture()=>Wire=new(Bootstrap,0);
        internal WeaponOwnerContext Owner(ushort life=1)=>new(1,1,0,1,life);
        internal CustomStateMessage Empty(uint revision=1,ushort life=1,uint settled=0,uint committed=100)
            =>CustomStateMessage.Empty(Bootstrap,Manifest.Digest,new(10),110,revision,settled,committed,Owner(life),Definition);
        internal CustomStateMessage Owned(uint revision=1,ushort life=1,bool selected=false)
        {
            var runtime=new WeaponRuntimeState(Definition,Owner(life),100);
            if(selected&&runtime.SetEquipped(100,true)!=WeaponActionResult.Accepted)throw new Exception("Empty-inventory owned fixture cannot equip.");
            return new(Bootstrap,Manifest.Digest,new(10),110,revision,0,selected?new(10):default,100,runtime.Snapshot());
        }
        internal byte[] Encode(CustomStateMessage message)
        {var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(message,bytes,Manifest,Definition);return bytes;}
        internal bool Receive(CustomClientCombat client,CustomStateMessage message)
            =>client.Receive(PacketType.CustomWeaponState,Encode(message),out var ack)&&ack==default;
        internal CustomWeaponAction Action(CustomWeaponActionKind kind=CustomWeaponActionKind.Equip,ushort life=1,uint sequence=1)
            =>new(Bootstrap,Manifest.Digest,0,life,new(10),sequence,100,kind==CustomWeaponActionKind.Fire?100U:0U,
                0,kind,0,Vector3.Zero,kind==CustomWeaponActionKind.Fire?-Vector3.UnitZ:Vector3.Zero);
        internal CustomControlRequest Control(CustomWeaponActionKind kind=CustomWeaponActionKind.Equip,ushort life=1,uint sequence=1)
            =>new(Bootstrap,Manifest.Digest,0,life,new(10),sequence,kind,0);
    }
    private sealed class Sink : ICustomWeaponInputSink
    {
        internal int Count;internal uint Sequence;
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {Count++;sequence=++Sequence;return true;}
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;void Verify(bool pass,string label){check(pass,"empty custom inventory: "+label);count++;}
        Codec(Verify);Normalization(Verify);Client(Verify);LateResult(Verify);Input(Verify);
        Console.WriteLine($"Empty custom inventory: {count} pure codec/client/controller assertions; no live activation claim.");
    }
    private static void Codec(Action<bool,string> check)
    {
        var f=new Fixture();var empty=f.Empty();var owned=f.Owned();var absent=f.Encode(empty);var present=f.Encode(owned);
        check(CustomStateMessage.Size==146&&absent.Length==146&&absent[113]==0&&present[113]==1&&owned.HasWeapon,
            "146-byte private state preserves exact HasWeapon byte113 and existing constructors default to owned");
        check(absent.Skip(114).All(value=>value==0)&&present.Skip(114).All(value=>value==0),
            "empty and default knowledge fixtures retain zero knowledge, grant-world and effective-source suffix");
        check(CustomCombatResults.TryReadState(absent,f.Manifest,f.Definition,out var emptyCopy)&&emptyCopy==empty&&!emptyCopy.HasWeapon
            &&CustomCombatResults.TryReadState(present,f.Manifest,f.Definition,out var ownedCopy)&&ownedCopy==owned&&ownedCopy.HasWeapon,
            "explicit empty and owned states roundtrip as distinct typed values");
        for(int length=0;length<146;length++)
            check(!CustomCombatResults.TryReadState(absent.AsSpan(0,length),f.Manifest,f.Definition,out var refused)&&refused==default,
                "truncated private state refuses atomically: "+length);
        foreach(int legacy in new[]{113,114})
            check(!CustomCombatResults.TryReadState(absent.AsSpan(0,legacy),f.Manifest,f.Definition,out var refused)&&refused==default,
                "old private state wire layout refuses explicitly: "+legacy);
        check(!CustomCombatResults.TryReadState(absent.Concat(new byte[]{0}).ToArray(),f.Manifest,f.Definition,out var trailing)&&trailing==default,
            "147-byte state with trailing data refuses rather than ignoring it");
        for(int flag=2;flag<=255;flag++)
        {
            var bytes=(byte[])absent.Clone();bytes[113]=(byte)flag;
            check(!CustomCombatResults.TryReadState(bytes,f.Manifest,f.Definition,out var invalid)&&invalid==default,
                "non-boolean HasWeapon flag refuses atomically: "+flag);
        }
        var prefix=(byte[])absent.Clone();prefix[113]=1;
        check(CustomCombatResults.TryReadState(prefix,f.Manifest,f.Definition,out var explicitOwned)&&explicitOwned.HasWeapon,
            "ownership comes from explicit flag rather than inferring absence from zero resource quantities");
        var funded=(byte[])present.Clone();funded[113]=0;
        check(!CustomCombatResults.TryReadState(funded,f.Manifest,f.Definition,out var hidden)&&hidden==default,
            "flipping funded state to absent cannot hide nonzero inventory");
        byte[] shortBuffer=Enumerable.Repeat((byte)0xcc,145).ToArray();bool shortRefused=false;
        try{CustomCombatResults.Write(empty,shortBuffer,f.Manifest,f.Definition);}catch(ArgumentException){shortRefused=true;}
        check(shortRefused&&shortBuffer.All(value=>value==0xcc),"short state write refuses before changing destination");
    }
    private static void Normalization(Action<bool,string> check)
    {
        var f=new Fixture();var empty=f.Empty(settled:7,committed:105);var state=empty.State;
        check(!empty.HasWeapon&&!empty.Selected.IsValid&&state.Owner==f.Owner()&&state.Weapon==f.Definition.Identity
            &&state.Tick==105&&state.Phase==CustomWeaponPhase.Invalid&&state.Magazine==0&&state.Reserve==0&&state.Optic==0
            &&state.FireReadyTick==0&&state.ReloadCompleteTick==0&&state.LastAcceptedShotId==0&&state.LastAcceptedShotTick==0
            &&empty.SettledSequence==7&&CustomCombatResults.Valid(empty,f.Manifest,f.Definition),
            "empty factory keeps full identity and committed tick with canonical zero inventory/timers/selection");
        CustomStateMessage[] malformed={empty with{Selected=new(10)},empty with{State=state with{Tick=104}},
            empty with{State=state with{Tick=106}},empty with{State=state with{Phase=CustomWeaponPhase.Unequipped}},
            empty with{State=state with{Magazine=1}},empty with{State=state with{Reserve=1}},
            empty with{State=state with{Optic=1}},empty with{State=state with{FireReadyTick=1}},
            empty with{State=state with{ReloadCompleteTick=1}},empty with{State=state with{LastAcceptedShotId=1}},
            empty with{State=state with{LastAcceptedShotTick=1}},empty with{State=state with{Owner=new(1,1,0,2,1)}},
            empty with{State=state with{Weapon=default}}};
        foreach(var value in malformed)
        {
            byte[] buffer=Enumerable.Repeat((byte)0xcc,146).ToArray();bool refused=false;
            try{CustomCombatResults.Write(value,buffer,f.Manifest,f.Definition);}catch(ArgumentException){refused=true;}
            check(!CustomCombatResults.Valid(value,f.Manifest,f.Definition)&&refused&&buffer.All(b=>b==0xcc),
                "non-normalized absent state refuses validation and atomic encoding");
        }
        var bytes=f.Encode(empty);
        foreach(int at in new[]{82,92,93,94,95,96,97,101,105,109})
        {
            var changed=(byte[])bytes.Clone();changed[at]=1;
            check(!CustomCombatResults.TryReadState(changed,f.Manifest,f.Definition,out var invalid)&&invalid==default,
                "absent selected/phase/ammo/optic/timer/shot wire field must remain canonical zero: "+at);
        }
        var changedTick=(byte[])bytes.Clone();BinaryPrimitives.WriteUInt32LittleEndian(changedTick.AsSpan(88),106);
        check(!CustomCombatResults.TryReadState(changedTick,f.Manifest,f.Definition,out var mismatch)&&mismatch==default,
            "empty snapshot tick must equal committed source exactly");
        var zeroOwned=f.Owned() with{State=f.Owned().State with{Phase=CustomWeaponPhase.Empty,Magazine=0,Reserve=0}};
        check(zeroOwned.HasWeapon&&CustomCombatResults.Valid(zeroOwned,f.Manifest,f.Definition),
            "an owned but exhausted weapon remains distinct from an absent weapon");
    }
    private static void Client(Action<bool,string> check)
    {
        var f=new Fixture();var client=new CustomClientCombat(f.Wire);var empty=f.Empty();
        check(f.Receive(client,empty)&&client.State==empty&&client.InputProjection is {HasWeapon:false,Selected:false}
            &&!client.Invalidated,"client retains a nonnull canonical empty projection with false ownership");
        foreach(var kind in new[]{CustomWeaponActionKind.Fire,CustomWeaponActionKind.Reload,CustomWeaponActionKind.Optic,
            CustomWeaponActionKind.Equip,CustomWeaponActionKind.Unequip})
            check(!client.TrySubmitControl(kind,0,0,out var request)&&request==default
                &&!client.Submit(f.Control(kind),0)&&!client.Submit(f.Action(kind),0),
                "canonical empty recipient refuses generated controls and both raw submission APIs: "+kind);
        check(!client.TryNext(0,out _)&&!client.TryNextControl(0,out _)
            &&!client.TryInputControl(f.Owner(),0,out _)&&client.State==empty&&!client.Invalidated,
            "empty refusal creates no pending retries or invalidated connection");
        check(f.Receive(client,f.Owned(2))&&client.InputProjection is {HasWeapon:true,Selected:false}
            &&client.TrySubmitControl(CustomWeaponActionKind.Equip,0,.1,out var first)&&first.Sequence==1&&first.Owner==f.Owner(),
            "same-life ownership acquisition enables first explicit control without consuming sequence on empty refusals");
        var nextEmpty=f.Empty(3,2);
        check(f.Receive(client,nextEmpty)&&client.InputProjection is {HasWeapon:false,Selected:false}
            &&client.InputProjection.Value.Owner==f.Owner(2)&&!client.TryNextControl(.2,out _)
            &&!client.TrySubmitControl(CustomWeaponActionKind.Equip,0,.2,out _),
            "new empty life clears old pending control and does not inherit ownership");
        check(f.Receive(client,f.Owned(4,2))&&client.TrySubmitControl(CustomWeaponActionKind.Equip,0,.3,out var next)
            &&next.Sequence==1&&next.Owner==f.Owner(2),"new-life reacquisition restarts explicit control at that life's first sequence");
        check(f.Receive(client,nextEmpty)&&client.InputProjection is {HasWeapon:true},
            "older empty state revision cannot erase newer acquired inventory");

        var raw=new CustomClientCombat(f.Wire);
        check(f.Receive(raw,f.Empty())&&!raw.Submit(f.Action(),0)&&f.Receive(raw,f.Owned(2))
            &&raw.Submit(f.Action(),.1)&&raw.TryNext(.1,out var pending)&&pending==f.Action(),
            "raw action path becomes usable only after canonical ownership and retains first sequence");
        check(f.Receive(raw,f.Empty(3))&&!raw.TryNext(.2,out _)&&!raw.Submit(f.Action(sequence:2),.2)
            &&raw.InputProjection is {HasWeapon:false,Selected:false},
            "same-owner canonical absence cancels queued raw action and selection");
    }
    private static void LateResult(Action<bool,string> check)
    {
        var f=new Fixture();var client=new CustomClientCombat(f.Wire);
        Require(f.Receive(client,f.Owned()),"late receipt owned state");
        Require(client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var request),"late receipt pending equip");
        var action=f.Action();var runtime=new WeaponRuntimeState(f.Definition,f.Owner(),100);var before=runtime.Snapshot();
        Require(runtime.SetEquipped(100,true)==WeaponActionResult.Accepted,"late receipt equip state");
        var source=new CustomSourceCertificate(action,110,110,0,Vector3.Zero,-Vector3.UnitZ,true,false,false);
        var receipt=new CustomReceiptMessage(1,new(action,110,CustomJournalDisposition.Accepted,
            WeaponActionResult.Accepted,CustomEmissionDisposition.NotAttempted,source,before,runtime.Snapshot()));
        var bytes=new byte[CustomReceiptMessage.Size];CustomCombatResults.Write(receipt,bytes,f.Manifest,f.Definition);
        check(f.Receive(client,f.Empty(2))&&client.Receive(PacketType.CustomWeaponReceipt,bytes,out var ack)&&ack==receipt.Ack
            &&client.InputProjection is {HasWeapon:false,Selected:false,Optic:0}
            &&!client.TrySubmitControl(CustomWeaponActionKind.Fire,0,.1,out _),
            "late accepted Equip receipt remains ACKable but cannot resurrect canonical absent inventory");
        var nextEmpty=f.Empty(3,2);Require(f.Receive(client,nextEmpty),"new empty receipt life");
        check(client.Receive(PacketType.CustomWeaponReceipt,bytes,out ack)&&ack==receipt.Ack
            &&client.State==nextEmpty&&client.InputProjection is {HasWeapon:false,Selected:false},
            "old-life receipt retry cannot change a new empty life's state or selection");
    }
    private static void Input(Action<bool,string> check)
    {
        var f=new Fixture();var controller=new CustomWeaponInputController();var sink=new Sink();
        CustomInputProjection Projection(bool selected=false,bool owned=true,ushort life=1)
            =>new(f.Owner(life),selected,selected?(byte)2:(byte)0,0,0,0,f.Bootstrap,f.Manifest.Digest,owned);
        controller.Synchronize(Projection(true));
        controller.Sample(new(true,false,true,false,false,false,false,false),sink);
        controller.Synchronize(null);
        check(controller.SuppressNative&&!controller.Available,"missing projection preserves unresolved custom/native permission uncertainty");
        controller.Synchronize(Projection(owned:false));
        check(!controller.Available&&!controller.Selected&&!controller.SuppressNative&&controller.PresentationOptic==0,
            "explicit HasWeapon=false definitively clears pending selection, optic and native suppression uncertainty");
        int count=sink.Count;
        controller.Sample(new(true,true,false,true,true,true,true,true,true),sink);
        controller.Synchronize(null);
        check(sink.Count==count&&!controller.Selected&&!controller.SuppressNative&&!controller.Available,
            "empty canonical inventory blocks all custom actions and later missing state cannot resurrect its fence");
        controller.Synchronize(Projection());
        controller.Sample(new(true,false,false,false,false,false,false,false),sink);
        check(controller.Available&&!controller.Selected&&!controller.SuppressNative&&sink.Count==count,
            "reacquired inventory starts native and needs a fresh explicit custom selection");
        controller.Sample(new(true,true,false,false,false,false,false,false),sink);
        check(controller.Selected&&controller.SuppressNative&&sink.Count==count+1,
            "fresh post-acquisition selection predicts Equip through the ordinary sink");
        controller.Synchronize(Projection(owned:false,life:2));
        check(!controller.Available&&!controller.Selected&&!controller.SuppressNative,
            "new empty owner life clears the preceding life's pending Equip");
        var defaultOwned=new CustomInputProjection(f.Owner(),false,0,0,0,0);
        check(defaultOwned.HasWeapon,"existing custom input projection constructors preserve default owned semantics");
    }
    private static void Require(bool pass,string label)
    {if(!pass)throw new Exception("Empty inventory fixture: "+label);}
}
