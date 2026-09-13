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
        private readonly SpawnCandidate?[] _selectionBySlot = new SpawnCandidate?[PlayerEntity.SlotCapacity];
        private readonly ulong[] _selectionTickBySlot = new ulong[PlayerEntity.SlotCapacity];
        private readonly Vector3[] _selectionPositionBySlot = new Vector3[PlayerEntity.SlotCapacity];
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
            Array.Clear(_selectionBySlot);
            Array.Clear(_selectionTickBySlot);
            Array.Clear(_selectionPositionBySlot);
            LastSelection = null;
        }

        public void RecordDeath(Vector3 position)
        {
            if (!_scene.Services.IsReplica) { _history.RecordDeath(position, _scene.FrameCount); }
        }

        public PlayerSpawnEntity? Select(PlayerEntity requester)
        {
            LastSelection = null;
            if ((uint)requester.SlotIndex < (uint)_selectionBySlot.Length)
                _selectionBySlot[requester.SlotIndex] = null;
            PlayerSpawnEntity? chosen = _scene.Match.Rules.SpawnPolicy == SpawnPolicy.Classic
                ? SelectClassic(requester) : SelectWeighted(requester);
            if (chosen != null)
            {
                chosen.Cooldown = 2 * SimTicks.TicksPer30HzFrame;
                _history.RecordSpawn(chosen.Id, requester.SlotIndex,
                    requester.TeamIndex, chosen.Position, _scene.FrameCount);
                if (LastSelection is { } selection
                    && (uint)requester.SlotIndex < (uint)_selectionBySlot.Length)
                {
                    _selectionBySlot[requester.SlotIndex] = selection;
                    _selectionTickBySlot[requester.SlotIndex] = _scene.FrameCount;
                    _selectionPositionBySlot[requester.SlotIndex] = chosen.Position.AddY(1);
                }
            }
            return chosen;
        }

        /// <summary>
        /// Associates a committed enhanced-policy selection with the spawn event
        /// emitted later in the same authoritative tick. This keeps telemetry
        /// read-only and prevents a stale candidate from being attached to a
        /// later life at the same coordinates.
        /// </summary>
        public bool TryGetLastSelection(int slot, Vector3 finalPosition, ulong tick,
            out SpawnCandidate selection)
        {
            if ((uint)slot < (uint)_selectionBySlot.Length
                && _selectionBySlot[slot] is { } value
                && _selectionTickBySlot[slot] == tick
                && (_selectionPositionBySlot[slot] - finalPosition).LengthSquared <= 0.0001f)
            {
                selection = value;
                return true;
            }
            selection = default;
            return false;
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
                if (!candidate.IsActive || candidate.Cooldown != 0
                    || _scene.FrameCount == 0 && candidate.Availability
                    || !SpawnGeometry.IsSafe(_scene, candidate))
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
            // Classic retains the legacy fallback, but never bypasses the
            // body/camera safety gate that prevents black-screen lives.
            if (chosen == null)
                foreach (PlayerSpawnEntity fallback in _scene.GetPlayerSpawnEntities())
                    if (fallback.IsActive && SpawnGeometry.IsSafe(_scene, fallback))
                    { chosen = fallback; break; }
            Array.Clear(_classicSafe, 0, safeCount);
            return chosen;
        }

        private PlayerSpawnEntity? SelectWeighted(PlayerEntity requester)
        {
            PlayerSpawnEntity? best = null;
            float bestScore = Single.NegativeInfinity;
            uint ties = 0;
            bool mayRelaxTeam = _scene.Match.Rules.TeamCount > 2
                && _scene.Match.Rules.Mode is MatchMode.TeamBattle
                    or MatchMode.TeamSurvival;
            // Cooldown is relaxed before an immediate-hazard fallback. Team
            // affinity is relaxed only for non-objective team deathmatch modes;
            // Capture bases remain a hard ownership boundary.
            for (int teamPass = 0; teamPass < (mayRelaxTeam ? 2 : 1) && best == null;
                teamPass++)
            {
                bool teamFallback = teamPass != 0;
                for (int pass = 0; pass < 3 && best == null; pass++)
                {
                    bool cooldownFallback = pass != 0;
                    bool hazardFallback = pass == 2;
                    foreach (PlayerSpawnEntity candidate in _scene.GetPlayerSpawnEntities())
                    {
                        if (!candidate.IsActive
                            || _scene.FrameCount == 0 && candidate.Availability
                            || !SpawnGeometry.IsSafe(_scene, candidate)) { continue; }
                        if (!teamFallback && !TeamEligible(candidate, requester)) { continue; }
                        if (!cooldownFallback && candidate.Cooldown != 0) { continue; }
                        SpawnCandidate score = Score(candidate, requester,
                            cooldownFallback, hazardFallback, teamFallback);
                        if (!hazardFallback && score.ImmediateHazard) { continue; }
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
            }
            return best;
        }

        private bool TeamEligible(PlayerSpawnEntity candidate, PlayerEntity requester)
        {
            if (!_scene.Match.Rules.Teams || candidate.Data.TeamIndex == -1)
                return true;
            return candidate.Data.TeamIndex == requester.TeamIndex;
        }

        /// <summary>Read-only score diagnostics; does not select, advance RNG, or commit history.</summary>
        public SpawnCandidate Evaluate(PlayerSpawnEntity candidate, PlayerEntity requester)
            => Score(candidate, requester, cooldownFallback: false,
                hazardFallback: false, teamFallback: false);

        private static float Proximity(Vector3 point, Vector3 danger)
            => Math.Max(0, 1 - (point - danger).LengthSquared / 100);

        private SpawnCandidate Score(PlayerSpawnEntity candidate, PlayerEntity requester,
            bool cooldownFallback, bool hazardFallback, bool teamFallback)
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
            float hazards = HazardDanger(candidate.Position, requester,
                out bool immediateHazard) * (duel ? 1.5f : 1);
            float reservations = _history.ReservationDanger(candidate.Position,
                requester.TeamIndex, _scene.FrameCount) * (duel ? 800 : 500);
            float score = nearest - visible * (duel ? 400 : 200) - facing * (duel ? 300 : 150)
                - nearby * 100 - deaths - uses + friendly - objective - resources
                - hazards - reservations;
            return new SpawnCandidate(candidate.Id, score, nearest, visible, facing,
                nearby, deaths, uses, friendly, objective, resources, hazards,
                reservations, immediateHazard, cooldownFallback, hazardFallback,
                teamFallback);
        }

        private float HazardDanger(Vector3 position, PlayerEntity requester,
            out bool immediate)
        {
            immediate = false;
            float penalty = 0;
            Vector3 body = position.AddY(1.5f);
            foreach (BeamProjectileEntity beam in _scene.GetBeamProjectileEntities())
            {
                if (beam.RemainingLifeTicks <= 0 || !Hostile(beam.Owner, requester))
                    continue;
                float radius = Math.Max(0.25f, beam.CylinderRadius) + 0.75f;
                float segmentDistance = DistanceSquaredToSegment(body,
                    beam.BackPosition, beam.Position);
                if (segmentDistance <= radius * radius)
                {
                    immediate = true;
                    penalty += 2500;
                }
                penalty += Proximity(position, beam.Position) * 300;
            }
            foreach (BombEntity bomb in _scene.GetBombEntities())
            {
                if (!bomb.IsLive || !Hostile(bomb.Owner, requester)) continue;
                float radius = Math.Max(1.5f, Math.Max(bomb.Radius,
                    bomb.SelfRadius) + 0.75f);
                float distance = (body - bomb.Position).LengthSquared;
                if (distance <= radius * radius)
                {
                    immediate = true;
                    penalty += 3000;
                }
                penalty += Proximity(position, bomb.Position) * 500;
            }
            return penalty;
        }

        private bool Hostile(EntityBase? owner, PlayerEntity requester)
        {
            PlayerEntity? player = owner switch
            {
                PlayerEntity value => value,
                HalfturretEntity value => value.Owner,
                _ => null
            };
            if (player == requester) return false;
            return player == null || !_scene.Match.Rules.Teams
                || _scene.Match.Rules.FriendlyFire
                || player.TeamIndex != requester.TeamIndex;
        }

        private static float DistanceSquaredToSegment(Vector3 point, Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.LengthSquared;
            if (!(lengthSquared > 0.0001f) || !Single.IsFinite(lengthSquared))
                return (point - end).LengthSquared;
            float amount = Math.Clamp(Vector3.Dot(point - start, segment)
                / lengthSquared, 0, 1);
            return (point - (start + segment * amount)).LengthSquared;
        }
    }
}
