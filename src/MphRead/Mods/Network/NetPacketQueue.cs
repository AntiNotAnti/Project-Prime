using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Network;

public enum NetPacketPriority { Critical, Realtime, Background }
public readonly record struct NetPumpBudget(int Critical, int Realtime, int Background)
{
    public static NetPumpBudget Default => new(128, 256, 32);
    public static NetPumpBudget BeforeSimulation => new(128, 256, 0);
    public static NetPumpBudget AfterSimulation => new(0, 0, 32);
}

/// <summary>Bounded priority inbox. FIFO within each semantic category.
/// Successful enqueue transfers buffer ownership; dequeue transfers it back.</summary>
public sealed class NetPacketQueue
{
    private readonly object _lock = new();
    private readonly Queue<ReceivedPacket>[] _queues = { new(128), new(2048), new(32) };
    private readonly int _capacity, _normalLimit;
    private readonly Queue<ReceivedPacket> _cosmeticQueue = new(64);
    private int _count, _high, _cosmetics;
    private long _drops;
    public NetPacketQueue(int capacity = 2048, int criticalReserve = 128)
    {
        if (capacity < 1 || criticalReserve < 0 || criticalReserve > capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _normalLimit = capacity - criticalReserve;
    }
    public int Count => Volatile.Read(ref _count);
    public int HighWater { get { lock (_lock) return _high; } }
    public long Drops { get { lock (_lock) return _drops; } }
    public bool CanAcceptCritical { get { lock (_lock) return _count - _cosmetics < _capacity; } }
    public bool CanAccept(PacketType type)
    {
        lock (_lock) return Accepts(type);
    }
    private bool Accepts(PacketType type) => type == PacketType.LiveCombatImpact
        ? _cosmetics < 64 && _count - _cosmetics < Math.Max(0, _normalLimit - 128)
        : _count - _cosmetics < (Priority(type) == NetPacketPriority.Critical ? _capacity : _normalLimit);
    public static NetPacketPriority Priority(PacketType type) => type switch
    {
        PacketType.MatchSemanticEvent or PacketType.MatchAward or PacketType.ReplayShotFact
            => NetPacketPriority.Background,
        PacketType.LiveCombatImpact or PacketType.Intent or PacketType.SlotIntent or PacketType.Snapshot or PacketType.SnapshotFast or PacketType.PlayerSlowState or PacketType.WorldState or PacketType.HitClaim or PacketType.HitVerdict
            or PacketType.ReplayWorld or PacketType.MatchStartCommit => NetPacketPriority.Realtime,
        PacketType.QueueHello or PacketType.QueueWelcome or PacketType.QueueJoin or PacketType.QueueLeave
            or PacketType.QueueState or PacketType.QueueSeatOffer or PacketType.QueueAccept or PacketType.QueueDecline
            or PacketType.PartyReserveClaim or PacketType.PartyReserveState
            or PacketType.PartyReserveAccept
            or PacketType.HostChallenge or PacketType.HostChallengeReply or PacketType.HostRequest or PacketType.HostReply
            or PacketType.Hello or PacketType.Welcome or PacketType.Bye or PacketType.Refused or PacketType.SessionState
            or PacketType.Roster or PacketType.MatchState or PacketType.MapChange or PacketType.Authority
            or PacketType.WorldReady or PacketType.WorldBootstrap or PacketType.MatchLoaded or PacketType.MatchLoadFailed or PacketType.MatchEnd
            or PacketType.LobbyCommand or PacketType.LobbyCommandResult => NetPacketPriority.Critical,
        _ => NetPacketPriority.Background
    };
    public bool TryEnqueue(ReceivedPacket packet)
    {
        var priority = Priority(packet.Type);
        lock (_lock)
        {
            if (!Accepts(packet.Type)) { _drops++; return false; }
            if (packet.Type == PacketType.LiveCombatImpact)
            { _cosmetics++; _cosmeticQueue.Enqueue(packet); }
            else _queues[(int)priority].Enqueue(packet);
            _count++; _high = Math.Max(_high, _count); return true;
        }
    }
    public bool TryDequeue(NetPacketPriority priority, out ReceivedPacket packet)
    {
        lock (_lock)
        {
            if (!_queues[(int)priority].TryDequeue(out packet)
                && (priority != NetPacketPriority.Realtime || !_cosmeticQueue.TryDequeue(out packet))) return false;
            if (packet.Type == PacketType.LiveCombatImpact) _cosmetics--;
            _count--; return true;
        }
    }
}

/// <summary>Caller owns synchronization. Virtual-time friendly and allocation free.</summary>
public struct NetTokenBucket
{
    private double _tokens, _last;
    private bool _initialized;
    public bool Take(double nowMs, double perSecond, int burst)
    {
        if (!_initialized) { _initialized = true; _tokens = burst; _last = nowMs; }
        _tokens = Math.Min(burst, _tokens + Math.Max(0, nowMs - _last) * perSecond / 1000);
        _last = nowMs;
        if (_tokens < 1) return false;
        _tokens--; return true;
    }
}
