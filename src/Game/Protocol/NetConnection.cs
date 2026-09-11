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

        /// <summary>
        /// Legacy unkeyed compatibility seam for bootstrap and isolated tests.
        /// Established production connections must use the keyed overload;
        /// this overload does not provide a runtime authentication downgrade.
        /// </summary>
        public NetConnection(ulong id, IPEndPoint endpoint, uint matchId, double now,
            bool adaptiveReliableRto = true)
            : this(id, endpoint, matchId, now, ReadOnlySpan<byte>.Empty,
                NetAuthDirection.ClientToServer, adaptiveReliableRto) { }

        /// <summary>
        /// Creates an established connection with an owned copy of its UDP
        /// key. Join/bootstrap code is intentionally outside this primitive.
        /// </summary>
        public NetConnection(ulong id, IPEndPoint endpoint, uint matchId, double now,
            ReadOnlySpan<byte> authKey, NetAuthDirection authDirection,
            bool adaptiveReliableRto = true)
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
            transport.SendDatagram(Endpoint, datagram[..length]);
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
                transport.SendDatagram(Endpoint, datagram[..length]);
                Reliable.MarkSent(id, header.Sequence, now);
            }
        }

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
            if (preview is ReceiveResult.Duplicate or ReceiveResult.TooOld)
            {
                Metrics.AuthReplayRejected();
                return false;
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
