using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Cosmetics
{
    // Append styles; the low four bits of the cosmetic packet's death high byte
    // are reserved for these stable IDs. No hit, physics, or weapon rules.
    public enum BeamFxStyle : byte
    {
        IonFracture = 0,
        VoidRift = 1,
        QuantumSplit = 2,
        AlimbicResonance = 3,
        OmegaPulse = 4,
        ArcDisruptor = 5,
        SolarSpear = 6,
        CryoShatter = 7,
        PhantomTrace = 8
    }

    public sealed record BeamEffectDefinition(string Key, ushort WireId, string DisplayName,
        BeamFxStyle Style, Vector3 PrimaryColor, Vector3 SecondaryColor)
        : CosmeticDefinition(Key, WireId, DisplayName);

    // Color zero uses the authored style's own palette (the original per-slot
    // palette for Ion Fracture). All other tones are optional visual-only dyes.
    public static class BeamPalette
    {
        public static IReadOnlyList<string> Names { get; } = new[]
        {
            "Original", "Ion Cyan", "Plasma Pink", "Acid Green", "Solar Gold",
            "Ultraviolet", "Crimson", "Glacial Blue", "Pure White"
        };
        private static readonly Vector3[] Colors =
        {
            Vector3.Zero,
            new(0.13f, 0.88f, 1f),
            new(1f, 0.22f, 0.73f),
            new(0.58f, 1f, 0.18f),
            new(1f, 0.78f, 0.20f),
            new(0.54f, 0.42f, 1f),
            new(1f, 0.18f, 0.22f),
            new(0.55f, 0.86f, 1f),
            Vector3.One
        };
        public static bool Valid(byte value) => value < Colors.Length;
        public static Vector3 Resolve(byte value, Vector3 fallback) =>
            value == 0 || !Valid(value) ? fallback : Colors[value];
    }
}
