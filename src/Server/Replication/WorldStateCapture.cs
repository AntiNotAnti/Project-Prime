using System;
using System.Collections.Generic;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Single simulation-thread capture. Dynamic IDs never recycle within a match.</summary>
    public sealed class WorldStateCapture
    {
        private readonly WorldRecord[] _records = new WorldRecord[WorldPacket.Capacity];
        private readonly Dictionary<ItemInstanceEntity, uint> _items = new(WorldPacket.Capacity);
        private readonly HashSet<ItemInstanceEntity> _alive = new(WorldPacket.Capacity);
        private readonly ItemInstanceEntity[] _retired = new ItemInstanceEntity[WorldPacket.Capacity];
        private uint _nextItemId = 1;
        public uint MatchId { get; private set; }
        public uint Revision { get; private set; }
        public uint ServerTick { get; private set; }
        public int Count { get; private set; }
        public ReadOnlySpan<WorldRecord> Records => _records.AsSpan(0, Count);
        public int BatchCount => (Count + WorldPacket.RecordsPerBatch - 1) / WorldPacket.RecordsPerBatch;

        public void Capture(Scene scene, uint matchId, uint revision, uint serverTick)
        {
            if (!scene.IsHeadless || matchId == 0) { throw new InvalidOperationException("World capture requires an authoritative scene and match."); }
            if (MatchId != matchId) { ValidateRoom(scene); _items.Clear(); _nextItemId = 1; }
            MatchId = matchId; Revision = revision; ServerTick = serverTick; Count = 0;
            Add(new WorldRecord(WorldRecordKind.Match, 255, (ushort)((scene.Match.Rules.Teams ? 1 : 0) | (scene.Match.RadarPlayers ? 2 : 0)), 0,
                new Vector3(scene.Match.MatchTime, scene.Match.Rules.LegacyTimeGoal, 0), (uint)scene.Match.Rules.Mode.ToLegacyMode(), (uint)scene.Match.Phase,
                unchecked((uint)scene.Match.Rules.LegacyPointGoal), unchecked((uint)scene.Match.PrimeHunter), 0));
            for (byte slot = 0; slot < 8; slot++)
            {
                Add(new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)scene.Match.Players[slot].Points), unchecked((uint)scene.Match.Players[slot].Kills),
                    unchecked((uint)scene.Match.Players[slot].Deaths), unchecked((uint)scene.Match.TeamPoints[slot]),
                    unchecked((uint)scene.Match.TeamKills[slot])));
                Add(new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)scene.Match.TeamDeaths[slot]), WorldRecord.Bits(scene.Match.Players[slot].Time), WorldRecord.Bits(scene.Match.TeamTime[slot]),
                    unchecked((uint)scene.Match.Players[slot].NodesCaptured), unchecked((uint)scene.Match.Players[slot].OctolithScores)));
            }
            Add(new WorldRecord(WorldRecordKind.Lifecycle, 255, (ushort)(scene.Match.HasPhaseDeadline ? 1 : 0),
                0, Vector3.Zero, scene.Match.PhaseStartTick, scene.Match.PhaseEndTick,
                scene.Match.PhaseRevision, 0, 0));
            foreach (ItemSpawnEntity spawner in scene.GetItemSpawnEntities())
            {
                Add(new WorldRecord(WorldRecordKind.Spawner, 255, (ushort)(spawner.Active ? 1 : 0),
                    unchecked((uint)spawner.Id), spawner.Position, spawner.ServerRespawnTicks, spawner.ServerSpawnCount, spawner.LastConsumerSlot, 0, 0));
            }
            _alive.Clear();
            foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities())
            {
                if (item.DespawnTimer == 0) { continue; }
                if (_alive.Count == WorldPacket.Capacity) { throw new InvalidOperationException("World live item capacity exhausted."); }
                _alive.Add(item);
            }
            int retired = 0;
            foreach (ItemInstanceEntity item in _items.Keys)
            {
                if (!_alive.Contains(item)) { _retired[retired++] = item; }
            }
            for (int i = 0; i < retired; i++) { _items.Remove(_retired[i]); _retired[i] = null!; }
            foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities())
            {
                if (item.DespawnTimer == 0) { continue; }
                if (!_items.TryGetValue(item, out uint id))
                {
                    if (_items.Count == WorldPacket.Capacity || _nextItemId == 0) { throw new InvalidOperationException("World item identity capacity exhausted."); }
                    _items.Add(item, id = _nextItemId++);
                }
                Add(new WorldRecord(WorldRecordKind.Item, (byte)item.ItemType, 0, id, item.Position,
                    unchecked((uint)(item.Owner?.Id ?? -1)), unchecked((uint)item.DespawnTimer), 0, 0, 0));
            }
            if (scene.Match.Rules.Mode is MatchMode.Nodes or MatchMode.TeamNodes or MatchMode.Defender or MatchMode.TeamDefender)
            {
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities()) { Add(node.CaptureWorldState()); }
            }
            if (scene.Match.Rules.IsOctolithMode)
            {
                foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities()) { Add(flag.CaptureWorldState()); }
            }
        }
        public static void ReleasePlayer(Scene scene, PlayerEntity player)
        {
            if (!scene.IsHeadless) { throw new InvalidOperationException("Only the server releases objective ownership."); }
            foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities()) { node.ReleaseServerPlayer(player); }
            foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities()) { flag.ReleaseServerPlayer(player); }
            if (scene.Match.PrimeHunter == player.SlotIndex) { scene.Match.PrimeHunter = -1; }
        }

        public static void ValidateRoom(Scene scene) => AuthoritativeContent.ValidateRoom(scene);

        private void Add(in WorldRecord record)
        {
            if (Count == _records.Length) { throw new InvalidOperationException("World replication exceeds its 256-record content budget."); }
            _records[Count++] = record;
        }
        public int WriteBatch(Span<byte> destination, int batch) => WorldPacket.Write(destination,
            MatchId, Revision, ServerTick, Records, checked(batch * WorldPacket.RecordsPerBatch));
    }
}
