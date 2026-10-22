using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private Vector3 _botCombatGoal;
        private int _botCombatGoalFrames;
        private int _botCombatGoalTargetSlot = -1;

        private void ResetBotPositioning()
        {
            _botCombatGoal = Vector3.Zero;
            _botCombatGoalFrames = 0;
            _botCombatGoalTargetSlot = -1;
        }

        /// <summary>
        /// Only a short local positioning goal, not a replacement for the native
        /// room graph. Pick at most eight collision/floor-safe endpoints every
        /// ~20 ticks, and preserve that goal while steering toward it.
        /// </summary>
        private bool BotPlanCombatMove(PlayerEntity target, float preferredRange, bool lowHealth)
        {
            if (_botCombatGoalFrames > 0 && _botCombatGoalTargetSlot == target.SlotIndex
                && (_botCombatGoal - _player.Position).WithY(0).LengthSquared > 0.64f
                && BotFollowCombatGoal(_botCombatGoal))
            {
                _botCombatGoalFrames--;
                return true;
            }

            Vector3 toEnemy = (target.Position - _player.Position).WithY(0);
            if (toEnemy.LengthSquared < 0.001f)
            {
                ResetBotPositioning();
                return false;
            }
            Vector3 radial = toEnemy.Normalized();
            Vector3 lateral = new(radial.Z, 0, -radial.X);

            // Use world-relative candidate directions: facing turns should not
            // rotate an already chosen path around the bot.
            Span<Vector3> candidates = stackalloc Vector3[8];
            candidates[0] = radial;
            candidates[1] = -radial;
            candidates[2] = lateral;
            candidates[3] = -lateral;
            candidates[4] = (radial + lateral).Normalized();
            candidates[5] = (radial - lateral).Normalized();
            candidates[6] = (-radial + lateral).Normalized();
            candidates[7] = (-radial - lateral).Normalized();

            Vector3 enemyForward = target.FacingVector.WithY(0);
            enemyForward = enemyForward.LengthSquared > 0.001f
                ? enemyForward.Normalized() : -radial;
            Vector3 enemyView = target.CameraInfo.Position;
            if (enemyView == Vector3.Zero)
            {
                enemyView = target.Position.AddY(target.IsAltForm
                    ? Math.Max(.35f, Fixed.ToFloat(target.Values.AltColYPos)) : 1f);
            }

            float currentDistance = toEnemy.Length;
            float bestScore = float.NegativeInfinity;
            Vector3 bestGoal = Vector3.Zero;
            const float Step = 2.4f;

            foreach (Vector3 dir in candidates)
            {
                if (!TacticalCanMove(dir))
                {
                    continue;
                }
                Vector3 endpoint = _player.Position + dir * Step;
                if (!TacticalFloorSafe(endpoint))
                {
                    continue;
                }

                float nextDistance = (target.Position - endpoint).WithY(0).Length;
                Vector3 fromEnemy = (endpoint - target.Position).WithY(0);
                float facingDot = fromEnemy.LengthSquared > 0.001f
                    ? Vector3.Dot(fromEnemy.Normalized(), enemyForward) : 1f;

                bool cover = false;
                if (lowHealth)
                {
                    CollisionResult wall = default;
                    cover = CollisionDetection.CheckBetweenPoints(
                        endpoint.AddY(1f), enemyView, TestFlags.None, _scene, ref wall);
                }

                float crowdPenalty = 0;
                foreach (PlayerEntity ally in _scene.GetPlayerEntities())
                {
                    if (ally == _player || !ally.ModInPlay || ally.Health == 0
                        || ally.TeamIndex != _player.TeamIndex)
                    {
                        continue;
                    }
                    float allyDistance = (ally.Position - endpoint).WithY(0).Length;
                    crowdPenalty += Math.Max(0, 3.5f - allyDistance) * 1.5f;
                }

                float commitment = _botCombatGoalFrames > 0
                    && (endpoint - _botCombatGoal).LengthSquared < 3f
                    ? 3f : 0f;
                float score = BotPositionUtility.Score(
                    currentDistance, nextDistance, preferredRange,
                    lowHealth, cover, facingDot, crowdPenalty, commitment);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGoal = endpoint;
                }
            }

            if (!float.IsFinite(bestScore))
            {
                ResetBotPositioning();
                return false;
            }

            _botCombatGoal = bestGoal;
            _botCombatGoalTargetSlot = target.SlotIndex;
            _botCombatGoalFrames = _player.BotLevel == 3 ? 18 : 25;
            if (!BotFollowCombatGoal(bestGoal))
            {
                ResetBotPositioning();
                return false;
            }
            return true;
        }

        private bool BotFollowCombatGoal(Vector3 worldGoal)
        {
            Vector3 direction = (worldGoal - _player.Position).WithY(0);
            if (direction.LengthSquared < 0.36f)
            {
                return false;
            }
            direction.Normalize();
            if (!TacticalCanMove(direction))
            {
                return false;
            }

            Vector3 forward = _player.FacingVector.WithY(0);
            if (forward.LengthSquared < 0.001f)
            {
                return false;
            }
            forward.Normalize();
            Vector3 right = new(forward.Z, 0, -forward.X);
            float forwardAmount = Vector3.Dot(direction, forward);
            float strafeAmount = Vector3.Dot(direction, right);

            // The tactical layer cleared the old movement input before reaching
            // here. Emit one diagonal/axial movement request to the shared motor.
            if (MathF.Abs(strafeAmount) >= 0.35f)
            {
                _buttons.A.IsDown = strafeAmount > 0;
                _buttons.Y.IsDown = strafeAmount < 0;
            }
            if (MathF.Abs(forwardAmount) >= 0.35f)
            {
                _buttons.X.IsDown = forwardAmount > 0;
                _buttons.B.IsDown = forwardAmount < 0;
            }
            return true;
        }
    }
}
