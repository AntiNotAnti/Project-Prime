using System;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private void ConfigurePickupDelivery()
    {
        NetSession.PickupDeliveryPending = () =>
        {
            int count = 0;
            foreach (var peer in _peers)
                if (peer.MatchReady && peer.AdmissionReady && peer.Pickups != null)
                    count += NetPickupNetwork.PeerPending(peer.Pickups);
            return count;
        };
    }
    private void HandlePickupClaim(ReceivedPacket packet, double now)
    {
        var peer = Find(packet.Sender);
        if (_phase != SessionPhase.InMatch || peer == null || !peer.MatchReady || !peer.AdmissionReady
            || peer.Spectating || peer.SlotIndex < 0 || !Simulating
            || !PickupClaim.TryRead(packet.Payload, out var claim)) return;
        peer.LastSeen = now;
        var timing = _transport?.ConnectionStats(peer.EndPoint);
        NetPickupNetwork.Receive(peer.SlotIndex, claim, timing?.RttMilliseconds ?? 100,
            timing?.RttJitterMilliseconds ?? 0);
    }
    private void PumpPickupDelivery()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (!peer.MatchReady || !peer.AdmissionReady) continue;
            if (peer.Pickups == null || !NetPickupNetwork.Current(peer.Pickups)) peer.Pickups = NetPickupNetwork.Join();
            if (!NetPickupNetwork.Pump(peer.Pickups, peer.SlotIndex, (type, payload) =>
                _transport?.TrySendSemantic(peer.EndPoint, type, payload) == true))
            {
                _transport?.Send(peer.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                Remove(peer, "pickup delivery capacity/deadline exceeded; fresh admission required");
            }
        }
    }
}
