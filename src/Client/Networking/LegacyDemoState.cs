using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MphRead.Entities;

[assembly: InternalsVisibleTo("MphRead.Tests")]

namespace MphRead.Mods.Network
{
    // Format 2 retains frame deltas and compression. Its protocol byte selects
    // legacy datagrams (4) or authoritative presentation records (5 through 9).

    /// <summary>Socket-free playback of server facts. Inputs and connection control are never replayed.</summary>
    internal sealed class ModernDemoState
    {
        private readonly SnapshotPlayer[] _players = new SnapshotPlayer[8];
        private readonly NetRosterEntry[] _roster = new NetRosterEntry[8];
        private readonly ulong[] _identities = new ulong[8];
        private readonly uint[] _lives = new uint[8];
        private readonly WorldEvent[] _worldEvents = new WorldEvent[256];
        private int _worldEventCount;
        private readonly KillEvent[] _kills = new KillEvent[256];
        private int _killCount, _rosterCount;
        private bool _hasRoster;
        private readonly SessionChatPacket[] _chats = new SessionChatPacket[256];
        private int _chatCount;
        private readonly CombatEvent[] _events = new CombatEvent[256 * CombatEventBatch.MaxCount];
        private readonly ClientWorldState _world = new();
        private uint _loadedMatch;
        private byte _protocol = NetHeader.Version;
        internal MatchRules? InitialRules => _protocol >= 7 && Match.MatchId != 0 ? Match.Rules : null;
        private int _eventCount;
        private bool _dirty;
        private byte[]? _feedbackBytes;
        private int _feedbackOffset, _feedbackPart, _feedbackParts;
        private byte[]? _pendingFeedback;
        private byte[]? _pendingClock;
        private byte[]? _pendingChat;
        private DemoRecordKind _fragmentKind;
        private bool _reloadOnNextApply;
        private int? _recordedLocalSlot;
        internal void RequestSceneReload() => _reloadOnNextApply = true;
        internal bool HasCompleteCheckpoint => Match.MatchId != 0 && HasSnapshot && _world.HasState
            && _pendingClock != null && _pendingFeedback != null && _feedbackBytes == null
            && _hasRoster && _recordedLocalSlot.HasValue;
        public MatchTransitionPacket Match { get; private set; }
        public SnapshotPacket Snapshot { get; private set; }
        public int PlayerCount { get; private set; }
        public bool HasSnapshot { get; private set; }
        public ReadOnlySpan<SnapshotPlayer> Players => _players.AsSpan(0, PlayerCount);
        public ReadOnlySpan<NetRosterEntry> Roster => _roster.AsSpan(0, _rosterCount);
        public ClientWorldState World => _world;
        public bool ApplyingSnapshot { get; private set; }
        public long SnapshotsReceived { get; private set; }
        public long CombatEventsReceived { get; private set; }
        public long DamageEventsReceived { get; private set; }
        public int MatchesLoaded { get; private set; }
        public long WorldApplications { get; private set; }
        private uint _appliedWorldRevision;
        private bool _appliedWorld;
        public void DiscardEvents() { _eventCount = _killCount = _worldEventCount = _chatCount = 0; }

        public void Reset(byte protocol = NetHeader.Version)
        {
            _protocol = protocol;
            _recordedLocalSlot = null; _hasRoster = false;
            _feedbackBytes = _pendingFeedback = _pendingClock = _pendingChat = null; _reloadOnNextApply = false; _feedbackOffset = _feedbackPart = 0;
            _world.LegacyProtocol = protocol is 5 or 6;
            _world.Protocol7Demo = protocol == 7;
            Match = default; Snapshot = default; HasSnapshot = _dirty = false;
            PlayerCount = _eventCount = _killCount = _worldEventCount = _rosterCount = _chatCount = 0; _loadedMatch = 0;
            Array.Clear(_identities); Array.Clear(_lives);
            _world.Reset(0);
            SnapshotsReceived = CombatEventsReceived = DamageEventsReceived = WorldApplications = 0;
            MatchesLoaded = 0; _appliedWorld = false;
        }

        public bool Receive(ReadOnlySpan<byte> record)
        {
            if (record.Length < 1 || record.Length > NetConfig.MaxPacketSize) { return false; }
            ReadOnlySpan<byte> body = record[1..];
            switch ((DemoRecordKind)record[0])
            {
                case DemoRecordKind.Perspective:
                    if (_protocol < 8 || body.Length != 1 || (body[0] > 7 && body[0] != 255)) return false;
                    _recordedLocalSlot = body[0] == 255 ? -1 : body[0]; return true;
                case DemoRecordKind.Clock:
                    if (_protocol < 8 || body.Length != 32
                        || !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(body[16..]))
                        || !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(body[20..]))
                        || BinaryPrimitives.ReadSingleLittleEndian(body[16..]) < 0
                        || BinaryPrimitives.ReadSingleLittleEndian(body[20..]) < 0) return false;
                    _pendingClock = body.ToArray(); return true;
                case DemoRecordKind.ChatState:
                case DemoRecordKind.Presentation:
                    if (_protocol < 8 || body.Length < 8) return false;
                    int part = BinaryPrimitives.ReadUInt16LittleEndian(body);
                    int parts = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                    int total = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                    if (total is < 1 or > MphRead.Combat.ReplayFeedbackState.MaximumBytes || parts is < 1 or > 256 || part >= parts) return false;
                    if (part == 0) { _feedbackBytes = new byte[total]; _feedbackOffset = _feedbackPart = 0; _feedbackParts = parts; _fragmentKind = (DemoRecordKind)record[0]; }
                    if (_fragmentKind != (DemoRecordKind)record[0] || _feedbackBytes == null || _feedbackBytes.Length != total || parts != _feedbackParts || part != _feedbackPart
                        || body.Length - 8 > total - _feedbackOffset) return false;
                    body[8..].CopyTo(_feedbackBytes.AsSpan(_feedbackOffset)); _feedbackOffset += body.Length - 8; _feedbackPart++;
                    if (_feedbackPart == parts)
                    {
                        if (_feedbackOffset != total) return false;
                        if (_fragmentKind == DemoRecordKind.Presentation) _pendingFeedback = _feedbackBytes;
                        else _pendingChat = _feedbackBytes;
                        _feedbackBytes = null;
                    }
                    return true;
                case DemoRecordKind.Match:
                    if (!TryReadMatch(body, out MatchTransitionPacket match) || match.MatchId == 0)
                    { return false; }
                    if (match.MatchId != Match.MatchId)
                    {
                        _hasRoster = false; _recordedLocalSlot = null;
                        HasSnapshot = false; PlayerCount = _eventCount = _killCount = _worldEventCount = _rosterCount = _chatCount = 0;
                        Array.Clear(_identities); Array.Clear(_lives);
                        _world.Reset(match.MatchId);
                        _appliedWorld = false;
                    }
                    Match = match;
                    NetSession.SetPlaybackMatch(match);
                    return true;
                case DemoRecordKind.Snapshot:
                    Span<SnapshotPlayer> players = stackalloc SnapshotPlayer[8];
                    if (!TryReadSnapshot(body, players, out SnapshotPacket snapshot, out int count)
                        || snapshot.MatchId != Match.MatchId || Match.MatchId == 0) { return false; }
                    if (HasSnapshot && !Sequence32.IsNewer(snapshot.Sequence, Snapshot.Sequence)) { return true; }
                    players[..count].CopyTo(_players);
                    SnapshotsReceived++;
                    PlayerCount = count; Snapshot = snapshot; HasSnapshot = _dirty = true;
                    Array.Clear(NetSession.SlotOccupied);
                    foreach (SnapshotPlayer player in Players)
                    {
                        NetSession.SlotOccupied[player.Slot] = true;
                        NetSession.SlotHunter[player.Slot] = player.Hunter;
                    }
                    return true;
                case DemoRecordKind.World:
                    if (!_world.ValidatePacket(body)) { return false; }
                    _world.Receive(body);
                    return true;
                case DemoRecordKind.Roster:
                    // Entries contain strings, so use the small fixed-size managed table.
                    if (body.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(body) != Match.MatchId
                        || !TryReadRoster(body[4..], _roster, out int rosterCount)) { return false; }
                    _rosterCount = rosterCount; _hasRoster = true;
                    Array.Clear(NetSession.SlotOccupied);
                    foreach (NetRosterEntry entry in _roster.AsSpan(0, rosterCount))
                    {
                        NetSession.SlotOccupied[entry.Slot] = true;
                        NetSession.SlotHunter[entry.Slot] = entry.Hunter;
                        NetSession.SlotPing[entry.Slot] = entry.PingMs;
                    }
                    return true;
                case DemoRecordKind.Event:
                    if (body.Length < 5 || BinaryPrimitives.ReadUInt32LittleEndian(body) != Match.MatchId) { return false; }
                    ReliableEventType type = (ReliableEventType)body[4];
                    if (type == ReliableEventType.Chat)
                    {
                        if (!SessionChatPacket.TryRead(body[5..], out SessionChatPacket chat)) { return false; }
                        if (_protocol >= 8)
                        {
                            if (_chatCount == _chats.Length) return false;
                            _chats[_chatCount++] = chat;
                        }
                        else Chat.ChatBox.Receive(new ChatPacket { Slot = chat.Slot, Kind = ChatPacket.KindSay, Name = chat.Name, Text = chat.Text });
                        return true;
                    }
                    if (type == ReliableEventType.WorldEvent && _protocol >= 8)
                    {
                        if (!WorldEvent.TryRead(body[5..], out WorldEvent worldEvent) || worldEvent.MatchId != Match.MatchId
                            || _worldEventCount == _worldEvents.Length) return false;
                        _worldEvents[_worldEventCount++] = worldEvent;
                        return true;
                    }
                    if (type == ReliableEventType.Kill && _protocol >= 8)
                    {
                        if (!KillEvent.TryRead(body[5..], out KillEvent kill) || kill.MatchId != Match.MatchId
                            || _killCount == _kills.Length) return false;
                        _kills[_killCount++] = kill;
                        return true;
                    }
                    Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                    if (type != ReliableEventType.Combat || !CombatEventBatch.TryRead(body[5..], events, out int eventCount)
                        || _eventCount + eventCount > _events.Length) { return false; }
                    events[..eventCount].CopyTo(_events.AsSpan(_eventCount));
                    _eventCount += eventCount;
                    CombatEventsReceived += eventCount;
                    foreach (CombatEvent value in events[..eventCount])
                    { if (value.Kind == CombatEventKind.Damage) { DamageEventsReceived++; } }
                    return true;
                default: return false;
            }
        }

        private bool TryReadSnapshot(ReadOnlySpan<byte> body, Span<SnapshotPlayer> players,
            out SnapshotPacket packet, out int count)
            => _protocol >= 8 ? SnapshotPacket.TryRead(body, players, out packet, out count)
                : Protocol7DemoCodec.TryReadSnapshot(body, players, out packet, out count);

        private bool TryReadRoster(ReadOnlySpan<byte> body, Span<NetRosterEntry> entries, out int count)
            => _protocol >= 8 ? SessionRosterPacket.TryRead(body, entries, out _, out count)
                : Protocol7DemoRoster.TryRead(body, entries, out _, out count);

        private bool TryReadMatch(ReadOnlySpan<byte> body, out MatchTransitionPacket match)
        {
            if (_protocol >= 8) { return MatchTransitionPacket.TryRead(body, out match); }
            if (_protocol == 7) { return Protocol7DemoCodec.TryReadMatch(body, out match); }
            // Protocol 5/6 demo-only layout. Never use this decoder on a live socket.
            match = default;
            if (body.Length != 9 + MatchStatePacket.MaxNameBytes || body[8] < (byte)GameMode.Battle
                || body[8] > (byte)GameMode.PrimeHunter || !NetWireIdentity.ValidText(body[9..])) { return false; }
            string room = NetText.Read(body[9..]);
            if (String.IsNullOrWhiteSpace(room)) { return false; }
            match = new MatchTransitionPacket(BinaryPrimitives.ReadUInt32LittleEndian(body),
                BinaryPrimitives.ReadUInt32LittleEndian(body[4..]), (GameMode)body[8], room);
            return match.MatchId != 0;
        }

        internal void ApplyPendingChat()
        {
            if (_pendingChat != null)
            {
                if (!Chat.ChatBox.RestoreReplay(_pendingChat)) throw new ProgramException("Invalid replay chat checkpoint.");
                _pendingChat = null;
            }
            for (int i = 0; i < _chatCount; i++)
            {
                var chat = _chats[i];
                Chat.ChatBox.Receive(new ChatPacket { Slot = chat.Slot, Kind = ChatPacket.KindSay, Name = chat.Name, Text = chat.Text });
            }
            _chatCount = 0;
        }

        internal void ApplyRoster(Scene scene)
        {
            foreach (NetRosterEntry entry in Roster)
                scene.Roster.Nicknames[entry.Slot] = entry.Name;
        }

        public void BeforeSimulation(Scene scene)
        {
            ApplyRoster(scene);
            if (Match.MatchId == 0) { return; }
            scene.Match.MatchId = Match.MatchId;
            if (_loadedMatch == 0 && !_reloadOnNextApply) { _loadedMatch = Match.MatchId; MatchesLoaded++; }
            else if (_loadedMatch != Match.MatchId || _reloadOnNextApply)
            {
                _loadedMatch = Match.MatchId;
                _reloadOnNextApply = false;
                MatchesLoaded++;
                // Rotation owns mode/room immediately; retain the prior legacy goal
                // until the authoritative world stream supplies the new round's rules.
                int pointGoal = scene.Match.Rules.LegacyPointGoal;
                scene.Match.ApplyRules(_protocol >= 7 ? Match.Rules
                    : scene.Match.Rules.With(mode: Match.Mode.ToMatchMode(),
                        roomKey: Match.Room, scoreGoal: pointGoal, startingLives: pointGoal));
                scene.Match.Flow.ResetProgress();
                if (_protocol >= 7)
                {
                    scene.Match.Phase = MatchPhase.WaitingForPlayers;
                    scene.Match.PhaseRevision = 1;
                    scene.Match.PhaseStartTick = Match.ServerTick;
                    scene.Match.HasPhaseDeadline = false;
                    scene.Match.MatchTime = Match.Rules.TimeLimit.HasValue ? (float)Match.Rules.TimeLimit.Value.TotalSeconds : -1;
                    scene.Match.RadarPlayers = Match.Rules.PlayerRadar;
                }
                scene.TransitionRoomId = Metadata.GetRoomByName(Match.Room).Item1?.Id
                    ?? throw new ProgramException($"Unknown demo room: {Match.Room}");
                (scene.Room ?? throw new ProgramException("Demo scene has no room.")).LoadRoom(resume: false);
            }
            if (_pendingClock != null)
            {
                ReadOnlySpan<byte> clock = _pendingClock;
                scene.RestorePresentationClock(BinaryPrimitives.ReadUInt64LittleEndian(clock),
                    BinaryPrimitives.ReadUInt64LittleEndian(clock[8..]), BinaryPrimitives.ReadSingleLittleEndian(clock[16..]),
                    BinaryPrimitives.ReadSingleLittleEndian(clock[20..]));
                scene.Random.SetRng1(BinaryPrimitives.ReadUInt32LittleEndian(clock[24..]));
                scene.Random.SetRng2(BinaryPrimitives.ReadUInt32LittleEndian(clock[28..]));
                _pendingClock = null;
            }
            if (_dirty)
            {
                ApplyingSnapshot = true;
                try { ApplySnapshot(scene); _dirty = false; }
                finally { ApplyingSnapshot = false; }
            }
            _world.Apply(scene, HasSnapshot ? Snapshot.ServerTick : null);
            if (_world.HasState && (!_appliedWorld || _appliedWorldRevision != _world.Revision))
            { _appliedWorld = true; _appliedWorldRevision = _world.Revision; WorldApplications++; }
            ApplyPendingChat();
            if (_pendingFeedback != null && scene.Presentation is ScenePresentation restoredPresentation)
            {
                if (!MphRead.Combat.ReplayFeedbackState.Restore(_pendingFeedback, restoredPresentation.CombatFeedback, restoredPresentation.WorldFeedback))
                    throw new ProgramException("Invalid replay presentation checkpoint.");
                _recordedLocalSlot = restoredPresentation.CombatFeedback.Local.IsValid
                    ? restoredPresentation.CombatFeedback.Local.Slot : -1;
                _pendingFeedback = null;
            }
            MphRead.Combat.CombatFeedback? feedback = (scene.Presentation as ScenePresentation)?.CombatFeedback;
            int local = _recordedLocalSlot ?? scene.LocalPlayerSlot;
            feedback?.Bind(Match.MatchId, local < 0 ? CombatActor.None : new CombatActor((byte)local, _identities[local], _lives[local]), _roster.AsSpan(0, _rosterCount), HasSnapshot ? Snapshot.ServerTick : 0, scene.Match.PhaseRevision);
            MphRead.Combat.WorldFeedback? worldFeedback = (scene.Presentation as ScenePresentation)?.WorldFeedback;
            worldFeedback?.Bind(Match.MatchId, scene.Match.PhaseRevision);
            for (int i = 0; i < _eventCount; i++)
            {
                CombatEvent value = _events[i];
                if (feedback != null && !feedback.Process(value)) continue;
                CombatActor subject = value.Kind is CombatEventKind.Shot or CombatEventKind.Bomb ? value.Actor : value.Target;
                if (subject.IsValid && _identities[subject.Slot] == subject.ConnectionId && _lives[subject.Slot] == subject.Life)
                { scene.Players[subject.Slot].GetPresentation().PresentCombat(value); }
            }
            for (int i = 0; i < _killCount; i++) feedback?.Process(_kills[i]);
            for (int i = 0; i < _worldEventCount; i++) worldFeedback?.Process(_worldEvents[i], feedback?.Local ?? CombatActor.None,
                HasSnapshot ? Snapshot.ServerTick : 0, scene.Match.Rules.PickupRespawnAnnouncements);
            _eventCount = _killCount = _worldEventCount = 0;
        }

        public void AfterSimulation(Scene scene)
        {
            if (!HasSnapshot) { return; }
            foreach (SnapshotPlayer state in Players)
            {
                PlayerEntity player = scene.Players[state.Slot];
                player.Controls.ClearAll();
                player.ApplySnapshotTransform(state);
            }
        }

        private void ApplySnapshot(Scene scene)
        {
            int occupied = 0;
            foreach (SnapshotPlayer state in Players)
            {
                int slot = state.Slot;
                occupied |= 1 << slot;
                PlayerEntity player = scene.Players[slot];
                if (_identities[slot] != state.ConnectionId)
                {
                    player.ClientActivate(state);
                    _identities[slot] = state.ConnectionId;
                    _lives[slot] = 0;
                }
                player.ApplyServerState(state, _lives[slot] != state.Life);
                player.GetPresentation().ReconcileNetworkAfflictions(state, Snapshot.ServerTick, legacy: _protocol < 8);
                player.ApplySnapshotTransform(state);
                player.Controls.ClearAll();
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
            foreach (SnapshotPlayer state in Players)
            {
                scene.Match.Players[state.Slot].Points = state.Points;
                scene.Match.Players[state.Slot].Kills = state.Kills;
                scene.Match.Players[state.Slot].Deaths = state.Deaths;
                scene.Match.Players[state.Slot].Assists = state.Assists;
            }
            scene.Players.ActiveCount = PlayerCount;
        }
    }
}
