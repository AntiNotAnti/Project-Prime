using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Durable application consumption on the client owner thread. Network callbacks
// never construct GL objects or apply health. Presentation drains typed facts.
internal sealed class CustomReplicaConsumer
{
    internal const int Capacity=CustomReplicaReceiver.SeenCapacity;
    private readonly CustomReplicaReceiver _receiver;
    private readonly CustomReplicaEvent[] _facts=new CustomReplicaEvent[Capacity];
    private readonly CustomReplicaSlot[] _baselineSlots=new CustomReplicaSlot[8];
    private readonly CustomReplicaFlight[] _baselineFlights=new CustomReplicaFlight[64];
    private CustomReplicaCapture _baseline;
    private int _head,_count;
    private bool _installed;
    internal bool Closed {get;private set;}
    internal bool IsReady=>!Closed&&_installed&&_receiver.IsComplete&&!_receiver.Invalidated;
    internal int PendingFacts=>_count;
    internal ulong DeliveredEvents=>_receiver.DeliveredEvents;
    internal ulong StateRevision=>_receiver.StateRevision;
    internal CustomReplicaConsumer(CustomReadinessWire wire,double now)=>_receiver=new(wire,now);
    internal bool Receive(PacketType type,ReadOnlySpan<byte> bytes,double now,out CustomReplicaAck ack)
    {
        ack=default;if(Closed)return false;
        bool accepted;
        if(type==PacketType.CustomWeaponReplicaPage)
        {
            accepted=_receiver.ReceivePage(bytes,now,out ack);
            if(accepted&&_receiver.IsComplete&&!_installed)
            {
                if(!_receiver.TryCopyBaseline(_baselineSlots,_baselineFlights,out _baseline)){Close();ack=default;return false;}
                _installed=true;
            }
            // Exact retry after later facts/state only resends ACK; never
            // restore the original baseline over current presentation again.
        }
        else if(type==PacketType.CustomWeaponReplicaEvent)
        {
            // Kind3 is a whole-table state projection, not a semantic queue fact.
            bool state=bytes.Length>66&&bytes[66]==(byte)CustomReplicaKind.State;
            if(!state&&_count==Capacity)return false;
            ulong before=_receiver.DeliveredEvents;
            accepted=_receiver.ReceiveEvent(bytes,now,out ack);
            if(accepted&&_receiver.DeliveredEvents!=before)
            {
                if(_receiver.LastEvent is not {} value||_count==Capacity){Close();ack=default;return false;}
                _facts[(_head+_count)%Capacity]=value;_count++;
            }
        }
        else return false;
        if(_receiver.Invalidated){Close();ack=default;return false;}
        return accepted;
    }
    internal bool TryTakeFact(out CustomReplicaEvent fact)
    {
        fact=default;if(!IsReady||_count==0)return false;
        fact=_facts[_head];_facts[_head]=default;_head=(_head+1)%Capacity;_count--;return true;
    }
    internal bool TryCopyBaseline(Span<CustomReplicaSlot> slots,Span<CustomReplicaFlight> flights,out CustomReplicaCapture capture)
    {
        capture=default;if(!IsReady||slots.Length<8||flights.Length<64)return false;
        _baselineSlots.AsSpan().CopyTo(slots);_baselineFlights.AsSpan(0,_baseline.FlightCount).CopyTo(flights);capture=_baseline;return true;
    }
    internal bool TryCopyState(Span<CustomReplicaSlot> slots,out uint world)
    {world=0;return IsReady&&_receiver.TryCopyState(slots,out world);}
    internal void Close()
    {Closed=true;_installed=false;_head=_count=0;Array.Clear(_facts);Array.Clear(_baselineSlots);Array.Clear(_baselineFlights);_baseline=default;}
}
