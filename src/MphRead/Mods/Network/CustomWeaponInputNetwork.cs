using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private void HandleCustomWeaponInput(ReceivedPacket packet,double now)
    {
        if (_phase is SessionPhase.Lobby or SessionPhase.Starting || !Simulating
            || Find(packet.Sender) is not { MatchReady:true } peer
            || !CustomInputCarrier.TryRead(packet.Payload,out var request,out var intent)
            || request.Slot!=peer.SlotIndex) return;
        var scene=_sim?.EvidenceScene;
        var entry=scene==null ? null:CurrentCustomActionEntry(peer.SlotIndex,scene);
        ulong fence=0;
        if (entry is { Combat:not null,Boundary:not null })
        {
            // Reject a contradictory custom envelope before native mutation.
            // An absent/retired custom lane still permits ordinary native input,
            // so loss of custom readiness cannot freeze movement.
            if (request.Bootstrap!=entry.State.Wire.Bootstrap || request.Manifest!=entry.State.Wire.Manifest.Digest
                || request.Weapon.Value!=10 || packet.ConnectionId==0 || packet.ConnectionId!=entry.State.ConnectionId) return;
            if (!entry.Boundary.TryGetFence(request.Owner,out fence)) entry=null;
        }
        // The exact accepted current native carrier must exist before admitting
        // a previously unseen nonce. An old/duplicate input can recover native
        // events but can never retrofit a custom trigger into frozen history.
        bool current=HandleDecodedIntent(packet,now,peer,intent);
        if (!current || scene==null || entry is not { Combat:not null } || !CustomCanReceiveAction(scene,entry.Combat)
            || !entry.Combat.AcceptsMode(request.Owner,2)
            || !NetPlayerLifecycle.Matches(request.Slot,request.Owner.Generation,request.Life)) return;
        // Native input can repeat at60Hz. An exact retained nonce is not another
        // semantic offer and must not consume the action/ACK quota each frame.
        if (entry.Combat.Outbox.ContainsControl(request) || !entry.Combat.Admit(now)) return;
        // Capture the fence BEFORE native acceptance; using the new serial here
        // would force an extra input/world and lose atomic source binding.
        AdmitCustomControl(scene,entry,request,fence,now);
    }
}
