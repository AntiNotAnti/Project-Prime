using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Observer-owned delivery only. It has no reference to private action outboxes,
// canonical ammo/health, launch payments, source clocks, or native shot history.
internal sealed class CustomReplicaMailbox
{
    internal const int Capacity=256;
    private sealed class Entry
    {
        internal CustomWeaponAction Action;
        internal bool Active,NeedsLaunch,LaunchReady,LaunchSent,LaunchAcked,ContactSent,ContactAcked,ContactExpected,DeadlineKnown;
        internal CustomReplicaLaunch Launch;
        internal CustomContactOutcome? Contact;
        internal ulong LaunchToken,ContactToken;
        internal readonly byte[] LaunchBytes=new byte[CustomReplicaCodec.EventPrefix+CustomReplicaCodec.LaunchSize],
            ContactBytes=new byte[CustomReplicaCodec.EventPrefix+CustomContactMessage.Size];
        internal WeaponDigest LaunchDigest,ContactDigest;
        internal double Created,LaunchDue,ContactDue;
        internal uint Deadline;
        internal void Clear()
        {
            Action=default;Active=NeedsLaunch=LaunchReady=LaunchSent=LaunchAcked=ContactSent=ContactAcked=ContactExpected=DeadlineKnown=false;
            Launch=default;Contact=null;LaunchToken=ContactToken=0;LaunchDigest=ContactDigest=default;
            Created=LaunchDue=ContactDue=0;Deadline=0;
        }
    }
    private readonly CustomReadinessWire _wire;
    private readonly ulong _connection;
    private readonly Entry[] _entries=new Entry[Capacity];
    private readonly byte[] _pages=new byte[CustomReplicaCodec.MaximumPages*CustomReplicaCodec.MaximumPayload];
    private readonly int[] _offsets=new int[CustomReplicaCodec.MaximumPages],_lengths=new int[CustomReplicaCodec.MaximumPages];
    private readonly double[] _pageDue=new double[CustomReplicaCodec.MaximumPages];
    private readonly double _created;
    private double _time;
    private ulong _token;
    private int _owed;
    private CustomReplicaPage _baseline;
    private WeaponDigest _baselineDigest;
    private uint _pageSent;
    private bool _hasBaseline;
    internal bool IsReady {get;private set;}
    internal bool Closed {get;private set;}
    internal int Count {get;private set;}
    internal ulong Frontier=>_token;
    internal CustomReplicaMailbox(CustomReadinessWire wire,ulong connection,double now)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if(connection==0||!double.IsFinite(now)||now<0)throw new ArgumentException("Real recipient connection and monotonic start required.");
        _wire=wire;_connection=connection;_created=_time=now;
        for(int i=0;i<Capacity;i++)_entries[i]=new();
    }
    private bool Time(double now)
    {if(Closed||!double.IsFinite(now)||now<_time)return false;_time=now;return true;}
    private Entry? Find(CustomActionIdentity identity)
    {foreach(var entry in _entries)if(entry.Active&&CustomActionIdentity.For(entry.Action)==identity)return entry;return null;}
    private bool Headroom(int tokens)
    {
        ulong owed=(ulong)(_owed+tokens);
        if(_token>ulong.MaxValue-owed)return false;
        ulong oldest=_token+1;
        foreach(var entry in _entries)if(entry.Active)
        {
            if(entry.LaunchToken!=0&&!entry.LaunchAcked)oldest=Math.Min(oldest,entry.LaunchToken);
            if(entry.ContactToken!=0&&!entry.ContactAcked)oldest=Math.Min(oldest,entry.ContactToken);
        }
        ulong last=_token+owed;
        return last<oldest||last-oldest<CustomReplicaReceiver.SeenCapacity;
    }
    internal bool CanPrepare(in CustomWeaponAction action)
    {
        if(Closed||!IsReady||!action.IsValid||action.Kind!=CustomWeaponActionKind.Fire||action.Weapon.Value!=10
            ||action.Manifest!=_wire.Manifest.Digest||action.Bootstrap.Start!=_wire.Bootstrap.Start)return false;
        if(Find(CustomActionIdentity.For(action)) is {} old)return old.Action==action;
        return Count<Capacity&&Headroom(2);
    }
    internal bool Prepare(in CustomWeaponAction action,double now)
    {
        if(!CanPrepare(action)||!Time(now))return false;
        if(Find(CustomActionIdentity.For(action))!=null)return true;
        Add(action,now,true,0);return true;
    }
    private void Add(in CustomWeaponAction action,double now,bool launch,uint deadline)
    {
        foreach(var entry in _entries)if(!entry.Active)
        {
            entry.Action=action;entry.Active=true;entry.NeedsLaunch=launch;entry.LaunchAcked=!launch;
            entry.ContactExpected=true;entry.Created=now;entry.Deadline=deadline;entry.DeadlineKnown=!launch;
            Count++;_owed+=launch?2:1;return;
        }
        throw new InvalidOperationException("Reserved observer capacity disappeared on its owner thread.");
    }
    internal bool BeginBaseline(in CustomReplicaCapture capture,ReadOnlySpan<CustomReplicaSlot> slots,ReadOnlySpan<CustomReplicaFlight> flights,double now,ulong id=1)
    {
        if(!Time(now)||_hasBaseline||Count!=0||id==0||capture.MatchId!=_wire.Bootstrap.Start.MatchId
            ||capture.AuthorityEpoch!=_wire.Bootstrap.Start.AuthorityEpoch||capture.Manifest!=_wire.Manifest.Digest
            ||capture.SlotCount!=8||capture.FlightCount is <0 or >64||slots.Length<8||flights.Length<capture.FlightCount
            ||unchecked((int)(capture.World-_wire.Bootstrap.AuthorityFrame))<0||!Headroom(capture.FlightCount))return false;
        for(int i=0;i<8;i++)if(!CustomReplicaCodec.ValidSlot(slots[i],i,_wire))return false;
        if(!slots[_wire.Slot].Occupied||slots[_wire.Slot].Generation!=_wire.Bootstrap.SlotGeneration)return false;
        for(int i=0;i<capture.FlightCount;i++)
        {
            var flight=flights[i];if(!CustomReplicaCodec.ValidFlight(flight,_wire,capture.World))return false;
            var source=slots[flight.Action.Owner.Slot];if(!source.Occupied||source.Spectating||source.Life==0
                ||unchecked((short)(source.Life-flight.Action.Owner.Life))<0||source.Generation!=flight.Action.Owner.Generation)return false;
            for(int j=0;j<i;j++)if(flights[j].Action==flight.Action)return false;
        }
        var baseline=new CustomReplicaPage(id,_token,capture.World,(byte)capture.FlightCount,0);int length=0;
        for(byte i=0;i<baseline.PageCount;i++)
        {
            var page=baseline with {Index=i};
            if(!CustomReplicaCodec.TryWritePage(_wire,page,slots,flights,_pages.AsSpan(length),out int written))return false;
            _offsets[i]=length;_lengths[i]=written;length+=written;
        }
        // Reservation of every captured flight precedes baseline publication.
        for(int i=0;i<capture.FlightCount;i++)Add(flights[i].Certificate.Action,now,false,flights[i].PaymentDeadline);
        _baseline=baseline;_baselineDigest=CustomReplicaCodec.Hash(_pages.AsSpan(0,length));_hasBaseline=true;return true;
    }
    internal bool HasReservation(in CustomWeaponAction action)
        =>!Closed&&action.IsValid&&Find(CustomActionIdentity.For(action)) is {} entry&&entry.Action==action;
    internal bool PublishLaunch(in CustomReplicaLaunch launch,double now)
    {
        if(!Time(now)||!CustomReplicaCodec.ValidLaunch(launch,_wire,launch.World)||Find(launch.Identity) is not {} entry||entry.Action!=launch.Source.Action||!entry.NeedsLaunch)return false;
        if(entry.LaunchReady){if(entry.Launch==launch)return true;Close();return false;}
        entry.Launch=launch;entry.LaunchToken=++_token;_owed--;
        var value=new CustomReplicaEvent(CustomReplicaKind.Launch,entry.LaunchToken,launch.World,launch,default);
        if(!CustomReplicaCodec.TryWriteEvent(_wire,value,entry.LaunchBytes,out _)){Close();return false;}
        entry.LaunchDigest=CustomReplicaCodec.Hash(entry.LaunchBytes);entry.LaunchReady=true;
        entry.Deadline=unchecked(launch.World+(uint)_wire.Definition.Mechanics.LifetimeTicks+2);
        entry.DeadlineKnown=true;
        if(entry.Contact is {} contact&&!EncodeContact(entry,contact)){Close();return false;}return true;
    }
    private bool EncodeContact(Entry entry,in CustomContactOutcome outcome)
    {
        if(entry.ContactToken!=0)return entry.Contact==outcome;
        entry.ContactToken=++_token;_owed--;
        var contact=new CustomContactMessage(entry.Action.Bootstrap,entry.ContactToken,outcome);
        var value=new CustomReplicaEvent(CustomReplicaKind.Contact,entry.ContactToken,outcome.AuthorityFrame,default,contact);
        if(!CustomReplicaCodec.TryWriteEvent(_wire,value,entry.ContactBytes,out _))return false;
        entry.ContactDigest=CustomReplicaCodec.Hash(entry.ContactBytes);return true;
    }
    internal bool PublishContact(in CustomContactOutcome outcome,double now)
    {
        if(!Time(now)||!outcome.Contact.Key.Action.IsValid||Find(outcome.Contact.Key.Action) is not {} entry
            ||!entry.ContactExpected||!CustomCombatResults.Valid(new CustomContactMessage(entry.Action.Bootstrap,1,outcome),_wire.Manifest,_wire.Definition))return false;
        if(entry.Contact is {} prior){if(prior==outcome)return true;Close();return false;}
        entry.Contact=outcome;
        if(!entry.NeedsLaunch||entry.LaunchReady)
            if(!EncodeContact(entry,outcome)){Close();return false;}
        return true;
    }
    internal bool FinalizeReceipt(in CustomActionReceipt receipt)
    {
        if(Closed||!receipt.Action.IsValid||Find(CustomActionIdentity.For(receipt.Action)) is not {} entry||entry.Action!=receipt.Action)return false;
        if(receipt.Emission==CustomEmissionDisposition.Spawned){if(entry.LaunchReady)return true;Close();return false;}
        // Uncertain effects cannot be silently erased or reissued. The live
        // adapter retires only this observer stream; private payment is separate.
        if(receipt.Emission is not (CustomEmissionDisposition.NotAttempted or CustomEmissionDisposition.NoSpawn)
            ||entry.Contact!=null||entry.LaunchReady){Close();return false;}
        Free(entry);return true;
    }
    private void Free(Entry entry)
    {
        if(entry.NeedsLaunch&&entry.LaunchToken==0)_owed--;
        if(entry.ContactExpected&&entry.ContactToken==0)_owed--;
        entry.Clear();Count--;
    }
    internal void Advance(uint world)
    {
        if(Closed)return;
        foreach(var entry in _entries)if(entry.Active&&entry.ContactExpected&&entry.DeadlineKnown&&entry.Contact==null
            &&unchecked((int)(world-entry.Deadline))>0)
        {
            entry.ContactExpected=false;_owed--;
            if(entry.LaunchAcked)Free(entry);
        }
    }
    internal bool HasExpired(double now)
    {
        if(Closed||!double.IsFinite(now)||now<_time)return true;
        if(!IsReady&&now-_created>15)return true;
        foreach(var entry in _entries)if(entry.Active&&now-entry.Created>15)return true;return false;
    }
    internal bool TryNext(double now,out PacketType type,out ReadOnlyMemory<byte> bytes,out int key)
    {
        type=default;bytes=default;key=0;if(!Time(now)||!_hasBaseline)return false;
        if(!IsReady)
        {
            for(int i=0;i<_baseline.PageCount;i++)if(now>=_pageDue[i])
            {type=PacketType.CustomWeaponReplicaPage;bytes=_pages.AsMemory(_offsets[i],_lengths[i]);key=-(i+1);return true;}
            return false;
        }
        for(int i=0;i<Capacity;i++)
        {
            var entry=_entries[i];if(!entry.Active)continue;
            if(entry.LaunchReady&&!entry.LaunchAcked&&now>=entry.LaunchDue)
            {type=PacketType.CustomWeaponReplicaEvent;bytes=entry.LaunchBytes;key=i*2+1;return true;}
            if(entry.ContactToken!=0&&!entry.ContactAcked&&entry.LaunchAcked&&now>=entry.ContactDue)
            {type=PacketType.CustomWeaponReplicaEvent;bytes=entry.ContactBytes;key=i*2+2;return true;}
        }
        return false;
    }
    internal bool MarkSent(int key,double now)
    {
        if(!Time(now))return false;
        if(key<0)
        {
            int i=-key-1;if(!_hasBaseline||IsReady||(uint)i>=_baseline.PageCount)return false;
            _pageSent|=1u<<i;_pageDue[i]=now+.25;return true;
        }
        if(key==0||key>Capacity*2||!IsReady)return false;
        var entry=_entries[(key-1)/2];if(!entry.Active)return false;
        if((key&1)!=0){if(!entry.LaunchReady)return false;entry.LaunchSent=true;entry.LaunchDue=now+.25;}
        else {if(entry.ContactToken==0||!entry.LaunchAcked)return false;entry.ContactSent=true;entry.ContactDue=now+.25;}return true;
    }
    internal bool Acknowledge(ReadOnlySpan<byte> bytes,ulong connection,double now)
    {
        if(connection!=_connection||!CustomReplicaCodec.TryReadAck(_wire,bytes,out var ack)||!Time(now))return false;
        if(ack.Baseline)
        {
            if(!_hasBaseline||ack.Id!=_baseline.Id||ack.Frontier!=_baseline.Frontier||ack.Digest!=_baselineDigest
                ||_pageSent!=(1u<<_baseline.PageCount)-1)return false;
            IsReady=true;return true;
        }
        if(!IsReady)return false;
        foreach(var entry in _entries)if(entry.Active)
        {
            if(entry.LaunchToken==ack.Id&&entry.LaunchSent&&ack.Digest==entry.LaunchDigest)entry.LaunchAcked=true;
            else if(entry.ContactToken==ack.Id&&entry.ContactSent&&ack.Digest==entry.ContactDigest)entry.ContactAcked=true;
            else continue;
            if(entry.LaunchAcked&&(!entry.ContactExpected||entry.ContactAcked))Free(entry);return true;
        }
        return false;
    }
    internal void Close()
    {
        Closed=true;IsReady=false;foreach(var entry in _entries)entry.Clear();Count=_owed=0;
        Array.Clear(_pages);Array.Clear(_lengths);Array.Clear(_offsets);Array.Clear(_pageDue);
    }
}
