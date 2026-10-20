using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Supplied pure presentation facts. No transport authentication, health mutation
// or engine source proof is supplied by these fixtures.
internal static class ReplicaConsumerChecks
{
    private sealed class Fixture
    {
        internal readonly CustomReadinessWire Wire;
        internal readonly CustomReplicaSlot[] Slots=new CustomReplicaSlot[8];
        internal readonly CustomReplicaFlight[] Flights=new CustomReplicaFlight[64];
        internal readonly CustomReplicaConsumer Consumer;
        internal readonly byte[] Page;
        private readonly WorldBootstrapIdentity _source;
        internal Fixture()
        {
            var start=new MatchStartIdentity(7,0x123456789abcdef0,3);
            _source=new(start,2,5,990,1,1);Wire=new(new(start,9,77,990,1,1),7);
            for(byte i=0;i<8;i++)Slots[i]=new(i,false,0,0,false,false,default,0,0,default);
            Slots[7]=new(7,true,77,0,true,false,default,0,0,default);
            var owner=new WeaponOwnerContext(7,start.AuthorityEpoch,1,5,10);
            var state=new WeaponStateSnapshot(owner,Wire.Definition.Identity,1001,CustomWeaponPhase.Ready,4,16,1,0,0,0,0);
            Slots[1]=new(1,true,5,10,false,true,new(10),0,1001,state);
            var page=new CustomReplicaPage(1,0,1001,0,0);Page=new byte[page.Length];
            if(!CustomReplicaCodec.TryWritePage(Wire,page,Slots,Flights,Page,out _))throw new Exception("Consumer baseline fixture invalid.");
            Consumer=new(Wire,0);
        }
        internal byte[] Event(ulong token,bool contact=false)
        {
            var action=new CustomWeaponAction(_source,Wire.Manifest.Digest,1,9,new(10),(uint)token,1000,999,0,
                CustomWeaponActionKind.Fire,1,new(1,2.5f,3),Vector3.UnitZ);
            var source=new CustomSourceCertificate(action,1000,999,0,new(1,2,3),Vector3.UnitZ,true,false,false);
            var launch=new CustomReplicaLaunch(source,1000,new(1,2.4f,3),Vector3.UnitZ);
            var outcome=new CustomContactOutcome(new(new(CustomActionIdentity.For(action),new(2,6,11),1,CustomImpactChannel.Body),new(1,2,6),false),1001,new(200,135,0,0,9));
            var value=new CustomReplicaEvent(contact?CustomReplicaKind.Contact:CustomReplicaKind.Launch,token,contact?1001u:1000u,contact?default:launch,contact?new CustomContactMessage(_source,token,outcome):default);
            byte[] bytes=new byte[1024];
            if(!CustomReplicaCodec.TryWriteEvent(Wire,value,bytes,out int length))throw new Exception("Consumer event fixture invalid.");
            return bytes[..length];
        }
        internal byte[] State(ulong revision,int magazine=3)
        {
            var slots=(CustomReplicaSlot[])Slots.Clone();var row=slots[1];
            slots[1]=row with {SettledSequence=1,State=row.State with {Magazine=magazine,LastAcceptedShotId=1}};
            byte[] bytes=new byte[1024];if(!CustomReplicaCodec.TryWriteState(Wire,1002,slots,bytes,out int length,revision))throw new Exception("Consumer state fixture invalid.");
            return bytes[..length];
        }
        internal void Open(Action<bool,string> check)
        {
            check(Consumer.Receive(PacketType.CustomWeaponReplicaPage,Page,0,out var ack)&&ack.Baseline&&Consumer.IsReady,
                "consumer atomically installs genuine supplied Life0 spectator baseline before ACK");
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        Durable(check);Full(check);Close(check);
    }
    private static void Durable(Action<bool,string> check)
    {
        var f=new Fixture();f.Open(check);var consumer=f.Consumer;var packet=f.Event(1);
        check(consumer.Receive(PacketType.CustomWeaponReplicaEvent,packet,0,out var ack)&&!ack.Baseline&&ack.Id==1
            &&consumer.PendingFacts==1&&consumer.DeliveredEvents==1,"semantic event ACK follows durable FIFO enqueue");
        check(consumer.Receive(PacketType.CustomWeaponReplicaEvent,packet,0,out var duplicate)&&duplicate==ack&&consumer.PendingFacts==1,
            "exact retry returns durable digest ACK without duplicating presentation fact");
        check(consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.Event(2,true),0,out _)&&consumer.PendingFacts==2,
            "terminal contact is durable independently of presentation drain");
        check(consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.State(1),0,out var stateAck)&&stateAck==default&&consumer.StateRevision==1,
            "reliable whole-table state updates independently of semantic queue");
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        check(consumer.TryCopyState(slots,out uint world)&&world==1002&&slots[1].State.Magazine==3,
            "current presentation table reflects later canonical state");
        check(consumer.Receive(PacketType.CustomWeaponReplicaPage,f.Page,0,out var baselineAck)&&baselineAck.Baseline
            &&consumer.TryCopyState(slots,out world)&&world==1002&&slots[1].State.Magazine==3&&consumer.PendingFacts==2,
            "duplicate baseline after state and contact only retries ACK and cannot restore old resources or facts");
        check(consumer.TryCopyBaseline(slots,flights,out var capture)&&capture.World==1001&&slots[1].State.Magazine==4
            &&slots[7] is {Occupied:true,Spectating:true,Life:0,HasWeapon:false},"original immutable baseline stays inspectable without inventing spectator owner");
        check(consumer.TryTakeFact(out var first)&&first.Token==1&&first.Kind==CustomReplicaKind.Launch
            &&consumer.TryTakeFact(out var second)&&second.Token==2&&second.Kind==CustomReplicaKind.Contact
            &&!consumer.TryTakeFact(out _),"presentation drains exact once in accepted FIFO order");
    }
    private static void Full(Action<bool,string> check)
    {
        var f=new Fixture();f.Open(check);bool accepted=true;
        for(ulong i=1;i<=(ulong)CustomReplicaConsumer.Capacity;i++)accepted&=f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.Event(i),0,out _);
        check(accepted&&f.Consumer.PendingFacts==1024&&f.Consumer.DeliveredEvents==1024,"bounded consumer holds exactly1024 durable semantic facts");
        var next=f.Event(1025);
        check(!f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,next,0,out var ack)&&ack==default&&f.Consumer.DeliveredEvents==1024,
            "full queue refuses novel event before receiver deduplication frontier or ACK changes");
        check(!f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.Event(1),0,out ack)&&ack==default,
            "full queue conservatively withholds duplicate ACK until presentation frees capacity");
        check(f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.State(1),0,out _)&&f.Consumer.StateRevision==1&&f.Consumer.PendingFacts==1024,
            "state table remains live while semantic queue is full");
        check(f.Consumer.TryTakeFact(out var oldest)&&oldest.Token==1&&f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,next,0,out ack)
            &&ack.Id==1025&&f.Consumer.DeliveredEvents==1025&&f.Consumer.PendingFacts==1024,
            "unchanged refused packet retries successfully after one durable fact is consumed");
    }
    private static void Close(Action<bool,string> check)
    {
        var f=new Fixture();f.Open(check);f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.Event(1),0,out _);f.Consumer.Close();
        check(f.Consumer.Closed&&!f.Consumer.IsReady&&f.Consumer.PendingFacts==0&&!f.Consumer.TryTakeFact(out _),
            "retiring recipient clears queued old-incarnation presentation facts");
        check(!f.Consumer.Receive(PacketType.CustomWeaponReplicaPage,f.Page,0,out var ack)&&ack==default
            &&!f.Consumer.Receive(PacketType.CustomWeaponReplicaEvent,f.Event(2),0,out ack)&&ack==default,
            "closed recipient refuses delayed baseline and semantic event without resurrection");
    }
}
