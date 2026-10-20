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
    private sealed class Entry
    {
        internal readonly byte[] ReceiptBytes=new byte[CustomReceiptMessage.Size],ContactBytes=new byte[CustomContactMessage.Size];
        internal CustomWeaponAction Action;
        internal ulong ReceiptToken,ContactToken;
        internal bool Active,ReceiptReady,ReceiptSent,ReceiptAcked,ContactExpected,ContactReady,ContactSent,ContactAcked;
        internal double Created,ReceiptDue,ContactDue;
        internal uint EmittedWorld;
        internal void Clear()
        {
            Action=default; ReceiptToken=ContactToken=0;
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
    { if (!action.IsValid) return null; foreach (var entry in _entries) if (entry.Active && entry.Action.Owner==action.Owner && entry.Action.Sequence==action.Sequence) return entry; return null; }
    private void Free(Entry entry) { entry.Clear(); Count--; }
    private void MaybeFree(Entry entry)
    { if (entry.ReceiptAcked && (!entry.ContactExpected || entry.ContactAcked)) Free(entry); }
    internal CustomOutboxOffer Reserve(in CustomWeaponAction action,double now)
    {
        if (!Time(now)) return CustomOutboxOffer.Closed;
        if (!action.IsValid || action.Bootstrap!=_wire.Bootstrap || action.Manifest!=_wire.Manifest.Digest
            || action.Slot!=_wire.Slot || action.Weapon.Value!=10) return CustomOutboxOffer.Foreign;
        if (Find(action) is { } prior) return prior.Action==action ? CustomOutboxOffer.Duplicate:CustomOutboxOffer.Conflict;
        if (Count==Capacity) return CustomOutboxOffer.Full;
        int tokens=action.Kind==CustomWeaponActionKind.Fire ? 2:1;
        if (_token > ulong.MaxValue-(ulong)tokens) { Close(); return CustomOutboxOffer.Closed; }
        foreach (var entry in _entries)
            if (!entry.Active)
            {
                entry.Action=action; entry.Created=now; entry.ReceiptDue=entry.ContactDue=now;
                entry.ReceiptToken=++_token; entry.ContactExpected=tokens==2;
                if (entry.ContactExpected) entry.ContactToken=++_token;
                entry.Active=true; Count++; return CustomOutboxOffer.Reserved;
            }
        return CustomOutboxOffer.Full;
    }
    internal bool HasActionReservation(in CustomWeaponAction action)
        => !_closed && Find(action) is { ReceiptReady:false } entry && entry.Action==action;
    internal bool HasContactReservation(in CustomWeaponAction action)
        => !_closed && Find(action) is { ContactExpected:true,ContactReady:false } entry && entry.Action==action;
    internal void ReleaseUnprocessed(in CustomWeaponAction action)
    {
        if (Find(action) is { ReceiptReady:false,ContactReady:false } entry && entry.Action==action) Free(entry);
    }
    internal bool Publish(in CustomActionReceipt receipt)
    {
        if (_closed || Find(receipt.Action) is not { ReceiptReady:false } entry || entry.Action!=receipt.Action) return false;
        var message=new CustomReceiptMessage(entry.ReceiptToken,receipt);
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
            if (entry.Active && entry.ContactExpected && !entry.ContactReady && CustomActionIdentity.For(entry.Action)==key)
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
            if (entry.Active && entry.Action.Owner==ack.Owner && entry.Action.Weapon==ack.Weapon && entry.Action.Sequence==ack.Sequence)
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
            { type=PacketType.CustomWeaponReceipt; bytes=entry.ReceiptBytes; token=entry.ReceiptToken; return true; }
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
        if (_closed || (uint)index>=Capacity || !_entries[index].Active || _entries[index].ReceiptReady) return false;
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
