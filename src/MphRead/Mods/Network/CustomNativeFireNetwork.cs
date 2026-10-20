using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private bool FilterCustomNativeIntent(Peer peer,in IntentPacket intent,ulong connection,out IntentPacket filtered)
    {
        filtered=intent;
        if(_sim?.EvidenceScene is not {CustomCombat:{} root} scene)return true;
        // Once a source enters this lane, losing readiness cannot turn its old
        // custom-owned fire into native fire. Native-only rooms never enter it.
        if(intent.MatchId==0||intent.AuthorityEpoch==0||intent.SlotGeneration==0||intent.LifeId==0)return false;
        var owner=new WeaponOwnerContext(intent.MatchId,intent.AuthorityEpoch,(byte)peer.SlotIndex,intent.SlotGeneration,intent.LifeId);
        if(!root.HasNativeSelectionHistory(owner)&&CurrentCustomActionEntry(peer.SlotIndex,scene)==null)return true;
        if(connection==0||_transport?.ConnectionStats(peer.EndPoint)?.ConnectionId!=connection
            ||!NetPlayerLifecycle.AcceptEventCarrier(peer.SlotIndex,intent))return false;
        return root.FilterNativeIntent((byte)peer.SlotIndex,intent,connection,out filtered);
    }
    private void PumpCustomNativeIntents()
    {
        if(_sim?.EvidenceScene?.CustomCombat is not {} root)return;
        for(int slot=0;slot<8;slot++)
            for(int count=0;count<8&&root.TryTakeNativeIntent(slot,out var intent,out ulong connection,out var key);count++)
            {
                Peer? peer=null;
                foreach(var candidate in _peers)if(candidate.SlotIndex==slot){peer=candidate;break;}
                if(peer is not {MatchReady:true}||connection==0
                    ||_transport?.ConnectionStats(peer.EndPoint)?.ConnectionId!=connection
                    ||intent.MatchId!=_matchId||intent.AuthorityEpoch!=_authorityEpoch
                    ||intent.SlotGeneration!=_slotGenerations[slot])
                {root.RejectNativeIntent(key);continue;}
                if(!root.ReleaseNativeIntent(key,connection)){root.RejectNativeIntent(key);continue;}
                // Original source/pose/history, recovered as an old carrier.
                // This cannot refresh peer liveness or create a custom source.
                if(NetSession.AcceptSlotIntent(slot,intent))RelayNativeIntent(peer,intent);
            }
    }
    private void RelayNativeIntent(Peer peer,in IntentPacket intent)
    {
        _scratch[0]=(byte)peer.SlotIndex;
        var presentation=NetFireEvents.WithoutTurretOffers(intent);
        _sim?.EvidenceScene?.CustomCombat?.FilterAdmittedNativeEvents(peer.SlotIndex,ref presentation);
        int length=CompactIntentCodec.Write(presentation,_scratch.AsSpan(1));
        foreach(var recipient in _peers)
            if(recipient!=peer&&recipient.MatchReady)
                _transport?.Send(recipient.EndPoint,PacketType.SlotIntent,_scratch.AsSpan(0,length+1));
    }
}
