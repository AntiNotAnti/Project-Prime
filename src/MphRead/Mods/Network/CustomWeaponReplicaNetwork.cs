using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private readonly CustomReplicaSlot[] _customReplicaSlots=new CustomReplicaSlot[8];
    private readonly CustomReplicaFlight[] _customReplicaFlights=new CustomReplicaFlight[64];
    private readonly byte[] _customReplicaStateBytes=new byte[CustomReplicaCodec.MaximumPayload];
    private readonly ulong[] _customReplicaStateRevisions=new ulong[8];
    private readonly double[] _customReplicaStateDue=new double[8];
    private readonly CustomReplicaMailbox?[] _customReplicaStateOwners=new CustomReplicaMailbox?[8];
    private void HandleCustomReplicaAck(ReceivedPacket packet)
    {
        if(_sim?.EvidenceScene is not {} scene||Find(packet.Sender) is not {} peer
            ||CurrentCustomRecipientEntry(peer,scene) is not {Replica:not null} entry
            ||packet.ConnectionId==0||packet.ConnectionId!=entry.State.ConnectionId
            ||_transport?.ConnectionStats(peer.EndPoint)?.ConnectionId!=packet.ConnectionId)return;
        // Life0 viewers and exact old-source-life obligations both route through
        // the RECIPIENT entry, without requiring a source inventory or boundary.
        entry.Replica.Acknowledge(packet.Payload,packet.ConnectionId,NetSession.Clock);
    }
    private void PumpCustomReplica()
    {
        if(!DeveloperCustomWeaponCombat||_transport==null||_customReadyEntries==null
            ||_sim?.EvidenceScene is not {CustomCombat:not null} scene||_customAudience==null
            ||_customAudienceStart!=CurrentStartIdentity)return;
        double now=NetSession.Clock;
        for(byte slot=0;slot<8;slot++)
        {
            var candidate=_customReadyEntries[slot];
            var entry=candidate!=null?CurrentCustomRecipientEntry(candidate.Peer,scene):null;
            _customAudience.SetRecipient(slot,entry?.Replica);
        }
        _customAudience.Advance(NetSession.NetFrame);
        bool captured=scene.CustomCombat.TryCaptureReplica(_customReplicaSlots,_customReplicaFlights,out var capture);
        for(byte slot=0;slot<8;slot++)
        {
            if(_customReadyEntries[slot] is not {Replica:not null} candidate
                ||CurrentCustomRecipientEntry(candidate.Peer,scene) is not {} entry)continue;
            var mailbox=entry.Replica!;
            if(mailbox.Closed||mailbox.HasExpired(now))
            {Remove(entry.Peer,"custom replica acknowledgement timed out or stream retired");continue;}
            if(!mailbox.HasBaseline&&captured)
            {
                if(!_customAudience.BeginBaseline(slot,mailbox,capture,_customReplicaSlots,_customReplicaFlights,now))
                {mailbox.Close();continue;}
            }
            for(int n=0;n<4&&mailbox.TryNext(now,out var type,out var bytes,out int key);n++)
            {
                if(!_transport.TrySendSemantic(entry.Peer.EndPoint,type,bytes.Span))break;
                mailbox.MarkSent(key,now);
            }
            if(!captured||!mailbox.IsReady||now<_customReplicaStateDue[slot])continue;
            if(!ReferenceEquals(_customReplicaStateOwners[slot],mailbox))
            {_customReplicaStateOwners[slot]=mailbox;_customReplicaStateRevisions[slot]=0;}
            ulong revision=_customReplicaStateRevisions[slot];
            if(revision==ulong.MaxValue){mailbox.Close();continue;}
            if(CustomReplicaCodec.TryWriteState(entry.State.Wire,capture.World,_customReplicaSlots,_customReplicaStateBytes,out int length,revision+1)
                &&_transport.TrySendSemantic(entry.Peer.EndPoint,PacketType.CustomWeaponReplicaEvent,_customReplicaStateBytes.AsSpan(0,length)))
            {_customReplicaStateRevisions[slot]=revision+1;_customReplicaStateDue[slot]=now+.25;}
        }
        Array.Clear(_customReplicaSlots);Array.Clear(_customReplicaFlights);
    }
    internal bool TryGetCustomReplicaReady(int slot,out bool ready,out int pending)
    {
        ready=false;pending=0;
        if(!_running||(uint)slot>=8||_customReadyEntries?[slot] is not {Replica:not null} entry)return false;
        ready=entry.Replica.IsReady&&!entry.Replica.Closed;pending=entry.Replica.Count;return true;
    }
}

public static partial class NetSession
{
    private static CustomReplicaConsumer? _customReplicaConsumer;
    private static CustomReadinessWire? _customReplicaWire;
    private static ulong _customReplicaConnection;
    private static CustomReplicaConsumer? CurrentCustomReplica()
    {
        if(!CustomWeaponReadinessConfirmed||_customReadiness is not {} ready)
        {_customReplicaConsumer?.Close();_customReplicaConsumer=null;_customReplicaWire=null;_customReplicaConnection=0;return null;}
        if(!ReferenceEquals(_customReplicaWire,ready.Wire)||_customReplicaConnection!=_customReadinessConnection)
        {
            _customReplicaConsumer?.Close();_customReplicaWire=ready.Wire;_customReplicaConnection=_customReadinessConnection;
            _customReplicaConsumer=new(ready.Wire,Clock);
        }
        return _customReplicaConsumer;
    }
    internal static bool CustomWeaponReplicaReady=>CurrentCustomReplica() is {IsReady:true,PendingFacts: < CustomReplicaConsumer.Capacity};
    private static void ResetCustomReplica()
    {_customReplicaConsumer?.Close();_customReplicaConsumer=null;_customReplicaWire=null;_customReplicaConnection=0;}
    internal static bool TryTakeCustomReplicaFact(out CustomReplicaEvent fact)
    {fact=default;return CurrentCustomReplica()?.TryTakeFact(out fact)==true;}
    internal static bool TryCopyCustomReplicaState(Span<CustomReplicaSlot> slots,out uint world)
    {world=0;return CurrentCustomReplica()?.TryCopyState(slots,out world)==true;}
    internal static bool TryCopyCustomReplicaBaseline(Span<CustomReplicaSlot> slots,Span<CustomReplicaFlight> flights,out CustomReplicaCapture capture)
    {capture=default;return CurrentCustomReplica()?.TryCopyBaseline(slots,flights,out capture)==true;}
    private static void HandleCustomReplica(ReceivedPacket packet)
    {
        if(packet.ConnectionId==0||packet.ConnectionId!=_customReadinessConnection||_hostEndPoint==null
            ||!packet.Sender.Equals(_hostEndPoint)||_transport?.ConnectionStats(_hostEndPoint)?.ConnectionId!=packet.ConnectionId
            ||CurrentCustomReplica() is not {} consumer)return;
        if(consumer.Receive(packet.Type,packet.Payload,Clock,out var ack)&&ack.Id!=0&&_transport!=null)
        {
            Span<byte> bytes=stackalloc byte[CustomReplicaAck.Size];
            if(_customReplicaWire!=null&&CustomReplicaCodec.TryWriteAck(_customReplicaWire,ack,bytes))
                _transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponReplicaAck,bytes);
        }
        if(consumer.Closed)_customReadiness?.Invalidate();
    }
}
