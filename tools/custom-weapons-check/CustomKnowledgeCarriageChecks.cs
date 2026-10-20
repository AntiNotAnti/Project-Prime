using System.Buffers.Binary;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure private wire carriage only. No nonce is treated as source admission,
// readiness, a pickup contact, resource payment or permission to apply damage.
internal static class CustomKnowledgeCarriageChecks
{
    private static readonly CustomResourceKnowledge InputStamp=new(0x0102030405060708UL,0x1112131415161718UL,0x2122232425262728UL);
    private static readonly CustomResourceKnowledge ControlStamp=new(9,10,11);
    private static readonly byte[] InputStampBytes={8,7,6,5,4,3,2,1,24,23,22,21,20,19,18,17,40,39,38,37,36,35,34,33};
    private sealed class Fixture
    {
        internal readonly WorldBootstrapIdentity Bootstrap=new(new(1,1,1),1,1,100,1,1);
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomWeaponManifest Manifest;
        internal readonly WeaponOwnerContext Owner=new(1,1,0,1,1);
        internal readonly CustomWeaponAction Action;
        internal readonly CustomReceiptMessage Receipt;
        internal readonly CustomStateMessage State;
        internal CustomControlRequest Control=>new(Bootstrap,Manifest.Digest,0,1,new(10),1,CustomWeaponActionKind.Fire,0,ControlStamp);
        internal Fixture()
        {
            Manifest=new(new[]{Definition});
            Action=new(Bootstrap,Manifest.Digest,0,1,new(10),1,200,150,0,CustomWeaponActionKind.Fire,0,Vector3.Zero,-Vector3.UnitZ);
            var runtime=new WeaponRuntimeState(Definition,Owner,200);runtime.SetEquipped(200,true);
            var before=runtime.Snapshot();runtime.TryFire(200,1,true);
            var source=new CustomSourceCertificate(Action,201,150,0,Vector3.Zero,-Vector3.UnitZ,true,false,false);
            Receipt=new(1,new(Action,201,CustomJournalDisposition.Accepted,WeaponActionResult.Accepted,
                CustomEmissionDisposition.Spawned,source,before,runtime.Snapshot()),InputStamp);
            State=new(Bootstrap,Manifest.Digest,new(10),201,1,1,new(10),200,runtime.Snapshot(),true,InputStamp,150,200);
        }
        internal byte[] StateBytes(CustomStateMessage state)
        {var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(state,bytes,Manifest,Definition);return bytes;}
        internal byte[] ReceiptBytes(CustomReceiptMessage receipt)
        {var bytes=new byte[CustomReceiptMessage.Size];CustomCombatResults.Write(receipt,bytes,Manifest,Definition);return bytes;}
        internal IntentPacket Intent(int events=0)
        {
            var intent=new IntentPacket{MatchId=1,AuthorityEpoch=1,SlotGeneration=1,LifeId=1,Frame=200,AckFrame=150,
                Position=Vector3.Zero,Aim=-Vector3.UnitZ,HasState=true,Buttons=IntentButtons.InPlayState,
                HasFireEvents=true,FireEventCount=(byte)events};
            for(int index=0;index<events;index++)intent.FireEvents[index]=new((uint)index+1,200,150,0,
                FireEventKind.ReleaseFire,(byte)BeamType.PowerBeam,0,0,SourceLife:1);
            return intent;
        }
    }
    private static byte[] Native(in IntentPacket intent)
    {var bytes=new byte[CompactIntentCodec.Size(intent)];CompactIntentCodec.Write(intent,bytes);return bytes;}
    private static bool Empty(in CustomInputEnvelope value)=>value.Bootstrap==default&&!value.Manifest.IsValid
        &&value.Knowledge.IsEmpty&&value.Control==null&&value.Intent.Frame==0;
    internal static void Run(Action<bool,string> check)
    {
        int count=0;void Verify(bool pass,string label){check(pass,"private knowledge carriage: "+label);count++;}
        var f=new Fixture();PrivateAction(f,Verify);PrivateResults(f,Verify);Carrier(f,Verify);Malformed(f,Verify);
        JournalRetention(f,Verify);ClientCreationStamp(f,Verify);BoundaryMetadata(f,Verify);CapturedStamp(f,Verify);Allocation(f,Verify);
        Console.WriteLine($"Private knowledge carriage: {count} pure codec/bounds/privacy assertions; no live admission claim.");
    }
    private static void PrivateAction(Fixture f,Action<bool,string> check)
    {
        var canonical=new byte[CustomWeaponAction.Size];f.Action.Write(canonical);
        var value=new CustomPrivateAction(f.Action,InputStamp);var bytes=new byte[CustomPrivateAction.Size];value.Write(bytes);
        check(CustomWeaponAction.Size==109&&CustomPrivateAction.Size==133&&value.IsValid
            &&bytes.AsSpan(0,109).SequenceEqual(canonical)&&bytes.AsSpan(109,24).SequenceEqual(InputStampBytes),
            "private action is unchanged canonical109 plus independently specified24-byte stamp");
        check(CustomPrivateAction.TryRead(bytes,out var copy)&&copy==value&&copy.Action==f.Action
            &&CustomWeaponAction.TryRead(canonical,out var action)&&action==f.Action,
            "private action roundtrip preserves canonical identity separately from private knowledge");
        var other=new CustomPrivateAction(f.Action,ControlStamp);var changed=new byte[133];other.Write(changed);
        check(changed.AsSpan(0,109).SequenceEqual(canonical)&&!changed.AsSpan(109).SequenceEqual(bytes.AsSpan(109))
            &&CustomActionIdentity.For(other.Action)==CustomActionIdentity.For(value.Action),
            "nonce changes never alter canonical Action bytes or projectile identity");
        for(int length=0;length<133;length++)
            check(!CustomPrivateAction.TryRead(bytes.AsSpan(0,length),out var missing)&&missing==default,
                "private action rejects truncated/old canonical-only payload atomically: "+length);
        check(!CustomPrivateAction.TryRead(bytes.Concat(new byte[]{0}).ToArray(),out var extra)&&extra==default,
            "private action rejects trailing bytes");
        var shortWrite=Enumerable.Repeat((byte)0xcc,132).ToArray();bool shortRefused=false;
        try{value.Write(shortWrite);}catch(ArgumentException){shortRefused=true;}
        var wide=Enumerable.Repeat((byte)0xcc,150).ToArray();value.Write(wide);
        check(shortRefused&&shortWrite.All(b=>b==0xcc)&&wide.AsSpan(0,133).SequenceEqual(bytes)
            &&wide.AsSpan(133).IndexOfAnyExcept((byte)0xcc)<0,
            "private-action writer checks its exact133-byte footprint before writing and preserves trailing destination");
        var request=f.Control;var encoded=new byte[100];request.Write(encoded);
        check(CustomControlRequest.Size==100&&encoded.AsSpan(76,24).SequenceEqual(new byte[]{9,0,0,0,0,0,0,0,10,0,0,0,0,0,0,0,11,0,0,0,0,0,0,0})
            &&CustomControlRequest.TryRead(encoded,out var decoded)&&decoded==request,
            "control keeps its exact core76 and separately roundtrips creation-time knowledge");
        check(request.Matches(f.Action)&&(request with{Knowledge=InputStamp}).Matches(f.Action)
            &&request!=(request with{Knowledge=InputStamp}),
            "request-to-canonical join ignores private stamp while exact request equality preserves it");
        var denial=new CustomControlDenied(request,0x0102030405060708UL,201,CustomControlDenialReason.SourceUnavailable);
        var denied=new byte[CustomControlDenied.Size];denial.Write(denied);
        check(denied.Length==113&&denied.AsSpan(0,100).SequenceEqual(encoded)
            &&BinaryPrimitives.ReadUInt64LittleEndian(denied.AsSpan(100))==denial.Token
            &&BinaryPrimitives.ReadUInt32LittleEndian(denied.AsSpan(108))==201&&denied[112]==1
            &&CustomControlDenied.TryRead(denied,out var refusal)&&refusal==denial,
            "denial retains original control stamp and exact token/world/reason offsets100/108/112");
        var box=new CustomActionOutbox(new(f.Bootstrap,0),f.Definition);
        check(box.Reserve(request,0,0)==CustomOutboxOffer.Reserved&&box.ContainsControl(request)
            &&!box.ContainsControl(request with{Knowledge=InputStamp})
            &&box.Reserve(request with{Knowledge=InputStamp},0,0)==CustomOutboxOffer.Conflict,
            "same control identity cannot be refreshed to a new stamp through duplicate reservation");
    }
    private static void PrivateResults(Fixture f,Action<bool,string> check)
    {
        var receipt=f.ReceiptBytes(f.Receipt);var plain=f.ReceiptBytes(f.Receipt with{Knowledge=default});
        check(CustomReceiptMessage.Size==233&&receipt.AsSpan(0,209).SequenceEqual(plain.AsSpan(0,209))
            &&receipt.AsSpan(209).SequenceEqual(InputStampBytes)
            &&CustomCombatResults.TryReadReceipt(receipt,f.Manifest,f.Definition,out var read)&&read==f.Receipt
            &&f.Receipt.Ack==(f.Receipt with{Knowledge=ControlStamp}).Ack,
            "private receipt appends knowledge without changing canonical result core or public ACK identity");
        var state=f.StateBytes(f.State);var developer=f.State with{Knowledge=default,GrantWorld=0,EffectiveSource=0};
        var legacy=f.StateBytes(developer);
        check(CustomStateMessage.Size==146&&state[113]==1&&state.AsSpan(0,114).SequenceEqual(legacy.AsSpan(0,114))
            &&state.AsSpan(114,24).SequenceEqual(InputStampBytes)
            &&BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(138))==150
            &&BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(142))==200
            &&CustomCombatResults.TryReadState(state,f.Manifest,f.Definition,out var copy)&&copy==f.State,
            "private state retains HasWeapon at113, stamp114, grant-world138 and effective-source142");
        var empty=CustomStateMessage.Empty(f.Bootstrap,f.Manifest.Digest,new(10),201,2,0,200,f.Owner,f.Definition);
        check(CustomCombatResults.Valid(empty,f.Manifest,f.Definition)&&empty.Knowledge.IsEmpty
            &&empty.GrantWorld==0&&empty.EffectiveSource==0&&CustomCombatResults.Valid(developer,f.Manifest,f.Definition),
            "empty inventory and legacy developer ownership use empty knowledge with zero funding floors");
        foreach(var invalid in new[]{empty with{Knowledge=InputStamp},empty with{GrantWorld=1},empty with{EffectiveSource=1},
            developer with{GrantWorld=150},developer with{EffectiveSource=200},
            f.State with{GrantWorld=202},f.State with{EffectiveSource=201}})
            check(!CustomCombatResults.Valid(invalid,f.Manifest,f.Definition),
                "empty/unstamped state cannot carry funding floors and stamped state cannot precede them");
        var exact=f.State with{GrantWorld=201,EffectiveSource=200};
        check(CustomCombatResults.Valid(exact,f.Manifest,f.Definition),"grant world and effective source floors are inclusive");
        var wrap=f.State with{Bootstrap=f.Bootstrap with{AuthorityFrame=uint.MaxValue-3},AuthorityFrame=1,
            CommittedSource=0,GrantWorld=uint.MaxValue,EffectiveSource=uint.MaxValue,State=f.State.State with{Tick=0}};
        check(CustomCombatResults.Valid(wrap,f.Manifest,f.Definition)
            &&!CustomCombatResults.Valid(wrap with{GrantWorld=2},f.Manifest,f.Definition)
            &&!CustomCombatResults.Valid(wrap with{EffectiveSource=1},f.Manifest,f.Definition),
            "private-state source/world funding floors use wrap-aware ordering");
        check(!CustomCombatResults.TryReadReceipt(receipt.AsSpan(0,209),f.Manifest,f.Definition,out _)
            &&!CustomCombatResults.TryReadState(state.AsSpan(0,114),f.Manifest,f.Definition,out _),
            "old receipt209 and state114 layouts cannot silently drop private knowledge");
    }
    private static void Carrier(Fixture f,Action<bool,string> check)
    {
        var native=Native(f.Intent());var control=f.Control;
        foreach(bool withControl in new[]{false,true})
        {
            int prefix=withControl?192:92;var bytes=new byte[prefix+native.Length];
            check(CustomInputCarrier.EmptyPrefixSize==92&&CustomInputCarrier.PrefixSize==192
                &&CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,withControl?control:null,native,bytes,out int written)
                &&written==bytes.Length,"independently stamped carrier writes exact92/192 prefix; control="+withControl);
            check(bytes[65]==(withControl?1:0)&&bytes.AsSpan(66,24).SequenceEqual(InputStampBytes)
                &&BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(90))==native.Length
                &&bytes.AsSpan(prefix).SequenceEqual(native)
                &&CustomInputCarrier.TryRead(bytes,out CustomInputEnvelope decoded)
                &&decoded.Bootstrap==f.Bootstrap&&decoded.Manifest==f.Manifest.Digest&&decoded.Knowledge==InputStamp
                &&decoded.Control==(withControl?control:null)&&Native(decoded.Intent).SequenceEqual(native),
                "carrier preserves separate source-input stamp, optional old control stamp and all native bytes");
            if(withControl)check(bytes.AsSpan(92,100).SequenceEqual(Encode(control))
                &&CustomInputCarrier.TryRead(bytes,out var request,out var presentIntent)&&request.Knowledge==ControlStamp
                &&Native(presentIntent).SequenceEqual(native),"present-control convenience reader preserves the control's original stamp");
            else check(!CustomInputCarrier.TryRead(bytes,out var missingControl,out var missingIntent)&&missingControl==default&&missingIntent.Frame==0,
                "legacy present-control reader cannot invent a request from knowledge-only input");
            for(int length=0;length<bytes.Length;length++)
                check(!CustomInputCarrier.TryRead(bytes.AsSpan(0,length),out CustomInputEnvelope missing)&&Empty(missing),
                    "truncated source envelope refuses every output: "+withControl+"/"+length);
            foreach(int offset in new[]{0,65,92,192})
            {
                var overlap=new byte[bytes.Length+native.Length+offset];native.CopyTo(overlap,offset);
                check(CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,withControl?control:null,
                    overlap.AsSpan(offset,native.Length),overlap.AsSpan(0,bytes.Length),out int used)
                    &&used==bytes.Length&&overlap.AsSpan(0,used).SequenceEqual(bytes),
                    "overlapping native input is copied intact before either prefix shape is written");
            }
            foreach(int flag in new[]{2,255})
            {var bad=(byte[])bytes.Clone();bad[65]=(byte)flag;check(!CustomInputCarrier.TryRead(bad,out CustomInputEnvelope flagAbsent)&&Empty(flagAbsent),"unknown optional-control flag refuses");}
            var wrongLength=(byte[])bytes.Clone();BinaryPrimitives.WriteUInt16LittleEndian(wrongLength.AsSpan(90),(ushort)(native.Length+1));
            check(!CustomInputCarrier.TryRead(wrongLength,out CustomInputEnvelope invalid)&&Empty(invalid)
                &&!CustomInputCarrier.TryRead(bytes.Concat(new byte[]{0}).ToArray(),out invalid)&&Empty(invalid),
                "native length and trailing bytes are strict for both envelope shapes");
        }
        var output=new byte[CustomInputCarrier.PrefixSize+native.Length];
        check(CustomInputCarrier.TryWrite(control,native,output,out _)
            &&CustomInputCarrier.TryRead(output,out CustomInputEnvelope convenience)&&convenience.Knowledge.IsEmpty
            &&convenience.Control==control,"old writer convenience supplies empty input stamp without upgrading or erasing control stamp");
        check(CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,default,control,native,output,out _)
            &&CustomInputCarrier.TryRead(output,out convenience)&&convenience.Knowledge.IsEmpty&&convenience.Control?.Knowledge==ControlStamp,
            "empty source stamp and present control stamp are independent valid values");
        check(CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,control with{Knowledge=default},native,output,out _)
            &&CustomInputCarrier.TryRead(output,out convenience)&&convenience.Knowledge==InputStamp&&convenience.Control?.Knowledge.IsEmpty==true,
            "fresh input cannot rewrite an older unstamped pending control");
        for(int events=0;events<=NetFireEvents.LiveCapacity;events++)
        foreach(bool withControl in new[]{false,true})
        {
            var payload=Native(f.Intent(events));var destination=Enumerable.Repeat((byte)0xcc,CustomInputCarrier.MaxPayload).ToArray();
            int prefix=withControl?192:92;bool fits=payload.Length+prefix<=CustomInputCarrier.MaxPayload;
            bool wrote=CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,withControl?control:null,payload,destination,out int length);
            check(wrote==fits&&(fits?length==prefix+payload.Length&&destination.AsSpan(prefix,payload.Length).SequenceEqual(payload)
                :length==0&&destination.All(value=>value==0xcc)),"both prefixes preserve complete native fire history or atomically refuse at realtime MTU");
        }
    }
    private static byte[] Encode(CustomControlRequest request)
    {var bytes=new byte[CustomControlRequest.Size];request.Write(bytes);return bytes;}
    private static void Malformed(Fixture f,Action<bool,string> check)
    {
        var native=Native(f.Intent());
        foreach(var bad in new[]{new CustomResourceKnowledge(0,1,0),new(0,0,1),new(1,0,0)})
        {
            var privateAction=new CustomPrivateAction(f.Action,bad);var request=f.Control with{Knowledge=bad};
            check(!privateAction.IsValid&&!request.IsValid
                &&!CustomCombatResults.Valid(f.Receipt with{Knowledge=bad},f.Manifest,f.Definition)
                &&!CustomCombatResults.Valid(f.State with{Knowledge=bad},f.Manifest,f.Definition),
                "partial invalid knowledge cannot enter any private action/control/receipt/state shape");
            var actionBytes=new byte[CustomPrivateAction.Size];new CustomPrivateAction(f.Action,InputStamp).Write(actionBytes);
            var requestBytes=Encode(f.Control);var receiptBytes=f.ReceiptBytes(f.Receipt);var stateBytes=f.StateBytes(f.State);
            Corrupt(actionBytes,109,bad);Corrupt(requestBytes,76,bad);Corrupt(receiptBytes,209,bad);Corrupt(stateBytes,114,bad);
            check(!CustomPrivateAction.TryRead(actionBytes,out var invalidAction)&&invalidAction==default
                &&!CustomControlRequest.TryRead(requestBytes,out var invalidRequest)&&invalidRequest==default
                &&!CustomCombatResults.TryReadReceipt(receiptBytes,f.Manifest,f.Definition,out var invalidReceipt)&&invalidReceipt==default
                &&!CustomCombatResults.TryReadState(stateBytes,f.Manifest,f.Definition,out var invalidState)&&invalidState==default,
                "malformed wire knowledge refuses atomically in all four private codecs");
            var destination=Enumerable.Repeat((byte)0xcc,CustomInputCarrier.MaxPayload).ToArray();
            check(!CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,bad,null,native,destination,out int used)
                &&used==0&&destination.All(value=>value==0xcc),"invalid source knowledge refuses before mutating carrier destination");
        }
        var bytes=new byte[192+native.Length];CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,f.Control,native,bytes,out _);
        foreach(int at in new[]{66,168})
        {
            var malformed=(byte[])bytes.Clone();malformed.AsSpan(at,8).Clear();
            check(!CustomInputCarrier.TryRead(malformed,out CustomInputEnvelope absent)&&Empty(absent),
                "malformed source or nested-control stamp refuses the whole carrier: "+at);
        }
        foreach(var changed in new[]{f.Bootstrap with{Revision=2},f.Bootstrap with{SlotGeneration=2}})
        {
            var destination=Enumerable.Repeat((byte)0xcc,bytes.Length).ToArray();
            check(!CustomInputCarrier.TryWrite(changed,f.Manifest.Digest,InputStamp,f.Control,native,destination,out int used)
                &&used==0&&destination.All(value=>value==0xcc),"outer scope cannot attach a request from a different bootstrap");
        }
        var small=Enumerable.Repeat((byte)0xcc,bytes.Length-1).ToArray();
        check(!CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,f.Control,native,small,out int shortSize)
            &&shortSize==0&&small.All(value=>value==0xcc),"short full-envelope destination remains untouched");
    }
    private static void Corrupt(byte[] bytes,int offset,CustomResourceKnowledge knowledge)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset),knowledge.Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset+8),knowledge.NonceLo);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset+16),knowledge.NonceHi);
    }
    private sealed class Admission : ICustomSourceAdmission
    {
        public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
        {
            source=new(action,201,action.Kind==CustomWeaponActionKind.Fire?action.AckFrame:201,
                action.Kind==CustomWeaponActionKind.Fire?action.AckSubFrame:(byte)0,
                Vector3.Zero,-Vector3.UnitZ,true,false,false);
            return CustomSourceDecision.Verified;
        }
    }
    private static void JournalRetention(Fixture f,Action<bool,string> check)
    {
        // The oracle and existing emitter stub attest explicit observations and
        // capacity. This exercises real journal payment, not engine collision.
        using var registry=new WeaponRegistry(true,new[]{f.Definition});
        var journal=new CustomSourceActionJournal(f.Owner,new(f.Owner,200,201),f.Manifest,registry,
            new[]{new WeaponRuntimeId(10)},200,201,true);
        var oracle=new Admission();var emitter=new SourceJournalChecks.Emitter();
        var equip=f.Action with{Kind=CustomWeaponActionKind.Equip,AckFrame=0,Direction=Vector3.Zero};
        check(journal.Offer(equip,201,InputStamp)==CustomQueueResult.Queued
            &&journal.TryKnowledge(equip,out var pendingKnowledge)&&pendingKnowledge==InputStamp
            &&journal.Offer(equip,201,ControlStamp)==CustomQueueResult.Conflict
            &&journal.Offer(equip,201,InputStamp)==CustomQueueResult.Duplicate,
            "pending journal action binds first stamp and refuses knowledge replacement on the same canonical action");
        Require(journal.TryProcessNext(201,oracle,emitter,out var equipped)&&equipped.Result==WeaponActionResult.Accepted,
            "retention fixture equips real resource journal");
        var fire=f.Action with{Sequence=2};
        Require(journal.Offer(fire,201,InputStamp)==CustomQueueResult.Queued,"retention fixture queues stamped Fire");
        Require(journal.TryProcessNext(201,oracle,emitter,out var receipt),"retention fixture settles Fire");
        check(receipt.Result==WeaponActionResult.Accepted&&receipt.Emission==CustomEmissionDisposition.Spawned
            &&receipt.Before?.Magazine==4&&receipt.After?.Magazine==3&&receipt.After?.Reserve==16&&emitter.Calls==1
            &&journal.TryKnowledge(fire,out var retainedKnowledge)&&retainedKnowledge==InputStamp,
            "real journal Fire pays one round/emits once and retains its bound private stamp beside canonical receipt");
        check(journal.Offer(fire,201,ControlStamp)==CustomQueueResult.Conflict
            &&journal.Offer(fire,201,InputStamp)==CustomQueueResult.Duplicate
            &&!journal.TryProcessNext(201,oracle,emitter,out _)&&emitter.Calls==1
            &&journal.TryCommitted(new(10),out var state)&&state.Magazine==3,
            "terminal retry cannot replace stamp, pay twice or emit twice");
        check(!journal.TryKnowledge(fire with{SourceFrame=201},out _)
            &&journal.Offer(fire with{SourceFrame=201},201,InputStamp)==CustomQueueResult.Conflict,
            "retained knowledge lookup requires exact canonical action rather than sequence alone");
        var box=new CustomActionOutbox(new(f.Bootstrap,0),f.Definition);
        Require(box.Reserve(fire,InputStamp,0)==CustomOutboxOffer.Reserved,"retention fixture reserves first terminal");
        check(!box.Publish(receipt)&&!box.Publish(receipt,ControlStamp)&&box.Publish(receipt,InputStamp),
            "private publication requires original explicit stamp; legacy-empty and changed-stamp publish cannot retag it");
        Require(box.TryNext(0,out var type,out var payload,out ulong token)
            &&type==PacketType.CustomWeaponReceipt&&CustomCombatResults.TryReadReceipt(payload.Span,f.Manifest,f.Definition,out _),
            "retention fixture exposes private terminal");
        CustomCombatResults.TryReadReceipt(payload.Span,f.Manifest,f.Definition,out var first);
        check(first.Knowledge==InputStamp&&first.Receipt==receipt&&box.TryKnowledge(fire,out var boxed)&&boxed==InputStamp,
            "private publication uses reservation knowledge while canonical receipt remains unchanged");
        Require(box.MarkSent(token,0)&&box.Acknowledge(first.Ack),"retention fixture ACKs terminal");
        // Explicit pure world-clock expiry releases the unused flight slot;
        // there is no collision/contact claim in this fixture.
        box.AdvanceFlights(unchecked(receipt.AuthorityFrame+(uint)f.Definition.Mechanics.LifetimeTicks+3));
        check(box.Count==0&&!box.TryKnowledge(fire,out _)
            &&journal.TryGetReceipt(fire.Sequence,out var original)&&original==receipt
            &&journal.TryKnowledge(fire,out retainedKnowledge)&&retainedKnowledge==InputStamp,
            "ACK plus declared flight expiry evicts outbox while bounded journal retains canonical terminal and original stamp");
        journal.Retire(WeaponCancellation.RulesChanged);
        check(journal.IsRetired&&journal.Offer(fire,202,InputStamp)==CustomQueueResult.Duplicate
            &&journal.Offer(fire,202,ControlStamp)==CustomQueueResult.Conflict
            &&journal.TryGetReceipt(fire.Sequence,out var retiredReceipt)&&retiredReceipt==receipt
            &&journal.TryKnowledge(fire,out retainedKnowledge)&&retainedKnowledge==InputStamp
            &&!journal.TryProcessNext(202,oracle,emitter,out _)&&emitter.Calls==1,
            "retired terminal preserves exact duplicate/original receipt/stamp and refuses retagging without another emission");
        Require(journal.TryKnowledge(fire,out retainedKnowledge),"retention fixture recovers journal stamp");
        check(box.Reserve(fire,retainedKnowledge,.1)==CustomOutboxOffer.Reserved
            &&box.Reserve(fire,ControlStamp,.1)==CustomOutboxOffer.Conflict
            &&!box.Publish(receipt,ControlStamp)&&box.Publish(receipt,retainedKnowledge)
            &&box.TryNext(.1,out type,out payload,out _)
            &&CustomCombatResults.TryReadReceipt(payload.Span,f.Manifest,f.Definition,out var retried)
            &&retried.Knowledge==InputStamp&&retried.Receipt==receipt&&emitter.Calls==1,
            "post-ACK terminal recovery republishes original knowledge rather than retagging from current inventory");
        var canonical=new byte[109];receipt.Action.Write(canonical);
        check(payload.Span[..109].SequenceEqual(canonical)&&CustomWeaponAction.Size==109
            &&CustomWeaponAction.TryRead(canonical,out var publicAction)&&publicAction==fire,
            "recovered private terminal preserves nonce-free canonical109-byte action");
    }
    private static void ClientCreationStamp(Fixture f,Action<bool,string> check)
    {
        var client=new CustomClientCombat(new(f.Bootstrap,0));
        Require(client.Receive(PacketType.CustomWeaponState,f.StateBytes(f.State),out _),"client original funding state");
        Require(client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var first),"client original stamped control");
        check(first.Sequence==2&&first.Knowledge==InputStamp,"control creation automatically captures current private-state knowledge");
        var fresh=new CustomResourceKnowledge(InputStamp.Revision+1,ControlStamp.NonceLo,ControlStamp.NonceHi);
        var rotated=f.State with{Revision=2,Knowledge=fresh,GrantWorld=201};
        CustomControlRequest pending=default;
        check(client.Receive(PacketType.CustomWeaponState,f.StateBytes(rotated),out _)
            &&client.TryInputControl(f.Owner,.1,out pending)&&pending==first
            &&client.Submit(first,.1)&&!client.Submit(first with{Knowledge=fresh},.1),
            "state grant rotation cannot refresh an already pending control's creation-time stamp");
        check(client.TrySubmitControl(CustomWeaponActionKind.Reload,0,.1,out var second)
            &&second.Sequence==3&&second.Knowledge==fresh&&client.TryInputControl(f.Owner,.1,out pending)&&pending==first,
            "new control captures rotated knowledge while oldest pending control retains its prior stamp");
        var native=Native(f.Intent());var bytes=new byte[192+native.Length];
        check(CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,fresh,pending,native,bytes,out _)
            &&CustomInputCarrier.TryRead(bytes,out CustomInputEnvelope carrier)&&carrier.Knowledge==fresh&&carrier.Control==first,
            "current source input and repeated older pending control carry their independent authentic stamps together");
        var denied=new CustomControlDenied(first,1,201,CustomControlDenialReason.SourceUnavailable);
        var denial=new byte[CustomControlDenied.Size];denied.Write(denial);
        check(client.Receive(PacketType.CustomWeaponControlDenied,denial,out var ack)&&ack==denied.Ack
            &&client.TryInputControl(f.Owner,.2,out pending)&&pending==second,
            "exact old-stamp terminal consumption advances to the newly stamped pending control");
    }
    private static void BoundaryMetadata(Fixture f,Action<bool,string> check)
    {
        const ulong connection=17;
        var peer=new CustomReadinessPeer(f.Bootstrap,0,connection,0);var boundary=new CustomPeerSourceBoundary(peer);
        var pre=new CustomSourceInput(f.Owner,199,150,0,Vector3.Zero,-Vector3.UnitZ,(uint)IntentButtons.InPlayState,ControlStamp,200);
        Require(boundary.NoteAcceptedInput(pre),"boundary pre-readiness input");
        Require(peer.Receive(peer.Wire.ReadyBytes,connection,201,0)==CustomReadinessResult.Accepted&&boundary.ConfirmReadiness(),
            "boundary publishes real readiness evidence");
        var accepted=pre with{Frame=200,Knowledge=InputStamp,IngressWorld=202};
        var engine=accepted with{Knowledge=default,IngressWorld=0};
        CustomSourceInput original=default;
        check(boundary.NoteAcceptedInput(accepted)&&boundary.TryAcceptedInput(engine,out original)&&original==accepted
            &&original.Knowledge==InputStamp&&original.IngressWorld==202,
            "engine candidate without transport metadata recovers exact accepted source stamp and ingress world");
        check(!boundary.TryOpen(engine,out _)&&boundary.TryOpen(original,out var context)&&context.FirstSourceFrame==200,
            "engine boundary opens only after restoring retained transport metadata to the exact consumed input");
        check(!boundary.NoteAcceptedInput(accepted)&&!boundary.NoteAcceptedInput(accepted with{Knowledge=ControlStamp,IngressWorld=999})
            &&boundary.TryAcceptedInput(engine,out original)&&original==accepted,
            "duplicate Note cannot upgrade source knowledge or arrival time");
        check(boundary.TryAcceptedInput(accepted with{Knowledge=ControlStamp,IngressWorld=999},out original)&&original==accepted,
            "metadata lookup returns original evidence rather than trusting replacement candidate metadata");
        foreach(var altered in new[]{engine with{Frame=201},engine with{AckFrame=151},engine with{ReportedBody=Vector3.UnitX},
            engine with{ReportedAim=Vector3.UnitX},engine with{InputBits=0},engine with{Owner=new(1,1,0,1,2)}})
            check(!boundary.TryAcceptedInput(altered,out var absent)&&absent==default,
                "source metadata recovery still requires exact owner/frame/ACK/body/aim/buttons");
        var next=accepted with{Frame=201,Knowledge=ControlStamp,IngressWorld=203};
        check(boundary.NoteAcceptedInput(next)&&!boundary.TryAcceptedInput(engine,out _)
            &&boundary.TryAcceptedInput(next with{Knowledge=default,IngressWorld=0},out original)&&original==next,
            "new source metadata replaces latest input without making old engine candidates current again");
    }
    private static void CapturedStamp(Fixture f,Action<bool,string> check)
    {
        var captured=new CustomCapturedKnowledge(f.Owner,f.Bootstrap,f.Manifest.Digest,InputStamp,200);
        var frozen=captured;var rotated=captured with{Knowledge=ControlStamp,SourceFrame=201};
        check(default(CustomCapturedKnowledge).IsEmpty&&default(CustomCapturedKnowledge).IsValid
            &&captured.IsValid&&!captured.IsEmpty&&frozen.Knowledge==InputStamp&&frozen.SourceFrame==200&&rotated!=frozen,
            "captured knowledge is an immutable full-scope/source value; later stamp/source cannot mutate an earlier capture");
        foreach(var invalid in new[]{captured with{Owner=new(2,1,0,1,1)},captured with{Owner=new(1,2,0,1,1)},
            captured with{Owner=new(1,1,0,2,1)},captured with{Bootstrap=f.Bootstrap with{Revision=0}},
            captured with{Manifest=default},captured with{Knowledge=default}})
            check(!invalid.IsValid,"nonempty captured stamp requires exact world/generation, valid bootstrap/manifest and present knowledge");
        check((captured with{SourceFrame=0}).IsValid
            &&captured!=(captured with{SourceFrame=201})&&captured!=(captured with{Owner=new(1,1,1,1,1)})
            &&captured!=(captured with{Bootstrap=f.Bootstrap with{Revision=2}}),
            "capture value distinguishes source/slot/bootstrap substitutions while permitting wrapped source zero");
        // Actual encoder acceptance compares this value to its current owner,
        // bootstrap and exact input frame; this pure value test does not invoke
        // the NetSession encoder or establish an authenticated capture hook.
    }
    private static void Require(bool condition,string label)
    {if(!condition)throw new InvalidOperationException("Knowledge carriage fixture: "+label);}
    private static void Allocation(Fixture f,Action<bool,string> check)
    {
        var native=Native(f.Intent());var full=new byte[192+native.Length];var empty=new byte[92+native.Length];
        var action=new CustomPrivateAction(f.Action,InputStamp);var actionBytes=new byte[133];var receipt=f.ReceiptBytes(f.Receipt);
        var state=f.StateBytes(f.State);bool valid=true;
        void Probe()
        {
            action.Write(actionBytes);valid&=CustomPrivateAction.TryRead(actionBytes,out var read)&&read==action;
            valid&=CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,f.Control,native,full,out _)
                &&CustomInputCarrier.TryRead(full,out CustomInputEnvelope control)&&control.Control==f.Control;
            valid&=CustomInputCarrier.TryWrite(f.Bootstrap,f.Manifest.Digest,InputStamp,null,native,empty,out _)
                &&CustomInputCarrier.TryRead(empty,out CustomInputEnvelope absent)&&absent.Control==null;
            valid&=CustomCombatResults.TryReadReceipt(receipt,f.Manifest,f.Definition,out _)
                &&CustomCombatResults.TryReadState(state,f.Manifest,f.Definition,out _);
        }
        for(int index=0;index<1000;index++)Probe();
        long before=GC.GetAllocatedBytesForCurrentThread();for(int index=0;index<100000;index++)Probe();
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid&&bytes==0,"100000 warmed private-action/results and independent carrier codecs allocate zero bytes");
        Console.WriteLine($"CUSTOM KNOWLEDGE CARRIAGE ALLOCATION:100000 warmed codec interactions={bytes}B");
    }
}
