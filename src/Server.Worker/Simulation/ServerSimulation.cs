using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Reporting;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public sealed class ServerSimulation : IDisposable
    {
        private readonly ulong[] _activeConnections = new ulong[8];
        private readonly SnapshotPlayer[] _states = new SnapshotPlayer[8];
        private readonly WorldStateCapture _pristineCapture;
        private readonly WorldRecord[] _pristineWorld;
        private readonly Action _resetForCountdown;
        private readonly uint _initialRng1;
        private readonly uint _initialRng2;
        private ServerNetwork? _network;
        private int _stateCount;
        private readonly bool _headshotValidationScenario;
        private readonly int _headshotScenarioFrames;
        private int _headshotValidationArm = -1;
        private int _headshotScenarioPlayingFrames;
        private ulong _headshotValidationShooterConnection;
        private uint _headshotValidationTargetLife;
        public Scene Scene { get; }
        public ServerCombat Combat { get; }
        public ServerBotManager Bots { get; }
        public MatchLifecycle Lifecycle { get; }
        public MatchParticipantLedger? Reports { get; set; }
        public bool ReportingMayStart { get; set; } = true;
        public bool ReplayMayStart { get; set; } = true;
        public bool AdminMayStart { get; set; } = true;
        public bool VoteLobbyHold { get; set; }
        public ReadOnlySpan<SnapshotPlayer> States => _states.AsSpan(0, _stateCount);
        internal int CountdownResets { get; private set; }

        public ServerSimulation(RotationEntry entry, bool lagCompEnabled = true, bool projectileCatchUpEnabled = true, uint rng1 = Rng.Rng1StartValue, uint rng2 = Rng.Rng2StartValue, bool historicalDynamicCollisionEnabled = false)
            : this(entry.ToMatchRules(), lagCompEnabled, projectileCatchUpEnabled, rng1: rng1, rng2: rng2,
                historicalDynamicCollisionEnabled: historicalDynamicCollisionEnabled)
        {
        }

        public ServerSimulation(MatchRules rules, bool lagCompEnabled = true,
            bool projectileCatchUpEnabled = true, BotFillPolicy? botFill = null,
            uint rng1 = Rng.Rng1StartValue, uint rng2 = Rng.Rng2StartValue,
            bool historicalDynamicCollisionEnabled = false)
            : this(rules, lagCompEnabled, projectileCatchUpEnabled, botFill,
                rng1, rng2, historicalDynamicCollisionEnabled,
                DeveloperValidationFixtureId.None)
        {
        }

        internal ServerSimulation(MatchRules rules, bool lagCompEnabled,
            bool projectileCatchUpEnabled, BotFillPolicy? botFill,
            uint rng1, uint rng2, bool historicalDynamicCollisionEnabled,
            DeveloperValidationFixtureId validationFixture,
            bool headshotValidationScenario = false, int headshotScenarioSeconds = 15)
        {
            MatchLifecycle.ValidateRules(rules);
            (botFill ?? new BotFillPolicy()).Validate(rules.MaxPlayers);
            if (headshotValidationScenario
                && validationFixture != DeveloperValidationFixtureId.Unit1Rm1Dynamic)
                throw new ProgramException("Headshot validation requires the isolated Unit1 RM1 developer fixture.");
            if (headshotValidationScenario && headshotScenarioSeconds is (< 12 or > 60))
                throw new ArgumentOutOfRangeException(nameof(headshotScenarioSeconds));
            _headshotValidationScenario = headshotValidationScenario;
            _headshotScenarioFrames = checked((headshotValidationScenario ? headshotScenarioSeconds : 15) * 60);
            _pristineCapture = new(validationFixture != DeveloperValidationFixtureId.None);
            Scene = Scene.CreateHeadless();
            Scene.Random.SetRng1(rng1);
            Scene.Random.SetRng2(rng2);
            Combat = new ServerCombat(lagCompEnabled, projectileCatchUpEnabled, Scene.Random.Rng2,
                historicalDynamicCollisionEnabled);
            Combat.BindScene(Scene);
                Scene.Services = new ServerSceneServices(Combat);
            try
            {
                Scene.Match.ApplyRules(rules);
                if (validationFixture == DeveloperValidationFixtureId.None)
                {
                    ServerContent.RequireRoom(rules.RoomKey, rules.Mode.ToLegacyMode());
                    Scene.LoadServerRoom(rules.RoomKey, rules.Mode.ToLegacyMode(), players: 8,
                        roomPlayerCount: ServerContent.ResolveRoomPlayerCount(
                            rules.EntityLayerPlayerCount, 8));
                }
                else
                {
                    DeveloperValidationFixtureDescriptor descriptor
                        = DeveloperValidationFixtures.Require(validationFixture);
                    if (rules.RoomKey != descriptor.MapKey || rules.Mode.ToLegacyMode() != descriptor.Mode)
                        throw new ProgramException("Developer validation fixture rules mismatch.");
                    Scene.LoadServerValidationFixture(validationFixture, descriptor.Mode, players: 8,
                        roomPlayerCount: NetConfig.RoomPlayerCount);
                }
                // Room entities and their collision shapes now exist. Freeze
                // the match-owned registry before any authoritative tick.
                Combat.InitializeHistoricalCollision(Scene);
                if (validationFixture != DeveloperValidationFixtureId.None)
                {
                    DeveloperValidationFixtureRegistryEvidence expected
                        = DeveloperValidationFixtures.Require(validationFixture).ExpectedRegistry;
                    HistoricalCollisionRegistry registry = Combat.HistoricalCollisionRegistry;
                    if (registry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Door) != expected.Doors
                        || registry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.ForceField) != expected.ForceFields
                        || registry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Object) != expected.Objects
                        || registry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Platform) != expected.Platforms
                        || registry.Count != expected.Total)
                        throw new ProgramException("Developer validation fixture dynamic collision registry mismatch.");
                }
                WorldStateCapture.ValidateRoom(Scene,
                    allowValidationFixtureDynamics: validationFixture != DeveloperValidationFixtureId.None);
                if (!ServerContentPackage.HasObjectives(Scene, rules.Mode.ToLegacyMode()))
                {
                    throw new ProgramException($"{rules.RoomKey}/{rules.Mode} has no required objectives in the multiplayer entity layout.");
                }
                foreach (PlayerEntity player in Scene.GetPlayerEntities()) { player.ServerDeactivate(); }
                Scene.Players.ActiveCount = 0;
                Scene.Players.MaxPlayers = rules.MaxPlayers;
                Scene.Match.ApplyRules(rules);
                Scene.Match.RadarPlayers = rules.PlayerRadar;
                Lifecycle = new MatchLifecycle(Scene.Match);
                Bots = new ServerBotManager(this, botFill ?? new());
                _initialRng1 = Scene.Random.Rng1;
                _initialRng2 = Scene.Random.Rng2;
                Scene.SpawnDirector.Reset(_initialRng2);
                _pristineWorld = CaptureCompetitiveWorld();
                _resetForCountdown = ResetForCountdown;
            }
            catch
            {
                Scene.CloseHeadless();
                throw;
            }
        }

        public void Step(ServerNetwork network, uint tick)
        {
            Combat.BeginTick(tick);
            _network = network;
            Scene.Match.MatchId = network.MatchId;
            if (network.RebalanceBeforeStart() && Scene.Match.Phase == MatchPhase.Countdown)
            {
                // Restart the countdown through its owner so new teams get a
                // pristine spawn/input epoch before any world step can run.
                Lifecycle.AdvanceBeforeStep(tick, eligible: false, _resetForCountdown);
            }
            int active = 0;
            uint teams = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                PlayerEntity player = Scene.Players[slot];
                if (peer?.Connection.State == NetConnectionState.Ready && peer.ReturningParticipant
                    && Scene.Match.Rules.Mode is MatchMode.Survival or MatchMode.TeamSurvival
                    && (peer.SurvivalEliminated || Scene.Match.Deaths[slot] > Scene.Match.Rules.LegacyPointGoal))
                    peer.WaitingForNextMatch = true;
                if (peer?.Connection.State == NetConnectionState.Ready && !peer.ReturningParticipant
                    && Scene.Match.Phase is MatchPhase.Playing or MatchPhase.Ending or MatchPhase.Intermission
                    && Scene.Match.Rules.LateJoinPolicy == LateJoinPolicy.SpectateUntilNextMatch)
                    peer.WaitingForNextMatch = true;
                if (_activeConnections[slot] != 0 && _activeConnections[slot] != peer?.Connection.Id)
                {
                    Reports?.LeaveSlot(Scene, slot, tick);
                    if (Scene.Match.Result == null)
                    {
                        WorldStateCapture.ReleasePlayer(Scene, player);
                        if (!network.HasReconnectReservation(slot) && peer?.ReturningParticipant != true)
                            NetScoreboard.ForgetSlot(Scene, slot);
                    }
                    player.ServerDeactivate();
                    _activeConnections[slot] = 0;
                }
                if (peer?.Connection.State == NetConnectionState.Ready && peer.WaitingForNextMatch)
                {
                    // Session readiness delivers the world to an observer without
                    // activating a gameplay body or touching terminal statistics.
                    peer.Connection.StartPlaying();
                }
                else if (peer?.Connection.State == NetConnectionState.Ready && Scene.Match.Result == null)
                {
                    if (!peer.ReturningParticipant) NetScoreboard.ForgetSlot(Scene, slot);
                    Scene.Roster.Nicknames[slot] = peer.Name;
                    player.ServerActivate(peer.Connection.Id, peer.Hunter, peer.TeamIndex);
                    if (_headshotValidationScenario)
                    {
                        player.ModArmWeapon(BeamType.Imperialist);
                        player.EquipInfo.InfiniteAmmo = true;
                    }
                    peer.HasParticipated = true;
                    Reports?.Activate(Scene, peer, tick);
                    _activeConnections[slot] = peer.Connection.Id;
                    peer.Inputs.SetInputEpoch(player.ServerCombatIdentity.Life, tick,
                        player.ModGunVector);
                    peer.Connection.StartPlaying();
                }
                if (peer?.Connection.State == NetConnectionState.Playing && !peer.WaitingForNextMatch)
                {
                    active++;
                    if (peer.TeamIndex < Scene.Match.Rules.TeamCount)
                        teams |= 1u << peer.TeamIndex;
                }
            }
            Bots.Update(network, tick);
            foreach (var bot in Bots.Participants)
                if (bot != null)
                {
                    active++;
                    if (bot.TeamIndex < Scene.Match.Rules.TeamCount)
                        teams |= 1u << bot.TeamIndex;
                }
            Scene.Players.ActiveCount = active;
            bool eligible = ((ReportingMayStart && AdminMayStart && ReplayMayStart && !VoteLobbyHold) || Scene.Match.Phase == MatchPhase.Playing) && active >= (Scene.Match.Rules.MaxPlayers == 1 ? 1 : 2)
                && (!Scene.Match.Rules.Teams
                    || teams == (1u << Scene.Match.Rules.TeamCount) - 1);
            MatchPhase previousPhase = Scene.Match.Phase;
            Lifecycle.AdvanceBeforeStep(tick, eligible, _resetForCountdown);
            if (Scene.Match.Phase != previousPhase) { DiscardInputs(network); }
            PublishPhase(network);
            if (Scene.Match.Phase == MatchPhase.Playing)
            {
                Reports?.BeginPlaying(Scene, network, tick);
                foreach (var bot in Bots.Participants) if (bot != null) Reports?.ActivateBot(Scene, bot, tick);
                for (int slot = 0; slot < 8; slot++)
                {
                    ServerPeer? peer = network.Peers[slot];
                    if (peer?.Connection.State != NetConnectionState.Playing) { continue; }
                    PlayerEntity player = Scene.Players[slot];
                    uint inputEpoch = player.ServerCombatIdentity.Life;
                    if (inputEpoch != 0 && peer.Inputs.InputEpoch != inputEpoch)
                    {
                        // Spawn is the owner of life transitions. This check
                        // is a same-tick safeguard for tests and hosts that
                        // enter Playing without the normal activation branch.
                        peer.Inputs.SetInputEpoch(inputEpoch, tick, player.ModGunVector);
                    }
                    InputCommand input = peer.Inputs.Take(tick,
                        out byte rewindPresentationDelayTicks);
                    if (peer.WaitingForNextMatch) continue;
                    if (inputEpoch == 0 || input.InputEpoch != inputEpoch)
                    {
                        // Never let a stale fallback command mutate Controls or
                        // enqueue a weapon/boost edge after a respawn. Keep the
                        // combat journal's slot command current and neutral so
                        // later attribution cannot observe a prior-life edge.
                        player.Controls.ClearAll();
                        player.Input.ClearBoostIntents();
                        Combat.SetCommand(slot, NeutralNetworkInput(tick, inputEpoch),
                            peer.Connection.Metrics.SmoothedRttMs,
                            rewindPresentationDelayTicks);
                        continue;
                    }
                    Combat.SetCommand(slot, input, peer.Connection.Metrics.SmoothedRttMs,
                        rewindPresentationDelayTicks);
                    player.ApplyNetworkInput(input);
                }
                if (_headshotValidationScenario)
                    ApplyHeadshotValidationInput(network, tick);
                else
                {
                    foreach (var bot in Bots.Participants)
                        if (bot != null) Combat.SetCommand(bot.Slot, new InputCommand(tick, tick, tick, InputButtons.None, InputButtons.None, Scene.Players[bot.Slot].ModGunVector, InputCommand.NoWeapon));
                }
                ulong beforeFrame = Scene.FrameCount;
                Scene.StepHeadlessFrame();
                if (_headshotValidationScenario && Scene.Match.Phase == MatchPhase.Playing)
                    _headshotScenarioPlayingFrames++;
                for (int slot = 0; slot < 8; slot++)
                {
                    ServerPeer? peer = network.Peers[slot];
                    if (peer?.Connection.State != NetConnectionState.Playing) continue;
                    uint inputEpoch = Scene.Players[slot].ServerCombatIdentity.Life;
                    if (inputEpoch != 0 && peer.Inputs.InputEpoch != inputEpoch)
                    {
                        // NoteServerCombatSpawn owns this transition. Update
                        // the bounded stream immediately after the scene pass,
                        // before the next Poll can enqueue old retransmits.
                        peer.Inputs.SetInputEpoch(inputEpoch, tick,
                            Scene.Players[slot].ModGunVector);
                        Combat.SetCommand(slot, NeutralNetworkInput(tick, inputEpoch),
                            peer.Connection.Metrics.SmoothedRttMs,
                            peer.Timing.RewindPresentationDelayTicks);
                    }
                }
                if (Scene.FrameCount != beforeFrame) Reports?.RecordPlayedStep(tick);
                if (Scene.Match.Phase == MatchPhase.Playing) { Combat.CatchUp.Drain(); }
                else { Combat.CatchUp.Clear(); }
            }
            Lifecycle.ObserveCompletion(tick);
            Reports?.Complete(Scene, tick);
            if (Scene.Match.Rules.Mode is MatchMode.Survival or MatchMode.TeamSurvival)
            {
                foreach (ServerPeer? peer in network.Peers)
                {
                    if (peer is { HasParticipated: true } && Scene.Players[peer.Slot].Health == 0
                        && Scene.Match.TeamDeaths[peer.TeamIndex] > Scene.Match.Rules.LegacyPointGoal)
                        peer.SurvivalEliminated = true;
                }
            }
            PublishPhase(network);
            _stateCount = 0;
            if (Scene.Match.Phase == MatchPhase.Playing)
            {
                // Record exactly once at the completed authoritative boundary;
                // player samples below share this same tick.
                Combat.DynamicCollisionHistory.Record(tick);
            }
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                if (peer is { WaitingForNextMatch: true } && peer.Connection.State == NetConnectionState.Playing)
                {
                    _states[_stateCount++] = new SnapshotPlayer
                    {
                        Slot = peer.Slot, Hunter = peer.Hunter, TeamIndex = peer.TeamIndex,
                        ConnectionId = peer.Connection.Id, Life = 1,
                        Flags = SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch,
                        Aim = -OpenTK.Mathematics.Vector3.UnitZ, Facing = -OpenTK.Mathematics.Vector3.UnitZ,
                        EnhancedTargetSlot = 255
                    };
                    continue;
                }
                if (_activeConnections[slot] == 0 && !Bots.Occupied(slot)) { continue; }
                PlayerEntity player = Scene.Players[slot];
                player.ModRepairVectors();
                SnapshotPlayer state = player.CaptureServerState();
                _states[_stateCount++] = state;
                if (Scene.Match.Phase == MatchPhase.Playing)
                {
                    Combat.History.Record(tick, player, state.ConnectionId, state.Life);
                }
            }
        }

        /// <summary>
        /// Drives only the bot seat in the explicit developer headshot fixture.
        /// BotManager still owns the roster/identity; setting IsBot false after
        /// activation makes the ordinary PlayerEntity input path own movement
        /// while preserving the bot participant and its authoritative identity.
        /// </summary>
        private void ApplyHeadshotValidationInput(ServerNetwork network, uint tick)
        {
            BotParticipant? target = null;
            foreach (BotParticipant? candidate in Bots.Participants)
            {
                if (candidate != null)
                {
                    target = candidate;
                    break;
                }
            }
            if (target is not { } bot) return;
            PlayerEntity targetPlayer = Scene.Players[bot.Slot];
            uint inputEpoch = targetPlayer.ServerCombatIdentity.Life;
            if (HeadshotValidationController.NeedsRespawn(targetPlayer.Health))
            {
                // Imperialist headshots are genuinely lethal at the fixture's
                // production health. Exercise the same production fire-to-
                // respawn input edge instead of restoring health or injecting
                // a synthetic damage/hit. Spawn and the new life epoch remain
                // owned by PlayerProcess and the authoritative simulation.
                targetPlayer.IsBot = false;
                InputCommand respawn = HeadshotValidationController.CreateRespawnCommand(
                    tick, targetPlayer.ModGunVector, inputEpoch);
                targetPlayer.ApplyNetworkInput(respawn);
                Combat.SetCommand(bot.Slot, respawn);
                return;
            }
            if (!targetPlayer.ModInPlay)
            {
                // Spawn/respawn remains owned by the normal bot lifecycle. The
                // next active tick re-enters this deterministic controller.
                return;
            }

            PlayerEntity? shooter = null;
            for (int slot = 0; slot < Scene.Match.Rules.MaxPlayers; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                if (peer?.Connection.State == NetConnectionState.Playing
                    && !peer.WaitingForNextMatch && slot != bot.Slot)
                {
                    shooter = Scene.Players[slot];
                    shooter.ModArmWeapon(BeamType.Imperialist);
                    shooter.EquipInfo.InfiniteAmmo = true;
                    break;
                }
            }
            if (shooter == null) return;

            // Rejoining replaces the human connection identity and respawns
            // its body. Re-arm the current choreography arm so the target is
            // staged against the new authoritative shooter position instead
            // of waiting for the next ten-second arm boundary.
            ulong shooterConnection = shooter.ServerCombatIdentity.ConnectionId;
            if (_headshotValidationShooterConnection != shooterConnection)
            {
                _headshotValidationShooterConnection = shooterConnection;
                _headshotValidationArm = -1;
            }

            Vector3 toShooter = shooter.Position - targetPlayer.Position;
            Vector3 aim = toShooter.LengthSquared > 0.001f
                ? toShooter.Normalized() : -Vector3.UnitZ;
            float range = toShooter.Length;
            uint scenarioTick = checked((uint)_headshotScenarioPlayingFrames);
            HeadshotValidationPlan plan = HeadshotValidationController.Plan(
                scenarioTick, _headshotScenarioFrames / 60, range);
            if (HeadshotValidationController.NeedsRestage(plan.Arm,
                    _headshotValidationArm, inputEpoch,
                    _headshotValidationTargetLife))
            {
                // Each test arm begins at an observed close/long range. This
                // staging is confined to the explicit developer fixture; the
                // normal movement path remains responsible for the rest of
                // the arm and supplies the motion evidence used by the gate.
                Vector3 horizontal = targetPlayer.Position - shooter.Position;
                horizontal.Y = 0;
                Vector3 direction = horizontal.LengthSquared > 0.001f
                    ? horizontal.Normalized() : Vector3.UnitZ;
                float desiredRange = plan.LongRange ? 18f : 6f;
                Vector3 desired = SelectHeadshotStagingPosition(
                    shooter, targetPlayer, direction, desiredRange, out direction);
                targetPlayer.Reposition(desired - targetPlayer.Position, targetPlayer.NodeRef);
                // The ordinary target command aims back at the shooter. Set
                // the initial facing to the same radial direction so Forward
                // and Back are deterministic and Right/Left are a real
                // strafe, rather than depending on the spawn pose.
                targetPlayer.Reposition(targetPlayer.Position, -direction, targetPlayer.NodeRef);
                targetPlayer.Speed = Vector3.Zero;
                _headshotValidationArm = plan.Arm;
                Vector3 stagedAim = shooter.Position - targetPlayer.Position;
                aim = stagedAim.LengthSquared > 0.001f
                    ? stagedAim.Normalized() : -Vector3.UnitZ;
                _headshotValidationTargetLife = inputEpoch;
            }
            // The participant remains a bot to the Node/roster, but its body
            // receives an ordinary authoritative command for this test arm.
            targetPlayer.IsBot = false;
            InputCommand command = HeadshotValidationController.CreateCommand(
                tick, plan, aim, inputEpoch);
            targetPlayer.ApplyNetworkInput(command);
            Combat.SetCommand(bot.Slot, command);
        }

        /// <summary>
        /// Choose a deterministic range position whose actual production beam
        /// ray is not hidden by the validation room. The old fixture preserved
        /// the previous radial direction, which could place the bot behind a
        /// wall; the rendered client then aimed correctly at the presented bot
        /// while every legal beam struck the wall first. This helper is only
        /// reachable through the explicit headshot validation Worker flag.
        /// </summary>
        private Vector3 SelectHeadshotStagingPosition(PlayerEntity shooter,
            PlayerEntity target, Vector3 preferredDirection, float desiredRange,
            out Vector3 selectedDirection)
        {
            preferredDirection.Y = 0;
            if (preferredDirection.LengthSquared <= 0.001f)
                preferredDirection = Vector3.UnitZ;
            else
                preferredDirection = preferredDirection.Normalized();

            // Sixteen fixed 22.5-degree rotations keep the choreography
            // deterministic while covering the small validation room without
            // introducing a random or map-specific spawn choice.
            for (int index = 0; index < 16; index++)
            {
                float angle = index * (MathF.PI / 8);
                float cosine = MathF.Cos(angle);
                float sine = MathF.Sin(angle);
                Vector3 direction = new(
                    preferredDirection.X * cosine - preferredDirection.Z * sine,
                    0,
                    preferredDirection.X * sine + preferredDirection.Z * cosine);
                Vector3 candidate = shooter.Position + direction * desiredRange;
                candidate.Y = target.Position.Y;
                if (HasHeadshotStagingLineOfSight(shooter, target, candidate))
                {
                    selectedDirection = direction;
                    return candidate;
                }
            }

            // Do not claim a clear shot when the fixture has no legal
            // candidate. The fallback preserves the previous staging behavior
            // so the run reports a genuine invalid choreography instead of
            // changing authoritative collision or fabricating a hit.
            selectedDirection = preferredDirection;
            Vector3 fallback = shooter.Position + preferredDirection * desiredRange;
            fallback.Y = target.Position.Y;
            return fallback;
        }

        private bool HasHeadshotStagingLineOfSight(PlayerEntity shooter,
            PlayerEntity target, Vector3 candidate)
        {
            // Use the same muzzle origin that BeamProjectileEntity consumes;
            // the camera/eye point is not a sufficient proxy in this fixture.
            Vector3 start = shooter._muzzlePos;
            if (start.LengthSquared <= 0.001f)
                start = shooter.Position.AddY(Fixed.ToFloat(shooter.Values.AimYOffset));
            Vector3 end = candidate.AddY(
                Fixed.ToFloat(target.Values.MaxPickupHeight) - 0.15f);
            CollisionResult collision = default;
            return !CollisionDetection.CheckBetweenPoints(start, end,
                TestFlags.Beams | TestFlags.Players, Scene, ref collision);
        }

        private void PublishPhase(ServerNetwork network)
        {
            network.Phase = Scene.Match.Phase;
            network.PhaseRevision = Scene.Match.PhaseRevision;
        }

        private static InputCommand NeutralNetworkInput(uint tick, uint inputEpoch)
            => new(tick, tick, tick, InputButtons.None, InputButtons.None,
                -OpenTK.Mathematics.Vector3.UnitZ, InputCommand.NoWeapon, inputEpoch);

        private void ResetForCountdown()
        {
            ServerNetwork network = _network ?? throw new InvalidOperationException("Countdown requires a server session.");
            AssertPristineWorld();
            Combat.Reset();
            _headshotValidationArm = -1;
            _headshotScenarioPlayingFrames = 0;
            _headshotValidationShooterConnection = 0;
            _headshotValidationTargetLife = 0;
            Scene.Match.ResetCompetitiveState();
            Scene.Match.Flow.ResetProgress();
            for (int slot = 0; slot < 8; slot++) { Scene.Players[slot].ServerDeactivate(); }
            Scene.Random.SetRng1(_initialRng1);
            Scene.Random.SetRng2(_initialRng2);
            Scene.SpawnDirector.Reset(_initialRng2);
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                if (peer?.Connection.State == NetConnectionState.Playing)
                {
                    Scene.Players[slot].ServerActivate(peer.Connection.Id, peer.Hunter, peer.TeamIndex);
                    if (_headshotValidationScenario)
                    {
                        Scene.Players[slot].ModArmWeapon(BeamType.Imperialist);
                        Scene.Players[slot].EquipInfo.InfiniteAmmo = true;
                    }
                }
            }
            foreach (var bot in Bots.Participants) if (bot != null) Bots.Activate(bot);
            DiscardInputs(network);
            AssertPristineWorld();
            CountdownResets++;
        }

        private void DiscardInputs(ServerNetwork network)
        {
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer != null) { peer.Inputs = new ServerInputStream(); }
            }
            foreach (PlayerEntity player in Scene.Players) { player.Controls.ClearAll(); }
        }

        private WorldRecord[] CaptureCompetitiveWorld()
        {
            _pristineCapture.Capture(Scene, Scene.Match.MatchId == 0 ? 1u : Scene.Match.MatchId, 0, 0);
            var records = new List<WorldRecord>();
            foreach (WorldRecord record in _pristineCapture.Records)
            {
                if (record.Kind is WorldRecordKind.Item or WorldRecordKind.Spawner or WorldRecordKind.Node or WorldRecordKind.Flag)
                {
                    records.Add(record);
                }
            }
            return records.ToArray();
        }

        internal void AssertPristineWorld()
        {
            if (Scene.FrameCount != 0 || Scene.LiveFrames != 0
                || !CaptureCompetitiveWorld().AsSpan().SequenceEqual(_pristineWorld))
            {
                throw new InvalidOperationException("The waiting world changed before the competitive reset.");
            }
        }

        public void Dispose() => Scene.CloseHeadless();
    }
}
