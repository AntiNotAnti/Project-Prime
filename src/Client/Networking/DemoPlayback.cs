using System;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Plays frame-stamped files without opening a socket. Protocol 4 uses
    /// the legacy presentation adapter; authoritative recordings replay server facts only.
    /// </summary>
    public static class DemoPlayback
    {
        private static DemoReader? _reader;
        private static MatchRules? _initialRules;
        private static readonly ModernDemoState _modern = new();
        internal static ModernDemoState Modern => _modern;
        public static MatchRules? InitialRules => IsModern ? _modern.InitialRules ?? _initialRules : null;
        public static uint? WorldServerTick => IsModern && _modern.World.HasState ? _modern.World.ServerTick : null;
        public static bool IsModern => IsActive && _reader != null && DemoFile.IsAuthoritativeProtocol(_reader.ProtocolVersion);
        public static bool ApplyingSnapshot => IsModern && _modern.ApplyingSnapshot;
        public static void BeforeSimulation(Scene scene) { if (IsModern) { _modern.BeforeSimulation(scene); } }
        public static void AfterSimulation() { if (IsModern) { _modern.AfterSimulation(); } }
        private static DemoRecord? _pending;
        /// <summary>The frame of the recording about to be replayed.</summary>
        private static uint _frame;
        private static bool _started;

        public static bool IsActive { get; private set; }

        /// <summary>True once the file has no more records -- the scene holds on the last state rather than closing itself.</summary>
        public static bool AtEnd => IsActive && _pending == null;

        /// <summary>
        /// Why the last <see cref="Join"/> failed, for a screen that is
        /// still open to show it on -- Console.WriteLine is where this used
        /// to only go, which is invisible on the Windows build outside a
        /// typed command.
        /// </summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// How far into the recording <see cref="Join"/> will look for the
        /// match info before giving up. Twenty seconds of recorded frames:
        /// the server repeats its match state once a second, so a file that
        /// has not said what room it is by then does not contain one.
        /// </summary>
        private const uint JoinSearchFrames = 60 * 20;

        /// <summary>
        /// Frames to keep pumping after the room key is known.
        ///
        /// BuildPlayers, called right after this, reads NetSession.SlotHunter
        /// and SlotOccupied to decide every player's hunter and whether their
        /// slot is even active, and those come from Roster packets that do not
        /// necessarily land in the same burst as the MatchState that answers
        /// ServerMatch first. Returning on the room key alone showed real
        /// players with the wrong hunter, or briefly not active at all. The
        /// roster repeats once a second, so two of those.
        /// </summary>
        private const uint JoinGraceFrames = 120;

        /// <summary>
        /// Open the file and wind it forward to the first match info, the
        /// same shape as <see cref="NetLaunch.Join"/> -- true once
        /// <c>NetSession.ServerMatch</c> knows what room to load.
        ///
        /// Blocking, and called off the UI thread for that reason, but no
        /// longer *waiting*: a live join waits on a server, and this reads a
        /// file, so it costs a few hundred frames of parsing rather than the
        /// eight seconds the wall-clock version could spend.
        /// </summary>
        public static bool Join(string path, int timeoutMs = 8000)
        {
            _ = timeoutMs; // kept for the call site; nothing here waits on a clock
            Stop();
            LastError = null;
            DemoReader? reader = DemoReader.Open(path);
            if (reader == null)
            {
                LastError = "That file isn't a demo this build recognises "
                    + "(wrong extension, damaged, or from a different build).";
                Console.WriteLine($"[demo] \"{path}\": {LastError}");
                return false;
            }
            if (!DemoFile.IsSupportedProtocol(reader.ProtocolVersion))
            {
                LastError = $"Unsupported demo protocol {reader.ProtocolVersion}.";
                reader.Dispose();
                Stop();
                return false;
            }
            NetSession.StartPlayback();
            _reader = reader;
            _modern.Reset(reader.ProtocolVersion);
            IsActive = true;
            _frame = 0;
            _started = false;
            _pending = _reader.ReadNext();
            bool hadRecords = _pending != null;
            long knownAt = -1;
            while (_frame < JoinSearchFrames)
            {
                PumpFrame();
                _modern.DiscardEvents();
                NetSession.Update(_frame / 60.0);
                if (IsModern && _modern.HasSnapshot && _modern.World.HasState) { return Rewind(path); }
                if (NetSession.ServerMatch?.RoomKey.Length > 0)
                {
                    if (knownAt < 0)
                    {
                        knownAt = _frame;
                    }
                    else if (_frame - knownAt >= JoinGraceFrames || AtEnd)
                    {
                        return Rewind(path);
                    }
                }
                else if (AtEnd)
                {
                    break;
                }
            }
            LastError = !hadRecords
                ? "That demo file is empty -- nothing was ever recorded to it."
                : "That demo has no match info in its first few seconds -- "
                    + "the recording may have started before the server said what map it was running.";
            Console.WriteLine($"[demo] \"{path}\": {LastError}");
            Stop();
            return false;
        }

        /// <summary>
        /// Go back to the file's first frame, now that the room to load is
        /// known.
        ///
        /// The search above is not free: it hands its records to the session
        /// to be acted on, and there is no scene yet to act on them, so
        /// everything in the first second or three of the recording was
        /// consumed and then thrown away. The replay opened that far in --
        /// which is why the first thing anybody did after pressing record was
        /// missing from the file's playback while everything after it was
        /// fine. Reported as "the first shot is not in the demo", and it was
        /// in the demo; it was simply never played.
        ///
        /// Rewinding costs re-parsing a couple of hundred records. The state
        /// the search was for stays -- see
        /// <see cref="NetSession.RewindPlayback"/> for what has to go with it.
        /// </summary>
        private static bool Rewind(string path)
        {
            _reader?.Dispose();
            _reader = DemoReader.Open(path);
            if (_reader == null)
            {
                LastError = "That demo could not be read a second time.";
                Console.WriteLine($"[demo] \"{path}\": {LastError}");
                Stop();
                return false;
            }
            _frame = 0;
            _started = false;
            _pending = _reader.ReadNext();
            NetSession.RewindPlayback();
            Chat.ChatBox.Clear();
            _initialRules = _modern.InitialRules;
            _modern.Reset(_reader.ProtocolVersion);
            return true;
        }

        /// <summary>
        /// Called once a frame: hands over every packet the recorder saw on
        /// this frame of its own run.
        /// </summary>
        public static void PumpFrame()
        {
            if (!IsActive || _reader == null)
            {
                return;
            }
            // The first pumped frame is frame 0 of the recording; every one
            // after it is the next. Advancing before the release instead
            // would skip whatever the recorder caught on its own first frame.
            if (_started)
            {
                _frame++;
            }
            _started = true;
            int records = 0;
            while (_pending is DemoRecord record && record.Frame <= _frame)
            {
                if (++records > 4096)
                {
                    LastError = "Demo exceeds the per-frame record limit.";
                    Stop();
                    throw new ProgramException(LastError);
                }
                if (IsModern)
                {
                    if (!_modern.Receive(record.Data)) { NetSession.Metrics.Reject(); }
                }
                else { NetSession.InjectPlaybackPacket(record.Data, record.Data.Length); }
                _pending = _reader.ReadNext();
            }
        }

        public static void Stop() => NetSession.Stop();

        internal static void CloseFile()
        {
            IsActive = false;
            _reader?.Dispose();
            _reader = null;
            _initialRules = null;
            _pending = null;
            _frame = 0;
            _started = false;
            _modern.Reset();
        }
    }
}
