using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        public partial class PlayerAiData
        {
            private readonly struct BotMotorTuning
            {
                public float MoveAcceleration { get; }
                public float MoveBraking { get; }
                public float MaxYawPerTick { get; }
                public float MaxPitchPerTick { get; }
                public float TurnAcceleration { get; }

                public BotMotorTuning(float moveAcceleration, float moveBraking,
                    float maxYawPerTick, float maxPitchPerTick, float turnAcceleration)
                {
                    MoveAcceleration = moveAcceleration;
                    MoveBraking = moveBraking;
                    MaxYawPerTick = maxYawPerTick;
                    MaxPitchPerTick = maxPitchPerTick;
                    TurnAcceleration = turnAcceleration;
                }
            }

            // Values are per 60 Hz simulation tick, independent of render FPS.
            // Insane can still flick quickly, but not 35 degrees in one step.
            private static readonly BotMotorTuning[] _motorTuning =
            [
                new(0.18f, 0.28f, 3.5f, 3.0f, 0.85f), // Easy
                new(0.24f, 0.34f, 6.5f, 5.0f, 1.8f), // Normal
                new(0.32f, 0.42f, 11.0f, 8.0f, 3.2f), // Hard
                new(0.40f, 0.52f, 16.0f, 11.0f, 4.8f) // Insane
            ];

            private Vector2 _botMotorMovement;
            private float _botMotorYawVelocity;
            private float _botMotorPitchVelocity;
            private bool _botMotorBiped;

            private void ResetBotMotor()
            {
                _botMotorMovement = Vector2.Zero;
                _botMotorYawVelocity = 0;
                _botMotorPitchVelocity = 0;
                _botMotorBiped = false;
                _player.Controls?.ClearAnalogMovement();
            }

            /// <summary>
            /// Shape one final input command after the legacy tree, tactical layer and
            /// enhanced-hunter logic have each offered their choices. No separate
            /// physics path is introduced; all motion still runs through Controls.
            /// </summary>
            private void ApplyBotMotor()
            {
                if (_player.Health <= 0 || !_player.ModInPlay || _player.IsAltForm
                    || _player.IsMorphing || _player.IsUnmorphing)
                {
                    // Leave native alt-form controls entirely untouched. Morph Ball,
                    // Lockjaw, Triskelion and other alt attacks need their own tuning.
                    ResetBotMotor();
                    return;
                }

                BotMotorTuning tuning = _motorTuning[Math.Clamp(_player.BotLevel, 0, 3)];
                float desiredX = (_buttons.A.IsDown ? 1 : 0) - (_buttons.Y.IsDown ? 1 : 0);
                float desiredY = (_buttons.X.IsDown ? 1 : 0) - (_buttons.B.IsDown ? 1 : 0);
                float x = BotMotorMath.StepMoveAxis(_botMotorMovement.X, desiredX,
                    tuning.MoveAcceleration, tuning.MoveBraking);
                float y = BotMotorMath.StepMoveAxis(_botMotorMovement.Y, desiredY,
                    tuning.MoveAcceleration, tuning.MoveBraking);

                float lengthSqr = x * x + y * y;
                if (lengthSqr > 1)
                {
                    float invLength = 1f / MathF.Sqrt(lengthSqr);
                    x *= invLength;
                    y *= invLength;
                }

                // Tactical footwork already probes walls, floor and hazards. Prevent
                // the last few ticks of momentum from carrying a bot off a ledge
                // during a direction reversal; native objective routes are exempt.
                if (_tacticalSteeringActive && _tacticalTarget != null
                    && lengthSqr > 0.0001f
                    && !_player.IsAltForm)
                {
                    Vector3 forward = _player.FacingVector.WithY(0);
                    if (forward.LengthSquared > 0.0001f)
                    {
                        forward.Normalize();
                        Vector3 right = new(forward.Z, 0, -forward.X);
                        if (!TacticalCanMove(right * x + forward * y))
                        {
                            x = y = 0;
                        }
                    }
                }

                _botMotorMovement = new Vector2(x, y);
                _botMotorBiped = true;

                // A single winner on each axis avoids conflicting movement
                // requests from multiple AI subsystems.
                _buttons.A.IsDown = x > 0.001f;
                _buttons.Y.IsDown = x < -0.001f;
                _buttons.X.IsDown = y > 0.001f;
                _buttons.B.IsDown = y < -0.001f;

                _buttonAimX = BotMotorMath.StepAimAxis(ref _botMotorYawVelocity,
                    _buttonAimX, tuning.MaxYawPerTick, tuning.TurnAcceleration);
                _buttonAimY = BotMotorMath.StepAimAxis(ref _botMotorPitchVelocity,
                    _buttonAimY, tuning.MaxPitchPerTick, tuning.TurnAcceleration);
            }

            private void ApplyBotMotorAnalogInput()
            {
                // Bot input is consumed by the exact same native locomotion and
                // SlotIntent axis path as a human controller, including replay.
                if (_botMotorBiped && _player.Health > 0 && _player.ModInPlay
                    && !_player.IsAltForm && !_player.IsMorphing && !_player.IsUnmorphing)
                {
                    _player.Controls.SetAnalogMovement(_botMotorMovement.X, _botMotorMovement.Y);
                }
                else
                {
                    _player.Controls.ClearAnalogMovement();
                }
            }
        }
    }
}
