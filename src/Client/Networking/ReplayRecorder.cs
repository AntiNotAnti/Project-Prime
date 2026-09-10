using System;
using System.Buffers.Binary;
using System.IO;
using System.Collections.Generic;

namespace MphRead.Mods.Network
{
    /// <summary>Records accepted server facts at presentation frames, independently of connection traffic.</summary>
    internal static class ReplayRecorder
    {
        private static ReplayWriter? _writer;
        private static readonly RollingReplayTimeline _timeline = new();
        private static readonly ClientWorldState _world = new();
        private static uint _frame;
        private static uint _writerOriginFrame;
        private static uint _match;
        private static uint _rosterRevision;
        private static uint _worldRevision;
        private static long _snapshotCount;
        private static bool _hasRoster;
        private static bool _hasWorld;
        private static bool _hasTimelineRestore;
        private static uint _timelineRestoreFrame;
        private static bool _hasWriterKeyframe;
        private static uint _writerKeyframeFrame;
        private static readonly ReplayEventIndexer _markers = new();
        public static bool IsRecording => _writer != null;
        internal static IReplayTimeline Timeline => _timeline;
        public static string? CurrentPath { get; private set; }
        public static string? LastError { get; private set; }

        public static bool Start()
        {
            LastError = null;
            if (IsRecording || ReplayPlayback.IsActive || AuthoritativePlay.Current is not { } play
                || play.Client.Accepted.MatchId == 0)
            {
                LastError = "Join a match before recording a replay.";
                return false;
            }
            string room = SanitizeFileName(play.Client.Accepted.Room);
            string fileName = $"{room}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{ReplayFile.Extension}";
            string path = Paths.Combine(Paths.Export, "_replays", fileName);
            return Start(path, play.Client);
        }

        internal static bool Start(string path, NetClient client)
        {
            if (_writer != null || client.Accepted.MatchId == 0) { return false; }
            LastError = null;
            try { _writer = new ReplayWriter(path, NetHeader.Version, indexed: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[replay] could not start recording: {LastError}");
                return false;
            }
            CurrentPath = path;
            _writerOriginFrame = _frame;
            _hasWriterKeyframe = false;
            try { WriteOpeningFrame(client); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                LastError = ex.Message;
                Stop();
                return false;
            }
            return IsRecording;
        }

        public static void Stop()
        {
            ReplayWriter? writer = _writer;
            _writer = null;
            CurrentPath = null;
            _hasWriterKeyframe = false;
            try { writer?.Dispose(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[replay] could not finish recording: {LastError}");
            }
        }

        internal static void RecordFrame(NetClient client, Scene? scene = null)
        {
            if (client.Accepted.MatchId == 0) { return; }
            _frame = unchecked(_frame + 1);
            Span<byte> body = stackalloc byte[NetConfig.MaxPacketSize - 1];
            if (_match != client.Accepted.MatchId)
            {
                _timeline.Reset();
                _match = client.Accepted.MatchId;
                _hasRoster = _hasWorld = _hasTimelineRestore = _hasWriterKeyframe = false;
                _markers.Reset();
                _snapshotCount = -1;
                new MatchTransitionPacket(_match, client.Accepted.ServerTick, client.Accepted.Rules).Write(body[..MatchTransitionPacket.Size]);
                Write(ReplayRecordKind.Match, body[..MatchTransitionPacket.Size], client.Accepted.ServerTick);
            }
            if (client.HasRoster && (!_hasRoster || _rosterRevision != client.RosterRevision))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(body, _match);
                int count = SessionRosterPacket.Write(body[4..], client.RosterRevision, client.Roster);
                Write(ReplayRecordKind.Roster, body[..(count + 4)], CurrentServerTick(client));
                _rosterRevision = client.RosterRevision;
                _hasRoster = true;
            }
            if (client.HasSnapshot && _snapshotCount != client.SnapshotsReceived)
            {
                int count = client.Snapshot.Write(body, client.SnapshotPlayers);
                Write(ReplayRecordKind.Snapshot, body[..count], client.Snapshot.ServerTick);
                _snapshotCount = client.SnapshotsReceived;
            }
            if (_world.HasState && _world.MatchId == _match && (!_hasWorld || _worldRevision != _world.Revision))
            {
                ReplayMarker marker = ReplayMarker.None;
                foreach (WorldRecord state in _world.Records)
                    if (state.Kind == WorldRecordKind.Match && state.E != 0)
                    {
                        marker = _markers.ForTerminalWorld(CaptureProtocolVersion, terminal: true);
                        break;
                    }
                for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
                {
                    int count = WorldPacket.Write(body, _match, _world.Revision, _world.ServerTick, _world.Records, offset);
                    Write(ReplayRecordKind.World, body[..count], _world.ServerTick,
                        offset == 0 ? marker : ReplayMarker.None);
                }
                _worldRevision = _world.Revision;
                _hasWorld = true;
            }
            if (client.HasRoster && client.HasSnapshot && _world.HasState && _world.MatchId == _match
                && scene?.Presentation is ScenePresentation presentation
                && (!_hasTimelineRestore || _frame - _timelineRestoreFrame >= 300
                    || _writer != null && (!_hasWriterKeyframe || WriterFrame - _writerKeyframeFrame >= 300)))
            {
                try
                {
                    List<byte[]> checkpoint = BuildKeyframe(client, presentation);
                    if (!_hasTimelineRestore || _frame - _timelineRestoreFrame >= 300)
                    {
                        uint checkpointTick = CurrentServerTick(client);
                        var records = new ReplayTimelineRecord[checkpoint.Count];
                        for (int i = 0; i < records.Length; i++)
                        {
                            uint recordTick = checkpointTick;
                            ReplayTimelineTickReader.TryRead(checkpoint[i], checkpointTick, out recordTick);
                            records[i] = new ReplayTimelineRecord(_frame, recordTick, checkpoint[i]);
                        }
                        if (ReplayRestorePoint.TryCreate(_frame, checkpointTick, records,
                            out ReplayRestorePoint? restore) && restore != null)
                            _timeline.AppendRestorePoint(restore);
                        _hasTimelineRestore = true; _timelineRestoreFrame = _frame;
                    }
                    if (_writer != null && (!_hasWriterKeyframe || WriterFrame - _writerKeyframeFrame >= 300))
                    {
                        // The first checkpoint also seeds sequential playback's clocks and recorded
                        // local feedback perspective when recording begins in the middle of a match.
                        if (!_hasWriterKeyframe)
                            foreach (byte[] record in checkpoint)
                                if ((ReplayRecordKind)record[0] is ReplayRecordKind.Clock or ReplayRecordKind.Presentation or ReplayRecordKind.ChatState)
                                    _writer.WriteRecord(WriterFrame, record);
                        _writer.WriteKeyframe(WriterFrame, checkpoint);
                        _hasWriterKeyframe = true; _writerKeyframeFrame = WriterFrame;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
                { LastError = ex.Message; Stop(); }
            }
        }

        private static List<byte[]> BuildKeyframe(NetClient client, ScenePresentation presentation)
        {
            var records = new List<byte[]>();
            byte[] body = new byte[NetConfig.MaxPacketSize];
            new MatchTransitionPacket(_match, client.Accepted.ServerTick, client.Accepted.Rules).Write(body.AsSpan(0, MatchTransitionPacket.Size));
            records.Add(Record(ReplayRecordKind.Match, body.AsSpan(0, MatchTransitionPacket.Size)));
            records.Add(new byte[] { (byte)ReplayRecordKind.Perspective, client.IsObserver ? byte.MaxValue : client.Accepted.Slot });
            BinaryPrimitives.WriteUInt32LittleEndian(body, _match);
            int count = SessionRosterPacket.Write(body.AsSpan(4), client.RosterRevision, client.Roster);
            records.Add(Record(ReplayRecordKind.Roster, body.AsSpan(0, count + 4)));
            count = client.Snapshot.Write(body, client.SnapshotPlayers);
            records.Add(Record(ReplayRecordKind.Snapshot, body.AsSpan(0, count)));
            for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
            {
                count = WorldPacket.Write(body, _match, _world.Revision, _world.ServerTick, _world.Records, offset);
                records.Add(Record(ReplayRecordKind.World, body.AsSpan(0, count)));
            }
            BinaryPrimitives.WriteUInt64LittleEndian(body, presentation.World.FrameCount);
            BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(8), presentation.World.LiveFrames);
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(16), presentation.World.ElapsedTime);
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(20), presentation.World.GlobalElapsedTime);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(24), presentation.World.Random.Rng1);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(28), presentation.World.Random.Rng2);
            records.Add(Record(ReplayRecordKind.Clock, body.AsSpan(0, 32)));
            AddFragments(records, body, ReplayRecordKind.Presentation,
                MphRead.Combat.ReplayFeedbackState.Capture(presentation.CombatFeedback, presentation.WorldFeedback));
            AddFragments(records, body, ReplayRecordKind.ChatState, Chat.ChatBox.CaptureReplay());
            return records;
        }
        private static void AddFragments(List<byte[]> records, byte[] body, ReplayRecordKind kind, byte[] feedback)
        {
            const int fragmentSize = NetConfig.MaxPacketSize - 9;
            int fragments = (feedback.Length + fragmentSize - 1) / fragmentSize;
            for (int fragment = 0; fragment < fragments; fragment++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)fragment);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), (ushort)fragments);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), feedback.Length);
                int length = Math.Min(fragmentSize, feedback.Length - fragment * fragmentSize);
                feedback.AsSpan(fragment * fragmentSize, length).CopyTo(body.AsSpan(8));
                records.Add(Record(kind, body.AsSpan(0, length + 8)));
            }
        }
        private static byte[] Record(ReplayRecordKind kind, ReadOnlySpan<byte> body)
        {
            byte[] record = new byte[body.Length + 1]; record[0] = (byte)kind; body.CopyTo(record.AsSpan(1)); return record;
        }

        /// <summary>Cache complete worlds even before recording starts, so the opening frame has a baseline.</summary>
        internal static void RecordWorld(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < WorldPacket.HeaderSize) { return; }
            uint match = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            if (!WorldPacket.TryValidate(payload, match)) { return; }
            if (_world.MatchId != match) { _world.Reset(match); }
            _world.Receive(payload);
        }

        internal static void RecordEvent(in NetApplicationEvent message)
        {
            if (message.MatchId != _match
                || message.Type is not (ReliableEventType.Combat or ReliableEventType.Chat or ReliableEventType.Kill or ReliableEventType.WorldEvent or ReliableEventType.MatchAward or ReliableEventType.MatchSemantic)) { return; }
            Span<byte> body = stackalloc byte[5 + message.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(body, message.MatchId);
            body[4] = (byte)message.Type;
            message.Payload.Span.CopyTo(body[5..]);
            ReplayMarker marker = MarkerFor(message);
            Span<byte> record = stackalloc byte[body.Length + 1];
            record[0] = (byte)ReplayRecordKind.Event;
            body.CopyTo(record[1..]);
            if (!ReplayTimelineTickReader.TryRead(record, 0, out uint serverTick)) return;
            Write(ReplayRecordKind.Event, body, serverTick, marker);
        }

        internal static ReplayMarker MarkerFor(in NetApplicationEvent message,
            byte protocol = NetHeader.Version) => _markers.ForEvent(message, protocol);

        private static void Write(ReplayRecordKind kind, ReadOnlySpan<byte> payload, uint serverTick,
            ReplayMarker marker = ReplayMarker.None)
        {
            if (payload.Length >= NetConfig.MaxPacketSize)
            { throw new ArgumentOutOfRangeException(nameof(payload)); }
            Span<byte> record = stackalloc byte[payload.Length + 1];
            record[0] = (byte)kind;
            payload.CopyTo(record[1..]);
            CaptureTimelineRecord(_frame, serverTick, record, marker);
            if (_writer == null) return;
            try { _writer.WriteRecord(WriterFrame, record, marker); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[replay] recording stopped: {LastError}");
                Stop();
            }
        }

        private static uint WriterFrame => unchecked(_frame - _writerOriginFrame);
        internal static byte CaptureProtocolVersion => _writer?.ProtocolVersion ?? NetHeader.Version;

        internal static bool CaptureTimelineRecord(uint recordingFrame, uint serverTick,
            ReadOnlySpan<byte> record, ReplayMarker marker = ReplayMarker.None)
            => _timeline.Append(new ReplayTimelineRecord(recordingFrame, serverTick, record, marker));

        private static uint CurrentServerTick(NetClient client)
        {
            if (client.HasSnapshot && client.Snapshot.MatchId == client.Accepted.MatchId)
                return client.Snapshot.ServerTick;
            if (_world.HasState && _world.MatchId == client.Accepted.MatchId)
                return _world.ServerTick;
            return client.Accepted.ServerTick;
        }

        private static void WriteOpeningFrame(NetClient client)
        {
            if (_writer == null) return;
            Span<byte> body = stackalloc byte[NetConfig.MaxPacketSize - 1];
            new MatchTransitionPacket(client.Accepted.MatchId, client.Accepted.ServerTick,
                client.Accepted.Rules).Write(body[..MatchTransitionPacket.Size]);
            WriteFileRecord(ReplayRecordKind.Match, body[..MatchTransitionPacket.Size]);
            if (client.HasRoster)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(body, client.Accepted.MatchId);
                int count = SessionRosterPacket.Write(body[4..], client.RosterRevision, client.Roster);
                WriteFileRecord(ReplayRecordKind.Roster, body[..(count + 4)]);
            }
            if (client.HasSnapshot)
            {
                int count = client.Snapshot.Write(body, client.SnapshotPlayers);
                WriteFileRecord(ReplayRecordKind.Snapshot, body[..count]);
            }
            if (_world.HasState && _world.MatchId == client.Accepted.MatchId)
            {
                for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
                {
                    int count = WorldPacket.Write(body, _world.MatchId, _world.Revision,
                        _world.ServerTick, _world.Records, offset);
                    WriteFileRecord(ReplayRecordKind.World, body[..count]);
                }
            }
        }

        private static void WriteFileRecord(ReplayRecordKind kind, ReadOnlySpan<byte> payload)
        {
            if (_writer == null) return;
            Span<byte> record = stackalloc byte[payload.Length + 1];
            record[0] = (byte)kind; payload.CopyTo(record[1..]);
            _writer.WriteRecord(0, record);
        }

        internal static void ResetTimelineForSession()
        {
            Stop();
            _timeline.Reset();
            _world.Reset(0);
            _frame = uint.MaxValue;
            _writerOriginFrame = 0;
            _match = _rosterRevision = _worldRevision = 0;
            _snapshotCount = -1;
            _hasRoster = _hasWorld = _hasTimelineRestore = _hasWriterKeyframe = false;
            _markers.Reset();
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) { name = name.Replace(c, '_'); }
            return name;
        }
    }
}
