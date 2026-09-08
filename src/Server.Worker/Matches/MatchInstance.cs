using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Admin;
using MphRead.Replay;
using MphRead.Identity;
using MphRead.Reporting;
using MphRead.Telemetry;
using FruityPrime.Server.Shared;

namespace MphRead.Mods.Network;

/// <summary>One single-writer world. Tick performs one step and only bounded queue IO;
/// transport, scheduling, durable delivery and subsequent matches belong to the host.</summary>
public sealed class MatchInstance : IDisposable
{
    private readonly MatchInstanceOptions _options;
    private readonly INetTransport transport;
    private readonly WorldStateCapture world = new();
    private uint tick, snapshotSequence, worldRevision;
    private ServerReplaySession? replay;
    private MatchParticipantLedger? replayLedger;
    private readonly TelemetryCollector? telemetry;
    private bool _terminal, _disposed;
    private string? _error;
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
    public MatchInstanceStatus Status => new(MatchId, WireMatchId, State, tick,
        Simulation.Scene.Match.Phase, Network.Count + Simulation.Bots.Count, _error);
    public event Action<MatchCompletion>? Completed;
    public event Action<MatchFailure>? Failed;

    public MatchInstance(MatchInstanceOptions options, INetTransport transport)
        : this(options, transport, null) { }

    // Only the standalone adapter transfers its legacy session connections between rounds.
    internal MatchInstance(MatchInstanceOptions options, INetTransport transport, ServerNetwork? transferredNetwork)
    {
        options.Spec.Validate();
        if (options.WireMatchId == 0) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options;
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        tick = options.InitialTick;
        if (!options.LegacyDynamicAdmission)
        {
            if (options.EnableAdmin) throw new ArgumentException("Tournament authority belongs to Node; use the match admin bridge for Worker commands.");
            if (options.RequireReplay && Spec.ReplayPolicy != ReplayPolicy.Record
                || options.CollectTelemetry && Spec.TelemetryPolicy != TelemetryPolicy.Record
                || options.BotFill?.MinimumParticipants > 0 && Spec.BotFillPolicy == FruityPrime.Server.Shared.BotFillPolicy.Disabled)
                throw new ArgumentException("Runtime integration options conflict with the frozen match policies.");
            if (Spec.ReplayPolicy == ReplayPolicy.Record && String.IsNullOrWhiteSpace(options.ReplayDirectory))
                throw new ArgumentException("Recorded matches require a replay destination.");
        }
        BotFillPolicy fill = Spec.BotFillPolicy == FruityPrime.Server.Shared.BotFillPolicy.Disabled
            ? new() : options.BotFill ?? new(Spec.Roster.Count(seat => seat.Role != SeatRole.Observer));
        Simulation = new ServerSimulation(Spec.Rules, options.LagCompEnabled, options.ProjectileCatchUpEnabled,
            fill, Spec.Rng1Seed, Spec.Rng2Seed);
        try
        {
            if (options.ReportingServerId is { } server)
            {
                Simulation.Reports = new(server, Spec.NodeIncarnation, Spec.Content.BuildVersion, matchId: MatchId);
                Simulation.Reports.ConfigureRoundIdentity(Spec.TournamentId?.ToString("D"), Spec.RoundId?.ToString("D"), null);
            }
            Network = transferredNetwork ?? new ServerNetwork(transport, Spec.Rules, WireMatchId,
                Spec.ObserverPolicy == ObserverPolicy.Disabled ? new ObserverOptions(0) : options.Observers);
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
        replay = new ServerReplaySession(_options.ReplayDirectory);
        Network.ObserverFrameCaptured = frame => replay?.Capture(frame, Simulation.Scene);
        return replay;
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State == MatchInstanceState.Created) throw new InvalidOperationException("Start the match before ticking.");
        if (State != MatchInstanceState.Running) return;
        try
        {
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
    }

    private void Step()
    {
        ServerSimulation simulation = Simulation;
        ServerNetwork network = Network;
        Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
        Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
        network.Poll(tick);
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
        if (tick % 2 == 0)
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
        network.CommitObserverTick(tick);
        telemetry?.CommitTick(tick, simulation.Scene.Match.Result != null);
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
