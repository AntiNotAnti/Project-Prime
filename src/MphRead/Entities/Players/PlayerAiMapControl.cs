using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private NodeDefenseEntity? _botStrategicNode;
        private ItemInstanceEntity? _botStrategicItem;
        private PlayerEntity? _botStrategicEscort;
        private Vector3 _botStrategicGoal;
        private int _botStrategicTicks;
        private int _botStrategicCooldown;

        private void ResetBotStrategy()
        {
            _botStrategicNode = null;
            _botStrategicItem = null;
            _botStrategicEscort = null;
            _botStrategicGoal = Vector3.Zero;
            _botStrategicTicks = 0;
            _botStrategicCooldown = 0;
        }

        private BotSquadRole BotAssignedRole()
        {
            bool team = _scene.GameState.Teams;
            if (!team) return BotSquadRole.Vanguard;
            int ordinal = 0;
            foreach (PlayerEntity ally in _scene.GetPlayerEntities())
            {
                if (ally != _player && ally.SlotIndex < _player.SlotIndex && ally.IsBot
                    && ally.TeamIndex == _player.TeamIndex
                    && ally.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    ordinal++;
                }
            }
            return BotStrategicPolicy.RoleForOrdinal(ordinal,
                BotStrategicPolicy.IsNodeMode(_scene.GameState.Mode));
        }

        /// <summary>
        /// Tactical close-range direction selection toward visible, already-spawned
        /// map resources, a team objective, or a friendly carrier. Native ROM
        /// navigation remains responsible for long or obstructed routes.
        /// </summary>
        private void ApplyStrategicMapControl()
        {
            bool keepGoal = BotKeepStrategicGoal();
            if (!_scene.GameState.Multiplayer || _player.BotLevel < 2
                || _player.Health <= 0 || !_player.ModInPlay
                || _player.IsAltForm || _player.IsMorphing || _player.IsUnmorphing
                || (_tacticalTarget != null && !keepGoal)
                || (_tacticalMovementActive && !keepGoal)
                || _player.OctolithFlag != null
                || _scene.GameState.IsTokenMode
                    && (uint)_player.SlotIndex < (uint)_scene.GameState.TokenCarried.Length
                    && _scene.GameState.TokenCarried[_player.SlotIndex] > 0)
            {
                ResetBotStrategy();
                return;
            }

            // The native path for a requested pickup or objective must not be
            // overwritten by a local shortcut, especially a needed health pickup.
            if (Flags2.TestFlag(AiFlags2.TargetItem)
                && ((_itemC8 != null && IsHealth(_itemC8))
                    || (_itemSpawnC4 != null && IsHealth(_itemSpawnC4))))
            {
                ResetBotStrategy();
                return;
            }

            if (_botStrategicCooldown > 0)
            {
                _botStrategicCooldown--;
            }

            if (_botStrategicTicks > 0 && BotStrategicGoalValid()
                && BotTryFollowStrategicGoal(_botStrategicGoal))
            {
                _botStrategicTicks--;
                return;
            }
            _botStrategicNode = null;
            _botStrategicItem = null;
            _botStrategicEscort = null;
            _botStrategicTicks = 0;
            if (_botStrategicCooldown > 0) return;

            _botStrategicCooldown = 24;
            bool nodeMode = _scene.GameState.Teams
                && BotStrategicPolicy.IsNodeMode(_scene.GameState.Mode);
            float minimumScore = nodeMode ? 0f : 18f;
            float bestScore = minimumScore; // Real distant objectives stay eligible.
            Vector3 bestPosition = Vector3.Zero;
            NodeDefenseEntity? bestNode = null;
            ItemInstanceEntity? bestItem = null;
            PlayerEntity? bestEscort = null;

            if (_scene.GameState.Teams &&
                BotStrategicPolicy.IsNodeMode(_scene.GameState.Mode))
            {
                BotSquadRole role = BotAssignedRole();
                foreach (NodeDefenseEntity node in _scene.GetNodeDefenseEntities())
                {
                    if (_scene.GameState.IsHardpoint
                        && node.Id != _scene.GameState.ActiveHardpointId)
                    {
                        continue;
                    }

                    float distance = (node.Position - _player.Position).WithY(0).Length;
                    if (!BotCanReachStrategicGoal(node.Position, distance))
                    {
                        continue;
                    }
                    int teammates = 0;
                    int claims = 0;
                    foreach (PlayerEntity ally in _scene.GetPlayerEntities())
                    {
                        if (ally == _player || ally.TeamIndex != _player.TeamIndex
                            || ally.Health == 0 || !ally.ModInPlay)
                        {
                            continue;
                        }
                        if ((ally.Position - node.Position).WithY(0).LengthSquared < 5 * 5)
                            teammates++;
                        if (ally.IsBot && ally.AiData._botStrategicNode == node
                            && ally.AiData._botStrategicTicks > 0)
                            claims++;
                    }

                    float score = BotStrategicPolicy.NodeScore(role, distance,
                        owned: node.CurrentTeam == _player.TeamIndex,
                        contested: node.Contested,
                        hostileOccupier: node.OccupyingTeam != NodeDefenseEntity.NoTeam
                            && node.OccupyingTeam != _player.TeamIndex,
                        teammateClaims: claims, nearbyTeammates: teammates,
                        hardpoint: _scene.GameState.IsHardpoint);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = node.Position;
                        bestNode = node;
                        bestItem = null;
                        bestEscort = null;
                    }
                }
            }

            float healthFraction = _player.HealthMax > 0
                ? Math.Clamp(_player.Health / (float)_player.HealthMax, 0f, 1f) : 1f;
            // Evaluate active items only. Empty respawn sites cannot grant
            // information about the precise future spawn timing.
            bool lockedWeapons = _scene.GameState.InstaGib
                || _scene.GameState.OneInTheChamber
                || _scene.GameState.Fiesta || _scene.GameState.Mode == GameMode.GunGame;
            foreach (ItemInstanceEntity item in _scene.GetItemInstanceEntities())
            {
                if (item.DespawnTimer == 0
                    || !BotStrategicPolicy.PickupAllowedForMode(item.ItemType, lockedWeapons))
                {
                    continue;
                }
                float distance = (item.Position - _player.Position).WithY(0).Length;
                if (!BotCanReachStrategicGoal(item.Position, distance)) continue;

                bool owned = item.ItemType switch
                {
                    ItemType.Imperialist => _player.AvailableWeapons[BeamType.Imperialist],
                    ItemType.Battlehammer => _player.AvailableWeapons[BeamType.Battlehammer],
                    ItemType.VoltDriver => _player.AvailableWeapons[BeamType.VoltDriver],
                    ItemType.Judicator => _player.AvailableWeapons[BeamType.Judicator],
                    ItemType.Magmaul => _player.AvailableWeapons[BeamType.Magmaul],
                    ItemType.ShockCoil => _player.AvailableWeapons[BeamType.ShockCoil],
                    ItemType.AffinityWeapon => _player.AvailableWeapons[
                        Weapons.GetAffinityBeam(_player.Hunter)],
                    _ => false
                };
                float score = BotStrategicPolicy.PickupScore(item.ItemType,
                    healthFraction, owned, _player.ModAmmo.Ua / 120f,
                    _player.ModAmmo.Missiles / 30f) - distance * 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = item.Position;
                    bestNode = null;
                    bestItem = item;
                    bestEscort = null;
                }
            }

            // A single escort, rather than every bot clustering on the carrier.
            // The carrier's live position is friendly information, not wall
            // tracking. Never steal control while the bot is itself carrying.
            if (_scene.GameState.Teams
                && (_scene.GameState.Mode is GameMode.Capture or GameMode.BountyTeams))
            {
                foreach (PlayerEntity ally in _scene.GetPlayerEntities())
                {
                    if (ally == _player || ally.TeamIndex != _player.TeamIndex
                        || ally.Health == 0 || !ally.ModInPlay || ally.OctolithFlag == null)
                        continue;
                    float distance = (ally.Position - _player.Position).WithY(0).Length;
                    if (distance < 6 || distance > 14) continue;

                    int otherEscorts = 0;
                    foreach (PlayerEntity teammate in _scene.GetPlayerEntities())
                    {
                        if (teammate != _player && teammate.IsBot
                            && teammate.TeamIndex == _player.TeamIndex && teammate.Health > 0
                            && teammate.AiData._botStrategicEscort == ally
                            && teammate.AiData._botStrategicTicks > 0)
                        {
                            otherEscorts++;
                        }
                    }
                    Vector3 toAlly = (ally.Position - _player.Position).WithY(0);
                    Vector3 escortGoal = _player.Position + toAlly.Normalized()
                        * Math.Max(0, distance - 4.5f);
                    if (otherEscorts > 0 || !BotCanReachStrategicGoal(escortGoal,
                        (escortGoal - _player.Position).WithY(0).Length)) continue;
                    float score = 34f - distance + (BotAssignedRole() == BotSquadRole.Anchor ? 7f : 0f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = escortGoal;
                        bestNode = null;
                        bestItem = null;
                        bestEscort = ally;
                    }
                }
            }

            if (bestScore <= minimumScore || !BotTryFollowStrategicGoal(bestPosition)) return;

            _botStrategicGoal = bestPosition;
            _botStrategicNode = bestNode;
            _botStrategicItem = bestItem;
            _botStrategicEscort = bestEscort;
            _botStrategicTicks = _player.BotLevel == 3 ? 42 : 32;
        }

        private bool BotStrategicGoalValid()
        {
            if (_botStrategicItem != null)
                return _botStrategicItem.DespawnTimer != 0;
            if (_botStrategicNode != null)
                return (!_scene.GameState.IsHardpoint
                    || _botStrategicNode.Id == _scene.GameState.ActiveHardpointId);
            return _botStrategicEscort != null && _botStrategicEscort.ModInPlay
                && _botStrategicEscort.Health > 0
                && _botStrategicEscort.OctolithFlag != null;
        }

        private bool BotKeepStrategicGoal()
        {
            bool committed = _botStrategicTicks > 0 && BotStrategicGoalValid();
            if (!committed) return false;
            PlayerEntity? enemy = _tacticalTarget;
            bool visible = enemy != null && TacticalTargetValid(enemy) && TacticalCanSee(enemy);
            float range = visible && enemy != null
                ? (enemy.Position - _player.Position).Length : float.PositiveInfinity;
            float health = _player.HealthMax > 0
                ? _player.Health / (float)_player.HealthMax : 1f;
            return BotGoalPolicy.KeepCommittedGoal(committed,
                objectiveGoal: _botStrategicNode != null, visible, range, health);
        }

        private bool BotCanReachStrategicGoal(Vector3 goal, float horizontalDistance)
            => BotTryResolveStrategicWaypoint(goal, horizontalDistance, out _);

        /// <summary>
        /// Follow safe floor segments using the room's existing directed route
        /// table. Native AI retains aerial, alt-form and special transitions.
        /// </summary>
        private bool BotTryResolveStrategicWaypoint(Vector3 goal, float horizontalDistance,
            out Vector3 waypoint)
        {
            waypoint = Vector3.Zero;
            if (!float.IsFinite(horizontalDistance) || horizontalDistance < 1.5f
                || !float.IsFinite(goal.X) || !float.IsFinite(goal.Y)
                || !float.IsFinite(goal.Z))
                return false;

            if (horizontalDistance <= 9f
                && MathF.Abs(goal.Y - _player.Position.Y) <= 1.7f
                && TacticalFloorSafe(goal) && BotDirectRouteClear(goal))
            {
                waypoint = goal;
                return true;
            }

            if (_nodeList == null || _nodeList.Count < 2)
                return false;
            NodeData3 origin = FindClosestNonHazardNodeToPosition(_player.Position);
            NodeData3 destination = FindClosestNonHazardNodeToPosition(goal);
            if (!BotRoutePolicy.TryNextHop(_nodeList, origin, destination, out NodeData3? next)
                || next == null || next.NodeType is not (NodeType.Navigation or NodeType.Vantage))
                return false;
            Vector3 candidate = next.Position;
            float step = (candidate - _player.Position).WithY(0).Length;
            if (step < 0.7f || step > 9f
                || MathF.Abs(candidate.Y - _player.Position.Y) > 1.7f
                || !TacticalFloorSafe(candidate) || !BotDirectRouteClear(candidate))
                return false;
            waypoint = candidate;
            return true;
        }

        private bool BotDirectRouteClear(Vector3 goal)
        {
            Vector3 from = _player.Position.AddY(.8f);
            Vector3 to = goal.AddY(.8f);
            CollisionResult wall = default;
            return !CollisionDetection.CheckBetweenPoints(from, to,
                TestFlags.None, _scene, ref wall)
                && TacticalCanMove(goal - _player.Position);
        }

        private bool BotTryFollowStrategicGoal(Vector3 goal)
        {
            float distance = (goal - _player.Position).WithY(0).Length;
            if (!BotTryResolveStrategicWaypoint(goal, distance, out Vector3 waypoint))
                return false;
            bool oldRight = _buttons.A.IsDown, oldLeft = _buttons.Y.IsDown;
            bool oldForward = _buttons.X.IsDown, oldBack = _buttons.B.IsDown;
            _buttons.A.IsDown = _buttons.Y.IsDown = false;
            _buttons.X.IsDown = _buttons.B.IsDown = false;
            if (!BotFollowCombatGoal(waypoint))
            {
                _buttons.A.IsDown = oldRight;
                _buttons.Y.IsDown = oldLeft;
                _buttons.X.IsDown = oldForward;
                _buttons.B.IsDown = oldBack;
                return false;
            }
            _tacticalMovementActive = true;
            _tacticalSteeringActive = true;
            return true;
        }
    }
}
