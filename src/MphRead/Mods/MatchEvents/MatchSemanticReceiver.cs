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
    private uint _latestEvent, _latestAward, _presentedEvent, _presentedAward;
    private readonly SortedDictionary<uint, MatchSemanticEventPacket> _pendingEvents = new();
    private readonly SortedDictionary<uint, MatchAwardPacket> _pendingAwards = new();
    internal bool HasBaseline { get; private set; }
    internal bool PresentationOverrun { get; private set; }
    internal uint PresentedEvent => _presentedEvent;
    internal uint PresentedAward => _presentedAward;
    internal ushort MatchId { get; private set; }
    internal ulong Epoch { get; private set; }
    internal IReadOnlyCollection<MatchSemanticEventPacket> Events => _events;
    internal IReadOnlyCollection<MatchAwardPacket> Awards => _awards;
    internal void Begin(ushort match, ulong epoch, bool requireBaseline = false)
    {
        if (match == MatchId && epoch == Epoch) return;
        MatchId = match; Epoch = epoch; Array.Clear(_eventIds); Array.Clear(_awardIds);
        _latestEvent = _latestAward = _presentedEvent = _presentedAward = 0; _events.Clear(); _awards.Clear();
        _pendingEvents.Clear(); _pendingAwards.Clear(); HasBaseline = !requireBaseline; PresentationOverrun = false;
    }
    internal bool Accept(in MatchSemanticEventPacket packet)
    {
        if (packet.IsBaseline) return AcceptBaseline(packet);
        if (!packet.Validate() || packet.MatchId != MatchId || packet.AuthorityEpoch != Epoch
            || !Observe(packet.EventId, _eventIds, ref _latestEvent)) return false;
        if (_events.Count == Window) _events.Dequeue();
        _events.Enqueue(packet);
        if (HasBaseline && packet.EventId > _presentedEvent && packet.EventId - _presentedEvent > Window) PresentationOverrun = true;
        if (packet.EventId > _presentedEvent)
        {
            if (_pendingEvents.Count == Window) { PresentationOverrun = true; return true; }
            _pendingEvents[packet.EventId] = packet;
        }
        return true;
    }
    internal bool Accept(in MatchAwardPacket packet)
    {
        if (!packet.Validate() || packet.MatchId != MatchId || packet.AuthorityEpoch != Epoch
            || !Observe(packet.AwardId, _awardIds, ref _latestAward)) return false;
        if (_awards.Count == Window) _awards.Dequeue();
        _awards.Enqueue(packet);
        if (HasBaseline && packet.AwardId > _presentedAward && packet.AwardId - _presentedAward > Window) PresentationOverrun = true;
        if (packet.AwardId > _presentedAward)
        {
            if (_pendingAwards.Count == Window) { PresentationOverrun = true; return true; }
            _pendingAwards[packet.AwardId] = packet;
        }
        return true;
    }
    private bool AcceptBaseline(in MatchSemanticEventPacket packet)
    {
        if (!packet.Validate() || packet.MatchId != MatchId || packet.AuthorityEpoch != Epoch || HasBaseline) return false;
        HasBaseline = true; _presentedEvent = packet.EventId; _presentedAward = packet.AwardFrontier;
        if (_latestEvent > _presentedEvent && _latestEvent - _presentedEvent > Window
            || _latestAward > _presentedAward && _latestAward - _presentedAward > Window) PresentationOverrun = true;
        foreach (uint id in new List<uint>(_pendingEvents.Keys)) if (id <= _presentedEvent) _pendingEvents.Remove(id);
        foreach (uint id in new List<uint>(_pendingAwards.Keys)) if (id <= _presentedAward) _pendingAwards.Remove(id);
        return true;
    }
    internal void DrainPresentation(Action<MatchSemanticEventPacket> fact, Action<MatchAwardPacket> award)
    {
        if (!HasBaseline || PresentationOverrun) return;
        while (_presentedEvent < uint.MaxValue && _pendingEvents.Remove(_presentedEvent + 1, out var next))
        { _presentedEvent++; fact(next); }
        while (_presentedAward < uint.MaxValue && _pendingAwards.TryGetValue(_presentedAward + 1, out var next)
            && next.SourceEventId <= _presentedEvent)
        { _pendingAwards.Remove(++_presentedAward); award(next); }
    }
    internal void SuppressHistory()
    {
        _presentedEvent = _latestEvent; _presentedAward = _latestAward;
        _pendingEvents.Clear(); _pendingAwards.Clear(); PresentationOverrun = false;
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
        result.SuppressHistory(); // Checkpoint restoration must not announce historical facts.
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
