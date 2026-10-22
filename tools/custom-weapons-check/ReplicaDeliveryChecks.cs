using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

// Pure recipient codecs/assembly/delivery. Values below are supplied fixtures,
// not engine source proof, authenticated UDP, private health commands or visuals.
internal static class ReplicaDeliveryChecks
{
    private const ulong Connection=0x1020304050607080;
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomReadinessWire Wire;
        internal readonly WorldBootstrapIdentity SourceBootstrap;
        internal readonly CustomReplicaSlot[] Slots=new CustomReplicaSlot[8];
        internal readonly uint World;
        internal Fixture(bool viewer=false,uint world=1001)
        {
            World=world;uint birth=world<100 ? uint.MaxValue-30:world<2000 ? 990:unchecked(world-20);
            var start=new MatchStartIdentity(7,0x123456789abcdef0,3);
            SourceBootstrap=new(start,2,5,birth,1,1);
            Wire=new(new(start,9,viewer?(ushort)77:(ushort)5,birth,1,1),7);
            for(byte i=0;i<8;i++)
            {
                var owner=new WeaponOwnerContext(7,start.AuthorityEpoch,i,5,10);
                var state=new WeaponStateSnapshot(owner,Definition.Identity,world,CustomWeaponPhase.Ready,4,16,1,0,0,0,0);
                Slots[i]=new(i,true,5,10,false,true,new(10),0,world,state);
            }
            if(viewer)Slots[7]=new(7,true,77,0,true,false,default,0,0,default);
        }
        internal CustomReplicaCapture Capture(int flights=0)=>new(7,SourceBootstrap.Start.AuthorityEpoch,Wire.Manifest.Digest,World,8,flights);
        internal CustomWeaponAction Action(uint sequence=1,byte slot=3,uint? world=null)
        {
            uint tick=world??unchecked(World-1);
            return new(SourceBootstrap,Wire.Manifest.Digest,slot,9,new(10),sequence,tick,tick,0,CustomWeaponActionKind.Fire,1,new(1,2.5f,3),Vector3.UnitZ);
        }
        internal CustomSourceCertificate Source(uint sequence=1,byte slot=3,uint? world=null)
        { var action=Action(sequence,slot,world);return new(action,action.SourceFrame,action.SourceFrame,0,new(1,2,3),Vector3.UnitZ,true,false,false); }
        internal CustomReplicaLaunch Launch(uint sequence=1,byte slot=3,uint? world=null)
        { var source=Source(sequence,slot,world);return new(source,source.ObservedWorldFrame,new(1,2.4f,3),new Vector3(.01f,0,1).Normalized()); }
        internal CustomReplicaFlight Flight(uint sequence=1,byte slot=3,uint? world=null)
        {
            var launch=Launch(sequence,slot,world);return new(launch.Identity,launch.Source,launch.World,unchecked(launch.World+8),launch.Origin,
                launch.Origin+Vector3.UnitZ,launch.Direction*200,launch.Direction,.02f,.08f);
        }
        internal CustomReplicaFlight[] Flights(int count)
        { var values=new CustomReplicaFlight[count];for(int i=0;i<count;i++) values[i]=Flight((uint)(i%8+1),(byte)(i/8));return values; }
        internal CustomContactOutcome Contact(uint sequence=1,byte slot=3,uint? world=null)
        {
            var action=Action(sequence,slot,world);return new(new(new(CustomActionIdentity.For(action),new(4,6,11),1,CustomImpactChannel.Body),new(1,2,6),false),
                unchecked(action.SourceFrame+1),new(200,135,0,0,9));
        }
        internal CustomActionReceipt Receipt(CustomEmissionDisposition emission,uint sequence=1)
        {
            var source=Source(sequence);var state=new WeaponRuntimeState(Definition,source.Action.Owner,source.Action.SourceFrame);
            state.SetEquipped(source.Action.SourceFrame,true);state.TrySetOptic(source.Action.SourceFrame,1,1);var before=state.Snapshot();state.TryFire(source.Action.SourceFrame,sequence,true);
            var after=state.Snapshot();var disposition=CustomJournalDisposition.Accepted;var result=WeaponActionResult.Accepted;
            if(emission==CustomEmissionDisposition.NoSpawn)
            { after=before;disposition=CustomJournalDisposition.NoSpawn;result=WeaponActionResult.CapacityUnavailable; }
            else if(emission==CustomEmissionDisposition.NotAttempted)
            { after=before;disposition=CustomJournalDisposition.ResourceDenied;result=WeaponActionResult.Cooldown; }
            else if(emission==CustomEmissionDisposition.Uncertain)
            { after=after with {Phase=CustomWeaponPhase.Invalid,Optic=0};disposition=CustomJournalDisposition.EngineFault;result=WeaponActionResult.InvalidLife; }
            return new(source.Action,World,disposition,result,emission,source,before,after);
        }
        internal CustomReplicaMailbox Mailbox()=>new(Wire,Connection,0);
    }
    private static byte[] Ack(Fixture f,CustomReplicaAck ack)
    { var bytes=new byte[CustomReplicaAck.Size];if(!CustomReplicaCodec.TryWriteAck(f.Wire,ack,bytes))throw new Exception("ACK fixture invalid");return bytes; }
    private static byte[][] Pages(Fixture f,CustomReplicaFlight[] flights,ulong id=1,ulong frontier=0)
    {
        var first=new CustomReplicaPage(id,frontier,f.World,(byte)flights.Length,0);var pages=new byte[first.PageCount][];
        for(byte i=0;i<pages.Length;i++)
        { var page=first with {Index=i};pages[i]=new byte[page.Length];if(!CustomReplicaCodec.TryWritePage(f.Wire,page,f.Slots,flights,pages[i],out _))throw new Exception("Page fixture invalid "+i); }
        return pages;
    }
    private static byte[] Event(Fixture f,CustomReplicaEvent value)
    { var scratch=new byte[1024];if(!CustomReplicaCodec.TryWriteEvent(f.Wire,value,scratch,out int size))throw new Exception("Event fixture invalid");return scratch[..size]; }
    private static CustomReplicaAck Baseline(Fixture f,CustomReplicaMailbox box,CustomReplicaReceiver receiver,CustomReplicaFlight[] flights,Action<bool,string> check)
    {
        check(box.BeginBaseline(f.Capture(flights.Length),f.Slots,flights,0),"observer reserves complete baseline and every flight terminal obligation before any page send");
        CustomReplicaAck final=default;
        for(int i=0;i<1+(flights.Length+3)/4;i++)
        {
            check(box.TryNext(0,out var type,out var bytes,out int key)&&type==PacketType.CustomWeaponReplicaPage&&key==-(i+1),"unsent baseline pages enumerate bounded stable keys");
            check(receiver.ReceivePage(bytes.Span,0,out var ack),"baseline receiver accepts exact sender page");if(ack.Id!=0)final=ack;
            check(box.MarkSent(key,0),"transport success explicitly marks one immutable baseline page sent");
        }
        check(receiver.IsComplete&&final.Baseline&&box.Acknowledge(Ack(f,final),Connection,0)&&box.IsReady,"only complete exact-digest sent baseline opens observer event stream");return final;
    }
    private static CustomReplicaMailbox Ready(Fixture f,Action<bool,string> check)
    { var box=f.Mailbox();Baseline(f,box,new(f.Wire,0),Array.Empty<CustomReplicaFlight>(),check);return box; }
    internal static void Run(Action<bool,string> check)
    {
        UnknownEmission(check);RetainedStance(check);CanonicalReceipts(check);WrapExpiry(check);Golden(check);CodecAndRecipients(check);AtomicAssembly(check);BaselineAcknowledgements(check);WorldsAndJoins(check);StatesAndFuzz(check);
        EventOrdering(check);CapacityAndDeadlines(check);PrivateIndependence(check);TokenWindow(check);Allocation(check);
    }
    private static void RetainedStance(Action<bool,string> check)
    {
        var f = new Fixture(); var original = f.Launch();
        var source = original.Source with { Crouched = true };
        source = source with { Action = source.Action with { Origin = source.MuzzleOrigin } };
        var expected = original with { Source = source, Origin = source.MuzzleOrigin };
        var bytes = Event(f,new(CustomReplicaKind.Launch,1,expected.World,expected,default));
        check(bytes.Length == CustomReplicaCodec.EventPrefix + 171 && bytes[CustomReplicaCodec.EventPrefix+142] == 9
            && CustomReplicaCodec.TryReadEvent(f.Wire,bytes,out var read) && read.Launch == expected
            && read.Launch.Source.Crouched && read.Launch.Source.MuzzleOrigin == expected.Origin,
            "replica launch roundtrips retained crouch in spare source bit3 at the unchanged171-byte width");
        var tucked = source with { Tucked = true };
        tucked = tucked with { Action = tucked.Action with { Origin = tucked.MuzzleOrigin } };
        var tuckedExpected = expected with { Source = tucked, Origin = tucked.MuzzleOrigin };
        var tuckedBytes = Event(f,new(CustomReplicaKind.Launch,1,tuckedExpected.World,tuckedExpected,default));
        check(tuckedBytes.Length == CustomReplicaCodec.EventPrefix + 171
            && tuckedBytes[CustomReplicaCodec.EventPrefix+142] == 25
            && CustomReplicaCodec.TryReadEvent(f.Wire,tuckedBytes,out var tuckedRead)
            && tuckedRead.Launch == tuckedExpected && tuckedRead.Launch.Source.Tucked
            && (tucked.MuzzleOrigin - source.MuzzleOrigin - Vector3.UnitY * ParkourStance.TuckRise).Length < .00001f,
            "replica launch roundtrips tuck in bit4 and applies the shared stance offset");
        var malformed = tuckedBytes.ToArray(); malformed[CustomReplicaCodec.EventPrefix+142] &= 0xf7;
        check(!CustomReplicaCodec.TryReadEvent(f.Wire,malformed,out _),
            "replica launch refuses tucked source without accepted crouch");
        bytes[CustomReplicaCodec.EventPrefix+142] |= 32;
        check(!CustomReplicaCodec.TryReadEvent(f.Wire,bytes,out _),"replica source bits above the declared stance/modifier mask refuse");
    }
    private static void UnknownEmission(Action<bool,string> check)
    {
        var f=new Fixture();var box=Ready(f,check);box.Prepare(f.Action(),0);
        check(!box.FinalizeReceipt(f.Receipt((CustomEmissionDisposition)255))&&box.Closed,
            "unknown receipt emission cannot silently free reserved evidence and must close observer only");
    }
    private static void CanonicalReceipts(Action<bool,string> check)
    {
        var f=new Fixture();
        foreach(var emission in new[] {CustomEmissionDisposition.NoSpawn,CustomEmissionDisposition.Uncertain,CustomEmissionDisposition.NotAttempted})
        {
            var receipt=f.Receipt(emission);
            check(CustomCombatResults.Valid(new CustomReceiptMessage(1,receipt),f.Wire.Manifest,f.Definition),
                "observer fixture uses canonical root-consistent receipt shape for "+emission);
            if(emission==CustomEmissionDisposition.NoSpawn)
                check(receipt.After==receipt.Before&&receipt.Disposition==CustomJournalDisposition.NoSpawn&&receipt.Result==WeaponActionResult.CapacityUnavailable,
                    "canonical definite no-spawn restores exact prior resources before observer cleanup");
            if(emission==CustomEmissionDisposition.Uncertain)
                check(receipt.After is {Phase:CustomWeaponPhase.Invalid,Optic:0,Magazine:3}&&receipt.Disposition==CustomJournalDisposition.EngineFault&&receipt.Result==WeaponActionResult.InvalidLife,
                    "canonical uncertainty retains spent round and invalidates resources instead of refunding");
        }
        check(!CustomCombatResults.Valid(new CustomReceiptMessage(1,f.Receipt((CustomEmissionDisposition)255)),f.Wire.Manifest,f.Definition),
            "unknown emission fixture remains intentionally invalid for negative observer contract regression");
    }
    private static void Golden(Action<bool,string> check)
    {
        // Independent Python struct.pack of literal recipient/empty-slot fields;
        // SHA256 is over that literal page, not a production writer's output.
        const string pageHex="010700f0debc9a7856341203000000090000004d00de0300000100000001000000a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e90701000000000000000000000000000000e90300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000000000000000000000000000000000000000000000200000000000000000000000000000000000000000000000000000000000000000000000000000000030000000000000000000000000000000000000000000000000000000000000000000000000000000004000000000000000000000000000000000000000000000000000000000000000000000000000000000500000000000000000000000000000000000000000000000000000000000000000000000000000000060000000000000000000000000000000000000000000000000000000000000000000000000000000007034d0000000000000000000000000000000000000000000000000000000000000000000000000000";
        const string ackHex="010700f0debc9a7856341203000000090000004d00de0300000100000001000000a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9070101000000000000000000000000000000717d7666204227c09b3d78b23508b186caa35cfe3b8a62bd68f739417e20c0b1";
        var f=new Fixture(viewer:true);
        for(byte i=0;i<7;i++) f.Slots[i]=new(i,false,0,0,false,false,default,0,0,default);
        var actual=Pages(f,Array.Empty<CustomReplicaFlight>())[0];var expected=Convert.FromHexString(pageHex);
        check(actual.SequenceEqual(expected),"independent empty Life0 spectator page golden verifies exact outer identity, fixed rows and wire offsets");
        var receiver=new CustomReplicaReceiver(f.Wire,0);
        check(receiver.ReceivePage(expected,0,out var ack)&&Ack(f,ack).SequenceEqual(Convert.FromHexString(ackHex)),
            "independent page SHA256 and115-byte exact recipient baseline acknowledgement golden agree");
    }
    private static void WrapExpiry(Action<bool,string> check)
    {
        uint launch=uint.MaxValue-7;var f=new Fixture(world:launch);var flight=f.Flight(world:launch);
        var baseline=f.Mailbox();Baseline(f,baseline,new(f.Wire,0),new[] {flight},check);
        baseline.Advance(0);int baselineAt0=baseline.Count;baseline.Advance(1);int baselineAt1=baseline.Count;
        var live=Ready(f,check);var action=f.Action(world:launch);check(live.Prepare(action,0)&&live.PublishLaunch(f.Launch(world:launch),0),"wrapped new flight reserves and publishes original launch");
        CustomReplicaEvent emitted=default;
        check(live.TryNext(0,out _,out var packet,out int key)&&CustomReplicaCodec.TryReadEvent(f.Wire,packet.Span,out emitted),"wrapped launch event decodes");
        var eventAck=new CustomReplicaAck(false,emitted.Token,0,CustomReplicaCodec.Hash(packet.Span));live.MarkSent(key,0);live.Acknowledge(Ack(f,eventAck),Connection,0);
        live.Advance(0);int liveAt0=live.Count;live.Advance(1);int liveAt1=live.Count;
        Console.WriteLine($"REPLICA WRAP DEADLINE: baseline@0={baselineAt0},@1={baselineAt1}; launch@0={liveAt0},@1={liveAt1}");
        check(flight.PaymentDeadline==0&&baselineAt0==1&&baselineAt1==0,"baseline original deadline0 is inclusive and expires atworld1 after uint wrap");
        check(liveAt0==1&&liveAt1==0,"new launch original deadline0 is known and expires atworld1 after uint wrap");
    }
    private static void CodecAndRecipients(Action<bool,string> check)
    {
        var f=new Fixture(viewer:true);var flight=f.Flight();var pages=Pages(f,new[] {flight});
        check(f.Wire.Bootstrap.SlotGeneration==77&&flight.Action.Owner.Generation==5
            &&CustomReplicaCodec.TryReadPage(f.Wire,pages[1],out var decoded)&&decoded.Index==1,
            "outer viewer bootstrap remains independent of original source generation/revision/life");
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];var receiver=new CustomReplicaReceiver(f.Wire,0);
        receiver.ReceivePage(pages[1],0,out _);receiver.ReceivePage(pages[0],0,out _);
        check(receiver.TryCopyBaseline(slots,flights,out var capture)&&capture.FlightCount==1&&slots[7].Life==0&&slots[7].Spectating
            &&!slots[7].HasWeapon&&slots[7].State==default&&flights[0]==flight&&slots[3].State.Owner.Life==10&&flights[0].Action.Owner.Life==9,
            "Life0 viewer invents no source owner while old source life remains beside new public state");
        var foreign=new CustomReadinessWire(f.Wire.Bootstrap with {Revision=10},f.Wire.Slot);
        check(!CustomReplicaCodec.TryReadPage(foreign,pages[0],out _)&&!CustomReplicaCodec.HeaderMatches(pages[1],foreign),"different recipient incarnation rejects source-valid baseline bytes");
        foreach(int offset in new[] {0,1,3,11,15,19,21,25,29,33,65})
        {var changed=pages[0].ToArray();changed[offset]^=1;check(!CustomReplicaCodec.TryReadPage(f.Wire,changed,out _),"every outer recipient bootstrap/manifest/slot dimension is fenced");}
        foreach(var bytes in pages)
        {
            for(int n=0;n<bytes.Length;n++)check(!CustomReplicaCodec.TryReadPage(f.Wire,bytes.AsSpan(0,n),out var absent)&&absent==default,"every truncated baseline page rejects atomically");
            check(!CustomReplicaCodec.TryReadPage(f.Wire,bytes.Concat(new byte[1]).ToArray(),out _),"page extra bytes rejected");
        }
        var eventBytes=Event(f,new(CustomReplicaKind.Launch,1,1000,f.Launch(),default));
        check(CustomReplicaCodec.TryReadEvent(f.Wire,eventBytes,out var launch)&&launch.Launch.Source.Action.Bootstrap==f.SourceBootstrap
            &&launch.Launch.Source.Action.Bootstrap!=f.Wire.Bootstrap&&launch.Launch.Direction!=launch.Launch.Source.Aim,"event preserves original source bootstrap and actual postspread ray inside outer recipient header");
        for(int n=0;n<eventBytes.Length;n++)check(!CustomReplicaCodec.TryReadEvent(f.Wire,eventBytes.AsSpan(0,n),out _),"every truncated launch event rejects");
        var unknown=eventBytes.ToArray();unknown[66]=255;check(!CustomReplicaCodec.TryReadEvent(f.Wire,unknown,out _),"unknown observer event kind refused");
        var badFlights=new[] {flight with {PaymentDeadline=1009},flight with {RemainingSeconds=6},flight with {Direction=Vector3.Zero},flight with {AgeSeconds=float.NaN},flight with {Action=default}};
        foreach(var bad in badFlights)check(!CustomReplicaCodec.ValidFlight(bad,f.Wire,1001),"bad original deadline/seconds/ray/identity cannot form a baseline flight");
    }
    private static void AtomicAssembly(Action<bool,string> check)
    {
        var f=new Fixture();var expected=f.Flights(64);var pages=Pages(f,expected);check(pages.Length==17&&pages.All(x=>x.Length<=964),"64flights fit bounded17pages with no datagram beyond964bytes");
        var receiver=new CustomReplicaReceiver(f.Wire,0);var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        var sentinel=new CustomReplicaSlot(7,true,99,77,true,false,default,0,0,default);Array.Fill(slots,sentinel);
        for(int i=16;i>=1;i--)
        {check(receiver.ReceivePage(pages[i],0,out var ack)&&ack==default&&!receiver.IsComplete,"reverse/reordered flight pages stage without partial activation or premature ACK");
            check(receiver.ReceivePage(pages[i],0,out _)&&!receiver.TryCopyBaseline(slots,flights,out var absent)&&absent==default&&slots.All(x=>x==sentinel),"exact duplicate staged page is idempotent with untouched output");}
        check(receiver.ReceivePage(pages[0],0,out var final)&&final.Baseline&&receiver.IsComplete&&receiver.TryCopyBaseline(slots,flights,out _)
            &&slots.SequenceEqual(f.Slots)&&flights.SequenceEqual(expected),"only final missing state page atomically installs all8slots+64flights");
        var concat=pages.SelectMany(x=>x).ToArray();var digest=WeaponDigest.Read(SHA256.HashData(concat));
        check(final.Digest==digest&&final.Id==1&&final.Frontier==0,"baseline ACK digest is SHA256 of canonical ordered full pages independent of arrival order");
        var shortSlots=Enumerable.Repeat(sentinel,7).ToArray();check(!receiver.TryCopyBaseline(shortSlots,flights,out var failed)&&failed==default&&shortSlots.All(x=>x==sentinel),"short destination cannot partially expose installed baseline");
        for(int missing=0;missing<17;missing++)
        {var partial=new CustomReplicaReceiver(f.Wire,0);for(int i=0;i<17;i++)if(i!=missing)partial.ReceivePage(pages[i],0,out _);
            check(!partial.IsComplete&&!partial.TryCopyState(slots,out _),"each individually missing page prevents public state activation");}
        var conflict=new CustomReplicaReceiver(f.Wire,0);conflict.ReceivePage(pages[0],0,out _);var changed=pages[0].ToArray();changed[CustomReplicaCodec.PagePrefix+16+5]=3;
        check(!conflict.ReceivePage(changed,0,out _)&&conflict.Invalidated&&!conflict.IsComplete,"changed valid same-index baseline page poisons assembly without partial activation");
        var duplicateIdentity=expected.ToArray();duplicateIdentity[63]=duplicateIdentity[0];var impossible=Pages(f,duplicateIdentity);var bad=new CustomReplicaReceiver(f.Wire,0);
        bool accepted=true;foreach(var bytes in impossible)accepted &= bad.ReceivePage(bytes,0,out _);
        check(!accepted&&bad.Invalidated&&!bad.IsComplete,"duplicate full source action across separate pages cannot activate twice");
        var timeout=new CustomReplicaReceiver(f.Wire,0);timeout.ReceivePage(pages[0],0,out _);check(!timeout.ReceivePage(pages[1],15.001,out _)&&timeout.Invalidated,"missing-page receiver deadline is fixed, bounded and never promotes a partial baseline");
    }
    private static void BaselineAcknowledgements(Action<bool,string> check)
    {
        var f=new Fixture();var box=f.Mailbox();var flights=f.Flights(64);check(box.BeginBaseline(f.Capture(64),f.Slots,flights,0)&&box.Count==64&&!box.IsReady,"baseline holds64terminal reservations before publication");
        var receiver=new CustomReplicaReceiver(f.Wire,0);CustomReplicaAck ack=default;
        for(int i=0;i<17;i++) {box.TryNext(0,out _,out var bytes,out int key);receiver.ReceivePage(bytes.Span,0,out var row);if(row.Id!=0)ack=row;
            if(i<16)box.MarkSent(key,0);else check(!box.Acknowledge(Ack(f,ack),Connection,0),"complete assembled digest cannot ACK an unsent final sender page");}
        check(!box.Acknowledge(Ack(f,ack),Connection+1,0)&&!box.Acknowledge(Ack(f,ack with {Id=2}),Connection,0)
            &&!box.Acknowledge(Ack(f,ack with {Frontier=1}),Connection,0)&&!box.Acknowledge(Ack(f,ack with {Digest=CustomReplicaCodec.Hash(new byte[]{1})}),Connection,0),"baseline ACK fences connection/id/frontier/exact ordered digest");
        check(box.MarkSent(-17,0)&&box.Acknowledge(Ack(f,ack),Connection,0)&&box.IsReady,"only all-sent exact baseline ACK activates observer delivery");
        check(!box.BeginBaseline(f.Capture(),f.Slots,Array.Empty<CustomReplicaFlight>(),0)&&box.Count==64,"same mailbox cannot replace pinned baseline or drop its outstanding contact reservations");
    }
    private static void WorldsAndJoins(Action<bool,string> check)
    {
        var f=new Fixture(viewer:true);var physical=f.Flight() with {SpawnPosition=new(9,8,7)};
        var pages=Pages(f,new[] {physical});var receiver=new CustomReplicaReceiver(f.Wire,0);
        foreach(var bytes in pages)receiver.ReceivePage(bytes,0,out _);
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        check(receiver.TryCopyBaseline(slots,flights,out _)&&flights[0]==physical,
            "noncanonical actual spawn position roundtrips exactly instead of reconstructing source-body muzzle");
        foreach(int field in new[] {147,175})
        {var mismatch=pages[1].ToArray();BinaryPrimitives.WriteSingleLittleEndian(mismatch.AsSpan(CustomReplicaCodec.PagePrefix+field),42);
            check(!CustomReplicaCodec.TryReadPage(f.Wire,mismatch,out _),"duplicated embedded origin and actual spawn position must agree exactly");}
        foreach(var bad in new[] {physical with {AgeSeconds=.2f},physical with {AgeSeconds=-.01f},physical with {RemainingSeconds=.01f},physical with {RemainingSeconds=float.NaN}})
            check(!CustomReplicaCodec.ValidFlight(bad,f.Wire,f.World),"age bounds and age+remaining must equal original lifetime in seconds");
        foreach(int mode in new[] {0,1,2,3})
        {
            var invalid=new Fixture(viewer:true);var badFlights=new[] {invalid.Flight()};
            if(mode==0)invalid.Slots[7]=new(7,false,0,0,false,false,default,0,0,default);
            if(mode==1)invalid.Slots[7]=invalid.Slots[7] with {Generation=78};
            if(mode==2)invalid.Slots[3]=invalid.Slots[3] with {Life=0,HasWeapon=false,Selected=default,SettledSequence=0,CommittedSourceFrame=0,State=default};
            if(mode==3)
            {var source=badFlights[0].Certificate;var action=source.Action with {Life=11};source=source with {Action=action};badFlights[0]=badFlights[0] with {Action=CustomActionIdentity.For(action),Certificate=source};}
            var box=invalid.Mailbox();check(!box.BeginBaseline(invalid.Capture(1),invalid.Slots,badFlights,0)&&box.Count==0,
                "recipient occupant/generation and source current-life ordering are validated before reserving or publishing baseline");
            var malformed=Pages(invalid,badFlights);var assembler=new CustomReplicaReceiver(invalid.Wire,0);bool accepted=true;
            foreach(var bytes in malformed)accepted &= assembler.ReceivePage(bytes,0,out _);
            check(!accepted&&assembler.Invalidated&&!assembler.IsComplete,"cross-page recipient/source lifecycle join cannot activate invalid baseline");
        }
        var stale=pages[0].ToArray();BinaryPrimitives.WriteUInt32LittleEndian(stale.AsSpan(82),989);
        check(!CustomReplicaCodec.TryReadPage(f.Wire,stale,out _),"outer baseline world cannot predate recipient bootstrap despite valid nested source");
        var wrapped=new Fixture(world:0);var wrapPages=Pages(wrapped,Array.Empty<CustomReplicaFlight>());var wrapReceiver=new CustomReplicaReceiver(wrapped.Wire,0);
        check(wrapReceiver.ReceivePage(wrapPages[0],0,out _)&&wrapReceiver.TryCopyBaseline(slots,flights,out var wrapCapture)&&wrapCapture.World==0,
            "authority world0 is legitimate across uint wrap, independent of absent/default output sentinel");
        var worldZeroEvent=Event(wrapped,new(CustomReplicaKind.Launch,1,0,wrapped.Launch(world:0),default));
        check(wrapReceiver.ReceiveEvent(worldZeroEvent,0,out var zeroAck)&&zeroAck.Id==1&&wrapReceiver.LastEvent?.World==0,
            "postwrap world0 observer event retains exact source and acknowledgement");
    }
    private static void StatesAndFuzz(Action<bool,string> check)
    {
        var f=new Fixture();var page=Pages(f,Array.Empty<CustomReplicaFlight>())[0];var receiver=new CustomReplicaReceiver(f.Wire,0);receiver.ReceivePage(page,0,out _);
        var bytes=new byte[CustomReplicaCodec.EventPrefix+8*CustomReplicaCodec.SlotSize];var changed=f.Slots.ToArray();changed[3]=changed[3] with {State=changed[3].State with {Magazine=3}};
        check(CustomReplicaCodec.TryWriteState(f.Wire,f.World,changed,bytes,out _,1)&&receiver.ReceiveEvent(bytes,0,out var initialAck)
            &&initialAck==default&&receiver.StateRevision==1,
            "higher public-state revision can install legitimate changed rows while authority world remains unchanged");
        changed[3]=changed[3] with {State=changed[3].State with {Magazine=2}};
        CustomReplicaCodec.TryWriteState(f.Wire,f.World,changed,bytes,out _,1);
        check(!receiver.ReceiveEvent(bytes,0,out _)&&receiver.Invalidated,"same state revision cannot contradict its immutable whole-table projection");
        receiver=new(f.Wire,0);receiver.ReceivePage(page,0,out _);var newer=f.Slots.ToArray();
        for(int i=0;i<8;i++)newer[i]=newer[i] with {State=newer[i].State with {Tick=1002},CommittedSourceFrame=1002};
        CustomReplicaCodec.TryWriteState(f.Wire,1002,newer,bytes,out _,2);var currentBytes=bytes.ToArray();
        check(receiver.ReceiveEvent(bytes,0,out var stateAck)&&stateAck==default&&receiver.DeliveredEvents==0&&receiver.StateRevision==2,
            "reliable state batch revision projects values without semantic event cue or application event ACK");
        CustomReplicaCodec.TryWriteState(f.Wire,1001,f.Slots,bytes,out _,1);receiver.ReceiveEvent(bytes,0,out _);var copy=new CustomReplicaSlot[8];
        check(receiver.TryCopyState(copy,out uint world)&&world==1002&&copy.SequenceEqual(newer)&&receiver.StateRevision==2,
            "older public-state revision cannot roll back newer whole-table projection");
        var regressed=new CustomReplicaReceiver(f.Wire,0);regressed.ReceivePage(page,0,out _);regressed.ReceiveEvent(currentBytes,0,out _);
        CustomReplicaCodec.TryWriteState(f.Wire,1001,f.Slots,bytes,out _,3);
        check(!regressed.ReceiveEvent(bytes,0,out _)&&regressed.Invalidated,"higher state revision with regressing authority world fails closed");
        CustomReplicaCodec.TryWriteState(f.Wire,1001,f.Slots,bytes,out _,1);
        var old=bytes.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(old.AsSpan(75),989);check(!CustomReplicaCodec.TryReadState(f.Wire,old,out _),"outer state world is fenced to recipient bootstrap");
        var eventBytes=Event(f,new(CustomReplicaKind.Launch,1,1000,f.Launch(),default));BinaryPrimitives.WriteUInt32LittleEndian(eventBytes.AsSpan(75),989);
        check(!CustomReplicaCodec.TryReadEvent(f.Wire,eventBytes,out _),"outer event world is fenced independently of nested launch world");
        var random=new Random(431);var fuzz=new byte[1100];bool safe=true;
        try
        {
            for(int i=0;i<3000;i++)
            {random.NextBytes(fuzz);int length=i%1100;var body=fuzz.AsSpan(0,length);CustomReplicaCodec.TryReadPage(f.Wire,body,out _);
                CustomReplicaCodec.TryReadEvent(f.Wire,body,out _);CustomReplicaCodec.TryReadState(f.Wire,body,out _);CustomReplicaCodec.TryReadAck(f.Wire,body,out _);}
            for(int i=0;i<page.Length;i++){var mutation=page.ToArray();mutation[i]^=255;CustomReplicaCodec.TryReadPage(f.Wire,mutation,out _);}
        }
        catch{safe=false;}
        check(safe,"3000 bounded random payloads and every page-byte mutation fail or decode without throwing");
        var untouched=Enumerable.Repeat((byte)0xcc,1024).ToArray();
        check(!CustomReplicaCodec.TryWritePage(f.Wire,default,f.Slots,Array.Empty<CustomReplicaFlight>(),untouched,out int wrote)&&wrote==0&&untouched.All(x=>x==0xcc),
            "default invalid page cannot partially overwrite caller output");
        var wrapped=new Fixture(world:0);CustomReplicaCodec.TryWriteState(wrapped.Wire,0,wrapped.Slots,bytes,out int lengthOut);
        check(CustomReplicaCodec.TryReadState(wrapped.Wire,bytes.AsSpan(0,lengthOut),out uint zero)&&zero==0,"postwrap state world0 remains distinct from decoder failure");
    }
    private static void EventOrdering(Action<bool,string> check)
    {
        var f=new Fixture();var box=Ready(f,check);var receiver=new CustomReplicaReceiver(f.Wire,0);foreach(var page in Pages(f,Array.Empty<CustomReplicaFlight>()))receiver.ReceivePage(page,0,out _);
        var action=f.Action();check(box.Prepare(action,0)&&box.HasReservation(action)&&box.PublishContact(f.Contact(),0)&&!box.TryNext(0,out _,out _,out _),"catch-up contact stages before launch with reserved buffers and no visible premature terminal");
        ReadOnlyMemory<byte> launch=default;int key=0;
        check(box.PublishLaunch(f.Launch(),0)&&box.TryNext(0,out _,out launch,out key),"actual postspread launch publishes immutable source fact before staged contact");
        var launchBytes=launch.ToArray();check(receiver.ReceiveEvent(launchBytes,0,out var ack)&&receiver.DeliveredEvents==1&&!box.Acknowledge(Ack(f,ack),Connection,0),"unsent observer event cannot accept even exact recipient token+digest ACK");
        ReadOnlyMemory<byte> retry=default;
        check(box.MarkSent(key,0)&&!box.TryNext(.1,out _,out _,out _)&&box.TryNext(.25,out _,out retry,out int retryKey)
            &&retry.Span.SequenceEqual(launchBytes)&&retryKey==key,"withheld application ACK retries exact launch without emitting staged contact");
        check(receiver.ReceiveEvent(retry.Span,.25,out var repeated)&&repeated==ack&&receiver.DeliveredEvents==1,"launch retry ACK is idempotent and never creates another presentation cue");
        ReadOnlyMemory<byte> contact=default;int contactKey=0;
        check(box.Acknowledge(Ack(f,ack),Connection,.25)&&box.TryNext(.25,out _,out contact,out contactKey)
            &&CustomReplicaCodec.TryReadEvent(f.Wire,contact.Span,out var terminal)&&terminal.Kind==CustomReplicaKind.Contact
            &&terminal.Contact.Bootstrap==f.SourceBootstrap&&terminal.Contact.Outcome==f.Contact(),"only launch application ACK releases original source-life terminal event");
        check(receiver.ReceiveEvent(contact.Span,.25,out var contactAck)&&receiver.DeliveredEvents==2
            &&receiver.ReceiveEvent(contact.Span,.25,out _)&&receiver.DeliveredEvents==2,"terminal contact is delivered at most once and repeated bytes receive exact ACK");
        check(!box.Acknowledge(Ack(f,contactAck),Connection,.25)&&box.MarkSent(contactKey,.25)
            &&box.Acknowledge(Ack(f,contactAck),Connection,.25)&&box.Count==0,"contact ACK is sent-only and independently releases final obligation");
        var changed=launchBytes.ToArray();BinaryPrimitives.WriteSingleLittleEndian(changed.AsSpan(CustomReplicaCodec.EventPrefix+147),2);
        check(!receiver.ReceiveEvent(changed,.25,out _)&&receiver.Invalidated,"same observer token with another valid payload invalidates instead of replaying or replacing a cue");
    }
    private static void CapacityAndDeadlines(Action<bool,string> check)
    {
        var f=new Fixture();var box=Ready(f,check);
        for(uint seq=1;seq<=256;seq++)check(box.Prepare(f.Action(seq),0),"fixed observer buffer admits one bounded pre-effect reservation");
        check(box.Count==256&&!box.CanPrepare(f.Action(257))&&!box.Prepare(f.Action(257),0)&&box.Prepare(f.Action(),14),"capacity256 refuses new effects without eviction and exact duplicate cannot refresh creation deadline");
        check(!box.HasExpired(15)&&box.HasExpired(15.001),"observer retry deadline is inclusive15s and pinned to first reservation");
        var miss=Ready(f,check);miss.Prepare(f.Action(),0);miss.PublishLaunch(f.Launch(),0);miss.Advance(1008);
        check(miss.Count==1,"miss retains contact reservation at exact original deadline");miss.Advance(1009);
        for(int i=0;i<1000;i++)miss.Advance(1010);
        var owed=(int)typeof(CustomReplicaMailbox).GetField("_owed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(miss)!;
        check(owed==0&&miss.Count==1&&miss.CanPrepare(f.Action(2))&&!miss.PublishContact(f.Contact(),0),"expired miss releases owed contact once across repeatedAdvance while original launch ACK remains outstanding");
        miss.TryNext(0,out _,out var packet,out int key);CustomReplicaCodec.TryReadEvent(f.Wire,packet.Span,out var value);miss.MarkSent(key,0);
        check(miss.Acknowledge(Ack(f,new(false,value.Token,0,CustomReplicaCodec.Hash(packet.Span))),Connection,0)&&miss.Count==0,"late launch ACK retires expired miss without reset deadline or fabricated terminal");
        var contradicted=Ready(f,check);contradicted.Prepare(f.Action(),0);contradicted.PublishLaunch(f.Launch(),0);
        check(!contradicted.PublishLaunch(f.Launch() with {Origin=new(9,8,7)},0)&&contradicted.Closed,"changed valid publication under same action closes observer instead of replacing immutable source");
        var missingLaunch=Ready(f,check);missingLaunch.Prepare(f.Action(),0);
        check(!missingLaunch.FinalizeReceipt(f.Receipt(CustomEmissionDisposition.Spawned))&&missingLaunch.Closed,"spawned receipt without reserved published launch closes observer rather than releasing evidence");
        var noSpawn=Ready(f,check);noSpawn.Prepare(f.Action(),0);check(noSpawn.FinalizeReceipt(f.Receipt(CustomEmissionDisposition.NoSpawn))&&noSpawn.Count==0,"immutable accepted no-spawn receipt releases reserved observer group before any publication");
        var uncertain=Ready(f,check);uncertain.Prepare(f.Action(),0);check(!uncertain.FinalizeReceipt(f.Receipt(CustomEmissionDisposition.Uncertain))&&uncertain.Closed,"uncertain source effects close only observer stream instead of deleting or reissuing evidence");
    }
    private static void PrivateIndependence(Action<bool,string> check)
    {
        var f=new Fixture();using var registry=new WeaponRegistry(true,new[] {f.Definition});using var payments=new CustomLaunchPayments(f.Action().Owner,f.Wire.Manifest,registry,1000,2);
        var receipt=f.Receipt(CustomEmissionDisposition.Spawned);var accepted=new CustomAcceptedLaunch(f.Definition,receipt.Source!.Value,receipt.Before!.Value,receipt.After!.Value);
        check(payments.TryBeginLaunch(accepted,1000,out var launchLease,out var source)&&source!=null,"actual independent private payment store reserves its immutable source before observer closure");
        using(launchLease)
        {var observer=Ready(f,check);observer.Prepare(f.Action(),0);observer.Close();check(observer.Closed&&!observer.HasReservation(f.Action())&&payments.CommitEmitted(launchLease),"closing observer buffers cannot invalidate private launch lease or refund its ammo transition");}
        var contact=f.Contact().Contact;
        check(payments.TryBeginContact(source!,contact,1001,out var contactLease,out _)==CustomContactDecision.Reserved,"private canonical contact remains reservable after independent observer retirement");
        using(contactLease)check(payments.CompleteContact(contactLease,new(200,135,0,0,9),out var outcome)&&outcome.Health.BodyRemoved==65,"observer retirement cannot suppress private exactly-once canonical outcome");
        check(payments.TryBeginContact(source!,contact,1001,out _,out var prior)==CustomContactDecision.Duplicate&&prior.Health.BodyRemoved==65,"observer-independent private duplicate returns retained outcome without another payment");
    }
    private static void TokenWindow(Action<bool,string> check)
    {
        var f=new Fixture();var box=Ready(f,check);typeof(CustomReplicaMailbox).GetField("_token",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(box,ulong.MaxValue-1);
        check(!box.CanPrepare(f.Action())&&!box.Prepare(f.Action(),0)&&box.Frontier==ulong.MaxValue-1&&box.Count==0,"launch+contact reservation refuses before token arithmetic can wrap");
        var span=Ready(f,check);span.Prepare(f.Action(),0);span.PublishLaunch(f.Launch(),0);
        typeof(CustomReplicaMailbox).GetField("_token",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(span,1024UL);
        check(!span.CanPrepare(f.Action(2))&&span.Count==1&&span.HasReservation(f.Action()),"explicit frontier boundary cannot overrun1024-token span while oldest launch remains unacknowledged");
        var receiver=new CustomReplicaReceiver(f.Wire,0);foreach(var page in Pages(f,Array.Empty<CustomReplicaFlight>()))receiver.ReceivePage(page,0,out _);
        var jump=Event(f,new(CustomReplicaKind.Launch,1025,1000,f.Launch(),default));check(!receiver.ReceiveEvent(jump,0,out _)&&receiver.DeliveredEvents==0&&!receiver.Invalidated,"token jump beyond1024cannot erase bounded dedupe evidence");
        var first=Event(f,new(CustomReplicaKind.Launch,1,1000,f.Launch(),default));check(receiver.ReceiveEvent(first,0,out _),"first postbaseline token accepted");
        for(ulong token=2;token<=1025;token++){var bytes=Event(f,new(CustomReplicaKind.Launch,token,1000,f.Launch((uint)token),default));if(!receiver.ReceiveEvent(bytes,0,out _))throw new Exception("Contiguous token fixture failed");}
        check(!receiver.ReceiveEvent(first,0,out _)&&receiver.DeliveredEvents==1025,"old token outside bounded window is rejected rather than treated as a fresh cue");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var f=new Fixture(viewer:true);var pages=Pages(f,new[] {f.Flight()});var receiver=new CustomReplicaReceiver(f.Wire,0);foreach(var page in pages)receiver.ReceivePage(page,0,out _);
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];var bytes=Event(f,new(CustomReplicaKind.Launch,1,1000,f.Launch(),default));receiver.ReceiveEvent(bytes,0,out _);
        for(int i=0;i<10000;i++){receiver.ReceivePage(pages[0],0,out _);receiver.ReceiveEvent(bytes,0,out _);receiver.TryCopyBaseline(slots,flights,out _);}
        long before=GC.GetAllocatedBytesForCurrentThread();bool valid=true;
        for(int i=0;i<100000;i++)valid &= receiver.ReceivePage(pages[0],0,out _)&&receiver.ReceiveEvent(bytes,0,out _)&&receiver.TryCopyBaseline(slots,flights,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;check(valid&&allocated==0&&receiver.DeliveredEvents==1,"100000 warmed page/event duplicate+atomiccopy probes allocate0B and produce no second cue");
        Console.WriteLine($"REPLICA DELIVERY ALLOCATION:100000 warmed duplicate/assembly/copy probes={allocated}B");
    }
}
