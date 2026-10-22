// Only external enums/preferences are stubbed. All policy, formation, theme and motion logic is linked production source.
namespace MphRead { public enum Hunter : byte { Samus, Kanden, Trace, Sylux, Noxus, Spire, Weavel, Guardian } }
namespace MphRead.Mods.Cosmetics { public enum CosmeticEffectQuality { Off, Low, Medium, High } }
namespace MphRead.Mods.Launcher { public static class LauncherPrefs { public static bool ReduceMotion { get; set; } } }
namespace MphRead.Mods { public static class RenderOptions { public static Cosmetics.CosmeticEffectQuality CosmeticQuality { get; set; } = Cosmetics.CosmeticEffectQuality.Medium; } }
