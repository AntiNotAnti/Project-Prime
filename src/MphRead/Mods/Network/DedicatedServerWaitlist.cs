using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    public bool WaitlistEnabled { get; init; } = true;
    public int WaitlistCapacity { get; init; } = LobbyWaitlist.DefaultCapacity;
    public double WaitlistOfferSeconds { get; init; } = 15;
    public double WaitlistResumeGraceSeconds { get; init; } = 10;
    private LobbyWaitlist? _waitlist;
    private readonly Dictionary<IPEndPoint, QueuePeer> _queuePeers = new();
    private readonly List<IPEndPoint> _queueRemove = new();
    private NetTokenBucket _queueBootstrapBudget;
    private bool _queueAdmitting;
    private sealed class QueuePeer
    {
        internal IPEndPoint Endpoint = null!;
        internal uint ClientId, Revision;
        internal ulong Nonce, QueueId;
        internal double CreatedAt, LastSeen;
        internal bool Disconnected;
        internal LobbyQueueConnection Owner;
        internal LobbyWaitlistEntry? Published;
    }
    private void InitializeWaitlist()
    {
        if (WaitlistEnabled) _waitlist = new LobbyWaitlist(WaitlistCapacity, WaitlistOfferSeconds, WaitlistResumeGraceSeconds);
    }
    private bool QueueAdmissionAllowed => _phase == SessionPhase.Lobby
        || _phase == SessionPhase.InMatch && AllowJoinInProgress;
    private void RefreshWaitlist(double now)
    {
        if (_waitlist == null) return;
        ushort occupied = 0;
        foreach (var peer in _peers) occupied |= (ushort)(1 << peer.SlotIndex);
        foreach (var bot in _bots) occupied |= (ushort)(1 << bot.SlotIndex);
        _waitlist.Update(now, _maxPlayers, occupied, QueueAdmissionAllowed, _matchId, _authorityEpoch);
    }
    private void MaintainWaitlist(double now)
    {
        if (_waitlist == null) return;
        RefreshWaitlist(now);
        _queueRemove.Clear();
        foreach (var peer in _queuePeers.Values)
        {
            if (peer.QueueId == 0)
            {
                // Unproven bootstrap never occupies FIFO or player capacity.
                if (now - peer.CreatedAt >= 5) _queueRemove.Add(peer.Endpoint);
                continue;
            }
            if (!_waitlist.TryGetState(peer.Owner, peer.QueueId, out var state))
            {
                PublishQueueTerminal(peer, LobbyQueueWireState.Expired);
                _queueRemove.Add(peer.Endpoint); continue;
            }
            if (now - peer.LastSeen >= 5 && !peer.Disconnected)
            {
                _waitlist.Disconnect(peer.Owner, peer.QueueId, now); peer.Disconnected = true;
                _waitlist.TryGetState(peer.Owner, peer.QueueId, out state);
            }
            if (peer.Published != state) PublishQueueState(peer, state, now);
        }
        foreach (var endpoint in _queueRemove)
        { _queuePeers.Remove(endpoint); _transport?.RetireConnection(endpoint); }
    }
    private static uint NextQueueRevision(QueuePeer peer)
    {
        // Queue sessions cannot practically reach exhaustion; fail closed instead of wrapping revisions.
        if (peer.Revision == uint.MaxValue) throw new InvalidOperationException("Queue revision exhausted.");
        return ++peer.Revision;
    }
    private void PublishQueueState(QueuePeer peer, LobbyWaitlistEntry state, double now)
    {
        uint revision = NextQueueRevision(peer);
        var packet = new QueueStatePacket(revision, state.QueueId, (ushort)state.Position,
            (ushort)state.QueueLength, (LobbyQueueWireState)state.State);
        packet.Write(_scratch); _transport?.Send(peer.Endpoint, PacketType.QueueState, _scratch.AsSpan(0, QueueStatePacket.Size));
        if (state.Offer is LobbySeatOffer offer)
        {
            uint ticks = (uint)Math.Clamp(Math.Ceiling((offer.ExpiresAt - now) * Render.FrameTiming.SimulationHz), 1, 18000);
            var offered = new QueueSeatOfferPacket(revision, state.QueueId, offer.OfferId,
                (ushort)offer.MatchId, offer.AuthorityEpoch, ticks);
            offered.Write(_scratch); _transport?.Send(peer.Endpoint, PacketType.QueueSeatOffer, _scratch.AsSpan(0, QueueSeatOfferPacket.Size));
        }
        peer.Published = state;
    }
    private void PublishQueueTerminal(QueuePeer peer, LobbyQueueWireState state)
    {
        var packet = new QueueStatePacket(NextQueueRevision(peer), peer.QueueId, 0, (ushort)(_waitlist?.Count ?? 0), state);
        packet.Write(_scratch); _transport?.Send(peer.Endpoint, PacketType.QueueState, _scratch.AsSpan(0, QueueStatePacket.Size));
    }
    private void HandleQueueHello(ReceivedPacket packet, double now)
    {
        if (_waitlist == null || !QueueHelloPacket.TryRead(packet.Payload, out var hello) || hello.ClientNonce == 0
            || Find(packet.Sender) != null) return;
        if (!_queuePeers.TryGetValue(packet.Sender, out var peer))
        {
            if (_waitlist.Count >= WaitlistCapacity || _queuePeers.Count >= WaitlistCapacity + 16
                || _queuePeers.Values.Count(p => p.QueueId == 0) >= 16
                || !_queueBootstrapBudget.Take(now * 1000, 8, 16)) return;
            // A client ID cannot claim an already connected player or queue identity.
            if (_peers.Any(p => p.ClientId == hello.ClientId) || _queuePeers.Values.Any(p => p.ClientId == hello.ClientId)) return;
            peer = new QueuePeer { Endpoint = new IPEndPoint(packet.Sender.Address, packet.Sender.Port),
                ClientId = hello.ClientId, Nonce = hello.ClientNonce, CreatedAt = now, LastSeen = now };
            _queuePeers.Add(peer.Endpoint, peer);
        }
        else if (peer.ClientId != hello.ClientId || peer.Nonce != hello.ClientNonce) return;
        var welcome = new QueueWelcomePacket(NetConfig.ProtocolVersion, peer.ClientId, peer.Nonce);
        welcome.Write(_scratch); _transport?.Send(peer.Endpoint, PacketType.QueueWelcome, _scratch.AsSpan(0, QueueWelcomePacket.Size));
    }
    private bool HandleQueuePeer(ReceivedPacket packet, double now)
    {
        if (_waitlist == null || !_queuePeers.TryGetValue(packet.Sender, out var peer)) return false;
        if (packet.Type == PacketType.Hello) return true; // A queued endpoint cannot bypass its offer via normal Hello.
        if (packet.ConnectionId == 0 && packet.Type == PacketType.Bye)
        {
            // NetTransport alone creates an unwrapped local reliable-failure Bye;
            // remote unsequenced Bye is rejected by its receive boundary.
            if (peer.QueueId != 0) _waitlist.Leave(peer.Owner, peer.QueueId, now);
            RemoveQueuePeer(peer); return true;
        }
        if (packet.ConnectionId == 0 || packet.ConnectionId != _transport?.QueueConnectionId(packet.Sender)) return true;
        var owner = LobbyQueueConnection.FromEstablished(packet.Sender, packet.ConnectionId);
        if (peer.QueueId != 0 && peer.Owner != owner) return true;
        if (packet.Type is PacketType.Ping or PacketType.Pong or PacketType.QueueJoin or PacketType.QueueLeave
            or PacketType.QueueAccept or PacketType.QueueDecline)
        {
            peer.LastSeen = now;
            if (peer.Disconnected && _waitlist.Resume(owner, peer.QueueId, now)) peer.Disconnected = false;
        }
        switch (packet.Type)
        {
            case PacketType.QueueJoin:
                if (!QueueJoinPacket.TryRead(packet.Payload, out var join) || join.ClientNonce != peer.Nonce) break;
                if (peer.QueueId == 0)
                {
                    RefreshWaitlist(now);
                    if (!_waitlist.TryJoin(owner, join.ClientNonce, now, out var entry))
                    { PublishQueueTerminal(peer, LobbyQueueWireState.Rejected); RemoveQueuePeer(peer); break; }
                    peer.Owner = owner; peer.QueueId = entry.QueueId;
                }
                if (_waitlist.TryGetState(owner, peer.QueueId, out var state)) PublishQueueState(peer, state, now);
                break;
            case PacketType.QueueLeave:
                if (QueueLeavePacket.TryRead(packet.Payload, out var leave) && leave.QueueId == peer.QueueId
                    && _waitlist.Leave(owner, leave.QueueId, now))
                { PublishQueueTerminal(peer, LobbyQueueWireState.Left); RemoveQueuePeer(peer); }
                break;
            case PacketType.QueueDecline:
                if (QueueDeclinePacket.TryRead(packet.Payload, out var decline) && decline.QueueId == peer.QueueId
                    && _waitlist.Decline(owner, decline.QueueId, decline.OfferId, decline.MatchId, decline.AuthorityEpoch, now))
                { PublishQueueTerminal(peer, LobbyQueueWireState.Left); RemoveQueuePeer(peer); }
                break;
            case PacketType.QueueAccept:
                if (!QueueAcceptPacket.TryRead(packet.Payload, out var accept) || accept.QueueId != peer.QueueId) break;
                RefreshWaitlist(now);
                if (_waitlist.TryAccept(owner, accept.QueueId, accept.OfferId, accept.MatchId, accept.AuthorityEpoch, now, slot =>
                {
                    // Use ordinary team/lifecycle/player admission, preserving the same socket and connection.
                    byte[] hello = new byte[7]; hello[0] = (byte)PacketType.Hello;
                    hello[1] = NetConfig.ProtocolVersion; hello[2] = (byte)slot;
                    BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(3), peer.ClientId);
                    _queueAdmitting = true;
                    try { HandleHello(new ReceivedPacket(peer.Endpoint, hello, hello.Length, connectionId: packet.ConnectionId), now, slot); }
                    finally { _queueAdmitting = false; }
                    return Find(peer.Endpoint)?.SlotIndex == slot;
                })) _queuePeers.Remove(peer.Endpoint); // transport was promoted by the ordinary Welcome
                break;
            case PacketType.Bye:
                if (peer.QueueId != 0) _waitlist.Disconnect(owner, peer.QueueId, now);
                peer.Disconnected = true;
                break;
            case PacketType.Ping:
                _transport?.Send(peer.Endpoint, PacketType.Pong, ReadOnlySpan<byte>.Empty);
                break;
        }
        return true;
    }
    private void RemoveQueuePeer(QueuePeer peer)
    { _queuePeers.Remove(peer.Endpoint); _transport?.RetireConnection(peer.Endpoint); }
}
