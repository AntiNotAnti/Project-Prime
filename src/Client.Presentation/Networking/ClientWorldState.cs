using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;

namespace MphRead.Mods.Network
{
    /// <summary>Atomic, bounded full-world assembly. Missing batches preserve the last complete state.</summary>
    public sealed class ClientWorldState
    {
        private WorldRecord[] _pending = new WorldRecord[WorldPacket.Capacity];
        private WorldRecord[] _current = new WorldRecord[WorldPacket.Capacity];
        private readonly bool[] _received = new bool[WorldPacket.Capacity];
        private readonly Dictionary<uint, ItemInstanceEntity> _items = new(WorldPacket.Capacity);
        private readonly HashSet<uint> _present = new(WorldPacket.Capacity);
        private readonly uint[] _retired = new uint[WorldPacket.Capacity];
        private uint _pendingRevision;
        private uint _pendingTick;
        private int _pendingCount;
        private int _receivedCount;
        private bool _assembling;
        private bool _dirty;
        public uint MatchId { get; private set; }
        internal bool LegacyProtocol { get; set; }
        internal bool Protocol7Replay { get; set; }
        private bool HistoricalProtocol => LegacyProtocol || Protocol7Replay;
        private readonly PlayerResultIdentity[] _resultIdentities = new PlayerResultIdentity[8];
        public bool ValidatePacket(ReadOnlySpan<byte> body) => HistoricalProtocol
            ? Protocol7WorldPacket.TryValidate(body, MatchId, LegacyProtocol)
            : WorldPacket.TryValidate(body, MatchId);
        private bool ReadRecord(ReadOnlySpan<byte> bytes, out WorldRecord record)
        {
            if (!HistoricalProtocol) return WorldRecord.TryRead(bytes, out record);
            record = default;
            if (!Protocol7WorldRecord.TryRead(bytes, LegacyProtocol, out var old)) return false;
            record = new((WorldRecordKind)old.Kind, old.Slot, old.Flags, old.Id, old.Position, old.A, old.B, old.C, old.D, old.E);
            return true;
        }
        public uint Revision { get; private set; }
        public uint ServerTick { get; private set; }
        public int Count { get; private set; }
        public bool HasState { get; private set; }
        public MatchPhase Phase => !HasState ? MatchPhase.WaitingForPlayers : LegacyProtocol
            ? _current[0].B switch { 0 => MatchPhase.Playing, 1 => MatchPhase.Ending, _ => MatchPhase.Intermission }
            : (MatchPhase)_current[0].B;
        public uint PhaseStartTick => HasState && !LegacyProtocol ? _current[17].A : 0;
        public uint PhaseRevision => HasState && !LegacyProtocol ? _current[17].C : 0;
        public ReadOnlySpan<WorldRecord> Records => _current.AsSpan(0, Count);

        public void Reset(uint matchId)
        {
            MatchId = matchId; HasState = _assembling = _dirty = false;
            Count = _pendingCount = _receivedCount = 0;
            // Scene lifetime is owned by the caller; call Reset after closing the old scene.
            _items.Clear(); _present.Clear();
        }
        public bool Receive(ReadOnlySpan<byte> body)
        {
            if (!ValidatePacket(body)) { return false; }
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
            uint tick = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
            int total = BinaryPrimitives.ReadUInt16LittleEndian(body[12..]);
            if (HasState && !Sequence32.IsNewer(revision, Revision)) { return false; }
            if (!_assembling || revision != _pendingRevision)
            {
                if (_assembling && !Sequence32.IsNewer(revision, _pendingRevision)) { return false; }
                _pendingRevision = revision; _pendingTick = tick; _pendingCount = total;
                _receivedCount = 0; _assembling = true; Array.Clear(_received);
            }
            if (total != _pendingCount || tick != _pendingTick) { return false; }
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
            for (int i = 0; i < body[16]; i++)
            {
                int index = offset + i;
                if (_received[index]) { continue; }
                ReadRecord(body.Slice(WorldPacket.HeaderSize + i * WorldRecord.Size, WorldRecord.Size), out _pending[index]);
                _received[index] = true; _receivedCount++;
            }
            if (_receivedCount != total) { return false; }
            // Require the complete match and eight-player tables before entity records.
            if (total < 17 || _pending[0].Kind != WorldRecordKind.Match) { _assembling = false; return false; }
            for (int slot = 0; slot < 8; slot++)
            {
                if (_pending[1 + slot * 2].Kind != WorldRecordKind.Score || _pending[1 + slot * 2].Slot != slot
                    || _pending[2 + slot * 2].Kind != WorldRecordKind.Time || _pending[2 + slot * 2].Slot != slot)
                { _assembling = false; return false; }
            }
            if (!LegacyProtocol && (total < 18 || _pending[17].Kind != WorldRecordKind.Lifecycle))
            { _assembling = false; return false; }
            if (!LegacyProtocol && HasState && _pending[17].C != PhaseRevision
                && !Sequence32.IsNewer(_pending[17].C, PhaseRevision))
            { _assembling = false; return false; }
            if (!HistoricalProtocol)
            {
                if (total < WorldPacket.CanonicalRecordCount) { _assembling = false; return false; }
                for (int slot = 0; slot < 8; slot++)
                    for (int kind = 0; kind < 5; kind++)
                        if (_pending[18 + slot * 5 + kind].Kind != (WorldRecordKind)((int)WorldRecordKind.CombatStats + kind)
                            || _pending[18 + slot * 5 + kind].Slot != slot)
                        { _assembling = false; return false; }
                if (_pending[0].E > 0)
                {
                    int active = 0, activeSlots = 0, resultSlots = 0;
                    for (int slot = 0; slot < 8; slot++)
                    {
                        WorldRecord identity = _pending[22 + slot * 5];
                        if (identity.C == 0) continue;
                        if (String.IsNullOrWhiteSpace(identity.PlayerName)) { _assembling = false; return false; }
                        active++; activeSlots |= 1 << slot;
                    }
                    for (int index = 0; index < active; index++)
                    {
                        int slot = (int)((_pending[22 + index * 5].D >> 16) & 255);
                        int bit = 1 << slot;
                        if ((activeSlots & bit) == 0 || (resultSlots & bit) != 0) { _assembling = false; return false; }
                        resultSlots |= bit;
                    }
                }
                if (_pending[17].D > 0 && Sequence32.IsNewer(_pending[17].E, tick))
                { _assembling = false; return false; }
            }
            // Reject duplicate entity keys, even across distinct batches, before any scene writes.
            for (int i = 0; i < total; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (_pending[i].Kind == _pending[j].Kind && _pending[i].Id == _pending[j].Id
                        && (_pending[i].Kind is not (WorldRecordKind.Score or WorldRecordKind.Time or WorldRecordKind.CombatStats
                            or WorldRecordKind.ObjectiveStats or WorldRecordKind.WeaponStats0 or WorldRecordKind.WeaponStats1 or WorldRecordKind.PlayerIdentity)
                            || _pending[i].Slot == _pending[j].Slot))
                    { _assembling = false; return false; }
                }
            }
            (_current, _pending) = (_pending, _current);
            Count = total; Revision = revision; ServerTick = tick;
            HasState = _dirty = true; _assembling = false;
            return true;
        }

        public void Apply(Scene scene, uint? playerSnapshotTick = null)
        {
            if (!_dirty) { return; }
            _dirty = false; _present.Clear();
            if (scene.Match.MatchId != MatchId) { scene.Match.ResetResult(); scene.Match.MatchId = MatchId; }
            bool terminal = !HistoricalProtocol && _current[0].E > 0;
            foreach (WorldRecord state in Records) { if (state.Kind == WorldRecordKind.Item) { _present.Add(state.Id); } }
            int retired = 0;
            foreach (var pair in _items)
            {
                if (!_present.Contains(pair.Key))
                {
                    ItemInstanceEntity item = pair.Value;
                    if (item.Owner?.Item == item) { item.Owner.Item = null; }
                    item.Owner = null;
                    item.DespawnTimer = 0;
                    _retired[retired++] = pair.Key;
                }
            }
            for (int i = 0; i < retired; i++) { _items.Remove(_retired[i]); }
            foreach (WorldRecord state in Records)
            {
                switch (state.Kind)
                {
                    case WorldRecordKind.Item:
                        if (!_items.TryGetValue(state.Id, out ItemInstanceEntity? item))
                        {
                            item = new ItemInstanceEntity(new ItemInstanceEntityData(state.Position, (ItemType)state.Slot, 0), NodeRef.None, scene);
                            _items.Add(state.Id, item);
                            scene.AddEntity(item);
                        }
                        item.Position = state.Position;
                        // A delayed client frame must never expire a live authoritative item.
                        item.DespawnTimer = -1;
                        if (state.A != uint.MaxValue && scene.TryGetEntity(unchecked((int)state.A), out EntityBase? owner)
                            && owner is ItemSpawnEntity spawnerOwner)
                        { item.Owner = spawnerOwner; spawnerOwner.Item = item; }
                        break;
                    case WorldRecordKind.Spawner:
                        if (scene.TryGetEntity(unchecked((int)state.Id), out EntityBase? spawner) && spawner is ItemSpawnEntity itemSpawner)
                        { itemSpawner.ApplyWorldState(state); }
                        break;
                    case WorldRecordKind.Node:
                        if (scene.TryGetEntity(unchecked((int)state.Id), out EntityBase? node) && node is NodeDefenseEntity defense)
                        { defense.ApplyWorldState(state); }
                        break;
                    case WorldRecordKind.Flag:
                        if (scene.TryGetEntity(unchecked((int)state.Id), out EntityBase? flag) && flag is OctolithFlagEntity octolith)
                        { octolith.ApplyWorldState(state); }
                        break;
                    case WorldRecordKind.Match:
                        scene.Match.MatchTime = state.Position.X;
                        MatchRules rules = scene.Match.Rules;
                        int pointGoal = unchecked((int)state.C);
                        if (LegacyProtocol && (rules.LegacyTimeGoal != state.Position.Y || rules.LegacyPointGoal != pointGoal))
                        {
                            scene.Match.ApplyRules(rules.With(objectiveTimeGoal: TimeSpan.FromSeconds(state.Position.Y),
                                startingLives: rules.IsSurvival ? pointGoal : null,
                                scoreGoal: rules.IsSurvival ? null : pointGoal));
                        }
                        if (LegacyProtocol) { scene.Match.LegacyState = (MatchState)state.B; }
                        else
                        {
                            scene.Match.Phase = (MatchPhase)state.B;
                            scene.Match.RadarPlayers = (state.Flags & 2) != 0;
                        }
                        scene.Match.PrimeHunter = unchecked((int)state.D);
                        break;
                    case WorldRecordKind.Lifecycle:
                        scene.Match.PhaseStartTick = state.A;
                        scene.Match.PhaseEndTick = state.B;
                        scene.Match.HasPhaseDeadline = state.Flags != 0;
                        scene.Match.PhaseRevision = state.C;
                        scene.Match.Period = (MatchPeriod)state.D;
                        scene.Match.PeriodStartTick = state.E;
                        break;
                    case WorldRecordKind.CombatStats:
                        PlayerMatchStats stats = scene.Match.Players[state.Slot];
                        if (terminal || !playerSnapshotTick.HasValue || !Sequence32.IsNewer(playerSnapshotTick.Value, ServerTick)) stats.Assists = (int)state.A;
                        stats.DamageDealt = (int)state.B; stats.HeadshotKills = (int)state.C;
                        stats.LongestKillStreak = (int)state.D; stats.KillsAsPrime = (int)state.E;
                        break;
                    case WorldRecordKind.ObjectiveStats:
                        stats = scene.Match.Players[state.Slot];
                        stats.OctolithScores = (int)state.A; stats.OctolithDrops = (int)state.B;
                        stats.OctolithStops = (int)state.C; stats.NodesCaptured = (int)state.D; stats.NodesLost = (int)state.E;
                        break;
                    case WorldRecordKind.WeaponStats0:
                        stats = scene.Match.Players[state.Slot];
                        stats.SetBeamKills(0, (int)state.A); stats.SetBeamKills(1, (int)state.B); stats.SetBeamKills(2, (int)state.C);
                        stats.SetBeamKills(3, (int)state.D); stats.SetBeamKills(4, (int)state.E);
                        break;
                    case WorldRecordKind.WeaponStats1:
                        stats = scene.Match.Players[state.Slot];
                        stats.SetBeamKills(5, (int)state.A); stats.SetBeamKills(6, (int)state.B);
                        stats.SetBeamKills(7, (int)state.C); stats.SetBeamKills(8, (int)state.D); stats.PrimesKilled = (int)state.E;
                        break;
                    case WorldRecordKind.PlayerIdentity:
                        _resultIdentities[state.Slot] = new((Hunter)state.A, unchecked((int)state.B), (state.C & 1) != 0, state.PlayerName, (state.C & 2) != 0);
                        scene.Match.Players[state.Slot].Standings = (int)(state.D & 255);
                        scene.Match.TeamStandings[state.Slot] = (int)((state.D >> 8) & 255);
                        scene.Match.ResultSlots[state.Slot] = (int)((state.D >> 16) & 255);
                        break;
                    case WorldRecordKind.Score:
                        if (terminal || !playerSnapshotTick.HasValue || ServerTick == playerSnapshotTick.Value || Sequence32.IsNewer(ServerTick, playerSnapshotTick.Value))
                        {
                            scene.Match.Players[state.Slot].Points = unchecked((int)state.A); scene.Match.Players[state.Slot].Kills = unchecked((int)state.B);
                            scene.Match.Players[state.Slot].Deaths = unchecked((int)state.C);
                        }
                        scene.Match.TeamPoints[state.Slot] = unchecked((int)state.D);
                        scene.Match.TeamKills[state.Slot] = unchecked((int)state.E);
                        break;
                    case WorldRecordKind.Time:
                        scene.Match.TeamDeaths[state.Slot] = unchecked((int)state.A); scene.Match.Players[state.Slot].Time = WorldRecord.Float(state.B);
                        scene.Match.TeamTime[state.Slot] = WorldRecord.Float(state.C); scene.Match.Players[state.Slot].NodesCaptured = unchecked((int)state.D);
                        scene.Match.Players[state.Slot].OctolithScores = unchecked((int)state.E);
                        break;
                }
            }
            if (terminal && scene.Match.Result == null)
            {
                scene.Match.ActivePlayers = 0;
                foreach (var identity in _resultIdentities) if (identity.Active) scene.Match.ActivePlayers++;
                scene.Match.CaptureReplicatedResult(_current[0].Position.Z, (MatchEndReason)(_current[0].E - 1), _resultIdentities);
            }
        }
    }
}
