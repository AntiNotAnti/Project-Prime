using MphRead.Formats;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        /// <summary>Compact, state-derived feedback; it never authors gameplay.</summary>
        private void DrawEnhancedHunterStatus()
        {
            if (!_player._scene.Match.Rules.EnhancedHunters || _player.Health == 0) return;
            string? text = null;
            ColorRgba color = new(255, 190, 72, 255);
            if (_player.Hunter == Hunter.Samus && _player.EnhancedTargetTicks > 0)
                text = "TARGET LOCK";
            else if (_player.Hunter == Hunter.Kanden && _player.EnhancedTargetTicks > 0)
                text = "INTERFERENCE";
            else if (_player.Hunter == Hunter.Sylux && _player.Overcharge > 0)
            {
                text = _player.Overcharge switch
                {
                    1 => "OVERCHARGE 1/12", 2 => "OVERCHARGE 2/12",
                    3 => "OVERCHARGE 3/12", 4 => "OVERCHARGE 4/12",
                    5 => "OVERCHARGE 5/12", 6 => "OVERCHARGE 6/12",
                    7 => "OVERCHARGE 7/12", 8 => "OVERCHARGE 8/12",
                    9 => "OVERCHARGE 9/12", 10 => "OVERCHARGE 10/12",
                    11 => "OVERCHARGE 11/12", _ => "OVERCHARGE 12/12"
                };
                color = GetEnhancedOverchargeColor(_player.Overcharge);
            }
            else if (_player.ChilledTicks > 0)
            {
                text = "CHILLED";
                color = new ColorRgba(150, 225, 255, 255);
            }
            else if (_player.Hunter == Hunter.Trace && _player.CloakFadeTicks > 0)
                text = "CLOAK FADING";
            else if (_player.ConcussionTicks > 0)
            {
                text = "CONCUSSIVE IMPACT";
                color = new ColorRgba(255, 220, 150, 255);
            }
            if (text != null)
                DrawText2D(128, 50, Align.Center, 0, text, color, scale: .55f);
        }

        internal static ColorRgba GetEnhancedOverchargeColor(byte overcharge)
            => overcharge >= 9 ? new ColorRgba(112, 240, 255, 255)
                : overcharge >= 5 ? new ColorRgba(96, 210, 255, 255)
                : new ColorRgba(70, 155, 205, 255);
    }
}
