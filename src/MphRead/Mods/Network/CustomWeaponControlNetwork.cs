using OpenTK.Mathematics;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private void HandleCustomWeaponControl(ReceivedPacket packet,double now)
    {
        if (!DeveloperCustomWeaponCombat || _sim?.EvidenceScene is not { CustomCombat:not null } scene
            || Find(packet.Sender) is not { } peer || CurrentCustomActionEntry(peer.SlotIndex,scene) is not { Combat:not null,Boundary:not null } entry
            || entry.Peer!=peer || packet.ConnectionId==0 || packet.ConnectionId!=entry.State.ConnectionId
            || !entry.Combat.Admit(now) || !CustomRoomReady(scene)
            || !CustomControlRequest.TryRead(packet.Payload,out var request) || request.Slot!=peer.SlotIndex
            || request.Bootstrap!=peer.BootstrapIdentity || request.Manifest!=entry.State.Wire.Manifest.Digest
            || request.Weapon.Value!=10 || !NetPlayerLifecycle.Matches(request.Slot,request.Owner.Generation,request.Life)
            || !entry.Combat.AcceptsMode(request.Owner,2)
            || !entry.Boundary.TryGetFence(request.Owner,out ulong fence)) return;
        AdmitCustomControl(scene,entry,request,fence,now);
    }
    private void AdmitCustomControl(Scene scene,CustomReadyEntry entry,in CustomControlRequest request,ulong fence,double now)
    {
        if (entry.Combat==null) return;
        var root=scene.CustomCombat;
        if (root==null) return;
        // The real client also needs canonical initial state. Never retain a
        // primary control across an unbound/invalid life waiting for a favorable
        // biped pose to create its resource grant.
        if (!root.IsActionLifeActive(request.Owner)
            || !root.TryStateMetadata(scene.Players.Items[request.Slot],out _,out uint settled,out _)) return;
        if ((ulong)request.Sequence> (ulong)settled+CustomSourceActionJournal.PendingCapacity) return;
        var offered=entry.Combat.Outbox.Reserve(request,fence,now,NetSession.NetFrame,NetSession.Clock);
        if (offered is not (CustomOutboxOffer.Reserved or CustomOutboxOffer.Duplicate)) return;
        entry.Combat.UseMode(request.Owner,2);
        if (offered==CustomOutboxOffer.Duplicate) return;
        if (request.Sequence>settled) return; // Reservation waits for a genuinely new consumed carrier.
        if (root.TryControlReceipt(request,out var receipt))
        {
            if (entry.Combat.Outbox.Bind(request,receipt.Action)) entry.Combat.Outbox.Publish(receipt);
        }
        else if (root.TryUnobservedControl(request,out var denied))
            entry.Combat.Outbox.PublishDenied(request,denied.AuthorityFrame,denied.Reason);
        else entry.Combat.Outbox.PublishDenied(request,NetSession.NetFrame,CustomControlDenialReason.StaleSequence);
    }
    private void BindCustomControl(Scene scene,CustomAuthorityCombatAdapter root,in CustomSourceInput input)
    {
        if (CurrentCustomSourceEntry(input.Owner,scene) is not { Combat:not null,Boundary:not null } entry
            || entry.Combat.ControlMode!=2 || entry.Combat.ModeOwner!=input.Owner || !entry.Boundary.TryGetSerial(input,out ulong serial)
            || !root.TryStateMetadata(scene.Players.Items[input.Owner.Slot],out _,out uint settled,out _)
            || settled==uint.MaxValue || !entry.Combat.Outbox.TryControl(input.Owner,settled+1,serial,out var request)) return;
        var outbox=entry.Combat.Outbox;
        if (outbox.ControlSourceExpired(request,NetSession.NetFrame,NetSession.Clock) || !CustomRoomReady(scene)
            || !root.TryGetSourceObservation(input.Owner,input.Frame,out var sample))
        {
            if (root.SettleUnobserved(request)) outbox.PublishDenied(request,NetSession.NetFrame,CustomControlDenialReason.SourceUnavailable);
            else InvalidateCustomEntry(entry);
            return;
        }
        bool fire=request.Kind==CustomWeaponActionKind.Fire;
        var action=new CustomWeaponAction(request.Bootstrap,request.Manifest,request.Slot,request.Life,request.Weapon,request.Sequence,
            input.Frame,fire ? sample.Input.AckFrame:0,fire ? sample.Input.AckSubFrame:(byte)0,request.Kind,request.Optic,
            fire ? sample.Body+Vector3.UnitY*.4f:Vector3.Zero,fire ? sample.Aim:Vector3.Zero);
        if (!action.IsValid)
        {
            if (root.SettleUnobserved(request)) outbox.PublishDenied(request,NetSession.NetFrame,CustomControlDenialReason.SourceUnavailable);
            else InvalidateCustomEntry(entry);
            return;
        }
        // Only this trusted consumed-input callback can create the exact internal
        // control witness. Admission/capacity remain required before any effects.
        if (!outbox.Bind(request,action) || root.Offer(action)!=CustomQueueResult.Queued) InvalidateCustomEntry(entry);
    }
}
