using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private readonly CustomPickupReplicaMailbox?[] _customPickupMailboxes=new CustomPickupReplicaMailbox?[8];
    private readonly CustomReadyEntry?[] _customPickupOwners=new CustomReadyEntry?[8];
    private bool PickupRecipientReady(CustomReadyEntry entry)
        =>!PublicCustomWeaponMatch||ReferenceEquals(_customPickupOwners[entry.Peer.SlotIndex],entry)
            &&_customPickupMailboxes[entry.Peer.SlotIndex]?.IsReady==true;
    private void ReleaseCustomPickups()
    {
        foreach(var mailbox in _customPickupMailboxes)mailbox?.Close();
        Array.Clear(_customPickupMailboxes);Array.Clear(_customPickupOwners);
    }
    private void HandleCustomPickupAck(ReceivedPacket packet)
    {
        if(!PublicCustomWeaponMatch||_sim?.EvidenceScene is not {} scene||Find(packet.Sender) is not {} peer
            ||CurrentCustomRecipientEntry(peer,scene) is not {} entry||packet.ConnectionId==0||packet.ConnectionId!=entry.State.ConnectionId
            ||!ReferenceEquals(_customPickupOwners[peer.SlotIndex],entry))return;
        _customPickupMailboxes[peer.SlotIndex]?.Acknowledge(packet.Payload);
    }
    private void PumpCustomPickups()
    {
        if(!PublicCustomWeaponMatch||_transport==null||_sim?.EvidenceScene is not {CustomCombat.Pickups:{} pickups} scene)return;
        double now=NetSession.Clock;
        for(int slot=0;slot<8;slot++)
        {
            var candidate=_customReadyEntries?[slot];
            var entry=candidate!=null?CurrentCustomRecipientEntry(candidate.Peer,scene):null;
            if(!ReferenceEquals(entry,_customPickupOwners[slot]))
            {
                _customPickupMailboxes[slot]?.Close();_customPickupOwners[slot]=entry;
                _customPickupMailboxes[slot]=entry==null?null:new(entry.State.Wire.Bootstrap,pickups.World,pickups.Layout);
            }
            if(entry==null||_customPickupMailboxes[slot] is not {} mailbox)continue;
            for(int i=0;i<pickups.Layout.Count;i++)
                if(pickups.TryState(i,out var item))mailbox.Publish(i,item,pickups.AuthorityFrame,now);
            if(mailbox.Closed||mailbox.HasExpired(now)){Remove(entry.Peer,"custom pickup acknowledgement timed out");continue;}
            for(int n=0;n<4&&mailbox.TryNext(now,out int index,out var bytes);n++)
            {
                if(!_transport.TrySendSemantic(entry.Peer.EndPoint,PacketType.CustomWeaponPickupState,bytes.Span))break;
                mailbox.Sent(index,now);
            }
        }
    }
    private bool CanCollectCustomPickup(Scene scene,CustomAuthorityCombatAdapter root,int index)
    {
        if(scene.CustomCombat!=root||root.Pickups==null||!CustomRoomReady(scene))return false;
        foreach(var peer in _peers)
            if(CurrentCustomRecipientEntry(peer,scene) is not {} entry||!ReferenceEquals(_customPickupOwners[peer.SlotIndex],entry)
                ||_customPickupMailboxes[peer.SlotIndex]?.CanChange(index)!=true)return false;
        return true;
    }
    private bool PublishCustomPickup(Scene scene,CustomAuthorityCombatAdapter root,in CustomPickupOutcome outcome)
    {
        if(root.Pickups is not {} pickups||pickups.World!=outcome.Consumed.World)return false;
        int index=-1;for(int i=0;i<pickups.Layout.Count;i++)if(pickups.Layout.Placements[i].SpawnId==outcome.Consumed.SpawnId){index=i;break;}
        if(index<0||!CanCollectCustomPickup(scene,root,index))return false;
        foreach(var peer in _peers)
            if(_customPickupMailboxes[peer.SlotIndex]?.Publish(index,outcome.Next,outcome.AuthorityFrame,NetSession.Clock)!=true)return false;
        return true;
    }
}

public static partial class NetSession
{
    private static CustomPickupReplicaReceiver? _customPickupReceiver;
    private static CustomReadinessWire? _customPickupWire;
    private static ulong _customPickupConnection;
    private static void ResetCustomPickupReplica()
    {_customPickupReceiver=null;_customPickupWire=null;_customPickupConnection=0;}
    private static CustomPickupReplicaReceiver? CurrentCustomPickupReplica()
    {
        if(!PublicCustomWeaponAvailability||!CustomWeaponReadinessConfirmed||_customReadiness is not {} ready
            ||ServerSession is not {} session){ResetCustomPickupReplica();return null;}
        if(!ReferenceEquals(_customPickupWire,ready.Wire)||_customPickupConnection!=_customReadinessConnection)
        {
            using var registry=new WeaponRegistry(true,new[]{ready.Wire.Definition});
            var layout=new CustomPickupLayout(session.Match.MapIdentity.Content(session.Match.RoomKey),ready.Wire.Manifest,registry,
                StockRiftlinePickups.ForRoom(session.Match.RoomKey));
            _customPickupReceiver=new(ready.Wire.Bootstrap,layout);_customPickupWire=ready.Wire;_customPickupConnection=_customReadinessConnection;
        }
        return _customPickupReceiver;
    }
    private static void HandleCustomPickupState(ReceivedPacket packet)
    {
        if(packet.ConnectionId==0||packet.ConnectionId!=_customReadinessConnection||_transport==null||_hostEndPoint==null
            ||!packet.Sender.Equals(_hostEndPoint)||_transport.ConnectionStats(_hostEndPoint)?.ConnectionId!=packet.ConnectionId
            ||CurrentCustomPickupReplica() is not {} receiver)return;
        if(!receiver.Receive(packet.Payload,out var ack))
        {if(receiver.Closed)_customReadiness?.Invalidate();return;}
        Span<byte> bytes=stackalloc byte[CustomPickupReplicaCodec.AckSize];CustomPickupReplicaCodec.Write(ack,bytes);
        _transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponPickupAck,bytes);
    }
    internal static bool TryGetCustomPickupReplica(out CustomPickupReplicaReceiver receiver)
    {
        // Render reads cannot allocate a registry, advance state or open a lane.
        receiver=_customPickupReceiver!;
        return receiver?.IsReady==true&&PublicCustomWeaponAvailability&&CustomWeaponReadinessConfirmed
            &&_customPickupWire?.Bootstrap==ConfirmedWorldBootstrap&&_customPickupConnection==_customReadinessConnection;
    }
}
