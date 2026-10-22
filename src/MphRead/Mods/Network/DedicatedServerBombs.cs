using System;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private NetRetainedDelivery _bombDelivery = new(2048);
    private void ResetBombDelivery()
    {
        _bombDelivery = new(2048);
        foreach (var peer in _peers) peer.BombCursor = null;
        NetSession.BombEventSink = payload => _bombDelivery.Append(PacketType.BombEvent, payload);
    }
    private void HandleBombIntent(ReceivedPacket packet, double now)
    {
        var peer = Find(packet.Sender);
        if (_phase != SessionPhase.InMatch || peer == null || !peer.MatchReady || !peer.AdmissionReady
            || peer.Spectating || !Simulating || !BombIntent.TryRead(packet.Payload, out var request)
            || request.Key.MatchId != _matchId || request.Key.AuthorityEpoch != _authorityEpoch
            || request.Key.Generation != _slotGenerations[peer.SlotIndex]) return;
        peer.LastSeen = now;
        NetBombs.ReceiveRequest(peer.SlotIndex, request);
    }
    private void PumpBombDelivery()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (!peer.MatchReady || peer.BombCursor == null) continue;
            if (!_bombDelivery.PumpBatched(peer.BombCursor, (type, payload) =>
                _transport?.TrySendSemantic(peer.EndPoint, type, payload) == true))
            {
                _transport?.Send(peer.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                Remove(peer, "bomb history overrun; fresh world admission required");
            }
        }
    }
    private void SendBombState(uint frame)
    {
        var pages = NetBombs.CaptureSnapshot(frame);
        foreach (var page in pages)
        {
            ReplayCapture.AcceptBombState(page);
            foreach (var peer in _peers)
                if (peer.MatchReady) SendLane(peer, PacketType.BombState, page);
        }
    }
}
