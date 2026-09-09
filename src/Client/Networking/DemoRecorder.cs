using System;
using System.Buffers.Binary;
using System.IO;
using System.Collections.Generic;

namespace MphRead.Mods.Network
{
    /// <summary>Records accepted server facts at presentation frames, independently of connection traffic.</summary>
    internal static class DemoRecorder
    {
        private static DemoWriter? _writer;
        private static readonly ClientWorldState _world = new();
        private static uint _frame;
        private static uint _match;
        private static uint _rosterRevision;
        private static uint _worldRevision;
        private static long _snapshotCount;
        private static bool _hasRoster;
        private static bool _hasWorld;
        private static bool _hasKeyframe;
        private static uint _keyframeFrame;
        private static readonly ReplayEventIndexer _markers = new();
        public static bool IsRecording => _writer != null;
        public static string? CurrentPath { get; private set; }
        public static string? LastError { get; private set; }

        public static bool Start()
        {
            LastError = null;
            if (IsRecording || DemoPlayback.IsActive || AuthoritativePlay.Current is not { } play
                || play.Client.Accepted.MatchId == 0)
            {
                LastError = "Join a match before recording a demo.";
                return false;
            }
            string room = SanitizeFileName(play.Client.Accepted.Room);
            string fileName = $"{room}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{DemoFile.Extension}";
            string path = Paths.Combine(Paths.Export, "_demos", fileName);
            return Start(path, play.Client);
        }

        internal static bool Start(string path, NetClient client)
        {
            if (_writer != null || client.Accepted.MatchId == 0) { return false; }
            LastError = null;
            try { _writer = new DemoWriter(path, NetHeader.Version, indexed: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[demo] could not start recording: {LastError}");
                return false;
            }
            CurrentPath = path;
            _frame = uint.MaxValue;
            _match = _rosterRevision = _worldRevision = 0;
            _snapshotCount = -1;
            _hasRoster = _hasWorld = _hasKeyframe = false;
            _markers.Reset();
            RecordFrame(client);
            return IsRecording;
        }

        public static void Stop()
        {
            DemoWriter? writer = _writer;
            _writer = null;
            CurrentPath = null;
            _world.Reset(0);
            try { writer?.Dispose(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[demo] could not finish recording: {LastError}");
            }
        }

        internal static void RecordFrame(NetClient client, Scene? scene = null)
        {
            if (_writer == null || client.Accepted.MatchId == 0) { return; }
            _frame = unchecked(_frame + 1);
            Span<byte> body = stackalloc byte[NetConfig.MaxPacketSize - 1];
            if (_match != client.Accepted.MatchId)
            {
                _match = client.Accepted.MatchId;
                _hasRoster = _hasWorld = _hasKeyframe = false;
                _markers.Reset();
                _snapshotCount = -1;
                new MatchTransitionPacket(_match, client.Accepted.ServerTick, client.Accepted.Rules).Write(body[..MatchTransitionPacket.Size]);
                Write(DemoRecordKind.Match, body[..MatchTransitionPacket.Size]);
            }
            if (client.HasRoster && (!_hasRoster || _rosterRevision != client.RosterRevision))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(body, _match);
                int count = SessionRosterPacket.Write(body[4..], client.RosterRevision, client.Roster);
                Write(DemoRecordKind.Roster, body[..(count + 4)]);
                _rosterRevision = client.RosterRevision;
                _hasRoster = true;
            }
            if (client.HasSnapshot && _snapshotCount != client.SnapshotsReceived)
            {
                int count = client.Snapshot.Write(body, client.SnapshotPlayers);
                Write(DemoRecordKind.Snapshot, body[..count]);
                _snapshotCount = client.SnapshotsReceived;
            }
            if (_world.HasState && _world.MatchId == _match && (!_hasWorld || _worldRevision != _world.Revision))
            {
                ReplayMarker marker = ReplayMarker.None;
                foreach (WorldRecord state in _world.Records)
                    if (state.Kind == WorldRecordKind.Match && state.E != 0)
                    {
                        marker = _markers.ForTerminalWorld(_writer.ProtocolVersion, terminal: true);
                        break;
                    }
                for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
                {
                    int count = WorldPacket.Write(body, _match, _world.Revision, _world.ServerTick, _world.Records, offset);
                    Write(DemoRecordKind.World, body[..count], offset == 0 ? marker : ReplayMarker.None);
                }
                _worldRevision = _world.Revision;
                _hasWorld = true;
            }
            if (_writer != null && client.HasRoster && client.HasSnapshot && _world.HasState && _world.MatchId == _match
                && scene?.Presentation is ScenePresentation presentation
                && (!_hasKeyframe || _frame - _keyframeFrame >= 300))
            {
                try
                {
                    List<byte[]> checkpoint = BuildKeyframe(client, presentation);
                    // The first checkpoint also seeds sequential playback's clocks and recorded
                    // local feedback perspective when recording begins in the middle of a match.
                    if (!_hasKeyframe)
                        foreach (byte[] record in checkpoint)
                            if ((DemoRecordKind)record[0] is DemoRecordKind.Clock or DemoRecordKind.Presentation or DemoRecordKind.ChatState)
                                _writer.WriteRecord(_frame, record);
                    _writer.WriteKeyframe(_frame, checkpoint);
                    _hasKeyframe = true; _keyframeFrame = _frame;
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
            records.Add(Record(DemoRecordKind.Match, body.AsSpan(0, MatchTransitionPacket.Size)));
            records.Add(new byte[] { (byte)DemoRecordKind.Perspective, client.IsObserver ? byte.MaxValue : client.Accepted.Slot });
            BinaryPrimitives.WriteUInt32LittleEndian(body, _match);
            int count = SessionRosterPacket.Write(body.AsSpan(4), client.RosterRevision, client.Roster);
            records.Add(Record(DemoRecordKind.Roster, body.AsSpan(0, count + 4)));
            count = client.Snapshot.Write(body, client.SnapshotPlayers);
            records.Add(Record(DemoRecordKind.Snapshot, body.AsSpan(0, count)));
            for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
            {
                count = WorldPacket.Write(body, _match, _world.Revision, _world.ServerTick, _world.Records, offset);
                records.Add(Record(DemoRecordKind.World, body.AsSpan(0, count)));
            }
            BinaryPrimitives.WriteUInt64LittleEndian(body, presentation.World.FrameCount);
            BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(8), presentation.World.LiveFrames);
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(16), presentation.World.ElapsedTime);
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(20), presentation.World.GlobalElapsedTime);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(24), presentation.World.Random.Rng1);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(28), presentation.World.Random.Rng2);
            records.Add(Record(DemoRecordKind.Clock, body.AsSpan(0, 32)));
            AddFragments(records, body, DemoRecordKind.Presentation,
                MphRead.Combat.ReplayFeedbackState.Capture(presentation.CombatFeedback, presentation.WorldFeedback));
            AddFragments(records, body, DemoRecordKind.ChatState, Chat.ChatBox.CaptureReplay());
            return records;
        }
        private static void AddFragments(List<byte[]> records, byte[] body, DemoRecordKind kind, byte[] feedback)
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
        private static byte[] Record(DemoRecordKind kind, ReadOnlySpan<byte> body)
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
            if (_writer == null || message.MatchId != _match
                || message.Type is not (ReliableEventType.Combat or ReliableEventType.Chat or ReliableEventType.Kill or ReliableEventType.WorldEvent or ReliableEventType.MatchAward or ReliableEventType.MatchSemantic)) { return; }
            Span<byte> body = stackalloc byte[5 + message.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(body, message.MatchId);
            body[4] = (byte)message.Type;
            message.Payload.Span.CopyTo(body[5..]);
            ReplayMarker marker = MarkerFor(message);
            Write(DemoRecordKind.Event, body, marker);
        }

        internal static ReplayMarker MarkerFor(in NetApplicationEvent message,
            byte protocol = NetHeader.Version) => _markers.ForEvent(message, protocol);

        private static void Write(DemoRecordKind kind, ReadOnlySpan<byte> payload, ReplayMarker marker = ReplayMarker.None)
        {
            if (_writer == null) { return; }
            if (payload.Length >= NetConfig.MaxPacketSize)
            { throw new ArgumentOutOfRangeException(nameof(payload)); }
            Span<byte> record = stackalloc byte[payload.Length + 1];
            record[0] = (byte)kind;
            payload.CopyTo(record[1..]);
            try { _writer.WriteRecord(_frame, record, marker); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                LastError = ex.Message;
                Console.WriteLine($"[demo] recording stopped: {LastError}");
                Stop();
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) { name = name.Replace(c, '_'); }
            return name;
        }
    }
}
