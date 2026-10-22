using System.Buffers.Binary;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure envelope/client contracts. Native fixtures are codec values, not source
// observations, accepted gameplay, movement replay, or damage certificates.
internal static class InputCarrierChecks
{
    private const string RequestHex="01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d0000000101000000000000000000000000000000000000000000000000";
    private static readonly WorldBootstrapIdentity Bootstrap=new(new(0x1234,0x0102030405060708UL,0x11223344),0x55667788,0x99aa,100,0x50607080,0x90a0b0c0);
    private static IntentPacket Intent(CustomControlRequest request,int events=0)
    {
        var value=new IntentPacket { MatchId=request.Owner.MatchId,AuthorityEpoch=request.Owner.AuthorityEpoch,
            SlotGeneration=request.Owner.Generation,LifeId=request.Life,Frame=1000,AckFrame=190,
            Position=new(1,2,3),Aim=Vector3.UnitZ,Buttons=IntentButtons.InPlayState,HasState=true,HasFireEvents=true,
            HasAnalogMove=true,HasContinuousFireTick=true,FireEventCount=(byte)events };
        for (int i=0;i<events;i++) value.FireEvents[i]=new((uint)i+1,1000,190,128,FireEventKind.ReleaseFire,
            (byte)BeamType.PowerBeam,0,0,SourceLife:request.Life);
        return value;
    }
    private static byte[] Native(IntentPacket value)
    { var bytes=new byte[CompactIntentCodec.Size(value)]; CompactIntentCodec.Write(value,bytes); return bytes; }
    private static byte[] Envelope(CustomControlRequest request,byte[] native)
    {
        var bytes=new byte[CustomInputCarrier.PrefixSize+native.Length];
        if (!CustomInputCarrier.TryWrite(request,native,bytes,out int size) || size!=bytes.Length)
            throw new InvalidOperationException("Explicit pure carrier fixture failed.");
        return bytes;
    }
    internal static void Run(Action<bool,string> check)
    {
        check(CustomControlRequest.TryRead(Convert.FromHexString(RequestHex),out var request),"literal control fixture decodes independently checked request bytes");
        var intent=Intent(request); var native=Native(intent); var bytes=Envelope(request,native);
        var expected=new byte[192+native.Length];
        Convert.FromHexString(RequestHex).AsSpan(0,65).CopyTo(expected); expected[65]=1;
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(90),(ushort)native.Length);
        Convert.FromHexString(RequestHex).CopyTo(expected,92); native.CopyTo(expected,192);
        check(CustomInputCarrier.PrefixSize==192 && CustomInputCarrier.MaxPayload==1176 && bytes.SequenceEqual(expected),
            "carrier has exact outer identity/flag/empty input stamp/length, 100-byte request and unchanged compact native payload");
        check(CustomInputCarrier.TryRead(bytes,out var decoded,out var read) && decoded==request
            && Native(read).SequenceEqual(native) && CustomInputCarrier.Matches(request,read),
            "carrier roundtrip preserves every native byte without promoting request into native fire history");
        for (int size=0;size<bytes.Length;size++)
            check(!CustomInputCarrier.TryRead(bytes.AsSpan(0,size),out var absent,out var missing) && absent==default && missing.Frame==0,
                "every truncated atomic carrier refuses both outputs");
        foreach (int length in new[] { 0,native.Length-1,native.Length+1,ushort.MaxValue })
        {
            var changed=bytes.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(90),(ushort)length);
            check(!CustomInputCarrier.TryRead(changed,out _,out _),"mismatched declared native length cannot partially admit a wrapper");
        }
        check(!CustomInputCarrier.TryRead(bytes.Concat(new byte[1]).ToArray(),out _,out _)
            && !CustomInputCarrier.TryRead(new byte[1177],out _,out _),"extra bytes and oversized realtime payloads reject atomically");
        var mismatches=new[] { intent,intent,intent,intent };
        mismatches[0].MatchId++; mismatches[1].AuthorityEpoch++; mismatches[2].SlotGeneration++; mismatches[3].LifeId++;
        foreach (var changed in mismatches)
        {
            var bad=Native(changed); var destination=Enumerable.Repeat((byte)0xcc,bytes.Length).ToArray();
            check(!CustomInputCarrier.Matches(request,changed)
                && !CustomInputCarrier.TryWrite(request,bad,destination,out int written) && written==0
                && destination.All(x=>x==0xcc),"match/epoch/generation/life mismatch cannot touch native output or attach a custom nonce");
            var malformed=bytes.ToArray(); bad.CopyTo(malformed,192);
            check(!CustomInputCarrier.TryRead(malformed,out _,out _),"wire identity mismatch refuses both decoded outputs");
        }
        foreach (var bad in new[] { default(CustomControlRequest),request with { Sequence=0 },request with { Kind=(CustomWeaponActionKind)255 } })
        {
            var untouched=Enumerable.Repeat((byte)0xcc,bytes.Length).ToArray();
            check(!CustomInputCarrier.TryWrite(bad,native,untouched,out int size) && size==0 && untouched.All(x=>x==0xcc),
                "invalid request write is atomic and leaves destination untouched");
        }
        foreach (var badNative in new[] { Array.Empty<byte>(),native[..^1],native.Concat(new byte[1]).ToArray() })
        {
            var untouched=Enumerable.Repeat((byte)0xcc,bytes.Length).ToArray();
            check(!CustomInputCarrier.TryWrite(request,badNative,untouched,out int count) && count==0 && untouched.All(x=>x==0xcc),
                "invalid compact native payload cannot partially write an atomic control prefix");
        }
        var shortDest=Enumerable.Repeat((byte)0xcc,bytes.Length-1).ToArray();
        check(!CustomInputCarrier.TryWrite(request,native,shortDest,out _) && shortDest.All(x=>x==0xcc),"short destination fails without partial prefix/native mutation");
        foreach (int offset in new[] { 0,17,192,205 })
        {
            var overlap=Enumerable.Repeat((byte)0xcc,bytes.Length+offset+native.Length).ToArray();native.CopyTo(overlap,offset);
            check(CustomInputCarrier.TryWrite(request,overlap.AsSpan(offset,native.Length),overlap.AsSpan(0,bytes.Length),out int count)
                && count==bytes.Length && overlap.AsSpan(0,count).SequenceEqual(bytes),"overlapping source/destination preserves exact native bytes before writing prefix");
        }
        for (int events=0;events<=NetFireEvents.LiveCapacity;events++)
        {
            var payload=Native(Intent(request,events));var output=Enumerable.Repeat((byte)0xcc,CustomInputCarrier.MaxPayload).ToArray();
            bool fits=payload.Length+192<=1176;
            bool encoded=CustomInputCarrier.TryWrite(request,payload,output,out int count);
            check(encoded==fits && (fits ? output.AsSpan(192,payload.Length).SequenceEqual(payload) && count==payload.Length+192
                : count==0 && output.All(x=>x==0xcc)),"all native fire-event counts retain every byte or fall back intact at the real realtime MTU");
            if (events==NetFireEvents.LiveCapacity)
                check(!fits && CompactIntentCodec.TryRead(payload,out var full) && full.FireEventCount==14 && Native(full).SequenceEqual(payload),
                    "full fourteen-event native carrier remains valid and unchanged when custom envelope cannot fit");
        }
        var definition=RiftlineM7.Create();var wire=new CustomReadinessWire(Bootstrap,3);var client=new CustomClientCombat(wire);
        var first=request with { Sequence=1 };var state=new WeaponStateSnapshot(first.Owner,definition.Identity,1000,CustomWeaponPhase.Ready,4,16,1,0,0,0,0);
        var stateBytes=new byte[CustomStateMessage.Size]; CustomCombatResults.Write(new CustomStateMessage(Bootstrap,wire.Manifest.Digest,new(10),200,1,0,new(10),1000,state),stateBytes,wire.Manifest,definition);
        check(client.Receive(PacketType.CustomWeaponState,stateBytes,out _) && client.Submit(first,0)
            && client.Submit(first with { Sequence=2 },0),"input publisher starts from actual decoded canonical client projection and bounded pending controls");
        client.Sent(first,0); client.Sent(first with { Sequence=2 },0);
        check(!client.TryNextControl(.01,out _) && client.TryInputControl(first.Owner,.01,out var pending) && pending==first,
            "input publisher ignores reliable resend due time and selects oldest unconsumed primary control");
        check(!client.TryInputControl(new(first.Owner.MatchId,first.Owner.AuthorityEpoch,first.Owner.Slot,first.Owner.Generation,(ushort)(first.Life+1)),.01,out _)
            && client.TryInputControl(first.Owner,.02,out pending) && pending==first,"wrong life cannot borrow pending input control or consume its nonce");
        var denied=new CustomControlDenied(first,1,201,CustomControlDenialReason.SourceUnavailable);var denial=new byte[CustomControlDenied.Size];denied.Write(denial);
        check(client.Receive(PacketType.CustomWeaponControlDenied,denial,out _) && client.TryInputControl(first.Owner,.03,out pending)
            && pending.Sequence==2,"only application result consumption advances piggyback selection to next queued request");
        check(client.Submit(first with { Sequence=3 },.03) && client.TryInputControl(first.Owner,.04,out pending) && pending.Sequence==2,
            "reused pending-array hole cannot send newer nonce ahead of older unconsumed control");
        check(!client.TryInputControl(first.Owner,15.001,out _) && client.Invalidated,"input carrier repetition cannot refresh fixed application deadline");
        var box=new CustomActionOutbox(wire,definition);box.Reserve(first,10,0);
        check(box.ContainsControl(first) && !box.ContainsControl(first with { Optic=2 }) && !box.ContainsControl(first with { Sequence=2 }),
            "quota bypass recognizes only exact retained request payload, never another nonce or contradictory optic");
        for (int i=0;i<10000;i++) { CustomInputCarrier.TryRead(bytes,out _,out _);CustomInputCarrier.TryWrite(request,native,bytes,out _);box.ContainsControl(first); }
        long before=GC.GetAllocatedBytesForCurrentThread();bool valid=true;
        for (int i=0;i<100000;i++) valid &= CustomInputCarrier.TryRead(bytes,out _,out _) && CustomInputCarrier.TryWrite(request,native,bytes,out _) && box.ContainsControl(first);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && allocated==0,"100000 warmed carrier decode/write/exact-duplicate probes allocate zero bytes");
        Console.WriteLine($"INPUT CARRIER ALLOCATION:100000 warmed codec/retained-request probes={allocated}B");
    }
}
