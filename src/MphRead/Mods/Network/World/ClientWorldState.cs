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
        public uint Revision { get; private set; }
        public uint ServerTick { get; private set; }
        public int Count { get; private set; }
        public bool HasState { get; private set; }
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
            if (!WorldPacket.TryValidate(body, MatchId)) { return false; }
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
                WorldRecord.TryRead(body.Slice(WorldPacket.HeaderSize + i * WorldRecord.Size, WorldRecord.Size), out _pending[index]);
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
            // Reject duplicate entity keys, even across distinct batches, before any scene writes.
            for (int i = 0; i < total; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (_pending[i].Kind == _pending[j].Kind && _pending[i].Id == _pending[j].Id
                        && (_pending[i].Kind is not (WorldRecordKind.Score or WorldRecordKind.Time) || _pending[i].Slot == _pending[j].Slot))
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
                        if (rules.LegacyTimeGoal != state.Position.Y || rules.LegacyPointGoal != pointGoal)
                        {
                            scene.Match.ApplyRules(rules.With(objectiveTimeGoal: TimeSpan.FromSeconds(state.Position.Y),
                                startingLives: rules.IsSurvival ? pointGoal : null,
                                scoreGoal: rules.IsSurvival ? null : pointGoal));
                        }
                        scene.Match.LegacyState = (MatchState)state.B;
                        scene.Match.PrimeHunter = unchecked((int)state.D);
                        break;
                    case WorldRecordKind.Score:
                        if (!playerSnapshotTick.HasValue || ServerTick == playerSnapshotTick.Value || Sequence32.IsNewer(ServerTick, playerSnapshotTick.Value))
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
        }
    }
}
