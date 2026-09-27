using System;
using MphRead.Mods.Cosmetics;
namespace MphRead.Mods.Network
{
    public sealed partial class DedicatedServer
    {
        private readonly NetCosmetics _cosmetics = new();
        private void HandleCosmetics(ReceivedPacket received)
        {
            var peer = Find(received.Sender);
            if (peer == null || !CosmeticStatePacket.TryRead(received.Payload, out var input)
                || input.Slot != peer.SlotIndex || input.Hunter != peer.Hunter
                || input.Generation != _slotGenerations[peer.SlotIndex]
                || input.Match != _matchId || input.Authority != _authorityEpoch) return;
            var appearance = new CosmeticAppearance((Hunter)input.Hunter,
                CosmeticCatalog.FromWire((Hunter)input.Hunter, input.Skin, input.Armor, input.Death));
            var sanitized = CosmeticStatePacket.Create(peer.SlotIndex, (Hunter)peer.Hunter,
                _slotGenerations[peer.SlotIndex], _matchId, _authorityEpoch, input.Revision, appearance);
            if (!_cosmetics.Accept(sanitized, _matchId, _authorityEpoch, sanitized.Generation)) return;
            NetCosmetics.Live.Accept(sanitized, _matchId, _authorityEpoch, sanitized.Generation);
            ReplayCapture.AcceptedCosmetics();
            BroadcastCosmetics();
        }
        private void BroadcastCosmetics()
        {
            int bytes = _cosmetics.Write(_scratch, _matchId, _authorityEpoch);
            if (bytes == 0) return;
            foreach (var peer in _peers)
                _transport?.Send(peer.EndPoint, PacketType.CosmeticState, _scratch.AsSpan(0, bytes));
        }
    }
}
