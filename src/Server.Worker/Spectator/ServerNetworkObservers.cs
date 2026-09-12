using System;
using System.Buffers.Binary;
using System.Net;
namespace MphRead.Mods.Network;
public sealed partial class ServerNetwork
{
    private readonly ServerPeer?[] _observers = new ServerPeer?[16];
    private readonly ServerPeer?[] _connections = new ServerPeer?[24];
    private readonly ObserverFrameBuilder _observerFrames;
    private readonly ObserverTimeline _observerTimeline;
    public ObserverOptions ObserverConfiguration { get; }
    public int ObserverCount { get; private set; }
    private Action<ObserverFrame>? _observerFrameCaptured;
    internal Action<ObserverFrame>? ObserverFrameCaptured
    {
        get => _observerFrameCaptured;
        set
        {
            _observerFrameCaptured = value;
            // A replay can be enabled by tournament/admin control after the
            // initial roster publication. Seed the independent frame builder
            // immediately so its first complete frame still has identity.
            if (value != null
                && _rosterLength >= sizeof(uint)
                && BinaryPrimitives.ReadUInt32LittleEndian(_rosterPayload.AsSpan(0, sizeof(uint))) == MatchId)
                _observerFrames.Roster(_rosterPayload.AsSpan(0, _rosterLength), _rosterRevision);
        }
    }
    /// <summary>Replay or spectator capture needs one immutable frame.</summary>
    public bool FrameCaptureRequired => SpectatorHistoryRequired || ObserverFrameCaptured != null;
    /// <summary>Only spectator policy causes historical retention.</summary>
    public bool SpectatorHistoryRequired => ObserverConfiguration.MaxSpectators > 0;
    // Compatibility name for simulation call sites during the split. It no
    // longer means that replay-only matches retain spectator history.
    public bool ObserverFramesRequired => FrameCaptureRequired;
    internal int ObserverHistoryFrameCount => _observerTimeline.Count;
    internal int ObserverHistoryBytes => _observerTimeline.RetainedBytes;
    public ReadOnlySpan<ServerPeer?> ObserverPeers => _observers.AsSpan(0, ObserverConfiguration.MaxSpectators);
    public ReadOnlySpan<ServerPeer?> AllConnections => _connections;
    private bool AdmitObserver(IPEndPoint endpoint, in JoinPacket join, TicketIdentity? identity,
        ReadOnlySpan<byte> authKey = default)
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
        if (free < 0) { Refuse(endpoint, join.Nonce, "Observer connections are full or disabled.", join.AdmissionId, authKey); return true; }
        SimDuration delay = identity?.TrustedObserver == true
            ? SimDuration.Zero
            : SimDuration.FromSeconds(ObserverConfiguration.DelaySeconds);
        var baseline = _observerTimeline.Baseline(new(Tick), delay);
        ObserverFrame? baselineFrame = null;
        if (baseline is { } baselineCursor)
            _observerTimeline.TryGet(baselineCursor, out baselineFrame);
        // A delayed observer may arrive before a full delay window exists. Use
        // the newest complete frame only for connection metadata and roster;
        // presentation remains in the loading/rebaseline path below until a
        // due historical frame exists, so no live snapshot or world state is
        // exposed through this warm-up admission.
        if (baselineFrame == null)
        {
            var handshake = _observerTimeline.Baseline(new(Tick), SimDuration.Zero);
            if (handshake is { } handshakeCursor)
                _observerTimeline.TryGet(handshakeCursor, out baselineFrame);
        }
        if (baselineFrame == null)
        { Refuse(endpoint, join.Nonce, "Observer history is warming up. Try again later.", join.AdmissionId, authKey); return true; }
        ulong id = AllocateConnectionIdentity();
        var connection = new NetConnection(id, endpoint, baselineFrame.MatchId, _now,
            authKey, NetAuthDirection.ServerToClient, ReliableAdaptiveRtoEnabled,
            AckCoalescingEnabled);
        var peer = new ServerPeer(connection, join, byte.MaxValue, _now)
        {
            ConnectionIndex = (byte)(8 + free), TeamIndex = byte.MaxValue,
            PlayerId = identity?.PlayerId, GuestSessionId = identity?.GuestSessionId, ReservedSeat = identity?.ReservedSeat, TicketId = identity?.TicketId ?? Guid.Empty,
            TrustedObserver = identity?.TrustedObserver == true,
            ObserverDelay = delay, ObserverCursor = baseline, ObserverNeedsBaseline = true,
            Timing = new ServerNetworkTimingController(AdaptiveTimingEnabled,
                AdaptiveTimingV2Enabled)
        };
        var accepted = new JoinAcceptedPacket(join.Nonce, byte.MaxValue, baselineFrame.MatchId, baselineFrame.Tick, 60, baselineFrame.Rules);
        Span<byte> payload = stackalloc byte[JoinAcceptedPacket.Size]; accepted.Write(payload);
        // The new connection is still detached, so all reliable admission
        // events must be accepted before the old observer can be removed.
        if (!connection.Reliable.TryEnqueue(ReliableEventType.Welcome, payload, out _)
            || !connection.Reliable.TryEnqueue(ReliableEventType.Roster, baselineFrame.Roster!, out _))
        {
            connection.Disconnect();
            Refuse(endpoint, join.Nonce, "Observer admission could not be queued.", join.AdmissionId, authKey);
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
        SimDuration delay = peer.TrustedObserver
            ? SimDuration.Zero
            : SimDuration.FromSeconds(ObserverConfiguration.DelaySeconds);
        var baseline = _observerTimeline.Baseline(new(Tick), delay);
        if (baseline is not { } baselineCursor
            || !_observerTimeline.TryGet(baselineCursor, out ObserverFrame baselineFrame))
        { reason = "Observer history is not ready."; return false; }
        if (!peer.Connection.Reliable.CanEnqueueType(ReliableEventType.ObserverTransition))
        { reason = "Reliable control channel is full."; return false; }
        Span<byte> payload = stackalloc byte[MatchTransitionPacket.Size];
        new MatchTransitionPacket(baselineFrame.MatchId, baselineFrame.Tick, baselineFrame.Rules).Write(payload);
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
            ObserverDelay = delay, ObserverCursor = baselineCursor, ObserverNeedsBaseline = true,
            Timing = peer.Timing
        };
        ParticipantLeaving?.Invoke(peer, MphRead.Identity.ParticipantExitReason.Replaced, Tick);
        _peers[peer.Slot] = null; _connections[peer.ConnectionIndex] = null; _reconnectPeers[peer.Slot] = null;
        Count--; _observers[free] = observer; _connections[8 + free] = observer; ObserverCount++;
        peer.Connection.BeginLoading(baselineFrame.MatchId);
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
    { if (FrameCaptureRequired) _observerFrames.Snapshot(bytes); }
    public void CaptureObserverWorld(ReadOnlySpan<byte> bytes)
    { if (FrameCaptureRequired) _observerFrames.WorldBatch(bytes); }
    public void CaptureObserverEvent(ReliableEventType type, ReadOnlySpan<byte> bytes)
    {
        if (!FrameCaptureRequired) return;
        Span<byte> payload = stackalloc byte[ReliableChannel.MaxPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, MatchId); bytes.CopyTo(payload[4..]);
        _observerFrames.Event(type, payload[..(4 + bytes.Length)]);
    }
    public void CommitObserverTick(uint tick)
    {
        if (!FrameCaptureRequired) return;
        ObserverFrame captured = _observerFrames.Produce(tick, MatchId, Rules);
        // Replay receives the immutable produced frame even when the
        // spectator ring is disabled or cannot retain it.
        ObserverFrameCaptured?.Invoke(captured);
        if (SpectatorHistoryRequired)
        {
            if (captured.CaptureOverflowed) _observerTimeline.Clear();
            else if (!_observerTimeline.Retain(captured, out _))
            {
                // A frame that cannot be retained must not leave existing
                // cursors looking past an unobservable gap. Replay already
                // received the immutable frame above, so this is spectator
                // history failure only.
                _observerTimeline.Clear();
            }
        }
        foreach (ServerPeer? peer in _observers)
        {
            if (peer == null || peer.Connection.State != NetConnectionState.Playing) continue;
            if (peer.ObserverNeedsBaseline)
            {
                // Loading can take time and the initial cursor may be evicted
                // by a shorter delay-driven ring. Rebaseline explicitly while
                // loading instead of disconnecting the observer.
                var baseline = _observerTimeline.Baseline(new(tick), peer.ObserverDelay);
                if (baseline == null) continue;
                if (!_observerTimeline.TryGet(baseline.Value, out ObserverFrame baselineFrame)) continue;
                if (baselineFrame.MatchId != peer.Connection.MatchId)
                { TransitionObserver(peer, baseline.Value, baselineFrame); continue; }
                if (!SendObserverBaseline(peer, baselineFrame)) { RemoveObserver(peer.ConnectionIndex); continue; }
                peer.ObserverCursor = baseline; peer.ObserverNeedsBaseline = false;
            }
            if (peer.ObserverCursor is not { } cursor || !_observerTimeline.Owns(cursor))
            { RemoveObserver(peer.ConnectionIndex); continue; }
            for (int sent = 0; sent < 120
                && _observerTimeline.TryGetNext(cursor, out ObserverCursor next, out ObserverFrame frame)
                && ObserverTimeline.Due(new(tick), new(frame.Tick), peer.ObserverDelay); sent++)
            {
                if (frame.MatchId != peer.Connection.MatchId)
                {
                    if (!frame.Complete) { cursor = next; peer.ObserverCursor = cursor; continue; }
                    TransitionObserver(peer, next, frame); break;
                }
                if (!SendObserverFrame(peer, frame)) { RemoveObserver(peer.ConnectionIndex); break; }
                cursor = next; peer.ObserverCursor = cursor;
            }
        }
    }
    private void TransitionObserver(ServerPeer peer, ObserverCursor next, ObserverFrame frame)
    {
        var transition = new MatchTransitionPacket(frame.MatchId, frame.Tick, frame.Rules);
        Span<byte> payload = stackalloc byte[MatchTransitionPacket.Size]; transition.Write(payload);
        peer.Connection.BeginLoading(frame.MatchId);
        peer.Connection.Reliable.CancelPendingExceptWelcome();
        peer.ObserverCursor = next; peer.ObserverNeedsBaseline = true;
        if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.MapTransition, payload, out _)
            || !peer.Connection.Reliable.TryEnqueue(ReliableEventType.Roster, frame.Roster!, out _)) RemoveObserver(peer.ConnectionIndex);
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
