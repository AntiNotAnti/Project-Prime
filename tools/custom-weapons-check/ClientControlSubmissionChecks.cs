using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure client intent publication. Canonical projections/denials are explicit
// fixtures; these checks do not admit a source, grant ammo or apply combat HP.
internal static class ClientControlSubmissionChecks
{
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly CustomReadinessWire Wire;
        internal readonly WeaponRuntimeState Runtime;
        internal readonly CustomStateMessage Initial;
        internal Fixture(uint settled=0)
        {
            var bootstrap=new WorldBootstrapIdentity(new(0x4321,0xfedcba9876543210UL,0x12345678),
                0x87654321,0xabcd,100,0x11223344,0x55667788);
            Wire=new(bootstrap,5);
            Runtime=new(Definition,new(bootstrap.Start.MatchId,bootstrap.Start.AuthorityEpoch,5,bootstrap.SlotGeneration,9),100);
            Initial=new(bootstrap,Wire.Manifest.Digest,new(10),100,1,settled,default,100,Runtime.Snapshot());
        }
        internal byte[] Encode(CustomStateMessage state)
        {var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(state,bytes,Wire.Manifest,Definition);return bytes;}
        internal CustomClientCombat Client(bool state=true)
        {
            var client=new CustomClientCombat(Wire);
            if(state&&!client.Receive(PacketType.CustomWeaponState,Encode(Initial),out _))throw new InvalidOperationException("Initial control projection refused.");
            return client;
        }
        internal static byte[] Denied(CustomControlRequest request,ulong token)
        {var bytes=new byte[CustomControlDenied.Size];new CustomControlDenied(request,token,100,CustomControlDenialReason.SourceUnavailable).Write(bytes);return bytes;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var f=new Fixture();var native=Enumerable.Range(0,8).Select(NetDamage.Sequence).ToArray();
        MissingAndIdentity(f,check);Refusals(f,check);Capacity(f,check);Lifecycle(f,check);ModesAndLimit(f,check);Allocation(f,check);
        check(native.SequenceEqual(Enumerable.Range(0,8).Select(NetDamage.Sequence))&&f.Runtime.Snapshot()==f.Initial.State,
            "client control publication never mutates actual resource state or native damage counters");
    }
    private static void MissingAndIdentity(Fixture f,Action<bool,string> check)
    {
        var empty=f.Client(false);
        check(!empty.TrySubmitControl(CustomWeaponActionKind.Fire,1,0,out var absent)&&absent==default
            &&empty.State==null&&!empty.TryNextControl(0,out _),"automatic controls require canonical state and fail with default output");
        var client=f.Client();var before=client.State;
        check(client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var equip)&&equip.Sequence==1
            &&equip.Bootstrap==f.Initial.Bootstrap&&equip.Manifest==f.Initial.Manifest&&equip.Owner==f.Initial.State.Owner
            &&equip.Slot==5&&equip.Weapon==f.Initial.Weapon&&equip.Kind==CustomWeaponActionKind.Equip&&equip.Optic==0,
            "automatic publisher derives every full stream/owner/bootstrap/manifest/runtime field from canonical state");
        CustomControlRequest fire=default,retry=default;
        check(client.TrySubmitControl(CustomWeaponActionKind.Optic,2,0,out var optic)&&optic.Sequence==2
            &&client.TrySubmitControl(CustomWeaponActionKind.Fire,2,0,out fire)&&fire.Sequence==3
            &&client.State==before&&client.State!.Value.SettledSequence==0,
            "new controls use local submitted frontier while canonical settled sequence remains unchanged");
        var firstBytes=new byte[CustomControlRequest.Size];equip.Write(firstBytes);
        client.Sent(equip,0);client.Sent(optic,0);client.Sent(fire,0);
        check(!client.TryNextControl(.249,out _)&&client.TryNextControl(.25,out retry)&&retry==equip
            &&client.Submit(equip,.25),"application retry retains original exact request while future controls are pending");
        var retryBytes=new byte[CustomControlRequest.Size];retry.Write(retryBytes);
        check(firstBytes.SequenceEqual(retryBytes)&&client.State==before,
            "retry serialization and authoritative projection remain immutable after publication");
    }
    private static void Refusals(Fixture f,Action<bool,string> check)
    {
        foreach(var (kind,optic) in new[]{((CustomWeaponActionKind)0,(byte)0),((CustomWeaponActionKind)6,(byte)0),
            ((CustomWeaponActionKind)255,(byte)0),(CustomWeaponActionKind.Fire,(byte)3),(CustomWeaponActionKind.Optic,(byte)255),
            (CustomWeaponActionKind.Equip,(byte)1),(CustomWeaponActionKind.Reload,(byte)1),(CustomWeaponActionKind.Unequip,(byte)2)})
        {
            var client=f.Client();
            check(!client.TrySubmitControl(kind,optic,0,out var absent)&&absent==default&&client.State==f.Initial
                &&!client.TryNextControl(0,out _),"invalid control kind/optic leaves no pending request or projection mutation");
            check(client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var next)&&next.Sequence==1,
                "invalid kind/optic cannot burn next local sequence");
        }
        foreach(double now in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1d})
        {
            var client=f.Client();
            check(!client.TrySubmitControl(CustomWeaponActionKind.Equip,0,now,out var absent)&&absent==default
                &&client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var next)&&next.Sequence==1,
                "invalid or backdated publisher time cannot burn sequence or prevent exact valid retry");
        }
        var clock=f.Client();clock.TrySubmitControl(CustomWeaponActionKind.Equip,0,1,out _);
        check(!clock.TrySubmitControl(CustomWeaponActionKind.Reload,0,.5,out var stale)&&stale==default
            &&clock.TrySubmitControl(CustomWeaponActionKind.Reload,0,1,out var second)&&second.Sequence==2,
            "backdated time after a successful control leaves next sequence reusable");
    }
    private static void Capacity(Fixture f,Action<bool,string> check)
    {
        var client=f.Client();var requests=new CustomControlRequest[16];
        for(int i=0;i<requests.Length;i++)check(client.TrySubmitControl(CustomWeaponActionKind.Fire,1,0,out requests[i])
            &&requests[i].Sequence==(uint)i+1,"automatic controls fill exactly sixteen immutable pending slots");
        check(!client.TrySubmitControl(CustomWeaponActionKind.Fire,1,0,out var full)&&full==default
            &&client.Submit(requests[0],0),"full publisher refuses new intent without replacing an exact retained retry");
        check(client.Receive(PacketType.CustomWeaponControlDenied,Fixture.Denied(requests[0],1),out var ack)
            &&ack.Sequence==1&&client.LastControlDenied?.Request==requests[0]
            &&client.TrySubmitControl(CustomWeaponActionKind.Reload,0,0,out var next)&&next.Sequence==17,
            "no-source denial releases one pending slot and full refusal did not burn sequence17");
        check(client.State==f.Initial&&client.LastReceipt==null,
            "typed no-source settlement never fabricates a source receipt or debits canonical magazine");
    }
    private static void Lifecycle(Fixture f,Action<bool,string> check)
    {
        var client=f.Client();client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var old);
        var settled=f.Initial with{Revision=2,SettledSequence=7};
        check(client.Receive(PacketType.CustomWeaponState,f.Encode(settled),out _)
            &&client.TrySubmitControl(CustomWeaponActionKind.Optic,1,0,out var current)&&current.Sequence==8,
            "newer same-life canonical settlement advances automatic publisher beyond authority frontier");
        check(client.Receive(PacketType.CustomWeaponState,f.Encode(f.Initial),out _)
            &&client.TrySubmitControl(CustomWeaponActionKind.Fire,1,0,out var afterOld)&&afterOld.Sequence==9,
            "out-of-order state cannot rewind submitted or settled sequence");
        var oldOwner=f.Initial.State.Owner;
        var owner=new WeaponOwnerContext(oldOwner.MatchId,oldOwner.AuthorityEpoch,oldOwner.Slot,oldOwner.Generation,10);
        var nextLife=f.Initial with{Revision=3,SettledSequence=0,State=new WeaponRuntimeState(f.Definition,owner,100).Snapshot()};
        CustomControlRequest first=default;
        check(client.Receive(PacketType.CustomWeaponState,f.Encode(nextLife),out _)
            &&!client.TryNextControl(0,out _)&&client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out first)
            &&first.Sequence==1&&first.Owner==owner,"actual new-life projection clears old pending controls and starts nonce1 for exact new owner");
        check(client.Receive(PacketType.CustomWeaponControlDenied,Fixture.Denied(old,2),out var ack)
            &&ack.Owner==old.Owner&&client.LastControlDenied==null&&client.TryNextControl(0,out var retry)&&retry==first,
            "late old-life denial is acknowledged without consuming or replacing current-life intent");
        check(!client.Receive(PacketType.CustomWeaponState,f.Encode(settled with{Revision=4}),out _)
            &&client.State==nextLife&&client.TrySubmitControl(CustomWeaponActionKind.Reload,0,0,out var second)&&second.Sequence==2,
            "higher-revision old-life state cannot rebind publisher to retired life");
    }
    private static void ModesAndLimit(Fixture f,Action<bool,string> check)
    {
        var raw=f.Client();var action=new CustomWeaponAction(f.Initial.Bootstrap,f.Initial.Manifest,5,9,new(10),1,100,0,0,
            CustomWeaponActionKind.Equip,0,Vector3.Zero,Vector3.Zero);
        check(raw.Submit(action,0)&&!raw.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var absent)&&absent==default
            &&raw.TryNext(0,out var retry)&&retry==action,"raw action mode refuses automatic primary controls without replacing raw retry");
        var maximum=new Fixture(uint.MaxValue);var exhausted=maximum.Client();
        check(!exhausted.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out absent)&&absent==default
            &&!exhausted.TryNextControl(0,out _),"canonical uint-max frontier refuses nonce wrap to zero");
        var last=new Fixture(uint.MaxValue-1).Client();
        check(last.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var final)&&final.Sequence==uint.MaxValue
            &&!last.TrySubmitControl(CustomWeaponActionKind.Reload,0,0,out absent)&&absent==default
            &&last.TryNextControl(0,out var retryControl)&&retryControl==final,"last valid uint nonce retains its pending retry while future submissions refuse");
        var invalid=f.Client();
        check(!invalid.Receive(PacketType.CustomWeaponState,f.Encode(f.Initial with{SettledSequence=1}),out _)
            &&invalid.Invalidated&&!invalid.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out absent)&&absent==default,
            "contradictory canonical projection permanently denies automatic publication");
    }
    private static void Allocation(Fixture f,Action<bool,string> check)
    {
        var client=f.Client();var before=client.State;var buffer=new byte[CustomControlDenied.Size];bool ok=true;
        void Probe()
        {
            ok&=client.TrySubmitControl(CustomWeaponActionKind.Equip,0,0,out var request);
            new CustomControlDenied(request,request.Sequence,100,CustomControlDenialReason.SourceUnavailable).Write(buffer);
            ok&=client.Receive(PacketType.CustomWeaponControlDenied,buffer,out var ack)&&ack.Sequence==request.Sequence;
        }
        for(int i=0;i<10000;i++)Probe();
        long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++)Probe();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        Console.WriteLine($"CLIENT CONTROL SUBMISSION ALLOCATION:100000 warmed automatic publication/typed-denial cycles={bytes}B");
        check(ok&&bytes==0&&client.State==before&&f.Runtime.Snapshot()==f.Initial.State,
            "100000 warmed automatic controls and no-source settlements allocate zero without authority projection/resource mutation");
    }
}
