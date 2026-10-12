using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal static partial class ReplayCapture
{
    internal static void AcceptTurretOutcome(TurretFireOutcome fact)
    {
        if (!DemoPlayback.IsActive) Recorder.AcceptTurretOutcome(fact, NetSession.NetFrame);
    }
}
internal sealed partial class ReplayRecorder
{
    internal void AcceptTurretOutcome(TurretFireOutcome fact, uint frame)
    {
        if (!HasMatch || fact.Key.MatchId != _matchId || fact.Key.AuthorityEpoch != _epoch) return;
        Span<byte> packet = stackalloc byte[1 + TurretFireOutcome.Size];
        packet[0] = (byte)PacketType.TurretFireOutcome; fact.Write(packet[1..]);
        PublishTransient(new ReplayTimelineRecord(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, packet));
    }
}
internal sealed partial class ReplayReplicaState
{
    private readonly List<TurretFireOutcome> _turretPending = new();
    private readonly Dictionary<ShotKey, TurretFireOutcome> _turretSeen = new();
    private readonly Queue<ShotKey> _turretOrder = new();
    internal int PendingTurretOutcomeCount => _turretPending.Count;
    private bool AcceptTurretOutcome(ReadOnlySpan<byte> payload)
    {
        if (!TurretFireOutcome.TryRead(payload, out var fact)) throw Malformed();
        if (!Matches(fact.Key.MatchId, fact.Key.AuthorityEpoch)) return false;
        if (_turretSeen.TryGetValue(fact.Key, out var prior))
        { if (prior != fact) throw Malformed(); return false; }
        if (_turretPending.Count >= 512) throw new InvalidDataException("Replay turret outcome queue exceeded its budget.");
        if (_turretOrder.Count >= 512) _turretSeen.Remove(_turretOrder.Dequeue());
        _turretOrder.Enqueue(fact.Key); _turretSeen.Add(fact.Key, fact); _turretPending.Add(fact); return true;
    }
    internal void ApplyTurretOutcomes(Scene scene)
    {
        // Live/native clip producers capture arrival-time worlds. Indexed
        // playback owns its authority-time emissions through the pose cursor.
        bool indexed = scene.ReplayPoses?.IndexedFirePresentation == true;
        if (!indexed)
            foreach (var fact in _turretPending)
            {
                int slot = fact.Key.ShooterSlot;
                if (fact.Status != TurretFireOutcomeStatus.Emitted || slot >= scene.Players.Items.Count
                    || !TryGetPlayer(slot, out var player) || player.SlotGeneration != fact.Key.Generation) continue;
                NetFireEvents.PlayAccepted(scene.Players.Items[slot], fact.Key,
                    fact.Fire with { Origin = fact.EmissionOrigin, Direction = fact.EmissionDirection });
            }
        _turretPending.Clear();
    }
    private void ResetTurretOutcomes()
    { _turretPending.Clear(); _turretSeen.Clear(); _turretOrder.Clear(); }
    private void WriteTurretOutcomes(BinaryWriter writer)
    {
        Span<byte> bytes = stackalloc byte[TurretFireOutcome.Size];
        writer.Write(_turretOrder.Count);
        foreach (var key in _turretOrder) { _turretSeen[key].Write(bytes); writer.Write(bytes); }
        writer.Write(_turretPending.Count);
        foreach (var fact in _turretPending) { fact.Write(bytes); writer.Write(bytes); }
    }
    private void ReadTurretOutcomes(BinaryReader reader)
    {
        int count = reader.ReadInt32(); if (count < 0 || count > 512) throw Malformed();
        for (int i = 0; i < count; i++)
        {
            if (!TurretFireOutcome.TryRead(reader.ReadBytes(TurretFireOutcome.Size), out var fact)
                || !Matches(fact.Key.MatchId, fact.Key.AuthorityEpoch) || !_turretSeen.TryAdd(fact.Key, fact)) throw Malformed();
            _turretOrder.Enqueue(fact.Key);
        }
        count = reader.ReadInt32(); if (count < 0 || count > 512) throw Malformed();
        var pending = new HashSet<ShotKey>();
        for (int i = 0; i < count; i++)
        {
            if (!TurretFireOutcome.TryRead(reader.ReadBytes(TurretFireOutcome.Size), out var fact)
                || !_turretSeen.TryGetValue(fact.Key, out var prior) || prior != fact || !pending.Add(fact.Key)) throw Malformed();
            _turretPending.Add(fact);
        }
    }
    private void RestoreTurretOutcomes(ReplayReplicaState restored)
    {
        ResetTurretOutcomes(); _turretPending.AddRange(restored._turretPending);
        foreach (var key in restored._turretOrder) { _turretOrder.Enqueue(key); _turretSeen.Add(key, restored._turretSeen[key]); }
    }
}
