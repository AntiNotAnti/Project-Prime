using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Game-thread bridge for the authoritative client.</summary>
    public sealed partial class AuthoritativePlay : IDisposable
    {
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
        private Scene? _presentationScene;
        private ulong _viewConnectionId;
        private uint _inputViewTick;
        private bool _hasInputViewTick;
        private SnapshotPresentation _pendingPresentation;
        private bool _presentationPending;
        public NetClient Client { get; }
        public ClientPrediction Prediction { get; } = new();
        public long CombatEvents { get; private set; }
        public long DamageEvents { get; private set; }
        public bool HasWorldState => _world.HasState;
        public uint WorldServerTick => _world.ServerTick;
        private uint _inputPhaseRevision;
        public Vector3 VisualOffset => Prediction.VisualOffset;
        public bool IsObserver => Client.IsObserver;
        public int LocalSlot => IsObserver ? -1 : Client.Accepted.Slot;
        internal Action<PlayerEntity, uint>? ScriptInput { get; set; }

        public AuthoritativePlay(string host, int port, string name, Hunter hunter, ulong? joinNonce = null,
            string ticket = "", bool observer = false, Guid ownerCapability = default)
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
            try { Client = new NetClient(_transport, endpoint, name, Launcher.Hunters.Resolve(hunter),
                joinNonce, ticket, observer, ownerCapability); }
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
            AttachScene(scene);
            _loadedMatch = Client.Accepted.MatchId;
            _world.Reset(_loadedMatch);
            scene.Match.MatchId = Client.Accepted.MatchId;
            scene.Match.ApplyRules(Client.Accepted.Rules);
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < PlayerEntity.MaxPlayers; slot++)
            {
                scene.AddPlayer(slot == LocalSlot ? hunter : Hunter.Samus,
                    slot == LocalSlot ? recolor : 0,
                    GetAssignedTeam(slot));
                PlayerEntity player = PlayerEntity.Players[slot];
                player.IsBot = false;
                // The local slot must initialize its camera/HUD while the
                // room loads. Spawn selection remains disabled on clients.
                if (slot != LocalSlot) { player.LoadFlags &= ~LoadFlags.Active; }
            }
            PlayerEntity.MainPlayerIndex = IsObserver ? 0 : LocalSlot;
            PlayerEntity.PlayerCount = IsObserver ? 0 : 1;
            if (IsObserver) SpectatorMode.Start();
        }

        public void AttachScene(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (_presentationScene != null && !ReferenceEquals(_presentationScene, scene))
                throw new InvalidOperationException("A different scene is already attached to this client session.");
            _presentationScene = scene;
        }

        public void DetachScene(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!ReferenceEquals(_presentationScene, scene)) return;
            _presentationScene = null;
            _hasInputViewTick = false;
            _inputCount = 0;
            Prediction.Reset();
            ResetPresentation();
        }

        /// <summary>Poll transport/session state without reading or mutating a Scene.</summary>
        public void PollSession()
        {
            Client.Poll();
            if (Client.Failure != null) { throw new ProgramException(Client.Failure); }
            ulong connectionId = Client.Connection?.Id ?? 0;
            if (connectionId != _viewConnectionId)
            {
                _viewConnectionId = connectionId;
                ResetPresentation();
                _inputCount = 0;
                Prediction.Reset();
                _appliedSnapshot = 0;
            }
        }

        public void BeforeSimulation(Scene scene)
        {
            if (!ReferenceEquals(_presentationScene, scene))
                throw new InvalidOperationException("Attach the match scene before polling it.");
            // Input was sampled before this hook. New packets below must not
            // change which previously presented picture that input refers to.
            _hasInputViewTick = _interpolation.TryCaptureViewTick(out _inputViewTick);
            if (ClientSessionCoordinator.Shared.Owns(this))
                ClientSessionCoordinator.Shared.PollMatch(this, scene);
            else
                PollSession();
            DemoRecorder.RecordFrame(Client, scene);
            if (Client.State == NetConnectionState.Lobby)
            {
                foreach (PlayerEntity player in PlayerEntity.Players) player.Controls.ClearAll();
                NetDiagnostics.ReportAuthoritative(Client, Prediction, _interpolation, _transport.Metrics);
                return;
            }
            if (Client.Connection != null && Client.Accepted.MatchId != 0
                && (Client.Accepted.MatchId != _loadedMatch || Client.RoleRevision != _appliedRoleRevision))
            {
                if (Client.RoleRevision != _appliedRoleRevision)
                {
                    if (scene.Presentation is ScenePresentation rolePresentation)
                        ResetRoleFeedback(rolePresentation.CombatFeedback, rolePresentation.WorldFeedback);
                    else Chat.ChatBox.Clear();
                    foreach (PlayerEntity player in PlayerEntity.Players) player.Controls.ClearAll();
                }
                _appliedRoleRevision = Client.RoleRevision;
                _loadedMatch = Client.Accepted.MatchId;
                scene.Match.MatchId = Client.Accepted.MatchId;
                Array.Clear(_identities);
                Array.Clear(_lives);
                for (int slot = 0; slot < 8; slot++) { NetScoreboard.ForgetSlot(scene, slot); }
                _inputCount = 0;
                _appliedSnapshot = 0;
                Prediction.Reset();
                ResetPresentation();
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
            foreach (NetRosterEntry entry in Client.Roster) { GameState.Nicknames[entry.Slot] = entry.Name; }
            if (scene.Presentation is ScenePresentation feedbackPresentation)
                feedbackPresentation.CombatFeedback.Bind(_loadedMatch,
                    LocalSlot is >= 0 and < 8 ? new CombatActor((byte)LocalSlot, _identities[LocalSlot], _lives[LocalSlot]) : CombatActor.None,
                    Client.Roster, WorldServerTick, scene.Match.PhaseRevision);
            if (scene.Presentation is ScenePresentation worldPresentation)
                worldPresentation.WorldFeedback.Bind(_loadedMatch, scene.Match.PhaseRevision);
            DrainEvents();
            if (_inputPhaseRevision != scene.Match.PhaseRevision)
            {
                _inputPhaseRevision = scene.Match.PhaseRevision;
                _inputCount = 0;
                Prediction.Reset();
                _interpolation.Reset();
                _hasInputViewTick = false;
            }
            if (LocalSlot >= 0 && scene.Match.Phase == MatchPhase.Playing)
            { ScriptInput?.Invoke(PlayerEntity.Players[LocalSlot], _sequence); }
            else if (LocalSlot >= 0) { PlayerEntity.Players[LocalSlot].Controls.ClearAll(); }
            NetDiagnostics.ReportAuthoritative(Client, Prediction, _interpolation, _transport.Metrics);
        }

        public PlayerEntity RebuildPlayers(Scene scene, Hunter hunter, int recolor)
        {
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < 8; slot++)
            {
                PlayerEntity player = PlayerEntity.Create(slot == LocalSlot ? hunter : Hunter.Samus,
                    slot == LocalSlot ? recolor : 0) ?? throw new ProgramException("Could not rebuild network player.");
                player.LoadFlags = LoadFlags.SlotActive | LoadFlags.Initial;
                if (slot == LocalSlot) { player.LoadFlags |= LoadFlags.Active; }
                player.NodeRef = player.CameraInfo.NodeRef = NodeRef.None;
                player.IsBot = false;
                player.TeamIndex = GetAssignedTeam(slot);
            }
            PlayerEntity.MainPlayerIndex = IsObserver ? 0 : LocalSlot;
            PlayerEntity.PlayerCount = IsObserver ? 0 : 1;
            if (IsObserver) SpectatorMode.Start();
            return PlayerEntity.Main;
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
                        killPresentation.CombatFeedback.Process(kill);
                    continue;
                }
                if (message.MatchId != _loadedMatch || message.Type != ReliableEventType.Combat
                    || !CombatEventBatch.TryRead(message.Payload.Span, events, out int count)) { continue; }
                foreach (CombatEvent value in events[..count])
                {
                    if (_presentationScene?.Presentation is ScenePresentation feedbackPresentation
                        && !feedbackPresentation.CombatFeedback.Process(value)) continue;
                    CombatEvents++;
                    if (value.Kind == CombatEventKind.Damage) { DamageEvents++; }
                    CombatActor subject = value.Kind is CombatEventKind.Shot or CombatEventKind.Bomb
                        ? value.Actor : value.Target;
                    if (!subject.IsValid || _identities[subject.Slot] != subject.ConnectionId
                        || _lives[subject.Slot] != subject.Life) { continue; }
                    PlayerEntity.Players[subject.Slot].GetPresentation().PresentCombat(value,
                        predictedLocalShot: subject.Slot == LocalSlot);
                }
            }
        }

        public void AfterSimulation()
        {
            if (IsObserver)
            {
                foreach (SnapshotPlayer state in Client.SnapshotPlayers)
                    PlayerEntity.Players[state.Slot].ApplySnapshotTransform(state);
                return;
            }
            if (_presentationScene?.Match.Phase != MatchPhase.Playing || !_hasInputViewTick
                || Client.State is not (NetConnectionState.Ready or NetConnectionState.Playing)) { return; }
            PlayerEntity local = PlayerEntity.Players[LocalSlot];
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
                if (state.Slot != LocalSlot) { PlayerEntity.Players[state.Slot].ApplySnapshotTransform(state); }
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
                    PlayerEntity player = PlayerEntity.Players[slot];
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
                        SpectatorMode.ApplyWaitingForMatch((state.Flags & SnapshotPlayerFlags.WaitingForMatch) != 0);
                    player.GetPresentation().ReconcileNetworkAfflictions(state, Client.Snapshot.ServerTick);
                    if (!local)
                    {
                        player.ApplySnapshotTransform(state);
                    }
                    else if (newIdentity || newLife || (state.Flags & SnapshotPlayerFlags.Spawned) == 0)
                    {
                        Prediction.Reset();
                        player.ApplySnapshotTransform(state, local: true);
                    }
                    else if (Client.Snapshot.HasProcessedInput)
                    {
                        Vector3 corrected = Prediction.Reconcile(Client.Snapshot.LastProcessedInput,
                            state.Position, (state.Flags & SnapshotPlayerFlags.AltForm) != 0, player.Position);
                        player.CorrectPredictedPosition(corrected);
                        if (Prediction.LastCorrectionHard)
                        {
                            player.ApplyServerState(state, newLife: false);
                            player.Speed = state.Speed;
                        }
                    }
                    _lives[slot] = state.Life;
                }
                for (int slot = 0; slot < 8; slot++)
                {
                    if ((occupied & (1 << slot)) == 0 && _identities[slot] != 0)
                    {
                        PlayerEntity.Players[slot].GetPresentation().ClearNetworkAfflictions();
                        PlayerEntity.Players[slot].ServerDeactivate();
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
                PlayerEntity.PlayerCount = Client.SnapshotPlayers.Length;
            }
            finally { ApplyingSnapshot = false; }
        }

        public void Dispose()
        {
            Client.Close();
            Client.Dispose();
            _transport.Dispose();
            if (Current == this) { Current = null; }
            ClientSessionCoordinator.Shared.Forget(this);
        }

        public void AdvancePresentation()
        {
            long now = Stopwatch.GetTimestamp();
            Prediction.AdvanceVisual(Stopwatch.GetElapsedTime(_lastPresentation, now).TotalSeconds);
            _lastPresentation = now;
        }

        // A role transfer can rewind within the same match/phase. It is a new
        // presentation epoch: live cues must not survive into a delayed view.
        internal static void ResetRoleFeedback(MphRead.Combat.CombatFeedback combat, MphRead.Combat.WorldFeedback world)
        {
            combat.Bind(0, CombatActor.None, ReadOnlySpan<NetRosterEntry>.Empty);
            world.Bind(0, 0);
            Chat.ChatBox.Clear();
        }

        private void ResetPresentation()
        {
            _interpolation.Reset();
            _presentationPending = false;
            _hasInputViewTick = false;
        }

        public void BeginRemotePresentation(Scene scene)
        {
            if (!ReferenceEquals(scene, _presentationScene)) return;
            _presentationPending = false;
            // The pause map replaces the world in GetDrawItems. It cannot
            // advance a claim about remote poses the player did not see.
            if (!Client.HasSnapshot) return;
            long now = Stopwatch.GetTimestamp();
            double estimated = Client.Clock.Synchronized ? Client.Clock.EstimateServerTick(now)
                : Client.Snapshot.ServerTick + Stopwatch.GetElapsedTime(Client.SnapshotReceivedAt, now).TotalSeconds * 60;
            if (!_interpolation.TryPreparePresentation(estimated, out SnapshotPresentation presentation)) return;
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot != LocalSlot && _interpolation.TrySample(slot, presentation, out SnapshotPlayer state))
                {
                    PlayerEntity.Players[slot].GetPresentation().BeginInterpolatedPose(state);
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
            foreach (PlayerEntity player in PlayerEntity.Players) { player.GetPresentation().EndInterpolatedPose(); }
        }

    }
}
