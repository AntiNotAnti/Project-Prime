using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure request/wire/outbox contracts. The synthetic canonical actions below
// exercise joins and buffers, never engine source admission or actual damage.
internal static class ControlRequestChecks
{
    // Independent Python struct.pack of literal fields and the independently
    // checked M7 manifest digest used by ResultTransportChecks. No game writer
    // generated either expected byte string.
    private const string RequestHex="01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d0000000101000000000000000000000000000000000000000000000000";
    private const string DeniedHex="01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d00000001010000000000000000000000000000000000000000000000008877665544332211c900000001";
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomWeaponManifest Manifest;
        internal readonly WorldBootstrapIdentity Bootstrap=new(new(0x1234,0x0102030405060708UL,0x11223344),
            0x55667788,0x99aa,100,0x50607080,0x90a0b0c0);
        internal CustomControlRequest Request => new(Bootstrap,Manifest.Digest,3,0xbbcc,new(10),13,CustomWeaponActionKind.Fire,1);
        internal Fixture() => Manifest=new(new[] { Definition });
        internal CustomActionOutbox Box() => new(new(Bootstrap,3),Definition);
        internal CustomWeaponAction Action(CustomControlRequest? request=null,uint source=1000)
        {
            var value=request ?? Request; bool fire=value.Kind==CustomWeaponActionKind.Fire;
            return new(value.Bootstrap,value.Manifest,value.Slot,value.Life,value.Weapon,value.Sequence,source,
                fire ? 190u:0,fire ? (byte)128:(byte)0,value.Kind,value.Optic,
                fire ? new(1,2.4f,3):Vector3.Zero,fire ? -Vector3.UnitZ:Vector3.Zero);
        }
        internal CustomActionReceipt Receipt(CustomWeaponAction? action=null)
        {
            var value=action ?? Action();
            var runtime=new WeaponRuntimeState(Definition,value.Owner,value.SourceFrame);
            runtime.SetEquipped(value.SourceFrame,true); runtime.TrySetOptic(value.SourceFrame,1,1);
            var before=runtime.Snapshot(); runtime.TryFire(value.SourceFrame,value.Sequence,true);
            var certificate=new CustomSourceCertificate(value,200,value.AckFrame,value.AckSubFrame,
                new(1,2,3),-Vector3.UnitZ,true,false,false);
            return new(value,201,CustomJournalDisposition.Accepted,WeaponActionResult.Accepted,
                CustomEmissionDisposition.Spawned,certificate,before,runtime.Snapshot());
        }
        internal CustomContactOutcome Contact(CustomWeaponAction? action=null)
        {
            var value=action ?? Action();
            return new(new(new(CustomActionIdentity.For(value),new(4,0x1234,0x5678),1,CustomImpactChannel.Body),
                new(1,2,6),false),202,new(200,135,0,0,1));
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        var f=new Fixture(); Golden(f,check); Codecs(f,check); Matches(f,check);
        Fences(f,check); SourceExpiry(f,check); Binding(f,check); Capacity(f,check); DenialDelivery(f,check);
        CanonicalDelivery(f,check); Lifetimes(f,check); JournalSettlement(f,check); Client(f,check); Allocation(f,check);
    }
    private static byte[] Encode(CustomControlRequest request)
    { var bytes=new byte[CustomControlRequest.Size]; request.Write(bytes); return bytes; }
    private static byte[] Encode(CustomControlDenied denied)
    { var bytes=new byte[CustomControlDenied.Size]; denied.Write(bytes); return bytes; }
    private static byte[] Change(byte[] original,int at,byte value)
    { var bytes=original.ToArray(); bytes[at]=value; return bytes; }
    private static byte[] Zero(byte[] original,int at,int length)
    { var bytes=original.ToArray(); bytes.AsSpan(at,length).Clear(); return bytes; }
    private static void Reject(Action action,Action<bool,string> check,string label)
    {
        try { action(); } catch (ArgumentException) { check(true,label); return; }
        throw new InvalidOperationException("Expected control request rejection: "+label);
    }
    private static void Golden(Fixture f,Action<bool,string> check)
    {
        var request=f.Request; var denied=new CustomControlDenied(request,0x1122334455667788UL,201,CustomControlDenialReason.SourceUnavailable);
        check(CustomControlRequest.Size==100 && CustomControlDenied.Size==113 && CustomResultAck.Size==83,
            "control request carries full identity/kind/optic and private knowledge; no-source denial and ACK have reviewed fixed widths");
        check(Encode(request).AsSpan().SequenceEqual(Convert.FromHexString(RequestHex))
            && Encode(denied).AsSpan().SequenceEqual(Convert.FromHexString(DeniedHex)),
            "independent control and no-source denial golden vectors match exact wire bytes");
        check(CustomControlRequest.TryRead(Convert.FromHexString(RequestHex),out var read) && read==request
            && CustomControlDenied.TryRead(Convert.FromHexString(DeniedHex),out var refusal) && refusal==denied,
            "independent control vectors decode exact full identity, reason, authority world and token");
        check(request.Owner==f.Action().Owner && denied.Ack.Owner==request.Owner && denied.Ack.Sequence==request.Sequence
            && denied.Ack.Token==denied.Token && denied.Ack.Kind==CustomResultKind.Receipt,
            "honest no-source denial can be acknowledged by exact request identity without fabricating an Action");
        var wide=Enumerable.Repeat((byte)0xcc,128).ToArray(); request.Write(wide);
        check(wide.AsSpan(0,100).SequenceEqual(Encode(request)) && wide.AsSpan(100).IndexOfAnyExcept((byte)0xcc)<0,
            "request writer touches only its100-byte contract");
        wide.AsSpan().Fill(0xcc); denied.Write(wide);
        check(wide.AsSpan(0,113).SequenceEqual(Encode(denied)) && wide.AsSpan(113).IndexOfAnyExcept((byte)0xcc)<0,
            "typed denial writer touches only its113-byte contract");
    }
    private static void Codecs(Fixture f,Action<bool,string> check)
    {
        var bytes=Convert.FromHexString(RequestHex); var denied=Convert.FromHexString(DeniedHex);
        for (int length=0;length<bytes.Length;length++)
            check(!CustomControlRequest.TryRead(bytes.AsSpan(0,length),out var absent) && absent==default,
                "every truncated request refuses atomically");
        for (int length=0;length<denied.Length;length++)
            check(!CustomControlDenied.TryRead(denied.AsSpan(0,length),out var absent) && absent==default,
                "every truncated typed no-source denial refuses atomically");
        check(!CustomControlRequest.TryRead(bytes.Concat(new byte[1]).ToArray(),out var badRequest) && badRequest==default
            && !CustomControlDenied.TryRead(denied.Concat(new byte[1]).ToArray(),out var badDenied) && badDenied==default,
            "extra bytes cannot smuggle pose/time/resource fields into control codecs");
        foreach (var field in new (int At,int Size)[] { (1,2),(3,8),(11,4),(15,4),(19,2),(21,4),(25,4),(29,4),(33,32),(66,2),(68,2),(70,4) })
        {
            check(!CustomControlRequest.TryRead(Zero(bytes,field.At,field.Size),out var absent) && absent==default,
                "zero complete bootstrap/manifest/life/runtime/sequence refuses with default request");
            check(!CustomControlDenied.TryRead(Zero(denied,field.At,field.Size),out var refusal) && refusal==default,
                "typed denial retains the same complete request identity requirements");
        }
        foreach (int at in new[] { 0,65 })
        {
            byte bad=at==0 ? (byte)2:(byte)8;
            check(!CustomControlRequest.TryRead(Change(bytes,at,bad),out _) && !CustomControlDenied.TryRead(Change(denied,at,bad),out _),
                "unknown payload version or invalid owner slot cannot enter either control codec");
        }
        foreach (ushort runtime in new ushort[] { 1,4,9,74,ushort.MaxValue })
        {
            var changed=bytes.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(68),runtime);
            check(!CustomControlRequest.TryRead(changed,out var absent) && absent==default,"native and out-of-range runtimes cannot become custom controls");
        }
        for (int kind=0;kind<=255;kind++)
        {
            bool valid=kind is >=1 and <=5; var changed=Change(Change(bytes,74,(byte)kind),75,0);
            check(CustomControlRequest.TryRead(changed,out var read)==valid && (valid || read==default),
                "request kind parser accepts exactly the five declared control kinds");
        }
        foreach (var kind in Enum.GetValues<CustomWeaponActionKind>())
        for (int optic=0;optic<=255;optic++)
        {
            bool valid=optic<=2 && (kind is CustomWeaponActionKind.Fire or CustomWeaponActionKind.Optic || optic==0);
            var changed=Change(Change(bytes,74,(byte)kind),75,(byte)optic);
            check(CustomControlRequest.TryRead(changed,out var read)==valid && (valid || read==default),
                "only Fire/Optic may carry declared optic values; other controls require zero");
        }
        for (int reason=0;reason<=255;reason++)
        {
            bool valid=reason is >=1 and <=3;
            check(CustomControlDenied.TryRead(Change(denied,112,(byte)reason),out var read)==valid && (valid || read==default),
                "typed no-source result has exactly three explicit terminal reasons");
        }
        check(!CustomControlDenied.TryRead(Zero(denied,100,8),out var noToken) && noToken==default,"unacknowledgeable zero-token denial refuses");
        foreach (uint world in new uint[] { 0,99 })
        {
            var changed=denied.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(108),world);
            check(!CustomControlDenied.TryRead(changed,out var absent) && absent==default,
                "no-source terminal authority world cannot precede admitted bootstrap");
        }
        check(!default(CustomControlRequest).IsValid && !default(CustomControlDenied).IsValid
            && !default(CustomControlRequest).Matches(default),"default controls/denials have no source or identity authority");
        Reject(()=>f.Request.Write(new byte[99]),check,"short request destination refuses");
        Reject(()=>new CustomControlDenied(f.Request,1,200,CustomControlDenialReason.LifeRetired).Write(new byte[112]),check,"short typed-denial destination refuses");
        Reject(()=>default(CustomControlRequest).Write(new byte[100]),check,"invalid request writer refuses");
    }
    private static IEnumerable<CustomControlRequest> Foreign(CustomControlRequest r)
    {
        yield return r with { Bootstrap=r.Bootstrap with { Start=r.Bootstrap.Start with { MatchId=0x1235 } } };
        yield return r with { Bootstrap=r.Bootstrap with { Start=r.Bootstrap.Start with { AuthorityEpoch=r.Bootstrap.Start.AuthorityEpoch^0x8000000000000000UL } } };
        yield return r with { Bootstrap=r.Bootstrap with { Start=r.Bootstrap.Start with { StartGeneration=2 } } };
        yield return r with { Bootstrap=r.Bootstrap with { Revision=2 } };
        yield return r with { Bootstrap=r.Bootstrap with { SlotGeneration=2 } };
        yield return r with { Bootstrap=r.Bootstrap with { AuthorityFrame=2 } };
        yield return r with { Bootstrap=r.Bootstrap with { SlowRevision=2 } };
        yield return r with { Bootstrap=r.Bootstrap with { WorldRevision=2 } };
        yield return r with { Manifest=r.Manifest with { A=r.Manifest.A^1 } };
        yield return r with { Slot=4 }; yield return r with { Life=0xbbcd }; yield return r with { Weapon=new(11) };
        yield return r with { Sequence=14 }; yield return r with { Kind=CustomWeaponActionKind.Optic }; yield return r with { Optic=2 };
    }
    private static void Matches(Fixture f,Action<bool,string> check)
    {
        var r=f.Request; var action=f.Action();
        check(r.Matches(action),"canonical Action joins exactly its full request identity/kind/optic");
        foreach (var changed in Foreign(r))
            check(changed.IsValid && CustomControlRequest.TryRead(Encode(changed),out var copy) && copy==changed
                && !changed.Matches(action),"request parser preserves every identity/kind/optic substitution but exact Action join refuses it");
        foreach (var changed in new[] { action with { SourceFrame=2000 },action with { AckFrame=191 },action with { AckSubFrame=0 },
            action with { Origin=new(8,9,10) },action with { Direction=Vector3.UnitX } })
            check(r.Matches(changed),"request intentionally carries no client commitment to server-materialized source/time/ray fields");
        check(!r.Matches(action with { Direction=Vector3.Zero }) && !r.Matches(default),"request match still requires a structurally valid canonical Action");
    }
    private static void Fences(Fixture f,Action<bool,string> check)
    {
        var box=f.Box(); var r=f.Request;
        check(box.Reserve(r,10,0)==CustomOutboxOffer.Reserved && box.Count==1,"request reserves one bounded result obligation before any source exists");
        foreach (ulong serial in new ulong[] { 0,9,10 })
            check(!box.TryControl(r.Owner,r.Sequence,serial,out var absent) && absent==default,"accepted input at or before the frozen request fence cannot materialize it");
        check(box.TryControl(r.Owner,r.Sequence,11,out var ready) && ready==r,"strictly later accepted carrier serial can identify exact unbound request");
        check(box.Reserve(r,100,1)==CustomOutboxOffer.Duplicate && box.Count==1
            && box.TryControl(r.Owner,r.Sequence,11,out ready) && ready==r,"duplicate request cannot replace its first ingress fence with a later one");
        check(!box.HasExpired(15) && box.HasExpired(15.001),"duplicate request does not refresh its original fifteen-second lifetime");
        box=f.Box(); box.Reserve(r,ulong.MaxValue,0);
        check(!box.TryControl(r.Owner,r.Sequence,ulong.MaxValue,out _) && !box.TryControl(r.Owner,r.Sequence,0,out _),
            "accepted input serial exhaustion cannot wrap around a frozen fence");
        box=f.Box(); box.Reserve(r,0,0);
        check(!box.TryControl(r.Owner,r.Sequence,0,out _) && box.TryControl(r.Owner,r.Sequence,1,out ready) && ready==r,
            "zero first-ingress fence still requires one genuinely newer accepted input");
    }
    private static void Binding(Fixture f,Action<bool,string> check)
    {
        var box=f.Box(); var r=f.Request; var action=f.Action(); box.Reserve(r,10,0);
        check(!box.HasActionReservation(action) && !box.HasContactReservation(action) && !box.HasControlWitness(action)
            && !box.TryPending(0,out var absent) && absent==default,"unbound request has no invented canonical action or usable source witness");
        check(box.TryUnboundControl(0,out var unbound) && unbound==r && !box.TryUnboundControl(-1,out _)
            && !box.TryUnboundControl(16,out _),"unbound request enumeration is bounded and typed");
        foreach (var changed in new[] { action with { Optic=2 },action with { Sequence=14 },action with { Life=0xbbcd },
            action with { Manifest=action.Manifest with { A=action.Manifest.A^1 } },default })
            check(!box.Bind(r,changed) && !box.HasControlWitness(changed),"canonical binding requires exact request identity/kind/optic and valid action");
        check(box.TryControl(r.Owner,r.Sequence,11,out var eligible) && box.Bind(eligible,action)
            && box.HasActionReservation(action) && box.HasContactReservation(action) && box.HasControlWitness(action),
            "trusted caller binds once after a post-fence carrier and exposes exact reserved canonical witness");
        check(box.Bind(r,action) && !box.Bind(r,action with { SourceFrame=1001 })
            && !box.Bind(r,action with { Origin=action.Origin+Vector3.UnitX }) && !box.Bind(r,action with { AckFrame=191 }),
            "same canonical binding is idempotent; later source/ray/ACK cannot rebind it");
        check(box.Reserve(action,0)==CustomOutboxOffer.Conflict && box.HasControlWitness(action) && box.Count==1,
            "raw Action ingress cannot reinterpret a bound reliable control reservation even with equal canonical bytes");
        check(!box.TryControl(r.Owner,r.Sequence,12,out _) && !box.TryUnboundControl(0,out _)
            && box.TryPending(0,out var pending) && pending==action,"bound request leaves unbound enumeration and enters canonical pending queue");
        check(!box.PublishDenied(r,202,CustomControlDenialReason.SourceUnavailable),"bound action cannot later turn into a fabricated no-source denial");
        box=f.Box(); box.Reserve(action,0);
        check(!box.Bind(r,action) && !box.HasControlWitness(action),"legacy raw Action reservation never becomes a reliable-control source witness");
    }
    private static void SourceExpiry(Fixture f,Action<bool,string> check)
    {
        var r=f.Request; var box=f.Box();
        check(CustomActionOutbox.SourceWaitSeconds==64/60d,"source waiting has an explicit sixty-four-tick wall-time ceiling independent of result delivery deadline");
        check(box.Reserve(r,10,0,200,10)==CustomOutboxOffer.Reserved
            && !box.ControlSourceExpired(r,264,10) && box.ControlSourceExpired(r,265,10),
            "source wait accepts exact64 authority worlds and expires65 without requiring a wall-clock advance");
        box=f.Box(); box.Reserve(r,10,0,200,10);
        check(!box.ControlSourceExpired(r,200,10+CustomActionOutbox.SourceWaitSeconds)
            && box.ControlSourceExpired(r,200,10+CustomActionOutbox.SourceWaitSeconds+.000001),
            "stalled authority world accepts exact source wall deadline and expires immediately beyond it");
        check(box.Reserve(r,100,.5,264,10.5)==CustomOutboxOffer.Duplicate
            && box.ControlSourceExpired(r,265,10.5)
            && box.ControlSourceExpired(r,200,10+CustomActionOutbox.SourceWaitSeconds+.000001)
            && box.TryControl(r.Owner,r.Sequence,11,out var original) && original==r,
            "duplicate cannot refresh first source world/time/fence; caller must reject expired witness eligibility");
        check(box.Count==1 && box.TryUnboundControl(r.Owner,r.Sequence,out original) && original==r
            && !box.TryUnboundControl(r.Owner,r.Sequence+1,out _),
            "expired source obligation remains exact bounded request until an honest terminal result is published");
        check(box.PublishDenied(r,265,CustomControlDenialReason.SourceUnavailable)
            && !box.Bind(r,f.Action()) && !box.ControlSourceExpired(r,266,20) && box.Count==1,
            "source expiry terminates in typed denial, cannot bind later and keeps application ACK obligation");
        box=f.Box(); box.Reserve(r,10,0);
        check(!box.ControlSourceExpired(r,100,CustomActionOutbox.SourceWaitSeconds)
            && !box.HasExpired(1) && box.ControlSourceExpired(r,100,CustomActionOutbox.SourceWaitSeconds+.000001)
            && !box.HasExpired(15) && box.HasExpired(15.001),
            "source deadline and fifteen-second application-result deadline are separate clocks and boundaries");
        foreach (double wall in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,9.9 })
        {
            box=f.Box(); box.Reserve(r,10,0,200,10);
            check(box.ControlSourceExpired(r,200,wall),"invalid or backdated source clock fails closed without an invented source");
        }
        foreach (double sourceTime in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,-.1 })
        {
            box=f.Box(); check(box.Reserve(r,10,0,200,sourceTime)==CustomOutboxOffer.Foreign && box.Count==0,
                "invalid first source-clock domain cannot reserve a reliable control");
        }
        box=f.Box();
        check(box.Reserve(r,10,0,99,0)==CustomOutboxOffer.Foreign && box.Count==0,
            "control ingress authority world cannot precede its exact bootstrap");
        var wrap=f.Bootstrap with { AuthorityFrame=uint.MaxValue-10 }; var wrapRequest=r with { Bootstrap=wrap };
        box=new(new(wrap,3),f.Definition); box.Reserve(wrapRequest,10,0,uint.MaxValue-2,0);
        check(!box.ControlSourceExpired(wrapRequest,61,0) && box.ControlSourceExpired(wrapRequest,62,0),
            "source-world deadline retains64/65 semantics across uint authority wrap");
    }
    private static void Capacity(Fixture f,Action<bool,string> check)
    {
        var box=f.Box(); var first=f.Request with { Sequence=1 };
        for (uint seq=1;seq<=16;seq++) check(box.Reserve(first with { Sequence=seq },10,0)==CustomOutboxOffer.Reserved,
            "all sixteen unresolved controls reserve fixed capacity before source or effects");
        check(box.Count==16 && box.Reserve(first with { Sequence=17 },10,0)==CustomOutboxOffer.Full
            && box.Reserve(first,100,0)==CustomOutboxOffer.Duplicate,"seventeenth request backpressures without eviction or duplicate-fence refresh");
        for (int index=0;index<16;index++)
            check(box.TryUnboundControl(index,out var unbound) && unbound.Sequence==(uint)index+1
                && !box.TryPending(index,out _),"full unresolved capacity retains each request without any placeholder Action");
        check(box.Reserve(first with { Optic=2 },10,0)==CustomOutboxOffer.Conflict && box.Count==16
            && box.TryControl(first.Owner,first.Sequence,11,out var original) && original==first,
            "same-identity conflicting request cannot evict or revise first reserved control");
        check(box.PublishDenied(first,201,CustomControlDenialReason.SourceUnavailable)
            && box.TryNext(0,out var type,out var bytes,out ulong token) && type==PacketType.CustomWeaponControlDenied
            && CustomControlDenied.TryRead(bytes.Span,out var denied) && box.MarkSent(token,0) && box.Acknowledge(denied.Ack),
            "typed no-source denial can release one full outbox slot only after sent-result application ACK");
        check(box.Count==15 && box.Reserve(first with { Sequence=17 },20,0)==CustomOutboxOffer.Reserved
            && box.TryUnboundControl(0,out var replacement) && replacement.Sequence==17
            && !box.TryControl(first.Owner,17,20,out _) && box.TryControl(first.Owner,17,21,out _),
            "capacity recovery gives the retried seventeenth request its actual first successful ingress fence");
    }
    private static void DenialDelivery(Fixture f,Action<bool,string> check)
    {
        foreach (var reason in Enum.GetValues<CustomControlDenialReason>())
        {
            var box=f.Box(); var r=f.Request; box.Reserve(r,10,0);
            check(box.PublishDenied(r,201,reason) && !box.PublishDenied(r,202,reason)
                && !box.TryControl(r.Owner,r.Sequence,11,out _),"first typed no-source terminal result is immutable and cannot bind later");
            check(box.TryNext(0,out var type,out var bytes,out ulong token) && type==PacketType.CustomWeaponControlDenied
                && bytes.Length==113 && CustomControlDenied.TryRead(bytes.Span,out var denied)
                && denied.Request==r && denied.Reason==reason && denied.AuthorityFrame==201 && denied.Token==token,
                "no-source application result is honest fixed113-byte denial with exact original request");
            CustomControlDenied.TryRead(bytes.Span,out var result); byte[] original=bytes.ToArray();
            check(!box.Acknowledge(result.Ack) && box.Count==1,"unsent no-source result cannot be acknowledged speculatively");
            check(box.MarkSent(token,0) && !box.TryNext(.249,out _,out _,out _)
                && box.TryNext(.25,out type,out bytes,out var repeated) && repeated==token && bytes.Span.SequenceEqual(original),
                "no-source application resend respects quarter-second deadline and exact immutable payload/token");
            foreach (var bad in new[] { result.Ack with { Token=token+1 },result.Ack with { Kind=CustomResultKind.Contact },
                result.Ack with { Life=(ushort)(r.Life+1) },result.Ack with { Sequence=r.Sequence+1 } })
                check(!box.Acknowledge(bad) && box.Count==1,"foreign token/kind/life/sequence cannot release no-source obligation");
            check(box.Acknowledge(result.Ack) && box.Count==0 && !box.TryNext(.25,out _,out _,out _),
                "exact typed denial ACK retires the request and its unused Fire contact reservation together");
        }
    }
    private static void CanonicalDelivery(Fixture f,Action<bool,string> check)
    {
        var box=f.Box(); var r=f.Request; var action=f.Action(); var contact=f.Contact(action);
        box.Reserve(r,10,0); box.TryControl(r.Owner,r.Sequence,11,out var eligible); box.Bind(eligible,action);
        PacketType type=default; ReadOnlyMemory<byte> bytes=default; ulong contactToken=0,token=0;
        check(box.Publish(contact) && box.TryNext(0,out type,out bytes,out contactToken)
            && type==PacketType.CustomWeaponContact,"preallocated control Fire contact may publish before its canonical action receipt");
        CustomCombatResults.TryReadContact(bytes.Span,f.Manifest,f.Definition,out var outcome);
        check(box.MarkSent(contactToken,0) && box.Acknowledge(outcome.Ack) && box.Count==1,
            "contact-before-receipt ACK preserves the unresolved canonical receipt obligation");
        check(box.Publish(f.Receipt(action)) && !box.HasControlWitness(action)
            && box.TryNext(0,out type,out bytes,out token) && type==PacketType.CustomWeaponReceipt,
            "bound control publishes ordinary typed canonical receipt and then closes its emission witness");
        check(CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var message)
            && message.Receipt.Action==action && r.Matches(message.Receipt.Action)
            && token!=contactToken && box.MarkSent(token,0) && box.Acknowledge(message.Ack) && box.Count==0,
            "canonical returned action exactly matches request while independently retaining its server-selected source/ray");
    }
    private static void Lifetimes(Fixture f,Action<bool,string> check)
    {
        var box=f.Box(); var old=f.Request; var next=old with { Life=(ushort)(old.Life+1) };
        check(box.Reserve(old,10,0)==CustomOutboxOffer.Reserved && box.Reserve(next,11,0)==CustomOutboxOffer.Reserved
            && box.Count==2,"same occupant can retain old-life request results beside same-sequence new-life request");
        box.PublishDenied(old,201,CustomControlDenialReason.LifeRetired);
        box.TryNext(0,out _,out var bytes,out var token); CustomControlDenied.TryRead(bytes.Span,out var denied); box.MarkSent(token,0);
        check(!box.Acknowledge(denied.Ack with { Life=next.Life }) && box.Count==2
            && box.Acknowledge(denied.Ack) && box.Count==1 && box.TryControl(next.Owner,next.Sequence,12,out var current) && current==next,
            "old-life ACK frees only its exact obligation and never borrows new-life request identity");
        box.Close();
        check(box.Count==0 && !box.TryControl(next.Owner,next.Sequence,ulong.MaxValue,out _) && !box.TryUnboundControl(1,out _)
            && !box.HasControlWitness(f.Action(next)) && box.Reserve(next,12,0)==CustomOutboxOffer.Closed,
            "retired endpoint/bootstrap closes all request/witness/storage ownership");
    }
    private sealed class JournalAdmission(uint world) : ICustomSourceAdmission
    {
        public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
        {
            source=new(action,world,world,0,new(1,2,3),-Vector3.UnitZ,true,false,false);
            return CustomSourceDecision.Verified;
        }
    }
    private sealed class ForbiddenEmitter : ICustomWeaponEmitter
    {
        public bool HasCapacity(WeaponDefinition definition,WeaponOwnerContext owner) => throw new InvalidOperationException("Nonfire denial settlement must not reserve an emitter.");
        public CustomEmissionDisposition Emit(in CustomAcceptedLaunch launch) => throw new InvalidOperationException("Nonfire denial settlement must not emit.");
    }
    private static CustomSourceActionJournal Journal(Fixture f,WeaponRegistry registry)
    {
        var owner=f.Request.Owner; var clock=new CustomSourceClock(owner,1000,200);
        return new(owner,clock,f.Manifest,registry,new[] { new WeaponRuntimeId(10) },1000,200,true);
    }
    private static void JournalSettlement(Fixture f,Action<bool,string> check)
    {
        using var registry=new WeaponRegistry(true,new[] { f.Definition });
        var journal=Journal(f,registry); var first=f.Request with { Sequence=1 };
        journal.TryCommitted(new(10),out var before);
        foreach (var invalid in new[] { first with { Sequence=2 },first with { Life=0xbbcd },first with { Slot=4 },
            first with { Manifest=first.Manifest with { A=first.Manifest.A^1 } },default })
            check(!journal.SettleUnobserved(invalid,200,CustomControlDenialReason.SourceUnavailable)
                && journal.SettledSequence==0,"no-source settlement requires exact current life and contiguous request head");
        check(!journal.SettleUnobserved(first,199,CustomControlDenialReason.SourceUnavailable)
            && !journal.SettleUnobserved(first,200,CustomControlDenialReason.LifeRetired)
            && !journal.SettleUnobserved(first,200,CustomControlDenialReason.StaleSequence),
            "only actual current-life source-unavailable result may advance the live journal without source");
        check(journal.SettleUnobserved(first,201,CustomControlDenialReason.SourceUnavailable)
            && journal.SettledSequence==1 && journal.ObservedSourceFrame==1000 && journal.CommittedSourceFrame==1000
            && journal.TryCommitted(new(10),out var after) && after==before,
            "honest no-source terminal consumes sequence once without advancing source clock, resources or equip state");
        check(journal.TryGetUnobserved(1,out var refusal) && refusal.Request==first && refusal.AuthorityFrame==201
            && refusal.Reason==CustomControlDenialReason.SourceUnavailable && !journal.TryGetReceipt(1,out var absent) && absent==default
            && !journal.SettleUnobserved(first,202,CustomControlDenialReason.SourceUnavailable),
            "no-source result retains its typed request and has no fabricated canonical Action receipt");
        check(!journal.SettleUnobserved(first with { Optic=2 },202,CustomControlDenialReason.SourceUnavailable)
            && journal.TryGetUnobserved(1,out var originalDenial) && originalDenial==refusal,
            "changed payload under settled request sequence cannot replace retained typed no-source result");
        var second=first with { Sequence=2,Kind=CustomWeaponActionKind.Equip,Optic=0 };
        var action=f.Action(second,1001);
        check(journal.ObserveSource(first.Owner,1001,202,true) && journal.Offer(action,202)==CustomQueueResult.Queued
            && journal.TryProcessNext(202,new JournalAdmission(202),new ForbiddenEmitter(),out var receipt)
            && receipt.Result==WeaponActionResult.Accepted && journal.SettledSequence==2
            && journal.TryCommitted(new(10),out after) && after.Magazine==4 && after.Reserve==16 && after.Phase==CustomWeaponPhase.Ready,
            "next genuine source/equip request progresses after no-source terminal instead of deadlocking sequence gap");
        journal=Journal(f,registry);
        for (uint sequence=1;sequence<=4;sequence++) check(journal.SettleUnobserved(first with { Sequence=sequence },200,CustomControlDenialReason.SourceUnavailable),
            "no-source denials consume the same bounded per-world drain budget");
        check(!journal.SettleUnobserved(first with { Sequence=5 },200,CustomControlDenialReason.SourceUnavailable)
            && journal.SettleUnobserved(first with { Sequence=5 },201,CustomControlDenialReason.SourceUnavailable),
            "fifth no-source terminal waits for the next authority world rather than bypassing work budget");
        journal=Journal(f,registry);
        for (uint sequence=1;sequence<=128;sequence++) check(journal.SettleUnobserved(first with { Sequence=sequence },200+sequence,CustomControlDenialReason.SourceUnavailable),
            "typed no-source journal entries use bounded terminal retention");
        check(!journal.SettleUnobserved(first with { Sequence=129 },329,CustomControlDenialReason.SourceUnavailable)
            && journal.SettleUnobserved(first with { Sequence=129 },330,CustomControlDenialReason.SourceUnavailable)
            && !journal.TryGetUnobserved(1,out _) && journal.TryGetUnobserved(129,out refusal) && refusal.Request.Sequence==129,
            "terminal ring refuses still-retained alias and later reuses the cell without returning an old request");
    }
    private static byte[] StateBytes(Fixture f,uint revision=1,ushort? life=null)
    {
        var owner=life.HasValue ? new WeaponOwnerContext(f.Request.Owner.MatchId,f.Request.Owner.AuthorityEpoch,3,f.Request.Owner.Generation,life.Value):f.Request.Owner;
        // Explicit already-equipped authority projection. These pure values do
        // not grant a real player inventory or prove a source phase.
        var snapshot=new WeaponStateSnapshot(owner,f.Definition.Identity,1000,CustomWeaponPhase.Ready,4,16,1,0,0,0,0);
        var state=new CustomStateMessage(f.Bootstrap,f.Manifest.Digest,new(10),200,revision,0,new(10),1000,snapshot);
        var bytes=new byte[CustomStateMessage.Size]; CustomCombatResults.Write(state,bytes,f.Manifest,f.Definition); return bytes;
    }
    private static CustomClientCombat Client(Fixture f)
    {
        var client=new CustomClientCombat(new(f.Bootstrap,3));
        if (!client.Receive(PacketType.CustomWeaponState,StateBytes(f),out _)) throw new InvalidOperationException("Explicit client projection fixture failed.");
        return client;
    }
    private static void Client(Fixture f,Action<bool,string> check)
    {
        var request=f.Request with { Sequence=1 }; var client=Client(f); var initial=client.State;
        var unknown=request with { Weapon=new(73) };
        check(unknown.IsValid && !client.Submit(unknown,0) && client.State==initial
            && !client.TryNextControl(0,out _),
            "client rejects structurally valid unknown runtime73 before queuing or advancing an M7-only control stream");
        var unknownDenied=new CustomControlDenied(unknown,1,201,CustomControlDenialReason.SourceUnavailable);
        check(!client.Receive(PacketType.CustomWeaponControlDenied,Encode(unknownDenied),out var unknownAck)
            && unknownAck==default && client.LastControlDenied==null && client.State==initial,
            "typed no-source result for unknown runtime73 cannot create an acknowledgement or projection under the M7-only manifest");
        check(client.Submit(request,0) && client.Submit(request,0)
            && !client.Submit(request with { Optic=2 },0) && !client.Submit(request with { Sequence=3 },0),
            "client retains exact immutable contiguous control intent and rejects conflicts/gaps");
        check(!client.Submit(f.Action(request with { Sequence=2 }),0) && !client.TryNext(0,out _)
            && client.TryNextControl(0,out var outbound) && outbound==request,"one live client life has one control mode and never sends a guessed raw action");
        client.Sent(request,0);
        check(!client.TryNextControl(.249,out _) && client.TryNextControl(.25,out outbound) && outbound==request
            && client.LastReceipt==null && client.LastControlDenied==null,"request repeats on application retry schedule while transport success creates no verdict");
        var canonical=f.Action(request,1001); var message=new CustomReceiptMessage(1,f.Receipt(canonical));
        var bytes=new byte[CustomReceiptMessage.Size]; CustomCombatResults.Write(message,bytes,f.Manifest,f.Definition);
        check(client.Receive(PacketType.CustomWeaponReceipt,bytes,out var ack) && ack==message.Ack
            && client.LastReceipt==message && client.State==initial && !client.TryNextControl(.25,out _),
            "client accepts canonical server-selected source/ACK/ray when returned action matches request identity/kind/optic");
        check(client.Receive(PacketType.CustomWeaponReceipt,bytes,out ack) && ack==message.Ack && client.LastReceipt==message,
            "canonical returned receipt is idempotent and repeats its exact application acknowledgement");
        var changedMessage=new CustomReceiptMessage(1,f.Receipt(f.Action(request,1002)));
        CustomCombatResults.Write(changedMessage,bytes,f.Manifest,f.Definition);
        check(!client.Receive(PacketType.CustomWeaponReceipt,bytes,out _) && client.Invalidated && client.State==initial,
            "same canonical result token cannot revise server-selected source/action even after pending request was released");
        client=Client(f); client.Submit(request,0);
        var denied=new CustomControlDenied(request,1,201,CustomControlDenialReason.SourceUnavailable);
        var denialBytes=Encode(denied);
        check(client.Receive(PacketType.CustomWeaponControlDenied,denialBytes,out ack) && ack==denied.Ack
            && client.LastControlDenied==denied && client.LastReceipt==null && client.State==initial && !client.TryNextControl(0,out _)
            && client.Submit(request with { Sequence=2 },0),"honest no-source receipt settles one pending request and permits the next sequence without placeholder Action");
        check(client.Receive(PacketType.CustomWeaponControlDenied,denialBytes,out ack) && ack==denied.Ack && !client.Invalidated,
            "duplicate typed no-source result is idempotent after pending intent is released");
        check(!client.Receive(PacketType.CustomWeaponControlDenied,Encode(denied with { Reason=CustomControlDenialReason.LifeRetired }),out _)
            && client.Invalidated,"same result token with contradictory no-source payload poisons the custom cache");
        foreach (var changed in new[] { request with { Optic=2 },request with { Kind=CustomWeaponActionKind.Optic } })
        {
            client=Client(f); client.Submit(request,0);
            var contradiction=new CustomControlDenied(changed,1,201,CustomControlDenialReason.SourceUnavailable);
            check(!client.Receive(PacketType.CustomWeaponControlDenied,Encode(contradiction),out _) && client.Invalidated,
                "typed no-source result cannot rewrite a pending control's kind/optic under the same identity");
        }
        client=Client(f); client.Submit(request,0); ushort nextLife=(ushort)(request.Life+1);
        check(client.Receive(PacketType.CustomWeaponState,StateBytes(f,2,nextLife),out _)
            && !client.TryNextControl(0,out _) && client.Receive(PacketType.CustomWeaponControlDenied,denialBytes,out ack)
            && ack==denied.Ack && client.LastControlDenied==null,
            "new canonical life retires pending old controls while exact late old-life denial still receives ACK");
        check(client.Submit(f.Action(request with { Life=nextLife }),0),"new-life projection resets mutually exclusive control mode without leaking old pending intent");
        client=Client(f);
        for (uint sequence=1;sequence<=16;sequence++) check(client.Submit(request with { Sequence=sequence },0),"client retains sixteen bounded control requests before application verdicts");
        check(!client.Submit(request with { Sequence=17 },0),"client refuses seventeenth unresolved request without eviction or sequence advance");
        client=Client(f); client.Submit(request,0); client.Submit(request,14);
        check(!client.TryNextControl(15.001,out _) && client.Invalidated,"client duplicate submission cannot refresh fixed original request deadline");
    }
    private static void Allocation(Fixture f,Action<bool,string> check)
    {
        var request=Convert.FromHexString(RequestHex); var denied=Convert.FromHexString(DeniedHex); bool valid=true;
        var box=f.Box(); var r=f.Request; box.Reserve(r,10,0);
        for (int i=0;i<10000;i++)
        { valid&=CustomControlRequest.TryRead(request,out _) && CustomControlDenied.TryRead(denied,out _); box.TryControl(r.Owner,r.Sequence,11,out _); }
        long before=GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++)
            valid&=CustomControlRequest.TryRead(request,out _) && CustomControlDenied.TryRead(denied,out _)
                && box.Reserve(r,100,0)==CustomOutboxOffer.Duplicate && box.TryControl(r.Owner,r.Sequence,11,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && allocated==0,"100000 warmed request/denial decode, duplicate-fence and post-fence lookup probes allocate zero bytes");
        Console.WriteLine($"CONTROL REQUEST ALLOCATION:100000 warmed decode/duplicate/fence probes={allocated}B");
    }
}
