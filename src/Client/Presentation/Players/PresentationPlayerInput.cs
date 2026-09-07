using System;
using MphRead.Formats;
using OpenTK.Mathematics;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public void UpdateHudShiftY(float amount)
        {
            if (_player.IsMainPlayer)
            {
                float sum = 0;
                for (int i = 6; i >= 0; i--)
                {
                    float past = _player._pastAimY[i];
                    sum += past;
                    _player._pastAimY[i + 1] = past;
                }

                _player._pastAimY[0] = amount;
                if (Features.HudSway && !Features.FixedWeapon)
                {
                    float average = (sum + amount) / 8;
                    _hudShiftY = Math.Clamp(-MathF.Round(average), -8, 8);
                }
                else
                {
                    _hudShiftY = 0;
                }

                _objShiftY = -_hudShiftY / 2;
            }
        }

        public void UpdateHudShiftX(float amount)
        {
            if (_player.IsMainPlayer)
            {
                float sum = 0;
                for (int i = 6; i >= 0; i--)
                {
                    float past = _player._pastAimX[i];
                    sum += past;
                    _player._pastAimX[i + 1] = past;
                }

                _player._pastAimX[0] = amount;
                if (Features.HudSway && !Features.FixedWeapon)
                {
                    float average = (sum + amount) / 8;
                    _hudShiftX = Math.Clamp(MathF.Round(average), -8, 8);
                }
                else
                {
                    _hudShiftX = 0;
                }

                _objShiftX = _hudShiftX / 2;
            }
        }
    }
}
