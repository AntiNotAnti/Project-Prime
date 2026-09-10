using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MphRead.Combat;
using MphRead.Mods.Network;

namespace MphRead.Replay;

/// <summary>The same bounded observer presentation reducers and checkpoint codec as Client.
/// No local actor is invented; private participant hitmarkers/recaps remain absent.</summary>
internal sealed class ServerReplayFeedback
{
    private readonly CombatFeedback _combat = new();
    private readonly WorldFeedback _world = new();
    private readonly NetRosterEntry[] _roster = new NetRosterEntry[8];
    internal SemanticAwardJournal AwardJournal { get; } = new();
    private uint _matchId;

    internal void Bind(ObserverFrame frame, uint phase)
    {
        if (_matchId != frame.MatchId)
        {
            _matchId = frame.MatchId;
            AwardJournal.Reset();
        }
        if (!SessionRosterPacket.TryRead(frame.Roster!.AsSpan(4), _roster, out _, out int count))
            throw new ArgumentException("Invalid replay roster baseline.");
        _combat.Bind(frame.MatchId, CombatActor.None, _roster.AsSpan(0, count), frame.Tick, phase);
        _world.Bind(frame.MatchId, phase);
    }

    internal void Apply(ObserverFrame frame)
    {
        Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
        foreach (var item in frame.Events)
        {
            var payload = item.Payload.AsSpan(4);
            if (item.Type == ReliableEventType.Combat && CombatEventBatch.TryRead(payload, events, out int count))
                foreach (var value in events[..count]) _combat.Process(value);
            else if (item.Type == ReliableEventType.Kill && KillEvent.TryRead(payload, out var kill)) _combat.Process(kill);
            else if (item.Type == ReliableEventType.WorldEvent && WorldEvent.TryRead(payload, out var world))
                _world.Process(world, CombatActor.None, frame.Tick, frame.Rules.PickupRespawnAnnouncements);
            else if (item.Type == ReliableEventType.MatchAward && MatchAwardPacket.TryRead(payload, out MatchAwardPacket packet)
                && packet.MatchId == frame.MatchId && MatchAwardPacketConversion.TryToAward(packet, out MatchAward award))
                AwardJournal.Record(award);
        }
    }

    internal byte[][] Checkpoint()
    {
        byte[] bytes = ReplayFeedbackState.Capture(_combat, _world);
        const int size = NetConfig.MaxPacketSize - 9;
        int count = (bytes.Length + size - 1) / size;
        var records = new List<byte[]>(count);
        for (int fragment = 0; fragment < count; fragment++)
        {
            int length = Math.Min(size, bytes.Length - fragment * size);
            byte[] record = new byte[9 + length]; record[0] = (byte)ReplayRecordKind.Presentation;
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)fragment);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(3), (ushort)count);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(5), bytes.Length);
            bytes.AsSpan(fragment * size, length).CopyTo(record.AsSpan(9)); records.Add(record);
        }
        return records.ToArray();
    }
}
