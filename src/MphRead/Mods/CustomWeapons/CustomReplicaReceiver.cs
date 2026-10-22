using System;

namespace MphRead.Mods.CustomWeapons;

// A single authenticated recipient incarnation. The network adapter must fence
// the authority endpoint/connection before calling this pure presentation owner.
internal sealed class CustomReplicaReceiver
{
    internal const int SeenCapacity=1024;
    private readonly CustomReadinessWire _wire;
    private readonly byte[] _pages=new byte[CustomReplicaCodec.MaximumPages*CustomReplicaCodec.MaximumPayload];
    private readonly int[] _lengths=new int[CustomReplicaCodec.MaximumPages];
    private readonly byte[] _hashBytes=new byte[CustomReplicaCodec.MaximumPages*CustomReplicaCodec.MaximumPayload];
    private readonly CustomReplicaSlot[] _slots=new CustomReplicaSlot[8],_current=new CustomReplicaSlot[8],_nextState=new CustomReplicaSlot[8];
    private readonly CustomReplicaFlight[] _flights=new CustomReplicaFlight[64];
    private readonly ulong[] _tokens=new ulong[SeenCapacity];
    private readonly WeaponDigest[] _digests=new WeaponDigest[SeenCapacity];
    private readonly double _created;
    private CustomReplicaPage _baseline;
    private uint _mask,_stateWorld;
    private WeaponDigest _baselineDigest,_stateDigest;
    private ulong _highest;
    private ulong _stateRevision;
    private double _time;
    internal bool IsComplete {get;private set;}
    internal bool Invalidated {get;private set;}
    internal ulong DeliveredEvents {get;private set;}
    internal CustomReplicaEvent? LastEvent {get;private set;}
    internal uint StateWorld=>_stateWorld;
    internal ulong StateRevision=>_stateRevision;
    internal CustomReplicaReceiver(CustomReadinessWire wire,double now)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if(!double.IsFinite(now)||now<0)throw new ArgumentException("Finite monotonic incarnation start required.");
        _wire=wire;_created=_time=now;
    }
    private bool Time(double now)
    {
        if(Invalidated||!double.IsFinite(now)||now<_time)return false;
        _time=now;
        if(!IsComplete&&now-_created>15){Invalidated=true;return false;}return true;
    }
    internal bool ReceivePage(ReadOnlySpan<byte> bytes,double now,out CustomReplicaAck ack)
    {
        ack=default;
        if(!Time(now)||!CustomReplicaCodec.TryReadPage(_wire,bytes,out var page))return false;
        if(_mask==0)_baseline=page with {Index=0};
        else if((page with {Index=0})!=_baseline){Invalidated=true;return false;}
        int offset=page.Index*CustomReplicaCodec.MaximumPayload;
        uint bit=1u<<page.Index;
        if((_mask&bit)!=0)
        {
            if(_lengths[page.Index]!=bytes.Length||!bytes.SequenceEqual(_pages.AsSpan(offset,bytes.Length)))
            {Invalidated=true;return false;}
        }
        else
        {
            bytes.CopyTo(_pages.AsSpan(offset));_lengths[page.Index]=bytes.Length;_mask|=bit;
        }
        if(_mask!=(1u<<page.PageCount)-1)return true;
        if(!IsComplete)
        {
            int length=0;
            for(byte i=0;i<page.PageCount;i++)
            {
                var body=_pages.AsSpan(i*CustomReplicaCodec.MaximumPayload,_lengths[i]);
                var part=_baseline with {Index=i};
                CustomReplicaCodec.CopyPage(_wire,body,part,_slots,_flights);
                body.CopyTo(_hashBytes.AsSpan(length));length+=body.Length;
            }
            for(int i=0;i<_baseline.Flights;i++)
            {
                var identity=_flights[i].Action;var slot=_slots[identity.Owner.Slot];
                if(!slot.Occupied||slot.Spectating||slot.Life==0||unchecked((short)(slot.Life-identity.Owner.Life))<0
                    ||slot.Generation!=identity.Owner.Generation)
                {Invalidated=true;return false;}
                for(int j=0;j<i;j++)if(_flights[j].Action==identity){Invalidated=true;return false;}
            }
            if(!_slots[_wire.Slot].Occupied||_slots[_wire.Slot].Generation!=_wire.Bootstrap.SlotGeneration)
            {Invalidated=true;return false;}
            _baselineDigest=CustomReplicaCodec.Hash(_hashBytes.AsSpan(0,length));
            _slots.AsSpan().CopyTo(_current);_stateWorld=_baseline.World;_highest=_baseline.Frontier;IsComplete=true;
        }
        ack=new(true,_baseline.Id,_baseline.Frontier,_baselineDigest);return true;
    }
    internal bool ReceiveEvent(ReadOnlySpan<byte> bytes,double now,out CustomReplicaAck ack)
    {
        ack=default;if(!Time(now)||!IsComplete)return false;
        if(CustomReplicaCodec.TryReadState(_wire,bytes,out uint world,out ulong revision))
        {
            if(revision<_stateRevision)return true;
            var digest=CustomReplicaCodec.Hash(bytes);
            if(revision==_stateRevision&&_stateDigest.IsValid&&digest!=_stateDigest){Invalidated=true;return false;}
            if(unchecked((int)(world-_stateWorld))<0){Invalidated=true;return false;}
            CustomReplicaCodec.CopyState(_wire,bytes,_nextState);
            _nextState.AsSpan().CopyTo(_current);_stateWorld=world;_stateDigest=digest;_stateRevision=revision;return true;
        }
        if(!CustomReplicaCodec.TryReadEvent(_wire,bytes,out var value))return false;
        var hash=CustomReplicaCodec.Hash(bytes);ulong token=value.Token;
        if(token>_baseline.Frontier)
        {
            if(token<=_highest&&_highest-token>=SeenCapacity)return false;
            int at=(int)(token%SeenCapacity);
            if(_tokens[at]==token)
            {if(_digests[at]!=hash){Invalidated=true;return false;}}
            else
            {
                // A live sender also constrains its outstanding token span.
                // Refuse a malicious jump that could erase unconsumed evidence.
                if(token>_highest&&token-_highest>SeenCapacity)return false;
                _tokens[at]=token;_digests[at]=hash;_highest=Math.Max(_highest,token);
                LastEvent=value;DeliveredEvents++;
            }
        }
        ack=new(false,token,0,hash);return true;
    }
    internal bool TryCopyBaseline(Span<CustomReplicaSlot> slots,Span<CustomReplicaFlight> flights,out CustomReplicaCapture capture)
    {
        capture=default;if(!IsComplete||Invalidated||slots.Length<8||flights.Length<64)return false;
        _slots.AsSpan().CopyTo(slots);_flights.AsSpan(0,_baseline.Flights).CopyTo(flights);
        capture=new(_wire.Bootstrap.Start.MatchId,_wire.Bootstrap.Start.AuthorityEpoch,_wire.Manifest.Digest,_baseline.World,8,_baseline.Flights);return true;
    }
    internal bool TryCopyState(Span<CustomReplicaSlot> slots,out uint world)
    {world=0;if(!IsComplete||Invalidated||slots.Length<8)return false;_current.AsSpan().CopyTo(slots);world=_stateWorld;return true;}
}
