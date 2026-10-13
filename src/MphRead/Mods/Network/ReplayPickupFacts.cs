using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal static partial class ReplayCapture
{
    internal static void AcceptPickupClaim(PickupClaim fact)
    { if (!DemoPlayback.IsActive) Recorder.AcceptPickupClaim(fact, NetSession.NetFrame); }
    internal static void AcceptPickupVerdict(PickupVerdict fact)
    { if (!DemoPlayback.IsActive) Recorder.AcceptPickupVerdict(fact, NetSession.NetFrame); }
    internal static void AcceptPickupItem(PickupItemFact fact)
    { if (!DemoPlayback.IsActive) Recorder.AcceptPickupItem(fact, NetSession.NetFrame); }
}
internal sealed partial class ReplayRecorder
{
    private readonly Dictionary<PowerUpSpawnKey, PickupItemFact> _pickupSeed = new();
    private uint _pickupSeedRoom;
    private void ResetPickupSeed() { _pickupSeed.Clear(); _pickupSeedRoom = 0; }
    private bool ObservePickupSeedRoom(uint room)
    {
        if (room < _pickupSeedRoom) return false;
        if (room > _pickupSeedRoom) { ResetPickupSeed(); _pickupSeedRoom = room; }
        return _grantSeed.ObserveRoom(_epoch, _matchId, room);
    }
    private void SeedPickupItems(uint frame, Action<ReplayTimelineRecord> accept)
    {
        // Replay the retained identities in authority order: the normal decoder's
        // stale-packet watermark must not discard an older still-available item.
        var facts = new List<PickupItemFact>(_pickupSeed.Values);
        facts.Sort((a, b) => unchecked((int)(a.AuthorityFrame - b.AuthorityFrame)));
        Span<byte> packet = stackalloc byte[1 + PickupItemFact.Size];
        packet[0] = (byte)PacketType.PickupItem;
        foreach (var fact in facts)
        {
            fact.Write(packet[1..]);
            var record = new ReplayTimelineRecord(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, packet);
            try { accept(record); } finally { record.Release(); }
        }
    }
    internal void AcceptPickupClaim(PickupClaim fact, uint frame)
    {
        if (!fact.IsValid || !HasMatch || fact.Key.Match != _matchId || fact.Key.Epoch != _epoch) return;
        Span<byte> packet = stackalloc byte[1 + PickupClaim.Size]; packet[0] = (byte)PacketType.PickupClaim;
        fact.Write(packet[1..]); PublishTransient(new(frame, fact.SourceFrame, ReplayFactKind.Presentation, packet));
    }
    internal void AcceptPickupVerdict(PickupVerdict fact, uint frame)
    {
        // Unadmitted capacity responses carry no authority decision clock and
        // cannot affect gameplay. Keep them in campaign/transport diagnostics,
        // not the admitted replay ledger (reconnects cannot grow it unbounded).
        if (fact.Result == PickupResult.Capacity && fact.DecisionFrame == 0 && fact.ProofFrame == 0) return;
        if (!fact.IsValid || !HasMatch || fact.Claim.Key.Match != _matchId || fact.Claim.Key.Epoch != _epoch) return;
        if (ObservePickupSeedRoom(fact.Claim.Spawn.Room) && _grantSeed.HasCoverage) _grantSeed.Settle(fact, frame);
        ObserveGrantSeedFrame(frame);
        Span<byte> packet = stackalloc byte[1 + PickupVerdict.Size]; packet[0] = (byte)PacketType.PickupVerdict;
        fact.Write(packet[1..]); PublishTransient(new(frame, fact.DecisionFrame, ReplayFactKind.Presentation, packet));
    }
    internal void AcceptPickupItem(PickupItemFact fact, uint frame)
    {
        if (!fact.IsValid || !HasMatch || fact.Spawn.Match != _matchId || fact.Spawn.Epoch != _epoch) return;
        if (!ObservePickupSeedRoom(fact.Spawn.Room)) return;
        var result = PickupItemHistory.Accept(_pickupSeed, fact);
        if (result == PickupItemAcceptance.Unchanged) return;
        if (result != PickupItemAcceptance.Accepted) throw new InvalidDataException("Invalid replay pickup seed transition.");
        ObserveGrantSeedFrame(frame);
        Span<byte> packet = stackalloc byte[1 + PickupItemFact.Size]; packet[0] = (byte)PacketType.PickupItem;
        fact.Write(packet[1..]); PublishTransient(new(frame, fact.AuthorityFrame, ReplayFactKind.Presentation, packet));
    }
}

// Private identity/terminal evidence, feeding A3 presentation without a foreground ledger.
internal sealed partial class ReplayReplicaState
{
    internal const int PickupReplayCapacity = 2048;
    private uint _pickupRoom;
    private readonly Dictionary<PickupClaimKey, PickupClaim> _pickupClaims = new();
    private readonly Dictionary<PickupClaimKey, PickupVerdict> _pickupVerdicts = new();
    private readonly Dictionary<PowerUpSpawnKey, PickupItemFact> _pickupItems = new();
    internal uint PickupRoomIdentity => _pickupRoom;
    internal IReadOnlyDictionary<PickupClaimKey, PickupClaim> PickupClaims => _pickupClaims;
    internal IReadOnlyDictionary<PickupClaimKey, PickupVerdict> PickupVerdicts => _pickupVerdicts;
    internal IReadOnlyDictionary<PowerUpSpawnKey, PickupItemFact> PickupItems => _pickupItems;
    private void ResetPickupFacts()
    { _pickupRoom = 0; _pickupClaims.Clear(); _pickupVerdicts.Clear(); _pickupItems.Clear(); }
    private bool PickupRoom(uint room)
    {
        if (room < _pickupRoom) return false;
        if (Match is not { } match || !TimedGrants.ObserveRoom(match.AuthorityEpoch, match.MatchId, room)) return false;
        if (room > _pickupRoom) { ResetPickupFacts(); _pickupRoom = room; }
        return true;
    }
    private bool AcceptPickupClaim(ReadOnlySpan<byte> payload)
    {
        if (!PickupClaim.TryRead(payload, out var fact)) throw Malformed();
        if (!Matches(fact.Key.Match, fact.Key.Epoch) || !PickupRoom(fact.Spawn.Room)) return false;
        if (_pickupClaims.TryGetValue(fact.Key, out var prior))
        { if (prior != fact) throw Malformed(); return false; }
        if (_pickupVerdicts.TryGetValue(fact.Key, out var terminal) && terminal.Claim != fact) throw Malformed();
        if (_pickupClaims.Count >= PickupReplayCapacity) throw new InvalidDataException("Replay pickup claim budget exceeded.");
        _pickupClaims.Add(fact.Key, fact); return true;
    }
    private bool AcceptPickupVerdict(ReadOnlySpan<byte> payload, uint frame, bool settle = true)
    {
        if (!PickupVerdict.TryRead(payload, out var fact)) throw Malformed();
        var key = fact.Claim.Key;
        if (!Matches(key.Match, key.Epoch) || !PickupRoom(fact.Claim.Spawn.Room)) return false;
        if (_pickupVerdicts.TryGetValue(key, out var prior))
        { if (prior != fact) throw Malformed(); return false; }
        if (_pickupClaims.TryGetValue(key, out var claim) && claim != fact.Claim) throw Malformed();
        if (_pickupVerdicts.Count >= PickupReplayCapacity) throw new InvalidDataException("Replay pickup verdict budget exceeded.");
        // A2 historical verdicts alone never establish an A3 presentation contract.
        if (settle && TimedGrants.HasCoverage) TimedGrants.Settle(fact, frame);
        _pickupVerdicts.Add(key, fact); return true;
    }
    private bool AcceptPickupItem(ReadOnlySpan<byte> payload)
    {
        if (!PickupItemFact.TryRead(payload, out var fact)) throw Malformed();
        if (!Matches(fact.Spawn.Match, fact.Spawn.Epoch) || !PickupRoom(fact.Spawn.Room)) return false;
        var result = PickupItemHistory.Accept(_pickupItems, fact);
        if (result == PickupItemAcceptance.Unchanged) return false;
        if (result != PickupItemAcceptance.Accepted) throw Malformed();
        return true;
    }
    private void WritePickupFacts(BinaryWriter writer)
    {
        writer.Write(_pickupRoom);
        Span<byte> bytes = stackalloc byte[PickupVerdict.Size];
        writer.Write(_pickupClaims.Count);
        foreach (var fact in _pickupClaims.Values) { fact.Write(bytes[..PickupClaim.Size]); writer.Write(bytes[..PickupClaim.Size]); }
        writer.Write(_pickupVerdicts.Count);
        foreach (var fact in _pickupVerdicts.Values) { fact.Write(bytes); writer.Write(bytes); }
        writer.Write(_pickupItems.Count);
        foreach (var fact in _pickupItems.Values) { fact.Write(bytes[..PickupItemFact.Size]); writer.Write(bytes[..PickupItemFact.Size]); }
    }
    private void ReadPickupFacts(BinaryReader reader)
    {
        uint room = reader.ReadUInt32();
        int Count() { int count = reader.ReadInt32(); if (count < 0 || count > PickupReplayCapacity) throw Malformed(); return count; }
        for (int i = 0, count = Count(); i < count; i++)
        {
            byte[] bytes = reader.ReadBytes(PickupClaim.Size);
            if (!PickupClaim.TryRead(bytes, out var fact) || fact.Spawn.Room != room || !AcceptPickupClaim(bytes)) throw Malformed();
        }
        for (int i = 0, count = Count(); i < count; i++)
        {
            byte[] bytes = reader.ReadBytes(PickupVerdict.Size);
            if (!PickupVerdict.TryRead(bytes, out var fact) || fact.Claim.Spawn.Room != room || !AcceptPickupVerdict(bytes, RecordingFrame, settle: false)) throw Malformed();
        }
        for (int i = 0, count = Count(); i < count; i++)
        {
            byte[] bytes = reader.ReadBytes(PickupItemFact.Size);
            if (!PickupItemFact.TryRead(bytes, out var fact) || fact.Spawn.Room != room
                || !Matches(fact.Spawn.Match, fact.Spawn.Epoch) || !_pickupItems.TryAdd(fact.Spawn, fact)) throw Malformed();
        }
        _pickupRoom = room;
    }
    private void RestorePickupFacts(ReplayReplicaState restored)
    {
        ResetPickupFacts(); _pickupRoom = restored._pickupRoom;
        foreach (var pair in restored._pickupClaims) _pickupClaims.Add(pair.Key, pair.Value);
        foreach (var pair in restored._pickupVerdicts) _pickupVerdicts.Add(pair.Key, pair.Value);
        foreach (var pair in restored._pickupItems) _pickupItems.Add(pair.Key, pair.Value);
    }
}
