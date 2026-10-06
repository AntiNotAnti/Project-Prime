using System;
using System.IO;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Canonical replay capture for a dedicated server that runs the simulation.
    /// It records the packets the server itself treats as authoritative: accepted
    /// slot intents, its own snapshots, and the roster/match-state stream.
    ///
    /// Recording is intentionally failure-isolated. An I/O problem aborts the
    /// .part file and reports it, but never changes or stops the match.
    /// </summary>
    internal static class ServerReplayRecorder
    {
        internal enum RecordingState { Disabled, Idle, Preparing, Recording, Finalizing, Completed, Failed }
        internal readonly record struct RecordingDiagnostics(RecordingState State, ushort MatchId,
            ulong AuthorityEpoch, int Attempts, int Failures, long QueuedBytes, string? Error);
        private static ReplayWritePump? _writer, _lastWriter;
        private static uint _origin;
        private static bool _pending;
        private static ushort _matchId;
        private static ulong _authorityEpoch, _mapHash;
        private static string _roomKey = "";
        private static bool _matchKnown;
        private static RecordingState _state;
        private static int _attempts, _failures;
        private static ReplayTimelineRecord? _initialCheckpoint;
        static ServerReplayRecorder()
        {
            ReplayCapture.Recorder.Accepted += Accept;
            ReplayCapture.Recorder.CheckpointCaptured += checkpoint =>
            {
                if (_pending)
                {
                    ClearInitialCheckpoint();
                    checkpoint.Retain(); _initialCheckpoint = checkpoint;
                }
                if (_writer == null || checkpoint.RecordingFrame <= _origin) return;
                try { if (!_writer.Checkpoint(checkpoint)) Fail(WriterError()); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Fail(ex); }
            };
            ReplayCapture.Recorder.Resetting += () => Stop();
        }
        private static ServerReplayPolicy _policy = ServerReplayPolicy.Default;

        public static string ReplayDirectory => Paths.Combine(
            Paths.Export, "_demos", "server");
        public static bool Enabled => _policy.Enabled;
        public static bool IsRecording => _pending || _writer != null;
        public static string? CurrentPath { get; private set; }
        private static string? _lastError;
        public static string? LastError => _lastError ?? _lastWriter?.Error?.Message;
        internal static RecordingDiagnostics Diagnostics => new(!_policy.Enabled ? RecordingState.Disabled
            : _state == RecordingState.Finalizing && _lastWriter?.Completion.IsCompleted == true
                ? _lastWriter.Error == null ? RecordingState.Completed : RecordingState.Failed : _state,
            _matchId, _authorityEpoch, _attempts, _failures,
            (_writer ?? _lastWriter)?.QueuedBytes ?? 0, LastError);

        public static void Configure(ServerReplayPolicy policy)
        {
            Stop(); _matchKnown = false; _state = RecordingState.Idle;
            _attempts = _failures = 0; _lastError = null; _lastWriter = null;
            _policy = policy.Normalize();
            if (!_policy.Enabled)
            {
                Console.WriteLine("[replay] canonical server recording disabled");
                return;
            }

            Console.WriteLine($"[replay] canonical server recording enabled; "
                + $"storage {(_policy.StorageLimitGb == 0 ? "unlimited" : _policy.StorageLimitGb + " GB")}, "
                + $"retention {(_policy.RetentionDays == 0 ? "forever" : _policy.RetentionDays + " days")}, "
                + $"keep newest {_policy.KeepLast}");
            _ = System.Threading.Tasks.Task.Run(() => ApplyRetention("startup"));
        }

        // A fatal storage/content failure is latched for this exact authority match.
        // A later match or explicit reconfiguration gets one new attempt. In
        // particular, a failed optional writer never rebuilds a replica every tick.
        internal static bool ShouldBeginMatch(ushort matchId, ulong epoch) => _policy.Enabled
            && (!_matchKnown || _matchId != matchId || _authorityEpoch != epoch);
        internal static bool BeginMatch(ushort matchId, ulong epoch, string roomKey,
            Func<string, ulong>? identify = null)
        {
            if (!ShouldBeginMatch(matchId, epoch)) return false;
            Stop(); _matchKnown = true; _matchId = matchId; _authorityEpoch = epoch;
            _roomKey = roomKey; _mapHash = 0; _lastError = null; _lastWriter = null;
            _state = RecordingState.Preparing; _attempts++;
            try
            {
                _mapHash = (identify ?? ReplayMapIdentity.Compute)(roomKey);
                if (_mapHash == 0)
                    throw new IOException("The server replay map could not be identified.");

                string room = Sanitize(roomKey.Length == 0 ? "match" : roomKey);
                CurrentPath = Path.Combine(ReplayDirectory,
                    $"{room}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{DemoFile.Extension}");
                _pending = true;
                Console.WriteLine($"[replay] canonical server recording: {CurrentPath}");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or System.Collections.Generic.KeyNotFoundException)
            {
                Fail(ex);
                return false;
            }
        }

        internal static bool TryGetMapHash(ushort matchId, ulong epoch, string roomKey, out ulong hash)
        {
            hash = _mapHash;
            return _matchKnown && _matchId == matchId && _authorityEpoch == epoch
                && String.Equals(_roomKey, roomKey, StringComparison.Ordinal) && hash != 0;
        }

        internal static void Tick()
        {
            try
            {
                if (_pending && ReplayCapture.WorldCapture.World is { } world)
                {
                    _origin = world.Session.RecordingFrame;
                    ReplayMetadata metadata = _initialCheckpoint is { } checkpoint && checkpoint.RecordingFrame == _origin
                        ? ReplayTimelineArchive.Metadata(world, ReplayType.FullMatch, checkpoint.Payload)
                        : ReplayTimelineArchive.Metadata(world, ReplayType.FullMatch);
                    ClearInitialCheckpoint();
                    _writer = new ReplayWritePump(CurrentPath!, metadata, _origin,
                        finalized: () => ApplyRetention("match finalization"));
                    _pending = false; _lastWriter = _writer; _state = RecordingState.Recording;
                    _writer.Event(new(0, ReplayEventType.MatchStarted));
                }
                else if (_pending && ReplayCapture.WorldCapture.LastError is { } error)
                    Fail(new InvalidDataException(error));
                if (_writer != null && !_writer.EndFrame(Frame())) Fail(WriterError());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            { Fail(ex); }
        }
        private static void Accept(ReplayTimelineRecord record)
        {
            if (_writer == null) return;
            try { if (!_writer.Record(record)) Fail(WriterError()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { Fail(ex); }
        }
        private static void Fail(Exception ex)
        {
            if (_state == RecordingState.Failed) return;
            _state = RecordingState.Failed; _failures++;
            _lastError = ex.Message; _writer?.Abort(); _writer = null; _pending = false; CurrentPath = null;
            ClearInitialCheckpoint();
            Console.WriteLine($"[replay] canonical recording disabled for match {_matchId}: {ex.Message}; recover any .part file");
        }

        public static void Stop(bool matchEnded = false)
        {
            ReplayWritePump? writer = _writer;
            _writer = null; _pending = false;
            ClearInitialCheckpoint();
            if (_state != RecordingState.Failed) _state = writer == null ? RecordingState.Completed : RecordingState.Finalizing;
            if (writer == null)
            {
                CurrentPath = null;
                return;
            }
            try
            {
                if (matchEnded)
                    writer.Event(new ReplayEvent(Frame(), ReplayEventType.MatchEnded));
                writer.Complete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException)
            {
                _lastError = ex.Message;
                writer.Abort();
                Console.WriteLine($"[replay] canonical recording interrupted: {ex.Message}");
            }
            finally
            {
                CurrentPath = null;
            }
        }

        private static uint Frame() => NetSession.NetFrame >= _origin ? NetSession.NetFrame - _origin : 0;
        private static Exception WriterError() => _writer?.Error ?? new IOException("Replay storage no longer accepts records.");

        private static void ClearInitialCheckpoint()
        { _initialCheckpoint?.Release(); _initialCheckpoint = null; }

        private static void ApplyRetention(string reason)
        {
            try
            {
                Directory.CreateDirectory(ReplayDirectory);
                string gatePath = Path.Combine(ReplayDirectory, ".retention-lock");
                string stampPath = Path.Combine(ReplayDirectory, ".retention-stamp");
                using FileStream gate = new(gatePath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                TimeSpan debounce = reason == "startup"
                    ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);
                if (File.Exists(stampPath)
                    && DateTime.UtcNow - File.GetLastWriteTimeUtc(stampPath) < debounce)
                    return;

                ServerReplayRetentionResult result = ServerReplayRetention.Apply(
                    ReplayDirectory, _policy, CurrentPath);
                File.WriteAllText(stampPath, DateTime.UtcNow.Ticks.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                if (result.DeletedFiles > 0)
                {
                    Console.WriteLine($"[replay] retention after {reason}: deleted "
                        + $"{result.DeletedFiles}, {FormatBytes(result.BeforeBytes)} -> "
                        + $"{FormatBytes(result.AfterBytes)}");
                }
                if (!result.LimitSatisfied)
                {
                    Console.WriteLine($"[replay] retention after {reason}: storage remains "
                        + $"{FormatBytes(result.AfterBytes)} because {_policy.KeepLast} newest "
                        + "recordings/favorites are protected");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException)
            {
                Console.WriteLine($"[replay] retention after {reason} skipped: {ex.Message}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double GiB = 1024d * 1024 * 1024;
            const double MiB = 1024d * 1024;
            return bytes >= GiB ? $"{bytes / GiB:0.00} GiB"
                : bytes >= MiB ? $"{bytes / MiB:0.0} MiB"
                : $"{bytes / 1024d:0.0} KiB";
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }
    }
}
