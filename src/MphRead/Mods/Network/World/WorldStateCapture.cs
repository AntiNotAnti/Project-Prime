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
            Add(new WorldRecord(WorldRecordKind.Match, 255, (ushort)(GameState.Teams ? 1 : 0), 0,
                new Vector3(GameState.MatchTime, GameState.TimeGoal, 0), (uint)GameState.Mode, (uint)GameState.MatchState,
                unchecked((uint)GameState.PointGoal), unchecked((uint)GameState.PrimeHunter), 0));
            for (byte slot = 0; slot < 8; slot++)
            {
                Add(new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)GameState.Points[slot]), unchecked((uint)GameState.Kills[slot]),
                    unchecked((uint)GameState.Deaths[slot]), unchecked((uint)GameState.TeamPoints[slot]),
                    unchecked((uint)GameState.TeamKills[slot])));
                Add(new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)GameState.TeamDeaths[slot]), WorldRecord.Bits(GameState.Time[slot]), WorldRecord.Bits(GameState.TeamTime[slot]),
                    unchecked((uint)GameState.NodesCaptured[slot]), unchecked((uint)GameState.OctolithScores[slot])));
            }
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
            if (GameState.Mode is GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams)
            {
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities()) { Add(node.CaptureWorldState()); }
            }
            if (GameState.IsOctolithMode)
            {
                foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities()) { Add(flag.CaptureWorldState()); }
            }
        }
        public static void ReleasePlayer(Scene scene, PlayerEntity player)
        {
            if (!scene.IsHeadless) { throw new InvalidOperationException("Only the server releases objective ownership."); }
            foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities()) { node.ReleaseServerPlayer(player); }
            foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities()) { flag.ReleaseServerPlayer(player); }
            if (GameState.PrimeHunter == player.SlotIndex) { GameState.PrimeHunter = -1; }
        }

        public static void ValidateRoom(Scene scene)
        {
            foreach (EntityBase entity in scene.Entities)
            {
                if (entity.Type is EntityType.Door or EntityType.ForceField or EntityType.Platform)
                {
                    throw new ProgramException($"Authoritative multiplayer does not support {entity.Type} entity {entity.Id} in room {scene.RoomId}. "
                        + "Retail multiplayer rooms do not contain mutable door, forcefield or platform entities.");
                }
            }
        }

        private void Add(in WorldRecord record)
        {
            if (Count == _records.Length) { throw new InvalidOperationException("World replication exceeds its 256-record content budget."); }
            _records[Count++] = record;
        }
        public int WriteBatch(Span<byte> destination, int batch) => WorldPacket.Write(destination,
            MatchId, Revision, ServerTick, Records, checked(batch * WorldPacket.RecordsPerBatch));
    }
}
