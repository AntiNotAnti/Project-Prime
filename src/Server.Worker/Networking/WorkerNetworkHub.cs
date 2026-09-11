using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>
/// Sole physical transport owner. One I/O lane calls Pump; simulation lanes only use virtual transports.
/// Routing performs bounded work per pump and never creates state for unknown senders.
/// </summary>
public sealed class WorkerNetworkHub : IDisposable
{
    public const int DefaultMaximumDatagramsPerPump = NetConfig.DefaultMaximumDatagramsPerPump;
    private readonly INetTransport _physical;
    private readonly IWorkerDatagramRouter _router;
    private readonly object _gate = new();
    private readonly object _ioGate = new();
    private long _routingBudgetExhaustions;
    private long _datagramsAttempted;
    private long _datagramBudgetExhaustions;
    private readonly BoundedPercentileSampler _pumpDurations = new();
    public const int MaxRoutingAttemptsPerPump = 256;
    internal const int AdmissionRouteLimitPerMatch = 64;
    public long RoutingBudgetExhaustions => Interlocked.Read(ref _routingBudgetExhaustions);
    private readonly Dictionary<uint, MatchDatagramTransport> _matches = new();
    private readonly Dictionary<ulong, MatchDatagramTransport> _connections = new();
    private readonly Dictionary<Guid, AdmissionRoute> _admissions = new();
    private readonly List<Guid> _expiredAdmissions = new();
    private MatchDatagramTransport[] _activeMatches = Array.Empty<MatchDatagramTransport>();
    private readonly ReceivedPacket[] _receiveBuffer = new ReceivedPacket[MaxRoutingAttemptsPerPump];
    private ulong _nextConnection = NetConnection.NewIdentity();
    private int _flushCursor;
    private MatchDatagramTransport? _flushCursorMatch;
    private bool _disposed;
    private int _pumping;
    public Guid Incarnation { get; }
    public int LocalPort => _physical.LocalPort;
    public NetTrafficMetrics Metrics { get; } = new();
    public int MatchLimit { get; }
    public bool WorkerGlobalNetworkBudgetEnabled { get; }
    public bool UdpAuthenticationEnabled { get; }
    public int MaximumDatagramsPerPump { get; }
    public long DatagramsAttempted => Interlocked.Read(ref _datagramsAttempted);
    public long DatagramBudgetExhaustions => Interlocked.Read(ref _datagramBudgetExhaustions);
    public BoundedPercentileSnapshot PumpDurationPercentiles
    {
        get { lock (_ioGate) return _pumpDurations.Snapshot(); }
    }

    public WorkerNetworkHub(INetTransport physical, Guid incarnation, IWorkerDatagramRouter router,
        int matchLimit = 64, int maximumDatagramsPerPump = DefaultMaximumDatagramsPerPump,
        bool workerGlobalNetworkBudgetEnabled = true, bool udpAuthenticationEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(router);
        if (incarnation == Guid.Empty) throw new ArgumentException("Worker incarnation is required.", nameof(incarnation));
        if (matchLimit is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(matchLimit));
        if (maximumDatagramsPerPump is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(maximumDatagramsPerPump));
        _physical = physical; _router = router; Incarnation = incarnation; MatchLimit = matchLimit;
        MaximumDatagramsPerPump = maximumDatagramsPerPump;
        WorkerGlobalNetworkBudgetEnabled = workerGlobalNetworkBudgetEnabled;
        UdpAuthenticationEnabled = udpAuthenticationEnabled;
        _physical.AnswerPingsImmediately();
    }

    public MatchDatagramTransport RegisterMatch(uint wireMatchId, int queueCapacity = 2048,
        int drainBudget = 256, int maxConnections = 32, bool queueV2Enabled = true,
        int criticalReserve = 32)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (wireMatchId == 0) throw new ArgumentOutOfRangeException(nameof(wireMatchId));
            if (_router.LegacySingleMatchId is uint legacy && (legacy != wireMatchId || _matches.Count != 0))
                throw new InvalidOperationException("Legacy join framing supports exactly one explicitly pinned match.");
            if (_matches.Count >= MatchLimit || _matches.ContainsKey(wireMatchId)) throw new InvalidOperationException("Match route is duplicate or worker capacity is full.");
            var transport = new MatchDatagramTransport(this, wireMatchId, queueCapacity,
                drainBudget, maxConnections, queueV2Enabled, criticalReserve);
            _matches.Add(wireMatchId, transport);
            PublishActiveMatches();
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

    internal bool RegisterAdmission(Guid admissionId, MatchDatagramTransport match,
        long expiresAtUnixSeconds, out bool created)
    {
        created = false;
        if (admissionId == Guid.Empty || expiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        lock (_gate)
        {
            PurgeExpiredAdmissions(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (_disposed || !_matches.TryGetValue(match.WireMatchId, out MatchDatagramTransport? current)
                || current != match) return false;
            if (_admissions.TryGetValue(admissionId, out AdmissionRoute existing))
            {
                if (existing.Match != match) return false;
                // Admission IDs are immutable leases. A repeated install is
                // idempotent; do not extend the route before the authority
                // accepts the corresponding key/lease.
                return true;
            }
            int matchAdmissionCount = 0;
            foreach (AdmissionRoute route in _admissions.Values)
                if (route.Match == match) matchAdmissionCount++;
            if (matchAdmissionCount >= AdmissionRouteLimitPerMatch
                || _admissions.Count >= MatchLimit * AdmissionRouteLimitPerMatch) return false;
            _admissions[admissionId] = new(match, expiresAtUnixSeconds);
            created = true;
            return true;
        }
    }

    internal bool UnregisterAdmission(Guid admissionId, MatchDatagramTransport match)
    {
        if (admissionId == Guid.Empty) return false;
        lock (_gate)
        {
            if (_admissions.TryGetValue(admissionId, out AdmissionRoute route)
                && route.Match == match)
            {
                _admissions.Remove(admissionId);
                return true;
            }
            return false;
        }
    }

    private void PurgeExpiredAdmissions(long nowUnixSeconds)
    {
        _expiredAdmissions.Clear();
        foreach ((Guid id, AdmissionRoute route) in _admissions)
            if (route.ExpiresAtUnixSeconds <= nowUnixSeconds) _expiredAdmissions.Add(id);
        foreach (Guid id in _expiredAdmissions) _admissions.Remove(id);
    }

    internal void Unregister(MatchDatagramTransport match)
    {
        lock (_gate)
        {
            if (_matches.TryGetValue(match.WireMatchId, out var current) && current == match) _matches.Remove(match.WireMatchId);
            var remove = new List<ulong>();
            foreach (var entry in _connections) if (entry.Value == match) remove.Add(entry.Key);
            foreach (ulong id in remove) _connections.Remove(id);
            _expiredAdmissions.Clear();
            foreach (var entry in _admissions) if (entry.Value.Match == match) _expiredAdmissions.Add(entry.Key);
            foreach (Guid admissionId in _expiredAdmissions) _admissions.Remove(admissionId);
            PublishActiveMatches();
        }
    }

    private void PublishActiveMatches()
    {
        var active = new MatchDatagramTransport[_matches.Count];
        _matches.Values.CopyTo(active, 0);
        Volatile.Write(ref _activeMatches, active);
    }

    public void Pump()
    {
        if (Interlocked.Exchange(ref _pumping, 1) != 0) throw new InvalidOperationException("Hub has one I/O reader.");
        try
        {
            lock (_ioGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                long started = Stopwatch.GetTimestamp();
                try
                {
                    // The physical transport already bounds its drain; enforce a worker budget as well.
                    int received = _physical.Drain(_receiveBuffer);
                    for (int packetIndex = 0; packetIndex < received; packetIndex++)
                    {
                        ReceivedPacket packet = _receiveBuffer[packetIndex];
                        Metrics.Received(packet.Length);
                        if (packet.Length <= 0 || packet.Length > packet.Data.Length || packet.Length > NetConfig.MaxPacketSize
                            || !_router.TryRoute(packet.Data.AsSpan(0, packet.Length), out var route))
                        { Metrics.Reject(); continue; }
                        lock (_gate)
                        {
                            PurgeExpiredAdmissions(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                            MatchDatagramTransport? match = null;
                            bool found = false;
                            if (route.IsJoin && route.ConnectionId == 0)
                            {
                                if (route.AdmissionId != Guid.Empty && UdpAuthenticationEnabled
                                    && _admissions.TryGetValue(route.AdmissionId, out AdmissionRoute admission))
                                {
                                    match = admission.Match;
                                    found = true;
                                }
                                else if (route.AdmissionId == Guid.Empty && !UdpAuthenticationEnabled
                                    && _matches.TryGetValue(route.WireMatchId, out MatchDatagramTransport? legacyMatch))
                                {
                                    match = legacyMatch;
                                    found = true;
                                }
                            }
                            else if (!route.IsJoin && route.ConnectionId != 0
                                && _connections.TryGetValue(route.ConnectionId, out MatchDatagramTransport? establishedMatch))
                            {
                                match = establishedMatch;
                                found = true;
                            }
                            if (!found) { Metrics.Reject(); continue; }
                            if (match is null) { Metrics.Reject(); continue; }
                            if (route.WireMatchId != 0 && route.WireMatchId != match.WireMatchId)
                            { Metrics.Reject(); continue; }
                            if (!match.Enqueue(packet)) Metrics.DropQueued();
                        }
                    }
                    if (received == _receiveBuffer.Length && _physical.QueuedPackets > 0)
                        Interlocked.Increment(ref _routingBudgetExhaustions);
                    MatchDatagramTransport[] active = Volatile.Read(ref _activeMatches);
                    int remaining = MaximumDatagramsPerPump;
                    int attemptedThisPump = 0;
                    if (active.Length != 0)
                    {
                        if (!WorkerGlobalNetworkBudgetEnabled)
                        {
                            // Rollback mode preserves the former per-match
                            // scheduling contract: every match may consume
                            // its own bounded drain budget, with no worker-wide
                            // cap. The transport itself still counts attempts.
                            attemptedThisPump = 0;
                            foreach (MatchDatagramTransport match in active)
                                attemptedThisPump += match.Flush(_physical, Metrics, int.MaxValue);
                            remaining = MaximumDatagramsPerPump;
                        }
                        else
                        {
                            int start = ResolveFlushStart(active);
                            int index = start;
                            int visited = 0;
                            int roundAttempts = 0;
                            bool allExhausted = true;
                            int quantum = Math.Max(1, MaximumDatagramsPerPump / active.Length);
                            while (remaining > 0)
                            {
                                MatchDatagramTransport match = active[index];
                                int request = Math.Min(remaining, quantum);
                                int attempted = match.Flush(_physical, Metrics, request);
                                remaining -= attempted;
                                attemptedThisPump = MaximumDatagramsPerPump - remaining;
                                roundAttempts += attempted;
                                visited++;
                                _flushCursorMatch = match;
                                // A match that did not fill its request, or
                                // consumed its final queued datagram, has no
                                // reason to receive an extra no-op flush this
                                // pump. A saturated match with work remaining
                                // keeps the next round alive.
                                if (attempted == request && match.HeldOutgoingPackets > 0)
                                    allExhausted = false;
                                if (remaining == 0) break;
                                index = (index + 1) % active.Length;
                                if (visited == active.Length)
                                {
                                    if (roundAttempts == 0 || allExhausted) break;
                                    visited = 0;
                                    roundAttempts = 0;
                                    allExhausted = true;
                                }
                            }
                            _flushCursor = (index + 1) % active.Length;
                        }
                    }
                    else
                    {
                        _flushCursor = 0;
                        _flushCursorMatch = null;
                        attemptedThisPump = 0;
                    }
                    Interlocked.Add(ref _datagramsAttempted, attemptedThisPump);
                    if (WorkerGlobalNetworkBudgetEnabled && remaining == 0)
                        Interlocked.Increment(ref _datagramBudgetExhaustions);
                }
                finally
                {
                    _pumpDurations.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
        }
        finally { Volatile.Write(ref _pumping, 0); }
    }

    private int ResolveFlushStart(MatchDatagramTransport[] active)
    {
        if (_flushCursorMatch != null)
        {
            for (int i = 0; i < active.Length; i++)
            {
                if (ReferenceEquals(active[i], _flushCursorMatch))
                    return (i + 1) % active.Length;
            }
        }
        return (uint)_flushCursor < (uint)active.Length ? _flushCursor : 0;
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
            _admissions.Clear();
            Volatile.Write(ref _activeMatches, Array.Empty<MatchDatagramTransport>());
            _physical.Dispose();
        }
    }
}

internal readonly record struct AdmissionRoute(MatchDatagramTransport Match, long ExpiresAtUnixSeconds);
