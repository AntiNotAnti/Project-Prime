using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Authority-thread fanout leases. Current recipients can change; an admitted
// flight retains references to its actual recipient incarnations, never slot bits.
// Observer retirement never changes the mandatory private owner reservation.
internal sealed class CustomReplicaAudience
{
    internal const int Capacity=128;
    private sealed class Group
    {
        internal CustomWeaponAction Action;
        internal bool Active,Launched,ReceiptFinalized,Terminal;
        internal CustomReplicaLaunch Launch;
        internal readonly CustomReplicaMailbox?[] Recipients=new CustomReplicaMailbox?[8];
        internal void Clear(){Action=default;Active=Launched=ReceiptFinalized=Terminal=false;Launch=default;Array.Clear(Recipients);}
    }
    private readonly Group[] _groups=new Group[Capacity];
    private readonly CustomReplicaMailbox?[] _current=new CustomReplicaMailbox?[8];
    private readonly uint _flightDuration;
    internal int Count {get;private set;}
    internal CustomReplicaAudience(int lifetimeTicks)
    {
        if(lifetimeTicks<1||lifetimeTicks>1000000)throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));
        _flightDuration=(uint)lifetimeTicks+2;for(int i=0;i<Capacity;i++)_groups[i]=new();
    }
    internal void SetRecipient(byte slot,CustomReplicaMailbox? mailbox)
    {if(slot>=8)throw new ArgumentOutOfRangeException(nameof(slot));_current[slot]=mailbox;}
    private Group? Find(CustomActionIdentity identity)
    {foreach(var group in _groups)if(group.Active&&CustomActionIdentity.For(group.Action)==identity)return group;return null;}
    private void Free(Group group){group.Clear();Count--;}
    internal bool Prepare(in CustomWeaponAction action,double now)
    {
        if(!action.IsValid||action.Kind!=CustomWeaponActionKind.Fire||!double.IsFinite(now)||now<0)return false;
        var group=Find(CustomActionIdentity.For(action));
        if(group!=null&&(group.Action!=action||group.Launched))return false;
        if(group==null&&Count==Capacity)return false;
        // Inspect all recipients before mutating any reservation. A queued but
        // not emitted action refreshes its cohort after a viewer finishes joining.
        foreach(var mailbox in _current)
            if(mailbox!=null&&(mailbox.Closed||mailbox.HasExpired(now)||!mailbox.CanPrepare(action)))return false;
        if(group==null)
        {
            foreach(var candidate in _groups)if(!candidate.Active){group=candidate;break;}
            group!.Action=action;group.Active=true;Count++;
        }
        for(int slot=0;slot<8;slot++)
        {
            var mailbox=_current[slot];
            if(mailbox==null){group.Recipients[slot]=null;continue;}
            if(!mailbox.Prepare(action,now))
            {
                // Single-thread ownership and identical sampled time make this
                // an invariant failure, not normal capacity backpressure.
                mailbox.Close();return false;
            }
            group.Recipients[slot]=mailbox;
        }
        return true;
    }
    internal bool HasPrepared(in CustomWeaponAction action)
        =>action.IsValid&&Find(CustomActionIdentity.For(action)) is {Launched:false} group&&group.Action==action;
    internal bool PublishLaunch(in CustomReplicaLaunch launch,double now)
    {
        if(!launch.Source.Action.IsValid||Find(launch.Identity) is not {Launched:false} group||group.Action!=launch.Source.Action)return false;
        group.Launched=true;group.Launch=launch;
        foreach(var mailbox in group.Recipients)
            if(mailbox!=null&&!mailbox.Closed&&!mailbox.PublishLaunch(launch,now))mailbox.Close();
        return true;
    }
    internal void PublishContact(in CustomContactOutcome outcome,double now)
    {
        if(!outcome.Contact.Key.Action.IsValid||Find(outcome.Contact.Key.Action) is not {} group)return;
        foreach(var mailbox in group.Recipients)
            if(mailbox!=null&&!mailbox.Closed&&!mailbox.PublishContact(outcome,now))mailbox.Close();
        group.Terminal=true;
        if(group.Launched&&group.ReceiptFinalized)Free(group);
    }
    internal void FinalizeReceipt(in CustomActionReceipt receipt)
    {
        if(!receipt.Action.IsValid||Find(CustomActionIdentity.For(receipt.Action)) is not {} group||group.Action!=receipt.Action)return;
        foreach(var mailbox in group.Recipients)
            if(mailbox!=null&&!mailbox.Closed)
            {
                // Canonical receipt validation precedes any capacity release.
                if(!mailbox.FinalizeCanonicalReceipt(receipt))mailbox.Close();
            }
        if(receipt.Emission==CustomEmissionDisposition.Spawned&&group.Launched&&!group.Terminal)
        {group.ReceiptFinalized=true;return;}
        Free(group);
    }
    // Retire only work that never emitted. A source connection/bootstrap
    // replacement cannot leave other viewers holding a prepaid ghost launch.
    internal void CancelPrepared(byte sourceSlot,WorldBootstrapIdentity source,uint world)
    {
        foreach(var group in _groups)
            if(group.Active&&!group.Launched&&group.Action.Slot==sourceSlot&&group.Action.Bootstrap==source)
                FinalizeReceipt(new(group.Action,world,CustomJournalDisposition.SourceDenied,
                    WeaponActionResult.InvalidLife,CustomEmissionDisposition.NotAttempted,null,null,null));
    }
    internal bool BeginBaseline(byte slot,CustomReplicaMailbox mailbox,in CustomReplicaCapture capture,
        ReadOnlySpan<CustomReplicaSlot> slots,ReadOnlySpan<CustomReplicaFlight> flights,double now)
    {
        if(slot>=8||_current[slot]!=mailbox||capture.FlightCount is <0 or >64||flights.Length<capture.FlightCount)return false;
        for(int i=0;i<capture.FlightCount;i++)
        {
            var flight=flights[i];var group=Find(flight.Action);
            if(group is not {Launched:true,Terminal:false}||group.Action!=flight.Certificate.Action
                ||group.Launch.Source!=flight.Certificate||group.Launch.World!=flight.LaunchWorld
                ||group.Recipients[slot] is {} old&&!old.Closed&&!ReferenceEquals(old,mailbox))return false;
        }
        if(!mailbox.BeginBaseline(capture,slots,flights,now))return false;
        // The mailbox has reserved EVERY future contact before its first page
        // can be sent. Existing flights remain independent of this new viewer.
        for(int i=0;i<capture.FlightCount;i++)Find(flights[i].Action)!.Recipients[slot]=mailbox;
        return true;
    }
    internal void Advance(uint world)
    {
        foreach(var mailbox in _current)mailbox?.Advance(world);
        foreach(var group in _groups)
            if(group.Active&&group.Launched&&group.ReceiptFinalized
                &&unchecked((int)(world-unchecked(group.Launch.World+_flightDuration)))>0)Free(group);
    }
    internal void Close()
    {
        foreach(var mailbox in _current)mailbox?.Close();
        foreach(var group in _groups)group.Clear();Array.Clear(_current);Count=0;
    }
}
