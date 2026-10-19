using System;
using System.Collections.Generic;
using System.Net.Http;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private static readonly HttpClient SocialMembershipHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private SocialLobbyMembershipPump? _socialMembership;
    private long _socialMembershipSequence;
    private readonly Dictionary<Guid,SocialPeerWitness> _verifiedSocialPeers = new();
    private sealed record SocialPeerWitness(Peer Peer, ushort SlotGeneration, ulong Connection,
        ushort Match, ulong Epoch, SessionPhase Phase, string Ticket);
    private void PumpSocialMembership(double now)
    {
        if (_controlPlaneOnlyForTests || !CareerReportOutbox.Enabled) return;
        foreach (var entry in System.Linq.Enumerable.ToArray(_verifiedSocialPeers))
            if (!CurrentSocialPeer(entry.Value)) _verifiedSocialPeers.Remove(entry.Key);
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
        var desired = new List<SocialLobbyMembershipIdentity>(_peers.Count);
        foreach (Peer peer in _peers)
        {
            if (peer.ClientId == 0 || peer.CareerTicket.Length == 0) continue;
            var connection = _transport?.ConnectionStats(peer.EndPoint);
            if (connection == null || connection.Value.ConnectionId == 0) continue;
            var witness = new SocialPeerWitness(peer, _slotGenerations[peer.SlotIndex],
                connection.Value.ConnectionId, _matchId, _authorityEpoch, _phase, peer.CareerTicket);
            desired.Add(new(witness, _authorityEpoch, peer.ClientId, peer.CareerTicket,
                SessionPolicy == ServerSessionPolicy.Lobby && _phase == SessionPhase.Lobby && !peer.Spectating));
        }
        _socialMembership.Update(desired, now);
    }
    private bool CurrentSocialPeer(SocialPeerWitness witness)
        => _peers.Contains(witness.Peer) && witness.Peer.SlotIndex >= 0
        && _slotGenerations[witness.Peer.SlotIndex] == witness.SlotGeneration
        && _transport?.ConnectionStats(witness.Peer.EndPoint)?.ConnectionId == witness.Connection
        && _matchId == witness.Match && _authorityEpoch == witness.Epoch && _phase == witness.Phase
        && witness.Phase == SessionPhase.Lobby && witness.Peer.CareerTicket == witness.Ticket
        && !witness.Peer.Spectating;
}
