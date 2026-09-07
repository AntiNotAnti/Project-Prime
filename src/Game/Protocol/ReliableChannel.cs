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
        ChatRequest = 10
    }

    /// <summary>
    /// Small independent reliable events. No state packets enter this channel.
    /// One owner thread; bounded pending messages and send-attempt bookkeeping.
    /// Backpressure is explicit: the caller retries admission or disconnects.
    /// </summary>
    public sealed class ReliableChannel
    {
        public const int Capacity = 32;
        public const int MaxPayloadSize = 512;
        private const int AttemptCapacity = 256;
        private const double RetrySeconds = 0.15;

        private struct Pending
        {
            public uint Id;
            public ReliableEventType Type;
            public byte[]? Payload;
            public double Due;
        }

        private struct Attempt
        {
            public bool Valid;
            public uint PacketSequence;
            public uint EventId;
        }

        private readonly Pending[] _pending = new Pending[Capacity];
        private readonly Attempt[] _attempts = new Attempt[AttemptCapacity];
        private ReceiveWindow _received;
        private uint _nextId;
        private int _nextAttempt;
        private int _nextDue;

        public int PendingCount { get; private set; }
        public long Retransmissions { get; private set; }

        public ReliableChannel(uint firstEventId = 0)
        {
            _nextId = firstEventId;
        }

        public bool CanEnqueue
        {
            get
            {
                if (PendingCount == Capacity) { return false; }
                foreach (Pending pending in _pending)
                {
                    if (pending.Payload != null && unchecked(_nextId - pending.Id) > 32) { return false; }
                }
                return true;
            }
        }

        public bool TryEnqueue(ReliableEventType type, ReadOnlySpan<byte> payload, out uint eventId)
        {
            eventId = default;
            if (PendingCount == Capacity || payload.Length > MaxPayloadSize
                || type < ReliableEventType.Welcome || type > ReliableEventType.ChatRequest)
            {
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
                else if (unchecked(_nextId - _pending[i].Id) > 32)
                {
                    return false;
                }
            }
            eventId = _nextId++;
            _pending[free] = new Pending { Id = eventId, Type = type, Payload = payload.ToArray() };
            PendingCount++;
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
                pending.Due = now + RetrySeconds;
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
