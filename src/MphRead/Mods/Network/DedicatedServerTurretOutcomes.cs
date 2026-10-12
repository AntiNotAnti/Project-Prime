using System;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private NetRetainedDelivery _turretDelivery = new(2048);
    private void ResetTurretDelivery()
    {
        _turretDelivery = new(2048);
        foreach (var peer in _peers) peer.TurretCursor = null;
        NetSession.TurretDeliveryPending = () =>
        {
            int pending = 0;
            foreach (var peer in _peers)
                if (peer.MatchReady && peer.TurretCursor != null) pending += _turretDelivery.Pending(peer.TurretCursor);
            return pending;
        };
        NetSession.TurretOutcomeSink = payload => _turretDelivery.Append(PacketType.TurretFireOutcome, payload);
    }
    private void PumpTurretDelivery()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (!peer.MatchReady || peer.TurretCursor == null) continue;
            if (!_turretDelivery.PumpBatched(peer.TurretCursor, (type, payload) =>
                _transport?.TrySendSemantic(peer.EndPoint, type, payload) == true))
            {
                _transport?.Send(peer.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                Remove(peer, "turret outcome history overrun; fresh world admission required");
            }
        }
    }
}
