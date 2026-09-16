using System;
using System.Collections.Generic;
using MphRead.Mods.Chat;
using MphRead.Text;

namespace MphRead.Hud
{
    public enum HudFontStyle
    {
        Original = 0,
        Modern = 1
    }

    /// <summary>
    /// Selects the ASCII font used by the in-game HUD. The modern face keeps
    /// the existing 8x8 HUD texture path, so text remains deterministic and
    /// inexpensive without adding a platform font dependency.
    /// </summary>
    public static class HudFontSettings
    {
        private static readonly Font _modern = CreateModernFont();

        public static IReadOnlyList<string> StyleNames { get; }
            = new[] { "Original", "Modern" };

        public static HudFontStyle Style { get; set; } = HudFontStyle.Modern;

        public static Font Current
            => Style == HudFontStyle.Original ? Font.Normal : _modern;

        internal static Font Modern => _modern;

        public static HudFontStyle Parse(string? value,
            HudFontStyle fallback = HudFontStyle.Modern)
            => Enum.TryParse(value, ignoreCase: true, out HudFontStyle parsed)
                && Enum.IsDefined(parsed) ? parsed : fallback;

        public static string Format(HudFontStyle style)
            => style == HudFontStyle.Original ? "original" : "modern";

        private static Font CreateModernFont()
        {
            byte[] widths = new byte[ChatFont.Widths.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = checked((byte)ChatFont.Widths[i]);
            }

            var font = new Font();
            font.SetData(widths, new byte[widths.Length], ChatFont.Pixels,
                minChar: ChatFont.First, packed: false);
            return font;
        }
    }
}
