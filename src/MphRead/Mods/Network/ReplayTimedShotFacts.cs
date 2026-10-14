using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

internal static class ReplayTimedShotRecording
{
    // Independent replay opt-in. Ordinary NET-01 Shadow preserves canonical
    // recording bytes; controlled fixtures can override this internal value.
    internal static bool Enabled { get; set; }
        = Environment.GetEnvironmentVariable("PRIME_TIMED_REPLAY_FACTS") == "1";
}

// File-only producer capability/seed scope. Tag251 is unused by live PacketType
// and by existing replay construction/semantic/quiet-frame records.
internal readonly record struct ReplayTimedShotScope(ulong Epoch, ushort Match, uint Room,
    TimedGrantProofMode Mode, uint AuthorityFrame)
{
    internal const byte Tag = 251, Version = 1;
    internal const int Size = 21;
    internal bool IsValid => Epoch != 0 && Match != 0 && Mode is TimedGrantProofMode.Shadow or TimedGrantProofMode.Enforce;
    internal byte[] Packet()
    {
        if (!IsValid) throw new InvalidDataException("Invalid timed shot producer scope.");
        byte[] bytes = new byte[Size]; bytes[0] = Tag; bytes[1] = Version;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2), Epoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), Match);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), Room); bytes[16] = (byte)Mode;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(17), AuthorityFrame); return bytes;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReplayTimedShotScope scope)
    {
        scope = default;
        if (bytes.Length != Size || bytes[0] != Tag || bytes[1] != Version) return false;
        var value = new ReplayTimedShotScope(BinaryPrimitives.ReadUInt64LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            (TimedGrantProofMode)bytes[16], BinaryPrimitives.ReadUInt32LittleEndian(bytes[17..]));
        if (!value.IsValid) return false; scope = value; return true;
    }
}

internal static partial class ReplayCapture
{
    // The sole live boundary. Private decode, seek, export and copy have no
    // live session lookup and never re-evaluate source/grant ownership.
    private static void EnableTimedShotCertificates()
    {
        if (DemoPlayback.IsActive || !ReplayTimedShotRecording.Enabled || !NetTimedShotProof.Active || !Recorder.HasMatch) return;
        uint room = NetPowerUpLedger.Ledger?.Room ?? 0;
        if (!Recorder.EnableTimedShotCertificates(room, NetTimedShotProof.Mode, NetSession.NetFrame)) return;
        NetTimedShotProof.VisitRetained(room, NetSession.NetFrame, value =>
            Recorder.AcceptTimedShotCertificate(ReplayTimedShotFact.From(value, value.CaptureMode), NetSession.NetFrame));
    }
    internal static void AcceptedTimedShotCertificate(in TimedShotCertificate value)
    {
        if (DemoPlayback.IsActive || !ReplayTimedShotRecording.Enabled || !NetTimedShotProof.Active || value.Key.AuthorityEpoch != NetSession.AuthorityEpoch
            || value.Key.MatchId != NetSession.CurrentMatchId) return;
        Recorder.EnableTimedShotCertificates(value.Room, NetTimedShotProof.Mode, NetSession.NetFrame);
        Recorder.AcceptTimedShotCertificate(ReplayTimedShotFact.From(value, value.CaptureMode), NetSession.NetFrame);
    }
}

internal sealed partial class ReplayRecorder
{
    private readonly ReplayTimedShotState _timedShots = new();
    internal bool HasTimedShotCertificates => _timedShots.Enabled;
    // True means the scope was newly enabled, so only that transition needs a
    // retained-store seed. Ordinary frame capture never rescans the live store.
    internal bool EnableTimedShotCertificates(uint room, TimedGrantProofMode mode, uint frame)
    {
        if (!HasMatch) return false;
        if (_timedShots.Enabled && _timedShots.Epoch == _epoch
            && _timedShots.Match == _matchId && _timedShots.Room == room)
        { _timedShots.AdvanceAuthority(frame); return false; }
        if (!_timedShots.Begin(_epoch, _matchId, room, mode)) return false;
        _timedShots.AdvanceAuthority(frame);
        PublishTransient(new(frame, frame, ReplayFactKind.Presentation,
            new ReplayTimedShotScope(_epoch, _matchId, room, mode, frame).Packet()));
        return true;
    }
    internal bool AcceptTimedShotCertificate(in ReplayTimedShotFact fact, uint frame)
    {
        uint now = _timedShots.Enabled && NetLifecycleTracker.Newer(_timedShots.AuthorityFrame, fact.AuthorityFrame)
            ? _timedShots.AuthorityFrame : fact.AuthorityFrame;
        if (!HasMatch || fact.Key.AuthorityEpoch != _epoch || fact.Key.MatchId != _matchId
            || _timedShots.Remember(fact, now) != TimedShotRetention.Stored) return false;
        PublishTransient(new(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, fact.Packet())); return true;
    }
    private void SeedTimedShotCertificates(uint frame, Action<ReplayTimelineRecord> accept)
    {
        if (!_timedShots.Enabled) return;
        var scope = new ReplayTimedShotScope(_timedShots.Epoch, _timedShots.Match, _timedShots.Room,
            _timedShots.CaptureMode, _timedShots.AuthorityFrame);
        var begin = new ReplayTimelineRecord(frame, scope.AuthorityFrame, ReplayFactKind.Presentation, scope.Packet());
        try { accept(begin); } finally { begin.Release(); }
        _timedShots.Visit(fact =>
        {
            var record = new ReplayTimelineRecord(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, fact.Packet());
            try { accept(record); } finally { record.Release(); }
        });
    }
}

internal sealed partial class ReplayReplicaState
{
    internal ReplayTimedShotState TimedShots { get; private set; } = new();
    private bool AcceptTimedShotRecord(ReadOnlySpan<byte> packet)
    {
        if (packet[0] == ReplayTimedShotScope.Tag)
        {
            if (!ReplayTimedShotScope.TryRead(packet, out var scope)) throw Malformed();
            if (!Matches(scope.Match, scope.Epoch)) return false;
            if (!TimedShots.Begin(scope.Epoch, scope.Match, scope.Room, scope.Mode)) return false;
            uint now = _hasSnapshot && NetLifecycleTracker.Newer(ServerTick, scope.AuthorityFrame)
                ? ServerTick : scope.AuthorityFrame;
            TimedShots.AdvanceAuthority(now); return true;
        }
        if (!ReplayTimedShotFact.TryRead(packet, out var fact)) throw Malformed();
        if (!Matches(fact.Key.MatchId, fact.Key.AuthorityEpoch)) return false;
        uint now = TimedShots.Enabled && NetLifecycleTracker.Newer(TimedShots.AuthorityFrame, fact.AuthorityFrame)
            ? TimedShots.AuthorityFrame : fact.AuthorityFrame;
        return TimedShots.Remember(fact, now) == TimedShotRetention.Stored;
    }
    private void WriteTimedShotCertificates(BinaryWriter writer)
    {
        using var stream = new MemoryStream(); using var data = new BinaryWriter(stream);
        TimedShots.Write(data); data.Flush();
        writer.Write(checked((int)stream.Length)); writer.Write(stream.GetBuffer().AsSpan(0, (int)stream.Length));
    }
    private void ReadTimedShotCertificates(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 24 || count > 24 + ReplayTimedShotState.MaximumFacts * ReplayTimedShotFact.Size) throw Malformed();
        byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw Malformed();
        using var stream = new MemoryStream(bytes, writable: false); using var data = new BinaryReader(stream);
        var state = ReplayTimedShotState.Read(data);
        if (!Matches(state.Match, state.Epoch) || stream.Position != stream.Length) throw Malformed();
        TimedShots = state;
    }
}
