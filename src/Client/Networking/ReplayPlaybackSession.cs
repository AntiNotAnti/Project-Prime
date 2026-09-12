using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>Owns one replay timeline and decoded replica state.</summary>
    public sealed class ReplayPlaybackSession : IDisposable
    {
        private readonly IReplaySessionHost _host;
        private ReplayReader? _reader;
        private ReplayRecord? _pending;
        private ReplayTimelineClip? _clip;
        private int _clipCursor;
        private MatchRules? _initialRules;
        private uint _frame;
        private bool _started;
        private uint? _requestedSeek;
        private uint _seekTarget;
        private long _seekStarted;
        private int? _perspectiveSlot;
        private ReplayHighlight[]? _highlightReel;
        private int _highlightIndex;
        private uint? _playbackEndFrame;
        private CombatActor? _playbackFocus;
        private bool _exitAtPlaybackEnd;
        // The launcher may have to validate a replay before it can hand the
        // window to MatchStart.  Keep that validation explicit: an active
        // playback session by itself is not evidence that the next launch is
        // meant to reuse it.
        private string? _preparedPath;

        internal ReplayPlaybackSession(IReplaySessionHost host)
        {
            _host = host;
            Modern = new ModernReplayState(host);
            SceneServices = new ReplaySceneServices(this);
        }

        public ReplayPlaybackSession() : this(new PassiveReplaySessionHost()) { }

        internal object? SessionIdentity => (object?)_reader ?? _clip;
        internal ModernReplayState Modern { get; }
        public ISceneServices SceneServices { get; }
        public ReplayTransport Transport { get; } = new();
        public bool IsPassive => _host.IsPassive;
        public bool IsActive { get; private set; }
        public bool IsSeeking { get; private set; }
        public bool IsModern => IsActive && (_clip != null
            || _reader != null && ReplayFile.IsAuthoritativeProtocol(_reader.ProtocolVersion));
        public bool ApplyingSnapshot => IsModern && Modern.ApplyingSnapshot;
        public uint CurrentFrame => _frame;
        public uint CurrentTick => WorldServerTick ?? SnapshotServerTick ?? _frame;
        public int PerspectiveSlot => _perspectiveSlot ?? (IsModern ? Modern.RecordedLocalSlot : -1);
        public bool Paused { get => Transport.Paused; set => Transport.Paused = value; }
        public double PlaybackRate { get => Transport.Rate; set => Transport.Rate = value; }
        public uint DurationFrames => _clip?.EndRecordingFrame ?? _reader?.LastFrame ?? 0;
        public bool CanSeek => IsModern && (_clip != null || _reader?.CanSeek == true);
        public IReadOnlyList<ReplayIndexEntry> Index => _reader?.Index ?? Array.Empty<ReplayIndexEntry>();
        public MatchRules? InitialRules => IsModern ? Modern.InitialRules ?? _initialRules : null;
        public uint? SnapshotServerTick => IsModern && Modern.HasSnapshot ? Modern.Snapshot.ServerTick : null;
        public uint? WorldServerTick => IsModern && Modern.World.HasState ? Modern.World.ServerTick : null;
        public uint LastRestoreFrame { get; private set; }
        public int LastSeekSteps { get; private set; }
        public double LastSeekMilliseconds { get; private set; }
        public string? LastError { get; private set; }
        internal string? PreparedPath => _preparedPath;
        public bool AtEnd => IsActive && (_playbackEndFrame.HasValue
            ? _started && _frame >= _playbackEndFrame.Value
            : !HasPending && (_clip != null || _reader?.CanSeek != true
                || (_started && _frame >= _reader.LastFrame)));
        public bool HighlightPlayback => _highlightReel != null;
        public bool BoundedPlayback => _playbackEndFrame.HasValue;
        public bool ShouldExitAtEnd => _exitAtPlaybackEnd && AtEnd
            && (_highlightReel == null || _highlightIndex >= _highlightReel.Length - 1);
        internal CombatActor? PlaybackFocus => _playbackFocus;
        public int CurrentHighlightIndex => _highlightReel == null ? -1 : _highlightIndex;
        public ReplayHighlight? CurrentHighlight => _highlightReel != null
            ? _highlightReel[_highlightIndex] : null;
        internal ReplayHighlight? NextHighlight => _highlightReel != null
            && _highlightIndex + 1 < _highlightReel.Length
                ? _highlightReel[_highlightIndex + 1] : null;

        private bool HasPending => _clip != null ? _clipCursor < _clip.Records.Count : _pending != null;

        internal bool TryGetActorName(CombatActor actor, out string? name)
        {
            if (IsModern) return Modern.TryGetActorName(actor, out name);
            name = null;
            return false;
        }

        /// <summary>
        /// Opens and rewinds an exact replay path for a subsequent MatchStart
        /// launch.  The prepared identity is one-shot and is never inferred
        /// from an already active playback session.
        /// </summary>
        public bool Prepare(string path)
        {
            _preparedPath = null;
            if (!TryNormalizePath(path, out string normalized))
            {
                Stop();
                LastError = "That replay path is invalid.";
                return false;
            }
            if (!Join(normalized)) return false;
            _preparedPath = normalized;
            return true;
        }

        /// <summary>Consumes a successful <see cref="Prepare"/> exactly once.</summary>
        public bool ConsumePrepared(string path)
        {
            if (!TryNormalizePath(path, out string normalized)
                || _preparedPath is not { } prepared
                || !String.Equals(prepared, normalized, StringComparison.Ordinal)
                || !IsActive)
            {
                return false;
            }
            _preparedPath = null;
            return true;
        }

        public bool Join(string path, int timeoutMs = 8000)
        {
            _ = timeoutMs;
            _preparedPath = null;
            if (!TryNormalizePath(path, out string normalized))
            {
                Stop();
                LastError = "That replay path is invalid.";
                return false;
            }
            Stop();
            LastError = null;
            ReplayReader? reader = ReplayReader.Open(normalized);
            if (reader == null)
            {
                LastError = "That file isn't a replay this build recognises (wrong extension, damaged, or from a different build).";
                return false;
            }
            if (!ReplayFile.IsSupportedProtocol(reader.ProtocolVersion))
            {
                LastError = $"Unsupported replay protocol {reader.ProtocolVersion}.";
                reader.Dispose();
                return false;
            }
            if (_host.IsPassive && !ReplayFile.IsAuthoritativeProtocol(reader.ProtocolVersion))
            {
                LastError = "Legacy protocol replay requires the Theatre player.";
                reader.Dispose();
                return false;
            }
            _host.Start();
            _reader = reader;
            _clip = null;
            Modern.Reset(reader.ProtocolVersion);
            IsActive = true;
            _frame = 0;
            _started = false;
            _pending = reader.ReadNext();
            bool hadRecords = _pending != null;
            long knownAt = -1;
            while (_frame < ReplayPlayback.JoinSearchFrames)
            {
                PumpFrame();
                Modern.DiscardEvents();
                _host.AdvanceLegacy(_frame);
                if (IsModern && Modern.HasSnapshot && Modern.World.HasState) return Rewind(normalized);
                bool knowsMatch = IsModern
                    ? Modern.Match.MatchId != 0 && !String.IsNullOrWhiteSpace(Modern.Match.Room)
                    : NetSession.ServerMatch?.RoomKey.Length > 0;
                if (knowsMatch)
                {
                    if (knownAt < 0) knownAt = _frame;
                    else if (_frame - knownAt >= ReplayPlayback.JoinGraceFrames || AtEnd) return Rewind(normalized);
                }
                else if (AtEnd) break;
            }
            LastError = !hadRecords
                ? "That replay file is empty -- nothing was ever recorded to it."
                : "That replay has no match info in its first few seconds -- the recording may have started before the server said what map it was running.";
            Stop();
            return false;
        }

        /// <summary>Starts a passive session from a frozen rolling-timeline clip.</summary>
        public bool Join(ReplayTimelineClip clip)
        {
            ArgumentNullException.ThrowIfNull(clip);
            _preparedPath = null;
            Stop();
            LastError = null;
            _reader = null;
            _clip = clip;
            IsActive = true;
            if (!RestoreClipBaseline())
            {
                LastError = "Replay clip has an invalid restore point.";
                CloseFile();
                return false;
            }
            _initialRules = Modern.InitialRules;
            return true;
        }

        public bool Join(IReplayTimeline timeline, uint startRecordingFrame, uint endRecordingFrame)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            _preparedPath = null;
            if (!timeline.TryFreeze(startRecordingFrame, endRecordingFrame, out ReplayTimelineClip? clip)
                || clip == null)
            {
                Stop();
                LastError = "Replay timeline cannot provide a complete restore point for that range.";
                return false;
            }
            return Join(clip);
        }

        private bool RestoreClipBaseline()
        {
            if (_clip == null) return false;
            Modern.Reset(NetHeader.Version);
            foreach (ReplayTimelineRecord record in _clip.RestorePoint.Records)
                if (!Modern.Receive(record.Data.Span)) return false;
            if (!Modern.HasCompleteCheckpoint) return false;
            Modern.RequestSceneReload();
            _frame = _clip.RestorePoint.RecordingFrame;
            _started = false;
            _clipCursor = 0;
            return true;
        }

        private bool Rewind(string path)
        {
            _reader?.Dispose();
            _reader = ReplayReader.Open(path);
            if (_reader == null) { LastError = "That replay could not be read a second time."; Stop(); return false; }
            _frame = 0;
            _started = false;
            _pending = _reader.ReadNext();
            _host.Rewind();
            _host.ClearChat();
            _initialRules = Modern.InitialRules;
            Modern.Reset(_reader.ProtocolVersion);
            return true;
        }

        public bool Seek(uint frame)
        {
            if (!CanSeek) return false;
            LastError = null;
            uint minimum = _clip?.StartRecordingFrame ?? 0;
            _requestedSeek = Math.Clamp(frame, minimum, DurationFrames);
            return true;
        }

        public bool SeekEvent(bool next)
        {
            if (_clip != null)
            {
                ReplayTimelineRecord? selected = null;
                foreach (ReplayTimelineRecord entry in _clip.Records)
                {
                    if (entry.Marker == ReplayMarker.None) continue;
                    if (next && entry.RecordingFrame > _frame) { selected = entry; break; }
                    if (!next && entry.RecordingFrame < _frame) selected = entry;
                }
                return selected != null && Seek(selected.RecordingFrame);
            }
            ReplayIndexEntry? indexedSelection = null;
            foreach (ReplayIndexEntry entry in Index)
            {
                if (entry.Marker == ReplayMarker.None) continue;
                if (next && entry.Frame > _frame) { indexedSelection = entry; break; }
                if (!next && entry.Frame < _frame) indexedSelection = entry;
            }
            return indexedSelection.HasValue && Seek(indexedSelection.Value.Frame);
        }

        public void Step() => Transport.Step();

        public void SetPerspective(int slot)
        {
            if (slot is < -1 or > 7) throw new ArgumentOutOfRangeException(nameof(slot));
            _perspectiveSlot = slot;
        }

        public void ConfigureHighlights(IReadOnlyList<ReplayHighlight> highlights)
        {
            ArgumentNullException.ThrowIfNull(highlights);
            if (!IsActive || !CanSeek || highlights.Count is < 1 or > HighlightAnalyzer.MaximumHighlights)
                throw new InvalidOperationException("An active seekable replay and one to eight highlights are required.");
            var copy = new ReplayHighlight[highlights.Count];
            uint previousEnd = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                ReplayHighlight highlight = highlights[i];
                if (highlight.StartFrame > highlight.EndFrame
                    || highlight.EndFrame > DurationFrames
                    || i > 0 && highlight.StartFrame < previousEnd)
                {
                    throw new ArgumentException(
                        "Highlights must be ordered, non-overlapping, and inside the replay duration.",
                        nameof(highlights));
                }
                copy[i] = highlight;
                previousEnd = highlight.EndFrame;
            }
            _highlightReel = copy;
            _highlightIndex = 0;
            _exitAtPlaybackEnd = true;
            BeginHighlight(copy[0]);
        }

        /// <summary>
        /// Configures one user-authored bounded range without representing it
        /// as an authoritative highlight. The optional focus retains exact
        /// slot/connection/life identity for presentation validation.
        /// </summary>
        public void ConfigureRange(uint startFrame, uint endFrame,
            CombatActor? focus = null)
        {
            if (!IsActive || !CanSeek)
                throw new InvalidOperationException(
                    "An active seekable replay is required for a bounded range.");
            if (startFrame >= endFrame || endFrame > DurationFrames)
                throw new ArgumentException(
                    "The replay range must be ordered and inside the replay duration.");
            if (focus is { } actor && !actor.IsValid)
                throw new ArgumentException("A replay range focus must be an exact actor.");
            _highlightReel = null;
            _highlightIndex = 0;
            _playbackEndFrame = endFrame;
            _playbackFocus = focus;
            _exitAtPlaybackEnd = true;
            SetPerspective(focus is { IsValid: true } exact ? exact.Slot : -1);
            if (!Seek(startFrame))
                throw new InvalidOperationException(
                    "That replay range has no checkpoint inside the bounded seek window.");
        }

        internal bool AdvanceHighlightRange()
        {
            if (_highlightReel == null || !AtEnd
                || _highlightIndex >= _highlightReel.Length - 1) return false;
            BeginHighlight(_highlightReel[++_highlightIndex]);
            return true;
        }

        private void BeginHighlight(in ReplayHighlight highlight)
        {
            _playbackEndFrame = highlight.EndFrame;
            _playbackFocus = highlight.Focus.IsValid ? highlight.Focus : null;
            SetPerspective(highlight.Focus.IsValid ? highlight.Focus.Slot : -1);
            Seek(highlight.StartFrame);
        }

        private Hunter HunterForSlot(int slot)
        {
            foreach (NetRosterEntry entry in Modern.Roster)
                if (entry.Slot == slot) return entry.Hunter;
            return Hunter.Samus;
        }

        internal void BuildPlayers(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            Modern.ApplyRoster(scene);
            scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
            {
                scene.AddPlayer(HunterForSlot(slot), recolor: 0, team: -1);
                PlayerEntity player = scene.Players[slot];
                player.IsBot = false;
                player.BotLevel = 0;
                player.LoadFlags &= ~LoadFlags.Active;
            }
            int main = PerspectiveSlot is >= 0 and < PlayerEntity.SlotCapacity
                ? PerspectiveSlot : 0;
            scene.LocalPlayerSlot = main;
            scene.Players.ActiveCount = 0;
        }

        internal PlayerEntity RebuildPlayers(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            Modern.ApplyRoster(scene);
            scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
            {
                PlayerEntity player = scene.Players.Create(HunterForSlot(slot), recolor: 0)
                    ?? throw new ProgramException("Could not rebuild replay player.");
                player.LoadFlags = LoadFlags.SlotActive | LoadFlags.Initial;
                player.IsBot = false;
                player.BotLevel = 0;
            }
            int main = PerspectiveSlot is >= 0 and < PlayerEntity.SlotCapacity
                ? PerspectiveSlot : 0;
            scene.LocalPlayerSlot = main;
            scene.Players.ActiveCount = 0;
            return scene.LocalPlayer
                ?? throw new ProgramException("Replay scene has no local player after rebuild.");
        }

        internal void AfterRoomRebuild(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            for (int slot = 0; slot < scene.Players.Count; slot++)
            {
                if (slot == scene.LocalPlayerSlot) continue;
                PlayerEntity player = scene.Players[slot];
                if (!player.LoadFlags.TestFlag(LoadFlags.SlotActive)) continue;
                scene.InsertEntity(player);
                scene.InitializeEntity(player);
                scene.InitEntity(player.Halfturret);
            }
        }

        internal int TakeSimulationSteps() => IsActive ? (AtEnd ? 0 : Transport.TakeSteps()) : 1;

        internal bool ProcessSeek(Action simulationStep, Action? beginSeek = null)
        {
            if (!IsActive || _reader == null && _clip == null) return false;
            if (_requestedSeek is uint target)
            {
                _requestedSeek = null;
                _seekTarget = target;
                if (_clip != null)
                {
                    if (!RestoreClipBaseline()) { FailSeek("Replay clip restore point is damaged."); return true; }
                }
                else
                {
                    uint warmup = target > ReplayPlayback.TransientWarmupTicks ? target - ReplayPlayback.TransientWarmupTicks : 0;
                    uint candidate = 0;
                    foreach (ReplayIndexEntry entry in Index)
                    {
                        if (entry.Frame > warmup) break;
                        if (entry.Keyframe) candidate = entry.Frame;
                    }
                    if (target - candidate > ReplayPlayback.TransientWarmupTicks + 300)
                    { LastError = "Replay has no checkpoint within the bounded seek window."; return true; }
                    ReplayRecord[]? checkpoint = _reader!.Seek(warmup, out uint restoreFrame);
                    if (checkpoint == null) { FailSeek("Replay checkpoint is damaged."); return true; }
                    _host.Rewind();
                    _host.ClearChat();
                    Modern.Reset(_reader.ProtocolVersion);
                    foreach (ReplayRecord record in checkpoint)
                        if (!Modern.Receive(record.Data)) { FailSeek("Replay checkpoint contains an invalid fact."); return true; }
                    if (checkpoint.Length != 0 && !Modern.HasCompleteCheckpoint)
                    { FailSeek("Replay checkpoint is incomplete."); return true; }
                    Modern.RequestSceneReload();
                    _frame = restoreFrame; _started = false; _pending = _reader.ReadNext();
                }
                LastRestoreFrame = _frame; LastSeekSteps = 0;
                _seekStarted = Stopwatch.GetTimestamp(); IsSeeking = true;
                Modern.PresentationAudioSuppressed = true;
                beginSeek?.Invoke();
            }
            if (!IsSeeking) return false;
            for (int i = 0; i < 120; i++)
            {
                try { simulationStep(); }
                catch (Exception error) when (error is IOException or InvalidDataException or ProgramException)
                { FailSeek(error.Message); break; }
                LastSeekSteps++;
                if (_frame >= _seekTarget)
                { IsSeeking = false; Modern.PresentationAudioSuppressed = false; LastSeekMilliseconds = Stopwatch.GetElapsedTime(_seekStarted).TotalMilliseconds; break; }
            }
            return true;
        }

        private void FailSeek(string message)
        {
            LastError = message; IsSeeking = false; Modern.PresentationAudioSuppressed = false; _requestedSeek = null;
            if (_clip != null) _clipCursor = _clip.Records.Count;
            Transport.Paused = true; _pending = null; _frame = DurationFrames; _started = true;
        }

        public void PumpFrame()
        {
            if (!IsActive || _reader == null && _clip == null) return;
            if (_started) _frame++;
            _started = true;
            int records = 0;
            while (HasPending)
            {
                uint recordFrame;
                ReadOnlySpan<byte> data;
                if (_clip != null)
                {
                    ReplayTimelineRecord record = _clip.Records[_clipCursor];
                    recordFrame = record.RecordingFrame;
                    data = record.Data.Span;
                }
                else
                {
                    ReplayRecord record = _pending!.Value;
                    recordFrame = record.Frame;
                    data = record.Data;
                }
                if (recordFrame > _frame) break;
                if (++records > 4096)
                {
                    LastError = "Replay exceeds the per-frame record limit.";
                    Stop();
                    throw new ProgramException(LastError);
                }
                if (IsModern) { if (!Modern.Receive(data)) _host.RejectRecord(); }
                else _host.InjectLegacy(data.ToArray());
                if (_clip != null) _clipCursor++;
                else _pending = _reader!.ReadNext();
            }
        }

        internal void ApplyRoster(Scene scene) { if (IsModern) Modern.ApplyRoster(scene); }
        public void BeforeSimulation(Scene scene) { if (IsModern) Modern.BeforeSimulation(scene); }
        public void AfterSimulation(Scene scene) { if (IsModern) Modern.AfterSimulation(scene); }

        public void Stop()
        {
            _preparedPath = null;
            if (!_host.IsPassive && IsActive) { _host.Stop(); return; }
            CloseFile();
            _host.Stop();
        }

        internal void CloseFile()
        {
            _preparedPath = null;
            IsActive = false; IsSeeking = false; _requestedSeek = null; Transport.Reset();
            _reader?.Dispose(); _reader = null; _clip = null; _clipCursor = 0;
            _initialRules = null; _pending = null; _frame = 0; _started = false;
            _perspectiveSlot = null; Modern.Reset();
            _highlightReel = null; _highlightIndex = 0; _playbackEndFrame = null;
            _playbackFocus = null; _exitAtPlaybackEnd = false;
        }

        public void Dispose() => Stop();

        private static bool TryNormalizePath(string? path, out string normalized)
        {
            normalized = "";
            if (String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                normalized = Path.GetFullPath(path.Trim());
                return normalized.Length != 0;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }
}
