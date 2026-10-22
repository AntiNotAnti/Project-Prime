using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal static partial class ReplayCapture
{
    internal static void AcceptBombEvent(BombEvent fact)
    {
        if (DemoPlayback.IsActive) return;
        Recorder.AcceptBombEvent(fact, NetSession.NetFrame);
    }
    internal static void AcceptBombState(ReadOnlySpan<byte> payload)
    {
        if (DemoPlayback.IsActive) return;
        Recorder.AcceptBombState(payload, NetSession.NetFrame);
    }
}

internal sealed partial class ReplayRecorder
{
    private readonly BombStateReceiver _bombSeedReceiver = new();
    private readonly List<ReplayTimelineRecord> _bombSeed = new();
    private uint? _bombSeedPageFrame;
    private int _bombSeedPageMask;
    private void ResetBombFacts()
    {
        foreach (var record in _bombSeed) record.Release();
        _bombSeed.Clear(); _bombSeedReceiver.Reset();
        _bombSeedPageFrame = null; _bombSeedPageMask = 0;
    }
    internal void SeedBombFacts(Action<ReplayTimelineRecord> accept)
    { foreach (var record in _bombSeed) accept(record); }
    private void RetainBombFact(ReplayTimelineRecord record)
    {
        if (_bombSeed.Count >= 8192)
            throw new InvalidDataException("Replay bomb baseline exceeded its pending budget.");
        record.Retain(); _bombSeed.Add(record);
    }
    internal void AcceptBombEvent(BombEvent fact, uint frame)
    {
        if (!HasMatch || fact.State.Key.MatchId != _matchId || fact.State.Key.AuthorityEpoch != _epoch) return;
        Span<byte> packet = stackalloc byte[1 + BombEvent.Size];
        packet[0] = (byte)PacketType.BombEvent; fact.Write(packet[1..]);
        if (!BombEvent.TryRead(packet[1..], out _)) return;
        var record = new ReplayTimelineRecord(frame, fact.Frame, ReplayFactKind.Presentation, packet);
        try { RetainBombFact(record); Publish(record); } finally { record.Release(); }
    }
    internal void AcceptBombState(ReadOnlySpan<byte> payload, uint frame)
    {
        if (!HasMatch || !BombStatePage.TryRead(payload, out var page)
            || page!.MatchId != _matchId || page.Epoch != _epoch || !_bombSeedReceiver.Accept(page)) return;
        if (_bombSeedPageFrame != page.Frame)
        { _bombSeedPageFrame = page.Frame; _bombSeedPageMask = 0; }
        _bombSeedPageMask |= 1 << page.Page;
        byte[] packet = new byte[1 + payload.Length];
        packet[0] = (byte)PacketType.BombState; payload.CopyTo(packet.AsSpan(1));
        var record = new ReplayTimelineRecord(frame, page.Frame, ReplayFactKind.Presentation, packet);
        try
        {
            RetainBombFact(record); Publish(record);
            // Once complete, replace historical creation/terminal facts with the
            // same immutable baseline and retain only events beyond its frontier.
            if (_bombSeedPageMask == (1 << BombStatePage.Pages) - 1
                && _bombSeedReceiver.Frame == page.Frame && _bombSeedReceiver.Frontier == page.Frontier)
            {
                var future = new List<ReplayTimelineRecord>();
                foreach (var old in _bombSeed)
                {
                    if (old.Payload[0] == (byte)PacketType.BombEvent
                        && BombEvent.TryRead(old.Payload[1..], out var fact) && fact.Sequence > page.Frontier)
                        future.Add(old);
                    else old.Release();
                }
                _bombSeed.Clear();
                foreach (var bytes in BombStatePage.PackSnapshot(_matchId, _epoch, page.Frame,
                    page.Frontier, _bombSeedReceiver.States))
                {
                    byte[] baseline = new byte[bytes.Length + 1]; baseline[0] = (byte)PacketType.BombState;
                    bytes.CopyTo(baseline, 1);
                    _bombSeed.Add(new(frame, page.Frame, ReplayFactKind.Presentation, baseline));
                }
                _bombSeed.AddRange(future);
            }
        }
        finally { record.Release(); }
    }
}

internal sealed partial class ReplayReplicaState
{
    // Kept across Advance: a decoder may receive several ticks before a scene
    // exists, or before its next simulation step actually applies the packets.
    private readonly List<byte[]> _bombPackets = new();
    internal int PendingBombPacketCount => _bombPackets.Count;
    private bool AcceptBombPacket(PacketType type, ReadOnlySpan<byte> payload)
    {
        bool matching;
        if (type == PacketType.BombEvent)
        {
            if (!BombEvent.TryRead(payload, out var fact)) throw Malformed();
            matching = Matches(fact.State.Key.MatchId, fact.State.Key.AuthorityEpoch);
        }
        else
        {
            if (!BombStatePage.TryRead(payload, out var page)) throw Malformed();
            matching = Matches(page!.MatchId, page.Epoch);
        }
        if (!matching) return false;
        if (_bombPackets.Count >= 2048) throw new InvalidDataException("Replay bomb packet queue exceeded its budget.");
        byte[] bytes = new byte[payload.Length + 1]; bytes[0] = (byte)type; payload.CopyTo(bytes.AsSpan(1));
        _bombPackets.Add(bytes); return true;
    }
    internal void PrepareBombWorld(Scene scene)
    {
        if (_bombPackets.Count > 0 && Match is { } match)
            scene.Bombs.Begin(match.MatchId, match.AuthorityEpoch);
    }
    internal void ApplyBombPackets(Scene scene)
    {
        if (_bombPackets.Count == 0 || Match is not { } match) return;
        scene.Bombs.Begin(match.MatchId, match.AuthorityEpoch);
        foreach (byte[] packet in _bombPackets)
        {
            if (packet[0] == (byte)PacketType.BombEvent)
            {
                if (!BombEvent.TryRead(packet.AsSpan(1), out var fact)) throw Malformed();
                scene.Bombs.ReceiveEvent(fact);
            }
            else scene.Bombs.ReceiveState(packet.AsSpan(1));
        }
        _bombPackets.Clear();
    }
    private void WriteBombPackets(BinaryWriter writer)
    {
        writer.Write(_bombPackets.Count);
        foreach (byte[] packet in _bombPackets) { writer.Write(packet.Length); writer.Write(packet); }
    }
    private void ReadBombPackets(BinaryReader reader)
    {
        int count = reader.ReadInt32(); if (count < 0 || count > 2048) throw Malformed();
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > 1200) throw Malformed();
            byte[] packet = reader.ReadBytes(length);
            if (packet.Length != length || packet[0] != (byte)PacketType.BombEvent
                && packet[0] != (byte)PacketType.BombState || !AcceptBombPacket((PacketType)packet[0], packet.AsSpan(1)))
                throw Malformed();
        }
    }
}
