using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Configure on the server owner before Run. No lobby/CLI/public mode enables
    // this developer capability. Combat requires a separate explicit developer flag.
    internal bool DeveloperCustomWeaponReadiness { get; set; }
    // Separate explicit developer choice: capability alone never activates combat.
    internal bool DeveloperCustomWeaponCombat { get; set; }
    private sealed record CustomReadyEntry(Peer Peer,CustomReadinessPeer State,CustomPeerSourceBoundary? Boundary,CustomActionPeer? Combat);
    private CustomReadyEntry?[]? _customReadyEntries;
    private bool CustomNativeReady(Peer peer)
        => (uint)peer.SlotIndex < 8 && peer.SceneLoaded && peer.MatchReady && peer.AdmissionReady
            && !peer.Spectating && peer.BootstrapLength != 0 && peer.BootstrapIdentity.Start == CurrentStartIdentity
            && peer.BootstrapIdentity.SlotGeneration == _slotGenerations[peer.SlotIndex];
    private void PumpCustomWeaponReadiness(double now)
    {
        if (!DeveloperCustomWeaponReadiness || _transport == null)
        {
            if (_customReadyEntries != null)
                for (int slot=0; slot<8; slot++) { InvalidateCustomEntry(_customReadyEntries[slot]); _customReadyEntries[slot] = null; }
            return;
        }
        _customReadyEntries ??= new CustomReadyEntry?[8];
        byte seen = 0;
        foreach (var peer in _peers)
        {
            if (!CustomNativeReady(peer)) continue;
            ulong connection = _transport.ConnectionStats(peer.EndPoint)?.ConnectionId ?? 0;
            if (connection == 0) continue;
            int slot = peer.SlotIndex; seen |= (byte)(1 << slot);
            var entry = _customReadyEntries[slot];
            if (entry?.Peer != peer || entry.State.ConnectionId != connection || entry.State.Wire.Bootstrap != peer.BootstrapIdentity)
            {
                InvalidateCustomEntry(entry);
                var state = new CustomReadinessPeer(peer.BootstrapIdentity,(byte)slot,connection,now);
                entry = new(peer,state,DeveloperCustomWeaponCombat ? new(state) : null,
                    DeveloperCustomWeaponCombat ? new(state.Wire) : null);
                _customReadyEntries[slot] = entry;
            }
            if (entry.State.Due(now)) _transport.TrySendSemantic(peer.EndPoint,PacketType.CustomWeaponManifest,entry.State.Wire.ManifestBytes);
        }
        for (int slot=0; slot<8; slot++)
            if ((seen & (1 << slot)) == 0)
            { InvalidateCustomEntry(_customReadyEntries[slot]); _customReadyEntries[slot] = null; }
    }
    private void ReleaseCustomWeaponReadiness()
    {
        if (_customReadyEntries == null) return;
        foreach (var entry in _customReadyEntries) InvalidateCustomEntry(entry);
        _customReadyEntries = null;
    }
    private void RetireCustomWeaponReadiness(int slot)
    {
        if ((uint)slot >= 8 || _customReadyEntries == null) return;
        InvalidateCustomEntry(_customReadyEntries[slot]); _customReadyEntries[slot] = null;
    }
    private void HandleCustomWeaponReady(ReceivedPacket packet,double now)
    {
        if (!DeveloperCustomWeaponReadiness || _transport == null || _customReadyEntries == null) return;
        var peer = Find(packet.Sender);
        if (peer == null || !CustomNativeReady(peer) || _customReadyEntries[peer.SlotIndex] is not { } entry
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
        if (!_running || !DeveloperCustomWeaponReadiness || (uint)slot >= 8 || _customReadyEntries?[slot] is not { } entry
            || entry.State.Evidence is not { } ready) return false;
        evidence = ready; return true;
    }

    private CustomReadyEntry? CurrentCustomSourceEntry(WeaponOwnerContext owner,Scene scene)
    {
        if (!DeveloperCustomWeaponCombat || !DeveloperCustomWeaponReadiness || !_running || !Simulating
            || _sim?.EvidenceScene != scene || !owner.IsValid || owner.Slot >= 8
            || owner.MatchId != _matchId || owner.AuthorityEpoch != _authorityEpoch
            || !NetPlayerLifecycle.Matches(owner.Slot,owner.Generation,owner.Life)
            || _customReadyEntries?[owner.Slot] is not { Boundary: not null } entry
            || !CustomNativeReady(entry.Peer) || Find(entry.Peer.EndPoint) != entry.Peer
            || entry.State.Wire.Bootstrap != entry.Peer.BootstrapIdentity
            || _transport?.ConnectionStats(entry.Peer.EndPoint)?.ConnectionId != entry.State.ConnectionId) return null;
        return entry;
    }
    private sealed class LiveCustomSourceReadiness(DedicatedServer server,Scene scene) : ICustomSourceBoundaryReadiness
    {
        public bool TryGet(WeaponOwnerContext owner,out CustomSourceReadyContext context)
        {
            context = default;
            return server.CurrentCustomSourceEntry(owner,scene)?.Boundary?.TryGet(owner,out context) == true;
        }
        public bool TryOpen(in CustomSourceInput input,out CustomSourceReadyContext context)
        {
            context = default;
            return server.CurrentCustomSourceEntry(input.Owner,scene)?.Boundary?.TryOpen(input,out context) == true;
        }
    }
    private void NoteCustomCurrentInput(ReceivedPacket packet,Peer peer,in IntentPacket intent)
    {
        if (!DeveloperCustomWeaponCombat || _sim?.EvidenceScene is not { } scene) return;
        var owner = new WeaponOwnerContext(intent.MatchId,intent.AuthorityEpoch,(byte)peer.SlotIndex,intent.SlotGeneration,intent.LifeId);
        if (CurrentCustomSourceEntry(owner,scene) is not { } entry || entry.Peer != peer
            || packet.ConnectionId == 0 || packet.ConnectionId != entry.State.ConnectionId) return;
        entry.Boundary!.NoteAcceptedInput(new(owner,intent.Frame,intent.AckFrame,intent.AckSubFrame,
            intent.Position,intent.Aim,(uint)intent.Buttons));
    }
    private void ConfigureCustomSourceCombat(Scene scene)
    {
        if (!DeveloperCustomWeaponCombat) return;
        if (!DeveloperCustomWeaponReadiness)
            throw new InvalidOperationException("Developer custom combat requires its separate capability handshake.");
        scene.EnableDeveloperEngineCustomCombat(true,new LiveCustomSourceReadiness(this,scene),new LiveCustomCombatDelivery(this,scene));
    }
}

public static partial class NetSession
{
    internal static bool DeveloperCustomWeaponReadiness { get; set; }
    private static CustomReadinessClient? _customReadiness;
    private static ulong _customReadinessConnection;
    internal static bool CustomWeaponReadinessConfirmed => DeveloperCustomWeaponReadiness
        && Role == NetRole.Client && !_playback && WorldAdmissionConfirmed
        && _customReadiness is { Confirmed: true,Invalidated: false } state
        && state.Wire.Slot == LocalSlot
        && ConfirmedWorldBootstrap == state.Wire.Bootstrap
        && _hostEndPoint != null && _transport?.ConnectionStats(_hostEndPoint)?.ConnectionId == _customReadinessConnection;
    private static CustomReadinessClient? CurrentCustomReadiness(double now)
    {
        if (!DeveloperCustomWeaponReadiness || Role != NetRole.Client || _playback || IsAuthority
            || LocalSlot is < 0 or >= 8 || !WorldAdmissionConfirmed || ConfirmedWorldBootstrap is not { } bootstrap
            || bootstrap.SlotGeneration != NetPlayerLifecycle.Generation(LocalSlot))
        { _customReadiness = null; return null; }
        ulong connection = _hostEndPoint == null ? 0 : _transport?.ConnectionStats(_hostEndPoint)?.ConnectionId ?? 0;
        if (connection == 0) { _customReadiness = null; return null; }
        if (_customReadiness?.Wire.Bootstrap != bootstrap || _customReadiness.Wire.Slot != LocalSlot
            || _customReadinessConnection != connection)
        { _customReadiness = new(bootstrap,(byte)LocalSlot,now); _customReadinessConnection = connection; }
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
