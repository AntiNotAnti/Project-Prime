using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

// Pure public-item codec/receiver/mailbox contracts. No sockets, Scene contact,
// resource grant, authenticated delivery, rendered pickup or private nonce proof.
internal static class CustomPickupReplicaChecks
{
    // Independently packed with Python struct '<HQIIHIII' for the bootstrap,
    // '<BBIQHQIBQ' for the state tail, and hashlib.sha256(state) for the ACK.
    private const string StateGolden = "01341208070605040302014433221188776655AA997856341240302010807060500102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F4001028056341208070605040302013412181716151413121190563412002827262524232221";
    private const string AckGolden = "01341208070605040302014433221188776655AA997856341240302010807060500102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F40010807060504030201147176E803B83BBC0A292CF23256F8EF870DE67FCAE864F4C05FDD4C6398BB28";
    private sealed class Fixture : IDisposable
    {
        internal readonly WeaponRegistry Registry;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly CustomPickupLayout Layout;
        internal readonly CustomPickupWorld World;
        internal readonly WorldBootstrapIdentity Bootstrap;
        internal Fixture(int count=2,uint world=100)
        {
            var definitions=new[]{WeaponDefinition.Create(Guid.Parse("00000001-0000-0000-0000-000000000001"),"Replica A",MechanicsChecks.Rules())};
            Registry=new(true,definitions);Manifest=new(definitions);
            var placements=Enumerable.Range(0,count).Select(i=>new CustomPickupPlacement((ushort)(i+1),new(10),i*4096,0,0,0,1800,16));
            Layout=new(MapContentIdentity.BuiltIn("MP1 SANCTORUS"),Manifest,Registry,placements);
            World=new(new(0x1234,0x0102030405060708,0x11223344),Manifest.Digest,Layout.Digest);
            Bootstrap=new(World.Start,0x55667788,0x99AA,world,0x10203040,0x50607080);
        }
        internal CustomPickupItemState Item(int index=0,ulong incarnation=1,ulong revision=0,uint ready=100,bool available=true)
            =>new(new(World,Layout.Placements[index].SpawnId,incarnation),ready,available,revision);
        internal CustomPickupReplicaState State(int index=0,ulong serial=1,uint world=100,CustomPickupItemState? item=null)
            =>new(Bootstrap,World,(byte)index,(byte)Layout.Count,world,serial,item??Item(index));
        public void Dispose()=>Registry.Dispose();
    }
    private static byte[] Encode(CustomPickupReplicaState state)
    {var bytes=new byte[CustomPickupReplicaCodec.StateSize];CustomPickupReplicaCodec.Write(state,bytes);return bytes;}
    private static byte[] Encode(CustomPickupReplicaAck ack)
    {var bytes=new byte[CustomPickupReplicaCodec.AckSize];CustomPickupReplicaCodec.Write(ack,bytes);return bytes;}
    private static bool Throws(Action action)
    {try{action();return false;}catch(ArgumentException){return true;}}
    internal static void Run(Action<bool,string> Check)
    {
        int count=0;void Verify(bool value,string label){Check(value,"pickup replica: "+label);count++;}
        Golden(Verify);Malformed(Verify);Receiver(Verify);Mailbox(Verify);BoundedAndAllocation(Verify);
        Console.WriteLine($"Public pickup replica contracts passed: {count} assertions; pure state/ACK fixtures, no live pickup activation claim.");
    }
    private static void Golden(Action<bool,string> check)
    {
        var boot=new WorldBootstrapIdentity(new(0x1234,0x0102030405060708,0x11223344),0x55667788,0x99AA,0x12345678,0x10203040,0x50607080);
        var manifest=WeaponDigest.Read(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
        var layout=WeaponDigest.Read(Enumerable.Range(33,32).Select(i=>(byte)i).ToArray());
        var world=new CustomPickupWorld(boot.Start,manifest,layout);
        var state=new CustomPickupReplicaState(boot,world,1,2,0x12345680,0x0102030405060708,
            new(new(world,0x1234,0x1112131415161718),0x12345690,false,0x2122232425262728));
        byte[] bytes=Encode(state),golden=Convert.FromHexString(StateGolden);
        check(CustomPickupReplicaCodec.StateSize==134&&CustomPickupReplicaCodec.AckSize==138
            &&bytes.AsSpan().SequenceEqual(golden),"134-byte state matches independently packed golden including every field");
        check(CustomPickupReplicaCodec.TryReadState(golden,out var read)&&read==state,"golden state reads exact full identity and item");
        var ack=CustomPickupReplicaCodec.Ack(state,golden);byte[] ackBytes=Encode(ack);
        check(ackBytes.AsSpan().SequenceEqual(Convert.FromHexString(AckGolden)),"138-byte ACK matches independent SHA256 golden");
        check(CustomPickupReplicaCodec.TryReadAck(ackBytes,out var decoded)&&decoded==ack,"golden ACK roundtrips full recipient/world/index/serial/hash");
        var padded=Enumerable.Repeat((byte)0xCC,140).ToArray();CustomPickupReplicaCodec.Write(state,padded);
        check(padded.AsSpan(0,134).SequenceEqual(golden)&&padded.AsSpan(134).ToArray().All(b=>b==0xCC),"writer touches only declared public state length");
        check(Throws(()=>CustomPickupReplicaCodec.Write(default(CustomPickupReplicaState),new byte[134]))
            &&Throws(()=>CustomPickupReplicaCodec.Write(state,new byte[133]))
            &&Throws(()=>CustomPickupReplicaCodec.Write(default(CustomPickupReplicaAck),new byte[138])),"default/undersized writes refuse");
        foreach(var type in new[]{typeof(CustomPickupReplicaState),typeof(CustomPickupReplicaAck),typeof(CustomPickupItemState),typeof(CustomPickupWorld)})
            check(!type.GetProperties().Any(p=>p.Name.Contains("Knowledge")||p.Name.Contains("Nonce")||p.Name.Contains("Funding")||p.Name=="Owner"),
                "public value has no owner-resource/private-knowledge field: "+type.Name);
    }
    private static void Malformed(Action<bool,string> check)
    {
        byte[] state=Convert.FromHexString(StateGolden),ack=Convert.FromHexString(AckGolden);
        for(int n=0;n<state.Length;n++)check(!CustomPickupReplicaCodec.TryReadState(state.AsSpan(0,n),out var absent)&&absent==default,"truncated state default output "+n);
        for(int n=0;n<ack.Length;n++)check(!CustomPickupReplicaCodec.TryReadAck(ack.AsSpan(0,n),out var absent)&&absent==default,"truncated ACK default output "+n);
        check(!CustomPickupReplicaCodec.TryReadState(state.Concat(new byte[]{0}).ToArray(),out _)
            &&!CustomPickupReplicaCodec.TryReadAck(ack.Concat(new byte[]{0}).ToArray(),out _),"trailing bytes refuse exact widths");
        void BadState(string label,Action<byte[]> alter)
        {var bytes=state.ToArray();alter(bytes);check(!CustomPickupReplicaCodec.TryReadState(bytes,out var value)&&value==default,label);}
        void BadAck(string label,Action<byte[]> alter)
        {var bytes=ack.ToArray();alter(bytes);check(!CustomPickupReplicaCodec.TryReadAck(bytes,out var value)&&value==default,label);}
        BadState("schema version",b=>b[0]=2);BadState("zero match",b=>b.AsSpan(1,2).Clear());
        BadState("zero epoch",b=>b.AsSpan(3,8).Clear());BadState("zero start generation",b=>b.AsSpan(11,4).Clear());
        foreach(int at in new[]{15,21,25,29})BadState("zero bootstrap component "+at,b=>b.AsSpan(at,4).Clear());
        BadState("zero slot generation",b=>b.AsSpan(19,2).Clear());
        BadState("zero manifest",b=>b.AsSpan(33,32).Clear());BadState("zero layout",b=>b.AsSpan(65,32).Clear());
        BadState("index outside count",b=>b[97]=2);BadState("zero count",b=>b[98]=0);BadState("excess count",b=>b[98]=65);
        BadState("world older than bootstrap",b=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(99),0x12345677));
        BadState("zero serial",b=>b.AsSpan(103,8).Clear());BadState("zero spawn",b=>b.AsSpan(111,2).Clear());
        BadState("zero incarnation",b=>b.AsSpan(113,8).Clear());BadState("unknown availability",b=>b[125]=2);
        BadState("availability contradicts ready time",b=>b[125]=1);BadState("zero revision on consumed incarnation",b=>b.AsSpan(126,8).Clear());
        BadState("initial incarnation with nonzero revision",b=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(113),1));
        BadAck("ACK wrong schema",b=>b[0]=2);BadAck("ACK zero manifest",b=>b.AsSpan(33,32).Clear());
        BadAck("ACK zero layout",b=>b.AsSpan(65,32).Clear());BadAck("ACK index beyond capacity",b=>b[97]=64);
        BadAck("ACK zero serial",b=>b.AsSpan(98,8).Clear());BadAck("ACK zero digest",b=>b.AsSpan(106,32).Clear());
        var random=new Random(572024);byte[] fuzz=new byte[CustomPickupReplicaCodec.AckSize+1];
        for(int i=0;i<1000;i++)
        {
            random.NextBytes(fuzz);int length=random.Next(fuzz.Length+1);
            bool parsedState=CustomPickupReplicaCodec.TryReadState(fuzz.AsSpan(0,length),out var randomState);
            bool parsedAck=CustomPickupReplicaCodec.TryReadAck(fuzz.AsSpan(0,length),out var randomAck);
            check((parsedState||randomState==default)&&(parsedAck||randomAck==default),"bounded random readers do not throw and failures return defaults "+i);
        }
        using var f=new Fixture();var value=f.State();
        check(CustomPickupReplicaCodec.Matches(value,f.Layout),"actual frozen registry/layout resolves state");
        var wrongLayout=value.World with{Layout=WeaponDigest.Read(Enumerable.Repeat((byte)9,32).ToArray())};
        var wrongManifest=value.World with{Manifest=WeaponDigest.Read(Enumerable.Repeat((byte)8,32).ToArray())};
        foreach(var changed in new[]{value with{Count=1},
            value with{World=wrongLayout,Item=value.Item with{Key=value.Item.Key with{World=wrongLayout}}},
            value with{World=wrongManifest,Item=value.Item with{Key=value.Item.Key with{World=wrongManifest}}},
            value with{Item=value.Item with{Key=value.Item.Key with{SpawnId=99}}}})
            check(!CustomPickupReplicaCodec.Matches(changed,f.Layout),"layout/count/manifest/spawn substitution refuses frozen layout");
        check(Throws(()=>Encode(value with{World=value.World with{Start=value.World.Start with{MatchId=99}}})),
            "writer rejects bootstrap/world stream substitution");
    }
    private static void Receiver(Action<bool,string> check)
    {
        using var f=new Fixture();var receiver=new CustomPickupReplicaReceiver(f.Bootstrap,f.Layout);
        var second=f.State(1,2);var first=f.State();
        check(!receiver.IsReady&&!receiver.TryState(0,out var empty)&&empty==default,"partial baseline has no readable item");
        check(receiver.Receive(Encode(second),out var ack2)&&ack2.Index==1&&!receiver.IsReady&&!receiver.TryState(1,out _),"reordered first row stages without exposing partial baseline");
        var wrong=first with{Bootstrap=first.Bootstrap with{Revision=first.Bootstrap.Revision+1}};
        check(!receiver.Receive(Encode(wrong),out var absent)&&absent==default&&!receiver.IsReady,"foreign recipient does not fill missing row");
        check(receiver.Receive(Encode(first),out var ack1)&&receiver.IsReady&&receiver.TryState(0,out var a)&&a==first.Item
            &&receiver.TryState(1,out var b)&&b==second.Item,"all authored rows commit initial readable baseline atomically");
        check(receiver.Receive(Encode(first),out var retry)&&retry==ack1&&!receiver.Closed,"exact duplicate reACKs immutable fact");
        var collected=first with{Serial=3,AuthorityFrame=101,Item=f.Item(0,2,1,1901,false)};
        check(receiver.Receive(Encode(collected),out _)&&receiver.TryState(0,out var latest)&&latest==collected.Item,"new incarnation updates only its public item");
        check(!receiver.Receive(Encode(first),out var stale)&&stale==default&&receiver.TryState(0,out latest)&&latest==collected.Item,"stale serial cannot roll item back");
        var backdated=collected with{Serial=4,AuthorityFrame=100};
        check(!receiver.Receive(Encode(backdated),out _)&&receiver.TryState(0,out latest)&&latest==collected.Item,"new serial with backdated world refuses without mutation");
        var respawn=collected with{Serial=4,AuthorityFrame=1901,Item=collected.Item with{Available=true}};
        check(receiver.Receive(Encode(respawn),out _)&&receiver.TryState(0,out latest)&&latest.Available,"fixed ready tick permits availability without inventing a new incarnation/revision");
        var contradiction=respawn with{Item=respawn.Item with{ReadyAt=1900}};
        check(!receiver.Receive(Encode(contradiction),out var conflict)&&conflict==default&&receiver.Closed&&!receiver.TryState(0,out _),"same serial changed bytes poison receiver and hide all items");
        var strict=new CustomPickupReplicaReceiver(f.Bootstrap,f.Layout);strict.Receive(Encode(collected),out _);
        check(!strict.Receive(Encode(collected with{Serial=4,Item=collected.Item with{ReadyAt=1902}}),out _),"higher serial cannot retime same incarnation/revision");
        var strict2=new CustomPickupReplicaReceiver(f.Bootstrap,f.Layout);strict2.Receive(Encode(collected),out _);
        check(!strict2.Receive(Encode(collected with{Serial=4,Item=collected.Item with{Key=collected.Item.Key with{Incarnation=3}}}),out _),"new incarnation requires newer item revision");
        using var wrapped=new Fixture(world:uint.MaxValue-2);var wr=new CustomPickupReplicaReceiver(wrapped.Bootstrap,wrapped.Layout);
        var before=wrapped.State(serial:1,world:uint.MaxValue-2,item:wrapped.Item(ready:1,available:false));
        check(wr.Receive(Encode(before),out _)&&wr.Receive(Encode(before with{Serial=2,AuthorityFrame=1,Item=before.Item with{Available=true}}),out _),"world and ready-time serial arithmetic crosses uint wrap");
    }
    private static void Mailbox(Action<bool,string> check)
    {
        using var f=new Fixture();var box=new CustomPickupReplicaMailbox(f.Bootstrap,f.World,f.Layout);
        check(!box.IsReady&&box.CanChange(0)&&!box.CanChange(-1)&&!box.CanChange(2),"mailbox begins empty with exact authored capacity");
        int index=0;ReadOnlyMemory<byte> memory=default;
        check(box.Publish(0,f.Item(),100,0)&&!box.CanChange(0)&&box.TryNext(0,out index,out memory)&&index==0,"publication reserves immutable per-item fact before send");
        byte[] original=memory.ToArray();CustomPickupReplicaCodec.TryReadState(original,out var state);
        var ack=CustomPickupReplicaCodec.Ack(state,original);
        check(!box.Acknowledge(Encode(ack)),"application ACK cannot free an unsent fact");
        check(!box.Publish(0,f.Item(0,2,1,1901,false),101,.01)&&box.Publish(0,f.Item(),100,.1)
            &&box.TryNext(.1,out _,out memory)&&memory.Span.SequenceEqual(original),"backpressure refuses overwrite while exact publish retry preserves bytes/serial");
        box.Sent(0,.1);
        check(!box.TryNext(.349,out _,out _)&&box.TryNext(.35,out index,out memory)&&index==0&&memory.Span.SequenceEqual(original),"quarter-second resend keeps exact original fact");
        var wrongDigest=WeaponDigest.Read(Enumerable.Repeat((byte)7,32).ToArray());
        foreach(var forged in new[]{ack with{Serial=ack.Serial+1},ack with{Index=1},ack with{Digest=wrongDigest},
            ack with{Bootstrap=ack.Bootstrap with{Revision=ack.Bootstrap.Revision+1}},ack with{World=ack.World with{Layout=wrongDigest}}})
            check(!box.Acknowledge(Encode(forged))&&!box.CanChange(0),"ACK requires sent exact bootstrap/world/index/serial/payload hash");
        check(!box.HasExpired(15)&&box.HasExpired(15.00001),"unacknowledged deadline is inclusive fifteen seconds and duplicate publication does not refresh it");
        check(box.Acknowledge(Encode(ack))&&box.Acknowledge(Encode(ack))&&box.CanChange(0)&&!box.IsReady,"exact ACK is idempotent and readiness still waits every authored item");
        check(box.Publish(1,f.Item(1),100,.4)&&box.TryNext(.4,out index,out memory)&&index==1,"second authored item reserves its own fact");
        var secondBytes=memory.ToArray();CustomPickupReplicaCodec.TryReadState(secondBytes,out var second);
        box.Sent(1,.4);check(box.Acknowledge(Encode(CustomPickupReplicaCodec.Ack(second,secondBytes)))&&box.IsReady,"all exact sent ACKs open recipient baseline");
        check(box.Publish(0,f.Item(0,2,1,1901,false),101,.5)&&!box.IsReady&&!box.Acknowledge(Encode(ack)),"next incarnation backpressures readiness and old ACK cannot release new fact");
        box.TryNext(.5,out _,out memory);check(!memory.Span.SequenceEqual(original),"new item fact gets independent increasing serial");
        var join=new CustomPickupReplicaMailbox(f.Bootstrap with{Revision=f.Bootstrap.Revision+1},f.World,f.Layout);
        check(join.Publish(0,f.Item(0,2,1,1901,false),101,0)&&join.Publish(1,f.Item(1),101,0)&&!join.IsReady
            &&!join.Acknowledge(Encode(ack)),"late recipient receives current incarnations with independent bootstrap/ACK obligations");
        var exhausted=new CustomPickupReplicaMailbox(f.Bootstrap,f.World,f.Layout);
        check(exhausted.Publish(0,f.Item(),100,0)&&exhausted.TryNext(0,out _,out memory),"serial-exhaustion fixture publishes actual original fact");
        byte[] retained=memory.ToArray();CustomPickupReplicaCodec.TryReadState(retained,out var retainedState);
        byte[] retainedAck=Encode(CustomPickupReplicaCodec.Ack(retainedState,retained));
        exhausted.Sent(0,0);check(exhausted.Acknowledge(retainedAck)&&exhausted.CanChange(0),"serial-exhaustion fixture ACKs original fact before exhaustion");
        // Declared bounded-counter seam; no lifetime-worth of synthetic packets.
        typeof(CustomPickupReplicaMailbox).GetField("_serial",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
            .SetValue(exhausted,ulong.MaxValue);
        check(!exhausted.CanChange(0)&&!exhausted.CanChange(1)
            &&!exhausted.Publish(0,f.Item(0,2,1,1901,false),101,1)
            &&!exhausted.Publish(1,f.Item(1),100,1)
            &&memory.Span.SequenceEqual(retained)&&exhausted.Acknowledge(retainedAck)
            &&!exhausted.TryNext(1,out _,out _),
            "serial exhaustion refuses pre-funding headroom and publication without overwriting retained bytes/ACK identity");
        var closed=new CustomPickupReplicaMailbox(f.Bootstrap,f.World,f.Layout);closed.Close();
        check(!closed.CanChange(0)&&!closed.Publish(0,f.Item(),100,0)&&!closed.TryNext(0,out _,out _)
            &&!closed.Acknowledge(Encode(ack))&&!closed.IsReady,"closed mailbox cannot publish/send/ACK/reopen");
    }
    private static void BoundedAndAllocation(Action<bool,string> check)
    {
        using var f=new Fixture(64);var receiver=new CustomPickupReplicaReceiver(f.Bootstrap,f.Layout);
        var box=new CustomPickupReplicaMailbox(f.Bootstrap,f.World,f.Layout);
        for(int i=0;i<64;i++)
        {
            int index=0;ReadOnlyMemory<byte> bytes=default;
            check(box.Publish(i,f.Item(i),100,0)&&box.TryNext(0,out index,out bytes)&&index==i,"bounded64-item baseline row "+i);
            check(receiver.Receive(bytes.Span,out var ack),"bounded64-item receiver stage "+i);
            box.Sent(i,0);check(box.Acknowledge(Encode(ack)),"bounded64-item sent exact ACK "+i);
        }
        check(receiver.IsReady&&box.IsReady&&Enumerable.Range(0,64).All(i=>receiver.TryState(i,out _)),"all64 rows readable only after complete baseline");
        byte[] hot=Encode(f.State());receiver.Receive(hot,out var hotAck);byte[] ackBytes=Encode(hotAck);
        for(int i=0;i<1000;i++){receiver.Receive(hot,out _);CustomPickupReplicaCodec.TryReadState(hot,out _);CustomPickupReplicaCodec.TryReadAck(ackBytes,out _);box.Acknowledge(ackBytes);}
        long before=GC.GetAllocatedBytesForCurrentThread();bool okay=true;
        for(int i=0;i<100000;i++)okay&=receiver.Receive(hot,out _)&&CustomPickupReplicaCodec.TryReadState(hot,out _)
            &&CustomPickupReplicaCodec.TryReadAck(ackBytes,out _)&&box.Acknowledge(ackBytes)&&receiver.TryState(i%64,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(okay&&allocated==0,"100k warmed readers/duplicate receive/sent exact ACK/lookup allocate zero bytes");
    }
}
