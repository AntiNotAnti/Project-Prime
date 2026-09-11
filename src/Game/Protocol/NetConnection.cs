using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace MphRead.Mods.Network
{
    public enum NetConnectionState
    {
        Connecting,
        Loading,
        Ready,
        Playing,
        Disconnecting
    }

    /// <summary>
    /// Protocol state owned by one connection consumer, never a socket callback.
    /// Identity is random per admission. An authenticated-by-session-ID newer
    /// sequenced datagram can update routing; stale packets and keepalives cannot.
    /// Connection IDs are bearer session identifiers, not encrypted transport.
    /// </summary>
    public sealed class NetConnection
    {
        /// <summary>
        /// Opaque result of a successful MAC verification. The application may
        /// inspect the signed header/body, validate its message-specific body,
        /// and then pass this token to <see cref="ApplyVerified"/>. Callers
        /// cannot manufacture a verified token for another connection.
        /// </summary>
        public readonly ref struct VerifiedPacket
        {
            private readonly NetConnection? _owner;
            private readonly ReadOnlySpan<byte> _payload;

            internal VerifiedPacket(NetConnection owner, NetHeader header,
                ReadOnlySpan<byte> payload)
            {
                _owner = owner;
                Header = header;
                _payload = payload;
            }

            public NetHeader Header { get; }
            public ReadOnlySpan<byte> Payload => _payload;
            internal bool BelongsTo(NetConnection owner) => ReferenceEquals(_owner, owner);
        }

        private ReceiveWindow _received;
        private uint _nextSequence;
        private bool _sequenceExhausted;
        private readonly byte[]? _authKey;
        private readonly NetAuthDirection _authDirection;
        private readonly NetAuthDirection _receiveAuthDirection;
        private ulong _nextKeepAliveCounter = 1;
        private bool _hasKeepAliveCounter;
        private ulong _lastKeepAliveCounter;
        private readonly bool _ackCoalescingEnabled;
        private bool _ackPending;
        private uint _ackDueTick;
        private bool _ackDueTickValid;
        private double _ackDueAt;
        public ulong Id { get; }
        public IPEndPoint Endpoint { get; private set; }
        public NetConnectionState State { get; private set; } = NetConnectionState.Loading;
        public uint MatchId { get; private set; }
        public double LastReceived { get; private set; }
        public ReliableChannel Reliable { get; }
        public NetMetrics Metrics { get; } = new();
        public bool IsAuthenticated => _authKey != null;
        public bool IsSequenceExhausted => _sequenceExhausted;
        public NetAuthDirection AuthDirection => _authDirection;
        public NetAuthDirection ReceiveAuthDirection => _receiveAuthDirection;
        public bool AckCoalescingEnabled => _ackCoalescingEnabled;
        internal bool AckPending => _ackPending;
        internal uint AckDueTick => _ackDueTick;

        /// <summary>
        /// Legacy unkeyed compatibility seam for bootstrap and isolated tests.
        /// Established production connections must use the keyed overload;
        /// this overload does not provide a runtime authentication downgrade.
        /// </summary>
        public NetConnection(ulong id, IPEndPoint endpoint, uint matchId, double now,
            bool adaptiveReliableRto = true, bool ackCoalescingEnabled = false)
            : this(id, endpoint, matchId, now, ReadOnlySpan<byte>.Empty,
                NetAuthDirection.ClientToServer, adaptiveReliableRto, ackCoalescingEnabled) { }

        /// <summary>
        /// Creates an established connection with an owned copy of its UDP
        /// key. Join/bootstrap code is intentionally outside this primitive.
        /// </summary>
        public NetConnection(ulong id, IPEndPoint endpoint, uint matchId, double now,
            ReadOnlySpan<byte> authKey, NetAuthDirection authDirection,
            bool adaptiveReliableRto = true, bool ackCoalescingEnabled = false)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!Double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (!authKey.IsEmpty && authKey.Length != NetAuthentication.KeySize)
                throw new ArgumentException("UDP authentication keys must be 32 bytes.", nameof(authKey));
            if (authKey.IsEmpty && authDirection is not (NetAuthDirection.ClientToServer or NetAuthDirection.ServerToClient))
                throw new ArgumentOutOfRangeException(nameof(authDirection));
            if (!authKey.IsEmpty && authDirection is not (NetAuthDirection.ClientToServer or NetAuthDirection.ServerToClient))
                throw new ArgumentOutOfRangeException(nameof(authDirection));
            Id = id;
            Endpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
            MatchId = matchId;
            LastReceived = now;
            _authKey = authKey.IsEmpty ? null : authKey.ToArray();
            _authDirection = authDirection;
            _receiveAuthDirection = authDirection == NetAuthDirection.ClientToServer
                ? NetAuthDirection.ServerToClient
                : NetAuthDirection.ClientToServer;
            _ackCoalescingEnabled = ackCoalescingEnabled;
            Reliable = new ReliableChannel(adaptiveRetryEnabled: adaptiveReliableRto);
        }

        public static ulong NewIdentity()
        {
            Span<byte> bytes = stackalloc byte[8];
            ulong id;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                id = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            }
            while (id == 0);
            return id;
        }

        public NetHeader CreateHeader(NetMessageType type)
        {
            if (_authKey != null && (_sequenceExhausted || _nextSequence == uint.MaxValue))
            {
                RetireAuthenticatedSequence();
                throw new InvalidOperationException("Authenticated UDP sequence space is exhausted; retire the connection.");
            }
            NetHeaderFlags flags = _received.HasReceived ? NetHeaderFlags.HasAck : NetHeaderFlags.None;
            return new NetHeader(type, flags, Id, _nextSequence++, _received.Ack, _received.AckBits);
        }

        /// <summary>
        /// Begins one ACK generation for a reliable packet that was accepted
        /// by the application. The deadline is fixed to the next simulation
        /// tick and is never extended by duplicates in that tick.
        /// </summary>
        internal void RequestAck(uint simulationTick)
        {
            if (!_ackCoalescingEnabled || !_received.HasReceived || _ackPending)
            {
                return;
            }
            _ackPending = true;
            _ackDueTick = unchecked(simulationTick + 1);
            _ackDueTickValid = true;
            _ackDueAt = Double.NaN;
        }

        /// <summary>
        /// Monotonic-time deadline seam for clients whose polling cadence can
        /// differ from the 60 Hz simulation. Server owners use the tick-aware
        /// overload below and also retain this timestamp as a hard bound.
        /// </summary>
        internal void RequestAck(double now)
        {
            if (!_ackCoalescingEnabled || !_received.HasReceived || _ackPending
                || !Double.IsFinite(now))
            {
                return;
            }
            _ackPending = true;
            _ackDueTick = 0;
            _ackDueTickValid = false;
            _ackDueAt = now + NetConfig.SimulationTickSeconds;
        }

        internal void RequestAck(uint simulationTick, double now)
        {
            if (!_ackCoalescingEnabled || !_received.HasReceived || _ackPending)
            {
                return;
            }
            _ackPending = true;
            _ackDueTick = unchecked(simulationTick + 1);
            _ackDueTickValid = true;
            _ackDueAt = Double.IsFinite(now)
                ? now + NetConfig.SimulationTickSeconds : Double.NaN;
        }

        /// <summary>Whether the current tick has reached a pending ACK deadline.</summary>
        internal bool IsAckDue(uint simulationTick)
            => _ackPending && _ackDueTickValid && (simulationTick == _ackDueTick
                || Sequence32.IsNewer(simulationTick, _ackDueTick));

        /// <summary>
        /// Emits the standalone ACK at or after its hard deadline. A legacy
        /// void sink cannot prove queue acceptance, so this remains the
        /// fallback even when a prior carrier included the same ACK window.
        /// Optional accepted sinks may request another deadline after a
        /// rejected standalone submission.
        /// </summary>
        internal bool FlushPendingAck(INetDatagramSink transport, uint simulationTick,
            double now, bool force = false)
        {
            bool timeDue = Double.IsFinite(_ackDueAt) && now >= _ackDueAt;
            bool deadlineDue = timeDue || IsAckDue(simulationTick);
            if (!_ackCoalescingEnabled || !_ackPending || !_received.HasReceived
                || !Double.IsFinite(now) || !force && !deadlineDue)
            {
                return false;
            }
            bool accepted;
            try
            {
                accepted = SendCore(transport, NetMessageType.Ack, ReadOnlySpan<byte>.Empty);
            }
            catch (InvalidOperationException)
            {
                // Authenticated sequence retirement is terminal. Keep the
                // pending generation observable rather than claiming an ACK
                // was emitted after the sequence space closed.
                return false;
            }
            Metrics.StandaloneAckAttempt();
            if (accepted) Metrics.StandaloneAckAccepted();
            if (deadlineDue) Metrics.AckDeadlineExpired();
            if (transport is IAcceptedNetDatagramSink && !accepted)
            {
                _ackDueTick = unchecked(simulationTick + 1);
                _ackDueTickValid = true;
                _ackDueAt = now + NetConfig.SimulationTickSeconds;
            }
            else
            {
                _ackPending = false;
                _ackDueTickValid = false;
            }
            return true;
        }

        /// <summary>Deterministic boundary seam for protocol tests.</summary>
        internal void SetNextSequenceForTesting(uint nextSequence)
        {
            _nextSequence = nextSequence;
            _sequenceExhausted = false;
        }

        private void RetireAuthenticatedSequence()
        {
            _sequenceExhausted = true;
            State = NetConnectionState.Disconnecting;
            RetireAuthenticationKey();
        }

        /// <summary>
        /// The caller must validate the full body before passing its header
        /// here. Duplicates still process ACKs but never deliver gameplay twice.
        /// This is the legacy unkeyed compatibility path; established
        /// connections must use TryVerify followed by ApplyVerified.
        /// </summary>
        public bool TryReceive(in NetHeader header, IPEndPoint sender, double now, out ReceiveResult result)
        {
            result = ReceiveResult.TooOld;
            if (_authKey != null || header.ConnectionId != Id || State == NetConnectionState.Disconnecting
                || !Double.IsFinite(now) || now < LastReceived)
            {
                return false;
            }
            if ((header.Flags & NetHeaderFlags.Unsequenced) != 0)
            {
                if (header.Type != NetMessageType.KeepAlive || !sender.Equals(Endpoint))
                {
                    return false;
                }
                LastReceived = now;
                return true;
            }
            if (!sender.Equals(Endpoint))
            {
                if (_received.HasReceived && !Sequence32.IsNewer(header.Sequence, _received.Ack))
                {
                    return false;
                }
                Endpoint = sender;
            }
            result = _received.Record(header.Sequence);
            if (result == ReceiveResult.TooOld)
            {
                return false;
            }
            if ((header.Flags & NetHeaderFlags.HasAck) != 0)
            {
                Reliable.Acknowledge(header.Ack, header.AckBits);
            }
            LastReceived = now;
            return true;
        }

        public void BeginLoading(uint matchId)
        {
            if (State == NetConnectionState.Disconnecting)
            {
                return;
            }
            MatchId = matchId;
            State = NetConnectionState.Loading;
        }

        public bool Ready(uint matchId)
        {
            if (matchId != MatchId || State != NetConnectionState.Loading)
            {
                return false;
            }
            State = NetConnectionState.Ready;
            return true;
        }

        public bool StartPlaying()
        {
            if (State != NetConnectionState.Ready)
            {
                return false;
            }
            State = NetConnectionState.Playing;
            return true;
        }

        public void Disconnect()
        {
            State = NetConnectionState.Disconnecting;
            RetireAuthenticationKey();
        }

        private void RetireAuthenticationKey()
        {
            if (_authKey != null) CryptographicOperations.ZeroMemory(_authKey);
        }

        public void Send(INetDatagramSink transport, NetMessageType type, ReadOnlySpan<byte> payload = default)
        {
            SendCore(transport, type, payload);
        }

        private bool SendCore(INetDatagramSink transport, NetMessageType type,
            ReadOnlySpan<byte> payload)
        {
            Span<byte> datagram = stackalloc byte[NetConfig.MaxPacketSize];
            int maximumPayload = _authKey == null
                ? datagram.Length - NetHeader.Size : NetAuthentication.MaximumPayloadSize;
            if (payload.Length > maximumPayload)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            NetHeader header = CreateHeader(type);
            int length;
            if (_authKey is { } key)
            {
                length = NetAuthentication.Sign(key, _authDirection, header, payload, datagram);
            }
            else
            {
                header.Write(datagram);
                payload.CopyTo(datagram[NetHeader.Size..]);
                length = NetHeader.Size + payload.Length;
            }
            NetDeliveryClass delivery = DeliveryClassFor(type);
            bool accepted = SubmitDatagram(transport, datagram[..length], delivery);
            if (type != NetMessageType.Ack && _ackCoalescingEnabled
                && _ackPending && (header.Flags & NetHeaderFlags.HasAck) != 0)
            {
                // This is an observed carrier, not proof of delivery. Legacy
                // void sinks therefore leave the pending generation alive for
                // the standalone deadline below.
                Metrics.PiggybackAckAttempt();
                if (accepted)
                {
                    Metrics.PiggybackAckAccepted();
                    _ackPending = false;
                    _ackDueTickValid = false;
                }
            }
            return accepted;
        }

        public void FlushReliable(INetDatagramSink transport, double now)
        {
            if (_authKey != null && (_sequenceExhausted || _nextSequence == uint.MaxValue))
            {
                RetireAuthenticatedSequence();
                return;
            }
            Reliable.ConfigureRetry(Metrics.SmoothedRttMs, Metrics.JitterMs);
            Span<byte> datagram = stackalloc byte[NetConfig.MaxPacketSize];
            for (int budget = 0; budget < 8
                && Reliable.TryGetDue(now, out uint id, out ReliableEventType type, out ReadOnlyMemory<byte> payload);
                budget++)
            {
                NetHeader header = CreateHeader(type == ReliableEventType.Welcome
                    ? NetMessageType.Accepted : NetMessageType.Event);
                int bodyLength = ReliableEventPacket.Write(datagram[NetHeader.Size..], id, type, payload.Span);
                int length;
                if (_authKey is { } key)
                {
                    // The reliable body was written into the payload area;
                    // signing copies the exact body into the final envelope.
                    Span<byte> body = datagram.Slice(NetHeader.Size, bodyLength);
                    length = NetAuthentication.Sign(key, _authDirection, header, body,
                        datagram);
                }
                else
                {
                    header.Write(datagram);
                    length = NetHeader.Size + bodyLength;
                }
                NetDeliveryClass delivery = ReliableEventPolicy.IsCritical(type)
                    ? NetDeliveryClass.Critical : NetDeliveryClass.Reliable;
                bool accepted = SubmitDatagram(transport, datagram[..length], delivery);
                if (_ackCoalescingEnabled && _ackPending
                    && (header.Flags & NetHeaderFlags.HasAck) != 0)
                {
                    Metrics.PiggybackAckAttempt();
                    if (accepted)
                    {
                        Metrics.PiggybackAckAccepted();
                        _ackPending = false;
                        _ackDueTickValid = false;
                    }
                }
                Reliable.MarkSent(id, header.Sequence, now);
            }
        }

        private bool SubmitDatagram(INetDatagramSink transport,
            ReadOnlySpan<byte> datagram, NetDeliveryClass delivery)
        {
            if (transport is IAcceptedNetDatagramSink accepted)
            {
                return accepted.TrySendDatagram(Endpoint, datagram, delivery);
            }
            transport.SendDatagram(Endpoint, datagram, delivery);
            return false;
        }

        private static NetDeliveryClass DeliveryClassFor(NetMessageType type)
            => type switch
            {
                NetMessageType.Accepted or NetMessageType.Refused or NetMessageType.JoinPending
                    or NetMessageType.Ack => NetDeliveryClass.Critical,
                NetMessageType.Event => NetDeliveryClass.Reliable,
                NetMessageType.Input or NetMessageType.Snapshot => NetDeliveryClass.State,
                NetMessageType.World => NetDeliveryClass.World,
                NetMessageType.Debug or NetMessageType.TimingTelemetry
                    or NetMessageType.Ping or NetMessageType.Pong or NetMessageType.KeepAlive
                    => NetDeliveryClass.BestEffort,
                _ => NetDeliveryClass.Auto
            };

        /// <summary>
        /// Verifies the envelope without changing receive windows, endpoint,
        /// liveness, reliable acknowledgements, or gameplay state. The opaque
        /// result must be body-validated before it is applied.
        /// </summary>
        public bool TryVerify(ReadOnlySpan<byte> datagram, IPEndPoint sender,
            out VerifiedPacket packet)
        {
            ArgumentNullException.ThrowIfNull(sender);
            packet = default;
            if (_authKey is not { } key
                || !NetAuthentication.TryVerify(key, _receiveAuthDirection, datagram,
                    out NetHeader header, out ReadOnlySpan<byte> payload)
                || header.ConnectionId != Id)
            {
                Metrics.AuthenticationFailed(endpointChanged: false);
                if (!sender.Equals(Endpoint)) Metrics.UnauthenticatedRebindAttempt();
                return false;
            }
            Metrics.Authenticated();
            packet = new VerifiedPacket(this, header, payload);
            return true;
        }

        /// <summary>
        /// Applies a successfully authenticated packet after the caller has
        /// validated its message-specific body. A default or foreign token is
        /// rejected before any receive-window, endpoint, ACK or liveness state
        /// can change.
        /// </summary>
        public bool ApplyVerified(in VerifiedPacket packet, IPEndPoint sender,
            double now, out ReceiveResult result)
        {
            if (!packet.BelongsTo(this))
            {
                result = ReceiveResult.TooOld;
                return false;
            }
            return TryReceiveVerifiedCore(packet.Header, packet.Payload, sender, now, out result);
        }

        /// <summary>
        /// Applies a packet only after the caller has authenticated it and
        /// validated its message body. Replay rejection happens before any
        /// ACK, endpoint, reliable or liveness mutation.
        /// </summary>
        private bool TryReceiveVerifiedCore(in NetHeader header, ReadOnlySpan<byte> payload,
            IPEndPoint sender, double now, out ReceiveResult result)
        {
            result = ReceiveResult.TooOld;
            if (_authKey == null || header.ConnectionId != Id
                || State == NetConnectionState.Disconnecting
                || !Double.IsFinite(now) || now < LastReceived)
                return false;
            ArgumentNullException.ThrowIfNull(sender);
            if ((header.Flags & NetHeaderFlags.Unsequenced) != 0)
            {
                if (header.Type != NetMessageType.KeepAlive || payload.Length != NetAuthentication.CounterSize
                    || !sender.Equals(Endpoint))
                    return false;
                ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(payload);
                if (counter == 0 || _hasKeepAliveCounter && counter <= _lastKeepAliveCounter)
                {
                    Metrics.AuthReplayRejected(keepAlive: true);
                    return false;
                }
                _hasKeepAliveCounter = true;
                _lastKeepAliveCounter = counter;
                LastReceived = now;
                Metrics.AuthenticatedKeepAlive();
                return true;
            }
            if (header.Type == NetMessageType.KeepAlive) return false;

            ReceiveWindow next = _received;
            ReceiveResult preview = next.RecordNonWrapping(header.Sequence);
            result = preview;
            if (preview == ReceiveResult.TooOld)
            {
                Metrics.AuthReplayRejected();
                return false;
            }
            if (preview == ReceiveResult.Duplicate)
            {
                // Reliable retransmissions are valid authenticated traffic.
                // They must renew the ACK deadline, but they must not refresh
                // endpoint ownership or deliver the application event twice.
                if (header.Type is not (NetMessageType.Event or NetMessageType.Accepted)
                    || !sender.Equals(Endpoint))
                {
                    Metrics.AuthReplayRejected();
                    return false;
                }
                // A duplicate is eligible to renew the sender's ACK through
                // the owner callback, but it must not refresh liveness or
                // mutate the remote reliable state a second time.
                return true;
            }
            bool endpointChanged = !sender.Equals(Endpoint);
            if (endpointChanged && preview != ReceiveResult.Newest)
            {
                // A valid but old packet cannot prove a NAT rebinding.
                return false;
            }
            _received = next;
            if ((header.Flags & NetHeaderFlags.HasAck) != 0)
                Reliable.Acknowledge(header.Ack, header.AckBits);
            if (endpointChanged)
            {
                Endpoint = new IPEndPoint(sender.Address, sender.Port);
                Metrics.AuthenticatedRebind();
            }
            if (preview == ReceiveResult.Newest) LastReceived = now;
            return true;
        }

        /// <summary>Creates one transport-owned authenticated keepalive descriptor.</summary>
        public NetKeepAliveDescriptor CreateKeepAliveDescriptor()
        {
            if (_authKey is not { } key)
                throw new InvalidOperationException("Authenticated keepalives require a session key.");
            if (_sequenceExhausted)
                throw new InvalidOperationException("Authenticated connection is retired.");
            if (_nextKeepAliveCounter == ulong.MaxValue)
                throw new InvalidOperationException("Keepalive counter exhausted; retire the connection.");
            ulong counter = _nextKeepAliveCounter++;
            return new NetKeepAliveDescriptor(Endpoint, Id, counter, (byte[])key.Clone(), _authDirection);
        }
    }
}
