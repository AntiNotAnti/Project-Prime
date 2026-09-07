using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>Scene-owned spawn selection. Classic does not advance any RNG stream.</summary>
    public sealed class SpawnDirector
    {
        private readonly Scene _scene;
        private readonly PlayerSpawnEntity?[] _classicSafe = new PlayerSpawnEntity?[25];
        private readonly SpawnDangerHistory _history = new();
        private uint _randomState;
        public uint RandomState => _randomState;
        public int DeathHistoryCount => _history.DeathCount;
        public int SpawnHistoryCount => _history.SpawnCount;
        public SpawnCandidate? LastSelection { get; private set; }

        public SpawnDirector(Scene scene) { _scene = scene; }

        public void Reset(uint seed)
        {
            _randomState = seed;
            _history.Reset();
            Array.Clear(_classicSafe);
            LastSelection = null;
        }

        public void RecordDeath(Vector3 position)
        {
            if (!_scene.Services.IsReplica) { _history.RecordDeath(position, _scene.FrameCount); }
        }

        public PlayerSpawnEntity? Select(PlayerEntity requester)
        {
            LastSelection = null;
            PlayerSpawnEntity? chosen = _scene.Match.Rules.SpawnPolicy == SpawnPolicy.Classic
                ? SelectClassic(requester) : SelectWeighted(requester);
            if (chosen != null)
            {
                chosen.Cooldown = 2 * SimTicks.TicksPer30HzFrame;
                _history.RecordSpawn(chosen.Id, requester.SlotIndex, _scene.FrameCount);
            }
            return chosen;
        }

        private PlayerSpawnEntity? SelectClassic(PlayerEntity requester)
        {
            int limit = 0;
            int safeCount = 0;
            PlayerSpawnEntity? best = null;
            float bestDistance = 0;
            foreach (PlayerSpawnEntity candidate in _scene.GetPlayerSpawnEntities())
            {
                if (limit++ >= 25) { break; }
                if (!candidate.IsActive || candidate.Cooldown != 0 || _scene.FrameCount == 0 && candidate.Availability)
                    continue;
                if (_scene.Match.Rules.Mode == MatchMode.Capture && candidate.Data.TeamIndex != -1
                    && candidate.Data.TeamIndex != requester.TeamIndex) { continue; }
                float minDistSqr = 100;
                foreach (PlayerEntity player in _scene.GetPlayerEntities())
                {
                    if (player.Health > 0)
                    {
                        Vector3 between = candidate.Position - player.Position;
                        float distSqr = Vector3.Dot(between, between);
                        if (distSqr < minDistSqr) { minDistSqr = distSqr; }
                    }
                }
                if (minDistSqr >= 100) { _classicSafe[safeCount++] = candidate; }
                else if (minDistSqr > bestDistance) { bestDistance = minDistSqr; best = candidate; }
            }
            PlayerSpawnEntity? chosen = safeCount > 0
                ? _classicSafe[(int)(_scene.FrameCount % (ulong)safeCount)] : best;
            // Classic deliberately relaxes every filter except active in this legacy fallback.
            if (chosen == null)
                foreach (PlayerSpawnEntity fallback in _scene.GetPlayerSpawnEntities())
                    if (fallback.IsActive) { chosen = fallback; break; }
            Array.Clear(_classicSafe, 0, safeCount);
            return chosen;
        }

        private PlayerSpawnEntity? SelectWeighted(PlayerEntity requester)
        {
            PlayerSpawnEntity? best = null;
            float bestScore = Single.NegativeInfinity;
            uint ties = 0;
            // The second pass relaxes cooldown only. Team, active, authored availability,
            // and finite-position requirements remain hard even if no point is eligible.
            for (int pass = 0; pass < 2 && best == null; pass++)
            {
                foreach (PlayerSpawnEntity candidate in _scene.GetPlayerSpawnEntities())
                {
                    if (!candidate.IsActive || _scene.FrameCount == 0 && candidate.Availability
                        || !Single.IsFinite(candidate.Position.X) || !Single.IsFinite(candidate.Position.Y)
                        || !Single.IsFinite(candidate.Position.Z)) { continue; }
                    if (_scene.Match.Rules.Teams && candidate.Data.TeamIndex != -1
                        && candidate.Data.TeamIndex != requester.TeamIndex) { continue; }
                    if (pass == 0 && candidate.Cooldown != 0) { continue; }
                    SpawnCandidate score = Score(candidate, requester, pass != 0);
                    bool select = score.Score > bestScore;
                    if (select) { ties = 1; }
                    else if (score.Score == bestScore)
                    {
                        ties++;
                        // Private match stream; no global combat/AI randomness is consumed.
                        _randomState = unchecked(_randomState * 1664525u + 1013904223u);
                        select = _randomState % ties == 0;
                    }
                    if (select) { bestScore = score.Score; best = candidate; LastSelection = score; }
                }
            }
            return best;
        }

        /// <summary>Read-only score diagnostics; does not select, advance RNG, or commit history.</summary>
        public SpawnCandidate Evaluate(PlayerSpawnEntity candidate, PlayerEntity requester)
            => Score(candidate, requester, fallback: false);

        private static float Proximity(Vector3 point, Vector3 danger)
            => Math.Max(0, 1 - (point - danger).LengthSquared / 100);

        private SpawnCandidate Score(PlayerSpawnEntity candidate, PlayerEntity requester, bool fallback)
        {
            bool duel = _scene.Match.Rules.SpawnPolicy == SpawnPolicy.Duel;
            float nearest = 900;
            int visible = 0, facing = 0, nearby = 0;
            float friendly = 0;
            Vector3 spawnEye = candidate.Position.AddY(0.5f);
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player == requester || player.Health <= 0 || !player.LoadFlags.TestFlag(LoadFlags.Active)) { continue; }
                Vector3 between = candidate.Position - player.Position;
                float distance = between.LengthSquared;
                if (_scene.Match.Rules.Teams && player.TeamIndex == requester.TeamIndex)
                {
                    if (distance < 100) { friendly += 50 * (1 - distance / 100); }
                    continue;
                }
                nearest = Math.Min(nearest, distance);
                if (distance < 100) { nearby++; }
                CollisionResult collision = default;
                if (!CollisionDetection.CheckBetweenPoints(player.Position.AddY(0.5f), spawnEye,
                    TestFlags.None, _scene, ref collision))
                {
                    visible++;
                    if (distance > 0 && Vector3.Dot(player.FacingVector, between / MathF.Sqrt(distance)) > 0.5f)
                        facing++;
                }
            }
            float deaths = _history.DeathDanger(candidate.Position, _scene.FrameCount) * (duel ? 500 : 250);
            float uses = _history.RecentUse(candidate.Id, requester.SlotIndex, _scene.FrameCount, out bool repeat)
                * (duel ? 200 : 100) + (repeat ? (duel ? 700 : 350) : 0);
            float objective = 0;
            if (_scene.Match.Rules.Mode is MatchMode.Nodes or MatchMode.TeamNodes or MatchMode.Defender or MatchMode.TeamDefender)
                foreach (NodeDefenseEntity node in _scene.GetNodeDefenseEntities())
                    if (node.Contested || node.CurrentTeam != NodeDefenseEntity.NeutralTeam && node.CurrentTeam != requester.TeamIndex)
                        objective += Proximity(candidate.Position, node.Position) * 150;
            if (_scene.Match.Rules.Mode == MatchMode.Capture)
                foreach (FlagBaseEntity flag in _scene.GetFlagBaseEntities())
                    if (flag.Data.TeamId != requester.TeamIndex)
                        objective += Proximity(candidate.Position, flag.Position) * 150;
            float resources = 0;
            foreach (ItemSpawnEntity item in _scene.GetItemSpawnEntities())
                if ((item.Active || item.AlwaysActive) && item.Item != null
                    && item.Data.ItemType is ItemType.HealthBig or ItemType.DoubleDamage or ItemType.OmegaCannon or ItemType.Deathalt)
                    resources += Proximity(candidate.Position, item.Position) * (duel ? 300 : 75);
            float score = nearest - visible * (duel ? 400 : 200) - facing * (duel ? 300 : 150)
                - nearby * 100 - deaths - uses + friendly - objective - resources;
            return new SpawnCandidate(candidate.Id, score, nearest, visible, facing, nearby, deaths, uses, friendly, objective, resources, fallback);
        }
    }
}
