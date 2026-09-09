using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using MphRead.Identity;

namespace MphRead.Mods.Network
{
    public sealed class ServerPeer
    {
        public PlayerId? PlayerId { get; internal set; }
        public Guid? GuestSessionId { get; internal set; }
        public byte? ReservedSeat { get; internal set; }
        public bool IsBot { get; internal set; }
        public bool IsObserver => Slot == byte.MaxValue;
        internal byte ConnectionIndex { get; set; }
        internal uint ObserverDelayTicks { get; set; }
        internal System.Collections.Generic.LinkedListNode<ObserverFrame>? ObserverCursor { get; set; }
        internal bool ObserverNeedsBaseline { get; set; }
        internal Guid TicketId { get; set; }
        internal bool TrustedObserver { get; set; }
        public NetConnection Connection { get; }
        public ulong Nonce { get; }
        public byte Slot { get; }
        public byte TeamIndex { get; internal set; }
        public bool WaitingForNextMatch { get; internal set; }
        internal bool ReturningParticipant { get; set; }
        internal ulong ReturningFromConnectionId { get; set; }
        public bool HasParticipated { get; internal set; }
        internal bool SurvivalEliminated { get; set; }
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
    public sealed partial class ServerNetwork
    {
        public Func<ServerPeer, IntermissionVoteRequest, bool>? IntermissionVoteReceived { get; set; }
        private readonly INetTransport _transport;
        private readonly ServerPeer?[] _peers = new ServerPeer?[RosterPacket.MaxSlots];
        private readonly ServerPeer?[] _reconnectPeers = new ServerPeer?[RosterPacket.MaxSlots];
        private readonly uint[] _reconnectTicks = new uint[RosterPacket.MaxSlots];
        internal const uint ReconnectGraceTicks = 30 * 60;

        internal bool HasReconnectReservation(int slot)
        {
            if (_reconnectPeers[slot] != null && unchecked(Tick - _reconnectTicks[slot]) >= ReconnectGraceTicks)
                _reconnectPeers[slot] = null;
            return _reconnectPeers[slot] != null;
        }
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
        public IServerTicketAuthority? TicketAuthority { get; set; }
        public Guid ServerId => TicketAuthority?.ServerId ?? Guid.Empty;
        public Action<ServerPeer, ParticipantExitReason, uint>? ParticipantLeaving { get; set; }
        public ReadOnlySpan<ServerPeer?> Peers => _peers;
        public int Count { get; private set; }
        public long Rejected { get; private set; }

        public ServerNetwork(INetTransport transport, string room, GameMode mode,
            uint matchId = 1, int capacity = RosterPacket.MaxSlots)
            : this(transport, MatchRules.CreateDefault(mode.ToMatchMode(), room, capacity), matchId) { }

        public ServerNetwork(INetTransport transport, MatchRules rules, uint matchId = 1, ObserverOptions? observers = null)
        {
            ObserverConfiguration = observers ?? new();
            ObserverConfiguration.Validate();
            _observerTimeline.BeginMatch(matchId);
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
            if (TicketAuthority != null)
                while (TicketAuthority.TryRead(out ValidatedTicketJoin completed))
                {
                    if (completed.Identity is TicketIdentity identity && identity.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        Admit(completed.Endpoint, completed.Join, identity);
                    else Refuse(completed.Endpoint, completed.Join.Nonce, "Game ticket rejected.");
                }
            ProcessBotAdmissions();
            PublishRoster();
            Span<byte> ping = stackalloc byte[8];
            for (int slot = 0; slot < _connections.Length; slot++)
            {
                ServerPeer? peer = _connections[slot];
                if (peer == null) continue;
                NetConnection connection = peer.Connection;
                if (_now - connection.LastReceived > NetConfig.TimeoutSeconds)
                {
                    Remove(slot, reason: ParticipantExitReason.Timeout);
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
                    RulesetPreset = Rules.RulesetPreset, RankingEligibility = Rules.RankingEligibility,
                    Observers = (byte)ObserverCount, MaxObservers = (byte)ObserverConfiguration.MaxSpectators,
                    ObserverDelaySeconds = (byte)ObserverConfiguration.DelaySeconds,
                    Bots = CountRosterBots(),
                    ServerId = ServerId, RequiresTicket = TicketAuthority?.RequireTickets == true,
                    HasRules = (bytes[1] & ServerStatusPacket.RulesCapability) != 0,
                    FriendlyFire = Rules.FriendlyFire, PlayerRadar = Rules.PlayerRadar, SpawnPolicy = Rules.SpawnPolicy,
                    OvertimePolicy = Rules.OvertimePolicy, LateJoinPolicy = Rules.LateJoinPolicy,
                    MaxPlayers = (byte)_capacity, Protocol = NetHeader.Version, Family = NetWireIdentity.Family, ServerName = ServerName
                };
                Span<byte> reply = stackalloc byte[status.HasRules ? ServerStatusPacket.ExtendedSize : ServerStatusPacket.Size];
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
                    && SubmitJoin(packet.Sender, join);
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
                NetMessageType.Input => !peer.IsObserver && InputBundle.TryRead(body, commands, out uint inputMatch, out uint inputPhase, out commandCount)
                    && inputMatch == MatchId && peer.Connection.State == NetConnectionState.Playing
                    && Phase == MatchPhase.Playing && inputPhase == PhaseRevision,
                NetMessageType.Event => ReliableEventPacket.TryRead(body, out eventId, out eventType, out eventBody)
                    && (eventType == ReliableEventType.ClientReady && eventBody.Length == 4
                        || eventType == ReliableEventType.Disconnect && eventBody.IsEmpty
                        || !peer.IsObserver && !peer.IsBot && eventType == ReliableEventType.IntermissionVote
                            && IntermissionVoteRequest.TryRead(eventBody, out _)
                        || !peer.IsObserver && eventType == ReliableEventType.ChatRequest && eventBody.Length == 4 + SessionChatRequest.Size
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
                    if (eventType == ReliableEventType.Disconnect) { Remove(peer.ConnectionIndex, reason: ParticipantExitReason.ExplicitLeave); }
                    else if (eventType == ReliableEventType.ClientReady)
                    {
                        bool ready = connection.Ready(BinaryPrimitives.ReadUInt32LittleEndian(eventBody));
                        _rosterDirty |= ready && !peer.IsObserver;
                        if (ready && peer.IsObserver) { connection.StartPlaying(); peer.ObserverNeedsBaseline = true; }
                    }
                    else if (eventType == ReliableEventType.IntermissionVote)
                    {
                        if (IntermissionVoteRequest.TryRead(eventBody, out var vote)) IntermissionVoteReceived?.Invoke(peer, vote);
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
                BinaryPrimitives.WriteUInt32LittleEndian(reply[8..], peer.IsObserver ? peer.ObserverCursor?.Value.Tick ?? 0 : Tick);
                connection.Send(_transport, NetMessageType.Pong, reply);
            }
            return true;
        }

        public bool AdmissionClosed { get; set; }
        public bool RequireRoutedJoins { get; set; }
        public Func<JoinPacket, TicketIdentity, bool>? AdmissionIdentityPolicy { get; set; }
        private bool MatchesJoinRoute(in JoinPacket join) => (!RequireRoutedJoins || join.WireMatchId != 0)
            && (join.WireMatchId == 0 || join.WireMatchId == MatchId);
        private static bool SameAuthenticatedIdentity(ServerPeer peer, TicketIdentity? identity)
        {
            if (identity?.PlayerId is PlayerId player) return peer.PlayerId == player && !peer.GuestSessionId.HasValue;
            if (identity?.GuestSessionId is Guid guest) return peer.GuestSessionId == guest && !peer.PlayerId.HasValue;
            return !identity.HasValue && !peer.PlayerId.HasValue && !peer.GuestSessionId.HasValue;
        }
        public Func<int, bool>? CanClaimPlayerSlot { get; set; }
        public Func<int, NetRosterEntry?>? BotRosterEntry { get; set; }
        public Action<int, byte>? BotTeamAssigned { get; set; }
        public void InvalidateRoster() => _rosterDirty = true;

        private bool SubmitJoin(IPEndPoint endpoint, in JoinPacket join)
        {
            if (!MatchesJoinRoute(join)) return false;
            if (RetryBotAdmission(endpoint, join)) return true;
            if (join.Protocol != NetHeader.Version) return Admit(endpoint, join);
            if (!string.IsNullOrEmpty(join.Ticket))
            {
                if (TicketAuthority != null && TicketAuthority.Submit(endpoint, join)) return true;
                Refuse(endpoint, join.Nonce, "Ticket authentication unavailable or busy.");
                return true;
            }
            if (TicketAuthority?.RequireTickets == true)
            {
                Refuse(endpoint, join.Nonce, "This server requires a game ticket.");
                return true;
            }
            return Admit(endpoint, join);
        }

        private bool Admit(IPEndPoint endpoint, in JoinPacket join, TicketIdentity? authenticated = null)
        {
            if (!MatchesJoinRoute(join)) return false;
            if (RequireRoutedJoins && (authenticated is not { WorkerAdmission: true, ReservedSeat: not null }
                || AdmissionIdentityPolicy != null && !AdmissionIdentityPolicy(join, authenticated.Value))) return false;
            byte? reservedSeat = authenticated?.ReservedSeat;
            if (!join.Observer && reservedSeat >= _capacity) return false;
            if (RetryBotAdmission(endpoint, join)) return true;
            if (AdmissionClosed) { return false; }
            if (join.Protocol != NetHeader.Version)
            {
                Refuse(endpoint, join.Nonce, $"Authoritative protocol {NetHeader.Version} required.");
                return true;
            }
            if (join.Observer) return AdmitObserver(endpoint, join, authenticated);
            foreach (ServerPeer? observer in _observers)
                if (observer != null && (observer.Connection.Endpoint.Equals(endpoint) || observer.Nonce == join.Nonce)) return false;
            int free = -1;
            bool returningParticipant = false;
            ulong returningFrom = 0;
            byte? returningTeam = null;
            bool returningEliminated = false;
            for (int slot = 0; slot < _capacity; slot++)
            {
                if (reservedSeat.HasValue && slot != reservedSeat.Value) continue;
                ServerPeer? peer = _peers[slot];
                if (peer == null)
                {
                    if (HasReconnectReservation(slot))
                    {
                        ServerPeer previous = _reconnectPeers[slot]!;
                        bool accountMatch = authenticated.HasValue && SameAuthenticatedIdentity(previous, authenticated);
                        if (!accountMatch && previous.Connection.Id != join.PreviousConnectionId) continue;
                        if ((previous.PlayerId.HasValue || previous.GuestSessionId.HasValue) ? !accountMatch
                            : authenticated.HasValue || !previous.Connection.Endpoint.Equals(endpoint) || previous.Name != join.Name) return false;
                        if (previous.Hunter != join.Hunter || authenticated.HasValue && previous.TicketId == authenticated.Value.TicketId) return false;
                        free = slot;
                        returningParticipant = true;
                        returningFrom = previous.Connection.Id;
                        returningTeam = previous.TeamIndex;
                        returningEliminated = previous.SurvivalEliminated;
                        break;
                    }
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
                    return peer.Connection.Endpoint.Equals(endpoint) && SameAuthenticatedIdentity(peer, authenticated)
                        && (!authenticated.HasValue || peer.TicketId == authenticated.Value.TicketId);
                }
                bool sameAccount = authenticated.HasValue && SameAuthenticatedIdentity(peer, authenticated);
                if (sameAccount || peer.Connection.Id == join.PreviousConnectionId)
                {
                    // A public roster identity alone is not reconnect proof.
                    if ((peer.PlayerId.HasValue || peer.GuestSessionId.HasValue) ? !sameAccount
                        : authenticated.HasValue || !peer.Connection.Endpoint.Equals(endpoint) || peer.Name != join.Name) return false;
                    if (peer.Hunter != join.Hunter || authenticated.HasValue && peer.TicketId == authenticated.Value.TicketId) return false;
                    returningParticipant = peer.HasParticipated;
                    returningFrom = peer.Connection.Id;
                    returningTeam = returningParticipant ? peer.TeamIndex : null;
                    returningEliminated = returningParticipant && peer.SurvivalEliminated;
                    free = slot;
                    Remove(slot, allowReconnect: false, reason: ParticipantExitReason.Replaced);
                    break;
                }
                if (peer.Connection.Endpoint.Equals(endpoint))
                {
                    return false; // a stale initial Join cannot evict a peer
                }
            }
            bool duelInProgress = Phase is MatchPhase.Playing or MatchPhase.Ending or MatchPhase.Intermission;
            if (Rules.RulesetPreset == RulesetPreset.Duel && returningFrom == 0 && (free < 0 || duelInProgress))
            {
                if (authenticated?.WorkerAdmission == true) { Refuse(endpoint, join.Nonce, "Reserved player admission is unavailable."); return true; }
                return AdmitObserver(endpoint, join with { Observer = true }, authenticated);
            }
            if (free < 0)
            {
                Refuse(endpoint, join.Nonce, "Server is full.");
                return true;
            }
            bool inProgress = Phase is MatchPhase.Playing or MatchPhase.Ending or MatchPhase.Intermission;
            if (AdminRosterLocked && returningFrom == 0) { Refuse(endpoint, join.Nonce, "The player roster is locked."); return true; }
            if (inProgress && !returningParticipant && Rules.LateJoinPolicy == LateJoinPolicy.Disabled)
            {
                Refuse(endpoint, join.Nonce, "Joining is disabled until the next match.");
                return true;
            }
            if (!returningParticipant && CanClaimPlayerSlot != null)
            {
                free = -1;
                int botCandidate = -1;
                for (int candidate = 0; candidate < _capacity; candidate++)
                {
                    if (reservedSeat.HasValue && candidate != reservedSeat.Value) continue;
                    if (_peers[candidate] != null || HasReconnectReservation(candidate) || HasPendingBotAdmission(candidate)) continue;
                    if (BotRosterEntry?.Invoke(candidate) == null) { free = candidate; break; }
                    if (botCandidate < 0) botCandidate = candidate;
                }
                if (free < 0 && botCandidate >= 0)
                {
                    if (CanClaimPlayerSlot(botCandidate)) free = botCandidate;
                    else return QueueBotAdmission(endpoint, join, authenticated, botCandidate);
                }
                if (free < 0) { Refuse(endpoint, join.Nonce, "Player slots are not available yet."); return true; }
            }
            ulong id = AllocateConnectionIdentity();
            var connection = new NetConnection(id, endpoint, MatchId, _now);
            var accepted = new JoinAcceptedPacket(join.Nonce, (byte)free, MatchId, Tick, 60, Rules);
            Span<byte> payload = stackalloc byte[JoinAcceptedPacket.Size];
            accepted.Write(payload);
            connection.Reliable.TryEnqueue(ReliableEventType.Welcome, payload, out _);
            _peers[free] = new ServerPeer(connection, join, (byte)free, _now)
            {
                ConnectionIndex = (byte)free,
                PlayerId = authenticated?.PlayerId,
                GuestSessionId = authenticated?.GuestSessionId,
                ReservedSeat = authenticated?.ReservedSeat,
                TicketId = authenticated?.TicketId ?? Guid.Empty,
                TrustedObserver = authenticated?.TrustedObserver == true,
                TeamIndex = Rules.Teams ? authenticated?.ReservedTeam ?? returningTeam ?? SelectJoiningTeam((byte)free) : (byte)free,
                ReturningParticipant = returningParticipant,
                ReturningFromConnectionId = returningParticipant ? returningFrom : 0,
                HasParticipated = returningParticipant,
                SurvivalEliminated = returningEliminated,
                WaitingForNextMatch = inProgress && !returningParticipant
                    && Rules.LateJoinPolicy == LateJoinPolicy.SpectateUntilNextMatch
            };
            _connections[free] = _peers[free];
            _reconnectPeers[free] = null;
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
            var keepAlives = new NetKeepAlive[Count + ObserverCount];
            int count = 0;
            foreach (ServerPeer? peer in _connections)
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
                if (peer != null && !peer.WaitingForNextMatch) teams[peer.Slot] = peer.TeamIndex;
            for (int candidate = 0; candidate < _capacity; candidate++)
                if (_peers[candidate] == null && BotRosterEntry?.Invoke(candidate) is NetRosterEntry bot)
                    teams[candidate] = bot.Team;
            return TeamAllocator.Select(teams, (byte)(slot & 1));
        }

        public bool RebalanceBeforeStart()
        {
            if (!Rules.Teams || AdminTeamsAssigned || Rules.TeamBalancePolicy == TeamBalancePolicy.Locked || Phase is not (MatchPhase.WaitingForPlayers or MatchPhase.Countdown)) return false;
            Span<byte> teams = stackalloc byte[8];
            teams.Fill(TeamAllocator.Unassigned);
            foreach (ServerPeer? peer in _peers)
                if (peer is { WaitingForNextMatch: false } && peer.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing)
                    teams[peer.Slot] = peer.TeamIndex;
            for (int slot = 0; slot < _capacity; slot++)
                if (_peers[slot] == null && BotRosterEntry?.Invoke(slot) is NetRosterEntry bot)
                {
                    // A combined plan is atomic: do not move humans if its bot
                    // assignments cannot also be applied by the simulation owner.
                    if (BotTeamAssigned == null) return false;
                    teams[slot] = bot.Team;
                }
            if (TeamAllocator.Rebalance(teams) == 0) return false;
            foreach (ServerPeer? peer in _peers)
                if (peer != null && teams[peer.Slot] != TeamAllocator.Unassigned)
                    peer.TeamIndex = teams[peer.Slot];
            for (int slot = 0; slot < _capacity; slot++)
                if (_peers[slot] == null && teams[slot] != TeamAllocator.Unassigned)
                    BotTeamAssigned!(slot, teams[slot]);
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
                if (BotRosterEntry != null)
                    for (int slot = 0; slot < _capacity; slot++)
                        if (_peers[slot] == null && BotRosterEntry(slot) is NetRosterEntry bot)
                            _rosterEntries[count++] = bot;
                _rosterRevision++;
                BinaryPrimitives.WriteUInt32LittleEndian(_rosterPayload, MatchId);
                _rosterLength = 4 + SessionRosterPacket.Write(_rosterPayload.AsSpan(4), _rosterRevision,
                    _rosterEntries.AsSpan(0, count));
                Array.Clear(_rosterEntries, count, _rosterEntries.Length - count);
                _observerTimeline.Roster(_rosterPayload.AsSpan(0, _rosterLength), _rosterRevision);
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
            if (IsAdminMuted(speaker.Connection.Id) || !speaker.ChatCredit.Take(_now)) { speaker.ChatDropped++; return; }
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
                peer?.Connection.Reliable.TryEnqueue(ReliableEventType.Chat, payload, out _);
            if (ObserverFramesRequired) _observerTimeline.Event(ReliableEventType.Chat, payload);
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
            _observerTimeline.BeginMatch(matchId);
            Array.Clear(_reconnectPeers);
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
                if (!AdminTeamsAssigned) peer.TeamIndex = rules.Teams ? (byte)(teamMember++ & 1) : peer.Slot;
                peer.WaitingForNextMatch = false;
                peer.ReturningParticipant = false;
                peer.ReturningFromConnectionId = 0;
                peer.HasParticipated = false;
                peer.SurvivalEliminated = false;
                peer.Inputs = new ServerInputStream();
                peer.HasRoster = false;
                peer.Connection.BeginLoading(matchId);
                peer.Connection.Reliable.CancelPendingExceptWelcome();
                if (!peer.Connection.Reliable.TryEnqueue(ReliableEventType.MapTransition, payload, out _))
                {
                    Remove(slot, reason: ParticipantExitReason.Backpressure); // Explicit backpressure: never start a peer in the wrong room.
                }
            }
        }

        public bool TrySendEvent(ServerPeer peer, ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (Find(peer.Connection.Id) != peer || peer.Connection.State != NetConnectionState.Playing
                || type is not (ReliableEventType.MatchState or ReliableEventType.Combat or ReliableEventType.World or ReliableEventType.Kill or ReliableEventType.WorldEvent or ReliableEventType.MatchAward or ReliableEventType.MatchSemantic)
                || payload.Length > ReliableChannel.MaxPayloadSize - 4) { return false; }
            Span<byte> body = stackalloc byte[ReliableChannel.MaxPayloadSize];
            BinaryPrimitives.WriteUInt32LittleEndian(body, MatchId);
            payload.CopyTo(body[4..]);
            return peer.Connection.Reliable.TryEnqueue(type, body[..(payload.Length + 4)], out _);
        }

        /// <summary>Single-owner all-or-none admission prevents retry duplicates.</summary>
        public bool TryBroadcastEvent(ReliableEventType type, ReadOnlySpan<byte> payload)
        {
            if (type is not (ReliableEventType.MatchState or ReliableEventType.Combat or ReliableEventType.World or ReliableEventType.Kill or ReliableEventType.WorldEvent or ReliableEventType.MatchAward or ReliableEventType.MatchSemantic)
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

        /// <summary>
        /// Sends a bounded server-selected diagnostic. There is intentionally
        /// no corresponding client-to-server message type; only the
        /// authenticated Node admin lane can reach this method.
        /// </summary>
        public bool TrySendDebug(ServerPeer peer, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > NetConfig.MaxPacketSize - NetHeader.Size
                || Find(peer.Connection.Id) != peer
                || peer.IsObserver
                || peer.Connection.State is not (NetConnectionState.Ready or NetConnectionState.Playing))
                return false;
            peer.Connection.Send(_transport, NetMessageType.Debug, payload);
            return true;
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
            foreach (ServerPeer? peer in _connections)
            {
                if (peer?.Connection.Id == connectionId)
                {
                    return peer;
                }
            }
            return null;
        }

        private ulong AllocateConnectionIdentity()
        {
            if (_transport is IMatchConnectionRoutes routes) return routes.AllocateConnectionId();
            ulong id;
            do { id = NetConnection.NewIdentity(); } while (Find(id) != null);
            return id;
        }

        public void Remove(int slot, bool allowReconnect = true, ParticipantExitReason reason = ParticipantExitReason.Disconnected)
        {
            if (slot >= 8) { RemoveObserver(slot); return; }
            ServerPeer? peer = _peers[slot];
            if (peer != null)
            {
                if (allowReconnect && peer.HasParticipated && Phase is MatchPhase.Playing or MatchPhase.Ending or MatchPhase.Intermission)
                {
                    _reconnectPeers[slot] = peer;
                    _reconnectTicks[slot] = Tick;
                }
                _adminMuted.Remove(peer.Connection.Id);
                ParticipantLeaving?.Invoke(peer, reason, Tick);
                (_transport as IMatchConnectionRoutes)?.RemoveConnection(peer.Connection.Id);
                peer.Connection.Disconnect();
                _peers[slot] = null;
                _connections[slot] = null;
                Count--;
                PublishKeepAlives();
                _rosterDirty = true;
            }
        }
    }
}
