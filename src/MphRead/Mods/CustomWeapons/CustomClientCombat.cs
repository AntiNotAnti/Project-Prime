using System;
using System.Security.Cryptography;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Client presentation/intent only. No health setter, native ShotKey, damage
// history or resource-authority mutation exists on this path.
internal sealed class CustomClientCombat
{
    private readonly record struct Seen(ulong Token,PacketType Type,WeaponDigest Digest);
    private readonly record struct Pending(CustomWeaponAction Action,CustomControlRequest? Control,double Created,double Due)
    {
        internal WeaponOwnerContext Owner => Control?.Owner ?? Action.Owner;
        internal uint Sequence => Control?.Sequence ?? Action.Sequence;
    }
    private readonly Seen[] _seen=new Seen[128];
    private readonly Pending?[] _pending=new Pending?[16];
    private readonly CustomReadinessWire _wire;
    private ulong _highest;
    private WeaponOwnerContext _submittedOwner;
    private uint _submitted;
    private double _time;
    private byte _mode;
    internal bool Invalidated { get; private set; }
    internal CustomStateMessage? State { get; private set; }
    internal CustomReceiptMessage? LastReceipt { get; private set; }
    internal CustomContactMessage? LastContact { get; private set; }
    internal CustomControlDenied? LastControlDenied { get; private set; }
    internal readonly byte[] InputBytes=new byte[CustomInputCarrier.MaxPayload];
    internal CustomClientCombat(CustomReadinessWire wire) => _wire=wire;
    private bool Time(double now)
    { if (!double.IsFinite(now) || now<_time) return false; _time=now; return true; }
    private bool Recipient(WorldBootstrapIdentity bootstrap,WeaponDigest manifest,int slot)
        => bootstrap==_wire.Bootstrap && manifest==_wire.Manifest.Digest && slot==_wire.Slot;
    private bool First(ulong token,PacketType type,ReadOnlySpan<byte> payload,out bool contradiction)
    {
        contradiction=false;
        if (_highest>=128 && token<=_highest-128) return false;
        Span<byte> hash=stackalloc byte[32]; SHA256.HashData(payload,hash); var digest=WeaponDigest.Read(hash);
        int index=(int)(token%128);
        if (_seen[index].Token==token)
        { contradiction=_seen[index].Type!=type || _seen[index].Digest!=digest; return false; }
        _seen[index]=new(token,type,digest); if (token>_highest) _highest=token; return true;
    }
    internal bool Receive(PacketType type,ReadOnlySpan<byte> bytes,out CustomResultAck ack)
    {
        ack=default; if (Invalidated) return false;
        if (type==PacketType.CustomWeaponState)
        {
            if (!CustomCombatResults.TryReadState(bytes,_wire.Manifest,_wire.Definition,out var state)
                || !Recipient(state.Bootstrap,state.Manifest,state.State.Owner.Slot)) return false;
            if (State is { } old)
            {
                if (state.Revision<old.Revision) return true;
                if (state.Revision==old.Revision)
                { if (state!=old) Invalidated=true; return !Invalidated; }
                if (state.State.Owner.Generation!=old.State.Owner.Generation
                    || unchecked((short)(state.State.Owner.Life-old.State.Owner.Life))<0) return false;
            }
            State=state;
            if (_submittedOwner!=state.State.Owner)
            {
                _submittedOwner=state.State.Owner; _submitted=state.SettledSequence;
                _mode=0;
                if (LastReceipt?.Receipt.Action.Owner!=state.State.Owner) LastReceipt=null;
                if (LastControlDenied?.Request.Owner!=state.State.Owner) LastControlDenied=null;
                // A new canonical life retires old local intent/prediction;
                // late results still receive exact old-life acknowledgements.
                Array.Clear(_pending);
            }
            else if (_submitted<state.SettledSequence) _submitted=state.SettledSequence;
            return true;
        }
        if (type==PacketType.CustomWeaponReceipt)
        {
            if (!CustomCombatResults.TryReadReceipt(bytes,_wire.Manifest,_wire.Definition,out var message)
                || !Recipient(message.Receipt.Action.Bootstrap,message.Receipt.Action.Manifest,message.Receipt.Action.Slot)) return false;
            var action=message.Receipt.Action;
            for (int i=0;i<_pending.Length;i++)
                if (_pending[i] is { } pending && pending.Owner==action.Owner && pending.Sequence==action.Sequence)
                {
                    if (pending.Control is { } control ? !control.Matches(action):pending.Action!=action)
                    { Invalidated=true; return false; }
                    _pending[i]=null;
                }
            bool first=First(message.Token,type,bytes,out bool conflict);
            if (conflict) { Invalidated=true; return false; }
            ack=message.Ack;
            if (first && (State==null || State.Value.State.Owner==action.Owner)
                && (LastReceipt is not { } prior || prior.Receipt.Action.Owner!=action.Owner
                || prior.Receipt.Action.Sequence<action.Sequence)) LastReceipt=message;
            return true;
        }
        if (type==PacketType.CustomWeaponControlDenied)
        {
            if (!CustomControlDenied.TryRead(bytes,out var denied)
                || denied.Request.Weapon.Value!=10
                || !Recipient(denied.Request.Bootstrap,denied.Request.Manifest,denied.Request.Slot)) return false;
            for (int i=0;i<_pending.Length;i++)
                if (_pending[i] is { } pending && pending.Owner==denied.Request.Owner && pending.Sequence==denied.Request.Sequence)
                {
                    if (pending.Control!=denied.Request) { Invalidated=true; return false; }
                    _pending[i]=null;
                }
            bool first=First(denied.Token,type,bytes,out bool conflict);
            if (conflict) { Invalidated=true; return false; }
            ack=denied.Ack;
            if (first && (State==null || State.Value.State.Owner==denied.Request.Owner)
                && (LastControlDenied is not { } old || old.Request.Owner!=denied.Request.Owner || old.Request.Sequence<denied.Request.Sequence))
                LastControlDenied=denied;
            return true;
        }
        if (type==PacketType.CustomWeaponContact)
        {
            if (!CustomCombatResults.TryReadContact(bytes,_wire.Manifest,_wire.Definition,out var message)
                || !Recipient(message.Bootstrap,message.Outcome.Contact.Key.Action.Manifest,message.Outcome.Contact.Key.Action.Owner.Slot)) return false;
            bool first=First(message.Token,type,bytes,out bool conflict);
            if (conflict) { Invalidated=true; return false; }
            ack=message.Ack; if (first) LastContact=message; return true;
        }
        return false;
    }
    internal bool Submit(in CustomWeaponAction action,double now)
    {
        if (Invalidated || _mode==2 || !Time(now) || !action.IsValid || State is not { } state
            || action.Weapon!=state.Weapon
            || !Recipient(action.Bootstrap,action.Manifest,action.Slot) || action.Owner!=state.State.Owner
            || action.Owner!=_submittedOwner || _submitted==uint.MaxValue) return false;
        for (int i=0;i<_pending.Length;i++)
            if (_pending[i] is { } pending && pending.Action.Owner==action.Owner && pending.Action.Sequence==action.Sequence)
                return pending.Action==action;
        if (action.Sequence!=_submitted+1) return false;
        for (int i=0;i<_pending.Length;i++)
            if (_pending[i]==null) { _pending[i]=new(action,null,now,now); _submitted=action.Sequence; _mode=1; return true; }
        return false;
    }
    // Player controls supply only intent. The current canonical projection owns
    // every identity field; the bounded publisher owns its next sequence, which
    // can be ahead of the last authority projection while results are in flight.
    // A failed submission must leave that sequence available for a later input.
    internal bool TrySubmitControl(CustomWeaponActionKind kind,byte optic,double now,out CustomControlRequest request)
    {
        request=default;
        if (State is not { } state || _submitted==uint.MaxValue) return false;
        var candidate=new CustomControlRequest(state.Bootstrap,state.Manifest,state.State.Owner.Slot,state.State.Owner.Life,
            state.Weapon,_submitted+1,kind,optic);
        if (!Submit(candidate,now)) return false;
        request=candidate; return true;
    }
    internal bool Submit(in CustomControlRequest request,double now)
    {
        if (Invalidated || _mode==1 || !Time(now) || !request.IsValid || State is not { } state
            || request.Weapon!=state.Weapon
            || !Recipient(request.Bootstrap,request.Manifest,request.Slot) || request.Owner!=state.State.Owner
            || request.Owner!=_submittedOwner || _submitted==uint.MaxValue) return false;
        foreach (var pending in _pending)
            if (pending is { } value && value.Owner==request.Owner && value.Sequence==request.Sequence) return value.Control==request;
        if (request.Sequence!=_submitted+1) return false;
        for (int i=0;i<_pending.Length;i++)
            if (_pending[i]==null) { _pending[i]=new(default,request,now,now); _submitted=request.Sequence; _mode=2; return true; }
        return false;
    }
    internal bool TryNext(double now,out CustomWeaponAction action)
    {
        action=default; if (Invalidated || !Time(now)) return false;
        foreach (var pending in _pending)
            if (pending is { } expiring && now-expiring.Created>CustomActionOutbox.DeadlineSeconds)
            { Invalidated=true; return false; }
        foreach (var pending in _pending)
            if (pending is { } value)
            {
                if (value.Control==null && now>=value.Due) { action=value.Action; return true; }
            }
        return false;
    }
    internal void Sent(in CustomWeaponAction action,double now)
    {
        if (Invalidated || !Time(now)) return;
        for (int i=0;i<_pending.Length;i++)
            if (_pending[i] is { } value && value.Action==action) { _pending[i]=value with { Due=now+.25 }; return; }
    }
    internal bool TryNextControl(double now,out CustomControlRequest request)
    {
        request=default; if (Invalidated || !Time(now)) return false;
        foreach (var pending in _pending)
            if (pending is { } expiring && now-expiring.Created>CustomActionOutbox.DeadlineSeconds)
            { Invalidated=true; return false; }
        foreach (var pending in _pending)
            if (pending is { Control:{ } control } value && now>=value.Due) { request=control; return true; }
        return false;
    }
    // Input transport repeats only the oldest unconsumed control. Reliable91
    // remains responsible for delivery; neither carrier send nor transport ACK
    // removes the request. The authority still enforces universal sequence order.
    internal bool TryInputControl(WeaponOwnerContext owner,double now,out CustomControlRequest request)
    {
        request=default;
        if (Invalidated || !Time(now) || owner!=_submittedOwner || _mode!=2) return false;
        foreach (var pending in _pending)
            if (pending is { } value)
            {
                if (now-value.Created>CustomActionOutbox.DeadlineSeconds) { Invalidated=true; request=default; return false; }
                if (value.Control is { } control && control.Owner==owner && (!request.IsValid || control.Sequence<request.Sequence)) request=control;
            }
        return request.IsValid;
    }
    internal void Sent(in CustomControlRequest request,double now)
    {
        if (Invalidated || !Time(now)) return;
        for (int i=0;i<_pending.Length;i++)
            if (_pending[i] is { } value && value.Control==request) { _pending[i]=value with { Due=now+.25 }; return; }
    }
}
