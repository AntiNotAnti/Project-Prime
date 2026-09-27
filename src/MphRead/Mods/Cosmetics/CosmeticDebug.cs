using System;
namespace MphRead.Mods.Cosmetics
{
    internal static class CosmeticDebug
    {
        public static float? FixedTime { get; private set; }
        public static CosmeticLoadout? Override { get; private set; }
        public static void Apply(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-cosmetictime" && i + 1 < args.Length
                    && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float time) && float.IsFinite(time)) FixedTime = Math.Max(0, time);
                if (args[i] != "-cosmetic" || i + 1 >= args.Length) continue;
                var value = Override ?? CosmeticLoadout.Default;
                string kind = args[++i];
                if (kind == "clear") { Override = CosmeticLoadout.Default; continue; }
                if (i + 1 >= args.Length) continue;
                string key = args[++i];
                Override = kind switch { "skin" => value with { SkinKey = key }, "armor" => value with { ArmorEffectKey = key },
                    "death" => value with { DeathEffectKey = key }, _ => value };
            }
        }
    }
}
