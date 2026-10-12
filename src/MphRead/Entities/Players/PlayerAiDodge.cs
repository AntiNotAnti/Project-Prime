using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        public partial class PlayerAiData
        {
            private BeamProjectileEntity? _botDodgeObservedProjectile;
            private int _botDodgeReactionFrames;
            private int _botDodgeCommitFrames;
            private Vector3 _botDodgeWorldDirection;

            private void ResetBotDodge()
            {
                _botDodgeObservedProjectile = null;
                _botDodgeReactionFrames = 0;
                _botDodgeCommitFrames = 0;
                _botDodgeWorldDirection = Vector3.Zero;
            }

            /// <summary>
            /// Local, visible projectile anticipation. This never reads an enemy's
            /// hidden inputs or damage outcome, and issues ordinary movement controls.
            /// The reaction delay means even Insane cannot dodge a point-blank shot
            /// that a human would not have time to see.
            /// </summary>
            private void TryBotProjectileDodge()
            {
                if (_player.BotLevel == 0 || _player.Health <= 0 || !_player.ModInPlay)
                {
                    return;
                }

                if (_botDodgeCommitFrames > 0)
                {
                    _botDodgeCommitFrames--;
                    if (TryBotDodgeDirection(_botDodgeWorldDirection))
                    {
                        return;
                    }
                    _botDodgeCommitFrames = 0;
                }

                float horizonTicks = _player.BotLevel == 3 ? 22 : _player.BotLevel == 2 ? 18 : 14;
                float earliestTicks = float.MaxValue;
                BeamProjectileEntity? incoming = null;
                Vector3 closestPoint = Vector3.Zero;
                Vector3 eye = _player.CameraInfo.Position;
                if (eye == Vector3.Zero)
                {
                    eye = _player.Position.AddY(0.85f);
                }
                Vector3 body = _player.Position.AddY(0.75f);
                foreach (BeamProjectileEntity beam in _scene.GetBeamProjectileEntities())
                {
                    if (beam.Lifespan <= 0 || beam.Age < 0 || beam.Flags.TestFlag(BeamFlags.Collided)
                        || beam.Owner == _player
                        || beam.Owner is PlayerEntity ally && ally.TeamIndex == _player.TeamIndex
                        || (beam.Position - body).LengthSquared > 35 * 35)
                    {
                        continue;
                    }

                    Vector3 velocity = beam.Velocity - _player.Speed / 2f;
                    float radius = Math.Clamp(1f + Math.Max(0, beam.SplashRadius) * 0.40f, 1f, 2.25f);
                    if (!BotDodgeMath.ThreatensPosition(beam.Position, velocity, body,
                        radius, horizonTicks, out float ticks, out Vector3 closest)
                        || ticks >= earliestTicks)
                    {
                        continue;
                    }

                    // Knowing a projectile exists in the authoritative world must
                    // not let the bot dodge a shot still concealed by a wall.
                    CollisionResult wall = default;
                    if (CollisionDetection.CheckBetweenPoints(eye, beam.Position,
                        TestFlags.None, _scene, ref wall))
                    {
                        continue;
                    }
                    incoming = beam;
                    earliestTicks = ticks;
                    closestPoint = closest;
                }

                if (incoming == null)
                {
                    _botDodgeObservedProjectile = null;
                    _botDodgeReactionFrames = 0;
                    return;
                }

                if (_botDodgeObservedProjectile != incoming)
                {
                    _botDodgeObservedProjectile = incoming;
                    _botDodgeReactionFrames = _player.BotLevel == 3 ? 4 : _player.BotLevel == 2 ? 7 : 11;
                }
                if (_botDodgeReactionFrames > 0)
                {
                    _botDodgeReactionFrames--;
                    return;
                }

                Vector3 horizontalVelocity = incoming.Velocity.WithY(0);
                if (horizontalVelocity.LengthSquared < 0.001f)
                {
                    return;
                }
                Vector3 side = new(horizontalVelocity.Z, 0, -horizontalVelocity.X);
                side.Normalize();
                float sideDot = Vector3.Dot((_player.Position - closestPoint).WithY(0), side);
                if (MathF.Abs(sideDot) < 0.03f)
                {
                    // Stable tie break, not frame-by-frame random reversals.
                    if (!_tacticalStrafeRight)
                    {
                        side = -side;
                    }
                }
                else if (sideDot < 0)
                {
                    side = -side;
                }

                if (TryBotDodgeDirection(side))
                {
                    _botDodgeWorldDirection = side;
                    _botDodgeCommitFrames = _player.BotLevel == 3 ? 9 : 7;
                }
                else if (TryBotDodgeDirection(-side))
                {
                    _botDodgeWorldDirection = -side;
                    _botDodgeCommitFrames = 6;
                }
            }

            private bool TryBotDodgeDirection(Vector3 desiredWorldDirection)
            {
                Vector3 desired = desiredWorldDirection.WithY(0);
                Vector3 forward = _player.FacingVector.WithY(0);
                if (desired.LengthSquared < 0.001f || forward.LengthSquared < 0.001f)
                {
                    return false;
                }
                desired.Normalize();
                forward.Normalize();
                Vector3 right = new(forward.Z, 0, -forward.X);
                AiButton? chosen = null;
                float best = 0.15f;

                void Consider(Vector3 direction, AiButton button)
                {
                    float utility = Vector3.Dot(direction, desired);
                    if (utility > best && TacticalCanMove(direction))
                    {
                        chosen = button;
                        best = utility;
                    }
                }

                Consider(right, _buttons.A);
                Consider(-right, _buttons.Y);
                Consider(forward, _buttons.X);
                Consider(-forward, _buttons.B);
                if (chosen == null)
                {
                    return false;
                }

                _buttons.A.IsDown = false;
                _buttons.Y.IsDown = false;
                _buttons.X.IsDown = false;
                _buttons.B.IsDown = false;
                chosen.IsDown = true;
                _tacticalMovementActive = true;
                _tacticalSteeringActive = true;
                return true;
            }
        }
    }
}
