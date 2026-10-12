using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

internal static class ReplayPickupPrediction
{
    internal const int Size = PickupClaim.Size + sizeof(uint);
    internal static bool TryRead(ReadOnlySpan<byte> payload, out PickupClaim claim, out uint localFrame)
    {
        claim = default; localFrame = 0;
        if (payload.Length != Size || !PickupClaim.TryRead(payload[..PickupClaim.Size], out claim)) return false;
        localFrame = BinaryPrimitives.ReadUInt32LittleEndian(payload[PickupClaim.Size..]); return true;
    }
}
internal static class ReplayGrantBootstrap
{
    internal const int MaximumStateBytes = 32768;
    internal static bool TryRead(ReadOnlySpan<byte> payload, out uint originalFrame, out TimedGrantPresentation state)
    {
        originalFrame = 0; state = new();
        if (payload.Length < 4 + 14 || payload.Length > 4 + MaximumStateBytes) return false;
        try
        {
            originalFrame = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            using var stream = new MemoryStream(payload[4..].ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            state = TimedGrantPresentation.Read(reader);
            return state.HasCoverage && stream.Position == stream.Length;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
        { state = new(); return false; }
    }
}
internal static partial class ReplayCapture
{
    internal static void AcceptTimedGrant(TimedGrantFact fact)
    { if (!DemoPlayback.IsActive) Recorder.AcceptTimedGrant(fact, NetSession.NetFrame); }
    internal static void AcceptPickupPrediction(PickupClaim claim, uint localFrame)
    { if (!DemoPlayback.IsActive) Recorder.AcceptPickupPrediction(claim, localFrame, NetSession.NetFrame); }
}
internal sealed partial class ReplayRecorder
{
    private readonly TimedGrantPresentation _grantSeed = new();
    private uint _grantSeedLatestFrame;
    internal void SeedGrantFacts(uint frame, Action<ReplayTimelineRecord> accept)
    {
        SeedPickupItems(frame, accept);
        _grantSeed.Advance(frame);
        if (!_grantSeed.HasCoverage) return;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)PacketType.TimedGrantBootstrap); writer.Write(frame); _grantSeed.Write(writer); writer.Flush();
        if (stream.Length > 1 + 4 + ReplayGrantBootstrap.MaximumStateBytes)
            throw new InvalidDataException("Replay grant bootstrap exceeds its byte budget.");
        var record = new ReplayTimelineRecord(frame, _grantSeed.AuthorityNow(frame), ReplayFactKind.Presentation,
            stream.GetBuffer().AsSpan(0, (int)stream.Length));
        try { accept(record); } finally { record.Release(); }
    }
    private void ObserveGrantSeedFrame(uint frame)
    { if (NetLifecycleTracker.Newer(frame, _grantSeedLatestFrame)) _grantSeedLatestFrame = frame; }

    internal void AcceptTimedGrant(TimedGrantFact fact, uint frame)
    {
        if (!fact.IsValid || !HasMatch || fact.Life.MatchId != _matchId || fact.Life.AuthorityEpoch != _epoch) return;
        if (!ObservePickupSeedRoom(fact.Room)) return;
        _grantSeed.Accept(fact, frame); ObserveGrantSeedFrame(frame);
        Span<byte> packet = stackalloc byte[1 + TimedGrantFact.Size]; packet[0] = (byte)PacketType.TimedGrant;
        fact.Write(packet[1..]); PublishTransient(new(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, packet));
    }
    internal void AcceptPickupPrediction(PickupClaim claim, uint localFrame, uint frame)
    {
        if (!claim.IsValid || !HasMatch || claim.Key.Match != _matchId || claim.Key.Epoch != _epoch) return;
        if (!ObservePickupSeedRoom(claim.Spawn.Room)) return;
        _grantSeed.Predict(claim, frame); ObserveGrantSeedFrame(frame);
        Span<byte> packet = stackalloc byte[1 + ReplayPickupPrediction.Size]; packet[0] = (byte)PacketType.PickupPrediction;
        claim.Write(packet.Slice(1, PickupClaim.Size));
        BinaryPrimitives.WriteUInt32LittleEndian(packet[(1 + PickupClaim.Size)..], localFrame);
        PublishTransient(new(frame, Timeline.LastServerTick ?? frame, ReplayFactKind.Presentation, packet));
    }
}
internal sealed partial class ReplayReplicaState
{
    internal TimedGrantPresentation TimedGrants { get; private set; } = new();
    private bool AcceptGrantBootstrap(ReadOnlySpan<byte> payload, uint frame)
    {
        if (!ReplayGrantBootstrap.TryRead(payload, out uint originalFrame, out var state)) throw Malformed();
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(payload[4..]);
        ushort match = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        if (!Matches(match, epoch)) return false;
        state.Rebase(originalFrame, frame); TimedGrants = state; return true;
    }
    private bool AcceptTimedGrant(ReadOnlySpan<byte> payload, uint frame)
    {
        if (!TimedGrantFact.TryRead(payload, out var fact)) throw Malformed();
        return Matches(fact.Life.MatchId, fact.Life.AuthorityEpoch) && TimedGrants.Accept(fact, frame);
    }
    private bool AcceptPickupPrediction(ReadOnlySpan<byte> payload, uint frame)
    {
        if (!ReplayPickupPrediction.TryRead(payload, out var claim, out _)) throw Malformed();
        // Capture frames belong to the original producer. A clip can rebase the
        // timeline; projection uses this consumer's arrival frame consistently.
        return Matches(claim.Key.Match, claim.Key.Epoch) && TimedGrants.Predict(claim, frame);
    }
    private void WriteGrantFacts(BinaryWriter writer)
    {
        using var stream = new MemoryStream(); using var data = new BinaryWriter(stream);
        TimedGrants.Write(data); data.Flush();
        if (stream.Length > 32768) throw new InvalidDataException("Replay grant state exceeds its byte budget.");
        writer.Write((int)stream.Length); writer.Write(stream.GetBuffer().AsSpan(0, (int)stream.Length));
    }
    private void ReadGrantFacts(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 14 || length > 32768) throw Malformed();
        byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw Malformed();
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        ushort match = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        uint room = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10));
        if (room != 0 && !Matches(match, epoch)) throw Malformed();
        using var stream = new MemoryStream(bytes, writable: false); using var data = new BinaryReader(stream);
        TimedGrants = TimedGrantPresentation.Read(data);
        if (stream.Position != stream.Length) throw Malformed();
    }
}
