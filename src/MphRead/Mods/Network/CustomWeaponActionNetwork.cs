using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private sealed class CustomActionPeer(CustomReadinessWire wire)
    {
        internal readonly CustomActionOutbox Outbox=new(wire,wire.Definition);
        internal readonly byte[] StateBytes=new byte[CustomStateMessage.Size];
        internal CustomStateMessage? State;
        internal double StateDue,OfferWindow;
        internal int Offers;
        internal byte ControlMode; // 0 unused, 1 strict source action, 2 reliable primary control
        internal WeaponOwnerContext ModeOwner;
        internal bool AcceptsMode(WeaponOwnerContext owner,byte mode) => ModeOwner!=owner || ControlMode==0 || ControlMode==mode;
        internal void UseMode(WeaponOwnerContext owner,byte mode) { ModeOwner=owner; ControlMode=mode; }
        internal bool Admit(double now)
        {
            if (now-OfferWindow>=1) { OfferWindow=now; Offers=0; }
            if (Offers>=64) return false; Offers++; return true;
        }
    }
    private void InvalidateCustomEntry(CustomReadyEntry? entry)
    {
        if(entry==null)return;
        _customAudience?.CancelPrepared((byte)entry.Peer.SlotIndex,entry.State.Wire.Bootstrap,NetSession.NetFrame);
        entry.State.Invalidate();entry.Combat?.Outbox.Close();entry.Replica?.Close();
    }
    private bool PublishCustomReceipt(CustomActionPeer combat,in CustomActionReceipt receipt)
    {
        if(!combat.Outbox.Publish(receipt))return false;
        _customAudience?.FinalizeReceipt(receipt);return true;
    }
    private CustomReadyEntry? CurrentCustomActionEntry(int slot,Scene scene)
    {
        if ((uint)slot>=8) return null;
        ushort generation=NetPlayerLifecycle.Generation(slot),life=NetPlayerLifecycle.Get(slot);
        if (generation==0 || life==0 || _matchId==0 || _authorityEpoch==0) return null;
        return CurrentCustomSourceEntry(new(_matchId,_authorityEpoch,(byte)slot,generation,life),scene);
    }
    private CustomReadyEntry? CurrentCustomResultEntry(in CustomWeaponAction action,Scene scene)
        => action.IsValid && CurrentCustomActionEntry(action.Slot,scene) is { Combat:not null } entry
            && entry.State.Wire.Bootstrap==action.Bootstrap && entry.State.Wire.Manifest.Digest==action.Manifest ? entry:null;
    private bool CustomRoomReady(Scene scene)
    {
        if (_phase!=SessionPhase.InMatch || _bots.Count!=0 || _peers.Count==0) return false;
        foreach (var peer in _peers)
        {
            if(CurrentCustomRecipientEntry(peer,scene) is not {Replica.IsReady:true} recipient
                ||recipient.Replica.HasExpired(NetSession.Clock))return false;
            if(!peer.Spectating&&(CurrentCustomActionEntry(peer.SlotIndex,scene) is not {State.Evidence:not null} entry
                ||entry.Peer!=peer||entry.State.Evidence!.Manifest!=scene.CustomCombat?.Manifest.Digest))return false;
        }
        return true;
    }
    // Initial publication waits for all recipients. Once a public life has
    // canonical state, a newly joining peer must not strand its result nonce.
    // Reserve bounded results and terminally deny controls while execution waits.
    private bool CustomCanReceiveAction(Scene scene,CustomActionPeer combat)
        => CustomRoomReady(scene) || PublicCustomWeaponMatch && combat.State is { } published
            && NetPlayerLifecycle.Matches(published.State.Owner.Slot,published.State.Owner.Generation,published.State.Owner.Life);
    private sealed class LiveCustomCombatDelivery(DedicatedServer server,Scene scene) : ICustomCombatDelivery
    {
        public bool HasActionReservation(in CustomWeaponAction action)
            => (server.CustomRoomReady(scene) || server.PublicCustomWeaponMatch)
                && server.CurrentCustomResultEntry(action,scene)?.Combat?.Outbox.HasActionReservation(action)==true;
        public bool MayExecute(in CustomWeaponAction action) => server.CustomRoomReady(scene);
        public bool HasContactReservation(in CustomWeaponAction action)
            => server.CurrentCustomResultEntry(action,scene)?.Combat?.Outbox.HasContactReservation(action)==true;
        public bool Publish(in CustomActionReceipt receipt)
        {
            return server.CurrentCustomResultEntry(receipt.Action,scene)?.Combat is {} combat
                &&server.PublishCustomReceipt(combat,receipt);
        }
        public bool Publish(in CustomContactOutcome outcome)
        {
            var identity=outcome.Contact.Key.Action;
            if (!identity.IsValid || server.CurrentCustomActionEntry(identity.Owner.Slot,scene)?.Combat is not { } combat) return false;
            if(!combat.Outbox.Publish(outcome))return false;
            server._customAudience?.PublishContact(outcome,NetSession.Clock);return true;
        }
        public void AfterSourcePhase(CustomAuthorityCombatAdapter root) => server.ProcessCustomActions(scene,root);
        public void SourcePhaseSealed(CustomAuthorityCombatAdapter root)=>server.PumpCustomNativeIntents();
        public bool HasControlWitness(in CustomWeaponAction action)
            => server.CurrentCustomResultEntry(action,scene)?.Combat?.Outbox.HasControlWitness(action)==true;
        public bool TryPrepareAudience(in CustomWeaponAction action)
            =>server.CustomRoomReady(scene)&&server._customAudience?.Prepare(action,NetSession.Clock)==true;
        public bool HasPreparedAudience(in CustomWeaponAction action)=>server._customAudience?.HasPrepared(action)==true;
        public bool PublishLaunch(in CustomReplicaLaunch launch)=>server._customAudience?.PublishLaunch(launch,NetSession.Clock)==true;
        public void SourceObserved(CustomAuthorityCombatAdapter root,in CustomSourceInput input)
            => server.BindCustomControl(scene,root,input);
    }
    private void HandleCustomWeaponAction(ReceivedPacket packet,double now)
    {
        if (!CustomWeaponCombatEnabled || _sim?.EvidenceScene is not { CustomCombat:not null } scene
            || Find(packet.Sender) is not { } peer || CurrentCustomActionEntry(peer.SlotIndex,scene) is not { Combat:not null } entry
            || entry.Peer!=peer || packet.ConnectionId==0 || packet.ConnectionId!=entry.State.ConnectionId
            || !entry.Combat.Admit(now) || !CustomCanReceiveAction(scene,entry.Combat)
            || !CustomWeaponAction.TryRead(packet.Payload,out var action) || action.Slot!=peer.SlotIndex
            || action.Bootstrap!=peer.BootstrapIdentity || action.Manifest!=entry.State.Wire.Manifest.Digest
            || action.Weapon.Value!=10 || !NetPlayerLifecycle.Matches(action.Slot,action.Owner.Generation,action.Life)
            || !entry.Combat.AcceptsMode(action.Owner,1)) return;
        var offered=entry.Combat.Outbox.Reserve(action,now);
        if (offered is not (CustomOutboxOffer.Reserved or CustomOutboxOffer.Duplicate)) return;
        entry.Combat.UseMode(action.Owner,1);
        if (offered==CustomOutboxOffer.Duplicate) return; // Retained verdict retries independently; never execute twice.
        var root=scene.CustomCombat;
        var queued=root.Offer(action);
        if (queued==CustomQueueResult.Queued) return;
        if (queued==CustomQueueResult.Duplicate && root.TryReceipt(action,out var receipt))
        { PublishCustomReceipt(entry.Combat,receipt); return; }
        if (queued is CustomQueueResult.Retired or CustomQueueResult.WindowExceeded)
        {
            PublishCustomReceipt(entry.Combat,new CustomActionReceipt(action,NetSession.NetFrame,CustomJournalDisposition.SourceDenied,
                WeaponActionResult.InvalidLife,CustomEmissionDisposition.NotAttempted,null,null,null)); return;
        }
        entry.Combat.Outbox.ReleaseUnprocessed(action); // Backpressure/foreign is nonterminal; retry exact bytes.
    }
    private void HandleCustomResultAck(ReceivedPacket packet,double now)
    {
        if (!CustomWeaponCombatEnabled || _sim?.EvidenceScene is not { } scene || Find(packet.Sender) is not { } peer
            || CurrentCustomActionEntry(peer.SlotIndex,scene) is not { Combat:not null } entry || entry.Peer!=peer
            || packet.ConnectionId==0 || packet.ConnectionId!=entry.State.ConnectionId || !entry.Combat.Admit(now)
            || !CustomResultAck.TryRead(packet.Payload,out var ack) || ack.Slot!=peer.SlotIndex) return;
        entry.Combat.Outbox.Acknowledge(ack); // Includes old-life obligations, never foreign occupants/connections.
    }
    private void ProcessCustomActions(Scene scene,CustomAuthorityCombatAdapter root)
    {
        if (!_running || _sim?.EvidenceScene!=scene || scene.CustomCombat!=root) return;
        for (int slot=0;slot<8;slot++)
        {
            if (CurrentCustomActionEntry(slot,scene) is not { Combat:not null } entry) continue;
            var combat=entry.Combat;
            for (int n=0;n<CustomActionOutbox.Capacity;n++)
                if (combat.Outbox.TryUnboundControl(n,out var request) && !root.IsActionLifeActive(request.Owner)
                    && (!NetPlayerLifecycle.Matches(request.Slot,request.Owner.Generation,request.Life)
                        || root.TryStateMetadata(scene.Players.Items[slot],out _,out _,out _)))
                    combat.Outbox.PublishDenied(request,NetSession.NetFrame,CustomControlDenialReason.LifeRetired);
            if (root.TryStateMetadata(scene.Players.Items[slot],out _,out uint settledControl,out _) && settledControl!=uint.MaxValue
                && combat.Outbox.TryUnboundControl(new(_matchId,_authorityEpoch,(byte)slot,NetPlayerLifecycle.Generation(slot),NetPlayerLifecycle.Get(slot)),
                    settledControl+1,out var waiting) && combat.Outbox.ControlSourceExpired(waiting,NetSession.NetFrame,NetSession.Clock))
            {
                if (root.SettleUnobserved(waiting)) combat.Outbox.PublishDenied(waiting,NetSession.NetFrame,CustomControlDenialReason.SourceUnavailable);
                else InvalidateCustomEntry(entry);
            }
            if (CustomRoomReady(scene) || PublicCustomWeaponMatch)
                for (int n=0;n<CustomSourceActionJournal.MaxDrainPerTick;n++)
                    if (!root.TryProcess(scene.Players.Items[slot],out _)) break;
            for (int n=0;n<CustomActionOutbox.Capacity;n++)
                if (combat.Outbox.TryPending(n,out var pending) && !root.IsActionLifeActive(pending.Owner))
                    PublishCustomReceipt(combat,new CustomActionReceipt(pending,NetSession.NetFrame,CustomJournalDisposition.SourceDenied,
                        WeaponActionResult.InvalidLife,CustomEmissionDisposition.NotAttempted,null,null,null));
            combat.Outbox.AdvanceFlights(NetSession.NetFrame);
            var player=scene.Players.Items[slot];
            if (!root.TryState(player,out var snapshot) || !root.TryStateMetadata(player,out var selected,out var settled,out var committed)) continue;
            if (combat.State is { } old && old.State==snapshot && old.Selected==selected
                && old.SettledSequence==settled && old.CommittedSource==committed) continue;
            // Public controls cannot start their result deadline while another
            // recipient is still preparing its durable baseline. Baseline
            // assembly and native source ingress stay independent of this state.
            if (PublicCustomWeaponMatch && combat.State?.State.Owner != snapshot.Owner && !CustomRoomReady(scene)) continue;
            uint revision=combat.State?.Revision ?? 0;
            if (revision==uint.MaxValue) { InvalidateCustomEntry(entry); continue; }
            var state=new CustomStateMessage(entry.State.Wire.Bootstrap,root.Manifest.Digest,new(10),NetSession.NetFrame,
                revision+1,settled,selected,committed,snapshot);
            CustomCombatResults.Write(state,combat.StateBytes,entry.State.Wire.Manifest,entry.State.Wire.Definition); combat.State=state;
        }
    }
    private void PumpCustomActionResults(double now)
    {
        if (!CustomWeaponCombatEnabled || _transport==null || _customReadyEntries==null) return;
        for (int slot=0;slot<8;slot++)
        {
            if (_customReadyEntries[slot] is not { Combat:not null } entry) continue;
            var combat=entry.Combat;
            if (combat.Outbox.HasExpired(now)) { Remove(entry.Peer,"custom action result acknowledgement timed out"); continue; }
            for (int n=0;n<4 && combat.Outbox.TryNext(now,out var type,out var bytes,out var token);n++)
            {
                if (!_transport.TrySendSemantic(entry.Peer.EndPoint,type,bytes.Span)) break;
                combat.Outbox.MarkSent(token,now);
            }
            if (combat.State.HasValue && now>=combat.StateDue
                && _transport.TrySendSemantic(entry.Peer.EndPoint,PacketType.CustomWeaponState,combat.StateBytes)) combat.StateDue=now+.25;
        }
    }
    // Diagnostics only. Neither this count nor a cross-thread state query grants
    // capacity, opens readiness or authorizes an action.
    internal bool TryGetCustomActionPending(int slot,out int count)
    {
        count=0;
        if (!_running || (uint)slot>=8 || _customReadyEntries?[slot]?.Combat is not { } combat) return false;
        count=combat.Outbox.Count; return true;
    }
}
