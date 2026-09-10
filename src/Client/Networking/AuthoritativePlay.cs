using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
using MphRead.Mods.Input;

namespace MphRead.Mods.Network
{
    /// <summary>Game-thread bridge for the authoritative client.</summary>
    public sealed partial class AuthoritativePlay : IDisposable
    {
        public enum TerminalState { Active, Completed, Failed, Disposed }
        public TerminalState State { get; private set; }
        public Guid? NodeMatchId { get; private set; }
        public bool Interrupted { get; private set; }
        public bool ObserveCompletion()
        {
            NodeMatchId ??= NodeSessions.Current?.State.JoinedMatchId;
            if (State == TerminalState.Active && NodeMatchId is Guid id
                && NodeSessions.Current?.CompletionFor(id) is { } ended)
            {
                Interrupted = ended.Interrupted;
                State = ended.Interrupted ? TerminalState.Failed : TerminalState.Completed;
            }
            return State != TerminalState.Active;
        }

        private readonly CompletionResultDrain _completionDrain = new();

        /// <summary>Once Node completion stops input, drain queued terminal replication for at most
        /// 250 ms. Missing UDP results remain unavailable; never synthesize an authority result.</summary>
        public bool DrainCompletion(Scene scene)
        {
            if (!ObserveCompletion()) return false;
            return _completionDrain.Advance(Stopwatch.GetElapsedTime(0).TotalSeconds,
                () =>
                {
                    Client.Poll();
                    DemoRecorder.RecordFrame(Client, scene);
                    _world.Apply(scene, Client.HasSnapshot ? Client.Snapshot.ServerTick : null);
                }, () => scene.Match.Result != null);
        }

        public static AuthoritativePlay? Current { get; private set; }
        public static bool Active => Current != null || DemoPlayback.IsModern;
        public static bool ApplyingSnapshot { get; private set; }
        private readonly NetTransport _transport;
        private readonly InputCommand[] _inputs = new InputCommand[InputBundle.Capacity];
        private readonly ulong[] _identities = new ulong[8];
        private readonly uint[] _lives = new uint[8];
        private uint _sequence;
        private int _inputCount;
        private long _appliedSnapshot;
        private uint _loadedMatch;
        private uint _appliedRoleRevision;
        private long _lastPresentation = Stopwatch.GetTimestamp();
        private readonly ClientWorldState _world = new();
        private readonly SnapshotInterpolation _interpolation = new();
        private readonly ProjectilePresentationMeasurement _projectilePresentation = new();
        private readonly PredictedHitFeedback _hitPrediction = new();
        private readonly PredictedSelfImpulse _selfImpulse = new();
        private Scene? _presentationScene;
        private ulong _viewConnectionId;
        private uint _inputViewTick;
        private bool _hasInputViewTick;
        private SnapshotPresentation _pendingPresentation;
        private bool _presentationPending;
        private bool _localVelocityApplied;
        private uint _localVelocityAppliedTick;
        public NetClient Client { get; }
        public ClientPrediction Prediction { get; } = new();
        /// <summary>Per-match presentation diagnostics; never gameplay authority.</summary>
        public ProjectilePresentationMeasurement ProjectilePresentation => _projectilePresentation;
        public PredictedHitFeedback HitPrediction => _hitPrediction;
        public PredictedSelfImpulse SelfImpulse => _selfImpulse;
        public long CombatEvents { get; private set; }
        public long DamageEvents { get; private set; }
        public bool HasWorldState => _world.HasState;
        public uint WorldServerTick => _world.ServerTick;
        private uint _inputPhaseRevision;
        public Vector3 VisualOffset => Prediction.VisualOffset;
        public bool IsObserver => Client.IsObserver;
        public int LocalSlot => IsObserver ? -1 : Client.Accepted.Slot;
        internal Action<PlayerEntity, uint>? ScriptInput { get; set; }

        public AuthoritativePlay(string host, int port, string name, Hunter hunter, ulong? joinNonce = null, string ticket = "", bool observer = false, uint wireMatchId = 0)
        {
            if (Current != null || NetSession.Active)
            {
                throw new InvalidOperationException("A network session is already active.");
            }
            IPAddress? address = Array.Find(Dns.GetHostAddresses(host),
                candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
            if (address == null) { throw new ProgramException($"{host} has no IPv4 address."); }
            var endpoint = new IPEndPoint(address, port);
            _transport = new NetTransport(0);
            try { Client = new NetClient(_transport, endpoint, name, Launcher.Hunters.Resolve(hunter), joinNonce, ticket, observer, wireMatchId); }
            catch { _transport.Dispose(); throw; }
            Client.WorldPacketValidator = WorldPacket.TryValidate;
            Client.WorldPacketReceived = payload =>
            {
                _world.Receive(payload);
                DemoRecorder.RecordWorld(payload);
            };
            Current = this;
        }

        public void Join()
        {
            while (Client.State == NetConnectionState.Connecting)
            {
                Client.Poll();
                if (Client.Failure != null) { throw new ProgramException(Client.Failure); }
                Thread.Sleep(10);
            }
            NetLaunch.DisableCheatsForMatch();
        }

        public void BuildPlayers(Scene scene, Hunter hunter, int recolor)
        {
            _presentationScene = scene;
            foreach (NetRosterEntry entry in Client.Roster) scene.Roster.Nicknames[entry.Slot] = entry.Name;
            if (_loadedMatch != Client.Accepted.MatchId)
            {
                GamepadHaptics.Stop(clearIdentities: true);
                InputBalanceTelemetry.ResetAttribution();
            }
            _loadedMatch = Client.Accepted.MatchId;
            _world.Reset(_loadedMatch);
            scene.Match.MatchId = Client.Accepted.MatchId;
            scene.Match.ApplyRules(Client.Accepted.Rules);
            scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < scene.Players.MaxPlayers; slot++)
            {
                scene.AddPlayer(slot == LocalSlot ? hunter : Hunter.Samus,
                    slot == LocalSlot ? recolor : 0,
                    GetAssignedTeam(slot));
                PlayerEntity player = scene.Players[slot];
                player.IsBot = false;
                // The local slot must initialize its camera/HUD while the
                // room loads. Spawn selection remains disabled on clients.
                if (slot != LocalSlot) { player.LoadFlags &= ~LoadFlags.Active; }
            }
            scene.LocalPlayerSlot = IsObserver ? 0 : LocalSlot;
            scene.Players.ActiveCount = IsObserver ? 0 : 1;
            if (IsObserver) SpectatorMode.Start(scene);
        }

        public void BeforeSimulation(Scene scene)
        {
            _localVelocityApplied = false;
            if (ObserveCompletion()) return;
            // Input was sampled before this hook. New packets below must not
            // change which previously presented picture that input refers to.
            _hasInputViewTick = _interpolation.TryCaptureViewTick(out _inputViewTick);
            Client.Poll();
            DemoRecorder.RecordFrame(Client, scene);
            if (Client.Failure != null)
            {
                if (ObserveCompletion()) return;
                State = TerminalState.Failed;
                _projectilePresentation.Clear();
                _hitPrediction.Clear();
                _selfImpulse.Clear();
                throw new ProgramException(Client.Failure);
            }
            ulong connectionId = Client.Connection?.Id ?? 0;
            if (connectionId != _viewConnectionId)
            {
                _viewConnectionId = connectionId;
                if (scene.Presentation is ScenePresentation sessionPresentation)
                    sessionPresentation.WorldFeedback.ClearPendingNotices();
                ResetPresentation();
                _projectilePresentation.Clear();
                _hitPrediction.Clear();
                _selfImpulse.Clear();
                _inputCount = 0;
                Prediction.Reset();
                _appliedSnapshot = 0;
            }
            if (Client.Connection != null && (Client.Accepted.MatchId != _loadedMatch || Client.RoleRevision != _appliedRoleRevision))
            {
                if (Client.RoleRevision != _appliedRoleRevision)
                {
                    if (scene.Presentation is ScenePresentation rolePresentation)
                        ResetRoleFeedback(rolePresentation.CombatFeedback, rolePresentation.WorldFeedback,
                            rolePresentation.Announcer, rolePresentation.AwardHud);
                    else Chat.ChatBox.Clear();
                    foreach (PlayerEntity player in scene.Players) player.Controls.ClearAll();
                }
                _appliedRoleRevision = Client.RoleRevision;
                if (_loadedMatch != Client.Accepted.MatchId)
                {
                    GamepadHaptics.Stop(clearIdentities: true);
                    InputBalanceTelemetry.ResetAttribution();
                }
                _loadedMatch = Client.Accepted.MatchId;
                scene.Match.MatchId = Client.Accepted.MatchId;
                Array.Clear(_identities);
                Array.Clear(_lives);
                for (int slot = 0; slot < 8; slot++) { NetScoreboard.ForgetSlot(scene, slot); }
                _inputCount = 0;
                _appliedSnapshot = 0;
                Prediction.Reset();
                ResetPresentation();
                _projectilePresentation.Clear();
                _hitPrediction.Clear();
                _selfImpulse.Clear();
                _world.Reset(_loadedMatch);
                // Reliable rotation configuration is authoritative before any player rebuild.
                scene.Match.ApplyRules(Client.Accepted.Rules);
                scene.Match.Flow.ResetProgress();
                scene.Match.Phase = MatchPhase.WaitingForPlayers;
                scene.Match.PhaseRevision = 1;
                scene.Match.PhaseStartTick = Client.Accepted.ServerTick;
                scene.Match.HasPhaseDeadline = false;
                scene.Match.MatchTime = Client.Accepted.Rules.TimeLimit.HasValue
                    ? (float)Client.Accepted.Rules.TimeLimit.Value.TotalSeconds : -1;
                scene.Match.RadarPlayers = Client.Accepted.Rules.PlayerRadar;
                (RoomMetadata? metadata, _) = Metadata.GetRoomByName(Client.Accepted.Room);
                scene.TransitionRoomId = metadata?.Id
                    ?? throw new ProgramException($"Unknown server room: {Client.Accepted.Room}");
                (scene.Room ?? throw new ProgramException("Network scene has no room.")).LoadRoom(resume: false);
            }
            // This hook runs only after Scene.OnLoad. The socket's independent
            // keepalive covers the entire synchronous content load.
            if (Client.State == NetConnectionState.Loading) { Client.Ready(Client.Accepted.MatchId); }
            if (Client.HasSnapshot && Client.SnapshotsReceived != _appliedSnapshot
                && (!_world.HasState || !Sequence32.IsNewer(_world.PhaseStartTick, Client.Snapshot.ServerTick)))
            {
                ApplySnapshot(scene);
                _interpolation.Add(Client.Snapshot, Client.SnapshotPlayers, Client.SnapshotReceivedAt);
                _appliedSnapshot = Client.SnapshotsReceived;
            }
            // First usable state after join/rotation may arrive during Poll.
            // Until then no input command invents a historical view timestamp.
            if (!_hasInputViewTick)
                _hasInputViewTick = _interpolation.TryCaptureViewTick(out _inputViewTick);
            _world.Apply(scene, Client.HasSnapshot ? Client.Snapshot.ServerTick : null);
            foreach (NetRosterEntry entry in Client.Roster) { scene.Roster.Nicknames[entry.Slot] = entry.Name; }
            if (scene.Presentation is ScenePresentation feedbackPresentation)
                feedbackPresentation.CombatFeedback.Bind(_loadedMatch,
                    LocalSlot is >= 0 and < 8 ? new CombatActor((byte)LocalSlot, _identities[LocalSlot], _lives[LocalSlot]) : CombatActor.None,
                    Client.Roster, WorldServerTick, scene.Match.PhaseRevision);
            if (scene.Presentation is ScenePresentation worldPresentation)
                worldPresentation.WorldFeedback.Bind(_loadedMatch, scene.Match.PhaseRevision);
            bool predictionPhaseChanged = _inputPhaseRevision != scene.Match.PhaseRevision;
            if (predictionPhaseChanged)
            {
                _hitPrediction.ResetEpoch();
                _selfImpulse.ResetEpoch();
            }
            _projectilePresentation.SetContext(_loadedMatch, GetLocalCombatActor());
            _projectilePresentation.Advance();
            _hitPrediction.SetContext(_loadedMatch, GetLocalCombatActor());
            _hitPrediction.Advance();
            _selfImpulse.SetContext(_loadedMatch, GetLocalCombatActor());
            if (_localVelocityApplied)
                _selfImpulse.NoteAppliedSnapshot(_localVelocityAppliedTick);
            _selfImpulse.Advance();
            DrainEvents();
            if (_inputPhaseRevision != scene.Match.PhaseRevision)
            {
                _inputPhaseRevision = scene.Match.PhaseRevision;
                _inputCount = 0;
                Prediction.Reset();
                _interpolation.Reset();
                _hasInputViewTick = false;
            }
            PrepareRemoteLocomotion(scene);
            if (LocalSlot >= 0 && scene.Match.Phase == MatchPhase.Playing)
            { ScriptInput?.Invoke(scene.Players[LocalSlot], _sequence); }
            else if (LocalSlot >= 0) { scene.Players[LocalSlot].Controls.ClearAll(); }
            NetDiagnostics.ReportAuthoritative(Client, Prediction, _interpolation, _transport.Metrics,
                _hitPrediction, _selfImpulse);
        }

        public PlayerEntity RebuildPlayers(Scene scene, Hunter hunter, int recolor)
        {
            scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < 8; slot++)
            {
                PlayerEntity player = scene.Players.Create(slot == LocalSlot ? hunter : Hunter.Samus,
                    slot == LocalSlot ? recolor : 0) ?? throw new ProgramException("Could not rebuild network player.");
                player.LoadFlags = LoadFlags.SlotActive | LoadFlags.Initial;
                if (slot == LocalSlot) { player.LoadFlags |= LoadFlags.Active; }
                player.NodeRef = player.CameraInfo.NodeRef = NodeRef.None;
                player.IsBot = false;
                player.TeamIndex = GetAssignedTeam(slot);
            }
            scene.LocalPlayerSlot = IsObserver ? 0 : LocalSlot;
            scene.Players.ActiveCount = IsObserver ? 0 : 1;
            if (IsObserver) SpectatorMode.Start(scene);
            return scene.LocalPlayer!;
        }

        private int GetAssignedTeam(int slot)
        {
            foreach (NetRosterEntry entry in Client.Roster)
            { if (entry.Slot == slot) { return entry.Team; } }
            // Before the first authoritative roster/snapshot, do not infer a team.
            return -1;
        }

        private void DrainEvents()
        {
            if (_presentationScene is not Scene scene) return;
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            while (Client.TryDequeueEvent(out NetApplicationEvent message))
            {
                DemoRecorder.RecordEvent(message);
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.Chat
                    && SessionChatPacket.TryRead(message.Payload.Span, out SessionChatPacket chat))
                {
                    Mods.Chat.ChatBox.Receive(new ChatPacket
                    {
                        Slot = chat.Slot, Kind = ChatPacket.KindSay, Name = chat.Name, Text = chat.Text
                    });
                    continue;
                }
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.WorldEvent
                    && WorldEvent.TryRead(message.Payload.Span, out WorldEvent worldEvent))
                {
                    if (_presentationScene?.Presentation is ScenePresentation worldPresentation)
                        worldPresentation.WorldFeedback.Process(worldEvent, worldPresentation.CombatFeedback.Local, WorldServerTick,
                            _presentationScene.Match.Rules.PickupRespawnAnnouncements);
                    continue;
                }
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.Kill
                    && KillEvent.TryRead(message.Payload.Span, out KillEvent kill))
                {
                    if (_presentationScene?.Presentation is ScenePresentation killPresentation)
                    {
                        bool accepted = killPresentation.CombatFeedback.Process(kill);
                        if (accepted && LocalSlot >= 0 && LocalSlot < scene.Players.Count)
                        {
                            PlayerEntity localPlayer = scene.Players[LocalSlot];
                            LookDeviceKind device = GamepadInput.LookCoordinator.ActiveLookDevice;
                            if (kill.Killer == GetLocalCombatActor() && kill.Weapon <= 10)
                                InputBalanceTelemetry.RecordKill(device,
                                    (int)localPlayer.Hunter, kill.Weapon);
                            if (kill.Victim == GetLocalCombatActor())
                                InputBalanceTelemetry.RecordDeath(device,
                                    (int)localPlayer.Hunter, (int)localPlayer.CurrentWeapon);
                        }
                    }
                    continue;
                }
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.MatchAward
                    && MatchAwardPacket.TryRead(message.Payload.Span, out MatchAwardPacket awardPacket)
                    && MatchAwardPacketConversion.TryToAward(awardPacket, out MatchAward award))
                {
                    if (_presentationScene?.Presentation is ScenePresentation awardPresentation)
                    {
                        // Both consumers receive the same authoritative fact;
                        // each owns its own bounded dedup/priority queue.
                        awardPresentation.Announcer.Consume(award);
                        awardPresentation.AwardHud.Enqueue(award);
                    }
                    continue;
                }
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.MatchSemantic
                    && MatchSemanticEventPacket.TryRead(message.Payload.Span, out MatchSemanticEventPacket semanticPacket)
                    && MatchSemanticEventPacketConversion.TryToEvent(semanticPacket, out MatchEvent semanticEvent))
                {
                    if (_presentationScene?.Presentation is ScenePresentation semanticPresentation)
                    {
                        byte localTeam = semanticPresentation.CombatFeedback.Local.IsValid
                            ? (byte)_presentationScene.Players[semanticPresentation.CombatFeedback.Local.Slot].TeamIndex
                            : (byte)255;
                        if (semanticEvent.Kind == MatchEventKind.MatchEnded)
                            semanticPresentation.Announcer.Consume(semanticEvent, localTeam);
                        else
                            semanticPresentation.Announcer.Consume(semanticEvent);
                    }
                    continue;
                }
                if (message.MatchId != _loadedMatch || message.Type != ReliableEventType.Combat
                    || !CombatEventBatch.TryRead(message.Payload.Span, events, out int count)) { continue; }
                foreach (CombatEvent value in events[..count])
                {
                    if (value.Kind == CombatEventKind.Shot)
                        _projectilePresentation.RecordAuthoritativeShot(value);
                    bool currentTarget = !value.Target.IsValid
                        || value.Target.Slot < _identities.Length
                        && _identities[value.Target.Slot] == value.Target.ConnectionId
                        && _lives[value.Target.Slot] == value.Target.Life;
                    if (_presentationScene?.Presentation is ScenePresentation feedbackPresentation
                        && !feedbackPresentation.CombatFeedback.Process(value,
                            allowLocalHitMarker: currentTarget)) continue;
                    CombatEvents++;
                    if (value.Kind == CombatEventKind.Damage
                        && value.Actor == GetLocalCombatActor()
                        && value.Target != value.Actor && value.Weapon <= 10)
                        InputBalanceTelemetry.RecordAttributedHit(
                            value.CommandSequence, value.Weapon, value.Amount);
                    if (value.Kind == CombatEventKind.Damage)
                    {
                        DamageEvents++;
                        if (value.Actor == GetLocalCombatActor() && currentTarget)
                            _hitPrediction.Confirm(value);
                        if (value.Actor == GetLocalCombatActor() && value.Target == value.Actor
                            && LocalSlot >= 0 && IsPredictionEpochActive(scene)
                            && _selfImpulse.ApplyAuthoritative(value,
                                scene.Players[LocalSlot].Speed, scene.Players[LocalSlot].IsAltForm,
                                out Vector3 authoritativeSpeed))
                            scene.Players[LocalSlot].Speed = authoritativeSpeed;
                    }
                    CombatActor subject = value.Kind is CombatEventKind.Shot or CombatEventKind.Bomb
                        ? value.Actor : value.Target;
                    if (!subject.IsValid || _identities[subject.Slot] != subject.ConnectionId
                        || _lives[subject.Slot] != subject.Life) { continue; }
                    scene.Players[subject.Slot].GetPresentation().PresentCombat(value,
                        predictedLocalShot: subject.Slot == LocalSlot);
                }
            }
        }

        public void AfterSimulation()
        {
            if (State != TerminalState.Active) return;
            if (_presentationScene is not Scene scene) return;
            if (IsObserver)
            {
                foreach (SnapshotPlayer state in Client.SnapshotPlayers)
                    scene.Players[state.Slot].ApplySnapshotTransform(state);
                return;
            }
            if (_presentationScene?.Match.Phase != MatchPhase.Playing || !_hasInputViewTick
                || Client.State is not (NetConnectionState.Ready or NetConnectionState.Playing)) { return; }
            PlayerEntity local = scene.Players[LocalSlot];
            local.ModRepairVectors();
            _inputs[_sequence % InputBundle.Capacity] = local.CaptureNetworkInput(_sequence, _inputViewTick);
            Prediction.Record(_sequence, local.Position, local.IsAltForm);
            _inputCount = Math.Min(_inputCount + 1, InputBundle.Capacity);
            Span<InputCommand> bundle = stackalloc InputCommand[InputBundle.Capacity];
            for (int i = 0; i < _inputCount; i++)
            {
                bundle[i] = _inputs[unchecked(_sequence - (uint)(_inputCount - 1 - i)) % InputBundle.Capacity];
            }
            Client.SendInputs(bundle[.._inputCount], _inputPhaseRevision);
            _sequence++;
            // Remote engine animation may advance, but its physics cannot
            // become truth. P5 supplies delayed transform presentation here.
            foreach (SnapshotPlayer state in Client.SnapshotPlayers)
            {
                if (state.Slot != LocalSlot) { scene.Players[state.Slot].ApplySnapshotTransform(state); }
            }
        }

        private void ApplySnapshot(Scene scene)
        {
            int occupied = 0;
            ApplyingSnapshot = true;
            try
            {
                foreach (SnapshotPlayer state in Client.SnapshotPlayers)
                {
                    int slot = state.Slot;
                    occupied |= 1 << slot;
                    PlayerEntity player = scene.Players[slot];
                    bool newIdentity = _identities[slot] != state.ConnectionId;
                    if (newIdentity)
                    {
                        player.ClientActivate(state);
                        _identities[slot] = state.ConnectionId;
                        _lives[slot] = 0;
                    }
                    bool newLife = _lives[slot] != state.Life;
                    bool local = slot == LocalSlot;
                    player.ApplyServerState(state, newLife, local);
                    if (local)
                        SpectatorMode.ApplyWaitingForMatch(scene, (state.Flags & SnapshotPlayerFlags.WaitingForMatch) != 0);
                    player.GetPresentation().ReconcileNetworkAfflictions(state, Client.Snapshot.ServerTick);
                    if (!local)
                    {
                        player.ApplySnapshotTransform(state);
                    }
                    else if (newIdentity || newLife || (state.Flags & SnapshotPlayerFlags.Spawned) == 0)
                    {
                        Prediction.Reset();
                        player.ApplySnapshotTransform(state, local: true);
                        _localVelocityApplied = true;
                        _localVelocityAppliedTick = Client.Snapshot.ServerTick;
                    }
                    else if (Client.Snapshot.HasProcessedInput)
                    {
                        Vector3 corrected = Prediction.Reconcile(Client.Snapshot.LastProcessedInput,
                            state.Position, (state.Flags & SnapshotPlayerFlags.AltForm) != 0, player.Position);
                        float correctionDistance = (corrected - player.Position).Length;
                        player.CorrectPredictedPosition(corrected);
                        _selfImpulse.NoteCorrection(correctionDistance, Prediction.LastCorrectionHard);
                        if (Prediction.LastCorrectionHard)
                        {
                            player.ApplyServerState(state, newLife: false);
                            player.Speed = state.Speed;
                            _localVelocityApplied = true;
                            _localVelocityAppliedTick = Client.Snapshot.ServerTick;
                        }
                    }
                    _lives[slot] = state.Life;
                }
                for (int slot = 0; slot < 8; slot++)
                {
                    if ((occupied & (1 << slot)) == 0 && _identities[slot] != 0)
                    {
                        scene.Players[slot].GetPresentation().ClearNetworkAfflictions();
                        scene.Players[slot].ServerDeactivate();
                        NetScoreboard.ForgetSlot(scene, slot);
                        _identities[slot] = 0;
                    }
                }
                // Death presentation can touch another player's score. Copy
                // the complete authoritative table after every entity update.
                foreach (SnapshotPlayer state in Client.SnapshotPlayers)
                {
                    scene.Match.Players[state.Slot].Points = state.Points;
                    scene.Match.Players[state.Slot].Kills = state.Kills;
                    scene.Match.Players[state.Slot].Deaths = state.Deaths;
                    scene.Match.Players[state.Slot].Assists = state.Assists;
                }
                scene.Players.ActiveCount = Client.SnapshotPlayers.Length;
            }
            finally { ApplyingSnapshot = false; }
        }

        public void Dispose()
        {
            State = TerminalState.Disposed;
            if (_presentationScene?.Presentation is ScenePresentation sessionPresentation)
                sessionPresentation.WorldFeedback.ClearPendingNotices();
            _projectilePresentation.Clear();
            _hitPrediction.Clear();
            _selfImpulse.Clear();
            InputBalanceTelemetry.ResetAttribution();
            Client.Close();
            Client.Dispose();
            _transport.Dispose();
            if (Current == this) { Current = null; }
        }

        public void AdvancePresentation()
        {
            if (State != TerminalState.Active) return;
            long now = Stopwatch.GetTimestamp();
            Prediction.AdvanceVisual(Stopwatch.GetElapsedTime(_lastPresentation, now).TotalSeconds);
            _lastPresentation = now;
        }

        // A role transfer can rewind within the same match/phase. It is a new
        // presentation epoch: live cues must not survive into a delayed view.
        internal static void ResetRoleFeedback(MphRead.Combat.CombatFeedback combat, MphRead.Combat.WorldFeedback world,
            MphRead.Mods.Audio.AnnouncerService? announcer = null, MphRead.Mods.Hud.AwardHudQueue? awardHud = null)
        {
            combat.Bind(0, CombatActor.None, ReadOnlySpan<NetRosterEntry>.Empty);
            world.Bind(0, 0);
            world.ClearPendingNotices();
            announcer?.Reset();
            awardHud?.Reset();
            Chat.ChatBox.Clear();
        }

        private void ResetPresentation()
        {
            _interpolation.Reset();
            _presentationPending = false;
            _hasInputViewTick = false;
            if (_presentationScene is Scene scene)
            {
                foreach (PlayerEntity player in scene.Players)
                    player.ResetRemoteLocomotion();
            }
        }

        private bool TryPrepareRemotePresentation(long now,
            out SnapshotPresentation presentation)
        {
            presentation = default;
            if (!Client.HasSnapshot) return false;
            double estimated = Client.Clock.Synchronized
                ? Client.Clock.EstimateServerTick(now)
                : Client.Snapshot.ServerTick
                    + Stopwatch.GetElapsedTime(Client.SnapshotReceivedAt, now).TotalSeconds * 60;
            return _interpolation.TryPreparePresentation(estimated, out presentation);
        }

        private void PrepareRemoteLocomotion(Scene scene)
        {
            bool prepared = TryPrepareRemotePresentation(Stopwatch.GetTimestamp(),
                out SnapshotPresentation presentation);
            for (int slot = 0; slot < 8; slot++)
            {
                PlayerEntity player = scene.Players[slot];
                if (slot == LocalSlot)
                {
                    // A role change can turn a former remote slot into the
                    // local player. Never carry remote presentation state over.
                    player.ResetRemoteLocomotion();
                }
                else if (prepared
                    && _interpolation.TrySamplePresentation(slot, presentation,
                        out SnapshotPlayerPresentation sample))
                {
                    player.SetRemoteLocomotionIntent(sample.State, sample.VisualSpeed);
                }
                else
                {
                    player.ResetRemoteLocomotion();
                }
            }
        }

        public void BeginRemotePresentation(Scene scene)
        {
            if (!ReferenceEquals(scene, _presentationScene)) return;
            _presentationPending = false;
            // The pause map replaces the world in GetDrawItems. It cannot
            // advance a claim about remote poses the player did not see.
            long now = Stopwatch.GetTimestamp();
            if (!TryPrepareRemotePresentation(now, out SnapshotPresentation presentation)) return;
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot != LocalSlot && _interpolation.TrySamplePresentation(slot, presentation,
                    out SnapshotPlayerPresentation sample))
                {
                    scene.Players[slot].GetPresentation().BeginInterpolatedPose(sample.State);
                }
            }
            _pendingPresentation = presentation;
            _presentationPending = true;
        }

        /// <summary>Called by the owning scene only after a successful buffer swap.</summary>
        public void CommitRemotePresentation(Scene scene)
        {
            if (!ReferenceEquals(scene, _presentationScene) || !_presentationPending) return;
            _presentationPending = false;
            _interpolation.MarkPresented(_pendingPresentation);
        }

        public void EndRemotePresentation()
        {
            if (_presentationScene is not Scene scene) return;
            foreach (PlayerEntity player in scene.Players) { player.GetPresentation().EndInterpolatedPose(); }
        }

        internal uint? ObservePredictedProjectile(PlayerEntity shooter)
        {
            if (_presentationScene is not Scene scene || !ReferenceEquals(scene, shooter.Scene)
                || shooter.SlotIndex != LocalSlot)
                return null;
            CombatActor actor = GetLocalCombatActor();
            if (!actor.IsValid) return null;
            _projectilePresentation.ObservePredictedShot(shooter, actor, _sequence);
            return _sequence;
        }

        internal CombatShot CapturePresentationAttribution(EntityBase owner)
        {
            PlayerEntity? player = owner switch
            {
                PlayerEntity value => value,
                HalfturretEntity turret => turret.Owner,
                _ => null
            };
            CombatActor actor = GetLocalCombatActor();
            if (player is null || _presentationScene is not Scene scene
                || !ReferenceEquals(player.Scene, scene) || player.SlotIndex != LocalSlot
                || !IsPredictionEpochActive(scene) || !actor.IsValid)
                return default;
            var shot = new CombatShot(actor, _sequence, 0, 0, 0, 0);
            _hitPrediction.ObserveShot(shot);
            return shot;
        }

        internal void ObserveDamageAttempt(PlayerEntity victim, DamageFlags flags,
            Vector3? direction, EntityBase? source)
        {
            if (_presentationScene is not Scene scene || !ReferenceEquals(victim.Scene, scene))
                return;
            if (!IsPredictionEpochActive(scene)) return;
            if (victim.SlotIndex == LocalSlot && source is BeamProjectileEntity selfBeam
                && selfBeam.Flags.TestFlag(BeamFlags.SelfDamage) && direction.HasValue
                && selfBeam.Beam is BeamType.Missile or BeamType.Battlehammer or BeamType.Magmaul
                && !flags.TestFlag(DamageFlags.Halfturret) && victim.Health > 0 && !victim.ModFrozen
                && _hitPrediction.OwnsCurrentShot(selfBeam.CombatShot)
                && _selfImpulse.TryPredict(selfBeam.CombatShot, victim.Speed, direction.Value,
                    victim.IsAltForm, out Vector3 predictedSpeed))
            {
                victim.Speed = predictedSpeed;
                return;
            }
            if (MphRead.Combat.CombatFeedbackSettings.Timing != MphRead.Combat.HitMarkerTiming.Instant
                || !_hitPrediction.Enabled || victim.SlotIndex == LocalSlot
                || victim.SlotIndex < 0 || victim.SlotIndex >= _identities.Length
                || victim.Health <= 0 || victim.Flags2.TestFlag(PlayerFlags2.Spectating)
                || scene.Match.Rules.Teams && !scene.Match.Rules.FriendlyFire
                    && LocalSlot >= 0 && scene.Players[LocalSlot].TeamIndex == victim.TeamIndex)
                return;
            CombatActor target = new((byte)victim.SlotIndex, _identities[victim.SlotIndex],
                _lives[victim.SlotIndex]);
            CombatShot shot;
            byte weapon;
            bool continuous;
            switch (source)
            {
                case BeamProjectileEntity beam:
                    shot = beam.CombatShot;
                    weapon = (byte)beam.Beam;
                    continuous = beam.Flags.TestFlag(BeamFlags.Continuous);
                    break;
                case BombEntity bomb:
                    shot = bomb.CombatShot;
                    weapon = 255;
                    continuous = false;
                    break;
                case PlayerEntity player when player.SlotIndex == LocalSlot:
                    shot = CapturePresentationAttribution(player);
                    weapon = 255;
                    continuous = false;
                    break;
                case HalfturretEntity turret when turret.Owner.SlotIndex == LocalSlot:
                    shot = CapturePresentationAttribution(turret);
                    weapon = 255;
                    continuous = false;
                    break;
                default:
                    return;
            }
            if (!_hitPrediction.OwnsCurrentShot(shot)) return;
            if (!_hitPrediction.ObserveDamageAttempt(shot, target, weapon, continuous))
                return;
            if (scene.Presentation is ScenePresentation presentation)
                presentation.CombatFeedback.PresentPredictedHit(shot.Actor, target, WorldServerTick);
        }

        internal bool PredictBombJump(PlayerEntity player, BombEntity bomb, float ySpeed)
        {
            if (_presentationScene is not Scene scene || !ReferenceEquals(player.Scene, scene)
                || player.SlotIndex != LocalSlot || !Single.IsFinite(ySpeed)
                || !IsPredictionEpochActive(scene) || !_hitPrediction.OwnsCurrentShot(bomb.CombatShot))
                return false;
            return _selfImpulse.NoteBombJump(bomb.CombatShot,
                Math.Max(0, ySpeed - player.Speed.Y));
        }

        internal void ObserveAuthoritativeProjectileVisual(in CombatEvent value, bool visualWillSpawn)
            => _projectilePresentation.ObserveAuthoritativeVisual(value, visualWillSpawn);

        private CombatActor GetLocalCombatActor()
        {
            int slot = LocalSlot;
            return slot >= 0 && slot < _identities.Length
                && _identities[slot] != 0 && _lives[slot] != 0
                ? new CombatActor((byte)slot, _identities[slot], _lives[slot])
                : CombatActor.None;
        }

        private bool IsPredictionEpochActive(Scene scene)
        {
            int slot = LocalSlot;
            return !IsObserver && Client.State is NetConnectionState.Ready or NetConnectionState.Playing
                && scene.Match.Phase == MatchPhase.Playing && slot >= 0 && slot < _identities.Length
                && _identities[slot] != 0 && _lives[slot] != 0
                && scene.Players[slot].Health > 0
                && !scene.Players[slot].Flags2.TestFlag(PlayerFlags2.Spectating);
        }

    }
}

namespace MphRead.Mods.Network
{
    /// <summary>Bounded, nonblocking terminal replica drain; clock is supplied for deterministic tests.</summary>
    internal sealed class CompletionResultDrain
    {
        internal const double TimeoutSeconds = 0.25;
        private double? _started;
        private bool _finished;
        public bool Advance(double now, Action pollAndApply, Func<bool> hasResult)
        {
            if (_finished) return true;
            _started ??= now;
            // Always service the queued Worker data before choosing the missing-result fallback.
            pollAndApply();
            return _finished = hasResult() || now - _started.Value >= TimeoutSeconds;
        }
    }
}
