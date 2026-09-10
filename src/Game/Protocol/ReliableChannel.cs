using System;

namespace MphRead.Mods.Network
{
    public enum ReliableEventType : byte
    {
        Welcome = 1,
        ClientReady = 2,
        MapTransition = 3,
        Disconnect = 4,
        MatchState = 5,
        Combat = 6,
        World = 7,
        Roster = 8,
        Chat = 9,
        ChatRequest = 10,
        Kill = 11,
        WorldEvent = 12,
        ObserverTransition = 13,
        IntermissionBallot = 14,
        IntermissionVote = 15,
        MatchAward = 16,
        MatchSemantic = 17,
        TimingProfile = 18,
        TimingProfileApplied = 19
    }

    public enum ReliableAdmissionFailure
    {
        None,
        Capacity,
        ReservedCapacity,
        IdSpan,
        OversizedPayload,
        InvalidType
    }

    /// <summary>
    /// Small independent reliable events. No state packets enter this channel.
    /// One owner thread; bounded pending messages and send-attempt bookkeeping.
    /// Backpressure is explicit: the caller retries admission or disconnects.
    /// </summary>
    public sealed class ReliableChannel
    {
        public const int Capacity = 32;
        public const int CriticalReserve = 8;
        public const int OrdinaryCapacity = Capacity - CriticalReserve;
        public const int EventWindowCapacity = ReliableEventWindow.Capacity;
        public const int MaxPayloadSize = 512;
        private const int AttemptCapacity = 256;
        public const double InitialRetrySeconds = 0.15;
        public const double MinimumRetrySeconds = 0.05;
        public const double MaximumRetrySeconds = 0.5;

        private struct Pending
        {
            public uint Id;
            public ReliableEventType Type;
            public byte[]? Payload;
            public double Due;
            public double FirstSent;
            public uint SentAttempts;
        }

        private struct Attempt
        {
            public bool Valid;
            public uint PacketSequence;
            public uint EventId;
        }

        private readonly Pending[] _pending = new Pending[Capacity];
        private readonly Attempt[] _attempts = new Attempt[AttemptCapacity];
        private readonly ReliableEventWindow _received = new();
        private uint _nextId;
        private int _nextAttempt;
        private int _nextDue;
        private double _retrySeconds = InitialRetrySeconds;
        private readonly bool _adaptiveRetryEnabled;

        public int PendingCount { get; private set; }
        public long Retransmissions { get; private set; }
        public int PendingHighWater { get; private set; }
        public ReliableAdmissionFailure LastAdmissionFailure { get; private set; }
        public long CapacityRejections { get; private set; }
        public long IdSpanRejections { get; private set; }
        public long OversizedPayloadRejections { get; private set; }
        public long InvalidTypeRejections { get; private set; }
        public long OrdinaryAdmissionRejections { get; private set; }
        public long CriticalReserveUses { get; private set; }
        public long CriticalAdmissionRejections { get; private set; }
        public double RetrySeconds => _retrySeconds;

        // Distance from the next event ID to the oldest pending event, across uint wrap.
        // Compute only when diagnostics are sampled; admission keeps its existing scan.
        public uint OldestPendingSpan
        {
            get
            {
                uint span = 0;
                foreach (Pending pending in _pending)
                {
                    if (pending.Payload != null)
                    {
                        span = Math.Max(span, unchecked(_nextId - pending.Id));
                    }
                }
                return span;
            }
        }

        public readonly record struct PendingDiagnostic(uint Id, ReliableEventType Type, uint SentAttempts, double AgeSeconds);

        public PendingDiagnostic? OldestPending(double now)
        {
            Pending? oldest = null;
            foreach (Pending pending in _pending)
                if (pending.Payload != null && (!oldest.HasValue
                    || unchecked(_nextId - pending.Id) > unchecked(_nextId - oldest.Value.Id))) oldest = pending;
            return oldest is { } value ? new(value.Id, value.Type, value.SentAttempts,
                value.SentAttempts == 0 ? 0 : Math.Max(0, now - value.FirstSent)) : null;
        }

        public ReliableChannel(uint firstEventId = 0, bool adaptiveRetryEnabled = true)
        {
            _nextId = firstEventId;
            _adaptiveRetryEnabled = adaptiveRetryEnabled;
        }

        public bool CanEnqueue => CanEnqueueType(ReliableEventType.Combat);

        public bool CanEnqueueType(ReliableEventType type)
        {
            if (type < ReliableEventType.Welcome || type > ReliableEventType.TimingProfileApplied)
                return false;
            if (PendingCount >= (IsCritical(type) ? Capacity : OrdinaryCapacity)) return false;
            foreach (Pending pending in _pending)
            {
                if (pending.Payload != null && unchecked(_nextId - pending.Id) >= EventWindowCapacity)
                    return false;
            }
            return true;
        }

        public bool TryEnqueue(ReliableEventType type, ReadOnlySpan<byte> payload, out uint eventId)
        {
            eventId = default;
            // Record the first refusal in the same order as the existing admission guards.
            if (PendingCount == Capacity)
            {
                LastAdmissionFailure = ReliableAdmissionFailure.Capacity;
                CapacityRejections++;
                if (IsCritical(type)) CriticalAdmissionRejections++;
                else OrdinaryAdmissionRejections++;
                return false;
            }
            if (payload.Length > MaxPayloadSize)
            {
                LastAdmissionFailure = ReliableAdmissionFailure.OversizedPayload;
                OversizedPayloadRejections++;
                return false;
            }
            bool critical = IsCritical(type);
            if (!critical && PendingCount >= OrdinaryCapacity)
            {
                LastAdmissionFailure = ReliableAdmissionFailure.ReservedCapacity;
                CapacityRejections++;
                OrdinaryAdmissionRejections++;
                return false;
            }
            if (type < ReliableEventType.Welcome || type > ReliableEventType.TimingProfileApplied)
            {
                LastAdmissionFailure = ReliableAdmissionFailure.InvalidType;
                InvalidTypeRejections++;
                return false;
            }
            int free = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pending[i].Payload == null)
                {
                    if (free < 0) { free = i; }
                }
                // Bound the ID span too, not just the number of pending
                // messages. Otherwise newer completed events could push a
                // lost old event out of the receiver's deduplication window.
                else if (unchecked(_nextId - _pending[i].Id) >= EventWindowCapacity)
                {
                    LastAdmissionFailure = ReliableAdmissionFailure.IdSpan;
                    IdSpanRejections++;
                    return false;
                }
            }
            eventId = _nextId++;
            _pending[free] = new Pending { Id = eventId, Type = type, Payload = payload.ToArray() };
            PendingCount++;
            if (critical && PendingCount > OrdinaryCapacity) CriticalReserveUses++;
            PendingHighWater = Math.Max(PendingHighWater, PendingCount);
            LastAdmissionFailure = ReliableAdmissionFailure.None;
            return true;
        }

        // Match changes invalidate queued application events, but admission must
        // still complete if the welcome packet has not reached a loading client.
        public void CancelPendingExceptWelcome()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_pending[i].Payload != null && _pending[i].Type != ReliableEventType.Welcome)
                {
                    _pending[i] = default;
                    PendingCount--;
                }
            }
        }

        public int CancelPending(ReliableEventType type)
        {
            int removed = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pending[i].Payload != null && _pending[i].Type == type)
                {
                    _pending[i] = default;
                    PendingCount--;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Returns one due event in round-robin order. Call MarkSent only after
        /// submitting it to transport. now is a monotonic clock in seconds.
        /// </summary>
        public bool TryGetDue(double now, out uint eventId, out ReliableEventType type,
            out ReadOnlyMemory<byte> payload)
        {
            eventId = default;
            type = default;
            payload = default;
            for (int offset = 0; offset < Capacity; offset++)
            {
                int index = (_nextDue + offset) % Capacity;
                ref Pending pending = ref _pending[index];
                if (pending.Payload != null && now >= pending.Due)
                {
                    eventId = pending.Id;
                    type = pending.Type;
                    payload = pending.Payload;
                    _nextDue = (index + 1) % Capacity;
                    return true;
                }
            }
            return false;
        }

        public void MarkSent(uint eventId, uint packetSequence, double now)
        {
            for (int i = 0; i < Capacity; i++)
            {
                ref Pending pending = ref _pending[i];
                if (pending.Payload == null || pending.Id != eventId)
                {
                    continue;
                }
                if (pending.Due > 0)
                {
                    Retransmissions++;
                }
                if (pending.SentAttempts == 0) pending.FirstSent = now;
                if (pending.SentAttempts < uint.MaxValue) pending.SentAttempts++;
                int backoff = 1 << (int)Math.Min(3, pending.SentAttempts - 1);
                pending.Due = now + Math.Min(MaximumRetrySeconds, _retrySeconds * backoff);
                _attempts[_nextAttempt] = new Attempt
                {
                    Valid = true,
                    PacketSequence = packetSequence,
                    EventId = eventId
                };
                _nextAttempt = (_nextAttempt + 1) % AttemptCapacity;
                return;
            }
        }

        /// <summary>
        /// Updates the connection RTO only from validated round-trip samples.
        /// Invalid or absent samples retain the safe 150 ms initial value.
        /// </summary>
        public bool ConfigureRetry(double smoothedRttMs, double jitterMs)
        {
            if (!_adaptiveRetryEnabled) return false;
            if (!Double.IsFinite(smoothedRttMs) || smoothedRttMs <= 0
                || !Double.IsFinite(jitterMs) || jitterMs < 0) return false;
            double milliseconds = smoothedRttMs + Math.Max(10, jitterMs * 4);
            _retrySeconds = Math.Clamp(milliseconds / 1000,
                MinimumRetrySeconds, MaximumRetrySeconds);
            return true;
        }

        public static bool IsCritical(ReliableEventType type)
            => type is ReliableEventType.Welcome or ReliableEventType.ClientReady
                or ReliableEventType.MapTransition or ReliableEventType.Disconnect
                or ReliableEventType.MatchState or ReliableEventType.Kill
                or ReliableEventType.WorldEvent or ReliableEventType.ObserverTransition
                or ReliableEventType.IntermissionBallot or ReliableEventType.TimingProfile
                or ReliableEventType.TimingProfileApplied;

        public void Acknowledge(uint ack, uint ackBits)
        {
            for (int attemptIndex = 0; attemptIndex < AttemptCapacity; attemptIndex++)
            {
                ref Attempt attempt = ref _attempts[attemptIndex];
                if (!attempt.Valid || !ReceiveWindow.IsAcknowledged(attempt.PacketSequence, ack, ackBits))
                {
                    continue;
                }
                attempt.Valid = false;
                for (int i = 0; i < Capacity; i++)
                {
                    if (_pending[i].Payload != null && _pending[i].Id == attempt.EventId)
                    {
                        _pending[i] = default;
                        PendingCount--;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Call only after validating the complete event body and making room
        /// for its delivery. A duplicate packet still needs a datagram ACK.
        /// </summary>
        public bool Receive(uint eventId)
        {
            ReceiveResult result = _received.Record(eventId);
            return result is ReceiveResult.Newest or ReceiveResult.OutOfOrder;
        }
    }
}
