using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using ProjectPrime.Server.Shared;

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
    private readonly TimeProvider _clock;
    private readonly long _clockFrequency;
    private readonly long _admissionRetransmissionGraceTicks;
    private readonly object _gate = new();
    // Serializes lifetime publication of retired match counters with readers.
    // Keep this as the outer lock for retirement paths; no match lock is taken
    // while it is held, avoiding hub/match lock inversion.
    private readonly object _criticalDropsGate = new();
    private readonly object _ioGate = new();
    private long _routingBudgetExhaustions;
    private long _datagramsAttempted;
    private long _datagramBudgetExhaustions;
    private readonly BoundedPercentileSampler _pumpDurations = new();
    private readonly WorkerNetworkLoopMetrics _networkLoop = new();
    private Action? _networkWakeSignal;
    public const int MaxRoutingAttemptsPerPump = 256;
    internal const int AdmissionRouteLimitPerMatch = MultiplayerLimits.MaxAdmissionLeasesPerMatch;
    public long RoutingBudgetExhaustions => Interlocked.Read(ref _routingBudgetExhaustions);
    private readonly Dictionary<uint, MatchDatagramTransport> _matches = new();
    private readonly Dictionary<ulong, ConnectionRoute> _connections = new();
    private readonly Dictionary<Guid, AdmissionRoute> _admissions = new();
    private readonly List<Guid> _expiredAdmissions = new();
    private readonly JoinSourceIngressLimiter _joinIngress = new();
    private MatchDatagramTransport[] _activeMatches = Array.Empty<MatchDatagramTransport>();
    private readonly ReceivedPacket[] _receiveBuffer = new ReceivedPacket[MaxRoutingAttemptsPerPump];
    private ulong _nextConnection = NetConnection.NewIdentity();
    private int _flushCursor;
    private MatchDatagramTransport? _flushCursorMatch;
    private bool _disposed;
    private int _pumping;
    private long _preAuthIngressDrops;
    private long _establishedIngressDrops;
    private long _admissionIngressDrops;
    private long _perConnectionQuotaDrops;
    private int _maximumConnectionIngressDepth;
    private long _retiredCriticalTransportDrops;
    public Guid Incarnation { get; }
    public int LocalPort => _physical.LocalPort;
    public NetTrafficMetrics Metrics { get; } = new();
    public int MatchLimit { get; }
    public bool WorkerGlobalNetworkBudgetEnabled { get; }
    public bool UdpAuthenticationEnabled { get; }
    public int MaximumDatagramsPerPump { get; }
    public TimeSpan AdmissionRetransmissionGrace { get; }
    public static TimeSpan DefaultAdmissionRetransmissionGrace => TimeSpan.FromSeconds(3);
    public long DatagramsAttempted => Interlocked.Read(ref _datagramsAttempted);
    public long DatagramBudgetExhaustions => Interlocked.Read(ref _datagramBudgetExhaustions);
    public long PreAuthIngressDrops => Interlocked.Read(ref _preAuthIngressDrops);
    public long EstablishedIngressDrops => Interlocked.Read(ref _establishedIngressDrops);
    public long AdmissionIngressDrops => Interlocked.Read(ref _admissionIngressDrops);
    public long PerConnectionQuotaDrops => Interlocked.Read(ref _perConnectionQuotaDrops);
    public int MaximumConnectionIngressDepth => Volatile.Read(ref _maximumConnectionIngressDepth);
    public int ActiveAdmissionRouteCount { get { lock (_gate) return _admissions.Count; } }
    internal int CountAdmissionRoutes(MatchDatagramTransport match)
    {
        lock (_gate) return _admissions.Values.Count(route => route.Match == match);
    }
    public long CriticalTransportDrops
    {
        get
        {
            lock (_criticalDropsGate)
            {
                long total = Interlocked.Read(ref _retiredCriticalTransportDrops);
                foreach (MatchDatagramTransport match in Volatile.Read(ref _activeMatches))
                    total += match.CriticalTransportDrops;
                return total;
            }
        }
    }
    public BoundedPercentileSnapshot PumpDurationPercentiles
    {
        get { lock (_ioGate) return _pumpDurations.Snapshot(); }
    }
    public WorkerNetworkLoopSnapshot NetworkLoopDiagnostics => _networkLoop.Snapshot();

    public WorkerNetworkHub(INetTransport physical, Guid incarnation, IWorkerDatagramRouter router,
        int matchLimit = 64, int maximumDatagramsPerPump = DefaultMaximumDatagramsPerPump,
        bool workerGlobalNetworkBudgetEnabled = true, bool udpAuthenticationEnabled = false,
        TimeSpan? admissionRetransmissionGrace = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(router);
        if (incarnation == Guid.Empty) throw new ArgumentException("Worker incarnation is required.", nameof(incarnation));
        if (matchLimit is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(matchLimit));
        if (maximumDatagramsPerPump is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(maximumDatagramsPerPump));
        TimeSpan grace = admissionRetransmissionGrace ?? DefaultAdmissionRetransmissionGrace;
        if (grace < TimeSpan.FromSeconds(2) || grace > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(admissionRetransmissionGrace),
                "Admission retransmission grace must be between 2 and 5 seconds.");
        _clock = clock ?? TimeProvider.System;
        _clockFrequency = _clock.TimestampFrequency;
        if (_clockFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(clock));
        _admissionRetransmissionGraceTicks = checked((long)Math.Ceiling(
            grace.TotalSeconds * _clockFrequency));
        if (_admissionRetransmissionGraceTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(admissionRetransmissionGrace));
        _physical = physical; _router = router; Incarnation = incarnation; MatchLimit = matchLimit;
        MaximumDatagramsPerPump = maximumDatagramsPerPump;
        WorkerGlobalNetworkBudgetEnabled = workerGlobalNetworkBudgetEnabled;
        UdpAuthenticationEnabled = udpAuthenticationEnabled;
        AdmissionRetransmissionGrace = grace;
    }

    /// <summary>Attaches the worker-owned event used to wake the I/O loop.</summary>
    public void SetNetworkWake(Action? signal)
    {
        Volatile.Write(ref _networkWakeSignal, signal);
        _physical.SetNetworkWake(signal);
        foreach (MatchDatagramTransport match in Volatile.Read(ref _activeMatches))
            match.SetNetworkWake(signal);
    }

    internal void SignalNetworkWork()
    {
        Volatile.Read(ref _networkWakeSignal)?.Invoke();
    }

    public bool HasReadyNetworkWork
    {
        get
        {
            if (_physical.HasReadyNetworkWork) return true;
            long now = _clock.GetTimestamp();
            lock (_gate)
            {
                foreach (AdmissionRoute admission in _admissions.Values)
                    if (admission.ExpiresAtTimestamp <= now) return true;
            }
            foreach (MatchDatagramTransport match in Volatile.Read(ref _activeMatches))
                if (match.HasReadyNetworkWork) return true;
            return false;
        }
    }

    public long NextNetworkDeadlineTimestamp
    {
        get
        {
            long deadline = _physical.NextNetworkDeadlineTimestamp;
            foreach (MatchDatagramTransport match in Volatile.Read(ref _activeMatches))
                deadline = Math.Min(deadline, match.NextNetworkDeadlineTimestamp);
            lock (_gate)
                foreach (AdmissionRoute admission in _admissions.Values)
                    deadline = Math.Min(deadline, admission.ExpiresAtTimestamp);
            return deadline;
        }
    }

    internal void RecordNetworkWakeup() => _networkLoop.RecordWakeup();
    internal void RecordImmediateRepump() => _networkLoop.RecordImmediateRepump();
    internal void RecordIdleWait() => _networkLoop.RecordIdleWait();
    internal void ObserveOutboundEnqueueToSendAge(long ageTicks)
        => _networkLoop.RecordOutboundAge(ageTicks);

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
            transport.SetNetworkWake(Volatile.Read(ref _networkWakeSignal));
            _matches.Add(wireMatchId, transport);
            PublishActiveMatches();
            SignalNetworkWork();
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
            foreach (ConnectionRoute owner in _connections.Values)
                if (owner.Match == match) count++;
            if (count >= match.MaxConnections) throw new InvalidOperationException("Match connection route capacity exceeded.");
            // Never reuse an allocated ID during this incarnation, including after route removal.
            if (_nextConnection == ulong.MaxValue) throw new InvalidOperationException("Worker connection identity space exhausted.");
            ulong id = ++_nextConnection;
            _connections.Add(id, new ConnectionRoute(match,
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));
            return id;
        }
    }

    internal void RemoveConnection(MatchDatagramTransport match, ulong id)
    {
        lock (_gate)
            if (_connections.TryGetValue(id, out ConnectionRoute? current)
                && current.Match == match) _connections.Remove(id);
    }

    internal bool RegisterAdmission(Guid admissionId, MatchDatagramTransport match,
        long expiresAtUnixSeconds, out bool created)
    {
        created = false;
        long nowUnix = _clock.GetUtcNow().ToUnixTimeSeconds();
        long nowTimestamp = _clock.GetTimestamp();
        if (admissionId == Guid.Empty || expiresAtUnixSeconds <= nowUnix) return false;
        long remainingSeconds = expiresAtUnixSeconds - nowUnix;
        long durationTicks;
        long deadline;
        try
        {
            durationTicks = checked(remainingSeconds * _clockFrequency);
            deadline = checked(nowTimestamp + Math.Max(1, durationTicks));
        }
        catch (OverflowException) { return false; }
        lock (_gate)
        {
            PurgeExpiredAdmissions(nowTimestamp);
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
            _admissions[admissionId] = new(match, deadline);
            created = true;
            SignalNetworkWork();
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
                SignalNetworkWork();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Shortens a successful admission lease to the bounded retransmission
    /// grace. This is idempotent and scoped to the exact match route; an old
    /// admission cannot affect a superseding route.
    /// </summary>
    internal bool MarkAdmissionEstablished(Guid admissionId, MatchDatagramTransport match)
    {
        if (admissionId == Guid.Empty) return false;
        long nowTimestamp = _clock.GetTimestamp();
        long graceDeadline;
        try { graceDeadline = checked(nowTimestamp + _admissionRetransmissionGraceTicks); }
        catch (OverflowException) { return false; }
        lock (_gate)
        {
            PurgeExpiredAdmissions(nowTimestamp);
            if (!_admissions.TryGetValue(admissionId, out AdmissionRoute route)
                || route.Match != match) return false;
            if (route.Established) return true;
            _admissions[admissionId] = route with
            {
                ExpiresAtTimestamp = Math.Min(route.ExpiresAtTimestamp, graceDeadline),
                Established = true
            };
            SignalNetworkWork();
            return true;
        }
    }

    internal bool ReplaceAdmission(Guid oldAdmissionId, Guid newAdmissionId,
        MatchDatagramTransport match, long expiresAtUnixSeconds, out bool replaced)
    {
        replaced = false;
        if (newAdmissionId == Guid.Empty || oldAdmissionId == newAdmissionId)
            return RegisterAdmission(newAdmissionId, match, expiresAtUnixSeconds, out replaced);
        long nowUnix = _clock.GetUtcNow().ToUnixTimeSeconds();
        long nowTimestamp = _clock.GetTimestamp();
        if (expiresAtUnixSeconds <= nowUnix) return false;
        long deadline;
        try { deadline = checked(nowTimestamp + Math.Max(1, checked((expiresAtUnixSeconds - nowUnix) * _clockFrequency))); }
        catch (OverflowException) { return false; }
        lock (_gate)
        {
            PurgeExpiredAdmissions(nowTimestamp);
            if (_disposed || !_matches.TryGetValue(match.WireMatchId, out MatchDatagramTransport? current)
                || current != match) return false;
            if (_admissions.TryGetValue(newAdmissionId, out AdmissionRoute existing)
                && existing.Match != match) return false;
            if (_admissions.TryGetValue(oldAdmissionId, out AdmissionRoute old)
                && old.Match != match) return false;
            if (!_admissions.ContainsKey(oldAdmissionId)
                && !_admissions.ContainsKey(newAdmissionId))
            {
                int matchAdmissionCount = _admissions.Values.Count(route => route.Match == match);
                if (matchAdmissionCount >= AdmissionRouteLimitPerMatch
                    || _admissions.Count >= MatchLimit * AdmissionRouteLimitPerMatch) return false;
            }
            _admissions.Remove(oldAdmissionId);
            _admissions[newAdmissionId] = new(match, deadline);
            replaced = true;
            SignalNetworkWork();
            return true;
        }
    }

    private void PurgeExpiredAdmissions(long nowTimestamp)
    {
        _expiredAdmissions.Clear();
        foreach ((Guid id, AdmissionRoute route) in _admissions)
            if (route.ExpiresAtTimestamp <= nowTimestamp) _expiredAdmissions.Add(id);
        foreach (Guid id in _expiredAdmissions) _admissions.Remove(id);
    }

    internal void Unregister(MatchDatagramTransport match)
    {
        lock (_criticalDropsGate)
        {
            bool retired = false;
            lock (_gate)
            {
                if (_matches.TryGetValue(match.WireMatchId, out var current) && current == match)
                {
                    _matches.Remove(match.WireMatchId);
                    retired = true;
                }
                var remove = new List<ulong>();
                foreach (var entry in _connections) if (entry.Value.Match == match) remove.Add(entry.Key);
                foreach (ulong id in remove) _connections.Remove(id);
                _expiredAdmissions.Clear();
                foreach (var entry in _admissions) if (entry.Value.Match == match) _expiredAdmissions.Add(entry.Key);
                foreach (Guid admissionId in _expiredAdmissions) _admissions.Remove(admissionId);
                PublishActiveMatches();
            }
            if (retired)
                Interlocked.Add(ref _retiredCriticalTransportDrops, match.CriticalTransportDrops);
            SignalNetworkWork();
        }
    }

    private void PublishActiveMatches()
    {
        var active = new MatchDatagramTransport[_matches.Count];
        _matches.Values.CopyTo(active, 0);
        Volatile.Write(ref _activeMatches, active);
    }

    public void Pump() => PumpOnce();

    internal WorkerNetworkPumpResult PumpOnce()
    {
        if (Interlocked.Exchange(ref _pumping, 1) != 0) throw new InvalidOperationException("Hub has one I/O reader.");
        bool routingBudgetExhausted = false;
        bool flushBudgetExhausted = false;
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
                    long admissionTimestamp = _clock.GetTimestamp();
                    double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    lock (_gate) PurgeExpiredAdmissions(admissionTimestamp);
                    for (int packetIndex = 0; packetIndex < received; packetIndex++)
                    {
                        ReceivedPacket packet = _receiveBuffer[packetIndex];
                        Metrics.Received(packet.Length);
                        if (packet.Length <= 0 || packet.Length > packet.Data.Length || packet.Length > NetConfig.MaxPacketSize
                            || !_router.TryRoute(packet.Data.AsSpan(0, packet.Length), out var route))
                        { Metrics.Reject(); Interlocked.Increment(ref _preAuthIngressDrops); continue; }
                        bool statusQuery = packet.Length == 2
                            && packet.Data[0] == (byte)PacketType.StatusQuery;
                        if (route.IsJoin && !statusQuery
                            && !_joinIngress.TryTake(packet.Sender, now))
                        {
                            Metrics.Reject();
                            Interlocked.Increment(ref _admissionIngressDrops);
                            continue;
                        }
                        MatchDatagramTransport? match = null;
                        ConnectionRoute? connectionRoute = null;
                        bool found = false;
                        lock (_gate)
                        {
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
                                && _connections.TryGetValue(route.ConnectionId, out connectionRoute))
                            {
                                match = connectionRoute.Match;
                                found = true;
                            }
                            if (!found)
                            {
                                Metrics.Reject();
                                if (route.IsJoin) Interlocked.Increment(ref _admissionIngressDrops);
                                else Interlocked.Increment(ref _establishedIngressDrops);
                                continue;
                            }
                            if (match is null)
                            {
                                Metrics.Reject();
                                Interlocked.Increment(ref _preAuthIngressDrops);
                                continue;
                            }
                            if (route.WireMatchId != 0 && route.WireMatchId != match.WireMatchId)
                            {
                                Metrics.Reject();
                                Interlocked.Increment(ref _preAuthIngressDrops);
                                continue;
                            }
                        }
                        if (connectionRoute is not null)
                        {
                            if (!connectionRoute.TakeIngress(now))
                            {
                                Metrics.Reject();
                                Interlocked.Increment(ref _establishedIngressDrops);
                                continue;
                            }
                            if (!connectionRoute.TryReserve())
                            {
                                Metrics.Reject();
                                Interlocked.Increment(ref _perConnectionQuotaDrops);
                                continue;
                            }
                            ObserveHighWater(ref _maximumConnectionIngressDepth,
                                connectionRoute.Queued);
                        }
                        if (!match.Enqueue(packet, connectionRoute))
                            Metrics.DropQueued();
                        else
                            _networkLoop.RecordReceiveToRouteAge(
                                Math.Max(0, Stopwatch.GetTimestamp() - packet.ReceivedAt));
                    }
                    if (received == _receiveBuffer.Length && _physical.QueuedPackets > 0)
                    {
                        Interlocked.Increment(ref _routingBudgetExhaustions);
                        routingBudgetExhausted = true;
                    }
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
                    {
                        Interlocked.Increment(ref _datagramBudgetExhaustions);
                        flushBudgetExhausted = active.Any(match => match.HasReadyNetworkWork);
                    }
                    else if (!WorkerGlobalNetworkBudgetEnabled)
                    {
                        flushBudgetExhausted = active.Any(match => match.HasReadyNetworkWork);
                    }
                }
                finally
                {
                    _pumpDurations.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
        }
        finally { Volatile.Write(ref _pumping, 0); }
        return new WorkerNetworkPumpResult(routingBudgetExhausted, flushBudgetExhausted);
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
        MatchDatagramTransport[] matches;
        Volatile.Write(ref _networkWakeSignal, null);
        _physical.SetNetworkWake(null);
        lock (_ioGate)
        {
            lock (_criticalDropsGate)
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    matches = new MatchDatagramTransport[_matches.Count];
                    _matches.Values.CopyTo(matches, 0);
                    _matches.Clear(); _connections.Clear();
                    _admissions.Clear();
                    Volatile.Write(ref _activeMatches, Array.Empty<MatchDatagramTransport>());
                }
                // The match gate is entered only after the hub gate is
                // released, while the publication gate prevents readers from
                // observing the active-list removal before final capture.
                foreach (MatchDatagramTransport match in matches)
                {
                    match.SetNetworkWake(null);
                    match.CloseFromHub();
                    Interlocked.Add(ref _retiredCriticalTransportDrops, match.CriticalTransportDrops);
                }
            }
            _physical.Dispose();
        }
    }

    private static void ObserveHighWater(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }
}

internal readonly record struct WorkerNetworkPumpResult(
    bool RoutingBudgetExhausted, bool FlushBudgetExhausted)
{
    public bool CanImmediateRepump => RoutingBudgetExhausted || FlushBudgetExhausted;
}

public readonly record struct WorkerNetworkLoopSnapshot(
    long NetworkWakeups,
    long ImmediateRepumps,
    long IdleWaits,
    BoundedPercentileSnapshot ReceiveToRouteAgeMilliseconds,
    BoundedPercentileSnapshot OutboundEnqueueToSendAgeMilliseconds);

internal sealed class WorkerNetworkLoopMetrics
{
    private long _networkWakeups;
    private long _immediateRepumps;
    private long _idleWaits;
    private readonly BoundedPercentileSampler _receiveToRouteAge = new();
    private readonly BoundedPercentileSampler _outboundEnqueueToSendAge = new();

    public void RecordWakeup() => Interlocked.Increment(ref _networkWakeups);
    public void RecordImmediateRepump() => Interlocked.Increment(ref _immediateRepumps);
    public void RecordIdleWait() => Interlocked.Increment(ref _idleWaits);

    public void RecordReceiveToRouteAge(long ageTicks)
    {
        if (ageTicks < 0) ageTicks = 0;
        _receiveToRouteAge.Record(ageTicks * (1000.0 / Stopwatch.Frequency));
    }

    public void RecordOutboundAge(long ageTicks)
    {
        if (ageTicks < 0) ageTicks = 0;
        _outboundEnqueueToSendAge.Record(ageTicks * (1000.0 / Stopwatch.Frequency));
    }

    public WorkerNetworkLoopSnapshot Snapshot()
    {
        _receiveToRouteAge.TrySnapshot(out BoundedPercentileSnapshot receive);
        _outboundEnqueueToSendAge.TrySnapshot(out BoundedPercentileSnapshot outbound);
        return new(Interlocked.Read(ref _networkWakeups),
            Interlocked.Read(ref _immediateRepumps), Interlocked.Read(ref _idleWaits),
            receive, outbound);
    }
}

internal readonly record struct AdmissionRoute(MatchDatagramTransport Match,
    long ExpiresAtTimestamp, bool Established = false);

/// <summary>Fixed-size source-IP limiter for unauthenticated Join abuse.</summary>
internal sealed class JoinSourceIngressLimiter
{
    private const int Capacity = 128;
    private sealed class Entry
    {
        public IPAddress? Address;
        public NetRateLimit Limit;
        public double LastSeen;
    }

    private readonly Entry[] _entries = new Entry[Capacity];

    public JoinSourceIngressLimiter()
    {
        for (int i = 0; i < _entries.Length; i++) _entries[i] = new Entry();
    }

    public bool TryTake(IPEndPoint sender, double now)
    {
        IPAddress address = sender.Address;
        int free = -1;
        int oldest = 0;
        double oldestTime = double.PositiveInfinity;
        for (int i = 0; i < _entries.Length; i++)
        {
            Entry entry = _entries[i];
            if (entry.Address?.Equals(address) == true)
            {
                entry.LastSeen = now;
                return entry.Limit.Take(now);
            }
            if (entry.Address == null && free < 0) free = i;
            if (entry.Address != null && entry.LastSeen < oldestTime)
            {
                oldest = i;
                oldestTime = entry.LastSeen;
            }
        }
        Entry selected = _entries[free >= 0 ? free : oldest];
        selected.Address = address;
        selected.Limit = new NetRateLimit(20, 40, now);
        selected.LastSeen = now;
        return selected.Limit.Take(now);
    }
}
