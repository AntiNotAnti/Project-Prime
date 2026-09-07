using System;
using System.Buffers.Binary;
using System.IO;

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
            try { _writer = new DemoWriter(path, NetHeader.Version); }
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
            _hasRoster = _hasWorld = false;
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
            catch (IOException ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"[demo] could not finish recording: {LastError}");
            }
        }

        internal static void RecordFrame(NetClient client)
        {
            if (_writer == null || client.Accepted.MatchId == 0) { return; }
            _frame = unchecked(_frame + 1);
            Span<byte> body = stackalloc byte[NetConfig.MaxPacketSize - 1];
            if (_match != client.Accepted.MatchId)
            {
                _match = client.Accepted.MatchId;
                _hasRoster = _hasWorld = false;
                _snapshotCount = -1;
                new MatchTransitionPacket(_match, client.Accepted.ServerTick, client.Accepted.Mode, client.Accepted.Room).Write(body);
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
                for (int offset = 0; offset < _world.Count; offset += WorldPacket.RecordsPerBatch)
                {
                    int count = WorldPacket.Write(body, _match, _world.Revision, _world.ServerTick, _world.Records, offset);
                    Write(DemoRecordKind.World, body[..count]);
                }
                _worldRevision = _world.Revision;
                _hasWorld = true;
            }
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
                || message.Type is not (ReliableEventType.Combat or ReliableEventType.Chat)) { return; }
            Span<byte> body = stackalloc byte[5 + message.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(body, message.MatchId);
            body[4] = (byte)message.Type;
            message.Payload.Span.CopyTo(body[5..]);
            Write(DemoRecordKind.Event, body);
        }

        private static void Write(DemoRecordKind kind, ReadOnlySpan<byte> payload)
        {
            if (_writer == null) { return; }
            if (payload.Length >= NetConfig.MaxPacketSize)
            { throw new ArgumentOutOfRangeException(nameof(payload)); }
            Span<byte> record = stackalloc byte[payload.Length + 1];
            record[0] = (byte)kind;
            payload.CopyTo(record[1..]);
            try { _writer.WriteRecord(_frame, record); }
            catch (IOException ex)
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
