using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Admin;
using MphRead.Replay;
using MphRead.Identity;
using MphRead.Reporting;
using MphRead.Telemetry;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>One single-writer world. Tick performs one step and only bounded queue IO;
/// transport, scheduling, durable delivery and subsequent matches belong to the host.</summary>
/// <remarks>
/// The GC fields are process-wide collection deltas observed while this match
/// was alive. They are not causal or match-attributed allocation evidence.
/// </remarks>
public sealed record MatchPerformanceSnapshot(
    long TickCount,
    long DeadlineMisses,
    BoundedPercentileSnapshot TickDurationMilliseconds,
    long AllocatedBytes,
    double AllocatedBytesPerTick,
    double AllocatedBytesPerSecond,
    long ProcessGen0Collections,
    long ProcessGen1Collections,
    long ProcessGen2Collections);

/// <summary>
/// Immutable control-plane measurements gathered from the match-owned
/// observer history and replay writer. The underlying counters are sampled
/// atomically; this object never exposes mutable simulation state.
/// </summary>
public sealed record MatchDiagnosticsSnapshot(
    long ObserverRetainedFrames,
    long ObserverRetainedBytes,
    long ReplayQueueDepth,
    long ReplayQueueHighWater,
    bool ReplayQueueOverflowed)
{
    public static MatchDiagnosticsSnapshot Empty { get; } = new(0, 0, 0, 0, false);
}

public sealed class MatchInstance : IDisposable
{
    private readonly MatchInstanceOptions _options;
    private readonly MatchContentSnapshot? _contentSnapshot;
    private readonly INetTransport transport;
    private readonly WorldStateCapture world;
    private readonly SnapshotCadence _snapshotCadence;
    private uint tick, snapshotSequence, worldRevision;
    private ServerReplaySession? replay;
    private MatchParticipantLedger? replayLedger;
    private readonly TelemetryCollector? telemetry;
    private const int AwardCapacity = 256;
    private readonly MatchAward[] _awards = new MatchAward[AwardCapacity];
    private int _awardHead, _awardCount;
    private const int SemanticCapacity = 256;
    private readonly MatchEvent[] _semanticEvents = new MatchEvent[SemanticCapacity];
    private int _semanticHead, _semanticCount;
    public long DroppedAwards { get; private set; }
    public long DroppedSemanticEvents { get; private set; }
    public int AwardQueueHighWater { get; private set; }
    public int SemanticQueueHighWater { get; private set; }
    private bool _terminal, _disposed;
    private string? _error;
    private readonly BoundedPercentileSampler _tickDurations = new();
    private readonly long _performanceStarted = Stopwatch.GetTimestamp();
    private readonly int _gen0AtStart = GC.CollectionCount(0);
    private readonly int _gen1AtStart = GC.CollectionCount(1);
    private readonly int _gen2AtStart = GC.CollectionCount(2);
    private int _performanceSequence;
    private long _performanceTicks;
    private long _deadlineMisses;
    private long _allocatedBytes;
    private MatchPerformanceSnapshot _publishedPerformance = new(0, 0, default, 0, 0, 0, 0, 0, 0);
    private ServerTimingTelemetrySnapshot _publishedTimingTelemetry = ServerTimingTelemetrySnapshot.Empty;
    private readonly Queue<string> _diagnostics = new();
    public int DroppedDiagnostics { get; private set; }
    public bool TryDequeueDiagnostic(out string? message) => _diagnostics.TryDequeue(out message);
    private void Diagnose(string message)
    {
        if (_diagnostics.Count < 64) _diagnostics.Enqueue(message);
        else DroppedDiagnostics++;
    }

    public MatchSpec Spec => _options.Spec;
    public Guid MatchId => Spec.MatchId.Value;
    public uint WireMatchId => _options.WireMatchId;
    public MatchInstanceState State { get; private set; }
    public ServerSimulation Simulation { get; }
    public ServerNetwork Network { get; }
    public TournamentAdmin? Admin { get; }
    public ServerReplaySession? Replay => replay;
    public TelemetryCollector? Telemetry => telemetry;
    public Task ReplayCompletion => replay?.Completion ?? Task.CompletedTask;
    public MatchCompletion? Completion { get; private set; }
    public uint NextTick => tick;
    /// <summary>
    /// Immutable observational metrics for this match. Percentile reads are
    /// bounded and do not participate in simulation decisions.
    /// </summary>
    public MatchPerformanceSnapshot Performance
    {
        get
        {
            if (TryReadPerformance(out MatchPerformanceSnapshot snapshot))
            {
                Volatile.Write(ref _publishedPerformance, snapshot);
                return snapshot;
            }
            return Volatile.Read(ref _publishedPerformance);
        }
    }
    /// <summary>
    /// Returns one immutable diagnostic view. Replay and observer counters are
    /// read from their atomic owner-published/scalar sources only when the
    /// heartbeat samples the match, not from the 60 Hz simulation path.
    /// </summary>
    public MatchDiagnosticsSnapshot Diagnostics
    {
        get
        {
            ServerReplayDiagnosticsSnapshot replayDiagnostics = replay?.Diagnostics ?? default;
            return new(Network.ObserverHistoryFrameCount, Network.ObserverHistoryBytes,
                replayDiagnostics.QueueDepth, replayDiagnostics.QueueHighWater,
                replayDiagnostics.QueueOverflowed);
        }
    }
    /// <summary>Owner-published timing freshness observations for diagnostics.</summary>
    internal ServerTimingTelemetrySnapshot TimingTelemetry
        => Volatile.Read(ref _publishedTimingTelemetry);
    public MatchInstanceStatus Status => new(MatchId, WireMatchId, State, tick,
        Simulation.Scene.Match.Phase, Network.Count + Simulation.Bots.Count, _error);
    public event Action<MatchCompletion>? Completed;
    public event Action<MatchFailure>? Failed;

    /// <summary>
    /// Authenticated host-side developer output. It is intentionally separate
    /// from gameplay packets and must be invoked on this match's simulation
    /// lane by the WorkerRuntime.
    /// </summary>
    public string NetDebug(string command, in CombatShot shot,
        Vector3 projectileStart, Vector3 projectileEnd)
        => Simulation.Combat.NetDebug(command, shot, projectileStart, projectileEnd);

    /// <summary>Installs control-plane admission material on this match's owner lane.</summary>
    internal bool TryInstallAdmissionKey(InstallAdmissionKey command, out string reason)
    {
        if (_options.Tickets is not WorkerTicketAuthority authority)
        {
            reason = "admission_unavailable";
            return false;
        }
        bool installed = authority.TryInstallAdmissionKey(command, out reason,
            out Guid supersededAdmissionId);
        if (!installed) return false;
        if (_options.UdpAuthenticationEnabled && !_options.LegacyDynamicAdmission
            && transport is MatchDatagramTransport routed)
        {
            bool routeChanged;
            bool routeAccepted = supersededAdmissionId != Guid.Empty
                ? routed.ReplaceAdmissionId(supersededAdmissionId, command.AdmissionId,
                    command.ExpiresAt, out routeChanged)
                : routed.RegisterAdmissionId(command.AdmissionId, command.ExpiresAt, out routeChanged);
            if (!routeAccepted)
            {
                // Authority publication and route publication are both on this
                // match lane. Roll back the exact new lease if transport
                // capacity unexpectedly changed; never touch a newer lease.
                authority.TryRetireAdmission(new RetireAdmission(command.MatchId,
                    command.NodeSessionId, command.SeatId,
                    command.HandoffGeneration,
                    command.AdmissionId, command.WorkerId, command.WorkerIncarnation), out _);
                reason = "admission_route_capacity";
                return false;
            }
        }
        return true;
    }

    /// <summary>Retires one exact admission lease and its route on the match lane.</summary>
    internal bool TryRetireAdmission(RetireAdmission command, out string reason)
    {
        if (_options.Tickets is not WorkerTicketAuthority authority)
        {
            reason = "admission_unavailable";
            return false;
        }
        bool retired = authority.TryRetireAdmission(command, out reason);
        if (retired && transport is MatchDatagramTransport routed)
            routed.UnregisterAdmissionId(command.AdmissionId);
        return retired;
    }

    /// <summary>
    /// Sends one bounded QZ1.14 diagnostic to the explicitly selected player.
    /// This is called only by the authenticated Node-&gt;Worker admin command;
    /// no client packet can select a command, tick, or recipient.
    /// </summary>
    public bool SendHistoricalDebug(AdminAction action, byte seat)
    {
        if (seat >= Spec.Rules.MaxPlayers || action is not (AdminAction.LagCompHistory
            or AdminAction.LagCompDynamic or AdminAction.LagCompClear))
            return false;
        ServerPeer? peer = Network.AllConnections.ToArray().FirstOrDefault(candidate => candidate?.Slot == seat);
        if (peer == null) return false;
        Span<byte> payload = stackalloc byte[HistoricalCollisionDebugPacket.MaxSize];
        int length;
        if (action == AdminAction.LagCompClear)
        {
            length = HistoricalCollisionDebugPacket.WriteClear(payload, WireMatchId);
        }
        else
        {
            string command = action == AdminAction.LagCompHistory
                ? "netdebug lagcomp-history" : "netdebug lagcomp-dynamic";
            length = Simulation.Combat.WriteHistoricalDebugPacket(WireMatchId, command, payload);
        }
        return Network.TrySendDebug(peer, payload[..length]);
    }

    public MatchInstance(MatchInstanceOptions options, INetTransport transport)
        : this(options, transport, null) { }

    // Only the standalone adapter transfers its legacy session connections between rounds.
    internal MatchInstance(MatchInstanceOptions options, INetTransport transport, ServerNetwork? transferredNetwork)
    {
        options.Spec.Validate();
        if (options.WireMatchId == 0) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options;
        _contentSnapshot = options.ContentSnapshot;
        _snapshotCadence = new SnapshotCadence(options.SnapshotRateHz);
        world = new(options.ValidationFixture != DeveloperValidationFixtureId.None);
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        tick = options.InitialTick;
        if (!options.LegacyDynamicAdmission)
        {
            if (options.EnableAdmin) throw new ArgumentException("Tournament authority belongs to Node; use the match admin bridge for Worker commands.");
            if (options.RequireReplay && Spec.ReplayPolicy != ReplayPolicy.Record
                || options.CollectTelemetry && Spec.TelemetryPolicy != TelemetryPolicy.Record
                || options.BotFill?.MinimumParticipants > 0 && Spec.BotFillPolicy == ProjectPrime.Server.Shared.BotFillPolicy.Disabled)
                throw new ArgumentException("Runtime integration options conflict with the frozen match policies.");
            if (Spec.ReplayPolicy == ReplayPolicy.Record && String.IsNullOrWhiteSpace(options.ReplayDirectory))
                throw new ArgumentException("Recorded matches require a replay destination.");
        }
        BotFillPolicy fill = Spec.BotFillPolicy == ProjectPrime.Server.Shared.BotFillPolicy.Disabled
            ? new() : options.BotFill ?? new(Spec.Roster.Count(seat => seat.Role != SeatRole.Observer));
        using IDisposable? contentScope = _contentSnapshot == null
            ? null : ContentEnvironment.UseMatchContent(_contentSnapshot);
        Simulation = new ServerSimulation(Spec.Rules, options.LagCompEnabled, options.ProjectileCatchUpEnabled,
            fill, Spec.Rng1Seed, Spec.Rng2Seed, options.HistoricalDynamicCollisionEnabled,
            options.ValidationFixture, options.HeadshotValidationScenario,
            options.HeadshotScenarioSeconds);
        try
        {
            if (options.ReportingServerId is { } server)
            {
                Simulation.Reports = new(server, Spec.NodeIncarnation, Spec.Content.BuildVersion, matchId: MatchId);
                Simulation.Reports.ConfigureRoundIdentity(Spec.TournamentId?.ToString("D"), Spec.RoundId?.ToString("D"), null);
            }
            Network = transferredNetwork ?? new ServerNetwork(transport, Spec.Rules, WireMatchId,
                Spec.ObserverPolicy == ObserverPolicy.Disabled ? new ObserverOptions(0) : options.Observers,
                options.UdpAuthenticationEnabled && !options.LegacyDynamicAdmission,
                options.AckCoalescingEnabled);
            Network.ConfigureTiming(options.AdaptiveTimingEnabled, options.AdaptiveInputPlayoutEnabled,
                options.ReliableAdaptiveRtoEnabled, options.AdaptiveTimingV2Enabled);
            Network.AdmissionClosed = !options.LegacyDynamicAdmission && options.Tickets == null;
            Network.RequireRoutedJoins = !options.LegacyDynamicAdmission;
            Network.AdmissionIdentityPolicy = options.LegacyDynamicAdmission ? null : AdmitFrozenSeat;
            if (!options.LegacyDynamicAdmission)
            {
                foreach (var seat in Spec.Roster.Where(seat => seat.Role != SeatRole.Observer))
                {
                    Simulation.Scene.Players[seat.SeatId].PrepareSlot(seat.Hunter, seat.Team);
                    Simulation.Scene.Players[seat.SeatId].TeamIndex = Spec.Rules.Teams ? seat.Team : seat.SeatId;
                    Simulation.Scene.Roster.Nicknames[seat.SeatId] = seat.DisplayName;
                }
                Simulation.Bots.ConfigureFixedRoster(Spec.Roster.Where(seat => seat.Role == SeatRole.Bot)
                    .Select(seat => new BotParticipant(seat.SeatId, NetConnection.NewIdentity(), seat.Hunter, Spec.Rules.Teams ? seat.Team : seat.SeatId, seat.DisplayName)));
            }
            if (Network.MatchId != WireMatchId || Network.Rules != Spec.Rules)
                throw new ArgumentException("Transferred network does not match launch identity/rules.");
            Network.ServerName = options.ServerName;
            Network.TicketAuthority = options.Tickets;
            Network.Phase = Simulation.Scene.Match.Phase;
            Network.PhaseRevision = Simulation.Scene.Match.PhaseRevision;
            Network.CancelBotClaim = slot => Simulation.Bots.CancelClaim(slot);
            Network.CanClaimPlayerSlot = slot => Simulation.Bots.Claim(slot, Network, Network.Tick);
            Network.BotRosterEntry = slot => Simulation.Bots.Roster(slot);
            Network.BotTeamAssigned = (slot, team) => Simulation.Bots.AssignTeam(slot, team);
            Network.ParticipantLeaving = (peer, reason, leftTick) => Simulation.Reports?.Leave(Simulation.Scene, peer, reason, leftTick);
            Network.StatusProvider = () => new MatchStatePacket
            {
                MatchId = unchecked((ushort)Network.MatchId), Mode = (byte)Network.Mode,
                RoomKey = Network.Room, NextRoomKey = Network.Room,
                PlayerCount = (byte)(Network.Count + Simulation.Bots.Count), TimeRemaining = Math.Max(0, Simulation.Scene.Match.MatchTime),
                PointGoal = (ushort)Math.Clamp(Spec.Rules.LegacyPointGoal, 0, UInt16.MaxValue),
                Flags = (byte)((Simulation.Scene.Match.LegacyState == MatchState.InProgress
                    ? MatchStatePacket.FlagInProgress : MatchStatePacket.FlagEnding)
                    | (Spec.Rules.FriendlyFire ? MatchStatePacket.FlagFriendlyFire : 0))
            };
            if (options.EnableAdmin)
                Admin = new TournamentAdmin(() => Simulation, Network,
                    options.ResolveAdminSelection ?? ((_, _) => throw new ArgumentException("Select the next match through the host.")),
                    options.AdminSelectionRequested ?? (_ => { }), StartReplay);
            telemetry = Spec.TelemetryPolicy == TelemetryPolicy.Record ? new(Spec.Rules, WireMatchId, tick) : null;
            // Awards are emitted synchronously by the match-owned semantic
            // dispatcher. This fixed journal bridges simulation to reliable
            // transport/replay without making either path an event source.
            Simulation.Scene.Match.Awards.Awarded += QueueAward;
            if (!Simulation.Scene.Match.SemanticEvents.Subscribe(QueueSemanticEvent))
                throw new InvalidOperationException("Match semantic sink budget exhausted.");
        }
        catch { Simulation.Dispose(); throw; }
    }

    private bool AdmitFrozenSeat(JoinPacket join, TicketIdentity identity)
    {
        if (!identity.WorkerAdmission || identity.ReservedSeat is not { } seatId || join.WireMatchId != WireMatchId) return false;
        RosterSeat? seat = Spec.Roster.FirstOrDefault(candidate => candidate.SeatId == seatId);
        if (seat == null || seat.Role == SeatRole.Bot || join.Observer != (seat.Role == SeatRole.Observer)
            || join.Hunter != seat.Hunter || join.Name != seat.DisplayName || identity.ReservedTeam != seat.Team) return false;
        return seat.PlayerId is { } player
            ? identity.PlayerId == player && identity.GuestSessionId == null
            : seat.GuestSessionId is { } guest && identity.GuestSessionId == guest && identity.PlayerId == null;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State != MatchInstanceState.Created) throw new InvalidOperationException("A match can only start once.");
        State = MatchInstanceState.Running;
        if (Spec.ReplayPolicy == ReplayPolicy.Record) StartReplay();
    }

    private ServerReplaySession? StartReplay()
    {
        if (!_options.LegacyDynamicAdmission && Spec.ReplayPolicy == ReplayPolicy.Disabled) return null;
        if (replay != null || String.IsNullOrWhiteSpace(_options.ReplayDirectory)) return replay;
        if (_options.LegacyDynamicAdmission && _options.LegacyReplayMayOpen?.Invoke() == false) return null;
        ReplayMapIdentity mapIdentity = _contentSnapshot == null
            ? new ReplayMapIdentity(Spec.Rules.RoomKey, null, null)
            : new ReplayMapIdentity(Spec.Rules.RoomKey,
                _contentSnapshot.MapIdentity,
                _contentSnapshot.MatchContentIdentity);
        replay = new ServerReplaySession(_options.ReplayDirectory, mapIdentity);
        Network.ObserverFrameCaptured = frame => replay?.Capture(frame, Simulation.Scene);
        return replay;
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State == MatchInstanceState.Created) throw new InvalidOperationException("Start the match before ticking.");
        if (State != MatchInstanceState.Running) return;
        long started = Stopwatch.GetTimestamp();
        long allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            using IDisposable? contentScope = _contentSnapshot == null
                ? null : ContentEnvironment.UseMatchContent(_contentSnapshot);
            Step();
            uint completedTick = tick++;
            if (Simulation.Lifecycle.RotationDue) Finish(completedTick, null);
        }
        catch (Exception error)
        {
            _terminal = true; _error = error.Message; State = MatchInstanceState.Failed;
            Network.ObserverFrameCaptured = null;
            replay?.Complete();
            Failed?.Invoke(new(MatchId, WireMatchId, tick, error));
            throw;
        }
        finally
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - started;
            long allocatedAtEnd = GC.GetAllocatedBytesForCurrentThread();
            RecordPerformance(elapsedTicks, allocatedAtEnd >= allocatedAtStart ? allocatedAtEnd - allocatedAtStart : 0);
        }
    }

    private void RecordPerformance(long elapsedTicks, long allocatedBytes)
    {
        int sequence = Interlocked.Increment(ref _performanceSequence);
        double elapsedMilliseconds = elapsedTicks * (1000.0 / Stopwatch.Frequency);
        _tickDurations.Record(elapsedMilliseconds);
        _performanceTicks++;
        _allocatedBytes += allocatedBytes;
        if (elapsedMilliseconds > 1000.0 / 60.0) _deadlineMisses++;
        Volatile.Write(ref _performanceSequence, unchecked(sequence + 1));
    }

    private bool TryReadPerformance(out MatchPerformanceSnapshot snapshot)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int sequence = Volatile.Read(ref _performanceSequence);
            if ((sequence & 1) != 0) continue;
            long count = Volatile.Read(ref _performanceTicks);
            long deadlineMisses = Volatile.Read(ref _deadlineMisses);
            long allocated = Volatile.Read(ref _allocatedBytes);
            if (!_tickDurations.TrySnapshot(out BoundedPercentileSnapshot durations)) continue;
            Thread.MemoryBarrier();
            int completed = Volatile.Read(ref _performanceSequence);
            if (sequence != completed || (completed & 1) != 0) continue;
            double elapsedSeconds = Stopwatch.GetElapsedTime(_performanceStarted).TotalSeconds;
            // Process-wide GC context is sampled by the off-lane reader so
            // the authoritative writer only records preallocated scalars.
            long gen0Collections = Math.Max(0L, (long)GC.CollectionCount(0) - _gen0AtStart);
            long gen1Collections = Math.Max(0L, (long)GC.CollectionCount(1) - _gen1AtStart);
            long gen2Collections = Math.Max(0L, (long)GC.CollectionCount(2) - _gen2AtStart);
            snapshot = new MatchPerformanceSnapshot(count, deadlineMisses, durations, allocated,
                count == 0 ? 0 : allocated / (double)count,
                elapsedSeconds <= 0 ? 0 : allocated / elapsedSeconds,
                // These counters are process-wide observations, retained only
                // as lifetime context and never interpreted as match-caused GC.
                gen0Collections, gen1Collections, gen2Collections);
            return true;
        }
        snapshot = null!;
        return false;
    }

    private void Step()
    {
        ServerSimulation simulation = Simulation;
        ServerNetwork network = Network;
        Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
        Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
        network.Poll(tick);
        Volatile.Write(ref _publishedTimingTelemetry, network.TimingTelemetry);
        if (replay?.Status.State == "failed") throw new InvalidOperationException("Authoritative replay failed: " + replay.Status.Error);
        if (Spec.ReplayPolicy == ReplayPolicy.Record && replay == null) StartReplay();
        Admin?.BeforeStep(tick, replay);
        simulation.ReplayMayStart = ServerReplayPolicy.MayStart(Spec.ReplayPolicy == ReplayPolicy.Record, replay);
        if (Admin == null && replay != null && simulation.Reports is { Started: false } ledger
            && !ReferenceEquals(replayLedger, ledger))
        {
            ledger.ConfigureRoundIdentity(Spec.TournamentId?.ToString("D"), Spec.RoundId?.ToString("D"), replay.ReplayId);
            replayLedger = ledger;
        }
        simulation.Step(network, tick);
        telemetry?.Sample(tick, simulation.States, simulation.Scene.Match.Phase == MatchPhase.Playing, simulation.Scene);
        if (_snapshotCadence.IsDue(tick))
        {
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State is not (NetConnectionState.Playing or NetConnectionState.Ready)) { continue; }
                var snapshot = new SnapshotPacket(tick, snapshotSequence, network.MatchId,
                    peer.Inputs.LastProcessed, peer.Inputs.HasProcessed, simulation.Scene.Random.Rng1, simulation.Scene.Random.Rng2);
                int length = snapshot.Write(packet, simulation.States);
                peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
            }
            if (network.ObserverFramesRequired)
            {
                var observerSnapshot = new SnapshotPacket(tick, snapshotSequence, network.MatchId, 0, false, simulation.Scene.Random.Rng1, simulation.Scene.Random.Rng2);
                int observerLength = observerSnapshot.Write(packet, simulation.States);
                network.CaptureObserverSnapshot(packet[..observerLength]);
            }
            snapshotSequence++;
        }
        // State is recoverable from the next complete update.
        // A five-Hz world stream leaves budget for combat and movement.
        if (tick % 12 == 0 && (network.Count > 0 || network.ObserverFramesRequired))
        {
            world.Capture(simulation.Scene, network.MatchId, worldRevision++, tick);
            for (int batch = 0; batch < world.BatchCount; batch++)
            {
                int length = world.WriteBatch(packet, batch);
                network.CaptureObserverWorld(packet[..length]);
                foreach (ServerPeer? peer in network.Peers)
                {
                    if (peer != null) { network.SendWorld(peer, packet[..length]); }
                }
            }
        }
        // A stalled peer cannot block the shared event journal.
        // Admission is per peer; sequence-level dedup is reliable-layer owned.
        for (int batch = 0; batch < 8; batch++)
        {
            int count = simulation.Combat.CopyPending(events);
            if (count == 0) { break; }
            for (int i = 0; i < count; i++) telemetry?.Combat(events[i], simulation.Scene);
            int length = CombatEventBatch.Write(packet, events[..count]);
            network.CaptureObserverEvent(ReliableEventType.Combat, packet[..length]);
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.Combat, packet[..length]))
                {
                    Diagnose($"[server] slot {peer.Slot} disconnected: reliable admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                }
            }
            simulation.Combat.Consume(count);
        }
        while (simulation.Combat.TryPeekKill(out KillEvent kill))
        {
            telemetry?.Kill(kill);
            kill.Write(packet[..KillEvent.Size]);
            network.CaptureObserverEvent(ReliableEventType.Kill, packet[..KillEvent.Size]);
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.Kill, packet[..KillEvent.Size]))
                {
                    Diagnose($"[server] slot {peer.Slot} disconnected: reliable kill admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                }
            }
            simulation.Combat.ConsumeKill();
        }
        while (simulation.Combat.World.TryPeek(out WorldEvent worldEvent))
        {
            telemetry?.World(worldEvent);
            worldEvent.Write(packet[..WorldEvent.Size]);
            network.CaptureObserverEvent(ReliableEventType.WorldEvent, packet[..WorldEvent.Size]);
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.WorldEvent, packet[..WorldEvent.Size]))
                {
                    Diagnose($"[server] slot {peer.Slot} disconnected: reliable world event queue exhausted.");
                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                }
            }
            simulation.Combat.World.Consume();
        }
        while (TryPeekSemanticEvent(out MatchEvent semanticEvent))
        {
            telemetry?.Semantic(semanticEvent);
            MatchSemanticEventPacket wire = MatchSemanticEventPacketConversion.FromEvent(semanticEvent);
            wire.Write(packet);
            network.CaptureObserverEvent(ReliableEventType.MatchSemantic, packet[..MatchSemanticEventPacket.Size]);
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.MatchSemantic, packet[..MatchSemanticEventPacket.Size]))
                {
                    Diagnose($"[server] slot {peer.Slot} disconnected: reliable semantic-event admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                }
            }
            ConsumeSemanticEvent();
        }
        while (TryPeekAward(out MatchAward award))
        {
            telemetry?.Award(award);
            MatchAwardPacket wire = MatchAwardPacketConversion.FromAward(award);
            wire.Write(packet);
            network.CaptureObserverEvent(ReliableEventType.MatchAward, packet[..MatchAwardPacket.Size]);
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !network.TrySendEvent(peer, ReliableEventType.MatchAward, packet[..MatchAwardPacket.Size]))
                {
                    Diagnose($"[server] slot {peer.Slot} disconnected: reliable award admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                }
            }
            ConsumeAward();
        }
        network.CommitObserverTick(tick);
        telemetry?.CommitTick(tick, simulation.Scene.Match.Result != null);
    }

    private void QueueAward(MatchAward award)
    {
        if (_awardCount == AwardCapacity)
        {
            if (DroppedAwards < long.MaxValue) DroppedAwards++;
            Diagnose("Semantic award journal full; presentation fact dropped with authoritative gameplay unchanged.");
            return;
        }
        _awards[(_awardHead + _awardCount++) % AwardCapacity] = award;
        if (_awardCount > AwardQueueHighWater) AwardQueueHighWater = _awardCount;
    }

    private void QueueSemanticEvent(in MatchEvent value)
    {
        if (_semanticCount == SemanticCapacity)
        {
            if (DroppedSemanticEvents < long.MaxValue) DroppedSemanticEvents++;
            Diagnose("Match semantic event journal full; presentation fact dropped with authoritative gameplay unchanged.");
            return;
        }
        _semanticEvents[(_semanticHead + _semanticCount++) % SemanticCapacity] = value;
        if (_semanticCount > SemanticQueueHighWater) SemanticQueueHighWater = _semanticCount;
    }

    private bool TryPeekSemanticEvent(out MatchEvent value)
    {
        value = _semanticCount == 0 ? default : _semanticEvents[_semanticHead];
        return _semanticCount != 0;
    }

    private void ConsumeSemanticEvent()
    {
        if (_semanticCount == 0) throw new InvalidOperationException("No pending match semantic event.");
        _semanticEvents[_semanticHead] = default;
        _semanticHead = (_semanticHead + 1) % SemanticCapacity;
        _semanticCount--;
    }

    private bool TryPeekAward(out MatchAward award)
    {
        award = _awardCount == 0 ? default : _awards[_awardHead];
        return _awardCount != 0;
    }

    private void ConsumeAward()
    {
        if (_awardCount == 0) throw new InvalidOperationException("No pending semantic award.");
        _awards[_awardHead] = default;
        _awardHead = (_awardHead + 1) % AwardCapacity;
        _awardCount--;
    }

    private void Finish(uint finalTick, MatchStopReason? reason)
    {
        if (_terminal) return;
        _terminal = true;
        State = reason == null ? MatchInstanceState.Completed : MatchInstanceState.Stopped;
        Network.ObserverFrameCaptured = null;
        replay?.Complete(finalTick);
        Completion = new(MatchId, WireMatchId, finalTick, Simulation.Scene.Match.Result,
            Simulation.Reports?.Report, telemetry?.Complete(finalTick, Simulation.Scene.Match.Result != null,
                Simulation.Reports?.Report?.MatchId), reason);
        Completed?.Invoke(Completion);
    }

    public void RequestStop(MatchStopReason reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Finish(tick == _options.InitialTick ? tick : tick - 1, reason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_terminal) RequestStop(MatchStopReason.HostShutdown);
        Admin?.Commands.Close();
        Simulation.Dispose();
        _disposed = true;
        State = MatchInstanceState.Disposed;
        // ReplayCompletion is drained by the host; transport is likewise host-owned.
    }
}
