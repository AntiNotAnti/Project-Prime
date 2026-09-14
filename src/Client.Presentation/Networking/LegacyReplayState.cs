using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MphRead.Combat;
using MphRead.Cosmetics;
using MphRead.Entities;
using MphRead.Mods.Audio;
using MphRead.Mods.Hud;
using MphRead.Replay;

[assembly: InternalsVisibleTo("MphRead.Tests")]

namespace MphRead.Mods.Network
{
    // Format 2 retains frame deltas and compression. Its protocol byte selects
    // legacy datagrams (4) or authoritative presentation records (5 through 9).

    /// <summary>Socket-free playback of server facts. Inputs and connection control are never replayed.</summary>
    internal sealed class ModernReplayState
    {
        private readonly IReplaySessionHost _host;
        internal ModernReplayState(IReplaySessionHost? host = null)
            => _host = host ?? new TheatreReplaySessionHost();
        private readonly SnapshotPlayer[] _players = new SnapshotPlayer[8];
        private readonly NetRosterEntry[] _roster = new NetRosterEntry[8];
        private readonly ulong[] _identities = new ulong[8];
        private readonly uint[] _lives = new uint[8];
        private readonly WorldEvent[] _worldEvents = new WorldEvent[256];
        private int _worldEventCount;
        private readonly KillEvent[] _kills = new KillEvent[256];
        private int _killCount, _rosterCount;
        private readonly MatchEvent[] _semanticEvents = new MatchEvent[256];
        private int _semanticEventCount;
        private readonly SemanticAwardJournal _awardJournal = new();
        private long _appliedAwardRevision;
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
        private ReplayRecordKind _fragmentKind;
        private bool _reloadOnNextApply;
        private int? _recordedLocalSlot;
        internal bool PresentationAudioSuppressed { get; set; }
        internal int RecordedLocalSlot => _recordedLocalSlot ?? -1;
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

        internal bool TryGetActorName(CombatActor actor, out string? name)
        {
            name = null;
            if (!actor.IsValid || actor.Slot >= _identities.Length
                || _identities[actor.Slot] != actor.ConnectionId
                || _lives[actor.Slot] != actor.Life)
            {
                return false;
            }
            foreach (NetRosterEntry entry in Roster)
            {
                if (entry.Slot == actor.Slot
                    && entry.ConnectionId == actor.ConnectionId
                    && !String.IsNullOrWhiteSpace(entry.Name))
                {
                    name = entry.Name;
                    return true;
                }
            }
            return false;
        }
        private uint _appliedWorldRevision;
        private bool _appliedWorld;
        public void DiscardEvents()
        {
            _eventCount = _killCount = _worldEventCount = _chatCount = _semanticEventCount = 0;
            _awardJournal.Reset(); _appliedAwardRevision = 0;
        }

        public void Reset(byte protocol = NetHeader.Version)
        {
            _protocol = protocol;
            _recordedLocalSlot = null; _hasRoster = false;
            _feedbackBytes = _pendingFeedback = _pendingClock = _pendingChat = null; _reloadOnNextApply = false; _feedbackOffset = _feedbackPart = 0;
            _world.LegacyProtocol = protocol is 5 or 6;
            _world.Protocol7Replay = protocol == 7;
            Match = default; Snapshot = default; HasSnapshot = _dirty = false;
            PlayerCount = _eventCount = _killCount = _worldEventCount = _rosterCount = _chatCount = _semanticEventCount = 0; _loadedMatch = 0;
            _awardJournal.Reset(); _appliedAwardRevision = 0;
            Array.Clear(_identities); Array.Clear(_lives);
            _world.Reset(0);
            SnapshotsReceived = CombatEventsReceived = DamageEventsReceived = WorldApplications = 0;
            MatchesLoaded = 0; _appliedWorld = false;
            PresentationAudioSuppressed = false;
        }

        public bool Receive(ReadOnlySpan<byte> record)
        {
            if (record.Length < 1 || record.Length > NetConfig.MaxPacketSize) { return false; }
            ReadOnlySpan<byte> body = record[1..];
            switch ((ReplayRecordKind)record[0])
            {
                case ReplayRecordKind.Perspective:
                    if (_protocol < 8 || body.Length != 1 || (body[0] > 7 && body[0] != 255)) return false;
                    _recordedLocalSlot = body[0] == 255 ? -1 : body[0]; return true;
                case ReplayRecordKind.Clock:
                    if (_protocol < 8 || body.Length != 32
                        || !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(body[16..]))
                        || !float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(body[20..]))
                        || BinaryPrimitives.ReadSingleLittleEndian(body[16..]) < 0
                        || BinaryPrimitives.ReadSingleLittleEndian(body[20..]) < 0) return false;
                    _pendingClock = body.ToArray(); return true;
                case ReplayRecordKind.ChatState:
                case ReplayRecordKind.Presentation:
                    if (_protocol < 8 || body.Length < 8) return false;
                    int part = BinaryPrimitives.ReadUInt16LittleEndian(body);
                    int parts = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                    int total = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                    if (total is < 1 or > MphRead.Combat.ReplayFeedbackState.MaximumBytes || parts is < 1 or > 256 || part >= parts) return false;
                    if (part == 0) { _feedbackBytes = new byte[total]; _feedbackOffset = _feedbackPart = 0; _feedbackParts = parts; _fragmentKind = (ReplayRecordKind)record[0]; }
                    if (_fragmentKind != (ReplayRecordKind)record[0] || _feedbackBytes == null || _feedbackBytes.Length != total || parts != _feedbackParts || part != _feedbackPart
                        || body.Length - 8 > total - _feedbackOffset) return false;
                    body[8..].CopyTo(_feedbackBytes.AsSpan(_feedbackOffset)); _feedbackOffset += body.Length - 8; _feedbackPart++;
                    if (_feedbackPart == parts)
                    {
                        if (_feedbackOffset != total) return false;
                        if (_fragmentKind == ReplayRecordKind.Presentation) _pendingFeedback = _feedbackBytes;
                        else _pendingChat = _feedbackBytes;
                        _feedbackBytes = null;
                    }
                    return true;
                case ReplayRecordKind.Match:
                    if (!TryReadMatch(body, out MatchTransitionPacket match) || match.MatchId == 0)
                    { return false; }
                    if (match.MatchId != Match.MatchId)
                    {
                        _hasRoster = false; _recordedLocalSlot = null;
                        HasSnapshot = false; PlayerCount = _eventCount = _killCount = _worldEventCount = _rosterCount = _chatCount = _semanticEventCount = 0;
                        Array.Clear(_identities); Array.Clear(_lives);
                        _world.Reset(match.MatchId);
                        _appliedWorld = false;
                        _awardJournal.Reset(); _appliedAwardRevision = 0;
                    }
                    Match = match;
                    _host.SetMatch(match);
                    return true;
                case ReplayRecordKind.Snapshot:
                    Span<SnapshotPlayer> players = stackalloc SnapshotPlayer[8];
                    if (!TryReadSnapshot(body, players, out SnapshotPacket snapshot, out int count)
                        || snapshot.MatchId != Match.MatchId || Match.MatchId == 0) { return false; }
                    if (HasSnapshot && !Sequence32.IsNewer(snapshot.Sequence, Snapshot.Sequence)) { return true; }
                    players[..count].CopyTo(_players);
                    SnapshotsReceived++;
                    PlayerCount = count; Snapshot = snapshot; HasSnapshot = _dirty = true;
                    _host.SetSnapshotRoster(Players);
                    return true;
                case ReplayRecordKind.World:
                    if (!_world.ValidatePacket(body)) { return false; }
                    _world.Receive(body);
                    return true;
                case ReplayRecordKind.Roster:
                    // Entries contain strings, so use the small fixed-size managed table.
                    if (body.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(body) != Match.MatchId
                        || !TryReadRoster(body[4..], _roster, out int rosterCount)) { return false; }
                    _rosterCount = rosterCount; _hasRoster = true;
                    _host.SetRoster(_roster.AsSpan(0, rosterCount));
                    return true;
                case ReplayRecordKind.Event:
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
                        else _host.PresentChat(chat);
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
                    if (type == ReliableEventType.MatchAward && _protocol >= 9)
                    {
                        if (!MatchAwardPacket.TryRead(body[5..], out MatchAwardPacket packet)
                            || packet.MatchId != Match.MatchId
                            || !MatchAwardPacketConversion.TryToAward(packet, out MatchAward award)) return false;
                        _awardJournal.Record(award);
                        return true;
                    }
                    if (type == ReliableEventType.MatchSemantic && _protocol >= 9)
                    {
                        if (!MatchSemanticEventPacket.TryRead(body[5..], out MatchSemanticEventPacket packet)
                            || packet.MatchId != Match.MatchId
                            || !MatchSemanticEventPacketConversion.TryToEvent(packet, out MatchEvent semantic)
                            || _semanticEventCount == _semanticEvents.Length) return false;
                        _semanticEvents[_semanticEventCount++] = semantic;
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
            => _protocol >= 17 ? SnapshotPacket.TryRead(body, players, out packet, out count)
                : _protocol >= 8 ? Protocol16ReplayCodec.TryReadSnapshot(body, players, out packet, out count)
                : Protocol7ReplayCodec.TryReadSnapshot(body, players, out packet, out count);

        private bool TryReadRoster(ReadOnlySpan<byte> body, Span<NetRosterEntry> entries, out int count)
            => _protocol >= 19 ? SessionRosterPacket.TryRead(body, entries, out _, out count)
                : _protocol >= 8 ? Protocol18ReplayRoster.TryRead(body, entries, out _, out count)
                : Protocol7ReplayRoster.TryRead(body, entries, out _, out count);

        private bool TryReadMatch(ReadOnlySpan<byte> body, out MatchTransitionPacket match)
        {
            if (_protocol >= 9) { return MatchTransitionPacket.TryRead(body, out match); }
            if (_protocol == 8)
            {
                return MatchTransitionPacket.TryRead(body, out match)
                    || Protocol8ReplayCodec.TryReadMatch(body, out match);
            }
            if (_protocol == 7) { return Protocol7ReplayCodec.TryReadMatch(body, out match); }
            // Protocol 5/6 replay-only layout. Never use this decoder on a live socket.
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
                if (!_host.RestoreChat(_pendingChat)) throw new ProgramException("Invalid replay chat checkpoint.");
                _pendingChat = null;
            }
            for (int i = 0; i < _chatCount; i++)
            {
                var chat = _chats[i];
                _host.PresentChat(chat);
            }
            _chatCount = 0;
        }

        internal void ApplyRoster(Scene scene)
        {
            foreach (NetRosterEntry entry in Roster)
            {
                scene.Roster.Nicknames[entry.Slot] = entry.Name;
                if (entry.Slot < scene.Players.Count)
                {
                    PlayerEntity player = scene.Players[entry.Slot];
                    // Historical/data-only replay inspection intentionally has
                    // no client presentation. Preserve roster restoration there
                    // without manufacturing renderer ownership for cosmetics.
                    PlayerPresentation? presentation = player.Presentation
                        as PlayerPresentation;
                    if (presentation == null
                        && scene.Presentation is ScenePresentation)
                    {
                        presentation = player.GetPresentation();
                    }
                    presentation?.SetCosmeticLoadout(new CosmeticLoadoutIds(
                        entry.SkinId, entry.ArmorEffectId, entry.DeathEffectId));
                }
            }
        }

        public void BeforeSimulation(Scene scene)
        {
            ApplyRoster(scene);
            if (Match.MatchId == 0) { return; }
            scene.Match.MatchId = Match.MatchId;
            if (_loadedMatch == 0 && !_reloadOnNextApply) { _loadedMatch = Match.MatchId; MatchesLoaded++; }
            else if (_loadedMatch != Match.MatchId || _reloadOnNextApply)
            {
                // A seek/baseline restore can reuse the same match id while
                // replacing the scene's player instances. Drop any pending
                // remote locomotion state before the restored snapshot is
                // simulated, so the old life cannot leak into the replay.
                foreach (PlayerEntity player in scene.GetPlayerEntities())
                    player.ResetRemoteLocomotion();
                _loadedMatch = Match.MatchId;
                _reloadOnNextApply = false;
                MatchesLoaded++;
                if (scene.Presentation is ScenePresentation semanticPresentation)
                {
                    semanticPresentation.Announcer.Reset();
                    semanticPresentation.AwardHud.Reset();
                }
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
                scene.TransitionRoomId = Metadata.RequireRuntimeRoom(Match.Room).RuntimeId;
                (scene.Room ?? throw new ProgramException("Replay scene has no room.")).LoadRoom(resume: false);
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
            if (!_host.AllowsPresentationSideEffects)
            {
                _pendingFeedback = null;
                _eventCount = _killCount = _worldEventCount = _semanticEventCount = 0;
                _awardJournal.Reset();
                _appliedAwardRevision = 0;
                return;
            }
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
                bool currentTarget = !value.Target.IsValid
                    || value.Target.Slot < _identities.Length
                    && _identities[value.Target.Slot] == value.Target.ConnectionId
                    && _lives[value.Target.Slot] == value.Target.Life;
                if (feedback != null && !feedback.Process(value,
                    allowLocalHitMarker: currentTarget)) continue;
                if (scene.Presentation is ScenePresentation observedCombat)
                    observedCombat.BroadcastObservations.Record(value,
                        Match.MatchId, scene.Match.PhaseRevision);
                CombatActor subject = value.Kind is CombatEventKind.Shot or CombatEventKind.Bomb ? value.Actor : value.Target;
                if (subject.IsValid && _identities[subject.Slot] == subject.ConnectionId && _lives[subject.Slot] == subject.Life)
                { scene.Players[subject.Slot].GetPresentation().PresentCombat(value); }
            }
            for (int i = 0; i < _killCount; i++)
            {
                KillEvent value = _kills[i];
                bool accepted = feedback?.Process(value) == true;
                if (value.Victim.Slot < scene.Players.Count
                    && _identities[value.Victim.Slot] == value.Victim.ConnectionId)
                {
                    PlayerPresentation victimPresentation
                        = scene.Players[value.Victim.Slot].GetPresentation();
                    bool actorBound = victimPresentation.IsDeathPresentationBoundTo(
                        value.Victim, _identities[value.Victim.Slot],
                        _lives[value.Victim.Slot]);
                    bool semanticCurrent
                        = DeathPresentationRouting.IsSemanticCurrent(value,
                            Match.MatchId, scene.Match.PhaseRevision);
                    if (DeathPresentationRouting.ShouldObserveKill(accepted,
                            actorBound, semanticCurrent))
                        victimPresentation.PresentAuthoritativeKill(value,
                            scene.Services.WorldServerTick, PresentationAudioSuppressed);
                    else if (accepted)
                        Mods.DebugLog.Line("cosmetics/death",
                            $"Rejected replay kill {value.Id} for slot {value.Victim.Slot}: actor binding mismatch.");
                }
            }
            for (int i = 0; i < _worldEventCount; i++)
            {
                if (scene.Presentation is ScenePresentation observed)
                    observed.BroadcastObservations.Record(_worldEvents[i]);
                worldFeedback?.Process(_worldEvents[i], feedback?.Local ?? CombatActor.None,
                    HasSnapshot ? Snapshot.ServerTick : 0, scene.Match.Rules.PickupRespawnAnnouncements);
            }
            if (scene.Presentation is ScenePresentation awardPresentation)
            {
                for (int i = 0; i < _semanticEventCount; i++)
                    awardPresentation.BroadcastObservations.Record(_semanticEvents[i]);
                Span<MatchAward> observedAwards = stackalloc MatchAward[SemanticAwardJournal.Capacity];
                int observedAwardCount = _awardJournal.CopySince(_appliedAwardRevision,
                    observedAwards);
                for (int i = 0; i < observedAwardCount; i++)
                    awardPresentation.BroadcastObservations.Record(observedAwards[i]);
                byte localTeam = awardPresentation.CombatFeedback.Local.IsValid
                    ? (byte)scene.Players[awardPresentation.CombatFeedback.Local.Slot].TeamIndex
                    : (byte)255;
                ApplyPendingSemanticEvents(awardPresentation.Announcer, localTeam);
                ApplyPendingAwards(awardPresentation.Announcer, awardPresentation.AwardHud);
            }
            _eventCount = _killCount = _worldEventCount = _semanticEventCount = 0;
        }

        internal int ApplyPendingSemanticEvents(AnnouncerService announcer, byte localTeam)
        {
            ArgumentNullException.ThrowIfNull(announcer);
            int count = _semanticEventCount;
            for (int i = 0; i < _semanticEventCount; i++)
            {
                MatchEvent value = _semanticEvents[i];
                if (value.Kind == MatchEventKind.MatchEnded)
                    announcer.Consume(value, localTeam);
                else
                    announcer.Consume(value);
            }
            Array.Clear(_semanticEvents, 0, _semanticEventCount);
            _semanticEventCount = 0;
            return count;
        }

        /// <summary>
        /// Delivers retained raw awards to the bounded presentation consumers.
        /// The revision cursor continues to advance when the journal evicts old
        /// facts, so a replay paused for more than the journal capacity still
        /// receives its newest retained awards rather than stopping at 256.
        /// </summary>
        internal int ApplyPendingAwards(AnnouncerService announcer, AwardHudQueue hud)
        {
            ArgumentNullException.ThrowIfNull(announcer);
            ArgumentNullException.ThrowIfNull(hud);
            Span<MatchAward> pending = stackalloc MatchAward[SemanticAwardJournal.Capacity];
            int count = _awardJournal.CopySince(_appliedAwardRevision, pending);
            for (int i = 0; i < count; i++)
            {
                announcer.Consume(pending[i]);
                hud.Enqueue(pending[i]);
            }
            _appliedAwardRevision = _awardJournal.Revision;
            return count;
        }

        public void AfterSimulation(Scene scene)
        {
            if (!HasSnapshot) { return; }
            foreach (SnapshotPlayer state in Players)
            {
                PlayerEntity player = scene.Players[state.Slot];
                player.Controls.ClearAll();
                // Keep replay locomotion explicit on every application path;
                // playback has no live interpolation clock and the render
                // path may apply a frame without a newly decoded snapshot.
                player.SetRemoteLocomotionIntent(state, state.Speed);
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
                    player.GetPresentation().ResetAuthoritativeDeathPresentation();
                    player.ClientActivate(state);
                    _identities[slot] = state.ConnectionId;
                    _lives[slot] = 0;
                }
                bool snapshotDeath = state.Health == 0
                    && (state.Flags & (SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch)) == 0;
                bool engineDeath = snapshotDeath && player.Health > 0
                    && (state.Flags & SnapshotPlayerFlags.Spawned) == 0
                    && !scene.Services.SuppressDamage(player);
                player.ApplyServerState(state, _lives[slot] != state.Life);
                player.GetPresentation().ObserveAuthoritativeDeath(
                    new CombatActor(state.Slot, state.ConnectionId, state.Life), snapshotDeath,
                    (state.Flags & SnapshotPlayerFlags.AltForm) != 0, Snapshot.ServerTick,
                    engineDeath, PresentationAudioSuppressed);
                player.GetPresentation().ReconcileNetworkAfflictions(state, Snapshot.ServerTick, legacy: _protocol < 8);
                // Replays do not use live snapshot interpolation. Feed the
                // recorded engine velocity explicitly so removing animation
                // selection from ApplySnapshotTransform does not change their
                // deterministic locomotion.
                player.SetRemoteLocomotionIntent(state, state.Speed);
                player.ApplySnapshotTransform(state);
                player.Controls.ClearAll();
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
