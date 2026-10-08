using System;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// One normalized, framebuffer-top-left coordinate system for every lobby
    /// element: GL platform, real Hunter preview window and RmlUi nameplate.
    /// Never duplicate these positions in the shader or the RCSS stylesheet.
    /// </summary>
    public readonly record struct LobbyFormationSlot(
        float PadX, float PadY, float RadiusX, float RadiusY,
        float HunterLeft, float HunterTop, float HunterRight, float HunterBottom,
        float HunterDistance, float LabelY)
    {
        public float LabelX => PadX;

        public bool Valid =>
            PadX is > 0f and < 1f && PadY is > 0f and < 1f
            && RadiusX is > 0f and < 0.3f && RadiusY is > 0f and < 0.1f
            && PadX - RadiusX >= 0f && PadX + RadiusX <= 1f
            && PadY - RadiusY >= 0f && PadY + RadiusY <= 1f
            && HunterLeft >= 0f && HunterTop >= 0f
            && HunterRight <= 1f && HunterBottom <= 1f
            && HunterLeft < HunterRight && HunterTop < HunterBottom
            && HunterDistance is >= 0.65f and <= 1.25f
            && LabelY is > 0f and < 1f;
    }

    public static class LauncherLobbyFormation
    {
        public const int Capacity = 8;
        public const float CenterX = 0.5f;

        // Slot 0 belongs to the local player, always. The seven remaining
        // positions are filled in nearest-left/right, middle-left/right,
        // far-left/right, rear-center order, not in network-slot order.
        // Pad positions and camera rectangles share one authored frame.
        private static readonly LobbyFormationSlot[] _slots =
        {
            new(.500f, .805f, .235f, .060f, .315f, .100f, .675f, .940f, .93f, .832f),
            new(.380f, .690f, .130f, .038f, .290f, .365f, .440f, .755f, .78f, .723f),
            new(.620f, .690f, .130f, .038f, .560f, .365f, .710f, .755f, .78f, .723f),
            new(.320f, .590f, .115f, .033f, .235f, .325f, .370f, .655f, .76f, .620f),
            new(.680f, .590f, .115f, .033f, .630f, .325f, .765f, .655f, .76f, .620f),
            new(.270f, .505f, .100f, .029f, .190f, .285f, .305f, .565f, .72f, .534f),
            new(.730f, .505f, .100f, .029f, .695f, .285f, .810f, .565f, .72f, .534f),
            new(.500f, .430f, .090f, .026f, .440f, .235f, .560f, .505f, .72f, .260f)
        };

        public static LobbyFormationSlot At(int presentationSlot)
        {
            if ((uint)presentationSlot >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(presentationSlot));
            return _slots[presentationSlot];
        }

        // Native GL shader uniform arrays are packed tightly as vec4, one
        // (centerX, centerY, radiusX, radiusY) per presentation slot.
        public static float[] PackPads()
        {
            float[] result = new float[Capacity * 4];
            for (int i = 0; i < Capacity; i++)
            {
                LobbyFormationSlot pad = _slots[i];
                result[i * 4] = pad.PadX;
                result[i * 4 + 1] = pad.PadY;
                result[i * 4 + 2] = pad.RadiusX;
                result[i * 4 + 3] = pad.RadiusY;
            }
            return result;
        }

        public static bool Validate()
        {
            if (_slots.Length != Capacity || !(_slots[0].RadiusX > _slots[1].RadiusX)
                || Math.Abs(_slots[0].PadX - CenterX) > .001f)
                return false;
            for (int i = 0; i < Capacity; i++)
            {
                if (!_slots[i].Valid) return false;
                // The mirrored chevron wings must not collide with the
                // local platform centre even at small framebuffer sizes.
                if (i > 0 && i < 7
                    && Math.Abs(_slots[i].PadX - _slots[0].PadX) < .08f)
                    return false;
            }
            for (int i = 1; i < 7; i += 2)
            {
                if (Math.Abs((_slots[i].PadX + _slots[i+1].PadX)
                             - _slots[0].PadX * 2f) > .005f)
                    return false;
                if (Math.Abs(_slots[i].PadY - _slots[i+1].PadY) > .001f)
                    return false;
            }
            return true;
        }
    }
}
