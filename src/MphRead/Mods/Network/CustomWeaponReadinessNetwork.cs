using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Configure on the server owner before Run. No lobby/CLI/public mode enables
    // this developer capability, and no combat runtime consumes it in this slice.
    internal bool DeveloperCustomWeaponReadiness { get; set; }
    private sealed record CustomReadyEntry(Peer Peer,CustomReadinessPeer State);
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
                for (int slot=0; slot<8; slot++) { _customReadyEntries[slot]?.State.Invalidate(); _customReadyEntries[slot] = null; }
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
                entry?.State.Invalidate();
                entry = new(peer,new(peer.BootstrapIdentity,(byte)slot,connection,now));
                _customReadyEntries[slot] = entry;
            }
            if (entry.State.Due(now)) _transport.TrySendSemantic(peer.EndPoint,PacketType.CustomWeaponManifest,entry.State.Wire.ManifestBytes);
        }
        for (int slot=0; slot<8; slot++)
            if ((seen & (1 << slot)) == 0)
            { _customReadyEntries[slot]?.State.Invalidate(); _customReadyEntries[slot] = null; }
    }
    private void ReleaseCustomWeaponReadiness()
    {
        if (_customReadyEntries == null) return;
        foreach (var entry in _customReadyEntries) entry?.State.Invalidate();
        _customReadyEntries = null;
    }
    private void RetireCustomWeaponReadiness(int slot)
    {
        if ((uint)slot >= 8 || _customReadyEntries == null) return;
        _customReadyEntries[slot]?.State.Invalidate(); _customReadyEntries[slot] = null;
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
        if (result is CustomReadinessResult.Accepted or CustomReadinessResult.Duplicate)
            _transport.TrySendSemantic(peer.EndPoint,PacketType.CustomWeaponReadyConfirmed,entry.State.Wire.ReadyBytes);
    }
    // Atomic immutable diagnostic capability snapshot. This deliberately does
    // not implement H's ICustomSourceReadiness: no source boundary is proven yet.
    internal bool TryGetCustomWeaponReady(int slot,out CustomPeerReadyEvidence evidence)
    {
        evidence = null!;
        if (!_running || !DeveloperCustomWeaponReadiness || (uint)slot >= 8 || _customReadyEntries?[slot] is not { } entry
            || entry.State.Evidence is not { } ready) return false;
        evidence = ready; return true;
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
