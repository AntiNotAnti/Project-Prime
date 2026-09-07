using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

namespace MphRead.Mods.Network
{
    public sealed class ServerPeer
    {
        public NetConnection Connection { get; }
        public ulong Nonce { get; }
        public byte Slot { get; }
        public byte TeamIndex { get; internal set; }
        public string Name { get; }
        public Hunter Hunter { get; }
        public ServerInputStream Inputs { get; internal set; } = new();
        internal NetRateLimit Packets;
        internal long PingSent;
        internal double NextPing;
        internal NetRateLimit ChatCredit;
        internal uint RosterRevision;
        internal bool HasRoster;
        public long ChatDropped { get; internal set; }

        internal ServerPeer(NetConnection connection, in JoinPacket join, byte slot, double now)
        {
            Connection = connection;
            Nonce = join.Nonce;
            Slot = slot;
            Name = join.Name;
            Hunter = join.Hunter;
            Packets = new NetRateLimit(180, 240, now);
            ChatCredit = new NetRateLimit(0.5, 3, now);
        }
    }

    /// <summary>
    /// Admission and reliable lifecycle for the simulation's single writer.
    /// Poll never activates a player: the simulation consumes Ready peers and
    /// explicitly starts them. Socket workers only enqueue datagrams.
    /// </summary>
    public sealed class ServerNetwork
    {
        private readonly INetTransport _transport;
        private readonly ServerPeer?[] _peers = new ServerPeer?[RosterPacket.MaxSlots];
        private readonly int _capacity;
        private NetRateLimit _joins;
        private NetRateLimit _statusQueries;
        private double _now;
        private bool _rosterDirty = true;
        private uint _rosterRevision;
        private readonly NetRosterEntry[] _rosterEntries = new NetRosterEntry[8];
        private readonly byte[] _rosterPayload = new byte[4 + SessionRosterPacket.MaxSize];
        private int _rosterLength;
        private double _nextRosterPingRefresh;
        private readonly ushort[] _rosterPings = new ushort[8];
        public uint MatchId { get; private set; }
        public uint Tick { get; private set; }
        public string Room { get; private set; }
        public GameMode Mode { get; private set; }
        public MatchRules Rules { get; private set; }
        public MatchPhase Phase { get; set; } = MatchPhase.WaitingForPlayers;
        public uint PhaseRevision { get; set; }
        public string ServerName { get; set; } = "Prime Hunters";
        public Func<MatchStatePacket>? StatusProvider { get; set; }
        public ReadOnlySpan<ServerPeer?> Peers => _peers;
        public int Count { get; private set; }
        public long Rejected { get; private set; }

        public ServerNetwork(INetTransport transport, string room, GameMode mode,
            uint matchId = 1, int capacity = RosterPacket.MaxSlots)
            : this(transport, MatchRules.CreateDefault(mode.ToMatchMode(), room, capacity), matchId) { }

        public ServerNetwork(INetTransport transport, MatchRules rules, uint matchId = 1)
        {
            MatchLifecycle.ValidateRules(rules);
            if (matchId == 0) { throw new ArgumentOutOfRangeException(nameof(matchId)); }
            int capacity = rules.MaxPlayers;
            string room = rules.RoomKey;
            GameMode mode = rules.Mode.ToLegacyMode();
            Rules = rules;
            if (capacity < 1 || capacity > RosterPacket.MaxSlots)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            _transport = transport;
            _capacity = capacity;
            Room = room;
            Mode = mode;
            MatchId = matchId;
            _now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _joins = new NetRateLimit(16, 32, _now);
            _statusQueries = new NetRateLimit(8, 16, _now);
        }

        public void Poll(uint tick)
        {
            Tick = tick;
            long timestamp = Stopwatch.GetTimestamp();
            double now = timestamp / (double)Stopwatch.Frequency;
            if (now - _now > 1)
            {
                // An owner-side load is not network RTT. Ignore challenges
                // that straddled the pause and measure afresh after resuming.
                foreach (ServerPeer? peer in _peers)
                {
                    if (peer != null) { peer.PingSent = 0; peer.NextPing = 0; }
                }
            }
            _now = now;
            foreach (ReceivedPacket packet in _transport.Drain())
            {
                if (!Handle(packet, timestamp))
                {
                    Rejected++;
                }
            }
            PublishRoster();
            Span<byte> ping = stackalloc byte[8];
            for (int slot = 0; slot < _capacity; slot++)
            {
                ServerPeer? peer = _peers[slot];
                if (peer == null)
                {
                    continue;
                }
                NetConnection connection = peer.Connection;
                if (_now - connection.LastReceived > NetConfig.TimeoutSeconds)
                {
                    Remove(slot);
                    continue;
                }
                connection.FlushReliable(_transport, _now);
                if (_now >= peer.NextPing)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(ping, timestamp);
                    peer.PingSent = timestamp;
                    peer.NextPing = _now + 1;
                    connection.Send(_transport, NetMessageType.Ping, ping);
                }

            }
        }

        private bool Handle(in ReceivedPacket packet, long timestamp)
        {
            ReadOnlySpan<byte> bytes = packet.Data.AsSpan(0, packet.Length);
            // Discovery is a read-only compatibility surface, never a v4 join.
            if (bytes.Length == 2 && bytes[0] == (byte)PacketType.StatusQuery)
            {
                if (!_statusQueries.Take(_now)) { return false; }
                var status = new ServerStatusPacket
                {
                    Match = StatusProvider?.Invoke() ?? new MatchStatePacket
                    {
                        MatchId = unchecked((ushort)MatchId), Mode = (byte)Mode, RoomKey = Room,
                        NextRoomKey = Room, PlayerCount = (byte)Count,
                        Flags = MatchStatePacket.FlagInProgress
                    },
                    MaxPlayers = (byte)_capacity, Protocol = NetHeader.Version, Family = NetWireIdentity.Family, ServerName = ServerName
                };
                Span<byte> reply = stackalloc byte[ServerStatusPacket.Size];
                status.Write(reply);
                _transport.Send(packet.Sender, PacketType.StatusReply, reply);
                return true;
            }
            if (!NetHeader.TryRead(bytes, out NetHeader header))
            {
                return false;
            }
            ReadOnlySpan<byte> body = bytes[NetHeader.Size..];
            if (header.Type == NetMessageType.Join)
            {
                return _joins.Take(_now) && JoinPacket.TryRead(body, out JoinPacket join)
                    && Admit(packet.Sender, join);
            }
            ServerPeer? peer = Find(header.ConnectionId);
            if (peer == null || !peer.Packets.Take(_now))
            {
                return false;
            }
            uint eventId = 0;
            ReliableEventType eventType = default;
            ReadOnlySpan<byte> eventBody = default;
            Span<InputCommand> commands = stackalloc InputCommand[InputBundle.Capacity];
            int commandCount = 0;
            bool valid = header.Type switch
            {
                NetMessageType.KeepAlive or NetMessageType.Ack => body.IsEmpty,
                NetMessageType.Ping => body.Length == 8,
                NetMessageType.Pong => body.Length == 12 && peer.PingSent != 0
                    && BinaryPrimitives.ReadInt64LittleEndian(body) == peer.PingSent,
                NetMessageType.Input => InputBundle.TryRead(body, commands, out uint inputMatch, out uint inputPhase, out commandCount)
                    && inputMatch == MatchId && peer.Connection.State == NetConnectionState.Playing
                    && Phase == MatchPhase.Playing && inputPhase == PhaseRevision,
                NetMessageType.Event => ReliableEventPacket.TryRead(body, out eventId, out eventType, out eventBody)
                    && (eventType == ReliableEventType.ClientReady && eventBody.Length == 4
                        || eventType == ReliableEventType.Disconnect && eventBody.IsEmpty
                        || eventType == ReliableEventType.ChatRequest && eventBody.Length == 4 + SessionChatRequest.Size
                            && SessionChatRequest.TryRead(eventBody[4..], out _)),
                _ => false
            };
            NetConnection connection = peer.Connection;
            IPEndPoint previousEndpoint = connection.Endpoint;
            if (!valid || !connection.TryReceive(header, packet.Sender, _now, out ReceiveResult result))
            {
                return false;
            }
            if (!previousEndpoint.Equals(connection.Endpoint)) { PublishKeepAlives(); }
            connection.Metrics.Receive(packet, timestamp);
            if (header.Type == NetMessageType.Input && result != ReceiveResult.Duplicate)
            {
                peer.Inputs.Receive(commands[..commandCount], Tick);
                connection.Metrics.Input(timestamp);
            }
            else if (header.Type == NetMessageType.Event)
            {
                // ACK duplicates too: the previous ACK may have been lost.
                connection.Send(_transport, NetMessageType.Ack);
                if (connection.Reliable.Receive(eventId))
                {
                    if (eventType == ReliableEventType.Disconnect) { Remove(peer.Slot); }
                    else if (eventType == ReliableEventType.ClientReady)
                    {
                        _rosterDirty |= connection.Ready(BinaryPrimitives.ReadUInt32LittleEndian(eventBody));
                    }
                    else if (BinaryPrimitives.ReadUInt32LittleEndian(eventBody) == MatchId)
                    {
                        HandleChat(peer, eventBody[4..]);
                    }
                }
            }
            else if (header.Type == NetMessageType.Pong && result != ReceiveResult.Duplicate)
            {
                connection.Metrics.RecordRtt((timestamp - peer.PingSent) * (1000.0 / Stopwatch.Frequency));
                peer.PingSent = 0;
            }
            else if (header.Type == NetMessageType.Ping && result != ReceiveResult.Duplicate)
            {
                Span<byte> reply = stackalloc byte[12];
                body.CopyTo(reply);
                BinaryPrimitives.WriteUInt32LittleEndian(reply[8..], Tick);
                connection.Send(_transport, NetMessageType.Pong, reply);
            }
            return true;
        }

        public bool AdmissionClosed { get; set; }

        private bool Admit(IPEndPoint endpoint, in JoinPacket join)
        {
            if (AdmissionClosed) { return false; }
            if (join.Protocol != NetHeader.Version)
            {
                Refuse(endpoint, join.Nonce, $"Authoritative protocol {NetHeader.Version} required.");
                return true;
            }
            int free = -1;
            for (int slot = 0; slot < _capacity; slot++)
            {
                ServerPeer? peer = _peers[slot];
                if (peer == null)
                {
                    if (free < 0)
                    {
                        free = slot;
                    }
                    continue;
                }
                if (peer.Nonce == join.Nonce)
                {
                    // Retries are idempotent, and cannot change the routing
                    // of a session established by a different endpoint.
                    return peer.Connection.Endpoint.Equals(endpoint);
                }
                if (peer.Connection.Id == join.PreviousConnectionId)
                {
                    free = slot;
                    Remove(slot);
                    break;
                }
                if (peer.Connection.Endpoint.Equals(endpoint))
                {
                    return false; // a stale initial Join cannot evict a peer
                }
            }
            if (free < 0)
            {
                Refuse(endpoint, join.Nonce, "Server is full.");
                return true;
            }
            ulong id;
            do { id = NetConnection.NewIdentity(); } while (Find(id) != null);
            var connection = new NetConnection(id, endpoint, MatchId, _now);
            var accepted = new JoinAcceptedPacket(join.Nonce, (byte)free, MatchId, Tick, 60, Rules);
            Span<byte> payload = stackalloc byte[JoinAcceptedPacket.Size];
            accepted.Write(payload);
            connection.Reliable.TryEnqueue(ReliableEventType.Welcome, payload, out _);
            _peers[free] = new ServerPeer(connection, join, (byte)free, _now)
            { TeamIndex = Rules.Teams ? SelectJoiningTeam((byte)free) : (byte)free };
            Count++;
            PublishKeepAlives();
            _rosterDirty = true;
            return true;
        }

        private void PublishKeepAlives()
        {
            // Membership and authenticated routing changes are infrequent.
            // Publish complete immutable templates; the socket worker never
            // reads connection state, ACK windows, or simulation objects.
            var keepAlives = new NetKeepAlive[Count];
            int count = 0;
            foreach (ServerPeer? peer in _peers)
            {
                if (peer == null) { continue; }
                byte[] datagram = new byte[NetHeader.Size];
                new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced,
                    peer.Connection.Id, 0, 0, 0).Write(datagram);
                keepAlives[count++] = new(peer.Connection.Endpoint, datagram);
            }
            _transport.SetKeepAlives(keepAlives);
        }

        private byte SelectJoiningTeam(byte slot)
        {
            Span<byte> teams = stackalloc byte[8];
            teams.Fill(TeamAllocator.Unassigned);
            // Reserve the assignment during loading too, so simultaneous joins
            // do not all choose the same apparently empty team.
            foreach (ServerPeer? peer in _peers)
                if (peer != null) teams[peer.Slot] = peer.TeamIndex;
            return TeamAllocator.Select(teams, (byte)(slot & 1));
        }

        public bool RebalanceBeforeStart()
        {
            if (!Rules.Teams || Phase is not (MatchPhase.WaitingForPlayers or MatchPhase.Countdown)) return false;
            Span<byte> teams = stackalloc byte[8];
            teams.Fill(TeamAllocator.Unassigned);
            foreach (ServerPeer? peer in _peers)
                if (peer?.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing)
                    teams[peer.Slot] = peer.TeamIndex;
            if (TeamAllocator.Rebalance(teams) == 0) return false;
            foreach (ServerPeer? peer in _peers)
                if (peer != null && teams[peer.Slot] != TeamAllocator.Unassigned)
                    peer.TeamIndex = teams[peer.Slot];
            _rosterDirty = true;
            return true;
        }

        private static ushort MeasuredPing(ServerPeer peer) => peer.Connection.Metrics.Rtt.Count == 0
            ? (ushort)0 : (ushort)Math.Clamp(Math.Round(peer.Connection.Metrics.SmoothedRttMs), 1, UInt16.MaxValue);

        private void PublishRoster()
        {
            if (_now >= _nextRosterPingRefresh)
            {
                _nextRosterPingRefresh = _now + 2;
                foreach (ServerPeer? peer in _peers)
                {
                    if (peer != null && _rosterPings[peer.Slot] != MeasuredPing(peer)) { _rosterDirty = true; }
                }
            }
            if (_rosterDirty)
            {
                int count = 0;
                foreach (ServerPeer? peer in _peers)
                {
                    if (peer == null) { continue; }
                    byte team = peer.TeamIndex;
                    ushort ping = MeasuredPing(peer);
                    _rosterPings[peer.Slot] = ping;
                    _rosterEntries[count++] = new(peer.Slot, peer.Connection.Id, peer.Hunter, team, peer.Name, ping);
                }
                _rosterRevision++;
                BinaryPrimitives.WriteUInt32LittleEndian(_rosterPayload, MatchId);
                _rosterLength = 4 + SessionRosterPacket.Write(_rosterPayload.AsSpan(4), _rosterRevision,
                    _rosterEntries.AsSpan(0, count));
                Array.Clear(_rosterEntries, count, _rosterEntries.Length - count);
                _rosterDirty = false;
            }
            foreach (ServerPeer? peer in _peers)
            {
                if (peer != null && (!peer.HasRoster || peer.RosterRevision != _rosterRevision)
                    && peer.Connection.Reliable.TryEnqueue(ReliableEventType.Roster,
                        _rosterPayload.AsSpan(0, _rosterLength), out _))
                {
                    peer.HasRoster = true;
                    peer.RosterRevision = _rosterRevision;
                }
            }
        }

        private void HandleChat(ServerPeer speaker, ReadOnlySpan<byte> request)
        {
            if (!speaker.ChatCredit.Take(_now)) { speaker.ChatDropped++; return; }
            // Reserve every recipient before enqueueing any copy. The one
            // owner thread makes this check and publication indivisible.
            foreach (ServerPeer? peer in _peers)
            {
                if (peer != null && !peer.Connection.Reliable.CanEnqueue)
                {
                    speaker.ChatDropped++;
                    return;
                }
            }
            SessionChatRequest.TryRead(request, out string text);
            Span<byte> payload = stackalloc byte[4 + SessionChatPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, MatchId);
            new SessionChatPacket(speaker.Connection.Id, speaker.Slot, speaker.Name, text).Write(payload[4..]);
            foreach (ServerPeer? peer in _peers)
            {
                peer?.Connection.Reliable.TryEnqueue(ReliableEventType.Chat, payload, out _);
            }
        }

        /// <summary>Called by the simulation owner before loading the next room.</summary>
        public void ChangeMatch(uint matchId, string room, GameMode mode, uint tick)
            => ChangeMatch(matchId, MatchRules.CreateDefault(mode.ToMatchMode(), room, _capacity), tick);

        public void ChangeMatch(uint matchId, MatchRules rules, uint tick)
        {
            string room = rules.RoomKey;
            GameMode mode = rules.Mode.ToLegacyMode();
            if (rules.MaxPlayers != _capacity) { throw new ArgumentException("Rotation cannot change session capacity.", nameof(rules)); }
            if (!Sequence32.IsNewer(matchId, MatchId))
            {
                throw new ArgumentOutOfRangeException(nameof(matchId), "Match identity must advance.");
            }
            if (String.IsNullOrEmpty(room) || room.Length > MatchStatePacket.MaxNameBytes)
            {
                throw new ArgumentException("Room key must fit the protocol field.", nameof(room));
            }
            Span<byte> payload = stackalloc byte[MatchTransitionPacket.Size];
            new MatchTransitionPacket(matchId, tick, rules).Write(payload);
            if (!MatchTransitionPacket.TryRead(payload, out _))
            {
                throw new ArgumentException("Invalid match transition.");
            }
            MatchId = matchId;
            Rules = rules;
            Phase = MatchPhase.WaitingForPlayers;
            PhaseRevision = 0;
            Room = room;
            Mode = mode;
            Tick = tick;
            _rosterDirty = true;
            int teamMember = 0;
            for (int slot = 0; slot < _capacity; slot++)
            {
                ServerPeer? peer = _peers[slot];
                if (peer == null) { continue; }
                peer.TeamIndex = rules.Teams ? (byte)(teamMember++ & 1) : peer.Slot;
                peer.Inputs = new ServerInputStream();
                peer.HasRoster = false;
                peer.Connection.BeginLoading(matchId);
                peer.Connection.Reliable.CancelPendingExceptWelcome();
                if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.MapTransition, payload, out _))
                {
                    Remove(slot); // Explicit backpressure: never start a peer in the wrong room.
                }
            }
        }

        public bool TrySendEvent(ServerPeer peer, ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (Find(peer.Connection.Id) != peer || peer.Connection.State != NetConnectionState.Playing
                || type is not (ReliableEventType.MatchState or ReliableEventType.Combat or ReliableEventType.World)
                || payload.Length > ReliableChannel.MaxPayloadSize - 4) { return false; }
            Span<byte> body = stackalloc byte[ReliableChannel.MaxPayloadSize];
            BinaryPrimitives.WriteUInt32LittleEndian(body, MatchId);
            payload.CopyTo(body[4..]);
            return peer.Connection.Reliable.TryEnqueue(type, body[..(payload.Length + 4)], out _);
        }

        /// <summary>Single-owner all-or-none admission prevents retry duplicates.</summary>
        public bool TryBroadcastEvent(ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (type is not (ReliableEventType.MatchState or ReliableEventType.Combat or ReliableEventType.World)
                || payload.Length > ReliableChannel.MaxPayloadSize - 4) { return false; }
            foreach (ServerPeer? peer in _peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing && !peer.Connection.Reliable.CanEnqueue)
                {
                    return false;
                }
            }
            foreach (ServerPeer? peer in _peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing) { TrySendEvent(peer, type, payload); }
            }
            return true;
        }

        public void SendWorld(ServerPeer peer, ReadOnlySpan<byte> payload)
        {
            if (Find(peer.Connection.Id) == peer
                && peer.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing)
            {
                peer.Connection.Send(_transport, NetMessageType.World, payload);
            }
        }

        private void Refuse(IPEndPoint endpoint, ulong nonce, string reason)
        {
            Span<byte> datagram = stackalloc byte[NetHeader.Size + 8 + 40];
            new NetHeader(NetMessageType.Refused, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(datagram);
            BinaryPrimitives.WriteUInt64LittleEndian(datagram[NetHeader.Size..], nonce);
            NetText.Write(datagram[(NetHeader.Size + 8)..], reason);
            _transport.SendDatagram(endpoint, datagram);
        }

        public ServerPeer? Find(ulong connectionId)
        {
            foreach (ServerPeer? peer in _peers)
            {
                if (peer?.Connection.Id == connectionId)
                {
                    return peer;
                }
            }
            return null;
        }

        public void Remove(int slot)
        {
            ServerPeer? peer = _peers[slot];
            if (peer != null)
            {
                peer.Connection.Disconnect();
                _peers[slot] = null;
                Count--;
                PublishKeepAlives();
                _rosterDirty = true;
            }
        }
    }
}
