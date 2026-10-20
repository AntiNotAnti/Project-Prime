using System;
using System.Collections.Generic;
using System.Net.Http;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private static readonly HttpClient SocialMembershipHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private SocialLobbyMembershipPump? _socialMembership;
    private long _socialMembershipSequence;
    // Backend membership TTL is 60 s. Ten refreshes/second bound control
    // work without a 60 Hz per-peer allocation stream.
    private double _nextSocialMembershipPoll;
    private readonly List<SocialLobbyMembershipIdentity> _socialDesired = new(8);
    private readonly List<Guid> _socialExpired = new(8);
    private readonly Dictionary<Guid,SocialPeerWitness> _verifiedSocialPeers = new();
    private sealed record SocialPeerWitness(Peer Peer, ushort SlotGeneration, ulong Connection,
        ushort Match, ulong Epoch, SessionPhase Phase, string Ticket);
    private void PumpSocialMembership(double now)
    {
        if (_controlPlaneOnlyForTests || !CareerReportOutbox.Enabled || now < _nextSocialMembershipPoll) return;
        _nextSocialMembershipPoll = now + .1;
        _socialExpired.Clear();
        foreach (var entry in _verifiedSocialPeers)
            if (!CurrentSocialPeer(entry.Value)) _socialExpired.Add(entry.Key);
        foreach (Guid key in _socialExpired) _verifiedSocialPeers.Remove(key);
        _socialExpired.Clear();
        if (_socialMembership == null)
        {
            var endpoint = BackendServiceConfiguration.Capture().Function("social-lobby-membership");
            var relay = new SocialLobbyMembershipRelay(SocialMembershipHttp, endpoint,
                CareerReportOutbox.AuthorizeSocialReport);
            _socialMembership = new(relay.SendAsync, _socialMembershipSequence, (identity, player) =>
            {
                if (identity.Eligible && identity.Witness is SocialPeerWitness witness && CurrentSocialPeer(witness))
                    _verifiedSocialPeers[player] = witness;
            });
        }
        _socialDesired.Clear();
        foreach (Peer peer in _peers)
        {
            if (peer.ClientId == 0 || peer.CareerTicket.Length == 0) continue;
            var connection = _transport?.ConnectionStats(peer.EndPoint);
            if (connection == null || connection.Value.ConnectionId == 0) continue;
            var witness = new SocialPeerWitness(peer, _slotGenerations[peer.SlotIndex],
                connection.Value.ConnectionId, _matchId, _authorityEpoch, _phase, peer.CareerTicket);
            _socialDesired.Add(new(witness, _authorityEpoch, peer.ClientId, peer.CareerTicket,
                SessionPolicy == ServerSessionPolicy.Lobby && _phase == SessionPhase.Lobby && !peer.Spectating));
        }
        _socialMembership.Update(_socialDesired, now);
    }
    private bool CurrentSocialPeer(SocialPeerWitness witness)
        => _peers.Contains(witness.Peer) && witness.Peer.SlotIndex >= 0
        && _slotGenerations[witness.Peer.SlotIndex] == witness.SlotGeneration
        && _transport?.ConnectionStats(witness.Peer.EndPoint)?.ConnectionId == witness.Connection
        && _matchId == witness.Match && _authorityEpoch == witness.Epoch && _phase == witness.Phase
        && witness.Phase == SessionPhase.Lobby && witness.Peer.CareerTicket == witness.Ticket
        && !witness.Peer.Spectating;
}
