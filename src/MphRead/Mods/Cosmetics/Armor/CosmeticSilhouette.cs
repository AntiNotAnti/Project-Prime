namespace MphRead.Mods.Cosmetics.Armor
{
    // Presentation envelopes in game units. These do not change collision bounds.
    public readonly record struct CosmeticSilhouette(float Radius, float Base, float Height, float ParticleScale)
    {
        public static CosmeticSilhouette For(Hunter hunter, bool alt) => (hunter, alt) switch
        {
            (Hunter.Samus, false) => new(0.56f, 0.30f, 1.40f, 1),
            (Hunter.Kanden, false) => new(0.66f, 0.25f, 1.35f, 1.05f),
            (Hunter.Trace, false) => new(0.43f, 0.35f, 1.65f, 0.9f),
            (Hunter.Sylux, false) => new(0.58f, 0.30f, 1.40f, 1),
            (Hunter.Noxus, false) => new(0.52f, 0.30f, 1.40f, 0.95f),
            (Hunter.Spire, false) => new(0.78f, 0.25f, 1.35f, 1.15f),
            (Hunter.Weavel, false) => new(0.64f, 0.30f, 1.35f, 1.05f),
            (Hunter.Trace, true) => new(0.72f, 0.10f, 0.35f, 0.85f),
            (Hunter.Kanden, true) => new(0.58f, 0.10f, 0.30f, 0.85f),
            (Hunter.Spire, true) => new(0.72f, 0.10f, 0.80f, 1.1f),
            (Hunter.Weavel, true) => new(0.55f, 0.10f, 0.65f, 0.9f),
            (_, true) => new(0.44f, 0.10f, 0.45f, 0.85f),
            _ => new(0.56f, 0.30f, 1.40f, 1)
        };
    }
}
