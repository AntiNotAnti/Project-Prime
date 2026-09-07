using System;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MphRead.Admin;
using MphRead.Replay;
using MphRead.Identity;
using MphRead.Reporting;
using MphRead.Telemetry;

namespace MphRead.Mods.Network
{
    /// <summary>One scene, one writer, one fixed simulation step per tick.</summary>
    public sealed class AuthoritativeServer
    {
        private volatile bool _running = true;
        private readonly int _port;
        private readonly string _data;
        private readonly string _version;
        private readonly RotationEntry _entry;
        public ServerAdminOptions? AdminOptions { get; init; }
        public string? ReplayDirectory { get; init; }
        public ServerTicketOptions? TicketOptions { get; init; }
        public ObserverOptions Observers { get; init; } = new();
        public MapRotation? Rotation { get; init; }
        public ServerVoteOptions? Voting { get; init; }
        public int MaxPlayers { get; init; } = 8;
        public RulesetPreset RulesetPreset { get; init; } = RulesetPreset.Classic;
        public bool FriendlyFire { get; init; }
        public LateJoinPolicy? LateJoinPolicy { get; init; }
        public SpawnPolicy SpawnPolicy { get; init; } = SpawnPolicy.Classic;
        public OvertimePolicy OvertimePolicy { get; init; } = OvertimePolicy.Disabled;
        public bool CancelSpawnProtectionOnOffensiveAction { get; init; }
        public bool LagCompEnabled { get; init; } = true;
        public bool ProjectileCatchUpEnabled { get; init; } = true;
        public string ServerName { get; init; } = "Prime Hunters";
        public MasterReporter? Reporter { get; init; }
        public Update.ServerUpdateRuntime? Updates { get; init; }
        public int BoundPort { get; private set; }
        public ServerReportingOptions? Reporting { get; init; }
        public BotFillPolicy? BotFill { get; init; }
        public string? TelemetryDirectory { get; init; }
        public MatchRules? InitialRules { get; init; }
        public LobbyPolicy LobbyPolicy { get; init; } = new(
            LobbyPolicyKind.NoLobby, readyRequired: false, minimumPlayers: 1, hostMayForceStart: true);
        public Guid LobbyOwnerCapability { get; init; }
        public IPAddress? LobbyOwnerAddress { get; init; }

        public AuthoritativeServer(int port, string data, string version, RotationEntry entry)
        {
            _port = port;
            _data = data;
            _version = version;
            _entry = entry;
        }

        public void Stop() => _running = false;

        private MatchRules ResolveRules(RotationEntry entry)
        {
            MatchRules rules = RulesetResolver.Resolve(entry, RulesetPreset, MaxPlayers, FriendlyFire);
            return RulesetPreset is RulesetPreset.Competitive or RulesetPreset.Duel ? rules : rules.With(
                spawnPolicy: SpawnPolicy, cancelSpawnProtectionOnOffensiveAction: CancelSpawnProtectionOnOffensiveAction,
                overtimePolicy: OvertimePolicy, lateJoinPolicy: LateJoinPolicy);
        }

        private MatchRules ResolveInitialRules()
        {
            if (InitialRules == null) return ResolveRules(_entry);
            MatchLifecycle.ValidateRules(InitialRules);
            if (!String.Equals(InitialRules.RoomKey, _entry.RoomKey, StringComparison.OrdinalIgnoreCase)
                || InitialRules.MaxPlayers != MaxPlayers)
                throw new ArgumentException("Initial lobby rules must match the requested room and session capacity.");
            return InitialRules;
        }

        public void Run()
        {
            try { RunSimulation(); }
            finally
            {
                if (BoundPort != 0) { Reporter?.Farewell((ushort)BoundPort); }
                Reporter?.Dispose();
            }
        }

        private void RunSimulation()
        {
            ServerReportingOptions? reportOptions = Reporting ?? ServerReportingOptions.FromEnvironment();
            if (reportOptions != null && (reportOptions.ServerId == Guid.Empty
                || TicketOptions != null && TicketOptions.ServerId != reportOptions.ServerId))
                throw new ArgumentException("Reporting and ticket admission must use the same nonempty server identity.");
            IMatchReportTransport? reportTransport = reportOptions?.CreateTransport();
            using IDisposable? reportTransportLifetime = reportTransport as IDisposable;
            using MatchReportOutbox? outbox = reportOptions == null ? null : new(reportOptions.Outbox, reportTransport!);
            Guid incarnation = TicketOptions?.SessionId ?? Guid.NewGuid();
            Guid ledgerServerId = reportOptions?.ServerId ?? TicketOptions?.ServerId ?? Guid.NewGuid();
            string buildVersion = typeof(AuthoritativeServer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown-build";
            string? telemetryDirectory = TelemetryDirectory ?? Environment.GetEnvironmentVariable("PRIME_TELEMETRY_DIRECTORY");
            using TelemetryWriter? telemetryWriter = String.IsNullOrWhiteSpace(telemetryDirectory) ? null : new(telemetryDirectory);
            ServerContent.Open(_data, _version);
            using var transport = new UdpTransport(_port, Environment.GetEnvironmentVariable("PRIME_PRACTICE") == "1" ? System.Net.IPAddress.Loopback : null);
            using var tickets = TicketOptions == null ? null : new ServerTicketAuthority(TicketOptions);
            BoundPort = transport.LocalPort;
            if (Rotation != null)
                foreach (RotationEntry entry in Rotation.Entries) _ = ResolveRules(entry);
            string? replayDirectory = ReplayDirectory ?? Environment.GetEnvironmentVariable("PRIME_SERVER_REPLAY_DIRECTORY");
            MatchRules rules = ResolveInitialRules();
            ServerRankedPolicy.ValidatePublicStart(rules);
            bool automaticReplay = ServerReplayPolicy.Validate(rules, TicketOptions != null, reportOptions != null, replayDirectory);
            if (Rotation != null)
                foreach (RotationEntry entry in Rotation.Entries)
                {
                    ServerRankedPolicy.ValidatePublicStart(ResolveRules(entry));
                    ServerReplayPolicy.Validate(ResolveRules(entry), TicketOptions != null, reportOptions != null, replayDirectory);
                }
            BotFillPolicy botFill = BotFill ?? BotFillPolicy.FromEnvironment();
            if (rules.RulesetPreset == RulesetPreset.Duel && botFill.MinimumParticipants != 0)
                throw new ArgumentException("Duel requires bot fill to be disabled.");
            ServerSimulation simulation = new(rules, LagCompEnabled, ProjectileCatchUpEnabled, botFill);
            simulation.Reports = new(ledgerServerId, incarnation, buildVersion);
            TelemetryCollector? telemetry = telemetryWriter == null ? null : new(rules, 1, 0);
            uint telemetryTick = 0;
            TournamentAdmin? admin = null;
            AdminHttpServer? adminHttp = null;
            ServerReplaySession? replay = null;
            Task? replayClosing = null;
            ServerReplaySession? closingReplay = null;
            MphRead.Reporting.MatchParticipantLedger? replayLedger = null;
            MatchRules? selectedRules = null;
            ReportSubmission? reportSubmission = null;
            MatchReportOutbox.Reservation? reportReservation = null;
            try
            {
                var network = new ServerNetwork(transport, rules, observers: Observers,
                    lobbyOwnerCapability: LobbyOwnerCapability, lobbyOwnerAddress: LobbyOwnerAddress)
                {
                    ServerName = ServerName,
                    TicketAuthority = tickets,
                    Phase = simulation.Scene.Match.Phase,
                    PhaseRevision = simulation.Scene.Match.PhaseRevision
                };
                network.CancelBotClaim = slot => simulation.Bots.CancelClaim(slot);
                network.CanClaimPlayerSlot = slot => simulation.Bots.Claim(slot, network, network.Tick);
                network.BotRosterEntry = slot => simulation.Bots.Roster(slot);
                network.BotTeamAssigned = (slot, team) => simulation.Bots.AssignTeam(slot, team);
                ServerReplaySession? StartReplay()
                {
                    if (replay != null) return replay;
                    if (string.IsNullOrWhiteSpace(replayDirectory) || replayClosing?.IsCompleted == false) return null;
                    replay = new ServerReplaySession(replayDirectory);
                    network.ObserverFrameCaptured = frame => replay?.Capture(frame, simulation.Scene);
                    return replay;
                }
                MatchRules ResolveAdmin(string? room, string? presetName)
                {
                    room ??= network.Room;
                    RotationEntry? candidate = (Rotation?.Entries ?? new[] { _entry }).FirstOrDefault(e => e.RoomKey == room);
                    if (candidate == null) throw new ArgumentException("Map is not in the startup allowlist.");
                    if (!Enum.TryParse(presetName, true, out RulesetPreset preset) || !Enum.IsDefined(preset))
                        throw new ArgumentException("Unknown ruleset preset.");
                    MatchRules resolved = RulesetResolver.Resolve(candidate, preset, MaxPlayers, FriendlyFire);
                    if (resolved.MaxPlayers != network.Rules.MaxPlayers)
                        throw new ArgumentException("A ruleset cannot change this session's player capacity.");
                    ServerReplayPolicy.Validate(resolved, TicketOptions != null, reportOptions != null, replayDirectory);
                    ServerContent.RequireRoom(resolved.RoomKey, resolved.Mode.ToLegacyMode());
                    return resolved;
                }
                ServerLobby? lobby = null;
                ServerLobbyNetwork? lobbyNetwork = null;
                if (LobbyPolicy.Kind != LobbyPolicyKind.NoLobby)
                {
                    uint sessionId = unchecked((uint)NetConnection.NewIdentity());
                    if (sessionId == 0) sessionId = 1;
                    bool MapAllowed(string map) => (Rotation?.Entries ?? new[] { _entry })
                        .Any(entry => String.Equals(entry.RoomKey, map, StringComparison.OrdinalIgnoreCase));
                    bool SelectionAllowed(string map, MatchMode mode)
                    {
                        if (!MapAllowed(map)) return false;
                        try
                        {
                            ServerContent.RequireRoom(map, mode.ToLegacyMode());
                            return true;
                        }
                        catch (ArgumentException) { return false; }
                        catch (ProgramException) { return false; }
                    }
                    bool StartGate(MatchRules candidate)
                    {
                        try
                        {
                            if (candidate.MaxPlayers != network.Rules.MaxPlayers) return false;
                            if (!SelectionAllowed(candidate.RoomKey, candidate.Mode)) return false;
                            ServerReplayPolicy.Validate(candidate, TicketOptions != null, reportOptions != null, replayDirectory);
                            if (!ServerRankedPolicy.HasLockedConstraints(candidate)
                                || !ServerRankedPolicy.PublicStartAllowed(candidate)) return false;
                            if (candidate.RankingEligibility == RankingEligibility.VerifiedServerOnly
                                && (TicketOptions?.RequireTickets != true || reportOptions == null
                                    || botFill.MinimumParticipants != 0)) return false;
                            return true;
                        }
                        catch (ArgumentException) { return false; }
                        catch (ProgramException) { return false; }
                    }
                    lobby = new ServerLobby(sessionId, LobbyPolicy, rules, MapAllowed,
                        selectionAllowed: SelectionAllowed, reconnectGraceTicks: ServerNetwork.ReconnectGraceTicks,
                        botPolicy: botFill)
                    {
                        StartGate = StartGate
                    };
                    lobbyNetwork = new ServerLobbyNetwork(lobby, network, () => simulation.Bots);
                    if (LobbyStartPolicy.UsesLobbyBeforeFirstMatch(LobbyPolicy.Kind))
                    {
                        simulation.LobbyMayStart = false;
                        lobbyNetwork.Open(0);
                    }
                }
                var adminOptions = AdminOptions ?? ServerAdminOptions.FromEnvironment();
                if (adminOptions != null)
                {
                    admin = new TournamentAdmin(() => simulation, network, ResolveAdmin, value => selectedRules = value, StartReplay);
                    adminHttp = new AdminHttpServer(adminOptions, admin.Commands, () => admin.Status);
                }
                var voting = new ServerVoting(network, () => simulation,
                    Voting ?? ServerVoteOptions.Parse(null, false, RulesetPreset), Rotation, Rng.Rng2);
                network.ParticipantLeaving = (peer, reason, leftTick) => simulation.Reports?.Leave(simulation.Scene, peer, reason, leftTick);
                network.StatusProvider = () => new MatchStatePacket
                {
                    MatchId = unchecked((ushort)network.MatchId), Mode = (byte)network.Mode,
                    RoomKey = network.Room, NextRoomKey = Rotation?.Next.RoomKey ?? network.Room,
                    PlayerCount = (byte)(network.Count + simulation.Bots.Count), TimeRemaining = Math.Max(0, simulation.Scene.Match.MatchTime),
                    PointGoal = (ushort)Math.Clamp(simulation.Scene.Match.Rules.LegacyPointGoal, 0, UInt16.MaxValue),
                    Flags = (byte)((simulation.Scene.Match.LegacyState == MatchState.InProgress
                        ? MatchStatePacket.FlagInProgress : MatchStatePacket.FlagEnding)
                        | (simulation.Scene.Match.Rules.FriendlyFire ? MatchStatePacket.FlagFriendlyFire : 0))
                };
                var scheduler = new FixedTickScheduler();
                var world = new WorldStateCapture();
                uint tick = 0;
                uint snapshotSequence = 0;
                uint worldRevision = 0;
                int reportedPeers = -1;
                double nextReport = 0;
                long lastReport = Stopwatch.GetTimestamp();
                long previousIn = 0, previousOut = 0;
                long previousAllocated = GC.GetTotalAllocatedBytes(precise: false);
                using var process = Process.GetCurrentProcess();
                TimeSpan previousCpu = process.TotalProcessorTime;
                Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
                Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                Console.WriteLine($"[server] listening on UDP {BoundPort}; {_entry.RoomKey}; data {_version}");
                while (_running)
                {
                    int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
                    if (due == 0)
                    {
                        scheduler.Wait();
                        continue;
                    }
                    for (int step = 0; step < due && _running; step++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        if (outbox != null)
                        {
                            if (network.LobbyAdmissionOpen && reportSubmission?.State is
                                ReportSubmissionState.DurablyStored or ReportSubmissionState.BackendAccepted)
                                reportSubmission = null;
                            if (reportReservation == null && reportSubmission == null) outbox.TryReserve(out reportReservation);
                            simulation.ReportingMayStart = reportReservation != null && outbox.Healthy;
                            network.AdmissionClosed = !network.LobbyAdmissionOpen
                                && (reportReservation == null || !outbox.Healthy || simulation.Reports?.CapacityAvailable == false);
                        }
                        network.Poll(tick);
                        if (closingReplay?.Status.State == "failed")
                            throw new InvalidOperationException("Previous authoritative replay failed: " + closingReplay.Status.Error);
                        if (automaticReplay && replay == null && !network.LobbyAdmissionOpen) StartReplay();
                        admin?.BeforeStep(tick, replay);
                        if (lobby != null)
                        {
                            lobby.TournamentRosterLocked = network.AdminRosterLocked;
                            lobby.TournamentStartAllowed = admin?.RotationAllowed ?? true;
                            if (admin?.StartRequested == true && lobby.AdmissionOpen)
                                _ = lobby.TryStartAsServer(out _);
                            lobbyNetwork!.Tick(tick);
                            if (lobbyNetwork.TryStart(tick, out MatchRules? lobbyRules))
                            {
                                // A lobby start is always a new match boundary. Reusing the
                                // previous simulation when the rules are unchanged would retain
                                // its terminal MatchResult and prevent a rematch from starting.
                                botFill = lobby.BotPolicy;
                                simulation.Dispose();
                                simulation = new ServerSimulation(lobbyRules!, LagCompEnabled, ProjectileCatchUpEnabled, botFill);
                                simulation.Reports = new(ledgerServerId, incarnation, buildVersion);
                                telemetry = telemetryWriter == null ? null : new(lobbyRules!, network.MatchId, tick);
                                admin?.NewRound();
                                simulation.LobbyMayStart = true;
                                network.Phase = simulation.Scene.Match.Phase;
                                network.PhaseRevision = simulation.Scene.Match.PhaseRevision;
                            }
                            else if (network.LobbyAdmissionOpen)
                            {
                                simulation.LobbyMayStart = false;
                            }
                        }
                        simulation.ReplayMayStart = ServerReplayPolicy.MayStart(automaticReplay, replay);
                        if (admin == null && replay != null && simulation.Reports is { Started: false } ledger
                            && !ReferenceEquals(replayLedger, ledger))
                        {
                            ledger.ConfigureRoundIdentity(null, null, replay.ReplayId);
                            replayLedger = ledger;
                        }
                        if (Updates?.PollIdle(() => network.Count + network.ObserverCount == 0,
                            () => network.AdmissionClosed = true, () => network.AdmissionClosed = false) == true)
                        {
                            _running = false;
                            break;
                        }
                        int currentPeers = network.Count + network.ObserverCount;
                        if (reportedPeers != currentPeers)
                        {
                            reportedPeers = currentPeers;
                            Console.WriteLine($"[server] peers={reportedPeers}");
                        }
                        if (!network.LobbyAdmissionOpen) simulation.Step(network, tick);
                        voting.Tick(tick);
                        telemetryTick = tick;
                        telemetry?.Sample(tick, simulation.States, simulation.Scene.Match.Phase == MatchPhase.Playing, simulation.Scene);
                        if (lobbyNetwork != null && simulation.Scene.Match.Result is { } completed
                            && lobby!.LastMatchSummary?.MatchId != completed.MatchId)
                        {
                            bool ratingEligible = completed.Rules.RankingEligibility == RankingEligibility.VerifiedServerOnly
                                && simulation.Bots.Count == 0
                                && simulation.Reports?.Report is { } completedReport
                                && completedReport.Participants.All(participant => participant.Kind == ParticipantKind.RegisteredHuman);
                            MatchSummaryFlags flags = ratingEligible
                                ? MatchSummaryFlags.RatingEligible | MatchSummaryFlags.RatingPending
                                : MatchSummaryFlags.None;
                            // Publish from the immutable result before handing the Backend
                            // report to its asynchronous durability path below.
                            lobbyNetwork.PublishSummary(completed, flags, simulation.Reports?.Report);
                        }
                        if (outbox != null && reportSubmission == null && simulation.Reports?.Report is { } report)
                            if (reportReservation != null && outbox.TryEnqueue(report, reportReservation, out reportSubmission))
                            { reportReservation.Dispose(); reportReservation = null; }
                        if (tick % 2 == 0)
                        {
                            foreach (ServerPeer? peer in network.Peers)
                            {
                                if (peer?.Connection.State is not (NetConnectionState.Playing or NetConnectionState.Ready)) { continue; }
                                var snapshot = new SnapshotPacket(tick, snapshotSequence, network.MatchId,
                                    peer.Inputs.LastProcessed, peer.Inputs.HasProcessed, Rng.Rng1, Rng.Rng2);
                                int length = snapshot.Write(packet, simulation.States);
                                peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
                            }
                            if (network.ObserverFramesRequired)
                            {
                                var observerSnapshot = new SnapshotPacket(tick, snapshotSequence, network.MatchId, 0, false, Rng.Rng1, Rng.Rng2);
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
                                    Console.Error.WriteLine($"[server] slot {peer.Slot} disconnected: reliable admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
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
                                    Console.Error.WriteLine($"[server] slot {peer.Slot} disconnected: reliable kill admission refused. {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
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
                                    Console.Error.WriteLine($"[server] slot {peer.Slot} disconnected: reliable world event queue exhausted.");
                                    network.Remove(peer.Slot, reason: ParticipantExitReason.Backpressure);
                                }
                            }
                            simulation.Combat.World.Consume();
                        }
                        network.CommitObserverTick(tick);
                        telemetry?.CommitTick(tick, simulation.Scene.Match.Result != null);
                        bool enterLobby = lobby != null && LobbyStartPolicy.UsesLobbyAfterMatch(lobby.Policy.Kind)
                            && simulation.Lifecycle.RotationDue && (admin?.RotationAllowed ?? true);
                        if (enterLobby)
                        {
                            VoteResolution choice = selectedRules == null ? voting.ResolveForRotation() : new(IntermissionChoice.NextMap, -1);
                            MatchRules nextRules;
                            if (selectedRules != null) nextRules = selectedRules;
                            else if (choice.Kind is IntermissionChoice.Rematch or IntermissionChoice.Lobby) nextRules = simulation.Scene.Match.Rules;
                            else
                            {
                                RotationEntry next = choice.Kind == IntermissionChoice.Map
                                    ? (Rotation ?? throw new InvalidOperationException("Vote has no admitted rotation.")).Select(choice.RotationIndex)
                                    : Rotation?.Advance() ?? _entry;
                                nextRules = ResolveRules(next);
                            }
                            selectedRules = null;
                            if (!lobby!.TrySetServerRules(nextRules))
                                throw new InvalidOperationException("The validated rotation could not update the lobby draft.");
                            if (replay != null)
                            {
                                network.ObserverFrameCaptured = null; replay.Complete(tick); closingReplay = replay;
                                replayClosing = replay.Completion; replay = null;
                            }
                            if (telemetry != null && !telemetryWriter!.TryWrite(telemetry.Complete(tick,
                                simulation.Scene.Match.Result != null, simulation.Reports?.Report?.MatchId)))
                                Console.Error.WriteLine("[telemetry] writer queue full; match telemetry dropped");
                            telemetry = null;
                            lobbyNetwork!.Open(tick);
                            simulation.LobbyMayStart = false;
                            voting.NewMatch();
                            Console.WriteLine($"[server] lobby={lobby.Runtime.SessionId} after match={network.MatchId} tick={tick}");
                        }
                        else if ((selectedRules != null || (simulation.Lifecycle.RotationDue || voting.LobbyChoiceReady) && (admin?.RotationAllowed ?? true)) && (outbox == null
                            || selectedRules != null
                            || voting.LobbyChoiceReady && simulation.VoteLobbyHold && simulation.Scene.Match.Phase == MatchPhase.WaitingForPlayers
                            || reportSubmission?.State is ReportSubmissionState.DurablyStored or ReportSubmissionState.BackendAccepted))
                        {
                            VoteResolution choice = selectedRules == null ? voting.ResolveForRotation() : new(IntermissionChoice.NextMap, -1);
                            bool lobbyHold = selectedRules == null && choice.Kind == IntermissionChoice.Lobby;
                            MatchRules nextRules;
                            if (selectedRules != null) nextRules = selectedRules;
                            else if (choice.Kind is IntermissionChoice.Rematch or IntermissionChoice.Lobby) nextRules = simulation.Scene.Match.Rules;
                            else
                            {
                                RotationEntry next = choice.Kind == IntermissionChoice.Map
                                    ? (Rotation ?? throw new InvalidOperationException("Vote has no admitted rotation.")).Select(choice.RotationIndex)
                                    : Rotation?.Advance() ?? _entry;
                                nextRules = ResolveRules(next);
                            }
                            selectedRules = null;
                            if (replay != null)
                            {
                                network.ObserverFrameCaptured = null; replay.Complete(tick); closingReplay = replay; replayClosing = replay.Completion; replay = null;
                            }
                            uint match = unchecked(network.MatchId + 1);
                            if (match == 0) { match = 1; }
                            if (telemetry != null && !telemetryWriter!.TryWrite(telemetry.Complete(tick, simulation.Scene.Match.Result != null, simulation.Reports?.Report?.MatchId)))
                                Console.Error.WriteLine("[telemetry] writer queue full; match telemetry dropped");
                            telemetry = telemetryWriter == null ? null : new(nextRules, match, tick);
                            network.ChangeMatch(match, nextRules, tick);
                            // Flush the loading notification before synchronous content IO.
                            network.Poll(tick);
                            simulation.Dispose();
                            simulation = new ServerSimulation(nextRules, LagCompEnabled, ProjectileCatchUpEnabled, botFill);
                            simulation.VoteLobbyHold = lobbyHold;
                            voting.NewMatch();
                            simulation.Reports = new(ledgerServerId, incarnation, buildVersion);
                            reportSubmission = null;
                            admin?.NewRound();
                            network.Phase = simulation.Scene.Match.Phase;
                            network.PhaseRevision = simulation.Scene.Match.PhaseRevision;
                            Console.WriteLine($"[server] match={match} room={nextRules.RoomKey} tick={tick}");
                        }
                        tick++;
                        scheduler.DurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                    double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    if (now >= nextReport)
                    {
                        long reportAt = Stopwatch.GetTimestamp();
                        double seconds = Math.Max(Stopwatch.GetElapsedTime(lastReport, reportAt).TotalSeconds, 0.001);
                        long received = transport.Metrics.BytesReceived;
                        long sent = transport.Metrics.BytesSent;
                        long allocated = GC.GetTotalAllocatedBytes(precise: false);
                        TimeSpan cpu = process.TotalProcessorTime;
                        Console.WriteLine(FormattableString.Invariant($"[server] inKBps={(received - previousIn) / seconds / 1000:F1} outKBps={(sent - previousOut) / seconds / 1000:F1} allocatedKBps={(allocated - previousAllocated) / seconds / 1000:F1} cpuCores={(cpu - previousCpu).TotalSeconds / seconds:F3}"));
                        Console.WriteLine(FormattableString.Invariant($"[server] lagCompEnabled={simulation.Combat.LagCompEnabled} projectileCatchUpEnabled={simulation.Combat.ProjectileCatchUpEnabled} projectilesCaughtUp={simulation.Combat.CatchUp.ProjectilesCaughtUp} catchUpSteps={simulation.Combat.CatchUp.Steps} catchUpCollisions={simulation.Combat.CatchUp.Collisions} maxCatchUpSteps={simulation.Combat.CatchUp.MaxSteps} catchUpQueueDrops={simulation.Combat.CatchUp.QueueDrops}"));
                        if (outbox != null) Console.WriteLine($"[match-outbox] {outbox.Status}");
                        if (telemetry != null) Console.WriteLine($"[telemetry] droppedEvents={telemetry.DroppedEvents}");
                        lastReport = reportAt;
                        previousIn = received;
                        previousOut = sent;
                        previousAllocated = allocated;
                        previousCpu = cpu;
                        Console.WriteLine(FormattableString.Invariant($"[server] tick={tick} peers={network.Count} tickMeanMs={scheduler.DurationMs.Mean:F3} tickWorstMs={scheduler.DurationMs.Max:F3} dropped={scheduler.DroppedTicks} catchUp={scheduler.CatchUpTicks} driftMeanMs={scheduler.DriftMs.Mean:F3} rejected={network.Rejected} sentBytes={transport.Metrics.BytesSent} queueDrops={transport.Metrics.QueueDrops} shotsConsidered={simulation.Combat.ShotsConsidered} shotsEligible={simulation.Combat.ShotsEligible} shotsRewound={simulation.Combat.ShotsRewound} shotsClamped={simulation.Combat.ShotsClamped} requestedRewindMeanTicks={simulation.Combat.RequestedRewindTicks.Mean:F2} validatedRewindMeanTicks={simulation.Combat.ValidatedRewindTicks.Mean:F2} validatedRewindMaxTicks={simulation.Combat.ValidatedRewindTicks.Max:F0} historyQueries={simulation.Combat.History.Queries} historyMisses={simulation.Combat.History.Missing} combatDrops={simulation.Combat.Dropped}"));
                        foreach (ServerPeer? peer in network.Peers)
                        {
                            if (peer != null)
                            {
                                Console.WriteLine($"[server] reliable slot={peer.Slot} {ReliableDiagnostics.Describe(peer.Connection.Reliable)}");
                            }
                        }
                        nextReport = now + 30;
                    }
                    Reporter?.Beat(now, ServerName, (ushort)BoundPort, (byte)network.Count,
                        (byte)network.Rules.MaxPlayers, (byte)network.Mode, network.Room, protocol: NetHeader.Version);
                }
            }
            finally
            {
                admin?.Commands.Close(); adminHttp?.Dispose();
                replay?.Complete(telemetryTick);
                try
                {
                    if (!Task.WhenAll(replay?.Completion ?? Task.CompletedTask, replayClosing ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(5)))
                        Console.Error.WriteLine("[replay] shutdown deadline expired; recording remains incomplete, continuing result drain.");
                }
                catch (AggregateException)
                { Console.Error.WriteLine("[replay] background writer failed during shutdown; continuing result drain."); }
                if (replay?.Status is { State: "failed" } activeReplayFailure)
                    Console.Error.WriteLine($"[replay] {activeReplayFailure.ReplayId}: {activeReplayFailure.Error}; continuing result drain.");
                if (closingReplay?.Status is { State: "failed" } closingReplayFailure)
                    Console.Error.WriteLine($"[replay] {closingReplayFailure.ReplayId}: {closingReplayFailure.Error}; continuing result drain.");
                if (outbox != null && reportSubmission == null && simulation.Reports?.Report is { } terminal)
                {
                    long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
                    while (reportReservation != null && reportSubmission == null && Stopwatch.GetTimestamp() < until)
                    {
                        if (outbox.TryEnqueue(terminal, reportReservation, out reportSubmission)) break;
                        System.Threading.Thread.Sleep(10);
                    }
                    if (reportSubmission == null) Console.Error.WriteLine($"[match-outbox] shutdown could not transfer terminal report {terminal.MatchId}; not durable");
                }
                if (telemetry != null && !telemetryWriter!.TryWrite(telemetry.Complete(telemetryTick, simulation.Scene.Match.Result != null, simulation.Reports?.Report?.MatchId)))
                    Console.Error.WriteLine("[telemetry] shutdown queue full; match telemetry dropped");
                simulation.Dispose();
                reportReservation?.Dispose();
            }
        }
    }
}
