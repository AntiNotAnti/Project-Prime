namespace ProjectPrime.PayloadEscort;

/// <summary>Explicit native playtest profiles; not a claim of exact current Overwatch parity.</summary>
public static class EscortProfiles
{
    // Payload District is 321.907 world units, not Dorado's coordinate space.
    // 50% faster forward speeds make its first legs ~50.8/53.9 s with one escort.
    // Keep the generic/draft defaults stable for reference fixtures and future maps.
    public static EscortRules PayloadDistrictV12 { get; } = new()
    {
        ProfileId = "payload-district-v1.2-pacing-and-half-setup",
        PreparationTicks = 45 * EscortRules.TickRate / 2, // 22.5 s, exactly half of v1.1.
        OneEscortPerTick = 2250,   // 2.25 world units/s
        TwoEscortsPerTick = 2625,  // 2.625 world units/s
        ThreeEscortsPerTick = 3000 // 3.00 world units/s, also the four-player cap
    };
}
