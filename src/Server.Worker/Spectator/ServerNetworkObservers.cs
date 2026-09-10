using System;
using System.Buffers.Binary;
using System.Net;
namespace MphRead.Mods.Network;
public sealed partial class ServerNetwork
{
    private readonly ServerPeer?[] _observers = new ServerPeer?[16];
    private readonly ServerPeer?[] _connections = new ServerPeer?[24];
    private readonly ObserverTimeline _observerTimeline = new();
    public ObserverOptions ObserverConfiguration { get; }
    public int ObserverCount { get; private set; }
    internal Action<ObserverFrame>? ObserverFrameCaptured { get; set; }
    public bool ObserverFramesRequired => ObserverConfiguration.MaxSpectators > 0 || ObserverFrameCaptured != null;
    public ReadOnlySpan<ServerPeer?> ObserverPeers => _observers.AsSpan(0, ObserverConfiguration.MaxSpectators);
    public ReadOnlySpan<ServerPeer?> AllConnections => _connections;
    private bool AdmitObserver(IPEndPoint endpoint, in JoinPacket join, TicketIdentity? identity)
    {
        ServerPeer? replacing = null;
        int replacementSlot = -1;
        foreach (ServerPeer? existing in _connections)
        {
            if (existing == null) continue;
            if (existing.Nonce == join.Nonce)
                return existing.IsObserver && existing.Connection.Endpoint.Equals(endpoint)
                    && SameAuthenticatedIdentity(existing, identity) && existing.TicketId == (identity?.TicketId ?? Guid.Empty);
            if (identity?.ReservedSeat is byte reserved && existing.ReservedSeat == reserved)
            {
                // A routed observer reconnect may replace only its own stale
                // observer connection. The fresh nonce/ticket and previous
                // connection ID prevent a different session from evicting it;
                // all checks happen before the old connection is detached.
                if (existing.IsObserver && identity.Value.WorkerAdmission
                    && SameAuthenticatedIdentity(existing, identity)
                    && identity.Value.TicketId != Guid.Empty && identity.Value.TicketId != existing.TicketId
                    && join.PreviousConnectionId != 0 && join.PreviousConnectionId == existing.Connection.Id
                    && existing.ConnectionIndex >= 8
                    && existing.ConnectionIndex - 8 < ObserverConfiguration.MaxSpectators)
                {
                    replacing = existing;
                    replacementSlot = existing.ConnectionIndex - 8;
                    continue;
                }
                return false;
            }
            if (existing.Connection.Endpoint.Equals(endpoint)) return false;
        }
        int free = replacementSlot >= 0
            ? replacementSlot
            : Array.FindIndex(_observers, 0, ObserverConfiguration.MaxSpectators, peer => peer == null);
        if (free < 0) { Refuse(endpoint, join.Nonce, "Observer connections are full or disabled."); return true; }
        uint delay = identity?.TrustedObserver == true ? 0 : (uint)ObserverConfiguration.DelaySeconds * 60;
        var baseline = _observerTimeline.Baseline(Tick, delay);
        if (baseline == null) { Refuse(endpoint, join.Nonce, "Observer history is warming up. Try again later."); return true; }
        ulong id = AllocateConnectionIdentity();
        var connection = new NetConnection(id, endpoint, baseline.Value.MatchId, _now,
            ReliableAdaptiveRtoEnabled);
        var peer = new ServerPeer(connection, join, byte.MaxValue, _now)
        {
            ConnectionIndex = (byte)(8 + free), TeamIndex = byte.MaxValue,
            PlayerId = identity?.PlayerId, GuestSessionId = identity?.GuestSessionId, ReservedSeat = identity?.ReservedSeat, TicketId = identity?.TicketId ?? Guid.Empty,
            TrustedObserver = identity?.TrustedObserver == true,
            ObserverDelayTicks = delay, ObserverCursor = baseline, ObserverNeedsBaseline = true,
            Timing = new ServerNetworkTimingController(AdaptiveTimingEnabled)
        };
        var accepted = new JoinAcceptedPacket(join.Nonce, byte.MaxValue, baseline.Value.MatchId, baseline.Value.Tick, 60, baseline.Value.Rules);
        Span<byte> payload = stackalloc byte[JoinAcceptedPacket.Size]; accepted.Write(payload);
        // The new connection is still detached, so all reliable admission
        // events must be accepted before the old observer can be removed.
        if (!connection.Reliable.TryEnqueue(ReliableEventType.Welcome, payload, out _)
            || !connection.Reliable.TryEnqueue(ReliableEventType.Roster, baseline.Value.Roster!, out _))
        {
            connection.Disconnect();
            Refuse(endpoint, join.Nonce, "Observer admission could not be queued.");
            return true;
        }
        if (replacing != null)
        {
            _adminMuted.Remove(replacing.Connection.Id);
            (_transport as IMatchConnectionRoutes)?.RemoveConnection(replacing.Connection.Id);
            replacing.Connection.Disconnect();
            // The replacement occupies the same observer slot and preserves
            // ObserverCount, so no transient capacity window is exposed.
            _observers[free] = peer; _connections[8 + free] = peer;
        }
        else
        {
            _observers[free] = peer; _connections[8 + free] = peer; ObserverCount++;
        }
        PublishKeepAlives();
        return true;
    }
    public bool TryForceObserver(ServerPeer peer, out string reason)
    {
        reason = "";
        if (peer.IsObserver || Find(peer.Connection.Id) != peer) { reason = "Participant is not an active player connection."; return false; }
        int free = Array.FindIndex(_observers, 0, ObserverConfiguration.MaxSpectators, value => value == null);
        if (free < 0) { reason = "Observer capacity is full or disabled."; return false; }
        uint delay = peer.TrustedObserver ? 0 : (uint)ObserverConfiguration.DelaySeconds * 60;
        var baseline = _observerTimeline.Baseline(Tick, delay);
        if (baseline == null) { reason = "Observer history is not ready."; return false; }
        if (!peer.Connection.Reliable.CanEnqueueType(ReliableEventType.ObserverTransition))
        { reason = "Reliable control channel is full."; return false; }
        Span<byte> payload = stackalloc byte[MatchTransitionPacket.Size];
        new MatchTransitionPacket(baseline.Value.MatchId, baseline.Value.Tick, baseline.Value.Rules).Write(payload);
        // All preconditions are checked before detaching competitive ownership.
        // Cancelling old live events prevents them leaking across the delay fence.
        peer.Connection.Reliable.CancelPendingExceptWelcome();
        if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.ObserverTransition, payload, out _))
        { reason = "Reliable role transition could not be queued."; return false; }
        var observer = new ServerPeer(peer.Connection,
            new JoinPacket(NetHeader.Version, peer.Nonce, peer.Hunter, peer.Name, Observer: true), byte.MaxValue, _now)
        {
            ConnectionIndex = (byte)(8 + free), TeamIndex = byte.MaxValue, PlayerId = peer.PlayerId,
            GuestSessionId = peer.GuestSessionId, ReservedSeat = peer.ReservedSeat,
            TicketId = peer.TicketId, TrustedObserver = peer.TrustedObserver,
            ObserverDelayTicks = delay, ObserverCursor = baseline, ObserverNeedsBaseline = true,
            Timing = peer.Timing
        };
        ParticipantLeaving?.Invoke(peer, MphRead.Identity.ParticipantExitReason.Replaced, Tick);
        _peers[peer.Slot] = null; _connections[peer.ConnectionIndex] = null; _reconnectPeers[peer.Slot] = null;
        Count--; _observers[free] = observer; _connections[8 + free] = observer; ObserverCount++;
        peer.Connection.BeginLoading(baseline.Value.MatchId);
        _rosterDirty = true; PublishKeepAlives();
        return true;
    }
    private void RemoveObserver(int connectionIndex)
    {
        int index = connectionIndex - 8;
        if ((uint)index >= _observers.Length || _observers[index] is not ServerPeer peer) return;
        _adminMuted.Remove(peer.Connection.Id);
        (_transport as IMatchConnectionRoutes)?.RemoveConnection(peer.Connection.Id);
        peer.Connection.Disconnect(); _observers[index] = null; _connections[connectionIndex] = null;
        ObserverCount--; PublishKeepAlives();
    }
    public void CaptureObserverSnapshot(ReadOnlySpan<byte> bytes)
    { if (ObserverFramesRequired) _observerTimeline.Snapshot(bytes); }
    public void CaptureObserverWorld(ReadOnlySpan<byte> bytes)
    { if (ObserverFramesRequired) _observerTimeline.WorldBatch(bytes); }
    public void CaptureObserverEvent(ReliableEventType type, ReadOnlySpan<byte> bytes)
    {
        if (!ObserverFramesRequired) return;
        Span<byte> payload = stackalloc byte[ReliableChannel.MaxPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, MatchId); bytes.CopyTo(payload[4..]);
        _observerTimeline.Event(type, payload[..(4 + bytes.Length)]);
    }
    public void CommitObserverTick(uint tick)
    {
        if (!ObserverFramesRequired) return;
        ObserverFrame? captured = _observerTimeline.Commit(tick, MatchId, Rules);
        if (captured != null) ObserverFrameCaptured?.Invoke(captured);
        foreach (ServerPeer? peer in _observers)
        {
            if (peer == null || peer.Connection.State != NetConnectionState.Playing) continue;
            if (peer.ObserverCursor == null || !_observerTimeline.Owns(peer.ObserverCursor))
            { RemoveObserver(peer.ConnectionIndex); continue; }
            if (peer.ObserverNeedsBaseline)
            {
                // Loading can take time. Start from a complete due baseline,
                // never from live state or a partially assembled world.
                var baseline = _observerTimeline.Baseline(tick, peer.ObserverDelayTicks);
                if (baseline == null) continue;
                if (baseline.Value.MatchId != peer.Connection.MatchId)
                { TransitionObserver(peer, baseline); continue; }
                if (!SendObserverBaseline(peer, baseline.Value)) { RemoveObserver(peer.ConnectionIndex); continue; }
                peer.ObserverCursor = baseline; peer.ObserverNeedsBaseline = false;
            }
            for (int sent = 0; sent < 120 && peer.ObserverCursor.Next is { } next
                && ObserverTimeline.Due(tick, next.Value.Tick, peer.ObserverDelayTicks); sent++)
            {
                ObserverFrame frame = next.Value;
                if (frame.MatchId != peer.Connection.MatchId)
                {
                    if (!frame.Complete) { peer.ObserverCursor = next; continue; }
                    TransitionObserver(peer, next); break;
                }
                if (!SendObserverFrame(peer, frame)) { RemoveObserver(peer.ConnectionIndex); break; }
                peer.ObserverCursor = next;
            }
        }
    }
    private void TransitionObserver(ServerPeer peer, System.Collections.Generic.LinkedListNode<ObserverFrame> next)
    {
        var transition = new MatchTransitionPacket(next.Value.MatchId, next.Value.Tick, next.Value.Rules);
        Span<byte> payload = stackalloc byte[MatchTransitionPacket.Size]; transition.Write(payload);
        peer.Connection.BeginLoading(next.Value.MatchId);
        peer.Connection.Reliable.CancelPendingExceptWelcome();
        peer.ObserverCursor = next; peer.ObserverNeedsBaseline = true;
        if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.MapTransition, payload, out _)
            || !peer.Connection.Reliable.TryEnqueue(ReliableEventType.Roster, next.Value.Roster!, out _)) RemoveObserver(peer.ConnectionIndex);
    }
    private bool SendObserverBaseline(ServerPeer peer, ObserverFrame frame)
    {
        if (!frame.Complete || !peer.Connection.Reliable.TryEnqueue(ReliableEventType.Roster, frame.Roster!, out _)) return false;
        peer.RosterRevision = frame.RosterRevision;
        peer.Connection.Send(_transport, NetMessageType.Snapshot, frame.Snapshot!);
        foreach (byte[] batch in frame.World!) peer.Connection.Send(_transport, NetMessageType.World, batch);
        return true;
    }
    private bool SendObserverFrame(ServerPeer peer, ObserverFrame frame)
    {
        if (frame.Roster != null && peer.RosterRevision != frame.RosterRevision)
        {
            if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.Roster, frame.Roster, out _)) return false;
            peer.RosterRevision = frame.RosterRevision;
        }
        if (frame.FreshSnapshot && frame.Snapshot != null) peer.Connection.Send(_transport, NetMessageType.Snapshot, frame.Snapshot);
        if (frame.FreshWorld && frame.World != null) foreach (byte[] batch in frame.World) peer.Connection.Send(_transport, NetMessageType.World, batch);
        foreach (ObserverEvent item in frame.Events)
            if (!peer.Connection.Reliable.TryEnqueue(item.Type, item.Payload, out _)) return false;
        return true;
    }
}
