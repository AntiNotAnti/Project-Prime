using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Authoritative client connection owned by the game thread. A transport
    /// keepalive remains active while this owner synchronously loads a room.
    /// </summary>
    public sealed class NetClient : IDisposable
    {
        private readonly NetTransport _transport;
        private readonly IPEndPoint _server;
        private JoinPacket _join;
        private double _joinDue;
        private bool _discovered;
        private bool _hasRoleFence;
        private uint _roleHeaderFence;
        private uint _roleEventFence;
        public uint RoleRevision { get; private set; }
        private double _joinStarted;
        private double _lastJoinPending;
        public bool AwaitingBotRetirement { get; private set; }
        private double _pingDue;
        private long _pingSent;
        private readonly NetApplicationEvent[] _events = new NetApplicationEvent[256];
        private int _eventHead;
        private int _eventCount;
        private readonly NetRosterEntry[] _roster = new NetRosterEntry[8];
        private readonly NetRosterEntry[] _incomingRoster = new NetRosterEntry[8];
        private uint _incomingRosterRevision;
        private int _incomingRosterCount;
        private int _rosterCount;
        public uint RosterRevision { get; private set; }
        public bool HasRoster { get; private set; }
        public ReadOnlySpan<NetRosterEntry> Roster => _roster.AsSpan(0, _rosterCount);
        private double _disconnectDeadline;
        public NetWorldPacketValidator? WorldPacketValidator { get; set; }
        public NetWorldPacketHandler? WorldPacketReceived { get; set; }
        public bool IsDisconnecting => _disconnectDeadline != 0;
        private readonly SnapshotPlayer[] _snapshotPlayers = new SnapshotPlayer[8];
        private int _snapshotCount;
        private double _nextTimingTelemetry;
        private long _reportedUnderruns;
        private long _reportedExtrapolated;
        private uint _timingAcknowledgedRevision;
        public ReadOnlySpan<SnapshotPlayer> SnapshotPlayers => _snapshotPlayers.AsSpan(0, _snapshotCount);
        public SnapshotPacket Snapshot { get; private set; }
        public bool HasSnapshot { get; private set; }
        /// <summary>
        /// Last server-selected lag-compensation diagnostic. The packet is
        /// presentation-only; it has no client request or rewind authority.
        /// </summary>
        public HistoricalCollisionDebugPacket? HistoricalDebug { get; private set; }
        public long SnapshotsReceived { get; private set; }
        public long SnapshotReceivedAt { get; private set; }
        public NetworkTimingProfile TimingProfile { get; private set; }
            = NetworkTimingProfile.Compatibility;
        public bool HasTimingProfile => TimingProfile.Revision != 0;
        public bool IsObserver => _join.Observer;
        public NetConnection? Connection { get; private set; }
        public JoinAcceptedPacket Accepted { get; private set; }
        public NetClock Clock { get; private set; } = new();
        public string? Failure { get; private set; }
        public long Rejected { get; private set; }
        public NetConnectionState State => IsDisconnecting ? NetConnectionState.Disconnecting : Connection?.State ?? (Failure == null
            ? NetConnectionState.Connecting : NetConnectionState.Disconnecting);

        public NetClient(NetTransport transport, IPEndPoint server, string name, Hunter hunter, ulong? nonce = null, string ticket = "", bool observer = false, uint wireMatchId = 0)
        {
            _transport = transport;
            _server = server;
            if (nonce == 0 || (!string.IsNullOrEmpty(ticket) && (!nonce.HasValue || !JoinPacket.ValidTicketText(ticket))))
                throw new ArgumentException("An authenticated join requires its ticket's nonzero nonce.");
            _join = new JoinPacket(NetHeader.Version, nonce ?? NetConnection.NewIdentity(), hunter, name, Ticket: ticket, Observer: observer, WireMatchId: wireMatchId);
            _discovered = wireMatchId != 0;
            AwaitingBotRetirement = false;
            _joinStarted = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }

        public void Reconnect(ulong? nonce = null, string ticket = "")
        {
            if ((!string.IsNullOrEmpty(_join.Ticket) && (string.IsNullOrEmpty(ticket) || ticket == _join.Ticket || nonce == _join.Nonce)) || nonce == 0
                || (!string.IsNullOrEmpty(ticket) && (!nonce.HasValue || !JoinPacket.ValidTicketText(ticket))))
                throw new ArgumentException("Authenticated reconnect requires a fresh ticket and nonce.");
            _join = _join with
            {
                Ticket = ticket,
                Nonce = nonce ?? NetConnection.NewIdentity(),
                PreviousConnectionId = Connection?.Id ?? _join.PreviousConnectionId
            };
            _transport.SetKeepAlive(null);
            _hasRoleFence = false;
            Connection = null;
            _discovered = _join.WireMatchId != 0;
            Accepted = default;
            Clock = new NetClock();
            Failure = null;
            AwaitingBotRetirement = false;
            _joinStarted = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _joinDue = _pingDue = 0;
            _pingSent = 0;
            ClearMatchState();
            _disconnectDeadline = 0;
        }

        public void Poll()
        {
            long timestamp = Stopwatch.GetTimestamp();
            double now = timestamp / (double)Stopwatch.Frequency;
            foreach (ReceivedPacket packet in _transport.Drain())
            {
                if (!Handle(packet, timestamp, now))
                {
                    Rejected++;
                }
            }
            if (Failure != null)
            {
                return;
            }
            NetConnection? connection = Connection;
            if (connection == null)
            {
                if (now - _joinStarted > (AwaitingBotRetirement ? 30 : NetConfig.TimeoutSeconds)
                    || AwaitingBotRetirement && now - _lastJoinPending > NetConfig.TimeoutSeconds)
                {
                    Fail("Server did not accept the connection.");
                }
                else if (now >= _joinDue)
                {
                    if (!_discovered)
                    {
                        Span<byte> query = stackalloc byte[2] { (byte)PacketType.StatusQuery, NetHeader.Version };
                        _transport.SendDatagram(_server, query);
                        _joinDue = now + 0.5;
                        return;
                    }
                    Span<byte> datagram = stackalloc byte[NetHeader.Size + _join.EncodedSize];
                    new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(datagram);
                    _join.Write(datagram[NetHeader.Size..]);
                    _transport.SendDatagram(_server, datagram);
                    _joinDue = now + 0.5;
                }
                return;
            }
            if (now - connection.LastReceived > NetConfig.TimeoutSeconds)
            {
                Fail("Server connection timed out.");
                return;
            }
            connection.FlushReliable(_transport, now);
            if (IsDisconnecting)
            {
                if (connection.Reliable.PendingCount == 0 || now >= _disconnectDeadline)
                {
                    connection.Disconnect();
                    _transport.SetKeepAlive(null);
                }
                return;
            }
            if (now >= _pingDue)
            {
                Span<byte> ping = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(ping, timestamp);
                _pingSent = timestamp;
                connection.Send(_transport, NetMessageType.Ping, ping);
                _pingDue = now + 1;
            }
        }

        private bool Handle(in ReceivedPacket packet, long timestamp, double now)
        {
            if (Failure != null || !packet.Sender.Equals(_server)) { return false; }
            ReadOnlySpan<byte> datagram = packet.Data.AsSpan(0, packet.Length);
            if (Connection == null && datagram.Length > 1 && datagram[0] == (byte)PacketType.StatusReply)
            {
                if (!ServerStatusPacket.TryRead(datagram[1..], out ServerStatusPacket status)) { return false; }
                if (!NetWireIdentity.IsCompatible(status.Family, status.Protocol))
                {
                    Fail(NetWireIdentity.IncompatibilityReason(status.Family, status.Protocol));
                }
                else
                {
                    _discovered = true;
                    _joinDue = 0;
                }
                return true;
            }
            if (!_discovered || !NetHeader.TryRead(datagram, out NetHeader header))
            {
                return false;
            }
            if (_hasRoleFence && header.Type is NetMessageType.Snapshot or NetMessageType.World or NetMessageType.Debug
                && !Sequence32.IsNewer(header.Sequence, _roleHeaderFence)) return false;
            ReadOnlySpan<byte> body = packet.Data.AsSpan(NetHeader.Size, packet.Length - NetHeader.Size);
            if (Connection == null)
            {
                if (header.Type == NetMessageType.JoinPending && JoinPendingPacket.TryRead(body, out var pending)
                    && pending.Nonce == _join.Nonce)
                {
                    AwaitingBotRetirement = true;
                    _lastJoinPending = now;
                    return true;
                }
                if (header.Type == NetMessageType.Refused && body.Length == 48
                    && BinaryPrimitives.ReadUInt64LittleEndian(body) == _join.Nonce)
                {
                    Fail(NetText.Read(body[8..]));
                    return true;
                }
                if (header.Type != NetMessageType.Accepted
                    || !ReliableEventPacket.TryRead(body, out _, out ReliableEventType type, out ReadOnlySpan<byte> payload)
                    || type != ReliableEventType.Welcome
                    || !JoinAcceptedPacket.TryRead(payload, out JoinAcceptedPacket accepted)
                    || accepted.ClientNonce != _join.Nonce || _join.Observer && !accepted.IsObserver)
                {
                    return false;
                }
                Connection = new NetConnection(header.ConnectionId, _server, accepted.MatchId, now);
                Accepted = accepted;
                if (accepted.IsObserver) _join = _join with { Observer = true };
                Span<byte> keepalive = stackalloc byte[NetHeader.Size];
                new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced,
                    header.ConnectionId, 0, 0, 0).Write(keepalive);
                _transport.SetKeepAlive(_server, keepalive);
            }
            uint eventId = 0;
            ReliableEventType eventType = default;
            ReadOnlySpan<byte> eventBody = default;
            NetConnection connection = Connection;
            Span<SnapshotPlayer> players = stackalloc SnapshotPlayer[8];
            SnapshotPacket snapshot = default;
            HistoricalCollisionDebugPacket? debug = null;
            int playerCount = 0;
            bool valid = header.Type switch
            {
                NetMessageType.KeepAlive or NetMessageType.Ack => body.IsEmpty,
                NetMessageType.Accepted => ReliableEventPacket.TryRead(body, out eventId,
                    out ReliableEventType type, out ReadOnlySpan<byte> payload)
                    && type == ReliableEventType.Welcome
                    && JoinAcceptedPacket.TryRead(payload, out JoinAcceptedPacket accepted)
                    && accepted.ClientNonce == _join.Nonce && accepted.Slot == Accepted.Slot,
                NetMessageType.Event => ReliableEventPacket.TryRead(body, out eventId, out eventType, out eventBody)
                    && ValidateEvent(eventType, eventBody),
                NetMessageType.World => WorldPacketValidator != null && WorldPacketReceived != null
                    && WorldPacketValidator(body, connection.MatchId),
                NetMessageType.Ping => body.Length == 8,
                NetMessageType.Pong => body.Length == 12 && _pingSent != 0
                    && BinaryPrimitives.ReadInt64LittleEndian(body) == _pingSent,
                NetMessageType.Snapshot => SnapshotPacket.TryRead(body, players, out snapshot, out playerCount)
                    && snapshot.MatchId == connection.MatchId,
                NetMessageType.Debug => HistoricalCollisionDebugPacket.TryRead(body, connection.MatchId, out debug),
                _ => false
            };
            if (!valid || !connection.TryReceive(header, packet.Sender, now, out ReceiveResult result))
            {
                return false;
            }
            long processedAt = Stopwatch.GetTimestamp();
            connection.Metrics.Receive(packet, processedAt);
            long packetAt = packet.ReceivedAt > 0 ? packet.ReceivedAt : processedAt;
            if (header.Type == NetMessageType.Event)
            {
                connection.Send(_transport, NetMessageType.Ack);
                if (connection.Reliable.Receive(eventId))
                {
                    if (eventType == ReliableEventType.ObserverTransition && !_join.Observer)
                    { _hasRoleFence = true; _roleHeaderFence = header.Sequence; _roleEventFence = eventId; }
                    if (!_hasRoleFence || eventType == ReliableEventType.ObserverTransition || Sequence32.IsNewer(eventId, _roleEventFence))
                        ReceiveEvent(eventType, eventBody);
                }
            }
            else if (header.Type == NetMessageType.World && result != ReceiveResult.Duplicate)
            {
                WorldPacketReceived!(body);
            }
            else if (header.Type == NetMessageType.Ping && result != ReceiveResult.Duplicate)
            {
                Span<byte> reply = stackalloc byte[12];
                body.CopyTo(reply);
                BinaryPrimitives.WriteUInt32LittleEndian(reply[8..], Snapshot.ServerTick);
                connection.Send(_transport, NetMessageType.Pong, reply);
            }
            else if (header.Type == NetMessageType.Snapshot)
            {
                if (!HasSnapshot || Sequence32.IsNewer(snapshot.Sequence, Snapshot.Sequence))
                {
                    if (!HasSnapshot)
                    {
                        // A server may have paused its tick while loading.
                        // Re-establish the offset when gameplay resumes;
                        // old pre-load samples must not bias it for seconds.
                        Clock = new NetClock();
                        _pingSent = 0;
                        _pingDue = 0;
                    }
                    Snapshot = snapshot;
                    SnapshotReceivedAt = packet.ReceivedAt;
                    players[..playerCount].CopyTo(_snapshotPlayers);
                    _snapshotCount = playerCount;
                    HasSnapshot = true;
                    SnapshotsReceived++;
                    connection.Metrics.Snapshot(packetAt);
                    if (IsObserver) connection.StartPlaying();
                    foreach (SnapshotPlayer player in players[..playerCount])
                    {
                        if (player.Slot == Accepted.Slot && player.ConnectionId == connection.Id
                            && (player.Flags & SnapshotPlayerFlags.Active) != 0)
                        {
                            connection.StartPlaying();
                        }
                    }
                }
            }
            else if (header.Type == NetMessageType.Debug && result != ReceiveResult.Duplicate)
            {
                HistoricalDebug = debug!.IsClear ? null : debug;
            }
            else if (header.Type == NetMessageType.Accepted)
            {
                connection.Reliable.Receive(eventId);
                connection.Send(_transport, NetMessageType.Ack);
            }
            else if (header.Type == NetMessageType.Pong && result != ReceiveResult.Duplicate)
            {
                connection.Metrics.RecordRtt((timestamp - _pingSent)
                    * (1000.0 / Stopwatch.Frequency));
                Clock.Observe(_pingSent, timestamp, BinaryPrimitives.ReadUInt32LittleEndian(body[8..]));
                _pingSent = 0;
            }
            return true;
        }

        private bool ValidateEvent(ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (type == ReliableEventType.IntermissionBallot)
                return IntermissionBallot.TryRead(payload, out var ballot)
                    && (ballot!.MatchId == Connection!.MatchId || Sequence32.IsNewer(Connection!.MatchId, ballot.MatchId));
            if (type == ReliableEventType.Combat)
            {
                Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                if (payload.Length < 4 || !CombatEventBatch.TryRead(payload[4..], events, out _)) { return false; }
            }
            if (type == ReliableEventType.WorldEvent && (payload.Length < 4
                || !WorldEvent.TryRead(payload[4..], out WorldEvent worldEvent)
                || worldEvent.MatchId != BinaryPrimitives.ReadUInt32LittleEndian(payload))) return false;
            if (type == ReliableEventType.Kill && (payload.Length < 4
                || !KillEvent.TryRead(payload[4..], out KillEvent kill)
                || kill.MatchId != BinaryPrimitives.ReadUInt32LittleEndian(payload))) return false;
            if (type == ReliableEventType.MatchAward && (payload.Length < 4
                || !MatchAwardPacket.TryRead(payload[4..], out MatchAwardPacket award)
                || !MatchAwardPacketConversion.TryToAward(award, out _)
                || award.MatchId != BinaryPrimitives.ReadUInt32LittleEndian(payload))) return false;
            if (type == ReliableEventType.MatchSemantic && (payload.Length < 4
                || !MatchSemanticEventPacket.TryRead(payload[4..], out MatchSemanticEventPacket semantic)
                || !MatchSemanticEventPacketConversion.TryToEvent(semantic, out _)
                || semantic.MatchId != BinaryPrimitives.ReadUInt32LittleEndian(payload))) return false;
            if (type == ReliableEventType.Roster && (payload.Length < 4
                || !SessionRosterPacket.TryRead(payload[4..], _incomingRoster,
                    out _incomingRosterRevision, out _incomingRosterCount))) { return false; }
            if (type == ReliableEventType.Chat && (payload.Length < 4
                || !SessionChatPacket.TryRead(payload[4..], out _))) { return false; }
            if (type == ReliableEventType.TimingProfile)
                return NetworkTimingProfilePacket.TryRead(payload, out _);
            return type switch
            {
                ReliableEventType.MapTransition or ReliableEventType.ObserverTransition => MatchTransitionPacket.TryRead(payload, out _),
                ReliableEventType.Disconnect => payload.IsEmpty,
                ReliableEventType.MatchState or ReliableEventType.Combat or ReliableEventType.World
                    or ReliableEventType.Roster or ReliableEventType.Chat or ReliableEventType.Kill or ReliableEventType.WorldEvent
                    or ReliableEventType.MatchAward or ReliableEventType.MatchSemantic =>
                    payload.Length >= 4 && (BinaryPrimitives.ReadUInt32LittleEndian(payload) == Connection!.MatchId
                        ? type == ReliableEventType.Roster || _eventCount < _events.Length
                        : Sequence32.IsNewer(Connection!.MatchId, BinaryPrimitives.ReadUInt32LittleEndian(payload))),
                _ => false
            };
        }

        private void ReceiveEvent(ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (type == ReliableEventType.IntermissionBallot)
            {
                if (!IsDisconnecting && IntermissionBallot.TryRead(payload, out var ballot) && ballot!.MatchId == Connection!.MatchId
                    && (Ballot == null || Sequence32.IsNewer(ballot.Revision, Ballot.Revision)
                        || ballot.Revision == Ballot.Revision && Sequence32.IsNewer(ballot.UpdateRevision, Ballot.UpdateRevision))) Ballot = ballot;
                return;
            }
            if (type == ReliableEventType.TimingProfile)
            {
                NetworkTimingProfilePacket.TryRead(payload, out NetworkTimingProfile profile);
                if (TimingProfile.Revision == 0
                    || Sequence32.IsNewer(profile.Revision, TimingProfile.Revision))
                    TimingProfile = profile;
                return;
            }
            NetConnection connection = Connection!;
            if (IsDisconnecting && type != ReliableEventType.Disconnect) { return; }
            if (type == ReliableEventType.ObserverTransition)
            {
                if (_join.Observer) return;
                MatchTransitionPacket.TryRead(payload, out MatchTransitionPacket transition);
                _join = _join with { Observer = true };
                connection.BeginLoading(transition.MatchId);
                connection.Reliable.CancelPendingExceptWelcome();
                Accepted = Accepted with { Slot = byte.MaxValue, MatchId = transition.MatchId,
                    ServerTick = transition.ServerTick, Rules = transition.Rules };
                RoleRevision++; ClearMatchState();
            }
            else if (type == ReliableEventType.MapTransition)
            {
                MatchTransitionPacket.TryRead(payload, out MatchTransitionPacket transition);
                if (!Sequence32.IsNewer(transition.MatchId, connection.MatchId)) { return; }
                connection.BeginLoading(transition.MatchId);
                connection.Reliable.CancelPendingExceptWelcome();
                Accepted = Accepted with { MatchId = transition.MatchId, ServerTick = transition.ServerTick,
                    Rules = transition.Rules };
                ClearMatchState();
            }
            else if (type == ReliableEventType.Disconnect) { Fail("Server disconnected the session."); }
            else if (BinaryPrimitives.ReadUInt32LittleEndian(payload) == connection.MatchId)
            {
                if (type == ReliableEventType.Roster)
                {
                    if (!HasRoster || Sequence32.IsNewer(_incomingRosterRevision, RosterRevision))
                    {
                        _incomingRoster.AsSpan(0, _incomingRosterCount).CopyTo(_roster);
                        _rosterCount = _incomingRosterCount;
                        Array.Clear(_roster, _rosterCount, _roster.Length - _rosterCount);
                        RosterRevision = _incomingRosterRevision;
                        HasRoster = true;
                    }
                    return;
                }
                int index = (_eventHead + _eventCount) % _events.Length;
                _events[index] = new(connection.MatchId, type, payload[4..].ToArray());
                _eventCount++;
            }
        }

        private void ClearMatchState()
        {
            Ballot = null;
            HasSnapshot = false;
            Snapshot = default;
            HistoricalDebug = null;
            SnapshotReceivedAt = 0;
            _snapshotCount = 0;
            Array.Clear(_snapshotPlayers);
            Array.Clear(_events);
            Array.Clear(_roster);
            Array.Clear(_incomingRoster);
            _rosterCount = _incomingRosterCount = 0;
            RosterRevision = 0;
            HasRoster = false;
            _eventHead = _eventCount = 0;
            TimingProfile = NetworkTimingProfile.Compatibility;
            _timingAcknowledgedRevision = 0;
            _nextTimingTelemetry = 0;
            _reportedUnderruns = _reportedExtrapolated = 0;
        }

        public bool TryDequeueEvent(out NetApplicationEvent item)
        {
            item = default;
            if (_eventCount == 0) { return false; }
            item = _events[_eventHead];
            _events[_eventHead] = default;
            _eventHead = (_eventHead + 1) % _events.Length;
            _eventCount--;
            return true;
        }

        /// <summary>Keep polling until closed or the one-second delivery budget expires.</summary>
        public bool Disconnect()
        {
            if (Connection == null || IsDisconnecting || Connection.State == NetConnectionState.Disconnecting) { return false; }
            Connection.Reliable.CancelPendingExceptWelcome();
            if (!Connection.Reliable.TryEnqueue(ReliableEventType.Disconnect, default, out _)) { return false; }
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _disconnectDeadline = now + 1;
            Connection.FlushReliable(_transport, now);
            return true;
        }

        /// <summary>
        /// Complete the bounded disconnect exchange before the owner disposes
        /// the transport. Polling also lets simulated-delay queues transmit it.
        /// </summary>
        public void Close()
        {
            Disconnect();
            while (Connection != null && IsDisconnecting
                && Connection.State != NetConnectionState.Disconnecting
                && Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency < _disconnectDeadline)
            {
                Poll();
                if (Connection.State != NetConnectionState.Disconnecting) { Thread.Sleep(2); }
            }
            _transport.SetKeepAlive(null);
            Connection?.Disconnect();
        }

        public bool Ready(uint matchId)
        {
            NetConnection? connection = Connection;
            if (IsDisconnecting || connection == null || connection.MatchId != matchId || connection.State != NetConnectionState.Loading)
            {
                return false;
            }
            Span<byte> payload = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, matchId);
            return connection.Reliable.TryEnqueue(ReliableEventType.ClientReady, payload, out _)
                && connection.Ready(matchId);
        }

        public IntermissionBallot? Ballot { get; private set; }
        public bool Vote(byte optionId)
        {
            if (Connection == null || IsObserver || IsDisconnecting || Ballot == null || Ballot.SelectedId != 0
                || Ballot.MatchId != Connection.MatchId || Connection.State is not (NetConnectionState.Playing or NetConnectionState.Ready)
                || Ballot.HasDeadline && (Snapshot.ServerTick == Ballot.DeadlineTick || Sequence32.IsNewer(Snapshot.ServerTick, Ballot.DeadlineTick))) return false;
            bool offered = false;
            foreach (var option in Ballot.Options) if (option.Id == optionId) offered = true;
            if (!offered) return false;
            Span<byte> payload = stackalloc byte[IntermissionVoteRequest.Size];
            new IntermissionVoteRequest(Ballot.MatchId, Ballot.PhaseRevision, Ballot.Revision, optionId).Write(payload);
            return Connection.Reliable.TryEnqueue(ReliableEventType.IntermissionVote, payload, out _);
        }

        public bool SendChat(string text)
        {
            if (Connection == null || IsDisconnecting || Connection.State == NetConnectionState.Disconnecting
                || String.IsNullOrWhiteSpace(text)) { return false; }
            Span<byte> payload = stackalloc byte[4 + SessionChatRequest.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, Connection.MatchId);
            SessionChatRequest.Write(payload[4..], text);
            return Connection.Reliable.TryEnqueue(ReliableEventType.ChatRequest, payload, out _);
        }

        public bool SendInputs(ReadOnlySpan<InputCommand> commands, uint phaseRevision = 0)
        {
            if (IsObserver) return false;
            if (IsDisconnecting || Connection?.State is not (NetConnectionState.Ready or NetConnectionState.Playing))
            {
                return false;
            }
            Span<byte> payload = stackalloc byte[InputBundle.MaxSize];
            int length = InputBundle.Write(payload, Connection.MatchId, commands, phaseRevision);
            Connection.Send(_transport, NetMessageType.Input, payload[..length]);
            return true;
        }

        public bool AcknowledgeTimingProfile(uint revision)
        {
            NetConnection? connection = Connection;
            if (revision == 0 || revision != TimingProfile.Revision
                || revision == _timingAcknowledgedRevision || connection == null
                || IsDisconnecting) return false;
            Span<byte> payload = stackalloc byte[NetworkTimingProfileAppliedPacket.Size];
            NetworkTimingProfileAppliedPacket.Write(payload, revision);
            if (!connection.Reliable.TryEnqueue(ReliableEventType.TimingProfileApplied,
                payload, out _)) return false;
            _timingAcknowledgedRevision = revision;
            return true;
        }

        public bool ReportTiming(SnapshotInterpolation interpolation, long timestamp)
        {
            ArgumentNullException.ThrowIfNull(interpolation);
            NetConnection? connection = Connection;
            double now = timestamp / (double)Stopwatch.Frequency;
            if (!HasTimingProfile || connection == null || IsDisconnecting
                || connection.State is not (NetConnectionState.Ready or NetConnectionState.Playing)
                || now < _nextTimingTelemetry || connection.Metrics.SnapshotIntervalMs.Count == 0)
                return false;
            long underruns = Math.Max(0, interpolation.UnderrunSamples - _reportedUnderruns);
            long extrapolated = Math.Max(0, interpolation.ExtrapolatedSamples - _reportedExtrapolated);
            _reportedUnderruns = interpolation.UnderrunSamples;
            _reportedExtrapolated = interpolation.ExtrapolatedSamples;
            var telemetry = new NetworkTimingTelemetry(TimingProfile.Revision,
                (byte)Math.Clamp((int)Math.Round(interpolation.DelayTicks),
                    NetworkTimingProfile.MinimumPresentationDelayTicks,
                    NetworkTimingProfile.MaximumPresentationDelayTicks),
                (ushort)Math.Min(UInt16.MaxValue, underruns),
                (ushort)Math.Min(UInt16.MaxValue, extrapolated),
                (ushort)Math.Clamp((int)Math.Round(connection.Metrics.SnapshotIntervalMs.Mean * 10), 50, 10_000),
                (ushort)Math.Clamp((int)Math.Round(connection.Metrics.SnapshotIntervalJitterMs * 10), 0, 10_000));
            Span<byte> payload = stackalloc byte[NetworkTimingTelemetry.Size];
            telemetry.Write(payload);
            connection.Send(_transport, NetMessageType.TimingTelemetry, payload);
            _nextTimingTelemetry = now + 1;
            return true;
        }

        private void Fail(string reason)
        {
            Failure = reason;
            Connection?.Disconnect();
            _transport.SetKeepAlive(null);
        }

        public void Dispose()
        {
            Disconnect();
            _transport.SetKeepAlive(null);
            Connection?.Disconnect();
        }
    }
}
