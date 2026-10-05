using System;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Shot resolution is durable replay evidence, not gameplay control. Keep a
    // larger retained window than the semantic-announcement lane, but never
    // disconnect a player because their recorder cannot consume it quickly
    // enough: on overrun that peer resumes at the current tail.
    private readonly NetRetainedDelivery _replayShotDelivery = new(8192);
    private ushort _replayShotMatch;
    private ulong _replayShotEpoch;
    private long _replayShotDeliveryDrops;
    internal long ReplayShotDeliveryDrops => _replayShotDeliveryDrops;

    private void AppendReplayShotDelivery(ReadOnlySpan<byte> payload)
    {
        if (!ReplayShotFactPacket.TryRead(payload, out var fact)) return;
        if (fact.MatchId != _matchId || fact.AuthorityEpoch != _authorityEpoch) return;

        if (_replayShotMatch != fact.MatchId || _replayShotEpoch != fact.AuthorityEpoch)
        {
            _replayShotMatch = fact.MatchId;
            _replayShotEpoch = fact.AuthorityEpoch;
            // Existing participants start the new match at its first shot.
            // Late joiners still intentionally start at the current tail.
            foreach (var peer in _peers)
                peer.ReplayShotCursor = peer.AdmissionReady && peer.MatchReady
                    ? _replayShotDelivery.Join() : null;
        }
        _replayShotDelivery.Append(PacketType.ReplayShotFact, payload);
    }

    private void PumpReplayShotDelivery()
    {
        for (int i = 0; i < _peers.Count; i++)
        {
            var peer = _peers[i];
            if (!peer.AdmissionReady || !peer.MatchReady)
            {
                peer.ReplayShotCursor = null;
                continue;
            }
            if (_replayShotMatch == 0 || _replayShotMatch != _matchId
                || _replayShotEpoch != _authorityEpoch)
            {
                peer.ReplayShotCursor = null;
                continue;
            }

            peer.ReplayShotCursor ??= _replayShotDelivery.Join();
            if (!_replayShotDelivery.Pump(peer.ReplayShotCursor,
                fact => _transport?.TrySendSemantic(peer.EndPoint,
                    fact.Type, fact.Payload) == true, budget: 16))
            {
                // Recorder evidence is expendable under pathological backpressure.
                // Gameplay, semantic match events and the connection itself are not.
                _replayShotDeliveryDrops++;
                peer.ReplayShotCursor = _replayShotDelivery.Join();
            }
        }
    }
}
