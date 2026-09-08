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
        public uint MatchId { get; private set; }
        public uint Revision { get; private set; }
        public uint ServerTick { get; private set; }
        public int Count { get; private set; }
        public ReadOnlySpan<WorldRecord> Records => _records.AsSpan(0, Count);
        public int BatchCount => (Count + WorldPacket.RecordsPerBatch - 1) / WorldPacket.RecordsPerBatch;

        public void Capture(Scene scene, uint matchId, uint revision, uint serverTick)
        {
            if (!scene.IsHeadless || matchId == 0) { throw new InvalidOperationException("World capture requires an authoritative scene and match."); }
            if (MatchId != matchId) { ValidateRoom(scene); _items.Clear(); }
            MatchId = matchId; Revision = revision; ServerTick = serverTick; Count = 0;
            MatchRuntime match = scene.Match;
            MatchResult? result = match.Result;
            MatchRules rules = result?.Rules ?? match.Rules;
            Add(new WorldRecord(WorldRecordKind.Match, 255, (ushort)((rules.Teams ? 1 : 0) | (match.RadarPlayers ? 2 : 0)), 0,
                new Vector3(result?.RemainingMatchTime ?? match.MatchTime, rules.LegacyTimeGoal, result?.CompletedAtSimulationTime ?? 0),
                (uint)rules.Mode.ToLegacyMode(), (uint)match.Phase,
                unchecked((uint)rules.LegacyPointGoal), unchecked((uint)(result?.PrimeHunter ?? match.PrimeHunter)),
                result == null ? 0 : (uint)result.EndReason + 1));
            for (byte slot = 0; slot < 8; slot++)
            {
                PlayerMatchStats p = match.Players[slot];
                PlayerMatchResult? r = result?.Players[slot];
                TeamMatchResult? team = result?.Teams[slot];
                Add(new WorldRecord(WorldRecordKind.Score, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)(r?.Points ?? p.Points)), unchecked((uint)(r?.Kills ?? p.Kills)),
                    unchecked((uint)(r?.Deaths ?? p.Deaths)), unchecked((uint)(team?.Points ?? match.TeamPoints[slot])),
                    unchecked((uint)(team?.Kills ?? match.TeamKills[slot]))));
                Add(new WorldRecord(WorldRecordKind.Time, slot, 0, 0, Vector3.Zero,
                    unchecked((uint)(team?.Deaths ?? match.TeamDeaths[slot])), WorldRecord.Bits(r?.Time ?? p.Time),
                    WorldRecord.Bits(team?.Time ?? match.TeamTime[slot]), unchecked((uint)(r?.NodesCaptured ?? p.NodesCaptured)),
                    unchecked((uint)(r?.OctolithScores ?? p.OctolithScores))));
            }
            Add(new WorldRecord(WorldRecordKind.Lifecycle, 255, (ushort)(match.HasPhaseDeadline ? 1 : 0),
                0, Vector3.Zero, match.PhaseStartTick, match.PhaseEndTick,
                match.PhaseRevision, (uint)match.Period, match.PeriodStartTick));
            for (byte slot = 0; slot < 8; slot++)
            {
                PlayerMatchStats p = match.Players[slot];
                PlayerMatchResult? r = result?.Players[slot];
                Add(new(WorldRecordKind.CombatStats, slot, 0, 0, Vector3.Zero,
                    (uint)(r?.Assists ?? p.Assists), (uint)(r?.DamageDealt ?? p.DamageDealt),
                    (uint)(r?.HeadshotKills ?? p.HeadshotKills), (uint)(r?.LongestKillStreak ?? p.LongestKillStreak),
                    (uint)(r?.KillsAsPrime ?? p.KillsAsPrime)));
                Add(new(WorldRecordKind.ObjectiveStats, slot, 0, 0, Vector3.Zero,
                    (uint)(r?.OctolithScores ?? p.OctolithScores), (uint)(r?.OctolithDrops ?? p.OctolithDrops),
                    (uint)(r?.OctolithStops ?? p.OctolithStops), (uint)(r?.NodesCaptured ?? p.NodesCaptured), (uint)(r?.NodesLost ?? p.NodesLost)));
                uint Beam(int weapon) => (uint)(r?.BeamKills[weapon] ?? p.GetBeamKills(weapon));
                Add(new(WorldRecordKind.WeaponStats0, slot, 0, 0, Vector3.Zero, Beam(0), Beam(1), Beam(2), Beam(3), Beam(4)));
                Add(new(WorldRecordKind.WeaponStats1, slot, 0, 0, Vector3.Zero, Beam(5), Beam(6), Beam(7), Beam(8), (uint)(r?.PrimesKilled ?? p.PrimesKilled)));
                PlayerEntity player = scene.Players[slot];
                uint standings = (uint)(r?.Standings ?? p.Standings) | ((uint)(r?.TeamStanding ?? match.TeamStandings[slot]) << 8)
                    | ((uint)(result?.ResultSlots[slot] ?? match.ResultSlots[slot]) << 16);
                Add(new WorldRecord(WorldRecordKind.PlayerIdentity, slot, 0, 0, Vector3.Zero,
                    (uint)(r?.Hunter ?? player.Hunter), unchecked((uint)(r?.TeamIndex ?? player.TeamIndex)),
                    ((r?.Active ?? player.LoadFlags.TestFlag(LoadFlags.Active)) ? 1u : 0u)
                        | ((r?.IsBot ?? player.IsBot) ? 2u : 0u), standings, 0)
                    { PlayerName = r?.Nickname ?? scene.Roster.Nicknames[slot] });
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
                    if (_items.Count == WorldPacket.Capacity) { throw new InvalidOperationException("World item identity capacity exhausted."); }
                    id = scene.Services.GetWorldEntityId(scene, item);
                    if (id == 0) throw new InvalidOperationException("World capture requires scene-owned item identity.");
                    _items.Add(item, id);
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
