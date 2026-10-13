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
            private bool _botMotorAlt;

            private void ResetBotMotor()
            {
                _botMotorMovement = Vector2.Zero;
                _botMotorYawVelocity = 0;
                _botMotorPitchVelocity = 0;
                _botMotorBiped = false;
                _botMotorAlt = false;
                ResetBotDodge();
                _player.Controls?.ClearAnalogMovement();
            }

            /// <summary>
            /// Shape one final input command after the legacy tree, tactical layer and
            /// enhanced-hunter logic have each offered their choices. No separate
            /// physics path is introduced; all motion still runs through Controls.
            /// </summary>
            private void ApplyBotMotor()
            {
                if (_botSuppressFireThisTick)
                {
                    // No pre-acquisition fire, alt attack or boost from the
                    // legacy personality tree. The veto must run before the
                    // biped/alt separation, otherwise alt forms bypass it.
                    _buttons.R.IsDown = false;
                    if (_player.IsAltForm) _buttons.L.IsDown = false;
                }
                if (_player.Health <= 0 || !_player.ModInPlay
                    || _player.IsMorphing || _player.IsUnmorphing)
                {
                    ResetBotMotor();
                    return;
                }
                if (_player.IsAltForm)
                {
                    // Input smoothing applies equally to hunter alt locomotion,
                    // without replacing native rolling, bombs or boost physics.
                    ApplyBotAltMotor();
                    return;
                }
                if (_botMotorAlt)
                {
                    // Never carry the previous form's analog momentum into a
                    // new biped life or morph/unmorph transition.
                    _botMotorMovement = Vector2.Zero;
                    _botMotorYawVelocity = 0;
                    _botMotorPitchVelocity = 0;
                    _botMotorAlt = false;
                }

                // Treat observed incoming projectiles as time-limited dodge goals.
                // They can supersede ordinary footwork, but only when visible and
                // after a difficulty-dependent reaction delay.
                TryBotProjectileDodge();
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
                if (_tacticalSteeringActive && lengthSqr > 0.0001f
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

            private void ApplyBotAltMotor()
            {
                if (!_botMotorAlt)
                {
                    // A newly entered alt form begins with its own grounded
                    // input-rate history. We do not inherit biped strafe speed.
                    _botMotorMovement = Vector2.Zero;
                    _botMotorYawVelocity = 0;
                    _botMotorPitchVelocity = 0;
                    _botMotorAlt = true;
                    _botMotorBiped = false;
                    ResetBotDodge();
                }

                bool strafingAlt = _player.Values.AltFormStrafe != 0;
                Vector2 requested = BotMotorMath.AltDesiredAxes(strafingAlt,
                    _buttons.A.IsDown, _buttons.Y.IsDown, _buttons.X.IsDown, _buttons.B.IsDown,
                    _buttons.Right.IsDown, _buttons.Left.IsDown, _buttons.Up.IsDown, _buttons.Down.IsDown);
                BotMotorTuning tuning = _motorTuning[Math.Clamp(_player.BotLevel, 0, 3)];
                float x = BotMotorMath.StepMoveAxis(_botMotorMovement.X, requested.X,
                    tuning.MoveAcceleration, tuning.MoveBraking);
                float y = BotMotorMath.StepMoveAxis(_botMotorMovement.Y, requested.Y,
                    tuning.MoveAcceleration, tuning.MoveBraking);
                float lengthSquared = x * x + y * y;
                if (lengthSquared > 1)
                {
                    float inverse = 1f / MathF.Sqrt(lengthSquared);
                    x *= inverse;
                    y *= inverse;
                }
                _botMotorMovement = new Vector2(x, y);
                if (strafingAlt)
                {
                    // Trace, Sylux and Weavel turn their alt-form cameras, so
                    // retain the same bounded turning as their biped forms.
                    _buttonAimX = BotMotorMath.StepAimAxis(ref _botMotorYawVelocity,
                        _buttonAimX, tuning.MaxYawPerTick, tuning.TurnAcceleration);
                    _buttonAimY = BotMotorMath.StepAimAxis(ref _botMotorPitchVelocity,
                        _buttonAimY, tuning.MaxPitchPerTick, tuning.TurnAcceleration);
                    _buttons.A.IsDown = x > 0.001f;
                    _buttons.Y.IsDown = x < -0.001f;
                    _buttons.X.IsDown = y > 0.001f;
                    _buttons.B.IsDown = y < -0.001f;
                }
                else
                {
                    // Samus/Kanden/Spire/Noxus use camera-relative Roll input
                    // rather than alt-form strafing. The native roll physics
                    // consume the same analog magnitude as human controllers.
                    _buttons.Right.IsDown = x > 0.001f;
                    _buttons.Left.IsDown = x < -0.001f;
                    _buttons.Up.IsDown = y > 0.001f;
                    _buttons.Down.IsDown = y < -0.001f;
                }
            }

            private void ApplyBotMotorAnalogInput()
            {
                // Native locomotion consumes analog axes in *either* form. The
                // SlotIntent and replay codecs already transport these values.
                bool stable = _player.Health > 0 && _player.ModInPlay
                    && !_player.IsMorphing && !_player.IsUnmorphing;
                if (stable && (_botMotorBiped && !_player.IsAltForm
                    || _botMotorAlt && _player.IsAltForm))
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
