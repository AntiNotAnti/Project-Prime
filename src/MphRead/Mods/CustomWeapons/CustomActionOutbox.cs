using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal enum CustomOutboxOffer : byte { Reserved,Duplicate,Conflict,Full,Foreign,Closed }

/// <summary>One endpoint/bootstrap owner. A fixed record reserves both the
/// action receipt and Fire's eventual contact bytes BEFORE effects. Reliable
/// admission is not application consumption; only an exact sent-result ACK
/// releases that obligation. Caller owns the authority thread.</summary>
internal sealed class CustomActionOutbox
{
    internal const int Capacity=16;
    internal const double RetrySeconds=.25,DeadlineSeconds=15;
    internal const double SourceWaitSeconds=CustomSourceActionJournal.MaxWorldAge/60d;
    private sealed class Entry
    {
        internal readonly byte[] ReceiptBytes=new byte[CustomReceiptMessage.Size],ContactBytes=new byte[CustomContactMessage.Size];
        internal CustomWeaponAction Action;
        internal CustomControlRequest? Control;
        internal CustomResourceKnowledge Knowledge;
        internal ulong InputFence;
        internal uint ControlWorld;
        internal double SourceTime;
        internal bool Denied;
        internal WeaponOwnerContext Owner => Control?.Owner ?? Action.Owner;
        internal uint Sequence => Control?.Sequence ?? Action.Sequence;
        internal WeaponRuntimeId Weapon => Control?.Weapon ?? Action.Weapon;
        internal ulong ReceiptToken,ContactToken;
        internal bool Active,ReceiptReady,ReceiptSent,ReceiptAcked,ContactExpected,ContactReady,ContactSent,ContactAcked;
        internal double Created,ReceiptDue,ContactDue;
        internal uint EmittedWorld;
        internal void Clear()
        {
            Action=default; ReceiptToken=ContactToken=0;
            Control=null; Knowledge=default; InputFence=0; ControlWorld=0; SourceTime=0; Denied=false;
            Active=ReceiptReady=ReceiptSent=ReceiptAcked=ContactExpected=ContactReady=ContactSent=ContactAcked=false;
            Created=ReceiptDue=ContactDue=0; EmittedWorld=0;
        }
    }
    private readonly Entry[] _entries=new Entry[Capacity];
    private readonly CustomReadinessWire _wire;
    private readonly WeaponDefinition _definition;
    private ulong _token;
    private double _time;
    private bool _closed;
    internal int Count { get; private set; }
    internal CustomActionOutbox(CustomReadinessWire wire,WeaponDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(wire); ArgumentNullException.ThrowIfNull(definition);
        if (wire.Definition.Identity!=definition.Identity) throw new ArgumentException("Exact frozen built-in definition required.");
        _wire=wire; _definition=definition;
        for (int i=0;i<Capacity;i++) _entries[i]=new();
    }
    private bool Time(double now)
    {
        if (_closed || !double.IsFinite(now) || now < _time) return false;
        _time=now; return true;
    }
    private Entry? Find(in CustomWeaponAction action)
    { if (!action.IsValid) return null; foreach (var entry in _entries) if (entry.Active && entry.Owner==action.Owner && entry.Sequence==action.Sequence) return entry; return null; }
    private Entry? Find(in CustomControlRequest request)
    { if (!request.IsValid) return null; foreach (var entry in _entries) if (entry.Active && entry.Owner==request.Owner && entry.Sequence==request.Sequence) return entry; return null; }
    internal bool ContainsControl(in CustomControlRequest request)
        => !_closed && request.IsValid && Find(request) is { } entry && entry.Control==request;
    private void Free(Entry entry) { entry.Clear(); Count--; }
    private void MaybeFree(Entry entry)
    { if (entry.ReceiptAcked && (!entry.ContactExpected || entry.ContactAcked)) Free(entry); }
    internal CustomOutboxOffer Reserve(in CustomWeaponAction action,double now)=>Reserve(action,default,now);
    internal CustomOutboxOffer Reserve(in CustomWeaponAction action,CustomResourceKnowledge knowledge,double now)
    {
        if (!Time(now)) return CustomOutboxOffer.Closed;
        if (!action.IsValid || !knowledge.IsValid || action.Bootstrap!=_wire.Bootstrap || action.Manifest!=_wire.Manifest.Digest
            || action.Slot!=_wire.Slot || action.Weapon.Value!=10) return CustomOutboxOffer.Foreign;
        if (Find(action) is { } prior) return prior.Control==null && prior.Action==action && prior.Knowledge.Matches(knowledge) ? CustomOutboxOffer.Duplicate:CustomOutboxOffer.Conflict;
        if (Count==Capacity) return CustomOutboxOffer.Full;
        int tokens=action.Kind==CustomWeaponActionKind.Fire ? 2:1;
        if (_token > ulong.MaxValue-(ulong)tokens) { Close(); return CustomOutboxOffer.Closed; }
        foreach (var entry in _entries)
            if (!entry.Active)
            {
                entry.Action=action; entry.Knowledge=knowledge; entry.Created=now; entry.ReceiptDue=entry.ContactDue=now;
                entry.ReceiptToken=++_token; entry.ContactExpected=tokens==2;
                if (entry.ContactExpected) entry.ContactToken=++_token;
                entry.Active=true; Count++; return CustomOutboxOffer.Reserved;
            }
        return CustomOutboxOffer.Full;
    }
    internal CustomOutboxOffer Reserve(in CustomControlRequest request,ulong inputFence,double now,uint? authorityFrame=null,double? sourceNow=null)
    {
        if (!Time(now)) return CustomOutboxOffer.Closed;
        if (!request.IsValid || request.Bootstrap!=_wire.Bootstrap || request.Manifest!=_wire.Manifest.Digest
            || request.Slot!=_wire.Slot || request.Weapon.Value!=10
            || unchecked((int)((authorityFrame??_wire.Bootstrap.AuthorityFrame)-_wire.Bootstrap.AuthorityFrame))<0
            || !double.IsFinite(sourceNow??now) || (sourceNow??now)<0) return CustomOutboxOffer.Foreign;
        if (Find(request) is { } prior) return prior.Control==request ? CustomOutboxOffer.Duplicate:CustomOutboxOffer.Conflict;
        if (Count==Capacity) return CustomOutboxOffer.Full;
        int tokens=request.Kind==CustomWeaponActionKind.Fire ? 2:1;
        if (_token>ulong.MaxValue-(ulong)tokens) { Close(); return CustomOutboxOffer.Closed; }
        foreach (var entry in _entries)
            if (!entry.Active)
            {
                entry.Control=request; entry.Knowledge=request.Knowledge; entry.InputFence=inputFence; entry.ControlWorld=authorityFrame??_wire.Bootstrap.AuthorityFrame;
                entry.SourceTime=sourceNow??now;
                entry.Created=now; entry.ReceiptDue=entry.ContactDue=now;
                entry.ReceiptToken=++_token; entry.ContactExpected=tokens==2;
                if (entry.ContactExpected) entry.ContactToken=++_token;
                entry.Active=true; Count++; return CustomOutboxOffer.Reserved;
            }
        return CustomOutboxOffer.Full;
    }
    internal bool TryControl(WeaponOwnerContext owner,uint sequence,ulong inputSerial,out CustomControlRequest request)
    {
        request=default;
        if (_closed) return false;
        foreach (var entry in _entries)
            if (entry.Active && !entry.ReceiptReady && !entry.Action.IsValid && entry.Control is { } control
                && control.Owner==owner && control.Sequence==sequence && inputSerial>entry.InputFence)
            { request=control; return true; }
        return false;
    }
    internal bool Bind(in CustomControlRequest request,in CustomWeaponAction action)
    {
        if (_closed || !request.Matches(action) || Find(request) is not { ReceiptReady:false } entry
            || entry.Control!=request) return false;
        if (entry.Action.IsValid) return entry.Action==action;
        entry.Action=action; return true;
    }
    internal bool TryKnowledge(in CustomWeaponAction action,out CustomResourceKnowledge knowledge)
    {
        knowledge=default;
        if(_closed||Find(action) is not {} entry||entry.Action!=action)return false;
        knowledge=entry.Knowledge;return true;
    }
    internal bool HasControlWitness(in CustomWeaponAction action)
        => !_closed && Find(action) is { ReceiptReady:false,Control:not null } entry && entry.Action==action;
    internal bool ControlSourceExpired(in CustomControlRequest request,uint world,double? wallNow=null)
        => !_closed && Find(request) is { ReceiptReady:false } entry && entry.Control==request && !entry.Action.IsValid
            && (unchecked((int)(world-entry.ControlWorld))>CustomSourceActionJournal.MaxWorldAge
                || !double.IsFinite(wallNow??_time) || (wallNow??_time)<entry.SourceTime
                || (wallNow??_time)-entry.SourceTime>SourceWaitSeconds);
    internal bool PublishDenied(in CustomControlRequest request,uint world,CustomControlDenialReason reason)
    {
        if (_closed || Find(request) is not { ReceiptReady:false } entry || entry.Control!=request || entry.Action.IsValid) return false;
        var denied=new CustomControlDenied(request,entry.ReceiptToken,world,reason); if (!denied.IsValid) return false;
        denied.Write(entry.ReceiptBytes); entry.Denied=true; entry.ReceiptReady=true; entry.ContactExpected=false; return true;
    }
    internal bool TryUnboundControl(int index,out CustomControlRequest request)
    {
        request=default;
        if (_closed || (uint)index>=Capacity || _entries[index] is not { Active:true,ReceiptReady:false } entry
            || entry.Action.IsValid || entry.Control is not { } control) return false;
        request=control; return true;
    }
    internal bool TryUnboundControl(WeaponOwnerContext owner,uint sequence,out CustomControlRequest request)
    {
        request=default;
        for (int index=0;index<Capacity;index++)
            if (TryUnboundControl(index,out var control) && control.Owner==owner && control.Sequence==sequence)
            { request=control; return true; }
        return false;
    }
    internal bool HasActionReservation(in CustomWeaponAction action)
        => !_closed && Find(action) is { ReceiptReady:false } entry && entry.Action==action;
    internal bool HasContactReservation(in CustomWeaponAction action)
        => !_closed && Find(action) is { ContactExpected:true,ContactReady:false } entry && entry.Action==action;
    internal void ReleaseUnprocessed(in CustomWeaponAction action)
    {
        if (Find(action) is { ReceiptReady:false,ContactReady:false } entry && entry.Action==action) Free(entry);
    }
    internal bool Publish(in CustomActionReceipt receipt)=>Publish(receipt,default);
    internal bool Publish(in CustomActionReceipt receipt,CustomResourceKnowledge knowledge)
    {
        if (_closed || Find(receipt.Action) is not { ReceiptReady:false } entry || entry.Action!=receipt.Action||!entry.Knowledge.Matches(knowledge)) return false;
        var message=new CustomReceiptMessage(entry.ReceiptToken,receipt,knowledge);
        if (!CustomCombatResults.Valid(message,_wire.Manifest,_definition)) return false;
        bool noEmission=receipt.Emission is CustomEmissionDisposition.NotAttempted or CustomEmissionDisposition.NoSpawn;
        if (noEmission && entry.ContactReady) return false;
        CustomCombatResults.Write(message,entry.ReceiptBytes,_wire.Manifest,_definition);
        entry.ReceiptReady=true; entry.EmittedWorld=receipt.AuthorityFrame;
        if (noEmission)
        {
            entry.ContactExpected=false;
        }
        return true;
    }
    internal bool Publish(in CustomContactOutcome outcome)
    {
        if (_closed || !outcome.Contact.Key.IsValid) return false;
        var key=outcome.Contact.Key.Action;
        foreach (var entry in _entries)
            if (entry.Active && entry.Action.IsValid && entry.ContactExpected && !entry.ContactReady && CustomActionIdentity.For(entry.Action)==key)
            {
                var message=new CustomContactMessage(_wire.Bootstrap,entry.ContactToken,outcome);
                if (!CustomCombatResults.Valid(message,_wire.Manifest,_definition)) return false;
                CustomCombatResults.Write(message,entry.ContactBytes,_wire.Manifest,_definition); entry.ContactReady=true; return true;
            }
        return false;
    }
    internal bool Acknowledge(in CustomResultAck ack)
    {
        if (_closed || !ack.IsValid || ack.Bootstrap!=_wire.Bootstrap || ack.Manifest!=_wire.Manifest.Digest || ack.Slot!=_wire.Slot) return false;
        foreach (var entry in _entries)
            if (entry.Active && entry.Owner==ack.Owner && entry.Weapon==ack.Weapon && entry.Sequence==ack.Sequence)
            {
                if (ack.Kind==CustomResultKind.Receipt && entry.ReceiptReady && entry.ReceiptSent && entry.ReceiptToken==ack.Token) entry.ReceiptAcked=true;
                else if (ack.Kind==CustomResultKind.Contact && entry.ContactReady && entry.ContactSent && entry.ContactToken==ack.Token) entry.ContactAcked=true;
                else return false;
                MaybeFree(entry); return true;
            }
        return false;
    }
    internal bool TryNext(double now,out PacketType type,out ReadOnlyMemory<byte> bytes,out ulong token)
    {
        type=default; bytes=default; token=0;
        if (!Time(now)) return false;
        foreach (var entry in _entries)
        {
            if (!entry.Active) continue;
            if (entry.ReceiptReady && !entry.ReceiptAcked && now>=entry.ReceiptDue)
            { type=entry.Denied ? PacketType.CustomWeaponControlDenied:PacketType.CustomWeaponReceipt;
                bytes=entry.ReceiptBytes.AsMemory(0,entry.Denied ? CustomControlDenied.Size:CustomReceiptMessage.Size);
                token=entry.ReceiptToken; return true; }
            if (entry.ContactReady && !entry.ContactAcked && now>=entry.ContactDue)
            { type=PacketType.CustomWeaponContact; bytes=entry.ContactBytes; token=entry.ContactToken; return true; }
        }
        return false;
    }
    internal bool MarkSent(ulong token,double now)
    {
        if (!Time(now) || token==0) return false;
        foreach (var entry in _entries)
        {
            if (!entry.Active) continue;
            if (entry.ReceiptReady && token==entry.ReceiptToken) { entry.ReceiptSent=true; entry.ReceiptDue=now+RetrySeconds; return true; }
            if (entry.ContactReady && token==entry.ContactToken) { entry.ContactSent=true; entry.ContactDue=now+RetrySeconds; return true; }
        }
        return false;
    }
    internal bool TryPending(int index,out CustomWeaponAction action)
    {
        action=default;
        if (_closed || (uint)index>=Capacity || !_entries[index].Active || _entries[index].ReceiptReady || !_entries[index].Action.IsValid) return false;
        action=_entries[index].Action; return true;
    }
    internal void AdvanceFlights(uint world)
    {
        if (_closed) return;
        foreach (var entry in _entries)
            if (entry.Active && entry.ReceiptReady && entry.ContactExpected && !entry.ContactReady
                && unchecked((int)(world-entry.EmittedWorld)) > _definition.Mechanics.LifetimeTicks+2)
            { entry.ContactExpected=false; MaybeFree(entry); }
    }
    internal bool HasExpired(double now)
    {
        if (!Time(now)) return true;
        foreach (var entry in _entries) if (entry.Active && now-entry.Created>DeadlineSeconds) return true;
        return false;
    }
    internal void Close()
    { _closed=true; foreach (var entry in _entries) entry.Clear(); Count=0; }
}
