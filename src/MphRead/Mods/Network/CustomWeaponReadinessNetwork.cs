using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Fixture overrides remain independent of the frozen public match option.
    internal bool DeveloperCustomWeaponReadiness { get; set; }
    private bool PublicCustomWeaponMatch => _phase != SessionPhase.Lobby && _frozenMatch.RiftlineM7Available;
    private bool CustomWeaponReadinessEnabled => DeveloperCustomWeaponReadiness || PublicCustomWeaponMatch;
    // Separate explicit developer choice: capability alone never activates combat.
    internal bool DeveloperCustomWeaponCombat { get; set; }
    private bool CustomWeaponCombatEnabled => DeveloperCustomWeaponCombat || PublicCustomWeaponMatch;
    private sealed record CustomReadyEntry(Peer Peer,CustomReadinessPeer State,CustomPeerSourceBoundary? Boundary,CustomActionPeer? Combat,CustomReplicaMailbox? Replica);
    private CustomReadyEntry?[]? _customReadyEntries;
    private CustomReplicaAudience? _customAudience;
    private MatchStartIdentity _customAudienceStart;
    private bool CustomNativeRecipientReady(Peer peer)
        => (uint)peer.SlotIndex < 8 && peer.SceneLoaded && peer.MatchReady && peer.AdmissionReady
            && peer.BootstrapLength != 0 && peer.BootstrapIdentity.Start == CurrentStartIdentity
            && peer.BootstrapIdentity.SlotGeneration == _slotGenerations[peer.SlotIndex];
    private bool CustomNativeReady(Peer peer)=>!peer.Spectating&&CustomNativeRecipientReady(peer);
    private void PumpCustomWeaponReadiness(double now)
    {
        if (!CustomWeaponReadinessEnabled || _transport == null)
        {
            ReleaseCustomWeaponReadiness();
            return;
        }
        _customReadyEntries ??= new CustomReadyEntry?[8];
        byte seen = 0;
        for (int peerIndex = _peers.Count - 1; peerIndex >= 0; peerIndex--)
        {
            var peer = _peers[peerIndex];
            if (!CustomNativeRecipientReady(peer)) continue;
            ulong connection = _transport.ConnectionStats(peer.EndPoint)?.ConnectionId ?? 0;
            if (connection == 0) continue;
            int slot = peer.SlotIndex; seen |= (byte)(1 << slot);
            var entry = _customReadyEntries[slot];
            bool source=CustomWeaponCombatEnabled&&!peer.Spectating;
            if (entry?.Peer != peer || entry.State.ConnectionId != connection || entry.State.Wire.Bootstrap != peer.BootstrapIdentity
                || (entry.Boundary!=null)!=source || (entry.Replica!=null)!=CustomWeaponCombatEnabled)
            {
                InvalidateCustomEntry(entry);
                var state = new CustomReadinessPeer(peer.BootstrapIdentity,(byte)slot,connection,now);
                entry = new(peer,state,source ? new(state) : null,source ? new(state.Wire) : null,
                    CustomWeaponCombatEnabled ? new(state.Wire,connection,NetSession.Clock) : null);
                _customReadyEntries[slot] = entry;
            }
            if (entry.State.Due(now)) _transport.TrySendSemantic(peer.EndPoint,PacketType.CustomWeaponManifest,entry.State.Wire.ManifestBytes);
            if (PublicCustomWeaponMatch && entry.State.Invalidated)
            {
                SendRefusal(peer.EndPoint, RefusedPacket.ReasonCustomWeaponReadiness);
                Remove(peer, "Riftline M7 readiness failed or timed out");
                // Removing the last participant may unwind this match.
                if (!CustomWeaponReadinessEnabled || _customReadyEntries == null) return;
            }
        }
        for (int slot=0; slot<8; slot++)
            if ((seen & (1 << slot)) == 0)
            { InvalidateCustomEntry(_customReadyEntries[slot]); _customReadyEntries[slot] = null; }
    }
    private void ReleaseCustomWeaponReadiness()
    {
        if (_customReadyEntries != null)foreach (var entry in _customReadyEntries) InvalidateCustomEntry(entry);
        _customReadyEntries = null;
        _customAudience?.Close();_customAudience=null;_customAudienceStart=default;
        ReleaseCustomPickups();
        Array.Clear(_customReplicaStateOwners);Array.Clear(_customReplicaStateRevisions);Array.Clear(_customReplicaStateDue);
        Array.Clear(_customReplicaSlots);Array.Clear(_customReplicaFlights);
    }
    private void RetireCustomWeaponReadiness(int slot)
    {
        if ((uint)slot >= 8 || _customReadyEntries == null) return;
        InvalidateCustomEntry(_customReadyEntries[slot]); _customReadyEntries[slot] = null;
    }
    private void HandleCustomWeaponReady(ReceivedPacket packet,double now)
    {
        if (!CustomWeaponReadinessEnabled || _transport == null || _customReadyEntries == null) return;
        var peer = Find(packet.Sender);
        if (peer == null || !CustomNativeRecipientReady(peer) || _customReadyEntries[peer.SlotIndex] is not { } entry
            || entry.Peer != peer || entry.State.Wire.Bootstrap != peer.BootstrapIdentity
            || packet.ConnectionId == 0 || packet.ConnectionId != entry.State.ConnectionId
            || _transport.ConnectionStats(peer.EndPoint)?.ConnectionId != packet.ConnectionId) return;
        // The control-plane-only UDP fixture supplies a synthetic canonical
        // baseline, not an engine clock. Production NetFrame is at/after it.
        uint world = NetSession.NetFrame;
        if (unchecked((int)(world-peer.BootstrapIdentity.AuthorityFrame)) < 0) world = peer.BootstrapIdentity.AuthorityFrame;
        var result = entry.State.Receive(packet.Payload,packet.ConnectionId,world,now);
        if (result == CustomReadinessResult.Accepted) entry.Boundary?.ConfirmReadiness();
        if (result is CustomReadinessResult.Accepted or CustomReadinessResult.Duplicate)
            _transport.TrySendSemantic(peer.EndPoint,PacketType.CustomWeaponReadyConfirmed,entry.State.Wire.ReadyBytes);
    }
    // Atomic immutable diagnostic capability snapshot. This deliberately does
    // not implement ICustomSourceReadiness. The live bridge below independently
    // rechecks peer identity and requires an engine-consumed source boundary.
    internal bool TryGetCustomWeaponReady(int slot,out CustomPeerReadyEvidence evidence)
    {
        evidence = null!;
        if (!_running || !CustomWeaponReadinessEnabled || (uint)slot >= 8 || _customReadyEntries?[slot] is not { } entry
            || entry.State.Evidence is not { } ready) return false;
        evidence = ready; return true;
    }

    private CustomReadyEntry? CurrentCustomSourceEntry(WeaponOwnerContext owner,Scene scene)
    {
        if (!CustomWeaponCombatEnabled || !CustomWeaponReadinessEnabled || !_running || !Simulating
            || _sim?.EvidenceScene != scene || !owner.IsValid || owner.Slot >= 8
            || owner.MatchId != _matchId || owner.AuthorityEpoch != _authorityEpoch
            || !NetPlayerLifecycle.Matches(owner.Slot,owner.Generation,owner.Life)
            || _customReadyEntries?[owner.Slot] is not { Boundary: not null } entry
            || !CustomNativeReady(entry.Peer) || Find(entry.Peer.EndPoint) != entry.Peer
            || entry.State.Wire.Bootstrap != entry.Peer.BootstrapIdentity
            || _transport?.ConnectionStats(entry.Peer.EndPoint)?.ConnectionId != entry.State.ConnectionId) return null;
        return entry;
    }
    private CustomReadyEntry? CurrentCustomRecipientEntry(Peer peer,Scene scene)
    {
        if(!CustomWeaponCombatEnabled||!CustomWeaponReadinessEnabled||!_running||!Simulating
            ||_sim?.EvidenceScene!=scene||scene.CustomCombat?.IsEligible!=true||!CustomNativeRecipientReady(peer)
            ||_customReadyEntries?[peer.SlotIndex] is not {Replica:not null} entry||entry.Peer!=peer
            ||Find(peer.EndPoint)!=peer||entry.State.Evidence is not {} evidence
            ||evidence.Manifest!=scene.CustomCombat.Manifest.Digest||entry.State.Wire.Bootstrap!=peer.BootstrapIdentity
            ||_transport?.ConnectionStats(peer.EndPoint)?.ConnectionId!=entry.State.ConnectionId)return null;
        return entry;
    }
    private sealed class LiveCustomSourceReadiness(DedicatedServer server,Scene scene) : ICustomSourceBoundaryReadiness
    {
        public bool IsConfirmed(WeaponOwnerContext owner)
            =>server.CurrentCustomSourceEntry(owner,scene)?.Boundary?.IsConfirmed(owner)==true;
        public bool TryGet(WeaponOwnerContext owner,out CustomSourceReadyContext context)
        {
            context = default;
            return server.CurrentCustomSourceEntry(owner,scene)?.Boundary?.TryGet(owner,out context) == true;
        }
        public bool TryAcceptedInput(in CustomSourceInput candidate,out CustomSourceInput original)
        {
            original=default;
            return server.CurrentCustomSourceEntry(candidate.Owner,scene)?.Boundary?.TryAcceptedInput(candidate,out original)==true;
        }
        public bool TryOpen(in CustomSourceInput input,out CustomSourceReadyContext context)
        {
            context = default;
            return server.CurrentCustomSourceEntry(input.Owner,scene)?.Boundary?.TryOpen(input,out context) == true;
        }
    }
    private void NoteCustomCurrentInput(ReceivedPacket packet,Peer peer,in IntentPacket intent,CustomResourceKnowledge knowledge)
    {
        if (!CustomWeaponCombatEnabled || _sim?.EvidenceScene is not { } scene ||peer.Spectating
            ||intent.MatchId==0||intent.AuthorityEpoch==0||intent.SlotGeneration==0||intent.LifeId==0) return;
        var owner = new WeaponOwnerContext(intent.MatchId,intent.AuthorityEpoch,(byte)peer.SlotIndex,intent.SlotGeneration,intent.LifeId);
        if (CurrentCustomSourceEntry(owner,scene) is not { } entry || entry.Peer != peer
            || packet.ConnectionId == 0 || packet.ConnectionId != entry.State.ConnectionId) return;
        var input=new CustomSourceInput(owner,intent.Frame,intent.AckFrame,intent.AckSubFrame,
            intent.Position,intent.Aim,(uint)intent.Buttons,knowledge,NetSession.NetFrame);
        if(entry.Boundary!.NoteAcceptedInput(input))scene.CustomCombat?.CapturePickupIngress(input);
    }
    private void ConfigureCustomSourceCombat(Scene scene)
    {
        if (!CustomWeaponCombatEnabled) return;
        if (!CustomWeaponReadinessEnabled)
            throw new InvalidOperationException("Developer custom combat requires its separate capability handshake.");
        var root=scene.EnableDeveloperEngineCustomCombat(true,new LiveCustomSourceReadiness(this,scene),new LiveCustomCombatDelivery(this,scene),
            pickupOnly:PublicCustomWeaponMatch);
        if(PublicCustomWeaponMatch)root.ConfigurePickups(CurrentStartIdentity,CurrentDefinition.MapIdentity.Content(CurrentDefinition.RoomKey));
        _customAudience?.Close();_customAudience=new(root.Definition.Mechanics.LifetimeTicks);_customAudienceStart=CurrentStartIdentity;
    }
}

public static partial class NetSession
{
    internal static bool DeveloperCustomWeaponReadiness { get; set; }
    internal static bool PublicCustomWeaponAvailability => Role == NetRole.Client && !_playback
        && ServerSession is { Phase: not SessionPhase.Lobby, Match.RiftlineM7Available: true } session
        && ServerMatch is { } match && match.MatchId == session.MatchId && match.AuthorityEpoch == session.AuthorityEpoch
        && ((MatchModifierFlags)match.RuleBits & MatchModifierFlags.RiftlineM7Available) != 0;
    private static bool CustomWeaponReadinessEnabled => DeveloperCustomWeaponReadiness || PublicCustomWeaponAvailability;
    private static CustomReadinessClient? _customReadiness;
    private static ulong _customReadinessConnection;
    internal static bool CustomWeaponReadinessConfirmed => CustomWeaponReadinessEnabled
        && Role == NetRole.Client && !_playback && WorldAdmissionConfirmed
        && _customReadiness is { Confirmed: true,Invalidated: false } state
        && state.Wire.Slot == LocalSlot
        && ConfirmedWorldBootstrap == state.Wire.Bootstrap
        && _hostEndPoint != null && _transport?.ConnectionStats(_hostEndPoint)?.ConnectionId == _customReadinessConnection;
    private static CustomReadinessClient? CurrentCustomReadiness(double now)
    {
        if (!CustomWeaponReadinessEnabled || Role != NetRole.Client || _playback || IsAuthority
            || LocalSlot is < 0 or >= 8 || !WorldAdmissionConfirmed || ConfirmedWorldBootstrap is not { } bootstrap
            || bootstrap.SlotGeneration != NetPlayerLifecycle.Generation(LocalSlot))
        { _customReadiness = null; ResetCustomReplica(); return null; }
        ulong connection = _hostEndPoint == null ? 0 : _transport?.ConnectionStats(_hostEndPoint)?.ConnectionId ?? 0;
        if (connection == 0) { _customReadiness = null; ResetCustomReplica(); return null; }
        if (_customReadiness?.Wire.Bootstrap != bootstrap || _customReadiness.Wire.Slot != LocalSlot
            || _customReadinessConnection != connection)
        { ResetCustomReplica(); _customReadiness = new(bootstrap,(byte)LocalSlot,now); _customReadinessConnection = connection; }
        return _customReadiness;
    }
    private static void HandleCustomReadiness(ReceivedPacket packet,double now)
    {
        // Handle already fences the authenticated host endpoint and connection.
        CurrentCustomReadiness(now)?.Receive(packet.Type,packet.Payload,now);
    }
    private static void PumpCustomReadiness(double now)
    {
        var state = CurrentCustomReadiness(now);
        if (state?.Due(now) == true && _transport != null && _hostEndPoint != null)
            _transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponReady,state.Wire.ReadyBytes);
    }
}
