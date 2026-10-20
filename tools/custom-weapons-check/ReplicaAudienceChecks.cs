using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure supplied canonical facts and recipient ownership, with no engine or UDP.
internal static class ReplicaAudienceChecks
{
    private sealed class Fixture
    {
        internal readonly CustomReadinessWire Wire;
        internal readonly CustomReplicaSlot[] Slots=new CustomReplicaSlot[8];
        internal readonly CustomReplicaFlight[] Flights=new CustomReplicaFlight[64];
        internal readonly CustomReplicaCapture Capture;
        internal readonly CustomWeaponAction Action;
        internal readonly CustomReplicaLaunch Launch;
        internal readonly CustomContactOutcome Contact;
        internal readonly CustomActionReceipt Receipt;
        internal readonly CustomReplicaMailbox Box;
        internal Fixture(byte viewer=6,ulong connection=42)
        {
            var start=new MatchStartIdentity(7,0x123456789abcdef0,3);var sourceBootstrap=new WorldBootstrapIdentity(start,2,5,990,1,1);
            Wire=new(new(start,9,(ushort)(70+viewer),990,1,1),viewer);
            for(byte i=0;i<8;i++)Slots[i]=new(i,false,0,0,false,false,default,0,0,default);
            var owner=new WeaponOwnerContext(7,start.AuthorityEpoch,1,5,10);
            var state=new WeaponRuntimeState(Wire.Definition,owner,1000);state.SetEquipped(1000,true);state.TrySetOptic(1000,1,1);
            var before=state.Snapshot();state.TryFire(1000,1,true);var after=state.Snapshot();
            Slots[1]=new(1,true,5,10,false,true,new(10),1,1000,after);
            Slots[viewer]=new(viewer,true,(ushort)(70+viewer),0,true,false,default,0,0,default);
            Action=new(sourceBootstrap,Wire.Manifest.Digest,1,10,new(10),1,1000,999,0,CustomWeaponActionKind.Fire,1,new(0,.5f,0),Vector3.UnitZ);
            var source=new CustomSourceCertificate(Action,1000,999,0,Vector3.Zero,Vector3.UnitZ,true,false,false);
            Launch=new(source,1001,new(0,.4f,0),Vector3.UnitZ);
            Contact=new(new(new(CustomActionIdentity.For(Action),new(2,6,11),1,CustomImpactChannel.Body),Vector3.UnitZ,false),1001,new(200,135,0,0,9));
            Receipt=new(Action,1001,CustomJournalDisposition.Accepted,WeaponActionResult.Accepted,CustomEmissionDisposition.Spawned,source,before,after);
            Capture=new(7,start.AuthorityEpoch,Wire.Manifest.Digest,1001,8,0);Box=new(Wire,connection,0);
        }
        internal void Baseline(CustomReplicaMailbox box,CustomReadinessWire wire,CustomReplicaCapture capture,CustomReplicaSlot[] slots,CustomReplicaFlight[] flights,ulong connection,Action<bool,string> check)
        {
            check(box.BeginBaseline(capture,slots,flights,0),"pure recipient baseline reserves capacity");AckBaseline(box,wire,connection,check);
        }
        internal void Ready(Action<bool,string> check)=>Baseline(Box,Wire,Capture,Slots,Flights,42,check);
        internal static void AckBaseline(CustomReplicaMailbox box,CustomReadinessWire wire,ulong connection,Action<bool,string> check)
        {
            var consumer=new CustomReplicaConsumer(wire,0);CustomReplicaAck final=default;
            while(box.TryNext(0,out var type,out var bytes,out int key))
            {
                if(type!=PacketType.CustomWeaponReplicaPage)break;
                check(consumer.Receive(type,bytes.Span,0,out var ack)&&box.MarkSent(key,0),"pure observer consumes each baseline page before sent marking");
                if(ack.Id!=0)final=ack;
            }
            var encoded=new byte[CustomReplicaAck.Size];
            check(final.Baseline&&CustomReplicaCodec.TryWriteAck(wire,final,encoded)&&box.Acknowledge(encoded,connection,0)&&box.IsReady,
                "pure observer opens only after exact complete baseline digest ACK");
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        Capacity(check);JoinContact(check);Replacement(check);Cancel(check);
    }
    private static void Capacity(Action<bool,string> check)
    {
        var f=new Fixture();f.Ready(check);var audience=new CustomReplicaAudience(f.Wire.Definition.Mechanics.LifetimeTicks);audience.SetRecipient(6,f.Box);
        bool all=true;for(uint i=1;i<=CustomReplicaAudience.Capacity;i++)all&=audience.Prepare(f.Action with {Sequence=i},0);
        int count=f.Box.Count;
        check(all&&audience.Count==128&&count==128,"audience holds bounded128 action groups with observer reservations before effects");
        check(!audience.Prepare(f.Action with {Sequence=129},0)&&audience.Count==128&&f.Box.Count==count,
            "audience capacity refusal cannot partially reserve a recipient or claim a prepared action");
        check(!audience.HasPrepared(f.Action with {Sequence=129}),"refused action has no emission grant");
    }
    private static void JoinContact(Action<bool,string> check)
    {
        var f=new Fixture();f.Ready(check);var joining=new Fixture(7,43);var audience=new CustomReplicaAudience(f.Wire.Definition.Mechanics.LifetimeTicks);audience.SetRecipient(6,f.Box);
        check(audience.Prepare(f.Action,0)&&audience.PublishLaunch(f.Launch,0),"existing observer cohort reserves before actual supplied launch fact");
        audience.FinalizeReceipt(f.Receipt);audience.SetRecipient(7,joining.Box);
        joining.Flights[0]=new(CustomActionIdentity.For(f.Action),f.Launch.Source,1001,1009,f.Launch.Origin,f.Launch.Origin,
            Vector3.UnitZ*200,Vector3.UnitZ,0,.1f);
        check(audience.BeginBaseline(7,joining.Box,joining.Capture with {FlightCount=1},joining.Slots,joining.Flights,0),
            "joining viewer reserves existing flight terminal before any baseline page send");
        audience.PublishContact(f.Contact,0);
        check(audience.Count==0&&joining.Box.Count==1&&!joining.Box.IsReady,
            "actual supplied terminal during baseline remains retained for joining incarnation without reopening payment");
        Fixture.AckBaseline(joining.Box,joining.Wire,43,check);
        check(joining.Box.TryNext(0,out var type,out var bytes,out _)&&type==PacketType.CustomWeaponReplicaEvent
            &&CustomReplicaCodec.TryReadEvent(joining.Wire,bytes.Span,out var value)&&value.Kind==CustomReplicaKind.Contact&&value.Contact.Outcome==f.Contact,
            "baseline ACK releases exact retained in-flight contact to new viewer without a second launch");
    }
    private static void Replacement(Action<bool,string> check)
    {
        var f=new Fixture();f.Ready(check);var old=new Fixture(7,43);old.Baseline(old.Box,old.Wire,old.Capture,old.Slots,old.Flights,43,check);
        var audience=new CustomReplicaAudience(f.Wire.Definition.Mechanics.LifetimeTicks);audience.SetRecipient(6,f.Box);audience.SetRecipient(7,old.Box);
        check(audience.Prepare(f.Action,0)&&audience.PublishLaunch(f.Launch,0),"two recipient incarnations own original launch reservations");audience.FinalizeReceipt(f.Receipt);
        old.Box.Close();var replacement=new Fixture(7,44);replacement.Baseline(replacement.Box,replacement.Wire,replacement.Capture,replacement.Slots,replacement.Flights,44,check);
        audience.SetRecipient(7,replacement.Box);audience.PublishContact(f.Contact,0);
        check(old.Box.Closed&&replacement.Box.Count==0&&f.Box.Count==1&&audience.Count==0,
            "observer replacement cannot steal old incarnation contact; surviving recipient retains its independent obligation");
    }
    private static void Cancel(Action<bool,string> check)
    {
        var f=new Fixture();f.Ready(check);var second=new Fixture(7,43);second.Baseline(second.Box,second.Wire,second.Capture,second.Slots,second.Flights,43,check);
        var audience=new CustomReplicaAudience(f.Wire.Definition.Mechanics.LifetimeTicks);audience.SetRecipient(6,f.Box);audience.SetRecipient(7,second.Box);
        var launched=f.Action with {Sequence=2};var launchedSource=f.Launch.Source with {Action=launched};
        var foreign=f.Action with {Sequence=3,Bootstrap=f.Action.Bootstrap with {Revision=3}};
        var otherSlot=f.Action with {Sequence=4,Slot=2};
        check(audience.Prepare(f.Action,0)&&audience.Prepare(launched,0)&&audience.PublishLaunch(f.Launch with {Source=launchedSource},0)
            &&audience.Prepare(foreign,0)&&audience.Prepare(otherSlot,0),"retirement fixture reserves pending, launched, foreign-bootstrap and same-bootstrap other-slot groups for two recipient objects");
        audience.FinalizeReceipt(f.Receipt with {Action=launched,Source=launchedSource,After=f.Receipt.After!.Value with {LastAcceptedShotId=2}});
        audience.CancelPrepared(f.Action.Slot,f.Action.Bootstrap,1001);
        check(audience.Count==3&&f.Box.Count==3&&second.Box.Count==3&&!f.Box.Closed&&!second.Box.Closed
            &&!audience.HasPrepared(f.Action)&&audience.HasPrepared(foreign)&&audience.HasPrepared(otherSlot),
            "exact source-slot/bootstrap retirement frees every observer's unlaunched reservation while different slot, foreign source and launched group survive");
        var terminal=f.Contact with {Contact=f.Contact.Contact with {Key=f.Contact.Contact.Key with {Action=CustomActionIdentity.For(launched)}}};
        audience.PublishContact(terminal,0);
        check(audience.Count==2,"already launched group still delivers canonical terminal after source retirement");
        void AckEvents(Fixture viewer,ulong connection)
        {
            int delivered=0;
            while(viewer.Box.TryNext(0,out var type,out var bytes,out int key))
            {
                check(type==PacketType.CustomWeaponReplicaEvent&&CustomReplicaCodec.TryReadEvent(viewer.Wire,bytes.Span,out var fact),
                    "surviving launched retirement obligation remains valid semantic delivery");
                CustomReplicaCodec.TryReadEvent(viewer.Wire,bytes.Span,out var value);
                var ack=new CustomReplicaAck(false,value.Token,0,CustomReplicaCodec.Hash(bytes.Span));var encoded=new byte[CustomReplicaAck.Size];
                check(viewer.Box.MarkSent(key,0)&&CustomReplicaCodec.TryWriteAck(viewer.Wire,ack,encoded)&&viewer.Box.Acknowledge(encoded,connection,0),
                    "surviving launched obligation retires by exact sent digest ACK");delivered++;
            }
            check(delivered==2,"source retirement preserves one launch followed by one terminal per surviving observer");
        }
        AckEvents(f,42);AckEvents(second,43);audience.CancelPrepared(foreign.Slot,foreign.Bootstrap,1001);
        check(audience.Count==1&&audience.HasPrepared(otherSlot),"foreign-bootstrap cancellation cannot retire another source slot sharing the original bootstrap value");
        audience.CancelPrepared(otherSlot.Slot,otherSlot.Bootstrap,1001);audience.Advance(2000);
        check(audience.Count==0&&f.Box.Count==0&&second.Box.Count==0&&!f.Box.HasExpired(16)&&!second.Box.HasExpired(16),
            "cancelled unlaunched groups leave no ghost pending timeout beyond15seconds after live obligations are ACKed");
    }

}
