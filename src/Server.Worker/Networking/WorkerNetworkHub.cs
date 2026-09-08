using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>
/// Sole physical transport owner. One I/O lane calls Pump; simulation lanes only use virtual transports.
/// Routing performs bounded work per pump and never creates state for unknown senders.
/// </summary>
public sealed class WorkerNetworkHub : IDisposable
{
    private readonly INetTransport _physical;
    private readonly IWorkerDatagramRouter _router;
    private readonly object _gate = new();
    private readonly object _ioGate = new();
    private long _routingBudgetExhaustions;
    public const int MaxRoutingAttemptsPerPump = 256;
    public long RoutingBudgetExhaustions => Interlocked.Read(ref _routingBudgetExhaustions);
    private readonly Dictionary<uint, MatchDatagramTransport> _matches = new();
    private readonly Dictionary<ulong, MatchDatagramTransport> _connections = new();
    private ulong _nextConnection = NetConnection.NewIdentity();
    private bool _disposed;
    private int _pumping;
    public Guid Incarnation { get; }
    public int LocalPort => _physical.LocalPort;
    public NetTrafficMetrics Metrics { get; } = new();
    public int MatchLimit { get; }

    public WorkerNetworkHub(INetTransport physical, Guid incarnation, IWorkerDatagramRouter router, int matchLimit = 64)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(router);
        if (incarnation == Guid.Empty) throw new ArgumentException("Worker incarnation is required.", nameof(incarnation));
        if (matchLimit is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(matchLimit));
        _physical = physical; _router = router; Incarnation = incarnation; MatchLimit = matchLimit;
        _physical.AnswerPingsImmediately();
    }

    public MatchDatagramTransport RegisterMatch(uint wireMatchId, int queueCapacity = 2048, int drainBudget = 256, int maxConnections = 32)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (wireMatchId == 0) throw new ArgumentOutOfRangeException(nameof(wireMatchId));
            if (_router.LegacySingleMatchId is uint legacy && (legacy != wireMatchId || _matches.Count != 0))
                throw new InvalidOperationException("Legacy join framing supports exactly one explicitly pinned match.");
            if (_matches.Count >= MatchLimit || _matches.ContainsKey(wireMatchId)) throw new InvalidOperationException("Match route is duplicate or worker capacity is full.");
            var transport = new MatchDatagramTransport(this, wireMatchId, queueCapacity, drainBudget, maxConnections);
            _matches.Add(wireMatchId, transport);
            return transport;
        }
    }

    internal ulong AllocateConnection(MatchDatagramTransport match, Guid incarnation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (incarnation != Incarnation || !_matches.TryGetValue(match.WireMatchId, out var current) || current != match)
                throw new InvalidOperationException("Stale match or worker incarnation.");
            int count = 0;
            foreach (var owner in _connections.Values) if (owner == match) count++;
            if (count >= match.MaxConnections) throw new InvalidOperationException("Match connection route capacity exceeded.");
            // Never reuse an allocated ID during this incarnation, including after route removal.
            if (_nextConnection == ulong.MaxValue) throw new InvalidOperationException("Worker connection identity space exhausted.");
            ulong id = ++_nextConnection;
            _connections.Add(id, match);
            return id;
        }
    }

    internal void RemoveConnection(MatchDatagramTransport match, ulong id)
    {
        lock (_gate)
            if (_connections.TryGetValue(id, out var current) && current == match) _connections.Remove(id);
    }

    internal void Unregister(MatchDatagramTransport match)
    {
        lock (_gate)
        {
            if (_matches.TryGetValue(match.WireMatchId, out var current) && current == match) _matches.Remove(match.WireMatchId);
            var remove = new List<ulong>();
            foreach (var entry in _connections) if (entry.Value == match) remove.Add(entry.Key);
            foreach (ulong id in remove) _connections.Remove(id);
        }
    }

    public void Pump()
    {
        if (Interlocked.Exchange(ref _pumping, 1) != 0) throw new InvalidOperationException("Hub has one I/O reader.");
        try
        {
            lock (_ioGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // The physical transport already bounds its drain; enforce a worker budget as well.
                int received = 0;
                foreach (ReceivedPacket packet in _physical.Drain())
                {
                    if (++received > MaxRoutingAttemptsPerPump)
                    { Metrics.DropQueued(); Interlocked.Increment(ref _routingBudgetExhaustions); break; }
                    Metrics.Received(packet.Length);
                    if (packet.Length <= 0 || packet.Length > packet.Data.Length || packet.Length > NetConfig.MaxPacketSize
                        || !_router.TryRoute(packet.Data.AsSpan(0, packet.Length), out var route))
                    { Metrics.Reject(); continue; }
                    lock (_gate)
                    {
                        MatchDatagramTransport? match;
                        bool found = route.IsJoin
                            ? route.ConnectionId == 0 && _matches.TryGetValue(route.WireMatchId, out match)
                            : route.ConnectionId != 0 && _connections.TryGetValue(route.ConnectionId, out match);
                        if (!found) { Metrics.Reject(); continue; }
                        match = route.IsJoin ? _matches[route.WireMatchId] : _connections[route.ConnectionId];
                        if (route.WireMatchId != 0 && route.WireMatchId != match.WireMatchId)
                        { Metrics.Reject(); continue; }
                        if (!match.Enqueue(packet)) Metrics.DropQueued();
                    }
                }
                MatchDatagramTransport[] matches;
                lock (_gate) { matches = new MatchDatagramTransport[_matches.Count]; _matches.Values.CopyTo(matches, 0); }
                foreach (var match in matches) match.Flush(_physical, Metrics);
            }
        }
        finally { Volatile.Write(ref _pumping, 0); }
    }

    public void Dispose()
    {
        lock (_ioGate)
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var match in _matches.Values) match.CloseFromHub();
            _matches.Clear(); _connections.Clear();
            _physical.Dispose();
        }
    }
}
