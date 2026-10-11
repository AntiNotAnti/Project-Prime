using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;

namespace MphRead.Mods.Network;

// Placement requests have their own sequence namespace. Relayed movement/button
// packets never create a bomb; only the authority's native admission path does.
internal static class NetBombs
{
    internal static bool Admitting;
    private static ShotKey _admittingKey, _localStream;
    private static uint _nextRequest;
    private static BombIntent? _outgoing;
    private static bool _outgoingSent;
    private static readonly BombIntent?[] Pending = new BombIntent?[8];
    private static readonly uint[] QueuedAt = new uint[8];
    private static readonly ShotKey[] LastRequest = new ShotKey[8];
    // The fixed slot registry can exist before its native players are constructed.
    internal static Scene? LiveScene => PlayerEntity.Players.Count > 0 ? PlayerEntity.Players[0]?.OwningScene : null;
    internal static int PendingCount => Pending.Count(p => p.HasValue) + (_outgoing.HasValue ? 1 : 0);
    internal static void Reset()
    {
        Admitting = false; _admittingKey = _localStream = default; _nextRequest = 0;
        _outgoing = null; _outgoingSent = false; Array.Clear(Pending); Array.Clear(QueuedAt); Array.Clear(LastRequest);
        NetSession.ResetBombPackets();
    }
    internal static bool AllowNativePlacement(PlayerEntity owner)
    {
        if (owner.SceneServices.IsReplica) return !owner.OwningScene.Bombs.Canonical;
        if (!NetSession.Active) return true;
        if (Admitting) return true;
        var scene = owner.OwningScene;
        BombAction action = owner.Hunter == Hunter.Sylux && Count(owner, currentLife: true) >= 3
            ? BombAction.Detonate : BombAction.Place;
        if (NetSession.IsAuthority || NetSession.IsHost)
        {
            // Remote players may only enter through the dedicated request lane.
            if (!owner.IsBot && owner.SlotIndex != NetSession.LocalSlot) return false;
            var stream = ShotKey.For(owner.SlotIndex, 0);
            uint sequence = SameLife(LastRequest[owner.SlotIndex], stream) ? LastRequest[owner.SlotIndex].ShotId + 1 : 1;
            if (sequence == 0) return false;
            var request = new BombIntent(stream with { ShotId = sequence }, NetSession.NetFrame, action);
            LastRequest[owner.SlotIndex] = request.Key;
            NetBombEvidence.Request(request, "received");
            Admit(owner, request);
        }
        else if (owner.SlotIndex == NetSession.LocalSlot && NetSession.WorldAdmissionConfirmed && !NetSession.FreezeGameplay)
        {
            var stream = ShotKey.For(owner.SlotIndex, 0);
            if (!SameLife(_localStream, stream)) { _localStream = stream; _nextRequest = 0; _outgoing = null; }
            if (!_outgoing.HasValue && _nextRequest != uint.MaxValue)
            {
                _outgoing = new(stream with { ShotId = ++_nextRequest }, NetSession.NetFrame, action);
                _outgoingSent = false;
                NetBombEvidence.Request(_outgoing.Value, "requested");
                PumpClient();
            }
        }
        return false;
    }
    internal static bool SameLife(ShotKey a, ShotKey b) => a with { ShotId = 0 } == b with { ShotId = 0 };
    private static int Count(PlayerEntity owner, bool currentLife)
    {
        int count = 0; var stream = ShotKey.For(owner.SlotIndex, 0);
        foreach (var bomb in owner.OwningScene.GetBombEntities())
            if (!bomb.Flags.TestFlag(BombFlags.Exploded) && bomb.ModOwnerKey.ShooterSlot == owner.SlotIndex
                && bomb.ModOwnerKey.Generation == stream.Generation && bomb.ModOwnerKey.MatchId == stream.MatchId
                && bomb.ModOwnerKey.AuthorityEpoch == stream.AuthorityEpoch
                && (!currentLife || bomb.ModOwnerKey.LifeId == stream.LifeId)) count++;
        return count;
    }
    internal static void PumpClient()
    {
        if (_outgoing is not { } request) return;
        if (!SameLife(request.Key, ShotKey.For(request.Key.ShooterSlot, 0))) { _outgoing = null; return; }
        if (!_outgoingSent) _outgoingSent = NetSession.TrySendBombIntent(request);
    }
    internal static void RequestSettled(BombEvent fact)
    {
        if (_outgoing is { } request && request.Key == fact.State.Key
            && fact.Kind is BombEventKind.Placed or BombEventKind.Refused or BombEventKind.DetonateAccepted)
            _outgoing = null;
    }
    internal static void ReceiveRequest(int slot, BombIntent request)
    {
        if ((uint)slot >= 8 || request.Key.ShooterSlot != slot || !Current(request.Key)) return;
        var old = LastRequest[slot];
        if (SameLife(old, request.Key) && request.Key.ShotId <= old.ShotId) return;
        // One outstanding request per occupant. Malicious pipelining cannot
        // grow an application queue or replace an admitted request.
        if (Pending[slot] is { } pending)
        {
            if (SameLife(pending.Key, request.Key)) return;
            Pending[slot] = null;
            Refuse(pending, BombAdmissionReason.Stale);
        }
        LastRequest[slot] = request.Key; Pending[slot] = request; QueuedAt[slot] = NetSession.NetFrame;
        NetBombEvidence.Request(request, "received");
    }
    internal static void AfterSimulation()
    {
        if (NetSession.IsAuthority || NetSession.IsHost)
            for (int slot = 0; slot < Pending.Length; slot++)
            {
                if (Pending[slot] is not { } request) continue;
                if (!Current(request.Key) || slot >= PlayerEntity.Players.Count)
                { Pending[slot] = null; Refuse(request, BombAdmissionReason.Stale); continue; }
                // The reliable command may overtake the movement/form carrier
                // from the same local step. Wait for that pose before admission.
                bool pose = NetSession.RemoteIntentValid[slot]
                    && (NetSession.RemoteIntents[slot].Frame == request.Frame
                        || NetLifecycleTracker.Newer(NetSession.RemoteIntents[slot].Frame, request.Frame));
                if (!pose && unchecked(NetSession.NetFrame - QueuedAt[slot]) < 120) continue;
                Pending[slot] = null;
                if (!pose) Refuse(request, BombAdmissionReason.Stale);
                else Admit(PlayerEntity.Players[slot], request);
            }
        else PumpClient();
    }
    internal static bool Current(ShotKey key) => BombCodec.ValidKey(key)
        && NetSession.MatchesStream(key.MatchId, key.AuthorityEpoch)
        && key.Generation == NetPlayerLifecycle.Generation(key.ShooterSlot)
        && key.LifeId == NetPlayerLifecycle.Get(key.ShooterSlot);
    private static void Admit(PlayerEntity owner, BombIntent request)
    {
        var world = owner.OwningScene.Bombs; world.Begin(request.Key.MatchId, request.Key.AuthorityEpoch);
        BombAdmissionReason reason;
        _admittingKey = request.Key; Admitting = true;
        try
        {
            reason = request.Action == BombAction.Place && world.LiveCount >= 24
                ? BombAdmissionReason.Limit : owner.ModTryAdmitBomb(request.Action, out _);
        }
        finally { Admitting = false; _admittingKey = default; }
        if (reason != BombAdmissionReason.None) Refuse(request, reason);
        else if (request.Action == BombAction.Detonate)
            world.Publish(BombEventKind.DetonateAccepted, IdentityOnly(request.Key));
    }
    private static BombState IdentityOnly(ShotKey key) => new(key, default, 0, default, default, default, default, 0, 0, 0, 0, 0);
    private static void Refuse(BombIntent request, BombAdmissionReason reason)
    {
        if (LiveScene is not { } scene || !NetSession.MatchesStream(request.Key.MatchId, request.Key.AuthorityEpoch)) return;
        scene.Bombs.Begin(request.Key.MatchId, request.Key.AuthorityEpoch);
        scene.Bombs.Publish(BombEventKind.Refused, IdentityOnly(request.Key), (byte)reason);
    }
    internal static void Placed(BombEntity bomb)
    {
        if (!NetSession.Active || bomb.Owner.SceneServices.IsReplica || !Admitting) return;
        bomb.ModOwnerKey = _admittingKey; bomb.ModPlacedFrame = NetSession.NetFrame;
        bomb.Owner.OwningScene.Bombs.Placed(bomb);
    }
    internal static bool CanSimulate(BombEntity bomb)
    {
        if (bomb.ModReplica) return false;
        if (bomb.Owner.SceneServices.IsReplica) return !bomb.Owner.OwningScene.Bombs.Canonical;
        if (!NetSession.Active) return true;
        // Old-life bombs retain ownership until their own terminal event, but
        // never borrow the next occupant of a reused slot.
        var key = bomb.ModOwnerKey;
        return (NetSession.IsAuthority || NetSession.IsHost)
            && NetSession.MatchesStream(key.MatchId, key.AuthorityEpoch)
            && key.Generation == NetPlayerLifecycle.Generation(key.ShooterSlot)
            && (bomb.BombType != BombType.Lockjaw || key.LifeId == NetPlayerLifecycle.Get(key.ShooterSlot));
    }
    internal static void Terminal(BombEntity bomb, bool detonated)
    {
        if (bomb.ModReplica || bomb.Owner == null || bomb.Owner.SceneServices.IsReplica || !NetSession.Active) return;
        bomb.Owner.OwningScene.Bombs.Terminal(bomb, detonated);
    }
    internal static byte[][] CaptureSnapshot(uint frame)
    {
        if (LiveScene is { } scene)
        { scene.Bombs.Begin(NetSession.CurrentMatchId, NetSession.AuthorityEpoch); return scene.Bombs.CaptureSnapshot(frame); }
        return BombStatePage.PackSnapshot(NetSession.CurrentMatchId, NetSession.AuthorityEpoch, frame, 0, Array.Empty<BombState>());
    }
}

// Scene-owned canonical presentation, including detached replay worlds. Reliable
// facts are applied in event order; snapshots recover position without discarding
// the short-lived placements/terminals that happened between two snapshots.
internal sealed class NetBombWorld
{
    private const int History = 2048;
    private readonly Scene _scene;
    private ushort _match; private ulong _epoch, _frontier;
    private readonly HashSet<ShotKey> _authorityLive = new();
    private readonly SortedDictionary<ulong, BombEvent> _pending = new();
    private readonly Dictionary<ShotKey, ulong> _terminal = new();
    private readonly Queue<ShotKey> _terminalOrder = new();
    private readonly BombStateReceiver _receiver = new();
    private bool _baseline, _hasSnapshot;
    private uint _snapshotFrame;
    private ulong _snapshotFrontier;
    private readonly HashSet<ShotKey> _snapshotKeys = new();
    internal bool Canonical { get; private set; }
    internal bool Faulted { get; private set; }
    internal ulong Frontier => _frontier;
    internal int PendingEvents => _pending.Count;
    internal int LiveCount => _authorityLive.Count;
    internal NetBombWorld(Scene scene) => _scene = scene;
    internal void Begin(ushort match, ulong epoch)
    {
        if (Canonical && _match == match && _epoch == epoch) return;
        foreach (var bomb in ReplicaBombs()) bomb.ModRemoveReplica();
        _match = match; _epoch = epoch; _frontier = 0; Canonical = true; Faulted = false;
        _baseline = false; _hasSnapshot = false; _snapshotFrame = 0; _snapshotFrontier = 0;
        _authorityLive.Clear(); _pending.Clear(); _terminal.Clear(); _terminalOrder.Clear(); _snapshotKeys.Clear(); _receiver.Reset();
    }
    internal void Placed(BombEntity bomb)
    {
        Begin(bomb.ModOwnerKey.MatchId, bomb.ModOwnerKey.AuthorityEpoch);
        if (_authorityLive.Add(bomb.ModOwnerKey)) Publish(BombEventKind.Placed, bomb.ModCaptureState());
    }
    internal void Terminal(BombEntity bomb, bool detonated)
    {
        if (_authorityLive.Remove(bomb.ModOwnerKey))
            Publish(detonated ? BombEventKind.Detonated : BombEventKind.Removed, bomb.ModCaptureState());
    }
    internal void Publish(BombEventKind kind, BombState state, byte reason = 0)
    {
        var fact = new BombEvent(checked(++_frontier), NetSession.NetFrame, kind, reason, state);
        NetBombEvidence.Event(fact, "authority");
        ReplayCapture.AcceptBombEvent(fact);
        NetSession.PublishBombEvent(fact);
    }
    internal byte[][] CaptureSnapshot(uint frame)
    {
        var states = new List<BombState>(24);
        foreach (var bomb in _scene.GetBombEntities())
            if (bomb.ModReplica || _authorityLive.Contains(bomb.ModOwnerKey))
                if (!bomb.Flags.TestFlag(BombFlags.Exploded)) states.Add(bomb.ModCaptureState());
        if (!_scene.Services.IsReplica && (NetSession.IsAuthority || NetSession.IsHost))
            NetBombEvidence.Snapshot(frame, _frontier, states, "authority");
        return BombStatePage.PackSnapshot(_match, _epoch, frame, _frontier, states.ToArray());
    }
    private BombEntity[] ReplicaBombs()
    {
        var bombs = new List<BombEntity>(24);
        foreach (var bomb in _scene.GetBombEntities()) if (bomb.ModReplica) bombs.Add(bomb);
        return bombs.ToArray();
    }
    private BombEntity? Find(ShotKey key)
    { foreach (var bomb in _scene.GetBombEntities()) if (bomb.ModReplica && bomb.ModOwnerKey == key) return bomb; return null; }
    private bool Matches(ShotKey key) => key.MatchId == _match && key.AuthorityEpoch == _epoch;
    private BombEntity? Put(BombState state)
    {
        if (!Matches(state.Key) || state.Key.ShooterSlot >= _scene.Players.Items.Count) return null;
        var bomb = Find(state.Key);
        if (bomb != null) bomb.ModApplyState(state);
        else bomb = BombEntity.SpawnReplica(_scene.Players.Items[state.Key.ShooterSlot], state, _scene);
        if (bomb == null) Faulted = true;
        return bomb;
    }
    internal void ApplyBaseline(BombStateReceiver receiver)
    {
        if (!receiver.HasSnapshot) return;
        foreach (var bomb in ReplicaBombs()) bomb.ModRemoveReplica();
        _frontier = receiver.Frontier; _baseline = true;
        foreach (var sequence in _pending.Keys.Where(k => k <= _frontier).ToArray()) _pending.Remove(sequence);
        ApplySnapshot(receiver);
        Drain();
    }
    internal bool ReceiveState(ReadOnlySpan<byte> payload)
    {
        if (!BombStatePage.TryRead(payload, out var page) || page!.MatchId != _match || page.Epoch != _epoch) return false;
        if (!_receiver.Accept(page)) return false;
        if (_receiver.HasSnapshot && (!_hasSnapshot || NetLifecycleTracker.Newer(_receiver.Frame, _snapshotFrame)))
        {
            // Replays start with a full bomb baseline before any subsequent facts.
            if (!_baseline && _scene.Services.IsReplica) ApplyBaseline(_receiver);
            else if (_baseline) ApplySnapshot(_receiver);
        }
        return true;
    }
    private void ApplySnapshot(BombStateReceiver receiver)
    {
        _hasSnapshot = true; _snapshotFrame = receiver.Frame; _snapshotFrontier = receiver.Frontier; _snapshotKeys.Clear();
        foreach (var state in receiver.States) _snapshotKeys.Add(state.Key);
        foreach (var bomb in ReplicaBombs())
            if (!_snapshotKeys.Contains(bomb.ModOwnerKey) && !NetLifecycleTracker.Newer(bomb.ModPlacedFrame, _snapshotFrame))
                bomb.ModRemoveReplica();
        foreach (var state in receiver.States)
            if (!_terminal.ContainsKey(state.Key)) Put(state);
        NetBombVisibilityProbe.AppliedSnapshot(_scene, receiver.Frame, receiver.Frontier, receiver.States);
        if (!_scene.Services.IsReplica)
            NetBombEvidence.Snapshot(receiver.Frame, receiver.Frontier, receiver.States, "applied");
    }
    internal bool ReceiveEvent(BombEvent fact)
    {
        if (!Matches(fact.State.Key) || fact.Sequence <= _frontier) return false;
        if (_pending.TryGetValue(fact.Sequence, out var old)) { if (old != fact) Faulted = true; return false; }
        if (fact.Sequence - _frontier > History || _pending.Count >= History) { Faulted = true; return false; }
        _pending.Add(fact.Sequence, fact);
        if (_baseline) Drain();
        return true;
    }
    private void Drain()
    {
        while (_pending.Remove(_frontier + 1, out var fact))
        {
            _frontier = fact.Sequence;
            var state = fact.State;
            using var diagnostic = NetBombVisibilityProbe.AcceptedEvent(_scene, fact,
                !_hasSnapshot || NetLifecycleTracker.Newer(fact.Frame, _snapshotFrame) || _snapshotKeys.Contains(state.Key),
                _hasSnapshot, _snapshotFrame, _snapshotFrontier, _snapshotKeys.Contains(state.Key));
            switch (fact.Kind)
            {
                case BombEventKind.Placed:
                    if (!_terminal.ContainsKey(state.Key) && (!_hasSnapshot || NetLifecycleTracker.Newer(fact.Frame, _snapshotFrame)
                        || _snapshotKeys.Contains(state.Key)))
                    {
                        // A newer state may have arrived before this birth fact.
                        if (Find(state.Key) == null) Put(state)?.PlaySpawnSfx();
                    }
                    break;
                case BombEventKind.Detonated:
                case BombEventKind.Removed:
                    var bomb = Find(state.Key);
                    if (fact.Kind == BombEventKind.Detonated)
                    { bomb ??= Put(state); bomb?.ModApplyState(state); bomb?.ModPresentDetonation(); }
                    bomb?.ModRemoveReplica();
                    if (_terminal.TryAdd(state.Key, fact.Sequence)) _terminalOrder.Enqueue(state.Key);
                    while (_terminalOrder.Count > History) _terminal.Remove(_terminalOrder.Dequeue());
                    break;
            }
            if (!_scene.Services.IsReplica) { NetBombs.RequestSettled(fact); NetBombEvidence.Event(fact, "applied"); }
        }
    }
    internal void WriteCheckpoint(BinaryWriter writer)
    {
        writer.Write(Canonical); writer.Write(_match); writer.Write(_epoch); writer.Write(_frontier);
        writer.Write(_baseline); writer.Write(_hasSnapshot); writer.Write(_snapshotFrame); writer.Write(_snapshotFrontier); writer.Write(Faulted);
        writer.Write(_terminalOrder.Count);
        foreach (var key in _terminalOrder) { WriteKey(writer, key); writer.Write(_terminal[key]); }
        writer.Write(_snapshotKeys.Count); foreach (var key in _snapshotKeys.OrderBy(k => k.ShooterSlot).ThenBy(k => k.Generation).ThenBy(k => k.LifeId).ThenBy(k => k.ShotId)) WriteKey(writer, key);
        writer.Write(_pending.Count); foreach (var fact in _pending.Values) { byte[] bytes = new byte[BombEvent.Size]; fact.Write(bytes); writer.Write(bytes); }
        _receiver.WriteCheckpoint(writer);
    }
    internal void ReadCheckpoint(BinaryReader reader)
    {
        Canonical = reader.ReadBoolean(); _match = reader.ReadUInt16(); _epoch = reader.ReadUInt64(); _frontier = reader.ReadUInt64();
        _baseline = reader.ReadBoolean(); _hasSnapshot = reader.ReadBoolean(); _snapshotFrame = reader.ReadUInt32(); _snapshotFrontier = reader.ReadUInt64(); Faulted = reader.ReadBoolean();
        _terminal.Clear(); _terminalOrder.Clear(); _snapshotKeys.Clear(); _pending.Clear(); _authorityLive.Clear();
        for (int i = ReadCount(reader, History); i > 0; i--) { var key = ReadKey(reader); _terminal.Add(key, reader.ReadUInt64()); _terminalOrder.Enqueue(key); }
        for (int i = ReadCount(reader, 24); i > 0; i--) _snapshotKeys.Add(ReadKey(reader));
        for (int i = ReadCount(reader, History); i > 0; i--) { if (!BombEvent.TryRead(reader.ReadBytes(BombEvent.Size), out var fact)) throw new InvalidDataException("Invalid bomb event checkpoint"); _pending.Add(fact.Sequence, fact); }
        _receiver.ReadCheckpoint(reader);
    }
    private static int ReadCount(BinaryReader reader, int max) { int count = reader.ReadInt32(); if (count < 0 || count > max) throw new InvalidDataException("Invalid bomb checkpoint count"); return count; }
    private static void WriteKey(BinaryWriter writer, ShotKey key) { byte[] b = new byte[BombCodec.KeySize]; BombCodec.WriteKey(b, key); writer.Write(b); }
    private static ShotKey ReadKey(BinaryReader reader) { var b = reader.ReadBytes(BombCodec.KeySize); if (b.Length != BombCodec.KeySize) throw new EndOfStreamException(); var key = BombCodec.ReadKey(b); if (!BombCodec.ValidKey(key)) throw new InvalidDataException("Invalid bomb checkpoint key"); return key; }
}
