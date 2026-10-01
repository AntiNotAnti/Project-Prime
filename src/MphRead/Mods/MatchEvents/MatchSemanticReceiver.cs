using System;
using System.Collections.Generic;
using System.IO;
namespace MphRead.Mods.MatchEvents;
/// <summary>Passive authority facts; accepts bounded reordering without deriving gameplay or awards.</summary>
internal sealed class MatchSemanticReceiver
{
    internal const int Window = 1024;
    private readonly uint[] _eventIds = new uint[Window], _awardIds = new uint[Window];
    private readonly Queue<MatchSemanticEventPacket> _events = new();
    private readonly Queue<MatchAwardPacket> _awards = new();
    private uint _latestEvent, _latestAward;
    internal ushort MatchId { get; private set; }
    internal ulong Epoch { get; private set; }
    internal IReadOnlyCollection<MatchSemanticEventPacket> Events => _events;
    internal IReadOnlyCollection<MatchAwardPacket> Awards => _awards;
    internal void Begin(ushort match, ulong epoch)
    {
        if (match == MatchId && epoch == Epoch) return;
        MatchId = match; Epoch = epoch; Array.Clear(_eventIds); Array.Clear(_awardIds);
        _latestEvent = _latestAward = 0; _events.Clear(); _awards.Clear();
    }
    internal bool Accept(in MatchSemanticEventPacket packet)
    {
        if (!packet.Validate() || packet.MatchId != MatchId || packet.AuthorityEpoch != Epoch
            || !Observe(packet.EventId, _eventIds, ref _latestEvent)) return false;
        if (_events.Count == Window) _events.Dequeue();
        _events.Enqueue(packet); return true;
    }
    internal bool Accept(in MatchAwardPacket packet)
    {
        if (!packet.Validate() || packet.MatchId != MatchId || packet.AuthorityEpoch != Epoch
            || !Observe(packet.AwardId, _awardIds, ref _latestAward)) return false;
        if (_awards.Count == Window) _awards.Dequeue();
        _awards.Enqueue(packet); return true;
    }
    internal void WriteCheckpoint(BinaryWriter writer)
    {
        writer.Write(_events.Count);
        Span<byte> bytes = stackalloc byte[MatchSemanticEventPacket.Size];
        foreach (var fact in _events) { fact.Write(bytes); writer.Write(bytes); }
        writer.Write(_awards.Count);
        foreach (var award in _awards) { award.Write(bytes); writer.Write(bytes[..MatchAwardPacket.Size]); }
    }
    internal static MatchSemanticReceiver ReadCheckpoint(BinaryReader reader, ushort match, ulong epoch)
    {
        var result = new MatchSemanticReceiver(); result.Begin(match, epoch);
        int count = reader.ReadInt32();
        if (count < 0 || count > Window) throw new InvalidDataException("Invalid semantic history count.");
        for (int i = 0; i < count; i++)
            if (!MatchSemanticEventPacket.TryRead(reader.ReadBytes(MatchSemanticEventPacket.Size), out var fact) || !result.Accept(fact))
                throw new InvalidDataException("Invalid semantic checkpoint fact.");
        count = reader.ReadInt32();
        if (count < 0 || count > Window) throw new InvalidDataException("Invalid award history count.");
        for (int i = 0; i < count; i++)
            if (!MatchAwardPacket.TryRead(reader.ReadBytes(MatchAwardPacket.Size), out var award) || !result.Accept(award))
                throw new InvalidDataException("Invalid semantic checkpoint award.");
        return result;
    }
    private static bool Observe(uint id, uint[] history, ref uint latest)
    {
        // IDs cannot wrap inside an epoch. Reliable transport is explicitly unordered.
        if (id == 0 || latest >= id && latest - id >= Window) return false;
        int index = (int)(id % Window);
        if (history[index] == id) return false;
        if (id > latest) latest = id;
        history[index] = id; return true;
    }
}
