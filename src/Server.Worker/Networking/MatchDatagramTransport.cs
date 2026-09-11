using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace MphRead.Mods.Network;

public enum MatchTrafficClass : byte
{
    CriticalReliable,
    NormalReliable,
    Snapshot,
    World,
    BestEffort
}

/// <summary>
/// Bounded virtual transport. The simulation is the sole receive reader. Outbound
/// datagrams use fixed slots; snapshots coalesce per connection while reliable and
/// World traffic retain their ordering. The hub performs physical sends outside
/// the mailbox lock.
/// </summary>
public sealed class MatchDatagramTransport : INetTransport, IMatchConnectionRoutes
{
    private const int TrafficClassCount = 5;
    private static readonly MatchTrafficClass[] Schedule =
    {
        MatchTrafficClass.CriticalReliable,
        MatchTrafficClass.CriticalReliable,
        MatchTrafficClass.CriticalReliable,
        MatchTrafficClass.Snapshot,
        MatchTrafficClass.CriticalReliable,
        MatchTrafficClass.NormalReliable,
        MatchTrafficClass.World,
        MatchTrafficClass.Snapshot
    };
    private static readonly MatchTrafficClass[] AllTrafficClasses =
        Enum.GetValues<MatchTrafficClass>();
    private static readonly MatchTrafficClass[] LegacyControlClasses =
        [MatchTrafficClass.CriticalReliable, MatchTrafficClass.NormalReliable, MatchTrafficClass.BestEffort];
    private static readonly MatchTrafficClass[] LegacyUpdateClasses =
        [MatchTrafficClass.Snapshot, MatchTrafficClass.World];

    private sealed class OutboundSlot
    {
        public readonly byte[] Bytes = new byte[NetConfig.MaxPacketSize];
        public readonly IPEndPoint Target = new(IPAddress.Any, 0);
        public int Length;
        public long HoldTicks;
        public long EnqueuedAt;
        public MatchTrafficClass Class;
        public ulong SnapshotKey;
        public bool UsesCriticalReserve;
    }

    private sealed class SlotQueue
    {
        private readonly int[] _items;
        private int _head;
        public int Count { get; private set; }

        public SlotQueue(int capacity) => _items = new int[capacity];
        public void Enqueue(int value)
        {
            if (Count == _items.Length) throw new InvalidOperationException("Transport queue is full.");
            _items[(_head + Count) % _items.Length] = value;
            Count++;
        }
        public int Dequeue()
        {
            if (Count == 0) throw new InvalidOperationException("Transport queue is empty.");
            int value = _items[_head];
            _head = (_head + 1) % _items.Length;
            Count--;
            return value;
        }
        public int Peek()
        {
            if (Count == 0) throw new InvalidOperationException("Transport queue is empty.");
            return _items[_head];
        }
        public void Clear() { _head = Count = 0; }
    }

    private sealed class AuthenticatedKeepAlive
    {
        public readonly IPEndPoint Target;
        public readonly ulong ConnectionId;
        public readonly byte[] Key;
        public readonly NetAuthDirection Direction;
        public ulong Counter;
        public bool Retired;
        public bool Detached;
        public int InFlight;

        public AuthenticatedKeepAlive(IPEndPoint target, ulong connectionId,
            ulong counter, byte[] key, NetAuthDirection direction)
        {
            Target = target;
            ConnectionId = connectionId;
            Counter = counter;
            Key = key;
            Direction = direction;
        }
    }

    private readonly WorkerNetworkHub _hub;
    private readonly object _gate = new();
    private readonly Queue<ReceivedPacket> _inbound;
    private readonly OutboundSlot[] _slots;
    private readonly int[] _freeSlots;
    private readonly SlotQueue[] _outbound;
    private readonly Dictionary<ulong, int> _snapshots;
    private readonly long[] _classSent = new long[TrafficClassCount];
    private readonly long[] _classDrops = new long[TrafficClassCount];
    private readonly int[] _classHighWater = new int[TrafficClassCount];
    private readonly long[] _maximumClassAgeTicks = new long[TrafficClassCount];
    private int _freeCount;
    private int _outboundCount;
    private int _ordinaryUsed;
    private int _criticalReserveUsed;
    private int _scheduleCursor;
    private NetKeepAlive[] _keepAlives = Array.Empty<NetKeepAlive>();
    private AuthenticatedKeepAlive[] _authenticatedKeepAlives = Array.Empty<AuthenticatedKeepAlive>();
    private long _nextKeepAlive;
    private int _keepAliveCursor;
    private int _authenticatedKeepAliveCursor;
    private bool _disposed;
    private int _reading;
    private readonly int _capacity;
    private readonly int _budget;
    private readonly int _criticalReserve;
    private readonly int _ordinaryCapacity;
    private readonly bool _queueV2Enabled;
    private int _inboundQueueHighWater;
    private int _outboundQueueHighWater;
    private long _controlEnqueues;
    private long _updateEnqueues;
    private long _controlDrops;
    private long _updateDrops;
    private long _snapshotsSuperseded;
    private long _criticalReserveUses;
    private long _criticalReserveExhaustions;
    private long _criticalTransportDrops;
    private long _criticalReserveMaximumAgeTicks;
    private long _flushCount;
    private long _flushDatagramsAttempted;
    private long _flushBudgetExhaustions;
    private readonly BoundedPercentileSampler _flushDurations = new();

    public uint WireMatchId { get; }
    public int MaxConnections { get; }
    public int CriticalReserveCapacity => _criticalReserve;
    public int CriticalReserveInUse { get { lock (_gate) return _criticalReserveUsed; } }
    public int CriticalReserveHighWater { get; private set; }
    public long CriticalReserveUses => Interlocked.Read(ref _criticalReserveUses);
    public long CriticalReserveExhaustions => Interlocked.Read(ref _criticalReserveExhaustions);
    public long CriticalTransportDrops => Interlocked.Read(ref _criticalTransportDrops);

    internal bool RegisterAdmissionId(Guid admissionId, long expiresAtUnixSeconds, out bool created)
        => _hub.RegisterAdmission(admissionId, this, expiresAtUnixSeconds, out created);

    internal bool UnregisterAdmissionId(Guid admissionId) => _hub.UnregisterAdmission(admissionId, this);
    public double MaximumCriticalReserveQueueAgeMilliseconds
        => Volatile.Read(ref _criticalReserveMaximumAgeTicks)
            * (1000.0 / Stopwatch.Frequency);
    public double OldestCriticalReserveQueueAgeMilliseconds
    {
        get
        {
            lock (_gate)
            {
                if (_criticalReserveUsed == 0) return 0;
                long now = Stopwatch.GetTimestamp();
                long oldest = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    OutboundSlot slot = _slots[i];
                    if (slot.UsesCriticalReserve && slot.Length > 0)
                    {
                        long age = Math.Max(0, now - slot.EnqueuedAt);
                        if (age > oldest) oldest = age;
                    }
                }
                return oldest * (1000.0 / Stopwatch.Frequency);
            }
        }
    }
    public Guid WorkerIncarnation { get; }
    public int LocalPort => _hub.LocalPort;
    public NetTrafficMetrics Metrics { get; } = new();
    public long PacketsDropped => Metrics.QueueDrops;
    public int QueuedPackets { get { lock (_gate) return _inbound.Count; } }
    public int HeldIncomingPackets => 0;
    public int HeldOutgoingPackets { get { lock (_gate) return _outboundCount; } }
    public int InboundQueueHighWater => Volatile.Read(ref _inboundQueueHighWater);
    public int OutboundQueueHighWater => Volatile.Read(ref _outboundQueueHighWater);
    public long ControlEnqueues => Interlocked.Read(ref _controlEnqueues);
    public long UpdateEnqueues => Interlocked.Read(ref _updateEnqueues);
    public long ControlDrops => Interlocked.Read(ref _controlDrops);
    public long UpdateDrops => Interlocked.Read(ref _updateDrops);
    public long SnapshotsSuperseded => Interlocked.Read(ref _snapshotsSuperseded);
    public long FlushCount => Interlocked.Read(ref _flushCount);
    public long FlushDatagramsAttempted => Interlocked.Read(ref _flushDatagramsAttempted);
    public long FlushBudgetExhaustions => Interlocked.Read(ref _flushBudgetExhaustions);
    public BoundedPercentileSnapshot FlushDurationPercentiles
        => _flushDurations.TrySnapshot(out BoundedPercentileSnapshot snapshot) ? snapshot : default;

    public long PacketsSent(MatchTrafficClass trafficClass)
        => Interlocked.Read(ref _classSent[ClassIndex(trafficClass)]);
    public long ClassPacketsDropped(MatchTrafficClass trafficClass)
        => Interlocked.Read(ref _classDrops[ClassIndex(trafficClass)]);
    public int QueueHighWater(MatchTrafficClass trafficClass)
        => Volatile.Read(ref _classHighWater[ClassIndex(trafficClass)]);
    public double MaximumQueueAgeMilliseconds(MatchTrafficClass trafficClass)
        => Volatile.Read(ref _maximumClassAgeTicks[ClassIndex(trafficClass)])
            * (1000.0 / Stopwatch.Frequency);
    public double OldestQueueAgeMilliseconds(MatchTrafficClass trafficClass)
    {
        int index = ClassIndex(trafficClass);
        lock (_gate)
        {
            if (_outbound[index].Count == 0) return 0;
            return Stopwatch.GetElapsedTime(_slots[_outbound[index].Peek()].EnqueuedAt).TotalMilliseconds;
        }
    }

    internal MatchDatagramTransport(WorkerNetworkHub hub, uint wireMatchId, int capacity,
        int budget, int maxConnections, bool queueV2Enabled, int criticalReserve)
    {
        if (capacity is < 1 or > 65536 || budget < 1 || budget > capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (criticalReserve < 0) throw new ArgumentOutOfRangeException(nameof(criticalReserve));
        if (maxConnections is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxConnections));
        MaxConnections = maxConnections;
        _hub = hub;
        WireMatchId = wireMatchId;
        WorkerIncarnation = hub.Incarnation;
        _capacity = capacity;
        _budget = budget;
        // Keep a substantial ordinary region for small fixtures. The
        // production default (2048 total / 32 reserved) remains exact, while
        // a tiny queue cannot accidentally become reserve-only.
        _criticalReserve = Math.Min(criticalReserve, capacity / 4);
        _ordinaryCapacity = capacity - _criticalReserve;
        _queueV2Enabled = queueV2Enabled;
        _inbound = new Queue<ReceivedPacket>(capacity);
        _slots = new OutboundSlot[capacity];
        _freeSlots = new int[capacity];
        _outbound = new SlotQueue[TrafficClassCount];
        _snapshots = new Dictionary<ulong, int>(maxConnections);
        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new OutboundSlot();
            _freeSlots[i] = capacity - i - 1;
        }
        _freeCount = capacity;
        for (int i = 0; i < _outbound.Length; i++) _outbound[i] = new SlotQueue(capacity);
    }

    public ulong AllocateConnectionId() => _hub.AllocateConnection(this, WorkerIncarnation);
    public void RemoveConnection(ulong connectionId) => _hub.RemoveConnection(this, connectionId);

    internal bool Enqueue(in ReceivedPacket packet)
    {
        lock (_gate)
        {
            if (_disposed || _inbound.Count >= _capacity) { Metrics.DropQueued(); return false; }
            _inbound.Enqueue(packet);
            ObserveHighWater(ref _inboundQueueHighWater, _inbound.Count);
            Metrics.ObserveQueueDepth(_inbound.Count);
            Metrics.Received(packet.Length);
            return true;
        }
    }

    public IEnumerable<ReceivedPacket> Drain()
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            throw new InvalidOperationException("Match transport has one simulation reader.");
        try
        {
            for (int i = 0; i < _budget; i++)
            {
                ReceivedPacket packet;
                lock (_gate)
                {
                    if (_disposed || !_inbound.TryDequeue(out packet)) yield break;
                }
                yield return packet;
            }
        }
        finally { Volatile.Write(ref _reading, 0); }
    }

    public int Drain(Span<ReceivedPacket> destination)
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            throw new InvalidOperationException("Match transport has one simulation reader.");
        try
        {
            int count = 0;
            int limit = Math.Min(destination.Length, _budget);
            lock (_gate)
            {
                while (!_disposed && count < limit && _inbound.TryDequeue(out ReceivedPacket packet))
                    destination[count++] = packet;
            }
            return count;
        }
        finally { Volatile.Write(ref _reading, 0); }
    }

    public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload,
        long extraHoldTicks = 0)
    {
        if (payload.Length >= NetConfig.MaxPacketSize) { Metrics.Reject(); return; }
        Span<byte> bytes = stackalloc byte[payload.Length + 1];
        bytes[0] = (byte)type;
        payload.CopyTo(bytes[1..]);
        SendDatagram(target, bytes, extraHoldTicks);
    }

    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram)
        => SendDatagram(target, datagram, 0);

    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (datagram.IsEmpty || datagram.Length > NetConfig.MaxPacketSize || extraHoldTicks < 0)
        { Metrics.Reject(); return; }
        MatchTrafficClass trafficClass = Classify(datagram, out ulong snapshotKey);
        int classIndex = (int)trafficClass;
        lock (_gate)
        {
            if (_disposed) return;
            bool update = trafficClass is MatchTrafficClass.Snapshot or MatchTrafficClass.World;
            if (_queueV2Enabled && trafficClass == MatchTrafficClass.Snapshot
                && _snapshots.TryGetValue(snapshotKey, out int existing))
            {
                Fill(_slots[existing], target, datagram, extraHoldTicks, trafficClass, snapshotKey);
                Interlocked.Increment(ref _updateEnqueues);
                Interlocked.Increment(ref _snapshotsSuperseded);
                return;
            }
            if (!TryAcquireSlot(trafficClass, out bool usesCriticalReserve))
            {
                Drop(trafficClass, update);
                return;
            }
            int slotIndex = _freeSlots[--_freeCount];
            OutboundSlot slot = _slots[slotIndex];
            Fill(slot, target, datagram, extraHoldTicks, trafficClass, snapshotKey);
            slot.UsesCriticalReserve = usesCriticalReserve;
            if (usesCriticalReserve)
            {
                _criticalReserveUsed++;
                CriticalReserveHighWater = Math.Max(CriticalReserveHighWater, _criticalReserveUsed);
                Interlocked.Increment(ref _criticalReserveUses);
            }
            else
            {
                _ordinaryUsed++;
            }
            _outbound[classIndex].Enqueue(slotIndex);
            _outboundCount++;
            if (_queueV2Enabled && trafficClass == MatchTrafficClass.Snapshot)
                _snapshots.Add(snapshotKey, slotIndex);
            if (update) Interlocked.Increment(ref _updateEnqueues);
            else Interlocked.Increment(ref _controlEnqueues);
            ObserveHighWater(ref _classHighWater[classIndex], _outbound[classIndex].Count);
            ObserveHighWater(ref _outboundQueueHighWater, _outboundCount);
            Metrics.ObserveQueueDepth(_outboundCount);
        }
    }

    internal int Flush(INetTransport physical, NetTrafficMetrics hubMetrics, int budget)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
        int attempted = 0;
        long started = Stopwatch.GetTimestamp();
        try
        {
            lock (_gate)
            {
                Metrics.ObserveQueueDepth(_outboundCount);
                hubMetrics.ObserveQueueDepth(_outboundCount);
            }
            int limit = Math.Min(_budget, budget);
            for (int i = 0; i < limit; i++)
            {
                int slotIndex;
                OutboundSlot slot;
                lock (_gate)
                {
                    if (_disposed || !TryTakeScheduled(out slotIndex)) break;
                    slot = _slots[slotIndex];
                    Metrics.ObserveQueueDepth(_outboundCount);
                    hubMetrics.ObserveQueueDepth(_outboundCount);
                }
                SendPhysical(physical, hubMetrics, slot.Target,
                    slot.Bytes.AsSpan(0, slot.Length), slot.HoldTicks, slot.Class);
                attempted++;
                lock (_gate)
                {
                    if (!_disposed) Release(slotIndex);
                }
            }

            while (attempted < limit)
            {
                NetKeepAlive keepAlive;
                AuthenticatedKeepAlive? authenticatedKeepAlive;
                lock (_gate)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (!TryTakeKeepAlive(now, out keepAlive, out authenticatedKeepAlive))
                        break;
                }
                if (authenticatedKeepAlive is { } descriptor)
                {
                    SendAuthenticatedKeepAlive(physical, hubMetrics, descriptor);
                }
                else
                {
                    SendPhysical(physical, hubMetrics, keepAlive.Endpoint,
                        keepAlive.Datagram.Span, 0, MatchTrafficClass.BestEffort);
                }
                attempted++;
            }
            lock (_gate)
            {
                Metrics.ObserveQueueDepth(_outboundCount);
                hubMetrics.ObserveQueueDepth(_outboundCount);
            }
            if (attempted == limit && limit < _budget)
                Interlocked.Increment(ref _flushBudgetExhaustions);
            return attempted;
        }
        finally
        {
            Interlocked.Add(ref _flushDatagramsAttempted, attempted);
            _flushDurations.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Interlocked.Increment(ref _flushCount);
            Metrics.Flushed();
            hubMetrics.Flushed();
        }
    }

    private bool TryTakeKeepAlive(long now, out NetKeepAlive keepAlive,
        out AuthenticatedKeepAlive? authenticatedKeepAlive)
    {
        keepAlive = default;
        authenticatedKeepAlive = null;
        if (_disposed || now < _nextKeepAlive)
            return false;

        if (_authenticatedKeepAlives.Length > 0)
        {
            for (int scan = 0; scan < _authenticatedKeepAlives.Length; scan++)
            {
                if (_authenticatedKeepAliveCursor >= _authenticatedKeepAlives.Length)
                    _authenticatedKeepAliveCursor = 0;
                AuthenticatedKeepAlive candidate =
                    _authenticatedKeepAlives[_authenticatedKeepAliveCursor++];
                if (candidate.Retired) continue;
                if (_authenticatedKeepAliveCursor == _authenticatedKeepAlives.Length)
                {
                    _authenticatedKeepAliveCursor = 0;
                    _nextKeepAlive = now + Stopwatch.Frequency;
                }
                authenticatedKeepAlive = candidate;
                return true;
            }
            _authenticatedKeepAlives = Array.Empty<AuthenticatedKeepAlive>();
            _authenticatedKeepAliveCursor = 0;
            return false;
        }

        if (_keepAlives.Length == 0)
            return false;
        if (_keepAliveCursor >= _keepAlives.Length) _keepAliveCursor = 0;
        keepAlive = _keepAlives[_keepAliveCursor++];
        if (_keepAliveCursor == _keepAlives.Length)
        {
            _keepAliveCursor = 0;
            _nextKeepAlive = now + Stopwatch.Frequency;
        }
        return true;
    }

    private void SendAuthenticatedKeepAlive(INetTransport physical,
        NetTrafficMetrics hubMetrics, AuthenticatedKeepAlive keepAlive)
    {
        ulong counter;
        lock (_gate)
        {
            if (_disposed || keepAlive.Detached || keepAlive.Retired) return;
            keepAlive.InFlight++;
            counter = keepAlive.Counter;
            if (counter == ulong.MaxValue) keepAlive.Retired = true;
            else keepAlive.Counter = counter + 1;
        }
        try
        {
            Span<byte> datagram = stackalloc byte[
                NetHeader.Size + NetAuthentication.CounterSize + NetAuthentication.TagSize];
            NetHeader header = new(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced,
                keepAlive.ConnectionId, 0, 0, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(datagram[NetHeader.Size..], counter);
            int length = NetAuthentication.Sign(keepAlive.Key, keepAlive.Direction, header,
                datagram.Slice(NetHeader.Size, NetAuthentication.CounterSize), datagram);
            SendPhysical(physical, hubMetrics, keepAlive.Target, datagram[..length], 0,
                MatchTrafficClass.BestEffort);
        }
        finally
        {
            lock (_gate)
            {
                keepAlive.InFlight--;
                if (keepAlive.Detached && keepAlive.InFlight == 0)
                    CryptographicOperations.ZeroMemory(keepAlive.Key);
            }
        }
    }

    private static AuthenticatedKeepAlive? FindKeepAlive(
        AuthenticatedKeepAlive[] previous, NetKeepAliveDescriptor descriptor)
    {
        foreach (AuthenticatedKeepAlive candidate in previous)
        {
            if (candidate.ConnectionId == descriptor.ConnectionId
                && candidate.Direction == descriptor.Direction
                && CryptographicOperations.FixedTimeEquals(candidate.Key, descriptor.Key.Span))
                return candidate;
        }
        return null;
    }

    private static void RetireKeepAlivesLocked(AuthenticatedKeepAlive[] entries)
    {
        foreach (AuthenticatedKeepAlive entry in entries)
        {
            entry.Detached = true;
            if (entry.InFlight == 0)
                CryptographicOperations.ZeroMemory(entry.Key);
        }
    }

    private bool TryTakeScheduled(out int slotIndex)
    {
        slotIndex = -1;
        if (_outboundCount == 0) return false;
        if (!_queueV2Enabled) return TryTakeLegacy(out slotIndex);
        for (int scan = 0; scan < Schedule.Length; scan++)
        {
            MatchTrafficClass candidate = Schedule[_scheduleCursor];
            _scheduleCursor = (_scheduleCursor + 1) % Schedule.Length;
            SlotQueue queue = _outbound[(int)candidate];
            if (queue.Count != 0) { slotIndex = Take(queue, candidate); return true; }
        }
        foreach (MatchTrafficClass candidate in AllTrafficClasses)
        {
            SlotQueue queue = _outbound[(int)candidate];
            if (queue.Count != 0) { slotIndex = Take(queue, candidate); return true; }
        }
        throw new InvalidOperationException("Outbound queue count is inconsistent.");
    }

    private bool TryTakeLegacy(out int slotIndex)
    {
        slotIndex = -1;
        // Rollback mode preserves the former control-first behavior while
        // retaining the same bounded preallocated storage.
        if (TryTakeOldest(LegacyControlClasses, out slotIndex)) return true;
        return TryTakeOldest(LegacyUpdateClasses, out slotIndex);
    }

    private bool TryTakeOldest(ReadOnlySpan<MatchTrafficClass> classes, out int slotIndex)
    {
        slotIndex = -1;
        MatchTrafficClass selected = default;
        long oldest = long.MaxValue;
        foreach (MatchTrafficClass candidate in classes)
        {
            SlotQueue queue = _outbound[(int)candidate];
            if (queue.Count == 0) continue;
            long enqueued = _slots[queue.Peek()].EnqueuedAt;
            if (enqueued < oldest) { oldest = enqueued; selected = candidate; }
        }
        if (oldest == long.MaxValue) return false;
        slotIndex = Take(_outbound[(int)selected], selected);
        return true;
    }

    private int Take(SlotQueue queue, MatchTrafficClass trafficClass)
    {
        int slotIndex = queue.Dequeue();
        _outboundCount--;
        OutboundSlot slot = _slots[slotIndex];
        if (slot.UsesCriticalReserve)
        {
            _criticalReserveUsed--;
            long reserveAge = Math.Max(0, Stopwatch.GetTimestamp() - slot.EnqueuedAt);
            ObserveHighWater(ref _criticalReserveMaximumAgeTicks, reserveAge);
        }
        else
        {
            _ordinaryUsed--;
        }
        if (_queueV2Enabled && trafficClass == MatchTrafficClass.Snapshot)
            _snapshots.Remove(slot.SnapshotKey);
        long age = Math.Max(0, Stopwatch.GetTimestamp() - slot.EnqueuedAt);
        ObserveHighWater(ref _maximumClassAgeTicks[(int)trafficClass], age);
        return slotIndex;
    }

    private bool TryAcquireSlot(MatchTrafficClass incoming, out bool usesCriticalReserve)
    {
        usesCriticalReserve = false;
        bool critical = incoming == MatchTrafficClass.CriticalReliable;
        if (_freeCount > 0)
        {
            if (_ordinaryUsed < _ordinaryCapacity) return true;
            if (critical && _criticalReserveUsed < _criticalReserve)
            {
                usesCriticalReserve = true;
                return true;
            }
        }
        if (!TryEvictFor(incoming))
        {
            if (critical && _criticalReserve > 0 && _criticalReserveUsed >= _criticalReserve)
                Interlocked.Increment(ref _criticalReserveExhaustions);
            return false;
        }
        if (_ordinaryUsed < _ordinaryCapacity) return true;
        if (critical && _criticalReserveUsed < _criticalReserve)
        {
            usesCriticalReserve = true;
            return true;
        }
        if (critical && _criticalReserve > 0) Interlocked.Increment(ref _criticalReserveExhaustions);
        return false;
    }

    private bool TryEvictFor(MatchTrafficClass incoming)
    {
        if (incoming is MatchTrafficClass.Snapshot or MatchTrafficClass.World
            or MatchTrafficClass.BestEffort) return false;
        if (TryEvict(MatchTrafficClass.Snapshot)) return true;
        return TryEvict(MatchTrafficClass.BestEffort);
    }

    private bool TryEvict(MatchTrafficClass trafficClass)
    {
        SlotQueue queue = _outbound[(int)trafficClass];
        if (queue.Count == 0) return false;
        int slotIndex = Take(queue, trafficClass);
        Interlocked.Increment(ref _classDrops[(int)trafficClass]);
        if (trafficClass is MatchTrafficClass.Snapshot or MatchTrafficClass.World)
            Interlocked.Increment(ref _updateDrops);
        else Interlocked.Increment(ref _controlDrops);
        Metrics.DropQueued();
        Release(slotIndex);
        return true;
    }

    private void Drop(MatchTrafficClass trafficClass, bool update)
    {
        Metrics.DropQueued();
        Interlocked.Increment(ref _classDrops[(int)trafficClass]);
        if (trafficClass == MatchTrafficClass.CriticalReliable)
            Interlocked.Increment(ref _criticalTransportDrops);
        if (update) Interlocked.Increment(ref _updateDrops);
        else Interlocked.Increment(ref _controlDrops);
    }

    private void Release(int slotIndex)
    {
        OutboundSlot slot = _slots[slotIndex];
        slot.Length = 0;
        slot.HoldTicks = 0;
        slot.EnqueuedAt = 0;
        slot.SnapshotKey = 0;
        slot.UsesCriticalReserve = false;
        _freeSlots[_freeCount++] = slotIndex;
    }

    private static void Fill(OutboundSlot slot, IPEndPoint target,
        ReadOnlySpan<byte> datagram, long holdTicks, MatchTrafficClass trafficClass,
        ulong snapshotKey)
    {
        datagram.CopyTo(slot.Bytes);
        slot.Target.Address = target.Address;
        slot.Target.Port = target.Port;
        slot.Length = datagram.Length;
        slot.HoldTicks = holdTicks;
        slot.EnqueuedAt = Stopwatch.GetTimestamp();
        slot.Class = trafficClass;
        slot.SnapshotKey = snapshotKey;
    }

    private static MatchTrafficClass Classify(ReadOnlySpan<byte> datagram,
        out ulong snapshotKey)
    {
        snapshotKey = 0;
        if (!NetHeader.TryRead(datagram, out NetHeader header))
            return MatchTrafficClass.CriticalReliable;
        snapshotKey = header.ConnectionId;
        if (header.Type == NetMessageType.Snapshot) return MatchTrafficClass.Snapshot;
        if (header.Type == NetMessageType.World) return MatchTrafficClass.World;
        if (header.Type == NetMessageType.Debug) return MatchTrafficClass.BestEffort;
        if (header.Type == NetMessageType.Event
            && ReliableEventPacket.TryRead(datagram[NetHeader.Size..], out _,
                out ReliableEventType eventType, out _))
        {
            return eventType switch
            {
                ReliableEventType.Welcome or ReliableEventType.MapTransition
                    or ReliableEventType.ObserverTransition or ReliableEventType.Disconnect
                    or ReliableEventType.Kill or ReliableEventType.MatchState
                    or ReliableEventType.WorldEvent or ReliableEventType.TimingProfile
                    => MatchTrafficClass.CriticalReliable,
                _ => MatchTrafficClass.NormalReliable
            };
        }
        return header.Type is NetMessageType.Accepted or NetMessageType.Refused
            or NetMessageType.JoinPending or NetMessageType.Ack
            ? MatchTrafficClass.CriticalReliable : MatchTrafficClass.NormalReliable;
    }

    private static int ClassIndex(MatchTrafficClass trafficClass)
    {
        int index = (int)trafficClass;
        if ((uint)index >= TrafficClassCount) throw new ArgumentOutOfRangeException(nameof(trafficClass));
        return index;
    }

    private static void ObserveHighWater(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }

    private static void ObserveHighWater(ref long target, long value)
    {
        while (true)
        {
            long current = Interlocked.Read(ref target);
            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }

    private void SendPhysical(INetTransport physical, NetTrafficMetrics hubMetrics,
        IPEndPoint target, ReadOnlySpan<byte> bytes, long hold, MatchTrafficClass trafficClass)
    {
        try
        {
            physical.SendDatagram(target, bytes, hold);
            Metrics.Sent(bytes.Length);
            hubMetrics.Sent(bytes.Length);
            Interlocked.Increment(ref _classSent[(int)trafficClass]);
        }
        catch (System.Net.Sockets.SocketException)
        {
            Metrics.SendFailed();
            hubMetrics.SendFailed();
        }
    }

    public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default)
    {
        if (target == null) SetKeepAlives(ReadOnlySpan<NetKeepAlive>.Empty);
        else SetKeepAlives(new[] { new NetKeepAlive(target, datagram.ToArray()) });
    }

    public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries)
    {
        if (entries.Length > MaxConnections) throw new ArgumentOutOfRangeException(nameof(entries));
        var copies = new NetKeepAlive[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            NetKeepAlive entry = entries[i];
            if (entry.Endpoint == null || entry.Datagram.Length != NetHeader.Size
                || !NetHeader.TryRead(entry.Datagram.Span, out NetHeader header)
                || header.Type != NetMessageType.KeepAlive)
                throw new ArgumentException("Invalid authoritative keepalive.", nameof(entries));
            copies[i] = new(new IPEndPoint(entry.Endpoint.Address, entry.Endpoint.Port),
                entry.Datagram.ToArray());
        }
        lock (_gate)
        {
            if (!_disposed)
            {
                RetireKeepAlivesLocked(_authenticatedKeepAlives);
                _authenticatedKeepAlives = Array.Empty<AuthenticatedKeepAlive>();
                _authenticatedKeepAliveCursor = 0;
                _keepAlives = copies;
                _nextKeepAlive = 0;
            }
        }
    }

    public void SetKeepAliveDescriptors(ReadOnlySpan<NetKeepAliveDescriptor> entries)
    {
        if (entries.Length > MaxConnections) throw new ArgumentOutOfRangeException(nameof(entries));
        lock (_gate)
        {
            if (!_disposed)
            {
                AuthenticatedKeepAlive[] previous = _authenticatedKeepAlives;
                var copies = entries.IsEmpty
                    ? Array.Empty<AuthenticatedKeepAlive>()
                    : new AuthenticatedKeepAlive[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    NetKeepAliveDescriptor entry = entries[i];
                    entry.Validate();
                    AuthenticatedKeepAlive? prior = FindKeepAlive(previous, entry);
                    copies[i] = new AuthenticatedKeepAlive(
                        new IPEndPoint(new IPAddress(entry.Endpoint.Address.GetAddressBytes()), entry.Endpoint.Port),
                        entry.ConnectionId,
                        prior == null ? entry.Counter : Math.Max(entry.Counter, prior.Counter),
                        entry.Key.ToArray(), entry.Direction)
                    {
                        Retired = prior?.Retired == true
                    };
                }
                RetireKeepAlivesLocked(previous);
                _keepAlives = Array.Empty<NetKeepAlive>();
                _keepAliveCursor = 0;
                _authenticatedKeepAlives = copies;
                _authenticatedKeepAliveCursor = 0;
                _nextKeepAlive = 0;
            }
        }
    }

    public void AnswerPingsImmediately() { }
    public void EnqueueForPlayback(byte[] data, int length)
        => throw new NotSupportedException("Playback cannot bypass worker routing.");

    internal void CloseFromHub()
    {
        lock (_gate)
        {
            _disposed = true;
            _inbound.Clear();
            foreach (SlotQueue queue in _outbound) queue.Clear();
            _snapshots.Clear();
            _outboundCount = 0;
            _ordinaryUsed = 0;
            _criticalReserveUsed = 0;
            _freeCount = _capacity;
            _keepAlives = Array.Empty<NetKeepAlive>();
            _keepAliveCursor = 0;
            RetireKeepAlivesLocked(_authenticatedKeepAlives);
            _authenticatedKeepAlives = Array.Empty<AuthenticatedKeepAlive>();
            _authenticatedKeepAliveCursor = 0;
        }
    }

    public void Dispose()
    {
        CloseFromHub();
        _hub.Unregister(this);
    }
}
