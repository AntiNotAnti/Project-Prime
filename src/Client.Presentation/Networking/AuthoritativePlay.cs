using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Cosmetics;
using MphRead.Entities;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
using MphRead.Mods.Input;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Network
{
    /// <summary>Game-thread bridge for the authoritative client.</summary>
    public sealed partial class AuthoritativePlay : IDisposable
    {
        public enum TerminalState { Active, Completed, Failed, Transitioning, Disposed }
        public TerminalState State { get; private set; }
        public Guid? NodeMatchId { get; private set; }
        /// <summary>
        /// The Node transition that deliberately ended this Worker session.
        /// This is distinct from an interrupted/failed match so the launcher
        /// can retain its SDL host and continue directly into the replacement.
        /// </summary>
        public NodeMatchTransitionStarted? ExpectedTransition { get; private set; }
        public bool Interrupted { get; private set; }
        public MatchCompletionSummary? CompletionSummary { get; private set; }
        internal event Action<KillEvent>? LocalPlayerKilled;
        private MatchClientContext? _onlineContext;
        private int _disposeStarted;
        private RejoinRequest? _rejoin;
        private RejoinBaseline _rejoinBaseline;
        private long _rejoinStarted;
        private bool _rejoinReadySent;
        private bool _rejoinSnapshotReady;

        private readonly record struct RejoinBaseline(ulong ConnectionId, uint MatchId,
            byte Slot, bool Observer, long SnapshotCount);

        internal void AttachOnlineContext(MatchClientContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (_onlineContext is { } existing && !ReferenceEquals(existing, context))
                throw new InvalidOperationException("Gameplay already belongs to another online context.");
            _onlineContext = context;
        }

        internal void DetachOnlineContext(MatchClientContext context)
        {
            if (ReferenceEquals(_onlineContext, context)) _onlineContext = null;
        }

        internal void BindNodeMatch(Guid matchId)
        {
            if (matchId == Guid.Empty || NodeMatchId is { } existing && existing != matchId)
                throw new InvalidOperationException("Gameplay already belongs to a different Node match.");
            NodeMatchId = matchId;
        }
        public bool ObserveCompletion()
        {
            NodeMatchId ??= NodeSessions.Current?.State.JoinedMatchId;
            NodeControlClient? node = NodeSessions.Current;
            if (State == TerminalState.Active && NodeMatchId is Guid id
                && node?.ExpectedTransitionFor(id) is { } transition
                && !IsFailedTransition(node, transition)
                // Started is an expectation only. Either the Node has
                // observed the old match's terminal edge, or the gameplay
                // transport itself has actually terminated. A Started event
                // alone never ends the scene.
                && (node.ExpectedTransitionEndedFor(id) || Client.Failure != null))
            {
                ExpectedTransition = transition;
                // The old Worker was intentionally claimed by the Node. Do
                // not drain or present Results and do not surface this as a
                // disconnect to the user.
                _onlineContext?.CancelPendingRejoin();
                State = TerminalState.Transitioning;
            }
            else if (State == TerminalState.Active && NodeMatchId is Guid completedMatchId
                && node?.CompletionFor(completedMatchId) is { } ended)
            {
                CompletionSummary = node.CompletionSummaryFor(completedMatchId);
                Interrupted = ended.Interrupted;
                State = ended.Interrupted ? TerminalState.Failed : TerminalState.Completed;
            }
            return State != TerminalState.Active;
        }

        private static bool IsFailedTransition(NodeControlClient node,
            NodeMatchTransitionStarted transition)
            => node.TransitionVoteFor(transition.PreviousMatchId) is
                { TransitionId: var id, State: MatchTransitionVoteState.Failed }
                && id == transition.TransitionId;

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
                    ReplayRecorder.RecordFrame(Client, scene);
                    _world.Apply(scene, Client.HasSnapshot ? Client.Snapshot.ServerTick : null);
                }, () => scene.Match.Result != null);
        }

        private static AuthoritativePlay? _current;
        public static AuthoritativePlay? Current
        {
            get => ClientOnlineRuntime.Current?.Match?.Play ?? Volatile.Read(ref _current);
            private set => Volatile.Write(ref _current, value);
        }
        public static bool Active => Current != null || ReplayPlayback.IsModern;
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
        private readonly PresentedCollisionFrame _presentedCollision = new();
        private readonly ProjectilePresentationMeasurement _projectilePresentation = new();
        private readonly PredictedHitFeedback _hitPrediction = new();
        private readonly PredictedSelfImpulse _selfImpulse = new();
        private readonly PendingWeaponPrediction _weaponPrediction = new();
        private Scene? _presentationScene;
        private ulong _viewConnectionId;
        private uint _inputViewTick;
        private bool _hasInputViewTick;
        private SnapshotPresentation _pendingPresentation;
        private bool _presentationPending;
        private bool _localVelocityApplied;
        private uint _localVelocityAppliedTick;
        private uint _timingRevision;
        public NetClient Client { get; }
        public ClientPrediction Prediction { get; } = new();
        /// <summary>Per-match presentation diagnostics; never gameplay authority.</summary>
        public ProjectilePresentationMeasurement ProjectilePresentation => _projectilePresentation;
        public PresentedCollisionFrame PresentedCollision => _presentedCollision;
        public SnapshotInterpolationMetrics InterpolationMetrics => _interpolation.Metrics;
        public PredictedHitFeedback HitPrediction => _hitPrediction;
        public PredictedSelfImpulse SelfImpulse => _selfImpulse;
        public long CombatEvents { get; private set; }
        public long DamageEvents { get; private set; }
        /// <summary>
        /// Test-only observations used by nettest's scenario correlation. They
        /// are emitted from the existing local prediction and authoritative
        /// event boundaries; they never affect gameplay state.
        /// </summary>
        internal event Action<CombatShot>? LocalRootShotObserved;
        internal event Action<CombatEvent>? AuthoritativeCombatEventObserved;
        internal event Action<CombatShot, CombatActor, bool>? PredictedContactObserved;
        public bool HasWorldState => _world.HasState;
        public uint WorldServerTick => _world.ServerTick;
        private uint _inputPhaseRevision;
        public Vector3 VisualOffset => Prediction.VisualOffset;
        public bool IsObserver => Client.IsObserver;
        public int LocalSlot => IsObserver ? -1 : Client.Accepted.Slot;
        internal Action<PlayerEntity, uint>? ScriptInput { get; set; }

        public AuthoritativePlay(string host, int port, string name, Hunter hunter, ulong? joinNonce = null,
            string ticket = "", bool observer = false, uint wireMatchId = 0, Guid admissionId = default,
            byte[]? authKey = null, bool udpAuthenticationEnabled = false,
            bool ackCoalescingEnabled = false)
        {
            if (Current != null || NetSession.Active)
            {
                throw new InvalidOperationException("A network session is already active.");
            }
            ReplayRecorder.ResetTimelineForSession();
            IPAddress? address;
            if (!IPAddress.TryParse(host, out address))
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                // Preserve the established IPv4 preference for dual-address
                // hostnames while allowing IPv6-only DNS names.
                address = Array.Find(addresses,
                    candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                    ?? Array.Find(addresses,
                        candidate => candidate.AddressFamily == AddressFamily.InterNetworkV6);
            }
            if (address == null) { throw new ProgramException($"{host} has no IP address."); }
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var endpoint = new IPEndPoint(address, port);
            _transport = new NetTransport(0);
            try
            {
                Client = new NetClient(_transport, endpoint, name, Launcher.Hunters.Resolve(hunter), joinNonce,
                    ticket, observer, wireMatchId, admissionId, authKey, udpAuthenticationEnabled,
                    ackCoalescingEnabled);
            }
            catch { _transport.Dispose(); throw; }
            Client.WorldPacketValidator = WorldPacket.TryValidate;
            Client.WorldPacketReceived = payload =>
            {
                _world.Receive(payload);
                ReplayRecorder.RecordWorld(payload);
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
            ApplyCosmeticRoster(scene);
            scene.LocalPlayerSlot = IsObserver ? 0 : LocalSlot;
            scene.Players.ActiveCount = IsObserver ? 0 : 1;
            if (IsObserver) SpectatorMode.Start(scene);
        }

        public void BeforeSimulation(Scene scene)
        {
            _localVelocityApplied = false;
            if (ObserveCompletion())
            {
                _onlineContext?.CancelPendingRejoin();
                return;
            }
            if (TryStartQueuedRejoin(scene))
            {
                if (!AdvanceQueuedRejoin()) return;
            }
            // Input was sampled before this hook. New packets below must not
            // change which previously presented picture that input refers to.
            _hasInputViewTick = _interpolation.TryCaptureViewTick(out _inputViewTick);
            if (_rejoin == null) Client.Poll();
            UpdateNetworkTiming(Stopwatch.GetTimestamp());
            ReplayRecorder.RecordFrame(Client, scene);
            if (Client.Failure != null)
            {
                if (ObserveCompletion()) return;
                State = TerminalState.Failed;
                _projectilePresentation.Clear();
                _presentedCollision.Clear();
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
                _presentedCollision.Clear();
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
                _presentedCollision.Clear();
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
                RuntimeRoomRegistration registration = Metadata.RequireRuntimeRoom(Client.Accepted.Room);
                scene.TransitionRoomId = registration.RuntimeId;
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
            if (_rejoin is { } rejoin && _rejoinSnapshotReady)
            {
                if (!HasValidRejoinSnapshot())
                {
                    FailQueuedRejoin(rejoin, new InvalidOperationException(
                        "The Worker rejoin did not provide an eligible fresh player snapshot."));
                    return;
                }
                CompleteQueuedRejoin(rejoin);
            }
            // First usable state after join/rotation may arrive during Poll.
            // Until then no input command invents a historical view timestamp.
            if (!_hasInputViewTick)
                _hasInputViewTick = _interpolation.TryCaptureViewTick(out _inputViewTick);
            _world.Apply(scene, Client.HasSnapshot ? Client.Snapshot.ServerTick : null);
            foreach (NetRosterEntry entry in Client.Roster) { scene.Roster.Nicknames[entry.Slot] = entry.Name; }
            ApplyCosmeticRoster(scene);
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
                _hitPrediction, _selfImpulse, _presentedCollision);
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
            ApplyCosmeticRoster(scene);
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

        private void ApplyCosmeticRoster(Scene scene)
        {
            foreach (NetRosterEntry entry in Client.Roster)
            {
                if (entry.Slot >= scene.Players.Count) continue;
                scene.Players[entry.Slot].GetPresentation().SetCosmeticLoadout(
                    new CosmeticLoadoutIds(entry.SkinId, entry.ArmorEffectId,
                        entry.DeathEffectId));
            }
        }

        private void DrainEvents()
        {
            if (_presentationScene is not Scene scene) return;
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            while (Client.TryDequeueEvent(out NetApplicationEvent message))
            {
                ReplayRecorder.RecordEvent(message);
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
                    {
                        worldPresentation.BroadcastObservations.Record(worldEvent);
                        worldPresentation.WorldFeedback.Process(worldEvent, worldPresentation.CombatFeedback.Local, WorldServerTick,
                            _presentationScene.Match.Rules.PickupRespawnAnnouncements);
                    }
                    continue;
                }
                if (message.MatchId == _loadedMatch && message.Type == ReliableEventType.Kill
                    && KillEvent.TryRead(message.Payload.Span, out KillEvent kill))
                {
                    if (_presentationScene?.Presentation is ScenePresentation killPresentation)
                    {
                        bool accepted = killPresentation.CombatFeedback.Process(kill);
                        if (accepted && kill.Victim.Slot < scene.Players.Count
                            && _identities[kill.Victim.Slot] == kill.Victim.ConnectionId
                            && _lives[kill.Victim.Slot] == kill.Victim.Life)
                            scene.Players[kill.Victim.Slot].GetPresentation()
                                .PresentAuthoritativeKill(kill, WorldServerTick, suppressSound: false);
                        if (accepted && LocalSlot >= 0 && LocalSlot < scene.Players.Count)
                        {
                            PlayerEntity localPlayer = scene.Players[LocalSlot];
                            // Use the device consumed by the fixed-step input
                            // boundary. Render prediction may have changed
                            // ownership since this authoritative event arrived.
                            LookDeviceKind device = InputBalanceTelemetry.FixedStepLookDevice;
                            if (kill.Killer == GetLocalCombatActor() && kill.Weapon <= 10)
                                InputBalanceTelemetry.RecordKill(device,
                                    (int)localPlayer.Hunter, kill.Weapon);
                            if (kill.Victim == GetLocalCombatActor())
                            {
                                InputBalanceTelemetry.RecordDeath(device,
                                    (int)localPlayer.Hunter, (int)localPlayer.CurrentWeapon);
                                LocalPlayerKilled?.Invoke(kill);
                            }
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
                        awardPresentation.BroadcastObservations.Record(award);
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
                        semanticPresentation.BroadcastObservations.Record(semanticEvent);
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
                    bool matchedPredictedShot = false;
                    if (value.Kind == CombatEventKind.Shot)
                    {
                        matchedPredictedShot = _projectilePresentation.RecordAuthoritativeShot(value);
                        AuthoritativeCombatEventObserved?.Invoke(value);
                    }
                    else if (value.Kind == CombatEventKind.Damage)
                    {
                        // Observe the wire fact before presentation filtering;
                        // scenario correlation owns its own actor/life fence.
                        AuthoritativeCombatEventObserved?.Invoke(value);
                    }
                    bool currentTarget = !value.Target.IsValid
                        || value.Target.Slot < _identities.Length
                        && _identities[value.Target.Slot] == value.Target.ConnectionId
                        && _lives[value.Target.Slot] == value.Target.Life;
                    if (_presentationScene?.Presentation is ScenePresentation feedbackPresentation
                        && !feedbackPresentation.CombatFeedback.Process(value,
                            allowLocalHitMarker: currentTarget)) continue;
                    if (_presentationScene?.Presentation is ScenePresentation observedCombat)
                        observedCombat.BroadcastObservations.Record(value,
                            message.MatchId, scene.Match.PhaseRevision);
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
                    // A local slot alone does not prove that prediction created
                    // a visual. Preserve the authoritative shot as the fallback
                    // whenever the prediction ledger could not match one.
                    scene.Players[subject.Slot].GetPresentation().PresentCombat(value,
                        predictedLocalShot: HasPredictedPresentation(value.Kind,
                            subject.Slot == LocalSlot, matchedPredictedShot));
                }
            }
        }

        internal static bool HasPredictedPresentation(CombatEventKind kind,
            bool localSubject, bool matchedPredictedShot)
        {
            if (!localSubject)
                return false;
            return kind == CombatEventKind.Bomb
                || kind == CombatEventKind.Shot && matchedPredictedShot;
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
            uint inputEpoch = _lives[LocalSlot];
            if (inputEpoch == 0) { return; }
            local.ModRepairVectors();
            InputCommand command = local.CaptureNetworkInput(
                _sequence, _inputViewTick, inputEpoch);
            _inputs[_sequence % InputBundle.Capacity] = command;
            _weaponPrediction.ObserveInput(_identities[LocalSlot], inputEpoch,
                command.DesiredWeapon, command.Sequence);
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
                        player.GetPresentation().ResetAuthoritativeDeathPresentation();
                        player.ClientActivate(state);
                        _identities[slot] = state.ConnectionId;
                        _lives[slot] = 0;
                    }
                    bool newLife = _lives[slot] != state.Life;
                    bool local = slot == LocalSlot;
                    bool applyWeapon = true;
                    if (local)
                    {
                        _weaponPrediction.ObserveAuthoritative(state.ConnectionId,
                            state.Life, state.Weapon);
                        applyWeapon = _weaponPrediction.ShouldApplyAuthoritative(
                            state.ConnectionId, state.Life, Client.Snapshot.HasProcessedInput,
                            Client.Snapshot.LastProcessedInput);
                    }
                    bool snapshotDeath = state.Health == 0
                        && (state.Flags & (SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch)) == 0;
                    bool engineDeath = snapshotDeath && player.Health > 0
                        && (state.Flags & SnapshotPlayerFlags.Spawned) == 0
                        && !scene.Services.SuppressDamage(player);
                    player.ApplyServerState(state, newLife, local, applyWeapon);
                    player.GetPresentation().ObserveAuthoritativeDeath(
                        new CombatActor(state.Slot, state.ConnectionId, state.Life), snapshotDeath,
                        (state.Flags & SnapshotPlayerFlags.AltForm) != 0, Client.Snapshot.ServerTick,
                        engineDeath, suppressSound: false);
                    if (local)
                        SpectatorMode.ApplyWaitingForMatch(scene, (state.Flags & SnapshotPlayerFlags.WaitingForMatch) != 0);
                    player.GetPresentation().ReconcileNetworkAfflictions(state, Client.Snapshot.ServerTick);
                    if (!local)
                    {
                        player.ApplySnapshotTransform(state);
                    }
                    else if (newIdentity || newLife || (state.Flags & SnapshotPlayerFlags.Spawned) == 0)
                    {
                        if (local && (newLife || (state.Flags & SnapshotPlayerFlags.Spawned) == 0))
                        {
                            // A death or respawn starts a new input epoch. Do
                            // not retransmit commands from the prior life in
                            // the next redundant bundle.
                            _inputCount = 0;
                        }
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
                            player.ApplyServerState(state, newLife: false,
                                reconcileWeapon: applyWeapon);
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
                        scene.Players[slot].GetPresentation().ResetAuthoritativeDeathPresentation();
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
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
            State = TerminalState.Disposed;
            MatchClientContext? context = _onlineContext;
            context?.CancelPendingRejoin();
            _onlineContext = null;
            // Detach every callback before closing the transport. A queued
            // receive or presentation completion must not retain this match
            // after its context has been released.
            Client.WorldPacketValidator = null;
            Client.WorldPacketReceived = null;
            LocalPlayerKilled = null;
            LocalRootShotObserved = null;
            AuthoritativeCombatEventObserved = null;
            PredictedContactObserved = null;
            if (_presentationScene?.Presentation is ScenePresentation sessionPresentation)
                sessionPresentation.WorldFeedback.ClearPendingNotices();
            _projectilePresentation.Clear();
            _presentedCollision.Clear();
            _hitPrediction.Clear();
            _selfImpulse.Clear();
            _weaponPrediction.Reset();
            InputBalanceTelemetry.ResetAttribution();
            Client.Close();
            Client.Dispose();
            _transport.Dispose();
            ClientOnlineRuntime.Current?.ReleaseMatch(this, dispose: true);
            if (Current == this) { Current = null; }
            NetSession.ResetLiveState();
            ReplayRecorder.ResetTimelineForSession();
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
            _presentedCollision.Clear();
            _timingRevision = 0;
            _presentationPending = false;
            _hasInputViewTick = false;
            _weaponPrediction.Reset();
            if (_presentationScene is Scene scene)
            {
                foreach (PlayerEntity player in scene.Players)
                    player.ResetRemoteLocomotion();
            }
        }

        private bool TryStartQueuedRejoin(Scene scene)
        {
            if (_rejoin != null) return true;
            if (_onlineContext is not { } context || !context.Owns(this)
                || !context.TryTakeRejoin(out RejoinRequest request)) return false;
            if (!context.IsCurrentRejoin(request))
            {
                context.AbandonRejoin(request);
                return false;
            }
            NodeMatchHandoff handoff = request.Handoff;
            if (NodeMatchId != handoff.MatchId || handoff.WireMatchId == 0
                || handoff.Nonce == 0 || handoff.Ticket is not { Length: > 0 and <= JoinPacket.MaxRoutedTicketBytes }
                || handoff.UdpAuthenticationEnabled != Client.UdpAuthenticationEnabled
                || handoff.UdpAuthenticationEnabled && (handoff.AdmissionId == Guid.Empty
                    || handoff.AdmissionKey.Length != AdmissionKeyRules.Base64Length)
                || !handoff.UdpAuthenticationEnabled && (handoff.AdmissionId != Guid.Empty
                    || handoff.AdmissionKey.Length != 0)
                || Client.Accepted.MatchId != handoff.WireMatchId
                || handoff.Observer != Client.IsObserver
                || !Client.IsObserver && Client.Accepted.Slot >= PlayerEntity.SlotCapacity)
            {
                context.FailRejoin(request, new InvalidOperationException(
                    "The rejoin handoff does not match the current gameplay seat."));
                return false;
            }
            _rejoin = request;
            _rejoinBaseline = new(Client.Connection?.Id ?? 0, Client.Accepted.MatchId,
                Client.IsObserver ? byte.MaxValue : Client.Accepted.Slot,
                Client.IsObserver, Client.SnapshotsReceived);
            _rejoinStarted = Stopwatch.GetTimestamp();
            _rejoinReadySent = false;
            _rejoinSnapshotReady = false;
            byte[]? admissionKey = null;
            try
            {
                if (handoff.UdpAuthenticationEnabled) admissionKey = AdmissionKeyRules.Decode(handoff.AdmissionKey);
                Client.Reconnect(handoff.Nonce, handoff.Ticket, handoff.AdmissionId, admissionKey);
            }
            catch (Exception error)
            {
                FailQueuedRejoin(request, error);
                return false;
            }
            finally
            {
                if (admissionKey is not null)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(admissionKey);
            }
            _inputCount = 0;
            Array.Clear(_identities);
            Array.Clear(_lives);
            _appliedSnapshot = 0;
            _inputPhaseRevision = 0;
            _viewConnectionId = 0;
            _hasInputViewTick = false;
            Prediction.Reset();
            ResetPresentation();
            _world.Reset(handoff.WireMatchId);
            _projectilePresentation.Clear();
            _presentedCollision.Clear();
            _hitPrediction.Clear();
            _selfImpulse.Clear();
            if (scene.Presentation is ScenePresentation presentation)
                ResetRoleFeedback(presentation.CombatFeedback, presentation.WorldFeedback,
                    presentation.Announcer, presentation.AwardHud);
            else Chat.ChatBox.Clear();
            return true;
        }

        /// <summary>
        /// Advance exactly one bounded owner-side transport poll. The caller
        /// never waits or sleeps; the next simulation boundary continues the
        /// same request until a replacement connection supplies a snapshot.
        /// </summary>
        private bool AdvanceQueuedRejoin()
        {
            RejoinRequest? request = _rejoin;
            if (request == null) return false;
            MatchClientContext? context = _onlineContext;
            if (context is null || !context.Owns(this) || !context.IsCurrentRejoin(request))
            {
                context?.AbandonRejoin(request);
                _rejoin = null;
                _rejoinSnapshotReady = false;
                return false;
            }
            if (Stopwatch.GetElapsedTime(_rejoinStarted).TotalMilliseconds >= 8000)
            {
                FailQueuedRejoin(request, new TimeoutException(
                    "The Worker did not accept the rejoin before the deadline."));
                return false;
            }
            Client.Poll();
            if (!context.IsCurrentRejoin(request))
            {
                context.AbandonRejoin(request);
                _rejoin = null;
                _rejoinSnapshotReady = false;
                return false;
            }
            if (Client.Failure != null)
            {
                FailQueuedRejoin(request, new InvalidOperationException(Client.Failure));
                return false;
            }
            NetConnection? connection = Client.Connection;
            if (connection == null) return false;
            if (connection.Id == _rejoinBaseline.ConnectionId
                || Client.Accepted.MatchId != _rejoinBaseline.MatchId
                || Client.IsObserver != _rejoinBaseline.Observer
                || Client.Accepted.Slot != _rejoinBaseline.Slot
                || request.Handoff.Observer != Client.IsObserver)
            {
                FailQueuedRejoin(request, new InvalidOperationException(
                    "The Worker changed the authoritative match seat during rejoin."));
                return false;
            }
            if (Client.State == NetConnectionState.Loading && !_rejoinReadySent)
            {
                if (!Client.Ready(Client.Accepted.MatchId))
                {
                    FailQueuedRejoin(request, new InvalidOperationException(
                        "The replacement gameplay connection could not become ready."));
                    return false;
                }
                _rejoinReadySent = true;
            }
            if (!Client.HasSnapshot || Client.SnapshotsReceived <= _rejoinBaseline.SnapshotCount)
                return false;
            _rejoinSnapshotReady = true;
            return true;
        }

        private bool HasValidRejoinSnapshot()
        {
            if (_rejoin is not { } request || Client.Connection is not { } connection
                || connection.Id == _rejoinBaseline.ConnectionId
                || Client.Snapshot.MatchId != _rejoinBaseline.MatchId
                || Client.State is not (NetConnectionState.Ready or NetConnectionState.Playing)) return false;
            if (Client.IsObserver) return true;
            int slot = Client.Accepted.Slot;
            return (uint)slot < (uint)_identities.Length
                && _identities[slot] == connection.Id && _lives[slot] != 0
                && !request.Handoff.Observer;
        }

        private void CompleteQueuedRejoin(RejoinRequest request)
        {
            if (_onlineContext is not { } context || !context.IsCurrentRejoin(request)) return;
            int slot = Client.IsObserver ? -1 : Client.Accepted.Slot;
            uint inputEpoch = slot >= 0 && slot < _lives.Length ? _lives[slot] : 0;
            context.CompleteRejoin(request, new RejoinCompletion(Client.Connection!.Id, inputEpoch));
            _rejoin = null;
            _rejoinSnapshotReady = false;
        }

        private void FailQueuedRejoin(RejoinRequest request, Exception error)
        {
            if (_onlineContext is { } context) context.FailRejoin(request, error);
            _rejoin = null;
            _rejoinSnapshotReady = false;
            Client.Connection?.Disconnect();
        }

        private void UpdateNetworkTiming(long now)
        {
            NetworkTimingProfile profile = Client.TimingProfile;
            if (profile.IsValid && profile.Revision != _timingRevision)
            {
                _interpolation.SetTargetDelay(profile.PresentationDelayTicks, now);
                _timingRevision = profile.Revision;
            }
            if (_timingRevision != 0 && _interpolation.AdvanceDelay(now))
                Client.AcknowledgeTimingProfile(_timingRevision);
            Client.ReportTiming(_interpolation, now);
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
            _presentedCollision.Begin(presentation);
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot != LocalSlot && _interpolation.TrySamplePresentation(slot, presentation,
                    out SnapshotPlayerPresentation sample))
                {
                    scene.Players[slot].GetPresentation().BeginInterpolatedPose(sample.State);
                    _presentedCollision.Stage(sample.State);
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
            if (_interpolation.MarkPresented(_pendingPresentation))
                _presentedCollision.Commit(_pendingPresentation);
            else
                _presentedCollision.AbortPending();
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
            if (_projectilePresentation.ObservePredictedShot(shooter,
                    out CombatShot shot) > 0)
            {
                MeasureLocalShot(scene, shot);
                LocalRootShotObserved?.Invoke(shot with
                    { SourceWeapon = (byte)shooter.CurrentWeapon });
                return shot.CommandSequence;
            }
            return null;
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
            var shot = CreatePresentationShot(actor);
            _hitPrediction.ObserveShot(shot);
            MeasureLocalShot(scene, shot);
            return shot;
        }

        private CombatShot CreatePresentationShot(in CombatActor actor)
        {
            uint viewTick = _hasInputViewTick ? _inputViewTick
                : _interpolation.TryCaptureViewTick(out uint capturedTick) ? capturedTick : 0;
            return new CombatShot(actor, _sequence, WorldServerTick, viewTick, 0, 0);
        }

        private void MeasureLocalShot(Scene scene, in CombatShot shot)
        {
            if (!_presentedCollision.BeginLocalShot(shot)) return;
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot == LocalSlot) continue;
                PlayerEntity player = scene.Players[slot];
                if (slot >= _identities.Length || _identities[slot] == 0 || _lives[slot] == 0)
                    continue;
                CombatActor candidate = new((byte)slot, _identities[slot], _lives[slot]);
                bool active = player.LoadFlags.TestFlag(LoadFlags.Active);
                bool spawned = player.LoadFlags.TestFlag(LoadFlags.Spawned);
                if (!candidate.IsValid || !active || !spawned) continue;
                bool spectating = player.Flags2.TestFlag(PlayerFlags2.Spectating);
                bool alive = player.Health > 0;
                _presentedCollision.RecordShotCandidate(shot, candidate, player.Position,
                    player.Hunter, (byte)player.TeamIndex, active, spawned,
                    alive: alive, altForm: player.IsAltForm, spectating: spectating,
                    simulationTick: WorldServerTick);
            }
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
            if (!_hitPrediction.ObserveDamageAttempt(shot, target, weapon, flags, continuous))
                return;
            bool targetSpectating = victim.Flags2.TestFlag(PlayerFlags2.Spectating);
            bool targetAlive = victim.Health > 0;
            _presentedCollision.RecordSpeculativeHit(shot, target, victim.Position,
                victim.Hunter, (byte)victim.TeamIndex, victim.LoadFlags.TestFlag(LoadFlags.Active),
                spawned: victim.LoadFlags.TestFlag(LoadFlags.Spawned), alive: targetAlive, altForm: victim.IsAltForm,
                spectating: targetSpectating, simulationTick: WorldServerTick);
            PredictedContactObserved?.Invoke(shot with { SourceWeapon = weapon }, target,
                flags.TestFlag(DamageFlags.Headshot));
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

        internal bool KillcamLiveContextChanged(in KillEvent kill, bool includeNewLife = true)
        {
            if (State != TerminalState.Active || Client.Failure != null
                || Client.Accepted.MatchId != kill.MatchId || _loadedMatch != kill.MatchId
                || LocalSlot != kill.Victim.Slot)
                return true;
            CombatActor local = GetLocalCombatActor();
            return KillcamIdentityChanged(local, kill.Victim, includeNewLife);
        }

        /// <summary>
        /// Keep connection identity fenced even when a post-round killcam is
        /// allowed to survive a new life on the same connection. A slot is
        /// reusable across reconnects; includeNewLife only relaxes the life
        /// component of that exact identity.
        /// </summary>
        internal static bool KillcamIdentityChanged(CombatActor current,
            CombatActor killed, bool includeNewLife)
            => !current.IsValid || !killed.IsValid
                || current.ConnectionId != killed.ConnectionId
                || includeNewLife && current != killed;

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
